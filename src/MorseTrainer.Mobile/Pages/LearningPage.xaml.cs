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
    private readonly IAudioRecorderService _recorder;
    private const string AlphabetKey = "learning.alphabet";

    private readonly Button[] _alphabetButtons;
    private int _alphabet;
    private bool _bookOpen;

    public LearningPage(
        IAudioPlaybackService audioPlayback,
        MobileSettingsService settingsService,
        VoicePackService voicePack,
        ChantStore chantStore,
        TrainingHistoryStore historyStore,
        TrainingPage trainingPage,
        IAudioRecorderService recorder)
    {
        InitializeComponent();
        _audioPlayback = audioPlayback;
        _settingsService = settingsService;
        _voicePack = voicePack;
        _chantStore = chantStore;
        _historyStore = historyStore;
        _trainingPage = trainingPage;
        _recorder = recorder;
        _alphabetButtons = new[] { AlphabetRussianButton, AlphabetLatinButton, AlphabetBothButton, AlphabetDigitsButton };
        _alphabet = ChoiceButtons.LoadAlphabet(AlphabetKey);
        ChoiceButtons.Highlight(_alphabetButtons, _alphabet);
        RefreshItems();
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
            // Пустая строка статуса оставляла зазор над кнопкой
            CourseStatusLabel.IsVisible = passed > 0;
            CourseStartButton.Text = Texts.T("Начать курс");
            CourseNextButton.IsVisible = false;
            // После сброса с пройденными шагами меню остаётся: через «Выбрать шаг…» можно продолжить с нужного
            CourseMenuButton.IsVisible = passed > 0;
            UpdateCourseMarks(steps, history, 0, visible: passed > 0);
            return;
        }

        var stepPassed = Course.IsPassed(history, step);
        CourseTitleLabel.Text = Texts.F("Курс «С нуля до 60 зн/мин» · шаг {0} из {1}: {2}", step.Number, steps.Count, step.Title);
        CourseDetailsLabel.Text = step.Details;
        CourseStatusLabel.Text = Course.Status(history, step) + " " + Texts.F("Пройдено шагов: {0} из {1}.", passed, steps.Count);
        CourseStatusLabel.IsVisible = true;
        CourseStartButton.Text = step.IsExam ? Texts.T("Начать экзамен") : Texts.T("Начать шаг");
        CourseNextButton.IsVisible = step.Number < steps.Count;
        CourseNextButton.IsEnabled = stepPassed;
        CourseMenuButton.IsVisible = true;
        UpdateCourseMarks(steps, history, step.Number, visible: true);
    }

    /// <summary>Полоса прогресса курса: ● пройден, янтарный ● — текущий шаг, ○ — впереди.</summary>
    private void UpdateCourseMarks(IReadOnlyList<CourseStep> steps, IReadOnlyList<TrainingRecord> history, int current, bool visible)
    {
        CourseProgressLabel.IsVisible = visible;
        if (!visible)
        {
            return;
        }

        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var passedColor = (Color)Application.Current!.Resources[dark ? "Primary" : "PrimaryDark"];
        var text = new FormattedString();
        foreach (var mark in Course.Marks(steps, history, current))
        {
            text.Spans.Add(new Span
            {
                Text = mark == CourseMark.Ahead ? "○" : "●",
                TextColor = mark switch
                {
                    CourseMark.Passed => passedColor,
                    CourseMark.Current => Color.FromArgb("#F59E0B"),
                    _ => Color.FromArgb("#94A3B8")
                }
            });
        }

        CourseProgressLabel.FormattedText = text;
    }

    private async void CourseStartButton_OnClicked(object sender, EventArgs e)
    {
        var settings = _settingsService.LoadSettings();
        // Первый запуск курса — сначала «книжка»: как устроен курс и метод Коха (можно пропустить)
        if (settings.CourseStep == 0 && !await OpenCourseBookAsync(settings, startMode: true))
        {
            return;
        }

        settings.CourseStep = Math.Max(1, settings.CourseStep);
        await StartCourseStepAsync(settings);
    }

    // «📖 Как устроен курс» — перечитать книжку в любой момент
    private async void CourseBookButton_OnClicked(object sender, EventArgs e) => await OpenCourseBookAsync(_settingsService.LoadSettings(), startMode: false);

    /// <summary>Открывает книжку курса; true — курс нужно начать (дочитали или пропустили).</summary>
    private async Task<bool> OpenCourseBookAsync(AppSettings settings, bool startMode)
    {
        // Двойное нажатие не кладёт вторую книжку поверх первой
        if (_bookOpen)
        {
            return false;
        }

        _bookOpen = true;
        try
        {
            var book = new CourseBookPage(CourseBook.Pages(Course.AlphabetFor(settings.AlphabetIndex)), startMode);
            await Navigation.PushModalAsync(book);
            return await book.Result;
        }
        finally
        {
            _bookOpen = false;
        }
    }

    private async void CourseNextButton_OnClicked(object sender, EventArgs e)
    {
        var settings = _settingsService.LoadSettings();
        settings.CourseStep = Math.Min(Course.Steps(Course.AlphabetFor(settings.AlphabetIndex)).Count, settings.CourseStep + 1);
        await StartCourseStepAsync(settings);
    }

    // «⋯»: выбрать шаг, перечитать книжку, сбросить курс. Сброс — отдельным пунктом меню, случайно одним касанием не нажать
    private async void CourseMenuButton_OnClicked(object sender, EventArgs e)
    {
        var settings = _settingsService.LoadSettings();
        var choose = Texts.T("Выбрать шаг…");
        var book = Texts.T("📖 Как устроен курс");
        var reset = settings.CourseStep > 0 ? Texts.T("Сбросить курс") : null;
        var action = await DisplayActionSheetAsync(Texts.T("Курс «С нуля до 60 зн/мин»"), Texts.T("Отмена"), reset, choose, book);
        if (action == choose)
        {
            await ChooseCourseStepAsync(settings);
        }
        else if (action == book)
        {
            await OpenCourseBookAsync(settings, startMode: false);
        }
        else if (reset is not null && action == reset)
        {
            settings.CourseStep = 0;
            _settingsService.SaveSettings(settings);
            RefreshCourse();
        }
    }

    /// <summary>
    /// «Выбрать шаг…»: все шаги курса, пройденные (по истории) отмечены ✓. После случайного сброса можно продолжить
    /// с нужного шага, а не проходить всё с первого. Шаг только выбирается — задание создаёт «Начать шаг».
    /// </summary>
    private async Task ChooseCourseStepAsync(AppSettings settings)
    {
        var steps = Course.Steps(Course.AlphabetFor(settings.AlphabetIndex));
        var choices = Course.StepChoices(steps, _historyStore.Load());
        var picked = await DisplayActionSheetAsync(Texts.T("С какого шага продолжить?"), Texts.T("Отмена"), null,
            choices.Select(choice => choice.Label).ToArray());
        if (choices.FirstOrDefault(choice => choice.Label == picked) is not { } chosen)
        {
            return;
        }

        settings.CourseStep = chosen.Number;
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

    private void CourseDetailsButton_OnClicked(object sender, EventArgs e)
    {
        CourseDetailsLabel.IsVisible = !CourseDetailsLabel.IsVisible;
        CourseBookButton.IsVisible = CourseDetailsLabel.IsVisible;
        CourseDetailsButton.Text = CourseDetailsLabel.IsVisible ? "▴" : "▾";
    }

    private void AlphabetButton_OnClicked(object sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string parameter } || !int.TryParse(parameter, out var index))
        {
            return;
        }

        _alphabet = Math.Clamp(index, 0, 3);
        ChoiceButtons.SaveAlphabet(AlphabetKey, _alphabet);
        ChoiceButtons.Highlight(_alphabetButtons, _alphabet);
        RefreshItems();
    }

    // Поиск нужен редко — поле появляется по кнопке 🔍, при скрытии фильтр сбрасывается
    private void SearchToggleButton_OnClicked(object sender, EventArgs e)
    {
        SearchBox.IsVisible = !SearchBox.IsVisible;
        if (SearchBox.IsVisible)
        {
            SearchBox.Focus();
        }
        else if (!string.IsNullOrEmpty(SearchBox.Text))
        {
            SearchBox.Text = string.Empty;
        }
    }

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e) => RefreshItems();

    private void RefreshItems()
    {
        // Символы со своей записью голоса помечаются «🎙 ваш голос»
        var items = LearningCatalog.GetItems(_alphabet)
            .Select(item => CustomRecording(item.Symbol) is null ? item : item with { HasOwnVoice = true })
            .ToArray();
        var search = SearchBox.Text?.Trim() ?? string.Empty;
        CardsView.ItemsSource = string.IsNullOrWhiteSpace(search)
            ? items
            : items.Where(item => item.Symbol.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)
                                  || item.Chant.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private async void VoiceButton_OnClicked(object sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: char symbol })
        {
            return;
        }

        var path = await _voicePack.GetVoiceFileAsync(symbol);
        if (path is null)
        {
            await DisplayAlertAsync(Texts.T("Голос недоступен"), Texts.T("Для этого символа не найден встроенный напев."), Texts.T("Закрыть"));
            return;
        }

        await PlayFileSafelyAsync(path);
    }

    // ---------- Свои напевы: текст и голос ----------

    private static readonly string[] VoiceExtensions = { ".m4a", ".mp3", ".wav" };

    /// <summary>Своя запись символа (m4a, mp3 или wav в папке voice) или null.</summary>
    private static string? CustomRecording(char symbol) => CustomVoice.Find(MobilePaths.VoiceDirectory, symbol, VoiceExtensions);

    /// <summary>Все записи для полного голоса: русские буквы и цифры; латиница использует те же коды.</summary>
    private static IReadOnlyList<char> RecordingOrder() => MorseAlphabet.Russian.Keys
        .Concat(MorseAlphabet.Digits.Keys)
        .DistinctBy(symbol => VoiceClipCatalog.GetClipName(symbol))
        .ToArray();

    private async void EditChantButton_OnClicked(object sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: char symbol } || LearningCatalog.Find(symbol) is not { } item)
        {
            return;
        }

        var editText = Texts.T("Изменить текст напева");
        var record = Texts.T("Записать свой голос");
        var listen = Texts.T("Прослушать мою запись");
        var delete = Texts.T("Удалить мою запись");
        var hasRecording = CustomRecording(symbol) is not null;
        var options = hasRecording ? new[] { record, listen, editText } : new[] { record, editText };
        var choice = await DisplayActionSheetAsync($"{symbol} — {item.Chant}", Texts.T("Отмена"), hasRecording ? delete : null, options);
        if (choice == editText)
        {
            await EditChantTextAsync(item);
        }
        else if (choice == record)
        {
            await RecordSymbolAsync(symbol, sequence: false);
            RefreshItems();
        }
        else if (choice == listen && CustomRecording(symbol) is { } path)
        {
            await PlayFileSafelyAsync(path);
        }
        else if (choice is not null && choice == delete)
        {
            DeleteRecordings(symbol);
            RefreshItems();
        }
    }

    private async Task EditChantTextAsync(LearningSymbolItem item)
    {
        var builtIn = LearningCatalog.BuiltInChant(item.Symbol) ?? string.Empty;
        var text = await DisplayPromptAsync(
            Texts.F("Напев для {0}", item.Symbol),
            Texts.F("Код {0}, ритм: {1}. Один слог на каждую точку и тире через дефис. Пустое поле — встроенный напев «{2}».", item.Code, ChantBook.Pattern(item.Code), builtIn),
            Texts.T("Сохранить"), Texts.T("Отмена"), builtIn, ChantBook.MaxLength, Keyboard.Text, item.Chant);
        if (text is null)
        {
            return;
        }

        try
        {
            LearningCatalog.SetCustomChants(_chantStore.Set(item.Symbol, text));
            RefreshItems();
        }
        catch (ArgumentException exception)
        {
            await DisplayAlertAsync(Texts.T("Изменить напев"), exception.Message, Texts.T("Понятно"));
        }
    }

    /// <summary>
    /// Запись напева символа: пока открыто окно «Идёт запись», микрофон пишет; после «Готово» запись звучит, и её можно
    /// оставить или переписать. В режиме «по очереди» возвращает false, если пользователь решил закончить.
    /// </summary>
    private async Task<bool> RecordSymbolAsync(char symbol, bool sequence, int number = 0, int total = 0)
    {
        if (LearningCatalog.Find(symbol) is not { } item || VoiceClipCatalog.GetClipName(symbol) is not { } clipName)
        {
            return true;
        }

        if (!await _recorder.RequestPermissionAsync())
        {
            await DisplayAlertAsync(Texts.T("Свой голос"), Texts.T("Нет доступа к микрофону: разрешите его для Morse Trainer в настройках телефона."), Texts.T("Понятно"));
            return false;
        }

        var target = Path.Combine(MobilePaths.VoiceDirectory, clipName + ".m4a");
        var temporary = Path.Combine(FileSystem.CacheDirectory, "recording-" + clipName + ".m4a");
        var title = sequence ? Texts.F("Запись {0} из {1}: {2}", number, total, symbol) : Texts.F("Запись: {0}", symbol);
        while (true)
        {
            _audioPlayback.Stop();
            try
            {
                _recorder.Start(temporary);
            }
            catch (Exception exception)
            {
                await DisplayAlertAsync(Texts.T("Свой голос"), exception.Message, Texts.T("Закрыть"));
                return false;
            }

            await DisplayAlertAsync(title,
                Texts.F("Идёт запись. Скажите: «{0}».\nРитм {1}: протяжные слоги (тире) тяните, короткие (точки) — коротко.\nНажмите «Готово», когда закончите.",
                    item.Chant, ChantBook.Pattern(item.Code)),
                Texts.T("Готово"));
            if (!_recorder.Stop() || !File.Exists(temporary) || new FileInfo(temporary).Length < 1000)
            {
                var again = await DisplayAlertAsync(title, Texts.T("Запись не получилась: слишком коротко. Попробовать ещё раз?"), Texts.T("Ещё раз"), Texts.T("Пропустить"));
                if (again)
                {
                    continue;
                }

                return true;
            }

            await PlayFileSafelyAsync(temporary);
            var keep = sequence ? Texts.T("Оставить и дальше") : Texts.T("Оставить");
            var redo = Texts.T("Переписать");
            var choice = await DisplayActionSheetAsync($"{title}: {item.Chant}", sequence ? Texts.T("Закончить") : Texts.T("Отмена"), null, keep, redo);
            if (choice == redo)
            {
                continue;
            }

            if (choice == keep)
            {
                DeleteRecordings(symbol);
                Directory.CreateDirectory(MobilePaths.VoiceDirectory);
                File.Move(temporary, target, overwrite: true);
                return true;
            }

            // «Закончить» или «Отмена»: запись не сохраняется
            File.Delete(temporary);
            return !sequence;
        }
    }

    private static void DeleteRecordings(char symbol)
    {
        if (VoiceClipCatalog.GetClipName(symbol) is not { } clipName || !Directory.Exists(MobilePaths.VoiceDirectory))
        {
            return;
        }

        foreach (var extension in VoiceExtensions)
        {
            var path = Path.Combine(MobilePaths.VoiceDirectory, clipName + extension);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>Все напевы по очереди, начиная с первого без записи: 32 русские буквы и 10 цифр.</summary>
    private async Task RecordAllAsync()
    {
        var order = RecordingOrder();
        var start = order.ToList().FindIndex(symbol => CustomRecording(symbol) is null);
        if (start < 0)
        {
            var again = await DisplayAlertAsync(Texts.T("Свой голос"), Texts.T("Все напевы уже записаны. Записать заново с начала?"), Texts.T("С начала"), Texts.T("Отмена"));
            if (!again)
            {
                return;
            }

            start = 0;
        }

        for (var index = start; index < order.Count; index++)
        {
            if (!await RecordSymbolAsync(order[index], sequence: true, index + 1, order.Count))
            {
                break;
            }
        }

        RefreshItems();
        var recorded = order.Count(symbol => CustomRecording(symbol) is not null);
        await DisplayAlertAsync(Texts.T("Свой голос"),
            Texts.F("Записано {0} из {1}. Чтобы ваши напевы стали голосом приложения для всех, нажмите «Свой голос…» → «Поделиться записями» и отправьте файлы разработчику.", recorded, order.Count),
            Texts.T("Понятно"));
    }

    private async void CustomVoiceButton_OnClicked(object sender, EventArgs e)
    {
        var order = RecordingOrder();
        var recorded = order.Count(symbol => CustomRecording(symbol) is not null);
        var recordAll = Texts.T("Записать все напевы по очереди");
        var share = Texts.T("Поделиться записями");
        var add = Texts.T("Добавить файлы");
        var remove = recorded > 0 ? Texts.T("Удалить свой голос") : null;
        var title = Texts.F("Свой голос: записано {0} из {1}", recorded, order.Count);
        var options = recorded > 0 ? new[] { recordAll, share, add } : new[] { recordAll, add };
        var choice = await DisplayActionSheetAsync(title, Texts.T("Отмена"), remove, options);
        try
        {
            if (choice == recordAll)
            {
                await RecordAllAsync();
            }
            else if (choice == share)
            {
                var files = Directory.EnumerateFiles(MobilePaths.VoiceDirectory)
                    .Where(path => CustomVoice.IsClipFileName(Path.GetFileName(path)))
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .Select(path => new ShareFile(path))
                    .ToList();
                await Share.Default.RequestAsync(new ShareMultipleFilesRequest { Title = Texts.T("Напевы Morse Trainer"), Files = files });
            }
            else if (choice == add)
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

                RefreshItems();
                await DisplayAlertAsync(Texts.T("Свой голос"), Texts.F("Добавлено файлов: {0}, пропущено (имя не code_XXXX): {1}.", added, skipped), Texts.T("Понятно"));
            }
            else if (choice is not null && choice == remove)
            {
                var confirmed = await DisplayAlertAsync(Texts.T("Свой голос"), Texts.F("Удалить все ваши записи ({0})?", recorded), Texts.T("Удалить"), Texts.T("Отмена"));
                if (confirmed)
                {
                    Directory.Delete(MobilePaths.VoiceDirectory, recursive: true);
                    RefreshItems();
                }
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
