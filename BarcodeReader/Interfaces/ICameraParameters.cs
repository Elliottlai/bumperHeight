namespace BarcodeReader.Interfaces;

/// <summary>
/// 相機參數存取介面
/// </summary>
public interface ICameraParameters
{
    float ExposureTime { get; set; }
    float Gain { get; set; }
    float FrameRate { get; set; }

    void LoadFromDevice(ICodeReaderDevice device);
    void ApplyToDevice(ICodeReaderDevice device);
}