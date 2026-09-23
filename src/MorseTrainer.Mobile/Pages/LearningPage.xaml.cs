using System.Security.Cryptography;
using MorseTrainer.Domain;
using MorseTrainer.Localization;
using MorseTrainer.Mobile.Services;
using MorseTrainer.Models;
using MorseTrainer.Services;

namespace MorseTrainer.Mobile.Pages;

public partial class LearningPage : ContentPage
{
    private readonly IAudioPlaybackService _audioPlayback;
    private readonly MobileSettingsService _settingsService;
    private readonly VoicePackService _voicePack;
    private readonly ChantStore _chantStore;
    private readonly TrainingHistoryStore _historyStore;
    private readonly TrainingPage _trainingPage;
    private IReadOnlyList<LearningSymbolItem> _visibleItems = Array.Empty<LearningSymbolItem>();
    private LearningSymbolItem? _quizTarget;
    private int _quizCorrect;
    private int _quizTotal;
    private bool _pageReady;

    public LearningPage(
        IAudioPlaybackService audioPlayback,
        MobileSettingsService settingsService,
        VoicePackService voicePack,
        ChantStore chantStore,
        TrainingHistoryStore historyStore,
        TrainingPage trainingPage)
    {
        InitializeComponent();
        _audioPlayback = audioPlayback;
        _settingsService = settingsService;
        _voicePack = voicePack;
        _chantStore = chantStore;
        _historyStore = historyStore;
        _trainingPage = trainingPage;
        AlphabetPicker.ItemsSource = new[] { Texts.T("Русские"), Texts.T("Латинские"), Texts.T("Русские и латинские"), Texts.T("Цифры") };
        AlphabetPicker.SelectedIndex = 0;
        _pageReady = true;
        RefreshItems();
        var settings = _settingsService.LoadSettings();
        _quizCorrect = settings.QuizCorrect;
        _quizTotal = settings.QuizTotal;
        UpdateScore();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshCourse();
    }

    // ---------- Курс «С нуля до 60 зн/мин» ----------

    private void RefreshCourse()
    {
        var settings = _settingsService.LoadSettings();
        var steps = Course.Steps(Course.AlphabetFor(settings.AlphabetIndex));
        var history = _historyStore.Load();
        var passed = Course.PassedCount(history, steps);
        var step = Course.Find(steps, settings.CourseStep);
        if (step is null)
        {
            CourseTitleLabel.Text = Texts.T("Курс «С нуля до 60 зн/мин»");
            CourseDetailsLabel.Text = Texts.F("{0} шагов: метод Коха по 4 символа, слова на 50 и 60 зн/мин, итоговый экзамен. Кнопка ставит нужные настройки и создаёт задание.", steps.Count);
            CourseStatusLabel.Text = passed > 0 ? Texts.F("Пройдено шагов: {0} из {1}.", passed, steps.Count) : string.Empty;
            CourseStartButton.Text = Texts.T("Начать курс");
            CourseNextButton.IsVisible = false;
            CourseResetButton.IsVisible = false;
            return;
        }

        var stepPassed = Course.IsPassed(history, step);
        CourseTitleLabel.Text = Texts.F("Курс «С нуля до 60 зн/мин» · шаг {0} из {1}: {2}", step.Number, steps.Count, step.Title);
        CourseDetailsLabel.Text = step.Details;
        CourseStatusLabel.Text = Course.Status(history, step) + " " + Texts.F("Пройдено шагов: {0} из {1}.", passed, steps.Count);
        CourseStartButton.Text = step.IsExam ? Texts.T("Начать экзамен") : Texts.T("Начать шаг");
        CourseNextButton.IsVisible = step.Number < steps.Count;
        CourseNextButton.IsEnabled = stepPassed;
        CourseResetButton.IsVisible = true;
    }

    private async void CourseStartButton_OnClicked(object sender, EventArgs e)
    {
        var settings = _settingsService.LoadSettings();
        settings.CourseStep = Math.Max(1, settings.CourseStep);
        await StartCourseStepAsync(settings);
    }

    private async void CourseNextButton_OnClicked(object sender, EventArgs e)
    {
        var settings = _settingsService.LoadSettings();
        settings.CourseStep = Math.Min(Course.Steps(Course.AlphabetFor(settings.AlphabetIndex)).Count, settings.CourseStep + 1);
        await StartCourseStepAsync(settings);
    }

