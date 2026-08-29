@echo off
setlocal
cd /d "%~dp0"
title Morse Trainer - startup
set "MORSE_LOG=%~dp0morse-start.log"

echo Starting Morse Trainer...
echo The first launch may take up to a minute while the project is compiled.
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK was not found.
    echo Install .NET SDK 8 and restart Windows.
    goto :finish
)

dotnet run --project ".\src\MorseTrainer\MorseTrainer.csproj" > "%MORSE_LOG%" 2>&1
set "MORSE_EXIT_CODE=%ERRORLEVEL%"

type "%MORSE_LOG%"
echo.
if "%MORSE_EXIT_CODE%"=="0" (
    echo Morse Trainer was closed normally.
) else (
    echo ERROR: Morse Trainer exited with code %MORSE_EXIT_CODE%.
    echo Send the morse-start.log file for diagnosis.
)

:finish
echo.
echo This window will remain open until you press a key.
pause >nul
exit /b
