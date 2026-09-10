using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
namespace CodexUsageMonitor;
internal static class TestHarness
{
    static readonly List<object> results=[];static string root="";
    static void Check(string id,Action test,string classification="SYNTHETIC"){
        try{test();results.Add(new{id,classification,result="PASS"});}catch(Exception e){results.Add(new{id,classification,result="FAIL",error=e.GetType().Name,detail=e is InvalidOperationException?e.Message:"controlled test failed"});}
    }
    static void Assert(bool condition,string message="assertion failed"){if(!condition)throw new InvalidOperationException(message);}
    static JsonElement Json(string s)=>JsonDocument.Parse(s).RootElement.Clone();
    static Snapshot Normalize(string s)=>QuotaNormalizer.Normalize(Json(s),"synthetic-account","STABLE_SCOPE","gen","SYNTHETIC",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,1000);
    static Snapshot Quota(decimal used,int duration=10080)=>Normalize(JsonSerializer.Serialize(new{rateLimits=new{limitId="codex",primary=new{usedPercent=used,windowDurationMins=duration,resetsAt=1800000000}}}));
    static UsageSample Sample(decimal remaining,int sequence=0)=>UsageSample.From(Quota(100-remaining) with {PollId="test"+sequence,Observed=DateTimeOffset.Parse("2026-09-07T00:00:00Z").AddSeconds(sequence*90)},false,90)[0];
    public static void Run(string[] args)
    {
        int i=Array.IndexOf(args,"--test-root");if(i<0||i+1>=args.Length)return;
        root=Path.GetFullPath(args[i+1]);Directory.CreateDirectory(root);
        Check("T05 percentage semantics",()=>{
            Assert(Quota(25).Windows[0].Remaining==75);
            Assert(Quota(100).Windows[0].Compact=="0%");
            Assert(Quota(0).Windows[0].Compact=="100%");
            Assert(Quota(99.6m).Windows[0].Compact=="<1%");
        });
        Check("T06 duration semantics missing malformed",()=>{
            var s=Normalize("""{"rateLimits":{"primary":{"usedPercent":83,"windowDurationMins":10080},"secondary":null}}""");
            Assert(s.Windows.Count==1&&s.Windows[0].Name=="每週");
            foreach(var u in new[]{"null","\"25\"","-1","101"}){
                var w=Normalize("{\"rateLimits\":{\"primary\":{\"usedPercent\":"+u+",\"windowDurationMins\":300}}}").Windows[0];Assert(w.Remaining==null&&w.Compact=="來源資料異常");
            }
            Assert(Normalize("""{"rateLimits":{"primary":{"usedPercent":20}}}""").Windows[0].Quality=="SOURCE_DATA_INVALID");
            Assert(Normalize("""{"rateLimits":{"primary":{"usedPercent":20,"windowDurationMins":300,"resetsAt":"bad"}}}""").Windows[0].Remaining==80);
        });
        Check("T07 multi pool legacy dedup slots",()=>{
            var s=Normalize("""{"rateLimits":{"primary":{"usedPercent":98,"windowDurationMins":300}},"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":10,"windowDurationMins":300},"secondary":{"usedPercent":20,"windowDurationMins":300}},"other":{"primary":{"usedPercent":30,"windowDurationMins":10080}}}}""");
            Assert(s.Windows.Count==3&&s.Windows[0].Remaining==90);
            Assert(UsageSample.From(s,false,90).Select(x=>x.comparison_key).Distinct().Count()==3);
            var p=Sample(0);var c=Sample(100,1) with{source_slot="secondary"};Assert(EventDetector.Compare(p,c).Any(e=>e.event_type=="ZERO_TO_FULL_OBSERVED"));
            c=c with{comparison_key=c.comparison_key+"changed",window_duration_mins=300};Assert(EventDetector.Compare(p,c).All(e=>e.event_type=="SOURCE_CONTEXT_CHANGED"));
        });
        Check("T02 auth identity scope privacy",()=>{
            var salt=Convert.ToBase64String(new byte[32]);
            var a=Json("""{"account":{"type":"chatgpt","email":"synthetic@example.invalid","planType":"pro"}}""");
            var one=QuotaNormalizer.Identity(a,salt,"a");var two=QuotaNormalizer.Identity(a,salt,"b");
            Assert(one==two&&one.assurance=="SCOPE_NOT_FULLY_VERIFIED"&&!one.key.Contains("@"));
            var b=Json("""{"account":{"type":"chatgpt","email":"other@example.invalid","planType":"pro"}}""");Assert(one!=QuotaNormalizer.Identity(b,salt,"a"));
            foreach(var raw in new[]{"""{"account":null}""","""{"account":{"type":"apiKey"}}"""}){
                bool caught=false;try{QuotaNormalizer.Identity(Json(raw),salt,"a");}catch(MonitorException){caught=true;}Assert(caught);
            }
            var unknown=Json("""{"account":{"type":"chatgpt"}}""");Assert(QuotaNormalizer.Identity(unknown,salt,"a").key!=QuotaNormalizer.Identity(unknown,salt,"b").key);
        });
        Check("T08 UTC countdown clock changes",()=>{
            var w=Quota(100).Windows[0];
            Assert(w.ResetText(DateTimeOffset.FromUnixTimeSeconds(1800000001)).Contains("等待來源更新"));
            Assert(w.Remaining==0);Assert(DateTimeOffset.FromUnixTimeSeconds(0).ToOffset(TimeSpan.FromHours(8)).Hour==8);
            var e=EventDetector.Compare(Sample(0,2),Sample(100,1));Assert(e.All(x=>x.had_gap&&x.timing_relation=="UNCLASSIFIED"));
        });
        Check("T15 all event transitions deterministic",()=>{
            foreach(var (before,after,expected) in new (decimal,decimal,string?)[]{(0,100,"ZERO_TO_FULL_OBSERVED"),(20,100,"FULL_REPLENISHMENT_OBSERVED"),(0,83,"QUOTA_INCREASE_OBSERVED"),(20,95,"QUOTA_INCREASE_OBSERVED"),(100,100,null),(30,20,"QUOTA_DECREASE_OBSERVED")}){
                var ev=EventDetector.Compare(Sample(before),Sample(after,1));Assert(expected==null?ev.Count==0:ev.Count==1&&ev[0].event_type==expected);Assert(ev.Select(e=>e.event_id).SequenceEqual(EventDetector.Compare(Sample(before),Sample(after,1)).Select(e=>e.event_id)));
            }
            Assert(EventDetector.Compare(Sample(100),Sample(100,1) with{reset_at_unix_seconds=1800000010}).Single().event_type=="RESET_TIMESTAMP_CHANGED");
        });
        Check("T16 T17 gaps interval cause remains unknown",()=>{
            var p=Sample(0);var c=Sample(100,1) with{had_gap=true,source_generation="next",reset_credits_available_count=1,observed_at_utc=p.observed_at_utc.AddHours(8)};
            var ev=EventDetector.Compare(p,c);Assert(ev.Count==3&&ev.All(x=>x.cause=="UNKNOWN"&&x.had_gap));
            var increase=ev.Single(x=>x.event_type=="ZERO_TO_FULL_OBSERVED");Assert(increase.observation_from_utc==p.observed_at_utc&&increase.observation_to_utc==c.observed_at_utc&&increase.timing_relation=="UNCLASSIFIED");
            Assert(EventDetector.Compare(p,c with{account_context_key="other",comparison_key="other"}).All(e=>e.event_type=="SOURCE_CONTEXT_CHANGED"));
        });
        Check("T13 thresholds hysteresis persisted silence",()=>{
            var state=new AlertState();Assert(state.Observe([Sample(25)]).Count==0);Assert(state.Observe([Sample(4,1)]).Count==1);Assert(state.Observe([Sample(4,2)]).Count==0);
            Assert(state.Observe([Sample(0,3)]).Count==1);Assert(state.Observe([Sample(0.4m,4)]).Count==0);
            var file=Path.Combine(root,"alerts.json");AtomicJson.Save(file,state);state=AtomicJson.Load(file,()=>new AlertState());Assert(state.Observe([Sample(0,5)]).Count==0);
            state.Observe([Sample(22,6)]);Assert(state.Observe([Sample(20,7)]).Count==1);
            Assert(state.Observe([Sample(0,8) with{remaining_percent=null,data_quality="SOURCE_DATA_INVALID"}]).Count==0);
        });
        Check("T18 atomic corrupt config tail isolation",()=>{
            var file=Path.Combine(root,"settings.json");File.WriteAllText(file,"{bad");var s=AtomicJson.Load(file,()=>new Settings());Assert(s.Interval==90&&File.Exists(file+".bad"));AtomicJson.Save(file,s);
            var csv=Path.Combine(root,"tail.csv");File.WriteAllText(csv,Csv.Header<UsageSample>()+Csv.Line(Sample(25))+"\"partial",new UTF8Encoding(true));
            Csv.RepairTail(csv);Assert(Csv.Records<UsageSample>(csv).Count()==1&&File.Exists(csv+".tail.bad"));
        });
        Check("T19 CSV Unicode quoting newline formulas filter",()=>{
            var texts=new[]{"中文,逗號","\"引號\"","第一行\n第二行","=HYPERLINK(\"https://invalid\")","@SUM(A1)"," 正常"};
            var encoded=string.Join(",",texts.Select(x=>Csv.Cell(x)));Assert(Csv.Read(new StringReader(encoded+"\r\n")).Single().SequenceEqual(texts));
            Assert(Csv.Cell(" =1+1",true).StartsWith("\"'"));
            var store=new HistoryStore(Path.Combine(root,"csv-store"));
            Assert(store.Save([Sample(75.123m) with{limit_name_safe="中文,\n\"測試\""}]));
            var filter=new HistoryFilter(DateTimeOffset.Parse("2026-09-06T00:00:00Z"),DateTimeOffset.Parse("2026-09-08T00:00:00Z"),"codex",10080);
            var file=store.Export<UsageSample>(filter);Assert(File.ReadAllBytes(file).Take(3).SequenceEqual(new byte[]{239,187,191}));var back=Csv.Records<UsageSample>(file).Single();Assert(back.remaining_percent==75.123m&&back.limit_name_safe=="中文,\n\"測試\"");
            Assert(!store.Query<UsageSample>(filter with{Pool="other"}).Any());
        });
        Check("T14 T18 history transaction recover dedup lock bound",()=>{
            var dir=Path.Combine(root,"history-store");var store=new HistoryStore(dir);
            Assert(store.Save([Sample(0)]));Assert(store.Save([Sample(100,1)]));Assert(store.Save([Sample(100,1)]));
            var all=new HistoryFilter(DateTimeOffset.MinValue,DateTimeOffset.MaxValue);
            Assert(store.Query<UsageSample>(all).Count()==2);Assert(store.Query<UsageEvent>(all).Count()==1);
            // Remove only this synthetic event file to simulate samples committed before events.
            foreach(var f in Directory.GetFiles(Path.Combine(dir,"history"),"events_*.csv"))File.Delete(f);
            var recovered=new HistoryStore(dir);recovered.Recover();Assert(recovered.Query<UsageEvent>(all).Count()==1);recovered.Recover();Assert(recovered.Query<UsageEvent>(all).Count()==1);
            var usage=Directory.GetFiles(Path.Combine(dir,"history"),"usage_*.csv").Single();
            using(var locked=new FileStream(usage,FileMode.Open,FileAccess.Read,FileShare.Read)){
                Assert(!recovered.Save([Sample(80,2)]));Assert(recovered.SaveError!=null&&recovered.PendingCount==1);
            }
            Assert(recovered.Save([Sample(70,3)]));Assert(recovered.Query<UsageSample>(all).Count()==4);
            Assert(recovered.Page<UsageSample>(all,0,2)[0].remaining_percent==70);
            var exports=Path.Combine(dir,"exports","keep.txt");File.WriteAllText(exports,"keep");
            var notes=Path.Combine(dir,"event_notes.json");File.WriteAllText(notes,"keep");
            recovered.Cleanup(30,DateTimeOffset.Parse("2027-01-01T00:00:00Z"),1);
            Assert(File.Exists(exports)&&File.Exists(notes)&&!recovered.Query<UsageSample>(all).Any());
        });
        Check("T18 locked disk bounded buffer visible loss",()=>{
            var dir=Path.Combine(root,"locked-store");var store=new HistoryStore(dir);store.Save([Sample(50)]);
            var file=Directory.GetFiles(Path.Combine(dir,"history"),"usage_*.csv").Single();
            using var locked=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
            for(int n=1;n<=125;n++)store.Save([Sample(40,n)]);
            Assert(store.PendingCount==120&&store.DroppedBatches==5&&store.SaveError!=null);
        });
        Check("T09 backoff throttle defaults",()=>{
            Assert(new Settings().Interval==90&&new Settings().Startup==false);
            Assert(Enumerable.Range(1,6).Select(MonitorService.Backoff).SequenceEqual(new[]{90,180,360,720,900,900}));
            using var s=new MonitorService(Path.Combine(root,"service"));Assert(s.Refresh(true));Assert(!s.Refresh(true));s.Resume();Assert(!s.Verified&&s.Current==null);
        });
        Check("T11 negative monitor bounds removal",()=>{
            var b=OverlayForm.ClampBounds(new(-1800,30,400,80),[new(-1920,0,1920,1080),new(0,0,1920,1080)]);Assert(b.X==-1800);
            var removed=OverlayForm.ClampBounds(new(-1800,30,400,80),[new(0,0,1920,1080)]);Assert(removed.X==0);
        });
        Check("T21 isolated startup shortcut reversible spaces",()=>{
            var path=Path.Combine(root,"測試 shortcut.lnk");StartupLink.Set(true,Environment.ProcessPath!,path);Assert(File.Exists(path));StartupLink.Set(false,Environment.ProcessPath!,path);Assert(!File.Exists(path));
        });
        Check("T01 native architecture path validation",()=>{
            CodexLocator.ValidateArchitecture(Environment.ProcessPath!);
            bool reject=false;try{CodexLocator.ValidateArchitecture("codex.cmd");}catch(MonitorException){reject=true;}Assert(reject);
            var bad=Path.Combine(root,"codex.exe");File.WriteAllBytes(bad,new byte[100]);reject=false;try{CodexLocator.ValidateArchitecture(bad);}catch(MonitorException){reject=true;}Assert(reject);
            Assert(!CodexLocator.Candidates("").Contains(Path.Combine(Environment.CurrentDirectory,"codex.exe"),StringComparer.OrdinalIgnoreCase));
            Assert(OwnedProcess.Quote("中文 path\\").EndsWith("\\\\\""));
        });
        Check("T22 RPC allowlist",()=>{
            Assert(new[]{"initialize","account/read","account/rateLimits/read"}.All(AppServerClient.IsAllowed));
            Assert(new[]{"turn/start","thread/start","account/login/start","account/logout","account/rateLimitResetCredit/consume","exec","account/sendAddCreditsNudgeEmail"}.All(m=>!AppServerClient.IsAllowed(m)));
        });
        var fixture=Path.Combine(root,"..","producer-quota.json");
        if(File.Exists(fixture))Check("T04 real fixture normalization",()=>{
            var s=Normalize(File.ReadAllText(fixture));Assert(s.Windows.Any(w=>w.LimitId=="codex"&&w.Duration==10080));Assert(s.Windows.All(w=>w.Used.HasValue&&w.Remaining==100-w.Used));AtomicJson.Save(Path.Combine(root,"fixture-normalized.json"),s with{AccountKey="SANITIZED_REAL_FIXTURE"});
        },"SANITIZED_REAL_FIXTURE");
        foreach(var scenario in new[]{"normal","fragment","notification","server-request","duplicate","error401","error403","unsupported","non-json","eof","timeout","oversize"}){
            Check("T03 protocol "+scenario,()=>Task.Run(async()=>{
                using var client=new AppServerClient(new Diagnostic(root)){TimeoutSeconds=2};using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try {
                    await client.ConnectControlled(Environment.ProcessPath!,["--protocol-fixture",scenario],root,ct.Token);
                    var r=await client.Request("account/read",new{refreshToken=false},ct.Token);
                    bool expected=scenario is "normal" or "fragment" or "notification" or "server-request" or "duplicate";
                    Assert(expected&&r.GetProperty("account").GetProperty("type").GetString()=="chatgpt","expected protocol failure");
                }catch(MonitorException ex){
                    string wanted=scenario switch{"error401"=>"AUTH_REQUIRED","error403"=>"POLICY_DENIED","unsupported"=>"METHOD_UNSUPPORTED","non-json"=>"NON_JSON_OUTPUT","eof"=>"TRANSPORT_EOF","timeout"=>"REQUEST_TIMEOUT","oversize"=>"MESSAGE_TOO_LARGE",_=>""};
                    Assert(ex.Code==wanted,"unexpected safe error "+ex.Code+" expected "+wanted);
                }
            }).GetAwaiter().GetResult());
        }
        AtomicJson.Save(Path.Combine(root,"results.json"),results);
    }
    public static void Producer(string[] args)
    {
        string mode=args.Last();Console.InputEncoding=Encoding.UTF8;Console.OutputEncoding=new UTF8Encoding(false);
        string? line;bool initialized=false;
        void Output(object value){var text=JsonSerializer.Serialize(value);if(mode=="fragment"){foreach(char c in text){Console.Write(c);Console.Out.Flush();}}else Console.Write(text);Console.Write("\r\n");Console.Out.Flush();}
        while((line=Console.ReadLine())!=null){
            var r=Json(line);if(!r.TryGetProperty("method",out var m))continue;var method=m.GetString();
            if(method=="initialized"){initialized=true;continue;}
            var id=r.GetProperty("id").GetInt32();
            if(method=="initialize"){Output(new{id,result=new{userAgent="SYNTHETIC"}});continue;}
            if(!initialized){Output(new{id,error=new{code=-32600,message="handshake wrong"}});continue;}
            if(mode=="timeout"){Thread.Sleep(5000);continue;}
            if(mode=="eof")return;
            if(mode=="non-json"){Console.WriteLine("synthetic noise");Console.Out.Flush();continue;}
            if(mode=="oversize"){Console.WriteLine(new string('x',1024*1024+10));Console.Out.Flush();continue;}
            if(mode is "error401" or "error403" or "unsupported"){Output(new{id,error=new{code=mode=="unsupported"?-32601:-32000,message=mode=="error401"?"401 unauthorized":mode=="error403"?"403 forbidden":"unsupported"}});continue;}
            if(mode=="notification")Output(new{method="account/rateLimits/updated",@params=new{}});
            if(mode=="server-request")Output(new{id="server-1",method="unknown/execute",@params=new{}});
            Output(new{id,result=new{account=new{type="chatgpt",email="synthetic@example.invalid",planType="pro"}}});
            if(mode=="duplicate")Output(new{id,result=new{account=new{type="chatgpt"}}});
        }
    }
}

