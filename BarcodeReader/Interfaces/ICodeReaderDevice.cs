namespace BarcodeReader.Interfaces;

/// <summary>
/// 讀碼設備的核心操作介面
/// </summary>
public interface ICodeReaderDevice : IDisposable
{
    bool IsConnected { get; }
    bool IsGrabbing { get; }

    int CreateHandle(object deviceInfo);
    int OpenDevice();
    int CloseDevice();
    int DestroyHandle();

    int StartGrabbing();
    int StopGrabbing();
    int GetOneFrameTimeout(ref nint pData, nint pFrameInfo, int timeout);

    int SetEnumValue(string key, uint value);
    int SetFloatValue(string key, float value);
    int SetCommandValue(string key);
    int GetFloatValue(string key, ref float value);
}