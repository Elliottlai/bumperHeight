using Machine.Core;

namespace FoupInspecMachine.Manager
{
    /// <summary>
    /// CaptureImageSuffix → Camera NamedKey 的對照表。
    /// 根據 MainWindow_PLCAction 中各 Capture 方法裡
    /// 每個 count 實際呼叫的 cameraManager.XxxSaveImage 建立。
    /// </summary>
    public static class SimSuffixCameraMap
    {
        /// <summary>
        /// suffix → NamedKey。
        /// 同一個 suffix 只會對應一個 Camera。
        /// </summary>
        private static readonly Dictionary<string, NamedKey> _map = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 從 FlowConfig 中的所有 suffix，搭配已知的 Flow→Camera 規則，
        /// 自動建立對照表。呼叫一次即可。
        /// </summary>
        //public static void Build(IEnumerable<FlowConfig> flows)
        //{
        //    _map.Clear();

        //    foreach (var flow in flows)
        //    {
        //        for (int i = 0; i < flow.CaptureImageSuffix.Count; i++)
        //        {
        //            var suffix = flow.CaptureImageSuffix[i];
        //            var camera = ResolveCamera(flow.FlowName, i);
        //            if (camera != null && !_map.ContainsKey(suffix))
        //                _map[suffix] = camera;
        //        }
        //    }
        //}

        /// <summary>依 suffix 查詢對應的 Camera</summary>
        public static NamedKey Resolve(string suffix)
            => _map.TryGetValue(suffix, out var key) ? key : null;

        /// <summary>取得完整對照表（唯讀）</summary>
        public static IReadOnlyDictionary<string, NamedKey> All => _map;

        /// <summary>
        /// 根據 FlowName + count index 決定實際使用的 Camera。
        /// 這裡的邏輯完全對應 MainWindow_PLCAction.cs 中各方法的 switch-case。
        /// </summary>
        private static NamedKey ResolveCamera(string flowName, int index)
        {
            return flowName switch
            {
                // CaptureHandleL / CaptureHandleR：count==2 用 FoupTop，其餘 FoupSide
                "CaptureHandle1" => index == 2 ? NamedKeyCamera.FoupTop : NamedKeyCamera.FoupSide,
                "CaptureHandle2" => index == 2 ? NamedKeyCamera.FoupTop : NamedKeyCamera.FoupSide,

                // CaptureDoor：全部 FoupSide
                "CaptureDoor" => NamedKeyCamera.FoupSide,

                // CaptureValve：Valve 相機
                "CaptureValve" => NamedKeyCamera.Valve,

                // CaptureFoup：count==1 用 FilterTop，其餘 Foup
                "CaptureFoup" => index == 1 ? NamedKeyCamera.FilterTop : NamedKeyCamera.Foup,

                // CaptureDoorSide
                "CaptureDoorSide" => NamedKeyCamera.DoorSide,

                // CaptureDoorBack
                "CaptureDoorBack" => NamedKeyCamera.DoorBack,

                // CaptureHandleCrack1 / CaptureHandleCrack2
                "CaptureHandleCrack1" => NamedKeyCamera.HandleCrack,
                "CaptureHandleCrack2" => NamedKeyCamera.HandleCrack,

                // LineScanEnd2：偶數 index → Left，奇數 → Right
                "LineScanEnd2" => index % 2 == 0
                    ? NamedKeyCamera.LineCameraLeft
                    : NamedKeyCamera.LineCameraRight,

                _ => null
            };
        }
    }
}