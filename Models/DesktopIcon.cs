using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DeskMirror.Models;

public partial class DesktopIcon : ObservableObject
{
    private string _name = "";
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    private string _targetPath = "";
    public string TargetPath { get => _targetPath; set => SetProperty(ref _targetPath, value); }

    private BitmapSource? _icon;
    public BitmapSource? Icon { get => _icon; set => SetProperty(ref _icon, value); }

    private double _positionX;
    public double PositionX { get => _positionX; set => SetProperty(ref _positionX, value); }

    private double _positionY;
    public double PositionY { get => _positionY; set => SetProperty(ref _positionY, value); }

    private double _mirrorX;
    public double MirrorX { get => _mirrorX; set => SetProperty(ref _mirrorX, value); }

    private double _mirrorY;
    public double MirrorY { get => _mirrorY; set => SetProperty(ref _mirrorY, value); }

    private bool _isFolder;
    public bool IsFolder { get => _isFolder; set => SetProperty(ref _isFolder, value); }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}
