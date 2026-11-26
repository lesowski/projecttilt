# Changelog

Launcher releases, newest first. Version numbers follow `<Version>` in `Launcher/SyndicateLauncher/SyndicateLauncher.csproj` and the in-launcher Patch Notes (`Launcher/SyndicateLauncher/Models/PatchNotes.cs`). Releases up to 1.3.1 are listed only in the Patch Notes.

## 1.4.0 - November 27, 2026

### Project Tilt - Launcher Update

#### Rebranding

- Project Syndicate has officially been renamed to Project Tilt.
- Updated launcher branding throughout the application.
- Updated Discord Rich Presence to display Tilt.

#### Removed

- Discontinued the achievements system.
- Removed cheat-related functionality from public launcher builds.

#### Developer Edition

- Introduced a separate developer build.
- Restricted developer-only gameplay testing and debugging tools to the developer edition.

#### Miscellaneous

- Updated the official donation link.
- Improved separation between public and developer functionality.

### Also in this version

- New Statistics page: career totals, solo/duo/squad breakdown, the last 50 matches and playtime by season.
- Bot skill sliders are back in Settings > Match & bots, with presets and .fnp import/export.
- Adaptive difficulty: bots get tougher after wins and deep runs, easier after early eliminations.

### Notes for developers

- Build the editions with `Launcher/build-public.cmd` (`Launcher/dist/public/TiltLauncher.exe`) and `Launcher/build-dev.cmd` (`Launcher/dist/developer/TiltLauncher-Developer.exe`). See README.md.
- Discord Rich Presence is off until the Application ID of a Discord application named **Tilt** is set in `Services/DiscordPresence.cs` (the previous ID belonged to the original team's application).
- Home now reads `POST /launcher/home`; `POST /launcher/achievements` and `/launcher/achievements/claim` are gone. Saved achievement progress stays in player profiles untouched.
- Internal names (assembly `SyndicateLauncher`, DLL names, `SYNDICATE_*`/`TILTED_*` variables, data folders) are unchanged, so existing installs and saves keep working.
