using System.Diagnostics;
using System.Text;
using PLC_IO.Interfaces;
using PLC_IO.Models;

namespace PLC_IO.Services;

/// <summary>
/// 三菱 FX 系列 PLC 通訊服務
/// </summary>
public sealed class FxPlcCommunicator : IPlcCommunicator, IPlcSimulator, ICommandHandler<PlcCommand>
{
    private const int MaxPoints = 16;
    private const int MinFrameLength = 6;
    private const string ReadXCommandString = "E008CA002";
    private const string ReadYCommandString = "E008BC003";

    private readonly IBytesCommunicatable _transport;
    private readonly RequestReplyController<PlcCommand> _controller;
    private readonly PlcCommand _readXCommand;
    private readonly PlcCommand _readYCommand;
    private readonly List<byte> _receiveBuffer = [];
    private readonly object _bufferLock = new();

    private readonly bool[] _xData = new bool[MaxPoints];
    private readonly bool[] _yData = new bool[MaxPoints];

    private volatile bool _readXAnswered;
    private volatile bool _readYAnswered;
    private volatile bool _writeAnswered;

    private long _dogValue;
    private bool _disposed;

    // ★ 診斷用：記錄最近的通訊事件
    private readonly List<string> _diagLog = [];
    private readonly object _diagLock = new();

    // ★ TX/RX 摘要狀態
    private string _lastTx = "";
    private string _lastRx = "";
    private string _lastTxResult = ""; // ✓ or ✗
    private string _lastRxResult = "";
    private int _txCount;
    private int _rxCount;
    private int _errCount;
    private DateTime _lastCommTime;

    // ── 供 ViewModel 綁定的獨立屬性 ──

    public string LastTxText { get { lock (_diagLock) return _lastTx; } }
    public string LastRxText { get { lock (_diagLock) return _lastRx; } }
    public int TxCount { get { lock (_diagLock) return _txCount; } }
    public int RxCount { get { lock (_diagLock) return _rxCount; } }
    public int ErrCount { get { lock (_diagLock) return _errCount; } }

    public string CommAlive
    {
        get
        {
            lock (_diagLock)
            {
                var elapsed = DateTime.Now - _lastCommTime;
                return elapsed.TotalSeconds < 1 ? "✓ 通訊中" : $"⚠ 無回應 {elapsed.TotalSeconds:F1}s";
            }
        }
    }

    public string ErrorLog
    {
        get
        {
            lock (_diagLock)
            {
                if (_errCount == 0) return "";
                return string.Join("\n", _diagLog.TakeLast(10));
            }
        }
    }

    /// <summary>是否作為 PLC 端（模擬模式）</summary>
    public bool AsPLC { get; set; }

    public bool IsConnected => _transport.Communicatable();
    public long DogValue => _dogValue;

    public int RefreshInterval
    {
        get => _controller.CycleInterval;
        set => _controller.CycleInterval = value;
    }

    public FxPlcCommunicator(IBytesCommunicatable transport)
    {
        _transport = transport;
        _readXCommand = new PlcCommand("ReadX", BuildFrameBytes(ReadXCommandString));
        _readYCommand = new PlcCommand("ReadY", BuildFrameBytes(ReadYCommandString));

        _controller = new RequestReplyController<PlcCommand>(this) { CycleInterval = 20 };
        _controller.OnTimeout += cmd => Log($"⏱ TIMEOUT: {cmd.Command}");
        _transport.AddDataArrivalEvent(OnDataArrival);
    }

    // ── 診斷 ──

    /// <summary>取得最近的診斷訊息（最多 20 筆）</summary>
    public string GetDiagLog()
    {
        lock (_diagLock)
        {
            return string.Join("\n", _diagLog);
        }
    }

    /// <summary>取得 X/Y 狀態摘要</summary>
    public string GetStatusSummary()
    {
        var xBits = string.Join("", _xData.Take(13).Select(b => b ? "1" : "0"));
        var yBits = string.Join("", _yData.Take(11).Select(b => b ? "1" : "0"));
        return $"X[{xBits}] Y[{yBits}]";
    }

