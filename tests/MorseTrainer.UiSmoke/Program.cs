using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MorseTrainer.Localization;
using MorseTrainer.Models;
using MorseTrainer.Services;

namespace MorseTrainer.UiSmoke;

/// <summary>
/// Дымовой тест интерфейса Windows. Поднимает настоящие ресурсы App и главное окно, переключает все вкладки,
/// генерирует задание, нажимает основные кнопки и проверяет, что переводы {loc:Loc} и привязки живые.
/// Звук не воспроизводится, диалоги не открываются. Компиляция ошибки разметки на старте не ловит — этот тест ловит.
/// Аргумент командной строки: папка для скриншотов (необязательно).
/// </summary>
public static class Program
{
    private static readonly List<string> Failures = new();
    private static Exception? _dispatcherException;
    private static string _dataDirectory = string.Empty;

    [STAThread]
    public static int Main(string[] args)
    {
        // Данные пишутся во временную папку, чтобы не трогать настройки и историю пользователя
        _dataDirectory = Path.Combine(Path.GetTempPath(), "morse-ui-smoke-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, _dataDirectory);
        var screenshots = args.Length > 0 ? args[0] : null;

        // Сторожевой таймер: зависание или модальное окно не должны держать CI бесконечно
        var watchdog = new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromMinutes(4));
            Console.Error.WriteLine("FAIL  UI smoke test timed out: a modal dialog or a hang.");
            Environment.Exit(3);
        })
        {
            IsBackground = true
        };
        watchdog.Start();

        try
        {
            // Продолжения await после наших кликов должны возвращаться на поток окна, а не в пул потоков
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

            // Конструктор Application сам ставит в очередь OnStartup и StartupUri: первое окно создаёт настоящий
            // путь запуска приложения. Язык первого окна задаём заранее через settings.json во временной папке.
            ResetData(deleteDirectory: false);
            new SettingsService().Save(new AppSettings { LanguageIndex = (int)AppLanguage.Russian });
            App.HeadlessMode = true;
            var app = new App();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.CurrentDispatcher.UnhandledException += (_, e) =>
            {
                _dispatcherException ??= e.Exception;
                e.Handled = true;
            };
            DoEvents();
            var startupWindow = app.Windows.OfType<MainWindow>().FirstOrDefault()
                                ?? throw new InvalidOperationException("StartupUri did not open MainWindow.");
            Check(Texts.Language == AppLanguage.Russian, "OnStartup applied the language from settings.json");

            Run("Main window (Russian, StartupUri)", () => ExerciseMainWindow(startupWindow, screenshots, "ru"));
            Run("Main window (English, dark theme)", () =>
            {
                // Второй прогон — английский интерфейс в тёмной теме: скриншоты обеих тем для README
                ResetData(deleteDirectory: false);
                new SettingsService().Save(new AppSettings { LanguageIndex = (int)AppLanguage.English, ThemeIndex = 1 });
                Texts.Apply(AppLanguage.English);
                ExerciseMainWindow(new MainWindow(), screenshots, "en");
            });
            Run("Symbol selection window", ExerciseSymbolSelection);
            Run("Chant editor window", ExerciseChantEditor);
        }
        catch (Exception exception)
        {
            Failures.Add("Startup: " + exception.Message);
            Console.WriteLine($"FAIL  Startup: {exception}");
        }
        finally
        {
            ResetData(deleteDirectory: true);
        }

        if (Failures.Count > 0)
        {
            Console.Error.WriteLine($"{Failures.Count} UI smoke check(s) failed.");
            return 1;
        }

        Console.WriteLine("UI smoke test passed.");
        return 0;
    }

    private static void ExerciseMainWindow(MainWindow window, string? screenshots, string suffix)
    {
        try
        {
            if (!window.IsVisible)
            {
                window.Show();
            }

            WaitUntil(() => window.StatusBadgeText.Text == Texts.T("ГОТОВО") && window.GenerateButton.IsEnabled, "task generated on load");

            var version = typeof(App).Assembly.GetName().Version!;
            Check(window.SubtitleText.Text.Contains($"{version.Major}.{version.Minor}.{version.Build}"), "subtitle shows the version: " + window.SubtitleText.Text);
            Check(window.MainTabs.Items.Count == 5, "window has five tabs (Progress is hidden without typed answers)");
            foreach (var item in window.MainTabs.Items.OfType<TabItem>())
            {
                Check(item.Header is string header && header.Length > 0 && !header.Contains("loc:"), "tab header is translated: " + item.Header);
            }

            Check(((TabItem)window.MainTabs.Items[0]).Header as string == Texts.T("Тренировка"), "first tab header follows the language");
            Check(window.GenerateButton.Content as string == Texts.T("Сгенерировать задание"), "generate button text follows the language");
            Check(window.TaskMetaText.Text.Contains('×'), "task meta shows the group count: " + window.TaskMetaText.Text);
            Check(window.AnswerDisplayText.Text.Contains('•') && window.AnswerDisplayText.Text.Contains(' '), "answer is hidden as bullets");
            Check(window.PlayButton.IsEnabled && window.CheckAnswerButton.IsEnabled && window.UserAnswerText.IsEnabled, "task controls are enabled");
            var area = SystemParameters.WorkArea;
            Check(window.ActualWidth <= area.Width + 1 && window.ActualHeight <= area.Height + 1,
                $"window fits the work area: {window.ActualWidth:0}×{window.ActualHeight:0} in {area.Width:0}×{area.Height:0}");
            Check(window.ExamPlaybacksCombo.Items.Count == 3 && window.ExamPlaybacksCombo.SelectedIndex == 0, "exam playbacks default to one");
            Check(window.NudgeBanner.Visibility == Visibility.Collapsed, "no 'you have not practiced' banner for an empty history");

            // По умолчанию — как на телефоне: пишут на бумаге и сверяют с текстом задания, ввод ответа и «Прогресс» спрятаны
            Check(window.AnswerInputPanel.Visibility == Visibility.Collapsed && window.AnswerStatsGrid.Visibility == Visibility.Collapsed
                  && window.PaperHintText.Visibility == Visibility.Visible && window.ProgressTab.Visibility == Visibility.Collapsed
                  && window.AnswerInputButton.Content as string == Texts.T("Проверить вводом ▾"), "typed answer and Progress are hidden by default (paper mode)");
            SaveScreenshot(window, screenshots, "training-" + suffix);
            // Во время прослушивания: «Группа 3 из 10» и подсветка третьей группы среди точек
            window.ShowPlayingGroup(3);
            Check(window.GroupCounterText.Visibility == Visibility.Visible && window.GroupCounterText.Text == Texts.F("Группа {0} из {1}", 3, 10)
                  && window.AnswerDisplayText.Inlines.Count == 19, "group counter shows group 3 of 10 and highlights it: " + window.GroupCounterText.Text);
            SaveScreenshot(window, screenshots, "training-group-" + suffix);
            window.HidePlayingGroup();
            Check(window.GroupCounterText.Visibility == Visibility.Collapsed && window.AnswerDisplayText.Text.Contains('•'), "group counter hides after playback");
            Click(window.AnswerInputButton);
            Check(window.AnswerInputPanel.Visibility == Visibility.Visible && window.AnswerStatsGrid.Visibility == Visibility.Visible
                  && window.PaperHintText.Visibility == Visibility.Collapsed && window.ProgressTab.Visibility == Visibility.Visible
                  && window.AnswerInputButton.Content as string == Texts.T("Проверить вводом ▴"), "'Check by typing' shows the answer field and the Progress tab");
            Check(new SettingsService().Load().ShowAnswerInput, "typed answer choice is saved");

            // Что тренировать: четыре кнопки сверху управляют полным списком режимов в дополнительных настройках
            Check(window.AdvancedPanel.Visibility == Visibility.Collapsed && window.ModeHintText.Text.Length > 1, "advanced settings are collapsed, mode hint is shown");
            Click(window.ModeDigitsButton);
            Check(window.ContentModeCombo.SelectedIndex == (int)Domain.ContentMode.Digits && window.ModeDigitsButton.Style == window.FindResource("PrimaryButton"),
                "digits button selects the digits mode and is highlighted");
            Click(window.ModeBothButton);
            Check(window.ContentModeCombo.SelectedIndex == (int)Domain.ContentMode.LettersAndDigits, "letters and digits button restores the default mode");
            Click(window.AdvancedButton);
            Check(window.AdvancedPanel.Visibility == Visibility.Visible && window.AdvancedButton.Content as string == Texts.T("Дополнительные настройки ▴"), "advanced settings expand");
            Click(window.AdvancedButton);
            Check(window.AdvancedPanel.Visibility == Visibility.Collapsed, "advanced settings collapse again");
            Check(window.ReminderCheckBox.IsChecked == false && window.ReminderTimeCombo.Items.Count == 48 && window.ReminderTimeCombo.SelectedItem as string == "19:00",
                "Windows reminder is off by default at 19:00");
            // Уведомление без WinForms: значок через Shell_NotifyIcon, события приходят в главное окно
            try
            {
                using var reminder = new TrayReminder(window, () => { });
                reminder.Show("Morse Trainer", Texts.T("Пора потренироваться: пять минут азбуки Морзе."));
                Console.WriteLine("      tray notification shown");
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                // На раннере может не быть панели задач — тогда оболочка отказывает, это не ошибка программы
                Console.WriteLine("      tray notification unavailable: " + exception.Message);
            }
            Check(window.ExamLimitCombo.Items.Count == Domain.ExamSession.TimeLimitChoices.Count && window.ExamLimitCombo.Items[0] as string == Texts.T("без лимита"),
                "exam time limit choices are translated: " + window.ExamLimitCombo.Items[0]);

            // Показать ответ, ввести его и проверить: точность 100% и запись в истории
            Click(window.ToggleAnswerButton);
            var task = window.AnswerDisplayText.Text;
            Check(!task.Contains('•') && task.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 10, "answer is revealed: " + task);
            Check(window.ToggleAnswerButton.Content as string == Texts.T("Скрыть"), "toggle button switches to Hide");
            window.UserAnswerText.Text = task;
            Click(window.CheckAnswerButton);
            Check(window.AccuracyText.Text == "100%", "accuracy is 100%: " + window.AccuracyText.Text);
            Check(window.StatusBadgeText.Text == Texts.T("БЕЗ ОШИБОК"), "status badge after a perfect answer: " + window.StatusBadgeText.Text);
            Check(window.AttemptsText.Text == "1", "attempts counter is 1");
            Check(window.HistoryListView.Items.Count == 1, "history has one record");
            Check(window.ProgressSessionsText.Text == "1", "progress counts one session");

            // Пресет Фарнсворта двигает ползунки, подписи следуют за ними
            Click(window.FarnsworthButton);
            Check(window.SpeedSlider.Value >= 90 && (int)window.CharacterGapSlider.Value == 9 && (int)window.GroupGapSlider.Value == 21, "Farnsworth preset applied");
            Check(window.SpeedValueText.Text == Texts.F("{0} знаков/мин", (int)window.SpeedSlider.Value), "speed label follows the slider: " + window.SpeedValueText.Text);

            // Повторная генерация по кнопке
            Click(window.GenerateButton);
            WaitUntil(() => window.StatusBadgeText.Text == Texts.T("ГОТОВО") && window.GenerateButton.IsEnabled, "task regenerated");
            Check(window.AnswerDisplayText.Text.Contains('•'), "new task is hidden again");
            Check(window.AccuracyText.Text == "—", "accuracy resets for a new task");
            SaveScreenshot(window, screenshots, "training-typed-" + suffix);

            // Свёрнутая панель параметров: задание на всю ширину, «Новое задание» в шапке
            Click(window.TogglePanelButton);
            Check(window.TrainingPanel.Visibility == Visibility.Collapsed && window.QuickGenerateButton.Visibility == Visibility.Visible
                  && window.TogglePanelButton.Content as string == Texts.T("Параметры ▶"), "settings panel collapses");
            Click(window.QuickGenerateButton);
            WaitUntil(() => window.StatusBadgeText.Text == Texts.T("ГОТОВО") && window.GenerateButton.IsEnabled, "task generated from the header button");
            SaveScreenshot(window, screenshots, "training-compact-" + suffix);
            Click(window.TogglePanelButton);
            Check(window.TrainingPanel.Visibility == Visibility.Visible && window.QuickGenerateButton.Visibility == Visibility.Collapsed, "settings panel expands back");

            // Повтор сложных символов: ошибка в ответе попадает в историю, кнопка строит задание из неё
            Click(window.ToggleAnswerButton);
            var missedTask = window.AnswerDisplayText.Text;
            var missed = missedTask[0];
            window.UserAnswerText.Text = (missed == 'Б' ? 'А' : 'Б') + missedTask[1..];
            Click(window.CheckAnswerButton);
            Check(window.HistoryListView.Items.Count == 2, "history has two records after a wrong answer");
            Click(window.DrillButton);
            WaitUntil(() => window.StatusBadgeText.Text == Texts.T("ГОТОВО") && window.GenerateButton.IsEnabled, "drill task generated");
            Check(window.TaskMetaText.Text.StartsWith(Texts.T("Повтор сложных · "), StringComparison.Ordinal), "drill task meta: " + window.TaskMetaText.Text);
            Check(window.ResultDetailsText.Text.Contains(missed), $"drill lists the missed symbol {missed}: " + window.ResultDetailsText.Text);

            // Обучение: карточки, поиск, привязки шаблона карточки
            window.MainTabs.SelectedIndex = 1;
            DoEvents();
            Check(window.LearningItemsControl.Items.Count == 32, "learning shows 32 Russian letters (no Ё)");
            WaitUntil(() => FindChildren<TextBlock>(window.LearningItemsControl).Any(block => block.Text == "ай-даа"), "learning card bindings render the chant");
            window.LearningSearchText.Text = "ай";
            DoEvents();
            Check(window.LearningItemsControl.Items.Count is >= 1 and < 32, "search filters the cards: " + window.LearningItemsControl.Items.Count);
            window.LearningSearchText.Text = string.Empty;
            Check(window.LearningRussianButton.Style == window.FindResource("PrimaryButton") && window.LearningVoiceModeButton.Style == window.FindResource("PrimaryButton"),
                "learning: Russian letters and voice playback are highlighted by default");
            Click(window.LearningDigitsButton);
            Check(window.LearningItemsControl.Items.Count == 10 && window.LearningDigitsButton.Style == window.FindResource("PrimaryButton")
                  && window.LearningRussianButton.Style != window.FindResource("PrimaryButton"), "digits button shows 10 cards and is highlighted");
            Click(window.LearningChantModeButton);
            Check(window.LearningChantModeButton.Style == window.FindResource("PrimaryButton") && new SettingsService().Load().LearningAudioModeIndex == 0,
                "playback mode button switches and is saved");
            Click(window.LearningVoiceModeButton);
            Click(window.LearningRussianButton);
            Check(FindChildren<Button>(window.LearningItemsControl).Count(button => button.Content as string == "✎") == 32, "every learning card has the chant edit button");
            Check(window.CustomVoiceText.Text.StartsWith(Texts.T("Свой голос не добавлен: звучат встроенные напевы."), StringComparison.Ordinal),
                "custom voice status is shown: " + window.CustomVoiceText.Text);
            SaveScreenshot(window, screenshots, "learning-" + suffix);

            // Курс: «Начать курс» ставит настройки шага 1 и создаёт задание; верный ответ засчитывает шаг
            Check(window.CourseStartButton.Content as string == Texts.T("Начать курс") && window.CourseNextButton.Visibility == Visibility.Collapsed, "course is not started yet");
            Click(window.CourseStartButton);
            WaitUntil(() => window.StatusBadgeText.Text == Texts.T("ГОТОВО") && window.GenerateButton.IsEnabled, "course step task generated");
            Check(window.MainTabs.SelectedIndex == 0 && window.ContentModeCombo.SelectedIndex == (int)Domain.ContentMode.Koch
                  && (int)window.KochLevelSlider.Value == 2 && (int)window.SpeedSlider.Value == 60, "course step 1 settings are applied");
            Click(window.ToggleAnswerButton);
            window.UserAnswerText.Text = window.AnswerDisplayText.Text;
            Click(window.CheckAnswerButton);
            window.MainTabs.SelectedIndex = 1;
            DoEvents();
            Check(window.CourseNextButton.Visibility == Visibility.Visible && window.CourseNextButton.IsEnabled && window.CourseStatusText.Text.Contains('✓'),
                "a correct answer passes course step 1: " + window.CourseStatusText.Text);

            // На слух: вопрос, ответ, следующий символ сам; скорость, клавиатура, без автоперехода
            window.MainTabs.SelectedIndex = 2;
            DoEvents();
            Check(window.QuizAnswer0Button.IsEnabled == false && window.QuizRussianButton.Style == window.FindResource("PrimaryButton")
                  && window.QuizAutoNextCheckBox.IsChecked == true && window.QuizSpeedValueText.Text == Texts.F("{0} знаков/мин", 45), "quiz starts idle at 45 cpm with auto-next");
            var answerButtons = new[] { window.QuizAnswer0Button, window.QuizAnswer1Button, window.QuizAnswer2Button, window.QuizAnswer3Button };
            string QuizAnswers() => string.Join(" ", answerButtons.Select(button => button.Content as string));
            Click(window.QuizNewButton);
            WaitUntil(() => window.QuizStatusText.Text == Texts.T("Какой символ прозвучал?"), "quiz question after the signal");
            Check(answerButtons.All(button => button.IsEnabled && button.Content is string { Length: 1 }) && window.QuizRepeatButton.IsEnabled, "four answers and repeat are enabled: " + QuizAnswers());
            var firstSymbol = (char)window.QuizAnswer0Button.Tag;
            Check(window.FindQuizAnswerButton(char.ToLowerInvariant(firstSymbol)) == window.QuizAnswer0Button, "typing the symbol (any case) picks its answer button");
            var firstAnswers = QuizAnswers();
            Click(window.QuizAnswer0Button);
            // Верный вариант подсвечен мятным, ошибочный выбранный — красным (по-английски оба текста начинаются с «Correct:»)
            var answeredCorrectly = ReferenceEquals(window.QuizAnswer0Button.Background, window.FindResource("PrimaryBrush"));
            Check(answeredCorrectly || ReferenceEquals(window.QuizAnswer0Button.Background, window.FindResource("DangerBrush")), "the chosen answer is marked");
            Check(answerButtons.Count(button => ReferenceEquals(button.Background, window.FindResource("PrimaryBrush"))) == 1, "exactly one answer is marked as right");
            Check(window.QuizStatusText.Text.StartsWith(Texts.T("Верно: {0} — {1}").Split('{')[0], StringComparison.Ordinal)
                  || window.QuizStatusText.Text.StartsWith(Texts.T("Правильно: {0} — {1}").Split('{')[0], StringComparison.Ordinal),
                "answer result is shown: " + window.QuizStatusText.Text);
            Check(window.QuizScoreText.Text == Texts.F("Результат: {0} / {1}", answeredCorrectly ? 1 : 0, 1), "score counts the answer: " + window.QuizScoreText.Text);
            WaitUntil(() => window.QuizStatusText.Text == Texts.T("Какой символ прозвучал?") && QuizAnswers() != firstAnswers, "next quiz symbol plays by itself after the answer", 15);
            window.QuizSpeedSlider.Value = 83;
            DoEvents();
            Check(window.QuizSpeedValueText.Text == Texts.F("{0} знаков/мин", 85), "quiz speed snaps to 5: " + window.QuizSpeedValueText.Text);
            window.QuizAutoNextCheckBox.IsChecked = false;
            DoEvents();
            Click(window.QuizDigitsButton);
            Check(window.QuizAnswer0Button.IsEnabled == false && window.QuizDigitsButton.Style == window.FindResource("PrimaryButton"), "digits set resets the question");
            Click(window.QuizNewButton);
            WaitUntil(() => window.QuizStatusText.Text == Texts.T("Какой символ прозвучал?"), "digit question after the signal");
            Check(answerButtons.All(button => button.Content is string text && char.IsDigit(text[0])), "digit answers only: " + QuizAnswers());
            Click(window.QuizAnswer1Button);
            var answered = window.QuizStatusText.Text;
            Thread.Sleep(2600);
            DoEvents();
            Check(window.QuizStatusText.Text == answered && answered.Length > 3, "without auto-next the answer stays on screen: " + answered);
            SaveScreenshot(window, screenshots, "quiz-" + suffix);
            var saved = new SettingsService().Load();
            Check(saved.QuizSpeed == 85 && !saved.QuizAutoNext && saved.QuizAlphabetIndex == 3 && saved.QuizTotal == 2 && saved.LastPracticeAt is not null,
                $"quiz settings are saved: {saved.QuizSpeed} {saved.QuizAutoNext} {saved.QuizAlphabetIndex} {saved.QuizTotal}");
            Click(window.QuizResetButton);
            Check(window.QuizScoreText.Text == Texts.F("Результат: {0} / {1}", 0, 0), "quiz score resets");

            // Передача: режим задания, новое задание, стереть, очистить (ключ не нажимаем — без звука)
            window.MainTabs.SelectedIndex = 3;
            DoEvents();
            window.KeyerModeCombo.SelectedIndex = 1;
            DoEvents();
            var keyerTarget = string.Concat(window.KeyerTargetText.Inlines.OfType<System.Windows.Documents.Run>().Select(run => run.Text));
            Check(keyerTarget.Length >= 5, "keyer task generated: " + keyerTarget);
            Check(window.KeyerNewTaskButton.Visibility == Visibility.Visible && window.KeyerCheckButton.Visibility == Visibility.Visible, "keyer task buttons are visible");
            Click(window.KeyerNewTaskButton);
            Click(window.KeyerBackspaceButton);
            Click(window.KeyerClearButton);
            Check(window.KeyerTimingText.Text.Length > 0 && !window.KeyerTimingText.Text.Contains("loc:"), "keyer timing label is translated");
            SaveScreenshot(window, screenshots, "keyer-" + suffix);

            // Прогресс (виден, раз включён ввод ответа): сводка и таблица с привязками строк
            window.MainTabs.SelectedIndex = 4;
            DoEvents();
            WaitUntil(() => FindChildren<TextBlock>(window.HistoryListView).Any(block => block.Text == "100%"), "history row bindings render the accuracy");
            Check(window.ProgressSessionsText.Text == "3" && window.ProgressBestText.Text == "100%", "progress summary shows three sessions, best 100%");
            Check(window.DailyBarsPanel.Children.Count == 1, "daily bars show one day");
            Check(window.ExamSeriesText.Text == Texts.T("Экзаменов пока нет"), "exam series is empty: " + window.ExamSeriesText.Text);
            Check(window.StreakText.Text == Texts.F("Дней подряд: {0} · рекорд {1}", 1, 1), "streak counts today: " + window.StreakText.Text);
            Check(window.DailyGoalCombo.SelectedIndex > 0 && window.GoalText.Text.Length > 0 && window.GoalProgress.Visibility == Visibility.Visible,
                "daily goal is set by default: " + window.GoalText.Text);
            Check(window.SpeedBarsPanel.Children.Count == 1, "speed bars show one day");
            Check(window.ExportHistoryButton.Content as string == Texts.T("Экспорт истории…") && window.ImportHistoryButton.IsEnabled, "history transfer buttons are translated");
            var columnsWidth = ((GridView)window.HistoryListView.View).Columns.Sum(column => column.ActualWidth);
            Check(columnsWidth <= window.HistoryListView.ActualWidth, $"history columns fit without horizontal scroll: {columnsWidth:0} of {window.HistoryListView.ActualWidth:0}");
            SaveScreenshot(window, screenshots, "progress-" + suffix);

            window.MainTabs.SelectedIndex = 0;
            DoEvents();
        }
        finally
        {
            window.Close();
            DoEvents();
        }
    }

    private static void ExerciseSymbolSelection()
    {
        Texts.Apply(AppLanguage.Russian);
        var window = new SymbolSelectionWindow("АБВ");
        try
        {
            window.Show();
            DoEvents();
            Check(window.SelectionCountText.Text == Texts.F("Выбрано: {0}", 3), "three symbols are preselected: " + window.SelectionCountText.Text);
            Check(window.RussianSymbolsPanel.Children.Count == 32 && window.LatinSymbolsPanel.Children.Count == 26
                  && window.DigitSymbolsPanel.Children.Count == 10 && window.PunctuationSymbolsPanel.Children.Count >= 10, "symbol panels are filled");
            var first = window.RussianSymbolsPanel.Children.OfType<ToggleButton>().First();
            Check(first.IsChecked == true && first.Content as string == "А", "first Russian toggle is А and checked");
            first.IsChecked = false;
            DoEvents();
            Check(window.SelectionCountText.Text == Texts.F("Выбрано: {0}", 2), "unchecking updates the counter: " + window.SelectionCountText.Text);
        }
        finally
        {
            window.Close();
            DoEvents();
        }
    }

    /// <summary>Окно «Изменить напев» собирается кодом: проверяем разметку, ресурсы темы и проверку числа слогов.</summary>
    private static void ExerciseChantEditor()
    {
        Texts.Apply(AppLanguage.Russian);
        var window = new ChantEditorWindow(Domain.LearningCatalog.Find('А')!);
        try
        {
            window.Show();
            DoEvents();
            var box = FindChildren<TextBox>(window).Single();
            var save = FindChildren<Button>(window).Single(button => button.Content as string == Texts.T("Сохранить"));
            Check(box.Text == "ай-даа" && save.IsEnabled, "editor starts with the current chant: " + box.Text);
            box.Text = "раз-два-три";
            DoEvents();
            Check(!save.IsEnabled, "wrong syllable count disables Save");
            box.Text = "Ать — Даа";
            DoEvents();
            Check(save.IsEnabled, "a valid chant enables Save");
        }
        finally
        {
            window.Close();
            DoEvents();
        }
    }

    // ---------- вспомогательное ----------

    private static void Run(string name, Action action)
    {
        var before = Failures.Count;
        try
        {
            action();
            if (_dispatcherException is not null)
            {
                throw new InvalidOperationException("Dispatcher exception: " + _dispatcherException);
            }

            Console.WriteLine(Failures.Count == before ? $"PASS  {name}" : $"FAIL  {name}: {Failures.Count - before} check(s) failed");
        }
        catch (Exception exception)
        {
            Failures.Add($"{name}: {exception.Message}");
            Console.WriteLine($"FAIL  {name}: {exception}");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            Failures.Add(message);
            Console.WriteLine($"      check failed: {message}");
        }
    }

    private static void Click(ButtonBase button)
    {
        Check(button.IsEnabled, "button is enabled before click: " + button.Name);
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        DoEvents();
    }

    /// <summary>Прокручивает очередь диспетчера до фоновых приоритетов: срабатывают Loaded, привязки и продолжения await.</summary>
    private static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void WaitUntil(Func<bool> condition, string description, int timeoutSeconds = 40)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (_dispatcherException is not null)
            {
                throw new InvalidOperationException($"Dispatcher exception while waiting for {description}: {_dispatcherException}");
            }

            if (stopwatch.Elapsed.TotalSeconds > timeoutSeconds)
            {
                throw new TimeoutException("Timed out waiting for " + description);
            }

            DoEvents();
            Thread.Sleep(15);
        }
    }

    private static IEnumerable<T> FindChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var nested in FindChildren<T>(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>Снимок клиентской области окна в PNG: фон окна плюс содержимое, без рамки Windows.</summary>
    private static void SaveScreenshot(Window window, string? directory, string name)
    {
        if (string.IsNullOrWhiteSpace(directory) || window.Content is not FrameworkElement content)
        {
            return;
        }

        try
        {
            DoEvents();
            var offset = content.TranslatePoint(new Point(0, 0), window);
            var width = (int)Math.Ceiling(content.ActualWidth + offset.X * 2);
            var height = (int)Math.Ceiling(content.ActualHeight + offset.Y * 2);
            if (width <= 0 || height <= 0)
            {
                return;
            }

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
                context.DrawRectangle(new VisualBrush(content), null, new Rect(offset, new Size(content.ActualWidth, content.ActualHeight)));
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(directory);
            using var stream = File.Create(Path.Combine(directory, name + ".png"));
            encoder.Save(stream);
            Console.WriteLine($"      screenshot: {name}.png ({width}×{height})");
        }
        catch (Exception exception)
        {
            // Скриншот — бонус для README, его сбой не должен ронять проверку интерфейса
            Console.WriteLine($"      screenshot {name} skipped: {exception.Message}");
        }
    }

    private static void ResetData(bool deleteDirectory)
    {
        try
        {
            if (deleteDirectory)
            {
                if (Directory.Exists(_dataDirectory))
                {
                    Directory.Delete(_dataDirectory, recursive: true);
                }

                return;
            }

            Directory.CreateDirectory(_dataDirectory);
            foreach (var file in Directory.GetFiles(_dataDirectory))
            {
                File.Delete(file);
            }
        }
        catch
        {
            // Временная папка не критична
        }
    }
}
