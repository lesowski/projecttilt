@echo off
rem Tilt launcher, public edition: Launcher\dist\public\TiltLauncher.exe (no in-game menu, no developer tools).
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\dev\build-edition.ps1" -Edition Public %*
echo.
if not "%TILT_NO_PAUSE%"=="1" pause
