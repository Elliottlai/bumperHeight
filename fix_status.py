import re, sys

file_path = r"D:\0521_Slot_Inspection\Slot_Inspection\ViewModels\MainViewModel.cs"

# 偵測編碼
content = None
used_enc = None
for enc in ("utf-8-sig", "utf-8", "cp950", "big5"):
    try:
        with open(file_path, "r", encoding=enc) as f:
            content = f.read()
        if "StatusMessage" in content:
            used_enc = enc
            print(f"[OK] 使用編碼: {enc}")
            break
    except Exception as e:
        print(f"[SKIP] {enc}: {e}")

if content is None:
    print("[ERROR] 無法讀取檔案")
    sys.exit(1)

# ---- 替換對照表（原始中文 → 使用者友善中文）----
replacements = [
    # 初始預設值
    ("正在啟動...",                "系統啟動中，請稍候..."),

    # OnInitDevices
    ("初始化設備(Config + Servo + PLC)...",
                                   "正在初始化設備（設定檔 / 伺服軸 / PLC），請稍候..."),
    ("初始化成功 ✔ 請進行【原點復歸】",
                                   "✔ 設備初始化成功！請按【原點復歸】繼續"),
    ("初始化已取消",               "⏹ 設備初始化已取消"),

    # OnHome
    ("原點復歸中...",              "正在執行原點復歸，請稍候..."),
    ("原點復歸完成 ✔ 可放入載具後按 START",
                                   "✔ 原點復歸完成！請放入載具後按 START 開始"),
    ("原點復歸已取消",             "⏹ 原點復歸已取消"),

    # OnInitialize (全機初始化，另一段)
    ("正在初始化...",              "正在初始化設備，請稍候..."),
    ("初始化完成，請按 START 開始",
                                   "✔ 初始化完成！請按 START 開始作業"),
    ("初始化失敗",                 "✘ 初始化失敗，請檢查設備後重試"),

    # OnStart
    ("檢查載具位置與狀態...",      "正在確認載具位置，請稍候..."),
    ("找不到載具，請確認載具位置後重啟 START",
                                   "⚠ 未偵測到載具，請確認載具已放置正確後再按 START"),
    ("讀碼失敗(NoRead)，正在回原點...",
                                   "條碼讀取失敗，正在將軸退回原點..."),
    ("讀碼失敗，軸已回原點，請按 START 重新開始",
                                   "⚠ 條碼讀取失敗，軸已回原點，請重新放置後按 START"),
    ("已取消",                     "⏹ 作業已由使用者取消"),

    # OnRunInspection (試跑)
    ("移動至讀碼位置...",          "正在移動至條碼讀取位置..."),
    ("Y 軸移動超時，流程中止",     "⚠ Y 軸移動超時，流程中止，請確認設備狀態"),
    ("X 軸移動超時，流程中止",     "⚠ X 軸移動超時，流程中止，請確認設備狀態"),
    ("觸發讀取條碼中...",          "正在讀取條碼，請稍候..."),
    ("條碼讀取失敗 (NoRead)，流程中止",
                                   "⚠ 條碼讀取失敗（相機未辨識到條碼），流程中止"),
    ("試跑中...▶ Slot 量測進行中", "試跑模式進行中，正在量測各 Slot..."),
    ("試跑已取消",                 "⏹ 試跑已由使用者取消"),

    # OnStop
    ("已取消",                     "⏹ 作業已由使用者取消"),

    # OnReadBarcode
    ("讀碼失敗 (NoRead)",          "⚠ 條碼讀取失敗（相機未辨識到條碼）"),
    ("讀碼流程已取消",             "⏹ 讀碼作業已取消"),

    # OnMoveToPickPosition
    ("移動至取料位置已取消",       "⏹ 移動至取料位置已取消"),

    # Settings
    ("位置設定已更新（設定已儲存）", "✔ 位置設定已儲存"),
    ("裁切設定已更新（設定已儲存）", "✔ 裁切範圍設定已儲存"),
]

for old, new in replacements:
    if old in content:
        content = content.replace(old, new)
        print(f"  [replaced] {old[:30]} → {new[:30]}")

