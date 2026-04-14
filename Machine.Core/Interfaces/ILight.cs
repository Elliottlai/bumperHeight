using System;
using System.IO.Ports;
using Newtonsoft.Json;
using Machine.Core.Enums;

namespace Machine.Core.Interfaces
{
    public interface ILight : ILightArgs
    {
        bool SetLuminance(byte value, bool Wait = true);

        byte GetLuminance();

    }
}
