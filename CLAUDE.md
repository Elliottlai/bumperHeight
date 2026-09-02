# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# Slot Inspection 專案說明

工業用插槽外觀檢測機台的 WPF C# 應用程式（.NET 8, WPF, MVVM）。

## 建置與執行

**必須用 Visual Studio 的 MSBuild 建置整個 solution**，因為 `TestALG` 是舊式 .NET Framework 4.8 專案，`dotnet build` 對它的 Emgu.CV 還原會失敗（CS0246 找不到 `Mat`）：

```
"C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe" bumperHeight.sln -p:Configuration=Debug -restore
```

（MSBuild.exe 確切路徑會隨 VS 版本/安裝版（Community/Professional/Insiders）改變，先用 `Get-ChildItem` 找一次目前實際裝的是哪個，不要照抄舊路徑硬用。）

- 組態：`Debug` / `Release` / `Debug-Sim`（定義 `DEBUG_SIM` 常數 → `MachineController.SimulationMode` 預設 true，無硬體可跑）
- **給機台/實機用一律建置 `Debug`，不要用 `Debug-Sim`**：`SimulationMode` 是純執行期 static 旗標、不會持久化存檔，`Debug-Sim` 編出來的預設值 `true` 一旦忘記在 UI 手動關掉，機台重啟後會靜默改跑模擬資料（讀假圖、軸不移動），量測結果容易被誤判為正常
- ⚠️ **已知問題（2026-09-01 發現，尚未修）**：`bumperHeight.sln` 的方案組態對應設定有誤，選 `Debug-Sim` 組態建置時，主程式 `Slot_Inspection.csproj` 實際被對應成 `Debug`（不是 `Debug-Sim`），`DEBUG_SIM` 常數根本沒有定義到主程式裡，`SimulationMode` 不會預設 `true`。要在無硬體開發機上測試模擬模式，目前只能用 `Debug` 組態建置，登入後在 UI 手動勾選「模擬模式」（僅 Admin、非執行中可切換），不要依賴 `Debug-Sim` 組態自動生效
- 只建置純 net8 子專案（Machine.Core、PLC_IO 等）時 `dotnet build` 可用；但主程式 `Slot_Inspection.csproj` 依賴 TestALG，仍需走 MSBuild
- 執行：`Slot_Inspection\bin\Debug\net8.0-windows\Slot_Inspection.exe`
- 本 repo **沒有單元測試專案**；`TestALG` 是獨立的演算法測試 GUI 工具，不是測試專案
- 主視窗標題列與程式啟動時的 log 開頭會顯示版本號（`Slot_Inspection.csproj` 的 `InformationalVersion`，格式為建置當下日期），每次重新建置都會自動更新，部署後可以直接看標題列確認機台上是不是最新版本，不用每次都做 MD5 比對

## 部署到機台測試

機台（實機，有 PLC/軸體硬體）透過網路芳鄰共用資料夾更新，路徑固定是：

```
\\DESKTOP-0CCMP1C\Software\Slot_Inspection_正式版\net8.0-windows
```

（2026-08-28 起：這是正式上線版本的資料夾，從原本的 `Nico_Test\Slot_Inspection_0821` 改名搬移過來，
機台桌面捷徑需要手動更新指向新路徑，才不會開到舊的失效路徑。）

**一律用 `Scripts\deploy.ps1` 部署，不要手動壓縮/複製整包資料夾**——它用 robocopy 做增量同步，只複製有變更的檔案（CUDA/cuDNN/onnxruntime 等原生 DLL 平常不會變，全部跳過），一次部署通常只要幾秒到幾十秒，比整包壓縮/解壓縮快非常多。

```powershell
# 先建置（機台實際要用的組態，一律 Debug，見上面「已知問題」說明）
"C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe" bumperHeight.sln -p:Configuration=Debug -restore

# 再部署（預設來源 bin\Debug\net8.0-windows，預設目的地就是上面那個機台路徑）
.\Scripts\deploy.ps1

# 想先看這次會複製哪些檔案、不真的動到機台，加 -Preview
.\Scripts\deploy.ps1 -Preview
```

