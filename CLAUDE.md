# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# Slot Inspection 專案說明

工業用插槽外觀檢測機台的 WPF C# 應用程式（.NET 8, WPF, MVVM）。

## 建置與執行

**必須用 Visual Studio 的 MSBuild 建置整個 solution**，因為 `TestALG` 是舊式 .NET Framework 4.8 專案，`dotnet build` 對它的 Emgu.CV 還原會失敗（CS0246 找不到 `Mat`）：

```
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" bumperHeight.sln -p:Configuration=Debug-Sim -restore
```

- 組態：`Debug` / `Release` / `Debug-Sim`（定義 `DEBUG_SIM` 常數 → `MachineController.SimulationMode` 預設 true，無硬體可跑）
- 只建置純 net8 子專案（Machine.Core、PLC_IO 等）時 `dotnet build` 可用；但主程式 `Slot_Inspection.csproj` 依賴 TestALG，仍需走 MSBuild
- 執行：`Slot_Inspection\bin\Debug\net8.0-windows\Slot_Inspection.exe`（開發機無硬體時用 Debug-Sim 建置）
- 本 repo **沒有單元測試專案**；`TestALG` 是獨立的演算法測試 GUI 工具，不是測試專案

## 專案結構

| 目錄 | 用途 |
|------|------|
| `Slot_Inspection/` | 主應用程式（WPF MVVM） |
| `Machine.Core/` | 軸控核心函式庫（`IAxis` 介面 + 各廠牌實作） |
| `DeltaAxis_RS485/` | Delta ASDA-B3 RS485 軸控實作 |
| `PLC_IO/` | 三菱 FX PLC 通訊 |
| `BarcodeReader/` | 海康讀碼器 SDK |
| `webapidll/` | MES WebAPI 用戶端 |

主要 solution：`bumperHeight.sln`

主流程集中在兩個大檔案：
- `Slot_Inspection/Services/MachineController.cs`（~2400 行）：所有硬體整合與 S00–S03 檢測流程的實際執行
- `Slot_Inspection/ViewModels/MainViewModel.cs`（~2000 行）：UI 狀態、指令、流程啟動/取消與 DispatcherTimer 輪詢

## 硬體配置

| 設備 | 連線 |
|------|------|
| AxisY（載台移動）| COM12, SlaveId=1 |
| AxisX（讀碼軸）| COM13, SlaveId=1 |
| AxisZL（左相機高度）| COM14, SlaveId=1 |
| AxisZR（右相機高度）| COM15, SlaveId=1 |
| 三菱 FX PLC | COM11, 115200 baud |
| HighBright 光源 | COM16 |
| 相機（Left / Right）| Sapera SDK |
| 讀碼器 | 海康 SDK, 192.168.1.10:8000 |

## 架構慣例

### MVVM 模式
- ViewModel 繼承 `ObservableObject`，指令用 `RelayCommand`
- UI 狀態變更必須在 Dispatcher 執行緒上；DispatcherTimer tick 裡的 UI 動作（含 MessageBox）一律用 `Application.Current.Dispatcher.BeginInvoke(...)` 延後執行，避免巢狀訊息泵（re-entrancy）

### 模擬模式
- `MachineController.SimulationMode` 靜態旗標，`#if DEBUG_SIM` 組態預設 true
- **所有** 硬體操作方法開頭必須先檢查 `SimulationMode`，模擬時回傳假資料或直接 return，不碰硬體

### CancellationToken
- 所有 async 流程方法必須接受並傳遞 `CancellationToken ct`
- **發出軸移動指令（`MotMoveAbs` / `MotMoveRel`）之前，一定要先呼叫 `ct.ThrowIfCancellationRequested()`**，避免對話框期間 tick 已取消但軸還在動

### Z 軸安全高度
- **Y 軸移動前必須先呼叫 `EnsureZSafeBeforeYMoveAsync()`**，確認 ZL/ZR 已在安全高度
- 直接移動 Y 軸而不確認 Z 軸高度，可能造成機械碰撞

### PLC IO 讀取規則
- 三菱 FX X 點位是**八進制**位址（X10 = index 8、X12 = index 10）
- **一律用 `GetPlcXOctal(int plcOctalAddr)` / `SetPlcYOctal(int, bool)`**，傳入面板上看到的數字
- `GetPlcXOctal` 回傳 `bool?`：**null 表示 PLC 斷線**，不代表訊號正常
  - 永遠不要用 `?? true`（把斷線當正常，會誤解除警報）
  - 正確做法：null 時凍結現有狀態，不更新計數器，不觸發任何轉換

### IO 訊號 Debounce 模式
持續狀態的 IO 訊號（例如板子在席感測器）需要 debounce，避免雜訊誤觸發：
- 觸發方向：連續 N 次讀到異常才確認（`_xxxCount >= Threshold`）
- 恢復方向：連續 N 次讀到正常才確認
- **只在狀態轉換時寫 Log**，不在每個 tick 都寫

### 設定檔
- 所有 JSON 設定存於執行檔旁的 `Config/` 目錄
  - `InspectionConfig.json`：光源、曝光、閾值、軸高度等參數
  - `SlotPositions.json`：Y 軸各 Slot 座標
  - `CimApiSettings.json`：MES WebAPI 設定
  - `Recipes/{name}.json`：軸參數 Recipe
- `InspectionConfig.Load()` 讀取；`config.Save()` 寫入

### Logging
- 一般 Log：NLog，各類別 `private static readonly Logger _logger = LogManager.GetCurrentClassLogger()`
- 使用者操作稽核：`AuditLogger.Write(username, role, action)`，存於 `Logs/AuditLog/AuditLog_yyyyMMdd.log`
- 流程取消事件：`CancelEventLogger.Write(source, detail)`，存於 `Logs/CancelEvent/`

## 帳號與權限

角色：`Admin` > `Engineer` > `Operator`
- 角色權限定義在 `RolePermissions.json`，由 `RolePermissionManager` 管理
- 使用者異動（新增/刪除/改角色）需透過 `UserManager`，並同步寫入 `AuditLogger`
- **刪除或降級帳號前必須確保系統中仍保留至少一個 Admin**

## 檢測流程步驟

- **S00** Init：連線所有設備，回原點
- **S01** 讀取載台在席（X12/X13）+ 讀條碼（AxisX 移動）
- **S03** 檢測：AxisY 依 Slot 座標移動 → ZL/ZR 下降拍照 → ZL/ZR 升回安全高度 → 下一 Slot
- 流程取消後需重新 Init 才能再次 Run

## 注意事項

- `cAxis_Simulation` 是純假軸，不接硬體，所有方法直接回傳固定值，用於模擬模式
- `AsdaB3AxisAdapter`（DeltaAxis_RS485 專案）和 `cAxis_RS485`（Machine.Core 專案）功能相同，目前主程式使用 `cAxis_RS485`
- Y 軸行程極限：`YAxisMaxMm = 496.0 mm`
- ZL/ZR 到位後建議等 150 ms 讓驅動器清除 InPosition 旗標，再開始輪詢 `Wait()`
