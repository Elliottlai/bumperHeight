using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using BarcodeReader.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MvCodeReaderSDKNet;

namespace BarcodeReader.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceEnumerator _enumerator;
    private readonly ICodeReaderDevice _device;
    private readonly IBarcodeResultParser _parser;
    private readonly ICameraParameters _cameraParams;
    private readonly IImageRenderer _renderer;

    private Thread? _receiveThread;
    private volatile bool _grabbing;
    private readonly byte[] _imageBuffer = new byte[1024 * 1024 * 20];

    public MainViewModel(
        IDeviceEnumerator enumerator,
        ICodeReaderDevice device,
        IBarcodeResultParser parser,
        ICameraParameters cameraParams,
        IImageRenderer renderer)
    {
        _enumerator = enumerator;
        _device = device;
        _parser = parser;
        _cameraParams = cameraParams;
        _renderer = renderer;
    }

    // ── 可觀察屬性 ──

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenDeviceCommand))]
    private DeviceInfoModel? _selectedDevice;

    [ObservableProperty]
    private float _exposureTime;

    [ObservableProperty]
    private float _gain;

    [ObservableProperty]
    private float _frameRate;

    [ObservableProperty]
    private bool _isTriggerMode;

    [ObservableProperty]
    private bool _isSoftTrigger;

    public ObservableCollection<DeviceInfoModel> Devices { get; } = [];
    public ObservableCollection<BarcodeResult> BarcodeResults { get; } = [];

    // ── 狀態屬性 ──

    public bool IsConnected => _device.IsConnected;
    public bool IsGrabbing => _grabbing;

    // ── 命令 ──

    [RelayCommand]
    private void EnumerateDevices()
    {
        Devices.Clear();
        foreach (var dev in _enumerator.EnumerateDevices())
            Devices.Add(dev);

        if (Devices.Count > 0)
            SelectedDevice = Devices[0];
    }

    [RelayCommand(CanExecute = nameof(CanOpenDevice))]
    private void OpenDevice()
    {
        if (SelectedDevice?.RawDeviceInfo is not MvCodeReader.MV_CODEREADER_DEVICE_INFO)
            return;

        int ret = _device.CreateHandle(SelectedDevice.RawDeviceInfo);
        if (ret != 0) return;

        ret = _device.OpenDevice();
        if (ret != 0) { _device.DestroyHandle(); return; }

        // 預設連續模式
        _device.SetEnumValue("TriggerMode", (uint)MvCodeReader.MV_CODEREADER_TRIGGER_MODE.MV_CODEREADER_TRIGGER_MODE_OFF);

        LoadParameters();
        NotifyStateChanged();
    }

    private bool CanOpenDevice() => SelectedDevice is not null && !IsConnected;

    [RelayCommand]
    private void CloseDevice()
    {
        if (_grabbing) StopGrab();
        _device.CloseDevice();
        _device.DestroyHandle();
        NotifyStateChanged();
    }

    [RelayCommand]
    private void StartGrab()
    {
        BarcodeResults.Clear();
        _grabbing = true;

        _receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
        _receiveThread.Start();

        int ret = _device.StartGrabbing();
        if (ret != 0)
        {
            _grabbing = false;
            _receiveThread.Join();
        }

        NotifyStateChanged();
    }

    [RelayCommand]
    private void StopGrab()
    {
        _grabbing = false;
        _device.StopGrabbing();
        _receiveThread?.Join();
        NotifyStateChanged();
    }

    [RelayCommand]
    private void TriggerOnce()
    {
        _device.SetCommandValue("TriggerSoftware");
    }

    [RelayCommand]
    private void LoadParameters()
    {
        _cameraParams.LoadFromDevice(_device);
        ExposureTime = _cameraParams.ExposureTime;
        Gain = _cameraParams.Gain;
        FrameRate = _cameraParams.FrameRate;
    }

    [RelayCommand]
    private void ApplyParameters()
    {
        _cameraParams.ExposureTime = ExposureTime;
        _cameraParams.Gain = Gain;
        _cameraParams.FrameRate = FrameRate;
        _cameraParams.ApplyToDevice(_device);
    }

    // ── 觸發模式切換 ──

    partial void OnIsTriggerModeChanged(bool value)
    {
        uint mode = value
            ? (uint)MvCodeReader.MV_CODEREADER_TRIGGER_MODE.MV_CODEREADER_TRIGGER_MODE_ON
            : (uint)MvCodeReader.MV_CODEREADER_TRIGGER_MODE.MV_CODEREADER_TRIGGER_MODE_OFF;
        _device.SetEnumValue("TriggerMode", mode);
    }

    partial void OnIsSoftTriggerChanged(bool value)
    {
        uint source = value
            ? (uint)MvCodeReader.MV_CODEREADER_TRIGGER_SOURCE.MV_CODEREADER_TRIGGER_SOURCE_SOFTWARE
            : (uint)MvCodeReader.MV_CODEREADER_TRIGGER_SOURCE.MV_CODEREADER_TRIGGER_SOURCE_LINE0;
        _device.SetEnumValue("TriggerSource", source);
    }

    // ── 取像迴圈 ──

    private void ReceiveLoop()
    {
        nint pData = 0;
        var stFrameInfo = new MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2();
        nint pFrameInfo = Marshal.AllocHGlobal(Marshal.SizeOf<MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2>());
        Marshal.StructureToPtr(stFrameInfo, pFrameInfo, false);

        try
        {
            while (_grabbing)
            {
                int ret = _device.GetOneFrameTimeout(ref pData, pFrameInfo, 1000);
                if (ret != 0) continue;

                stFrameInfo = Marshal.PtrToStructure<MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2>(pFrameInfo);
                if (stFrameInfo.nFrameLen <= 0) continue;

                Marshal.Copy(pData, _imageBuffer, 0, (int)stFrameInfo.nFrameLen);

                // 判斷像素格式並渲染
                var pixelFormat = stFrameInfo.enPixelType switch
                {
                    MvCodeReader.MvCodeReaderGvspPixelType.PixelType_CodeReader_Gvsp_Mono8 => ImagePixelFormat.Mono8,
                    MvCodeReader.MvCodeReaderGvspPixelType.PixelType_CodeReader_Gvsp_Jpeg => ImagePixelFormat.Jpeg,
                    _ => ImagePixelFormat.Jpeg
                };

                // UI 執行緒渲染
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    _renderer.RenderImage(_imageBuffer, stFrameInfo.nWidth, stFrameInfo.nHeight, pixelFormat);

                    // 解析條碼
                    var barcodes = _parser.Parse(pFrameInfo);
                    foreach (var bc in barcodes)
                    {
                        // 繪製條碼區域
                        _renderer.DrawBarcodeRegion(bc.Points);
                        BarcodeResults.Insert(0, bc);
                    }

                    // 運單區域
                    var wayList = Marshal.PtrToStructure<MvCodeReader.MV_CODEREADER_WAYBILL_LIST>(stFrameInfo.pstWaybillList);
                    for (int i = 0; i < wayList.nWaybillNum; i++)
                    {
                        var w = wayList.stWaybillInfo[i];
                        _renderer.DrawWaybillRegion(w.fCenterX, w.fCenterY, w.fWidth, w.fHeight, w.fAngle);
                    }

                    // OCR 區域
                    var ocrList = Marshal.PtrToStructure<MvCodeReader.MV_CODEREADER_OCR_INFO_LIST>(stFrameInfo.UnparsedOcrList.pstOcrList);
                    for (int i = 0; i < ocrList.nOCRAllNum; i++)
                    {
                        var o = ocrList.stOcrRowInfo[i];
                        _renderer.DrawOcrRegion(o.nOcrRowCenterX, o.nOcrRowCenterY, o.nOcrRowWidth, o.nOcrRowHeight, o.fOcrRowAngle);
                    }

                    _renderer.Refresh();
                });
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pFrameInfo);
        }
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsGrabbing));
        OpenDeviceCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_grabbing) StopGrab();
        _device.Dispose();
        GC.SuppressFinalize(this);
    }
}