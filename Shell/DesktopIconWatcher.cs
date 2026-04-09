using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DeskMirror.Native;

namespace DeskMirror.Shell;

public class DesktopIconWatcher : IDisposable
{
    private const uint WM_SHNOTIFY = 0x0401;

    private uint _notifyId;
    private HwndSource? _hwndSource;
    private DispatcherTimer? _positionTimer;
    private bool _disposed;

    public event Action? DesktopChanged;

    public void Start(IntPtr ownerHwnd)
    {
        RegisterShellNotify(ownerHwnd);
        StartPositionPolling();
    }

    private void RegisterShellNotify(IntPtr hwnd)
    {
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(ShellNotifyProc);

        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        int hr = NativeMethods.SHParseDisplayName(desktopPath, IntPtr.Zero, out var pidl, 0, out _);

        if (hr != 0 || pidl == IntPtr.Zero)
            return;

        try
        {
            var entry = new NativeMethods.SHChangeNotifyEntry
            {
                pidl = pidl,
                fRecursive = false
            };

            _notifyId = NativeMethods.SHChangeNotifyRegister(
                hwnd,
                NativeMethods.SHCNRF_SHELLLEVEL | NativeMethods.SHCNRF_INTERRUPTLEVEL | NativeMethods.SHCNRF_NEWDELIVERY,
                NativeMethods.SHCNE_ALLEVENTS,
                WM_SHNOTIFY,
                1,
                ref entry);
        }
        finally
        {
            NativeMethods.ILFree(pidl);
        }
    }

    private void StartPositionPolling()
    {
        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };
        _positionTimer.Tick += (_, _) => DesktopChanged?.Invoke();
        _positionTimer.Start();
    }

    private IntPtr ShellNotifyProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SHNOTIFY)
        {
            Application.Current?.Dispatcher.InvokeAsync(() => DesktopChanged?.Invoke(),
                DispatcherPriority.Background);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _positionTimer?.Stop();
        _positionTimer = null;

        if (_notifyId != 0)
        {
            NativeMethods.SHChangeNotifyDeregister(_notifyId);
            _notifyId = 0;
        }

        _hwndSource?.RemoveHook(ShellNotifyProc);
        _hwndSource = null;

        GC.SuppressFinalize(this);
    }
}
