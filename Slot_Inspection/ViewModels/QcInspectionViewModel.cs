using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Slot_Inspection.Helper;
using Slot_Inspection.Models;
using Slot_Inspection.Services;

namespace Slot_Inspection.ViewModels;

/// <summary>
/// QC 比對儀表板每次刷新時，由 MainViewModel 提供的「當下這一輪檢測」狀態快照。
/// AreaA / AreaB 直接傳活的集合參考即可（每輪原地更新），不需要複製。
/// </summary>
public sealed record QcLiveSnapshot(
    string RecipeName,
    string CarrierSide,
    string Barcode,
    DateTime? LastRunAt,
    IReadOnlyList<SlotItem> AreaA,
    IReadOnlyList<SlotItem> AreaB);

/// <summary>
/// 獨立「QC 比對」視窗的 ViewModel。跟舊版塞在「Recipe 切換」視窗裡最大的差別：
/// 1) 生效 Recipe / 側別 / 條碼 都自動帶入，不再靠使用者手打
/// 2) 開著就是一個即時儀表板——每跑完一片，MainViewModel 會呼叫 <see cref="Refresh"/> 重新比對
/// 3) QC 容差三個欄位也整合進來，不用再開「光源設定」視窗
/// </summary>
public sealed class QcInspectionViewModel : ObservableObject
{
    private readonly InspectionConfig _config;
    private readonly UserAccount? _user;
    private readonly Func<QcLiveSnapshot> _snapshotProvider;

    public bool CanUseQcRecipe { get; }

    /// <summary>
    /// 是否可以「改動 QC 設定」——存基準、改允許範圍。僅 Engineer / Admin。
    /// Operator 可以開視窗、看比對明細、按「重新比對」，但不能覆蓋基準、不能改允許範圍。
    /// </summary>
    public bool CanEditQc { get; }

    public QcInspectionViewModel(
        InspectionConfig config, UserAccount? user, bool canUseQcRecipe, bool canEditQc,
        Func<QcLiveSnapshot> snapshotProvider)
    {
        _config = config;
        _user = user;
        CanUseQcRecipe = canUseQcRecipe;
        CanEditQc = canEditQc;
        _snapshotProvider = snapshotProvider;

        _qcToleranceValue = config.QcToleranceValue;
        _qcToleranceMeanIntensity = config.QcToleranceMeanIntensity;
        _qcToleranceDistanceToRoiCenter = config.QcToleranceDistanceToRoiCenter;

        SaveBaselineCommand = new RelayCommand(OnSaveBaseline,
            () => CanEditQc && IsSideKnown && HasRecipe && _hasMeasurements);
        RefreshCommand = new RelayCommand(Refresh);
        SaveTolerancesCommand = new RelayCommand(OnSaveTolerances, () => CanEditQc);
        OpenReportFolderCommand = new RelayCommand(OnOpenReportFolder);

        Refresh();
    }

    // ─────────────────────────── 目前這一輪的狀態 ───────────────────────────

    private string _recipeName = "";
    public string RecipeName { get => _recipeName; private set => SetProperty(ref _recipeName, value); }

    private string _carrierSide = "";
    public string CarrierSide { get => _carrierSide; private set { if (SetProperty(ref _carrierSide, value)) OnPropertyChanged(nameof(IsSideKnown)); } }

    private string _barcode = "";
    public string Barcode { get => _barcode; private set => SetProperty(ref _barcode, value); }

    private DateTime? _lastRunAt;
    public DateTime? LastRunAt { get => _lastRunAt; private set => SetProperty(ref _lastRunAt, value); }

    public bool IsSideKnown => AxisRecipeManager.IsValidSide(CarrierSide);
    private bool HasRecipe => AxisRecipeManager.IsValidName(RecipeName);
    private bool _hasMeasurements;

    // ─────────────────────────── R / L 基準狀態 ───────────────────────────

    private string _baselineRInfo = "—";
    public string BaselineRInfo { get => _baselineRInfo; private set => SetProperty(ref _baselineRInfo, value); }

    private string _baselineLInfo = "—";
    public string BaselineLInfo { get => _baselineLInfo; private set => SetProperty(ref _baselineLInfo, value); }