- 部署腳本會自動排除 `Config`、`logs`、`Results`、`MachineAssembly`、`MvSDKLog`、`WebApiLog`——這些是機台現場執行時期會自己產生/累積的資料，不會被覆蓋或刪除
- 每次執行的複製明細存在 `Scripts\deploy_logs\deploy_<時間戳記>.log`
- **部署前務必先確認機台上沒有殘留執行中的 `Slot_Inspection.exe`**，否則 DLL 會被鎖住、robocopy 會回報 FAILED；卡死或未回應的舊程序也要先在工作管理員強制結束
- 機台上的 log 檔在 `<部署目的地>\logs\<yyyy-MM-dd>.log`，可以直接用檔案路徑讀取（`\\DESKTOP-0CCMP1C\Software\Slot_Inspection_正式版\net8.0-windows\logs\...`），不需要使用者手動複製回來
- **每次部署完，用 `Get-FileHash -Algorithm MD5` 比對本機建置輸出跟機台上同一份 DLL/EXE 的雜湊值**，兩邊一致才算部署完成，不能只看 robocopy 沒報錯——曾經遇過檔案被鎖住導致 robocopy FAILED 卻沒即時注意到

### 沒辦法直接連機台時：改用小包裹請人代為更新
如果暫時連不上 `\\DESKTOP-0CCMP1C\Software`（人不在現場、網路不通等），可以改成：只把「這次修改實際變動到的專案」對應的輸出檔案（`.dll`/`.exe`/`.pdb`，比對 `git diff --stat` 就知道動到哪些專案）打包成一個小 zip（通常幾百 KB 到 1MB，不用整包 `bin\Debug\net8.0-windows`），存到 `給客服的資料\` 這個資料夾，附上文字說明「確認沒有殘留執行中的 exe → 解壓縮覆蓋同名檔案 → 重開程式確認標題列版本號」，交給能到現場的人（客服/同事）代為覆蓋更新，不需要對方有這個 repo 或建置環境。

## Git Remote

這個 repo 同時推到兩個地方，**單純 `git push` 只會推 `origin`**，要同時更新兩邊要各自指定：

| Remote 名稱 | 位置 | 指令 |
|---|---|---|
| `origin` | GitHub（`Elliottlai/bumperHeight`） | `git push origin <branch>` |
| `PD330_main_flow` | 內部 Git（`http://RD_Git:8081/NicoChen/PD330.git`） | `git push PD330_main_flow <branch>` |

⚠️ **commit 前務必確認沒有不小心夾帶大型二進位檔案**（廠商交付的原始資料夾、備份資料夾、CUDA/cuDNN/onnxruntime 等原生執行期 DLL）——GitHub 對單一檔案有 100MB 硬性上限，曾經不小心把 1.5GB 的原生依賴整批 commit 進去，push 到一半才發現，只能用 `git commit --amend`（僅限還沒 push 過的 commit）重寫掉。`.gitignore` 已經排除 `AI演算法整合資料/`、`Lib/BumperFlatDetection_backup_*/`，以及 `Lib/BumperFlatDetection/` 底下除了 `BumperFlatDetection.dll`/`SegmentationNative.dll` 以外的所有檔案——這些是可以從 NuGet/官方安裝檔重新取得的通用原生執行期 DLL，不需要進版控。

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

**HighBright 光源已知問題**：控制器有時會卡進「對所有後續指令都回應通用錯誤碼 `E`（偶爾夾雜殘缺回應如 `"1,<"`）」的狀態，COM Port 本身不會斷線（`IsOpen` 全程 true），但重開軟體/重連 COM Port 都無法清除，**唯一有效的解法是把控制器實體斷電重開**。廠商說明是 RS-232 指令彼此重疊（間隔不足 300ms）導致控制器略過指令；`HighBright_Controller.SetValue()`/`TurnOffAll()` 已經加上 300ms 最短指令間隔保護，能降低觸發機率但無法保證完全不會再發生。

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
- `GetPlcXOctal` 回傳 `bool?`：**null 表示「無法信任這次讀值」**，不代表訊號正常
  - 永遠不要用 `?? true` 或 `?? false` 把「不確定」硬套成某個確定狀態（尤其是安全訊號，會誤解除警報）
  - 正確做法：null 時凍結現有狀態，不更新計數器/計時器，不觸發任何轉換
  - null 有兩種成因，`GetPlcXOctal` 內部都會回傳 null：① `IPlcCommunicator.IsConnected`=false（底層連線斷了，例如 COM Port 沒開）；② `IsConnected`=true 但 `IsXDataFresh`=false——**連線沒斷不代表讀取還在正常更新**，如果背景輪詢因為佇列卡住/裝置不回應而停止更新，`IsConnected` 會一直是 true，只有 `IsXDataFresh`（`FxPlcCommunicator` 內部追蹤「最後一次成功解析 X 訊框的時間」，超過門檻沒更新就視為不新鮮）才抓得到這種情況

