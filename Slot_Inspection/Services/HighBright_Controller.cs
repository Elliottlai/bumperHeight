using System;
using System.IO.Ports;

namespace Slot_Inspection.Services;

/// <summary>
/// HighBright 光源控制器
/// RS-232 設定：9600-N-8-1
/// 指令格式：{channel},{brightness}\r\n
/// 亮度範圍：0~255（百分比換算）
/// </summary>
public sealed class HighBright_Controller : IDisposable
{
    // ── 固定常數 ─────────────────────────────────────────────
    private const int    MaxDeviceValue          = 255;
    private const string RespChannelNotDefined   = "CHANNEL not Defined!";
    private const string RespChannelNotAvailable = "CHANNEL not available!";
    private const string RespError               = "E";

    // ── 欄位 ─────────────────────────────────────────────────
    private readonly SerialPort _port;
    private bool _disposed;

    // ── 屬性 ─────────────────────────────────────────────────
    public bool   IsOpen   => _port?.IsOpen    == true;
    public string PortName => _port?.PortName  ?? string.Empty;

    // ── 建構子 ───────────────────────────────────────────────
    public HighBright_Controller(string portName, int baudRate = 9600)
    {
        _port = new SerialPort(portName)
        {
            BaudRate     = baudRate,
            Parity       = Parity.None,
            DataBits     = 8,
            StopBits     = StopBits.One,
            ReadTimeout  = 1000,
            WriteTimeout = 1000,
            NewLine      = "\r\n"
        };
    }

    // ── 連線 / 斷線 ──────────────────────────────────────────
    public void Connect()
    {
        if (IsOpen) return;
        _port.Open();
        _port.DiscardInBuffer();
        _port.DiscardOutBuffer();
    }

    public void Disconnect()
    {
        if (!IsOpen) return;
        try { _port.Close(); } catch { /* 忽略關閉時的例外 */ }
    }

    // ── 設值（輸入 0~100 % → 裝置 0~255）────────────────────
    /// <returns>true = 成功回應；false = 通訊錯誤或通訊失敗</returns>
    public bool SetValue(int channel, int percent)
    {
        ThrowIfNotOpen();

        int    deviceValue = ConvertPercentToDeviceValue(percent);
        string command     = $"{channel},{deviceValue}";

        try
        {
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
            _port.WriteLine(command);                   // 送出 "ch,val\r\n"

            string response = _port.ReadLine().Trim();  // 讀回一行

            // 成功：裝置回傳相同的指令內容
            if (response == command)
                return true;

            // 已知錯誤回應
            if (response.StartsWith(RespChannelNotDefined)  ||
                response.StartsWith(RespChannelNotAvailable) ||
                response.StartsWith(RespError))
            {
                LastUnexpectedResponse = response;
                return false;
            }

            // 未知回應（保留供外部 Log 追蹤）
            LastUnexpectedResponse = response;
            return false;
        }
        catch (TimeoutException)
        {
            LastUnexpectedResponse = $"[Timeout] cmd={command}";
            return false;
        }
    }

    /// <summary>最近一次非預期的回應內容（供外部 Log 使用）</summary>
    public string LastUnexpectedResponse { get; private set; } = string.Empty;

    /// <summary>關閉所有通道（送出 A,0）</summary>
    public bool TurnOffAll()
    {
        ThrowIfNotOpen();

        const string command = "A,0";
        try
        {
            _port.DiscardInBuffer();
            _port.WriteLine(command);
            string response = _port.ReadLine().Trim();
            return response == command;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    // ── 內部 ─────────────────────────────────────────────────
    private static int ConvertPercentToDeviceValue(int percent)
    {
        percent = Math.Max(0, Math.Min(100, percent));
        return (int)Math.Round(percent / 100.0 * MaxDeviceValue);
    }

    private void ThrowIfNotOpen()
    {
        if (!IsOpen)
            throw new InvalidOperationException("HighBright 光源尚未連線");
    }

    // ── Dispose ───────────────────────────────────────────────
    public void Dispose()
    {
        if (_disposed) return;
        try { if (IsOpen) TurnOffAll(); } catch { }
        Disconnect();
        _port.Dispose();
        _disposed = true;
    }
}
