using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MorseTrainer.Domain;
using MorseTrainer.Localization;
using MorseTrainer.Models;
using MorseTrainer.Services;

namespace MorseTrainer;

/// <summary>Главное окно: Параметры задания, «Что тренировать», дополнительные настройки и профили.</summary>
public partial class MainWindow
{
    private void AlphabetCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateKochSummary();
        UpdateModeButtons();
        if (_windowLoaded)
        {
            RefreshCourse();
        }
    }

    private void KochLevelSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateKochSummary();
        if (_windowLoaded)
        {
            _settingsService.Save(ReadSettings());
        }
    }

    private void UpdateKochSummary()
    {
        if (KochSummaryText is null || KochLevelSlider is null || AlphabetCombo is null)
        {
            return;
        }

        var alphabet = (AlphabetMode)Math.Clamp(AlphabetCombo.SelectedIndex, 0, 2);
        KochLevelSlider.Maximum = KochMethod.MaxLevel(alphabet);
        KochSummaryText.Text = KochMethod.Describe(alphabet, (int)KochLevelSlider.Value);
    }

    private void FarnsworthButton_OnClick(object sender, RoutedEventArgs e)
    {
        var settings = ReadSettings();
        TrainingPresets.ApplyFarnsworth(settings);
        SpeedSlider.Value = settings.CharactersPerMinute;
        CharacterGapSlider.Value = settings.CharacterGapUnits;
        GroupGapSlider.Value = settings.GroupGapUnits;
        UpdateSettingLabels();
        ResultDetailsText.Text = TrainingPresets.FarnsworthDescription;
        ResultDetailsText.Foreground = (Brush)FindResource("MutedTextBrush");
        if (_windowLoaded)
        {
            _settingsService.Save(ReadSettings());
        }
    }

    private void TogglePanelButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetTrainingPanelCollapsed(TrainingPanel.Visibility == Visibility.Visible);
        if (_windowLoaded)
        {
            _settingsService.Save(ReadSettings());
        }
    }

    /// <summary>Свёрнутая панель параметров отдаёт ширину заданию; «Новое задание» переезжает в шапку.</summary>
    private void SetTrainingPanelCollapsed(bool collapsed)
    {
        TrainingPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        TrainingPanelColumn.Width = new GridLength(collapsed ? 0 : 380);
        TrainingPanelSpacer.Width = new GridLength(collapsed ? 0 : 18);
        QuickGenerateButton.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        TogglePanelButton.Content = collapsed ? Texts.T("Параметры ▶") : Texts.T("◀ Параметры");
    }

    private void SelectSymbolsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SymbolSelectionWindow(_selectedSymbols) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _selectedSymbols = dialog.SelectedSymbols;
            UpdateSelectedSymbolsSummary();
            _settingsService.Save(ReadSettings());
        }
    }

    private void UpdateSelectedSymbolsSummary()
    {
        UpdateModeButtons();
        SelectedSymbolsSummaryText.Text = string.IsNullOrEmpty(_selectedSymbols)
            ? Texts.T("Ничего не выбрано")
            : Texts.F("{0} символов: {1}", _selectedSymbols.Length, Truncate(_selectedSymbols, 45));
    }

    private void ContentModeCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCustomSymbolsVisibility();

    // ---------- Что тренировать: четыре основных режима сверху, остальное — в дополнительных ----------

    private static readonly ContentMode[] MainModes = { ContentMode.Letters, ContentMode.Digits, ContentMode.LettersAndDigits, ContentMode.Custom };

    private void ModeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out var mode))
        {
            return;
        }

        // Полный список режимов в дополнительных настройках — единственный источник режима
        ContentModeCombo.SelectedIndex = mode;
        if (_windowLoaded)
        {
            _settingsService.Save(ReadSettings());
        }
    }

    private void UpdateModeButtons()
    {
        // Во время InitializeComponent часть элементов ещё не создана
        if (ModeLettersButton is null || ModeHintText is null || ContentModeCombo is null || AlphabetCombo is null || KochLevelSlider is null)
        {
            return;
        }

        var mode = ContentModes.Clamp(ContentModeCombo.SelectedIndex);
        var buttons = new[] { ModeLettersButton, ModeDigitsButton, ModeBothButton, ModeCustomButton };
        for (var index = 0; index < buttons.Length; index++)
        {
            buttons[index].Style = (Style)FindResource(MainModes[index] == mode ? "PrimaryButton" : typeof(Button));
        }

        var alphabet = (AlphabetMode)Math.Clamp(AlphabetCombo.SelectedIndex, 0, 2);
        ModeHintText.Text = Array.IndexOf(MainModes, mode) < 0
            ? Texts.F("Сейчас особый режим «{0}» — он выбран в дополнительных настройках.", (ContentModeCombo.SelectedItem as ComboBoxItem)?.Content as string ?? string.Empty)
            : Texts.F("Символов в задании: {0} · алфавит: {1} (меняется в дополнительных настройках)",
                MorseAlphabet.BuildPool(alphabet, mode, _selectedSymbols, (int)KochLevelSlider.Value).Count,
                (AlphabetCombo.SelectedItem as ComboBoxItem)?.Content as string ?? string.Empty);
    }

    private void AdvancedButton_OnClick(object sender, RoutedEventArgs e)
    {
        var show = AdvancedPanel.Visibility != Visibility.Visible;
        AdvancedPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        AdvancedButton.Content = show ? Texts.T("Дополнительные настройки ▴") : Texts.T("Дополнительные настройки ▾");
    }

    private void UpdateCustomSymbolsVisibility()
    {
        UpdateModeButtons();
        if (CustomSymbolsPanel is not null && ContentModeCombo is not null)
        {
            CustomSymbolsPanel.Visibility = ContentModeCombo.SelectedIndex == (int)ContentMode.Custom
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (KochPanel is not null && ContentModeCombo is not null)
        {
            KochPanel.Visibility = ContentModeCombo.SelectedIndex == (int)ContentMode.Koch
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (GroupHintText is not null && ContentModeCombo is not null)
        {
            GroupHintText.Text = ContentModes.IsWordMode(ContentModes.Clamp(ContentModeCombo.SelectedIndex))
                ? Texts.T("Каждая группа — одно слово, позывной или код")
                : Texts.T("В каждой группе 5 символов");
        }
    }

    private void SettingSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_windowLoaded)
        {
            UpdateSettingLabels();
        }
    }

    private void UpdateSettingLabels()
    {
        if (SpeedValueText is null
            || FrequencyValueText is null
            || VolumeValueText is null
            || CharacterGapValueText is null
            || GroupGapValueText is null
            || StartPauseValueText is null
            || SpeedSlider is null
            || FrequencySlider is null
            || VolumeSlider is null
            || CharacterGapSlider is null
            || GroupGapSlider is null
            || StartPauseSlider is null
            || NoiseSlider is null
            || QsbSlider is null
            || DriftSlider is null
            || NoiseValueText is null
            || QsbValueText is null
            || DriftValueText is null)
        {
            return;
        }

        NoiseValueText.Text = $"{(int)NoiseSlider.Value}%";
        QsbValueText.Text = $"{(int)QsbSlider.Value}%";
        DriftValueText.Text = Texts.F("±{0} Гц", (int)DriftSlider.Value);
        SpeedValueText.Text = Texts.F("{0} знаков/мин", (int)SpeedSlider.Value);
        FrequencyValueText.Text = Texts.F("{0} Гц", (int)FrequencySlider.Value);
        VolumeValueText.Text = $"{(int)VolumeSlider.Value}%";
        CharacterGapValueText.Text = Texts.F("{0} точек", (int)CharacterGapSlider.Value);
        GroupGapValueText.Text = Texts.F("{0} точек", (int)GroupGapSlider.Value);
        StartPauseValueText.Text = Texts.F("{0} точек", (int)StartPauseSlider.Value);
    }

    private bool TryReadGroupCount(out int groupCount)
    {
        if (!int.TryParse(GroupCountText.Text, out groupCount) || groupCount is < 1 or > 100)
        {
            MessageBox.Show(Texts.T("Количество групп должно быть целым числом от 1 до 100."), Texts.T("Проверьте параметры"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            GroupCountText.Focus();
            GroupCountText.SelectAll();
            return false;
        }

        return true;
    }

    private AppSettings ReadSettings()
    {
        var groupCount = int.TryParse(GroupCountText.Text, out var parsedGroupCount) ? Math.Clamp(parsedGroupCount, 1, 100) : 10;
        return new AppSettings
        {
            AlphabetIndex = Math.Clamp(AlphabetCombo.SelectedIndex, 0, 2),
            ContentModeIndex = (int)ContentModes.Clamp(ContentModeCombo.SelectedIndex),
            KochLevel = (int)KochLevelSlider.Value,
            EmphasizeProblemSymbols = EmphasizeProblemsCheckBox.IsChecked == true,
            AutoSpeed = AutoSpeedCheckBox.IsChecked == true,
            ExamPlaybacks = ExamSession.ClampPlaybacks(ExamPlaybacksCombo.SelectedIndex + 1),
            ExamTimeLimitMinutes = ExamSession.TimeLimitChoices[Math.Clamp(ExamLimitCombo.SelectedIndex, 0, ExamSession.TimeLimitChoices.Count - 1)],
            DailyGoalMinutes = SelectedDailyGoal,
            ReminderEnabled = ReminderCheckBox.IsChecked == true,
            ReminderMinutes = SelectedReminderMinutes,
            GroupCount = groupCount,
            CharactersPerMinute = (int)SpeedSlider.Value,
            FrequencyHz = (int)FrequencySlider.Value,
            VolumePercent = (int)VolumeSlider.Value,
            CharacterGapUnits = (int)CharacterGapSlider.Value,
            GroupGapUnits = (int)GroupGapSlider.Value,
            StartPauseUnits = (int)StartPauseSlider.Value,
            PlayStartSignal = PlayStartSignalCheckBox.IsChecked == true,
            NoisePercent = (int)NoiseSlider.Value,
            QsbPercent = (int)QsbSlider.Value,
            DriftHz = (int)DriftSlider.Value,
            CustomSymbols = _selectedSymbols,
            ThemeIndex = Math.Clamp(ThemeCombo.SelectedIndex, 0, 2),
            LanguageIndex = Math.Clamp(LanguageCombo.SelectedIndex, 0, 2),
            LearningAlphabetIndex = _learningAlphabet,
            LearningAudioModeIndex = _learningAudioMode,
            QuizCorrect = _quizCorrect,
            QuizTotal = _quizTotal,
            QuizAlphabetIndex = _quizAlphabet,
            QuizSpeed = _quizSpeed,
            LearningSignalSpeed = _learningSpeed,
            QuizAutoNext = QuizAutoNextCheckBox.IsChecked == true,
            QuizMisses = new Dictionary<string, int>(_quizMisses),
            ShowAnswerInput = _answerInputPreferred,
            LastPracticeAt = _lastPracticeAt,
            ActiveProfileName = string.IsNullOrWhiteSpace(ProfileCombo.Text) ? "Основной" : ProfileCombo.Text.Trim(),
            WindowWidth = WindowState == WindowState.Normal ? ActualWidth : RestoreBounds.Width,
            WindowHeight = WindowState == WindowState.Normal ? ActualHeight : RestoreBounds.Height,
            WindowMaximized = WindowState == WindowState.Maximized,
            MainTabIndex = Math.Max(0, MainTabs.SelectedIndex),
            TrainingPanelCollapsed = TrainingPanel.Visibility != Visibility.Visible,
            CourseStep = _courseStep
        };
    }

    private void ApplySettings(AppSettings settings)
    {
        AlphabetCombo.SelectedIndex = Math.Clamp(settings.AlphabetIndex, 0, 2);
        ContentModeCombo.SelectedIndex = (int)ContentModes.Clamp(settings.ContentModeIndex);
        KochLevelSlider.Value = KochMethod.ClampLevel((AlphabetMode)Math.Clamp(settings.AlphabetIndex, 0, 2), settings.KochLevel);
        EmphasizeProblemsCheckBox.IsChecked = settings.EmphasizeProblemSymbols;
        AutoSpeedCheckBox.IsChecked = settings.AutoSpeed;
        ExamPlaybacksCombo.SelectedIndex = ExamSession.ClampPlaybacks(settings.ExamPlaybacks) - 1;
        ExamLimitCombo.SelectedIndex = Math.Max(0, ExamSession.TimeLimitChoices.ToList().IndexOf(ExamSession.ClampTimeLimit(settings.ExamTimeLimitMinutes)));
        DailyGoalCombo.SelectedIndex = Math.Max(0, TrainingStatistics.DailyGoalChoices.ToList().IndexOf(TrainingStatistics.ClampGoal(settings.DailyGoalMinutes)));
        ReminderCheckBox.IsChecked = settings.ReminderEnabled;
        ReminderTimeCombo.SelectedIndex = PracticeNudge.NearestChoiceIndex(settings.ReminderMinutes);
        GroupCountText.Text = Math.Clamp(settings.GroupCount, 1, 100).ToString(CultureInfo.InvariantCulture);
        SpeedSlider.Value = Math.Clamp(settings.CharactersPerMinute, 20, 300);
        FrequencySlider.Value = Math.Clamp(settings.FrequencyHz, 300, 1_200);
        VolumeSlider.Value = Math.Clamp(settings.VolumePercent, 0, 100);
        CharacterGapSlider.Value = Math.Clamp(settings.CharacterGapUnits, 3, 20);
        GroupGapSlider.Value = Math.Clamp(settings.GroupGapUnits, 7, 30);
        StartPauseSlider.Value = Math.Clamp(settings.StartPauseUnits, 7, 60);
        PlayStartSignalCheckBox.IsChecked = settings.PlayStartSignal;
        NoiseSlider.Value = Math.Clamp(settings.NoisePercent, 0, 100);
        QsbSlider.Value = Math.Clamp(settings.QsbPercent, 0, 100);
        DriftSlider.Value = Math.Clamp(settings.DriftHz, 0, NoiseProfile.MaxDriftHz);
        _selectedSymbols = string.IsNullOrWhiteSpace(settings.CustomSymbols) ? "АГЖД" : settings.CustomSymbols;
        ThemeCombo.SelectedIndex = Math.Clamp(settings.ThemeIndex, 0, 2);
        LanguageCombo.SelectedIndex = Math.Clamp(settings.LanguageIndex, 0, 2);
        _learningAlphabet = Math.Clamp(settings.LearningAlphabetIndex, 0, 3);
        HighlightChoice(LearningAlphabetButtons, _learningAlphabet);
        _learningAudioMode = Math.Clamp(settings.LearningAudioModeIndex, 0, 1);
        UpdateLearningAudioModeButtons();
        _quizAlphabet = Math.Clamp(settings.QuizAlphabetIndex, 0, 3);
        HighlightChoice(QuizAlphabetButtons, _quizAlphabet);
        _quizSpeed = EarQuiz.ClampSpeed(settings.QuizSpeed);
        QuizSpeedSlider.Value = _quizSpeed;
        _learningSpeed = EarQuiz.ClampSpeed(settings.LearningSignalSpeed);
        LearningSpeedSlider.Value = _learningSpeed;
        UpdateLearningSpeedText();
        UpdateQuizSpeedText();
        QuizAutoNextCheckBox.IsChecked = settings.QuizAutoNext;
        _quizMisses = new Dictionary<string, int>(settings.QuizMisses ?? new Dictionary<string, int>());
        _lastPracticeAt = settings.LastPracticeAt;
        _courseStep = Math.Max(0, settings.CourseStep);
        UpdateKochSummary();
    }

    private void LoadProfiles(AppSettings settings)
    {
        _profiles = _profileService.Load();
        if (_profiles.Count == 0)
        {
            _profiles = _profileService.Save(TrainingProfile.FromSettings("Основной", settings));
        }

        _applyingProfile = true;
        ProfileCombo.ItemsSource = _profiles;
        ProfileCombo.DisplayMemberPath = nameof(TrainingProfile.Name);
        ProfileCombo.SelectedItem = _profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name, settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase)) ?? _profiles[0];
        ProfileCombo.Text = ((TrainingProfile)ProfileCombo.SelectedItem).Name;
        _applyingProfile = false;
    }

    private void ProfileCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_windowLoaded || _applyingProfile || ProfileCombo.SelectedItem is not TrainingProfile profile)
        {
            return;
        }

        var settings = ReadSettings();
        profile.ApplyTo(settings);
        ApplySettings(settings);
        UpdateSettingLabels();
        UpdateCustomSymbolsVisibility();
        UpdateSelectedSymbolsSummary();
        ProfileCombo.Text = profile.Name;
        _settingsService.Save(settings);
    }

    private void SaveProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = TrainingProfile.NormalizeName(ProfileCombo.Text);
            var settings = ReadSettings();
            settings.ActiveProfileName = name;
            _profiles = _profileService.Save(TrainingProfile.FromSettings(name, settings));
            LoadProfiles(settings);
            _settingsService.Save(settings);
            SetStatus(Texts.T("ПРОФИЛЬ СОХРАНЁН"), isActive: true);
        }
        catch (ArgumentException)
        {
            MessageBox.Show(Texts.T("Введите название профиля."), Texts.T("Профили"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ExportProfilesButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = Texts.T("Экспорт профилей"),
            Filter = Texts.T("Профили Morse Trainer (*.json)|*.json"),
            FileName = $"morse-profiles-{DateTime.Now:yyyy-MM-dd}.json",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            // Свои напевы едут вместе с профилями
            File.WriteAllText(dialog.FileName, ProfileTransfer.Export(_profiles, LearningCatalog.CustomChants), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            SetStatus(Texts.T("ПРОФИЛИ ЭКСПОРТИРОВАНЫ"), isActive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, Texts.T("Экспорт профилей"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportProfilesButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Texts.T("Импорт профилей"),
            Filter = Texts.T("Профили Morse Trainer (*.json)|*.json|Все файлы (*.*)|*.*")
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var package = ProfileTransfer.ImportPackage(File.ReadAllText(dialog.FileName));
            _profiles = _profileService.Import(package.Profiles);
            LoadProfiles(ReadSettings());
            var message = Texts.F("Импортировано профилей: {0}.", package.Profiles.Count);
            if (package.Chants.Count > 0)
            {
                LearningCatalog.SetCustomChants(_chantStore.Merge(package.Chants));
                RefreshLearningItems();
                UpdateCustomVoiceText();
                message += " " + Texts.F("Своих напевов: {0}.", package.Chants.Count);
            }

            MessageBox.Show(this, message, Texts.T("Профили"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is FormatException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, Texts.T("Импорт профилей"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not TrainingProfile profile)
        {
            return;
        }

        _profiles = _profileService.Delete(profile.Name);
        var settings = ReadSettings();
        if (_profiles.Count == 0)
        {
            settings.ActiveProfileName = "Основной";
            _profiles = _profileService.Save(TrainingProfile.FromSettings("Основной", settings));
        }
        else
        {
            settings.ActiveProfileName = _profiles[0].Name;
            _profiles[0].ApplyTo(settings);
            ApplySettings(settings);
        }

        LoadProfiles(settings);
        _settingsService.Save(settings);
    }
}
