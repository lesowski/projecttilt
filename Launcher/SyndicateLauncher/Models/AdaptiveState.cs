using System;

namespace SyndicateLauncher.Models;

/// <summary>
/// A player's adaptive bot level, saved per profile in settings.json (AppSettings.Adaptive). Lives in Models so the
/// Release obfuscation (obfuscar.xml skips this namespace) never renames the saved JSON keys.
/// </summary>
public sealed class AdaptiveState
{
    /// <summary>0-100, the value every bot skill slider gets. New players start in the middle.</summary>
    public int Level { get; set; } = 50;
    /// <summary>The newest match already counted, so each match moves the level once.</summary>
    public DateTime? LastMatchUtc { get; set; }
    public int MatchesCounted { get; set; }
    /// <summary>How much the last counted match moved the level (for the Settings text).</summary>
    public int LastChange { get; set; }
}
