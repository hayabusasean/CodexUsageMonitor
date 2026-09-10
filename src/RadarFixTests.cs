using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace CodexUsageMonitor;

// All notices and observations in this harness are SYNTHETIC. It never starts MonitorService,
// HTTP polling, the Codex producer, a model turn, or a reset-credit action.
internal static class RadarFixTests {
 static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-09-08T12:00:00Z",CultureInfo.InvariantCulture);
 const string Source="openai-help-resets",ListUrl="https://help.openai.com/en/articles/synthetic-fix-r001-list";
 static void Assert(bool value,string why){if(!value)throw new InvalidOperationException(why);}
 static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
 static string Hash(string text)=>Hash(Encoding.UTF8.GetBytes(text));
 sealed class Proof(string directory) {
  public string Directory{get;}=directory;
  public List<object> Stages{get;}=[];
  public List<Announcement> Parse(string label,string html,DateTimeOffset? at=null,string url=ListUrl){
   string input=Path.Combine(Directory,label+".html");System.IO.Directory.CreateDirectory(Directory);File.WriteAllText(input,html,new UTF8Encoding(false));
   Stages.Add(new{stage="HTML_INPUT",classification="SYNTHETIC",file=Path.GetFileName(input),sha256=Hash(html),source=Source,url,parse_now=at??Now});
   var entries=AnnouncementParser.Parse(Source,url,html,at??Now,out int candidateArticles);
   Stages.Add(new{stage="PRODUCTION_PARSE",candidate_articles=candidateArticles,accepted=entries.Count,announcements=entries});return entries;
  }
  public void State(string stage,ResetRadar radar)=>Stages.Add(new{stage,state=radar.Snapshot(),unread_at_fixed_now=radar.UnreadAt(Now).Select(x=>new{x.EventId,x.RevisionId}).ToArray()});
  public string StateRoot(string name="state")=>Path.Combine(Directory,"synthetic-"+name);
 }
 public static void Run(string root,string fixtures){
  root=Path.GetFullPath(root);fixtures=Path.GetFullPath(fixtures);Directory.CreateDirectory(root);
  string Fixture(string name)=>File.ReadAllText(Path.Combine(fixtures,name),Encoding.UTF8);
  var results=new List<object>();int failures=0;
  void Check(string id,Action<Proof> action){
   string directory=Path.Combine(root,id+"-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(directory);var p=new Proof(directory);string result="PASS",error="";
   try{action(p);}catch(Exception e){result="FAIL";error=e.GetType().Name+": "+e.Message;failures++;}
   var evidence=new{test=id,result,classification="SYNTHETIC_PRODUCTION_PIPELINE",fixed_now_utc=Now,executed_utc=DateTimeOffset.UtcNow,exe_sha256=ReportExporter.ExeHash(),error,stages=p.Stages};
   AtomicJson.Save(Path.Combine(directory,"proof.json"),evidence);results.Add(new{id,result,classification="SYNTHETIC_PRODUCTION_PIPELINE",proof=Path.GetRelativePath(root,Path.Combine(directory,"proof.json")),error});
  }
  Check("F01_article_metadata",p=>{
   var entries=p.Parse("F01",Fixture("F01_article_publication_metadata.html"));Assert(entries.Count==1,"article time metadata lost the notice");var a=entries.Single();
   Assert(a.PublishedAt==DateTimeOffset.Parse("2026-09-08T08:00:00Z",CultureInfo.InvariantCulture),"publication must retain article datetime, not fetch time");
   Assert(a.PublicationBasis=="ARTICLE_TIME","publication basis is not precise article time");Assert(a.ResetUtc==DateTimeOffset.Parse("2026-09-10T01:00:00Z",CultureInfo.InvariantCulture)&&a.TimeQuality=="APPROXIMATE","tomorrow PT/around conversion");
   Assert(a.OriginalId=="codex-reset-alpha"&&a.IdentityConfidence=="ORIGINAL_ID","original article identity missing");
   using var radar=new ResetRadar(p.StateRoot());radar.Apply(entries,Now);p.State("APPLY",radar);Assert(radar.UnreadAt(Now).Count==1,"fresh official article missing from unread");
   var later=p.Parse("F01-later-fetch",Fixture("F01_article_publication_metadata.html"),Now.AddHours(6));Assert(later.Single().PublishedAt==a.PublishedAt&&later.Single().ResetUtc==a.ResetUtc,"fetch time shifted original publication/reset");
   Assert(!ResetRadar.Fresh(a,Now.AddDays(4)),"old passed article revived by freshness");
   Assert(p.Parse("F01-old-fetch",Fixture("F01_article_publication_metadata.html"),Now.AddDays(4)).Count==0,"old passed article reparsed as fresh");
  });
  Check("F04_split_same_article",p=>{
   var entries=p.Parse("F04",Fixture("F04_split_date_and_body.html"));Assert(entries.Count==1,"same-article date/body were separated");var a=entries.Single();
   Assert(a.PublishedAt?.UtcDateTime.Date==Now.UtcDateTime.Date&&a.PublicationBasis=="ARTICLE_DATE_ONLY","date-only provenance was lost or falsely precise");
   Assert(a.ResetUtc==DateTimeOffset.Parse("2026-09-10T01:00:00Z",CultureInfo.InvariantCulture)&&a.TimeQuality=="APPROXIMATE","split date anchor/time quality");
   using var radar=new ResetRadar(p.StateRoot());radar.Apply(entries,Now);p.State("APPLY",radar);Assert(radar.UnreadAt(Now).Count==1,"split article did not reach unread");
  });
  Check("F02_distinct_apply_read_restart",p=>{
   var entries=p.Parse("F02",Fixture("F02_distinct_same_day_articles.html"));Assert(entries.Count==2&&entries.Select(x=>x.EventId).Distinct().Count()==2,"same-day article IDs collided in Parse");
   var plus=entries.Single(x=>x.Scope=="PLUS");var pro=entries.Single(x=>x.Scope=="PRO");
   Assert(plus.ResetUtc==DateTimeOffset.Parse("2026-09-09T01:00:00Z",CultureInfo.InvariantCulture)&&pro.ResetUtc==DateTimeOffset.Parse("2026-09-09T02:00:00Z",CultureInfo.InvariantCulture),"distinct plan/time values were merged");
   Assert(entries.All(x=>x.IdentityConfidence=="ORIGINAL_ID")&&entries.Select(x=>x.OriginalId).Distinct().Count()==2,"source identities not retained");
   string state=p.StateRoot();using(var radar=new ResetRadar(state)){
    radar.Apply(entries,Now);Assert(radar.UnreadAt(Now).Count==2&&radar.Snapshot().Events.Count==2,"Apply fallback merged separate original IDs");p.State("APPLY_TWO",radar);
    radar.Read(plus.EventId,plus.RevisionId);p.State("READ_PLUS_ONLY",radar);Assert(radar.UnreadAt(Now).Single().EventId==pro.EventId,"reading Plus consumed Pro");
    var stamp=radar.Snapshot().Events.Single(x=>x.EventId==plus.EventId).ReadAt;radar.Read(plus.EventId,plus.RevisionId);Assert(radar.Snapshot().Events.Single(x=>x.EventId==plus.EventId).ReadAt==stamp,"read is not idempotent");
   }
   using var restarted=new ResetRadar(state);restarted.Apply(entries,Now.AddMinutes(1));p.State("RESTART_REAPPLY",restarted);Assert(restarted.Snapshot().Events.Count==2&&restarted.UnreadAt(Now).Single().EventId==pro.EventId,"restart lost separate read states");
  });
  Check("F02_shared_article_independent_units",p=>{
   string html="<article id=\"reset-history\"><h2>Codex reset updates</h2><p>2026-09-08: Codex global reset for Plus will begin today at 6 PM PT.</p><p>2026-09-08: Codex global reset for Pro will begin today at 7 PM PT.</p></article>";
   var entries=p.Parse("shared-article",html);Assert(entries.Count==2&&entries.Select(x=>x.EventId).Distinct().Count()==2,"shared article units collapsed in Parse");
   Assert(entries.Select(x=>x.OriginalId).Distinct().Count()==2&&entries.All(x=>x.IdentityConfidence=="LOW_STRUCTURED_FALLBACK"),"article identity overclaims independent event IDs");
   using var radar=new ResetRadar(p.StateRoot());radar.Apply(entries,Now);p.State("APPLY_TWO_UNITS",radar);Assert(radar.Snapshot().Events.Count==2,"Apply collapsed distinct same-article units");
   var plus=entries.Single(x=>x.Scope=="PLUS");radar.Read(plus.EventId,plus.RevisionId);radar.Apply(entries,Now.AddMinutes(1));
   Assert(radar.Snapshot().Events.Count==2&&radar.Snapshot().Events.Single(x=>x.Scope=="PLUS").ReadAt!=null&&radar.Snapshot().Events.Single(x=>x.Scope=="PRO").ReadAt==null,"unit dedup/read state leaked");p.State("REAPPLY_READ_ISOLATED",radar);
  });
  Check("F02_minor_language_major_revisions",p=>{
   string html=Fixture("F01_article_publication_metadata.html");using var radar=new ResetRadar(p.StateRoot());radar.Apply(p.Parse("original",html),Now);var first=radar.Snapshot().Events.Single();radar.Read(first.EventId,first.RevisionId);var read=radar.Snapshot().Events.Single().ReadAt;
   string minor=html.Replace("will begin tomorrow","will begin tomorrow as scheduled",StringComparison.Ordinal);
   radar.Apply(p.Parse("minor",minor),Now.AddMinutes(1));var current=radar.Snapshot().Events.Single();Assert(current.EventId==first.EventId&&current.RevisionId==first.RevisionId&&current.ReadAt==read,"minor wording manufactured unread/revision");
   string translated=minor.Replace("/en/articles/","/zh-TW/articles/",StringComparison.Ordinal).Replace("Codex reset notice","Codex 重置公告 / Codex reset notice",StringComparison.Ordinal).Replace("</p>"," 同一公告的繁體中文說明。</p>",StringComparison.Ordinal);
   radar.Apply(p.Parse("canonical-language",translated,url:ListUrl.Replace("/en/","/zh-TW/",StringComparison.Ordinal)),Now.AddMinutes(2));current=radar.Snapshot().Events.Single();
   Assert(current.EventId==first.EventId&&current.OriginalId==first.OriginalId&&current.RevisionId==first.RevisionId&&current.ReadAt==read,"canonical language variant duplicated event or lost read");p.State("MINOR_AND_LANGUAGE_PRESERVE_READ",radar);
   var later=p.Parse("major-time",html.Replace("6 PM PT","7 PM PT",StringComparison.Ordinal));radar.Apply(later,Now.AddMinutes(3));current=radar.Snapshot().Events.Single();string timeRevision=current.RevisionId;
   Assert(current.EventId==first.EventId&&timeRevision!=first.RevisionId&&current.ReadAt==null&&radar.UnreadAt(Now).Count==1,"major time revision did not alert once");
   radar.Read(current.EventId,first.RevisionId);Assert(radar.UnreadAt(Now).Count==1,"stale displayed revision consumed the new revision");radar.Read(current.EventId,current.RevisionId);radar.Apply(later,Now.AddMinutes(4));current=radar.Snapshot().Events.Single();Assert(current.RevisionId==timeRevision&&current.ReadAt!=null,"identical major revision repeatedly alerted");
   var scope=p.Parse("major-scope",html.Replace("all paid plans","Pro",StringComparison.Ordinal).Replace("6 PM PT","7 PM PT",StringComparison.Ordinal));radar.Apply(scope,Now.AddMinutes(5));current=radar.Snapshot().Events.Single();Assert(current.Scope=="PRO"&&current.RevisionId!=timeRevision&&current.ReadAt==null,"scope major revision missing");
   radar.Read(current.EventId,current.RevisionId);var scopeRevision=current.RevisionId;radar.Apply(scope,Now.AddMinutes(6));Assert(radar.Snapshot().Events.Single().RevisionId==scopeRevision&&radar.UnreadAt(Now).Count==0,"scope revision repeated");p.State("MAJOR_REVISIONS_ONCE",radar);
  });
  Check("F02_permalink_and_fallback",p=>{
   string html=Fixture("F02_distinct_same_day_articles.html").Replace(" id=\"reset-plus-20260908\"","",StringComparison.Ordinal).Replace(" id=\"reset-pro-20260908\"","",StringComparison.Ordinal);
   var permalink=p.Parse("permalinks",html);Assert(permalink.Count==2&&permalink.All(x=>x.IdentityConfidence=="CANONICAL_PERMALINK"),"permalink-only article identity missing");
   using(var r=new ResetRadar(p.StateRoot("permalinks"))){r.Apply(permalink,Now);Assert(r.Snapshot().Events.Count==2,"permalink identities merged on Apply");r.Read(permalink[0].EventId);var old=r.Snapshot().Events.Single(x=>x.EventId==permalink[0].EventId);
    r.Apply(p.Parse("permalinks-language",html.Replace("/en/articles/","/zh-TW/articles/",StringComparison.Ordinal),url:ListUrl.Replace("/en/","/zh-TW/",StringComparison.Ordinal)),Now.AddMinutes(1));Assert(r.Snapshot().Events.Count==2&&r.Snapshot().Events.Single(x=>x.EventId==old.EventId).ReadAt==old.ReadAt,"canonical permalink language changed read identity");p.State("PERMALINK_APPLY",r);}
   string fallback="<article><h2>Codex Plus notice</h2><p>2026-09-08: Codex global reset for Plus will begin today at 6 PM PT.</p></article><article><h2>Codex Pro notice</h2><p>2026-09-08: Codex global reset for Pro will begin today at 7 PM PT.</p></article>";
   var low=p.Parse("structured-fallback",fallback);Assert(low.Count==2&&low.Select(x=>x.EventId).Distinct().Count()==2&&low.All(x=>x.IdentityConfidence=="LOW_STRUCTURED_FALLBACK"),"unidentified structured events collide or overclaim identity");
   using var r2=new ResetRadar(p.StateRoot("fallback"));r2.Apply(low,Now);Assert(r2.Snapshot().Events.Count==2,"Apply collapsed different low-confidence identities");p.State("STRUCTURED_FALLBACK_APPLY",r2);
  });
  foreach(decimal remaining in new[]{43m,0m})Check("F03_credit_grant_"+remaining.ToString(CultureInfo.InvariantCulture),p=>{
   var entries=p.Parse("F03",Fixture("F03_banked_credit_not_automatic_refill.html"));Assert(entries.Count==1,"useful credit-grant notice was discarded");var a=entries.Single();
   Assert(a.Effect=="BANKED_CREDIT_GRANT"&&a.Scope=="PAID"&&a.ResetType!="GLOBAL","scope confused with automatic-reset effect");
   using var radar=new ResetRadar(p.StateRoot());radar.Apply(entries,Now);a=radar.Snapshot().Events.Single();var key=ResetRadar.SuggestionKey(a,remaining,2,false,Now,"pro");Assert(!ActionHint(key),"grant produced automatic quota work/wait advice");
   var texts=new[]{"en-US","zh-TW"}.Select(language=>new{language,key,text=ResetRadar.Suggest(a,remaining,2,false,Now,"pro",language)}).ToArray();Assert(texts.All(x=>!string.IsNullOrWhiteSpace(x.text)&&!x.text.StartsWith("Radar.",StringComparison.Ordinal)),"grant bilingual suggestion missing");
   var rows=Refill(a.ResetUtc??Now.AddHours(1),"grant",remaining);radar.Correlate(rows);var c=radar.Snapshot().Correlations.Single();Assert(c.Classification!="GLOBAL_RESET_PROBABLE"&&c.Correlation!="PROBABLE"&&c.Cause=="UNKNOWN","credit grant became automatic refill correlation");
   p.Stages.Add(new{stage="SUGGESTION_AND_CORRELATION",plan="pro",weekly=remaining,available_credits=2,texts,observations=rows,correlation=c});p.State("GRANT_APPLIED",radar);
  });
  Check("F03_positive_automatic_conditions",p=>{
   var entries=p.Parse("automatic",Fixture("F01_article_publication_metadata.html"));using var radar=new ResetRadar(p.StateRoot());radar.Apply(entries,Now);var a=radar.Snapshot().Events.Single();
   Assert(a.Effect=="AUTOMATIC_QUOTA_RESET"&&a.ResetType=="GLOBAL","positive automatic reset was demoted");
   var choices=new List<object>();foreach(var item in new[]{(43m,2,"pro","SuggestPlannedWork"),(0m,2,"pro","SuggestKeepReset"),(20m,2,"pro","SuggestNeutral"),(43m,2,"free","SuggestUnknown")}){var key=ResetRadar.SuggestionKey(a,item.Item1,item.Item2,false,Now,item.Item3);choices.Add(new{remaining=item.Item1,credits=item.Item2,plan=item.Item3,key});Assert(key==item.Item4,"automatic advice eligibility: "+key);}
   Assert(!ActionHint(ResetRadar.SuggestionKey(a,43,2,false,Now,null)),"unknown current plan permits action advice");
   var rows=Refill(a.ResetUtc!.Value,"automatic",43);radar.Correlate(rows);var c=radar.Snapshot().Correlations.Single();Assert(c.Classification=="GLOBAL_RESET_PROBABLE"&&c.Correlation=="PROBABLE"&&c.LocalObservation=="REPLENISHMENT_OBSERVED"&&c.Cause=="UNKNOWN","supported automatic correlation regressed or overclaimed cause");
   radar.Correlate([rows[0],rows[1] with{context_assurance="SCOPE_NOT_FULLY_VERIFIED"}]);Assert(radar.Snapshot().Correlations.Single().Correlation!="PROBABLE","unverified account scope made probable correlation");
   p.Stages.Add(new{stage="CONDITIONAL_POSITIVE",choices,observations=rows,supported_correlation=c});p.State("UNVERIFIED_SCOPE_NEUTRAL",radar);
  });
  Check("F03_faq_purchase_negative_uncertain",p=>{
   foreach(var item in new[]{
    ("faq","Codex banked resets are available to all paid plans. You can purchase reset credits to reset your quota manually."),
    ("purchase","2026-09-08: Codex users on all paid plans will be able to purchase one banked reset credit tomorrow around 6 PM PT."),
    ("negative","2026-09-08: Codex usage limits will not reset tomorrow around 6 PM PT."),
    ("uncertain","2026-09-08: Codex global reset for all paid plans might begin tomorrow around 6 PM PT."),
    ("conditional","2026-09-08: Eligible Pro users will receive a Codex global reset tomorrow around 6 PM PT."),
    ("no-refill","2026-09-08: Codex global reset for all paid plans will begin tomorrow around 6 PM PT, but the current quota will not automatically refill.")}){
    var entries=p.Parse(item.Item1,Article(item.Item1,item.Item2));using var radar=new ResetRadar(p.StateRoot(item.Item1));radar.Apply(entries,Now);
    foreach(var a in radar.Snapshot().Events){Assert(!ActionHint(ResetRadar.SuggestionKey(a,43,2,false,Now,"pro")),item.Item1+" produced unsupported action advice");radar.Correlate(Refill(a.ResetUtc??Now.AddHours(1),item.Item1,43));Assert(radar.Snapshot().Correlations.All(c=>c.Correlation!="PROBABLE"&&c.Cause=="UNKNOWN"),item.Item1+" supported automatic correlation");}
    if(item.Item1 is "faq" or "purchase")Assert(entries.All(a=>a.Effect is "MECHANISM_OR_PURCHASE" or "UNKNOWN"),"FAQ/purchase effect became automatic/grant");
    if(item.Item1 is "negative" or "uncertain" or "no-refill")Assert(radar.UnreadAt(Now).All(a=>a.Effect!="AUTOMATIC_QUOTA_RESET"||a.Confidence!="HIGH"),item.Item1+" became confident automatic announcement");p.State("NEGATIVE_CASE_"+item.Item1,radar);
   }
  });
  Check("F03_cancel_revision",p=>{
   string html=Fixture("F01_article_publication_metadata.html");using var radar=new ResetRadar(p.StateRoot());radar.Apply(p.Parse("planned",html),Now);var original=radar.Snapshot().Events.Single();radar.Read(original.EventId,original.RevisionId);
   var cancelled=p.Parse("cancelled",html.Replace("will begin tomorrow around 6 PM PT.","planned for tomorrow around 6 PM PT has been cancelled.",StringComparison.Ordinal));Assert(cancelled.Single().Phase=="CANCELLED","cancellation article was lost");radar.Apply(cancelled,Now.AddMinutes(1));var current=radar.Snapshot().Events.Single();Assert(current.EventId==original.EventId&&current.ReadAt==null&&current.RevisionId!=original.RevisionId,"cancellation did not revise existing identity");
   Assert(ResetRadar.SuggestionKey(current,43,2,false,Now,"pro")=="SuggestCancelled","cancelled notice retains action advice");radar.Correlate(Refill(current.ResetUtc??Now.AddDays(1),"cancel",43));Assert(radar.Snapshot().Correlations.All(x=>x.Correlation!="PROBABLE"&&x.Cause=="UNKNOWN"),"cancelled reset supported automatic correlation");p.State("CANCELLED_REVISION",radar);
  });
  Check("F01_publication_trust_and_boundaries",p=>{
   var cases=new Dictionary<string,string>{
    ["undated-fetch"]="<article id=\"undated\"><h2>Codex reset notice</h2><p>Codex global reset for all paid plans will begin tomorrow around 6 PM PT.</p></article>",
    ["page-modified"]="<meta property=\"article:modified_time\" content=\"2026-09-08T08:00:00Z\"><article id=\"faq\"><h2>Codex FAQ</h2><p>Last updated September 8, 2026</p><p>Codex global reset for all paid plans will begin tomorrow around 6 PM PT.</p></article>",
    ["old-publication"]="<meta property=\"article:modified_time\" content=\"2026-09-08T08:00:00Z\">"+Fixture("F01_article_publication_metadata.html").Replace("2026-09-08T08:00:00Z","2026-01-01T08:00:00Z",StringComparison.Ordinal),
    ["old-dated-body"]="<article id=\"old\"><h2>Codex reset notice</h2><time datetime=\"2026-09-08T08:00:00Z\">Updated today</time><p>2026-01-01: Codex global reset for all paid plans was completed at 6 PM PT.</p></article>",
    ["cross-article"]="<article id=\"dated-other\"><h2>Routine release</h2><p>September 8, 2026</p><p>Routine improvements.</p></article><article id=\"undated-reset\"><h2>Codex reset notice</h2><p>Codex global reset for all paid plans will begin tomorrow around 6 PM PT.</p></article>"
   };
   foreach(var item in cases){var entries=p.Parse(item.Key,item.Value);using var radar=new ResetRadar(p.StateRoot(item.Key));radar.Apply(entries,Now);Assert(radar.UnreadAt(Now).Count==0,item.Key+" manufactured a fresh reset from unrelated/fetch/modified dates");Assert(entries.All(a=>!ActionHint(ResetRadar.SuggestionKey(a,43,2,false,Now,"pro"))),item.Key+" guessed actionable time");p.State("TRUST_"+item.Key,radar);}
  });
  Check("F02_legacy_unique_read_migration",p=>{
   var entries=p.Parse("current",Fixture("F01_article_publication_metadata.html"));string state=p.StateRoot();WriteLegacy(state,entries.Single(),false);p.Stages.Add(new{stage="OLD_SCHEMA_CACHE",state=JsonNode.Parse(File.ReadAllText(Path.Combine(state,"reset_radar.json")))});
   using(var radar=new ResetRadar(state)){radar.Apply(entries,Now);var found=radar.Snapshot().Events.Single(x=>x.OriginalId==entries.Single().OriginalId);Assert(found.EventId=="synthetic-legacy-read"&&found.ReadAt!=null,"unique canonical legacy read was discarded");Assert(radar.UnreadAt(Now).Count==0,"legacy read became fresh unread");p.State("MIGRATED",radar);}
   using var restart=new ResetRadar(state);restart.Apply(entries,Now.AddMinutes(1));Assert(restart.UnreadAt(Now).Count==0&&restart.Snapshot().Events.Count==1,"legacy restart repeated/multiplied notice");p.State("MIGRATED_RESTART",restart);
  });
  Check("F02_legacy_ambiguous_read_no_merge",p=>{
   var entries=p.Parse("two-current",Fixture("F02_distinct_same_day_articles.html"));string state=p.StateRoot();WriteLegacy(state,entries[0],true);using(var radar=new ResetRadar(state)){
    radar.Apply(entries,Now);var successors=radar.Snapshot().Events.Where(x=>entries.Any(n=>n.OriginalId==x.OriginalId)).ToList();Assert(successors.Count==2&&successors.Select(x=>x.EventId).Distinct().Count()==2,"ambiguous legacy migration merged modern source IDs");Assert(successors.All(x=>x.ReadAt!=null)&&radar.UnreadAt(Now).Count==0,"ambiguous old read caused duplicate new unread");p.State("AMBIGUOUS_MIGRATED",radar);
   }
   using var restart=new ResetRadar(state);restart.Apply(entries,Now.AddMinutes(1));Assert(restart.UnreadAt(Now).Count==0,"restart revived conservative legacy successors");p.State("AMBIGUOUS_RESTART",restart);
  });
  Check("F03_production_bilingual_log_evidence",p=>{
   using var service=new MonitorService(p.StateRoot("report"));var grants=p.Parse("F03-export",Fixture("F03_banked_credit_not_automatic_refill.html"));
   var automatic=p.Parse("automatic-export",Article("synthetic-auto-report","Codex global reset for all paid plans will begin at 2026-09-08T11:00:00Z."));Assert(grants.Count==1&&automatic.Count==1,"export fixture parser inputs missing");service.Radar.Apply(grants.Concat(automatic),Now);
   var rows=Refill(automatic.Single().ResetUtc!.Value,"report-auto",43);Assert(service.History.Save(rows),"synthetic history could not save");service.Radar.Correlate(rows);service.Radar.Read(grants.Single().EventId);var state=service.Radar.Snapshot();Assert(state.Correlations.Single().Cause=="UNKNOWN","export precondition overclaims cause");
   var filter=new HistoryFilter(Now.AddDays(-1),Now);var files=new List<object>();foreach(string language in new[]{"en-US","zh-TW"}){
    string file=ReportExporter.Export(service,filter,false,CancellationToken.None,Path.Combine(p.Directory,"synthetic-radar-"+language+".log"),language);string text=File.ReadAllText(file);Assert(text.StartsWith(ReportExporter.Prompt(language),StringComparison.Ordinal),"report did not embed selected bilingual prompt");
    var header=Data(file,"00_REPORT_HEADER").Single();Assert(header.GetProperty("synthetic_test_data").GetBoolean()&&header.GetProperty("report_language").GetString()==language,"synthetic/language report metadata missing");
    var announcements=Data(file,"17_ANNOUNCEMENT_EVENTS").Select(x=>x.GetProperty("announcement")).ToList();Assert(announcements.Count==2&&announcements.Any(x=>x.GetProperty("Effect").GetString()=="BANKED_CREDIT_GRANT")&&announcements.Any(x=>x.GetProperty("Effect").GetString()=="AUTOMATIC_QUOTA_RESET"),"report lost separate event effects");
    Assert(announcements.All(x=>!string.IsNullOrWhiteSpace(x.GetProperty("OriginalId").GetString())&&!string.IsNullOrWhiteSpace(x.GetProperty("PublicationBasis").GetString())),"report lost source identity/publication provenance");
    Assert(Data(file,"19_RESET_CORRELATION").Single().GetProperty("Cause").GetString()=="UNKNOWN","report promoted cause");Assert(Data(file,"21_READ_STATE").Count==2&&Data(file,"04_SAMPLES").Count==2,"report omitted read/observation records");VerifyIntegrity(file);files.Add(new{language,file=Path.GetFileName(file),sha256=Hash(File.ReadAllBytes(file)),section_count=22,events=announcements.Count,classification="SYNTHETIC"});
   }
   p.Stages.Add(new{stage="PRODUCTION_EXPORT",files,state});
  });
  Check("F01_header_last_updated_not_publication",p=>{
   string old="<article id=\"old\"><header><h2>Codex help</h2>Last updated <time datetime=\"2026-09-08T08:00:00Z\">September 8</time></header><p>January 1, 2026: We provided a Codex global reset for all users.</p></article>";
   // The relative variant isolates the misleading header from the separate old-body-date guard.
   foreach(var item in new[]{("old-body",old),("relative-body",old.Replace("January 1, 2026: We provided a Codex global reset for all users.","Codex global reset for all paid plans will begin tomorrow around 6 PM PT.",StringComparison.Ordinal))}){
    var entries=p.Parse(item.Item1,item.Item2);using var radar=new ResetRadar(p.StateRoot(item.Item1));radar.Apply(entries,Now);
    Assert(entries.All(a=>a.PublicationBasis!="ARTICLE_TIME"),"bare time under Last updated was classified as publication");Assert(radar.UnreadAt(Now).Count==0,"header update time revived an old or undated reset");
    Assert(entries.All(a=>!ActionHint(ResetRadar.SuggestionKey(a,43,2,false,Now,"pro"))),"header update time supplied an actionable relative anchor");p.State("HEADER_MODIFIED_"+item.Item1,radar);
   }
  });
  Check("F02_original_id_survives_changed_slug",p=>{
   string html=Fixture("F01_article_publication_metadata.html");var original=p.Parse("original-slug",html);Assert(original.Count==1,"original identity fixture missing");using var radar=new ResetRadar(p.StateRoot());radar.Apply(original,Now);
   var first=radar.Snapshot().Events.Single();radar.Read(first.EventId,first.RevisionId);var stamp=radar.Snapshot().Events.Single().ReadAt;
   string alternate=html.Replace("https://help.openai.com/en/articles/fixture-reset-alpha","https://help.openai.com/zh-TW/articles/translated-codex-reset-announcement",StringComparison.Ordinal);
   var translated=p.Parse("changed-nonnumeric-slug",alternate,url:ListUrl.Replace("/en/","/zh-TW/",StringComparison.Ordinal));Assert(translated.Count==1&&translated.Single().OriginalId==first.OriginalId,"source original ID lost on slug edit");
   Assert(translated.Single().EventId==first.EventId,"permalink slug participates in strong original-ID identity");radar.Apply(translated,Now.AddMinutes(1));var after=radar.Snapshot().Events.Single();
   Assert(after.EventId==first.EventId&&after.RevisionId==first.RevisionId&&after.ReadAt==stamp&&radar.UnreadAt(Now).Count==0,"changed translated slug duplicated event or discarded existing read");p.State("CHANGED_SLUG_PRESERVES_ORIGINAL",radar);
  });
  Check("F03_negated_credit_grant",p=>{
   string html="<article id=\"no-credit\"><header><time datetime=\"2026-09-08T08:00:00Z\">September 8</time></header><h2>Codex reset credits</h2><p>All paid plans will not receive one Codex banked reset credit tomorrow around 6 PM PT.</p></article>";
   var entries=p.Parse("negated-grant",html);using var radar=new ResetRadar(p.StateRoot());radar.Apply(entries,Now);var suggestions=new List<object>();
   foreach(var a in radar.Snapshot().Events){Assert(a.Effect!="BANKED_CREDIT_GRANT","negated receipt became affirmative credit grant");foreach(decimal value in new[]{43m,0m}){
     var key=ResetRadar.SuggestionKey(a,value,2,false,Now,"pro");Assert(key!="SuggestCreditGrant"&&!ActionHint(key),"negated grant produced affirmative grant or quota advice");suggestions.Add(new{remaining=value,key,en=ResetRadar.Suggest(a,value,2,false,Now,"pro","en-US"),zh=ResetRadar.Suggest(a,value,2,false,Now,"pro","zh-TW")});}
    radar.Correlate(Refill(a.ResetUtc??Now.AddDays(1),"negated-grant",43));Assert(radar.Snapshot().Correlations.All(c=>c.Correlation!="PROBABLE"&&c.Cause=="UNKNOWN"),"negated grant supplied probable reset evidence");}
   p.Stages.Add(new{stage="NEGATED_GRANT_SUGGESTIONS",suggestions});p.State("NEGATED_GRANT_APPLIED",radar);
  });
  AtomicJson.Save(Path.Combine(root,"radar-fix-tests.json"),new{classification="SYNTHETIC_PRODUCTION_PIPELINE",mission="CUM_R004_PUBLIC_RADAR_CORRECTNESS_FIX_R001",fixed_now_utc=Now,executed_utc=DateTimeOffset.UtcNow,exe_sha256=ReportExporter.ExeHash(),test_count=results.Count,failed=failures,results,scope="Production HTML Parse -> Apply -> deterministic unread/read/suggestion/correlation -> production bilingual ReportExporter. Native card rendering and real HTTP/producer smoke are separate evidence.",model_turns=0,network_requests=0,credit_actions=0});
  if(failures>0)Environment.ExitCode=1;
 }
 static bool ActionHint(string key)=>key is "SuggestPlannedWork" or "SuggestKeepReset";
 static string Article(string id,string body)=>"<!doctype html><!-- SYNTHETIC TEST INPUT, NOT OFFICIAL NEWS --><article id=\""+id+"\"><h2>Codex reset test notice</h2><time datetime=\"2026-09-08T08:00:00Z\">September 8, 2026</time><p>"+body+"</p></article>";
 static List<UsageSample> Refill(DateTimeOffset at,string id,decimal before){
  var reset=at.AddDays(7);
  UsageSample Row(int i,decimal remaining)=>new(){sample_id="SYNTHETIC-FIX-"+id+"-"+i,poll_id="SYNTHETIC-FIX-POLL-"+id+"-"+i,observed_at_utc=at.AddSeconds(i==0?-90:0),observed_at_local=at.AddSeconds(i==0?-90:0).ToLocalTime(),request_started_utc=at.AddSeconds(i==0?-91:-1),remaining_percent=remaining,used_percent_raw=100-remaining,limit_id="codex",source_slot="primary",window_duration_mins=10080,account_context_key="SYNTHETIC-FIX-ACCOUNT",source_generation="SYNTHETIC-FIX-SOURCE",comparison_key="SYNTHETIC-FIX-codex-weekly",context_assurance="STABLE_SCOPE",plan_type="pro",data_quality="VALID",polling_interval_seconds=90,reset_at_utc=reset,reset_at_unix_seconds=reset.ToUnixTimeSeconds(),reset_credits_available_count=2};
  return [Row(0,before),Row(1,100)];
 }
 static void WriteLegacy(string root,Announcement parsed,bool ambiguous){
  // Construct only the persisted old schema from a real production Parse result; do not use
  // manually built Announcements to bypass the production HTML path under review.
  var item=JsonSerializer.SerializeToNode(parsed)!.AsObject();foreach(string key in new[]{"Effect","OriginalId","PublicationBasis","MigrationProvenance"})item.Remove(key);
  item["EventId"]="synthetic-legacy-read";item["IdentityConfidence"]="LOW_STRUCTURED_FALLBACK";item["ReadAt"]=JsonValue.Create(Now.AddHours(-1));item["FirstSeen"]=JsonValue.Create(Now.AddHours(-2));
  if(ambiguous){item["Url"]=ListUrl;item["Scope"]="UNKNOWN";item["ResetUtc"]=null;item["MajorTime"]=null;}
  Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"reset_radar.json"),new JsonObject{["Events"]=new JsonArray(item),["Sources"]=new JsonArray(),["Correlations"]=new JsonArray()}.ToJsonString(new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false));
 }
 static List<JsonElement> Data(string file,string section)=>File.ReadLines(file).Where(x=>x.StartsWith("{",StringComparison.Ordinal)).Select(x=>JsonSerializer.Deserialize<JsonElement>(x)).Where(x=>x.TryGetProperty("section",out var s)&&s.GetString()==section&&x.GetProperty("record_type").GetString()=="DATA").Select(x=>x.GetProperty("data")).ToList();
 static void VerifyIntegrity(string file){
  var integrity=Data(file,"15_INTEGRITY").Single();var counts=integrity.GetProperty("section_record_counts");var hashes=integrity.GetProperty("section_data_sha256");Assert(counts.EnumerateObject().Count()==22,"stable report section count changed");
  foreach(var item in counts.EnumerateObject()){
   var lines=File.ReadLines(file).Where(x=>x.StartsWith("{",StringComparison.Ordinal)).Where(line=>{var r=JsonSerializer.Deserialize<JsonElement>(line);return r.TryGetProperty("section",out var s)&&s.GetString()==item.Name&&r.GetProperty("record_type").GetString()=="DATA";}).ToArray();Assert(lines.Length==item.Value.GetInt32(),"report count mismatch "+item.Name);
   if(item.Name!="15_INTEGRITY")Assert(Hash(string.Concat(lines.Select(x=>x+"\n")))==hashes.GetProperty(item.Name).GetString(),"report checksum mismatch "+item.Name);
  }
 }
}
