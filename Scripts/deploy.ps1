<#
.SYNOPSIS
    把本機建置輸出（bin\Debug\net8.0-windows）增量同步到機台的網路共用資料夾。

.DESCRIPTION
    用 robocopy 取代「壓縮 -> 搬過去 -> 解壓縮」流程。robocopy 預設只複製檔案大小
    或最後修改時間有差異的檔案，其餘完全跳過，所以除了第一次之外，之後每次更新
    通常幾秒到幾十秒就結束（CUDA/cuDNN/onnxruntime/Emgu.CV 這些幾百 MB 的原生
    DLL 平常不會變，不會每次都重新複製）。

    不加 /MIR 或 /PURGE，且預設排除 Config、logs、Results、MachineAssembly、MvSDKLog、
    WebApiLog 這幾個機台執行時期會自己產生/累積內容的資料夾，避免覆蓋或刪除機台上
    現場調校過的設定檔、Recipe、稽核 log、量測結果。要更新設定檔請另外手動處理。

    目的地預設是機台實際執行 exe 的路徑本身（\\DESKTOP-0CCMP1C\Software\
    Slot_Inspection_正式版\net8.0-windows，正式上線版本，2026-08-28 由 Nico_Test\Slot_Inspection_0821
    改名搬移過來），同步完立即生效，機台不用重開機、不用手動搬檔。

.PARAMETER Preview
    只顯示這次會複製哪些檔案，不實際執行（robocopy /L 空跑模式）。

.EXAMPLE
    .\deploy.ps1
    直接同步到機台。

.EXAMPLE
    .\deploy.ps1 -Preview
    先看看這次會動到哪些檔案，確認沒問題再拿掉 -Preview 正式執行。
#>
param(
    [string]$Source = "D:\Programs\Slot_Inspection_最新版\Slot_Inspection\bin\Debug\net8.0-windows",
    [string]$Destination = "\\DESKTOP-0CCMP1C\Software\Slot_Inspection_正式版\net8.0-windows",
    [switch]$Preview
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Source)) {
    Write-Host "[deploy] 找不到來源資料夾: $Source" -ForegroundColor Red
    Write-Host "[deploy] 請先用 MSBuild 建置過再執行這個腳本。" -ForegroundColor Red
    exit 1
}

$logDir = Join-Path $PSScriptRoot "deploy_logs"
if (-not (Test-Path $logDir)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$logFile = Join-Path $logDir "deploy_$timestamp.log"

Write-Host "[deploy] 來源: $Source"
Write-Host "[deploy] 目的: $Destination"
Write-Host "[deploy] Log : $logFile"
if ($Preview) {
    Write-Host "[deploy] === 預覽模式，不會實際複製 ===" -ForegroundColor Yellow
}
Write-Host ""

# 先確認目的地共用資料夾連得到，連不到 robocopy 會卡在重試迴圈很久才報錯
if (-not (Test-Path $Destination)) {
    Write-Host "[deploy] 連不到共用資料夾: $Destination" -ForegroundColor Red
    Write-Host "[deploy] 請確認機台是否開機、Software 資料夾是否有分享出來、網路是否正常。" -ForegroundColor Red
    exit 1
}

$logArg = "/LOG+:" + $logFile

$robocopyArgs = New-Object System.Collections.Generic.List[string]
$robocopyArgs.Add($Source)
$robocopyArgs.Add($Destination)
$robocopyArgs.Add("/E")
$robocopyArgs.Add("/XD")
$robocopyArgs.Add("Config")
$robocopyArgs.Add("logs")
$robocopyArgs.Add("Results")
$robocopyArgs.Add("MachineAssembly")
$robocopyArgs.Add("MvSDKLog")
$robocopyArgs.Add("WebApiLog")
$robocopyArgs.Add("/MT:16")
$robocopyArgs.Add("/R:2")
$robocopyArgs.Add("/W:2")
$robocopyArgs.Add("/TEE")
$robocopyArgs.Add("/NP")
$robocopyArgs.Add($logArg)

if ($Preview) {
    $robocopyArgs.Add("/L")
}

& robocopy @robocopyArgs
$exitCode = $LASTEXITCODE

Write-Host ""

# robocopy 結束碼是位元旗標: 0-7 都算成功, 8 以上才是真的出錯
if ($exitCode -eq 0) {
    Write-Host "[deploy] 完成: 目的地跟來源已經一致，這次沒有檔案需要複製。" -ForegroundColor Green
}
elseif ($exitCode -le 7) {
    if ($Preview) {
        Write-Host "[deploy] 預覽完成，上面列出的就是這次會複製的檔案。確認沒問題後拿掉 -Preview 再跑一次。" -ForegroundColor Yellow
    }
    else {
        Write-Host "[deploy] 完成: 已同步變更的檔案到機台。" -ForegroundColor Green
    }
}
else {
    Write-Host "[deploy] robocopy 回傳代碼 $exitCode，可能有檔案複製失敗，詳見 log: $logFile" -ForegroundColor Red
}
