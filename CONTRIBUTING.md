# Contributing

English and Traditional Chinese feedback are welcome. CodexUsageMonitor is a personal utility maintained on a best-effort basis.

## Report a bug or suggest an improvement

Use the [bug report form](https://github.com/hayabusasean/CodexUsageMonitor/issues/new?template=bug_report.yml) for reproducible problems, or [open an issue](https://github.com/hayabusasean/CodexUsageMonitor/issues) to discuss an improvement. Include the app version, Windows version, display scale, selected language, expected behavior and a short reproduction when possible.

Review screenshots and diagnostic summaries before sharing. Do not attach credentials, private account responses, company code or a complete analysis Log by default. Report sensitive vulnerabilities through [private security reporting](SECURITY.md).

## Keep the product boundaries

Changes should preserve read-only quota access, local history, English / Traditional Chinese parity and existing settings. Do not add credential-file inspection, conversation or project-content access, model turns, purchases, reset-credit use, telemetry or cross-device synchronization.

Keep source validation and process cleanup limited to processes the monitor owns. Discuss substantial behavior changes in an issue before spending time on a large pull request.

## Build and test

Contributors need Windows x64, PowerShell and a .NET 10 SDK. Users of the portable release do not need these development tools.

~~~powershell
.\tests\build_public.ps1
~~~

The script prefers an existing project-local **.tools/dotnet10/dotnet.exe**, then a .NET 10 SDK on PATH. It checks prerequisites and bilingual resource parity, restores dependencies, publishes a self-contained EXE, runs isolated fixtures and writes a candidate ZIP and checksums under a unique **artifacts/public-build/** directory. It does not install a global SDK, change execution policy or overwrite the user-facing **dist** app.

Use **-ValidateOnly** to check prerequisites and resources without building. The SDK baseline is **10.0.400**, recorded in **global.json**.

The public fixture entry point is **--public-test --test-root <isolated-folder>**. Focused groups are **--public-history-test**, **--public-radar-test** and **--public-report-test**. Use isolated data roots and clearly labelled fixtures; never put Codex credentials in CI.

Fixture results do not replace native UI review or real account integration. UI and resource changes need checks from the published single EXE in both languages, including a path containing Chinese characters and spaces. Keep satellite resource DLLs absent when checking the portable app.

The build workflow uses read-only repository permissions and pinned actions. It builds candidate artifacts; it does not publish Releases. A local build result is not evidence that a cloud workflow ran.

## License

Contributions are made under the repository's [MIT License](LICENSE). Submit only code and assets you have the right to contribute, and identify any third-party licensing requirements.

## 繁體中文

歡迎以英文或繁體中文回報問題、討論改善或提交修改。

請先提供版本、Windows 版本、顯示縮放、介面語言、預期行為及可重現步驟。截圖與診斷摘要分享前請先檢查；不要預設附上完整私人分析 Log、憑證、帳號原始回應或公司程式碼。敏感漏洞請依 [SECURITY.md](SECURITY.md) 私下回報。

修改請保留唯讀額度存取、本機歷史、雙語品質與既有設定。不要加入讀取憑證檔／對話／專案內容、模型回合、購買或使用重置券、遙測或跨電腦同步。較大的行為變更請先開 issue 討論。

建置需要 Windows x64、PowerShell 與 .NET 10 SDK，指令如上。測試請使用隔離資料目錄與明確標示的測試資料；單元或情境測試不能取代最終 EXE 的原生介面與真實串接驗證。貢獻採本專案 MIT 授權，請確認你有權提供相關程式與素材。
