using System.Globalization;
using System.Security.Cryptography;
using System.Text;
namespace CodexUsageMonitor;

// Synthetic HTML only. This harness invokes the shipped Parse -> Apply -> Suggestion path;
// it does not poll official pages, start the Codex producer, or use an account/reset credit.
internal static class FinalPhaseTests {
 static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-09-08T12:00:00Z",CultureInfo.InvariantCulture);
 const string Automatic="AUTOMATIC_QUOTA_RESET",Grant="BANKED_CREDIT_GRANT";
 public static List<object> Run(string root){
  Directory.CreateDirectory(root);var results=new List<object>();int failures=0;
  var cases=new (string Id,string Body,string Phase,string Effect)[]{
   ("A_future_applied","Codex global reset for all paid plans will be applied tomorrow around 6 PM PT.","PLANNED",Automatic),
   ("A_future_provided","Codex global reset for all paid plans will be provided tomorrow around 6 PM PT.","PLANNED",Automatic),
   ("A_future_grant","One Codex banked reset credit for all paid plans will be provided tomorrow around 6 PM PT.","PLANNED",Grant),
   ("B_actual_applied","Codex global reset for all paid plans has been applied today at 11 AM UTC.","COMPLETED",Automatic),
   ("B_was_applied","Codex global reset for all paid plans was applied today at 11 AM UTC.","COMPLETED",Automatic),
   ("B_has_completed","Codex global reset for all paid plans has completed today at 11 AM UTC.","COMPLETED",Automatic),
   ("B_completed","Codex global reset for all paid plans completed today at 11 AM UTC.","COMPLETED",Automatic),
   ("B_took_place","Codex global reset took place today at 11 AM UTC for all paid plans.","COMPLETED",Automatic),
   ("B_reset_provided","Codex global reset for all paid plans has been provided today at 11 AM UTC.","COMPLETED",Automatic),
   ("B_grant_provided","One Codex banked reset credit for all paid plans has been provided today at 11 AM UTC.","COMPLETED",Grant),
   ("B_grant_was_provided","One Codex banked reset credit for all paid plans was provided today at 11 AM UTC.","COMPLETED",Grant),
   ("C_has_begun","Codex global reset for all paid plans has begun today at 11 AM UTC.","STARTED",Automatic),
   ("C_started","Codex global reset for all paid plans started today at 11 AM UTC.","STARTED",Automatic),
   ("C_underway","Codex global reset for all paid plans is underway today at 11 AM UTC.","STARTED",Automatic),
   ("P_will_reset","Codex usage quota will reset tomorrow around 6 PM PT for all paid plans.","PLANNED",Automatic),
   ("P_will_begin","Codex global reset for all paid plans will begin tomorrow around 6 PM PT.","PLANNED",Automatic),
   ("P_scheduled","Codex global reset for all paid plans is scheduled for tomorrow around 6 PM PT.","PLANNED",Automatic),
   ("P_planned","Codex global reset for all paid plans is planned for tomorrow around 6 PM PT.","PLANNED",Automatic),
   ("P_upcoming","An upcoming Codex global reset for all paid plans is announced for tomorrow around 6 PM PT.","PLANNED",Automatic),
   ("P_future_timestamp","Codex global reset for all paid plans: tomorrow around 6 PM PT.","PLANNED",Automatic),
   ("P_future_completed","Codex global reset for all paid plans will be completed tomorrow around 6 PM PT.","PLANNED",Automatic),
   ("P_future_perfect","Codex global reset for all paid plans will have been applied tomorrow around 6 PM PT.","PLANNED",Automatic),
   ("P_future_perfect_grant","One Codex banked reset credit for all paid plans will have been provided tomorrow around 6 PM PT.","PLANNED",Grant),
   ("P_is_to_completed","Codex global reset for all paid plans is to be completed tomorrow around 6 PM PT.","PLANNED",Automatic),
   ("P_due_to_completed","Codex global reset for all paid plans is due to be completed tomorrow around 6 PM PT.","PLANNED",Automatic),
   ("P_expected_completed","Codex global reset for all paid plans is expected to be completed tomorrow around 6 PM PT.","PLANNED",Automatic),
   ("P_conditional_completion","Once the Codex global reset for all paid plans has completed tomorrow around 6 PM PT, the quota can be checked.","PLANNED",Automatic),
   ("N_not_completed","Codex global reset for all paid plans has not completed; it will begin tomorrow around 6 PM PT.","PLANNED",Automatic),
   ("N_never_completed","Codex global reset for all paid plans never completed; it will begin tomorrow around 6 PM PT.","PLANNED",Automatic),
   ("N_not_applied","Codex global reset for all paid plans has not been applied; it will begin tomorrow around 6 PM PT.","PLANNED",Automatic),
   ("N_not_started","Codex global reset for all paid plans has not started; it will begin tomorrow around 6 PM PT.","PLANNED",Automatic),
   ("N_bare_applied","Codex global reset for all paid plans: applied settings are documented; it will begin tomorrow around 6 PM PT.","PLANNED",Automatic),
   ("N_bare_provided","Codex global reset for all paid plans: provided guidance is informational; it will begin tomorrow around 6 PM PT.","PLANNED",Automatic),
   ("N_unrelated_instruction","Instructions have been provided for a Codex global reset for all paid plans that will begin tomorrow around 6 PM PT.","PLANNED",Automatic),
   ("C_started_future_completion","Codex global reset for all paid plans has begun; it will be completed tomorrow around 6 PM PT.","STARTED",Automatic),
   ("C_cancelled_preserved","Codex global reset for all paid plans planned for tomorrow around 6 PM PT has been cancelled.","CANCELLED",Automatic)
  };
  foreach(var item in cases){
   string html="<article id=\"phase-"+item.Id+"\"><header><h2>Codex reset phase notice</h2><time datetime=\"2026-09-08T08:00:00Z\">September 8, 2026</time></header><p>"+item.Body+"</p></article>";
   string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html))).ToLowerInvariant();
   string status="PASS",error="";Announcement? parsed=null,persisted=null;string suggestion="";int candidates=0;
   try{
    var entries=AnnouncementParser.Parse("openai-help-resets","https://help.openai.com/en/articles/synthetic-final-phase",html,Now,out candidates);
    if(entries.Count!=1)throw new InvalidOperationException("Expected exactly one production-parsed notice; got "+entries.Count);
    parsed=entries.Single();
    if(parsed.Phase!=item.Phase)throw new InvalidOperationException("Expected phase "+item.Phase+"; got "+parsed.Phase);
    if(parsed.Effect!=item.Effect)throw new InvalidOperationException("Effect changed: expected "+item.Effect+"; got "+parsed.Effect);
    using var radar=new ResetRadar(Path.Combine(root,item.Id+"-"+Guid.NewGuid().ToString("N")[..8]));
    radar.Apply(entries,Now);persisted=radar.Snapshot().Events.Single();
    if(persisted.Phase!=item.Phase)throw new InvalidOperationException("Apply changed parsed phase");
    suggestion=ResetRadar.SuggestionKey(persisted,43,2,false,Now,"pro");
    if(item.Phase=="PLANNED"&&suggestion=="SuggestCompleted")throw new InvalidOperationException("A planned notice generated completed advice");
    if(item.Id=="A_future_applied"&&suggestion!="SuggestPlannedWork")throw new InvalidOperationException("Explicit future automatic reset lost conditional planned-work advice");
    if(item.Effect==Grant&&(suggestion=="SuggestPlannedWork"||suggestion=="SuggestKeepReset"||suggestion=="SuggestCompleted"))throw new InvalidOperationException("Credit grant generated automatic-reset advice");
   }catch(Exception e){status="FAIL";error=e.GetType().Name+": "+e.Message;failures++;}
   results.Add(new{id=item.Id,result=status,classification="SYNTHETIC_PRODUCTION_HTML_PIPELINE",fixed_now_utc=Now,html,html_sha256=hash,candidate_articles=candidates,expected_phase=item.Phase,expected_effect=item.Effect,parsed,persisted,suggestion,error});
  }
  AtomicJson.Save(Path.Combine(root,"final-phase-tests.json"),new{classification="SYNTHETIC_PRODUCTION_HTML_PIPELINE",fixed_now_utc=Now,executed_utc=DateTimeOffset.UtcNow,exe_sha256=ReportExporter.ExeHash(),total=results.Count,failures,results});
  return results;
 }
}
