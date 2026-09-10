using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace CodexUsageMonitor;

// Focused native QA only; normal launches never attach this observer or create fixtures.
internal static class RadarFixNativeHarness {
 static readonly DateTimeOffset FixedNow=DateTimeOffset.Parse("2026-09-08T12:00:00Z",CultureInfo.InvariantCulture);
 static readonly string[] FixtureNames=[
  "F01_article_publication_metadata.html","F02_distinct_same_day_articles.html",
  "F03_banked_credit_not_automatic_refill.html","F04_split_date_and_body.html"];
 const string SourceUrl="https://learn.chatgpt.com/docs/changelog";
 public static void Attach(OverlayForm overlay,MonitorService service,string[] args){
  int index=Array.IndexOf(args,"--radar-fix-ui");if(index<0)return;
  int input=Array.IndexOf(args,"--fixtures");
  if(index+1>=args.Length||input<0||input+1>=args.Length)throw new ArgumentException("Focused QA requires output and fixture directories.");
  string output=Path.GetFullPath(args[index+1]),fixtures=Path.GetFullPath(args[input+1]);Directory.CreateDirectory(output);
  var structures=new List<object>();var observerErrors=new List<string>();var captureGate=new object();var checks=new List<object>();
  var attachedUtc=DateTimeOffset.UtcNow;int frameCount=0;bool focused=false;
  var previousObserver=service.Radar.HtmlObserved;
  service.Radar.HtmlObserved=(id,url,html)=>{
   previousObserver?.Invoke(id,url,html);
   try{
    var privacy=new ReportExporter.Privacy();
    var articles=AnnouncementParser.Matches(html,@"<article\b[^>]*>(.*?)</article>").Cast<Match>().Take(3).Select(article=>{
     string tag=article.Value[..(article.Value.IndexOf('>')+1)];
     var heading=AnnouncementParser.Match(article.Value,@"<h[123]\b[^>]*>(.*?)</h[123]>").Value;
     var timeElements=AnnouncementParser.Matches(article.Value,@"<time\b[^>]*>.*?</time>").Cast<Match>().Take(4).Select(t=>{
      string open=t.Value[..(t.Value.IndexOf('>')+1)];
      return new{datetime=privacy.Scrub(AnnouncementParser.Attr(open,"datetime")),label=privacy.Scrub(AnnouncementParser.Clean(AnnouncementParser.Plain(t.Value),100))};
     }).ToArray();
     string body=string.Join(" ",AnnouncementParser.Matches(article.Value,@"<p\b[^>]*>(.*?)</p>").Cast<Match>().Take(4).Select(x=>AnnouncementParser.Plain(x.Groups[1].Value)));
     string href=AnnouncementParser.Attr(AnnouncementParser.Match(heading,@"<a\b[^>]*>").Value,"href");
     string link=Uri.TryCreate(new Uri(url),href,out var u)?privacy.PublicUrl(u.AbsoluteUri):"";
     return new{original_id=privacy.Scrub(AnnouncementParser.Clean(AnnouncementParser.Attr(tag,"id"),160)),
      event_id=privacy.Scrub(AnnouncementParser.Clean(AnnouncementParser.Attr(tag,"data-event-id"),160)),
      heading=privacy.Scrub(AnnouncementParser.Clean(AnnouncementParser.Plain(heading),180)),permalink=link,time_elements=timeElements,
      paragraph_count=AnnouncementParser.Matches(article.Value,@"<p\b").Count,
      patterns=new{mentions_codex=body.Contains("Codex",StringComparison.OrdinalIgnoreCase),mentions_reset=body.Contains("reset",StringComparison.OrdinalIgnoreCase),mentions_credit=body.Contains("credit",StringComparison.OrdinalIgnoreCase)},
      body_excerpt=privacy.Scrub(AnnouncementParser.Clean(body,420))};
    }).ToArray();
    lock(captureGate){
     structures.Add(new{classification="REAL_PUBLIC_HTTP_STRUCTURAL_PROJECTION",source_id=privacy.Scrub(id),source_url=privacy.PublicUrl(url),observed_utc=DateTimeOffset.UtcNow,
      html_utf8_bytes=Encoding.UTF8.GetByteCount(html),html_sha256=Hash(Encoding.UTF8.GetBytes(html)),article_count=AnnouncementParser.Matches(html,@"<article\b").Count,
      retained_article_count=articles.Length,articles,raw_page_saved=false,observer_position="PRODUCT_HTTP_BODY_BEFORE_PRODUCTION_PARSE"});
     AtomicJson.Save(Path.Combine(output,"public-source-structure.json"),structures);
    }
   }catch(Exception ex){lock(captureGate){observerErrors.Add(ex.GetType().Name);}}
  };
  void Check(string id,bool condition,object? detail=null,string classification="FINAL_EXE_NATIVE_FLOW"){
   checks.Add(new{id,result=condition?"PASS":"FAIL",classification,detail});
   AtomicJson.Save(Path.Combine(output,"radar-fix-checks.json"),checks);
   if(!condition)throw new InvalidOperationException(id);
  }
  async Task Shot(Form form,string name,string kind,string cases){
   await PublicNativeHarness.Shot(form,name,kind,cases);frameCount++;
  }
  async Task DemoShot(OverlayForm form,string name){
   await PublicNativeHarness.DemoShot(form,name);frameCount++;
  }
  overlay.Shown+=async(_,_)=>{
   string initialLanguage=L.Language;bool initialCompact=service.Settings.Compact;int initialOpacity=service.Settings.OpacityPercent;
   string initialSettingLanguage=service.Settings.UiLanguage;Point initialLocation=overlay.Location;
   try{
    var wait=Stopwatch.StartNew();while(!service.Verified&&wait.Elapsed.TotalSeconds<75)await Task.Delay(150);
    Check("J real app-server to production parser and weekly clock",service.Verified&&service.ChildId>0&&service.MainClock.Value?.Remaining!=null,
     new{codex_version=service.Current?.Version,assurance=service.Current?.Assurance,weekly_remaining=service.MainClock.Value?.Remaining,main_last_success=service.MainClock.SuccessUtc,owned_child_present=service.ChildId>0},"REAL_READ_ONLY_PRODUCER");
    int child=service.ChildId;string generation=service.Current!.Generation;var success=service.MainClock.SuccessUtc;
    for(int i=0;i<12;i++){L.SetLanguage(i%2==0?"zh-TW":"en-US");await Task.Delay(35);}
    Check("J language switches preserve live producer and last-success",child==service.ChildId&&generation==service.Current?.Generation&&success==service.MainClock.SuccessUtc,
     new{switch_count=12,child_unchanged=child==service.ChildId,last_success_unchanged=success==service.MainClock.SuccessUtc},"REAL_READ_ONLY_PRODUCER");
    PublicNativeHarness.BeginFocused(output,overlay.DeviceDpi);focused=true;
    L.SetLanguage("en-US");service.Settings.Compact=false;service.Settings.OpacityPercent=70;overlay.ApplySettings();overlay.Location=new(180,160);
    await Shot(overlay,"live-standard","REAL","J final EXE true weekly quota");
    overlay.ToggleMode();await Shot(overlay,"live-compact","REAL","J accepted Compact retained");overlay.Hide();

    var inputs=new Dictionary<string,string>();var parsed=new Dictionary<string,List<Announcement>>();var fixtureSources=new List<SourceStatus>();
    string inputsRoot=Path.Combine(output,"inputs");Directory.CreateDirectory(inputsRoot);
    foreach(string name in FixtureNames){
     string path=Path.Combine(fixtures,name);string html=File.ReadAllText(path,Encoding.UTF8);inputs[name]=html;
     File.Copy(path,Path.Combine(inputsRoot,name),true);
     string id="synthetic-review-"+name[..3];
     var entries=AnnouncementParser.Parse(id,SourceUrl,html,FixedNow,out int articleCount);parsed[name]=entries;
     fixtureSources.Add(new SourceStatus(id,SourceUrl,Status:"SYNTHETIC_HTML_INPUT",LastAttempt:FixedNow,CandidateCount:articleCount,
      BodyHash:Hash(Encoding.UTF8.GetBytes(html)),ResponseBytes:Encoding.UTF8.GetByteCount(html),FinalUrl:SourceUrl));
     AtomicJson.Save(Path.Combine(output,"parsed-"+name[..3]+".json"),new{classification="SYNTHETIC_HTML_THROUGH_PRODUCTION_PARSER",fixture=name,parse_now_utc=FixedNow,actual_parse_utc=DateTimeOffset.UtcNow,
      input_sha256=Hash(Encoding.UTF8.GetBytes(html)),article_candidates=articleCount,entries});
    }
    var expectedTime=DateTimeOffset.Parse("2026-09-10T01:00:00Z",CultureInfo.InvariantCulture);
    foreach(string name in new[]{FixtureNames[0],FixtureNames[3]})
     Check(name[..3]+" article publication anchors relative execution time",parsed[name].Count==1&&parsed[name][0].ResetUtc==expectedTime&&parsed[name][0].TimeQuality=="APPROXIMATE",
      new{parse_now_utc=FixedNow,parsed=parsed[name]},"SYNTHETIC_HTML_THROUGH_PRODUCTION_PARSER");
    var pair=parsed[FixtureNames[1]];var grants=parsed[FixtureNames[2]];
    Check("F02 exact HTML preserves both independent article identities",pair.Count==2&&pair.Select(x=>x.EventId).Distinct().Count()==2&&pair.Any(x=>x.Scope=="PLUS")&&pair.Any(x=>x.Scope=="PRO"),classification:"SYNTHETIC_HTML_THROUGH_PRODUCTION_PARSER");
    Check("F03 exact HTML grants a credit without automatic quota refill",grants.Count==1&&grants[0].Effect=="BANKED_CREDIT_GRANT"&&grants[0].ResetType!="GLOBAL",classification:"SYNTHETIC_HTML_THROUGH_PRODUCTION_PARSER");

    foreach(string lang in new[]{"en-US","zh-TW"}){
     L.SetLanguage(lang);string dataRoot=Path.Combine(output,"synthetic-native-"+lang);
     Seed(dataRoot,43,lang,fixtureSources);
     using var data=new MonitorService(dataRoot);Check("Synthetic cached quota has no producer "+lang,data.ChildId==0&&!data.Verified&&data.MainClock.Value?.Remaining==43);
     data.Radar.Apply(pair,FixedNow);Check("F02 Apply keeps two unread articles "+lang,data.Radar.UnreadItems.Count==2);
     using var hud=new OverlayForm(data,true){TestWeekly="43%",TestAge=L.T("Overlay.UpdatedSeconds",23),TestDays=["—","—","—"]};
     data.Settings.Compact=false;hud.Show();hud.Location=new(180,160);hud.Render();
     await DemoShot(hud,"unread-standard-"+lang);
     hud.ToggleMode();await DemoShot(hud,"unread-compact-"+lang);hud.Hide();
     var first=pair.Single(x=>x.Scope=="PLUS");
     using(var card=new ResetInfoForm(data,first)){
      card.Show();PublicNativeHarness.Demo(card);await Shot(card,"F02-first-"+lang,"SYNTHETIC","F02 first article only read");
      var firstState=data.Radar.Snapshot().Events.Single(x=>x.EventId==first.EventId);var readAt=firstState.ReadAt;
      Check("F02 only successfully rendered first article is read "+lang,card.DisplayedEventId==first.EventId&&readAt!=null&&data.Radar.UnreadItems.Count==1);
      L.SetLanguage(lang=="en-US"?"zh-TW":"en-US");await Task.Delay(80);L.SetLanguage(lang);await Task.Delay(80);
      Check("F02 card language changes preserve event and read timestamp "+lang,card.DisplayedEventId==first.EventId&&data.Radar.Snapshot().Events.Single(x=>x.EventId==first.EventId).ReadAt==readAt&&data.Radar.UnreadItems.Count==1);
      PublicNativeHarness.Click(card,"Radar.Next");await Shot(card,"F02-next-"+lang,"SYNTHETIC","F02 second independent article; automatic positive suggestion");
      Check("F02 Next reads only the second displayed article "+lang,card.DisplayedEventId==pair.Single(x=>x.Scope=="PRO").EventId&&data.Radar.UnreadItems.Count==0);
      card.Close();
     }
     data.Radar.Apply(grants,FixedNow);
     await GrantCard(data,grants[0],43,lang,"F03-credit-43-"+lang);
     string zeroRoot=Path.Combine(output,"synthetic-zero-"+lang);Seed(zeroRoot,0,lang,fixtureSources);
     using(var zero=new MonitorService(zeroRoot)){
      zero.Radar.Apply(grants,FixedNow);await GrantCard(zero,grants[0],0,lang,"F03-credit-zero-"+lang);
      Check("F03 zero-credit fixture does not start a producer "+lang,zero.ChildId==0&&!zero.Verified);
     }

     // This additional HTML is explicitly synthetic and uses current timestamps only to
     // exercise correlation with recent synthetic observations. It is never live news.
     var rows=R004Tests.Rows(74,100);var execution=rows[1].observed_at_utc;var published=rows[0].observed_at_utc.AddMinutes(-1);
     string autoHtml="<html><body><article id=\"synthetic-explicit-automatic\"><header><h2>DEMO Codex automatic quota reset</h2><time datetime=\""+published.ToString("O",CultureInfo.InvariantCulture)+"\">Published</time></header><p>Codex global reset for all paid plans started at "+execution.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'",CultureInfo.InvariantCulture)+".</p></article></body></html>";
     string autoFile=Path.Combine(inputsRoot,"SYNTHETIC_explicit_automatic_"+lang+".html");File.WriteAllText(autoFile,autoHtml,Encoding.UTF8);
     var automatic=AnnouncementParser.Parse("synthetic-explicit-automatic",SourceUrl,autoHtml,DateTimeOffset.UtcNow,out int autoCount);
     Check("Synthetic recent automatic HTML passes the actual parser "+lang,automatic.Count==1&&automatic[0].Effect=="AUTOMATIC_QUOTA_RESET"&&automatic[0].ResetUtc!=null);
     data.History.Save(rows);data.RebuildDaily();data.Radar.Apply(automatic,DateTimeOffset.UtcNow);data.Radar.Correlate(rows);
     var correlation=data.Radar.Snapshot().Correlations.Single();
     Check("Synthetic automatic correlation retains unknown account cause "+lang,correlation.Classification=="GLOBAL_RESET_PROBABLE"&&correlation.Cause=="UNKNOWN"&&correlation.AnnouncementId==automatic[0].EventId);
     AtomicJson.Save(Path.Combine(output,"synthetic-chain-"+lang+".json"),new{classification="SYNTHETIC_HTML_TO_NATIVE_CARD_TO_EXPORT",fixed_fixture_parse_utc=FixedNow,
      actual_render_utc=DateTimeOffset.UtcNow,rows,automatic_html_sha256=Hash(Encoding.UTF8.GetBytes(autoHtml)),automatic_candidates=autoCount,automatic,radar=data.Radar.Snapshot(),model_calls=0,producer_started=false});
     var range=new HistoryFilter(DateTimeOffset.UtcNow.AddHours(-1),DateTimeOffset.UtcNow);
     using(var export=new ExportForm(data,range)){
      export.ReportLanguage=lang;export.Show();PublicNativeHarness.Demo(export);await Task.Delay(120);
      bool visible=export.Visible;await export.RunExport(Path.Combine(output,"synthetic-analysis-"+lang+".log"));
      Check("I visible formal ExportForm produces bilingual analysis Log "+lang,visible&&export.LastExport!=null,
       new{route="ExportForm.RunExport",report_language=lang});
      ValidateLog(export.LastExport!,lang,Check);export.Close();
     }
     string csv=ReportExporter.ExportCsv(data,range);PublicNativeHarness.CopyCsv(csv,"synthetic-usage-"+lang);
     Check("I production CSV includes both selected synthetic samples "+lang,Csv.Records<UsageSample>(csv).Count()==2);
     hud.Close();
    }
    var sourceWait=Stopwatch.StartNew();
    bool SourceCompleted()=>ResetRadar.OfficialSources.All(x=>service.Radar.Snapshot().Sources.Any(s=>s.Id==x.id&&s.LastAttempt>=attachedUtc.AddSeconds(-2)));
    while(!SourceCompleted()&&sourceWait.Elapsed.TotalSeconds<30)await Task.Delay(150);
    var radar=service.Radar.Snapshot();var scrub=new ReportExporter.Privacy();object[] structural;string[] errors;
    lock(captureGate){structural=structures.ToArray();errors=observerErrors.ToArray();}
    bool available=radar.Sources.Any(x=>x.HttpStatus==200&&x.Status=="OK");
    if(available)Check("F01 real HTTP 200 retains safe article structure and production parser output",structural.Length>0&&errors.Length==0,
     new{structural_samples=structural.Length,parsed_announcements=radar.Events.Count},"REAL_PUBLIC_HTTP_AND_PRODUCTION_PARSER");
    else checks.Add(new{id="F01 live supported source structure",result="LIMITED",classification="REAL_PUBLIC_HTTP_UNAVAILABLE",detail="No successful HTTP 200 body; no bypass or fabricated notice."});
    AtomicJson.Save(Path.Combine(output,"public-source-smoke.json"),new{classification="REAL_PRODUCT_HTTP_AND_PRODUCTION_PARSER",exe_sha256=ReportExporter.ExeHash(),actual_utc=DateTimeOffset.UtcNow,
     coverage=radar.Sources.Count<2||radar.Sources.Any(x=>x.Failures>0||x.LastSuccess==null)?"LIMITED":"AVAILABLE_CONFIGURED_SOURCES_ONLY",
     source_attempts_completed=SourceCompleted(),source_status=radar.Sources.Select(scrub.Source),parsed_announcements=radar.Events.Select(scrub.Announcement),
     structural_sample_count=structural.Length,observer_errors=errors,cookies=false,model_calls=0,no_current_notice_is_valid=true});
    Check("J real quota survives all native language and Radar work",service.Verified&&service.ChildId==child&&service.MainClock.Value?.Remaining!=null&&service.MainClock.SuccessUtc>=success,
     new{codex_version=service.Current?.Version,weekly_remaining=service.MainClock.Value?.Remaining,main_last_success=service.MainClock.SuccessUtc,child_unchanged=service.ChildId==child},"REAL_READ_ONLY_PRODUCER");
    Check("Affected native resources resolved",L.Missing.Count==0,L.Missing.Keys.ToArray());
    AtomicJson.Save(Path.Combine(output,"radar-fix-checks.json"),checks);
    AtomicJson.Save(Path.Combine(output,"radar-fix-complete.json"),new{exe_sha256=ReportExporter.ExeHash(),version=L.Version,frames=frameCount,actual_os=Environment.OSVersion.VersionString,actual_dpi=overlay.DeviceDpi,
     fixed_fixture_parse_utc=FixedNow,actual_complete_utc=DateTimeOffset.UtcNow,visual_review="PENDING_ACTUAL_IMAGE_REVIEW",win11="NOT_RUN",physical_other_dpi="NOT_RUN",model_calls=0});
   }catch(Exception ex){
    AtomicJson.Save(Path.Combine(output,"radar-fix-failure.json"),new{classification="NATIVE_RUN_INCOMPLETE",error=new ReportExporter.Privacy().Scrub(ex.Message),exception_type=ex.GetType().Name,frames=frameCount,exe_sha256=ReportExporter.ExeHash()});
   }finally{
    service.Radar.HtmlObserved=previousObserver;
    if(focused)PublicNativeHarness.EndFocused();
    service.Settings.Compact=initialCompact;service.Settings.OpacityPercent=initialOpacity;service.Settings.UiLanguage=initialSettingLanguage;
    L.SetLanguage(initialLanguage);overlay.ApplySettings();overlay.Location=initialLocation;
    if(args.Contains("--radar-fix-exit"))overlay.Close();else overlay.Show();
   }

   async Task GrantCard(MonitorService data,Announcement grant,decimal value,string lang,string name){
    using var card=new ResetInfoForm(data,grant);card.Show();PublicNativeHarness.Demo(card);await Shot(card,name,"SYNTHETIC","F03 parsed credit grant at "+value.ToString(CultureInfo.InvariantCulture)+"% with two credits");
    string key=ResetRadar.SuggestionKey(grant,value,data.Current?.ResetCredits,false,DateTimeOffset.UtcNow,"pro");
    var text=card.AllControls().OfType<Label>().Select(x=>x.Text).ToArray();
    Check("F03 native card separates credit grant from automatic reset "+value+" "+lang,
     key=="SuggestCreditGrant"&&text.Contains(L.T("Radar.CreditGrantTime"))&&text.Contains(L.T("Radar.SuggestCreditGrant"))&&text.Contains(value.ToString(CultureInfo.InvariantCulture)+"%"),
     new{effect=grant.Effect,suggestion_key=key,available_credits=data.Current?.ResetCredits,displayed_remaining=data.MainClock.Value?.Remaining});
    card.Close();
   }
  };
 }
 static void Seed(string root,decimal remaining,string lang,List<SourceStatus> sources){
  Directory.CreateDirectory(root);var now=DateTimeOffset.UtcNow;
  var window=new WindowQuota("codex","Codex","primary",10080,100-remaining,now.AddDays(7).ToUnixTimeSeconds(),"VALID","pro",null,null,null,"");
  var cache=new Snapshot(now,now,0,"SYNTHETIC-ACCOUNT","STABLE_SCOPE","SYNTHETIC-GENERATION","SYNTHETIC_NO_PRODUCER",[window],2,[]);
  AtomicJson.Save(Path.Combine(root,"last_good.json"),cache);
  AtomicJson.Save(Path.Combine(root,"settings.json"),new Settings{UiLanguage=lang,OpacityPercent=70,Compact=false});
  AtomicJson.Save(Path.Combine(root,"reset_radar.json"),new RadarState{Sources=sources.ToList()});
 }
 static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
 static void ValidateLog(string file,string language,Action<string,bool,object?,string> check){
  string text=File.ReadAllText(file,Encoding.UTF8);var records=File.ReadLines(file).Where(x=>x.StartsWith("{",StringComparison.Ordinal)).Select(x=>JsonSerializer.Deserialize<JsonElement>(x)).ToList();
  var data=records.Where(x=>x.TryGetProperty("record_type",out var r)&&r.GetString()=="DATA").ToList();
  var header=data.Single(x=>x.GetProperty("section").GetString()=="00_REPORT_HEADER").GetProperty("data");
  check("I embedded prompt and explicit synthetic report "+language,text.StartsWith(ReportExporter.Prompt(language),StringComparison.Ordinal)&&header.GetProperty("report_language").GetString()==language&&header.GetProperty("synthetic_test_data").GetBoolean(),null,"SYNTHETIC_FORMAL_EXPORT");
  var integrity=data.Single(x=>x.GetProperty("section").GetString()=="15_INTEGRITY").GetProperty("data");
  var counts=integrity.GetProperty("section_record_counts");var hashes=integrity.GetProperty("section_data_sha256");
  bool valid=counts.EnumerateObject().Count()==22;
  foreach(var section in counts.EnumerateObject()){
   var rows=File.ReadLines(file).Where(line=>{if(!line.StartsWith("{",StringComparison.Ordinal))return false;using var d=JsonDocument.Parse(line);var x=d.RootElement;return x.TryGetProperty("section",out var s)&&s.GetString()==section.Name&&x.GetProperty("record_type").GetString()=="DATA";}).ToArray();
   valid&=rows.Length==section.Value.GetInt32();
   if(section.Name!="15_INTEGRITY")valid&=Hash(Encoding.UTF8.GetBytes(string.Concat(rows.Select(x=>x+"\n"))))==hashes.GetProperty(section.Name).GetString();
  }
  check("I all 22 machine section counts and hashes "+language,valid,null,"SYNTHETIC_FORMAL_EXPORT");
  var announcements=data.Where(x=>x.GetProperty("section").GetString()=="17_ANNOUNCEMENT_EVENTS").Select(x=>x.GetProperty("data").GetProperty("announcement")).ToArray();
  check("I analysis retains parsed effects and article provenance "+language,announcements.Any(x=>x.GetProperty("Effect").GetString()=="BANKED_CREDIT_GRANT")&&announcements.Any(x=>x.GetProperty("Effect").GetString()=="AUTOMATIC_QUOTA_RESET")&&announcements.All(x=>x.GetProperty("OriginalId").GetString()!="")&&data.Any(x=>x.GetProperty("section").GetString()=="19_RESET_CORRELATION")&&data.Any(x=>x.GetProperty("section").GetString()=="21_READ_STATE"),null,"SYNTHETIC_FORMAL_EXPORT");
 }
}
