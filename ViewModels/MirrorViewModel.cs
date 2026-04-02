using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopIconMirror.Models;
using DesktopIconMirror.Monitor;
using DesktopIconMirror.Native;
using DesktopIconMirror.Shell;

namespace DesktopIconMirror.ViewModels;

public partial class MirrorViewModel : ObservableObject
{
    public ObservableCollection<DesktopIcon> Icons { get; } = [];

    private MonitorData? _primaryMonitor;
    private MonitorData? _targetMonitor;
    private double _targetDpiScale = 1.0;

    public void Initialize(MonitorData primary, MonitorData target, double targetDpiScale)
    {
        _primaryMonitor = primary;
        _targetMonitor = target;
        _targetDpiScale = targetDpiScale;
    }

    public void RefreshIcons()
    {
        var icons = DesktopIconEnumerator.Enumerate();

        Application.Current.Dispatcher.Invoke(() =>
        {
            Icons.Clear();
            foreach (var icon in icons)
            {
                MapPosition(icon);
                Icons.Add(icon);
            }
        });
    }

    private void MapPosition(DesktopIcon icon)
    {
        if (_primaryMonitor == null || _targetMonitor == null)
        {
            icon.MirrorX = icon.PositionX;
            icon.MirrorY = icon.PositionY;
            return;
        }

        double scaleX = (double)_targetMonitor.WorkingArea.Width / _primaryMonitor.WorkingArea.Width;
        double scaleY = (double)_targetMonitor.WorkingArea.Height / _primaryMonitor.WorkingArea.Height;

        icon.MirrorX = icon.PositionX * scaleX / _targetDpiScale;
        icon.MirrorY = icon.PositionY * scaleY / _targetDpiScale;
    }

    [RelayCommand]
    private void OpenIcon(DesktopIcon? icon)
    {
        if (icon == null || string.IsNullOrEmpty(icon.TargetPath))
            return;

        try
        {
            if (icon.TargetPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                && TryActivateExistingApp(icon.TargetPath, icon.Name))
                return;

            Process.Start(new ProcessStartInfo(icon.TargetPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[OpenIcon] Failed to open {icon.TargetPath}: {ex}");
        }
    }

    private static bool TryActivateExistingApp(string lnkPath, string? iconName)
    {
        try
        {
            var targetExe = ResolveShortcutTarget(lnkPath);
            Trace.WriteLine($"[TryActivate] Shortcut target: {targetExe ?? "(null)"}, iconName: {iconName}");
            if (string.IsNullOrEmpty(targetExe))
                return false;

            var exeName = Path.GetFileNameWithoutExtension(targetExe);
            var processes = Process.GetProcessesByName(exeName);
            Trace.WriteLine($"[TryActivate] Process '{exeName}': found {processes.Length} instance(s)");

            if (processes.Length == 0)
                return false;

            var targetPids = new HashSet<uint>(processes.Select(p => (uint)p.Id));

            // Strategy 1: if there's already a visible window, just activate it
            var visibleWnd = FindLargestVisibleWindow(targetPids);
            if (visibleWnd != IntPtr.Zero)
            {
                Trace.WriteLine($"[TryActivate] Found visible window 0x{visibleWnd:X}, activating");
                ForceActivateWindow(visibleWnd);
                return true;
            }

            // Strategy 2: no visible window → app is in tray, use UI Automation (Win11)
            if (!string.IsNullOrEmpty(iconName)
                && TrayIconHelper.TryClickViaUIAutomation(iconName, targetPids))
            {
                Trace.WriteLine("[TryActivate] UI Automation tray click succeeded");
                return true;
            }

            // Strategy 3: toolbar-based tray icon click (Win10)
            var pids = processes.Select(p => (uint)p.Id);
            if (TrayIconHelper.TryClickTrayIcon(pids))
            {
                Trace.WriteLine("[TryActivate] Toolbar tray click succeeded");
                return true;
            }

            Trace.WriteLine("[TryActivate] All strategies failed, falling back to Process.Start");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[TryActivate] EXCEPTION: {ex}");
        }

        return false;
    }

    private static IntPtr FindLargestVisibleWindow(HashSet<uint> targetPids)
    {
        IntPtr bestHwnd = IntPtr.Zero;
        int bestArea = 0;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (targetPids.Contains(pid)
                && NativeMethods.IsWindowVisible(hwnd)
                && NativeMethods.GetWindowTextLength(hwnd) > 0)
            {
                NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rc);
                if (rc.Area > bestArea)
                {
                    bestArea = rc.Area;
                    bestHwnd = hwnd;
                }
            }
            return true;
        }, IntPtr.Zero);

        return bestArea > 50000 ? bestHwnd : IntPtr.Zero;
    }

    private static void ForceActivateWindow(IntPtr hwnd)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        uint foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        uint targetThread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);

        bool attached = false;
        if (foregroundThread != targetThread)
            attached = NativeMethods.AttachThreadInput(foregroundThread, targetThread, true);

        try
        {
            if (NativeMethods.IsIconic(hwnd))
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

            NativeMethods.BringWindowToTop(hwnd);
            NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
                NativeMethods.AttachThreadInput(foregroundThread, targetThread, false);
        }
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
        catch
        {
            return null;
        }
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "";
        var sb = new System.Text.StringBuilder(256);
        NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetWindowClass(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "";
        var sb = new System.Text.StringBuilder(256);
        NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    [RelayCommand]
    private void SelectIcon(DesktopIcon? icon)
    {
        if (icon == null) return;

        foreach (var i in Icons)
            i.IsSelected = false;

        icon.IsSelected = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var i in Icons)
            i.IsSelected = false;
    }

    [RelayCommand]
    private void OpenFileLocation(DesktopIcon? icon)
    {
        if (icon == null || string.IsNullOrEmpty(icon.TargetPath))
            return;

        try
        {
            Process.Start("explorer.exe", $"/select,\"{icon.TargetPath}\"");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open location for {icon.TargetPath}: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ShowProperties(DesktopIcon? icon)
    {
        if (icon == null || string.IsNullOrEmpty(icon.TargetPath))
            return;

        try
        {
            var psi = new ProcessStartInfo("explorer.exe")
            {
                Arguments = $"/select,\"{icon.TargetPath}\"",
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to show properties for {icon.TargetPath}: {ex.Message}");
        }
    }
}
