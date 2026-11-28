# Changelog

Launcher releases, newest first. Version numbers follow `<Version>` in `Launcher/SyndicateLauncher/SyndicateLauncher.csproj` and the in-launcher Patch Notes (`Launcher/SyndicateLauncher/Models/PatchNotes.cs`). Releases up to 1.3.1 are listed only in the Patch Notes.

## 1.4.6 - November 29, 2026

### Loot

- Season 2 (1.11, 2.5): trap drops (chests, floor loot, supply drops) become a directional jump pad 40% of the time and a jump pad 27% of the time.
- Season 6 (6.20): half of the trap drops become bouncers.
- An item a build does not have is looked up once and the drop stays as rolled.

## 1.4.5 - November 28, 2026

### Bug fixes

- Fixed "MongoDB exited during startup" on every season. The launcher killed MongoDB (`Process.Kill`) after every match, on every season switch and on a failed launch; a kill that landed while it wrote its journal left the database unable to recover (WiredTiger "Recovery failed", a missing `WiredTigerLog` file), so every later start aborted. Each failed start also left three new 100 MB journal files.
- MongoDB now stays running between matches and season switches (only the Node backend restarts) and is shut down cleanly when the launcher closes (the `shutdown` command, killed only if it does not answer within 8 seconds).
- A database that fails to start because it is damaged is repaired with MongoDB's own `--repair`. If that fails, the folder is kept as `SyndicateDatabase-damaged-<date>` and a new database is made; accounts are recreated on sign-in.

## 1.4.4 - November 28, 2026

### Bug fixes

- Fixed the Erbium match server crashing in `InternalPickup` when a pickup overflowed a full stack: the overflow stack was given before the old stack was updated, which could move the inventory arrays and leave the code writing through stale pointers. Players froze, then were kicked to the lobby with a network error.

### Bots

- Chapter 2 (11.00): bots get a water map built from the level's water bodies (sea, lakes, ponds, rivers, swamps, reservoirs), measured piece by piece. They no longer path into water, pick destinations under it or land in it; bots in water (waist deep or more) stop shooting and head for the shore; bots with materials build floors across at surface level.
- Bot status lines in the bot log now include the movement mode, the water surface over the bot, how long it has been stalled and its current reason.

## 1.4.3 - November 27, 2026

### Seasons

- Added Chapter 2 Season 1 (Fortnite 11.00). The backend now gets season 11 for it, so it loads that season's Battle Pass and challenges. The later Chapter 2 seasons stay locked in the public launcher.

### Bug fixes

- Fixed Pre-season, Season 1 and Season 2 waiting forever on "Waiting for players to ready up". Those seasons only log in to party chat over TLS and only trust certificates built into the game. The launcher now makes its own loopback certificate per install (`%APPDATA%SyndicateLauncherlegacy-xmpp-tls`), gives it to the backend and writes it into the game exe on Play (backup: `.tilt-original`).

## 1.4.2 - November 27, 2026

### Miscellaneous

- Renamed the backend folder in the download from Syndicate-backend to Tilt-backend. The launcher still finds a Syndicate-backend folder, so older installs and dev kits keep working.

## 1.4.1 - November 27, 2026

### Bots

- Added a Bot skill slider under Difficulty in Settings > Match & bots: one slider from 0 to 100 that sets all 20 bot skills at once.
- Easy, Medium, Hard and Sweat sit at 25, 50, 75 and 100; anything in between makes a custom difficulty. The individual skills are still under Fine-tune bot skills.

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