    // ─────────────────────────── 比對結果 ───────────────────────────

    public ObservableCollection<QcSlotComparison> Rows { get; } = [];

    private int _exceedCount;
    public int ExceedCount { get => _exceedCount; private set => SetProperty(ref _exceedCount, value); }

    private string _comparisonHeader = "尚未比對";
    public string ComparisonHeader { get => _comparisonHeader; private set => SetProperty(ref _comparisonHeader, value); }

    private string _statusMessage = "";
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    // ─────────────────────────── QC 比對總開關 ───────────────────────────

    /// <summary>
    /// 是否啟用 QC 比對。關閉時：每輪 RUN 後不自動比對、不寫主畫面狀態列、不產生 CSV。
    /// 這個視窗仍可手動按「重新比對」查看。設定即時存進 config，重開程式沿用。
    /// </summary>
    public bool QcCompareEnabled
    {
        get => _config.QcCompareEnabled;
        set
        {
            if (_config.QcCompareEnabled == value) return;
            _config.QcCompareEnabled = value;
            _config.Save();
            if (_user != null)
                AuditLogger.Write(_user.Username, _user.Role, $"QC 比對自動檢查切換為{(value ? "啟用" : "停用")}");
            OnPropertyChanged();
            Refresh();
        }
    }

    // ─────────────────────────── QC 容差 ───────────────────────────

    private double _qcToleranceValue;
    public double QcToleranceValue { get => _qcToleranceValue; set => SetProperty(ref _qcToleranceValue, value); }

    private double _qcToleranceMeanIntensity;
    public double QcToleranceMeanIntensity { get => _qcToleranceMeanIntensity; set => SetProperty(ref _qcToleranceMeanIntensity, value); }

    private double _qcToleranceDistanceToRoiCenter;
    public double QcToleranceDistanceToRoiCenter { get => _qcToleranceDistanceToRoiCenter; set => SetProperty(ref _qcToleranceDistanceToRoiCenter, value); }

    // ─────────────────────────── 命令 ───────────────────────────

    public ICommand SaveBaselineCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand SaveTolerancesCommand { get; }
    public ICommand OpenReportFolderCommand { get; }

    /// <summary>重新抓當下這一輪的量測結果，依偵測到的 R/L 取對應基準重新比對、刷新畫面。</summary>
    public void Refresh()
    {
        var snap = _snapshotProvider();
        RecipeName = snap.RecipeName ?? "";
        CarrierSide = (snap.CarrierSide ?? "").Trim();
        Barcode = snap.Barcode ?? "";
        LastRunAt = snap.LastRunAt;
        _hasMeasurements = snap.AreaA.Concat(snap.AreaB).Any(s => s.Value != 0);

        RefreshBaselineInfo();

        Rows.Clear();
        ExceedCount = 0;

        if (!HasRecipe)
        {
            ComparisonHeader = "尚無生效中的 Recipe（請先在「Recipe 切換」讀取套用一組 Recipe）";
        }
        else if (!IsSideKnown)
        {
            ComparisonHeader = "尚未偵測到載台側別（R/L），請先對板子執行一次 RUN";
        }
        else if (!AxisRecipeManager.HasBaseline(RecipeName, CarrierSide))
        {
            ComparisonHeader = $"Recipe「{RecipeName}」的 {CarrierSide} 側尚未建立 QC 基準";
        }
        else if (!_hasMeasurements)
        {
            ComparisonHeader = $"已有 {CarrierSide} 側基準，等待一輪量測結果後開始比對";
        }
        else
        {
            try
            {
                var baseline = AxisRecipeManager.LoadBaseline(RecipeName, CarrierSide);
                var rows = QcComparisonViewModel.BuildRows(baseline, snap.AreaA, snap.AreaB, _config);
                foreach (var r in rows) Rows.Add(r);
                ExceedCount = rows.Count(r => r.ExceedsTolerance);
                ComparisonHeader =
                    $"比對 Recipe「{RecipeName}」{CarrierSide} 側基準（{baseline.SavedAt:yyyy-MM-dd HH:mm}）" +
                    $"　→　{(ExceedCount > 0 ? $"⚠ 有 {ExceedCount} 個 Slot 偏離基準" : "✔ 全部貼近基準")}";
            }
            catch (Exception ex)
            {
                ComparisonHeader = $"讀取基準失敗：{ex.Message}";
            }
        }

        if (!QcCompareEnabled)
            ComparisonHeader = "⏸ QC 比對已停用：每輪 RUN 不會自動比對／記錄。以下為手動比對結果。　" + ComparisonHeader;

        CommandManager.InvalidateRequerySuggested();
    }