### IO 訊號 Debounce 模式
持續狀態的 IO 訊號（例如板子在席感測器、光閘 X7、EMO X0）需要 debounce，避免雜訊誤觸發，目前並存兩種寫法：

- **舊寫法（X3/X11 板子感測等仍在用）**：數「連續 tick 次數」，觸發/恢復都要連續 N 次同方向才確認（`_xxxCount >= Threshold`）
- **新寫法（EMO、光閘 X7 已改用，之後新增安全訊號建議直接用這個）**：改算「牆鐘時間」，不數 tick 次數——原因是 `DispatcherTimer` 在 UI 執行緒忙碌時，tick 間隔常會被拉長到數秒甚至更久（EMO 8/25、光閘 9/1 都實測過），用「連續 N 次 tick」防抖在這種情況下時間感會跟著失準，改成「從第一次讀到某個狀態算起，經過固定時長 X 仍持續同狀態才確認」，不管中間 tick 實際間隔多長、多不規則都準確
- **只在狀態轉換時寫 Log**，不在每個 tick 都寫

### 安全訊號的獨立背景 Thread 模式（EMO、光閘 X7）
EMO（X0）跟光閘（X7）都已經改成**獨立的 background `Thread`**（不是 `DispatcherTimer`、也刻意不是 `System.Threading.Timer`/ThreadPool），不再共用 UI 執行緒的 `_plcTimer`，範例見 `MainViewModel.EmoWatchLoop`/`OnEmoTickCore` 與 `LightGateWatchLoop`/`OnLightGateTickCore`。之後如果有新的安全關鍵訊號要處理，照抄這一套，幾個關鍵點缺一不可：

1. **獨立 Thread，不是 ThreadPool**：ThreadPool 曾經在 EMO 觸發、軸體 Modbus 大量重試時被佔滿，導致連 EMO 自己的輪詢 callback 都排不到執行緒可以跑（8/25 現場實測）
2. **確認「剛觸發」的當下，取消流程／強制煞停要直接在這條背景 Thread 上同步做**，不要透過 `Dispatcher.Invoke`/`BeginInvoke` 先切回 UI 執行緒才做——機台停下來的動作不能因為 UI 執行緒忙碌而跟著延遲
3. **套用 UI 綁定狀態（畫面文字、警報、MES 回報…）要切回 UI 執行緒，但用 `Dispatcher.BeginInvoke`（非阻塞），不要用 `Invoke`**——用會阻塞的 `Invoke`，一旦 UI 執行緒卡住，這條專屬背景 Thread 也會被拖著一起卡死，等於獨立 Thread 的保護整個失效（EMO 8/26 現場實測踩過這個坑）
4. **防抖邏輯判斷「目前是不是已經處於觸發狀態」，要用背景 Thread 自己同步維護的欄位（例如 `_lightGateBlockedConfirmed`），不要用透過 `BeginInvoke` 非同步更新、可能延遲的 UI 屬性（例如 `IsLightGateBlocked`）本身**——UI 執行緒忙碌時，這個屬性的實際寫入會延遲，背景 Thread 下一輪輪詢如果讀到舊值，會判斷錯分支、把真正的新觸發/新解除吃掉（光閘 9/1 實測踩過這個坑：手按住光閘模擬觸發超過門檻時間卻沒有觸發）。UI 屬性只當作「反映結果給畫面看」的鏡子，不要拿來當背景 Thread 自己判斷用的依據

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
