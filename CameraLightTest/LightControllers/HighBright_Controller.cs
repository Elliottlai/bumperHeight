using System;
using System.IO.Ports;

namespace CameraLightTest.LightControllers
{
    /// <summary>
    /// HighBright 光源控制器
    /// RS-232 協定：9600-N-8-1
    /// 指令格式：{channel},{brightness}\r\n
    /// 亮度範圍：0~255（由百分比換算）
    /// </summary>
    public sealed class HighBright_Controller : IDisposable
    {
        // ── 協定常數 ──────────────────────────────────────
        private const int MaxDeviceValue = 255;
        private const string RespChannelNotDefined   = "CHANNEL not Defined!";
        private const string RespChannelNotAvailable = "CHANNEL not available!";
        private const string RespError               = "E";

        // ── 欄位 ─────────────────────────────────────────
        private readonly SerialPort _port;
        private bool _disposed;

        // ── 屬性 ─────────────────────────────────────────
        public bool IsOpen => _port?.IsOpen == true;
        public string PortName => _port?.PortName ?? string.Empty;

        // ── 建構子 ───────────────────────────────────────
        public HighBright_Controller(string portName, int baudRate = 9600)
        {
            _port = new SerialPort(portName)
            {
                BaudRate  = baudRate,
                Parity    = Parity.None,
                DataBits  = 8,
                StopBits  = StopBits.One,
                ReadTimeout  = 1000,
                WriteTimeout = 1000,
                NewLine   = "\r\n"
            };
        }

        // ── 連線 / 斷線 ──────────────────────────────────
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
            try { _port.Close(); } catch { /* 忽略關閉例外 */ }
        }

        // ── 設定亮度（百分比 0~100 → 裝置值 0~255）────────
        /// <returns>true = 控制器回應成功；false = 通道錯誤或通訊失敗</returns>
        public bool SetValue(int channel, int percent)
        {
            ThrowIfNotOpen();

            int deviceValue = ConvertPercentToDeviceValue(percent);
            string command  = $"{channel},{deviceValue}";

            try
            {
                _port.DiscardInBuffer();
                _port.WriteLine(command);                  // 送出 "ch,val\r\n"

                string response = _port.ReadLine().Trim(); // 讀回一行

                // 成功：控制器回傳相同指令內容
                if (response == command)
                    return true;

                // 已知錯誤回應
                if (response.StartsWith(RespChannelNotDefined) ||
                    response.StartsWith(RespChannelNotAvailable) ||
                    response.StartsWith(RespError))
                    return false;

                // 其他未知回應視為失敗
                return false;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        /// <summary>關閉所有通道（送出 A,0）</summary>
        /// <returns>true = 成功</returns>
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

        // ── 換算 ─────────────────────────────────────────
        private static int ConvertPercentToDeviceValue(int percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            return (int)Math.Round(percent / 100.0 * MaxDeviceValue);
        }

        // ── 輔助 ─────────────────────────────────────────
        private void ThrowIfNotOpen()
        {
            if (!IsOpen)
                throw new InvalidOperationException("HighBright 控制器尚未連線");
        }

        // ── Dispose ──────────────────────────────────────
        public void Dispose()
        {
            if (_disposed) return;
            try { if (IsOpen) TurnOffAll(); } catch { }
            Disconnect();
            _port.Dispose();
            _disposed = true;
        }
    }
}