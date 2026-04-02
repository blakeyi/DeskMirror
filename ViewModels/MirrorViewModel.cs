using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopIconMirror.Models;
using DesktopIconMirror.Monitor;
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
            Process.Start(new ProcessStartInfo(icon.TargetPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open {icon.TargetPath}: {ex.Message}");
        }
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
