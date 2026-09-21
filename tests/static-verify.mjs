import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const requiredFiles = [
  'src/MorseTrainer/MorseTrainer.csproj',
  'src/MorseTrainer/App.xaml',
  'src/MorseTrainer/App.xaml.cs',
  'src/MorseTrainer/MainWindow.xaml',
  'src/MorseTrainer/MainWindow.xaml.cs',
  'src/MorseTrainer/SymbolSelectionWindow.xaml',
  'src/MorseTrainer/SymbolSelectionWindow.xaml.cs',
  'src/MorseTrainer/Domain/LearningCatalog.cs',
  'src/MorseTrainer/Services/ThemeService.cs',
  'src/MorseTrainer/Services/SpeechService.cs',
  'src/MorseTrainer/Services/VoicePackService.cs',
  'src/MorseTrainer/Services/ProfileService.cs',
  'src/MorseTrainer/Models/TrainingProfile.cs',
  'src/MorseTrainer/App.xaml.cs',
  'src/MorseTrainer/Assets/MorseTrainer.ico',
  'installer/MorseTrainer.iss',
  'scripts/build.ps1',
  '.github/workflows/build.yml',
  '.github/workflows/release.yml',
  'START-MORSE-TRAINER.cmd',
  'CHECK-BEFORE-GITHUB.cmd',
  'BUILD-EXE.cmd',
  'README.md',
  'LICENSE',
  'src/MorseTrainer.Core/MorseTrainer.Core.csproj',
  'src/MorseTrainer.Mobile/MorseTrainer.Mobile.csproj',
  'src/MorseTrainer.Mobile/MauiProgram.cs',
  'src/MorseTrainer.Mobile/Pages/TrainingPage.xaml',
  'src/MorseTrainer.Mobile/Pages/TrainingPage.xaml.cs',
  'src/MorseTrainer.Mobile/Pages/LearningPage.xaml',
  'src/MorseTrainer.Mobile/Pages/LearningPage.xaml.cs',
  'src/MorseTrainer.Mobile/Pages/SettingsPage.xaml',
  'src/MorseTrainer.Mobile/Pages/SettingsPage.xaml.cs',
  '.github/workflows/mobile.yml'
];

for (const relativePath of requiredFiles) {
  assert(existsSync(resolve(root, relativePath)), `Missing required file: ${relativePath}`);
}

const alphabetSource = read('src/MorseTrainer/Domain/MorseAlphabet.cs');
const mapNames = ['Russian', 'Latin', 'Digits', 'Punctuation'];
const expectedCounts = { Russian: 33, Latin: 26, Digits: 10 };

