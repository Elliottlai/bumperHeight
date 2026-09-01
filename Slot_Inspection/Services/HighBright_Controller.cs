using System;
using System.IO.Ports;

namespace Slot_Inspection.Services;

/// <summary>
/// HighBright �������
/// RS-232 �]�w�G9600-N-8-1
/// ���O�榡�G{channel},{brightness}\r\n
/// �G�׽d��G0~255�]�ʤ��񴫺�^
/// </summary>
public sealed class HighBright_Controller : IDisposable
{
    // �w�w �T�w�`�� �w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w
    private const int    MaxDeviceValue          = 255;
    private const string RespChannelNotDefined   = "CHANNEL not Defined!";
    private const string RespChannelNotAvailable = "CHANNEL not available!";
    private const string RespError               = "E";

    // 廠商說明：RS-232 指令發出若彼此重疊（間隔不足 300ms），控制器可能略過其中一個指令，
    // 回應會出現通用錯誤碼「E」，或殘缺回應（例如觀察到的 "1,<"）。實測過重開軟體/重連
    // COM Port 都無法清除這個狀態，必須每個指令之間至少間隔 300ms 才不會觸發。
    private const int MinCommandIntervalMs = 300;

    // �w�w ��� �w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w
    private readonly SerialPort _port;
    private bool _disposed;
    private DateTime _lastCommandUtc = DateTime.MinValue;

    // �w�w �ݩ� �w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w
    public bool   IsOpen   => _port?.IsOpen    == true;
    public string PortName => _port?.PortName  ?? string.Empty;

    // �w�w �غc�l �w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w
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

    // �w�w �s�u / �_�u �w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w
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
        try { _port.Close(); } catch { /* ���������ɪ��ҥ~ */ }
    }

    // �w�w �]�ȡ]��J 0~100 % �� �˸m 0~255�^�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w
    /// <returns>true = ���\�^���Ffalse = �q�T���~�γq�T����</returns>
    public bool SetValue(int channel, int percent)
    {
        ThrowIfNotOpen();
        WaitForMinInterval();

        int    deviceValue = ConvertPercentToDeviceValue(percent);
        string command     = $"{channel},{deviceValue}";

        try
        {
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
            _port.WriteLine(command);                   // �e�X "ch,val\r\n"

            string response = _port.ReadLine().Trim();  // Ū�^�@��

            // ���\�G�˸m�^�ǬۦP�����O���e
            if (response == command)
                return true;

            // �w�����~�^��
            if (response.StartsWith(RespChannelNotDefined)  ||
                response.StartsWith(RespChannelNotAvailable) ||
                response.StartsWith(RespError))
            {
                LastUnexpectedResponse = response;
                return false;
            }

            // �����^���]�O�d�ѥ~�� Log �l�ܡ^
            LastUnexpectedResponse = response;
            return false;
        }
        catch (TimeoutException)
        {
            LastUnexpectedResponse = $"[Timeout] cmd={command}";
            return false;
        }
        finally
        {
            _lastCommandUtc = DateTime.UtcNow;
        }
    }

    private void WaitForMinInterval()
    {
        var elapsed = DateTime.UtcNow - _lastCommandUtc;
        var remaining = TimeSpan.FromMilliseconds(MinCommandIntervalMs) - elapsed;
        if (remaining > TimeSpan.Zero)
            System.Threading.Thread.Sleep(remaining);
    }

    /// <summary>�̪�@���D�w�����^�����e�]�ѥ~�� Log �ϥΡ^</summary>
    public string LastUnexpectedResponse { get; private set; } = string.Empty;

    /// <summary>�����Ҧ��q�D�]�e�X A,0�^</summary>
    public bool TurnOffAll()
    {
        ThrowIfNotOpen();
        WaitForMinInterval();

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
        finally
        {
            _lastCommandUtc = DateTime.UtcNow;
        }
    }

    // �w�w ���� �w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w
    private static int ConvertPercentToDeviceValue(int percent)
    {
        percent = Math.Max(0, Math.Min(100, percent));
        return (int)Math.Round(percent / 100.0 * MaxDeviceValue);
    }

    private void ThrowIfNotOpen()
    {
        if (!IsOpen)
            throw new InvalidOperationException("HighBright �����|���s�u");
    }

    // �w�w Dispose �w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w�w
    public void Dispose()
    {
        if (_disposed) return;
        try { if (IsOpen) TurnOffAll(); } catch { }
        Disconnect();
        _port.Dispose();
        _disposed = true;
    }
}