    private void CourseResetButton_OnClicked(object sender, EventArgs e)
    {
        var settings = _settingsService.LoadSettings();
        settings.CourseStep = 0;
        _settingsService.SaveSettings(settings);
        RefreshCourse();
    }

    /// <summary>Ставит настройки шага, открывает «Тренировку» и создаёт задание (или экзамен).</summary>
    private async Task StartCourseStepAsync(AppSettings settings)
    {
        if (Course.Find(Course.Steps(Course.AlphabetFor(settings.AlphabetIndex)), settings.CourseStep) is not { } step)
        {
            return;
        }

        Course.Apply(step, settings);
        _settingsService.SaveSettings(settings);
        RefreshCourse();
        await Shell.Current.GoToAsync("//training");
        await _trainingPage.StartTaskAsync(step.IsExam);
    }

    private void Filter_OnChanged(object sender, EventArgs e)
    {
        if (_pageReady)
        {
            RefreshItems();
        }
    }

    private void RefreshItems()
    {
        if (CardsView is null)
        {
            return;
        }

        var items = LearningCatalog.GetItems(Math.Clamp(AlphabetPicker?.SelectedIndex ?? 0, 0, 3));
        var search = SearchBox?.Text?.Trim() ?? string.Empty;
        _visibleItems = string.IsNullOrWhiteSpace(search)
            ? items
            : items.Where(item => item.Symbol.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)
                                  || item.Chant.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        CardsView.ItemsSource = _visibleItems;
    }

