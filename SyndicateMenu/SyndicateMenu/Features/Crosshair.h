#pragma once

// Syndicate custom crosshair: drawn on the game's own frame (the Present hook), at the exact centre
// of what the game renders, so it stays centred in windowed, fullscreen and stretched resolutions.
// The launcher (Settings > Display > Crosshair) writes crosshair.ini to %LOCALAPPDATA%\ProjectTilted;
// changes there show up in game within half a second, even mid-match.
//
// The launcher loads SyndicateMenu.dll for the crosshair alone when the menu setting is off. It then
// writes menu-load.txt = "crosshair" first, and the DLL runs in crosshair-only mode: no game-object
// scan, no hooks besides Present/ResizeBuffers, no keys (Insert and End go to the game), no menu.

#ifdef TILT_CROSSHAIR_ONLY
// Public Tilt build (msbuild /p:TiltCrosshairOnly=true): crosshair-only is a compile-time constant, so every
// menu, ESP, aim and weapon-mod code path is dead code the linker removes. menu-load.txt is never read.
constexpr bool CrosshairOnlyMode() { return true; }
#else
bool CrosshairOnlyMode();     // read once from menu-load.txt
#endif
void TickCrosshairConfig();   // reloads crosshair.ini when it changed (cheap, checks twice a second)
void DrawCrosshair(float width, float height, bool menuOpen);
