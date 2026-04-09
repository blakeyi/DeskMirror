using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DesktopIconMirror.Native;
using Vanara.PInvoke;

namespace DesktopIconMirror.Shell;

/// <summary>Shows the same default Shell context menu as Explorer (Open, Properties, etc.).</summary>
internal static class ShellContextMenuHelper
{
    private const uint IdCmdFirst = 1;
    private const uint IdCmdLast = 0x7FFF;
    private const uint CmicMaskUnicode = 0x00004000;
    private const uint CmicMaskPtInvoke = 0x20000000;
    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmNoNotify = 0x0080;
    private const uint WM_INITMENUPOPUP = 0x0117;
    private const uint WM_DRAWITEM = 0x002B;
    private const uint WM_MEASUREITEM = 0x002C;
    private const uint WM_MENUCHAR = 0x0120;
    private const uint WM_MENUSELECT = 0x011F;
    private const uint WM_UNINITMENUPOPUP = 0x0125;
    private const uint WM_ENTERIDLE = 0x0121;
    private const uint MF_BYCOMMAND = 0x0000;

    private static Shell32.IContextMenu2? s_cm2;
    private static Shell32.IContextMenu3? s_cm3;

    /// <summary>Whether a shell context menu is currently being tracked (suppresses HWND_BOTTOM in WndProc).</summary>
    public static bool IsMenuActive { get; private set; }

    /// <summary>
    /// Call from the owner window's WndProc to forward menu messages to the shell context menu handler.
    /// </summary>
    public static bool HandleMenuMessage(int msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = IntPtr.Zero;
        uint umsg = (uint)msg;

        if (s_cm2 is null && s_cm3 is null)
            return false;

        if (!IsForwardedMenuMessage(umsg))
            return false;

        try
        {
            if (s_cm3 != null)
            {
                s_cm3.HandleMenuMsg2(umsg, wParam, lParam, out result);
                return true;
            }
            if (s_cm2 != null)
            {
                s_cm2.HandleMenuMsg(umsg, wParam, lParam);
                return true;
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ShellCtxMenu] HandleMenuMsg error: {ex.Message}");
        }

        return false;
    }

    private static bool IsForwardedMenuMessage(uint msg) =>
        msg is WM_INITMENUPOPUP
            or WM_DRAWITEM
            or WM_MEASUREITEM
            or WM_MENUCHAR
            or WM_MENUSELECT
            or WM_UNINITMENUPOPUP
            or WM_ENTERIDLE;

    /// <returns><see langword="true"/> if a Shell menu was shown.</returns>
    public static bool TryShowDefaultContextMenu(nint ownerHwnd, string parsingName, int screenX, int screenY)
    {
        Trace.WriteLine($"[ShellCtxMenu] Start: path=\"{parsingName}\" pos=({screenX},{screenY})");

        if (string.IsNullOrEmpty(parsingName))
            return false;

        if (!SupportsNativeContextMenu(parsingName))
        {
            Trace.WriteLine($"[ShellCtxMenu] Native menu disabled for virtual item: \"{parsingName}\"");
            return false;
        }

        Shell32.IShellItem? item;
        try
        {
            item = Shell32.SHCreateItemFromParsingName<Shell32.IShellItem>(parsingName);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ShellCtxMenu] SHCreateItemFromParsingName failed: {ex.Message}");
            return false;
        }

        if (item is null)
            return false;

        using var menuHost = new MenuHostWindow(screenX, screenY);
        nint shellOwnerHwnd = ownerHwnd != IntPtr.Zero ? ownerHwnd : menuHost.Handle;
        nint menuTrackHwnd = menuHost.Handle != IntPtr.Zero ? menuHost.Handle : shellOwnerHwnd;
        Trace.WriteLine($"[ShellCtxMenu] Shell owner hwnd=0x{shellOwnerHwnd:X}, track hwnd=0x{menuTrackHwnd:X}");

