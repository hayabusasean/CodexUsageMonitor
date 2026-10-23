namespace CodexUsageMonitor;
internal sealed record Advice(string Code,string Text);
internal static class QuotaAdvice
{
    internal static readonly TimeZoneInfo Taiwan=TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
    internal static string At(DateTimeOffset? t)=>t.HasValue?TimeZoneInfo.ConvertTime(t.Value,Taiwan).ToString("MM-dd HH:mm"):"時間未提供";
    internal static List<Advice> Evaluate(BurnForecastResult? f,Snapshot? snapshot,Announcement? announcement,DateTimeOffset now)
    {
        var result=new List<Advice>();if(f?.Sample?.remaining_percent is not decimal remaining||snapshot==null||f.Confidence=="STALE")return result;
        var expiry=snapshot.CreditExpirations.Order().Select(DateTimeOffset.FromUnixTimeSeconds).FirstOrDefault(x=>x>now);
        if(f.Announced?.HoursToReset is >0 and <3&&remaining>20)
            result.Add(new("ANNOUNCED_RESET_SOON_BURN",$"公告說台灣 {At(f.Announced.Deadline)} 前重置，還剩 {remaining:0.#}%；要用完需每小時約 {f.Announced.NeededRateToFinish:0.#}%。"));
        if(f.Weekly?.HoursToReset is >0 and <12&&f.Weekly.ProjectedUsedAtReset is <90)
            result.Add(new("WEEKLY_RESET_UNDERUSED",$"照這速度，每週重置時還剩 {100-f.Weekly.ProjectedUsedAtReset:0.#}%，可以調整使用速度。"));
        if(f.Weekly?.ExhaustAt is DateTimeOffset exhaust&&f.Weekly.Deadline is DateTimeOffset reset&&exhaust<reset&&snapshot.ResetCredits>0)
            result.Add(new("EXHAUST_BEFORE_RESET_WITH_CREDITS",$"照這速度，台灣 {At(exhaust)} 會用完；你有 {snapshot.ResetCredits} 張券，最早一張 {At(expiry==default?null:expiry)} 到期。"));
        if(snapshot.ResetCredits>0&&expiry!=default&&(expiry-now).TotalHours<=72)
            result.Add(new("CREDIT_EXPIRING",f.Weekly?.Deadline is DateTimeOffset weekly&&weekly<expiry?"券快過期：重置後的新週期也要在到期前用完，兌換券才有價值。":"券快過期：先把這週額度用完再兌換，就不會浪費。"));
        if(f.Announced?.HoursToReset is >0 and <3&&snapshot.ResetCredits>0)
            result.Add(new("PRESERVE_CREDITS_BEFORE_GLOBAL_RESET","本機兩次全球重置後券數都沒變；公告重置前先保留券，等額度用完再評估。"));
        return result;
    }
}
internal sealed class AdviceNotificationState
{
    public Dictionary<string,DateTimeOffset> LastNotified{get;set;}=[];
    internal List<string> Observe(IEnumerable<Advice> advice,string context,DateTimeOffset now)
    {
        var result=new List<string>();foreach(var a in advice){var key=Safe.Hash(context)+"|"+a.Code;if(LastNotified.TryGetValue(key,out var at)&&now-at<TimeSpan.FromHours(6))continue;LastNotified[key]=now;result.Add(a.Text);}
        foreach(var key in LastNotified.Where(x=>now-x.Value>TimeSpan.FromDays(7)).Select(x=>x.Key).ToArray())LastNotified.Remove(key);
        return result;
    }
}
