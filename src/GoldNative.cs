using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
namespace CodexUsageMonitor;
internal static class GoldNative {
 [DllImport("user32.dll")]static extern bool PrintWindow(IntPtr hwnd,IntPtr dc,uint flags);
 static void StaticFrame(Form form,string path){using var bitmap=new Bitmap(form.Width,form.Height);using(var g=Graphics.FromImage(bitmap)){var dc=g.GetHdc();try{if(!PrintWindow(form.Handle,dc,2))throw new IOException("STATIC_CAPTURE_FAILED");}finally{g.ReleaseHdc(dc);}}bitmap.Save(path);}
 [DllImport("user32.dll")]static extern IntPtr GetThreadDesktop(uint id);
 [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetUserObjectInformation(IntPtr obj,int index,StringBuilder text,uint size,out uint needed);
 internal static void Attach(OverlayForm overlay,MonitorService service,string[] args){bool staticOnly=args.Contains("--gold-native-static");bool inputAllowed=args.Contains("--gold-native-input-authorized");int at=Array.IndexOf(args,inputAllowed?"--gold-native-input-authorized":staticOnly?"--gold-native-static":"--gold-native");if(at<0)return;string output=Path.GetFullPath(args[at+1]);Directory.CreateDirectory(output);
  var desktop=new StringBuilder(256);GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()),2,desktop,512,out _);if(!desktop.ToString().StartsWith("CUM_RC4_QA_")&&!(inputAllowed&&desktop.ToString()=="Default"))throw new InvalidOperationException("Private desktop required");
  overlay.Shown+=async(_,_)=>{
   var receipts=new List<object>();Process? recording=null;
   try{
    var now=DateTimeOffset.UtcNow.AddMinutes(-5);var rows=Rc4HistoryEventTests.EvidenceRows(now).samples;rows.Add(Rc4HistoryEventTests.Sample("confirmation",now.AddSeconds(90),99,reset:now.AddDays(7)));service.History.Save(rows);var last=rows.Last();var fixture=new Snapshot(now,now,0,last.account_context_key,last.context_assurance,last.source_generation,"synthetic",[new WindowQuota("codex","Codex",last.source_slot,10080,last.used_percent_raw,last.reset_at_utc!.Value.ToUnixTimeSeconds(),"VALID",last.plan_type,null,null,null,"")],1,[]);typeof(MonitorService).GetProperty("Current")!.SetValue(service,fixture);service.MainClock.Observe(fixture,service.Monotonic);
    var radar=Rc4HistoryEventTests.RadarAt(now);service.Radar.Apply(radar.Events,now.AddHours(-1));service.RebuildLocalCycles();service.Settings.ChartView="FULL";service.Settings.UiLanguage="en-US";L.SetLanguage("en-US");
    var menu=(ContextMenuStrip)typeof(OverlayForm).GetField("menu",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(overlay)!;
    for(int language=0;language<2;language++){
     L.SetLanguage(language==0?"en-US":"zh-TW");
     foreach(string key in new[]{"Menu.History","Menu.Reset","Menu.Settings"})for(int repeat=0;repeat<(!staticOnly&&language==0?6:1);repeat++){
      string name=(key=="Menu.Reset"?"radar":key=="Menu.History"?"history":"settings")+"-"+L.Language+"-"+repeat;string capture=Path.Combine(output,name);Directory.CreateDirectory(capture);
      var opened=new TaskCompletionSource<ProductForm>();long requested=Stopwatch.GetTimestamp();
      if(!staticOnly){
        var start=new ProcessStartInfo("python"){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetFullPath(Path.Combine(output,"..","..",".."))};
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"..","..","tests","capture_product_window.py"));start.ArgumentList.Add("0");start.ArgumentList.Add(capture);
        recording=Process.Start(start);var timeout=Stopwatch.StartNew();while(!File.Exists(Path.Combine(capture,"capture-ready"))&&timeout.ElapsedMilliseconds<8000)await Task.Delay(20);
        if(!File.Exists(Path.Combine(capture,"capture-ready")))throw new IOException("WGC_NOT_READY");}
      ProductForm.PresentationObserved=form=>{
       AtomicJson.Save(Path.Combine(capture,"target.json"),new{hwnd=form.Handle.ToInt64()});
       form.Shown+=(_,_)=>{AtomicJson.Save(Path.Combine(capture,"open.json"),new{requested_qpc=requested,shown_qpc=Stopwatch.GetTimestamp(),qpc_frequency=Stopwatch.Frequency,form=form.Name,normal_menu_entry=key,dpi=form.DeviceDpi,hwnd=form.Handle.ToInt64()});opened.TrySetResult(form);};
      };
      var item=menu.Items.OfType<ToolStripMenuItem>().First(x=>x.Text==L.T(key));overlay.BeginInvoke((Action)(()=>item.PerformClick()));
      // Settings uses a modal loop; its async pre-show callback releases the normal menu handler.
      var page=await opened.Task.WaitAsync(TimeSpan.FromSeconds(12));ProductForm.PresentationObserved=null;
      await Task.Delay(2300);
      if(repeat==0)StaticFrame(page,Path.Combine(capture,"static-native.png"));
      if(page is HistoryForm history&&repeat==0){history.OpenEventFocusForSample("after-100");await Task.Delay(2200);StaticFrame(page,Path.Combine(capture,"static-focus.png"));await history.ExportCsv();if(staticOnly){using var export=new ExportForm(service,new(now.AddHours(-2),DateTimeOffset.UtcNow));export.Show(page);export.ExportButton.PerformClick();var exportWait=Stopwatch.StartNew();while(export.LastExport==null&&exportWait.ElapsedMilliseconds<15000)await Task.Delay(50);if(export.LastExport==null)throw new IOException("UI_LOG_EXPORT_FAILED");PublicReportTests.VerifyIntegrity(export.LastExport);StaticFrame(export,Path.Combine(capture,"static-export.png"));AtomicJson.Save(Path.Combine(capture,"ui-export.json"),new{csv_via_history=true,analysis_log_via_export_button=true,log=Path.GetFileName(export.LastExport),integrity_verified=true,dpi=export.DeviceDpi});export.Close();}}
      if(repeat==1){page.Size=new(page.Width-100,page.Height-60);await Task.Delay(600);page.WindowState=FormWindowState.Maximized;await Task.Delay(600);page.WindowState=FormWindowState.Normal;await Task.Delay(700);}
      File.WriteAllText(Path.Combine(capture,"stop-capture"),"done");if(recording!=null){await recording.WaitForExitAsync();recording.Dispose();recording=null;}
      receipts.Add(new{name,open_completed=true,dynamic_capture_verified=false,static_auxiliary_only=staticOnly});AtomicJson.Save(Path.Combine(output,"native-progress.json"),receipts);page.Close();await Task.Delay(50);
     }
    }
   }catch(Exception e){AtomicJson.Save(Path.Combine(output,"native-error.json"),new{error=e.ToString()});}
   finally{ProductForm.PresentationObserved=null;recording?.Dispose();overlay.Close();}
  };
 }
}
