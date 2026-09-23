using System.Drawing;
using System.IO;
using Forms = System.Windows.Forms;

namespace MorseTrainer.Services;

/// <summary>
/// Уведомление Windows через значок в области уведомлений: на Windows 10/11 подсказка значка показывается
/// обычным системным уведомлением. Значок виден только пока уведомление на экране. Щелчок открывает окно.
/// </summary>
public sealed class TrayReminder : IDisposable
{
    private readonly Forms.NotifyIcon _icon;

    public TrayReminder(Action onClick)
    {
        ArgumentNullException.ThrowIfNull(onClick);
        _icon = new Forms.NotifyIcon { Icon = LoadIcon(), Text = "Morse Trainer", Visible = false };
        _icon.BalloonTipClicked += (_, _) => onClick();
        _icon.Click += (_, _) => onClick();
        _icon.BalloonTipClosed += (_, _) => _icon.Visible = false;
    }

    public void Show(string title, string text)
    {
        _icon.Visible = true;
        _icon.ShowBalloonTip(15000, title, text, Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private static Icon LoadIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (path is not null && File.Exists(path) && Icon.ExtractAssociatedIcon(path) is { } icon)
            {
                return icon;
            }
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Нет доступа к exe — берём системный значок
        }

        return SystemIcons.Information;
    }
}
