# DeltaAxis_RS485 部署說明（機台現場）

> 這份文件講「怎麼把 DeltaAxis_RS485 這支手動推軸工具放到機台電腦上、並讓它自動讀到主程式的安全設定」。
> 寫給第一次接觸這個專案的人，看不懂的名詞都有白話解釋。

---

## 1. 這支程式是什麼、為什麼要讀「安全設定」

`DeltaAxis_RS485.exe` 是一支**手動一格一格推軸**的小工具（Y / X / ZL / ZR 四軸），
用來在機台現場單獨測試軸體、對點、排除問題，不跑正式檢測流程。

因為是「純手動」，沒有流程保護，容易發生：

- 手滑把 ZL / ZR 往下降太多 → 鏡頭撞到插槽
- Z 軸還在下面沒收上來就去移動 Y 軸 → 載台橫著撞過去

**「安全設定」** 這個功能，就是讓 DeltaAxis 去**借用正式主程式 `Slot_Inspection` 已經設好的那些數字**
（ZL/ZR 安全高度、相機檢測高度、各 Slot 的 Y 座標），在你快要做危險動作時**跳警告或直接擋下**。

DeltaAxis 自己**不存**任何安全數字，它只是打開主程式的設定檔來看：

```
<主程式資料夾>\Config\InspectionConfig.json   ← 安全高度、檢測高度、取料位置
<主程式資料夾>\Config\SlotPositions.json      ← 各 Slot 的 Y 座標
```

所以「部署」要做的事只有一件：**讓 DeltaAxis 找得到主程式的 `Config\` 資料夾**。

---

## 2. 前提

| 項目 | 說明 |
|---|---|
| .NET 8 Desktop Runtime | 機台上一定已經有——能跑主程式 `Slot_Inspection`（也是 .NET 8）就代表有，DeltaAxis 直接沾光 |
| 主程式的安全高度已教導 | `InspectionConfig.json` 裡的 `ZLSafeHeight_R/L`、`ZRSafeHeight_R/L` 不能是 `0`（見第 6 節） |
| COM12～COM15 沒被佔用 | DeltaAxis 要推軸就得用這幾個 COM Port，**主程式 `Slot_Inspection` 必須先關掉**，兩支不能同時開 |

---

## 3. DeltaAxis 開機時怎麼自動找 Config

程式一啟動會照這個順序一個一個試，**第一個「資料夾存在、而且裡面有那兩個 json」的就採用**：

| 順序 | 找的位置 | 什麼情況會用到 |
|---|---|---|
| 1 | 工具列輸入框你手動填的路徑 | 你這次自己指定 |
| 2 | 環境變數 `SLOT_INSPECTION_CONFIG` | 系統層級設一次，一勞永逸 |
| 3 | `DeltaAxisSettings.json` 記住的上次成功路徑 | 上次成功讀過就記住，下次自動帶入 |
| 4 | `DeltaAxis_RS485.exe 旁邊\Config` | **exe 直接放在主程式資料夾裡** |
| 5 | 上一層 `..\Config`、上兩層 `..\..\Config` | **exe 放在主程式資料夾的子資料夾裡** |
| 6 | `..\Slot_Inspection\Config` 等 | 兩支程式並排放 |
| 7 | 開發機的相對路徑 | 只有開發電腦會中 |
| 8 | `\\DESKTOP-0CCMP1C\Software\Slot_Inspection_正式版\net8.0-windows\Config` | 寫死的機台網路路徑，最後一搏 |

**本文件的做法就是靠順序 4 或 5**——把 exe 放在主程式資料夾（或其子資料夾），Config 自動找到，**完全不用手動設定路徑**。

---

## 4. 部署做法

機台上主程式的正式資料夾是：

```
\\DESKTOP-0CCMP1C\Software\Slot_Inspection_正式版\net8.0-windows\
├── Slot_Inspection.exe          ← 主程式
├── Config\                       ← 安全設定就在這（deploy.ps1 不會覆蓋它）
├── DeltaAxis_RS485.exe          ← 這支！主程式有參考它，deploy.ps1 會一起帶上去
└── ...
```

### 做法一：直接用主程式資料夾裡現成的 exe（最省事）

`DeltaAxis_RS485.exe` 因為被主程式參考，**建置時就已經被複製進主程式的輸出資料夾**，
跟 `Config\` 在同一層。所以：

1. 正常用 `Scripts\deploy.ps1` 部署主程式（它不會排除 DeltaAxis 的檔案）
   ```powershell
   .\Scripts\deploy.ps1
   ```
2. 到機台的 `...\net8.0-windows\` 資料夾，對 `DeltaAxis_RS485.exe`
   **按右鍵 → 建立捷徑 → 拖到桌面**
3. 完成。開機時走順序 4 自動找到旁邊的 `Config\`

> 小缺點：DeltaAxis 執行時會在自己旁邊產生 `DeltaAxisSettings.json`、log 等檔案，
> 這些會混在主程式資料夾裡（不影響運作，只是看起來雜）。介意的話用做法二。

### 做法二：放獨立子資料夾（比較乾淨，推薦長期擺著用）

把 DeltaAxis 放進主程式資料夾底下一個獨立子資料夾，跟主程式的檔案分開：

```
\\DESKTOP-0CCMP1C\Software\Slot_Inspection_正式版\net8.0-windows\
├── Slot_Inspection.exe
├── Config\
└── DeltaAxisTool\               ← 新建這個子資料夾
    ├── DeltaAxis_RS485.exe
    ├── DeltaAxis_RS485.dll
    └── ...（DeltaAxis 的所有相依檔）