        Shell32.IContextMenu ctx;
        IDisposable contextCleanup;
        if (!TryCreateContextMenu(parsingName, item, shellOwnerHwnd, out ctx, out contextCleanup))
            return false;

        s_cm2 = ctx as Shell32.IContextMenu2;
        s_cm3 = ctx as Shell32.IContextMenu3;

        HMENU menu = User32.CreatePopupMenu();
        if (menu.IsNull)
        {
            contextCleanup.Dispose();
            s_cm2 = null;
            s_cm3 = null;
            return false;
        }

        Action? deferredInvoke = null;
        bool shown = false;
        try
        {
            ctx.QueryContextMenu(menu, 0, IdCmdFirst, IdCmdLast, Shell32.CMF.CMF_NORMAL).ThrowIfFailed();

            int count = GetMenuItemCount(menu.DangerousGetHandle());
            Trace.WriteLine($"[ShellCtxMenu] Menu ready: {count} items");

            IsMenuActive = true;

            SetForegroundWindow(menuTrackHwnd);

            Trace.WriteLine("[ShellCtxMenu] TrackPopupMenuEx...");
            Trace.Flush();
            uint cmd = TrackPopupMenuEx(
                menu.DangerousGetHandle(),
                TpmReturnCmd | TpmRightButton | TpmNoNotify,
                screenX,
                screenY,
                menuTrackHwnd,
                IntPtr.Zero);

            IsMenuActive = false;
            Trace.WriteLine($"[ShellCtxMenu] cmd={cmd}");

            if (cmd >= IdCmdFirst)
            {
                string menuText = GetMenuItemText(menu.DangerousGetHandle(), cmd);
                Trace.WriteLine($"[ShellCtxMenu] Selected text=\"{menuText}\"");
                Trace.WriteLine($"[ShellCtxMenu] Invoking offset={cmd - IdCmdFirst}");
                Trace.Flush();
                try
                {
                    Trace.WriteLine("[ShellCtxMenu] Checking known command...");
                    Trace.Flush();
                    if (TryBuildKnownCommand(parsingName, menuText, out var knownCommand))
                    {
                        Trace.WriteLine("[ShellCtxMenu] Queued known command via safe path");
                        deferredInvoke = knownCommand;
                    }
                    else
                    {
                        var request = CreateInvokeCommandRequest(shellOwnerHwnd, (int)(cmd - IdCmdFirst), screenX, screenY);
                        deferredInvoke = () =>
                        {
                            InvokeMenuCommand(ctx, request);
                            Trace.WriteLine("[ShellCtxMenu] InvokeCommand OK");
                        };
                        Trace.WriteLine("[ShellCtxMenu] Queued shell InvokeCommand");
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[ShellCtxMenu] InvokeCommand failed: {ex.Message}");
                }
            }

            shown = true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ShellCtxMenu] Error: {ex}");
            return false;
        }
        finally
        {
            IsMenuActive = false;
            s_cm2 = null;
            s_cm3 = null;
            User32.DestroyMenu(menu);
            contextCleanup.Dispose();
        }

        if (!shown)
            return false;

        if (deferredInvoke != null)
        {
            Trace.WriteLine("[ShellCtxMenu] Scheduling deferred command");
            Trace.Flush();
            ScheduleDeferredCommand(deferredInvoke);
        }

