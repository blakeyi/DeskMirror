using System.Windows;
using System.Windows.Controls;
using DesktopIconMirror.Monitor;
using DesktopIconMirror.Services;

namespace DesktopIconMirror.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public bool SettingsChanged { get; private set; }

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        LoadSettings();
    }

    private void LoadSettings()
    {
        var monitors = MonitorDetector.GetAllMonitors();
        MonitorComboBox.Items.Clear();
        int selectedIndex = 0;

        for (int i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            var label = $"{m.DeviceName} ({m.Bounds.Width}x{m.Bounds.Height}){(m.IsPrimary ? " [主屏]" : "")}";
            MonitorComboBox.Items.Add(new ComboBoxItem { Content = label, Tag = m.DeviceName });

            if (m.DeviceName == _settings.TargetMonitorDeviceName ||
                (string.IsNullOrEmpty(_settings.TargetMonitorDeviceName) && !m.IsPrimary))
            {
                selectedIndex = i;
            }
        }

        if (MonitorComboBox.Items.Count > 0)
            MonitorComboBox.SelectedIndex = selectedIndex;

        foreach (ComboBoxItem item in IconSizeComboBox.Items)
        {
            if (item.Tag is string tag && int.TryParse(tag, out int size) && size == _settings.IconSize)
            {
                IconSizeComboBox.SelectedItem = item;
                break;
            }
        }

        PollIntervalSlider.Value = _settings.PositionPollIntervalMs;
        AutoStartCheckBox.IsChecked = _settings.AutoStart;
        MirrorLayoutCheckBox.IsChecked = _settings.MirrorLayout;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (MonitorComboBox.SelectedItem is ComboBoxItem selectedMonitor)
            _settings.TargetMonitorDeviceName = selectedMonitor.Tag as string;

        if (IconSizeComboBox.SelectedItem is ComboBoxItem selectedSize &&
            selectedSize.Tag is string sizeStr && int.TryParse(sizeStr, out int size))
            _settings.IconSize = size;

        _settings.PositionPollIntervalMs = (int)PollIntervalSlider.Value;
        _settings.AutoStart = AutoStartCheckBox.IsChecked == true;
        _settings.MirrorLayout = MirrorLayoutCheckBox.IsChecked == true;

        SettingsService.Save(_settings);
        SettingsService.SetAutoStart(_settings.AutoStart);

        SettingsChanged = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
