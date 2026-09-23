param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Repository = "nevadimka1415/morse",
    [string]$OutDir = "dist/winget"
)

# Манифесты winget (схема 1.9.0) для релиза: SHA-256 считается по установщику из GitHub Releases.
# Первая публикация: workflow «Publish to winget» или вручную wingetcreate submit <папка> (см. docs/WINGET.md).
$ErrorActionPreference = "Stop"
$Version = $Version.TrimStart("v")
$id = "Nevadimka1415.MorseTrainer"
$url = "https://github.com/$Repository/releases/download/v$Version/MorseTrainer-Setup-x64.exe"
$target = Join-Path $OutDir "manifests/n/Nevadimka1415/MorseTrainer/$Version"
New-Item -ItemType Directory -Path $target -Force | Out-Null

$installer = Join-Path ([IO.Path]::GetTempPath()) "MorseTrainer-Setup-x64-$Version.exe"
Invoke-WebRequest -Uri $url -OutFile $installer -UseBasicParsing
$sha = (Get-FileHash $installer -Algorithm SHA256).Hash.ToUpperInvariant()
if ((Get-Item $installer).Length -lt 10MB) { throw "Установщик подозрительно маленький: $((Get-Item $installer).Length) байт" }

$utf8 = New-Object System.Text.UTF8Encoding $false
function Write-Manifest([string]$name, [string]$text) {
    [IO.File]::WriteAllText((Join-Path $target $name), $text.Replace("`r`n", "`n"), $utf8)
}

Write-Manifest "$id.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.9.0.schema.json
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.9.0
"@

Write-Manifest "$id.installer.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.9.0.schema.json
PackageIdentifier: $id
PackageVersion: $Version
InstallerType: inno
Scope: user
InstallModes:
- interactive
- silent
- silentWithProgress
UpgradeBehavior: install
ProductCode: '{F5BA0D4D-BED2-4C8D-8963-42CE7B70C3AD}_is1'
ReleaseDate: $(Get-Date -Format yyyy-MM-dd)
Installers:
- Architecture: x64
  InstallerUrl: $url
  InstallerSha256: $sha
ManifestType: installer
ManifestVersion: 1.9.0
"@

Write-Manifest "$id.locale.en-US.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.9.0.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: nevadimka1415
PublisherUrl: https://github.com/nevadimka1415
PublisherSupportUrl: https://github.com/$Repository/issues
PackageName: Morse Trainer
PackageUrl: https://github.com/$Repository
License: MIT
LicenseUrl: https://github.com/$Repository/blob/main/LICENSE
ShortDescription: Offline Morse code trainer with Koch method, exams, keyer practice and progress history.
Description: Morse Trainer is an offline Morse code (CW) trainer for Russian and Latin alphabets, digits and punctuation. It generates random groups, words, callsigns and Q-codes, supports the Koch and Farnsworth methods, exams with a timer, on-air noise, a straight key practice with timing analysis, a course from zero to 60 characters per minute and a progress history.
Moniker: morse-trainer
Tags:
- morse
- morse-code
- cw
- ham-radio
- education
- training
ReleaseNotesUrl: https://github.com/$Repository/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: 1.9.0
"@

Write-Manifest "$id.locale.ru-RU.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.locale.1.9.0.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: ru-RU
Publisher: nevadimka1415
PackageName: Morse Trainer
License: MIT
ShortDescription: Автономный тренажёр азбуки Морзе: метод Коха, экзамен, передача ключом и история прогресса.
Description: Morse Trainer — тренажёр азбуки Морзе без интернета для русского и латинского алфавитов, цифр и знаков. Случайные группы, слова, позывные и Q-код, методы Коха и Фарнсворта, экзамен с таймером, помехи эфира, передача ключом с разбором ритма, курс с нуля до 60 знаков в минуту и история тренировок.
ManifestType: locale
ManifestVersion: 1.9.0
"@

Write-Host "Манифесты winget для $Version в $target (SHA256 $sha)"
