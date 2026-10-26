# CodexUsageMonitor 0.5.0-beta2

新版說明統一使用繁體中文，完整操作、升級、燒速公式、通知與隱私說明請見 [README.md](README.md)。

[0.5.0-beta2 預發行頁](https://github.com/hayabusasean/CodexUsageMonitor/releases/tag/v0.5.0-beta2) 提供 Windows x64 可攜包與 SHA-256；[新版功能與四張實機截圖（含 10/26 使用紀錄）](README.md#實機畫面)見首頁。升級前先從舊版右鍵選單選「結束」，再開新版 `CodexUsageMonitor.exe`；保留 `%LOCALAPPDATA%\CodexUsageMonitor` 以接續設定與歷史。

第一次啟動新版前，請先另存含有舊 `Salt` 的 `settings.json`，並妥善保管、不外傳。0.4.1 讀不到新版 DPAPI 的 `SaltProtected`；退版前必須先結束新版並還原升級前的設定，不能假定 `pre-bilingual.bak` 一定可用。沒有可用備份時，先不要直接啟動 0.4.1；詳見 [升級與退版說明](README.md#開始試用與升級)。

要以同一份正式 EXE 使用獨立資料，可設定 `CODEX_USAGE_MONITOR_HOME` 為一般本機資料夾的絕對路徑；未設定時沿用原本的 `%LOCALAPPDATA%\CodexUsageMonitor`。程式先將 8.3 短名與 SUBST 別名解析成實際本機路徑，再拒絕原正式資料夾、`CODEX_HOME`、這兩者的子資料夾與上層資料夾。也拒絕磁碟根目錄、網路或 device 路徑；目標與既存上層資料夾不可是 junction、符號連結等資料夾捷徑。同一實際資料夾的別名共用執行鎖。不修改系統 `LOCALAPPDATA` 或 `CODEX_HOME`，新資料目錄仍正常每 90 秒查詢額度。獨立資料目錄不會建立、修改或刪除 Windows 開機捷徑；設定頁的開機啟動選項停用，複製設定中的開機啟動值也不會套用到全域捷徑。詳見 [獨立資料目錄](README.md#使用獨立資料目錄)。

排查問題時，可只提供目前資料目錄的 `diagnostics`：`first_paint_YYYY-MM-DD.jsonl` 每個視窗實例首次繪製前後共記兩行；`runtime-session_YYYY-MM-DD.jsonl` 與 `runtime-metrics.v1.json` 可核對執行的 EXE、成功輪詢及寫入量。診斷不含 email、accountId、帳戶雜湊、token 或 Salt，但啟動紀錄會包含本機資料夾路徑；不必附上設定或歷史 CSV。

浮窗現在顯示每週剩餘、燒速、重置倒數與預計用完時間。下次查詢秒數移到 tooltip；右鍵第一項可切換顯示／隱藏，單擊通知區圖示可叫回。來源可讀率不再出現在浮窗上。

`rate_2h` 使用跳格中點計算完整可觀測視窗的增加量，包含沒再跳格的尾段；回放 2026-10-22 23:01 得到 `4.0%/h`。跳不到兩個不同格或累積不足 2 個百分點時顯示「資料不足」。每週與公告重置分別提供預測，介面以較早者倒數。

工具維持唯讀：不送 model turn、不兌換券、不讀 `auth.json`，也不存 email／token。歷史 CSV 的欄位、schema 1、只追加、去重與尾端修復保留。提醒每個條件 6 小時內最多一次；公告全球重置前先保留券。

[完整使用說明](README.md) · [建置與測試](README_BUILD.md) · [版本紀錄](CHANGELOG.md) · [MIT 授權](LICENSE)
