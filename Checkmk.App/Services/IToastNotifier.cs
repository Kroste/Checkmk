using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NLog;

namespace Checkmk.App.Services;

/// <summary>Zeigt eine native OS-Benachrichtigung (Toast) an.</summary>
public interface IToastNotifier
{
    void Notify(string title, string body);
}

public static class ToastNotifierFactory
{
    public static IToastNotifier Create()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsToastNotifier();
        if (OperatingSystem.IsLinux())
            return new LinuxToastNotifier();
        return new NullToastNotifier();
    }
}

/// <summary>Kein OS-Support -> stiller Fallback (Tray-Signal bleibt).</summary>
public sealed class NullToastNotifier : IToastNotifier
{
    public void Notify(string title, string body) { }
}

/// <summary>Linux: nutzt das Standard-CLI notify-send (KDE/GNOME, Bazzite vorhanden).</summary>
public sealed class LinuxToastNotifier : IToastNotifier
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public void Notify(string title, string body)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "notify-send",
                ArgumentList = { "-a", "Checkmk Cockpit", title, body },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "notify-send nicht verfuegbar.");
        }
    }
}

/// <summary>
/// Windows: Balloon-/Toast-Notification via Shell_NotifyIcon an einem eigenen,
/// versteckten Tray-Eintrag (message-only Fenster). Braucht kein WinRT-Paket.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsToastNotifier : IToastNotifier, IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private const int WM_APP = 0x8000;
    private const uint NIM_ADD = 0x0, NIM_MODIFY = 0x1, NIM_DELETE = 0x2;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_STATE = 0x8, NIF_INFO = 0x10;
    private const uint NIS_HIDDEN = 0x1;
    private const uint NIIF_INFO = 0x1;
    private const int IDI_INFORMATION = 0x7F04;

    private readonly IntPtr _hwnd;
    private readonly uint _id = 1;
    private bool _added;

    public WindowsToastNotifier()
    {
        try
        {
            _hwnd = CreateMessageWindow();
            var data = BuildData(NIF_ICON | NIF_TIP | NIF_STATE | NIF_MESSAGE);
            data.uCallbackMessage = WM_APP;
            data.dwState = NIS_HIDDEN;
            data.dwStateMask = NIS_HIDDEN;
            data.hIcon = LoadIcon(IntPtr.Zero, IDI_INFORMATION);
            data.szTip = "Checkmk Cockpit";
            _added = Shell_NotifyIcon(NIM_ADD, ref data);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "WindowsToastNotifier konnte nicht initialisiert werden.");
        }
    }

    public void Notify(string title, string body)
    {
        if (!_added) return;
        try
        {
            var data = BuildData(NIF_INFO);
            data.szInfoTitle = Trunc(title, 63);
            data.szInfo = Trunc(body, 255);
            data.dwInfoFlags = NIIF_INFO;
            Shell_NotifyIcon(NIM_MODIFY, ref data);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Shell_NotifyIcon Balloon fehlgeschlagen.");
        }
    }

    private NOTIFYICONDATA BuildData(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = _id,
        uFlags = flags
    };

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];

    private static IntPtr CreateMessageWindow()
    {
        var className = "CheckmkCockpitToast_" + Environment.ProcessId;
        var wc = new WNDCLASS
        {
            lpfnWndProc = DefWindowProc,
            lpszClassName = className,
            hInstance = GetModuleHandle(null)
        };
        RegisterClass(ref wc);
        // HWND_MESSAGE = new IntPtr(-3): message-only window, kein sichtbares Fenster
        return CreateWindowEx(0, className, className, 0, 0, 0, 0, 0,
            new IntPtr(-3), IntPtr.Zero, wc.hInstance, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = BuildData(0);
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }
        if (_hwnd != IntPtr.Zero)
            DestroyWindow(_hwnd);
    }

    // --- P/Invoke ---

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    private static readonly WndProcDelegate DefWindowProc = (h, m, w, l) => DefWindowProcW(h, m, w, l);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName,
        uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu,
        IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, int lpIconName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
