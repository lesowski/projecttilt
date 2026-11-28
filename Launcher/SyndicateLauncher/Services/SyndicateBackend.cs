using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SyndicateLauncher.Services;

/// <summary>
/// Runs Syndicate's Node backend (the one Syndicate uses) in place of LawinServer.
/// It serves the Fortnite API on 127.0.0.1:3551 and keeps its accounts in the local MongoDB.
/// </summary>
public static class SyndicateBackend
{
    private const int ApiPort = 3551;
    private const int MongoPort = 27028;

    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri($"http://127.0.0.1:{ApiPort}/"),
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <summary>The Node backend and matchmaker this launcher started. MongoDB is kept apart (<see cref="_mongo"/>).</summary>
    private static readonly List<Process> Started = new();
    /// <summary>
    /// The MongoDB this launcher started. It is never killed between matches or season switches: a hard kill can land
    /// while it writes its journal, and the next start then fails ("MongoDB exited during startup") on every season
    /// until the database is repaired. It is shut down cleanly when the launcher closes (<see cref="StopDatabase"/>).
    /// </summary>
    private static Process? _mongo;
    private static readonly SemaphoreSlim StartGate = new(1, 1);
    private static int _startedSeason = -1;

    /// <summary>The shared Runtime\Server folder the matchmaker reads server/build state from.</summary>
    public static string? RuntimeDirectory
    {
        get
        {
            var backend = FindBackend();
            return backend is null ? null : Path.GetFullPath(Path.Combine(backend, "..", "Runtime", "Server"));
        }
    }

    public static string? FindBackend()
    {
        foreach (var candidate in BackendCandidates())
            if (File.Exists(Path.Combine(candidate, "src", "index.js")) &&
                File.Exists(Path.Combine(candidate, "package.json")) &&
                Directory.Exists(Path.Combine(candidate, "node_modules")))
                return candidate;
        return null;
    }

    private static IEnumerable<string> BackendCandidates()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        // Release packages ship the backend as Tilt-backend; dev kits (UPDATE FROM GIT) and older installs use Syndicate-backend.
        yield return Path.Combine(AppContext.BaseDirectory, "Tilt-backend");
        yield return Path.Combine(AppContext.BaseDirectory, "Syndicate-backend");
        yield return Path.Combine(local, "SyndicateLauncher", "Syndicate-backend");
        // Older Project Tilted installs keep their backend under its old folder name.
        string legacyBackend = "Til" + "ted-backend";
        yield return Path.Combine(documents, "Project Tilted", legacyBackend);
        yield return Path.Combine(documents, "tiltedlegacy", legacyBackend);
    }

    private static string? FindNode(string backend)
    {
        foreach (var candidate in new[]
        {
            Path.Combine(Path.GetDirectoryName(backend) ?? "", "Dependencies", "Node", "node.exe"),
            Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(backend) ?? "") ?? "", "Dependencies", "Node", "node.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Project Tilted", "Dependencies", "Node", "node.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "tiltedlegacy", "Dependencies", "Node", "node.exe")
        })
            if (File.Exists(candidate)) return candidate;

        // Fall back to the system node on PATH.
        return "node.exe";
    }

    internal static string? FindMongo(string backend)
    {
        foreach (var candidate in new[]
        {
            Path.Combine(Path.GetDirectoryName(backend) ?? "", "Dependencies", "MongoDB", "mongod.exe"),
            Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(backend) ?? "") ?? "", "Dependencies", "MongoDB", "mongod.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Project Tilted", "Dependencies", "MongoDB", "mongod.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "tiltedlegacy", "Dependencies", "MongoDB", "mongod.exe")
        })
            if (File.Exists(candidate)) return candidate;
        return null;
    }

    /// <summary>Starts MongoDB, the Node API and the matchmaker before Play is pressed.
    /// Failures are ignored here; Play calls EnsureRunningAsync again and reports them.</summary>
    public static async Task WarmUpAsync(int mainSeason)
    {
        try { await EnsureRunningAsync(_ => { }, mainSeason); }
        catch { /* Play retries and reports the real error. */ }
    }

    public static async Task EnsureRunningAsync(Action<string> status, int mainSeason = 7)
    {
        await StartGate.WaitAsync();
        try
        {
            await EnsureRunningCoreAsync(status, mainSeason);
            // Once per launcher run: players from a Project Tilted install on this PC (Services/TiltedImport.cs).
            // Inside the gate, so Play never signs in before an imported player exists.
            if (FindBackend() is { } backend) await TiltedImport.RunOnceAsync(FindMongo(backend), Http, status);
        }
        finally { StartGate.Release(); }
    }

    private static async Task EnsureRunningCoreAsync(Action<string> status, int mainSeason)
    {
        var backend = FindBackend()
            ?? throw new InvalidOperationException(
#if DEV_BUILD
                "The Tilt backend is missing. Copy Syndicate-backend next to the launcher (with node_modules), or run UPDATE FROM GIT.cmd once.");
#else
                "The Tilt backend is missing. Reinstall the launcher (extract the whole ZIP), and add the Tilt folder to your antivirus exclusions.");
