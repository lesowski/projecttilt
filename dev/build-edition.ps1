# Builds one edition of the Tilt launcher into its own folder:
#   Public    -> Launcher\dist\public\TiltLauncher.exe
#   Developer -> Launcher\dist\developer\TiltLauncher-Developer.exe
# Called by Launcher\build-public.cmd and Launcher\build-dev.cmd.
#
# Public: built without DEV_BUILD (no in-game menu, no Studio, no developer backend routes, no Save the World
# tools) and obfuscated. Its SyndicateMenu.dll is the crosshair-only build (TiltCrosshairOnly): the menu, ESP, aim
# and weapon mods are not compiled into it, so no setting or file can turn them on. SyndicateStudio.dll is left out.
# Developer: built with DEV_BUILD (DEVELOPER BUILD badge, Studio, menu toggle) and the full SyndicateMenu.dll.
#
# The game DLLs that are not built from this repo (cobalt.dll, memory.dll, console.dll, lawinserver.exe, ...) come
# from the dev kit: pass -RuntimeFrom <folder with dlls\ and backend\>, or keep them in Documents\Syndicate\Runtime.
# Without them the launcher still builds, but it can't start a match until Runtime\ is completed.
param(
  [Parameter(Mandatory = $true)][ValidateSet('Public', 'Developer')][string]$Edition,
  [string]$RuntimeFrom = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$repo = Split-Path $PSScriptRoot -Parent
$dev = $Edition -eq 'Developer'
$name = if ($dev) { 'TiltLauncher-Developer.exe' } else { 'TiltLauncher.exe' }
$out = Join-Path $repo ('Launcher\dist\' + $Edition.ToLowerInvariant())
$work = Join-Path $env:LOCALAPPDATA ('PassionProject\dev\edition-' + $Edition.ToLowerInvariant())
$project = Join-Path $repo 'Launcher\SyndicateLauncher\SyndicateLauncher.csproj'
Write-Host "Tilt launcher, $Edition edition"
Write-Host "Output: $out"

# ---- launcher ------------------------------------------------------------------------------------------------
Step 'Launcher'
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (!$dotnet) { $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
if (!(Test-Path $dotnet)) { throw 'The .NET 8 SDK was not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0' }
# A clean intermediate folder per build: the two editions compile the same project with different constants.
foreach ($d in 'obj\Release', 'bin\Release') {
  $p = Join-Path (Split-Path $project) $d
  if (Test-Path $p) { Remove-Item -LiteralPath $p -Recurse -Force }
}
$publish = Join-Path $work 'publish'
if (Test-Path $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$devFlag = if ($dev) { 'true' } else { 'false' }
$code = Invoke-Native { & $dotnet publish $project -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none -p:DebugSymbols=false "-p:SyndicateDevBuild=$devFlag" -o $publish -v minimal }
if ($code -ne 0) { throw "The launcher build failed (dotnet publish exit $code)." }
New-Item -ItemType Directory -Path $out -Force | Out-Null
Get-ChildItem $out -Filter 'TiltLauncher*.exe' | Remove-Item -Force
Copy-Item -LiteralPath (Join-Path $publish 'SyndicateLauncher.exe') -Destination (Join-Path $out $name) -Force
Write-Host "  $name"

# ---- runtime ---------------------------------------------------------------------------------------------------
Step 'Runtime'
$runtime = Join-Path $out 'Runtime'
if (!$RuntimeFrom) {
  $kit = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Syndicate\Runtime'
  if (Test-Path (Join-Path $kit 'dlls')) { $RuntimeFrom = $kit }
}
if ($RuntimeFrom) {
  if (!(Test-Path (Join-Path $RuntimeFrom 'dlls'))) { throw "$RuntimeFrom has no dlls folder." }
  Write-Host "  copying the runtime from $RuntimeFrom"
  if (Test-Path $runtime) { Remove-Item -LiteralPath $runtime -Recurse -Force }
  Copy-Item -LiteralPath $RuntimeFrom -Destination $runtime -Recurse
} else {
  Write-Host '  no dev kit runtime found (Documents\Syndicate\Runtime or -RuntimeFrom): only the DLLs built here are added.' -ForegroundColor Yellow
  # Fresh copy of the repo's runtime data (backend profiles, configs, game-10.40.json, OWNERSHIP.txt) on every
  # build, so an edited file never stays stale from an earlier build; binaries are not in Git.
  if (Test-Path $runtime) { Remove-Item -LiteralPath $runtime -Recurse -Force }
  Copy-Item -LiteralPath (Join-Path $repo 'Launcher\SyndicateLauncher\Runtime') -Destination $runtime -Recurse
}
$dlls = Join-Path $runtime 'dlls'
New-Item -ItemType Directory -Path $dlls -Force | Out-Null
# The release tag the update check compares with GitHub (Services\UpdateChecker.cs) always comes from the repo,
# so a runtime copied from a dev kit can't ship an old tag and nag players about an update they already have.
$tag = Join-Path $repo 'Launcher\SyndicateLauncher\Runtime\release-tag.txt'
if (Test-Path -LiteralPath $tag) {
  Copy-Item -LiteralPath $tag -Destination (Join-Path $runtime 'release-tag.txt') -Force
  Write-Host ('  release tag ' + (Get-Content -LiteralPath $tag -Raw).Trim())
}

# ---- SyndicateMenu.dll (crosshair-only in the public edition) ----------------------------------------------------
Step ('SyndicateMenu.dll (' + $(if ($dev) { 'full menu' } else { 'crosshair only' }) + ')')
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = if (Test-Path $vswhere) { (& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -format json | Out-String) | ConvertFrom-Json | Select-Object -First 1 }
if (!$vs) { throw 'Visual Studio (or Build Tools) with "Desktop development with C++" is needed to build SyndicateMenu.dll.' }
$msbuild = Join-Path $vs.installationPath 'MSBuild\Current\Bin\MSBuild.exe'
$toolset = if ([int]($vs.installationVersion.Split('.')[0]) -ge 18) { 'v145' } else { 'v143' }
$menuOut = (Join-Path $work 'menu\out') + '\'
$menuInt = (Join-Path $work 'menu\int') + '\'
$menuLog = Join-Path $work 'menu-build.log'
$crosshairOnly = if ($dev) { 'false' } else { 'true' }
$code = Invoke-Native { & $msbuild (Join-Path $repo 'SyndicateMenu\SyndicateMenu\SyndicateMenu.vcxproj') /t:Rebuild /p:Configuration=Release /p:Platform=x64 `
  "/p:PlatformToolset=$toolset" "/p:TiltCrosshairOnly=$crosshairOnly" "/p:OutDir=$menuOut" "/p:IntDir=$menuInt" /m /nologo /v:minimal *> $menuLog }
$menuDll = Join-Path $menuOut 'SyndicateMenu.dll'
if ($code -ne 0 -or !(Test-Path $menuDll)) {
  Get-Content -LiteralPath $menuLog | Select-String -Pattern ': error ' | Select-Object -First 10 | ForEach-Object { Write-Host "  $($_.Line.Trim())" -ForegroundColor Red }
  throw "Building SyndicateMenu.dll failed. Full log: $menuLog"
}
Copy-Item -LiteralPath $menuDll -Destination (Join-Path $dlls 'SyndicateMenu.dll') -Force
Write-Host '  Runtime\dlls\SyndicateMenu.dll'
if (!$dev) {
  # Developer-only modules never ship in the public edition.
  foreach ($devOnly in 'SyndicateStudio.dll') {
    $p = Join-Path $dlls $devOnly
    if (Test-Path $p) { Remove-Item -LiteralPath $p -Force; Write-Host "  removed $devOnly (developer edition only)" }
  }
}

# ---- manifest --------------------------------------------------------------------------------------------------
Step 'Manifest'
# The launcher only starts the game when every DLL matches Runtime\dlls\manifest.json.
Update-ModuleManifest $dlls | Out-Null
Write-Host '  Runtime\dlls\manifest.json'

# ---- completeness ----------------------------------------------------------------------------------------------
Step 'Completeness'
# Same list as Services\RuntimeBundle.cs (Required). A launcher without these shows "The launcher file bundle is
# incomplete" on every season, so a public build stops here instead of producing a package players can't use.
$required = 'dlls\manifest.json', 'dlls\Erbium.dll', 'dlls\cobalt.dll', 'dlls\memory.dll', 'dlls\console.dll',
  'dlls\Syndicate1040.dll', 'dlls\SyndicateConsoleRecovery.dll', 'dlls\SyndicateMenu.dll', 'game-10.40.json',
  'backend\lawinserver.exe', 'backend\titanium-profiles.cjs', 'backend\titanium-modes.cjs',
  'backend\CloudStorage\DefaultEngine.ini', 'backend\CloudStorage\DefaultInput.ini', 'backend\CloudStorage\DefaultGame.ini'
$missing = @($required | Where-Object { !(Test-Path -LiteralPath (Join-Path $runtime $_)) })
if ($missing.Count -gt 0) {
  $missing | ForEach-Object { Write-Host "  missing Runtime\$_" -ForegroundColor Red }
  if (!$dev) { throw 'The runtime is incomplete; players could not start a match. Pass -RuntimeFrom <complete runtime>.' }
  Write-Host '  (developer build: continuing)' -ForegroundColor Yellow
} else { Write-Host '  all runtime files present' }
Write-Host '  A release package also needs Syndicate-backend (with node_modules) and Dependencies\Node + Dependencies\MongoDB next to the exe.'

Write-Host ''
Write-Host "Done: $(Join-Path $out $name)" -ForegroundColor Green
