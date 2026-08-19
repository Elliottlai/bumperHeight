using System.Collections.ObjectModel;
using System.Linq;
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

    // ── Recipe（Y/ZL/ZR 參數組合）名稱清單 ──
    public ObservableCollection<string> RecipeNames { get; } = [];

    private InspectionConfig? _config;

    /// <summary>目前登入的使用者，由 MainViewModel.OnPositionSettings 設定，供Recipe操作寫Audit Log用。</summary>
    public UserAccount? CurrentUser { get; set; }

    /// <summary>是否有權限操作 QC 基準比對（由 MainViewModel.OnPositionSettings 依角色權限設定）。</summary>
    public bool CanUseQcRecipe { get; init; }

    /// <summary>開啟此視窗當下，AreaA 的最新量測結果快照，供「存為QC基準」/「QC比對」使用。</summary>
    public IReadOnlyList<SlotItem> CurrentMeasurementsAreaA { get; init; } = [];

    /// <summary>開啟此視窗當下，AreaB 的最新量測結果快照，供「存為QC基準」/「QC比對」使用。</summary>
    public IReadOnlyList<SlotItem> CurrentMeasurementsAreaB { get; init; } = [];

    /// <summary>供 QC 基準比對讀取容差設定；由 LoadFrom 一併帶入。</summary>
    public InspectionConfig? Config => _config;

    /// <summary>開啟此視窗當下的條碼快照，供「QC比對」寫入報表時記錄用。</summary>
    public string CurrentBarcode { get; init; } = "";

    private string _selectedRecipeName = "";
    /// <summary>Recipe 選擇/輸入框目前的名稱（讀取/建立/刪除皆以此為對象）。</summary>
    public string SelectedRecipeName
    {
        get => _selectedRecipeName;
        set => SetProperty(ref _selectedRecipeName, value);
    }

    private bool _isDirty;
    /// <summary>座標數值是否在「上次備份／讀取」之後又被手動修改過，尚未備份。用於離開視窗前提醒使用者備份。</summary>
    public bool IsDirty
    {
        get => _isDirty;
        set => SetProperty(ref _isDirty, value);
    }

    private string _currentRecipeName = "";
    /// <summary>目前套用中的 Recipe 名稱，顯示在UI上；變更時同步寫回 InspectionConfig 並存檔。</summary>
    public string CurrentRecipeName
    {
        get => _currentRecipeName;
        set
        {
            if (SetProperty(ref _currentRecipeName, value) && _config != null)
            {
                _config.CurrentRecipeName = value;
                _config.Save();
            }
        }
    }

    /// <summary>重新從 Config/Recipes 載入 Recipe 名稱清單。</summary>
    public void RefreshRecipeList()
    {
        RecipeNames.Clear();
        foreach (var name in AxisRecipeManager.List())
            RecipeNames.Add(name);
    }

    /// <summary>把目前編輯畫面上的 Y/ZL/ZR 數值組成一份 AxisRecipeData（用於「建立」Recipe）。</summary>
    public AxisRecipeData BuildRecipeData()
    {
        var data = new AxisRecipeData();
        for (int i = 0; i < data.YPositions_R.Length && i < YPositions_R.Count; i++)
            data.YPositions_R[i] = YPositions_R[i].Value;
        for (int i = 0; i < data.YPositions_L.Length && i < YPositions_L.Count; i++)
            data.YPositions_L[i] = YPositions_L[i].Value;

        if (ZLPositions_R.Count >= 2)
        {
            data.CameraHeightZL_R = ZLPositions_R[0].Value;
            data.ZLSafeHeight_R   = ZLPositions_R[1].Value;
        }
        if (ZLPositions_L.Count >= 2)
        {
            data.CameraHeightZL_L = ZLPositions_L[0].Value;
            data.ZLSafeHeight_L   = ZLPositions_L[1].Value;
        }
        if (ZRPositions_R.Count >= 2)
        {
            data.CameraHeightZR_R = ZRPositions_R[0].Value;
            data.ZRSafeHeight_R   = ZRPositions_R[1].Value;
        }
        if (ZRPositions_L.Count >= 2)
        {
            data.CameraHeightZR_L = ZRPositions_L[0].Value;
            data.ZRSafeHeight_L   = ZRPositions_L[1].Value;
        }
        return data;
    }

    /// <summary>把一份 AxisRecipeData 套用到目前編輯畫面（用於「讀取」Recipe）。</summary>
    public void ApplyRecipeData(AxisRecipeData data)
    {
        for (int i = 0; i < YPositions_R.Count && i < data.YPositions_R.Length; i++)
            YPositions_R[i].Value = data.YPositions_R[i];
        for (int i = 0; i < YPositions_L.Count && i < data.YPositions_L.Length; i++)
            YPositions_L[i].Value = data.YPositions_L[i];

        if (ZLPositions_R.Count >= 2)
        {
            ZLPositions_R[0].Value = data.CameraHeightZL_R;
            ZLPositions_R[1].Value = data.ZLSafeHeight_R;
        }
        if (ZLPositions_L.Count >= 2)
        {
            ZLPositions_L[0].Value = data.CameraHeightZL_L;
            ZLPositions_L[1].Value = data.ZLSafeHeight_L;
        }
        if (ZRPositions_R.Count >= 2)
        {
            ZRPositions_R[0].Value = data.CameraHeightZR_R;
            ZRPositions_R[1].Value = data.ZRSafeHeight_R;
        }
        if (ZRPositions_L.Count >= 2)
        {
            ZRPositions_L[0].Value = data.CameraHeightZR_L;
            ZRPositions_L[1].Value = data.ZRSafeHeight_L;
        }
    }

    /// <summary>從 InspectionConfig 與 SlotPositionTable 載入目前值。</summary>
    public void LoadFrom(InspectionConfig config)
    {
        _config = config;
        SetProperty(ref _currentRecipeName, config.CurrentRecipeName, nameof(CurrentRecipeName));

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

        RefreshRecipeList();

        // 載入完成後才開始追蹤「使用者手動修改」，避免LoadFrom本身的賦值被誤判為已修改
        foreach (var entry in YPositions_R.Concat(YPositions_L)
                     .Concat(ZLPositions_R).Concat(ZRPositions_R)
                     .Concat(ZLPositions_L).Concat(ZRPositions_L)
                     .Concat(XPositions).Concat(PickPositions))
            entry.PropertyChanged += (_, __) => IsDirty = true;
        IsDirty = false;
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
