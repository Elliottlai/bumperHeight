namespace BarcodeReader.Interfaces;

/// <summary>
/// 設備列舉介面
/// </summary>
public interface IDeviceEnumerator
{
    IReadOnlyList<DeviceInfoModel> EnumerateDevices();
}

public class DeviceInfoModel
{
    public string UserDefinedName { get; set; } = string.Empty;
    public string ManufacturerName { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public object? RawDeviceInfo { get; set; }
}