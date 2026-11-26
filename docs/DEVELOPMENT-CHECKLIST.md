# Development checklist

Status of the project measured against the "OG Fortnite launcher" brief. Written on 2026-11-27 from an inspection of this source tree. Each item is marked:

- `[x]` exists in the source
- `[~]` partial
- `[ ]` missing
- `[!]` blocked: needs something that isn't in this repo

## Phase 1: analysis (done)

Syndicate is not a starting skeleton. It is a finished, released project (launcher v1.3.1) with about 1 MB of bot AI in C++, two game servers, a Node backend and a WPF launcher. Rebuilding it from scratch would throw working code away. The remaining work is the gaps listed below.

| Component | Language | Where | State |
|---|---|---|---|
| Launcher | C# WPF, .NET 8 | `Launcher/SyndicateLauncher` | Complete: seasons, play, locker via backend, themes, settings, Studio (dev builds only) |
| Season X game server | C++ DLL | `ServerAndConsole` (`Syndicate1040.dll`) | Complete: bots, loot, storm, vehicles, traps, late game |
| Chapter 1 game server | C++ DLL | `SyndicateClassic/game-server/Erbium` | Complete: SpectraBots for builds 1.7.2 to 10.31 |
| Backend | Node.js, Express, MongoDB, XMPP | `SyndicateClassic/backend` | Complete: accounts, locker, battle pass, shop, progression, arena |
| In-game menu | C++ DLL, ImGui | `SyndicateMenu` | Complete (affects bots only) |

### Prebuilt binaries that are not in this repo `[!]`

The launcher refuses to start a match without these files. They come from the dev kit (`Documents\Syndicate`) or a GitHub release:

- `cobalt.dll`, `console.dll`, `memory.dll`: third-party prebuilt modules. There is no source for them here, so they can't be rebuilt.
- `lawinserver.exe`: the prebuilt backend for Season X. `Launcher/BackendSource` is a reference copy only.
- `SyndicateClient.dll`, `SyndicatePadAssist.dll`, `TiltedInject.exe`: prebuilt client modules.
- Fortnite builds: the player supplies their own.

## Phase 2: build environment

Installed with winget on 2026-11-27:

