using System.IO;
using System.IO.Ports;
using System.Threading;

namespace Machine.Core.Models.Axis.DeltaRS485
{
    /// <summary>
    /// Delta RS485 專用 Modbus RTU 通訊實作
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
                _serial.Close();
            _serial?.Dispose();
            _serial = null;
        }

        /// <summary>讀取單一暫存器 (FC03)</summary>
        public ushort ReadRegister(ushort address)
        {
            return ReadRegisters(address, 1)[0];
        }

        /// <summary>讀取多個暫存器 (FC03)</summary>
        public ushort[] ReadRegisters(ushort address, ushort count)
        {
            lock (_lock)
            {
                EnsureConnected();

                return ExecuteWithRetry(() =>
                {
                    var request = new byte[8];
                    request[0] = _slaveId;
                    request[1] = 0x03; // Function Code 03: Read Holding Registers
                    request[2] = (byte)(address >> 8);
                    request[3] = (byte)(address & 0xFF);
                    request[4] = (byte)(count >> 8);
                    request[5] = (byte)(count & 0xFF);
                    AppendCrc(request, 6);

                    SendAndFlush(request);

                    int expectedBytes = 5 + count * 2;
                    var response = ReadResponse(0x03, expectedBytes);
                    ValidateResponse(response, 0x03);

                    var values = new ushort[count];
                    for (int i = 0; i < count; i++)
                        values[i] = (ushort)((response[3 + i * 2] << 8) | response[4 + i * 2]);
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
                    var request = new byte[8];
                    request[0] = _slaveId;
                    request[1] = 0x06; // Function Code 06: Write Single Register
                    request[2] = (byte)(address >> 8);
                    request[3] = (byte)(address & 0xFF);
                    request[4] = (byte)(value >> 8);
                    request[5] = (byte)(value & 0xFF);
                    AppendCrc(request, 6);

                    SendAndFlush(request);

                    var response = ReadResponse(0x06, 8);
                    ValidateResponse(response, 0x06);
                    return true;
                }, $"WriteRegister(addr=0x{address:X4}, value=0x{value:X4})");
            }
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

                    var response = ReadResponse(0x10, 8);
                    ValidateResponse(response, 0x10);
                    return true;
                }, $"WriteRegisters(addr=0x{address:X4}, count={values.Length})");
            }
        }

        /// <summary>寫入 32-bit 數值（連續兩個暫存器, Little-Endian: Low word 在低位址）</summary>
        public void WriteRegister32(ushort address, int value)
        {
            WriteRegisters(address, new ushort[]
            {
                (ushort)(value & 0xFFFF),
                (ushort)((value >> 16) & 0xFFFF)
            });
        }

        /// <summary>讀取 32-bit 數值（連續兩個暫存器, Little-Endian: Low word 在低位址）</summary>
        public int ReadRegister32(ushort address)
        {
            var values = ReadRegisters(address, 2);
            return (values[1] << 16) | values[0];
        }

        // ============================
        //  內部輔助方法
        // ============================

        private void EnsureConnected()
        {
            if (!IsConnected)
                throw new InvalidOperationException("Modbus RTU 尚未連線，請先呼叫 Connect()");
        }

        private void SendAndFlush(byte[] request)
        {
            _serial!.DiscardInBuffer();
            // 跟 ReadWithWatchdog 同樣的理由：SerialPort.Write() 的 WriteTimeout 一樣不完全可靠，
            // 8/25 現場實測發現「只修讀取端」還是會卡死——RS485 半雙工收發器在對方裝置完全
            // 斷電/無回應時，寫入端也可能卡住，不能只顧讀取那一半。
            WriteWithWatchdog(request, 0, request.Length);
        }

        /// <summary>寫入端的強制看門狗，用法與理由同 <see cref="ReadWithWatchdog"/>。</summary>
        private void WriteWithWatchdog(byte[] buffer, int offset, int count)
        {
            using var cts = new CancellationTokenSource(ReadWatchdogMs);
            try
            {
                _serial!.BaseStream.WriteAsync(buffer, offset, count, cts.Token)
                    .GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Modbus RTU 寫入逾時（WriteTimeout 未生效，watchdog 強制中止）");
            }
        }

        /// <summary>
        /// 讀取回應封包。先讀取前 3 bytes 判斷是否為 Modbus 異常回應 (Exception Response)，
        /// 異常回應固定只有 5 bytes ([Slave][FC|0x80][ErrorCode][CRC])，
        /// 若不先判斷就照正常長度等待，遇到異常回應會因為對方根本不會送出那麼多 bytes，
        /// 導致白白等到 ReadTimeout 逾時，且原始錯誤碼也被誤判成「逾時」。
        /// </summary>
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

        /// <summary>
        /// 讀取逾時的強制看門狗上限（毫秒）。故意設得比 <see cref="SerialPort.ReadTimeout"/>（1000ms）
        /// 略長一點，正常情況下 ReadTimeout 會先生效；這個值只在 ReadTimeout 失靈時才會用到。
        /// </summary>
        private const int ReadWatchdogMs = 1500;

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
                    bytesRead = ReadWithWatchdog(buffer, offset, remaining);
                }
                catch (TimeoutException)
                {
                    throw new TimeoutException($"Modbus RTU 通訊逾時，已接收 {offset}/{length} bytes");
                }

                if (bytesRead == 0)
                    throw new IOException("Modbus RTU 通訊中斷，無回應資料");

                offset += bytesRead;
                remaining -= bytesRead;
            }
            return buffer;
        }

        /// <summary>
        /// 包一層強制逾時的讀取，不能只信任 <see cref="SerialPort.ReadTimeout"/>——實際踩過的問題：
        /// 對方裝置完全沒回應時（例如 EMO 觸發、伺服電源被切斷，軸體整個斷訊），部分 USB 轉 RS485
        /// 驅動下 <c>SerialPort.Read()</c> 有機率完全不理會 ReadTimeout，直接卡死，呼叫端永遠等不到
        /// 例外也等不到資料——會把整條呼叫鏈（包含可能在等待結果的 UI 執行緒）一起拖死到程式看起來
        /// 像當機（8/25 現場實測：CPU 掉到 0%、程式完全沒回應，重開才能恢復）。
        ///
        /// 改用 <c>BaseStream.ReadAsync</c> + <see cref="CancellationTokenSource"/>：.NET 的
        /// <c>SerialStream</c> 在 token 被取消時會呼叫 Win32 <c>CancelIoEx</c> 真正中止底層的
        /// overlapped I/O，比同步 <c>Read()</c> 的 ReadTimeout 機制可靠得多，逾時後呼叫端一定能
        /// 拿回控制權繼續走既有的重試/失敗流程，不會無限期卡住。
        /// </summary>
        private int ReadWithWatchdog(byte[] buffer, int offset, int count)
        {
            using var cts = new CancellationTokenSource(ReadWatchdogMs);
            try
            {
                return _serial!.BaseStream.ReadAsync(buffer, offset, count, cts.Token)
                    .GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Modbus RTU 讀取逾時（ReadTimeout 未生效，watchdog 強制中止）");
            }
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

        private void ValidateResponse(byte[] response, byte expectedFc)
        {
            if (response[0] != _slaveId)
                throw new IOException($"回應站號不符: 預期={_slaveId}, 實際={response[0]}");

            if (response[1] == (expectedFc | 0x80))
            {
                byte errorCode = response[2];
                throw new IOException($"Modbus 異常回應: FC=0x{expectedFc:X2}, ErrorCode=0x{errorCode:X2} ({GetErrorDescription(errorCode)})");
            }

            if (response[1] != expectedFc)
                throw new IOException($"回應 Function Code 不符: 預期=0x{expectedFc:X2}, 實際=0x{response[1]:X2}");

            ushort receivedCrc = (ushort)(response[^1] << 8 | response[^2]);
            ushort calculatedCrc = CalculateCrc(response, response.Length - 2);
            if (receivedCrc != calculatedCrc)
                throw new IOException($"CRC 校驗失敗: 預期=0x{calculatedCrc:X4}, 實際=0x{receivedCrc:X4}");
        }

        private static string GetErrorDescription(byte errorCode) => errorCode switch
        {
            0x01 => "不合法的 Function Code",
            0x02 => "不合法的暫存器位址",
            0x03 => "不合法的資料值",
            0x04 => "設備故障",
            _ => "未知錯誤"
        };

        private static void AppendCrc(byte[] buffer, int length)
        {
            ushort crc = CalculateCrc(buffer, length);
            buffer[length] = (byte)(crc & 0xFF);
            buffer[length + 1] = (byte)(crc >> 8);
        }

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
}
