using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace CodexUsageMonitor;

// Isolated application tests. PrintWindow captures are synchronous render evidence,
// not a recording of the compositor's first visible frames.
internal static class V050UiTests {
 [DllImport("user32.dll")]static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
 [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
 [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int GetWindowStyle(IntPtr window,int index);
 static IEnumerable<Control> Tree(Control c){yield return c;foreach(Control child in c.Controls)foreach(var x in Tree(child))yield return x;}
 static void Capture(Form form,string path){form.Refresh();using var bitmap=new Bitmap(form.Width,form.Height);using(var g=Graphics.FromImage(bitmap)){var dc=g.GetHdc();try{if(!PrintWindow(form.Handle,dc,2))throw new IOException("同步畫面擷取失敗");}finally{g.ReleaseHdc(dc);}}bitmap.Save(path);}
 static void SetProperty(object target,string name,object? value)=>target.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)?.SetValue(target,value);
 public static void Run(string root,int requestedDpi=144){
  root=Path.GetFullPath(root);Directory.CreateDirectory(root);var checks=new List<object>();var frames=new List<object>();var errors=new List<string>();
  void Check(string name,bool ok,object? details=null){checks.Add(new{name,result=ok?"PASS":"FAIL",details});}
  using var host=new Form{Text="CodexUsageMonitor 隔離畫面測試",ShowInTaskbar=false,Opacity=0};
  host.Shown+=async(_,_)=>{
   try{
    {
     var oldContext=SetThreadDpiAwarenessContext(new IntPtr(requestedDpi==96?-1:-4));
     try{
      string data=Path.Combine(root,"data-"+requestedDpi);using var service=new MonitorService(data);L.SetLanguage("zh-TW");service.Settings.UiLanguage="zh-TW";service.Settings.OpacityPercent=100;service.Settings.StartHidden=false;
      var now=DateTimeOffset.UtcNow;var reset=now.AddDays(5);var rows=new List<UsageSample>();
      for(int i=0;i<=88;i++){decimal used=53+(i/10);var t=now.AddSeconds((i-88)*90);var reading=new Snapshot(t,t,i*90000,"UI-ACCOUNT","ACCOUNT_STABLE","UI-GENERATION","TEST",[new WindowQuota("codex","Codex","primary",10080,used,reset.ToUnixTimeSeconds(),"VALID","pro",null,null,null,"")],2,[]){PollId="UI-POLL-"+i};rows.AddRange(UsageSample.From(reading,false,90));}
      service.History.Save(rows);var last=rows[^1];var snapshot=new Snapshot(now,now,0,"UI-ACCOUNT","ACCOUNT_STABLE","UI-GENERATION","TEST",[new WindowQuota("codex","Codex","primary",10080,last.used_percent_raw,reset.ToUnixTimeSeconds(),"VALID","pro",null,null,null,"")],2,[now.AddDays(3).ToUnixTimeSeconds(),now.AddDays(8).ToUnixTimeSeconds()]){PollId=last.poll_id};SetProperty(service,"Current",snapshot);service.MainClock.Observe(snapshot,service.Monotonic);
      var announcement=AnnouncementParser.Candidate("team-codex-reset","https://codex-reset.com/tibo",2,"ui-reset","Codex reset","Codex global reset for all paid plans will begin tomorrow around 10 AM PT.",now,now)! with{ResetUtc=now.AddHours(2),TimeQuality="DEADLINE",SignalLevel="INCOMING",Scope="ALL",Eligibility="AS_ANNOUNCED",SourceClass="TEAM_SIGNAL_RELAY",Author="Tibo / thsottiaux",OriginalSourceUrl="https://x.com/thsottiaux/status/2113162413008884099",RelayUrl="https://codex-reset.com/tibo"};
      service.Radar.Apply([announcement],now);service.RebuildDaily();service.RebuildLocalCycles();
      var forecastType=typeof(MonitorService).Assembly.GetType("CodexUsageMonitor.BurnForecast");if(forecastType?.GetMethod("Calculate",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic) is {} calculate)SetProperty(service,"Forecast",calculate.Invoke(null,[rows,now,announcement]));SetProperty(service,"ForecastAnnouncement",announcement);
      using(var overlay=new OverlayForm(service,true)){
       overlay.Show();overlay.Render();await Task.Delay(80);Check($"浮窗原生 DPI {requestedDpi}",GetDpiForWindow(overlay.Handle)==requestedDpi&&overlay.DeviceDpi==requestedDpi,new{actual=GetDpiForWindow(overlay.Handle),device=overlay.DeviceDpi});
       overlay.PreviewCompact=false;overlay.Render();Capture(overlay,Path.Combine(root,$"overlay-standard-{requestedDpi}.png"));int before=(int?)typeof(OverlayForm).GetProperty("CardSurfaceBuilds",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(overlay)??0;for(int i=0;i<12;i++){overlay.Render();overlay.Update();}int after=(int?)typeof(OverlayForm).GetProperty("CardSurfaceBuilds",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(overlay)??0;Check($"浮窗背景快取重用 {requestedDpi}",before==after,new{before,after});
       var testTray=(NotifyIcon)typeof(OverlayForm).GetField("tray",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(overlay)!;using(var icon=testTray.Icon!.ToBitmap())icon.Save(Path.Combine(root,$"tray-icon-{requestedDpi}.png"));
       if(typeof(OverlayForm).GetProperty("AlertDeliveryCount",BindingFlags.Instance|BindingFlags.NonPublic) is {} delivery){
        var listeners=(Action<List<string>>?)typeof(MonitorService).GetField("Alert",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(service);listeners?.Invoke(["合成提醒一","合成提醒二","合成提醒三","合成提醒四","合成提醒五"]);await Task.Delay(30);Check($"五條通知第一條立即送出 {requestedDpi}",(int)(delivery.GetValue(overlay)??0)==1);var nextAlert=typeof(OverlayForm).GetMethod("ShowNextAlert",BindingFlags.Instance|BindingFlags.NonPublic)!;for(int i=0;i<5;i++)nextAlert.Invoke(overlay,null);Check($"五條通知依序出列且不覆蓋 {requestedDpi}",(int)(delivery.GetValue(overlay)??0)==5);
       }
       overlay.PreviewCompact=true;overlay.Render();Capture(overlay,Path.Combine(root,$"overlay-compact-{requestedDpi}.png"));
       if(service.Forecast is {} activeForecast){SetProperty(service,"Forecast",activeForecast with{Confidence="STALE"});overlay.Render();Capture(overlay,Path.Combine(root,$"overlay-compact-stale-{requestedDpi}.png"));Check($"過時資料明確標示 {requestedDpi}",overlay.DisplayText.Contains("資料已過時"));SetProperty(service,"Forecast",activeForecast);}
       overlay.PreviewCompact=false;overlay.Render();
       var menu=(ContextMenuStrip)typeof(OverlayForm).GetField("menu",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(overlay)!;var first=menu.Items.OfType<ToolStripMenuItem>().First();Check($"第一項為浮窗開關 {requestedDpi}",first.Text==L.T("Menu.Hide"));first.PerformClick();Check($"隱藏浮窗 {requestedDpi}",!overlay.Visible);typeof(ContextMenuStrip).GetMethod("OnOpening",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(menu,[new System.ComponentModel.CancelEventArgs()]);Check($"隱藏後顯示叫回選單 {requestedDpi}",first.Text==L.T("Menu.Show"));first.PerformClick();Check($"選單叫回 {requestedDpi}",overlay.Visible);Capture(overlay,Path.Combine(root,$"reveal-menu-{requestedDpi}.png"));typeof(OverlayForm).GetMethod("HideOverlay")?.Invoke(overlay,null);typeof(OverlayForm).GetMethod("TrayLeftClickForTest",BindingFlags.Instance|BindingFlags.NonPublic)?.Invoke(overlay,null);Check($"tray 左鍵事件叫回 {requestedDpi}",overlay.Visible);Capture(overlay,Path.Combine(root,$"reveal-tray-{requestedDpi}.png"));
       overlay.Hide();
       foreach(string kind in new[]{"history","settings","reset","details"})for(int repeat=0;repeat<(kind=="details"?1:5);repeat++){
        using ProductForm form=kind switch{"history"=>new HistoryForm(service),"settings"=>new SettingsForm(service),"reset"=>new ResetInfoForm(service,service.Radar.ActiveSignal),_=>new DetailsForm(service)};
        form.Show();if(form is HistoryForm history)await history.LoadPage();await Task.Delay(90);form.Refresh();uint actual=GetDpiForWindow(form.Handle);Check($"{kind} 開窗 {repeat+1} DPI {requestedDpi}",actual==requestedDpi&&form.Visible&&form.Opacity==1,new{actual,form.DeviceDpi,form.Width,form.Height});Check($"{kind} 只有子控制項緩衝 {requestedDpi}-{repeat}",(GetWindowStyle(form.Handle,-20)&0x02000000)==0);
        if(kind=="details")Check($"詳細資訊保留可比燒速 {requestedDpi}",service.Forecast?.Rate2h is >0&&Tree(form).OfType<Label>().Any(c=>c.Text.Contains("4.0%/h")));
        string filename=$"{kind}-{requestedDpi}-{repeat+1}.png";Capture(form,Path.Combine(root,filename));frames.Add(new{filename,requested_dpi=requestedDpi,native_dpi=actual,form.DeviceDpi,classification="SYNCHRONOUS_PRINTWINDOW_NOT_VIDEO"});
        var panel=Tree(form).OfType<SpacePanel>().FirstOrDefault();if(panel!=null){panel.AutoScrollPosition=new Point(0,90);await Task.Delay(40);panel.Refresh();if(repeat==0)Capture(form,Path.Combine(root,$"{kind}-{requestedDpi}-scrolled.png"));}
        form.Close();await Task.Delay(30);
       }
       overlay.Close();
      }
     }finally{if(oldContext!=IntPtr.Zero)SetThreadDpiAwarenessContext(oldContext);}
    }
   }catch(Exception e){errors.Add(e.ToString());}
   finally{AtomicJson.Save(Path.Combine(root,"ui-results.json"),new{checks,frames,errors,render_evidence_only=true,video_recording=false,first_visible_compositor_frames_verified=false,physical_system_dpi_unchanged=true,model_calls=0,classification="隔離資料的原生視窗測試；100% 使用獨立 DPI unaware 程序，150% 使用實際螢幕 DPI"});Environment.ExitCode=errors.Count>0||checks.Any(x=>JsonSerializer.Serialize(x).Contains("\"FAIL\""))?1:0;host.Close();}
  };Application.Run(host);
 }
}
