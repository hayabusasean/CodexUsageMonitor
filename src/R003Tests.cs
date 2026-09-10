
using System.Text.Json;
namespace CodexUsageMonitor;
internal static class R003Tests {
 public static void Run(string[] args){
  string Get(string key)=>args[Array.IndexOf(args,key)+1];var output=Get("--test-root");Directory.CreateDirectory(output);using var doc=JsonDocument.Parse(File.ReadAllText(Get("--cases")));var results=new List<object>();
  foreach(var test in doc.RootElement.GetProperty("cases").EnumerateArray()){
   string id=test.GetProperty("id").GetString()!;try{
    var tz=TimeZoneInfo.FindSystemTimeZoneById(test.GetProperty("time_zone").GetString()!);var rows=new List<UsageSample>();
    foreach(var x in test.GetProperty("samples").EnumerateArray()){
     string S(string key,string fallback="")=>x.TryGetProperty(key,out var v)?v.ToString():fallback;
     var time=DateTimeOffset.Parse(S("observed_at"));DateTimeOffset? reset=DateTimeOffset.TryParse(S("reset_at"),out var rr)?rr:null;
     rows.Add(new(){sample_id=S("sample_id"),observed_at_utc=time.ToUniversalTime(),observed_at_local=time,remaining_percent=x.GetProperty("remaining_percent").ValueKind==JsonValueKind.Number?x.GetProperty("remaining_percent").GetDecimal():null,account_context_key=S("context"),limit_id=S("limit_id"),window_duration_mins=int.Parse(S("window_duration_mins")),source_slot=S("source_slot"),source_generation=S("generation"),plan_type=S("plan"),reset_at_utc=reset,reset_at_unix_seconds=reset?.ToUnixTimeSeconds(),data_quality=S("quality"),context_assurance=S("context_assurance","ACCOUNT_ONLY_WORKSPACE_UNVERIFIED"),polling_interval_seconds=test.GetProperty("poll_seconds").GetInt32(),had_gap=x.TryGetProperty("had_gap_since_previous",out var g)&&g.GetBoolean()});
    }
    var expected=test.GetProperty("expected_daily_observed_drawdown_pp");var dates=expected.EnumerateObject().Select(x=>DateOnly.Parse(x.Name)).Order().ToArray();
    var from=DailyUsageCalculator.Boundary(dates[0],tz);var to=DailyUsageCalculator.Boundary(dates[^1].AddDays(1),tz).AddTicks(-1);
    var result=DailyUsageCalculator.Calculate(rows,tz,from,to);var actual=result.Days.ToDictionary(d=>d.date,d=>d.observed_drawdown_pp);
    foreach(var e in expected.EnumerateObject()){decimal? wanted=e.Value.ValueKind==JsonValueKind.Number?e.Value.GetDecimal():null;if(!actual.TryGetValue(e.Name,out var got)||wanted!=got)throw new InvalidOperationException(e.Name+" expected "+wanted+" got "+got);}
    if(test.TryGetProperty("required_flags",out var flags))foreach(var flag in flags.EnumerateArray())if(!result.Days.Any(d=>d.flags.Contains(flag.GetString())))throw new InvalidOperationException("Missing flag "+flag);
    if(id=="D05"){
     using var service=new MonitorService(Path.Combine(output,"synthetic-boundary"));service.History.Save(rows);
     var report=ReportExporter.Export(service,new(from,to),false,CancellationToken.None,Path.Combine(output,"synthetic-boundary.log"));
     if(!File.ReadAllText(report).StartsWith(ReportExporter.Prompt()))throw new Exception("prompt prefix");
    }
    results.Add(new{id,classification="SYNTHETIC_PRODUCT_CALCULATOR",result="PASS",actual,details=result.Days});
   }catch(Exception ex){results.Add(new{id,classification="SYNTHETIC_PRODUCT_CALCULATOR",result="FAIL",error=ex.Message});}
  }
  void Check(string id,Action a){try{a();results.Add(new{id,classification="SYNTHETIC",result="PASS"});}catch(Exception e){results.Add(new{id,classification="SYNTHETIC",result="FAIL",error=e.Message});}}
  Check("opacity migration",()=>{var s=new Settings();if(s.OpacityPercent!=70)throw new Exception();s.OpacityPercent=35;s.Validate();if(s.OpacityPercent!=70)throw new Exception();});
  Check("usage read whitelist negative",()=>{if(!AppServerClient.IsAllowed("account/usage/read")||AppServerClient.IsAllowed("thread/start")||AppServerClient.IsAllowed("account/resetCredits/consume"))throw new Exception();});
  Check("prompt embedded",()=>{if(ReportExporter.Prompt().Length<1000)throw new Exception();});
  Check("CSV injection",()=>{if(!Csv.Cell("=SUM(1)",true).Contains("'="))throw new Exception();});
  Check("main clock only main success",()=>{
   var now=DateTimeOffset.UtcNow;var w=new WindowQuota("codex","","primary",10080,50,null,"VALID","pro",null,null,null,"");
   var snap=new Snapshot(now,now,0,"a","STABLE_SCOPE","g","test",[w],null,[]);
   var clock=new MainQuotaClock();clock.Observe(snap,1000);if(clock.Age(6000,now.AddSeconds(5))!=5)throw new Exception("age");
   clock.Observe(snap with{Windows=[w with{LimitId="codex_bengalfox"}],Observed=now.AddSeconds(6)},7000);
   if(clock.Age(8000,now.AddSeconds(7))!=7||clock.Value?.LimitId!="codex")throw new Exception("spark reset");
   clock.Failure();if(clock.Text(9000,now.AddSeconds(8),90)!=L.T("Overlay.FailedAge",1))throw new Exception("failure");
   clock.Age(10000,now.AddHours(1),true);if(clock.Age(11000,now.AddHours(1))<3600)throw new Exception("resume floor");
   clock.Observe(snap with{Observed=now.AddHours(1)},12000);if(clock.Age(12000,now.AddHours(1))!=0)throw new Exception("same value success");
  });
  Check("privacy canaries and notes",()=>{var p=new ReportExporter.Privacy();var text=p.Scrub("mail@example.com Bearer secret123 sk-abcdef password=canary C:\\Users\\Private\\auth.json");if(text.Contains("example.com")||text.Contains("secret123")||text.Contains("canary")||text.Contains("Private"))throw new Exception();});
  Check("large export volumes and cancellation",()=>{
   using var service=new MonitorService(Path.Combine(output,"synthetic-volume"));var start=DateTimeOffset.UtcNow.AddDays(-12);
   string file=Path.Combine(service.Root,"history","usage_"+start.UtcDateTime.ToString("yyyy-MM-dd")+".csv");
   using(var writer=new StreamWriter(file,false,new System.Text.UTF8Encoding(true))){writer.Write(Csv.Header<UsageSample>());for(int i=0;i<10000;i++)writer.Write(Csv.Line(new UsageSample{sample_id="synthetic-"+i,poll_id="synthetic-poll-"+i,observed_at_utc=start.AddSeconds(i*90),observed_at_local=start.AddSeconds(i*90).ToLocalTime(),limit_id="codex",window_duration_mins=10080,source_slot="primary",source_generation="synthetic-source",account_context_key="synthetic-account",context_assurance="STABLE_SCOPE",plan_type="pro",data_quality="VALID",polling_interval_seconds=90,used_percent_raw=i%1000/10m,remaining_percent=100-i%1000/10m}));}
   var filter=new HistoryFilter(start,DateTimeOffset.UtcNow);
   string result=ReportExporter.Export(service,filter,false,CancellationToken.None,Path.Combine(output,"synthetic-volumes.log"));
   if(Path.GetExtension(result)!=".zip")throw new Exception("expected multiple volumes");
   using(var zip=System.IO.Compression.ZipFile.OpenRead(result)){if(zip.Entries.Count<3)throw new Exception("volume count");foreach(var e in zip.Entries.Where(e=>e.Name.EndsWith(".log"))){using var reader=new StreamReader(e.Open());if(!reader.ReadToEnd().StartsWith(ReportExporter.Prompt()))throw new Exception("volume prompt");}}
   string cancelled=Path.Combine(output,"cancelled.log");try{ReportExporter.Export(service,filter,false,new CancellationToken(true),cancelled);throw new Exception("cancel ignored");}catch(OperationCanceledException){}
   if(File.Exists(cancelled))throw new Exception("cancel published completed file");
  });
  AtomicJson.Save(Path.Combine(output,"r003-tests.json"),results);
 }
}