    private void RefreshBaselineInfo()
    {
        BaselineRInfo = DescribeBaseline("R");
        BaselineLInfo = DescribeBaseline("L");
    }

    private string DescribeBaseline(string side)
    {
        if (!HasRecipe) return "—";
        if (!AxisRecipeManager.HasBaseline(RecipeName, side)) return "尚未建立";
        try
        {
            var b = AxisRecipeManager.LoadBaseline(RecipeName, side);
            string by = string.IsNullOrWhiteSpace(b.SavedByUsername) ? "" : $"　by {b.SavedByUsername}";
            return $"{b.SavedAt:yyyy-MM-dd HH:mm}{by}";
        }
        catch
        {
            return "檔案損毀";
        }
    }

    /// <summary>把目前這一輪量測結果存成「當下側別」的 QC 基準。R、L 各存一份，需各跑一片良品各存一次。</summary>
    private void OnSaveBaseline()
    {
        if (!CanEditQc)
        {
            StatusMessage = "✘ 權限不足：僅 Engineer 以上可以存 QC 基準";
            return;
        }

        var snap = _snapshotProvider();
        string name = (snap.RecipeName ?? "").Trim();
        string side = (snap.CarrierSide ?? "").Trim();

        if (!AxisRecipeManager.IsValidName(name))
        {
            StatusMessage = "✘ 尚無生效中的 Recipe，無法存基準";
            return;
        }
        if (!AxisRecipeManager.IsValidSide(side))
        {
            StatusMessage = "✘ 尚未偵測到 R/L 側別，請先對良品板執行一次 RUN";
            return;
        }
        if (!snap.AreaA.Concat(snap.AreaB).Any(s => s.Value != 0))
        {
            StatusMessage = "✘ 目前尚無量測結果，請先對良品板執行一次 RUN";
            return;
        }
        if (AxisRecipeManager.HasBaseline(name, side) &&
            MessageBox.Show($"Recipe「{name}」的 {side} 側已有 QC 基準，是否覆蓋？",
                "確認覆蓋", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var data = QcBaselineData.FromSlotItems(name, side, _user?.Username ?? "", snap.Barcode ?? "",
            snap.AreaA, snap.AreaB);
        AxisRecipeManager.SaveBaseline(name, side, data);

        if (_user != null)
            AuditLogger.Write(_user.Username, _user.Role, $"儲存 QC 基準（Recipe「{name}」{side} 側）");

        StatusMessage = $"✔ 已存為 Recipe「{name}」{side} 側的 QC 基準";
        Refresh();
    }

    private void OnSaveTolerances()
    {
        if (!CanEditQc)
        {
            StatusMessage = "✘ 權限不足：僅 Engineer 以上可以改 QC 允許範圍";
            return;
        }

        _config.QcToleranceValue = QcToleranceValue;
        _config.QcToleranceMeanIntensity = QcToleranceMeanIntensity;
        _config.QcToleranceDistanceToRoiCenter = QcToleranceDistanceToRoiCenter;
        _config.Save();

        if (_user != null)
            AuditLogger.Write(_user.Username, _user.Role,
                $"套用 QC 容差（Value={QcToleranceValue}，亮度={QcToleranceMeanIntensity}，位置={QcToleranceDistanceToRoiCenter}）");

        StatusMessage = "✔ QC 容差已儲存，重新比對中…";
        Refresh();
    }

    private void OnOpenReportFolder()
    {
        string folder = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Results", "QcCompare");
        System.IO.Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start("explorer.exe", folder);
    }
}
