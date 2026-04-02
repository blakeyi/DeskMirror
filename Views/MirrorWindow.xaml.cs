using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using DesktopIconMirror.Models;
using DesktopIconMirror.Monitor;
using DesktopIconMirror.Native;
using DesktopIconMirror.ViewModels;

namespace DesktopIconMirror.Views;

public partial class MirrorWindow : Window
{
    private MirrorViewModel ViewModel => (MirrorViewModel)DataContext;

    public MirrorWindow()
    {
        InitializeComponent();
    }

    public void PlaceOnMonitor(MonitorData monitor, double dpiScale)
    {
        Left = monitor.WorkingArea.Left / dpiScale;
        Top = monitor.WorkingArea.Top / dpiScale;
        Width = monitor.WorkingArea.Width / dpiScale;
        Height = monitor.WorkingArea.Height / dpiScale;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        hwndSource?.AddHook(WndProc);
        SendToBottom();
    }

    private void SendToBottom()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_BOTTOM, 0, 0, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_WINDOWPOSCHANGING)
        {
            var pos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
            pos.hwndInsertAfter = NativeMethods.HWND_BOTTOM;
            pos.flags &= ~NativeMethods.SWP_NOZORDER;
            Marshal.StructureToPtr(pos, lParam, true);
            handled = false;
        }

        return IntPtr.Zero;
    }

    private void Icon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DesktopIcon icon)
        {
            ViewModel.SelectIconCommand.Execute(icon);
        }
        e.Handled = true;
    }

    private void Icon_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DesktopIcon icon)
        {
            ViewModel.SelectIconCommand.Execute(icon);
            ShowIconContextMenu(icon, e);
        }
        e.Handled = true;
    }

    private void ShowIconContextMenu(DesktopIcon icon, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();

        var openItem = new MenuItem { Header = "打开(_O)" };
        openItem.Click += (_, _) => ViewModel.OpenIconCommand.Execute(icon);
        menu.Items.Add(openItem);

        if (!string.IsNullOrEmpty(icon.TargetPath))
        {
            var locationItem = new MenuItem { Header = "打开文件位置(_L)" };
            locationItem.Click += (_, _) => ViewModel.OpenFileLocationCommand.Execute(icon);
            menu.Items.Add(locationItem);

            menu.Items.Add(new Separator());

            var copyPathItem = new MenuItem { Header = "复制路径(_C)" };
            copyPathItem.Click += (_, _) => Clipboard.SetText(icon.TargetPath);
            menu.Items.Add(copyPathItem);
        }

        menu.IsOpen = true;
    }

    private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ViewModel.ClearSelectionCommand.Execute(null);
    }
}
