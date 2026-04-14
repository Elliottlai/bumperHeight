using System.Collections.Generic;
using System.Threading.Tasks;
using Synpower.Lighting.Domain;
using Synpower.Lighting.Infrastructure;
using Synpower.Lighting.Application;
using Synpower.Lighting.Presentation;
using System;

namespace Synpower.Lighting
{
    public static class LightSetup
    {
        public static async Task<(LightService service, List<LightChannelViewModel> vms)> BuildAsync(LightConfig cfg)
        {
            // === 建立控制器 ===
            var controllers = new List<(ControllerKey, ILightDeviceController)>();

            if (cfg.Simulation)
            {
                // 模擬：建立多台 Fake（名稱只是方便辨識）
                var opt1 = new FakeDeviceAdapter("OPT#1(Fake)");
                var opt2 = new FakeDeviceAdapter("OPT#2(Fake)");
                var opt3 = new FakeDeviceAdapter("OPT#3(Fake)");
                var vsbox = new FakeDeviceAdapter("VST#1(Fake)");
                var line = new FakeDeviceAdapter("LineBox#1(Fake)");
                var viswellbox = new FakeDeviceAdapter("viswell#1(Fake)");

                controllers.Add((ControllerKey.Of(LightKind.OPT, 1), opt1));
                controllers.Add((ControllerKey.Of(LightKind.OPT, 2), opt2));
                controllers.Add((ControllerKey.Of(LightKind.OPT, 3), opt3));
                controllers.Add((ControllerKey.Of(LightKind.VST, 1), vsbox));
                controllers.Add((ControllerKey.Of(LightKind.Viswell, 1), line));
                controllers.Add((ControllerKey.Of(LightKind.Viswell, 2), viswellbox));
            }
            else
            {
                // 實機：使用你現有的兩個 adapter
                var opt1 = new OptDeviceAdapter(cfg.Opt1Port);
                var opt2 = new OptDeviceAdapter(cfg.Opt2Port); //new OptDeviceAdapter(cfg.Opt2Port); 
                var opt3 = new OptDeviceAdapter(cfg.Opt3Port);
                var vsbox1 = new VSDeviceAdapter(cfg.VST1Port);
                var vsbox2 = new VSDeviceAdapter(cfg.VST2Port);
                var line = new ViswellDeviceAdapter(cfg.VswellPort, cfg.LineChannels);
                 
                controllers.Add((ControllerKey.Of(LightKind.OPT, 1), opt1));
                controllers.Add((ControllerKey.Of(LightKind.OPT, 2), opt2));
                controllers.Add((ControllerKey.Of(LightKind.OPT, 3), opt3));
                controllers.Add((ControllerKey.Of(LightKind.VST, 1), vsbox1));
                controllers.Add((ControllerKey.Of(LightKind.VST, 2), vsbox2));
                controllers.Add((ControllerKey.Of(LightKind.Viswell, 1), line));





                //controllers.Add((ControllerKey.Of(LightKind.OPT, 3), opt3));
                //controllers.Add((ControllerKey.Of(LightKind.VST, 1), vsbox));
                //controllers.Add((ControllerKey.Of(LightKind.Viswell, 1), line));
                //controllers.Add((ControllerKey.Of(LightKind.Viswell, 2), viswellbox));
            }
             

            // === 通道映射（UI -> 裝置）===
            var channels = new[]
            {

                new LightChannelConfig(LightChannelId.DoorBack,   "DoorBack",  ControllerKey.Of(LightKind.OPT, 1),     DeviceChannel: 1, CapPercent: 50),
                new LightChannelConfig(LightChannelId.Foup,    "Foup",   ControllerKey.Of(LightKind.OPT, 1),     DeviceChannel: 2, CapPercent: 50),


                 new LightChannelConfig(LightChannelId.Handle,   "Handle",  ControllerKey.Of(LightKind.OPT, 2),     DeviceChannel: 1, CapPercent: 1),
                new LightChannelConfig(LightChannelId.FoupSide,    "FoupSide",   ControllerKey.Of(LightKind.OPT, 2),     DeviceChannel: 2),
                new LightChannelConfig(LightChannelId.FoupTop, "FoupTop", ControllerKey.Of(LightKind.OPT, 2),     DeviceChannel: 3),
                 new LightChannelConfig(LightChannelId.HandleCrack, "HandleCrack", ControllerKey.Of(LightKind.OPT, 2),     DeviceChannel: 4,CapPercent:2),

                new LightChannelConfig(LightChannelId.Filter,    "Filter",   ControllerKey.Of(LightKind.OPT, 3),     DeviceChannel: 1),
                new LightChannelConfig(LightChannelId.Valve1, "Valve1", ControllerKey.Of(LightKind.OPT, 3),     DeviceChannel: 2),
                new LightChannelConfig(LightChannelId.Valve2, "Valve2", ControllerKey.Of(LightKind.OPT, 3),     DeviceChannel: 3),
                new LightChannelConfig(LightChannelId.FoupSideOuter, "FoupSideOuter", ControllerKey.Of(LightKind.OPT, 3),     DeviceChannel: 4),


                new LightChannelConfig(LightChannelId.DoorSide,  "DoorSide", ControllerKey.Of(LightKind.VST, 1), DeviceChannel: 1),

                new LightChannelConfig(LightChannelId.Foup1, "Foup1",ControllerKey.Of(LightKind.VST, 2), DeviceChannel: 1),
                new LightChannelConfig(LightChannelId.Foup2, "Foup2",ControllerKey.Of(LightKind.VST, 2), DeviceChannel: 2),

                new LightChannelConfig(LightChannelId.LineLeft,  "Line Left", ControllerKey.Of(LightKind.Viswell, 1), DeviceChannel: 1),
                new LightChannelConfig(LightChannelId.LineRight, "Line Right",ControllerKey.Of(LightKind.Viswell, 1), DeviceChannel: 2),



                //new LightChannelConfig(LightChannelId.OptSide,   "HANDLE",  ControllerKey.Of(LightKind.OPT, 1),     DeviceChannel: 1),
                //new LightChannelConfig(LightChannelId.OptTop,    "OPT Top1",   ControllerKey.Of(LightKind.OPT, 1),     DeviceChannel: 2),
                //new LightChannelConfig(LightChannelId.BackLight, "BackLight1", ControllerKey.Of(LightKind.OPT, 1),     DeviceChannel: 3, CapPercent: 9),
                //new LightChannelConfig(LightChannelId.Opt4,      "OPT  1",     ControllerKey.Of(LightKind.OPT, 1),     DeviceChannel: 4),

                //new LightChannelConfig(LightChannelId.OptSide1,   "OPT Side2",  ControllerKey.Of(LightKind.OPT, 2),     DeviceChannel: 1),
                //new LightChannelConfig(LightChannelId.OptTop1,    "OPT Top2",   ControllerKey.Of(LightKind.OPT, 2),     DeviceChannel: 2),
                //new LightChannelConfig(LightChannelId.BackLight1, "BackLight2", ControllerKey.Of(LightKind.OPT, 2),     DeviceChannel: 3, CapPercent: 9),
                //new LightChannelConfig(LightChannelId.Opt41,      "OPT 2",     ControllerKey.Of(LightKind.OPT, 2),      DeviceChannel: 4),


                //new LightChannelConfig(LightChannelId.OptSide2,   "OPT Side3",  ControllerKey.Of(LightKind.OPT, 3),     DeviceChannel: 1),
                //new LightChannelConfig(LightChannelId.OptTop2,    "OPT Top3",   ControllerKey.Of(LightKind.OPT, 3),     DeviceChannel: 2),
                //new LightChannelConfig(LightChannelId.BackLight2, "BackLight3", ControllerKey.Of(LightKind.OPT, 3),     DeviceChannel: 3, CapPercent: 9),
                //new LightChannelConfig(LightChannelId.Opt42,      "OPT 3",     ControllerKey.Of(LightKind.OPT, 3),      DeviceChannel: 4),


                //new LightChannelConfig(LightChannelId.OptSide3,   "VST Side4",  ControllerKey.Of(LightKind.VST, 1),     DeviceChannel: 1),
                //new LightChannelConfig(LightChannelId.OptTop3,    "VST Top4",   ControllerKey.Of(LightKind.VST, 1),     DeviceChannel: 2),
                //new LightChannelConfig(LightChannelId.BackLight3, "BackLight4", ControllerKey.Of(LightKind.VST, 1),     DeviceChannel: 3, CapPercent: 9),
                //new LightChannelConfig(LightChannelId.Opt43,      "VST 4",     ControllerKey.Of(LightKind.VST, 1),      DeviceChannel: 4),




                //new LightChannelConfig(LightChannelId.VswellFront,  "Line Left2", ControllerKey.Of(LightKind.Viswell, 2), DeviceChannel: 1),
                //new LightChannelConfig(LightChannelId.VswellBack, "Line Right2",ControllerKey.Of(LightKind.Viswell, 2), DeviceChannel: 2),

            };

            // === 組成 Service 與 VM ===
            var service = new LightService(controllers, channels);
            await service.OpenAllAsync();

            var vms = new List<LightChannelViewModel>
            {
                new(LightChannelId.DoorBack,   "Door Back",   service),
                new(LightChannelId.Foup,       "Foup",    service),

                new(LightChannelId.Handle,     "Handle",  service),
                new(LightChannelId.FoupSide,   "FoupSide",      service),
                new(LightChannelId.FoupTop,    "FoupTop",   service),
                new(LightChannelId.HandleCrack,    "HandleCrack",   service),

                new(LightChannelId.Filter,   "Filter",      service),
                new(LightChannelId.Valve1,    "Valve1",   service),
                new(LightChannelId.Valve2,    "Valve2",   service),
                new(LightChannelId.FoupSideOuter,    "FoupSideOuter",   service),

                new(LightChannelId.DoorSide,   "Door Side",      service),

                new(LightChannelId.Foup1,    "Foup1",   service),
                new(LightChannelId.Foup2,    "Foup2",   service),


                new(LightChannelId.LineLeft,   "Line Left",    service),
                new(LightChannelId.LineRight,  "Line Right",  service),
                
            };


            foreach (var v in vms)
                v.Value = 0;

            return (service, vms);
        }
    }
}
