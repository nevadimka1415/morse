using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MorseTrainer.Services;

/// <summary>
/// Уведомление Windows через значок в области уведомлений: на Windows 10/11 подсказка значка показывается
/// обычным системным уведомлением. Значок виден только пока уведомление на экране. Щелчок открывает окно.
/// Напрямую через Shell_NotifyIcon, без WinForms: NotifyIcon тянул в самодостаточную сборку ~7 МБ.
/// События значка (щелчок, закрытие уведомления) приходят сообщением в главное окно программы.
/// </summary>
public sealed class TrayReminder : IDisposable
{
    private const int NimAdd = 0;
    private const int NimModify = 1;
    private const int NimDelete = 2;
    private const int NifMessage = 0x1;
    private const int NifIcon = 0x2;
    private const int NifTip = 0x4;
    private const int NifInfo = 0x10;
    private const int NiifInfo = 0x1;
    private const int WmLButtonUp = 0x0202;
    private const int WmApp = 0x8000;
    private const int NinBalloonHide = 0x0403;
    private const int NinBalloonTimeout = 0x0404;
    private const int NinBalloonUserClick = 0x0405;
    private const int CallbackMessage = WmApp + 1;
    private const int IconId = 1;
    private const int IdiInformation = 32516;

    private readonly Action _onClick;
    private readonly HwndSource _window;
    private readonly IntPtr _icon;
    private readonly bool _ownsIcon;
    private bool _added;
    private bool _disposed;

    public TrayReminder(Window owner, Action onClick)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(onClick);
        _onClick = onClick;
        _window = HwndSource.FromHwnd(new WindowInteropHelper(owner).EnsureHandle())
            ?? throw new InvalidOperationException("У окна нет HwndSource");
        _window.AddHook(WndProc);
        (_icon, _ownsIcon) = LoadIcon();
    }

    public void Show(string title, string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var data = CreateData(NifMessage | NifIcon | NifTip);
        if (!_added)
        {
            if (!ShellNotifyIcon(NimAdd, ref data))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Shell_NotifyIcon: значок не добавлен");
            }

            _added = true;
        }

        data.uFlags = NifInfo;
        data.szInfoTitle = Truncate(title, 63);
        data.szInfo = Truncate(text, 255);
        data.dwInfoFlags = NiifInfo;
        data.uTimeoutOrVersion = 15000;
        if (!ShellNotifyIcon(NimModify, ref data))
        {
            Hide();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Shell_NotifyIcon: уведомление не показано");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Hide();
        _window.RemoveHook(WndProc);
        if (_ownsIcon)
        {
            DestroyIcon(_icon);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != CallbackMessage)
        {
            return IntPtr.Zero;
        }

        // Без NOTIFYICON_VERSION_4 в lParam приходит само событие значка
        switch ((int)lParam.ToInt64())
        {
            case NinBalloonUserClick:
                Hide();
                _onClick();
                break;
            case WmLButtonUp:
                _onClick();
                break;
            case NinBalloonHide:
            case NinBalloonTimeout:
                Hide();
                break;
        }

        handled = true;
        return IntPtr.Zero;
    }

    private void Hide()
    {
        if (!_added)
        {
            return;
        }

        var data = CreateData(0);
        ShellNotifyIcon(NimDelete, ref data);
        _added = false;
    }

    private NotifyIconData CreateData(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = _window.Handle,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = "Morse Trainer",
        szInfo = string.Empty,
        szInfoTitle = string.Empty
    };

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    private static (IntPtr Icon, bool Owned) LoadIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (path is not null && File.Exists(path) && ExtractIconEx(path, 0, IntPtr.Zero, out var small, 1) > 0 && small != IntPtr.Zero)
            {
                return (small, true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Нет доступа к exe — берём системный значок
        }

        // Системный значок общий: освобождать его не нужно
        return (LoadIconW(IntPtr.Zero, new IntPtr(IdiInformation)), false);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(int message, ref NotifyIconData data);

    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconEx(string file, int index, IntPtr large, out IntPtr small, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIconW(IntPtr instance, IntPtr name);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
