using System.Windows;
using System.Windows.Media;
using Slot_Inspection.ViewModels;

namespace Slot_Inspection.Models;

public sealed class SlotItem : ObservableObject
{
    private string _name = string.Empty;
    private double _value;
    private bool _isNg;
    private double _meanIntensity;
    private double _distanceToRoiCenter;
    private IReadOnlyList<Rect> _defectBoxes = [];
    private ImageSource? _imageSource;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public double Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public bool IsNg
    {
        get => _isNg;
        set => SetProperty(ref _isNg, value);
    }

    public double MeanIntensity
    {
        get => _meanIntensity;
        set => SetProperty(ref _meanIntensity, value);
    }

    public double DistanceToRoiCenter
    {
        get => _distanceToRoiCenter;
        set => SetProperty(ref _distanceToRoiCenter, value);
    }

    public IReadOnlyList<Rect> DefectBoxes
    {
        get => _defectBoxes;
        set => SetProperty(ref _defectBoxes, value);
    }

    public ImageSource? ImageSource
    {
        get => _imageSource;
        set => SetProperty(ref _imageSource, value);
    }
}
