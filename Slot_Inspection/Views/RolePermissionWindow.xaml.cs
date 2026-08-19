using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Slot_Inspection.Helper;
using Slot_Inspection.Models;
using Slot_Inspection.ViewModels;

namespace Slot_Inspection.Views;

/// <summary>單一權限項目在UI上的可勾選列。Permission/DisplayName建立後不變，Operator/EngineerAllowed隨checkbox雙向繫結。</summary>
public sealed class PermissionRow(Permission permission, string displayName, bool operatorAllowed, bool engineerAllowed)
{
    public Permission Permission { get; } = permission;
    public string DisplayName { get; } = displayName;
    public bool OperatorAllowed { get; set; } = operatorAllowed;
    public bool EngineerAllowed { get; set; } = engineerAllowed;
}

public partial class RolePermissionWindow : Window
{
    private readonly UserAccount _currentAdmin;
    private readonly MainViewModel _mainViewModel;

    public ObservableCollection<PermissionRow> BasicRows { get; } = [];
    public ObservableCollection<PermissionRow> AdvancedRows { get; } = [];

    private static readonly (Permission Permission, string DisplayName)[] BasicDefs =
    [
        (Permission.Run, "RUN（開始量測）"),
        (Permission.Stop, "STOP"),
        (Permission.Home, "原點賦歸"),
        (Permission.InitDevices, "初始化設備"),
        (Permission.ReadBarcode, "讀取條碼"),
        (Permission.MoveToPickPosition, "移至取料位"),
        (Permission.OpenResultFolder, "開啟結果資料夾"),
        (Permission.SlcReset, "SLC Reset"),
        (Permission.ToggleLightGateBypass, "上料/下料模式切換"),
    ];

    private static readonly (Permission Permission, string DisplayName)[] AdvancedDefs =
    [
        (Permission.PositionSettings, "料號切換（含座標調整）"),
        (Permission.LightSettings, "光源設定"),
        (Permission.CropSettings, "裁切設定"),
        (Permission.AxisSettleDelay, "ZL/ZR 安定延遲"),
        (Permission.QcRecipe, "QC 基準比對"),
        // SimulationMode 固定為 Admin 專屬，不開放角色設定
    ];

    public RolePermissionWindow(UserAccount currentAdmin, MainViewModel mainViewModel)
    {
        InitializeComponent();
        _currentAdmin = currentAdmin;
        _mainViewModel = mainViewModel;
        DataContext = this;

        foreach (var (permission, displayName) in BasicDefs)
            BasicRows.Add(new PermissionRow(permission, displayName,
                RolePermissionManager.IsAllowed(UserRole.Operator, permission),
                RolePermissionManager.IsAllowed(UserRole.Engineer, permission)));

        foreach (var (permission, displayName) in AdvancedDefs)
            AdvancedRows.Add(new PermissionRow(permission, displayName,
                RolePermissionManager.IsAllowed(UserRole.Operator, permission),
                RolePermissionManager.IsAllowed(UserRole.Engineer, permission)));
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in BasicRows.Concat(AdvancedRows))
        {
            RolePermissionManager.SetAllowed(UserRole.Operator, row.Permission, row.OperatorAllowed);
            RolePermissionManager.SetAllowed(UserRole.Engineer, row.Permission, row.EngineerAllowed);
        }

        AuditLogger.Write(_currentAdmin.Username, _currentAdmin.Role, "修改角色權限設定");
        _mainViewModel.RefreshPermissions();
        DialogResult = true;
    }
}
