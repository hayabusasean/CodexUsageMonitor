# Security

## Report a vulnerability privately

Please use [GitHub private vulnerability reporting](https://github.com/hayabusasean/CodexUsageMonitor/security/advisories/new) for sensitive security findings.

Include the affected version, impact and minimal reproduction. Remove credentials, personal paths, account responses and unrelated private data. Do not post exploit details or someone else's information in a public issue.

For ordinary functional bugs without sensitive security implications, use the [bug report form](https://github.com/hayabusasean/CodexUsageMonitor/issues/new?template=bug_report.yml).

## Scope and support

Reports are especially useful when they show a breach of the monitor's intended boundaries: read-only quota access, no direct credential-file access, no conversation or project-content inspection, no model turns, no purchases or resets, local history, public-source-only Radar and cleanup limited to processes the monitor owns.

The current public version is **v0.4.0-rc.3**, a pre-release. Maintenance and fixes are best effort; there is no promised response time, security certification or supported-version SLA.

The Windows executable is unsigned. Published checksums help compare files; they do not establish publisher identity or guarantee that software is safe. Follow your computer's security policy rather than disabling security protections.

The build workflow uses read-only repository permissions and pinned actions. It does not require Codex credentials, use **pull_request_target**, push changes or publish Releases.

## 繁體中文

敏感安全問題請使用 [GitHub 私密漏洞回報](https://github.com/hayabusasean/CodexUsageMonitor/security/advisories/new)，提供受影響版本、影響與最小重現步驟。請移除憑證、個人路徑、帳號原始回應及無關私人資料，不要把漏洞利用細節或他人的資料貼到公開 issue。

一般功能問題可使用[問題回報表單](https://github.com/hayabusasean/CodexUsageMonitor/issues/new?template=bug_report.yml)。

目前公開版本為 **v0.4.0-rc.3 預發行版**，由個人盡力維護，沒有承諾回應時限或安全認證。程式尚未數位簽章；雜湊僅供檔案比對，並非安全保證或發布者身分證明。請遵循電腦的安全政策。
