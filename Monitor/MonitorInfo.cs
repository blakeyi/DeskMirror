using System.Windows.Forms;

namespace DesktopIconMirror.Monitor;

public record MonitorData
{
    public string DeviceName { get; init; } = "";
    public System.Drawing.Rectangle Bounds { get; init; }
    public System.Drawing.Rectangle WorkingArea { get; init; }
    public bool IsPrimary { get; init; }
}

public static class MonitorDetector
{
    public static List<MonitorData> GetAllMonitors()
    {
        return Screen.AllScreens.Select(s => new MonitorData
        {
            DeviceName = s.DeviceName,
            Bounds = s.Bounds,
            WorkingArea = s.WorkingArea,
            IsPrimary = s.Primary
        }).ToList();
    }

    public static MonitorData? GetPrimaryMonitor() =>
        GetAllMonitors().FirstOrDefault(m => m.IsPrimary);

    public static MonitorData? GetFirstSecondaryMonitor() =>
        GetAllMonitors().FirstOrDefault(m => !m.IsPrimary);

    public static List<MonitorData> GetSecondaryMonitors() =>
        GetAllMonitors().Where(m => !m.IsPrimary).ToList();
}
