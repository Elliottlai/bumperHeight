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
    // ── Y 軸：R 組（X12=true，R 載台）25 個 Slot 座標 ──
    public ObservableCollection<AxisPositionEntry> YPositions_R { get; } = [];

    // ── Y 軸：L 組（X13=true，L 載台）25 個 Slot 座標 ──
    public ObservableCollection<AxisPositionEntry> YPositions_L { get; } = [];

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

        YPositions_R.Clear();
        for (int i = 0; i < SlotPositionTable.AreaA_Row1_R.Length; i++)
            YPositions_R.Add(new AxisPositionEntry($"Slot {i + 1}", SlotPositionTable.AreaA_Row1_R[i].Y));

        YPositions_L.Clear();
        for (int i = 0; i < SlotPositionTable.AreaA_Row1_L.Length; i++)
            YPositions_L.Add(new AxisPositionEntry($"Slot {i + 1}", SlotPositionTable.AreaA_Row1_L[i].Y));

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
        // Y 軸 R 組 → SlotPositionTable.AreaA_Row1_R
        for (int i = 0; i < YPositions_R.Count && i < SlotPositionTable.AreaA_Row1_R.Length; i++)
            SlotPositionTable.AreaA_Row1_R[i] = new SlotPosition(YPositions_R[i].Value);

        // Y 軸 L 組 → SlotPositionTable.AreaA_Row1_L
        for (int i = 0; i < YPositions_L.Count && i < SlotPositionTable.AreaA_Row1_L.Length; i++)
            SlotPositionTable.AreaA_Row1_L[i] = new SlotPosition(YPositions_L[i].Value);

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
