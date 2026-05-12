using System.Collections.ObjectModel;
using Slot_Inspection.Models;

namespace Slot_Inspection.ViewModels;

/// <summary>單一軸座標項目（用於清單繫結）。</summary>
public sealed class AxisPositionEntry : ObservableObject
{
    private double _value;
    public string Label { get; }

    public double Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public AxisPositionEntry(string label, double value)
    {
        Label  = label;
        _value = value;
    }
}

/// <summary>
/// 座標設定對話視窗的 ViewModel。
/// Y 軸 25 個座標、ZL/ZR（分 R組/L組）、X 各自一個分頁。
/// </summary>
public sealed class PositionSettingsViewModel : ObservableObject
{
    // ── Y 軸：25 個 Slot 座標（AreaA_Row1，對應實體 Slot 1~25）──
    public ObservableCollection<AxisPositionEntry> YPositions { get; } = [];

    // ── ZL 軸 R組（X12=true，板子在右側）──
    public ObservableCollection<AxisPositionEntry> ZLPositions_R { get; } = [];

    // ── ZR 軸 R組 ──
    public ObservableCollection<AxisPositionEntry> ZRPositions_R { get; } = [];

    // ── ZL 軸 L組（X13=true，板子在左側）──
    public ObservableCollection<AxisPositionEntry> ZLPositions_L { get; } = [];

    // ── ZR 軸 L組 ──
    public ObservableCollection<AxisPositionEntry> ZRPositions_L { get; } = [];

    // ── X 軸 ──
    public ObservableCollection<AxisPositionEntry> XPositions { get; } = [];

    // ── 取料位座標（讀碼 Y）──
    public ObservableCollection<AxisPositionEntry> PickPositions { get; } = [];

    /// <summary>從 InspectionConfig 與 SlotPositionTable 載入目前值。</summary>
    public void LoadFrom(InspectionConfig config)
    {
        SlotPositionTable.Load();   // 從 JSON 讀取最新已存座標
        YPositions.Clear();
        for (int i = 0; i < SlotPositionTable.AreaA_Row1.Length; i++)
            YPositions.Add(new AxisPositionEntry($"Slot {i + 1}", SlotPositionTable.AreaA_Row1[i].Y));

        ZLPositions_R.Clear();
        ZLPositions_R.Add(new AxisPositionEntry("ZL 相機高度 (R組)", config.CameraHeightZL_R));
        ZLPositions_R.Add(new AxisPositionEntry("ZL 安全高度 (R組)", config.ZLSafeHeight_R));

        ZRPositions_R.Clear();
        ZRPositions_R.Add(new AxisPositionEntry("ZR 相機高度 (R組)", config.CameraHeightZR_R));
        ZRPositions_R.Add(new AxisPositionEntry("ZR 安全高度 (R組)", config.ZRSafeHeight_R));

        ZLPositions_L.Clear();
        ZLPositions_L.Add(new AxisPositionEntry("ZL 相機高度 (L組)", config.CameraHeightZL_L));
        ZLPositions_L.Add(new AxisPositionEntry("ZL 安全高度 (L組)", config.ZLSafeHeight_L));

        ZRPositions_L.Clear();
        ZRPositions_L.Add(new AxisPositionEntry("ZR 相機高度 (L組)", config.CameraHeightZR_L));
        ZRPositions_L.Add(new AxisPositionEntry("ZR 安全高度 (L組)", config.ZRSafeHeight_L));

        XPositions.Clear();
        XPositions.Add(new AxisPositionEntry("X 左側讀碼",   config.BarcodePositionLeftX));
        XPositions.Add(new AxisPositionEntry("X 右側讀碼",   config.BarcodePositionRightX));

        PickPositions.Clear();
        PickPositions.Add(new AxisPositionEntry("讀碼位置 Y", config.BarcodePositionY));
    }

    /// <summary>將目前值寫回 InspectionConfig 與 SlotPositionTable。</summary>
    public void ApplyTo(InspectionConfig config)
    {
        // Y 軸 → SlotPositionTable
        for (int i = 0; i < YPositions.Count && i < SlotPositionTable.AreaA_Row1.Length; i++)
            SlotPositionTable.AreaA_Row1[i] = new SlotPosition(YPositions[i].Value);

        // ZL R組
        if (ZLPositions_R.Count >= 1) config.CameraHeightZL_R = ZLPositions_R[0].Value;
        if (ZLPositions_R.Count >= 2) config.ZLSafeHeight_R   = ZLPositions_R[1].Value;

        // ZR R組
        if (ZRPositions_R.Count >= 1) config.CameraHeightZR_R = ZRPositions_R[0].Value;
        if (ZRPositions_R.Count >= 2) config.ZRSafeHeight_R   = ZRPositions_R[1].Value;

        // ZL L組
        if (ZLPositions_L.Count >= 1) config.CameraHeightZL_L = ZLPositions_L[0].Value;
        if (ZLPositions_L.Count >= 2) config.ZLSafeHeight_L   = ZLPositions_L[1].Value;

        // ZR L組
        if (ZRPositions_L.Count >= 1) config.CameraHeightZR_L = ZRPositions_L[0].Value;
        if (ZRPositions_L.Count >= 2) config.ZRSafeHeight_L   = ZRPositions_L[1].Value;

        // X
        if (XPositions.Count >= 1) config.BarcodePositionLeftX  = XPositions[0].Value;
        if (XPositions.Count >= 2) config.BarcodePositionRightX = XPositions[1].Value;

        // 取料位座標
        if (PickPositions.Count >= 1) config.BarcodePositionY = PickPositions[0].Value;
    }
}
