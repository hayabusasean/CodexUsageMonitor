namespace CodexUsageMonitor;

// Derived values live outside UsageSample so the schema-1 CSV header remains unchanged.
internal static class QuotaWindow {
 internal const int ResetToleranceSeconds=120;
 internal static bool IdentityComparable(string assurance)=>assurance is "STABLE_SCOPE" or "ACCOUNT_STABLE" or "EMAIL_SCOPED";
 internal static string ComparisonAssurance(string a,string b)=>!IdentityComparable(a)||!IdentityComparable(b)?"SCOPE_NOT_FULLY_VERIFIED":a=="EMAIL_SCOPED"||b=="EMAIL_SCOPED"?"EMAIL_SCOPED":a=="ACCOUNT_STABLE"||b=="ACCOUNT_STABLE"?"ACCOUNT_STABLE":"STABLE_SCOPE";
 internal static bool SourceComparable(UsageSample p,UsageSample c)=>p.source_generation==c.source_generation||SameScope(p,c)&&IdentityComparable(p.context_assurance)&&IdentityComparable(c.context_assurance);
 internal static List<UsageSample> AnalysisSamples(IEnumerable<UsageSample> rows,Snapshot? current,bool verified){
  string? key=verified&&current?.Assurance=="EMAIL_SCOPED"&&current.AccountKey is {Length:>0}?current.AccountKey:null;
  return rows.Select(row=>key!=null&&row.account_context_key==key&&(row.context_assurance is "CONNECTION_ONLY" or "SCOPE_NOT_FULLY_VERIFIED")?row with{context_assurance="EMAIL_SCOPED"}:row).ToList();
 }
 internal static DateTimeOffset? ResetAt(UsageSample sample){
  if(sample.reset_at_utc is DateTimeOffset reset)return reset;
  try{return sample.reset_at_unix_seconds is long seconds?DateTimeOffset.FromUnixTimeSeconds(seconds):null;}catch(ArgumentOutOfRangeException){return null;}
 }
 internal static bool SameReset(DateTimeOffset? a,DateTimeOffset? b)=>a is null||b is null?a==b:Math.Abs((a.Value-b.Value).TotalSeconds)<=ResetToleranceSeconds;
 internal static bool DefinitelyBeforeReset(DateTimeOffset observed,DateTimeOffset reset)=>(reset-observed).TotalSeconds>ResetToleranceSeconds;
 internal static bool ResetInObservationWindow(DateTimeOffset reset,DateTimeOffset from,DateTimeOffset to)=>(reset-from).TotalSeconds>=-ResetToleranceSeconds&&(reset-to).TotalSeconds<=ResetToleranceSeconds;
 internal static string State(UsageSample sample){
  if(!DailyUsageCalculator.Valid(sample)||sample.window_duration_mins is not >0||ResetAt(sample) is not DateTimeOffset reset)return "UNKNOWN";
  return sample.remaining_percent==100&&Math.Abs((reset-sample.observed_at_utc.AddMinutes(sample.window_duration_mins.Value)).TotalSeconds)<=ResetToleranceSeconds?"WINDOW_NOT_STARTED":"ACTIVE";
 }
 internal static DateTimeOffset? StartedAt(UsageSample sample)=>State(sample)=="ACTIVE"&&ResetAt(sample) is DateTimeOffset reset?reset.AddMinutes(-sample.window_duration_mins!.Value):null;
 internal static bool SameScope(UsageSample p,UsageSample c)=>p.account_context_key.Length>0&&p.account_context_key==c.account_context_key&&p.comparison_key==c.comparison_key&&p.plan_type==c.plan_type&&p.limit_id==c.limit_id&&p.window_duration_mins==c.window_duration_mins&&p.source_slot==c.source_slot;
}

internal sealed record ForecastProjection(DateTimeOffset Deadline,double HoursToReset,double? ProjectedUsedAtReset,DateTimeOffset? ExhaustAt,double NeededRateToFinish);
internal sealed record BurnForecastResult(UsageSample? Sample,string WindowState,DateTimeOffset? WindowStartedAt,
 double? Rate30m,double? Rate2h,double? RateSession,DateTimeOffset? LastStepAt,int? IdleMinutes,string Confidence,
 ForecastProjection? Weekly,ForecastProjection? Announced,Announcement? Announcement){
 public double? Rate=>Rate2h??Rate30m??RateSession;
 public string Basis=>"STEP_TIMES";
}

