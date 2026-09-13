# Changelog

## [0.4.1] — 2026-09-13

- Corrected an outdated CI test that still expected 29 report sections. The accepted Gold report already contains 34; tests now require the exact section names, framing and checksum keys.
- No UI, quota semantics, report output or monitoring behavior changes.
- v0.4.0 remains available as the first Gold release. This maintenance release is the recommended download.

## [0.4.0] — 2026-09-13

### Added
- Reset Radar source-readability display and explicit official/team-relay roles.
- Independent active/unread WATCH and INCOMING indicators with source links.
- Gold local replenishment summaries and event-focused history with observation intervals.
- Retirement of relevant old-cycle attention after a matching local replenishment.
- Time navigation, a resizable history/chart split, and source/event evidence in analysis exports.

### Improved
- Space-themed surfaces with restrained gold event accents.
- Weekly overlay countdown and clear separation of quota colours from radar alerts.
- More visible history rows; the gold summary stays outside the plot.

### Fixed
- Repeated idle redraw and expensive History grid repainting.
- Details refresh/sizing behaviour and handling of missing or invalid metadata.
- Confusing handoffs between different builds using the same data directory.

### Notes
- The author accepted this version for normal use. No claim of universal OS/DPI certification.
- The September 12 story records one real WATCH and a local 7%→100% replenishment, not a correctly predicted reset time or a completed 72-hour soak.
- New features are paused; maintenance is limited to issues that matter in use.

## v0.4.0-rc.3 — First public pre-release

CodexUsageMonitor's first public release is a portable Windows x64 app under the MIT License.

- English and Traditional Chinese in one EXE, with live language switching and migration of existing settings.
- Standard and Compact floating views showing weekly quota, update age and three days of estimated usage.
- Local history with Latest segment / Full range charts, visible monitoring gaps and CSV export.
- Reset Radar Lite with local-time notices and separate public-announcement, local-observation and correlation evidence.
- More precise handling of article dates, duplicate notices and read state. Automatic resets, credit grants and mechanism explanations remain distinct; planned, completed and negated statements are handled conservatively.
- Detailed bilingual analysis Logs with an embedded prompt, structured evidence and report-window display-scale metadata.
- Portable packaging, public documentation and source build instructions.

The final EXE was checked on Windows 10 Home 22H2 x64 at 150% display scale, including native bilingual views and real quota integration. Windows 11 for this release remains untested. The executable is unsigned; Radar source access and account-causation limits remain documented in the README.

### 繁體中文

這是 CodexUsageMonitor 首次公開的預發行版，以 MIT 授權提供 Windows x64 可攜程式。

- 單一 EXE 提供英文／繁體中文即時切換，並遷移既有設定。
- 標準與精簡浮窗呈現每週剩餘額度、相對更新秒數及三日估計用量。
- 本機歷史提供最近區段／完整範圍圖表、監測空窗提示及 CSV 匯出。
- Reset Radar Lite 以本機時區顯示訊息，區分公開公告、本機觀測與關聯證據。
- 修正文章日期、重複公告與已讀狀態；區分自動重置、額度贈送及機制說明，保守處理預告、已執行與否定敘述。
- 雙語詳細分析 Log 內嵌 Prompt、結構化證據及報告視窗顯示縮放資訊。
- 提供可攜包、公開文件與原始碼建置說明。

最終 EXE 已於 Windows 10 Home 22H2 x64、150% 顯示縮放環境檢查原生雙語畫面及真實額度串接。本版尚未實測 Windows 11，也尚未數位簽章；Radar 來源限制及帳號因果判定限制詳見 README。