#endif

        bool ours = Started.Any(process => !process.HasExited);
        if (await IsUpAsync())
        {
            if (ours && _startedSeason == mainSeason)
            {
                await EnsureMatchmakerAsync(backend, status);
                return;
            }

            if (ours)
            {
                // One backend serves one season's Battle Pass data (MAIN_SEASON), so switching
                // builds restarts it instead of showing the previous season's pass.
                status("Restarting the local backend for the selected season...");
                StopStarted();
                await Task.Delay(600);
            }
            else
            {
                // A backend left by another Syndicate install reads its own runtime files (often a
                // different season) and its own matchmaker address. Replace it so this session
                // uses Syndicate's build and localhost matchmaker.
                status("Stopping the previous local backend...");
                StopListener(ApiPort, status);
                if (PortOpen(6969)) StopListener(6969, status);
                await Task.Delay(600);
            }
        }

        if (PortOpen(6969)) StopListener(6969, status);

        if (!PortOpen(MongoPort))
        {
            var mongod = FindMongo(backend)
                ?? throw new InvalidOperationException("MongoDB is not running and mongod.exe could not be found.");
            await StartDatabaseAsync(mongod, status);
        }

        status("Starting the Tilt backend...");
        StartNode(backend, mainSeason);
        await WaitForApiAsync(TimeSpan.FromSeconds(45));

        await EnsureMatchmakerAsync(backend, status);
    }

    private static async Task EnsureMatchmakerAsync(string backend, Action<string> status)
    {
        if (PortOpen(6969)) return;

        var matchmaker = Path.Combine(backend, "Matchmaker", "index.js");
        if (!File.Exists(matchmaker)) return; // older backend without split matchmaking

        status("Starting matchmaking...");

        // Capture the matchmaker's output so a startup crash ("Matchmaking exited during startup") can be
        // diagnosed instead of vanishing. Written next to the other session logs, read-shared so the error
        // reporter can pick it up. All guarded so logging can never stop the matchmaker from starting.
        string? mmLog = null;
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SyndicateLauncher", "Logs");
            Directory.CreateDirectory(logDir);
            mmLog = Path.Combine(logDir, "matchmaker.log");
        }
        catch { mmLog = null; }

        var info = new ProcessStartInfo(FindNode(backend)!)
        {
            Arguments = "Matchmaker/index.js",
            WorkingDirectory = backend,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        var proc = Process.Start(info) ?? throw new InvalidOperationException("Could not start matchmaking.");
        TextWriter? mmWriter = null;
        try
        {
            if (mmLog != null)
                mmWriter = TextWriter.Synchronized(new StreamWriter(
                    new FileStream(mmLog, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true });
        }
        catch { mmWriter = null; }
        proc.OutputDataReceived += (_, e) => { if (e.Data != null && mmWriter != null) { try { mmWriter.WriteLine(e.Data); } catch { } } };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null && mmWriter != null) { try { mmWriter.WriteLine(e.Data); } catch { } } };
        try { proc.BeginOutputReadLine(); proc.BeginErrorReadLine(); } catch { }
        Started.Add(proc);
        await WaitForPortAsync(6969, TimeSpan.FromSeconds(15), "Matchmaking");
    }

    /// <summary>Signs a local player in and returns the exchange code Fortnite expects.</summary>
    public static async Task<string> ExchangeCodeAsync(string username, bool unlockAll, Action<string>? status = null, bool stwSkins = false)
    {
        var quick = await Http.PostAsJsonAsync("launcher/quick", new { username });
        if (!quick.IsSuccessStatusCode)
            throw new InvalidOperationException("The Tilt backend could not create or find the player '" + username + "'.");

        string token;
        using (var document = JsonDocument.Parse(await quick.Content.ReadAsStringAsync()))
            token = document.RootElement.GetProperty("token").GetString()
                ?? throw new InvalidOperationException("The Tilt backend returned no login token.");

        using (var request = new HttpRequestMessage(HttpMethod.Post, "launcher/unlockall/set"))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(new { enabled = unlockAll });
            using var response = await Http.SendAsync(request);
            // Unlock-all is optional: a backend without the route still lets the player in.
        }

        // Save the World skins (Choose Player, under Unlock All): BR cosmetics only unless the player turned it on.
        using (var request = new HttpRequestMessage(HttpMethod.Post, "launcher/stwskins/set"))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(new { enabled = unlockAll && stwSkins });
            using var response = await Http.SendAsync(request);
        }

        using (var request = new HttpRequestMessage(HttpMethod.Post, "launcher/exchange"))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(new { });
            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("The Tilt backend did not return a login code.");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            status?.Invoke("Signed in as " + username + ".");
            return document.RootElement.GetProperty("code").GetString()
                ?? throw new InvalidOperationException("The Tilt backend returned an empty login code.");
        }
    }

    // ---------------------------------------------------------------- launcher Home (wallet, career, recent matches)

    /// <summary>True when the local backend answers (it is warmed at startup and by the Play screen).</summary>
    public static Task<bool> IsAvailableAsync() => IsUpAsync();

    private static async Task<string> PlayerTokenAsync(string username)
    {
        using var quick = await Http.PostAsJsonAsync("launcher/quick", new { username });
        if (!quick.IsSuccessStatusCode)
            throw new InvalidOperationException("The Tilt backend could not find the player '" + username + "'.");
        using var document = JsonDocument.Parse(await quick.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("The Tilt backend returned no login token.");
    }

    /// <summary>
    /// POSTs to one of the backend's launcher routes as <paramref name="username"/> and returns the JSON reply
    /// (raw text). A refused request throws with the backend's own message.
    /// </summary>
    public static async Task<string> PostAsPlayerAsync(string username, string route, object body)
    {
        string token = await PlayerTokenAsync(username);
        using var request = new HttpRequestMessage(HttpMethod.Post, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(body);
        using var response = await Http.SendAsync(request);
        string text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            string message = "The backend refused the request.";
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("message", out var m) && m.GetString() is { Length: > 0 } s) message = s;
            }
            catch (JsonException) { }
            throw new InvalidOperationException(message);
        }
        return text;
    }

    /// <summary>
    /// Renames the player's backend account (level, locker, V-Bucks... all stay). False when the backend has no
    /// account with that name yet (a player who never pressed Play): then only the launcher profile is renamed.
    /// </summary>
    public static async Task<bool> RenamePlayerAsync(string username, string newName)
    {
        using var response = await Http.PostAsJsonAsync("launcher/players/rename", new { username, newName });
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        await ThrowIfRefusedAsync(response);
        return true;
    }

    /// <summary>Deletes the player's backend account, profiles and friends list. A missing account is fine.</summary>
    public static async Task DeletePlayerAsync(string username)
    {
        using var response = await Http.PostAsJsonAsync("launcher/players/delete", new { username });
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return;
        await ThrowIfRefusedAsync(response);
    }

    private static async Task ThrowIfRefusedAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        string message = "The Tilt backend refused the change.";
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("message", out var m) && m.GetString() is { Length: > 0 } text) message = text;
        }
        catch (JsonException) { }
        throw new InvalidOperationException(message);
    }

    /// <summary>
    /// Tells the backend whether this launch has the in-game menu on. While it is, kills and wins pay no V-Bucks
    /// (backend Utils/syndicateProgression.js). Set at every launch, never at exit.
    /// </summary>
    public static Task SetMenuAsync(string username, bool active) =>
        PostAsPlayerAsync(username, "launcher/menu", new { active });

    /// <summary>
    /// Starts MongoDB. When it quits while starting because its data is damaged (it cannot replay its journal after an
    /// earlier hard stop), runs MongoDB's own --repair, which salvages what it can, and starts it again. If that fails
    /// too, the damaged folder is kept aside and a new database is made: players, lockers and settings live in
    /// %APPDATA%\SyndicateLauncher, and accounts are made again on sign-in, so Play keeps working.
    /// </summary>
    private static async Task StartDatabaseAsync(string mongod, Action<string> status)
    {
        string root = LegacyData.DatabaseDirectory;
        string log = Path.Combine(root, "mongodb.log");
        long logStart = File.Exists(log) ? new FileInfo(log).Length : 0;
        status("Starting the local account database...");
        if (await WaitForMongoAsync(StartMongo(mongod, root))) return;

        string reason = ReadLogFrom(log, logStart);
        if (!DamagedDatabase(reason))
            throw new InvalidOperationException("MongoDB exited during startup." + MongoHint(reason));

        status("The local account database was damaged. Repairing it, this can take a minute...");
        if (await RepairMongoAsync(mongod, root) && await WaitForMongoAsync(StartMongo(mongod, root)))
        {
            status("Repaired the local account database.");
            return;
        }

        string aside = root + "-damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        try { Directory.Move(root, aside); }
        catch (Exception error) { throw new InvalidOperationException("The local account database is damaged and could not be repaired or moved aside (" + error.Message + "). Close every Tilt window and press Play again."); }
        status("The local account database could not be repaired, so Tilt started a new one. The old one is kept in " + aside + ".");
        if (!await WaitForMongoAsync(StartMongo(mongod, root)))
            throw new InvalidOperationException("MongoDB exited during startup, even with a new database." + MongoHint(ReadLogFrom(Path.Combine(root, "mongodb.log"), 0)));
    }

    /// <summary>True once MongoDB listens; false when the process quit first.</summary>
    private static async Task<bool> WaitForMongoAsync(Process process)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (PortOpen(MongoPort)) return true;
            if (process.HasExited) return false;
            await Task.Delay(150);
        }
        if (PortOpen(MongoPort)) return true;
        throw new TimeoutException("MongoDB did not start within 30 seconds.");
    }

    /// <summary>MongoDB's log says its data files or journal could not be opened or recovered.</summary>
    internal static bool DamagedDatabase(string log) =>
        log.Contains("--repair", StringComparison.OrdinalIgnoreCase)
        || log.Contains("Recovery failed", StringComparison.OrdinalIgnoreCase)
        || log.Contains("WT_ERROR", StringComparison.Ordinal)
        || log.Contains("WT_PANIC", StringComparison.Ordinal)
        || log.Contains("WT_TRY_SALVAGE", StringComparison.Ordinal)
        || log.Contains("\"msgid\":50944", StringComparison.Ordinal)
        || log.Contains("corrupt", StringComparison.OrdinalIgnoreCase);

    private static string MongoHint(string log) =>
        log.Contains("Address already in use", StringComparison.OrdinalIgnoreCase) || log.Contains("SocketException", StringComparison.Ordinal)
            ? " Port " + MongoPort + " is used by another program. Close other Tilt copies or restart your PC, then press Play again."
            : log.Length == 0
                ? " Windows may have blocked mongod.exe: add the Tilt folder to your antivirus exclusions and extract the whole ZIP again."
                : "";

    private static string ReadLogFrom(string path, long offset)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (offset > stream.Length) offset = 0;
            stream.Position = offset;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch { return ""; }
    }

    private static async Task<bool> RepairMongoAsync(string mongod, string root)
    {
        try
        {
            var info = new ProcessStartInfo(mongod)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(mongod)!
            };
            foreach (var argument in new[] { "--dbpath", root, "--repair", "--logpath", Path.Combine(root, "mongodb.log"), "--logappend" })
                info.ArgumentList.Add(argument);
            using var repair = Process.Start(info);
            if (repair is null) return false;
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            try { await repair.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { try { repair.Kill(); } catch { } return false; }
            return repair.ExitCode == 0;
        }
        catch { return false; }
    }

    private static Process StartMongo(string mongod, string root)
    {
        Directory.CreateDirectory(root);

        var info = new ProcessStartInfo(mongod)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(mongod)!
        };
        foreach (var argument in new[]
        {
            "--dbpath", root,
            "--bind_ip", "127.0.0.1",
            "--port", MongoPort.ToString(),
            "--logpath", Path.Combine(root, "mongodb.log"),
            "--logappend"
        }) info.ArgumentList.Add(argument);

        var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start MongoDB.");
        _mongo = process;
        return process;
    }

    private static void StartNode(string backend, int mainSeason)
    {
        var info = new ProcessStartInfo(FindNode(backend)!)
        {
            Arguments = "src/index.js",
            WorkingDirectory = backend,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        string logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SyndicateLauncher", "Logs");
        Directory.CreateDirectory(logDirectory);

        // dotenv does not overwrite process environment variables, so these win over the
        // installation's .env without editing it.
        info.Environment["PORT"] = ApiPort.ToString();
        info.Environment["MONGO_URI"] = $"mongodb://127.0.0.1:{MongoPort}/project_tilted";
        info.Environment["MATCHMAKER_IP"] = "127.0.0.1:6969";
        info.Environment["GAMESERVER_IP"] = "127.0.0.1:7777";
        info.Environment["MAIN_SEASON"] = mainSeason.ToString();
        // Drives the lobby "Ready" text (hotfix: "${BACKEND_NAME} - Ready"), quit prompt, etc.
        // Shown in game: "CONNECTING TO Tilt" and the region names (backend Utils/hotfixes.js).
        info.Environment["BACKEND_NAME"] = "Tilt";
        info.Environment["TILTED_BACKEND_LOG"] = Path.Combine(logDirectory, "syndicate-backend.log");
        info.Environment["BOT_TOKEN"] = "";
        info.Environment["DISCORD_TOKEN"] = "";
        // This install's chat certificate for Pre-season, Season 1 and Season 2 (Services/LegacyXmppTrust.cs). Without
        // it those seasons never log in to chat, so the party never syncs and ready-up waits forever.
        try { LegacyXmppTrust.EnsureCertificate(); info.Environment["TILT_LEGACY_XMPP_TLS"] = LegacyXmppTrust.TlsDirectory; }
        catch { /* the other seasons don't need it */ }
#if DEV_BUILD
        // Dev launcher: the backend's developer routes (Syndicate Studio's live map editor, match telemetry).
        info.Environment["TILTED_DEVKIT"] = "1";
#endif

        var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the Tilt backend.");
        Started.Add(process);
        _startedSeason = mainSeason;
    }

    private static async Task<bool> IsUpAsync()
    {
        if (!PortOpen(ApiPort)) return false;
        try
        {
            using var response = await Http.GetAsync("fortnite/api/version");
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    private static bool PortOpen(int port)
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync("127.0.0.1", port).Wait(250) && client.Connected;
        }
        catch { return false; }
    }

    private static async Task WaitForPortAsync(int port, TimeSpan timeout, string name)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < timeout)
        {
            if (Started.Any(p => p.HasExited))
                throw new InvalidOperationException(name + " exited during startup.");
            if (PortOpen(port)) return;
            await Task.Delay(150);
        }
        throw new TimeoutException(name + " did not start within " + timeout.TotalSeconds + " seconds.");
    }

    private static async Task WaitForApiAsync(TimeSpan timeout)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < timeout)
        {
            if (Started.Any(p => p.HasExited))
                throw new InvalidOperationException("The Tilt backend exited during startup. See Logs/syndicate-backend.log.");
            if (await IsUpAsync()) return;
            await Task.Delay(250);
        }
        throw new TimeoutException("The Tilt backend did not become ready within " + timeout.TotalSeconds + " seconds.");
    }

    /// <summary>Stops whatever process is listening on a port (used to replace a stale backend/matchmaker).</summary>
    private static void StopListener(int port, Action<string>? status)
    {
        try
        {
            var info = new ProcessStartInfo("netstat.exe", "-ano -p tcp")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var netstat = Process.Start(info);
            if (netstat is null) return;
            string text = netstat.StandardOutput.ReadToEnd();
            netstat.WaitForExit(5000);

            foreach (var line in text.Split('\n'))
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5) continue;
                if (!parts[1].EndsWith(":" + port, StringComparison.Ordinal)) continue;
                if (!parts[3].Equals("LISTENING", StringComparison.OrdinalIgnoreCase)) continue;
                if (!int.TryParse(parts[4], out int pid) || pid <= 4) continue;

                try
                {
                    using var process = Process.GetProcessById(pid);
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                    status?.Invoke("Stopped the old local service on port " + port + ".");
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>Stops only the processes this launcher started (an existing Syndicate session is left alone).</summary>
    /// <summary>Stops the Node backend and matchmaker. MongoDB keeps running for the next match (see <see cref="_mongo"/>).</summary>
    public static void StopStarted()
    {
        foreach (var process in Started.ToArray().Reverse())
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { }
        }
        Started.Clear();
        _startedSeason = -1;
    }

    /// <summary>
    /// Shuts down the MongoDB this launcher started, cleanly: the shutdown command, then up to 8 seconds for it to
    /// finish writing. Only a MongoDB that does not answer is killed. Called when the launcher closes.
    /// </summary>
    public static void StopDatabase()
    {
        var mongo = _mongo;
        _mongo = null;
        if (mongo is null) return;
        try
        {
            if (mongo.HasExited) return;
            SendMongoShutdown();
            if (!mongo.WaitForExit(8000)) mongo.Kill();
        }
        catch { try { if (!mongo.HasExited) mongo.Kill(); } catch { } }
    }

    /// <summary>
    /// Sends { shutdown: 1 } to the local MongoDB as one OP_MSG (the wire protocol's command message), so the database
    /// is closed the way MongoDB's own tools close it. No driver is needed for this one command.
    /// </summary>
    internal static void SendMongoShutdown()
    {
        using var client = new TcpClient();
        if (!client.ConnectAsync("127.0.0.1", MongoPort).Wait(1000)) return;
        using var stream = client.GetStream();
        stream.WriteTimeout = 2000; stream.ReadTimeout = 2000;
        var command = MongoShutdownMessage();
        stream.Write(command, 0, command.Length);
        try { stream.ReadExactly(new byte[16]); } catch { /* MongoDB closes the connection as it shuts down */ }
    }

    internal static byte[] MongoShutdownMessage()
    {
        // BSON document { shutdown: 1, $db: "admin" }.
        var document = new MemoryStream();
        var writer = new BinaryWriter(document);
        writer.Write(0); // length, set below
        writer.Write((byte)0x10); writer.Write(System.Text.Encoding.ASCII.GetBytes("shutdown\0")); writer.Write(1);
        writer.Write((byte)0x02); writer.Write(System.Text.Encoding.ASCII.GetBytes("$db\0")); writer.Write(6); writer.Write(System.Text.Encoding.ASCII.GetBytes("admin\0"));
        writer.Write((byte)0);
        writer.Flush();
        var body = document.ToArray();
        BitConverter.GetBytes(body.Length).CopyTo(body, 0);

        // OP_MSG: header (length, request id, response to, opcode 2013), flag bits, one body section (kind 0).
        var message = new MemoryStream();
        var output = new BinaryWriter(message);
        output.Write(16 + 4 + 1 + body.Length); output.Write(1); output.Write(0); output.Write(2013);
        output.Write(0); output.Write((byte)0); output.Write(body);
        output.Flush();
        return message.ToArray();
    }
}
