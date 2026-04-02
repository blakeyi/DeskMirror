using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using DesktopIconMirror.Native;

namespace DesktopIconMirror.Shell;

internal static class TrayIconHelper
{
    /// <summary>
    /// Use Windows UI Automation to find a tray icon by display name and click it.
    /// Works on Windows 11 where the notification area is XAML-based.
    /// </summary>
    public static bool TryClickViaUIAutomation(string iconName)
    {
        Trace.WriteLine($"[TrayUIA] Searching for tray icon named \"{iconName}\"...");

        try
        {
            // The taskbar: Shell_TrayWnd
            var trayWnd = AutomationElement.RootElement.FindFirst(
                TreeScope.Children,
                new PropertyCondition(AutomationElement.ClassNameProperty, "Shell_TrayWnd"));

            if (trayWnd == null)
            {
                Trace.WriteLine("[TrayUIA] Shell_TrayWnd not found");
                return false;
            }

            // Search for a button whose Name contains the icon name
            // Win11 tray icons are typically Button controls with the app name as Name/tooltip
            var allButtons = trayWnd.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

            Trace.WriteLine($"[TrayUIA] Found {allButtons.Count} buttons in taskbar");

            AutomationElement? match = null;
            foreach (AutomationElement btn in allButtons)
            {
                string name = btn.Current.Name ?? "";
                if (name.Contains(iconName, StringComparison.OrdinalIgnoreCase))
                {
                    Trace.WriteLine($"[TrayUIA] Match: \"{name}\" (ClassName={btn.Current.ClassName})");
                    match = btn;
                    break;
                }
            }

            // Also check the overflow flyout if open, or try to find in system tray overflow
            // If not found in main taskbar, open the overflow tray and search there
            if (match == null)
            {
                Trace.WriteLine("[TrayUIA] Not in main taskbar, opening overflow area...");

                // Find and click "显示隐藏的图标" (Show hidden icons) chevron button
                AutomationElement? chevron = null;
                foreach (AutomationElement btn in allButtons)
                {
                    string name = btn.Current.Name ?? "";
                    if (name.Contains("隐藏", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("hidden", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("overflow", StringComparison.OrdinalIgnoreCase))
                    {
                        chevron = btn;
                        Trace.WriteLine($"[TrayUIA] Found chevron: \"{name}\"");
                        break;
                    }
                }

                if (chevron != null)
                {
                    if (chevron.TryGetCurrentPattern(InvokePattern.Pattern, out object? chevronInvoke))
                        ((InvokePattern)chevronInvoke).Invoke();
                    else
                    {
                        var chevronRect = chevron.Current.BoundingRectangle;
                        if (!chevronRect.IsEmpty)
                            SimulateClick((int)(chevronRect.X + chevronRect.Width / 2),
                                          (int)(chevronRect.Y + chevronRect.Height / 2));
                    }

                    Thread.Sleep(500);

                    // Now search the overflow popup
                    var overflowWnd = AutomationElement.RootElement.FindFirst(
                        TreeScope.Children,
                        new PropertyCondition(AutomationElement.ClassNameProperty, "TopLevelWindowForOverflowXamlIsland"));

                    if (overflowWnd != null)
                    {
                        Trace.WriteLine("[TrayUIA] Overflow window found, searching...");
                        var overflowButtons = overflowWnd.FindAll(
                            TreeScope.Descendants,
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

                        Trace.WriteLine($"[TrayUIA] Overflow has {overflowButtons.Count} buttons");
                        foreach (AutomationElement btn in overflowButtons)
                        {
                            string name = btn.Current.Name ?? "";
                            Trace.WriteLine($"[TrayUIA] Overflow button: \"{name}\"");
                            if (name.Contains(iconName, StringComparison.OrdinalIgnoreCase))
                            {
                                Trace.WriteLine($"[TrayUIA] Overflow match: \"{name}\"");
                                match = btn;
                                break;
                            }
                        }
                    }
                    else
                    {
                        Trace.WriteLine("[TrayUIA] Overflow window not found after clicking chevron");
                    }
                }
                else
                {
                    Trace.WriteLine("[TrayUIA] Chevron button not found");
                }
            }

            if (match == null)
            {
                // Log all button names for diagnostics
                Trace.WriteLine("[TrayUIA] No match. All taskbar button names:");
                foreach (AutomationElement btn in allButtons)
                {
                    string name = btn.Current.Name ?? "(null)";
                    if (!string.IsNullOrWhiteSpace(name))
                        Trace.WriteLine($"  - \"{name}\"");
                }
                return false;
            }

            // Try InvokePattern first (standard button click)
            if (match.TryGetCurrentPattern(InvokePattern.Pattern, out object? invokeObj))
            {
                ((InvokePattern)invokeObj).Invoke();
                Trace.WriteLine("[TrayUIA] Invoked via InvokePattern");
                return true;
            }

            // Try TogglePattern
            if (match.TryGetCurrentPattern(TogglePattern.Pattern, out object? toggleObj))
            {
                ((TogglePattern)toggleObj).Toggle();
                Trace.WriteLine("[TrayUIA] Invoked via TogglePattern");
                return true;
            }

            // Fallback: simulate click at the element's center using SendInput
            var rect = match.Current.BoundingRectangle;
            if (!rect.IsEmpty)
            {
                int cx = (int)(rect.X + rect.Width / 2);
                int cy = (int)(rect.Y + rect.Height / 2);
                Trace.WriteLine($"[TrayUIA] Clicking at ({cx}, {cy})");
                SimulateClick(cx, cy);
                return true;
            }

            Trace.WriteLine("[TrayUIA] No interaction pattern available and no bounding rect");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[TrayUIA] EXCEPTION: {ex.Message}");
        }

        return false;
    }

    private static void SimulateClick(int screenX, int screenY)
    {
        int normalizedX = screenX * 65535 / NativeMethods.GetSystemMetrics(0);
        int normalizedY = screenY * 65535 / NativeMethods.GetSystemMetrics(1);

        var inputs = new NativeMethods.INPUT[3];

        inputs[0].type = 0; // INPUT_MOUSE
        inputs[0].u.mi.dx = normalizedX;
        inputs[0].u.mi.dy = normalizedY;
        inputs[0].u.mi.dwFlags = 0x8001; // MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE

        inputs[1].type = 0;
        inputs[1].u.mi.dwFlags = 0x0002; // MOUSEEVENTF_LEFTDOWN

        inputs[2].type = 0;
        inputs[2].u.mi.dwFlags = 0x0004; // MOUSEEVENTF_LEFTUP

        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public static bool TryClickTrayIcon(IEnumerable<uint> targetPids)
    {
        var pidSet = new HashSet<uint>(targetPids);
        return TryViaToolbar(pidSet);
    }

    private static bool TryViaToolbar(HashSet<uint> targetPids)
    {
        var toolbars = FindTrayToolbars();
        foreach (var toolbar in toolbars)
        {
            if (TryClickInToolbar(toolbar, targetPids))
                return true;
        }
        return false;
    }

    private static List<IntPtr> FindTrayToolbars()
    {
        var result = new List<IntPtr>();

        var shellTray = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (shellTray != IntPtr.Zero)
        {
            var trayNotify = NativeMethods.FindWindowEx(shellTray, IntPtr.Zero, "TrayNotifyWnd", null);
            if (trayNotify != IntPtr.Zero)
            {
                var sysPager = NativeMethods.FindWindowEx(trayNotify, IntPtr.Zero, "SysPager", null);
                if (sysPager != IntPtr.Zero)
                {
                    var toolbar = NativeMethods.FindWindowEx(sysPager, IntPtr.Zero, "ToolbarWindow32", null);
                    if (toolbar != IntPtr.Zero) result.Add(toolbar);
                }

                NativeMethods.EnumChildWindows(trayNotify, (hwnd, _) =>
                {
                    var sb = new StringBuilder(64);
                    NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
                    if (sb.ToString() == "ToolbarWindow32" && !result.Contains(hwnd))
                        result.Add(hwnd);
                    return true;
                }, IntPtr.Zero);
            }
        }

        var overflow = NativeMethods.FindWindow("NotifyIconOverflowWindow", null);
        if (overflow != IntPtr.Zero)
        {
            var toolbar = NativeMethods.FindWindowEx(overflow, IntPtr.Zero, "ToolbarWindow32", null);
            if (toolbar != IntPtr.Zero) result.Add(toolbar);
        }

        return result;
    }

    private static bool TryClickInToolbar(IntPtr toolbar, HashSet<uint> targetPids)
    {
        int buttonCount = (int)NativeMethods.SendMessage(toolbar, NativeMethods.TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero);
        if (buttonCount <= 0) return false;

        NativeMethods.GetWindowThreadProcessId(toolbar, out uint explorerPid);
        IntPtr hProcess = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_VM_OPERATION | NativeMethods.PROCESS_VM_READ,
            false, explorerPid);
        if (hProcess == IntPtr.Zero) return false;

        int tbButtonSize = Marshal.SizeOf<NativeMethods.TBBUTTON64>();
        int trayDataSize = Marshal.SizeOf<NativeMethods.TRAYDATA>();
        uint allocSize = (uint)Math.Max(tbButtonSize, trayDataSize);

        IntPtr remoteMem = NativeMethods.VirtualAllocEx(hProcess, IntPtr.Zero, allocSize,
            NativeMethods.MEM_COMMIT, NativeMethods.PAGE_READWRITE);
        if (remoteMem == IntPtr.Zero) { NativeMethods.CloseHandle(hProcess); return false; }

        IntPtr localBuf = Marshal.AllocHGlobal((int)allocSize);
        bool found = false;

        try
        {
            for (int i = 0; i < buttonCount; i++)
            {
                NativeMethods.SendMessage(toolbar, NativeMethods.TB_GETBUTTON, (IntPtr)i, remoteMem);
                if (!NativeMethods.ReadProcessMemory(hProcess, remoteMem, localBuf, (uint)tbButtonSize, out _))
                    continue;
                var btn = Marshal.PtrToStructure<NativeMethods.TBBUTTON64>(localBuf);
                if (btn.dwData == 0) continue;
                if (!NativeMethods.ReadProcessMemory(hProcess, (IntPtr)btn.dwData, localBuf, (uint)trayDataSize, out _))
                    continue;
                var tray = Marshal.PtrToStructure<NativeMethods.TRAYDATA>(localBuf);
                if (tray.hwnd == IntPtr.Zero || tray.uCallbackMessage == 0) continue;

                NativeMethods.GetWindowThreadProcessId(tray.hwnd, out uint iconPid);
                if (targetPids.Contains(iconPid))
                {
                    Trace.WriteLine($"[TrayClick] Toolbar match: hwnd=0x{tray.hwnd:X}, PID={iconPid}, msg=0x{tray.uCallbackMessage:X}");
                    NativeMethods.PostMessage(tray.hwnd, tray.uCallbackMessage,
                        (IntPtr)tray.uID, (IntPtr)NativeMethods.WM_LBUTTONDOWN);
                    NativeMethods.PostMessage(tray.hwnd, tray.uCallbackMessage,
                        (IntPtr)tray.uID, (IntPtr)NativeMethods.WM_LBUTTONUP);
                    found = true;
                    break;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(localBuf);
            NativeMethods.VirtualFreeEx(hProcess, remoteMem, 0, NativeMethods.MEM_RELEASE);
            NativeMethods.CloseHandle(hProcess);
        }
        return found;
    }
}