internal static class BurnForecast {
 internal const string WindowRateFormula="STEP_COUNT_OVER_OBSERVED_WINDOW";
 sealed record Step(DateTimeOffset At,double Delta);
 internal static BurnForecastResult Calculate(IEnumerable<UsageSample> input,DateTimeOffset now,Announcement? announced=null){
  var rows=input.Where(s=>s.limit_id=="codex"&&s.window_duration_mins==10080&&s.observed_at_utc<=now).DistinctBy(s=>s.sample_id).OrderBy(s=>s.observed_at_utc).ToArray();
  if(rows.Length==0)return new(null,"UNKNOWN",null,null,null,null,null,null,"LOW_DATA",null,null,null);
  var last=rows[^1];string window=QuotaWindow.State(last);
  if(window=="UNKNOWN")return new(last,window,null,null,null,null,null,null,"LOW_DATA",null,null,null);
  var scoped=rows.Where(s=>QuotaWindow.SameScope(s,last)).ToArray();
  int start=scoped.Length-1;
  while(start>0){var p=scoped[start-1];var c=scoped[start];double seconds=(c.observed_at_utc-p.observed_at_utc).TotalSeconds;
   if(!DailyUsageCalculator.Valid(p)||!DailyUsageCalculator.Valid(c)||c.had_gap||seconds<=0||seconds>Math.Max(1,c.polling_interval_seconds)*3||
      !QuotaWindow.SourceComparable(p,c)||!QuotaWindow.SameReset(QuotaWindow.ResetAt(p),QuotaWindow.ResetAt(last))||
      !QuotaWindow.SameReset(QuotaWindow.ResetAt(c),QuotaWindow.ResetAt(last))||c.used_percent_raw<p.used_percent_raw)break;
   start--;
  }
  var segment=scoped[start..];var steps=new List<Step>();
  for(int i=1;i<segment.Length;i++){
   var p=segment[i-1];var c=segment[i];if(c.used_percent_raw>p.used_percent_raw&&p.used_percent_raw.HasValue&&c.used_percent_raw.HasValue)
    steps.Add(new(p.observed_at_utc.AddTicks((c.observed_at_utc-p.observed_at_utc).Ticks/2),(double)(c.used_percent_raw.Value-p.used_percent_raw.Value)));
  }
  // Count midpoint-located steps over the observed window, including its quiet tail.
  // Requiring two distinct crossings avoids inventing a rate from one rounded jump.
  // Unlike first/last-only division, this estimator does not discard idle edges.
  double? WindowRate(TimeSpan duration){
   var from=last.observed_at_utc-duration;if(from<segment[0].observed_at_utc)from=segment[0].observed_at_utc;
   var selected=steps.Where(s=>s.At>=from&&s.At<=last.observed_at_utc).ToArray();double hours=(last.observed_at_utc-from).TotalHours;
   return selected.Length<2||selected.Sum(s=>s.Delta)<2||hours<=0?null:selected.Sum(s=>s.Delta)/hours;
  }
  double? r30=WindowRate(TimeSpan.FromMinutes(30)),r2=WindowRate(TimeSpan.FromHours(2));
  double sessionHours=(last.observed_at_utc-segment[0].observed_at_utc).TotalHours;
  double? session=steps.Count>=2&&steps.Sum(s=>s.Delta)>=2&&sessionHours>0?(double)(last.used_percent_raw!.Value-segment[0].used_percent_raw!.Value)/sessionHours:null;
  bool stale=(now-last.observed_at_utc).TotalSeconds>Math.Max(1,last.polling_interval_seconds)*3;
  string confidence=stale?"STALE":r2.HasValue||r30.HasValue||session.HasValue?"OK":"LOW_DATA";
  int? idle=null;if(!stale&&steps.Count>=2){double average=(steps[^1].At-steps[0].At).TotalMinutes/(steps.Count-1);double quiet=(now-steps[^1].At).TotalMinutes;if(average>0&&quiet>average*2)idle=(int)Math.Floor(quiet);}
  double? rate=r2??r30??session;
  ForecastProjection? Project(DateTimeOffset? deadline){
   if(deadline is not DateTimeOffset at||at<=now||last.used_percent_raw is not decimal used||last.remaining_percent is not decimal remaining)return null;
   double hours=(at-now).TotalHours;DateTimeOffset? exhaust=remaining==0?now:rate is >0?now.AddHours((double)remaining/rate.Value):null;
   return new(at,hours,rate.HasValue?Math.Min(100,(double)used+rate.Value*hours):null,exhaust,(double)remaining/hours);
  }
  var incoming=announced is {SignalLevel:"INCOMING",ResetUtc:not null}&&announced.ResetUtc>now?announced:null;
  return new(last,window,QuotaWindow.StartedAt(last),r30,r2,session,steps.LastOrDefault()?.At,idle,confidence,
   window=="ACTIVE"?Project(QuotaWindow.ResetAt(last)):null,Project(incoming?.ResetUtc),incoming);
 }
}
