using System.Globalization;
namespace CodexUsageMonitor;

// Isolated, explicitly synthetic production-code checks. Native visual evidence is collected separately.
internal static class PublicHistoryTests {
 static readonly DateTimeOffset Origin=DateTimeOffset.Parse("2026-09-08T10:00:00+08:00",CultureInfo.InvariantCulture);
 internal static UsageSample Sample(decimal value,int index,bool gap=false){
  var at=Origin.AddSeconds(index*90);var reset=Origin.AddDays(7);
  return new(){sample_id="PUBLIC_HISTORY_SYNTHETIC_"+index,poll_id="PUBLIC_HISTORY_SYNTHETIC_"+index,observed_at_utc=at.ToUniversalTime(),observed_at_local=at,
   remaining_percent=value,used_percent_raw=100-value,limit_id="codex",source_slot="primary",window_duration_mins=10080,account_context_key="SYNTHETIC",
   source_generation="SYNTHETIC",comparison_key="SYNTHETIC-codex-weekly",context_assurance="STABLE_SCOPE",plan_type="pro",data_quality="VALID",
   polling_interval_seconds=90,reset_at_utc=reset,reset_at_unix_seconds=reset.ToUnixTimeSeconds(),had_gap=gap};
 }
 internal static Dictionary<string,List<UsageSample>> Fixtures()=>new(){
  ["H01-100-99"]=[Sample(100,0),Sample(99,1)],
  ["H02-low-gap-recent"]=[Sample(13,0),Sample(5,1),Sample(92,10,true),Sample(91,11)],
  ["H03-latest-one"]=[Sample(100,0),Sample(94,1),Sample(96,10,true)],
  ["H04-overnight"]=[Sample(70,0),Sample(40,960,true)],
  ["H05-reset"]=[Sample(2,0),Sample(0,1),Sample(100,2),Sample(99,3)],
  ["H08-empty"]=[],
  ["H08-same"]=[Sample(99,0),Sample(99,1)],
  ["H08-low"]=[Sample(5,0),Sample(0,1)]
 };
 static void Assert(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
 internal static void Run(Action<string,Action> check){
  var f=Fixtures();
  check("H01 latest 100 to 99 is 98–100 and half-plot difference",()=>{
   var selected=TrendView.LatestSegment(f["H01-100-99"]);var range=TrendView.Zoom(selected);
   Assert(selected.Count==2&&range==(98m,100m),"Latest selection or scale mismatch.");
   Assert(1/(range.max-range.min)>=.25m,"A 1 pp change is too small in the plot.");
  });
  check("H02 latest keeps recent 92 to 91 while full data retains 13 and 5",()=>{
   var all=f["H02-low-gap-recent"];var selected=TrendView.LatestSegment(all);
   Assert(selected.Select(x=>x.remaining_percent).SequenceEqual(new decimal?[]{92,91}),"Latest segment mixed in the earlier low section.");
   using var chart=new TrendView{Rows=all};Assert(chart.DisplayRows.Count==2,"Default view must be latest.");
   chart.Latest=false;Assert(chart.DisplayRows.Count==4&&chart.Range==(0m,100m),"Full range hid observations or zoomed its axis.");
  });
  check("H03 latest single observation does not borrow the earlier segment",()=>{
   var rows=f["H03-latest-one"];var latest=TrendView.LatestSegment(rows);
   Assert(latest.Count==1&&latest[0].remaining_percent==96,"Borrowed an older segment.");
   Assert(!TrendView.Bridge(rows[1],rows[2]),"Gap was bridged.");
  });
  check("H04 overnight gap is neither a line nor allocated 30 pp",()=>{
   var rows=f["H04-overnight"];Assert(!TrendView.Bridge(rows[0],rows[1]),"Overnight line was connected.");
   var result=DailyUsageCalculator.Calculate(rows,TimeZoneInfo.Utc,Origin.AddDays(-1),Origin.AddDays(2));
   Assert(result.Allocations.Sum(x=>x.observed_drawdown_pp)==0,"The gap allocated quota to a day.");
   Assert(HistoryForm.Change(rows[0],rows[1])=="—","Gap table change was presented as measured.");
   Assert(HistoryForm.RowStatus(rows[0],rows[1])==L.T("History.Resumed"),"Resumed status missing.");
  });
  check("H05 replenishment starts a new chart segment without reducing usage",()=>{
   var rows=f["H05-reset"];var segment=TrendView.LatestSegment(rows);
   Assert(segment.Count==2&&segment[0].remaining_percent==100,"Reset carried over the older segment.");
   var daily=DailyUsageCalculator.Calculate(rows,TimeZoneInfo.Utc,Origin.AddDays(-1),Origin.AddDays(1));
   Assert(daily.Days.Sum(x=>x.observed_drawdown_pp??0)==3,"Replenishment cancelled measured consumption.");
  });
  check("H06 latest is selected-period latest, with no current account dependency",()=>{
   var rows=f["H02-low-gap-recent"].Select(x=>x with{observed_at_utc=x.observed_at_utc.AddDays(-7),observed_at_local=x.observed_at_local.AddDays(-7)}).ToList();
   var segment=TrendView.LatestSegment(rows);
   Assert(segment.Count==2&&segment[^1].observed_at_utc==rows[^1].observed_at_utc,"Past range was replaced by current samples.");
  });
  check("H07 chart focus leaves source CSV projection and row count unchanged",()=>{
   var rows=f["H02-low-gap-recent"];string before=string.Concat(rows.Select(x=>Csv.Line(x)));
   using var chart=new TrendView{Rows=rows};chart.Latest=false;chart.Latest=true;
   Assert(rows.Count==4&&string.Concat(rows.Select(x=>Csv.Line(x)))==before&&chart.Rows.Count==4,"Focus changed source observations.");
  });
  check("H08 empty and invalid observations produce no synthetic value",()=>{
   Assert(TrendView.LatestSegment([]).Count==0&&TrendView.Zoom([])==(0m,100m),"Empty state generated data.");
   Assert(TrendView.LatestSegment([Sample(0,0) with{remaining_percent=null,data_quality="MISSING"}]).Count==0,"Missing reading became zero.");
  });
  check("H08 equal values and lower edge stay bounded with at least 2 pp",()=>{
   foreach(var key in new[]{"H08-same","H08-low"}){
    var rows=f[key];var range=TrendView.Zoom(rows);Assert(range.min>=0&&range.max<=100&&range.max-range.min>=2,"Invalid y scale.");
    Assert(range.min<=rows.Min(x=>x.remaining_percent)&&range.max>=rows.Max(x=>x.remaining_percent),"Scale clipped a value.");
   }
  });
  check("H08 dense render reduction preserves extrema and both sides of gaps",()=>{
   var rows=Enumerable.Range(0,20000).Select(i=>Sample(95-i/1000m,i,i==5000||i==10000)).ToList();
   rows[1234]=rows[1234] with{remaining_percent=1,used_percent_raw=99};
   var reduced=TrendView.Reduce(rows,120);
   Assert(reduced.Count<rows.Count/3&&reduced.Min(x=>x.remaining_percent)==1,"No bounded rendering or minimum lost.");
   foreach(int i in new[]{4999,5000,9999,10000})Assert(reduced.Any(x=>x.sample_id==rows[i].sample_id),"Boundary endpoint lost.");
   Assert(rows.Count==20000,"Reduction modified source data.");
  });
  check("H09 invalid and reversed dates are rejected without throwing",()=>{
   foreach(var (begin,end) in new[]{("2026-02-30","2026-03-01"),("09/08/2026","2026-09-08"),("2026-09-09","2026-09-08"),("9999-12-31","9999-12-31")})
    Assert(!HistoryForm.TryDateRange(begin,end,TimeZoneInfo.Utc,out _,out _,out _),"Invalid date range was accepted.");
  });
  check("H09 date range includes first midnight and excludes following midnight across year",()=>{
   Assert(HistoryForm.TryDateRange("2026-12-31","2027-01-01",TimeZoneInfo.Utc,out var first,out var last,out _),"Valid cross-year range rejected.");
   Assert(first==DateTimeOffset.Parse("2026-12-31T00:00:00Z")&&last.AddTicks(1)==DateTimeOffset.Parse("2027-01-02T00:00:00Z"),"Date endpoints mismatch.");
  });
  check("H09 local DST day uses actual 23-hour boundary",()=>{
   var zone=TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
   Assert(HistoryForm.TryDateRange("2026-03-08","2026-03-08",zone,out var first,out var last,out _),"DST range rejected.");
   Assert((last.AddTicks(1)-first).TotalHours==23,"Assumed a 24-hour local DST day.");
  });
  check("H09 same visible dates rebase to the changed local time zone without rewriting samples",()=>{
   var rows=f["H01-100-99"];string before=string.Concat(rows.Select(x=>Csv.Line(x)));
   var taipei=TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");var pacific=TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
   Assert(HistoryForm.TryDateRange("2026-09-08","2026-09-08",taipei,out var firstA,out var lastA,out _),"Initial local range rejected.");
   Assert(HistoryForm.TryDateRange("2026-09-08","2026-09-08",pacific,out var firstB,out var lastB,out _),"New local range rejected.");
   Assert((firstB-firstA).TotalHours==15&&(lastB-lastA).TotalHours==15,"UTC query boundaries were not rebased to the new zone.");
   Assert(rows.Count(x=>x.observed_at_utc>=firstA&&x.observed_at_utc<=lastA)==2&&rows.Count(x=>x.observed_at_utc>=firstB&&x.observed_at_utc<=lastB)==0,"The same local date should select different observations in these zones.");
   Assert(string.Concat(rows.Select(x=>Csv.Line(x)))==before,"Time zone view rebuilt stored samples.");
  });
  check("H source/account/plan/comparison changes split the latest segment",()=>{
   var p=Sample(92,0);var c=Sample(91,1);
   foreach(var changed in new[]{c with{source_generation="NEW"},c with{account_context_key="NEW"},c with{plan_type="plus"},c with{comparison_key="NEW"},c with{reset_at_unix_seconds=c.reset_at_unix_seconds+1}})
    Assert(TrendView.LatestSegment([p,changed]).Count==1,"A comparison boundary was bridged.");
  });
  check("H chart x-axis has 3–6 ticks",()=>{
   foreach(float width in new[]{320,650,900,1600})Assert(TrendView.TickCount(width) is >=3 and <=6,"Invalid tick count.");
  });
  check("H bilingual remaining changes preserve explicit percentage-point semantics",()=>{
   string old=L.Language;try{
    L.SetLanguage("en-US");Assert(HistoryForm.Change(Sample(100,0),Sample(99,1))=="Remaining −1 pp","English change semantics unclear.");
    L.SetLanguage("zh-TW");Assert(HistoryForm.Change(Sample(100,0),Sample(99,1)).Contains("−1"),"Chinese numeric change drifted.");
   }finally{L.SetLanguage(old);}
  });
 }
}
