namespace Machine.Core.Models.Axis.DeltaRS485
{
    /// <summary>
    /// Delta RS485 專用 Modbus RTU 通訊介面
    /// </summary>
    public interface IModbusRtuClient
    {
        /// <summary>建立 RS485 連線</summary>
        void Connect(string portName, int baudRate, byte slaveId);

        /// <summary>斷開連線</summary>
        void Disconnect();

        /// <summary>是否已連線</summary>
        bool IsConnected { get; }

        /// <summary>讀取單一暫存器 (FC03)</summary>
        ushort ReadRegister(ushort address);

        /// <summary>讀取多個暫存器 (FC03)</summary>
        ushort[] ReadRegisters(ushort address, ushort count);

        /// <summary>寫入單一暫存器 (FC06)</summary>
        void WriteRegister(ushort address, ushort value);

        /// <summary>寫入多個暫存器 (FC10)</summary>
        void WriteRegisters(ushort address, ushort[] values);

        /// <summary>寫入 32-bit 數值（連續兩個暫存器）</summary>
        void WriteRegister32(ushort address, int value);

        /// <summary>讀取 32-bit 數值（連續兩個暫存器）</summary>
        int ReadRegister32(ushort address);
    }
}