```

DeltaAxis 開機時走順序 5 的「上一層 `..\Config`」→ 找到 `net8.0-windows\Config\`，一樣零設定。

**步驟：**

1. 建置整包（一律 `Debug`）
   ```powershell
   "C:\Program Files\Microsoft Visual Studio\<版本>\<版別>\MSBuild\Current\Bin\MSBuild.exe" bumperHeight.sln -p:Configuration=Debug -restore
   ```
   （MSBuild.exe 確切路徑會隨 VS 版本改變，先用 `Get-ChildItem` 找一次目前實際裝的是哪個。）

2. 複製 **DeltaAxis 自己的輸出資料夾**（不是主程式的那個）的全部內容：
   ```
   來源： D:\Programs\Slot_Inspection_最新版\DeltaAxis_RS485\bin\Debug\net8.0-windows\   （整包，約 47 MB）
   目的： \\DESKTOP-0CCMP1C\Software\Slot_Inspection_正式版\net8.0-windows\DeltaAxisTool\
   ```

3. **複製前先刪掉來源裡的 `DeltaAxisSettings.json`**——這個檔會記著**開發機的路徑**，機台上不存在。
   留著不會出錯（找不到會自動往下一個候選找），但刪掉讓機台乾淨重新偵測。

4. 到機台桌面對 `DeltaAxisTool\DeltaAxis_RS485.exe` 建捷徑。

> `deploy.ps1` 只同步主程式的 `Slot_Inspection\bin\...`，**不會**碰這個 `DeltaAxisTool\` 子資料夾
> （它不加 `/MIR`，不會刪機台上的額外檔案）。所以之後 DeltaAxis 有更新時，這個子資料夾要**手動**重新複製。

### 做法三：設環境變數（IT 統一管理機台時最乾淨）

不管 exe 放哪、重裝幾次都讀得到。在機台電腦「系統內容 → 進階 → 環境變數」新增**系統變數**：

```
變數名稱： SLOT_INSPECTION_CONFIG
變數值：   D:\Software\Slot_Inspection_正式版\net8.0-windows\Config
          （↑ 換成機台上主程式 Config 的實際完整路徑）
