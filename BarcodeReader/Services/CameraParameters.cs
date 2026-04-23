using BarcodeReader.Interfaces;

namespace BarcodeReader.Services;

/// <summary>
/// ICameraParameters ¹ê§@
/// </summary>
public sealed class CameraParameters : ICameraParameters
{
    public float ExposureTime { get; set; }
    public float Gain { get; set; }
    public float FrameRate { get; set; }

    public void LoadFromDevice(ICodeReaderDevice device)
    {
        float value = 0f;

        if (device.GetFloatValue("ExposureTime", ref value) == 0)
            ExposureTime = value;

        if (device.GetFloatValue("Gain", ref value) == 0)
            Gain = value;

        if (device.GetFloatValue("AcquisitionFrameRate", ref value) == 0)
            FrameRate = value;
    }

    public void ApplyToDevice(ICodeReaderDevice device)
    {
        device.SetEnumValue("ExposureAuto", 0);
        device.SetFloatValue("ExposureTime", ExposureTime);

        device.SetEnumValue("GainAuto", 0);
        device.SetFloatValue("Gain", Gain);

        device.SetFloatValue("AcquisitionFrameRate", FrameRate);
    }
}