@echo off
cd /d "%~dp0"
if exist "MovieTweaks\bin\Release\net9.0-windows\MovieTweaks.exe" (
    start "" "MovieTweaks\bin\Release\net9.0-windows\MovieTweaks.exe"
) else (
    start "" "MovieTweaks\bin\Debug\net9.0-windows\MovieTweaks.exe"
)
