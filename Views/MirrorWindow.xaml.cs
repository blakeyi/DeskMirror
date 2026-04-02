using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopIconMirror.Models;
using DesktopIconMirror.Monitor;
using DesktopIconMirror.Native;
using DesktopIconMirror.ViewModels;

namespace DesktopIconMirror.Views;

public partial class MirrorWindow : Window
{
    private MirrorViewModel ViewModel => (MirrorViewModel)DataContext;
    private System.Drawing.Rectangle _rawBounds;

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
        _rawBounds = monitor.WorkingArea;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        hwndSource?.AddHook(WndProc);
        SendToBottom();
    }

    internal bool DesktopShown { get; set; }

    private void SendToBottom()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_BOTTOM, 0, 0, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE);
    }

    internal void BringAboveDesktop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, new IntPtr(-1) /*HWND_TOPMOST*/, 0, 0, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE);
        NativeMethods.SetWindowPos(hwnd, new IntPtr(-2) /*HWND_NOTOPMOST*/, 0, 0, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_WINDOWPOSCHANGING && !DesktopShown)
        {
            var pos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
            pos.hwndInsertAfter = NativeMethods.HWND_BOTTOM;
            pos.flags &= ~NativeMethods.SWP_NOZORDER;
            Marshal.StructureToPtr(pos, lParam, true);
        }
        return IntPtr.Zero;
    }

    private const string InternalDragFormat = "DesktopIconMirror_Path";

    private System.Windows.Point? _dragStartPoint;
    private DesktopIcon? _dragIcon;

    private void Icon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DesktopIcon icon)
        {
            ViewModel.SelectIconCommand.Execute(icon);
            _dragStartPoint = e.GetPosition(this);
            _dragIcon = icon;
        }
        e.Handled = true;
    }

    private void Icon_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStartPoint == null || _dragIcon == null)
            return;

        var pos = e.GetPosition(this);
        var diff = pos - _dragStartPoint.Value;

        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        string path = _dragIcon.TargetPath;
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            path = ResolveShortcutTarget(path) ?? path;

        _dragStartPoint = null;
        _dragIcon = null;

        // Internal-only format so dropping outside the mirror window does nothing
        var data = new System.Windows.DataObject(InternalDragFormat, path);
        System.Windows.DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Link);
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

    private static readonly System.Windows.Media.Brush DropHighlightBg =
        new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x44, 0x99, 0xD6, 0xFF));
    private static readonly System.Windows.Media.Brush DropHighlightBorder =
        new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x88, 0x99, 0xD6, 0xFF));

    private static bool HasDroppableData(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(InternalDragFormat);

    private void Icon_DragEnter(object sender, DragEventArgs e)
    {
        if (HasDroppableData(e) && sender is Border border)
        {
            e.Effects = DragDropEffects.Link;
            border.Background = DropHighlightBg;
            border.BorderBrush = DropHighlightBorder;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Icon_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasDroppableData(e) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void Icon_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = System.Windows.Media.Brushes.Transparent;
            border.BorderBrush = System.Windows.Media.Brushes.Transparent;
        }
    }

    private void Icon_Drop(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = System.Windows.Media.Brushes.Transparent;
            border.BorderBrush = System.Windows.Media.Brushes.Transparent;
        }

        if (sender is not FrameworkElement fe || fe.DataContext is not DesktopIcon icon)
            return;

        // Collect dropped file paths from either external FileDrop or internal drag
        string[]? files = null;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
            files = e.Data.GetData(DataFormats.FileDrop) as string[];
        else if (e.Data.GetDataPresent(InternalDragFormat) && e.Data.GetData(InternalDragFormat) is string path)
            files = [path];

        if (files == null || files.Length == 0 || string.IsNullOrEmpty(icon.TargetPath))
            return;

        try
        {
            string targetPath = icon.TargetPath;

            // Resolve .lnk to actual target
            if (targetPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                targetPath = ResolveShortcutTarget(targetPath) ?? targetPath;

            Trace.WriteLine($"[Drop] Target resolved: \"{icon.Name}\" -> {targetPath}");

            if (Directory.Exists(targetPath))
            {
                CopyFilesToFolder(files, targetPath);
            }
            else
            {
                var args = string.Join(" ", files.Select(f => $"\"{f}\""));
                Process.Start(new ProcessStartInfo(icon.TargetPath)
                {
                    Arguments = args,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Drop] Failed: {ex.Message}");
        }

        e.Handled = true;
    }

    private static string? ResolveShortcutTarget(string lnkPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(lnkPath);
            string target = shortcut.TargetPath;
            Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
            return string.IsNullOrEmpty(target) ? null : target;
        }
        catch { return null; }
    }

    private static void CopyFilesToFolder(string[] sources, string destFolder)
    {
        // Build double-null-terminated source list for SHFileOperation
        string from = string.Join('\0', sources) + "\0\0";
        string to = destFolder + "\0\0";

        var op = new NativeMethods.SHFILEOPSTRUCT
        {
            hwnd = IntPtr.Zero,
            wFunc = 0x0002, // FO_COPY
            pFrom = from,
            pTo = to,
            fFlags = 0x0040 | 0x0010, // FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR
        };

        int result = NativeMethods.SHFileOperation(ref op);
        Trace.WriteLine($"[Drop] SHFileOperation copy -> result={result}, aborted={op.fAnyOperationsAborted}");
    }

    private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ViewModel.ClearSelectionCommand.Execute(null);
    }
}
