using System.Net;
using System.Text;
namespace CodexUsageMonitor;
internal static class R004Tests {
 public static List<UsageSample> Rows(decimal before,decimal after,bool gap=false){
  var t=DateTimeOffset.Now.AddMinutes(-5);var r=t.AddDays(7);
  UsageSample Row(decimal v,int i)=>new(){sample_id="SYNTHETIC-"+i,poll_id="SYNTHETIC-"+i,observed_at_utc=t.AddSeconds(i*90).ToUniversalTime(),observed_at_local=t.AddSeconds(i*90),remaining_percent=v,used_percent_raw=100-v,limit_id="codex",source_slot="primary",window_duration_mins=10080,account_context_key="SYNTHETIC",source_generation="SYNTHETIC",comparison_key="SYNTHETIC-codex-weekly",context_assurance="STABLE_SCOPE",plan_type="pro",data_quality="VALID",polling_interval_seconds=90,reset_at_utc=r,reset_at_unix_seconds=r.ToUnixTimeSeconds(),reset_credits_available_count=2,had_gap=i==1&&gap};
  return [Row(before,0),Row(after,1)];
 }
 public static Announcement Announcement(DateTimeOffset now)=>AnnouncementParser.Candidate("fixture-official","https://help.openai.com/en/articles/fixture",1,"synthetic-global-reset","Codex Reset", "Codex usage global reset for all paid plans will begin tomorrow around 6 PM PT.",now,now)!;
 public static void Run(string[] args){
  int i=Array.IndexOf(args,"--test-root");string root=Path.GetFullPath(args[i+1]);Directory.CreateDirectory(root);var results=new List<object>();var now=DateTimeOffset.UtcNow;
  void Assert(bool b,string message="assertion"){if(!b)throw new InvalidOperationException(message);}
  void Check(string id,Action test){try{test();results.Add(new{id,result="PASS",classification="SYNTHETIC_PRODUCT_CODE"});}catch(Exception e){results.Add(new{id,result="FAIL",classification="SYNTHETIC_PRODUCT_CODE",error=e.Message});}}
  Check("F05 startup mode migration",()=>{var s=new Settings{Compact=true};s.Validate();s.ApplyStartupMode();Assert(s.Compact);s.StartupMode="STANDARD";s.ApplyStartupMode();Assert(!s.Compact);s.StartupMode="COMPACT";s.ApplyStartupMode();Assert(s.Compact);});
  Check("F06 default opacity",()=>Assert(new Settings().OpacityPercent==70));
  foreach(var (id,a,b) in new[]{("F10",100m,99m),("F11",100m,95m),("F12",100m,40m),("F13",5m,0m)})
   Check(id+" chart range",()=>{var range=TrendView.Zoom(Rows(a,b));Assert(range.min<=b&&range.max>=a&&range.max-range.min>=2&&range.max<=100&&range.min>=0);if(id=="F10")Assert(range==(98m,100m));});
  Check("F14 no gap bridge",()=>{var rows=Rows(70,40,true);Assert(!TrendView.Bridge(rows[0],rows[1]));});
  Check("F07 retained daily math",()=>{var rows=Rows(100,99);var d=DailyUsageCalculator.Calculate(rows,TimeZoneInfo.Local,DateTimeOffset.Now.Date,now);Assert(d.Days.Sum(x=>x.observed_drawdown_pp??0)==1);});
  Check("F15 no announcement no badge",()=>{using var radar=new ResetRadar(Path.Combine(root,"empty"));Assert(radar.Unread==null);});
  var ann=Announcement(now);AtomicJson.Save(Path.Combine(root,"announcement-fixture.json"),ann);
  // PUBLIC blueprint supersedes language-specific wording and requires a known applicable plan for action hints.
  void AssertSuggestion(Announcement item,decimal? weekly,int? credits,bool observed,string key,string? plan="pro"){
   foreach(string language in new[]{"en-US","zh-TW"})
    Assert(ResetRadar.Suggest(item,weekly,credits,observed,now,plan,language)==L.TFor(language,"Radar."+key),"Suggestion semantics: "+key+" / "+language);
  }
  Check("F16 F19 F20 F21 revision dedup",()=>{
   using var radar=new ResetRadar(Path.Combine(root,"dedup"));radar.Apply([ann],now);Assert(radar.Unread!=null);radar.Read(ann.EventId);radar.Apply([ann],now.AddMinutes(1));Assert(radar.Unread==null);
   var minor=ann with{Summary=ann.Summary+" Minor copy edit.",ContentHash="minor"};radar.Apply([minor],now.AddMinutes(2));Assert(radar.Unread==null);
   var major=ann with{ResetUtc=ann.ResetUtc!.Value.AddMinutes(30)};radar.Apply([major],now.AddMinutes(3));Assert(radar.Unread!=null);radar.Read(ann.EventId);
   radar.Apply([major],now.AddMinutes(4));Assert(radar.Unread==null);
   radar.Apply([major with{Scope="PLUS"}],now.AddMinutes(5));Assert(radar.Unread!=null);
  });
  Check("F22 F23 PT UTC explicit local timezone approximate",()=>{
   var t=AnnouncementParser.Time("September 7, 2026 around 6 PM PT",now);Assert(t.utc==DateTimeOffset.Parse("2026-09-08T01:00:00Z")&&t.quality=="APPROXIMATE");
   Assert(AnnouncementParser.Time("September 7, 2026 18:00 UTC",now).utc==DateTimeOffset.Parse("2026-09-07T18:00:00Z"));
   // The PUBLIC edition displays machine-local time; language never selects Taipei implicitly.
   foreach(string language in new[]{"en-US","zh-TW"}){
    var fixture=ann with{ResetUtc=t.utc,TimeQuality=t.quality};
    Assert(AnnouncementParser.LocalTime(fixture,now,TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time"),language)==L.TFor(language,"Radar.TimeAround","2026-09-08 09:00 (UTC+08:00)"));
    Assert(AnnouncementParser.LocalTime(fixture,now,TimeZoneInfo.Utc,language)==L.TFor(language,"Radar.TimeAround","2026-09-08 01:00 (UTC+00:00)"));
   }
  });
  Check("Review tomorrow date anchor",()=>Assert(AnnouncementParser.Time("September 8, 2026: Codex will reset tomorrow around 6 PM PT",now).utc==DateTimeOffset.Parse("2026-09-10T01:00:00Z")));
  Check("Review phrase edit retains event identity",()=>{string date=now.ToString("MMMM d, yyyy",System.Globalization.CultureInfo.InvariantCulture);
   var one=AnnouncementParser.Parse("s","https://help.openai.com/en/test","<article><h1>Codex</h1><p>"+date+": Codex global reset for all users will begin.</p></article>",now,out _).Single();
   var two=AnnouncementParser.Parse("s","https://help.openai.com/en/test","<article><h1>Codex</h1><p>"+date+": Codex usage limits reset for all users will begin.</p></article>",now,out _).Single();Assert(one.EventId==two.EventId);
  });
  Check("Review tomorrow explicit date no double shift",()=>Assert(AnnouncementParser.Time("Codex will reset tomorrow, September 9, 2026, at 6 PM PT",now).utc==DateTimeOffset.Parse("2026-09-10T01:00:00Z")));
  Check("Review same day separate categories",()=>{string date=now.ToString("MMMM d, yyyy",System.Globalization.CultureInfo.InvariantCulture);var items=AnnouncementParser.Parse("s","https://help.openai.com/en/test","<article><h1>Codex</h1><p>"+date+": Codex global reset for all users will begin.</p><p>"+date+": Codex banked usage reset will be provided.</p></article>",now,out _);Assert(items.Count==2);});
  Check("F24 no time never guessed",()=>{var t=AnnouncementParser.Time("Codex global reset tomorrow; time TBD",now);Assert(t.utc==null);});
  Check("F25 tier3 no official alert",()=>{using var radar=new ResetRadar(Path.Combine(root,"tier3"));radar.Apply([ann with{Tier=3,Confidence="LOW"}],now);Assert(radar.Unread==null);AssertSuggestion(ann with{Tier=3},40,1,false,"SuggestUnknown");});
  Check("F30 recovery recent only",()=>{
   Assert(AnnouncementParser.Candidate("s","https://help.openai.com/en/test",1,"a","Codex","Codex global reset tomorrow at 6 PM PT.",now.AddHours(-49),now)==null);
   Assert(AnnouncementParser.Candidate("s","https://help.openai.com/en/test",1,"a","Codex","Codex global reset tomorrow at 6 PM PT.",now.AddHours(-24),now)!=null);
   Assert(AnnouncementParser.Candidate("s","https://help.openai.com/en/test",1,"a","Codex","On January 1, 2026 we provided a Codex global reset.",null,now)==null);
  });
  Check("F31 irrelevant and negated news",()=>{
   foreach(string body in new[]{"New Codex model released today.","Codex usage limits will not reset today.","There is no global reset for Codex today.","Codex may reset usage limits tomorrow."})
    {var a=AnnouncementParser.Candidate("s","https://help.openai.com/en/test",1,"a","Codex",body,now,now);Assert(a==null||a.Confidence!="HIGH",body);}
  });
  Check("F32 suggestion A",()=>{AssertSuggestion(ann,43,2,false,"SuggestPlannedWork");AssertSuggestion(ann,20,2,false,"SuggestNeutral");});
  Check("F33 suggestion B",()=>AssertSuggestion(ann,0,2,false,"SuggestKeepReset"));
  Check("F34 suggestion C",()=>{AssertSuggestion(ann with{ResetUtc=null},43,2,false,"SuggestUnknown");AssertSuggestion(ann,43,2,false,"SuggestUnknown",null);});
  Check("F35 passed time no local refill",()=>AssertSuggestion(ann with{ResetUtc=now.AddMinutes(-1)},74,2,false,"SuggestPassed"));
  Check("F36 evidence gates scope scheduled banked cause unknown",()=>{
   using var radar=new ResetRadar(Path.Combine(root,"correlation"));var rows=Rows(74,100);var a=ann with{ResetUtc=rows[1].observed_at_utc,Scope="ALL"};
   radar.Apply([a],now);radar.Correlate(rows);var c=radar.Snapshot().Correlations.Single();
   // PUBLIC section12.6 explicitly supersedes GLOBAL_RESET_CONFIRMED as an account-cause claim.
   // A verified announcement plus comparable local refill establishes only PROBABLE correlation.
   Assert(c.Classification=="GLOBAL_RESET_PROBABLE"&&c.AnnouncementStatus=="OFFICIAL_ANNOUNCED"&&c.LocalObservation=="REPLENISHMENT_OBSERVED"&&c.Correlation=="PROBABLE"&&c.Cause=="UNKNOWN");
   radar.Correlate([rows[0],rows[1] with{context_assurance="SCOPE_NOT_FULLY_VERIFIED"}]);c=radar.Snapshot().Correlations.Single();
   Assert(c.Correlation=="INCONCLUSIVE"&&c.LocalObservation=="NOT_COMPARABLE"&&!c.ScopeVerified&&c.Cause=="UNKNOWN"&&c.Reason.Contains("scope insufficient"));
   foreach(var unverified in new[]{a with{Scope="PLUS"},a with{Eligibility="CONDITIONAL"},a with{Eligibility="UNKNOWN"}}){
    radar.Apply([unverified],now);radar.Correlate(rows);c=radar.Snapshot().Correlations.Single();
    Assert(c.Classification=="INCONCLUSIVE"&&c.Correlation=="INCONCLUSIVE"&&c.Cause=="UNKNOWN"&&c.Reason.Contains("eligibility or plan not verified"));
   }
   radar.Apply([a],now);radar.Correlate([rows[0],rows[1] with{reset_credits_available_count=1}]);c=radar.Snapshot().Correlations.Single();
   Assert(c.Classification=="BANKED_RESET_PROBABLE"&&c.Cause=="UNKNOWN"&&c.Correlation!="PROBABLE","Credit decline is supporting evidence, never a proven cause");
   radar.Correlate([rows[0] with{reset_at_utc=rows[1].observed_at_utc},rows[1]]);c=radar.Snapshot().Correlations.Single();
   Assert(c.Classification=="SCHEDULED_WEEKLY_RESET"&&c.Scheduled&&c.Cause=="UNKNOWN"&&c.Correlation!="PROBABLE","Scheduled time is supporting evidence, never a proven cause");
  });
  Check("F37 Human Sept08 observation remains unknown",()=>{
   using var radar=new ResetRadar(Path.Combine(root,"human-sep08"));var rows=Rows(74,100);var t=DateTimeOffset.Parse("2026-09-08T09:22:00+08:00");rows=[rows[0] with{observed_at_utc=t.AddMinutes(-1),observed_at_local=t.AddMinutes(-1),context_assurance="SCOPE_NOT_FULLY_VERIFIED"},rows[1] with{observed_at_utc=t,observed_at_local=t,context_assurance="SCOPE_NOT_FULLY_VERIFIED",reset_at_utc=t.AddDays(7),reset_at_unix_seconds=t.AddDays(7).ToUnixTimeSeconds()}];
   radar.Correlate(rows);var c=radar.Snapshot().Correlations.Single();Assert(c.Classification=="UNCLASSIFIED_REPLENISHMENT"&&c.LocalObservation=="NOT_COMPARABLE"&&c.Cause=="UNKNOWN");
   AtomicJson.Save(Path.Combine(root,"2026-09-08-human-observation-fixture.json"),new{classification="HUMAN_REPORTED",fixture_kind="SYNTHETIC_NARRATIVE_NOT_ACTUAL_PRODUCER",original_producer_samples_available=false,note="Author reported 74% to 100% at 09:22 after a prior-evening manual Full Reset. These rows are synthesized from that narrative; raw producer observations were not supplied. No fixed Tuesday schedule or account-specific cause is inferred.",rows,correlation=c});
  });
  Check("F38 read state persists",()=>{string p=Path.Combine(root,"persist");using(var r=new ResetRadar(p)){r.Apply([ann],now);r.Read(ann.EventId);}using(var r=new ResetRadar(p))Assert(r.Unread==null&&r.Snapshot().Events.Single().ReadAt!=null);});
  foreach(var mode in new[]{"timeout","403","parser","offline","stall"}){
   Check("F26-F29 isolated "+mode,()=>{
    using var service=new MonitorService(Path.Combine(root,"isolation-"+mode));var before=service.MainClock.Value;using var radar=new ResetRadar(Path.Combine(root,"failure-"+mode),new FixtureHandler(mode));
    using var ct=new CancellationTokenSource(40000);radar.Poll(true,ct.Token).GetAwaiter().GetResult();Assert(radar.FailedSourceCount==2);Assert(service.MainClock.Value==before&&!service.Busy&&radar.Unread==null);
   });
  }
  Check("F39 source cache conditional request",()=>{
   string p=Path.Combine(root,"cache");using(var r=new ResetRadar(p,new FixtureHandler("ok")))r.Poll(true,CancellationToken.None).GetAwaiter().GetResult();
   using var next=new ResetRadar(p,new FixtureHandler("304"));next.Poll(false,CancellationToken.None).GetAwaiter().GetResult();Assert(next.HttpRequestCount==0);next.Poll(true,CancellationToken.None).GetAwaiter().GetResult();Assert(next.Snapshot().Sources.All(x=>x.Status=="UNCHANGED"));
  });
  Check("F42 readonly RPC whitelist",()=>{foreach(string forbidden in new[]{"thread/start","turn/start","account/rateLimits/reset","account/resetCredits/consume","credits/purchase"})Assert(!AppServerClient.IsAllowed(forbidden));});
  Check("F41 announcement privacy",()=>{string s=AnnouncementParser.Clean(@"test@example.com C:\Users\someone\private bearer SECRET sk-key123");Assert(!s.Contains("@")&&!s.Contains("C:\\")&&!s.Contains("SECRET"));});
  Check("F40 R004 report sections and prompt",()=>{
   using var service=new MonitorService(Path.Combine(root,"synthetic-report"));service.History.Save(Rows(100,99));service.Radar.Apply([ann],now);
   string file=ReportExporter.Export(service,new(now.AddDays(-1),now),false,CancellationToken.None,Path.Combine(root,"synthetic-report.log"));string text=File.ReadAllText(file);Assert(text.StartsWith(ReportExporter.Prompt())&&text.Contains("21_READ_STATE")&&text.Contains("correlation")&&text.Contains("17_ANNOUNCEMENT_EVENTS"));
  });
  Check("G21 popover bounds 100 125 150",()=>{foreach(float scale in new[]{1f,1.25f,1.5f}){var area=new Rectangle(-1920,0,1920,1080);var rect=DailyPopover.Place(new(-100,1030,90,40),new((int)(292*scale),(int)(152*scale)),area);Assert(area.Contains(rect)&&rect.Right<=0&&rect.Bottom<=1080);}});
  AtomicJson.Save(Path.Combine(root,"r004-tests.json"),new{classification="SYNTHETIC_PRODUCT_CODE",exe_sha256=ReportExporter.ExeHash(),utc=now,results});Environment.ExitCode=results.Any(x=>System.Text.Json.JsonSerializer.Serialize(x).Contains("\"FAIL\""))?1:0;
 }
 sealed class FixtureHandler(string mode):HttpMessageHandler {
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){
   if(mode=="timeout")throw new TaskCanceledException("synthetic timeout");if(mode=="offline")throw new HttpRequestException("synthetic offline");
   if(mode=="403")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
   if(mode=="304"){if(!r.Headers.IfNoneMatch.Any())throw new InvalidOperationException("Missing ETag");return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));}
   var response=new HttpResponseMessage(HttpStatusCode.OK){Content=mode=="stall"?new StreamContent(new StallStream()):new StringContent(mode=="parser"?"changed html":"<article><h1>Codex</h1><p>Routine Codex improvements; no announcement.</p></article>",Encoding.UTF8,"text/html")};
   response.Headers.ETag=new("\"fixture\"");return Task.FromResult(response);
  }
 }
 sealed class StallStream:Stream {
  public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>0;set=>throw new NotSupportedException();}
  public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();public override async ValueTask<int> ReadAsync(Memory<byte> b,CancellationToken ct=default){await Task.Delay(Timeout.Infinite,ct);return 0;}
  public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long n)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
 }
}

