namespace CodexUsageMonitor;
internal static class StatusWriter
{
    internal static object Create(Snapshot? snapshot,BurnForecastResult? f,Announcement? announcement,bool ok,string? error,DateTimeOffset now,Advice? advice)
    {
        var sample=f?.Sample;
        return new{
            schema="codex-usage-status.v1",generated_at=now.ToUniversalTime(),ok,error_code=ok?null:string.IsNullOrWhiteSpace(error)?"NOT_VERIFIED":error,
            stale_seconds=snapshot==null?(int?)null:(int)Math.Max(0,(now-snapshot.Observed).TotalSeconds),plan=sample?.plan_type,
            weekly=sample==null?null:new{used_percent=sample.used_percent_raw,remaining_percent=sample.remaining_percent,window_minutes=sample.window_duration_mins,window_state=f!.WindowState,window_started_at=f.WindowStartedAt,reset_at=sample.reset_at_utc,hours_to_reset=f.Weekly?.HoursToReset},
            burn=new{rate_30m_pct_per_hour=f?.Rate30m,rate_2h_pct_per_hour=f?.Rate2h,rate_session_pct_per_hour=f?.RateSession,last_step_at=f?.LastStepAt,basis="STEP_TIMES",confidence=f?.Confidence??"LOW_DATA"},
            projection=new{exhaust_at=f?.Weekly?.ExhaustAt,projected_used_at_reset=f?.Weekly?.ProjectedUsedAtReset,needed_rate_to_finish_by_reset=f?.Weekly?.NeededRateToFinish},
            announced_reset=announcement==null?null:new{level=announcement.SignalLevel,deadline_at=announcement.ResetUtc,time_quality=announcement.TimeQuality,source=announcement.Source,url=string.IsNullOrEmpty(announcement.OriginalSourceUrl)?announcement.Url:announcement.OriginalSourceUrl,needed_rate_to_finish=f?.Announced?.NeededRateToFinish,projected_used_at_reset=f?.Announced?.ProjectedUsedAtReset,exhaust_at=f?.Announced?.ExhaustAt},
            reset_credits=new{available=snapshot?.ResetCredits,expires_at=snapshot?.CreditExpirations.Order().Select(DateTimeOffset.FromUnixTimeSeconds),next_expiry_hours=snapshot?.CreditExpirations.Order().Select(x=>(double?)(DateTimeOffset.FromUnixTimeSeconds(x)-now).TotalHours).FirstOrDefault(x=>x>=0)},
            advice=new{code=advice?.Code??"OBSERVE",text_zh_tw=advice?.Text??"持續記錄額度變化；預測只依已觀察到的使用速度。"}
        };
    }
}
