namespace Slot_Inspection.Services;

/// <summary>
/// 影像量測服務 — 從相機影像計算出一個量測值。
/// ?? TODO：實作真實的 Halcon / OpenCV 量測演算法。
/// 目前為 Stub，固定回傳 0.50，流程可跑通但結果無意義。
/// </summary>
public static class ImageMeasurer
{
    /// <summary>
    /// 對單張影像執行量測，回傳量測值。
    /// </summary>
    /// <param name="image">相機取得的 HImage（可為 null，Stub 不使用）</param>
    /// <param name="slotName">Slot 名稱（用於 Log）</param>
    /// <returns>量測值</returns>
    public static double Measure(object? image, string slotName)
    {
        // ?? TODO：在這裡寫你的 Halcon 量測邏輯，例如：
        //
        // var hImage = (HImage)image!;
        //
        // 範例 1 — 灰階平均（簡單驗收用）
        // HOperatorSet.Intensity(hImage, hImage, out HTuple mean, out HTuple deviation);
        // return mean.D / 255.0;
        //
        // 範例 2 — 邊緣量測
        // HOperatorSet.EdgesSubPix(hImage, out HObject edges, "canny", 1, 20, 40);
        // ...計算邊緣間距...
        //
        // 範例 3 — 樣板匹配 + 座標計算
        // ...

        // Stub：固定回傳 0.50
        System.Diagnostics.Debug.WriteLine($"[ImageMeasurer] STUB: {slotName} → 0.50");
        return 0.50;
    }
}
