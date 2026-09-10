using System.Diagnostics;
using System.Text.Json;
namespace CodexUsageMonitor;
internal sealed class MonitorService : IDisposable
{
    public Settings Settings{get;} public HistoryStore History{get;}public string Root=>History.Root;
    public ResetRadar Radar{get;}
    public MainQuotaClock MainClock{get;}=new();
    public long Monotonic=>mono.ElapsedMilliseconds;
    public DailyResult? Daily{get;private set;}
    public object ServiceTokenActivity{get;private set;}=new{status="NOT_PROVIDED",time_zone="UNKNOWN"};
    DateTimeOffset usageChecked; string usageIdentity="";
    public void RebuildDaily(){try{var now=DateTimeOffset.UtcNow;Daily=DailyUsageCalculator.Calculate(History.Query<UsageSample>(new(now.AddDays(-4),now)),TimeZoneInfo.Local,DailyUsageCalculator.Boundary(DateOnly.FromDateTime(DateTime.Today.AddDays(-2)),TimeZoneInfo.Local),now);}catch{}}
    public Snapshot? Current{get;private set;} public string Status{get;private set;}="正在取得額度";public bool Verified{get;private set;}
    public bool Busy{get;private set;}public int Failures{get;private set;}public DateTimeOffset? NextRetry{get;private set;}
    public string LastErrorCode{get;private set;}="";public string VerifiedPath{get;private set;}="";
    public int ChildId=>client?.ChildId??0;public event Action? Changed;public event Action<List<string>>? Alert;
    readonly SemaphoreSlim pollGate=new(1,1);readonly object identityGate=new();readonly Diagnostic log;readonly Stopwatch mono=Stopwatch.StartNew();readonly CancellationTokenSource stop=new();readonly SemaphoreSlim signal=new(0,1);
    AppServerClient? client;AlertState alertState;Task? loop;long lastManual=-100000,lastPoll=-100000;bool gap=true,forceReconnect;
    public MonitorService(string root)
    {
        History=new(root);Radar=new(root);Settings=Settings.Load(Path.Combine(root,"settings.json"));Settings.Validate();
        log=new(root);alertState=AtomicJson.Load(Path.Combine(root,"notification_state.json"),()=>new AlertState());
        Current=AtomicJson.Load<Snapshot?>(Path.Combine(root,"last_good.json"),()=>null);
        MainClock.Cache(Current);
        if(Current!=null)Status="上次資料，尚未驗證";
    }
    public void Start(){if(loop!=null)return;Radar.SetEnabled(Settings.RadarEnabled);Radar.Start();loop=Task.Run(Run);}
    public bool Refresh(bool manual=false,bool reconnect=false)
    {
        if(manual&&mono.ElapsedMilliseconds-lastManual<10000)return false;
        if(manual)lastManual=mono.ElapsedMilliseconds;
        if(reconnect)forceReconnect=true;
        try{signal.Release();}catch(SemaphoreFullException){}return true;
    }
    public void Resume(){MainClock.Age(Monotonic,DateTimeOffset.UtcNow,true);gap=true;Verified=false;Status="恢復監測，正在重新驗證";Refresh();Changed?.Invoke();}
    public bool SaveSettings(){Settings.Validate();try{AtomicJson.Save(Path.Combine(Root,"settings.json"),Settings);return true;}catch{Status="SETTINGS_SAVE_FAILED";Changed?.Invoke();return false;}}
    async Task Run()
    {
        History.Recover();RebuildDaily();SaveSettings();
        while(!stop.IsCancellationRequested){
            try{await PollOnce(stop.Token);}catch(OperationCanceledException){break;}
            var seconds=Failures==0?Settings.Interval:Backoff(Failures);
            NextRetry=DateTimeOffset.UtcNow.AddSeconds(seconds);Changed?.Invoke();
            try{
                await signal.WaitAsync(TimeSpan.FromSeconds(seconds),stop.Token);
                // all triggers are coalesced; no more than one chain, with minimum 10 s spacing
                var delay=10000-(mono.ElapsedMilliseconds-lastPoll);if(delay>0)await Task.Delay((int)delay,stop.Token);
                while(signal.Wait(0)){}
            }catch(OperationCanceledException){break;}
        }
    }
    public static int Backoff(int failures)=>(int)Math.Min(900,90*Math.Pow(2,Math.Min(4,Math.Max(0,failures-1))));
    public async Task PollOnce(CancellationToken ct)
    {
        await pollGate.WaitAsync(ct);Busy=true;lastPoll=mono.ElapsedMilliseconds;Changed?.Invoke();
        var started=DateTimeOffset.UtcNow;
        try{
            if(forceReconnect){client?.Dispose();client=null;forceReconnect=false;gap=true;}
            if(client==null||client.Broken){
                client?.Dispose();client=null;
                var neutral=Path.Combine(Root,"neutral");
                string? lastError=null;
                foreach(var candidate in CodexLocator.Candidates(Settings.CodexPath)){
                    var test=new AppServerClient(log);
                    try{await test.Connect(candidate,neutral,ct);client=test;VerifiedPath=candidate;break;}
                    catch(OperationCanceledException){test.Dispose();throw;}
                    catch(Exception e){lastError=e is MonitorException me?me.Code:e is System.ComponentModel.Win32Exception?"PROCESS_START_DENIED":"PROCESS_START_FAILURE";test.Dispose();log.Write("connect",lastError);}
                }
                if(client==null)throw new MonitorException(lastError??"CODEX_NOT_FOUND");
                client.Notification+=name=>{
                    if(name=="account/updated"){lock(identityGate){Verified=false;Current=null;MainClock.Invalidate();ServiceTokenActivity=new{status="NOT_PROVIDED",time_zone="UNKNOWN"};usageChecked=default;gap=true;Status="帳戶來源變更，正在重新驗證";}Changed?.Invoke();}
                    if(name=="transport/closed"){MainClock.Failure();Verified=false;gap=true;Status="額度連線中斷；將自動恢復";Changed?.Invoke();Refresh();}
                    if(name=="account/updated"||!Busy)Refresh();
                };
            }
            long epoch=client.AccountEpoch;
            var before=await client.Request("account/read",new{refreshToken=false},ct);
            var identity=QuotaNormalizer.Identity(before,Settings.Salt,client.Generation);
            var quota=await client.Request("account/rateLimits/read",null,ct);
            var after=await client.Request("account/read",new{refreshToken=false},ct);
            var identityAfter=QuotaNormalizer.Identity(after,Settings.Salt,client.Generation);
            if(epoch!=client.AccountEpoch||identity!=identityAfter)throw new MonitorException("SOURCE_CONTEXT_CHANGED");
            var snapshot=QuotaNormalizer.Normalize(quota,identity.key,identity.assurance,client.Generation,client.Version,started,DateTimeOffset.UtcNow,mono.ElapsedMilliseconds);
            var rows=UsageSample.From(snapshot,gap,Settings.Interval);bool saved=false;
            lock(identityGate){if(epoch!=client.AccountEpoch)throw new MonitorException("SOURCE_CONTEXT_CHANGED");saved=History.Save(rows);Current=snapshot;MainClock.Observe(snapshot,Monotonic);Verified=true;} History.Cleanup(Settings.RetentionDays,DateTimeOffset.UtcNow);
            gap=!saved||snapshot.Windows.Any(w=>w.Quality=="SOURCE_DATA_INVALID");Failures=0;LastErrorCode="";
            Status=gap?"來源資料異常（有效窗口仍顯示）":"已更新";
            try{AtomicJson.Save(Path.Combine(Root,"last_good.json"),snapshot);}catch{Status="即時資料已取得；快取未保存";}
            var messages=alertState.Observe(rows);
            bool persisted=false;try{AtomicJson.Save(Path.Combine(Root,"notification_state.json"),alertState);persisted=true;}catch{Status="提醒狀態未保存";}
            if(Settings.Alerts&&persisted&&messages.Count>0)Alert?.Invoke(messages);
            RebuildDaily();try{Radar.Correlate(History.Query<UsageSample>(new(DateTimeOffset.UtcNow.AddHours(-48),DateTimeOffset.UtcNow)));}catch{}log.Write("poll","OK");Changed?.Invoke();
            if(DateTimeOffset.UtcNow-usageChecked>TimeSpan.FromMinutes(15)||usageIdentity!=identity.key){
                usageChecked=DateTimeOffset.UtcNow;usageIdentity=identity.key;
                ServiceTokenActivity=new{status="NOT_SUPPORTED",time_zone="UNKNOWN"};
                // Capability is pinned to the locally verified 0.153.4 schema; other versions stay explicitly unsupported.
                if(client.Version=="0.153.4"){
                    try{using var optional=CancellationTokenSource.CreateLinkedTokenSource(ct);optional.CancelAfter(3000);
                        var data=await client.Request("account/usage/read",null,optional.Token);
                        var safe=new Dictionary<string,object?>{{"status","PROVIDED"},{"time_zone","UNKNOWN"},{"as_of",DateTimeOffset.UtcNow}};
                        if(data.TryGetProperty("summary",out var summary)){
                            var numeric=new Dictionary<string,long?>();foreach(var k in new[]{"lifetimeTokens","peakDailyTokens","longestRunningTurnSec","longestStreakDays","currentStreakDays"})numeric[k]=summary.TryGetProperty(k,out var n)&&n.ValueKind==JsonValueKind.Number&&n.TryGetInt64(out var x)&&x>=0?x:null;safe["summary"]=numeric;
                        }
                        var buckets=new List<object>();if(data.TryGetProperty("dailyUsageBuckets",out var arr)&&arr.ValueKind==JsonValueKind.Array)
                            foreach(var b in arr.EnumerateArray().Take(3660))if(b.TryGetProperty("startDate",out var d)&&DateOnly.TryParse(d.GetString(),out var day)&&b.TryGetProperty("tokens",out var n)&&n.ValueKind==JsonValueKind.Number&&n.TryGetInt64(out var count)&&count>=0)buckets.Add(new{start_date=day.ToString("yyyy-MM-dd"),tokens=count});
                        safe["daily_buckets"]=buckets;ServiceTokenActivity=safe;
                    }catch{ServiceTokenActivity=new{status="NOT_PROVIDED",time_zone="UNKNOWN"};}
                }
            }
        }catch(OperationCanceledException){throw;}
        catch(Exception ex){
            MainClock.Failure();gap=true;Verified=false;Failures++;LastErrorCode=ex is MonitorException e?e.Code:ex is UnauthorizedAccessException?"POLICY_DENIED":ex is IOException?"NETWORK_ERROR":"INTERNAL_ERROR";
            Status=Explain(LastErrorCode);log.Write("poll",LastErrorCode,Failures,DateTimeOffset.UtcNow.AddSeconds(Backoff(Failures)));
        }finally{Busy=false;pollGate.Release();Changed?.Invoke();}
    }
    public async Task<string> ValidateCandidate(string path,CancellationToken ct)
    {
        if(Path.GetFullPath(path).Equals(Environment.ProcessPath,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(path).Equals("CodexUsageMonitor.exe",StringComparison.OrdinalIgnoreCase))throw new MonitorException("SELF_SOURCE_REJECTED");
        await CodexLocator.ValidateVersion(path,Path.Combine(Root,"neutral"),ct);
        await pollGate.WaitAsync(ct);
        try {
            client?.Dispose();client=null;gap=true;Verified=false;
            var test=new AppServerClient(log);
            try{await test.Connect(path,Path.Combine(Root,"neutral"),ct);
                var before=await test.Request("account/read",new{refreshToken=false},ct);var identity=QuotaNormalizer.Identity(before,Settings.Salt,test.Generation);
                var limits=await test.Request("account/rateLimits/read",null,ct);
                var after=await test.Request("account/read",new{refreshToken=false},ct);
                if(identity!=QuotaNormalizer.Identity(after,Settings.Salt,test.Generation))throw new MonitorException("SOURCE_CONTEXT_CHANGED");
                QuotaNormalizer.Normalize(limits,identity.key,identity.assurance,test.Generation,test.Version,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,Monotonic);
                return test.Version;}
            finally{test.Dispose();}
        } finally {pollGate.Release();Refresh();}
    }
    public string Validity(DateTimeOffset now)
    {
        if(Current==null)return Status;
        if((now-Current.Observed).TotalSeconds>Settings.Interval*3)return "資料過期｜"+Status;
        return Verified?Status:"上次資料，尚未驗證｜"+Status;
    }
    public static string Explain(string code)=>L.T(code switch {
        "SOURCE_NOT_FOUND" or "CODEX_NOT_FOUND"=>"State.NotFound",
        "AUTH_REQUIRED" or "AUTH_UNAUTHORIZED" or "RPC_401" or "UNAUTHORIZED"=>"State.SignIn",
        "PROTOCOL_UNSUPPORTED" or "RPC_METHOD_NOT_FOUND" or "VERSION_UNSUPPORTED" or "METHOD_UNSUPPORTED" or "INVALID_CODEX_VERSION" or "ARCHITECTURE_MISMATCH"=>"State.Unsupported",
        "SOURCE_DATA_MISSING" or "MAIN_WEEKLY_MISSING"=>"State.WeeklyMissing",
        "SOURCE_DATA_INVALID"=>"State.Error",
        "SELF_SOURCE_REJECTED" or "SOURCE_INVALID"=>"State.CheckSource",
        "TIMEOUT" or "CONNECT_TIMEOUT" or "RPC_TIMEOUT" or "REQUEST_TIMEOUT"=>"State.Offline",
        "PRODUCER_EXITED" or "EOF" or "TRANSPORT_EOF" or "TRANSPORT_FAILURE"=>"State.Protocol",
        "POLICY_DENIED"=>"State.Policy",
        "API_KEY_ONLY" or "ACCOUNT_UNSUPPORTED"=>"State.Account",
        _=>"State.Error"
    });
    public void Dispose(){Radar.Dispose();stop.Cancel();client?.Dispose();try{loop?.Wait(1000);}catch{}stop.Dispose();}
}

