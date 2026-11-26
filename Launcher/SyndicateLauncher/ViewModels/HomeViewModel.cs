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

/// <summary>Home tab: V-Bucks, Total Playtime, 7-Day Activity, career totals and the latest matches.</summary>
public sealed class HomeViewModel : ViewModelBase
{
    private readonly Func<string?> _profile;
    private readonly Func<Task> _ensureBackend;
    private string? _loadedFor;
    private bool _refreshing, _loaded;
    private long? _vbucks;
    private string _totalPlaytime = "0m";
    private string _weekPlaytime = "0m";
    private int _weekGames;
    private int _totalGames;
    private bool _menuActive;
    private bool _backendOffline;
    private string _kills = "0", _wins = "0", _matches = "0", _winRate = "0%";
    private double _localMinutes, _matchMinutes;

    public HomeViewModel(Func<string?> profile, Func<Task> ensureBackend)
    {
        _profile = profile;
        _ensureBackend = ensureBackend;
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(force: true));
        StatsService.Changed += name =>
        {
            if (string.Equals(name, _profile(), StringComparison.OrdinalIgnoreCase))
                Application.Current?.Dispatcher.BeginInvoke(() => _ = RefreshAsync(force: true));
        };
    }

    public ObservableCollection<ActivityDay> Activity { get; } = new();
    /// <summary>The Home strip: the player's latest matches, newest first (backend launcher/home).</summary>
    public ObservableCollection<RecentMatchRow> Recent { get; } = new();

    public ICommand RefreshCommand { get; }

    public bool HasVBucks => _vbucks is not null;
    public string VBucksText => _vbucks is long v ? v.ToString("N0") : "—";
    public string TotalPlaytime { get => _totalPlaytime; private set => Set(ref _totalPlaytime, value); }
    public string WeekPlaytime { get => _weekPlaytime; private set => Set(ref _weekPlaytime, value); }
    public int WeekGames { get => _weekGames; private set => Set(ref _weekGames, value); }
    public int TotalGames { get => _totalGames; private set => Set(ref _totalGames, value); }
    public bool MenuActive { get => _menuActive; private set => Set(ref _menuActive, value); }
    public bool BackendOffline { get => _backendOffline; private set { if (Set(ref _backendOffline, value)) OnPropertyChanged(nameof(RecentPlaceholder)); } }
    public string Kills { get => _kills; private set => Set(ref _kills, value); }
    public string Wins { get => _wins; private set => Set(ref _wins, value); }
    public string Matches { get => _matches; private set => Set(ref _matches, value); }
    public string WinRate { get => _winRate; private set => Set(ref _winRate, value); }
    public bool ShowRecentPlaceholder => Recent.Count == 0;
    /// <summary>The newest match, for Home's Recent Match tile (null before the first match).</summary>
    public RecentMatchRow? LatestMatch => Recent.Count > 0 ? Recent[0] : null;
    public string RecentPlaceholder => BackendOffline
        ? "Your matches load from the Tilt backend. It isn't running right now."
        : "No matches yet. Finish a match in any season and your latest results show up here.";

    private static string CacheFile(string profile) => CacheFileFor(profile);

    /// <summary>The cached backend reply for a profile (also moved/removed by StatsService on rename/delete).</summary>
    internal static string CacheFileFor(string profile) =>
        Path.Combine(SettingsService.DataDirectory, "stats", string.Concat(profile.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_')) + ".home.json");

    /// <summary>Called when Home is shown and when the profile changes. Cheap when nothing changed.</summary>
    public async Task RefreshAsync(bool force = false)
    {
        string? profile = _profile();
        if (profile is null) return;
        bool switched = !string.Equals(profile, _loadedFor, StringComparison.OrdinalIgnoreCase);
        if (!force && !switched && !BackendOffline && _loaded) { LoadLocal(profile); return; }
        _loadedFor = profile;
        LoadLocal(profile);
        if (switched) ApplyCached(profile);
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            if (!await SyndicateBackend.IsAvailableAsync())
            {
                if (GameLauncher.IsRunning) { BackendOffline = true; return; }
                await _ensureBackend();
            }
            string json = await SyndicateBackend.PostAsPlayerAsync(profile, "launcher/home", new { });
            if (!string.Equals(profile, _profile(), StringComparison.OrdinalIgnoreCase)) return;
            Apply(json);
            _loaded = true;
            BackendOffline = false;
            try { File.WriteAllText(CacheFile(profile), json); } catch (Exception) { }
        }
        catch (Exception)
        {
            BackendOffline = true;
        }
        finally { _refreshing = false; }
    }

    private void LoadLocal(string profile)
    {
        var stats = StatsService.Load(profile);
        _localMinutes = stats.TotalMinutes;
        ShowTotalPlaytime();
        TotalGames = stats.Sessions.Count;
        var days = stats.SevenDayActivity();
        double max = Math.Max(1, days.Max(d => d.Minutes));
        WeekGames = days.Sum(d => d.Games);
        WeekPlaytime = PlaytimeFormat.Duration(days.Sum(d => d.Minutes));
        Activity.Clear();
        foreach (var d in days) Activity.Add(d with { Height = d.Minutes <= 0 ? 0 : Math.Max(0.06, d.Minutes / max) });
    }

    private void ShowTotalPlaytime() => TotalPlaytime = PlaytimeFormat.Duration(Math.Max(_localMinutes, _matchMinutes));

    private void ApplyCached(string profile)
    {
        _vbucks = null; _matchMinutes = 0; _loaded = false;
        Recent.Clear(); OnPropertyChanged(nameof(ShowRecentPlaceholder)); OnPropertyChanged(nameof(LatestMatch));
        Kills = Wins = Matches = "0"; WinRate = "0%";
        OnPropertyChanged(nameof(VBucksText)); OnPropertyChanged(nameof(HasVBucks));
        try { if (File.Exists(CacheFile(profile))) Apply(File.ReadAllText(CacheFile(profile))); } catch (Exception) { }
    }

    /// <summary>Shows a launcher/home reply: {vbucks, menuActive, totals:{kills,wins,matches,hours}, recent:[...]}.</summary>
    public void Apply(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("vbucks", out var vb) && vb.TryGetInt64(out long v)) { _vbucks = v; OnPropertyChanged(nameof(VBucksText)); OnPropertyChanged(nameof(HasVBucks)); }
        MenuActive = root.TryGetProperty("menuActive", out var m) && m.ValueKind == JsonValueKind.True;
        if (root.TryGetProperty("totals", out var t))
        {
            Kills = Num(t, "kills"); Wins = Num(t, "wins"); Matches = Num(t, "matches");
            WinRate = StatsViewModel.Percent(Int(t, "wins"), Int(t, "matches"));
            // Time in matches as the backend counts it (any season, also matches the launcher didn't time, such as
            // a friend's server), so Total Playtime shows whichever total is larger.
            _matchMinutes = t.TryGetProperty("hours", out var h) && h.TryGetDouble(out double hours) ? hours * 60 : 0;
            ShowTotalPlaytime();
        }
        Recent.Clear();
        if (root.TryGetProperty("recent", out var recent) && recent.ValueKind == JsonValueKind.Array)
            foreach (var r in recent.EnumerateArray().Take(4))
            {
                if (r.ValueKind != JsonValueKind.Object) continue;
                DateTime? at = r.TryGetProperty("at", out var a) && a.ValueKind == JsonValueKind.String &&
                               DateTime.TryParse(a.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when) ? when : null;
                Recent.Add(new RecentMatchRow(at, Int(r, "season"), Str(r, "mode"), r.TryGetProperty("arena", out var ar) && ar.ValueKind == JsonValueKind.True,
                    Int(r, "place"), Int(r, "players"), Int(r, "kills"), Int(r, "seconds"), r.TryGetProperty("won", out var w) && w.ValueKind == JsonValueKind.True));
            }
        OnPropertyChanged(nameof(ShowRecentPlaceholder)); OnPropertyChanged(nameof(LatestMatch));
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.TryGetDouble(out double d) && d > 0 ? (int)Math.Min(int.MaxValue, Math.Floor(d)) : 0;
    private static string Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.TryGetDouble(out double d) ? Math.Floor(d).ToString("N0") : "0";
}
