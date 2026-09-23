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
  'src/MorseTrainer/Domain/VoiceClipCatalog.cs',
  'src/MorseTrainer/Domain/TrainingStatistics.cs',
  'src/MorseTrainer/Domain/KochMethod.cs',
  'src/MorseTrainer/Domain/TrainingPresets.cs',
  'src/MorseTrainer/Domain/ProfileTransfer.cs',
  'src/MorseTrainer/Domain/KeyerDecoder.cs',
  'src/MorseTrainer/Domain/WordLists.cs',
  'src/MorseTrainer/Domain/ExamSession.cs',
  'src/MorseTrainer/Domain/SpeedLadder.cs',
  'src/MorseTrainer/Domain/KeyerAnalysis.cs',
  'src/MorseTrainer/Domain/ReminderSchedule.cs',
  'src/MorseTrainer.Mobile/Services/IReminderService.cs',
  'src/MorseTrainer.Mobile/Services/ReminderTexts.cs',
  'src/MorseTrainer.Mobile/Platforms/Android/ReminderService.cs',
  'src/MorseTrainer.Mobile/Platforms/iOS/ReminderService.cs',
  'src/MorseTrainer.Mobile/Pages/KeyerPage.xaml',
  'src/MorseTrainer.Mobile/Pages/KeyerPage.xaml.cs',
  'src/MorseTrainer.Mobile/Services/TabletLayout.cs',
  'src/MorseTrainer/Localization/Texts.cs',
  'src/MorseTrainer/Localization/LocExtension.cs',
  'src/MorseTrainer.Mobile/Localization/LocExtension.cs',
  'src/MorseTrainer/Models/TrainingRecord.cs',
  'src/MorseTrainer/Services/TrainingHistoryStore.cs',
  'src/MorseTrainer.Mobile/Pages/ProgressPage.xaml',
  'src/MorseTrainer.Mobile/Pages/ProgressPage.xaml.cs',
  'src/MorseTrainer/Services/ThemeService.cs',
  'src/MorseTrainer/Services/SpeechService.cs',
  'src/MorseTrainer/Services/VoicePackService.cs',
  'src/MorseTrainer/Services/ProfileService.cs',
  'src/MorseTrainer/Services/UpdateService.cs',
  'src/MorseTrainer/Services/AppPaths.cs',
  'src/MorseTrainer/Services/CrashReport.cs',
  'src/MorseTrainer.Mobile/Services/MobilePaths.cs',
  'src/MorseTrainer/Models/TrainingProfile.cs',
  'tests/MorseTrainer.UiSmoke/MorseTrainer.UiSmoke.csproj',
  'tests/MorseTrainer.UiSmoke/Program.cs',
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
  '.github/workflows/mobile.yml',
  '.github/dependabot.yml',
  'docs/RUSTORE.md',
  'tests/android-smoke.py'
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
for (const page of ['TrainingPage', 'LearningPage', 'SettingsPage', 'ProgressPage', 'KeyerPage']) {
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
  'src/MorseTrainer/Domain/VoiceClipCatalog.cs',
  'src/MorseTrainer/Domain/TrainingStatistics.cs',
  'src/MorseTrainer/Domain/KochMethod.cs',
  'src/MorseTrainer/Domain/TrainingPresets.cs',
  'src/MorseTrainer/Domain/ProfileTransfer.cs',
  'src/MorseTrainer/Domain/KeyerDecoder.cs',
  'src/MorseTrainer/Domain/WordLists.cs',
  'src/MorseTrainer/Domain/ExamSession.cs',
  'src/MorseTrainer/Domain/SpeedLadder.cs',
  'src/MorseTrainer/Domain/KeyerAnalysis.cs',
  'src/MorseTrainer/Domain/ReminderSchedule.cs',
  'src/MorseTrainer.Mobile/Platforms/Android/ReminderService.cs',
  'src/MorseTrainer.Mobile/Platforms/iOS/ReminderService.cs',
  'src/MorseTrainer.Mobile/Pages/KeyerPage.xaml.cs',
  'src/MorseTrainer.Mobile/Services/TabletLayout.cs',
  'src/MorseTrainer/Localization/Texts.cs',
  'src/MorseTrainer/Services/TrainingHistoryStore.cs',
  'src/MorseTrainer.Mobile/Pages/ProgressPage.xaml.cs',
  'src/MorseTrainer/Services/MorseAudioService.cs',
  'src/MorseTrainer/Services/ThemeService.cs',
  'src/MorseTrainer/Services/SpeechService.cs',
  'src/MorseTrainer/Services/SettingsService.cs',
  'src/MorseTrainer/Services/ProfileService.cs',
  'src/MorseTrainer/Services/VoicePackService.cs',
  'src/MorseTrainer/Services/UpdateService.cs',
  'src/MorseTrainer/MainWindow.xaml.cs',
  'tests/MorseTrainer.Tests/Program.cs',
  'tests/MorseTrainer.UiSmoke/Program.cs',
  'src/MorseTrainer.Mobile/MauiProgram.cs',
  'src/MorseTrainer.Mobile/Pages/TrainingPage.xaml.cs',
  'src/MorseTrainer.Mobile/Pages/LearningPage.xaml.cs',
  'src/MorseTrainer.Mobile/Pages/SettingsPage.xaml.cs'
]) {
  assertBalancedBraces(read(relativePath), relativePath);
}

