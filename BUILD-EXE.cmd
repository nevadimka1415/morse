@echo off
setlocal
cd /d "%~dp0"
title Morse Trainer - EXE builder

set "MORSE_PROJECT=%~dp0src\MorseTrainer\MorseTrainer.csproj"
set "MORSE_OUTPUT=%~dp0release"

echo Building MorseTrainer.exe using the installed .NET 10 runtime...
echo No large Windows runtime package download is required.
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK 10 was not found.
    goto :failed
)

if exist "%MORSE_OUTPUT%\MorseTrainer.exe" del /q "%MORSE_OUTPUT%\MorseTrainer.exe"

dotnet publish "%MORSE_PROJECT%" ^
    --configuration Release ^
    --runtime win-x64 ^
    --self-contained false ^
    --output "%MORSE_OUTPUT%" ^
    -p:Version=2.2.1 ^
    -p:PublishSingleFile=true ^
    -p:RestoreIgnoreFailedSources=true ^
    -p:DebugType=None ^
    -p:DebugSymbols=false

if errorlevel 1 goto :failed
if not exist "%MORSE_OUTPUT%\MorseTrainer.exe" goto :failed

echo.
echo SUCCESS: application created:
echo %MORSE_OUTPUT%\MorseTrainer.exe
explorer /select,"%MORSE_OUTPUT%\MorseTrainer.exe"
echo.
pause
exit /b 0

:failed
echo.
echo ERROR: EXE build failed. Copy the output above and send it for diagnosis.
echo.
pause
exit /b 1
