using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using DesktopIconMirror.Monitor;
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
    private AppSettings _settings = new();
    private bool _isRefreshing;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        _settings = SettingsService.Load();
        InitializeTrayIcon();
        InitializeMirrorWindow();
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            Icon = SystemIcons.Application,
            ToolTipText = "Desktop Icon Mirror"
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
        var secondary = !string.IsNullOrEmpty(_settings.TargetMonitorDeviceName)
            ? MonitorDetector.GetAllMonitors().FirstOrDefault(m => m.DeviceName == _settings.TargetMonitorDeviceName)
            : MonitorDetector.GetFirstSecondaryMonitor();

        if (primary == null || secondary == null)
        {
            _trayIcon?.ShowBalloonTip("Desktop Icon Mirror",
                "未检测到副屏，程序将在托盘等待。\n接入副屏后请右键刷新。",
                BalloonIcon.Info);
            return;
        }

        _viewModel = new MirrorViewModel();
        _mirrorWindow = new MirrorWindow { DataContext = _viewModel };

        double dpiScale = GetDpiScaleForPoint(secondary.Bounds.Left, secondary.Bounds.Top);
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

    private void TrayMenu_ToggleVisibility(object sender, RoutedEventArgs e)
    {
        if (_mirrorWindow == null)
        {
            InitializeMirrorWindow();
            return;
        }

        if (_mirrorWindow.IsVisible)
            _mirrorWindow.Hide();
        else
            _mirrorWindow.Show();
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
        Debug.WriteLine($"Unhandled exception: {e.Exception}");
        e.Handled = true;
    }
}
