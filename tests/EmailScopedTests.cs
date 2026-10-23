using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace CodexUsageMonitor;

internal static class EmailScopedTests {
 internal static void Run(Action<string,Func<bool>> check,string root){
  const string email="email-scope-canary@example.invalid";
  string salt=Convert.ToBase64String(Enumerable.Range(0,32).Select(x=>(byte)x).ToArray());
  var now=DateTimeOffset.Parse("2026-10-22T15:00:00Z");
  (string key,string assurance) Identity(string? address,string generation="email-generation-a",string plan="pro",string? id=null,string? workspace=null){
   using var json=JsonDocument.Parse(JsonSerializer.Serialize(new{account=new{type="chatgpt",email=address,planType=plan,accountId=id,workspaceId=workspace}}));
   return QuotaNormalizer.Identity(json.RootElement,salt,generation);
  }
  var first=Identity(email);var reconnected=Identity(email,"email-generation-b");
  UsageSample Sample(string id,DateTimeOffset at,decimal used,DateTimeOffset reset,string generation="email-generation-a",(string key,string assurance)? identity=null){
   var who=identity??first;
   return new(){sample_id=id,poll_id=id,request_started_utc=at,observed_at_utc=at,observed_at_local=at,account_context_key=who.key,context_assurance=who.assurance,source_generation=generation,plan_type="pro",limit_id="codex",source_slot="primary",window_duration_mins=10080,used_percent_raw=used,remaining_percent=100-used,reset_at_utc=reset,reset_at_unix_seconds=reset.ToUnixTimeSeconds(),data_quality="VALID",comparison_key=who.key+"|fixture-weekly",polling_interval_seconds=90,reset_credits_available_count=2};
  }
  var before=Sample("email-before",now.AddSeconds(-90),90,now.AddDays(3));
  var after=Sample("email-after",now,0,now.AddDays(7),"email-generation-b",reconnected);
  var confirm=Sample("email-confirm",now.AddSeconds(90),1,now.AddDays(7),"email-generation-b",reconnected);
  var useBefore=Sample("email-use-before",now.AddSeconds(-180),10,now.AddDays(3));
  var useMiddle=Sample("email-use-middle",now.AddSeconds(-90),11,now.AddDays(3),"email-generation-b",reconnected);
  var useAfter=Sample("email-use-after",now,12,now.AddDays(3),"email-generation-b",reconnected);
  check("EMAIL_SCOPED同email跨generation保留相同鍵與分級",()=>first==reconnected&&first.assurance=="EMAIL_SCOPED"&&QuotaWindow.IdentityComparable(first.assurance));
  check("EMAIL_SCOPED保留既有HMAC位元組",()=>{
   using var hmac=new HMACSHA256(Convert.FromBase64String(salt));
   string raw=email+"||pro|"+(Environment.GetEnvironmentVariable("CODEX_HOME")??"inherited-default");
   return first.key==Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
  });
  check("EMAIL_SCOPED不同email使用不同鍵",()=>Identity("other-scope@example.invalid").key!=first.key);
  check("EMAIL_SCOPED不同plan使用不同鍵",()=>Identity(email,plan:"plus").key!=first.key);
  check("EMAIL_SCOPED不同CODEX_HOME使用不同鍵",()=>{
   string? previous=Environment.GetEnvironmentVariable("CODEX_HOME");try{
    Environment.SetEnvironmentVariable("CODEX_HOME","fixture-codex-home-a");var a=Identity(email);
    Environment.SetEnvironmentVariable("CODEX_HOME","fixture-codex-home-b");var b=Identity(email);
    return a.key!=b.key;
   }finally{Environment.SetEnvironmentVariable("CODEX_HOME",previous);}
  });
  check("沒有id及非空email才使用generation連線鍵",()=>new string?[]{null,"","   "}.All(address=>{
   var a=Identity(address,"connection-a");var b=Identity(address,"connection-b");return a.assurance=="CONNECTION_ONLY"&&b.assurance=="CONNECTION_ONLY"&&a.key==Safe.Hash(salt+"connection-a")&&a.key!=b.key;
  }));
  check("有accountId時仍優先使用ACCOUNT_STABLE或STABLE_SCOPE",()=>Identity(email,id:"fixture-id").assurance=="ACCOUNT_STABLE"&&Identity(email,id:"fixture-id",workspace:"fixture-workspace").assurance=="STABLE_SCOPE"&&Identity("another@example.invalid",id:"fixture-id").key==Identity(email,id:"fixture-id").key);
  check("EMAIL_SCOPED每日用量跨generation可比且不單因身分標部分紀錄",()=>{
   var day=DailyUsageCalculator.Calculate([useMiddle,useAfter],TimeZoneInfo.Utc,useMiddle.observed_at_utc,now).Days.Single();
   return DailyUsageCalculator.Reject(useMiddle with{source_generation="email-generation-a"},useAfter).Length==0&&day.observed_drawdown_pp==1&&!day.partial&&day.source_context_assurance=="EMAIL_SCOPED";
  });
  check("EMAIL_SCOPED事件時間關係跨generation可分類",()=>{
   var events=EventDetector.Compare(before,after);return events.Any(e=>e.event_type=="EARLY_RESET")&&!events.Any(e=>e.event_type=="CONNECTION_GAP")&&events.All(e=>e.timing_relation=="OBSERVED_BEFORE_PREVIOUS_ANNOUNCEMENT"&&e.identity_assurance=="EMAIL_SCOPED");
  });
  check("EMAIL_SCOPED本機週期跨generation保留弱身分層級",()=>{
   var cycle=LocalQuotaCycle.Derive([before,after,confirm],now).Single();return LocalQuotaCycle.Continuous(before,after)&&cycle.Confirmed&&cycle.evidence_assurance=="EMAIL_SCOPED";
  });
  check("EMAIL_SCOPED燒速與圖表跨generation保留連續區段",()=>BurnForecast.Calculate([useBefore,useMiddle,useAfter],now).Rate2h>0&&TrendView.LatestSegment([useBefore,useMiddle,useAfter]).Count==3);
  check("EMAIL_SCOPED仍保留實際缺口",()=>{
   var gapped=after with{had_gap=true};var cycle=LocalQuotaCycle.Derive([before,gapped,confirm],now).First();
   return DailyUsageCalculator.Reject(before,gapped).Contains("KNOWN_GAP")&&EventDetector.Compare(before,gapped).Any(e=>e.event_type=="RESET_DURING_GAP")&&cycle.kind=="RESET_DURING_GAP"&&!cycle.Confirmed&&BurnForecast.Calculate([useBefore,useMiddle with{had_gap=true},useAfter],now).Rate==null;
  });
  check("EMAIL_SCOPED混合證據不升級成帳戶或工作區已驗證",()=>QuotaWindow.ComparisonAssurance("STABLE_SCOPE","EMAIL_SCOPED")=="EMAIL_SCOPED"&&QuotaWindow.ComparisonAssurance("EMAIL_SCOPED","ACCOUNT_STABLE")=="EMAIL_SCOPED"&&!QuotaWindow.IdentityComparable("CONNECTION_ONLY"));
  check("EMAIL_SCOPED不把不同email或plan的採樣混算",()=>{
   var other=useAfter with{account_context_key=Identity("other-scope@example.invalid").key,comparison_key="fixture-other-key"};
   return DailyUsageCalculator.Reject(useMiddle,other).Contains("RESET_OR_CONTEXT_BOUNDARY")&&EventDetector.Compare(useMiddle,other).Single().event_type=="SOURCE_CONTEXT_CHANGED"&&DailyUsageCalculator.Reject(useMiddle,useAfter with{plan_type="plus"}).Contains("RESET_OR_CONTEXT_BOUNDARY");
  });
  check("EMAIL_SCOPED Radar關聯可比較但保留EMAIL_SCOPED",()=>{
   using var radar=new ResetRadar(Path.Combine(root,"email-scoped-radar"));
   var announcement=new Announcement("email-reset-event","email-reset-revision","team-codex-reset","https://example.invalid/reset",2,"MEDIUM","測試公告","測試來源",now.AddHours(-1),null,now,"America/Los_Angeles","EXACT","ALL","GLOBAL","PLANNED","fixture-content",now.AddHours(-1),now,Eligibility:"AS_ANNOUNCED",Effect:"AUTOMATIC_QUOTA_RESET",SourceClass:"TEAM_SIGNAL_RELAY",SignalLevel:"INCOMING");
   radar.Apply([announcement],now);radar.Correlate([before,after]);var state=radar.Snapshot();
   return state.Correlations.Single().ScopeVerified&&state.Correlations.Single().Correlation=="PROBABLE"&&state.Correlations.Single().Cause=="UNKNOWN"&&state.QuotaEvidence.Single().ScopeAssurance=="EMAIL_SCOPED";
  });
  check("EMAIL_SCOPED分析匯出保留重置券變動事件",()=>{
   string output=Path.Combine(root,"email-scoped-export");using var service=new MonitorService(output);
   var creditBefore=useMiddle with{sample_id="email-credit-before",source_generation="email-generation-a"};
   var creditAfter=useAfter with{sample_id="email-credit-after",used_percent_raw=creditBefore.used_percent_raw,remaining_percent=creditBefore.remaining_percent,reset_credits_available_count=1};
   if(!service.History.Save([creditBefore,creditAfter]))return false;service.Radar.Correlate([creditBefore,creditAfter]);
   string report=ReportExporter.Export(service,new(creditBefore.observed_at_utc,now,"codex",10080),false,CancellationToken.None,Path.Combine(output,"email-analysis.log"),"zh-TW");
   var local=File.ReadLines(report).Where(line=>line.StartsWith('{')).Select(line=>JsonSerializer.Deserialize<JsonElement>(line)).Where(record=>record.TryGetProperty("data",out var data)&&data.TryGetProperty("record_type",out var type)&&type.GetString()=="LOCAL_QUOTA").Single().GetProperty("data");
   return local.GetProperty("state").GetString()=="BANKED_RESET_MOVEMENT"&&!File.ReadAllText(report).Contains(email,StringComparison.OrdinalIgnoreCase);
  });
  check("EMAIL_SCOPED原始email不寫入歷史或衍生狀態",()=>{
   string output=Path.Combine(root,"email-scoped-persistence");var history=new HistoryStore(output);if(!history.Save([before,after,confirm]))return false;
   var snapshot=new Snapshot(now,now,0,first.key,first.assurance,"email-generation-b","fixture",[new WindowQuota("codex","Codex","primary",10080,0,now.AddDays(7).ToUnixTimeSeconds(),"VALID","pro",null,null,null,"")],2,[]);
   AtomicJson.Save(Path.Combine(output,"last_good.json"),snapshot);var cycles=new LocalQuotaCycleStore(output);if(!cycles.Rebuild([before,after,confirm],new RadarState(),now))return false;
   AtomicJson.Save(Path.Combine(output,"status.v1.json"),StatusWriter.Create(snapshot,BurnForecast.Calculate([before,after],now),null,true,null,now,null));
   return Directory.GetFiles(output,"*",SearchOption.AllDirectories).All(path=>!File.ReadAllText(path).Contains(email,StringComparison.OrdinalIgnoreCase))&&!first.key.Contains('@');
  });
  var currentEmail=new Snapshot(now,now,0,first.key,"EMAIL_SCOPED","email-generation-b","fixture",[new WindowQuota("codex","Codex","primary",10080,12,now.AddDays(3).ToUnixTimeSeconds(),"VALID","pro",null,null,null,"")],2,[]);
  var legacy=new[]{useBefore with{context_assurance="CONNECTION_ONLY"},useMiddle with{context_assurance="SCOPE_NOT_FULLY_VERIFIED"},useAfter with{context_assurance="CONNECTION_ONLY"}};
  check("legacy同鍵僅在目前已驗證EMAIL時重分級且原樣本不變",()=>{
   var mapped=QuotaWindow.AnalysisSamples(legacy,currentEmail,true);
   return mapped.All(row=>row.context_assurance=="EMAIL_SCOPED")&&legacy.All(row=>row.context_assurance!="EMAIL_SCOPED")&&mapped.Zip(legacy).All(pair=>pair.First.account_context_key==pair.Second.account_context_key&&pair.First.comparison_key==pair.Second.comparison_key&&pair.First.sample_id==pair.Second.sample_id);
  });
  check("legacy未驗證或非EMAIL目前來源不升級",()=>QuotaWindow.AnalysisSamples(legacy,currentEmail,false).SequenceEqual(legacy)&&QuotaWindow.AnalysisSamples(legacy,currentEmail with{Assurance="CONNECTION_ONLY"},true).SequenceEqual(legacy)&&QuotaWindow.AnalysisSamples(legacy,null,true).SequenceEqual(legacy));
  check("legacy其他帳戶或隨機generation鍵不升級",()=>{
   var random=legacy[0] with{account_context_key=Safe.Hash(salt+"legacy-random-generation"),comparison_key="legacy-random-comparison"};
   return QuotaWindow.AnalysisSamples([random],currentEmail,true).Single()==random&&QuotaWindow.AnalysisSamples(legacy,currentEmail with{AccountKey="different-current-key"},true).SequenceEqual(legacy);
  });
  check("legacy重分級後每日事件及週期可跨generation比較",()=>{
   var mapped=QuotaWindow.AnalysisSamples(legacy,currentEmail,true);
   var reset=QuotaWindow.AnalysisSamples([before with{context_assurance="CONNECTION_ONLY"},after with{context_assurance="CONNECTION_ONLY"},confirm with{context_assurance="SCOPE_NOT_FULLY_VERIFIED"}],currentEmail,true);
   return DailyUsageCalculator.Reject(mapped[0],mapped[1]).Length==0&&!EventDetector.Compare(reset[0],reset[1]).Any(e=>e.event_type=="CONNECTION_GAP")&&LocalQuotaCycle.Derive(reset,now).Single().evidence_assurance=="EMAIL_SCOPED";
  });
  check("legacy快取及分析匯出只重分級記憶體不改CSV",()=>{
   string output=Path.Combine(root,"legacy-memory-only");using var service=new MonitorService(output);if(!service.History.Save(legacy.ToList()))return false;
   string csv=Directory.GetFiles(Path.Combine(output,"history"),"usage_*.csv").Single();string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(csv)));
   typeof(MonitorService).GetProperty("Current")!.SetValue(service,currentEmail);typeof(MonitorService).GetProperty("Verified")!.SetValue(service,true);
   if(!service.RecentSamples().All(row=>row.context_assurance=="EMAIL_SCOPED"))return false;
   string report=ReportExporter.Export(service,new(legacy[0].observed_at_utc,now,"codex",10080),false,CancellationToken.None,Path.Combine(output,"legacy-analysis.log"),"zh-TW");
   var header=File.ReadLines(report).Where(line=>line.StartsWith('{')).Select(line=>JsonSerializer.Deserialize<JsonElement>(line)).Single(record=>record.TryGetProperty("section",out var section)&&section.GetString()=="00_REPORT_HEADER"&&record.GetProperty("record_type").GetString()=="DATA").GetProperty("data");
   typeof(MonitorService).GetProperty("Verified")!.SetValue(service,false);
   return service.RecentSamples().All(row=>row.context_assurance!="EMAIL_SCOPED")&&header.GetProperty("analysis_identity_reclassification").GetProperty("sample_count").GetInt32()==legacy.Length&&Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(csv)))==hash&&Csv.Records<UsageSample>(csv).All(row=>row.context_assurance!="EMAIL_SCOPED");
  });
  check("diagnostics依保留天數清理且保留新檔與歷史追加內容",()=>{
   string output=Path.Combine(root,"diagnostic-retention");var history=new HistoryStore(output);if(!history.Save([useBefore,useMiddle]))return false;
   var original=Directory.GetFiles(Path.Combine(output,"history"),"*.csv").ToDictionary(path=>path,File.ReadAllBytes);
   string folder=Path.Combine(output,"diagnostics");Directory.CreateDirectory(folder);
   string oldPaint=Path.Combine(folder,"first_paint_2026-08-01.jsonl"),oldSession=Path.Combine(folder,"runtime-session_2026-08-01.jsonl"),newPaint=Path.Combine(folder,"first_paint_2026-10-22.jsonl"),newSession=Path.Combine(folder,"runtime-session_2026-10-21.jsonl"),metrics=Path.Combine(folder,"runtime-metrics.v1.json");
   foreach(string file in new[]{oldPaint,oldSession,newPaint,newSession,metrics}){File.WriteAllText(file,"{}\n");File.SetLastWriteTimeUtc(file,now.UtcDateTime);}
   history.Cleanup(30,now);
   if(File.Exists(oldPaint)||File.Exists(oldSession)||!File.Exists(newPaint)||!File.Exists(newSession)||!File.Exists(metrics)||!original.All(pair=>File.ReadAllBytes(pair.Key).AsSpan().SequenceEqual(pair.Value)))return false;
   if(!history.Save([useAfter]))return false;
   return original.All(pair=>File.ReadAllBytes(pair.Key).AsSpan().StartsWith(pair.Value));
  });
  check("diagnostics與logs共用容量上限且保留當日runtime回執",()=>{
   string output=Path.Combine(root,"diagnostic-cap");var history=new HistoryStore(output);if(!history.Save([useBefore]))return false;
   var original=Directory.GetFiles(Path.Combine(output,"history"),"*.csv").ToDictionary(path=>path,File.ReadAllBytes);
   string folder=Path.Combine(output,"diagnostics");Directory.CreateDirectory(folder);
   string oldSession=Path.Combine(folder,"runtime-session_2026-10-21.jsonl"),recentLog=Path.Combine(output,"logs","app_2026-10-21.jsonl"),newPaint=Path.Combine(folder,"first_paint_2026-10-22.jsonl"),metrics=Path.Combine(folder,"runtime-metrics.v1.json");
   File.WriteAllBytes(oldSession,new byte[1024]);File.SetLastWriteTimeUtc(oldSession,now.AddDays(-2).UtcDateTime);
   File.WriteAllBytes(recentLog,new byte[1024]);File.SetLastWriteTimeUtc(recentLog,now.AddDays(-1).UtcDateTime);
   foreach(string file in new[]{newPaint,metrics}){File.WriteAllBytes(file,new byte[32]);File.SetLastWriteTimeUtc(file,now.UtcDateTime);}
   long cap=original.Sum(pair=>pair.Value.LongLength)+1024+64;history.Cleanup(30,now,cap);
   return !File.Exists(oldSession)&&File.Exists(recentLog)&&File.Exists(newPaint)&&File.Exists(metrics)&&original.All(pair=>File.ReadAllBytes(pair.Key).AsSpan().SequenceEqual(pair.Value));
  });
  if(Environment.GetEnvironmentVariable("CUM_PRIVATE_BACKUP_COMPAT") is {Length:>0} backup)check("私有0.4.1備份記憶體相容fixture",()=>CheckPrivateBackup(backup,root));
 }
 static bool CheckPrivateBackup(string backup,string outputRoot){
  int filesRead=0,samples=0,matching=0,reclassified=0,comparablePairs=0,cycleEvidence=0;bool unchanged=false,pass=false;
  var hashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
  try{
   string settingsPath=Path.Combine(backup,"settings.json"),snapshotPath=Path.Combine(backup,"last_good.json");
   var usage=Directory.GetFiles(Path.Combine(backup,"history"),"usage_*.csv").Order().ToArray();
   foreach(string path in usage.Concat(Directory.GetFiles(Path.Combine(backup,"history"),"events_*.csv")).Append(settingsPath).Append(snapshotPath))hashes[path]=Hash(path);
   filesRead=hashes.Count;using var settings=JsonDocument.Parse(File.ReadAllText(settingsPath));
   string salt;if(settings.RootElement.TryGetProperty("Salt",out var plain))salt=plain.GetString()!;
   else salt=Convert.ToBase64String(ProtectedData.Unprotect(Convert.FromBase64String(settings.RootElement.GetProperty("SaltProtected").GetString()!),null,DataProtectionScope.CurrentUser));
   if(Convert.FromBase64String(salt).Length!=32)throw new InvalidDataException();
   var baseline=JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(snapshotPath))??throw new InvalidDataException();
   // This is a fixture, not live verification. Never mock an email key from a generation-only key.
   if(baseline.AccountKey==Safe.Hash(salt+baseline.Generation))throw new InvalidDataException();
   var raw=usage.SelectMany(Csv.Records<UsageSample>).OrderBy(row=>row.observed_at_utc).ToArray();samples=raw.Length;
   var assumedCurrent=baseline with{Assurance="EMAIL_SCOPED"};var analyzed=QuotaWindow.AnalysisSamples(raw,assumedCurrent,true);
   matching=raw.Count(row=>row.account_context_key==baseline.AccountKey&&(row.context_assurance is "CONNECTION_ONLY" or "SCOPE_NOT_FULLY_VERIFIED"));
   reclassified=raw.Zip(analyzed).Count(pair=>pair.First.context_assurance!=pair.Second.context_assurance);
   bool keysPreserved=raw.Zip(analyzed).All(pair=>pair.First.account_context_key==pair.Second.account_context_key&&pair.First.comparison_key==pair.Second.comparison_key&&pair.First.sample_id==pair.Second.sample_id);
   bool notRandom=raw.Where(row=>row.account_context_key==baseline.AccountKey).All(row=>row.account_context_key!=Safe.Hash(salt+row.source_generation));
   var scoped=raw.Where(row=>row.account_context_key==baseline.AccountKey&&(row.context_assurance is "CONNECTION_ONLY" or "SCOPE_NOT_FULLY_VERIFIED")).ToArray();
   for(int i=1;i<scoped.Length;i++){
    var previous=scoped[i-1];var current=scoped[i];
    if(!QuotaWindow.SameScope(previous,current)||current.had_gap||!QuotaWindow.SameReset(QuotaWindow.ResetAt(previous),QuotaWindow.ResetAt(current))||!DailyUsageCalculator.Valid(previous)||!DailyUsageCalculator.Valid(current))continue;
    double seconds=(current.observed_at_utc-previous.observed_at_utc).TotalSeconds;if(seconds<=0||seconds>3*Math.Max(1,current.polling_interval_seconds))continue;
    var pair=QuotaWindow.AnalysisSamples([previous,current with{source_generation="compat-fixture-reconnect"}],assumedCurrent,true);
    if(DailyUsageCalculator.Reject(pair[0],pair[1]).Length==0&&LocalQuotaCycle.Continuous(pair[0],pair[1])&&!EventDetector.Compare(pair[0],pair[1]).Any(e=>e.event_type=="CONNECTION_GAP"))comparablePairs++;
   }
   cycleEvidence=LocalQuotaCycle.Derive(analyzed,baseline.Observed).Count(cycle=>cycle.evidence_assurance=="EMAIL_SCOPED");
   unchanged=hashes.All(pair=>Hash(pair.Key)==pair.Value);
   pass=matching>0&&matching==reclassified&&keysPreserved&&notRandom&&comparablePairs>0&&cycleEvidence>0&&unchanged;
  }catch{pass=false;try{unchanged=hashes.Count>0&&hashes.All(pair=>Hash(pair.Key)==pair.Value);}catch{}}
  AtomicJson.Save(Path.Combine(outputRoot,"private-backup-compatibility.json"),new{classification="PRIVATE_BACKUP_RECLASSIFICATION_FIXTURE",result=pass?"PASS":"FAIL",source_files_read=filesRead,samples,matching_legacy_samples=matching,reclassified_samples=reclassified,cross_generation_fixture_pairs=comparablePairs,email_scoped_cycle_evidence=cycleEvidence,source_unchanged=unchanged,real_source_identity_verified=false});
  return pass;
 }
}
