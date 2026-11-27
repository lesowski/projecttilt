using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using SyndicateLauncher.Models;
using SyndicateLauncher.Services;

namespace SyndicateLauncher.ViewModels;

/// <summary>One row of the Statistics page's mode table (solo, duo or squad career totals).</summary>
public sealed record ModeStatRow(string Mode, int Matches, int Wins, int Kills, int Top, string TopLabel)
{
    public string WinRate => StatsViewModel.Percent(Wins, Matches);
    public string KillsPerMatch => StatsViewModel.Ratio(Kills, Matches);
    public bool Played => Matches > 0;
}

/// <summary>One match in the Statistics page's history (backend syndicateCareerCore.js, newest first).</summary>
public sealed record RecentMatchRow(DateTime? At, int Season, string Mode, bool Arena, int Place, int Players, int Kills, int Seconds, bool Won)
{
    public string WhenText => At is DateTime t ? t.ToLocalTime().ToString("MMM d, HH:mm", CultureInfo.InvariantCulture) : "";
    public string SeasonText => StatsViewModel.SeasonName(Season);
    public string ModeText => Arena ? "Arena" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Mode == "unknown" ? "Other" : Mode);
    public string PlaceText => Won ? "#1" : Place > 0 ? "#" + Place : "—";
    public string PlayersText => Players > 0 ? "of " + Players : "";
    public string ResultText => Won ? "VICTORY ROYALE" : Place > 0 && Place <= 10 ? "TOP 10" : "ELIMINATED";
    public string KillsText => Kills == 1 ? "1 elim" : Kills + " elims";
    public string TimeText => Seconds <= 0 ? "—" : TimeSpan.FromSeconds(Seconds).TotalHours >= 1
        ? (int)TimeSpan.FromSeconds(Seconds).TotalHours + "h " + TimeSpan.FromSeconds(Seconds).Minutes + "m"
        : TimeSpan.FromSeconds(Seconds).Minutes + "m " + TimeSpan.FromSeconds(Seconds).Seconds.ToString("00") + "s";
}

/// <summary>Playtime on one season, from the launcher's own session log (Models/PlayerStats.cs).</summary>
public sealed record SeasonPlayRow(string Season, int Sessions, double Minutes, double Fraction)
{
    public string MinutesText => PlaytimeFormat.Duration(Minutes);
    public string SessionsText => Sessions == 1 ? "1 session" : Sessions + " sessions";
}

/// <summary>
/// The Statistics screen. Career totals and match history come from the backend (POST launcher/stats: solo, duo
/// and squad totals the backend records from every match's events, plus the last 50 matches); playtime per season
/// comes from the launcher's own session log. The last backend reply is cached per profile, so the page still
/// shows numbers while the backend is off (BackendOffline).
/// </summary>
public sealed class StatsViewModel : ViewModelBase
{
    private readonly Func<string?> _profile;
    private readonly Func<Task> _ensureBackend;
    private bool _refreshing, _backendOffline, _hasCareer;
    private string _matches = "0", _wins = "0", _winRate = "0%", _kills = "0", _killsPerMatch = "0.00", _top = "0";
    private string _timePlayed = "0m", _averagePlace = "—", _bestStreak = "0", _historyNote = "";
    private double _localMinutes, _backendMinutes;

