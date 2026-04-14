namespace Synpower.Lighting
{
    // 你也可以把這個對應到 appsettings.json
    public sealed class LightConfig
    {
        public bool Simulation { get; init; } = false;  // true=使用模擬裝置
        public string Opt1Port { get; init; } = "COM11";  //12
        public string Opt2Port { get; init; } = "Com13";
        public string Opt3Port { get; init; } = "COM10";
        public string VST1Port { get; init; } = "COM12"; //11
        public string VST2Port { get; init; } = "COM15";
        public string VswellPort { get; init; } = "COM1";
       // public string VswellPort { get; init; } = "COM17";
        public int LineChannels { get; init; } = 2;
    }
}
