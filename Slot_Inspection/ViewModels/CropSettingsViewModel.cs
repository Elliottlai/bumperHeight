using Slot_Inspection.Models;

namespace Slot_Inspection.ViewModels;

/// <summary>單一相機 ROI 的可觀察包裝，供裁切設定 UI 繫結。</summary>
public sealed class CameraRoiViewModel : ObservableObject
{
    private int _x;
    private int _y;
    private int _w;
    private int _h;
    private bool _enabled;

    public string CameraId { get; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            SetProperty(ref _enabled, value);
            OnPropertyChanged(nameof(InputEnabled));
        }
    }

    /// <summary>輸入框是否可編輯（啟用裁切時才可輸入）</summary>
    public bool InputEnabled => _enabled;

    public int X { get => _x; set => SetProperty(ref _x, value); }
    public int Y { get => _y; set => SetProperty(ref _y, value); }
    public int W { get => _w; set => SetProperty(ref _w, value); }
    public int H { get => _h; set => SetProperty(ref _h, value); }

    public CameraRoiViewModel(string cameraId, CameraRoi roi)
    {
        CameraId = cameraId;
        _enabled = roi.IsEnabled;
        _x = roi.X < 0 ? 0 : roi.X;
        _y = roi.Y < 0 ? 0 : roi.Y;
        _w = roi.W < 0 ? 0 : roi.W;
        _h = roi.H < 0 ? 0 : roi.H;
    }

    /// <summary>套用回 CameraRoi Model。</summary>
    public void ApplyTo(CameraRoi roi)
    {
        if (_enabled)
        {
            roi.X = _x;
            roi.Y = _y;
            roi.W = _w;
            roi.H = _h;
        }
        else
        {
            roi.X = -1;
            roi.Y = -1;
            roi.W = -1;
            roi.H = -1;
        }
    }
}

/// <summary>裁切設定視窗的 ViewModel（C1~C4 各自獨立 ROI）。</summary>
public sealed class CropSettingsViewModel : ObservableObject
{
    public CameraRoiViewModel C1 { get; }
    public CameraRoiViewModel C2 { get; }
    public CameraRoiViewModel C3 { get; }
    public CameraRoiViewModel C4 { get; }

    public CropSettingsViewModel(InspectionConfig config)
    {
        C1 = new CameraRoiViewModel("C1", config.RoiC1);
        C2 = new CameraRoiViewModel("C2", config.RoiC2);
        C3 = new CameraRoiViewModel("C3", config.RoiC3);
        C4 = new CameraRoiViewModel("C4", config.RoiC4);
    }

    /// <summary>將所有 ROI 套用回 InspectionConfig。</summary>
    public void ApplyTo(InspectionConfig config)
    {
        C1.ApplyTo(config.RoiC1);
        C2.ApplyTo(config.RoiC2);
        C3.ApplyTo(config.RoiC3);
        C4.ApplyTo(config.RoiC4);
    }
}
