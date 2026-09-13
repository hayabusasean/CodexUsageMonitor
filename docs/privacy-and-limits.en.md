# Privacy, sources and limits

[Home](../README.md) · [繁體中文](privacy-and-limits.zh-TW.md)

## Local quota data

The application reads account/usage interfaces through the locally installed official Codex app-server. It does not need an API key supplied to this tool, browser cookies, or direct access to auth.json. The official Codex process still uses its own account authentication and network connection.

The monitor does not start model turns, redeem reset credits or purchase quota. It does not read project source or conversations for monitoring. Its own history/settings and exports are local. A portable EXE does not mean its history is stored beside the EXE: data is under `%LOCALAPPDATA%\CodexUsageMonitor`.

There is no app telemetry. Public announcement hosts still receive ordinary HTTPS requests and network metadata. Following a source link opens your normal browser; that browser uses its own privacy and login settings.

## Configured announcement sources

The following list is the configured source set, not a promise of present availability. The release process checks it against the actual release source.

| Source | Role | Configured endpoint |
| --- | --- | --- |
| Codex Changelog | Official OpenAI | https://developers.openai.com/codex/changelog/rss.xml |
| OpenAI Release Notes | Official OpenAI | https://openai.com/products/release-notes/ |
| OpenAI Status | Official status; supporting context only | https://status.openai.com/feed.rss |
| OpenAI Help Center | Official OpenAI | https://help.openai.com/en/articles/20001498-how-banked-codex-resets-work |
| ModelYard team signal relay | Third-party relay of team posts | https://tibo.modelyard.dev/feed.xml |
| Codex Reset team signal relay | Third-party relay of team posts | https://codex-reset.com/tibo |

There is no automatic crawling of private Facebook groups or authenticated X sessions. Team posts may arrive through relays; their original links are distinct from relay links. Repeated copies of one original post are not independent confirmations.

The photographed state was 4/6 readable: Changelog, Status and both team relays; Release Notes and Help returned HTTP 403. This is a historical snapshot, not a live service guarantee. A 403 does not mean you should disable security or provide cookies to bypass it.

## Alert meanings

WATCH is an attributable hint worth checking. INCOMING requires more explicit information, not merely a high forecast percentage on another site. Neither promises an outcome for a particular account. Status incidents are supporting context, not automatic reset announcements; receiving a reset credit is not the same as an automatic replenishment.

The source-readability percentage measures configured endpoints that meet the app's freshness/parse rules. It does not measure the whole web, message completeness, independent evidence, or reset probability. Successful retrieval can still miss a newer post.

A gold local event records a replenishment and, when available, a source-reported cycle change. The observation time is not necessarily the exact server time. Cause remains unknown without appropriate evidence. A retired local reminder does not rewrite the original announcement as globally confirmed.

## Logs, compatibility and support

CSV and detailed analysis logs may expose usage patterns, timing and account-related context. Inspect them before sharing with an AI service or an issue. Do not upload credentials, raw auth files, recovery codes, company material or unreviewed private logs. For a bug report, use the app version, Windows version, a short description and an optional reviewed screenshot.

Windows x64 is the intended platform. Earlier rc.3 was used on Windows 11 for a full workday. Later Gold changes have their own narrower engineering checks and author acceptance; they were not retested on every Windows 11, DPI, multi-monitor or full-screen arrangement. This one reset story is not a completed 72-hour test. The executable is unsigned; checksum verification confirms file identity, not that a file is risk-free.

This is a personal community project with no service-level commitment. Preserve important exported records separately and report reproducible issues without sensitive information.
