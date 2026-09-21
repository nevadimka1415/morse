using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using MorseTrainer.Localization;
using MorseTrainer.Services;

namespace MorseTrainer;

public partial class App : Application
{
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MorseTrainer",
        "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        // Язык нужно выбрать до разбора XAML главного окна
        var settings = new SettingsService().Load();
        Texts.Apply((AppLanguage)Math.Clamp(settings.LanguageIndex, 0, 2), CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        DispatcherUnhandledException += App_OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_OnUnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_OnUnobservedTaskException;
        base.OnStartup(e);
    }

    private void App_OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog("Dispatcher exception", e.Exception);
        e.Handled = true;
        MessageBox.Show(
            $"Morse Trainer encountered an error and must close.\n\nDiagnostic file:\n{CrashLogPath}\n\n{e.Exception.Message}",
            "Morse Trainer",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Shutdown(1);
    }

    private static void CurrentDomain_OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        WriteCrashLog("Unhandled application exception", e.ExceptionObject as Exception);
    }

    private static void TaskScheduler_OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog("Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    private static void WriteCrashLog(string source, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            var report = new StringBuilder()
                .AppendLine("Morse Trainer crash report")
                .AppendLine($"Time: {DateTime.Now:O}")
                .AppendLine($"Source: {source}")
                .AppendLine($"Version: {typeof(App).Assembly.GetName().Version}")
                .AppendLine($"OS: {Environment.OSVersion}")
                .AppendLine($".NET: {Environment.Version}")
                .AppendLine()
                .AppendLine(exception?.ToString() ?? "Unknown exception")
                .ToString();
            File.WriteAllText(CrashLogPath, report, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            Console.Error.WriteLine(report);
        }
        catch
        {
            // Error reporting must never trigger another application crash.
        }
    }
}