| Tool | Needed for | Status |
|---|---|---|
| .NET 8 SDK 8.0.428 (desktop runtime 8.0.34) | launcher | installed |
| Visual Studio Build Tools 2026 (18.12), C++ workload, toolset v145 | `Syndicate1040.dll`, `Erbium.dll`, `SyndicateMenu.dll`, `SyndicateStudio.dll` | installed |
| Git 2.56.1 (per user, `%LOCALAPPDATA%\Programs\Git`) | `UPDATE FROM GIT.cmd` workflow | installed |
| Node.js v24.19.0 | backend | was already installed |
| MongoDB | backend | not checked (normally ships in the dev kit's `Dependencies`) |

Backup: the untouched source download is at `Downloads\Syndicate-Source-Code-main.zip`. The folder isn't a Git repository yet.

### Launcher build: works

- `dotnet build Launcher\SyndicateLauncher.sln -c Release` builds and obfuscates with 0 errors. The only warning is an unused field, `MainWindow._padTriggersHeld`.
- The dev build (`-p:SyndicateDevBuild=true`, what `UPDATE FROM GIT.cmd` makes) builds with 0 errors.
- **Fixed:** the Release build failed on any PC whose .NET desktop runtime wasn't exactly `8.0.32`. `obfuscar.xml` hard-coded that folder, and the csproj comment claiming the target replaced it was wrong. `obfuscar.xml` now holds a placeholder. The `ObfuscateAssembly` target fills in `$(NetCoreRoot)shared\Microsoft.WindowsDesktop.App\$(BundledNETCoreAppPackageVersion)`, writes `obj\...\obfuscar.generated.xml`, and stops with a clear error if that runtime is missing.
- Files extracted from the downloaded zip carry Windows' "downloaded from the internet" mark. `dotnet tool restore` refuses to read a marked `Launcher\.config\dotnet-tools.json`, so that one file was unblocked (`Unblock-File`). On a fresh copy of the zip, run `Unblock-File Launcher\.config\dotnet-tools.json` once.
### Game server and menu builds: work

MSBuild from Build Tools 2026, `Release|x64`, `/p:PlatformToolset=v145`, the same arguments `dev/update-from-git.ps1` uses. All exit 0:

- `ServerAndConsole/10.40.vcxproj` builds `Syndicate1040.dll`
- `SyndicateMenu/SyndicateMenu/*.vcxproj` builds `SyndicateMenu.dll`
- `SyndicateClassic/game-server/Erbium/Erbium.vcxproj` (with `SolutionDir=game-server\`) builds `Erbium.dll`
- `SyndicateClassic/game-server/SyndicateStudio/SyndicateStudio.vcxproj` builds `SyndicateStudio.dll`

Freshly built DLLs won't match the hashes in `Runtime/dlls/manifest.json`. `UPDATE FROM GIT.cmd` rewrites the manifest when it installs them into `Documents\Syndicate\Runtime\dlls`, and that needs the dev kit folder.

- **WPF tests** (`dotnet run --project Launcher\WpfTests -c Debug -- <png folder>`): **44/44 pass** (difficulty chips, presets saved across restarts, the 20 skill sliders, adaptive difficulty rules and chip, Erbium tier mapping, the Statistics page with a sample backend reply, no WPF binding errors). `ui-difficulty-chips.png` shows the six chips. PNGs go to the folder you pass: `ui-all-skills.png` shows the slider panel, `ui-statistics.png` the Statistics page. `ui-difficulty.png` still comes out as a blank background because the test clears the settings fade-in animation, so the panel stays invisible; this is a test artefact.

Backend unit tests (`node --test`, the files listed in `package.json`): **39 pass, 3 fail** (35 + 3 before the career test was added). All three failures are Discord webhook/relay delivery tests. They expect the original team's Cloudflare relay, which this public release removed on purpose (see README). No gameplay code is involved.

## Season support

| Season | Build | Public launcher | Notes |
|---|---|---|---|
| C1 Pre-season | 1.7.2 | supported | Erbium |
| C1 S1 | 1.8.2 | supported | Erbium, legacy XMPP (TLS 1.0 on 7071) |
| C1 S2 | 1.11 (2.5 dev only) | supported | 2.5 crashes, so only dev builds accept it |
| C1 S3-S9 | 3.5.1, 4.5, 5.10, 6.20, 7.40, 8.50, 9.40 | supported | Erbium |
| C1 Season X | 10.40 | supported | main Syndicate server |
| C2 S1 | 11.00 | **dev builds only** | not finished |
| C2 S2 | 12.50 | **dev builds only** | no backend, bots or loot yet |
| C2 S3-S8 | none | not released | catalogue entries only |

The public launcher already marks only playable seasons as available (`Models/SeasonCatalog.cs`, `#if DEV_BUILD`). Build detection: `Services/BuildDetector.cs`, `Services/GameInstallation.cs`.

## Feature checklist against the brief

### Seasons
- [x] Multiple seasons, chapter grouping, artwork, dates
- [x] Build detection and validation (`BuildDetector.cs`, DLL manifest SHA-256 check)
- [x] Supported vs. unsupported shown honestly
- [~] "Experimental" tier: Chapter 2 is hidden in public builds rather than shown as experimental

### Bots (`ServerAndConsole/PlayerBots.h`, `Bot*.h`, `CombatSkills.h`, Erbium `SpectraBots`)
- [x] Bus jump, landing choice, gliding, looting, chests, inventory, healing
- [x] Combat: aim, tracking, prediction, strafing, jumping
- [x] Building, edits, high ground, defensive builds, harvesting
- [x] Storm rotation, navigation, vehicles (Season X)
- [x] 0-99 bots, ranges such as 40-60 (`MatchOptions.cs`)
- [x] 20 tunable skill sliders with `.fnp` presets (`Models/BotPreset.cs`, `ViewModels/DifficultyViewModel.cs`). Shown in Settings > Match & bots > **Fine-tune bot skills** (added 2026-11-27), under the difficulty chips. Groups: Aim (accuracy, tracking, reaction speed, firing frequency, movement prediction), Fighting (aggression, pursuit, strafing, combat jumping, awareness), Building (build frequency and speed, high ground, defensive building, window edit frequency and speed), Survival (looting, material gathering, healing, early rotation). Each skill's description is a tooltip. Moving a slider makes a saved Custom difficulty. The panel also has a preset picker, Save preset, and Import/Export `.fnp`.
- [~] Difficulty names: existing levels are Bot / Easy / Medium / Hard / Sweat / Variety (mixed). The brief's Beginner / Normal / Expert line up with these.
- [x] **Adaptive** difficulty (added 2026-11-27): an Adaptive chip next to Bot / Easy / Medium / Hard / Variety (`Services/AdaptiveDifficulty.cs`, `Models/AdaptiveState.cs`).
  - How the level moves: each player has a level from 10 to 100, starting at 50. Before each match server starts, each new result moves it:
    - Victory Royale: +6
    - top 10%: +3
    - top 25%: +1
    - bottom half: -2
    - bottom quarter: -4
    - eliminations: +1 to +3 for 3 or more, -1 for none
    - the total for one match is capped between -5 and +8
  - The first time Adaptive is turned on, only the last 10 matches seed the level. Arena matches don't count. Each match counts once.
  - Where results come from: the backend's `launcher/stats` history, or the Statistics page's cache when the backend isn't up yet.
  - Hook: `GameLauncher.DifficultyForNextMatch` is asked in `StartHostOnlyAsync`, so the next match and the Fast Queue background server pick up the new level. PLAY also updates it first.
  - Limits:
    - Season X resets matches inside one server, so its level changes only when PLAY starts a new server.
    - Fast Queue loads the next server during the current match, so the level there lags one match behind.
    - Pre-season to Season 9 get the nearest of four tiers, not the exact level.
    - The bots play by the same rules at every level; only the skill values change.
  - Picking another difficulty, moving a slider or loading a preset turns Adaptive off. The level is kept per player in `settings.json` (`Adaptive`), in `Models` so obfuscation never renames its keys.
  - Not tried in a real match yet.
- [x] **Fixed: custom presets in Pre-season to Season 9.** Erbium only reads `TILTED_BOT_SKILL` (bot / easy / medium / hard / variety). Any custom slider preset used to reach it as "medium" whatever the sliders said. It now gets the tier nearest its slider average (`GameLauncher.SyndicateSkillName`), and the slider panel says Season X is the only season that uses all 20 sliders.

### Backend and match flow
- [x] Local accounts, profiles, locker, battle pass, shop, XMPP, matchmaker
- [x] Starts and stops automatically (`Services/SyndicateBackend.cs`, `CompanionProcesses.cs`)
- [x] Crash recovery and logs (`RuntimeRecovery.cs`, `ErrorReporter.cs`, `LaunchProblem.cs`, `%LOCALAPPDATA%\PassionProject`)
- [x] Kills, placement and wins recorded on the backend (`User/Mongodb/Schema/user.js`)

### Launcher pages
- [x] Home, season wheel and browser, Play, Settings, Themes, Locker (in game, through the backend)
- [x] **Statistics** (added 2026-11-27): STATS tab, plus a Statistics item in the sidebar and in gamepad page switching (`ViewModels/StatsViewModel.cs`, "STATISTICS PAGE" in `MainWindow.xaml`).
  - Totals: matches, Victory Royales, win rate, eliminations, eliminations per match, top finishes, time played, average placement.
  - A Solo / Duos / Squads table.
  - Match history (last 50: result, placement and lobby size, season, mode, date, eliminations, time alive) and best win streak.
  - Playtime by season.
  - Data: career totals and history come from the backend's `POST /launcher/stats`. Playtime by season comes from the launcher's session log. The last backend reply is cached per profile (`stats\<profile>.career.json`, moved or deleted with the profile), so the page still shows numbers, with a notice, while the backend is off.
  - Backend changes:
    - `syndicateCareerCore.js` now keeps `syndicate_recent_matches`. Kills are counted until the player's `match` event, because Erbium events carry no match id.
    - `/launcher/stats` returns `recent`.
    - `syndicateProgression.js` now reads the progression queue oldest first. File names are random GUIDs, so a last kill could otherwise be counted in the next match.
    - New test: `src/Utils/test/syndicateCareer.test.js`.
  - History starts with the first match played after this backend update; older matches only appear in the totals.
  - Not tested against a live backend or a real match: this PC has no MongoDB, `node_modules` or game files. Covered by the 4 backend unit tests and 12 WPF checks that use a sample backend reply.

## Next steps, in order

1. ~~Install the toolchain~~ (done, see Phase 2).
2. **Get the dev kit** or the prebuilt DLLs and `lawinserver.exe` (see above). Without them a built launcher can't start a match.
3. ~~Bot skill sliders in Settings~~ (done; the WPF test checks all 20 render, the 4 groups, and that an on-screen change is saved).
4. ~~Statistics page~~ (done; needs one real match on a dev-kit install to confirm end to end).
5. ~~Adaptive difficulty~~ (done; needs a few real matches on a dev-kit install to tune the step sizes).
6. Optional: show Chapter 2 as "Experimental" in dev builds instead of as a normal available season.

## Limits that won't change

- Fortnite builds, art and the Burbank font belong to Epic Games. Players need their own legally obtained builds.
- No anti-cheat, DRM or anti-virus evasion work (project rule).
- Menu ESP and aim features only ever target bots.
