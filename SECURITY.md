# 安全問題與回報方式

## 先知道目前的範圍

目前供新使用者參考的版本與下載入口，以 [首頁](README.md)和[發行頁](https://github.com/hayabusasean/CodexUsageMonitor/releases)為準。較新測試版不等於已經獨立完成資安認證；本專案由個人盡力維護，沒有承諾回應時限或企業支援。

工具本身尚未加上發布者數位簽章。檔案校驗值只用來核對檔案，不保證安全。不要為了執行它而關閉防毒、略過公司限制或提供登入資料。

## 一般使用問題

例如找不到 Codex、視窗顯示不完整、歷史曲線讀不到，請先看[新手教學](docs/START_HERE.md)，再用[問題回報](https://github.com/hayabusasean/CodexUsageMonitor/issues/new?template=bug_report.yml)。提供版本、操作步驟及必要截圖即可，不要貼設定檔、登入檔或完整私人歷史。

## 疑似資料外洩或其他安全漏洞

優先使用 [GitHub 私密漏洞回報](https://github.com/hayabusasean/CodexUsageMonitor/security/advisories/new)。請寫受影響版本、風險、最小重現方式，並移除個資、金鑰、登入回應與無關檔案。不要把完整利用方式或別人的資料貼在公開討論區。

若私密回報頁不可用，先不要公開細節。可以在一般問題區只留下「需要私密回報管道」，不附漏洞或敏感資料。

特別歡迎檢查：是否超出帳號／額度查詢範圍、是否意外讀取專案或登入內容、是否傳出本機資料、是否觸發模型任務／購買／兌換，以及是否誤動不屬於本工具的資料或程序。

## 供技術查核

額度請求白名單在 `src/AppServerClient.cs`；權限設定在 `src/app.manifest`。公開消息網路邊界在 `src/ResetRadar.cs`。資料、匯出與子程序邊界請從 [AI 查核索引](docs/AI_HELP.md)逐項追查。

自動建置使用唯讀儲存庫權限及固定版本的 GitHub Actions，不需要 Codex 登入資料；一般建置不會自動發布 Release。測試通過、作者審查或 AI 意見均不能當成獨立安全認證。

[費用與資料安全](PRIVACY.md) · [本輪程式碼檢查](docs/reviews/2026-10-26.md)

## English

Use private vulnerability reporting for sensitive findings. Do not post credentials, private histories or exploit details in public issues. This is an unsigned, personally maintained experimental application with no independent security certification or response-time commitment. See the current README for the recommended version; do not infer support from old release documents.