```

設完 DeltaAxis 走順序 2 直接命中。要改路徑也只改這一個地方。

---

## 5. 連不上機台時：打包給客服代為更新

人不在現場、網路不通，可以把 DeltaAxis 自己的輸出資料夾壓成 zip 交給能到現場的人：

1. 來源資料夾：`DeltaAxis_RS485\bin\Debug\net8.0-windows\`
2. **先刪掉 `DeltaAxisSettings.json`**（開發機路徑，見上）
3. （可選瘦身）裡面有一半是相機 SDK（`DALSA.SaperaLT.SapClassBasic.dll` 23 MB、`halcondotnetxl.dll`、
   `Basler.Pylon.dll` 等），是 `Machine.Core` 硬拖進來、DeltaAxis 根本沒用到的，可以刪。
   不確定就整包壓，一定能跑，只是肥一點。
4. 壓成 zip，放到 repo 的 `給客服的資料\` 資料夾
5. 附文字說明給客服：
   > 「① 確認機台上 `Slot_Inspection.exe` 已關閉（工作管理員也要確認沒有殘留）
   > → ② 在主程式資料夾 `...\net8.0-windows\` 底下建一個 `DeltaAxisTool\` 子資料夾
   > → ③ 把 zip 解壓縮進去
   > → ④ 對 `DeltaAxisTool\DeltaAxis_RS485.exe` 建桌面捷徑
   > → ⑤ 開起來，看視窗最下面狀態列是不是顯示綠色勾勾『✔ 已讀取…』」

---

## 6. 驗證：怎麼確認安全設定真的讀到了

開 DeltaAxis 後，**看視窗最下面那條狀態列**：

| 顯示 | 意思 | 要做什麼 |
|---|---|---|
| `✔ 已讀取（時間）自 <資料夾>｜已知Y位置N點，ZL深度上限…，Y移動前Z需低於 ZL…/ZR…mm（主程式安全高度+容差…）｜自動跟隨：開` | 成功 | 「自 ___」後面就是它實際採用的資料夾，確認是機台上主程式的 Config 就對了 |
| `✘ 找不到 Slot_Inspection 的 Config 資料夾…` | 八個候選位置都沒中 | 在工具列輸入框貼上主程式 Config 的完整路徑 → 按「讀取安全設定」；成功後路徑會存進 `DeltaAxisSettings.json`，下次自動帶入 |
| `⚠ 這次重讀失敗…仍沿用上次讀到的設定` | 暫時性讀取失敗（例如剛好讀到主程式寫一半的檔），已自動保留上一份好的 | 通常自己會恢復，不用管 |

**進階確認**：狀態列的「Y移動前Z需低於 ZL…/ZR…mm」數字，應該跟主程式 `InspectionConfig.json` 裡
`ZLSafeHeight_*` / `ZRSafeHeight_*` 加上 `ZSafeTolerance` 對得起來。

### 「自動跟隨主程式變更」

工具列有個勾選框（預設打勾）。打勾時，機台上有人在主程式改了安全高度並存檔，
DeltaAxis 會在約 1 秒後**自動重新讀一次**，狀態列出現 `↻ 偵測到主程式設定變更，已自動重新載入`。
不用手動按「讀取安全設定」。

---

## 7. 安全設定實際上在檢查什麼

讀到設定後，以下保護會自動生效，你不用做任何事：

| 你的動作 | DeltaAxis 會做的檢查 | 結果 |
|---|---|---|
| 移動 **Y 軸** | ZL/ZR 是否都已連線、絕對座標正常，且高度**低於安全高度門檻**（門檻＝主程式安全高度＋容差） | 不符合 → **直接擋下**，狀態列寫原因 |
| 移動 **ZL / ZR 往下** | ① Y 軸目前位置不在任何已知 Slot / 取料位置上？<br>② 目標比主程式設定的相機檢測高度還深？ | 觸發 → 跳「有撞機風險，確定繼續？」對話框（**警告，可選擇繼續**，不強制擋） |
| 移動接近「取料點」 | 目標是否落在取料點 ± 容差內 | 觸發 → 跳「接近參考位置，確定繼續？」對話框 |

### ⚠️ 前提：主程式的安全高度必須教過

如果主程式 `InspectionConfig.json` 裡的 `ZLSafeHeight_*` / `ZRSafeHeight_*` 還是預設的 `0`（沒教過），
DeltaAxis 會照 `0` 算 → 變成「Z 幾乎要完全歸零才准移動 Y」，非常嚴格、很容易被擋。

**部署前務必先在主程式把 ZL/ZR 安全高度教成實際值。**
用記事本打開 `Config\InspectionConfig.json`，確認 `ZLSafeHeight_R`、`ZLSafeHeight_L`、
`ZRSafeHeight_R`、`ZRSafeHeight_L` 不是 `0`。

如果 DeltaAxis **完全找不到** Config（狀態列 `✘`），Y 軸移動門檻會退回內建保守值 `5 mm`，
且「Y 是否在已知 Slot 位置」的比對會整個停用——這是最低限度的保護，不是正常狀態。

---

## 7.5 各軸「定位控制目標上限」（改設定檔）

「定位控制」的目標位置可以設一個**上限**：輸入的目標超過上限時，

- 「移動」按鈕旁邊會即時出現一行**紅字**「超過上限 XXX mm」
- 「移動」按鈕會**變灰不能按**（打字當下就變灰）
- Jog 若會讓位置超過上限，也會被擋下不動（狀態列寫原因）

這個上限**只能改設定檔，介面上不提供修改**（避免現場誤觸放寬）。

檔案：`<DeltaAxis_RS485.exe 旁>\DeltaAxisSettings.json`，裡面的 `AxisTargetUpperLimitMm` 段：

```json
{
  "AxisTargetUpperLimitMm": {
    "Y軸": 496.0,
    "X軸": null,
    "ZL軸": null,
    "ZR軸": null
  }
}
```

- 數字 = 該軸上限（mm）
- `null` = 該軸不限制
- 第一次執行程式時會自動寫出這段範本（Y 軸帶行程極限 496mm，其餘 `null`），照著改就好
- **改完存檔 → 重開 DeltaAxis 才生效**

---

## 8. 疑難排解

| 症狀 | 原因 / 處理 |
|---|---|
| 開 DeltaAxis 後某個軸連不上、報 COM Port 被占用 | 主程式 `Slot_Inspection` 還開著，或工作管理員有殘留的 `Slot_Inspection.exe`。先全部關掉 |
| 狀態列一直 `✘ 找不到…` | exe 沒放在主程式資料夾（或其子資料夾）旁邊。改用做法三設環境變數，或在輸入框手動指定路徑按「讀取安全設定」 |
| 狀態列顯示的路徑是舊的 / 錯的 | 刪掉 DeltaAxis exe 旁邊的 `DeltaAxisSettings.json`（它記著上次的路徑），重開讓它重新偵測 |
| 主程式改了安全高度，DeltaAxis 沒跟著變 | 確認工具列「自動跟隨主程式變更」有打勾；或手動按一次「讀取安全設定」 |
| 移動 Y 軸一直被擋，說「Z 未低於安全高度門檻」 | ① 先把 ZL/ZR 降到安全高度以下再移動 Y（正常防呆）<br>② 若門檻數字明顯不對，檢查主程式安全高度是不是還沒教（見第 7 節） |
| DeltaAxis 更新後機台還是舊版 | 做法一：重跑 `deploy.ps1`。做法二 / 三：`DeltaAxisTool\` 子資料夾要手動重新複製，`deploy.ps1` 不會碰它 |

---

## 9. 相關檔案位置速查

| 檔案 | 路徑 | 說明 |
|---|---|---|
| DeltaAxis 主程式 | `DeltaAxis_RS485\bin\Debug\net8.0-windows\DeltaAxis_RS485.exe` | 建置輸出 |
| DeltaAxis 自己的小設定 | `<DeltaAxis.exe 旁>\DeltaAxisSettings.json` | 記住上次 Config 路徑、自動跟隨偏好、各軸定位上限（見 7.5）；可安全刪除（會重建，但軸上限會回到範本值） |
| 主程式安全設定來源 | `<主程式資料夾>\Config\InspectionConfig.json`、`SlotPositions.json` | DeltaAxis 讀取的對象 |
| 尋找 Config 的邏輯 | `DeltaAxis_RS485\Services\SlotInspectionConfigLocator.cs` | 第 3 節那張順序表的實作 |
| 讀取 + 自動跟隨的邏輯 | `DeltaAxis_RS485\ViewModels\MainViewModel.cs`（`LoadSafetyConfig` / `ArmConfigWatcher`） | — |