    /// <summary>取得 TX/RX 通訊摘要（適合 UI 顯示）</summary>
    public string GetDiagSummary()
    {
        lock (_diagLock)
        {
            var elapsed = DateTime.Now - _lastCommTime;
            var alive = elapsed.TotalSeconds < 1 ? "✓ 通訊中" : $"⚠ 無回應 {elapsed.TotalSeconds:F1}s";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[{alive}]  TX:{_txCount}  RX:{_rxCount}  ERR:{_errCount}");
            sb.AppendLine($"TX → {_lastTx}");
            sb.AppendLine($"RX ← {_lastRx}");

            // 有錯誤時才展開最近 log
            if (_errCount > 0)
            {
                sb.AppendLine("── 最近事件 ──");
                foreach (var line in _diagLog.TakeLast(10))
                    sb.AppendLine(line);
            }

            return sb.ToString();
        }
    }

    private void Log(string message)
    {
        Debug.WriteLine($"[FX] {message}");
        lock (_diagLock)
        {
            _diagLog.Add($"{DateTime.Now:HH:mm:ss.fff} {message}");
            while (_diagLog.Count > 30)
                _diagLog.RemoveAt(0);

            _lastCommTime = DateTime.Now;

            if (message.StartsWith("TX"))
            {
                _lastTx = message;
                _txCount++;
            }
            else if (message.StartsWith("RX") || message.StartsWith("✓"))
            {
                _lastRx = message;
                _lastRxResult = "✓";
                _rxCount++;
            }
            else if (message.StartsWith("✗") || message.StartsWith("❌") || message.StartsWith("⏱"))
            {
                _lastRxResult = "✗";
                _errCount++;
            }
        }
    }

    private static string ToHex(IEnumerable<byte> data)
        => string.Join(" ", data.Select(b => b.ToString("X2")));

    // ── IPlcCommunicator ──

