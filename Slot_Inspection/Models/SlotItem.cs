using System.Windows.Media;
using Slot_Inspection.ViewModels;

namespace Slot_Inspection.Models;

public sealed class SlotItem : ObservableObject
{
    private string _name = string.Empty;
    private double _value;
    private bool _isNg;
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

    public ImageSource? ImageSource
    {
        get => _imageSource;
        set => SetProperty(ref _imageSource, value);
    }
}
