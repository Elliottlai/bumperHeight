using Slot_Inspection.Models;

namespace Slot_Inspection.ViewModels;

/// <summary>
/// 亮度設定視窗的 ViewModel。
/// 持有左/右相機亮度、Value 上下極限、以及（Admin限定）曝光時間的可編輯副本，按 OK 後呼叫端再寫回 config。
/// </summary>
public sealed class LightSettingsViewModel : ObservableObject
{
    private int _lightIntensityLeft;
    private int _lightIntensityRight;
    private double _valueLimitLow;
    private double _valueLimitHigh;
    private double _exposureTimeLeft;
    private double _exposureTimeRight;

    public int LightIntensityLeft
    {
        get => _lightIntensityLeft;
        set => SetProperty(ref _lightIntensityLeft, Math.Clamp(value, 0, 100));
    }

    public int LightIntensityRight
    {
        get => _lightIntensityRight;
        set => SetProperty(ref _lightIntensityRight, Math.Clamp(value, 0, 100));
    }

    public double ValueLimitLow
    {
        get => _valueLimitLow;
        set => SetProperty(ref _valueLimitLow, value);
    }

    public double ValueLimitHigh
    {
        get => _valueLimitHigh;
        set => SetProperty(ref _valueLimitHigh, value);
    }

    /// <summary>左相機曝光時間 (µs)。僅 Admin 登入時，UI 上的曝光時間 Tab 才會顯示/可編輯。</summary>
    public double ExposureTimeLeft
    {
        get => _exposureTimeLeft;
        set => SetProperty(ref _exposureTimeLeft, Math.Max(1.0, value));
    }

    /// <summary>右相機曝光時間 (µs)。僅 Admin 登入時，UI 上的曝光時間 Tab 才會顯示/可編輯。</summary>
    public double ExposureTimeRight
    {
        get => _exposureTimeRight;
        set => SetProperty(ref _exposureTimeRight, Math.Max(1.0, value));
    }

    /// <summary>目前登入者是否為 Admin；決定曝光時間 Tab 是否顯示（由呼叫端傳入，不在此判斷角色）。</summary>
    public bool IsAdmin { get; }

    public LightSettingsViewModel(InspectionConfig config, bool isAdmin)
    {
        _lightIntensityLeft  = config.LightIntensityLeft;
        _lightIntensityRight = config.LightIntensityRight;
        _valueLimitLow       = config.ValueLimitLow;
        _valueLimitHigh      = config.ValueLimitHigh;
        _exposureTimeLeft    = config.ExposureTimeLeft;
        _exposureTimeRight   = config.ExposureTimeRight;
        IsAdmin = isAdmin;
    }

    /// <summary>將目前值寫回 InspectionConfig 並呼叫 Save()。</summary>
    public void ApplyTo(InspectionConfig config)
    {
        config.LightIntensityLeft  = LightIntensityLeft;
        config.LightIntensityRight = LightIntensityRight;
        config.ValueLimitLow       = ValueLimitLow;
        config.ValueLimitHigh      = ValueLimitHigh;
        config.ExposureTimeLeft    = ExposureTimeLeft;
        config.ExposureTimeRight   = ExposureTimeRight;
        config.Save();
    }
}
