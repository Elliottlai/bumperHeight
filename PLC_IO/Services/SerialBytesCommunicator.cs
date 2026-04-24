using System.Diagnostics;
using System.IO.Ports;
using PLC_IO.Interfaces;

namespace PLC_IO.Services;

/// <summary>
/// 透過 SerialPort 實作位元組層級通訊（主動輪詢模式）
/// </summary>
public sealed class SerialBytesCommunicator : IBytesCommunicatable, IDisposable
{
    private readonly SerialPort _serialPort;
    private readonly List<byte> _inBuffer = [];
    private readonly object _bufferLock = new();
    private readonly Thread _readThread;
    private volatile bool _disposed;

    public event Action? DataArrival;

    public SerialBytesCommunicator(string portName, int baudRate, int dataBits, Parity parity, StopBits stopBits)
    {
        _serialPort = new SerialPort
        {
            PortName = portName,
            BaudRate = baudRate,
            DataBits = dataBits,
            Parity = parity,
            StopBits = stopBits,
            ReadTimeout = 100,
            WriteTimeout = 100
        };
        _serialPort.Open();

        // ★ 用主動輪詢取代 DataReceived 事件
        // .NET 8 的 SerialPort.DataReceived 有已知的「漏事件」問題
        _readThread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = $"SerialRead-{portName}"
        };
        _readThread.Start();
    }

    public bool Communicatable() => !_disposed && _serialPort.IsOpen;

    public void Send(byte[] data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _serialPort.Write(data, 0, data.Length);
    }

    public byte[] Get()
    {
        lock (_bufferLock)
        {
            byte[] result = [.. _inBuffer];
            _inBuffer.Clear();
            return result;
        }
    }

    public void AddDataArrivalEvent(Action dataArrivalEvent)
    {
        DataArrival += dataArrivalEvent;
    }

    /// <summary>
    /// 主動輪詢迴圈 — 持續檢查 serial port 是否有資料
    /// </summary>
    private void ReadLoop()
    {
        while (!_disposed)
        {
            try
            {
                if (_serialPort.IsOpen && _serialPort.BytesToRead > 0)
                {
                    lock (_bufferLock)
                    {
                        while (_serialPort.BytesToRead > 0)
                        {
                            _inBuffer.Add((byte)_serialPort.ReadByte());
                        }
                    }
                    DataArrival?.Invoke();
                }
            }
            catch (Exception ex) when (!_disposed)
            {
                Debug.WriteLine($"[Serial] ReadLoop error: {ex.Message}");
            }

            Thread.Sleep(1); // 1ms 輪詢間隔，CPU 負擔極低
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 等待讀取執行緒結束
        _readThread.Join(500);

        if (_serialPort.IsOpen) _serialPort.Close();
        _serialPort.Dispose();
    }
}