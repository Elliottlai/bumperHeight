using Slot_Inspection.Models;

namespace Slot_Inspection.Services;

/// <summary>
/// 「套用一組 Recipe 到目前生效設定」的共用邏輯。
/// 原本這段散在 <c>PositionSettingsWindow.BtnLoadRecipe_Click</c> 裡（只能從視窗按鈕呼叫），
/// 抽出來讓「手動切換視窗」與「掃碼自動切換」走同一套程式碼，行為保證一致。
/// </summary>
public static class RecipeService
{
    /// <summary>
    /// 讀取指定 Recipe 並「立即完整套用」到目前生效設定，全部寫入並存檔：
    /// <list type="bullet">
    ///   <item>25 個 Slot 的 Y 座標（R/L 兩組）→ <see cref="SlotPositionTable"/> + SlotPositions.json</item>
    ///   <item>ZL/ZR 相機高度、安全高度（R/L 兩組）→ 傳入的 <paramref name="config"/> + InspectionConfig.json</item>
    ///   <item>演算法參數 JSON → 覆蓋回 Config\ 底下（若這份 Recipe 有帶快照）</item>
    ///   <item>目前 Recipe 名稱 → <paramref name="config"/>.CurrentRecipeName</item>
    /// </list>
    /// Recipe 檔不存在 / 解析失敗會丟例外（<see cref="System.IO.FileNotFoundException"/> /
    /// <see cref="System.IO.InvalidDataException"/>），由呼叫端顯示錯誤。
    /// </summary>
    /// <param name="recipeName">Config\Recipes 下的 Recipe 名稱（不含副檔名）。</param>
    /// <param name="config">目前生效的設定實例（通常是 MachineController.Config）。會被就地修改並存檔。</param>
    /// <returns>true＝這份 Recipe 有帶演算法參數快照；false＝只套了座標與高度。</returns>
    public static bool Apply(string recipeName, InspectionConfig config)
    {
        var data = AxisRecipeManager.Load(recipeName);   // 找不到 / 壞檔會丟例外

        // ── Y 座標 → SlotPositionTable 靜態陣列（R / L 兩組）──
        var rowR = SlotPositionTable.AreaA_Row1_R;
        for (int i = 0; i < rowR.Length && i < data.YPositions_R.Length; i++)
            rowR[i] = new SlotPosition(data.YPositions_R[i]);

        var rowL = SlotPositionTable.AreaA_Row1_L;
        for (int i = 0; i < rowL.Length && i < data.YPositions_L.Length; i++)
            rowL[i] = new SlotPosition(data.YPositions_L[i]);

        SlotPositionTable.Save();

        // ── ZL/ZR 相機高度 + 安全高度 → config ──
        config.CameraHeightZL_R = data.CameraHeightZL_R;
        config.CameraHeightZL_L = data.CameraHeightZL_L;
        config.CameraHeightZR_R = data.CameraHeightZR_R;
        config.CameraHeightZR_L = data.CameraHeightZR_L;
        config.ZLSafeHeight_R   = data.ZLSafeHeight_R;
        config.ZLSafeHeight_L   = data.ZLSafeHeight_L;
        config.ZRSafeHeight_R   = data.ZRSafeHeight_R;
        config.ZRSafeHeight_L   = data.ZRSafeHeight_L;

        // ── 演算法參數 JSON（Parameter.json / C1~C4 各 Slot 檔 / BumperFlatDetectionOptions.json）──
        bool hasAlgFiles = AxisRecipeManager.LoadAlgFiles(recipeName);

        // ── 目前 Recipe 名稱 ──
        config.CurrentRecipeName = recipeName;
        config.Save();

        return hasAlgFiles;
    }
}
