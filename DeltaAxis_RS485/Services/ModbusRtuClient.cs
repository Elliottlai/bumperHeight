using System.IO;
using System.IO.Ports;
using DeltaAxis_RS485.Interfaces;

namespace DeltaAxis_RS485.Services;

/// <summary>
/// Modbus RTU 通訊實作
/// 使用 System.IO.Ports.SerialPort 進行 RS485 通訊
/// </summary>
public class ModbusRtuClient : IModbusRtuClient, IDisposable
{
    private SerialPort? _serial;
    private byte _slaveId;
    private readonly object _lock = new();

    /// <summary>是否已連線</summary>
    public bool IsConnected => _serial?.IsOpen ?? false;

    /// <summary>建立 RS485 連線</summary>
    public void Connect(string portName, int baudRate, byte slaveId)
    {
        // 確保舊連線已關閉，避免 SerialPort 重複開啟
        Disconnect();

        _slaveId = slaveId;

        _serial = new SerialPort
        {
            PortName = portName,
            BaudRate = baudRate,
            DataBits = 8,
            StopBits = StopBits.Two,
            Parity = Parity.None,
            ReadTimeout = 1000,
            WriteTimeout = 1000
        };

        try
        {
            _serial.Open();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"無法開啟 COM Port: {portName}，錯誤: {ex.Message}", ex);
        }
    }

    /// <summary>斷開連線</summary>
    public void Disconnect()
    {
        if (_serial?.IsOpen == true)
        {
            _serial.Close();
        }
        _serial?.Dispose();
        _serial = null;
    }

    /// <summary>讀取單一暫存器 (FC03)</summary>
    public ushort ReadRegister(ushort address)
    {
        var result = ReadRegisters(address, 1);
        return result[0];
    }

    /// <summary>讀取多個暫存器 (FC03)</summary>
    public ushort[] ReadRegisters(ushort address, ushort count)
    {
        lock (_lock)
        {
            EnsureConnected();

            return ExecuteWithRetry(() =>
            {
                // 組建 Modbus RTU 請求: [SlaveId][FC03][AddrHi][AddrLo][CountHi][CountLo][CRC]
                var request = new byte[8];
                request[0] = _slaveId;
                request[1] = 0x03; // Function Code 03: Read Holding Registers
                request[2] = (byte)(address >> 8);
                request[3] = (byte)(address & 0xFF);
                request[4] = (byte)(count >> 8);
                request[5] = (byte)(count & 0xFF);
                AppendCrc(request, 6);

                SendAndFlush(request);

                // 回應格式: [SlaveId][FC03][ByteCount][Data...][CRC]
                int expectedBytes = 5 + count * 2; // 1+1+1+(count*2)+2
                var response = ReadResponse(0x03, expectedBytes);

                ValidateResponse(response, 0x03);

                // 解析資料
                var values = new ushort[count];
                for (int i = 0; i < count; i++)
                {
                    values[i] = (ushort)((response[3 + i * 2] << 8) | response[4 + i * 2]);
                }
                return values;
            }, $"ReadRegisters(addr=0x{address:X4}, count={count})");
        }
    }

    /// <summary>寫入單一暫存器 (FC06)</summary>
    public void WriteRegister(ushort address, ushort value)
    {
        lock (_lock)
        {
            EnsureConnected();

            ExecuteWithRetry(() =>
            {
                // 組建請求: [SlaveId][FC06][AddrHi][AddrLo][ValueHi][ValueLo][CRC]
                var request = new byte[8];
                request[0] = _slaveId;
                request[1] = 0x06; // Function Code 06: Write Single Register
                request[2] = (byte)(address >> 8);
                request[3] = (byte)(address & 0xFF);
                request[4] = (byte)(value >> 8);
                request[5] = (byte)(value & 0xFF);
                AppendCrc(request, 6);

                SendAndFlush(request);

                // FC06 回應與請求相同
                var response = ReadResponse(0x06, 8);
                ValidateResponse(response, 0x06);
                return true;
            }, $"WriteRegister(addr=0x{address:X4}, value=0x{value:X4})");
        }
    }

    /// <summary>寫入單一暫存器 (FC06) — WriteRegister 別名，語意更明確</summary>
    public void WriteSingleRegister(ushort address, ushort value)
    {
        WriteRegister(address, value);
    }

    /// <summary>寫入多個暫存器 (FC10)</summary>
    public void WriteRegisters(ushort address, ushort[] values)
    {
        lock (_lock)
        {
            EnsureConnected();

            ExecuteWithRetry(() =>
            {
                int count = values.Length;
                int byteCount = count * 2;

                // 組建請求: [SlaveId][FC10][AddrHi][AddrLo][CountHi][CountLo][ByteCount][Data...][CRC]
                var request = new byte[9 + byteCount];
                request[0] = _slaveId;
                request[1] = 0x10; // Function Code 10: Write Multiple Registers
                request[2] = (byte)(address >> 8);
                request[3] = (byte)(address & 0xFF);
                request[4] = (byte)(count >> 8);
                request[5] = (byte)(count & 0xFF);
                request[6] = (byte)byteCount;
                for (int i = 0; i < count; i++)
                {
                    request[7 + i * 2] = (byte)(values[i] >> 8);
                    request[8 + i * 2] = (byte)(values[i] & 0xFF);
                }
                AppendCrc(request, 7 + byteCount);

                SendAndFlush(request);

                // FC10 回應: [SlaveId][FC10][AddrHi][AddrLo][CountHi][CountLo][CRC]
                var response = ReadResponse(0x10, 8);
                ValidateResponse(response, 0x10);
                return true;
            }, $"WriteRegisters(addr=0x{address:X4}, count={values.Length})");
        }
    }

    /// <summary>寫入 32-bit 數值（佔兩個連續暫存器，Little-Endian: Low word 在低位址）</summary>
    public void WriteRegister32(ushort address, int value)
    {
        var values = new ushort[2];
        values[0] = (ushort)(value & 0xFFFF);          // Low word → 低位址
        values[1] = (ushort)((value >> 16) & 0xFFFF);  // High word → 高位址
        WriteRegisters(address, values);
    }

    /// <summary>讀取 32-bit 數值（佔兩個連續暫存器，Little-Endian: Low word 在低位址）</summary>
    public int ReadRegister32(ushort address)
    {
        var values = ReadRegisters(address, 2);
        return (values[1] << 16) | values[0];
    }

    // ============================
    //  內部輔助方法
    // ============================

    /// <summary>確認連線狀態</summary>
    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Modbus RTU 尚未連線，請先呼叫 Connect()");
    }

    /// <summary>送出資料並清空接收緩衝區</summary>
    private void SendAndFlush(byte[] request)
    {
        _serial!.DiscardInBuffer();
        _serial.Write(request, 0, request.Length);
    }

    /// <summary>
    /// 讀取回應封包。先讀取前 3 bytes 判斷是否為 Modbus 異常回應 (Exception Response)，
    /// 異常回應固定只有 5 bytes ([Slave][FC|0x80][ErrorCode][CRC])，
    /// 若不先判斷就照正常長度等待，遇到異常回應會因為對方根本不會送出那麼多 bytes，
    /// 導致白白等到 ReadTimeout 逾時，且原始錯誤碼也被誤判成「逾時」。
    /// </summary>
    /// <param name="expectedFc">本次請求預期的 Function Code</param>
    /// <param name="normalLength">正常(非異常)回應的完整長度</param>
    private byte[] ReadResponse(byte expectedFc, int normalLength)
    {
        var header = ReadExact(3);

        bool isException = header[1] == (byte)(expectedFc | 0x80);
        int totalLength = isException ? 5 : normalLength;

        var response = new byte[totalLength];
        Array.Copy(header, response, 3);

        if (totalLength > 3)
        {
            var rest = ReadExact(totalLength - 3);
            Array.Copy(rest, 0, response, 3, rest.Length);
        }

        return response;
    }

    /// <summary>從序列埠精確讀取指定長度的 bytes，不足則持續等待直到逾時</summary>
    private byte[] ReadExact(int length)
    {
        var buffer = new byte[length];
        int offset = 0;
        int remaining = length;

        while (remaining > 0)
        {
            int bytesRead;
            try
            {
                bytesRead = _serial!.Read(buffer, offset, remaining);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Modbus RTU 通訊逾時，已接收 {offset}/{length} bytes");
            }

            if (bytesRead == 0)
                throw new IOException("Modbus RTU 通訊中斷，未收到回應");

            offset += bytesRead;
            remaining -= bytesRead;
        }

        return buffer;
    }

    /// <summary>
    /// 重試包裝：針對暫時性通訊錯誤 (CRC失敗/逾時/中斷) 自動重試，
    /// 不重試由 EnsureConnected() 等邏輯丟出的 InvalidOperationException。
    /// </summary>
    private const int MaxRetryAttempts = 3;

    private T ExecuteWithRetry<T>(Func<T> action, string opDescription)
    {
        Exception? lastError = null;
        for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (ex is IOException or TimeoutException)
            {
                lastError = ex;
                if (attempt < MaxRetryAttempts)
                    Thread.Sleep(20 * attempt);
            }
        }

        throw new IOException($"Modbus RTU {opDescription} 失敗，已重試 {MaxRetryAttempts} 次仍失敗: {lastError!.Message}", lastError);
    }

    /// <summary>驗證回應資料（檢查站號、FC、CRC）</summary>
    private void ValidateResponse(byte[] response, byte expectedFc)
    {
        // 檢查站號
        if (response[0] != _slaveId)
            throw new IOException($"回應站號不符: 預期={_slaveId}, 實際={response[0]}");

        // 檢查是否為異常回應 (FC + 0x80)
        if (response[1] == (expectedFc | 0x80))
        {
            byte errorCode = response[2];
            throw new IOException($"Modbus 異常回應: FC=0x{expectedFc:X2}, ErrorCode=0x{errorCode:X2} ({GetErrorDescription(errorCode)})");
        }

        // 檢查 FC
        if (response[1] != expectedFc)
            throw new IOException($"回應 Function Code 不符: 預期=0x{expectedFc:X2}, 實際=0x{response[1]:X2}");

        // 驗證 CRC
        ushort receivedCrc = (ushort)(response[^1] << 8 | response[^2]);
        ushort calculatedCrc = CalculateCrc(response, response.Length - 2);
        if (receivedCrc != calculatedCrc)
            throw new IOException($"CRC 校驗失敗: 預期=0x{calculatedCrc:X4}, 實際=0x{receivedCrc:X4}");
    }

    /// <summary>Modbus 異常碼說明</summary>
    private static string GetErrorDescription(byte errorCode) => errorCode switch
    {
        0x01 => "不合法的 Function Code",
        0x02 => "不合法的暫存器位址",
        0x03 => "不合法的資料值",
        0x04 => "設備故障",
        _ => "未知錯誤"
    };

    /// <summary>在封包尾端加上 CRC-16 (Modbus)</summary>
    private static void AppendCrc(byte[] buffer, int length)
    {
        ushort crc = CalculateCrc(buffer, length);
        buffer[length] = (byte)(crc & 0xFF);        // CRC Low
        buffer[length + 1] = (byte)(crc >> 8);       // CRC High
    }

    /// <summary>計算 CRC-16 (Modbus RTU)</summary>
    private static ushort CalculateCrc(byte[] buffer, int length)
    {
        ushort crc = 0xFFFF;
        for (int i = 0; i < length; i++)
        {
            crc ^= buffer[i];
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x0001) != 0)
                    crc = (ushort)((crc >> 1) ^ 0xA001);
                else
                    crc >>= 1;
            }
        }
        return crc;
    }

    /// <summary>釋放資源</summary>
    public void Dispose()
    {
        Disconnect();
        GC.SuppressFinalize(this);
    }
}