param(
    [string]$Version = "2.3.0",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $repoRoot "src\MorseTrainer\MorseTrainer.csproj"
$tests = Join-Path $repoRoot "tests\MorseTrainer.Tests\MorseTrainer.Tests.csproj"
$dist = Join-Path $repoRoot "dist"
$portable = Join-Path $dist "portable"

if (Test-Path $dist) {
    Remove-Item -Path $dist -Recurse -Force
}

New-Item -ItemType Directory -Path $portable -Force | Out-Null

# PowerShell не считает ненулевой код выхода native-команды ошибкой: проверяем LASTEXITCODE сами
function Assert-LastExitCode([string]$step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$step failed with exit code $LASTEXITCODE"
    }
}

Write-Host "Running tests..."
dotnet run --project $tests --configuration Release
Assert-LastExitCode "Tests"

Write-Host "Publishing self-contained Windows application..."
dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $portable `
    -p:Version=$Version `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false
Assert-LastExitCode "Publish"
if (-not (Test-Path (Join-Path $portable "MorseTrainer.exe"))) {
    throw "Publish did not produce MorseTrainer.exe"
}

Copy-Item (Join-Path $repoRoot "README.md") $portable
Copy-Item (Join-Path $repoRoot "LICENSE") $portable

$portableArchive = Join-Path $dist "MorseTrainer-Windows-x64.zip"
Compress-Archive -Path (Join-Path $portable "*") -DestinationPath $portableArchive -CompressionLevel Optimal

if (-not $SkipInstaller) {
    $isccCandidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    )
    $iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) {
        throw "Inno Setup 6 was not found. Install it or run with -SkipInstaller."
    }

    Write-Host "Building installer..."
    & $iscc "/DAppVersion=$Version" (Join-Path $repoRoot "installer\MorseTrainer.iss")
    Assert-LastExitCode "Inno Setup"
}

Write-Host "Build completed: $dist"
