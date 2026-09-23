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
            Run("Main window (English)", () =>
            {
                ResetData(deleteDirectory: false);
                Texts.Apply(AppLanguage.English);
                ExerciseMainWindow(new MainWindow(), screenshots, "en");
            });
            Run("Symbol selection window", ExerciseSymbolSelection);
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
            Check(window.MainTabs.Items.Count == 4, "window has four tabs");
            foreach (var item in window.MainTabs.Items.OfType<TabItem>())
            {
                Check(item.Header is string header && header.Length > 0 && !header.Contains("loc:"), "tab header is translated: " + item.Header);
            }

            Check(((TabItem)window.MainTabs.Items[0]).Header as string == Texts.T("Тренировка"), "first tab header follows the language");
            Check(window.GenerateButton.Content as string == Texts.T("Сгенерировать задание"), "generate button text follows the language");
            Check(window.TaskMetaText.Text.Contains('×'), "task meta shows the group count: " + window.TaskMetaText.Text);
            Check(window.AnswerDisplayText.Text.Contains('•') && window.AnswerDisplayText.Text.Contains(' '), "answer is hidden as bullets");
            Check(window.PlayButton.IsEnabled && window.CheckAnswerButton.IsEnabled && window.UserAnswerText.IsEnabled, "task controls are enabled");
            Check(window.ExamPlaybacksCombo.Items.Count == 3 && window.ExamPlaybacksCombo.SelectedIndex == 0, "exam playbacks default to one");
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
            SaveScreenshot(window, screenshots, "training-" + suffix);

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
            Check(window.LearningItemsControl.Items.Count == 33, "learning shows 33 Russian letters");
            WaitUntil(() => FindChildren<TextBlock>(window.LearningItemsControl).Any(block => block.Text == "ай-даа"), "learning card bindings render the chant");
            window.LearningSearchText.Text = "ай";
            DoEvents();
            Check(window.LearningItemsControl.Items.Count is >= 1 and < 33, "search filters the cards: " + window.LearningItemsControl.Items.Count);
            window.LearningSearchText.Text = string.Empty;
            window.LearningAlphabetCombo.SelectedIndex = 3;
            DoEvents();
            Check(window.LearningItemsControl.Items.Count == 10, "digits section has 10 cards");
            window.LearningAlphabetCombo.SelectedIndex = 0;
            DoEvents();
            SaveScreenshot(window, screenshots, "learning-" + suffix);

            // Передача: режим задания, новое задание, стереть, очистить (ключ не нажимаем — без звука)
            window.MainTabs.SelectedIndex = 2;
            DoEvents();
            window.KeyerModeCombo.SelectedIndex = 1;
            DoEvents();
            Check(window.KeyerTargetText.Text.Length >= 5, "keyer task generated: " + window.KeyerTargetText.Text);
            Check(window.KeyerNewTaskButton.Visibility == Visibility.Visible && window.KeyerCheckButton.Visibility == Visibility.Visible, "keyer task buttons are visible");
            Click(window.KeyerNewTaskButton);
            Click(window.KeyerBackspaceButton);
            Click(window.KeyerClearButton);
            Check(window.KeyerTimingText.Text.Length > 0 && !window.KeyerTimingText.Text.Contains("loc:"), "keyer timing label is translated");
            SaveScreenshot(window, screenshots, "keyer-" + suffix);

            // Прогресс: сводка и таблица с привязками строк
            window.MainTabs.SelectedIndex = 3;
            DoEvents();
            WaitUntil(() => FindChildren<TextBlock>(window.HistoryListView).Any(block => block.Text == "100%"), "history row bindings render the accuracy");
            Check(window.ProgressSessionsText.Text == "2" && window.ProgressBestText.Text == "100%", "progress summary shows two sessions, best 100%");
            Check(window.DailyBarsPanel.Children.Count == 1, "daily bars show one day");
            Check(window.ExamSeriesText.Text == Texts.T("Экзаменов пока нет"), "exam series is empty: " + window.ExamSeriesText.Text);
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
            Check(window.RussianSymbolsPanel.Children.Count == 33 && window.LatinSymbolsPanel.Children.Count == 26
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
