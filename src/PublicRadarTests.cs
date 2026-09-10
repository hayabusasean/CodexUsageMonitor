using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
namespace CodexUsageMonitor;

// Isolated production-code fixtures. No account, model turn, reset action or external HTTP is used.
internal static class PublicRadarTests {
 public static List<object> Run(string root){
  Directory.CreateDirectory(root);var result=new List<object>();var now=DateTimeOffset.UtcNow;
  void Assert(bool ok,string why="assertion"){if(!ok)throw new InvalidOperationException(why);}
  void Check(string id,Action action){try{action();result.Add(new{id,result="PASS",classification="SYNTHETIC_PRODUCTION_CODE"});}catch(Exception e){result.Add(new{id,result="FAIL",classification="SYNTHETIC_PRODUCTION_CODE",error=e.GetType().Name+": "+e.Message});}}
  ResetRadar Radar(string name,HttpMessageHandler? handler=null)=>new(Path.Combine(root,name),handler);
  Announcement Ann(string key="one")=>AnnouncementParser.Candidate("fixture-official","https://help.openai.com/en/articles/fixture",1,key,"Codex public reset",
   "Codex global reset for all paid plans will begin tomorrow around 6 PM PT.",now,now)!;
  var ann=Ann();AtomicJson.Save(Path.Combine(root,"official-notice-SYNTHETIC.json"),new{classification="SYNTHETIC",announcement=ann});
  Check("T01 Pacific daylight time",()=>Assert(AnnouncementParser.Time("September 7, 2026 at 6 PM PT",null).utc==DateTimeOffset.Parse("2026-09-08T01:00:00Z")));
  Check("T02 Pacific standard time",()=>Assert(AnnouncementParser.Time("January 14, 2026 at 6 PM PT",null).utc==DateTimeOffset.Parse("2026-01-15T02:00:00Z")));
  Check("T03 Language independent local time",()=>{
   var a=ann with{ResetUtc=DateTimeOffset.Parse("2026-09-08T01:00:00Z"),TimeQuality="EXPLICIT"};
   foreach(var lang in new[]{"en-US","zh-TW"}){
    Assert(AnnouncementParser.LocalTime(a,now,TimeZoneInfo.Utc,lang).Contains("2026-09-08 01:00 (UTC+00:00)"));
    Assert(AnnouncementParser.LocalTime(a,now,TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time"),lang).Contains("2026-09-08 09:00 (UTC+08:00)"));
   }
  });
  Check("T04 DST ambiguous and invalid stay unknown",()=>{
   Assert(AnnouncementParser.Time("November 1, 2026 at 1:30 AM PT",null).utc==null);
   Assert(AnnouncementParser.Time("March 8, 2026 at 2:30 AM PT",null).utc==null);
   Assert(AnnouncementParser.Time("November 1, 2026 at 1:30 AM PDT",null).utc==DateTimeOffset.Parse("2026-11-01T08:30:00Z"));
   Assert(AnnouncementParser.Time("November 1, 2026 at 1:30 AM PST",null).utc==DateTimeOffset.Parse("2026-11-01T09:30:00Z"));
  });
  Check("T05 Around deadline CST relative semantics",()=>{
   Assert(AnnouncementParser.Time("September 7, 2026 around 6 PM PT",null).quality=="APPROXIMATE");
   Assert(AnnouncementParser.Time("September 7, 2026 by 6 PM PT",null).quality=="DEADLINE");
   Assert(AnnouncementParser.Time("Codex will reset tomorrow at 6 PM PT",null).utc==null);
   Assert(AnnouncementParser.Time("September 7, 2026 at 6 PM CST",null).utc==null);
   Assert(AnnouncementParser.LocalTime(ann with{TimeQuality="DEADLINE"},now,TimeZoneInfo.Utc,"en-US").StartsWith("By "));
   Assert(AnnouncementParser.LocalTime(ann with{TimeQuality="DEADLINE"},now,TimeZoneInfo.Utc,"zh-TW").StartsWith("不晚於 "));
  });
  Check("T05 Prior publication anchor fixes retained",()=>{
   Assert(AnnouncementParser.Time("September 8, 2026: Codex will reset tomorrow at 6 PM PT",now).utc==DateTimeOffset.Parse("2026-09-10T01:00:00Z"));
   Assert(AnnouncementParser.Time("Codex will reset tomorrow, September 9, 2026, at 6 PM PT",now).utc==DateTimeOffset.Parse("2026-09-10T01:00:00Z"));
  });
  Check("R01 R02 R03 Read idempotency and revision",()=>{
   using var r=Radar("read");r.Apply([ann],now);Assert(r.Unread!=null);r.Read(ann.EventId,ann.RevisionId);
   var stamp=r.Snapshot().Events.Single().ReadAt;r.Read(ann.EventId);Assert(r.Snapshot().Events.Single().ReadAt==stamp);
   r.Apply([ann with{Title="Unmodified external title",Summary=ann.Summary+" Minor wording.",ContentHash="minor"}],now.AddSeconds(1));Assert(r.Unread==null);
   foreach(var language in new[]{"en-US","zh-TW"})_ =AnnouncementParser.LocalTime(r.Snapshot().Events.Single(),now,TimeZoneInfo.Utc,language);
   Assert(r.Snapshot().Events.Single().ReadAt==stamp);
   r.Apply([ann with{ResetUtc=ann.ResetUtc!.Value.AddMinutes(30)}],now.AddSeconds(2));Assert(r.Unread!=null);
   r.Read(ann.EventId,ann.RevisionId);Assert(r.Unread!=null,"Old displayed revision must not consume new unread revision");
  });
  Check("R04 One shown event does not consume another",()=>{
   using var r=Radar("multi");var second=Ann("two");r.Apply([ann,second],now);Assert(r.UnreadItems.Count==2);
   r.Read(ann.EventId);Assert(r.UnreadItems.Count==1&&r.UnreadItems[0].EventId==second.EventId);
   r.Read(second.EventId);Assert(r.Unread==null);
  });
  Check("R03 Language variant canonical identity",()=>{
   var dated=ann with{SourceDate=now.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)};
   using var r=Radar("variant");r.Apply([dated],now);r.Read(dated.EventId);var stamp=r.Snapshot().Events.Single().ReadAt;
   r.Apply([dated with{EventId="translated-source-key",Url="https://help.openai.com/zh-TW/articles/fixture",Title="Original title from alternate source language"}],now.AddSeconds(1));
   Assert(r.Snapshot().Events.Count==1&&r.Unread==null&&r.Snapshot().Events.Single().ReadAt==stamp);
  });
  Check("R05 No FAQ or speculative official badge",()=>{
   foreach(string body in new[]{"Codex resets are available to all users.","Codex limits will not reset today.","There is no global reset for Codex today.","Codex may reset usage limits tomorrow."}){
    var a=AnnouncementParser.Candidate("fixture","https://help.openai.com/en/fixture",1,"fixture","Codex",body,now,now);Assert(a==null||a.Confidence!="HIGH",body);
   }
   using var r=Radar("third-party");r.Apply([ann with{Tier=3,Confidence="LOW"}],now);Assert(r.Unread==null);
  });
  Check("R02 Cancellation is a major revision",()=>{
   using var r=Radar("cancel");r.Apply([ann],now);r.Read(ann.EventId);
   var cancelled=AnnouncementParser.Candidate(ann.Source,ann.Url,1,"one",ann.Title,"Codex global reset for all paid plans scheduled for tomorrow has been cancelled.",now,now);
   Assert(cancelled?.Phase=="CANCELLED","Cancellation with intervening eligibility/schedule words must remain in same sentence and set CANCELLED");r.Apply([cancelled!],now);Assert(r.Unread?.Phase=="CANCELLED","Cancellation revision must become unread");
   foreach(var wording in new[]{"has not been cancelled","will never be cancelled"}){
    var retained=AnnouncementParser.Candidate(ann.Source,ann.Url,1,"one",ann.Title,"Codex global reset for all paid plans tomorrow "+wording+".",now,now);
    Assert(retained!=null&&retained.Phase!="CANCELLED","Negated cancellation must not set CANCELLED: "+wording);
   }
  });
  Check("R05 Old mechanism news stays old future event retained",()=>{
   Assert(AnnouncementParser.Candidate("s",ann.Url,1,"old","Codex","On January 1, 2026 we provided a Codex global reset.",null,now)==null);
   Assert(!ResetRadar.Fresh(ann with{PublishedAt=now.AddMonths(-6),ResetUtc=null},now));
   Assert(ResetRadar.Fresh(ann with{PublishedAt=now.AddDays(-4),ResetUtc=now.AddDays(1)},now));
  });
  Check("R06 Official and local are separate cause unknown",()=>{
   using var r=Radar("evidence");var rows=R004Tests.Rows(74,100);var a=ann with{ResetUtc=rows[1].observed_at_utc,Scope="ALL"};r.Apply([a],now);r.Correlate(rows);
   var c=r.Snapshot().Correlations.Single();Assert(c.AnnouncementStatus=="OFFICIAL_ANNOUNCED"&&c.LocalObservation=="REPLENISHMENT_OBSERVED"&&c.Correlation=="PROBABLE"&&c.Cause=="UNKNOWN"&&c.Classification=="GLOBAL_RESET_PROBABLE");
   r.Correlate([rows[0],rows[1] with{context_assurance="SCOPE_NOT_FULLY_VERIFIED"}]);c=r.Snapshot().Correlations.Single();Assert(c.LocalObservation=="NOT_COMPARABLE"&&c.Correlation=="INCONCLUSIVE"&&c.Cause=="UNKNOWN");
   r.Apply([a with{Scope="PLUS"}],now);r.Correlate(rows);Assert(r.Snapshot().Correlations.Single().Correlation=="INCONCLUSIVE");
   r.Apply([a],now);r.Correlate([rows[0],rows[1] with{reset_credits_available_count=1}]);Assert(r.Snapshot().Correlations.Single().Cause=="UNKNOWN");
   r.Correlate([rows[0] with{reset_at_utc=rows[1].observed_at_utc},rows[1]]);Assert(r.Snapshot().Correlations.Single().Cause=="UNKNOWN");
  });
  Check("R06 Historical confirmed projection does not retain causal proof",()=>{
   var c=new ResetCorrelation("a","b",now,now,0,100,"GLOBAL_RESET_CONFIRMED","legacy label","x",true,false);
   var dir=Path.Combine(root,"legacy-evidence");AtomicJson.Save(Path.Combine(dir,"reset_radar.json"),new RadarState{Correlations=[c]});
   using var r=new ResetRadar(dir);Assert(r.Snapshot().Correlations.Single().Cause=="UNKNOWN"&&r.Snapshot().Correlations.Single().Classification!="GLOBAL_RESET_CONFIRMED");
  });
  Check("R07 Conditional suggestions bounded by quota scope and expiry",()=>{
   Assert(ResetRadar.SuggestionKey(ann,43,2,false,now,"pro")=="SuggestPlannedWork");
   Assert(ResetRadar.SuggestionKey(ann,20,2,false,now,"pro")=="SuggestNeutral");
   Assert(ResetRadar.SuggestionKey(ann,0,2,false,now,"pro")=="SuggestKeepReset");
   Assert(ResetRadar.SuggestionKey(ann,0,null,false,now,"pro")=="SuggestNeutral");
   Assert(ResetRadar.SuggestionKey(ann,43,2,false,now,null)=="SuggestUnknown");
   Assert(ResetRadar.SuggestionKey(ann,43,2,false,now,"unknown")=="SuggestUnknown");
   Assert(ResetRadar.SuggestionKey(ann with{Scope="PLUS"},43,2,false,now,"pro")=="SuggestUnknown");
   Assert(ResetRadar.SuggestionKey(ann with{Eligibility="CONDITIONAL"},43,2,false,now,"pro")=="SuggestUnknown");
   Assert(ResetRadar.SuggestionKey(ann with{ResetUtc=now.AddMinutes(-1)},43,2,false,now,"pro")=="SuggestPassed");
  });
  Check("R08 Allowlisted HTTPS URL validation",()=>{
   foreach(var url in new[]{"http://help.openai.com/a","https://localhost/a","https://127.0.0.1/a","https://192.168.1.1/a","file:///C:/a","https://help.openai.com.evil.example/a","https://help.openai.com:8443/a","https://user:pass@help.openai.com/a"})
    Assert(!ResetRadar.IsAllowedPublicUri(new Uri(url)),url);
   Assert(ResetRadar.IsAllowedPublicUri(new Uri("https://help.openai.com/en/fixture")));
  });
  Check("R08 Two parallel source requests bounded and credential free",()=>{
   int active=0,peak=0;
   using var h=new Handler(async(request,ct)=>{
    Assert(request.Headers.Authorization==null&&!request.Headers.Contains("Cookie"));int n=Interlocked.Increment(ref active);InterlockedExtensions.Max(ref peak,n);
    await Task.Delay(50,ct);Interlocked.Decrement(ref active);return Ok();});
   using var r=Radar("parallel",h);r.Poll(true,CancellationToken.None).GetAwaiter().GetResult();Assert(peak==2&&r.HttpRequestCount==2&&r.FailedSourceCount==0);
  });
  Check("R08 Safe three redirects and per-hop bound",()=>{
   var counts=new ConcurrentDictionary<string,int>();using var h=new Handler((request,ct)=>{
    int n=counts.AddOrUpdate(request.RequestUri!.Host,1,(_,old)=>old+1);return Task.FromResult(n<=3?Redirect(new Uri("/hop-"+n,UriKind.Relative)):Ok());});
   using var r=Radar("redirect-three",h);r.Poll(true,CancellationToken.None).GetAwaiter().GetResult();Assert(r.HttpRequestCount==8&&r.FailedSourceCount==0);
  });
  Check("R08 Fourth redirect blocked",()=>{
   using var r=Radar("redirect-loop",new Handler((_,_)=>Task.FromResult(Redirect(new Uri("/loop",UriKind.Relative)))));
   r.Poll(true,CancellationToken.None).GetAwaiter().GetResult();Assert(r.HttpRequestCount==8&&r.Snapshot().Sources.All(x=>x.Error=="REDIRECT_LIMIT"));
  });
  Check("R08 Unsafe redirect never sent",()=>{
   using var r=Radar("unsafe-redirect",new Handler((_,_)=>Task.FromResult(Redirect(new Uri("https://127.0.0.1/private")))));
   r.Poll(true,CancellationToken.None).GetAwaiter().GetResult();Assert(r.HttpRequestCount==2&&r.Snapshot().Sources.All(x=>x.Error=="REDIRECT_NOT_ALLOWED"));
  });
  Check("R08 Retry-After respected including manual force",()=>{
   using var r=Radar("retry-after",new Handler((_,_)=>{var x=new HttpResponseMessage(HttpStatusCode.TooManyRequests);x.Headers.RetryAfter=new RetryConditionHeaderValue(TimeSpan.FromHours(2));return Task.FromResult(x);}));
   r.Poll(true,CancellationToken.None).GetAwaiter().GetResult();Assert(r.Snapshot().Sources.All(x=>x.HttpStatus==429&&x.NextCheck>now.AddMinutes(100)));
   r.Poll(true,CancellationToken.None).GetAwaiter().GetResult();Assert(r.HttpRequestCount==2);
  });
  Check("R08 Decompressed body limit accepts four MiB and rejects extra byte",()=>{
   const string html="<article><h1>Codex</h1><p>Routine improvements only.</p></article>";
   var fit=Encoding.UTF8.GetBytes(html+new string(' ',ResetRadar.MaxResponseBytes-html.Length));
   using(var r=Radar("size-fit",new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new BodyStream(fit))})))){r.Poll(true,CancellationToken.None).GetAwaiter().GetResult();Assert(r.Snapshot().Sources.All(x=>x.ResponseBytes==ResetRadar.MaxResponseBytes&&x.Status=="OK"));}
   using(var r=Radar("size-over",new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new BodyStream(new byte[ResetRadar.MaxResponseBytes+1]))})))){r.Poll(true,CancellationToken.None).GetAwaiter().GetResult();Assert(r.Snapshot().Sources.All(x=>x.Error=="RESPONSE_TOO_LARGE"));}
  });
  Check("R08 Whole body timeout twenty seconds",()=>{
   Assert(ResetRadar.RequestTimeout==TimeSpan.FromSeconds(20));var sw=Stopwatch.StartNew();
   using var r=Radar("body-timeout",new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new BodyStream(null))})));
   r.Poll(true,CancellationToken.None).GetAwaiter().GetResult();Assert(r.Snapshot().Sources.All(x=>x.Error=="TIMEOUT")&&sw.Elapsed<TimeSpan.FromSeconds(28));
  });
  Check("R08 Disable prevents HTTP and cancels owned active pass",()=>{
   var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
   using var r=Radar("disabled",new Handler(async(_,ct)=>{entered.TrySetResult();await Task.Delay(Timeout.Infinite,ct);return Ok();}));
   r.SetEnabled(false);r.Poll(true,CancellationToken.None).GetAwaiter().GetResult();Assert(r.HttpRequestCount==0);
   r.SetEnabled(true);var pass=r.Poll(true,CancellationToken.None);Assert(entered.Task.Wait(2000));r.SetEnabled(false);
   Assert(pass.Wait(2000)&&!r.Enabled&&r.Snapshot().Sources.Count==0);
  });
  Check("R08 Cache ETag 304 does not invent announcement",()=>{
   string dir=Path.Combine(root,"cache");
   using(var r=new ResetRadar(dir,new Handler((_,_)=>{var response=Ok();response.Headers.ETag=new EntityTagHeaderValue("\"fixture\"");return Task.FromResult(response);})))r.Poll(true,CancellationToken.None).GetAwaiter().GetResult();
   using var next=new ResetRadar(dir,new Handler((request,_)=>{Assert(request.Headers.IfNoneMatch.Any());return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));}));
   next.Poll(true,CancellationToken.None).GetAwaiter().GetResult();Assert(next.Snapshot().Sources.All(x=>x.Status=="UNCHANGED")&&next.Unread==null);
  });
  Check("R08 Known source failures isolated from quota clock",()=>{
   using var service=new MonitorService(Path.Combine(root,"quota-isolation"));var value=service.MainClock.Value;
   foreach(string mode in new[]{"403","offline","parser"}){
    using var r=Radar(mode,new Handler((_,_)=>mode switch{"403"=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)),"offline"=>throw new HttpRequestException("synthetic offline"),_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("unexpected structure")})}));
    r.Poll(true,CancellationToken.None).GetAwaiter().GetResult();Assert(r.FailedSourceCount==2&&service.MainClock.Value==value&&!service.Busy);
   }
  });
  Check("R10 Human case never promoted to producer or cause",()=>{
   using var r=Radar("human-case");var rows=R004Tests.Rows(74,100);var observed=DateTimeOffset.Parse("2026-09-08T09:22:00+08:00");
   rows=[rows[0] with{observed_at_utc=observed.AddMinutes(-1),observed_at_local=observed.AddMinutes(-1),context_assurance="SCOPE_NOT_FULLY_VERIFIED"},rows[1] with{observed_at_utc=observed,observed_at_local=observed,context_assurance="SCOPE_NOT_FULLY_VERIFIED"}];
   r.Correlate(rows);var c=r.Snapshot().Correlations.Single();Assert(c.Cause=="UNKNOWN"&&c.LocalObservation=="NOT_COMPARABLE");
   AtomicJson.Save(Path.Combine(root,"2026-09-08-human-case.json"),new{classification="HUMAN_REPORTED_FIXTURE_NOT_PRODUCER",description="Author reported 74% to 100% at 09:22 after a manual reset the prior evening. Original producer samples were not supplied.",correlation=c});
  });
  AtomicJson.Save(Path.Combine(root,"public-radar-tests.json"),new{classification="SYNTHETIC_PRODUCTION_CODE",exe_sha256=ReportExporter.ExeHash(),utc=now,results=result});
  return result;
 }
 static HttpResponseMessage Ok()=>new(HttpStatusCode.OK){Content=new StringContent("<article><h1>Codex</h1><p>Routine improvements only.</p></article>",Encoding.UTF8,"text/html")};
 static HttpResponseMessage Redirect(Uri target){var r=new HttpResponseMessage(HttpStatusCode.Found);r.Headers.Location=target;return r;}
 sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> call):HttpMessageHandler{
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>call(request,ct);
 }
 sealed class BodyStream(byte[]? body):Stream{
  int position;
  public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
  public override long Length=>throw new NotSupportedException();public override long Position{get=>position;set=>throw new NotSupportedException();}
  public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
  public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default){
   if(body==null){await Task.Delay(Timeout.Infinite,ct);return 0;}
   ct.ThrowIfCancellationRequested();int count=Math.Min(buffer.Length,body.Length-position);body.AsMemory(position,count).CopyTo(buffer);position+=count;return count;
  }
  public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();
  public override void SetLength(long n)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
 }
 static class InterlockedExtensions{
  public static void Max(ref int target,int value){int old;do{old=Volatile.Read(ref target);if(old>=value)return;}while(Interlocked.CompareExchange(ref target,value,old)!=old);}
 }
}