# f-string 樣式替換（用 regex）
fstring_replacements = [
    # 條碼格式錯誤
    (r'(StatusMessage\s*=\s*\$")條碼格式錯誤：(\{validation\.Message\}")',
     r'\g<1>⚠ 條碼格式不符：\g<2>'),
    # 條碼確認
    (r'(StatusMessage\s*=\s*\$")條碼確認：(\{code\}")',
     r'\g<1>✔ 條碼確認：\g<2>'),
    # 開始檢測
    (r'(StatusMessage\s*=\s*\$")開始檢測：(\{_confirmedBarcode\}")',
     r'\g<1>▶ 開始量測 Slot，條碼：\g<2>'),
    # 量測結果
    (r'(StatusMessage\s*=\s*\$")(\{_confirmedBarcode\})([^"]*?)(\{.+?PASS.+?FAIL.+?\}")',
     r'\g<1>量測完成  \g<2>  →  \g<4>'),
    # 試跑結果
    (r'(StatusMessage\s*=\s*\$")試跑完成([^"]*?)(\{.+?PASS.+?FAIL.+?\}")',
     r'\g<1>試跑完成  →  \g<3>'),
    # Y 軸移動至讀碼（含 F1 格式）
    (r'(StatusMessage\s*=\s*\$")Y 軸移動至讀碼位置 (\{[^}]+:F1\}) mm\.\.\."',
     r'\g<1>Y 軸移動至條碼讀取位置 \g<2> mm，請稍候..."'),
    # Y 軸移動至讀碼（不含 F1）
    (r'(StatusMessage\s*=\s*\$")Y 軸移動至 (\{[^}]+\}) mm\.\.\."',
     r'\g<1>Y 軸移動至條碼讀取位置 \g<2> mm，請稍候..."'),
    # Y 軸已到位（讀碼）
    (r'(StatusMessage\s*=\s*\$")Y 軸已到位 \((\{[^}]+\}) mm\)"',
     r'\g<1>✔ Y 軸已到達指定位置（\g<2> mm）"'),
    # X 軸移動
    (r'(StatusMessage\s*=\s*\$")偵測到 (\{[^}]+\})，X 軸移動至 (\{[^}]+:F1\}) mm\.\.\."',
     r'\g<1>偵測到 \g<2> 訊號，X 軸移動至 \g<3> mm，請稍候..."'),
    (r'(StatusMessage\s*=\s*\$")偵測到 (\{[^}]+\})=ON ，X 軸移動至 (\{[^}]+\}) mm\.\.\."',
     r'\g<1>偵測到 \g<2> 訊號，X 軸移動至 \g<3> mm，請稍候..."'),
    # X 軸已到位
    (r'(StatusMessage\s*=\s*\$")X 軸已到位 \((\{[^}]+\}) mm\)"',
     r'\g<1>✔ X 軸已到達指定位置（\g<2> mm）"'),
    (r'(StatusMessage\s*=\s*\$")X 軸已到位 \((\{[^}]+\}) mm\)，準備讀碼\.\.\."',
     r'\g<1>✔ X 軸已到位（\g<2> mm），正在觸發讀碼..."'),
    # 條碼讀取成功（含「開始檢測」）
    (r'(StatusMessage\s*=\s*\$")條碼讀取成功：(\{[^}]+\})，開始檢測\.\.\."',
     r'\g<1>✔ 條碼讀取成功：\g<2>，準備開始量測..."'),
    # 條碼讀取成功（單純）
    (r'(StatusMessage\s*=\s*\$")條碼讀取成功：(\{[^}]+\}")',
     r'\g<1>✔ 條碼讀取成功：\g<2>'),
    # Y 軸移動至取料位置
    (r'(StatusMessage\s*=\s*\$")Y 軸移動至取料位置 (\{[^}]+\}) mm\.\.\."',
     r'\g<1>Y 軸移動至取料位置 \g<2> mm，請稍候..."'),
    # Y 軸已到達取料位置
    (r'(StatusMessage\s*=\s*\$")Y 軸已到達取料位置 \((\{[^}]+\}) mm\)[^"]*"',
     r'\g<1>✔ Y 軸已到達取料位置（\g<2> mm）"'),
    # 光源設定儲存
    (r'(StatusMessage\s*=\s*\$")光源設定已儲存[^"]*"',
     r'\g<1>✔ 光源設定已儲存（左={vm.LightIntensityLeft}，右={vm.LightIntensityRight}）"'),
    # 初始化失敗（f-string）
    (r'(StatusMessage\s*=\s*\$")初始化失敗：(\{[^}]+\}) - (\{[^}]+\}")',
     r'\g<1>✘ 設備初始化失敗：\g<2> - \g<3>'),
    # 原點復歸失敗（f-string）
    (r'(StatusMessage\s*=\s*\$")原點復歸失敗：(\{[^}]+\}) - (\{[^}]+\}")',
     r'\g<1>✘ 原點復歸失敗：\g<2> - \g<3>'),
    # 各類 exception
    (r'(StatusMessage\s*=\s*\$")初始化例外：(\{[^}]+\}")',
     r'\g<1>✘ 設備初始化發生錯誤：\g<2>'),
    (r'(StatusMessage\s*=\s*\$")原點復歸例外：(\{[^}]+\}")',
     r'\g<1>✘ 原點復歸發生錯誤：\g<2>'),
    (r'(StatusMessage\s*=\s*\$")檢測例外：(\{[^}]+\}")',
     r'\g<1>✘ 量測發生錯誤：\g<2>'),
    (r'(StatusMessage\s*=\s*\$")試跑例外：(\{[^}]+\}")',
     r'\g<1>✘ 試跑發生錯誤：\g<2>'),
    (r'(StatusMessage\s*=\s*\$")讀碼流程例外：(\{[^}]+\}")',
     r'\g<1>✘ 讀碼發生錯誤：\g<2>'),
    (r'(StatusMessage\s*=\s*\$")移動至取料位置發生例外：(\{[^}]+\}")',
     r'\g<1>✘ 移動至取料位置發生錯誤：\g<2>'),
    # 偵測到載具位置
    (r'(StatusMessage\s*=\s*\$")偵測到載具位置：(\{[^}]+\}")',
     r'\g<1>✔ 偵測到載具位置：\g<2>'),
]

for pattern, replacement in fstring_replacements:
    new_content, count = re.subn(pattern, replacement, content)
    if count:
        print(f"  [regex replaced x{count}] {pattern[:40]}")
        content = new_content

with open(file_path, "w", encoding=used_enc) as f:
    f.write(content)

print("\n[DONE] 所有 StatusMessage 已更新完成！")
