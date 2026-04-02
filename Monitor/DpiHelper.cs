using System.Windows;
using System.Windows.Media;
using DesktopIconMirror.Native;

namespace DesktopIconMirror.Monitor;

public static class DpiHelper
{
    public static (double scaleX, double scaleY) GetDpiScale(Visual visual)
    {
        var source = PresentationSource.FromVisual(visual);
        if (source?.CompositionTarget != null)
        {
            return (source.CompositionTarget.TransformToDevice.M11,
                    source.CompositionTarget.TransformToDevice.M22);
        }
        return (1.0, 1.0);
    }

    public static (uint dpiX, uint dpiY) GetMonitorDpi(IntPtr hMonitor)
    {
        int hr = NativeMethods.GetDpiForMonitor(hMonitor, 0, out uint dpiX, out uint dpiY);
        return hr == 0 ? (dpiX, dpiY) : ((uint)96, (uint)96);
    }

    public static double GetScaleFactor(IntPtr hMonitor)
    {
        var (dpiX, _) = GetMonitorDpi(hMonitor);
        return dpiX / 96.0;
    }

    public static (double x, double y) MapPosition(
        double sourceX, double sourceY,
        System.Drawing.Rectangle sourceBounds,
        System.Drawing.Rectangle targetBounds,
        double sourceDpiScale, double targetDpiScale)
    {
        double relativeX = sourceX / sourceBounds.Width;
        double relativeY = sourceY / sourceBounds.Height;

        double mappedX = relativeX * targetBounds.Width / targetDpiScale;
        double mappedY = relativeY * targetBounds.Height / targetDpiScale;

        return (mappedX, mappedY);
    }
}
