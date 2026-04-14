using System;
using System.IO.Ports;
using Newtonsoft.Json;
using Machine.Core.Enums;

namespace Machine.Core.Interfaces
{
    public interface ILightController : ILightArgs
    {
        int GetChannelCount();

        void SetLuminance(int channel, int value);
        void SetLuminance(int[] value, bool changeOnly = true);

        int GetLuminance(int channel);
        int[] GetLuminance();
    }
}
