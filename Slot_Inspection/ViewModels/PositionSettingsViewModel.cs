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
/// Y 軸 25 個座標、ZL/ZR、X 各自一個分頁。
/// </summary>
public sealed class PositionSettingsViewModel : ObservableObject
{
    // ── Y 軸：25 個 Slot 座標（AreaA_Row1，對應實體 Slot 1~25）──
    public ObservableCollection<AxisPositionEntry> YPositions { get; } = [];

    // ── ZL 軸 ──
    public ObservableCollection<AxisPositionEntry> ZLPositions { get; } = [];

    // ── ZR 軸 ──
    public ObservableCollection<AxisPositionEntry> ZRPositions { get; } = [];

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

        ZLPositions.Clear();
        ZLPositions.Add(new AxisPositionEntry("ZL 相機高度",   config.CameraHeightZL));
        ZLPositions.Add(new AxisPositionEntry("ZL 安全高度",  config.ZLSafeHeight));

        ZRPositions.Clear();
        ZRPositions.Add(new AxisPositionEntry("ZR 相機高度",   config.CameraHeightZR));
        ZRPositions.Add(new AxisPositionEntry("ZR 安全高度",  config.ZRSafeHeight));

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

        // ZL
        if (ZLPositions.Count >= 1) config.CameraHeightZL = ZLPositions[0].Value;
        if (ZLPositions.Count >= 2) config.ZLSafeHeight   = ZLPositions[1].Value;

        // ZR
        if (ZRPositions.Count >= 1) config.CameraHeightZR = ZRPositions[0].Value;
        if (ZRPositions.Count >= 2) config.ZRSafeHeight   = ZRPositions[1].Value;

        // X
        if (XPositions.Count >= 1) config.BarcodePositionLeftX  = XPositions[0].Value;
        if (XPositions.Count >= 2) config.BarcodePositionRightX = XPositions[1].Value;

        // 取料位座標
        if (PickPositions.Count >= 1) config.BarcodePositionY = PickPositions[0].Value;
    }
}
