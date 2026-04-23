using System.Runtime.InteropServices;
using BarcodeReader.Interfaces;
using MvCodeReaderSDKNet;

namespace BarcodeReader.Services;

/// <summary>
/// MvCodeReaderSDKNet ªº ICodeReaderDevice ¹ê§@
/// </summary>
public sealed class MvCodeReaderDevice : ICodeReaderDevice
{
    private MvCodeReader? _device;
    private bool _disposed;

    public bool IsConnected { get; private set; }
    public bool IsGrabbing { get; private set; }

    public int CreateHandle(object deviceInfo)
    {
        _device ??= new MvCodeReader();

        if (deviceInfo is not MvCodeReader.MV_CODEREADER_DEVICE_INFO devInfo)
            return MvCodeReader.MV_CODEREADER_E_PARAMETER;

        return _device.MV_CODEREADER_CreateHandle_NET(ref devInfo);
    }

    public int OpenDevice()
    {
        ArgumentNullException.ThrowIfNull(_device);
        int ret = _device.MV_CODEREADER_OpenDevice_NET();
        if (ret == MvCodeReader.MV_CODEREADER_OK)
            IsConnected = true;
        return ret;
    }

    public int CloseDevice()
    {
        ArgumentNullException.ThrowIfNull(_device);
        if (IsGrabbing)
            StopGrabbing();

        int ret = _device.MV_CODEREADER_CloseDevice_NET();
        if (ret == MvCodeReader.MV_CODEREADER_OK)
            IsConnected = false;
        return ret;
    }

    public int DestroyHandle()
    {
        ArgumentNullException.ThrowIfNull(_device);
        int ret = _device.MV_CODEREADER_DestroyHandle_NET();
        IsConnected = false;
        return ret;
    }

    public int StartGrabbing()
    {
        ArgumentNullException.ThrowIfNull(_device);
        int ret = _device.MV_CODEREADER_StartGrabbing_NET();
        if (ret == MvCodeReader.MV_CODEREADER_OK)
            IsGrabbing = true;
        return ret;
    }

    public int StopGrabbing()
    {
        ArgumentNullException.ThrowIfNull(_device);
        int ret = _device.MV_CODEREADER_StopGrabbing_NET();
        if (ret == MvCodeReader.MV_CODEREADER_OK)
            IsGrabbing = false;
        return ret;
    }

    public int GetOneFrameTimeout(ref nint pData, nint pFrameInfo, int timeout)
    {
        ArgumentNullException.ThrowIfNull(_device);
        return _device.MV_CODEREADER_GetOneFrameTimeoutEx2_NET(ref pData, pFrameInfo, (uint)timeout);
    }

    public int SetEnumValue(string key, uint value)
    {
        ArgumentNullException.ThrowIfNull(_device);
        return _device.MV_CODEREADER_SetEnumValue_NET(key, value);
    }

    public int SetFloatValue(string key, float value)
    {
        ArgumentNullException.ThrowIfNull(_device);
        return _device.MV_CODEREADER_SetFloatValue_NET(key, value);
    }

    public int SetCommandValue(string key)
    {
        ArgumentNullException.ThrowIfNull(_device);
        return _device.MV_CODEREADER_SetCommandValue_NET(key);
    }

    public int GetFloatValue(string key, ref float value)
    {
        ArgumentNullException.ThrowIfNull(_device);
        MvCodeReader.MV_CODEREADER_FLOATVALUE param = new();
        int ret = _device.MV_CODEREADER_GetFloatValue_NET(key, ref param);
        if (ret == MvCodeReader.MV_CODEREADER_OK)
            value = param.fCurValue;
        return ret;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (IsGrabbing) StopGrabbing();
        if (IsConnected) CloseDevice();
        DestroyHandle();
    }
}