    public StatsViewModel(Func<string?> profile, Func<Task> ensureBackend)
    {
        _profile = profile;
        _ensureBackend = ensureBackend;
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync());
        StatsService.Changed += name =>
        {
            if (string.Equals(name, _profile(), StringComparison.OrdinalIgnoreCase))
                Application.Current?.Dispatcher.BeginInvoke(() => _ = RefreshAsync());
        };
    }

    public ICommand RefreshCommand { get; }
    public ObservableCollection<ModeStatRow> Modes { get; } = new();
    public ObservableCollection<RecentMatchRow> Recent { get; } = new();
    public ObservableCollection<SeasonPlayRow> Seasons { get; } = new();

    public string Matches { get => _matches; private set => Set(ref _matches, value); }
    public string Wins { get => _wins; private set => Set(ref _wins, value); }
    public string WinRate { get => _winRate; private set => Set(ref _winRate, value); }
    public string Kills { get => _kills; private set => Set(ref _kills, value); }
    public string KillsPerMatch { get => _killsPerMatch; private set => Set(ref _killsPerMatch, value); }
    /// <summary>Top 10 (solo) + top 5 (duo) + top 3 (squad) finishes.</summary>
    public string TopFinishes { get => _top; private set => Set(ref _top, value); }
    public string TimePlayed { get => _timePlayed; private set => Set(ref _timePlayed, value); }
    /// <summary>Average placement over the matches in the history.</summary>
    public string AveragePlace { get => _averagePlace; private set => Set(ref _averagePlace, value); }
    /// <summary>Most Victory Royales in a row within the history.</summary>
    public string BestStreak { get => _bestStreak; private set => Set(ref _bestStreak, value); }
    public string HistoryNote { get => _historyNote; private set => Set(ref _historyNote, value); }
    public bool BackendOffline { get => _backendOffline; private set => Set(ref _backendOffline, value); }
    /// <summary>The backend has recorded at least one match for this player.</summary>
    public bool HasCareer { get => _hasCareer; private set { if (Set(ref _hasCareer, value)) OnPropertyChanged(nameof(ShowEmpty)); } }
    public bool HasRecent => Recent.Count > 0;
    public bool HasSeasons => Seasons.Count > 0;
    public bool ShowNoSeasons => Seasons.Count == 0;
    public bool ShowEmpty => !HasCareer;

    /// <summary>The cached backend reply for a profile (moved/removed by StatsService on rename/delete).</summary>
    internal static string CacheFileFor(string profile) =>
        Path.Combine(SettingsService.DataDirectory, "stats", string.Concat(profile.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_')) + ".career.json");

    /// <summary>Called when the Statistics screen opens and after each game session.</summary>
    public async Task RefreshAsync()
    {
        string? profile = _profile();
        if (profile is null) { Clear(); return; }
        LoadLocal(profile);
        LoadCached(profile);
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            if (!await SyndicateBackend.IsAvailableAsync())
            {
                // Never start the backend under a running game: it already has its own.
                if (GameLauncher.IsRunning) { BackendOffline = true; return; }
                await _ensureBackend();
            }
            string json = await SyndicateBackend.PostAsPlayerAsync(profile, "launcher/stats", new { });
            if (!string.Equals(profile, _profile(), StringComparison.OrdinalIgnoreCase)) return;
            ApplyJson(json);
            BackendOffline = false;
            try { Directory.CreateDirectory(Path.GetDirectoryName(CacheFileFor(profile))!); File.WriteAllText(CacheFileFor(profile), json); } catch (Exception) { }
        }
        catch (Exception)
        {
            BackendOffline = true;
        }
        finally { _refreshing = false; }
    }

    private void Clear()
    {
        _localMinutes = 0; Seasons.Clear(); OnPropertyChanged(nameof(HasSeasons)); OnPropertyChanged(nameof(ShowNoSeasons));
        ApplyJson("{}");
    }

    /// <summary>Playtime per season from the launcher's session log.</summary>
    public void LoadLocal(string profile) => ApplyLocal(StatsService.Load(profile));

    public void ApplyLocal(PlayerStats stats)
    {
        _localMinutes = stats.TotalMinutes;
        var rows = stats.Sessions.GroupBy(s => s.Season)
            .Select(g => (Season: g.Key, Sessions: g.Count(), Minutes: g.Sum(s => s.Minutes)))
            .OrderByDescending(r => r.Minutes).ToList();
        double max = Math.Max(1, rows.Count == 0 ? 1 : rows.Max(r => r.Minutes));
        Seasons.Clear();
        foreach (var r in rows) Seasons.Add(new SeasonPlayRow(SeasonName(r.Season), r.Sessions, r.Minutes, Math.Max(0.04, r.Minutes / max)));
        OnPropertyChanged(nameof(HasSeasons)); OnPropertyChanged(nameof(ShowNoSeasons));
        ShowTime();
    }

    private void LoadCached(string profile)
    {
        try { if (File.Exists(CacheFileFor(profile))) { ApplyJson(File.ReadAllText(CacheFileFor(profile))); return; } } catch (Exception) { }
        ApplyJson("{}");
    }

    /// <summary>Shows a launcher/stats reply: {modes:{solo,duo,squad}, total, recent:[...]}. Missing parts read as zero.</summary>
    public void ApplyJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        int matches = 0, wins = 0, kills = 0, top = 0;
        long seconds = 0;
        Modes.Clear();
        foreach (var (key, label, topLabel) in new[] { ("solo", "SOLO", "Top 10"), ("duo", "DUOS", "Top 5"), ("squad", "SQUADS", "Top 3") })
        {
            JsonElement m = default;
            bool has = root.TryGetProperty("modes", out var modes) && modes.ValueKind == JsonValueKind.Object && modes.TryGetProperty(key, out m);
            var row = new ModeStatRow(label, has ? Int(m, "matches") : 0, has ? Int(m, "wins") : 0, has ? Int(m, "kills") : 0, has ? Int(m, "top") : 0, topLabel);
            Modes.Add(row);
            matches += row.Matches; wins += row.Wins; kills += row.Kills; top += row.Top;
            if (has) seconds += Int(m, "seconds");
        }
        Matches = matches.ToString("N0"); Wins = wins.ToString("N0"); Kills = kills.ToString("N0"); TopFinishes = top.ToString("N0");
        WinRate = Percent(wins, matches); KillsPerMatch = Ratio(kills, matches);
        _backendMinutes = seconds / 60.0;
        ShowTime();

        Recent.Clear();
        if (root.TryGetProperty("recent", out var recent) && recent.ValueKind == JsonValueKind.Array)
            foreach (var r in recent.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object) continue;
                DateTime? at = r.TryGetProperty("at", out var a) && a.ValueKind == JsonValueKind.String &&
                               DateTime.TryParse(a.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : null;
                Recent.Add(new RecentMatchRow(at, Int(r, "season"), Str(r, "mode"), Bool(r, "arena"), Int(r, "place"), Int(r, "players"),
                    Int(r, "kills"), Int(r, "seconds"), Bool(r, "won")));
            }
        OnPropertyChanged(nameof(HasRecent));
        var placed = Recent.Where(r => r.Place > 0).ToList();
        AveragePlace = placed.Count == 0 ? "—" : "#" + Math.Round(placed.Average(r => r.Place)).ToString("0");
        int streak = 0, best = 0;
        foreach (var r in Recent) { streak = r.Won ? streak + 1 : 0; best = Math.Max(best, streak); }
        BestStreak = best.ToString();
        HistoryNote = Recent.Count == 0
            ? (matches > 0 ? "Match history starts with your next match." : "")
            : "Your last " + (Recent.Count == 1 ? "match" : Recent.Count + " matches") + ", newest first.";
        HasCareer = matches > 0 || Recent.Count > 0;
    }

    private void ShowTime() => TimePlayed = PlaytimeFormat.Duration(Math.Max(_localMinutes, _backendMinutes));

    /// <summary>Season names as the game server reports them (Season 0 builds report 1; Chapter 2 is 11 and 12 in
    /// match history, and in the launcher's own session log since 1.4.3).</summary>
    public static string SeasonName(int season) => season switch
    {
        0 => "Pre-season",
        >= 1 and <= 9 => "Season " + season,
        10 => "Season X",
        11 => "Chapter 2 Season 1",
        12 => "Chapter 2 Season 2",
        _ => "Other"
    };

    public static string Percent(int part, int whole) => whole <= 0 ? "0%" : (100.0 * part / whole).ToString(part * 100 % whole == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + "%";
    public static string Ratio(int part, int whole) => whole <= 0 ? "0.00" : ((double)part / whole).ToString("0.00", CultureInfo.InvariantCulture);

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.TryGetDouble(out double d) && d > 0 ? (int)Math.Min(int.MaxValue, Math.Floor(d)) : 0;
    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
