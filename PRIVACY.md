# Privacy

CodexUsageMonitor keeps settings, observations, notes, diagnostics and its announcement cache in **%LOCALAPPDATA%\CodexUsageMonitor**. Copying the program does not synchronise data to another computer.

## Data access and network activity

The app starts its own official Codex app-server child process and sends a restricted set of read-only account/quota requests over standard input/output. It does not directly read auth.json, cookies, credentials, project files, prompts or responses from your development work. The official app-server retains its own authentication updates and network behaviour.

Reset Radar makes ordinary HTTPS GET requests to a small allowlist of public announcement pages. These requests do not attach account credentials or usage history. Receiving sites see ordinary request metadata, such as your IP address. Radar can be disabled in Settings.

There is no monitor telemetry, cloud history, automatic upload, translation service, advertising or model-turn request. The monitor does not consume resets, purchase credits or change Codex settings.

## Local observations and exports

History includes observation times, quota values, gaps, reset metadata and available credit/reset counts. Account and source comparison boundaries are retained locally to avoid comparing incompatible observations. Retention is configurable; the default is 90 days.

Analysis Log exports the entire selected period. It contains usage-time patterns even after identifiers are removed. It is not automatically safe to publish. Notes are excluded by default and may contain private information if you opt in. The exporter removes known secret patterns, email addresses, personal paths and raw account/source identifiers; no filter replaces a review of what you choose to share.

CSV and report data use invariant numbers and stable field names. Announcement text and notes are untrusted data, never executable instructions. The fixed report prompt explicitly tells external analysis tools to treat them that way.

About's **Copy diagnostics** provides a smaller previewable summary without the detailed usage timeline. Prefer this for ordinary bug reports. Public examples use labelled synthetic fixtures. Do not post a complete personal analysis report in an issue.

The app does not send an exported report anywhere. If you manually share it with an external AI or another person, that destination's terms and privacy practices apply.

## Removing data

Exit the monitor before managing its application-data folder. Removing the EXE alone leaves settings and history in place for manual updates. Remove the product's own LocalAppData folder only if you intentionally want to discard its history. Never remove Codex authentication or project data to troubleshoot this utility.

This describes the monitor's behaviour, not a guarantee about OpenAI, Windows or a network proxy. See [Security](SECURITY.md) for reporting a privacy defect.
