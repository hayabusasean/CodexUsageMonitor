using System.Text.Json;
namespace CodexUsageMonitor;

internal static class Analytics050Tests {
 internal static void Run(string root){
  Directory.CreateDirectory(root);int failures=0;var checks=new List<object>();
  void Check(string name,Func<bool> body){try{bool pass=body();checks.Add(new{name,pass});if(!pass)failures++;}catch(Exception e){failures++;checks.Add(new{name,pass=false,error=e.ToString()});}}
  string fixture=Path.Combine(AppContext.BaseDirectory,"fixtures","usage-replay");
  if(!Directory.Exists(fixture))fixture=Path.GetFullPath(Path.Combine("tests","fixtures","usage-replay"));
  var rows=Directory.GetFiles(fixture,"usage_*.csv").OrderBy(x=>x).SelectMany(Csv.Records<UsageSample>).OrderBy(x=>x.observed_at_utc).ToArray();
  var events=new List<UsageEvent>();foreach(var stream in rows.GroupBy(x=>x.comparison_key)){UsageSample? previous=null;foreach(var current in stream){events.AddRange(EventDetector.Compare(previous,current));previous=current;}}
  var local=LocalQuotaCycle.Derive(rows,DateTimeOffset.Parse("2026-10-22T15:10:00Z"));
  var taiwan=TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
  DateTimeOffset Taiwan(UsageSample row)=>TimeZoneInfo.ConvertTime(row.observed_at_utc,taiwan);
  var replaySample=rows.Single(x=>x.limit_id=="codex"&&x.window_duration_mins==10080&&Taiwan(x).ToString("yyyy-MM-dd HH:mm")=="2026-10-22 23:01");
  var forecast=BurnForecast.Calculate(rows,replaySample.observed_at_utc);
  decimal restored=0;foreach(var stream in rows.GroupBy(x=>x.comparison_key)){var ss=stream.OrderBy(x=>x.observed_at_utc).ToArray();for(int i=1;i<ss.Length;i++){var priorRow=ss[i-1];var currentRow=ss[i];if(priorRow.reset_at_unix_seconds!=currentRow.reset_at_unix_seconds&&DailyUsageCalculator.Reject(priorRow,currentRow).Length==0)restored+=Math.Max(0,priorRow.remaining_percent.GetValueOrDefault()-currentRow.remaining_percent.GetValueOrDefault());}}
  Check("回放資料已去識別",()=>rows.All(x=>x.account_context_key.StartsWith("fixture-account-")&&x.comparison_key.StartsWith("fixture-comparison-")&&x.source_generation.StartsWith("fixture-generation-")));
  Check("10月18日04時41分為提前重置",()=>events.Any(x=>x.event_type=="EARLY_RESET"&&TimeZoneInfo.ConvertTime(x.first_observed_at_utc,taiwan).ToString("yyyy-MM-dd HH:mm")=="2026-10-18 04:41")&&local.Any(x=>x.kind=="EARLY_RESET"));
  Check("10月20日為缺口期間重置",()=>events.Any(x=>x.event_type=="RESET_DURING_GAP"&&TimeZoneInfo.ConvertTime(x.first_observed_at_utc,taiwan).ToString("yyyy-MM-dd")=="2026-10-20")&&local.Any(x=>x.kind=="RESET_DURING_GAP"));
  var idle=rows.Where(x=>Taiwan(x)>=DateTimeOffset.Parse("2026-10-18T04:41:00+08:00")&&Taiwan(x)<=DateTimeOffset.Parse("2026-10-18T08:31:00+08:00")&&x.limit_id=="codex"&&x.window_duration_mins==10080).ToArray();
  Check("10月18日04時41分到08時31分尚未開始週期",()=>idle.Length>100&&idle.All(x=>QuotaWindow.State(x)=="WINDOW_NOT_STARTED"));
  Check("重置時間變動事件不超過3筆",()=>events.Count(x=>x.event_type=="RESET_TIMESTAMP_CHANGED")<=3);
  Check("23時01分兩小時燒速符合驗收範圍",()=>forecast.Rate2h is >=3.8 and <=4.2);
  Check("23時01分每週重置距離112點7小時",()=>forecast.Weekly?.HoursToReset is >=112.6 and <=112.8);
  Check("每日用量找回4個百分點",()=>restored==4);
  var now=DateTimeOffset.Parse("2026-10-22T15:00:00Z");var p=Make("p",now.AddMinutes(-45),10,now.AddDays(3));
  var c=Make("c",now.AddMinutes(-30),11,now.AddDays(3));var n=Make("n",now.AddMinutes(-15),12,now.AddDays(3));var tail=Make("tail",now,12,now.AddDays(3));
  Check("重置時間正負120秒內可比",()=>DailyUsageCalculator.Reject(p,c with{reset_at_utc=p.reset_at_utc!.Value.AddSeconds(120),reset_at_unix_seconds=p.reset_at_unix_seconds+120}).Length==0);
  Check("重置時間超過120秒分段",()=>DailyUsageCalculator.Reject(p,c with{reset_at_utc=p.reset_at_utc!.Value.AddSeconds(121),reset_at_unix_seconds=p.reset_at_unix_seconds+121}).Contains("RESET_OR_CONTEXT_BOUNDARY"));
  Check("不足兩個不同跳格時燒速為null",()=>BurnForecast.Calculate([p,c],c.observed_at_utc).Rate==null&&BurnForecast.Calculate([p,c with{used_percent_raw=12,remaining_percent=88}],c.observed_at_utc).Rate==null);
  Check("缺口以前的跳格不沿用",()=>BurnForecast.Calculate([p,c,n,tail with{had_gap=true}],now).Rate==null);
  Check("週期以前的跳格不沿用",()=>BurnForecast.Calculate([p,c,n,tail with{reset_at_utc=now.AddDays(4)}],now).Rate==null);
  Check("資料過期標記STALE",()=>BurnForecast.Calculate([p,c,n,tail],now.AddMinutes(16)).Confidence=="STALE");
  Check("週期未開始不計每週倒數",()=>BurnForecast.Calculate([Make("idle",now,0,now.AddDays(7))],now).Weekly==null);
  Check("週期起點由重置減視窗推回",()=>QuotaWindow.StartedAt(p)==p.reset_at_utc!.Value.AddDays(-7));
  Check("有accountId沒有workspaceId為ACCOUNT_STABLE",()=>{
   using var json=JsonDocument.Parse("{\"account\":{\"type\":\"chatgpt\",\"accountId\":\"fake-id\",\"planType\":\"pro\"}}");return QuotaNormalizer.Identity(json.RootElement,Convert.ToBase64String(new byte[32]),"fake-generation").assurance=="ACCOUNT_STABLE";
  });
  Check("僅email標為EMAIL_SCOPED而非ACCOUNT_STABLE",()=>{
   using var json=JsonDocument.Parse("{\"account\":{\"type\":\"chatgpt\",\"email\":\"fake@example.invalid\"}}");return QuotaNormalizer.Identity(json.RootElement,Convert.ToBase64String(new byte[32]),"fake-generation").assurance=="EMAIL_SCOPED";
  });
  Check("ACCOUNT_STABLE重連後可比較",()=>DailyUsageCalculator.Reject(p with{context_assurance="ACCOUNT_STABLE"},c with{context_assurance="ACCOUNT_STABLE",source_generation="reconnected"}).Length==0);
  var announcement=new Announcement("fixture-event","fixture-revision","fixture-source","https://example.invalid/reset",2,"TEAM","測試重置","測試來源",now,null,now.AddHours(2),"America/Los_Angeles","DEADLINE","ALL","GLOBAL","INCOMING","fixture-hash",now,now,SignalLevel:"INCOMING");
  var announcedForecast=BurnForecast.Calculate(rows,replaySample.observed_at_utc,announcement);
  Check("公告及每週重置各計一份預測",()=>announcedForecast.Weekly!=null&&announcedForecast.Announced!=null&&announcedForecast.Announced.Deadline==announcement.ResetUtc&&announcedForecast.Weekly.Deadline!=announcedForecast.Announced.Deadline);
  Check("預測用量上限100",()=>announcedForecast.Weekly?.ProjectedUsedAtReset==100);
  (List<UsageEvent> events,LocalQuotaCycleEvent cycle) ResetPair(int oldResetSecondsAfterObservation,bool gap=false){
   var oldReset=now.AddSeconds(oldResetSecondsAfterObservation);var before=Make("reset-before",now.AddSeconds(-90),80,oldReset);
   var after=Make("reset-after",now,0,now.AddDays(7)) with{had_gap=gap};
   return(EventDetector.Compare(before,after),LocalQuotaCycle.Derive([before,after],now).Single());
  }
  Check("正常到點補滿保留原事件且不標提前",()=>{
   var result=ResetPair(0);return result.events.Any(e=>e.event_type=="FULL_REPLENISHMENT_OBSERVED")&&!result.events.Any(e=>e.event_type=="EARLY_RESET")&&result.events.All(e=>e.timing_relation=="MATCHES_PREVIOUS_ANNOUNCEMENT")&&result.cycle.kind=="FULL_REPLENISHMENT_WITH_NEW_CYCLE"&&result.cycle.schedule_relation=="AT_SCHEDULE_WINDOW";
  });
  Check("正負120秒邊界屬原定時間附近不標提前",()=>new[]{-120,-119,0,119,120}.All(seconds=>{
   var result=ResetPair(seconds);return !result.events.Any(e=>e.event_type=="EARLY_RESET")&&result.events.All(e=>e.timing_relation=="MATCHES_PREVIOUS_ANNOUNCEMENT")&&result.cycle.kind=="FULL_REPLENISHMENT_WITH_NEW_CYCLE"&&result.cycle.schedule_relation=="AT_SCHEDULE_WINDOW";
  }));
  Check("原定時間前121秒才確定為提前",()=>{
   var result=ResetPair(121);return result.events.Any(e=>e.event_type=="EARLY_RESET")&&result.events.All(e=>e.timing_relation=="OBSERVED_BEFORE_PREVIOUS_ANNOUNCEMENT")&&result.cycle.kind=="EARLY_RESET"&&result.cycle.schedule_relation=="BEFORE_SCHEDULE_WINDOW";
  });
  Check("真正提早數小時的補滿仍標提前",()=>{
   var result=ResetPair(3*3600);return result.events.Any(e=>e.event_type=="EARLY_RESET")&&result.cycle.kind=="EARLY_RESET";
  });
  Check("舊重置點早於觀察起點120秒仍屬原定時間附近",()=>{
   var atBoundary=ResetPair(-210);var outside=ResetPair(-211);return atBoundary.events.All(e=>e.timing_relation=="MATCHES_PREVIOUS_ANNOUNCEMENT")&&atBoundary.cycle.schedule_relation=="AT_SCHEDULE_WINDOW"&&outside.events.All(e=>e.timing_relation=="UNCLASSIFIED")&&outside.cycle.schedule_relation=="UNKNOWN";
  });
  Check("缺口重置維持推定且不標確定提前",()=>{
   var result=ResetPair(0,true);return result.events.Any(e=>e.event_type=="RESET_DURING_GAP")&&!result.events.Any(e=>e.event_type=="EARLY_RESET")&&result.events.All(e=>e.timing_relation=="UNCLASSIFIED")&&result.cycle.kind=="RESET_DURING_GAP"&&result.cycle.schedule_relation=="UNKNOWN";
  });
  EmailScopedTests.Run(Check,root);
  AtomicJson.Save(Path.Combine(root,"analytics-050-results.json"),new{classification="ANONYMIZED_REAL_CSV_REPLAY",formula=BurnForecast.WindowRateFormula,samples=rows.Length,reset_timestamp_changed=events.Count(x=>x.event_type=="RESET_TIMESTAMP_CHANGED"),restored_daily_percentage_points=restored,replay_observed_at=replaySample.observed_at_utc,forecast,checks,failures});
  if(failures>0)Environment.ExitCode=1;
 }
 static UsageSample Make(string id,DateTimeOffset time,decimal used,DateTimeOffset reset)=>new(){sample_id=id,poll_id=id,account_context_key="fixture-account",comparison_key="fixture-comparison",source_generation="fixture-generation",context_assurance="ACCOUNT_STABLE",plan_type="pro",limit_id="codex",source_slot="primary",window_duration_mins=10080,used_percent_raw=used,remaining_percent=100-used,reset_at_utc=reset,reset_at_unix_seconds=reset.ToUnixTimeSeconds(),request_started_utc=time,observed_at_utc=time,observed_at_local=time,data_quality="VALID",polling_interval_seconds=300};
}
