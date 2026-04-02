using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using DesktopIconMirror.Models;
using DesktopIconMirror.Native;

namespace DesktopIconMirror.Shell;

public static class DesktopIconEnumerator
{
    private static readonly string[] DesktopPaths =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
    ];

    public static List<DesktopIcon> Enumerate()
    {
        var listViewHwnd = FindDesktopListView();
        if (listViewHwnd == IntPtr.Zero)
            return [];

        NativeMethods.GetWindowThreadProcessId(listViewHwnd, out uint pid);
        var hProcess = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_VM_OPERATION | NativeMethods.PROCESS_VM_READ | NativeMethods.PROCESS_VM_WRITE,
            false, pid);

        if (hProcess == IntPtr.Zero)
            return [];

        try
        {
            int count = (int)NativeMethods.SendMessage(listViewHwnd, NativeMethods.LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            var items = new List<DesktopIcon>(count);

            for (int i = 0; i < count; i++)
            {
                var name = ReadItemText(hProcess, listViewHwnd, i);
                if (string.IsNullOrEmpty(name))
                    continue;

                var pos = ReadItemPosition(hProcess, listViewHwnd, i);

                var icon = new DesktopIcon
                {
                    Name = name,
                    PositionX = pos.X,
                    PositionY = pos.Y
                };

                ResolveIconDetails(icon);
                items.Add(icon);
            }

            return items;
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }
    }

    private static IntPtr FindDesktopListView()
    {
        var progman = NativeMethods.FindWindow("Progman", null);
        var defView = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

        if (defView == IntPtr.Zero)
        {
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                var dv = NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (dv != IntPtr.Zero)
                {
                    defView = dv;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
        }

        return defView == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
    }

    private static string ReadItemText(IntPtr hProcess, IntPtr listView, int index)
    {
        const int textBufferSize = 512;
        int lvItemSize = Marshal.SizeOf<NativeMethods.LVITEM>();
        uint totalSize = (uint)(lvItemSize + textBufferSize * 2);

        var remoteMem = NativeMethods.VirtualAllocEx(hProcess, IntPtr.Zero, totalSize,
            NativeMethods.MEM_COMMIT, NativeMethods.PAGE_READWRITE);
        if (remoteMem == IntPtr.Zero)
            return "";

        try
        {
            var remoteText = remoteMem + lvItemSize;

            var lvItem = new NativeMethods.LVITEM
            {
                mask = NativeMethods.LVIF_TEXT,
                iItem = index,
                iSubItem = 0,
                cchTextMax = textBufferSize,
                pszText = remoteText
            };

            var localLvItem = Marshal.AllocHGlobal(lvItemSize);
            try
            {
                Marshal.StructureToPtr(lvItem, localLvItem, false);
                NativeMethods.WriteProcessMemory(hProcess, remoteMem, localLvItem, (uint)lvItemSize, out _);
            }
            finally
            {
                Marshal.FreeHGlobal(localLvItem);
            }

            NativeMethods.SendMessage(listView, NativeMethods.LVM_GETITEMTEXTW, (IntPtr)index, remoteMem);

            var localText = Marshal.AllocHGlobal(textBufferSize * 2);
            try
            {
                NativeMethods.ReadProcessMemory(hProcess, remoteText, localText, (uint)(textBufferSize * 2), out _);
                return Marshal.PtrToStringUni(localText) ?? "";
            }
            finally
            {
                Marshal.FreeHGlobal(localText);
            }
        }
        finally
        {
            NativeMethods.VirtualFreeEx(hProcess, remoteMem, 0, NativeMethods.MEM_RELEASE);
        }
    }

    private static NativeMethods.POINT ReadItemPosition(IntPtr hProcess, IntPtr listView, int index)
    {
        uint size = (uint)Marshal.SizeOf<NativeMethods.POINT>();
        var remoteMem = NativeMethods.VirtualAllocEx(hProcess, IntPtr.Zero, size,
            NativeMethods.MEM_COMMIT, NativeMethods.PAGE_READWRITE);

        if (remoteMem == IntPtr.Zero)
            return default;

        try
        {
            NativeMethods.SendMessage(listView, NativeMethods.LVM_GETITEMPOSITION, (IntPtr)index, remoteMem);

            var localPoint = Marshal.AllocHGlobal((int)size);
            try
            {
                NativeMethods.ReadProcessMemory(hProcess, remoteMem, localPoint, size, out _);
                return Marshal.PtrToStructure<NativeMethods.POINT>(localPoint);
            }
            finally
            {
                Marshal.FreeHGlobal(localPoint);
            }
        }
        finally
        {
            NativeMethods.VirtualFreeEx(hProcess, remoteMem, 0, NativeMethods.MEM_RELEASE);
        }
    }

    private static void ResolveIconDetails(DesktopIcon item)
    {
        foreach (var dir in DesktopPaths)
        {
            try
            {
                foreach (var entry in Directory.GetFileSystemEntries(dir))
                {
                    var nameNoExt = Path.GetFileNameWithoutExtension(entry);
                    var nameWithExt = Path.GetFileName(entry);

                    if (string.Equals(nameNoExt, item.Name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(nameWithExt, item.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        item.TargetPath = entry;
                        item.IsFolder = Directory.Exists(entry);
                        item.Icon = GetIconForPath(entry, item.IsFolder);
                        return;
                    }
                }
            }
            catch
            {
                // Ignore permission errors
            }
        }

        item.Icon = GetFallbackIcon();
    }

    private const int IconRequestSize = 512;

    private static BitmapSource? GetIconForPath(string path, bool isFolder)
    {
        var highRes = GetHighResIcon(path);
        if (highRes != null)
            return highRes;

        return GetIconViaShGetFileInfo(path, isFolder);
    }

    private static BitmapSource? GetHighResIcon(string path)
    {
        try
        {
            NativeMethods.SHCreateItemFromParsingName(
                path, IntPtr.Zero, NativeMethods.IID_IShellItemImageFactory, out var obj);

            var factory = (NativeMethods.IShellItemImageFactory)obj;
            var size = new NativeMethods.SIZE(IconRequestSize, IconRequestSize);
            int hr = factory.GetImage(size, NativeMethods.SIIGBF.ICONONLY | NativeMethods.SIIGBF.BIGGERSIZEOK, out var hBitmap);

            if (hr != 0 || hBitmap == IntPtr.Zero)
                return null;

            try
            {
                return HBitmapToBitmapSource(hBitmap);
            }
            finally
            {
                NativeMethods.DeleteObject(hBitmap);
            }
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? HBitmapToBitmapSource(IntPtr hBitmap)
    {
        var bmp = new NativeMethods.BITMAP();
        NativeMethods.GetObject(hBitmap, Marshal.SizeOf<NativeMethods.BITMAP>(), ref bmp);

        if (bmp.bmWidth <= 0 || bmp.bmHeight <= 0)
            return null;

        if (bmp.bmBitsPixel == 32 && bmp.bmBits != IntPtr.Zero)
        {
            int stride = bmp.bmWidth * 4;
            int byteCount = stride * bmp.bmHeight;
            var pixels = new byte[byteCount];
            Marshal.Copy(bmp.bmBits, pixels, 0, byteCount);

            var flipped = new byte[byteCount];
            for (int y = 0; y < bmp.bmHeight; y++)
            {
                Buffer.BlockCopy(pixels, (bmp.bmHeight - 1 - y) * stride,
                                 flipped, y * stride, stride);
            }

            var source = BitmapSource.Create(
                bmp.bmWidth, bmp.bmHeight, 96, 96,
                System.Windows.Media.PixelFormats.Pbgra32,
                null, flipped, stride);
            source.Freeze();
            return source;
        }

        var fallback = Imaging.CreateBitmapSourceFromHBitmap(
            hBitmap, IntPtr.Zero, Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        fallback.Freeze();
        return fallback;
    }

    private static BitmapSource? GetIconViaShGetFileInfo(string path, bool isFolder)
    {
        var shfi = new NativeMethods.SHFILEINFO();
        uint flags = NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON;
        uint fileAttr = isFolder ? 0x10u : 0u;

        var result = NativeMethods.SHGetFileInfo(path, fileAttr, ref shfi,
            (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(), flags);

        if (result != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
        {
            try
            {
                var bmp = Imaging.CreateBitmapSourceFromHIcon(
                    shfi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bmp.Freeze();
                return bmp;
            }
            finally
            {
                NativeMethods.DestroyIcon(shfi.hIcon);
            }
        }

        return null;
    }

    private static BitmapSource? GetFallbackIcon()
    {
        var shfi = new NativeMethods.SHFILEINFO();
        var result = NativeMethods.SHGetFileInfo("", 0, ref shfi,
            (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
            NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);

        if (result != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
        {
            try
            {
                var bmp = Imaging.CreateBitmapSourceFromHIcon(
                    shfi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bmp.Freeze();
                return bmp;
            }
            finally
            {
                NativeMethods.DestroyIcon(shfi.hIcon);
            }
        }

        return null;
    }
}
