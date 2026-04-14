using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core.Interfaces
{
    public interface IModbusTransport
    {
        byte[] ReadCoils(byte Unit, ushort StartAddress, ushort Quantity);

        byte[] ReadDiscreteInputs(byte Unit, ushort StartAddress, ushort Quantity);

        byte[] ReadHoldingRegisters(byte Unit, ushort StartAddress, ushort Quantity);

        byte[] ReadInputRegisters(byte Unit, ushort StartAddress, ushort Quantity);

        bool WriteSingleCoil(byte Unit, ushort OutputAddress, bool OutputValue);

        bool WriteSingleRegister(byte Unit, ushort OutputAddress, short OutputValue);

        bool WriteMultipleCoils(byte Unit, ushort StartAddress, ushort Quantity, byte[] OutputValues);

        bool WriteMultipleRegisters(byte Unit, ushort StartAddress, ushort Quantity, short[] OutputValues);

    }
}