const contentModes = (read('src/MorseTrainer/MainWindow.xaml').match(/<ComboBoxItem Content="[^"]*" \/>/g) || []).length;
assert(read('src/MorseTrainer/MainWindow.xaml').includes('Метод Коха: по уровням') && read('src/MorseTrainer.Mobile/Pages/SettingsPage.xaml.cs').includes('Texts.T("Метод Коха")'),
  'Both apps must offer the Koch content mode.');
assert(read('src/MorseTrainer/MainWindow.xaml').includes("{loc:Loc 'Позывные'}") && read('src/MorseTrainer.Mobile/Pages/SettingsPage.xaml.cs').includes('Texts.T("Позывные")'),
  'Both apps must offer the words, callsigns and Q-code content modes.');
assert(read('src/MorseTrainer/MainWindow.xaml').includes('x:Name="ExamButton"') && read('src/MorseTrainer.Mobile/Pages/TrainingPage.xaml').includes('x:Name="ExamButton"'),
  'Both apps must offer the exam mode.');
assert(read('src/MorseTrainer/MainWindow.xaml').includes('x:Name="NoiseSlider"') && read('src/MorseTrainer.Mobile/Pages/SettingsPage.xaml').includes('x:Name="NoiseSlider"'),
  'Both apps must offer the interference sliders (noise, QSB, drift).');
assert(read('src/MorseTrainer/MainWindow.xaml').includes('x:Name="AutoSpeedCheckBox"') && read('src/MorseTrainer.Mobile/Pages/SettingsPage.xaml').includes('x:Name="AutoSpeedSwitch"'),
  'Both apps must offer the auto speed switch.');
assert(read('src/MorseTrainer/MainWindow.xaml').includes('x:Name="KeyerAnalyzeButton"') && read('src/MorseTrainer.Mobile/Pages/KeyerPage.xaml').includes('x:Name="AnalyzeButton"'),
  'Both apps must offer the keyer quality analysis.');
assert(read('src/MorseTrainer/MainWindow.xaml').includes('x:Name="ProgressProfileCombo"') && read('src/MorseTrainer.Mobile/Pages/ProgressPage.xaml').includes('x:Name="ProfilePicker"'),
  'Both apps must filter progress by profile.');
assert(read('src/MorseTrainer/MainWindow.xaml').includes('x:Name="ExportCsvButton"') && read('src/MorseTrainer.Mobile/Pages/ProgressPage.xaml').includes('ShareCsvButton_OnClicked'),
  'Both apps must export the history as CSV.');
const androidManifest = read('src/MorseTrainer.Mobile/Platforms/Android/AndroidManifest.xml');
assert(androidManifest.includes('android.permission.POST_NOTIFICATIONS') && androidManifest.includes('android.permission.RECEIVE_BOOT_COMPLETED'),
  'Android manifest must allow reminder notifications and rescheduling after reboot.');
assert(read('src/MorseTrainer.Mobile/Pages/SettingsPage.xaml').includes('x:Name="ReminderSwitch"') && read('src/MorseTrainer.Mobile/MauiProgram.cs').includes('IReminderService'),
  'Settings page must offer the practice reminder and MauiProgram must register the platform service.');
assert(read('src/MorseTrainer/App.xaml').includes('TargetType="GridViewColumnHeader"'), 'History table header must follow the theme.');
assert(read('src/MorseTrainer/MainWindow.xaml.cs').includes('DownloadAndRunInstallerAsync'), 'Windows update must download and run the installer.');
assert(read('src/MorseTrainer.Mobile/MorseTrainer.Mobile.csproj').includes('<AndroidLinkTool>r8</AndroidLinkTool>'), 'Android release build must use R8.');
assert(read('README.md').includes('actions/workflows/build.yml/badge.svg'), 'README must show the build status badges.');
for (const shot of ['training', 'learning', 'keyer', 'progress', 'training-en']) {
  assert(existsSync(resolve(root, `docs/screenshots/${shot}.png`)) && statSync(resolve(root, `docs/screenshots/${shot}.png`)).size > 10000, `Screenshot is missing: ${shot}.png`);
}
assert(read('README.md').includes('docs/screenshots/training.png'), 'README must show the screenshots.');

for (const file of ['src/MorseTrainer/MainWindow.xaml', 'src/MorseTrainer/SymbolSelectionWindow.xaml', ...['TrainingPage', 'LearningPage', 'SettingsPage', 'ProgressPage', 'KeyerPage'].map((page) => `src/MorseTrainer.Mobile/Pages/${page}.xaml`)]) {
  const xaml = read(file);
  assert(!/\b(Text|Content|Header|Title|ToolTip|Placeholder)="[^"{]*[А-Яа-яЁё]/.test(xaml), `${file} has untranslated Russian text; use {loc:Loc '…'}.`);
  assert(!/<x:String>[^<]*[А-Яа-яЁё]/.test(xaml), `${file} has Russian picker items in XAML; set ItemsSource in code with Texts.T.`);
}
assert(read('src/MorseTrainer.Core/MorseTrainer.Core.csproj').includes('Texts.cs'), 'Core project must compile Localization/Texts.cs.');

const buildWorkflow = read('.github/workflows/build.yml');
assert(buildWorkflow.includes('scripts\\build.ps1'), 'Build workflow does not call the build script.');
const buildScript = read('scripts/build.ps1');
assert((buildScript.match(/Assert-LastExitCode/g) || []).length >= 4, 'build.ps1 must fail fast after dotnet run, dotnet publish and ISCC.');
assert(buildScript.includes('MorseTrainer.exe'), 'build.ps1 must verify that publish produced MorseTrainer.exe.');
assert(buildScript.includes('MorseTrainer.UiSmoke'), 'build.ps1 must run the WPF UI smoke test before publish.');
assert(read('src/MorseTrainer/MorseTrainer.csproj').includes('InternalsVisibleTo Include="MorseTrainer.UiSmoke"'), 'WPF project must expose internals to the UI smoke test.');
assert(read('tests/MorseTrainer.UiSmoke/MorseTrainer.UiSmoke.csproj').includes('net10.0-windows'), 'UI smoke project must target net10.0-windows.');
assert(buildWorkflow.includes('MorseTrainer-Screenshots'), 'Build workflow must upload the UI smoke screenshots.');
assert(buildWorkflow.includes('MorseTrainer-Setup-x64.exe'), 'Build workflow does not publish the installer.');

assert(read('src/MorseTrainer/MorseTrainer.csproj').includes('<TargetFramework>net10.0-windows</TargetFramework>'), 'Windows project must target net10.0-windows.');
assert(read('src/MorseTrainer.Core/MorseTrainer.Core.csproj').includes('<TargetFramework>net10.0</TargetFramework>'), 'Core project must target net10.0.');
const testsProject = read('tests/MorseTrainer.Tests/MorseTrainer.Tests.csproj');
assert(testsProject.includes('<TargetFramework>net10.0</TargetFramework>'), 'Tests project must target cross-platform net10.0.');
assert(testsProject.includes('MorseTrainer.Core.csproj'), 'Tests must reference MorseTrainer.Core, not the WPF app.');
assert(!buildWorkflow.includes('8.0.x'), 'Build workflow must use .NET SDK 10.');
assert(existsSync(resolve(root, 'global.json')), 'global.json must pin the .NET SDK major version.');

const coreProject = read('src/MorseTrainer.Core/MorseTrainer.Core.csproj');
for (const file of ['UpdateService.cs', 'TrainingHistoryStore.cs', 'TrainingRecord.cs', 'CrashReport.cs']) {
  assert(coreProject.includes(file), `Core project must compile ${file}.`);
}
assert(read('src/MorseTrainer.Mobile/Platforms/Android/AndroidManifest.xml').includes('android.permission.INTERNET'), 'Android manifest must allow the update check to reach GitHub.');
const mauiProgram = read('src/MorseTrainer.Mobile/MauiProgram.cs');
assert(mauiProgram.includes('UnhandledException') && mauiProgram.includes('UnobservedTaskException') && mauiProgram.includes('CrashReport.Write'),
  'MauiProgram must write crash.log from unhandled exceptions.');
assert(read('src/MorseTrainer.Mobile/Pages/SettingsPage.xaml').includes('CrashReportLayout'), 'Settings page must offer the crash report actions.');

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
assert(mobileWorkflow.includes('android-emulator-runner') && mobileWorkflow.includes('tests/android-smoke.py') && mobileWorkflow.includes('MorseTrainer-Screenshots-Android'),
  'Mobile workflow must run the Android emulator smoke test and upload its screenshots.');
assert(/catch \(Exception exception\)\s*\{\s*\/\/ Сбой звука/.test(read('src/MorseTrainer.Mobile/Pages/TrainingPage.xaml.cs')),
  'Phone playback errors must not crash the app (async void handler).');

const releaseWorkflow = read('.github/workflows/release.yml');
assert(releaseWorkflow.includes('gh release create'), 'Release workflow does not create a GitHub release.');
assert(releaseWorkflow.includes('--notes-file'), 'Release workflow must take release notes from CHANGELOG.md.');
assert(!releaseWorkflow.includes('8.0.x'), 'Release workflow must use .NET SDK 10.');
for (const [name, workflow] of [['build', buildWorkflow], ['mobile', mobileWorkflow], ['release', releaseWorkflow]]) {
  assert(!/uses: actions\/[a-z-]+@v[1-4]\b/.test(workflow), `The ${name} workflow uses an outdated action version (Node 20).`);
}
assert(releaseWorkflow.includes('MorseTrainer-Android.apk'), 'Release workflow does not attach the Android APK.');
assert(releaseWorkflow.includes('SHA256SUMS.txt') && releaseWorkflow.includes('sha256sum'), 'Release workflow must attach SHA256SUMS.txt with checksums of all assets.');
assert(releaseWorkflow.includes('MorseTrainer-iOS-unsigned.ipa') && mobileWorkflow.includes('MorseTrainer-iOS-unsigned.ipa'), 'Workflows must build the unsigned iPhone IPA.');
assert(mobileWorkflow.includes('EnableCodeSigning=false') && mobileWorkflow.includes('RuntimeIdentifier=ios-arm64'), 'iPhone device build must be unsigned and target ios-arm64.');
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

  const eventNames = [...xaml.matchAll(/(?:Click|Clicked|Loaded|Closing|KeyDown|KeyUp|ValueChanged|SelectionChanged|SelectedIndexChanged|TextChanged|Toggled|Pressed|Released|PreviewMouseLeftButtonDown|PreviewMouseLeftButtonUp|MouseLeave|LostMouseCapture)="([^"]+)"/g)]
    .map((match) => match[1]);
  for (const eventName of new Set(eventNames)) {
    assert(codeBehind.includes(`${eventName}(`), `Missing code-behind handler ${eventName} for ${xamlPath}.`);
  }

  return { controlCount: controlNames.length, eventCount: new Set(eventNames).size };
}

function assertBalancedBraces(source, relativePath) {
  let balance = 0;
  for (const character of stripStringsAndComments(source)) {
    if (character === '{') balance += 1;
    if (character === '}') balance -= 1;
    assert(balance >= 0, `Unexpected closing brace in ${relativePath}.`);
  }
  assert(balance === 0, `Unbalanced braces in ${relativePath}.`);
}

// Убирает строки, символьные литералы и комментарии C#, чтобы скобки внутри них не считались.
// Понимает обычные строки с \-экранированием, verbatim @"..." с "", сырые """...""" и $-интерполяцию
// (фигурные скобки внутри интерполяции тоже не считаются: их баланс проверяет компилятор).
function stripStringsAndComments(source) {
  let out = '';
  let i = 0;
  while (i < source.length) {
    const c = source[i];
    const next = source[i + 1];
    if (c === '/' && next === '/') {
      while (i < source.length && source[i] !== '\n') i += 1;
      continue;
    }
    if (c === '/' && next === '*') {
      const end = source.indexOf('*/', i + 2);
      i = end < 0 ? source.length : end + 2;
      continue;
    }
    if (c === '\'' ) {
      let j = i + 1;
      if (source[j] === '\\') j += 2; else j += 1;
      if (source[j] === '\'') { i = j + 1; continue; }
      out += c; i += 1; continue;
    }
    let k = i;
    while (source[k] === '$' || source[k] === '@') k += 1;
    if (source[k] === '"') {
      const prefix = source.slice(i, k);
      const verbatim = prefix.includes('@');
      if (!verbatim && source.startsWith('"""', k)) {
        let quotes = 0;
        while (source[k + quotes] === '"') quotes += 1;
        const closing = '"'.repeat(quotes);
        const end = source.indexOf(closing, k + quotes);
        i = end < 0 ? source.length : end + quotes;
        out += '""';
        continue;
      }
      let j = k + 1;
      while (j < source.length) {
        if (verbatim) {
          if (source[j] === '"' && source[j + 1] === '"') { j += 2; continue; }
          if (source[j] === '"') break;
          j += 1;
        } else {
          if (source[j] === '\\') { j += 2; continue; }
          if (source[j] === '"' || source[j] === '\n') break;
          j += 1;
        }
      }
      i = j + 1;
      out += '""';
      continue;
    }
    out += c;
    i += 1;
  }
  return out;
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
