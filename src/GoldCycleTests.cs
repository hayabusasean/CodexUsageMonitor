using System.Text.Json;
namespace CodexUsageMonitor;
internal static class GoldCycleTests {
 internal static void CheckReal(string output){
  string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexUsageMonitor","history","usage_2026-09-12.csv");
  string Hash()=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
  var before=Hash();var rows=Csv.Records<UsageSample>(file).ToArray();var e=LocalQuotaCycle.Derive(rows,DateTimeOffset.UtcNow).Single(x=>x.remaining_before==7&&x.remaining_after==100);
  bool unchanged=before==Hash();AtomicJson.Save(output,new{classification="ACTUAL_EXISTING_HISTORY_READ_ONLY",exe_sha256=ReportExporter.ExeHash(),e.observed_from,e.observed_to,e.remaining_before,e.remaining_after,e.delta_pp,e.kind,e.evidence_assurance,e.schedule_relation,e.cause,e.banked_before,e.banked_after,confirmed=e.Confirmed,history_unchanged=unchanged,private_identity_fields_omitted=true});
  Environment.ExitCode=unchanged&&e.Confirmed&&e.cause=="UNKNOWN"?0:1;
 }
 internal static void Run(string root){
  Directory.CreateDirectory(root);var results=new List<object>();int failures=0;
  void Check(string id,Func<bool> test){try{bool ok=test();results.Add(new{id,pass=ok});if(!ok)failures++;}catch(Exception e){failures++;results.Add(new{id,pass=false,error=e.GetType().Name});}}
  var now=DateTimeOffset.UtcNow;var p=Rc4HistoryEventTests.Sample("before",now.AddSeconds(-90),7,reset:now.AddDays(3));var c=Rc4HistoryEventTests.Sample("after",now,100,reset:now.AddDays(7));var n=Rc4HistoryEventTests.Sample("confirm",now.AddSeconds(90),99,reset:now.AddDays(7));
  LocalQuotaCycleEvent Make(params UsageSample[] x)=>LocalQuotaCycle.Derive(x,now).First();
  var full=Make(p,c,n);
  Check("E01_confirmed_new_cycle",()=>full.Confirmed&&full.delta_pp==93&&full.cause=="UNKNOWN"&&full.schedule_relation=="BEFORE_SCHEDULE_WINDOW");
  Check("E02_stable_identity",()=>Make(p,c,n).id==full.id);
  Check("pending_first_100",()=>!Make(p,c).Confirmed);
  Check("E08_partial",()=>Make(p,c with{remaining_percent=95,used_percent_raw=5},n).kind=="PARTIAL_INCREASE");
  Check("E09_metadata_only",()=>Make(p with{remaining_percent=91,used_percent_raw=9},c with{remaining_percent=91,used_percent_raw=9}).kind=="CYCLE_CHANGE_ONLY");
  Check("E10_transient_retract",()=>Make(p,c,n with{remaining_percent=7,used_percent_raw=93,reset_at_utc=p.reset_at_utc}).retracted_reason=="RETURNED_TO_PREVIOUS_CYCLE");
  Check("E11_confirm_99",()=>full.confirmation_sample_id==n.sample_id);
  Check("E11_confirm_91",()=>Make(p,c,n with{remaining_percent=91,used_percent_raw=9}).Confirmed);
  Check("producer_reset_settles_25_seconds",()=>Make(p,c,n with{reset_at_utc=c.reset_at_utc!.Value.AddSeconds(25)}).Confirmed);
  Check("different_reset_window_not_confirmation",()=>!Make(p,c,n with{reset_at_utc=c.reset_at_utc!.Value.AddMinutes(3)}).Confirmed);
  Check("old_cycle_jitter_retracts",()=>Make(p,c,n with{reset_at_utc=p.reset_at_utc!.Value.AddSeconds(25)}).retracted_reason=="RETURNED_TO_PREVIOUS_CYCLE");
  Check("E12_gap",()=>!Make(p,c with{had_gap=true},n).Confirmed);
  Check("E07_other_account",()=>!LocalQuotaCycle.Derive([p,c with{account_context_key="different"},n],now).Any(x=>x.Confirmed));
  Check("E07_other_plan",()=>!LocalQuotaCycle.Derive([p,c with{plan_type="different"},n],now).Any(x=>x.Confirmed));
  Check("reset_jitter_no_cycle",()=>!Make(p,c with{reset_at_utc=p.reset_at_utc!.Value.AddSeconds(1)},n with{reset_at_utc=p.reset_at_utc!.Value.AddSeconds(1)}).Confirmed);
  Check("interleaved_streams",()=>LocalQuotaCycle.Derive([p,p with{sample_id="B1",account_context_key="B",observed_at_utc=p.observed_at_utc.AddSeconds(1)},c,c with{sample_id="B2",account_context_key="B",observed_at_utc=c.observed_at_utc.AddSeconds(1)},n],now).Any(x=>x.id==full.id&&x.Confirmed));
  Check("late_retraction",()=>!Make(p,c,n,n with{sample_id="revert",observed_at_utc=n.observed_at_utc.AddSeconds(90),reset_at_utc=p.reset_at_utc}).Confirmed);
  Check("E13_missing_reset",()=>!Make(p,c with{reset_at_utc=null},n).Confirmed);
  Check("E14_at_schedule",()=>Make(p with{reset_at_utc=now},c,n).schedule_relation=="AT_SCHEDULE_WINDOW");
  Check("E14_unknown_schedule",()=>Make(p with{reset_at_utc=null},c,n).schedule_relation=="UNKNOWN");
  Check("E15_credit_drop",()=>Make(p,c with{reset_credits_available_count=0},n).credit_relation=="POSSIBLE_BANKED_RESET_ASSOCIATION");
  Check("local_continuity_limited",()=>Make(p with{context_assurance="SCOPE_NOT_FULLY_VERIFIED"},c with{context_assurance="SCOPE_NOT_FULLY_VERIFIED"},n).evidence_assurance=="LOCAL_STREAM_CONTINUITY_ONLY");
  var radar=Rc4HistoryEventTests.RadarAt(now);var a=radar.Events.First();
  a=a with{FirstSeen=now.AddHours(-2),PublishedAt=now.AddHours(-2),ResetUtc=null,SignalLevel="WATCH",Phase="WATCH",Effect="UNKNOWN"};radar.Events=[a];radar.SignalHistory=[];radar.Transitions=[];
  Check("E03_old_revision_related",()=>LocalQuotaCycle.Related(a,full,radar));
  Check("E04_future_incoming",()=>!LocalQuotaCycle.Related(a with{ResetUtc=now.AddHours(5),SignalLevel="INCOMING"},full,radar));
  Check("E04_new_announcement",()=>!LocalQuotaCycle.Related(a with{FirstSeen=now.AddMinutes(2)},full,radar));
  Check("banked_grant_not_retired",()=>!LocalQuotaCycle.Related(a with{Effect="BANKED_CREDIT_GRANT"},full,radar));
  Check("E05_relay_lastseen",()=>LocalQuotaCycle.Related(a with{LastSeen=now.AddMinutes(4)},full,radar));
  Check("E06_major_revision",()=>{radar.Transitions.Add(new(a.EventId,a.RevisionId,now.AddMinutes(2),"WATCH","INCOMING","major"));bool result=!LocalQuotaCycle.Related(a,full,radar);radar.Transitions.Clear();return result;});
  var store=new LocalQuotaCycleStore(root);Check("E17_persistence",()=>{store.Rebuild([p,c,n],radar,now);var reopened=new LocalQuotaCycleStore(root);return reopened.Snapshot().events.Single().Confirmed&&reopened.Snapshot().dispositions.Count==1;});
  Check("E02_rebuild_idempotent",()=>{store.Rebuild([p,c,n],radar,now.AddMinutes(5));return store.Snapshot().events.Count==1&&store.Snapshot().events[0].first_processed_at==now;});
  Check("E03_read_and_phase_preserved",()=>radar.Events[0].ReadAt==a.ReadAt&&radar.Events[0].Phase==a.Phase);
  Check("A_confirmed_then_B_scope",()=>{var saved=store.Snapshot();var snap=new RadarState{LocalCycleEvents=saved.events,AttentionDispositions=saved.dispositions,CurrentLocalScopes=[LocalQuotaCycle.Scope(p)]};bool same=ResetRadar.LocallySatisfied(a,snap,now.AddMinutes(10));snap.CurrentLocalScopes=[LocalQuotaCycle.Scope(p with{account_context_key="B"})];return same&&!ResetRadar.LocallySatisfied(a,snap,now.AddMinutes(10));});
  Check("two_scopes_persist_independently",()=>{var dualRoot=Path.Combine(root,"dual");var dual=new LocalQuotaCycleStore(dualRoot);var bp=p with{sample_id="B-before",account_context_key="B"};var bc=c with{sample_id="B-after",account_context_key="B"};var bn=n with{sample_id="B-confirm",account_context_key="B"};dual.Rebuild([p,c,n,bp,bc,bn],radar,now);var saved=new LocalQuotaCycleStore(dualRoot).Snapshot();return saved.dispositions.Count==2&&new[]{LocalQuotaCycle.Scope(p),LocalQuotaCycle.Scope(bp)}.All(scope=>ResetRadar.LocallySatisfied(a,new(){LocalCycleEvents=saved.events,AttentionDispositions=saved.dispositions,CurrentLocalScopes=[scope]},now.AddMinutes(10)));});
  Check("monitor_cached_scope_survives_revalidation",()=>{string cachedRoot=Path.Combine(root,"cached");Directory.CreateDirectory(cachedRoot);var snapshot=new Snapshot(now,now,0,p.account_context_key,p.context_assurance,p.source_generation,"test",[new WindowQuota("codex","Codex",p.source_slot,10080,1,c.reset_at_utc!.Value.ToUnixTimeSeconds(),"VALID",p.plan_type,null,null,null,"")],1,[]);AtomicJson.Save(Path.Combine(cachedRoot,"last_good.json"),snapshot);using var service=new MonitorService(cachedRoot);service.LocalCycles.Rebuild([p,c,n],radar,now);bool cached=!service.Verified&&ResetRadar.LocallySatisfied(a,service.Radar.Snapshot(),now.AddMinutes(10));service.Resume();return cached&&!service.Verified&&ResetRadar.LocallySatisfied(a,service.Radar.Snapshot(),now.AddMinutes(10));});
  Check("gap_not_gold_full_evidence",()=>!Make(p,c with{had_gap=true},n).Full);
  Check("known_eligibility_mismatch",()=>!LocalQuotaCycle.Related(a with{Scope="ENTERPRISE",Eligibility="AS_ANNOUNCED"},full,radar));
  Check("E18_save_failure",()=>{string bad=Path.Combine(root,"blocked");Directory.CreateDirectory(bad);Directory.CreateDirectory(Path.Combine(bad,"local_quota_cycle_state.v1.json"));var broken=new LocalQuotaCycleStore(bad);return !broken.Rebuild([p,c,n],radar,now)&&broken.Diagnostic=="LOCAL_CYCLE_STATE_NOT_SAVED"&&broken.Snapshot().dispositions.Count==0;});
  Check("V04_short_zoom",()=>{using var v=new TrendView{Latest=false,Rows=[p,c with{observed_at_utc=p.observed_at_utc.AddSeconds(10)}]};v.ZoomFullAt(.5,true);return true;});
  Check("multiple_gold_markers_narrow_and_wide",()=>{using var v=new TrendView{Latest=false,Rows=[p,c,n],GoldEvents=[full,full with{id="second",observed_to=now.AddSeconds(10)},full with{id="third",observed_to=now.AddSeconds(20)},full with{id="fourth",observed_to=now.AddSeconds(30)}]};using var bitmap=new Bitmap(1100,300);using var g=Graphics.FromImage(bitmap);v.DrawGoldEvents(g,40,480,30,240);v.DrawGoldEvents(g,40,1060,30,240);return true;});
  AtomicJson.Save(Path.Combine(root,"gold-cycle-results.json"),new{synthetic=true,checks=results,failures});Environment.ExitCode=failures==0?0:1;
 }
}
