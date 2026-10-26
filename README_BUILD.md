# 進階：從原始碼編譯

一般使用者不需要做這些。請先看[新手教學](docs/START_HERE.md)；以下保留給開發者與需要查核的 AI。

使用 Windows x64、PowerShell 7 與 `global.json` 指定的 .NET SDK 10.0.400。一般使用者可直接使用 GitHub 發行頁的可攜包，不必安裝 SDK 或 .NET Runtime。

在專案根目錄執行：

```powershell
./tests/build_beta.ps1
```

腳本負責還原、建置正式程式與獨立測試專案、執行指定檢查，並建立 0.5.0-beta3 可攜包。輸出位於 `artifacts/beta-0.5.0/`。此流程不取代正在執行的程式，也不向網路發布版本；實際驗收結果另見測試輸出的 JSON 與本次驗收報告。

十三組結果（包含 `first-paint-diagnostics`）由 `tests/SummarizeBetaTests050.ps1` 自動彙整為該次輸出目錄的 `test-summary.json`；缺檔、失敗或無法辨識的結果會使流程中止。`tests/AuditRelease050.ps1 -RunRoot <輸出目錄>` 可在不執行正式 EXE 的情況下，核對 ZIP、bundle、雜湊與測試入口排除。

發行審查工具使用 PowerShell 7 與同版 .NET SDK。要核對最後交付的包，可執行：

```powershell
$run = './artifacts/beta-0.5.0/<此次輸出目錄>'
./tests/AuditRelease050.ps1 -RunRoot $run -ReportPath './docs/releases/v0.5.0-beta3.md' -FinalPackage
```

`-FinalPackage` 會額外要求包版本與目前專案一致，且 bundle 內的產品 assembly 與目前 Release `obj` 完全相同。成功後，結果會以 UTF-8、同資料夾暫存檔及原子替換保存為該次輸出目錄的 `release-audit.json`，同時輸出 JSON。內容標記這是最終候選包，列出實際 EXE／ZIP 的 SHA-256，並逐檔核對 ZIP 與 `portable` 目錄；它不會啟動正式 EXE，也不會把使用驗收當成已通過。

旗標與型別清單每次從當下的原始碼產生，不使用固定的通過數字：旗標取自 `Program.cs`，測試來源依 MSBuild 的正式 `Compile` 清單排除，再以 C# 語法樹取得型別名稱，包含巢狀型別。審查結果分別列出測試來源檔數、實際型別名稱數、完整檢查名單，以及來源和工具的 SHA-256；檔案數不能當成型別數。工具直接讀取產品 assembly 的 metadata 核對型別，不載入或執行產品程式碼。

若驗收報告還沒填入這次包的雜湊，可暫加 `-AllowReportPending`；結果會是 `PASS_REPORT_PENDING`，不代表報告已核對。報告完成後應移除此參數重跑。報告不在預設位置時，以 `-ReportPath` 指定該次報告；不要拿前一次包的審查 JSON 充當本次交付證據。

正式程式位於 `src/CodexUsageMonitor.csproj`；測試位於 `tests/CodexUsageMonitor.Tests.csproj`。正式版排除測試 harness 與測試旗標。兩個專案使用相同的產品原始碼，測試專案另加入 fixture 與測試入口。不要把測試 EXE 當成試用版。

`tests/RunFinalExeAuditBeta2.ps1 -RunRoot <此次輸出目錄> -DurationSeconds 720` 用同一份正式 EXE 及私有備份副本實跑，固定 90 秒輪詢；它會讀取 Windows 使用者／系統的 CODEX_HOME 設定，避免繼承代理 shell 的暫時值。需要既有登入狀態及本機備份，僅供本機工程驗收，不會附帶私人資料給使用者。

如需分別建置：

```powershell
dotnet build src/CodexUsageMonitor.csproj -c Release
dotnet build tests/CodexUsageMonitor.Tests.csproj -c Release
dotnet publish src/CodexUsageMonitor.csproj -c Release -r win-x64 --self-contained true
```

產物為 self-contained single-file，啟用 `EnableCompressionInSingleFile`；未啟用 WinForms 不支援的 trimming。背景內嵌縮小的 JPEG，原始 PNG 保留於原始碼供後續編修。授權聲明隨包保留於 `licenses/`。

## 回放與測試資料

`tests/fixtures/usage-replay/` 是 2026-10-17～10-22 CSV 的去識別副本。`account_context_key`、`comparison_key`、`source_generation` 已換成固定假值；替換時保留原值相同／不同的關係。不得把原始私人識別值放入 fixture。

需要重新製作時，可執行：

```powershell
./tests/CreateReplayFixtures.ps1
```

此腳本只讀取既有 CSV，寫入專案內的 fixture，並以製作前後 SHA-256 確認來源未變更。預設來源為 `C:\Users\user\AppData\Local\CodexUsageMonitor\history`，可用 `-SourceHistory` 指定其他位置。請不要在其他帳戶或資料集上誤用本次驗收數字。

獨立測試入口包含 `--analytics-050-test`、`--radar-050-test`、`--integration-050-test`，並以 `--test-root` 指定隔離的測試輸出。例如：

```powershell
./tests/bin/Release/net10.0-windows/win-x64/CodexUsageMonitor.Tests.exe --analytics-050-test --test-root ./artifacts/tests/analytics
```

產品試用包不含 SDK、真實執行時資料、測試程式或 fixture。測試輸出與程式二進位的 SHA-256 以此次建置清單為準；相同版本字串不代表兩次獨立建置的位元組完全相同。

`--v050-ui-test` 保存原生視窗的 PrintWindow 靜態 PNG；`--ui-dpi96` 啟用獨立的 96 邏輯 DPI 程序，省略時使用目前 144 DPI。它不會改動實體螢幕設定。靜態擷取不算開窗錄影。

測試專案另有 `--v050-screen-record`、`--record-pilot` 入口。它只擷取中性背景內的測試視窗，需取得可驗證前景；前景或遮擋檢查失敗就中止。`tests/encode_ui_recordings.py` 僅能將成功保存的影格依實際時間編成 GIF，不會擷取桌面。本次 pilot 沒有取得可用影格，不能將已備妥的程式視為錄影驗收通過；詳細證據與限制見 [beta2 發行說明](docs/releases/v0.5.0-beta3.md)。

四張裝飾背景為本專案圖像資產；控制項、曲線與驗收畫面由程式繪製。未散布字型。完整操作與資料安全說明見 [README.md](README.md)。
