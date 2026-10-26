using System.Text.Json;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
namespace CodexUsageMonitor;
internal static class V050IntegrationTests
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true,ExactSpelling=true)]
    static extern uint GetShortPathNameW(string longPath,StringBuilder shortPath,uint length);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true,ExactSpelling=true)]
    static extern uint QueryDosDeviceW(string name,StringBuilder target,uint length);
    static string ShortPath(string path){var buffer=new StringBuilder(32768);uint n=GetShortPathNameW(path,buffer,(uint)buffer.Capacity);if(n==0||n>=buffer.Capacity)throw new Win32Exception(Marshal.GetLastWin32Error());return buffer.ToString();}
    static string? DriveTarget(string drive){var buffer=new StringBuilder(32768);if(QueryDosDeviceW(drive,buffer,(uint)buffer.Capacity)>0)return buffer.ToString();int error=Marshal.GetLastWin32Error();if(error==2)return null;throw new Win32Exception(error);}
    static void Subst(string drive,string target){using var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"subst.exe")){UseShellExecute=false,CreateNoWindow=true,ArgumentList={drive,target},RedirectStandardOutput=true,RedirectStandardError=true})!;string output=process.StandardOutput.ReadToEnd()+process.StandardError.ReadToEnd();process.WaitForExit();if(process.ExitCode!=0)throw new InvalidOperationException("SUBST測試指令失敗："+output);}
    static string? WithSubst(string target,Action<string> action){
        string? drive=null;for(char letter='Z';letter>='G';letter--){string candidate=letter+":";if(DriveTarget(candidate)==null&&!Directory.Exists(candidate+@"\")){drive=candidate;break;}}
        if(drive==null)return "沒有空閒磁碟代號；未變更既有磁碟對映。";
        string expected=@"\??\"+Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));bool created=false;
        try{Subst(drive,target);created=true;if(!string.Equals(DriveTarget(drive),expected,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("新增SUBST對映與隔離目標不同");action(drive+@"\");}
        finally{if(created){if(!string.Equals(DriveTarget(drive),expected,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("SUBST測試對映已改變，保留現況且不移除他人對映");Subst(drive,"/D");}}
        return null;
    }
    internal static void Run(string root)
    {
        Directory.CreateDirectory(root);var results=new List<object>();int failed=0;
        void Check(string name,Action action){try{action();results.Add(new{name,result="PASS"});}catch(Exception e){failed++;results.Add(new{name,result="FAIL",error=e.ToString()});}}
        void CheckOptional(string name,Func<string?> action){try{string? reason=action();if(reason==null)results.Add(new{name,result="PASS"});else results.Add(new{name,result="N/A",reason});}catch(Exception e){failed++;results.Add(new{name,result="FAIL",error=e.ToString()});}}
        void Assert(bool condition){if(!condition)throw new InvalidOperationException("驗證失敗");}
        void Reserved(string path){try{RuntimeEnvironment.DataRoot(path);}catch(IOException e)when(e.Message=="DATA_ROOT_RESERVED"){return;}throw new InvalidOperationException("未拒絕保留資料夾或其上層："+path);}
        var now=DateTimeOffset.Parse("2026-10-22T15:01:00Z");
        var q=new WindowQuota("codex","Codex","primary",10080,61,now.AddHours(112.7).ToUnixTimeSeconds(),"VALID","pro",null,null,null,"");
        var snapshot=new Snapshot(now,now,0,"private-account-hash","ACCOUNT_STABLE","test","test",[q],2,[now.AddDays(2).ToUnixTimeSeconds()]);
        var sample=UsageSample.From(snapshot,false,90).Single();
        var announcement=new Announcement("event","revision","Tibo via codex-reset.com","https://x.com/thsottiaux/status/2113162413008884099",2,"MEDIUM","來源說法","",now,null,now.AddHours(2),"America/Los_Angeles","DEADLINE","ALL","GLOBAL","PLANNED","hash",now,now,SignalLevel:"INCOMING");
        var weekly=new ForecastProjection(now.AddHours(112.7),112.7,100,now.AddHours(9.75),39/112.7);
        var announced=new ForecastProjection(now.AddHours(2),2,69,now.AddHours(9.75),19.5);
        var f=new BurnForecastResult(sample,"ACTIVE",now.AddHours(112.7).AddDays(-7),4,4,3.2,now.AddMinutes(-8),null,"OK",weekly,announced,announcement);
        Check("status欄位完整且沒有帳號識別資料",()=>{
            string raw=JsonSerializer.Serialize(StatusWriter.Create(snapshot,f,announcement,true,null,now,QuotaAdvice.Evaluate(f,snapshot,announcement,now).FirstOrDefault()));
            Assert(!raw.Contains(snapshot.AccountKey)&&!raw.Contains("context_key")&&!raw.Contains("Salt"));using var d=JsonDocument.Parse(raw);Assert(d.RootElement.GetProperty("schema").GetString()=="codex-usage-status.v1"&&d.RootElement.GetProperty("burn").GetProperty("rate_2h_pct_per_hour").GetDouble()==4);
        });
        Check("公告重置、提早耗盡、有券、券到期與保留券提醒",()=>{var codes=QuotaAdvice.Evaluate(f,snapshot,announcement,now).Select(x=>x.Code).ToArray();Assert(codes.Contains("ANNOUNCED_RESET_SOON_BURN")&&codes.Contains("EXHAUST_BEFORE_RESET_WITH_CREDITS")&&codes.Contains("CREDIT_EXPIRING")&&codes.Contains("PRESERVE_CREDITS_BEFORE_GLOBAL_RESET"));});
        Check("每週重置前仍剩很多的提醒",()=>{var near=f with{Weekly=new(now.AddHours(3),3,73,null,13)};Assert(QuotaAdvice.Evaluate(near,snapshot,announcement,now).Any(x=>x.Code=="WEEKLY_RESET_UNDERUSED"));});
        Check("同條件6小時節流，重開程式仍保留",()=>{var state=new AdviceNotificationState();var advice=new[]{new Advice("test","測試通知")};Assert(state.Observe(advice,"account",now).Count==1&&state.Observe(advice,"account",now.AddHours(5)).Count==0);string path=Path.Combine(root,"notification.json");AtomicJson.Save(path,state);state=AtomicJson.Load(path,()=>new AdviceNotificationState());Assert(state.Observe(advice,"account",now.AddHours(6)).Count==1);});
        Check("過期資料不發用量預測提醒",()=>Assert(QuotaAdvice.Evaluate(f with{Confidence="STALE"},snapshot,announcement,now).Count==0));
        Check("atomic JSON內容不變不重寫且不縮排",()=>{string path=Path.Combine(root,"unchanged.json");AtomicJson.Save(path,new{value=42});var stamp=File.GetLastWriteTimeUtc(path);long bytes=AtomicJson.BytesWritten;AtomicJson.Save(path,new{value=42});Assert(File.GetLastWriteTimeUtc(path)==stamp&&AtomicJson.BytesWritten==bytes&&!File.ReadAllText(path).Contains('\n'));});
        Check("DPAPI保存salt，舊salt可無損遷移",()=>{string path=Path.Combine(root,"salt-settings.json");string salt=Convert.ToBase64String(Enumerable.Range(0,32).Select(x=>(byte)x).ToArray());File.WriteAllText(path,JsonSerializer.Serialize(new{settings_version=2,ui_language="zh-TW",Salt=salt,Interval=90}));var s=Settings.Load(path);Assert(s.Salt==salt);AtomicJson.Save(path,s);string text=File.ReadAllText(path);Assert(!text.Contains(salt)&&!text.Contains("\"Salt\":"));Assert(Settings.Load(path).Salt==salt);});
        Check("DPAPI解密失敗保留設定且禁止默默換salt",()=>{string data=Path.Combine(root,"locked-salt");Directory.CreateDirectory(data);string path=Path.Combine(data,"settings.json");File.WriteAllText(path,"{\"SaltProtected\":\"AAAA\",\"Interval\":180}");string before=File.ReadAllText(path);using var service=new MonitorService(data);Assert(!service.Settings.SaltAvailable&&service.Settings.Interval==180&&!service.SaveSettings()&&File.ReadAllText(path)==before);});
        Check("空值與錯誤型態的DPAPI設定也不覆寫",()=>{foreach(var value in new[]{"null","42","{}","\"\""}){string data=Path.Combine(root,"bad-salt-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);string path=Path.Combine(data,"settings.json");File.WriteAllText(path,"{\"SaltProtected\":"+value+",\"Interval\":180}");string before=File.ReadAllText(path);using var service=new MonitorService(data);Assert(!service.Settings.SaltAvailable&&!service.SaveSettings()&&File.ReadAllText(path)==before);}});
        Check("CSV欄位順序與既有schema完全相容",()=>{string fixture=Path.Combine(AppContext.BaseDirectory,"fixtures","usage-replay","usage_2026-10-17.csv");using var fixtureReader=File.OpenText(fixture);Assert(Csv.Read(new StringReader(Csv.Header<UsageSample>())).Single().SequenceEqual(Csv.Read(fixtureReader).First()));Assert(sample.schema_version=="1"&&new UsageEvent().schema_version=="1");});
        Check("沒有accountId時以EMAIL_SCOPED辨識前後帳戶但不宣稱帳戶已驗證",()=>{var salt=Convert.ToBase64String(new byte[32]);using var first=JsonDocument.Parse("{\"account\":{\"type\":\"chatgpt\",\"email\":\"fixture-a@example.invalid\",\"planType\":\"pro\"}}");using var second=JsonDocument.Parse("{\"account\":{\"type\":\"chatgpt\",\"email\":\"fixture-b@example.invalid\",\"planType\":\"pro\"}}");var a=QuotaNormalizer.Identity(first.RootElement,salt,"fixture");var b=QuotaNormalizer.Identity(second.RootElement,salt,"fixture");Assert(a.key!=b.key&&a.assurance=="EMAIL_SCOPED"&&!a.key.Contains("fixture-a"));});
        Check("唯讀RPC白名單保持四項",()=>{foreach(var method in new[]{"initialize","account/read","account/rateLimits/read","account/usage/read"})Assert(AppServerClient.IsAllowed(method));foreach(var method in new[]{"thread/start","turn/start","account/resetCredits/redeem","model/turn"})Assert(!AppServerClient.IsAllowed(method));});
        Check("未簽章執行檔不被信任",()=>{try{CodexTrust.Verify(Environment.ProcessPath!,root);throw new InvalidOperationException("未拒絕");}catch(MonitorException e){Assert(e.Code=="CODEX_SIGNATURE_INVALID");}});
        Check("成功後失敗status保留數值",()=>{string data=Path.Combine(root,"status-data");AtomicJson.Save(Path.Combine(data,"last_good.json"),snapshot);using var service=new MonitorService(data);service.WriteStatus(now,true);using var before=JsonDocument.Parse(File.ReadAllText(Path.Combine(data,"status.v1.json")));service.WriteStatus(now.AddMinutes(5),false);using var after=JsonDocument.Parse(File.ReadAllText(Path.Combine(data,"status.v1.json")));Assert(!after.RootElement.GetProperty("ok").GetBoolean());Assert(JsonElement.DeepEquals(before.RootElement.GetProperty("weekly"),after.RootElement.GetProperty("weekly"))&&JsonElement.DeepEquals(before.RootElement.GetProperty("projection"),after.RootElement.GetProperty("projection"))&&after.RootElement.GetProperty("burn").GetProperty("confidence").GetString()=="STALE");});
        Check("採樣保存後七日快取立即更新且去重",()=>{using var service=new MonitorService(Path.Combine(root,"ring"));var current=sample with{observed_at_utc=DateTimeOffset.UtcNow,sample_id="ring-sample"};Assert(service.History.Save([current])&&service.RecentSamples().Count==1);service.History.Save([current]);Assert(service.RecentSamples().Count==1);});
        Check("沒有目前週額度時不沿用舊預測",()=>{string data=Path.Combine(root,"no-weekly");AtomicJson.Save(Path.Combine(data,"last_good.json"),snapshot);using var service=new MonitorService(data);Assert(service.Forecast!=null);typeof(MonitorService).GetProperty("Current")!.SetValue(service,snapshot with{Windows=[q with{Duration=300}]});service.RebuildForecast(now);Assert(service.Forecast==null);});
        Check("衍生cache錯誤不影響已提交CSV",()=>{var store=new HistoryStore(Path.Combine(root,"observer"));store.SamplesSaved+=_=>throw new InvalidOperationException();Assert(store.Save([sample]));Assert(store.Query<UsageSample>(new(now.AddDays(-1),now.AddDays(1))).Count()==1);});
        Check("一小時合成輪詢寫入量低於3MB",()=>{
            string data=Path.Combine(root,"hour-write");using var service=new MonitorService(data);var start=DateTimeOffset.UtcNow.AddHours(-1);long initial=AtomicJson.BytesWritten;
            for(int i=0;i<60;i++){var at=start.AddMinutes(i);var s=sample with{sample_id="hour-"+i,poll_id="hour-"+i,observed_at_utc=at,request_started_utc=at,observed_at_local=at.ToLocalTime(),used_percent_raw=61+i/15,remaining_percent=39-i/15};service.History.Save([s]);typeof(MonitorService).GetProperty("Current")!.SetValue(service,snapshot with{Observed=at,PollId="hour-"+i,Windows=[q with{Used=s.used_percent_raw}]});service.RebuildDaily();service.RebuildLocalCycles();service.Radar.Correlate(service.RecentSamples());service.WriteStatus(at,true);AtomicJson.Save(Path.Combine(data,"last_good.json"),snapshot with{Observed=at,PollId="hour-"+i});}
            long json=AtomicJson.BytesWritten-initial;long other=Directory.GetFiles(data,"*.csv",SearchOption.AllDirectories).Sum(p=>new FileInfo(p).Length)+Directory.GetFiles(data,"*.jsonl",SearchOption.AllDirectories).Sum(p=>new FileInfo(p).Length);
            AtomicJson.Save(Path.Combine(root,"hour-write-result.json"),new{classification="SIMULATED_60_POLLS_NOT_WALL_CLOCK",json_bytes=json,append_bytes=other,total=json+other,threshold=3_000_000});Assert(json+other<3_000_000);
        });
        Check("正式資料目錄預設不變",()=>Assert(RuntimeEnvironment.DataRoot(null)==Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexUsageMonitor")));
        Check("正式資料目錄接受獨立絕對路徑",()=>{string profile=Path.GetFullPath(Path.Combine(root,"profile"));Assert(RuntimeEnvironment.DataRoot(profile)==Path.TrimEndingDirectorySeparator(profile));});
        Check("正式資料目錄拒絕相對路徑及磁碟根目錄",()=>{foreach(string value in new[]{"relative-profile",Path.GetPathRoot(Path.GetFullPath(root))!}){bool rejected=false;try{RuntimeEnvironment.DataRoot(value);}catch(IOException){rejected=true;}Assert(rejected);}});
        Check("正式資料目錄拒絕正式資料與CODEX_HOME",()=>{
            string codexHome=Environment.GetEnvironmentVariable("CODEX_HOME")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
            foreach(string value in new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexUsageMonitor"),codexHome}){bool rejected=false;try{RuntimeEnvironment.DataRoot(value);}catch(IOException){rejected=true;}Assert(rejected);}
        });
        Check("正式資料目錄拒絕保留資料夾的上層",()=>{
            string standard=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexUsageMonitor");Reserved(Path.GetDirectoryName(standard)!);Reserved(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            string? previous=Environment.GetEnvironmentVariable("CODEX_HOME");try{string reserved=Path.Combine(Path.GetFullPath(root),"ancestor-profile","reserved");Environment.SetEnvironmentVariable("CODEX_HOME",reserved);Reserved(Path.GetDirectoryName(reserved)!);Reserved(Path.Combine(reserved,"new-child"));}finally{Environment.SetEnvironmentVariable("CODEX_HOME",previous);}
        });
        Check("預設資料目錄的執行鎖維持舊版名稱",()=>{string standard=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexUsageMonitor");Assert(RuntimeIdentity.CanonicalRoot(standard)==standard);});
        CheckOptional("8.3短名展開並拒絕保留資料及其上層",()=>{
            string profile=Path.Combine(Path.GetFullPath(root),"short-name-profile-with-long-name");Directory.CreateDirectory(profile);string alias=ShortPath(profile);
            if(string.Equals(alias,profile,StringComparison.OrdinalIgnoreCase))return "隔離測試磁碟沒有提供8.3短名；未變更磁碟設定。";
            Assert(RuntimeIdentity.CanonicalRoot(alias)==RuntimeIdentity.CanonicalRoot(profile));Assert(RuntimeIdentity.CanonicalRoot(Path.Combine(alias,"not-created","child"))==RuntimeIdentity.CanonicalRoot(Path.Combine(profile,"not-created","child")));
            string? previous=Environment.GetEnvironmentVariable("CODEX_HOME");try{Environment.SetEnvironmentVariable("CODEX_HOME",profile);Reserved(alias);Reserved(Path.Combine(alias,"new-child"));Reserved(Path.GetDirectoryName(alias)!);}finally{Environment.SetEnvironmentVariable("CODEX_HOME",previous);}return null;
        });
        CheckOptional("既存正式資料的8.3別名唯讀拒絕且共用舊版鎖",()=>{
            string standard=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexUsageMonitor");if(!Directory.Exists(standard))return "正式資料夾尚不存在；未為測試建立正式資料。";string alias=ShortPath(standard);
            if(string.Equals(alias,standard,StringComparison.OrdinalIgnoreCase))return "既存正式資料沒有可用8.3別名；未變更磁碟設定。";
            Reserved(alias);Reserved(Path.Combine(alias,"not-created","child"));Reserved(Path.GetDirectoryName(alias)!);Assert(RuntimeIdentity.CanonicalRoot(alias)==standard);return null;
        });
        CheckOptional("SUBST展開、保留範圍雙向拒絕與同資料夾共用鎖",()=>{
            string mount=Path.Combine(Path.GetFullPath(root),"subst-isolated-target");string reserved=Path.Combine(mount,"profiles","reserved");string safe=Path.Combine(mount,"safe-profile");Directory.CreateDirectory(reserved);Directory.CreateDirectory(safe);
            return WithSubst(mount,alias=>{string? previous=Environment.GetEnvironmentVariable("CODEX_HOME");try{Environment.SetEnvironmentVariable("CODEX_HOME",reserved);Reserved(Path.Combine(alias,"profiles","reserved"));Reserved(Path.Combine(alias,"profiles","reserved","not-created","child"));Reserved(Path.Combine(alias,"profiles"));string mapped=Path.Combine(alias,"safe-profile");Assert(string.Equals(RuntimeEnvironment.DataRoot(mapped),RuntimeEnvironment.PhysicalPath(safe),StringComparison.OrdinalIgnoreCase));Assert(RuntimeIdentity.CanonicalRoot(mapped)==RuntimeIdentity.CanonicalRoot(safe));Assert(RuntimeIdentity.CanonicalRoot(Path.Combine(mapped,"not-created","child"))==RuntimeIdentity.CanonicalRoot(Path.Combine(safe,"not-created","child")));}finally{Environment.SetEnvironmentVariable("CODEX_HOME",previous);}});
        });
        CheckOptional("正式資料上層的SUBST別名只讀拒絕且共用舊版鎖",()=>{
            string standard=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexUsageMonitor");string parent=Path.GetDirectoryName(standard)!;
            return WithSubst(parent,alias=>{string mapped=Path.Combine(alias,"CodexUsageMonitor");Reserved(mapped);Reserved(Path.Combine(mapped,"not-created","child"));Assert(RuntimeIdentity.CanonicalRoot(mapped)==standard);});
        });
        Check("正式資料目錄拒絕device與網路路徑",()=>{
            foreach(string value in new[]{@"\\?\C:\Users\Public\profile",@"\\.\C:\profile",@"\??\C:\profile",@"\\server\share\profile","//server/share/profile"}){
                bool rejected=false;try{RuntimeEnvironment.DataRoot(value);}catch(IOException){rejected=true;}Assert(rejected);
            }
        });
        Check("正式資料目錄允許一般本機資料夾及尚未建立的子資料夾",()=>{
            string parent=Path.Combine(Path.GetFullPath(root),"normal-profile");Directory.CreateDirectory(parent);string fresh=Path.Combine(parent,"not-created","child");
            Assert(RuntimeEnvironment.DataRoot(parent)==parent&&RuntimeEnvironment.DataRoot(fresh)==fresh);
        });
        Check("正式資料目錄拒絕既存junction及其不存在子目錄",()=>{
            string basePath=Path.Combine(Path.GetFullPath(root),"junction-profile");string target=Path.Combine(basePath,"target");string link=Path.Combine(basePath,"alias");Directory.CreateDirectory(target);
            using var create=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe"){
                UseShellExecute=false,CreateNoWindow=true,ArgumentList={"/d","/c","mklink","/J",link,target},RedirectStandardOutput=true,RedirectStandardError=true
            })!;
            create.WaitForExit();Assert(create.ExitCode==0&&(File.GetAttributes(link)&FileAttributes.ReparsePoint)!=0);
            foreach(string value in new[]{link,Path.Combine(link,"not-created","child")}){
                bool rejected=false;try{RuntimeEnvironment.DataRoot(value);}catch(IOException){rejected=true;}Assert(rejected);
            }
        });
        Check("正式執行診斷包含來源EXE與內容寫入量",()=>{
            string data=Path.Combine(root,"runtime-metrics");using(var metrics=new RuntimeSessionMetrics(data)){AtomicJson.Save(Path.Combine(data,"state.json"),new{value=42});Directory.CreateDirectory(Path.Combine(data,"history"));Directory.CreateDirectory(Path.Combine(data,"radar_history"));File.AppendAllText(Path.Combine(data,"history","rows.csv"),"a,b\n");File.AppendAllText(Path.Combine(data,"radar_history","coverage.jsonl"),"{}\n");File.AppendAllText(Path.Combine(data,"unrelated.csv"),new string('x',100_000));}
            using var result=JsonDocument.Parse(File.ReadAllText(Path.Combine(data,"diagnostics","runtime-metrics.v1.json")));var entry=result.RootElement;
            long diagnosticsBytes=Directory.GetFiles(Path.Combine(data,"diagnostics"),"*.jsonl").Sum(p=>new FileInfo(p).Length);Assert(entry.GetProperty("phase").GetString()=="PROCESS_EXIT"&&entry.GetProperty("exe_sha256").GetString()!.Length==64&&entry.GetProperty("json_rewrite_bytes").GetInt64()>0&&entry.GetProperty("append_growth_bytes").GetInt64()==diagnosticsBytes+7&&entry.GetProperty("current_receipt_excluded").GetBoolean());
            using var boot=JsonDocument.Parse(File.ReadAllLines(Directory.GetFiles(Path.Combine(data,"diagnostics"),"runtime-session_*.jsonl").Single()).Single());Assert(boot.RootElement.GetProperty("data_root").GetString()==Path.GetFullPath(data));
        });
        Check("REVIEW_01 repeated sample within one batch is written once",()=>{
            var store=new HistoryStore(Path.Combine(root,"review-duplicate-batch"));
            Assert(store.Save([sample,sample,sample]));
            var rows=store.Query<UsageSample>(new(now.AddDays(-1),now.AddDays(1))).ToList();
            Assert(rows.Count==1&&rows[0].sample_id==sample.sample_id);
        });
        Check("REVIEW_02 duplicate batch stays deduplicated after reopening",()=>{
            string folder=Path.Combine(root,"review-duplicate-reopen");
            var store=new HistoryStore(folder);Assert(store.Save([sample,sample]));
            var reopened=new HistoryStore(folder);reopened.Recover();Assert(reopened.Save([sample,sample]));
            Assert(reopened.Query<UsageSample>(new(now.AddDays(-1),now.AddDays(1))).Count()==1);
        });
        Check("REVIEW_03 API-key-only accounts are refused",()=>{
            foreach(var kind in new[]{"apiKey","apikey"}){
                using var response=JsonDocument.Parse(JsonSerializer.Serialize(new{account=new{type=kind}}));
                bool refused=false;try{QuotaNormalizer.Identity(response.RootElement,Convert.ToBase64String(new byte[32]),"test");}
                catch(MonitorException e){refused=e.Code=="API_KEY_ONLY";}Assert(refused);
            }
        });
        Check("REVIEW_04 model purchase reset and login requests remain refused",()=>{
            foreach(var method in new[]{"turn/start","thread/start","account/rateLimitResetCredit/consume","account/sendAddCreditsNudgeEmail","account/login/start","account/logout","command/exec","config/value/write"})Assert(!AppServerClient.IsAllowed(method));
        });
        AtomicJson.Save(Path.Combine(root,"integration-v050-tests.json"),new{results,failed});Environment.ExitCode=failed>0?1:0;
    }
}






