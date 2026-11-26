using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using SyndicateLauncher.Models;

namespace SyndicateLauncher.Services;

/// <summary>One finished match as the backend's launcher/stats reports it (syndicateCareerCore.js).</summary>
public sealed record MatchResult(DateTime AtUtc, int Place, int Players, int Kills, bool Won, bool Arena);

/// <summary>
/// Adaptive difficulty (Settings &gt; Match &amp; bots &gt; Difficulty &gt; Adaptive). Before each match server
/// starts, the level moves with the player's newest results: a Victory Royale or a deep run makes the bots tougher,
/// an early elimination makes them easier, and eliminations add or take a little. Season X bots use the level for
/// all 20 skills; Erbium (Pre-season to Season 9) has four tiers and gets the nearest one (GameLauncher).
/// The bots themselves are unchanged and play by the same rules at every level.
/// </summary>
public static class AdaptiveDifficulty
{
    public const string PresetName = "Adaptive";
    public const int StartLevel = 50, MinLevel = 10, MaxLevel = 100;
    /// <summary>Turning Adaptive on for a player with history seeds the level from this many recent matches.</summary>
    public const int SeedMatches = 10;

    /// <summary>How one match moves the level. Placement is judged against the lobby size.</summary>
    public static int Change(MatchResult m)
    {
        int players = m.Players > 1 ? m.Players : 100;
        double share = m.Place > 0 ? (double)m.Place / players : 1; // 0.01 = first, 1 = first out
        int change = m.Won ? 6 : share <= 0.10 ? 3 : share <= 0.25 ? 1 : share > 0.75 ? -4 : share > 0.50 ? -2 : 0;
        change += m.Kills >= 10 ? 3 : m.Kills >= 6 ? 2 : m.Kills >= 3 ? 1 : m.Kills == 0 ? -1 : 0;
        return Math.Clamp(change, -5, 8);
    }

    /// <summary>
    /// Counts the matches newer than the last one counted (oldest first) and returns how many moved the level.
    /// The first time (no match counted yet) only the latest <see cref="SeedMatches"/> are used, so years of history
    /// can't swing the level all at once. Arena matches don't count: Arena lobbies are tuned separately.
    /// </summary>
    public static int Update(AdaptiveState state, IEnumerable<MatchResult> results)
    {
        var fresh = results.Where(r => !r.Arena && (state.LastMatchUtc is not DateTime last || r.AtUtc > last))
            .OrderByDescending(r => r.AtUtc).ToList();
        if (state.LastMatchUtc is null) fresh = fresh.Take(SeedMatches).ToList();
        fresh.Reverse();
        foreach (var m in fresh)
        {
            int before = state.Level;
            state.Level = Math.Clamp(state.Level + Change(m), MinLevel, MaxLevel);
            state.LastChange = state.Level - before;
            state.LastMatchUtc = m.AtUtc;
            state.MatchesCounted++;
        }
        // Remember "now" on the first run with no history, so old matches synced later aren't counted.
        state.LastMatchUtc ??= DateTime.UtcNow;
        return fresh.Count;
    }

    /// <summary>The preset sent to the match server: every skill at the level.</summary>
    public static BotPreset Preset(int level)
    {
        int value = Math.Clamp(level, 0, 100);
        return new BotPreset { Name = PresetName, Mixed = false, BuiltinIndex = -1, Values = BotPreset.Definitions.ToDictionary(d => d.Key, _ => value) };
    }

    /// <summary>The Erbium tier (TILTED_BOT_SKILL) closest to a slider average: the built-ins sit at 5, 25, 50 and 75.</summary>
    public static string ErbiumTier(double average) => average < 15 ? "bot" : average < 38 ? "easy" : average < 63 ? "medium" : "hard";

    public static string TierName(int level) => ErbiumTier(level) switch { "bot" => "Bot", "easy" => "Easy", "hard" => "Hard", _ => "Medium" };

    /// <summary>The "recent" list of a launcher/stats reply. Entries without a valid date are skipped.</summary>
    public static List<MatchResult> ParseRecent(string json)
    {
        var list = new List<MatchResult>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("recent", out var recent) || recent.ValueKind != JsonValueKind.Array) return list;
        foreach (var r in recent.EnumerateArray())
        {
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("at", out var a) || a.ValueKind != JsonValueKind.String ||
                !DateTime.TryParse(a.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at))
                continue;
            list.Add(new MatchResult(at, Int(r, "place"), Int(r, "players"), Int(r, "kills"),
                r.TryGetProperty("won", out var w) && w.ValueKind == JsonValueKind.True,
                r.TryGetProperty("arena", out var ar) && ar.ValueKind == JsonValueKind.True));
        }
        return list;
    }

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.TryGetDouble(out double d) && d > 0 ? (int)Math.Min(int.MaxValue, Math.Floor(d)) : 0;
}