for (let index = 0; index < mapNames.length; index += 1) {
  const name = mapNames[index];
  const nextName = mapNames[index + 1];
  const start = alphabetSource.indexOf(`> ${name} =`);
  const end = nextName ? alphabetSource.indexOf(`> ${nextName} =`) : alphabetSource.indexOf('public static bool TryGetCode');
  assert(start >= 0 && end > start, `Cannot locate ${name} Morse map.`);
  const section = alphabetSource.slice(start, end);
  const entries = [...section.matchAll(/\['([^']+)'\]\s*=\s*"([.-]+)"/g)];
  if (expectedCounts[name]) {
    assert(entries.length === expectedCounts[name], `${name} map has ${entries.length} entries instead of ${expectedCounts[name]}.`);
  } else {
    assert(entries.length >= 10, `${name} map must contain at least 10 entries.`);
  }
  for (const [, symbol, code] of entries) {
    assert(/^[.-]+$/.test(code), `Invalid Morse code for ${symbol}: ${code}`);
  }
}

const mainWindowVerification = verifyXamlCodeBehind(
  'src/MorseTrainer/MainWindow.xaml',
  'src/MorseTrainer/MainWindow.xaml.cs'
);
verifyXamlCodeBehind(
  'src/MorseTrainer/SymbolSelectionWindow.xaml',
  'src/MorseTrainer/SymbolSelectionWindow.xaml.cs'
);
for (const page of ['TrainingPage', 'LearningPage', 'SettingsPage']) {
  verifyXamlCodeBehind(
    `src/MorseTrainer.Mobile/Pages/${page}.xaml`,
    `src/MorseTrainer.Mobile/Pages/${page}.xaml.cs`
  );
}

for (const relativePath of [
  'src/MorseTrainer/Domain/MorseAlphabet.cs',
  'src/MorseTrainer/Domain/TrainingGenerator.cs',
  'src/MorseTrainer/Domain/TrainingEvaluator.cs',
  'src/MorseTrainer/Domain/LearningCatalog.cs',
  'src/MorseTrainer/Services/MorseAudioService.cs',
  'src/MorseTrainer/Services/ThemeService.cs',
  'src/MorseTrainer/Services/SpeechService.cs',
  'src/MorseTrainer/Services/SettingsService.cs',
  'src/MorseTrainer/Services/ProfileService.cs',
  'src/MorseTrainer/Services/VoicePackService.cs',
  'src/MorseTrainer/MainWindow.xaml.cs',
  'tests/MorseTrainer.Tests/Program.cs',
  'src/MorseTrainer.Mobile/MauiProgram.cs',
  'src/MorseTrainer.Mobile/Pages/TrainingPage.xaml.cs',
  'src/MorseTrainer.Mobile/Pages/LearningPage.xaml.cs',
  'src/MorseTrainer.Mobile/Pages/SettingsPage.xaml.cs'
]) {
  assertBalancedBraces(read(relativePath), relativePath);
}

const buildWorkflow = read('.github/workflows/build.yml');
assert(buildWorkflow.includes('scripts\\build.ps1'), 'Build workflow does not call the build script.');
assert(buildWorkflow.includes('MorseTrainer-Setup-x64.exe'), 'Build workflow does not publish the installer.');

const mobileProject = read('src/MorseTrainer.Mobile/MorseTrainer.Mobile.csproj');
assert(mobileProject.includes('<TargetFrameworks>net10.0-android;net10.0-ios</TargetFrameworks>'),
  'Mobile project must target net10.0-android and net10.0-ios.');
assert(mobileProject.includes('Include="Microsoft.Maui.Controls"'),
  'Mobile project must reference Microsoft.Maui.Controls explicitly (required since .NET 9).');

const mobileWorkflow = read('.github/workflows/mobile.yml');
assert(!mobileWorkflow.includes('net8.0'), 'Mobile workflow still references unsupported net8.0 mobile targets.');
assert(mobileWorkflow.includes('-p:MobileTargetFrameworks=net10.0-android'), 'Android job must restrict the build to net10.0-android via MobileTargetFrameworks.');
assert(mobileWorkflow.includes('-p:MobileTargetFrameworks=net10.0-ios'), 'iOS job must restrict the build to net10.0-ios via MobileTargetFrameworks.');
assert(!mobileWorkflow.includes('-p:TargetFrameworks='), 'Do not pass TargetFrameworks globally: it breaks restore of MorseTrainer.Core.');
assert(mobileProject.includes("'$(MobileTargetFrameworks)' != ''"), 'Mobile project must honour MobileTargetFrameworks.');
assert(mobileWorkflow.includes('iossimulator-arm64'), 'iOS job must build for the ARM64 simulator.');

const releaseWorkflow = read('.github/workflows/release.yml');
assert(releaseWorkflow.includes('gh release create'), 'Release workflow does not create a GitHub release.');
assert(releaseWorkflow.includes('MorseTrainer-Android.apk'), 'Release workflow does not attach the Android APK.');
for (const [name, workflow] of [['mobile', mobileWorkflow], ['release', releaseWorkflow]]) {
  assert(workflow.includes('ANDROID_KEYSTORE_BASE64') && workflow.includes('AndroidSigningKeyAlias=morsetrainer'),
    `The ${name} workflow must sign the APK with the release keystore from secrets.`);
}

const exeBuilder = read('BUILD-EXE.cmd');
assert(exeBuilder.includes('--self-contained false'), 'Local EXE builder must use the installed .NET runtime.');
assert(exeBuilder.includes('-p:PublishSingleFile=true'), 'Local EXE builder must produce one executable.');
assert(!exeBuilder.includes('EnableCompressionInSingleFile'),
  'Framework-dependent single-file publishing must not enable bundle compression.');

const voiceDirectory = resolve(root, 'src/MorseTrainer.Mobile/Resources/Raw/voice');
const voiceFiles = readdirSync(voiceDirectory).filter((name) => /^code_[01]+\.m4a$/.test(name));
assert(voiceFiles.length === 42, `Mobile voice pack has ${voiceFiles.length} clips instead of 42.`);
for (const file of voiceFiles) {
  assert(statSync(resolve(voiceDirectory, file)).size > 1000, `Voice clip is empty: ${file}`);
}
const desktopVoiceDirectory = resolve(root, 'src/MorseTrainer/Assets/Voice');
const desktopVoiceFiles = readdirSync(desktopVoiceDirectory).filter((name) => /^code_[01]+\.wav$/.test(name));
assert(desktopVoiceFiles.length === 42, `Desktop voice pack has ${desktopVoiceFiles.length} clips instead of 42.`);
const voiceCodes = new Set(voiceFiles.map((file) => file.slice('code_'.length, -'.m4a'.length)));
const requiredVoiceCodes = [...alphabetSource.matchAll(/\['[^']+'\]\s*=\s*"([.-]+)"/g)]
  .map((match) => match[1])
  .slice(0, 33 + 26 + 10)
  .map((code) => [...code].map((symbol) => symbol === '.' ? '0' : '1').join(''));
for (const code of new Set(requiredVoiceCodes)) {
  assert(voiceCodes.has(code), `Voice pack is missing Morse code ${code}.`);
  assert(existsSync(resolve(desktopVoiceDirectory, `code_${code}.wav`)), `Desktop voice pack is missing Morse code ${code}.`);
}

console.log(`Static verification passed: ${requiredFiles.length} required files, ${mainWindowVerification.controlCount} named controls, ${mainWindowVerification.eventCount} main-window event handlers.`);

function read(relativePath) {
  return readFileSync(resolve(root, relativePath), 'utf8');
}

function verifyXamlCodeBehind(xamlPath, codeBehindPath) {
  const xaml = read(xamlPath);
  const codeBehind = read(codeBehindPath);
  const controlNames = [...xaml.matchAll(/x:Name="([^"]+)"/g)].map((match) => match[1]);
  assert(new Set(controlNames).size === controlNames.length, `${xamlPath} contains duplicate x:Name values.`);

  const eventNames = [...xaml.matchAll(/(?:Click|Clicked|Loaded|Closing|KeyDown|ValueChanged|SelectionChanged|SelectedIndexChanged|TextChanged|Toggled)="([^"]+)"/g)]
    .map((match) => match[1]);
  for (const eventName of new Set(eventNames)) {
    assert(codeBehind.includes(`${eventName}(`), `Missing code-behind handler ${eventName} for ${xamlPath}.`);
  }

  return { controlCount: controlNames.length, eventCount: new Set(eventNames).size };
}

function assertBalancedBraces(source, relativePath) {
  const withoutStringsAndComments = source
    .replace(/\/\/.*$/gm, '')
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/@?"(?:""|\\.|[^"\\])*"/g, '""')
    .replace(/'(?:\\.|[^'\\])'/g, "''");
  let balance = 0;
  for (const character of withoutStringsAndComments) {
    if (character === '{') balance += 1;
    if (character === '}') balance -= 1;
    assert(balance >= 0, `Unexpected closing brace in ${relativePath}.`);
  }
  assert(balance === 0, `Unbalanced braces in ${relativePath}.`);
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