    public bool GetX(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, MaxPoints);
        return _xData[index];
    }

    public bool GetY(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, MaxPoints);
        return _yData[index];
    }

    public void SetY(int index, bool value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, MaxPoints);

        // ★ FX Force 命令格式：E7/E8 + "0" + hex(bit_index) + "5E"
        // bit_index 與 _yData 陣列 index 一致（十六進制）
        // Y0  = index 0  → "E7005E"   Y7  = index 7  → "E7075E"
        // Y10 = index 8  → "E7085E"   Y11 = index 9  → "E7095E"
        // Y12 = index 10 → "E70A5E"
        string addrHex = $"0{index:X}"; // "00"~"0F"

        string data = value
            ? $"E7{addrHex}5E" + (char)3
            : $"E8{addrHex}5E" + (char)3;

        byte[] frame = BuildFrame(data);
        Log($"▶ SetY({index},{value}): {ToHex(frame)}");
        _writeAnswered = false;
        _controller.AddRequest(new PlcCommand("WriteY", frame));
    }

    // ── IPlcSimulator ──

    public void SetX(int index, bool value)
    {
        if (!AsPLC) return;
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, MaxPoints);
        _xData[index] = value;
    }

    // ── ICommandHandler<PlcCommand> ──

    public void SendCommand(PlcCommand command)
    {
        // ★ 記錄所有送出的命令（包括 ReadX/ReadY）
        Log($"TX {command.Command}: {ToHex(command.BytesCommand)}");
        _transport.Send(command.BytesCommand);
    }

    public bool CheckIsCommandAnswered(PlcCommand command)
    {
        if (!_transport.Communicatable()) return true;

        return command.Command switch
        {
            "ReadX" => _readXAnswered,
            "ReadY" => _readYAnswered,
            _ => _writeAnswered
        };
    }

    public void IdleProcess()
    {
        if (AsPLC) return;

        _readXAnswered = false;
        _controller.AddRequest(_readXCommand);
        _readYAnswered = false;
        _controller.AddRequest(_readYCommand);
    }

    // ── 資料接收處理 ──

    private void OnDataArrival()
    {
        lock (_bufferLock)
        {
            try
            {
                byte[] newData = _transport.Get();

                // ★ 記錄每次收到的原始 bytes
                if (newData.Length > 0)
                    Log($"RX raw({newData.Length}): {ToHex(newData)}");

                _receiveBuffer.AddRange(newData);

                if (AsPLC)
                    ProcessAsPLC();
                else
                    ProcessAsClient();
            }
            catch (Exception ex)
            {
                Log($"❌ ERROR: {ex.Message}");
                _receiveBuffer.Clear();
            }
        }
    }

    private void ProcessAsClient()
    {
        _dogValue = (_dogValue + 1) % 10000;

        int maxIterations = 10;

        while (_receiveBuffer.Count > 0 && maxIterations-- > 0)
        {
            // 先處理 ACK / NAK
            int stxIndex = _receiveBuffer.IndexOf(0x02);
            int ackIndex = _receiveBuffer.IndexOf(0x06);
            int nakIndex = _receiveBuffer.IndexOf(0x15);

            // ACK 在 STX 之前
            if (ackIndex >= 0 && (stxIndex < 0 || ackIndex < stxIndex))
            {
                _writeAnswered = true;
                Log("✓ ACK");
                _receiveBuffer.RemoveRange(0, ackIndex + 1);
                continue;
            }

            // ★ NAK 處理
            if (nakIndex >= 0 && (stxIndex < 0 || nakIndex < stxIndex))
            {
                _writeAnswered = true;
                Log("✗ NAK");
                _receiveBuffer.RemoveRange(0, nakIndex + 1);
                continue;
            }

            if (stxIndex < 0)
            {
                if (ackIndex >= 0)
                {
                    _writeAnswered = true;
                    _receiveBuffer.RemoveRange(0, ackIndex + 1);
                    continue;
                }
                Log($"⚠ garbage: {ToHex(_receiveBuffer)}");
                _receiveBuffer.Clear();
                break;
            }

            if (stxIndex > 0)
                _receiveBuffer.RemoveRange(0, stxIndex);

            if (_receiveBuffer.Count < MinFrameLength)
                break;

            int etxIndex = -1;
            for (int i = 1; i < _receiveBuffer.Count; i++)
            {
                if (_receiveBuffer[i] == 0x03)
                {
                    etxIndex = i;
                    break;
                }
            }

            if (etxIndex < 0 || etxIndex + 2 >= _receiveBuffer.Count)
                break;

            int frameEnd = etxIndex + 3;
            string rawHex = ToHex(_receiveBuffer.Take(frameEnd));

            if (!ValidateChecksum(0, etxIndex))
            {
                Log($"✗ CHECKSUM: {rawHex}");
                _receiveBuffer.RemoveRange(0, frameEnd);
                continue;
            }

            int dataLength = etxIndex - 1;

            if (dataLength == 4)
            {
                bool ok = TryParseXResponse(1, 4);
                Log($"✓ X: {rawHex} → {string.Join("", _xData.Take(13).Select(b => b ? "1" : "0"))}");
            }
            else if (dataLength == 6)
            {
                bool ok = TryParseYResponse(1, 6);
                Log($"✓ Y: {rawHex} → {string.Join("", _yData.Take(11).Select(b => b ? "1" : "0"))}");
            }
            else
            {
                Log($"⚠ UNKNOWN len={dataLength}: {rawHex}");
            }

            _receiveBuffer.RemoveRange(0, frameEnd);
        }
    }

    private bool TryParseXResponse(int offset, int length)
    {
        try
        {
            string hex = Encoding.ASCII.GetString(
                _receiveBuffer.GetRange(offset, length).ToArray());

            short dataInfo = Convert.ToInt16(hex, 16);
            byte[] bytes = BitConverter.GetBytes(dataInfo);

            for (int i = 0; i < 8; i++)
                _xData[i] = (bytes[1] & (1 << i)) > 0;
            for (int i = 0; i < 4; i++)
                _xData[8 + i] = (bytes[0] & (1 << i)) > 0;

            _readXAnswered = true;
            return true;
        }
        catch (Exception ex)
        {
            Log($"❌ ParseX: {ex.Message}");
            return false;
        }
    }

    /// <summary>解析 Y 回覆（6 hex chars = 3 bytes）</summary>
    private bool TryParseYResponse(int offset, int length)
    {
        try
        {
            string hex = Encoding.ASCII.GetString(
                _receiveBuffer.GetRange(offset, length).ToArray());

            // "AABBCC" → 0x00AABBCC (int32)
            // little-endian bytes: [CC, BB, AA, 00]
            // bytes[2] = AA = 第1 byte → Y0~Y7
            // bytes[1] = BB = 第2 byte → Y10~Y17  ← 原本錯用 bytes[3]
            // bytes[0] = CC = 第3 byte → Y20~Y27
            int dataInfo = Convert.ToInt32(hex, 16);
            byte[] bytes = BitConverter.GetBytes(dataInfo);

            for (int i = 0; i < 8; i++)
                _yData[i] = (bytes[2] & (1 << i)) > 0;        // Y0~Y7

            for (int i = 0; i < 4; i++)
                _yData[8 + i] = (bytes[1] & (1 << i)) > 0;   // Y10~Y13 ← 改 bytes[3]→bytes[1]

            _readYAnswered = true;
            return true;
        }
        catch (Exception ex)
        {
            Log($"❌ ParseY: {ex.Message}");
            return false;
        }
    }

    private void ProcessAsPLC()
    {
        int maxIterations = 10;

        while (_receiveBuffer.Count >= MinFrameLength && maxIterations-- > 0)
        {
            int stxIndex = _receiveBuffer.IndexOf(0x02);
            if (stxIndex < 0)
            {
                _receiveBuffer.Clear();
                break;
            }

            if (stxIndex > 0)
                _receiveBuffer.RemoveRange(0, stxIndex);

            if (_receiveBuffer.Count < MinFrameLength)
                break;

            int etxIndex = -1;
            for (int i = 1; i < _receiveBuffer.Count; i++)
            {
                if (_receiveBuffer[i] == 0x03)
                {
                    etxIndex = i;
                    break;
                }
            }

            if (etxIndex < 0 || etxIndex + 2 >= _receiveBuffer.Count)
                break;

            int frameEnd = etxIndex + 3;

            if (!ValidateChecksum(0, etxIndex))
            {
                _receiveBuffer.RemoveRange(0, frameEnd);
                continue;
            }

            string command = Encoding.ASCII.GetString(
                _receiveBuffer.GetRange(1, etxIndex - 1).ToArray());

            if (command == ReadXCommandString)
            {
                _transport.Send(BuildReadXAnswer());
            }
            else if (command == ReadYCommandString)
            {
                _transport.Send(BuildReadYAnswer());
            }
            else if (command.Length == 6
                     && (command[1] == '7' || command[1] == '8')
                     && command.EndsWith("5E"))
            {
                // ★ 位址是 2 位數八進制字串，如 "00"~"07", "10"~"12"
                // 需轉回 0-based index：octalAddr=10 → index=8
                if (int.TryParse(command.AsSpan(2, 2), out int octalAddr))
                {
                    int yIndex = (octalAddr / 10) * 8 + (octalAddr % 10);
                    bool on = command[1] == '7';

                    if (yIndex >= 0 && yIndex < MaxPoints)
                    {
                        _yData[yIndex] = on;
                        _transport.Send([0x06]);
                    }
                }
            }

            _receiveBuffer.RemoveRange(0, frameEnd);
        }
    }

    // ── FX 協定工具方法 ──

    private bool ValidateChecksum(int startIndex, int endIndex)
    {
        int sum = 0;
        for (int i = startIndex + 1; i <= endIndex; i++)
            sum += _receiveBuffer[i];

        string checksum = sum.ToString("X2");
        return checksum[^2] == (char)_receiveBuffer[endIndex + 1]
            && checksum[^1] == (char)_receiveBuffer[endIndex + 2];
    }

    private static byte[] BuildFrame(string data)
    {
        byte[] buffer = Encoding.ASCII.GetBytes(data);
        int sum = 0;
        foreach (byte b in buffer) sum += b;
        string checksum = sum.ToString("X2");
        string message = (char)2 + data + checksum[^2..];
        return Encoding.ASCII.GetBytes(message);
    }

    private static byte[] BuildFrameBytes(string commandString)
    {
        return BuildFrame(commandString + (char)3);
    }

    private byte[] BuildReadXAnswer()
    {
        byte highNibble = BuildNibble(_xData, 4);
        byte lowNibble = BuildNibble(_xData, 0);
        byte lowNibble2 = BuildNibble(_xData, 8);
        string data = $"{highNibble:X}{lowNibble:X}0{lowNibble2:X}{(char)3}";
        return BuildFrame(data);
    }

    private byte[] BuildReadYAnswer()
    {
        byte highNibble = BuildNibble(_yData, 4);
        byte lowNibble = BuildNibble(_yData, 0);
        byte lowNibble2 = BuildNibble(_yData, 8);
        string data = $"{highNibble:X}{lowNibble:X}0{lowNibble2:X}00{(char)3}";
        return BuildFrame(data);
    }

    private static byte BuildNibble(bool[] source, int offset)
    {
        byte value = 0;
        for (int i = 0; i < 4; i++)
        {
            if (source[offset + i])
                value |= (byte)(1 << i);
        }
        return value;
    }

    // ── Dispose ──

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controller.Dispose();
        if (_transport is IDisposable disposable)
            disposable.Dispose();
    }
}