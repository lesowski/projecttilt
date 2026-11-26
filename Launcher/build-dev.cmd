@echo off
rem Tilt launcher, developer edition: Launcher\dist\developer\TiltLauncher-Developer.exe (local testing tools).
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\dev\build-edition.ps1" -Edition Developer %*
echo.
if not "%TILT_NO_PAUSE%"=="1" pause
