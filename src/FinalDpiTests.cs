using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace CodexUsageMonitor;
internal static class FinalDpiTests {
 [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint id);
 [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr obj,int index,StringBuilder text,uint size,out uint needed);
 [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
 static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
 static JsonElement EnvironmentRecord(string file)=>File.ReadLines(file).Where(x=>x.StartsWith("{")).Select(x=>JsonSerializer.Deserialize<JsonElement>(x)).Single(x=>x.GetProperty("record_type").GetString()=="DATA"&&x.GetProperty("section").GetString()=="02_ENVIRONMENT").GetProperty("data");
 public static void Run(string root){
  Directory.CreateDirectory(root);var desktop=new StringBuilder(256);GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()),2,desktop,512,out _);
  if(!desktop.ToString().StartsWith("CUM_FINAL_QA_",StringComparison.Ordinal))throw new InvalidOperationException("Native QA must run on the isolated non-input desktop.");
  var results=new List<object>();
  void Check(string id,bool success,object? detail=null){results.Add(new{id,result=success?"PASS":"FAIL",detail});AtomicJson.Save(Path.Combine(root,"final-dpi-checks.json"),results);if(!success)throw new Exception(id);}
  string dataRoot=Path.Combine(root,"synthetic-data");Directory.CreateDirectory(dataRoot);var now=DateTimeOffset.UtcNow;
  var quota=new WindowQuota("codex","Codex","primary",10080,57,now.AddDays(7).ToUnixTimeSeconds(),"VALID","pro",null,null,null,"");
  AtomicJson.Save(Path.Combine(dataRoot,"last_good.json"),new Snapshot(now,now,0,"SYNTHETIC","STABLE_SCOPE","SYNTHETIC","SYNTHETIC_NO_PRODUCER",[quota],2,[]));
  using var service=new MonitorService(dataRoot);var range=new HistoryFilter(now.AddDays(-1),now.AddDays(1));
  using var host=new Form{ShowInTaskbar=false,WindowState=FormWindowState.Minimized};
  host.Shown+=async(_,_)=>{
   try{
    NativeR003Harness.LastDpi=777;
    using(var unshown=new ExportForm(service,range)){
     var observation=UiDpi.Capture(unshown);Check("F05 no handle does not fabricate DPI",!unshown.IsHandleCreated&&observation.Value==null);
    }
    string isolated=ReportExporter.Export(service,range,false,CancellationToken.None,Path.Combine(root,"synthetic-no-ui.log"));
    var absent=EnvironmentRecord(isolated);Check("F05 direct isolated export uses null UNKNOWN despite poisoned old harness",absent.GetProperty("dpi").ValueKind==JsonValueKind.Null&&absent.GetProperty("dpi_source").GetString()=="UNKNOWN_NO_UI_HANDLE");
    var notices=new Dictionary<string,Announcement>();
    foreach(var phrase in new[]{("future","will be applied tomorrow around 6 PM PT"),("completed","has been applied"),("started","has begun")}){
     string html="<article id=\""+phrase.Item1+"\"><header><h2>DEMO Codex reset</h2><time datetime=\""+now.ToString("O",CultureInfo.InvariantCulture)+"\">Published</time></header><p>Codex global reset for all paid plans "+phrase.Item2+".</p></article>";
     File.WriteAllText(Path.Combine(root,phrase.Item1+".html"),html,new UTF8Encoding(false));var parsed=AnnouncementParser.Parse("synthetic-final","https://learn.chatgpt.com/docs/changelog",html,now,out _);
     Check("F04 formal parser "+phrase.Item1,parsed.Count==1,new{entries=parsed});notices[phrase.Item1]=parsed.Single();
    }
    service.Radar.Apply(notices.Values,now);
    foreach(string lang in new[]{"en-US","zh-TW"}){
     L.SetLanguage(lang);
     foreach(var pair in notices){
      using var card=new ResetInfoForm(service,pair.Value);card.Show();card.Text="DEMO · SYNTHETIC — "+card.Text;await Task.Delay(100);card.Refresh();card.Update();
      string expected=ResetRadar.SuggestionKey(pair.Value,43,2,false,DateTimeOffset.UtcNow,"pro");
      Check("F04 bilingual card "+pair.Key+" "+lang,card.AllControls().OfType<Label>().Any(x=>x.Text==L.T("Radar."+expected))&&(pair.Key!="future"||expected!="SuggestCompleted"),new{phase=pair.Value.Phase,suggestion_key=expected,language=lang,dpi=UiDpi.Capture(card)});
      using var bmp=new Bitmap(card.Width,card.Height);using(var graphics=Graphics.FromImage(bmp)){var dc=graphics.GetHdc();try{Check("Native PrintWindow "+pair.Key+" "+lang,PrintWindow(card.Handle,dc,2));}finally{graphics.ReleaseHdc(dc);}}
      string file=Path.Combine(root,"card-"+pair.Key+"-"+lang+".png");bmp.Save(file,System.Drawing.Imaging.ImageFormat.Png);
      AtomicJson.Save(Path.ChangeExtension(file,"json"),new{classification="ACTUAL_NATIVE_PRINTWINDOW_NON_INPUT_DESKTOP",desktop=desktop.ToString(),switched_to_input_desktop=false,synthetic=true,exe_sha256=ReportExporter.ExeHash(),dpi=UiDpi.Capture(card),png_sha256=Hash(File.ReadAllBytes(file)),phase=pair.Value.Phase,suggestion_key=expected,ui_language=lang});card.Close();
     }
     using var export=new ExportForm(service,range);export.ReportLanguage=lang;export.Show();await Task.Delay(100);
     var actual=UiDpi.Capture(export);Check("F05 native window reports144 "+lang,actual.Value==144,new{actual,winforms_device_dpi=export.DeviceDpi});
     await export.RunExport(Path.Combine(root,"synthetic-analysis-"+lang+".log"));Check("Formal native ExportForm completed "+lang,export.LastExport!=null);
     var env=EnvironmentRecord(export.LastExport!);Check("F05 real exported DPI144 independent of legacy harness "+lang,env.GetProperty("dpi").GetInt32()==actual.Value&&env.GetProperty("dpi_source").GetString()=="UI_WINDOW_GET_DPI_FOR_WINDOW",new{exported=env,legacy_harness_dpi=NativeR003Harness.LastDpi});
     VerifyIntegrity(export.LastExport!,lang,Check);export.Close();
    }
    string after=ReportExporter.Export(service,range,false,CancellationToken.None,Path.Combine(root,"synthetic-after-ui.log"));Check("F05 no global DPI leakage after native exports",EnvironmentRecord(after).GetProperty("dpi").ValueKind==JsonValueKind.Null);
    Check("No synthetic producer or model calls",service.ChildId==0&&!service.Verified);
    AtomicJson.Save(Path.Combine(root,"complete.json"),new{version=L.Version,exe_sha256=ReportExporter.ExeHash(),desktop=desktop.ToString(),input_desktop_switched=false,native_frames=6,actual_dpi=144,checks=results.Count,visual_review="PENDING",model_calls=0,actual_os=Environment.OSVersion.VersionString});
   }catch(Exception ex){AtomicJson.Save(Path.Combine(root,"failure.json"),new{error=ex.ToString(),exe_sha256=ReportExporter.ExeHash()});Environment.ExitCode=1;}
   finally{host.Close();}
  };
  Application.Run(host);
 }
 static void VerifyIntegrity(string file,string lang,Action<string,bool,object?> check){
  var lines=File.ReadAllLines(file);var rows=lines.Where(x=>x.StartsWith("{")).Select(x=>JsonSerializer.Deserialize<JsonElement>(x)).ToList();var integrity=rows.Single(x=>x.GetProperty("record_type").GetString()=="DATA"&&x.GetProperty("section").GetString()=="15_INTEGRITY").GetProperty("data");bool valid=true;
  var counts=integrity.GetProperty("section_record_counts");valid&=counts.EnumerateObject().Count()==22;
  foreach(var section in counts.EnumerateObject()){
   var data=lines.Where(line=>{if(!line.StartsWith("{"))return false;var x=JsonSerializer.Deserialize<JsonElement>(line);return x.GetProperty("record_type").GetString()=="DATA"&&x.GetProperty("section").GetString()==section.Name;}).ToArray();valid&=data.Length==section.Value.GetInt32();if(section.Name!="15_INTEGRITY")valid&=Hash(Encoding.UTF8.GetBytes(string.Concat(data.Select(x=>x+"\n"))))==integrity.GetProperty("section_data_sha256").GetProperty(section.Name).GetString();
  }
  check("Analysis Log22section integrity and bilingual fixed prompt "+lang,valid&&File.ReadAllText(file).StartsWith(ReportExporter.Prompt(lang)),null);
 }
}
