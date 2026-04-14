using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynPower.Lights.Litsource
{
    public abstract class Command
    {
        public const byte ACK = 0x06;
        public const byte NAK = 0x15;
        public const int STXLength = 1;
        public const int DataLengthLength = 2;
        public const int InstructionLength = 1;
        public const int ChecksumLength = 2;

        public SerialPort ComPort { get; private set; }
        public byte STX { get { return 2; } }
        public byte[] DataLength
        {
            get
            {
                int length = this.Data.Length;

                byte lowByte = (byte)(length & 0x00FF);
                byte highByte = (byte)(length & 0xFF00);

                return new[] { lowByte, highByte };
            }
        }
        public abstract byte[] Data { get; }
        public byte[] Checksum
        {
            get
            {
                // 將Data資料加總而成，最高位元進位不理會。
                int sum = this.Data.Sum(e => (int)e);

                byte lowByte = (byte)(sum & 0x00FF);
                byte highByte = (byte)((sum & 0xFF00) >> 8);

                return new[] { lowByte, highByte };
            }
        }

        public int CommandLength
        {
            get
            {
                return Command.STXLength
                    + this.DataLength.Length
                    + this.Data.Length
                    + this.Checksum.Length;
            }
        }
        public int ResponseLength
        {
            get
            {
                return Command.STXLength
                    + Command.DataLengthLength
                    + Command.InstructionLength
                    + this.ResponseDataTextLength
                    + Command.ChecksumLength;
            }
        }
        public int ResponseHeadLength
        {
            get
            {
                return Command.STXLength
                  + Command.DataLengthLength
                  + Command.InstructionLength;
            }
        }
        public abstract int ResponseDataTextLength { get; }

        public static implicit operator byte[](Command command)
        {
            byte[] cmdArray = new byte[] { command.STX };

            var cmd = cmdArray
                .Concat(command.DataLength)
                .Concat(command.Data)
                .Concat(command.Checksum)
                .ToArray();

            return cmd;
        }
    }
}
