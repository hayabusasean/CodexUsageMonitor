using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace CodexUsageMonitor;
internal record WindowQuota(string LimitId,string LimitName,string Slot,int? Duration,decimal? Used,long? Reset,string Quality,string Plan,string? Balance,bool? HasCredits,bool? Unlimited,string Reached)
{
    public decimal? Remaining=>Used.HasValue?100-Used.Value:null;
    public string Name=>Duration switch{300=>"5 小時",10080=>"每週",null=>"未知窗口",_=>$"{Duration} 分鐘"};
    public string Compact=>Remaining is null?"來源資料異常":Remaining is >0 and <1?"<1%":Math.Floor(Remaining.Value).ToString(CultureInfo.InvariantCulture)+"%";
    public string ResetText(DateTimeOffset now){
        if(!Reset.HasValue)return "未提供重置時間";
        var delta=DateTimeOffset.FromUnixTimeSeconds(Reset.Value)-now;
        return delta<=TimeSpan.Zero?"已到公告時間，等待來源更新":$"{(int)delta.TotalDays} 天 {delta.Hours} 小時 {delta.Minutes} 分鐘";
    }
}
internal record Snapshot(DateTimeOffset Started,DateTimeOffset Observed,long Monotonic,string AccountKey,string Assurance,string Generation,string Version,List<WindowQuota> Windows,int? ResetCredits,List<long> CreditExpirations)
{
    public string PollId {get;init;}=Guid.NewGuid().ToString("N");
    public string Shape=>string.Join("|",Windows.Select(w=>w.LimitId+":"+w.Duration).Order());
}
internal static class QuotaNormalizer
{
    internal static JsonElement Get(JsonElement e,string key)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(key,out var v)?v:default;
    static string Str(JsonElement e)=>e.ValueKind==JsonValueKind.String?e.GetString()??"":"";
    static decimal? Number(JsonElement e)=>e.ValueKind==JsonValueKind.Number&&e.TryGetDecimal(out var v)?v:null;
    static long? Integer(JsonElement e)=>e.ValueKind==JsonValueKind.Number&&e.TryGetInt64(out var v)?v:null;
    static bool? Bool(JsonElement e)=>e.ValueKind is JsonValueKind.True or JsonValueKind.False?e.GetBoolean():null;
    public static (string key,string assurance) Identity(JsonElement response,string salt,string generation)
    {
        var a=Get(response,"account");if(a.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)throw new MonitorException("AUTH_REQUIRED");
        var type=Str(Get(a,"type"));if(type=="apiKey"||type=="apikey")throw new MonitorException("API_KEY_ONLY");
        if(type is not ("chatgpt" or "chatgptAuthTokens"))throw new MonitorException("ACCOUNT_UNSUPPORTED");
        var id=Str(Get(a,"accountId"));var workspace=Str(Get(a,"workspaceId"));
        var email=Str(Get(a,"email"));var stable=id.Length>0&&workspace.Length>0;
        var identifier=id.Length>0?id:email;
        if(identifier.Length==0)return(Safe.Hash(salt+generation),"CONNECTION_ONLY");
        using var h=new HMACSHA256(Convert.FromBase64String(salt));
        var raw=identifier+"|"+workspace+"|"+Str(Get(a,"planType"))+"|"+(Environment.GetEnvironmentVariable("CODEX_HOME")??"inherited-default");
        return(Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant(),stable?"STABLE_SCOPE":"SCOPE_NOT_FULLY_VERIFIED");
    }
    public static Snapshot Normalize(JsonElement response,string key,string assurance,string generation,string version,DateTimeOffset started,DateTimeOffset now,long mono)
    {
        var pools=new List<(string id,JsonElement data)>();var map=Get(response,"rateLimitsByLimitId");
        if(map.ValueKind==JsonValueKind.Object){
            foreach(var p in map.EnumerateObject())if(p.Value.ValueKind==JsonValueKind.Object)pools.Add((Safe.Label(p.Name) is {Length:>0} label && label!="來源文字已隱藏"?label:"pool_"+Safe.Hash(p.Name)[..12],p.Value));
        }else if(map.ValueKind is not(JsonValueKind.Undefined or JsonValueKind.Null))throw new MonitorException("SOURCE_DATA_INVALID");
        else{
            var legacy=Get(response,"rateLimits");if(legacy.ValueKind==JsonValueKind.Object)pools.Add((Safe.Label(Str(Get(legacy,"limitId"))) is {Length:>0} id?id:"legacy",legacy));
        }
        var windows=new List<WindowQuota>();
        foreach(var (id,p) in pools){
            foreach(var slot in new[]{"primary","secondary"}){
                var w=Get(p,slot);if(w.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)continue;
                var duration=Number(Get(w,"windowDurationMins"));int? d=duration>0&&duration<=int.MaxValue&&decimal.Truncate(duration.Value)==duration?(int)duration.Value:null;
                var used=Number(Get(w,"usedPercent"));if(used is <0 or >100)used=null;
                var reset=Integer(Get(w,"resetsAt"));bool resetBad=false;
                if(reset.HasValue)try{DateTimeOffset.FromUnixTimeSeconds(reset.Value);}catch{reset=null;resetBad=true;}
                else if(Get(w,"resetsAt").ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))resetBad=true;
                var credit=Get(p,"credits");var bal=Get(credit,"balance");string? balance=null;
                if(bal.ValueKind==JsonValueKind.String&&decimal.TryParse(bal.GetString(),NumberStyles.Number,CultureInfo.InvariantCulture,out var bn))balance=bn.ToString(CultureInfo.InvariantCulture);
                if(bal.ValueKind==JsonValueKind.Number&&bal.TryGetDecimal(out var b))balance=b.ToString(CultureInfo.InvariantCulture);
                windows.Add(new(id,Safe.Label(Str(Get(p,"limitName"))),slot,d,used,reset,used is null||d is null?"SOURCE_DATA_INVALID":resetBad?"RESET_TIME_INVALID":"VALID",Safe.Label(Str(Get(p,"planType"))),balance,Bool(Get(credit,"hasCredits")),Bool(Get(credit,"unlimited")),Safe.Label(Str(Get(p,"rateLimitReachedType")))));
            }
        }
        if(windows.Count==0)throw new MonitorException("SOURCE_DATA_MISSING");
        var rc=Get(response,"rateLimitResetCredits");var count=Integer(Get(rc,"availableCount"));int? available=count>=0&&count<=int.MaxValue?(int)count:null;
        var expirations=new List<long>();var credits=Get(rc,"credits");
        if(credits.ValueKind==JsonValueKind.Array)foreach(var c in credits.EnumerateArray().Take(100)){var ex=Integer(Get(c,"expiresAt"));if(ex.HasValue)try{DateTimeOffset.FromUnixTimeSeconds(ex.Value);expirations.Add(ex.Value);}catch{}}
        return new(started,now,mono,key,assurance,generation,version,windows,available,expirations);
    }
}
internal record UsageSample
{
    public string schema_version{get;init;}="1";
    public string sample_id{get;init;}="";public string poll_id{get;init;}="";
    public DateTimeOffset request_started_utc{get;init;} public DateTimeOffset observed_at_utc{get;init;} public DateTimeOffset observed_at_local{get;init;}
    public long monotonic_elapsed_ms{get;init;} public string account_context_key{get;init;}=""; public string context_assurance{get;init;}="";
    public string source_generation{get;init;}="";public string codex_version{get;init;}="";public string limit_id{get;init;}="";public string limit_name_safe{get;init;}="";public string source_slot{get;init;}="";
    public int? window_duration_mins{get;init;} public decimal? used_percent_raw{get;init;} public decimal? remaining_percent{get;init;}
    public long? reset_at_unix_seconds{get;init;} public DateTimeOffset? reset_at_utc{get;init;} public string plan_type{get;init;}="";
    public string? credits_balance{get;init;}public string credits_unit{get;init;}="";public bool? has_credits{get;init;}public bool? unlimited{get;init;}
    public int? reset_credits_available_count{get;init;} public string reset_credit_expirations_utc{get;init;}="";public string rate_limit_reached_type{get;init;}="";
    public string data_quality{get;init;}="";public int last_success_age_seconds{get;init;}=0;
    public string comparison_key{get;init;}="";public bool had_gap{get;init;}public int polling_interval_seconds{get;init;}
    public static List<UsageSample> From(Snapshot s,bool gap,int interval){
        return s.Windows.Select(w=>new UsageSample{
            sample_id=Safe.Hash(s.PollId+"|"+w.LimitId+"|"+w.Slot),poll_id=s.PollId,request_started_utc=s.Started,observed_at_utc=s.Observed,observed_at_local=s.Observed.ToLocalTime(),monotonic_elapsed_ms=s.Monotonic,
            account_context_key=s.AccountKey,context_assurance=s.Assurance,source_generation=s.Generation,codex_version=s.Version,limit_id=w.LimitId,limit_name_safe=w.LimitName,source_slot=w.Slot,window_duration_mins=w.Duration,
            used_percent_raw=w.Used,remaining_percent=w.Remaining,reset_at_unix_seconds=w.Reset,reset_at_utc=w.Reset.HasValue?DateTimeOffset.FromUnixTimeSeconds(w.Reset.Value):null,plan_type=w.Plan,credits_balance=w.Balance,has_credits=w.HasCredits,unlimited=w.Unlimited,
            reset_credits_available_count=s.ResetCredits,reset_credit_expirations_utc=string.Join(";",s.CreditExpirations.Select(v=>DateTimeOffset.FromUnixTimeSeconds(v).ToString("O"))),rate_limit_reached_type=w.Reached,data_quality=w.Quality,
            comparison_key=s.AccountKey+"|"+Safe.Hash(s.Shape)+"|"+w.LimitId+"|"+w.Duration+(s.Windows.Count(x=>x.LimitId==w.LimitId&&x.Duration==w.Duration)>1?"|"+w.Slot:""),had_gap=gap,polling_interval_seconds=interval
        }).ToList();
    }
}
internal record UsageEvent
{
    public string schema_version{get;init;}="1";public string event_id{get;init;}="";public string event_type{get;init;}="";
    public string previous_sample_id{get;init;}="";public string current_sample_id{get;init;}="";
    public DateTimeOffset observation_from_utc{get;init;} public DateTimeOffset observation_to_utc{get;init;} public DateTimeOffset first_observed_at_utc{get;init;}
    public decimal? previous_remaining{get;init;}public decimal? current_remaining{get;init;}public long? previous_reset_at{get;init;}public long? current_reset_at{get;init;}
    public bool had_gap{get;init;} public string identity_assurance{get;init;}="";public string timing_relation{get;init;}="UNCLASSIFIED";public string cause{get;init;}="UNKNOWN";
    public string limit_id{get;init;}="";public int? window_duration_mins{get;init;}public string account_context_key{get;init;}="";
    public string previous_credits_balance{get;init;}="";public string current_credits_balance{get;init;}="";public int? previous_reset_credits{get;init;}public int? current_reset_credits{get;init;}
}
internal static class EventDetector
{
    public static List<UsageEvent> Compare(UsageSample? prev,UsageSample cur)
    {
        var list=new List<UsageEvent>();if(prev==null||prev.sample_id==cur.sample_id)return list;
        bool same=prev.comparison_key==cur.comparison_key;
        bool gap=cur.had_gap||prev.source_generation!=cur.source_generation||cur.observed_at_utc<=prev.observed_at_utc||(cur.observed_at_utc-prev.observed_at_utc).TotalSeconds>cur.polling_interval_seconds*3;
        void Add(string type){
            string timing="UNCLASSIFIED";
            if(!gap&&cur.context_assurance=="STABLE_SCOPE"&&prev.reset_at_unix_seconds.HasValue&&cur.remaining_percent>prev.remaining_percent){
                var reset=DateTimeOffset.FromUnixTimeSeconds(prev.reset_at_unix_seconds.Value);
                timing=reset>prev.observed_at_utc&&reset<=cur.observed_at_utc?"MATCHES_PREVIOUS_ANNOUNCEMENT":cur.observed_at_utc<reset?"OBSERVED_BEFORE_PREVIOUS_ANNOUNCEMENT":"UNCLASSIFIED";
            }
            list.Add(new(){event_id=Safe.Hash(prev.sample_id+"|"+cur.sample_id+"|"+type),event_type=type,previous_sample_id=prev.sample_id,current_sample_id=cur.sample_id,observation_from_utc=prev.observed_at_utc,observation_to_utc=cur.observed_at_utc,first_observed_at_utc=cur.observed_at_utc,previous_remaining=prev.remaining_percent,current_remaining=cur.remaining_percent,previous_reset_at=prev.reset_at_unix_seconds,current_reset_at=cur.reset_at_unix_seconds,had_gap=gap,identity_assurance=cur.context_assurance,timing_relation=timing,limit_id=cur.limit_id,window_duration_mins=cur.window_duration_mins,account_context_key=cur.account_context_key,previous_credits_balance=prev.credits_balance??"",current_credits_balance=cur.credits_balance??"",previous_reset_credits=prev.reset_credits_available_count,current_reset_credits=cur.reset_credits_available_count});
        }
        if(!same){Add("SOURCE_CONTEXT_CHANGED");return list;}
        if(gap){Add("CONNECTION_GAP");Add("RESUMED");}
        if(prev.remaining_percent.HasValue&&cur.remaining_percent.HasValue){
            if(cur.remaining_percent>prev.remaining_percent)Add(cur.remaining_percent==100?(prev.remaining_percent==0?"ZERO_TO_FULL_OBSERVED":"FULL_REPLENISHMENT_OBSERVED"):"QUOTA_INCREASE_OBSERVED");
            else if(cur.remaining_percent<prev.remaining_percent)Add("QUOTA_DECREASE_OBSERVED");
        }
        if(cur.reset_at_unix_seconds!=prev.reset_at_unix_seconds)Add("RESET_TIMESTAMP_CHANGED");
        return list;
    }
}

