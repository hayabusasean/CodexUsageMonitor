using System.Globalization;
namespace CodexUsageMonitor;
internal sealed record ObservationInterval(string interval_id,string previous_sample_id,string current_sample_id,DateTimeOffset from_utc,DateTimeOffset to_utc,decimal? net_decline_pp,bool comparable,string[] reasons,double duration_seconds,string context,string plan,string pool,int? window,string slot,bool rebound,bool suspected_correction);
internal sealed record UsageAllocation(string interval_id,string date,int hour,string offset,DateTimeOffset from_utc,DateTimeOffset to_utc,decimal observed_drawdown_pp,double observed_seconds,string allocation_method);
internal sealed class DailySummary
{
 public string date{get;set;}="";public string time_zone_id{get;set;}="";public string algorithm_version{get;set;}=DailyUsageCalculator.Version;
 public string metric_basis{get;set;}="weekly_quota_percentage_points";public DateTimeOffset as_of{get;set;}
 public decimal? observed_drawdown_pp{get;set;}public int successful_sample_count{get;set;}public int comparable_pair_count{get;set;}
 public DateTimeOffset? first_sample{get;set;}public DateTimeOffset? last_sample{get;set;}
 public double observed_interval_seconds{get;set;}public double reporting_period_seconds{get;set;}public double coverage_ratio{get;set;}
 public int gap_count{get;set;}public double gap_duration_seconds{get;set;}public int unallocated_count{get;set;}public decimal unallocated_net_change_pp{get;set;}
 public decimal boundary_estimated_pp{get;set;}public decimal suspected_correction_pp{get;set;}public bool has_rebound_or_correction{get;set;}
 public bool mixed_basis{get;set;}public bool partial{get;set;}public string source_context_assurance{get;set;}="";public string[] flags{get;set;}=[];
 public Dictionary<string,decimal> segments{get;set;}=new();
 public string Display=>observed_drawdown_pp is not decimal v?"—":v>0&&v<0.1m?"≈<0.1%":"≈"+v.ToString("0.#",CultureInfo.InvariantCulture)+"%";
 public string Explanation=>mixed_basis?"今日額度基準有變，分段保留，不合計。":successful_sample_count==0?"這天尚未記錄":comparable_pair_count==0?"資料累積中；沒有可比觀察區間。":$"依已記錄的每週額度變化估算；1%=1個百分點。涵蓋 {coverage_ratio:P1}；{first_sample?.ToLocalTime():MM/dd HH:mm}—{last_sample?.ToLocalTime():MM/dd HH:mm}。"+(partial?"部分記錄；缺口/晚開始/來源工作區識別不足。":"仍非官方精準帳單。");
}
internal sealed record HourlySummary(string date,int hour,string offset,decimal? observed_drawdown_pp,double observed_interval_seconds,double reporting_period_seconds,bool partial);
internal sealed record DailyResult(List<DailySummary> Days,List<ObservationInterval> Intervals,List<UsageAllocation> Allocations,List<HourlySummary> Hours);
internal static class DailyUsageCalculator
{
 public const string Version="CUM_OBSERVED_DRAWDOWN_V1";
 public static bool Valid(UsageSample s)=>s.data_quality=="VALID"&&s.remaining_percent is >=0 and <=100;
 public static DateOnly Day(DateTimeOffset t,TimeZoneInfo tz)=>DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t,tz).DateTime);
 public static DateTimeOffset Boundary(DateOnly day,TimeZoneInfo tz){
  var d=day.ToDateTime(TimeOnly.MinValue,DateTimeKind.Unspecified);
  while(tz.IsInvalidTime(d))d=d.AddMinutes(1);
  return new DateTimeOffset(d,tz.GetUtcOffset(d)).ToUniversalTime();
 }
 public static string[] Reject(UsageSample p,UsageSample c){
  var reasons=new List<string>();var seconds=(c.observed_at_utc-p.observed_at_utc).TotalSeconds;
  if(!Valid(p)||!Valid(c))reasons.Add("INVALID_ENDPOINT");
  if(seconds<=0)reasons.Add("NONPOSITIVE_INTERVAL");
  if(c.had_gap)reasons.Add("KNOWN_GAP");
  if(seconds>Math.Max(1,c.polling_interval_seconds)*3)reasons.Add("LONG_GAP");
  if(p.account_context_key!=c.account_context_key||p.plan_type!=c.plan_type||p.limit_id!=c.limit_id||p.window_duration_mins!=c.window_duration_mins||p.source_slot!=c.source_slot||
    (p.source_generation!=c.source_generation&&(p.context_assurance!="STABLE_SCOPE"||c.context_assurance!="STABLE_SCOPE"))||
    p.reset_at_unix_seconds!=c.reset_at_unix_seconds||
    (p.reset_at_utc.HasValue&&p.reset_at_utc>p.observed_at_utc&&p.reset_at_utc<=c.observed_at_utc))reasons.Add("RESET_OR_CONTEXT_BOUNDARY");
  return reasons.ToArray();
 }
 public static DailyResult Calculate(IEnumerable<UsageSample> input,TimeZoneInfo tz,DateTimeOffset from,DateTimeOffset asOf,string pool="codex",int duration=10080)
 {
  var rows=input.Where(s=>s.limit_id==pool&&s.window_duration_mins==duration&&s.observed_at_utc<=asOf).DistinctBy(s=>s.sample_id).OrderBy(s=>s.observed_at_utc).ToList();
  var intervals=new List<ObservationInterval>();var allocations=new List<UsageAllocation>();var correctionCycles=new HashSet<string>();
  string Cycle(UsageSample s)=>s.account_context_key+"|"+s.plan_type+"|"+s.reset_at_unix_seconds+"|"+s.source_slot;
  for(int i=1;i<rows.Count;i++){
   var p=rows[i-1];var c=rows[i];var why=Reject(p,c);decimal? delta=p.remaining_percent-c.remaining_percent;bool comparable=why.Length==0;bool rebound=comparable&&delta<0;
   if(rebound)correctionCycles.Add(Cycle(c));
   intervals.Add(new(Safe.Hash(p.sample_id+"|"+c.sample_id),p.sample_id,c.sample_id,p.observed_at_utc,c.observed_at_utc,delta,comparable,why,(c.observed_at_utc-p.observed_at_utc).TotalSeconds,c.account_context_key,c.plan_type,c.limit_id,c.window_duration_mins,c.source_slot,rebound,false));
  }
  for(int i=0;i<intervals.Count;i++){
   var v=intervals[i];var c=rows[i+1];v=v with{suspected_correction=correctionCycles.Contains(Cycle(c))};intervals[i]=v;
   if(!v.comparable||v.to_utc<=from||v.from_utc>=asOf)continue;
   var start=v.from_utc;decimal total=Math.Max(0,v.net_decline_pp??0);decimal assigned=0;
   bool boundary=Day(v.from_utc,tz)!=Day(v.to_utc,tz)||TimeZoneInfo.ConvertTime(v.from_utc,tz).Hour!=TimeZoneInfo.ConvertTime(v.to_utc,tz).Hour;
   // Step by UTC hour boundaries; this preserves repeated/skipped DST local hours.
   while(start<v.to_utc){
    var local=TimeZoneInfo.ConvertTime(start,tz);
    var next=start.AddSeconds(3600-(local.Minute*60+local.Second+local.Millisecond/1000.0));
    var midnight=Boundary(Day(start,tz).AddDays(1),tz);if(next>midnight)next=midnight;if(next<=start)next=start.AddHours(1);
    var end=next<v.to_utc?next:v.to_utc;
    var value=end==v.to_utc?total-assigned:total*(end-start).Ticks/(v.to_utc-v.from_utc).Ticks;assigned+=value;
    var aStart=start<from?from:start;var aEnd=end>asOf?asOf:end;
    if(aEnd>aStart)allocations.Add(new(v.interval_id,Day(aStart,tz).ToString("yyyy-MM-dd"),TimeZoneInfo.ConvertTime(aStart,tz).Hour,TimeZoneInfo.ConvertTime(aStart,tz).ToString("zzz"),aStart,aEnd,value*(aEnd-aStart).Ticks/(end-start).Ticks,(aEnd-aStart).TotalSeconds,boundary?"TIME_PROPORTIONAL_ESTIMATE":"OBSERVED_INTERVAL"));
    start=end;
   }
  }
  var intervalLookup=intervals.ToDictionary(v=>v.interval_id);var days=new List<DailySummary>();var hours=new List<HourlySummary>();
  for(var day=Day(from,tz);day<=Day(asOf,tz);day=day.AddDays(1)){
   var start=Boundary(day,tz);var end=Boundary(day.AddDays(1),tz);if(end>asOf)end=asOf;if(start<from)start=from;if(end<start)continue;
   string date=day.ToString("yyyy-MM-dd");
   var samples=rows.Where(s=>Day(s.observed_at_utc,tz)==day&&Valid(s)).ToList();
   var aa=allocations.Where(a=>a.date==date).ToList();var relevant=intervals.Where(v=>v.to_utc>start&&v.from_utc<end).ToList();
   var rejected=relevant.Where(v=>!v.comparable).ToList();var bases=samples.Select(s=>s.account_context_key+"|"+s.plan_type).Distinct().ToList();
   var flags=relevant.SelectMany(v=>v.reasons).ToHashSet();
   if(relevant.Any(v=>v.rebound))flags.Add("REBOUND_OR_CORRECTION");
   if(aa.Any(v=>v.allocation_method=="TIME_PROPORTIONAL_ESTIMATE"))flags.Add("TIME_PROPORTIONAL_ESTIMATE");
   if(bases.Count>1)flags.Add("MIXED_BASIS");
   var observed=Union(aa.Select(v=>(v.from_utc,v.to_utc)));
   var sum=new DailySummary{date=date,time_zone_id=tz.Id,as_of=asOf,observed_drawdown_pp=bases.Count>1||aa.Count==0?null:aa.Sum(v=>v.observed_drawdown_pp),
    successful_sample_count=samples.Count,comparable_pair_count=aa.Select(v=>v.interval_id).Distinct().Count(),first_sample=samples.FirstOrDefault()?.observed_at_utc,last_sample=samples.LastOrDefault()?.observed_at_utc,
    observed_interval_seconds=observed,reporting_period_seconds=(end-start).TotalSeconds,coverage_ratio=end>start?observed/(end-start).TotalSeconds:0,
    gap_count=rejected.Count,gap_duration_seconds=Union(rejected.Where(v=>v.to_utc>v.from_utc).Select(v=>(v.from_utc<start?start:v.from_utc,v.to_utc>end?end:v.to_utc))),
    unallocated_count=rejected.Count,unallocated_net_change_pp=rejected.Sum(v=>v.net_decline_pp??0),boundary_estimated_pp=aa.Where(v=>v.allocation_method=="TIME_PROPORTIONAL_ESTIMATE").Sum(v=>v.observed_drawdown_pp),
    suspected_correction_pp=aa.Where(a=>intervalLookup[a.interval_id].suspected_correction).Sum(v=>v.observed_drawdown_pp),
    has_rebound_or_correction=relevant.Any(v=>v.rebound||v.suspected_correction),mixed_basis=bases.Count>1,source_context_assurance=string.Join(",",samples.Select(s=>s.context_assurance).Distinct()),flags=flags.ToArray()};
   sum.partial=sum.coverage_ratio<0.999||rejected.Count>0||samples.Any(s=>s.context_assurance!="STABLE_SCOPE");
   foreach(var g in aa.GroupBy(a=>{var v=intervalLookup[a.interval_id];return v.context+"|"+v.plan;}))sum.segments[g.Key]=g.Sum(v=>v.observed_drawdown_pp);
   days.Add(sum);
   for(var h=start;h<end;){
    var local=TimeZoneInfo.ConvertTime(h,tz);var next=h.AddSeconds(3600-local.Minute*60-local.Second);if(next>end)next=end;
    var hh=aa.Where(a=>a.from_utc<next&&a.to_utc>h).ToList();
    double covered=Union(hh.Select(a=>(a.from_utc<h?h:a.from_utc,a.to_utc>next?next:a.to_utc)));
    hours.Add(new(date,local.Hour,local.ToString("zzz"),hh.Count==0||sum.mixed_basis?null:hh.Sum(v=>v.observed_drawdown_pp),covered,(next-h).TotalSeconds,covered<(next-h).TotalSeconds||sum.partial));h=next;
   }
  }
  return new(days,intervals.Where(v=>v.to_utc>=from).ToList(),allocations,hours);
 }
 public static double Union(IEnumerable<(DateTimeOffset start,DateTimeOffset end)> input){
  var a=input.Where(x=>x.end>x.start).OrderBy(x=>x.start).ToArray();if(a.Length==0)return 0;
  double sum=0;var start=a[0].start;var end=a[0].end;foreach(var p in a.Skip(1)){if(p.start<=end){if(p.end>end)end=p.end;}else{sum+=(end-start).TotalSeconds;start=p.start;end=p.end;}}return sum+(end-start).TotalSeconds;
 }
}
internal sealed class MainQuotaClock
{
 public WindowQuota? Value{get;private set;}public DateTimeOffset? SuccessUtc{get;private set;}public bool SessionVerified{get;private set;}
 long successMono;long floor;public bool Failed{get;private set;}public bool ClockAnomaly{get;private set;}
 public void Observe(Snapshot s,long mono){
  var main=s.Windows.Where(w=>w.LimitId=="codex"&&w.Duration==10080&&w.Quality=="VALID"&&w.Remaining.HasValue).ToList();
  if(main.Count!=1){Failed=true;return;}Value=main[0];SuccessUtc=s.Observed;successMono=mono;floor=0;SessionVerified=true;Failed=false;ClockAnomaly=false;
 }
 public void Cache(Snapshot? s){if(s==null)return;Value=s.Windows.FirstOrDefault(w=>w.LimitId=="codex"&&w.Duration==10080&&w.Quality=="VALID");if(Value!=null)SuccessUtc=s.Observed;SessionVerified=false;}
 public void Failure(){Failed=true;}
 public void Invalidate(){Value=null;SuccessUtc=null;SessionVerified=false;floor=0;}
 public long? Age(long mono,DateTimeOffset utc,bool resume=false){
  if(SuccessUtc==null)return null;double wall=(utc-SuccessUtc.Value).TotalSeconds;
  double elapsed=SessionVerified?(mono-successMono)/1000d:wall;
  if(resume)elapsed=Math.Max(elapsed,wall);
  if(wall<0||SessionVerified&&Math.Abs(wall-elapsed)>10)ClockAnomaly=true;
  long age=Math.Max(floor,(long)Math.Max(0,elapsed));floor=age;return age;
 }
 public string Text(long mono,DateTimeOffset utc,int poll){
  var age=Age(mono,utc);if(age==null)return L.T("Overlay.Connecting");
  // A brief health label replaces the age only until the next verified reading; stored age is unchanged.
  if(ClockAnomaly)return L.T("Overlay.ClockAnomaly");
  return (!SessionVerified?L.T("Overlay.CachedAge",age):age>3*poll?L.T("Overlay.StaleAge",(int)Math.Ceiling(age.Value/60d)):Failed?L.T("Overlay.FailedAge",(int)Math.Ceiling(age.Value/60d)):L.T("Overlay.UpdatedSeconds",age));
 }
}


