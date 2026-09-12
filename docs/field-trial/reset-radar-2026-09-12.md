# Reset Radar — First Live Field Trial

| Field | Value |
| --- | --- |
| Status | **IN PROGRESS** |
| Start | 2026-09-12 |
| Public release | v0.4.0-rc.3 |
| Field-trial build | rc.4 private candidate |

These screenshots were captured during a real user field trial and approved for publication. The rc.4 build is private and has not been released.

## First live evidence

| Observation | Value |
| --- | --- |
| Event level | WATCH |
| Evidence | Traceable Codex team reset hint |
| Timing | Not confirmed |
| Radar Coverage | 67% |
| Readable | 4 / 6 |
| Official | 2 / 4 |
| Team signals | 2 / 2 |
| Community | 0 / 0 |
| Weekly remaining at screenshot | 11% |
| Banked reset | 1 |
| Event read state | READ |
| Signal lifecycle | ACTIVE WATCH |

![Active WATCH after it has been read](../screenshots/field-trial/reset-radar-watch-compact-2026-09-12-zh-TW.png)

*Active WATCH after it has been read: the exclamation mark is removed, while the amber dot remains until the signal expires, is superseded or progresses.*

![Live Reset Radar detail view](../screenshots/field-trial/reset-radar-watch-details-2026-09-12-zh-TW.png)

*Live Reset Radar detail view: source coverage, source health, evidence, local quota context and traceable original/relay source actions.*

## Evidence model

An external signal is not local quota replenishment. Correlation is not causation.

This field trial currently demonstrates:

1. Radar obtained a traceable team signal.
2. Radar classified it as WATCH.
3. The monitor displayed source coverage and source health.
4. The active WATCH indicator remained after the event was read.

It does not yet establish:

- that a reset will happen
- when a reset will happen
- that this account is eligible
- that any future quota replenishment was caused by this signal

## What we are watching for next

1. **WATCH** — a team hint is detected.
2. **INCOMING** — an explicit reset, rollout or timing signal appears.
3. **Local before-state** — the weekly quota before a reset is recorded.
4. **Replenishment observed** — local quota rises significantly or reaches 100%.
5. **Reset metadata change** — `reset_at` moves or a new cycle is observed.
6. **Correlation report** — the announcement timeline is compared with local quota evidence.

The tool will not convert correlation into a proven cause.

## Radar Coverage

In this field-trial screenshot:

**67% = 4 readable sources / 6 enabled sources**

Healthy:

- Codex Changelog
- OpenAI Status
- ModelYard team signal relay
- Codex Reset team signal relay

Unavailable or degraded:

- OpenAI Release Notes — HTTP 403
- OpenAI Help Center — HTTP 403

Source failure does not trigger a Reset alert. Coverage and Reset alerts are separate concepts. Radar Coverage measures source readability; it is not a reset probability.

## Alert semantics

| Indicator | Meaning |
| --- | --- |
| Amber weekly % | Weekly quota is low |
| Red weekly % | Weekly quota is critically low |
| Amber ! | New WATCH signal |
| Amber dot | WATCH was read but is still active |
| Red ! | New INCOMING signal |
| Red dot | INCOMING was read but is still active |
| Teal / success | Replenishment or completed observation |
| Radar % | Source readability coverage, not reset probability |

## 繁體中文摘要

本頁記錄 2026-09-12 開始的第一次真實 Reset Radar 觀測。工具取得一則可追溯的團隊 Reset 暗示，因為沒有可靠時間，所以維持 WATCH／注意；事件已讀後，右上角黃色狀態點仍保留。畫面的 67% 是 6 個啟用來源中有 4 個可讀的來源涵蓋率，不是 Reset 發生機率。這些證據尚不能證明 Reset 必然發生、發生時間、帳號資格，或未來額度變化的原因。
