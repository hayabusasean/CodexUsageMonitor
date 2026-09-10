
using System.Collections;
using System.Globalization;
using System.Resources;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace CodexUsageMonitor;
internal static class PublicTests {
 public static void Run(string[] args){
  int at=Array.IndexOf(args,"--test-root");string root=Path.GetFullPath(args[at+1]);Directory.CreateDirectory(root);var results=new List<object>();
  void Check(string id,Action action){try{action();results.Add(new{id,result="PASS",classification="SYNTHETIC_PRODUCTION_CODE"});}catch(Exception ex){results.Add(new{id,result="FAIL",classification="SYNTHETIC_PRODUCTION_CODE",error=ex.GetType().Name+": "+ex.Message});}AtomicJson.Save(Path.Combine(root,"public-tests-progress.json"),results);}
  void Assert(bool value,string reason="assertion"){if(!value)throw new InvalidOperationException(reason);}
  string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
  Check("L01 New English default independent of OS culture",()=>{var before=CultureInfo.CurrentCulture;try{CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("zh-TW");Assert(Settings.Load(Path.Combine(root,"new","settings.json")).UiLanguage=="en-US");}finally{CultureInfo.CurrentCulture=before;}});
  Check("L02 Legacy settings and history preserved",()=>{var dir=Path.Combine(root,"legacy");Directory.CreateDirectory(dir);var path=Path.Combine(dir,"settings.json");var history=Path.Combine(dir,"history.csv");File.WriteAllText(path,"{\"Interval\":180,\"OpacityPercent\":85,\"Compact\":true,\"X\":-100,\"Y\":40}");File.WriteAllText(history,"SYNTHETIC original history\n");string original=Hash(path),hh=Hash(history);var value=Settings.Load(path);Assert(value.UiLanguage=="zh-TW"&&value.Compact&&value.OpacityPercent==85&&value.X==-100&&value.Interval==180);Assert(Hash(history)==hh&&Hash(path)==original&&Hash(path+".pre-bilingual.bak")==original);AtomicJson.Save(path,value);Assert(Settings.Load(path).UiLanguage=="zh-TW");});
  Check("L03 Explicit invalid and wrong type language preserve other settings",()=>{
   foreach(var lang in new[]{"\"en-US\"","\"zh-TW\"","\"unknown\"","123","{}","null"}){string path=Path.Combine(root,"language-"+Guid.NewGuid().ToString("N")+".json");File.WriteAllText(path,"{\"ui_language\":"+lang+",\"OpacityPercent\":85,\"Compact\":true}");var value=Settings.Load(path);Assert(value.UiLanguage==(lang=="\"zh-TW\""?"zh-TW":"en-US")&&value.OpacityPercent==85&&value.Compact);}
  });
  Check("L02 Unwritable migration backup preserves valid settings",()=>{string path=Path.Combine(root,"backup-error.json");File.WriteAllText(path,"{\"OpacityPercent\":85,\"Compact\":true}");Directory.CreateDirectory(path+".pre-bilingual.bak");var s=Settings.Load(path);Assert(s.UiLanguage=="zh-TW"&&s.OpacityPercent==85&&s.Compact&&s.MigrationDiagnostic.Contains("BACKUP_UNAVAILABLE"));});
  Check("L08 Resource and placeholder parity in published assembly",()=>{
   foreach(string group in new[]{"Ui","History","Radar","Report"}){
    var manager=new ResourceManager("CodexUsageMonitor.Resources."+group,typeof(L).Assembly);
    var en=manager.GetResourceSet(CultureInfo.InvariantCulture,true,true)!;var zh=manager.GetResourceSet(CultureInfo.GetCultureInfo("zh-TW"),true,false)!;Assert(zh!=null,"missing zh satellite "+group);
    var a=en.Cast<DictionaryEntry>().ToDictionary(x=>(string)x.Key,x=>(string)x.Value!);var b=zh!.Cast<DictionaryEntry>().ToDictionary(x=>(string)x.Key,x=>(string)x.Value!);Assert(a.Keys.Order().SequenceEqual(b.Keys.Order()),group+" key parity");
    foreach(var (key,text) in a){string[] Parts(string v)=>Regex.Matches(v,@"(?<!\{)\{(\d+)(?:[^}]*)\}").Select(m=>m.Groups[1].Value).Distinct().Order().ToArray();Assert(Parts(text).SequenceEqual(Parts(b[key])),group+" placeholders "+key);}
   }
   Assert(L.TFor("en-US","Settings.Title")=="Settings"&&L.TFor("zh-TW","Settings.Title")=="設定");Assert(!L.T("does.not.exist").Contains("does.not.exist"));L.Missing.Clear();
  });
  Check("L04 UI culture changes preserve time and numeric culture",()=>{var current=CultureInfo.CurrentCulture;var clock=new MainQuotaClock();var snapshot=NativeR004Harness.FixtureSnapshot(91);clock.Observe(snapshot,1000);var when=clock.SuccessUtc;
   for(int i=0;i<20;i++){L.SetLanguage(i%2==0?"zh-TW":"en-US");Assert(CultureInfo.CurrentCulture==current&&clock.SuccessUtc==when&&clock.Age(24000,snapshot.Observed.AddSeconds(23))==23);}
  });
  Check("L09 Invariant CSV in four OS cultures",()=>{var original=CultureInfo.CurrentCulture;try{string? reference=null;foreach(var culture in new[]{"en-US","zh-TW","de-DE","tr-TR"}){CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(culture);var row=PublicHistoryTests.Sample(98.5m,0);string csv=Csv.Line(row);reference??=csv;Assert(csv==reference);var values=Csv.Read(new StringReader(csv)).Single();Assert(values.Contains("1.5")&&values.Contains("98.5"));}}finally{CultureInfo.CurrentCulture=original;}});
  Check("O05 No fake zero or refresh on failure",()=>{var c=new MainQuotaClock();Assert(c.Value==null&&c.SuccessUtc==null);var snap=NativeR004Harness.FixtureSnapshot(.4m);c.Observe(snap,1000);var utc=c.SuccessUtc;c.Failure();L.SetLanguage("en-US");Assert(c.Text(601000,snap.Observed.AddMinutes(10),90).Contains("10m")&&c.SuccessUtc==utc&&c.Value!.Remaining==.4m);L.SetLanguage("zh-TW");Assert(c.Text(601000,snap.Observed.AddMinutes(10),90).Contains("10 分鐘")&&c.SuccessUtc==utc);});
  Check("O03 Stable mode and notice hit zones",()=>{using var service=new MonitorService(Path.Combine(root,"hits"));using var overlay=new OverlayForm(service,true);foreach(bool compact in new[]{false,true}){service.Settings.Compact=compact;foreach(float k in new[]{1f,1.25f,1.5f}){overlay.TestScale=k;overlay.Render();Assert(overlay.ModeHit.Width>=24*k-1&&!overlay.ModeHit.IntersectsWith(overlay.RadarHit));}}});
  Check("L05 Settings preview cancel and close preserve persisted values",()=>{
   using var service=new MonitorService(Path.Combine(root,"draft"));service.Settings.UiLanguage="en-US";service.SaveSettings();L.SetLanguage("en-US");using var overlay=new OverlayForm(service,true);overlay.Show();
   using(var form=new SettingsForm(service,overlay)){form.Show();Application.DoEvents();L.SetLanguage("zh-TW");form.AllControls().OfType<OpacitySlider>().Single().Value=85;Assert(Math.Abs(overlay.Opacity-.85)<.01);form.Close();Application.DoEvents();Assert(L.Language=="en-US"&&Math.Abs(overlay.Opacity-.7)<.01&&service.Settings.UiLanguage=="en-US");}
   overlay.Close();
  });
  Check("O08 Bounded popover at all working-area edges",()=>{foreach(float k in new[]{1f,1.25f,1.5f})foreach(var anchor in new[]{new Rectangle(1900,100,20,30),new Rectangle(1800,1000,30,30),new Rectangle(-1920,100,30,30)}){var area=anchor.Left<0?new Rectangle(-1920,0,1920,1080):new Rectangle(0,0,1920,1080);var b=DailyPopover.Place(anchor,new((int)(308*k),(int)(164*k)),area);Assert(area.Contains(b));}});
  Check("P02 actionable source errors in both languages",()=>{foreach(var lang in new[]{"en-US","zh-TW"}){L.SetLanguage(lang);Assert(MonitorService.Explain("CODEX_NOT_FOUND")==L.T("State.NotFound"));Assert(MonitorService.Explain("REQUEST_TIMEOUT")==L.T("State.Offline"));Assert(MonitorService.Explain("METHOD_UNSUPPORTED")==L.T("State.Unsupported"));Assert(MonitorService.Explain("API_KEY_ONLY")==L.T("State.Account"));}});
  Check("O07 high contrast respects system colors without changing settings",()=>{try{Theme.TestHighContrast=true;Assert(Theme.Canvas==SystemColors.Window&&Theme.Text==SystemColors.WindowText);using var service=new MonitorService(Path.Combine(root,"contrast"));using var overlay=new OverlayForm(service,true);overlay.ApplySettings();Assert(overlay.Opacity==1&&service.Settings.OpacityPercent==70);}finally{Theme.TestHighContrast=null;}Assert(Theme.Canvas==Theme.Hex("#111418"));});
  Check("O05 missing main shows actionable terminal state",()=>{foreach(var lang in new[]{"en-US","zh-TW"}){L.SetLanguage(lang);Assert(OverlayForm.MissingMainAge("AUTH_REQUIRED",false)==L.T("Overlay.SignInRequired"));Assert(OverlayForm.MissingMainAge("",true)==L.T("Overlay.WeeklyUnavailable"));Assert(OverlayForm.MissingMainAge("REQUEST_TIMEOUT",false)==L.T("Overlay.Offline"));Assert(OverlayForm.MissingMainAge("",false)==L.T("Overlay.Connecting"));}});
  PublicHistoryTests.Run(Check);
  results.AddRange(Task.Run(()=>PublicRadarTests.Run(Path.Combine(root,"radar"))).GetAwaiter().GetResult());
  PublicReportTests.Run(["--test-root",Path.Combine(root,"reports")]);using(var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"reports","public-report-tests.json")))){var element=doc.RootElement;var list=element.ValueKind==JsonValueKind.Array?element:element.GetProperty("results");foreach(var item in list.EnumerateArray())results.Add(item.Clone());}
  L.SetLanguage("en-US");AtomicJson.Save(Path.Combine(root,"public-tests.json"),new{classification="SYNTHETIC_PRODUCTION_CODE",exe_sha256=ReportExporter.ExeHash(),version=L.Version,results});
  Environment.ExitCode=results.Select(x=>JsonSerializer.SerializeToElement(x)).Any(x=>x.GetProperty("result").GetString()=="FAIL")?1:0;
 }
}

