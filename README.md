# Project Tilt

Tilt is a local Fortnite launcher for Chapter 1 (Season X / 10.40 base, plus Erbium-based Chapter 1 seasons) that you play against AI bots: launcher, game servers, bot AI and backend. It is built on the open-source Syndicate code (MIT, see `LICENSE`); folder, file and code names inside the repo still say Syndicate, and players don't see them. See `CHANGELOG.md` for what changed in the rename.

- No support or updates are promised. Fortnite, its art, music and the Burbank font belong to Epic Games; you need your own legally obtained Fortnite builds.
- Nothing here connects to the original team's servers. Discord webhooks, the Cloudflare relay and redeem codes are off until you set your own (see `SyndicateClassic/backend/.env.example`, `TILTED_DISCORD_WEBHOOK`, `TILTED_RELAY_URL`, `TILTED_REDEEM_URL`, and `error-webhook.txt` next to the launcher).
- MIT licensed (see `LICENSE`): use, change and share it, keeping the copyright notice.

## Layout

- `SyndicateClassic/` — the former Project Tilted source (Erbium game server, launcher, Node backend, release tools), imported from `d4shyb0i/tilted-source` at `b2ba897`. See `SyndicateClassic/README.md` and `SyndicateClassic/CLAUDE.md`. Not merged with the main code yet.

Syndicate:

- `ServerAndConsole/` — C++ game-server DLL (`10.40.vcxproj`, builds `Syndicate1040.dll`), bot AI, looting, vehicles, match flow. Uses the generated Unreal SDK in `ServerAndConsole/SDK/` and MinHook.
- `Launcher/` — C# WPF launcher (`SyndicateLauncher.sln`, .NET 8) and the Node.js backend (`BackendSource/`, plus runtime data under `SyndicateLauncher/Runtime/backend/`).
- `PresetExamples/` — sample bot difficulty presets (`.fnp`).

## Dev setup (once per PC)

1. Get the **dev kit** zip from the host (made with `MAKE DEV KIT.cmd`) and unzip it. You get a `Syndicate` folder with the files that aren't in Git: the built launcher (`SyndicateLauncher.exe`) and `Runtime\` with the game DLLs and `lawinserver.exe`.
2. Install the **.NET 8 SDK** (https://dotnet.microsoft.com/download/dotnet/8.0). To build the game server too, install **Visual Studio** (2022 or 2026) with "Desktop development with C++".
3. Clone this repo with **GitHub Desktop**.
4. Double-click **`UPDATE FROM GIT.cmd`** in the clone. The first time it asks for your `Syndicate` folder, then remembers it.
5. Start Syndicate with `Syndicate\SyndicateLauncher.exe`.

## Every update

1. GitHub Desktop: **Fetch origin**, then **Pull origin**.
2. Double-click **`UPDATE FROM GIT.cmd`**.

It copies changed runtime files (backend data, configs) into `Syndicate\Runtime`, rebuilds `Syndicate1040.dll` and `SyndicateConsoleRecovery.dll` when `ServerAndConsole/` changed, refreshes `Runtime\dlls\manifest.json` so the launcher accepts the new DLLs, and rebuilds `SyndicateLauncher.exe` when the launcher or runtime changed. Replaced files are kept in `Syndicate\_backups\`. `UPDATE FROM GIT.cmd -Force` rebuilds everything. Launcher profiles, lockers and settings (in `%APPDATA%\SyndicateLauncher`) are never touched.

## Making a dev kit

Close Syndicate and Fortnite, then double-click **`MAKE DEV KIT.cmd`**. It zips your `Syndicate` folder to `Desktop\Syndicate-Dev-Kit-<date>.zip`, leaving out backups and logs. Send it privately (Discord / Google Drive), never on GitHub.

The first time, the host makes the `Syndicate` folder from an existing Syndicate build: copy the launcher's `dist` folder (the one with the launcher exe and `Runtime\`) to `Documents\Syndicate`, run `UPDATE FROM GIT.cmd` once, then `MAKE DEV KIT.cmd`.

## Building

### Launcher editions

| Edition | Command | Output | For |
|---|---|---|---|
| Public | `Launcher\build-public.cmd` | `Launcher\dist\public\TiltLauncher.exe` | players |
| Developer | `Launcher\build-dev.cmd` | `Launcher\dist\developer\TiltLauncher-Developer.exe` | project developers, local testing |

Both need the .NET 8 SDK and Visual Studio (or Build Tools) with "Desktop development with C++". Each build:
1. publishes the launcher as one self-contained exe;
2. builds its own `SyndicateMenu.dll` into `Runtime\dlls`;
3. writes `Runtime\dlls\manifest.json`.

The game DLLs that aren't built from this repo (`cobalt.dll`, `memory.dll`, `console.dll`, `lawinserver.exe`, ...) are copied from `Documents\Syndicate\Runtime` (the dev kit), or from the folder you pass with `-RuntimeFrom <folder>`. Without them the launcher builds and opens, but it can't start a match.

- **Public** is built without `DEV_BUILD`:
  - The in-game menu is not compiled in. Its Settings toggle is gone, and `settings.json` can't turn it on.
  - Its `SyndicateMenu.dll` is the crosshair-only build (`msbuild /p:TiltCrosshairOnly=true`). The menu, ESP, aim and weapon-mod code is not in it, so no file or setting can enable it.
  - Syndicate Studio, its tools, `SyndicateStudio.dll`, the backend's developer routes and the Save the World tools are left out.
  - The launcher assembly is obfuscated.
- **Developer** is built with `DEV_BUILD`:
  - It shows a DEVELOPER BUILD badge and title.
  - It keeps the in-game menu toggle (it only ever affects bots), Syndicate Studio and the developer routes.
  - `UPDATE FROM GIT.cmd` also builds this edition, as `Documents\Syndicate\SyndicateLauncher.exe`.

### Other parts

- **Server DLL:** open `ServerAndConsole/10.40.vcxproj` in Visual Studio and build `Release | x64`.
- **Signed release:** `Launcher/build.bat` is the original signed public build (output `Launcher/dist`). It publishes the public edition, but without the crosshair-only menu DLL and the manifest step; prefer `build-public.cmd`.

### Discord

Rich Presence shows the name of a Discord application as "Playing ...". Create a Discord application named **Tilt**, upload the logo under Rich Presence > Art Assets as `tilt`, and put its Application ID in `ApplicationId` in `Launcher/SyndicateLauncher/Services/DiscordPresence.cs`. Until then, presence is off.

Build output (`bin/`, `obj/`, `x64/`, `dist/`), `.exe` and `.dll` files are not tracked. The launcher's runtime expects the built `Syndicate1040.dll` and other DLLs in `Launcher/SyndicateLauncher/Runtime/dlls/` and the backend executable in `Launcher/SyndicateLauncher/Runtime/backend/`. Publish built binaries as GitHub Releases.
