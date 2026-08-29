@echo off
setlocal
cd /d "%~dp0"

echo [1/3] Checking .NET SDK...
dotnet --version || goto :failed

echo [2/3] Running logic tests...
dotnet run --project ".\tests\MorseTrainer.Tests\MorseTrainer.Tests.csproj" --configuration Release || goto :failed

echo [3/3] Building Windows application...
dotnet build ".\src\MorseTrainer\MorseTrainer.csproj" --configuration Release || goto :failed

echo.
echo All checks passed. The project is ready for GitHub.
pause
exit /b 0

:failed
echo.
echo Check failed. Copy the error text and send it for diagnosis.
pause
exit /b 1

