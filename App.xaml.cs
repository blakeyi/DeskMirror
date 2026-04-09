using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using DesktopIconMirror.Monitor;
using DesktopIconMirror.Native;
using DesktopIconMirror.Services;
using DesktopIconMirror.Shell;
using DesktopIconMirror.ViewModels;
using DesktopIconMirror.Views;
using Hardcodet.Wpf.TaskbarNotification;

namespace DesktopIconMirror;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private MirrorWindow? _mirrorWindow;
    private MirrorViewModel? _viewModel;
    private DesktopIconWatcher? _watcher;
    private DispatcherTimer? _watchdogTimer;
    private AppSettings _settings = new();
    private bool _isRefreshing;
    private bool _userHidden;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

#if DEBUG
        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
        Trace.Listeners.Add(new TextWriterTraceListener(logPath));
        Trace.AutoFlush = true;
        Trace.WriteLine($"=== App started at {DateTime.Now} ===");
#endif

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Trace.WriteLine($"[FATAL] UnhandledException: {args.ExceptionObject}");
            Trace.Flush();
        };

        _settings = SettingsService.Load();
        InitializeTrayIcon();
        InitializeMirrorWindow();
        StartWatchdog();
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            Icon = LoadAppIcon(),
            ToolTipText = "DeskMirror"
        };

        var menu = new ContextMenu();

        var toggleItem = new MenuItem { Header = "显示/隐藏(_S)" };
        toggleItem.Click += TrayMenu_ToggleVisibility;
        menu.Items.Add(toggleItem);

        var refreshItem = new MenuItem { Header = "刷新图标(_R)" };
        refreshItem.Click += TrayMenu_Refresh;
        menu.Items.Add(refreshItem);

        menu.Items.Add(new Separator());

        var settingsItem = new MenuItem { Header = "设置(_T)" };
        settingsItem.Click += TrayMenu_Settings;
        menu.Items.Add(settingsItem);

        menu.Items.Add(new Separator());

        var autoStartItem = new MenuItem
        {
            Header = "开机自启(_A)",
            IsCheckable = true,
            IsChecked = _settings.AutoStart
        };
        autoStartItem.Click += TrayMenu_AutoStart;
        menu.Items.Add(autoStartItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "退出(_X)" };
        exitItem.Click += TrayMenu_Exit;
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenu = menu;
        _trayIcon.TrayMouseDoubleClick += (_, _) => TrayMenu_ToggleVisibility(this, new RoutedEventArgs());
    }

    private void InitializeMirrorWindow()
    {
        var primary = MonitorDetector.GetPrimaryMonitor();
        var allMonitors = MonitorDetector.GetAllMonitors();

        MonitorData? secondary = null;
        if (!string.IsNullOrEmpty(_settings.TargetMonitorDeviceName))
        {
            secondary = allMonitors.FirstOrDefault(
                m => m.DeviceName == _settings.TargetMonitorDeviceName && !m.IsPrimary);
        }
        secondary ??= allMonitors.FirstOrDefault(m => !m.IsPrimary);

        if (primary == null || secondary == null)
        {
            _trayIcon?.ShowBalloonTip("DeskMirror",
                "未检测到副屏，程序将在托盘等待。\n接入副屏后请右键刷新。",
                BalloonIcon.Info);
            return;
        }

        _viewModel = new MirrorViewModel();
        _mirrorWindow = new MirrorWindow { DataContext = _viewModel };

        double dpiScale = GetDpiScaleForPoint(secondary.Bounds.Left, secondary.Bounds.Top);
        DesktopIconEnumerator.SetDpiScale(dpiScale);
        _viewModel.Initialize(primary, secondary, dpiScale);
        _mirrorWindow.PlaceOnMonitor(secondary, dpiScale);
        _mirrorWindow.Show();

        Task.Run(() => _viewModel.RefreshIcons());
        StartWatcher();
    }

    private void StartWatcher()
    {
        if (_mirrorWindow == null) return;

        _watcher?.Dispose();
        _watcher = new DesktopIconWatcher();
        _watcher.DesktopChanged += OnDesktopChanged;

        var hwnd = new WindowInteropHelper(_mirrorWindow).Handle;
        _watcher.Start(hwnd);
    }

    private void StartWatchdog()
    {
        _watchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _watchdogTimer.Tick += (_, _) =>
        {
            if (_userHidden || _mirrorWindow == null) return;

            if (!IsWindowAlive(_mirrorWindow))
            {
                Trace.WriteLine("[Watchdog] Window dead, recreating...");
                _mirrorWindow = null;
                _watcher?.Dispose();
                InitializeMirrorWindow();
                return;
            }

            bool desktopNow = IsDesktopForeground();
            bool wasBefore = _mirrorWindow.DesktopShown;

            if (desktopNow && !wasBefore)
            {
                Trace.WriteLine("[Watchdog] Desktop shown (Win+D), popping window above desktop");
                _mirrorWindow.DesktopShown = true;
                _mirrorWindow.BringAboveDesktop();
            }
            else if (!desktopNow && wasBefore)
            {
                Trace.WriteLine("[Watchdog] Desktop hidden, sending window back to bottom");
                _mirrorWindow.DesktopShown = false;
            }
        };
        _watchdogTimer.Start();
    }

    private static bool IsDesktopForeground()
    {
        var fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;

        var sb = new System.Text.StringBuilder(256);
        NativeMethods.GetClassName(fg, sb, 256);
        string cls = sb.ToString();
        return cls is "Progman" or "WorkerW";
    }

    private static bool IsWindowAlive(Window w)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            return hwnd != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    private void OnDesktopChanged()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;

        Task.Run(() =>
        {
            try
            {
                _viewModel?.RefreshIcons();
            }
            finally
            {
                _isRefreshing = false;
            }
        });
    }

    private static double GetDpiScaleForPoint(int x, int y)
    {
        try
        {
            var screen = System.Windows.Forms.Screen.AllScreens
                .FirstOrDefault(s => s.Bounds.Contains(x + 1, y + 1));
            if (screen == null) return 1.0;

            using var g = Graphics.FromHwnd(IntPtr.Zero);
            return g.DpiX / 96.0;
        }
        catch
        {
            return 1.0;
        }
    }

    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/icon.png");
            var stream = Application.GetResourceStream(uri)?.Stream;
            if (stream != null)
            {
                using var bmp = new System.Drawing.Bitmap(stream);
                return System.Drawing.Icon.FromHandle(bmp.GetHicon());
            }
        }
        catch { }
        return SystemIcons.Application;
    }

    private void TrayMenu_ToggleVisibility(object sender, RoutedEventArgs e)
    {
        if (_mirrorWindow == null || !IsWindowAlive(_mirrorWindow))
        {
            _userHidden = false;
            _mirrorWindow = null;
            _watcher?.Dispose();
            InitializeMirrorWindow();
            return;
        }

        if (_mirrorWindow.IsVisible)
        {
            _userHidden = true;
            _mirrorWindow.Hide();
        }
        else
        {
            _userHidden = false;
            _mirrorWindow.Show();
            _mirrorWindow.WindowState = WindowState.Normal;
        }
    }

    private void TrayMenu_Refresh(object sender, RoutedEventArgs e)
    {
        if (_mirrorWindow == null)
            InitializeMirrorWindow();
        else
            OnDesktopChanged();
    }

    private void TrayMenu_Settings(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new Views.SettingsWindow(_settings);
        settingsWindow.ShowDialog();

        if (settingsWindow.SettingsChanged)
        {
            _settings = SettingsService.Load();
            _watcher?.Dispose();
            _mirrorWindow?.Close();
            _mirrorWindow = null;
            InitializeMirrorWindow();
        }
    }

    private void TrayMenu_AutoStart(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi)
        {
            _settings.AutoStart = mi.IsChecked;
            SettingsService.SetAutoStart(mi.IsChecked);
            SettingsService.Save(_settings);
        }
    }

    private void TrayMenu_Exit(object sender, RoutedEventArgs e)
    {
        _watcher?.Dispose();
        _mirrorWindow?.Close();
        _trayIcon?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _watcher?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Trace.WriteLine($"[UI] DispatcherUnhandledException: {e.Exception}");
        Trace.Flush();
        e.Handled = true;
    }
}
