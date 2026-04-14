using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Threading.Tasks;

namespace LightControl
{
    // ---------------------------------------------------------
    // Channel State Model
    // ---------------------------------------------------------
    public class ChannelState
    {
        public byte Brightness1;
        public byte Brightness2;
        public ushort Current1;
        public ushort Current2;
    }

    // ---------------------------------------------------------
    // Main Controller (LSG 2CH UART Protocol)
    // ---------------------------------------------------------
    public class VLP_LSG2CH
    {
        private SerialPort _port;
        private readonly List<byte> _rxBuffer = new List<byte>();

        // async TaskCompletionSource 用來等待回應
        private TaskCompletionSource<ChannelState> _tcsState;

        // ---------------------------------------------------------
        // Connect / Disconnect
        // ---------------------------------------------------------
        public void Connect(string portName, int baud = 115200)
        {
            _port = new SerialPort(portName, baud, Parity.None, 8, StopBits.One);
            _port.DataReceived += OnDataReceived;
            _port.Open();
        }

        public void Disconnect()
        {
            if (_port != null && _port.IsOpen)
            {
                _port.DataReceived -= OnDataReceived;
                _port.Close();
                _port.Dispose();
            }
        }

        // ---------------------------------------------------------
        // Public API
        // ---------------------------------------------------------

        // ★ 取得目前所有狀態（CH1/CH2 亮度與電流）
        public async Task<ChannelState> GetCurrentStateAsync()
        {
            _tcsState = new TaskCompletionSource<ChannelState>();

            SendPacket(BuildPacket(new byte[] { 0x02 })); // Command 0x02 = Get Brightness + Current

            return await _tcsState.Task;
        }

        // ★ 設定單通道亮度（會自動保護另一通道）
        public async Task SetBrightnessAsync(int channel, byte brightness)
        {
            if (channel != 1 && channel != 2)
                throw new ArgumentException("channel must be 1 or 2");

            ChannelState old = await GetCurrentStateAsync();

            byte ch1 = old.Brightness1;
            byte ch2 = old.Brightness2;

            if (channel == 1) ch1 = brightness;
            else ch2 = brightness;

            SendPacket(BuildPacket(new byte[]
            {
                0x1F, // Command: Set Brightness
                ch1,
                ch2
            }));
        }

        // ★ 設定單通道亮度 + 電流（會自動保護另一通道）
        public async Task SetBrightnessCurrentAsync(int channel, byte brightness, ushort current)
        {
            if (channel != 1 && channel != 2)
                throw new ArgumentException("channel must be 1 or 2");

            ChannelState old = await GetCurrentStateAsync();

            byte b1 = old.Brightness1;
            byte b2 = old.Brightness2;
            ushort c1 = old.Current1;
            ushort c2 = old.Current2;

            if (channel == 1)
            {
                b1 = brightness;
                c1 = current;
            }
            else
            {
                b2 = brightness;
                c2 = current;
            }

            SendPacket(BuildPacket(new byte[]
            {
                0x01,         // Command: Set Brightness + Current
                b1, b2,       // CH1, CH2 Brightness
                (byte)(c1 & 0xFF), (byte)(c1 >> 8),
                (byte)(c2 & 0xFF), (byte)(c2 >> 8)
            }));
        }

        // ---------------------------------------------------------
        // UART Packet Builder (符合 PDF 所有規範)
        // ---------------------------------------------------------
        private byte[] BuildPacket(byte[] data)
        {
            List<byte> packet = new List<byte>();
            packet.Add(0x02); // STX

            ushort length = (ushort)data.Length;
            packet.Add((byte)(length & 0xFF));        // Length Low
            packet.Add((byte)((length >> 8) & 0xFF)); // Length High

            packet.AddRange(data);

            ushort checksum = CalculateChecksum(data);
            packet.Add((byte)(checksum & 0xFF));        // Checksum Low
            packet.Add((byte)((checksum >> 8) & 0xFF)); // Checksum High

            return packet.ToArray();
        }

        // PDF p4: Checksum = sum of all DATA bytes (16 bit)
        private ushort CalculateChecksum(byte[] data)
        {
            uint sum = 0;
            foreach (byte b in data)
                sum += b;
            return (ushort)(sum & 0xFFFF);
        }

        // ---------------------------------------------------------
        // Send Raw UART Packet
        // ---------------------------------------------------------
        private void SendPacket(byte[] packet)
        {
            if (_port != null && _port.IsOpen)
            {
                _port.Write(packet, 0, packet.Length);
            }
        }

        // ---------------------------------------------------------
        // Receive & Parse
        // ---------------------------------------------------------
        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            int bytes = _port.BytesToRead;
            byte[] buf = new byte[bytes];
            _port.Read(buf, 0, bytes);
            Debug.WriteLine("RX Raw: " + BitConverter.ToString(buf));
            // 加入 buffer
            _rxBuffer.AddRange(buf);

            // 持續嘗試拆封包
            TryParsePacket();
        }

        private void TryParsePacket()
        {
            while (true)
            {
                if (_rxBuffer.Count < 5)
                    return;

                // STX check
                if (_rxBuffer[0] != 0x02)
                {
                    _rxBuffer.RemoveAt(0);
                    continue;
                }

                // 讀 Length
                ushort len = (ushort)(_rxBuffer[1] | (_rxBuffer[2] << 8));

                int fullLength = 1 + 2 + len + 2; // STX + Length + DATA + Checksum

                if (_rxBuffer.Count < fullLength)
                    return;

                // 取一包
                byte[] packet = _rxBuffer.GetRange(0, fullLength).ToArray();
                _rxBuffer.RemoveRange(0, fullLength);

                ParsePacket(packet);
            }
        }

        private void ParsePacket(byte[] packet)
        {
            byte stx = packet[0];
            ushort len = (ushort)(packet[1] | (packet[2] << 8));

            // 取 DATA
            byte[] data = new byte[len];
            Array.Copy(packet, 3, data, 0, len);

            // Checksum
            ushort checksumRecv = (ushort)(packet[3 + len] | (packet[4 + len] << 8));
            ushort checksumCalc = CalculateChecksum(data);

            if (checksumRecv != checksumCalc)
                return; // checksum fail

            byte cmd = data[0];

            // ★ 修正：不再要求 len == 7，而是 "至少大於等於 7"
            if (cmd == 0x02 && len >= 7)
            {
                ChannelState cs = new ChannelState
                {
                    Brightness1 = data[1],
                    Brightness2 = data[2],
                    Current1 = (ushort)(data[3] | (data[4] << 8)),
                    Current2 = (ushort)(data[5] | (data[6] << 8))
                };

                _tcsState?.TrySetResult(cs);
            }
        }

    }
}
