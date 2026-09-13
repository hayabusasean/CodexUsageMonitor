namespace CodexUsageMonitor;

// Derived evidence only. This store never edits raw samples or external announcement phases.
internal sealed record LocalQuotaCycleEvent {
 public string id{get;init;}=""; public string stream_key{get;init;}="";
 public string scope_key{get;init;}="";public string plan{get;init;}="";
 public string evidence_assurance{get;init;}="INSUFFICIENT";
 public string before_sample_id{get;init;}="";public string after_sample_id{get;init;}="";
 public DateTimeOffset observed_from{get;init;} public DateTimeOffset observed_to{get;init;}
 public decimal? remaining_before{get;init;} public decimal? remaining_after{get;init;} public decimal? delta_pp{get;init;}
 public DateTimeOffset? reset_at_before{get;init;} public DateTimeOffset? reset_at_after{get;init;}
 public int? banked_before{get;init;}public int? banked_after{get;init;}
 public string kind{get;init;}="UNVERIFIED_CHANGE"; public string schedule_relation{get;init;}="UNKNOWN";
 public string cause{get;init;}="UNKNOWN"; public string credit_relation{get;init;}="UNKNOWN";
 public string? confirmation_sample_id{get;init;} public DateTimeOffset? confirmed_at{get;init;}
 public DateTimeOffset first_processed_at{get;init;} public DateTimeOffset? retracted_at{get;init;}public string? retracted_reason{get;init;}
 public string[] related_signal_revisions{get;init;}=[];
 public bool Confirmed=>kind=="FULL_REPLENISHMENT_WITH_NEW_CYCLE"&&confirmation_sample_id!=null&&retracted_at==null;
 public bool Full=>remaining_after==100&&remaining_before<100&&retracted_at==null&&evidence_assurance!="INSUFFICIENT";
}
internal sealed record RadarAttentionDisposition(string event_id,string revision_id,string local_cycle_event_id,string status,DateTimeOffset timestamp,string evidence_assurance,string scope_key="");
internal sealed class LocalQuotaCycleState {
 public int schema_version{get;set;}=1;
 public List<LocalQuotaCycleEvent> events{get;set;}=[];
 public List<RadarAttentionDisposition> dispositions{get;set;}=[];
}
internal static class LocalQuotaCycle {
 // Public producer reset timestamps can settle slightly after a replenishment.
 // This tolerance only confirms an already >10-minute advanced cycle; raw times
 // remain exact and different accounts, gaps and producer generations still fail.
 internal static bool SameResetWindow(DateTimeOffset? a,DateTimeOffset? b)=>a.HasValue&&b.HasValue&&Math.Abs((a.Value-b.Value).TotalSeconds)<=120;
 internal static string Scope(UsageSample x)=>Safe.Hash(x.account_context_key+"|"+x.plan_type+"|"+x.limit_id+"|"+x.window_duration_mins+"|"+x.source_slot);
 internal static bool Continuous(UsageSample p,UsageSample c){
  double seconds=(c.observed_at_utc-p.observed_at_utc).TotalSeconds;
  return DailyUsageCalculator.Valid(p)&&DailyUsageCalculator.Valid(c)&&!c.had_gap&&seconds>0&&seconds<=Math.Max(300,3*c.polling_interval_seconds)
   &&!string.IsNullOrEmpty(p.comparison_key)&&p.comparison_key==c.comparison_key
   &&!string.IsNullOrEmpty(p.account_context_key)&&p.account_context_key==c.account_context_key
   &&!string.IsNullOrEmpty(p.source_generation)&&p.source_generation==c.source_generation
   &&p.plan_type==c.plan_type&&p.source_slot==c.source_slot&&p.limit_id==c.limit_id&&p.window_duration_mins==c.window_duration_mins;
 }
 internal static string Schedule(UsageSample p,UsageSample c,bool continuous){
  if(!continuous||p.reset_at_utc==null)return "UNKNOWN";
  var t=p.reset_at_utc.Value;
  if(t>=p.observed_at_utc.AddMinutes(-10)&&t<=c.observed_at_utc.AddMinutes(10))return "AT_SCHEDULE_WINDOW";
  return c.observed_at_utc<t.AddMinutes(-10)?"BEFORE_SCHEDULE_WINDOW":"UNKNOWN";
 }
 internal static List<LocalQuotaCycleEvent> Derive(IEnumerable<UsageSample> input,DateTimeOffset processed){
  var result=new List<LocalQuotaCycleEvent>();
  foreach(var stream in input.Where(x=>x.limit_id=="codex"&&x.window_duration_mins==10080).GroupBy(Scope)){
  var rows=stream.DistinctBy(x=>x.sample_id).OrderBy(x=>x.observed_at_utc).ToArray();
  for(int i=1;i<rows.Length;i++){
   var p=rows[i-1];var c=rows[i];
   if(!DailyUsageCalculator.Valid(p)||!DailyUsageCalculator.Valid(c))continue;
   bool rise=c.remaining_percent>p.remaining_percent,metadata=c.reset_at_utc!=p.reset_at_utc;
   if(!rise&&!metadata)continue;
   bool continuous=Continuous(p,c),full=rise&&c.remaining_percent==100;
   bool newCycle=continuous&&p.reset_at_utc!=null&&c.reset_at_utc>p.reset_at_utc.Value.AddMinutes(10)&&c.reset_at_utc>c.observed_at_utc&&c.reset_at_utc<=c.observed_at_utc.AddMinutes(10080+10);
   string assurance=continuous?(p.context_assurance=="STABLE_SCOPE"&&c.context_assurance=="STABLE_SCOPE"?"STABLE_SCOPE":"LOCAL_STREAM_CONTINUITY_ONLY"):"INSUFFICIENT";
   string kind=!continuous?"UNVERIFIED_CHANGE":full?(newCycle?"FULL_REPLENISHMENT_WITH_NEW_CYCLE":"FULL_REPLENISHMENT_OBSERVED"):rise?"PARTIAL_INCREASE":"CYCLE_CHANGE_ONLY";
   UsageSample? confirmation=null;DateTimeOffset? retracted=null;string? reason=null;
   if(full&&newCycle){for(int j=i+1;j<rows.Length;j++){var next=rows[j];
    if(!Continuous(rows[j-1],next))break;
    if(SameResetWindow(next.reset_at_utc,p.reset_at_utc)){retracted=next.observed_at_utc;reason="RETURNED_TO_PREVIOUS_CYCLE";confirmation=null;break;}
    if(!SameResetWindow(next.reset_at_utc,c.reset_at_utc))break;
    confirmation??=next;
   }
   }
   result.Add(new(){id=Safe.Hash("local-cycle-v1|"+p.sample_id+"|"+c.sample_id),scope_key=Scope(c),plan=c.plan_type,stream_key=Safe.Hash(c.comparison_key+"|"+c.source_generation),evidence_assurance=assurance,
    before_sample_id=p.sample_id,after_sample_id=c.sample_id,observed_from=p.observed_at_utc,observed_to=c.observed_at_utc,
    remaining_before=p.remaining_percent,remaining_after=c.remaining_percent,delta_pp=c.remaining_percent-p.remaining_percent,
    reset_at_before=p.reset_at_utc,reset_at_after=c.reset_at_utc,banked_before=p.reset_credits_available_count,banked_after=c.reset_credits_available_count,
    kind=kind,schedule_relation=Schedule(p,c,continuous),credit_relation=p.reset_credits_available_count.HasValue&&c.reset_credits_available_count.HasValue&&c.reset_credits_available_count<p.reset_credits_available_count?"POSSIBLE_BANKED_RESET_ASSOCIATION":"UNKNOWN",
    confirmation_sample_id=confirmation?.sample_id,confirmed_at=confirmation?.observed_at_utc,first_processed_at=processed,retracted_at=retracted,retracted_reason=reason});
  }
  }
  return result;
 }
 internal static bool Related(Announcement a,LocalQuotaCycleEvent e,RadarState radar){
  if(!e.Confirmed||a.SignalLevel is not ("WATCH" or "INCOMING")||a.Effect.Contains("BANKED",StringComparison.OrdinalIgnoreCase)||a.ResetType.Contains("BANKED",StringComparison.OrdinalIgnoreCase))return false;
  if(a.FirstSeen>e.observed_from||a.PublishedAt>e.observed_from||a.ResetUtc>e.observed_to)return false;
  if(a.Scope is not ("UNKNOWN" or "ALL")&&!ResetRadar.ScopeMatches(a,e.plan))return false;
  // Bind to the obtained semantic revision, not relay LastSeen. New revisions retain attention.
  var revision=radar.SignalHistory.Where(x=>x.EventId==a.EventId&&x.RevisionId==a.RevisionId).OrderBy(x=>x.Timestamp).FirstOrDefault();
  var changed=radar.Transitions.Where(x=>x.EventId==a.EventId&&x.RevisionId==a.RevisionId).Select(x=>(DateTimeOffset?)x.Timestamp).Max();
  return (revision?.Timestamp??a.FirstSeen)<=e.observed_from&&(changed==null||changed<=e.observed_from);
 }
}
internal sealed class LocalQuotaCycleStore {
 readonly string path;readonly object gate=new();LocalQuotaCycleState state;
 public string Diagnostic{get;private set;}="";
 public LocalQuotaCycleStore(string root){path=Path.Combine(root,"local_quota_cycle_state.v1.json");state=AtomicJson.Load(path,()=>new LocalQuotaCycleState());}
 public LocalQuotaCycleState Snapshot(){lock(gate)return new(){events=state.events.ToList(),dispositions=state.dispositions.ToList()};}
 public bool Rebuild(IEnumerable<UsageSample> input,RadarState radar,DateTimeOffset now){
  lock(gate){
   var derived=LocalQuotaCycle.Derive(input,now);var old=state.events.ToDictionary(x=>x.id);
   var merged=state.events.ToDictionary(x=>x.id);
   foreach(var e in derived)merged[e.id]=old.TryGetValue(e.id,out var prior)?e with{first_processed_at=prior.first_processed_at}:e;
   var events=merged.Values.OrderBy(x=>x.observed_to).TakeLast(2000).ToList();
   var dispositions=new List<RadarAttentionDisposition>();
   foreach(var e in events.Where(x=>x.Confirmed))foreach(var a in radar.Events.Where(a=>LocalQuotaCycle.Related(a,e,radar)))
    dispositions.Add(new(a.EventId,a.RevisionId,e.id,"SATISFIED_BY_LOCAL_CYCLE",e.confirmed_at!.Value,e.evidence_assurance,e.scope_key));
   dispositions=dispositions.OrderBy(d=>d.timestamp).ThenBy(d=>d.local_cycle_event_id,StringComparer.Ordinal).DistinctBy(d=>(d.event_id,d.revision_id,d.scope_key)).ToList();
   var next=new LocalQuotaCycleState{events=events.Select(e=>e with{related_signal_revisions=dispositions.Where(d=>d.local_cycle_event_id==e.id).Select(d=>d.event_id+"/"+d.revision_id).ToArray()}).ToList(),dispositions=dispositions};
   try{AtomicJson.Save(path,next);state=next;Diagnostic="";return true;}
   catch{Diagnostic="LOCAL_CYCLE_STATE_NOT_SAVED";return false;}
  }
 }
}