        return true;
    }

    private static bool TryCreateContextMenu(string parsingName, Shell32.IShellItem item, nint shellOwnerHwnd, out Shell32.IContextMenu ctx, out IDisposable cleanup)
    {
        if (TryCreateParentFolderContextMenu(parsingName, shellOwnerHwnd, out ctx, out cleanup))
        {
            Trace.WriteLine("[ShellCtxMenu] Context menu source=IShellFolder.GetUIObjectOf");
            return true;
        }

        try
        {
            ctx = Shell32.SHCreateDefaultContextMenuEx(shellOwnerHwnd, null, out cleanup, item);
            Trace.WriteLine("[ShellCtxMenu] Context menu source=SHCreateDefaultContextMenuEx");
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ShellCtxMenu] SHCreateDefaultContextMenuEx failed: {ex.Message}");
            ctx = null!;
            cleanup = EmptyDisposable.Instance;
            return false;
        }
    }

    private static bool TryCreateParentFolderContextMenu(string parsingName, nint shellOwnerHwnd, out Shell32.IContextMenu ctx, out IDisposable cleanup)
    {
        ctx = null!;
        cleanup = EmptyDisposable.Instance;
        IntPtr pidl = IntPtr.Zero;
        IntPtr contextMenuPtr = IntPtr.Zero;
        object? parentFolder = null;

        try
        {
            int hr = NativeMethods.SHParseDisplayName(parsingName, IntPtr.Zero, out pidl, 0, out _);
            if (hr != 0 || pidl == IntPtr.Zero)
            {
                Trace.WriteLine($"[ShellCtxMenu] SHParseDisplayName failed: 0x{hr:X8}");
                return false;
            }

            hr = NativeMethods.SHBindToParent(
                pidl,
                NativeMethods.IID_IShellFolder,
                out parentFolder,
                out IntPtr childPidl);
            if (hr != 0 || parentFolder is not NativeMethods.IShellFolderNative shellFolder || childPidl == IntPtr.Zero)
            {
                Trace.WriteLine($"[ShellCtxMenu] SHBindToParent failed: 0x{hr:X8}");
                return false;
            }

            var apidl = new[] { childPidl };
            Guid iidContextMenu = NativeMethods.IID_IContextMenu;
            hr = shellFolder.GetUIObjectOf(shellOwnerHwnd, 1, apidl, ref iidContextMenu, IntPtr.Zero, out contextMenuPtr);
            if (hr != 0 || contextMenuPtr == IntPtr.Zero)
            {
                Trace.WriteLine($"[ShellCtxMenu] GetUIObjectOf failed: 0x{hr:X8}");
                return false;
            }

            ctx = (Shell32.IContextMenu)Marshal.GetObjectForIUnknown(contextMenuPtr);
            cleanup = new ParentFolderContextMenuCleanup(pidl, contextMenuPtr, parentFolder);
            pidl = IntPtr.Zero;
            contextMenuPtr = IntPtr.Zero;
            parentFolder = null;
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ShellCtxMenu] Parent folder context menu failed: {ex.Message}");
            return false;
        }
        finally
        {
            if (contextMenuPtr != IntPtr.Zero)
                Marshal.Release(contextMenuPtr);
            if (parentFolder != null && Marshal.IsComObject(parentFolder))
                Marshal.FinalReleaseComObject(parentFolder);
            if (pidl != IntPtr.Zero)
                NativeMethods.ILFree(pidl);
        }
    }

    internal static InvokeCommandRequest CreateInvokeCommandRequest(nint hwnd, int cmdOffset, int screenX, int screenY) =>
        new(hwnd, cmdOffset, screenX, screenY, CmicMaskUnicode | CmicMaskPtInvoke);

    internal static bool SupportsNativeContextMenu(string parsingName) =>
        !(parsingName.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
            || parsingName.StartsWith("::", StringComparison.OrdinalIgnoreCase));

    internal static bool IsOpenMenuText(string? menuText)
    {
        if (string.IsNullOrWhiteSpace(menuText))
            return false;

        string normalized = NormalizeMenuText(menuText);
        return normalized.Equals("open", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("打开", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryBuildKnownCommand(string parsingName, string menuText, out Action? action)
    {
        action = null;

        if (!IsOpenMenuText(menuText))
            return false;

        action = () =>
        {
            Trace.WriteLine($"[ShellCtxMenu] Safe invoke for open: \"{parsingName}\"");
            Trace.Flush();
            OpenParsingName(parsingName);
        };
        return true;
    }

    private static void OpenParsingName(string parsingName)
    {
        if (parsingName.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", parsingName) { UseShellExecute = true });
            return;
        }

        Process.Start(new ProcessStartInfo(parsingName) { UseShellExecute = true });
    }

    private static void ScheduleDeferredCommand(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            Trace.WriteLine("[ShellCtxMenu] Running deferred command");
            Trace.Flush();
            action();
        }));
    }

    private sealed class ParentFolderContextMenuCleanup(IntPtr pidl, IntPtr contextMenuPtr, object parentFolder) : IDisposable
    {
        private IntPtr _pidl = pidl;
        private IntPtr _contextMenuPtr = contextMenuPtr;
        private object? _parentFolder = parentFolder;

        public void Dispose()
        {
            if (_contextMenuPtr != IntPtr.Zero)
            {
                Marshal.Release(_contextMenuPtr);
                _contextMenuPtr = IntPtr.Zero;
            }

            if (_parentFolder != null && Marshal.IsComObject(_parentFolder))
            {
                Marshal.FinalReleaseComObject(_parentFolder);
                _parentFolder = null;
            }

            if (_pidl != IntPtr.Zero)
            {
                NativeMethods.ILFree(_pidl);
                _pidl = IntPtr.Zero;
            }
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }

    private static string GetMenuItemText(IntPtr menuHandle, uint commandId)
    {
        var buffer = new StringBuilder(260);
        int length = GetMenuString(menuHandle, commandId, buffer, buffer.Capacity, MF_BYCOMMAND);
        return length > 0 ? buffer.ToString() : string.Empty;
    }

    private static string NormalizeMenuText(string menuText)
    {
        int tabIndex = menuText.IndexOf('\t');
        string text = tabIndex >= 0 ? menuText[..tabIndex] : menuText;
        text = text.Replace("&", string.Empty)
            .Replace("...", string.Empty)
            .Replace("…", string.Empty);
        text = Regex.Replace(text, @"\((?:&)?[A-Za-z]\)", string.Empty);
        return text.Trim();
    }

    private static void InvokeMenuCommand(Shell32.IContextMenu ctx, InvokeCommandRequest request)
    {
        var info = new CMINVOKECOMMANDINFOEX
        {
            cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
            fMask = request.Flags,
            hwnd = request.Hwnd,
            lpVerb = (IntPtr)request.CommandOffset,
            lpVerbW = (IntPtr)request.CommandOffset,
            nShow = NativeMethods.SW_SHOWNORMAL,
            ptInvoke = new NativeMethods.POINT { X = request.ScreenX, Y = request.ScreenY },
        };

        IntPtr ptr = Marshal.AllocHGlobal(info.cbSize);
        try
        {
            Trace.WriteLine($"[ShellCtxMenu] Invoke hwnd=0x{request.Hwnd:X} offset={request.CommandOffset} pt=({request.ScreenX},{request.ScreenY}) flags=0x{request.Flags:X8}");
            Trace.Flush();
            Marshal.StructureToPtr(info, ptr, false);
            ctx.InvokeCommand(ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    internal readonly record struct InvokeCommandRequest(nint Hwnd, int CommandOffset, int ScreenX, int ScreenY, uint Flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public uint fMask;
        public nint hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public NativeMethods.POINT ptInvoke;
    }

    private sealed class MenuHostWindow : IDisposable
    {
        private const int WsPopup = unchecked((int)0x80000000);
        private readonly HwndSource _source;

        public MenuHostWindow(int screenX, int screenY)
        {
            var parameters = new HwndSourceParameters("DeskMirrorShellContextMenuHost")
            {
                PositionX = screenX,
                PositionY = screenY,
                Width = 1,
                Height = 1,
                WindowStyle = WsPopup,
            };

            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
        }

        public nint Handle => _source.Handle;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (HandleMenuMessage(msg, wParam, lParam, out IntPtr result))
            {
                handled = true;
                return result;
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr hMenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMenuString(IntPtr hMenu, uint uIDItem, StringBuilder lpString, int cchMax, uint flags);
}