    private async void VoiceButton_OnClicked(object sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: char symbol })
        {
            return;
        }

        var item = LearningCatalog.Find(symbol);
        var path = await _voicePack.GetVoiceFileAsync(symbol);
        if (path is null)
        {
            await DisplayAlertAsync(Texts.T("Голос недоступен"), Texts.T("Для этого символа не найден встроенный напев."), Texts.T("Закрыть"));
            return;
        }

        QuizStatusLabel.Text = $"{item?.Symbol}: {item?.Chant}";
        await PlayFileSafelyAsync(path);
    }

    // ---------- Свои напевы и голос ----------

    private async void EditChantButton_OnClicked(object sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: char symbol } || LearningCatalog.Find(symbol) is not { } item)
        {
            return;
        }

        var builtIn = LearningCatalog.BuiltInChant(symbol) ?? string.Empty;
        var text = await DisplayPromptAsync(
            Texts.F("Напев для {0}", symbol),
            Texts.F("Код {0}, ритм: {1}. Один слог на каждую точку и тире через дефис. Пустое поле — встроенный напев «{2}».", item.Code, ChantBook.Pattern(item.Code), builtIn),
            Texts.T("Сохранить"), Texts.T("Отмена"), builtIn, ChantBook.MaxLength, Keyboard.Text, item.Chant);
        if (text is null)
        {
            return;
        }

        try
        {
            LearningCatalog.SetCustomChants(_chantStore.Set(symbol, text));
            RefreshItems();
        }
        catch (ArgumentException exception)
        {
            await DisplayAlertAsync(Texts.T("Изменить напев"), exception.Message, Texts.T("Понятно"));
        }
    }

    private async void CustomVoiceButton_OnClicked(object sender, EventArgs e)
    {
        var count = CustomVoice.Count(MobilePaths.VoiceDirectory);
        var add = Texts.T("Добавить файлы");
        var remove = count > 0 ? Texts.T("Удалить свой голос") : null;
        var hint = CustomVoice.Describe(count) + "\n" +
                   Texts.F("Имя файла — код символа, точка 0, тире 1: {0} для А.", CustomVoice.ExampleFileName('А', ".m4a"));
        var choice = await DisplayActionSheetAsync(hint, Texts.T("Отмена"), remove, add);
        try
        {
            if (choice == add)
            {
                var files = await FilePicker.Default.PickMultipleAsync(new PickOptions { PickerTitle = Texts.T("Файлы голоса code_XXXX (.m4a, .mp3, .wav)") });
                var added = 0;
                var skipped = 0;
                Directory.CreateDirectory(MobilePaths.VoiceDirectory);
                foreach (var file in files ?? Enumerable.Empty<FileResult?>())
                {
                    if (file is null || !CustomVoice.IsClipFileName(file.FileName))
                    {
                        skipped++;
                        continue;
                    }

                    await using var source = await file.OpenReadAsync();
                    await using var target = File.Create(Path.Combine(MobilePaths.VoiceDirectory, file.FileName.ToLowerInvariant()));
                    await source.CopyToAsync(target);
                    added++;
                }

                await DisplayAlertAsync(Texts.T("Свой голос"), Texts.F("Добавлено файлов: {0}, пропущено (имя не code_XXXX): {1}.", added, skipped), Texts.T("Понятно"));
            }
            else if (choice is not null && choice == remove)
            {
                Directory.Delete(MobilePaths.VoiceDirectory, recursive: true);
                await DisplayAlertAsync(Texts.T("Свой голос"), CustomVoice.Describe(0), Texts.T("Понятно"));
            }
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(Texts.T("Свой голос"), exception.Message, Texts.T("Закрыть"));
        }
    }

    private async void SignalButton_OnClicked(object sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: char symbol })
        {
            await PlayMorseAsync(symbol);
        }
    }

    private async Task PlayMorseAsync(char symbol)
    {
        var settings = _settingsService.LoadSettings();
        var clip = MorseAudioService.Render(symbol.ToString(), 45, settings.FrequencyHz, settings.VolumePercent, 3, 7);
        var path = await AudioFileService.SaveClipAsync(clip, "learning-signal.wav");
        await PlayFileSafelyAsync(path);
    }

    private async Task PlayFileSafelyAsync(string path)
    {
        try
        {
            _audioPlayback.Stop();
            await _audioPlayback.PlayAsync(path);
        }
        catch (OperationCanceledException)
        {
            // Starting another card intentionally stops the previous sound.
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(Texts.T("Не удалось воспроизвести"), exception.Message, Texts.T("Закрыть"));
        }
    }

    private async void QuizButton_OnClicked(object sender, EventArgs e)
    {
        var pool = _visibleItems.Count >= 4 ? _visibleItems : LearningCatalog.Russian;
        _quizTarget = pool[RandomNumberGenerator.GetInt32(pool.Count)];
        var answers = new List<LearningSymbolItem> { _quizTarget };
        answers.AddRange(pool.Where(item => item.Symbol != _quizTarget.Symbol && item.Code != _quizTarget.Code)
            .OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).Take(3));
        answers = answers.OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).ToList();

        QuizAnswers.Children.Clear();
        foreach (var answer in answers)
        {
            var button = new Button
            {
                Text = answer.Symbol.ToString(),
                CommandParameter = answer.Symbol,
                MinimumWidthRequest = 64,
                Margin = 4
            };
            button.Clicked += QuizAnswer_OnClicked;
            QuizAnswers.Children.Add(button);
        }

        QuizStatusLabel.Text = Texts.T("Слушайте…");
        await PlayMorseAsync(_quizTarget.Symbol);
        QuizStatusLabel.Text = Texts.T("Какой символ прозвучал?");
    }

    private void QuizAnswer_OnClicked(object? sender, EventArgs e)
    {
        if (_quizTarget is null || sender is not Button { CommandParameter: char answer })
        {
            return;
        }

        _quizTotal++;
        if (answer == _quizTarget.Symbol)
        {
            _quizCorrect++;
            QuizStatusLabel.Text = Texts.F("Верно: {0} — {1}", _quizTarget.Symbol, _quizTarget.Chant);
        }
        else
        {
            QuizStatusLabel.Text = Texts.F("Правильно: {0} — {1}", _quizTarget.Symbol, _quizTarget.Chant);
        }

        foreach (var button in QuizAnswers.Children.OfType<Button>())
        {
            button.IsEnabled = false;
        }

        var settings = _settingsService.LoadSettings();
        settings.QuizCorrect = _quizCorrect;
        settings.QuizTotal = _quizTotal;
        _settingsService.SaveSettings(settings);
        UpdateScore();
    }

    private void UpdateScore()
    {
        QuizScoreLabel.Text = Texts.F("Результат: {0} / {1}", _quizCorrect, _quizTotal);
    }

    // Планшет или альбомная ориентация: центрируем контент полосой до 720 px
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0)
        {
            RootLayout.Padding = TabletLayout.PaddingFor(width, 14);
        }
        var columns = TabletLayout.LearningColumnsFor(width);
        if (CardsLayout.Span != columns)
        {
            CardsLayout.Span = columns;
        }
    }
}
