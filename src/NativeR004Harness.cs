using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace CodexUsageMonitor;
internal static class NativeR004Harness {
 static string dir="";static MonitorService? real;static Form? background;static readonly List<object> frames=[];static readonly List<object> checks=[];
 static void Check(string id,bool ok,object? data=null){checks.Add(new{id,result=ok?"PASS":"FAIL",classification="NATIVE_UI_EVENT",data});if(!ok)throw new InvalidOperationException(id);}
 public static void Attach(OverlayForm overlay,MonitorService s,string[] args){
  int index=Array.IndexOf(args,"--r004-ui");if(index<0)return;dir=Path.GetFullPath(args[index+1]);Directory.CreateDirectory(dir);real=s;
  overlay.Shown+=async(_,_)=>{try{
   var watch=Stopwatch.StartNew();while(!s.Verified&&watch.Elapsed.TotalSeconds<75)await Task.Delay(200);Check("F01 REAL readonly quota",s.Verified,new{s.Current?.Version,s.Current?.Assurance});
   if(args.Contains("--r004-perf-only")){await Performance(overlay,s);AtomicJson.Save(Path.Combine(dir,"complete.json"),new{exe_sha256=ReportExporter.ExeHash(),performance_completed=true});if(args.Contains("--r004-exit"))overlay.Close();return;}
   background=new Form{FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,ShowInTaskbar=false,TopMost=true,BackColor=Color.FromArgb(235,237,239),Text="Controlled R004 QA background",Bounds=Screen.FromControl(overlay).WorkingArea};
   background.Show();overlay.TopMost=true;overlay.Location=new(180,160);s.Settings.Compact=false;s.Settings.OpacityPercent=70;overlay.ApplySettings();overlay.Location=new(180,160);overlay.Render();
   await Shot(overlay,"V01-standard-normal","REAL");await Shot(overlay,"V13-standard-light","REAL");background.BackColor=Theme.Canvas;await Shot(overlay,"V14-standard-dark","REAL");
   overlay.ToggleMode();Check("F04 direct toggle compact",overlay.Compact);await Shot(overlay,"V06-compact-normal","REAL");await Shot(overlay,"V16-compact-dark","REAL");background.BackColor=Color.FromArgb(245,245,245);await Shot(overlay,"V15-compact-light","REAL");overlay.ToggleMode();Check("F04 direct toggle standard",!overlay.Compact);
   overlay.Hide();background.Hide();
   using(var history=new HistoryForm(s)){history.TopMost=true;history.Show();await history.LoadPage();await Shot(history,"history-real","SANITIZED_REAL");await history.ExportCsv();Check("F01 CSV real UI",history.LastExport!=null);CopyCsv(history.LastExport!);history.Close();}
   using(var settings=new SettingsForm(s,overlay)){
    settings.TopMost=true;settings.Show();await Shot(settings,"V22-settings","REAL");Click(settings,"進階來源選項");var input=settings.AllControls().OfType<TextBox>().First();settings.AllControls().OfType<Panel>().First(x=>x.AutoScroll).ScrollControlIntoView(input);await Shot(settings,"V23-settings-advanced","SANITIZED_REAL");
    var opacity=settings.AllControls().OfType<OpacitySlider>().First();opacity.Value=85;Check("F06 opacity preview",Math.Abs(overlay.Opacity-.85)<.01);Click(settings,"取消");Check("F06 cancel restores70",Math.Abs(overlay.Opacity-.7)<.01);
   }
   using(var source=new DetailsForm(s)){source.TopMost=true;source.Show();await Shot(source,"V24-source","REAL");Click(source,"其他額度池");await Shot(source,"V25-other-pools","REAL");source.Close();}
   using(var export=new ExportForm(s,new(DateTimeOffset.Now.AddDays(-2),DateTimeOffset.Now))){export.TopMost=true;export.Show();await Shot(export,"V27-export","REAL");export.ExportButton.PerformClick();var w=Stopwatch.StartNew();while(export.LastExport==null&&w.Elapsed.TotalSeconds<30)await Task.Delay(100);Check("F40 formal UI Log",export.LastExport!=null);File.Copy(export.LastExport!,Path.Combine(dir,"real-ui-export"+Path.GetExtension(export.LastExport)),true);await Shot(export,"export-success","REAL");export.Close();}
   using var fake=new MonitorService(Path.Combine(dir,"synthetic-fixtures"));
   using var hud=new OverlayForm(fake,true);
   hud.Show();hud.TopMost=true;hud.Location=new(180,160);background.Show();SetForeground(hud);
   fake.MainClock.Observe(FixtureSnapshot(43),0);
   foreach(var pair in new[]{("V02-standard-20","20%"),("V03-standard-5","5%"),("V04-standard-zero","0%")}){
    hud.TestWeekly=pair.Item2;hud.TestAge="更新於 26 秒前";hud.TestDays=["≈1%","≈4%","—"];hud.Render();await Shot(hud,pair.Item1,"SYNTHETIC");
   }
   var announcement=R004Tests.Announcement(DateTimeOffset.UtcNow);fake.Radar.Apply([announcement],DateTimeOffset.UtcNow);hud.TestWeekly="43%";hud.Render();await Shot(hud,"V05-standard-unread","SYNTHETIC");Check("F16 unread badge",fake.Radar.Unread!=null);
   fake.Settings.Compact=true;
   foreach(var pair in new[]{("V07-compact-100","100%"),("V08-compact-99","99%"),("V09-compact-5","5%"),("V10-compact-zero","0%"),("V11-compact-below1","<1%")}){
    hud.TestWeekly=pair.Item2;hud.Render();await Shot(hud,pair.Item1,"SYNTHETIC");
   }
   await Shot(hud,"V12-compact-unread","SYNTHETIC");hud.Hide();background.Hide();
   using(var info=new ResetInfoForm(fake,announcement)){info.TopMost=true;info.Show();await Shot(info,"V26-reset-info","SYNTHETIC");Check("F18 open marks read",fake.Radar.Snapshot().Events.Single().ReadAt!=null);info.Close();}
   fake.Settings.Compact=false;background.Show();hud.Show();hud.Location=new(180,160);hud.TestWeekly="99%";hud.TestAge="更新失敗 · 95 秒前";hud.Render();await Shot(hud,"V29-update-failed","SYNTHETIC");hud.TestAge="資料已過期 · 99999 秒前";hud.Render();await Shot(hud,"V30-long-localized","SYNTHETIC");hud.TestAge="更新於 26 秒前";
   var today=DateTimeOffset.Now.Date;var full=new DailySummary{date=DateTime.Today.ToString("yyyy-MM-dd"),observed_drawdown_pp=8,partial=false,first_sample=today,last_sample=DateTimeOffset.Now,successful_sample_count=80,comparable_pair_count=79};
   var partial=new DailySummary{date=full.date,observed_drawdown_pp=1,partial=true,first_sample=DateTimeOffset.Now.AddHours(-2),last_sample=DateTimeOffset.Now,successful_sample_count=15,comparable_pair_count=13};
   foreach(var entry in new[]{("V31-popover-full",full),("V32-popover-partial",partial),("V33-popover-missing",new DailySummary())})await Pop(hud,entry.Item2,entry.Item1);
   var area=Screen.FromControl(hud).WorkingArea;hud.Location=new(area.Right-hud.Width-4,180);await Pop(hud,partial,"V34-popover-right");
   hud.Location=new(area.Right-hud.Width-4,area.Bottom-hud.Height-4);await Pop(hud,partial,"V35-popover-bottom");
   foreach(float scale in new[]{1f,1.25f,1.5f}){hud.TestScale=scale;hud.Render();hud.Location=new(area.Right-hud.Width-4,area.Bottom-hud.Height-4);await Pop(hud,partial,"V36-popover-scale-"+scale.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture),true);}
   hud.TestScale=null;hud.Render();hud.Location=new(180,160);SetForeground(hud);hud.ShowMenu();await Task.Delay(250);
   var menu=hud.ContextMenuStrip!;CaptureRect(menu.Bounds,"V28-context-menu","SYNTHETIC",hud);menu.Close();hud.Hide();background.Hide();
   foreach(var scenario in new[]{("V17-history-100-99",100m,99m,false),("V18-history-100-40",100m,40m,false),("V19-history-gap",70m,40m,true),("history-100-95",100m,95m,false),("history-5-0",5m,0m,false)}){
    using var data=new MonitorService(Path.Combine(dir,"synthetic-"+scenario.Item1));var rows=R004Tests.Rows(scenario.Item2,scenario.Item3,scenario.Item4);data.History.Save(rows);data.RebuildDaily();
    using var history=new HistoryForm(data,DateTime.Today);history.TopMost=true;history.Show();await history.LoadPage();await Shot(history,scenario.Item1,"SYNTHETIC");history.Close();
   }
   foreach(bool one in new[]{false,true}){
    using var data=new MonitorService(Path.Combine(dir,"synthetic-empty-"+one));if(one)data.History.Save([R004Tests.Rows(99,99)[0]]);
    using var history=new HistoryForm(data,DateTime.Today);history.TopMost=true;history.Show();await history.LoadPage();await Shot(history,one?"V21-history-one-row":"V20-history-empty","SYNTHETIC");history.Close();
   }
   hud.Close();background.Close();background=null;overlay.Show();overlay.Location=new(180,160);s.Settings.Compact=false;overlay.ApplySettings();
   if(!args.Contains("--r004-quick"))await Performance(overlay,s);
   var radar=s.Radar.Snapshot();AtomicJson.Save(Path.Combine(dir,"public-source-smoke.json"),new{classification="REAL_PUBLIC_HTTPS_GET",cookies=false,login=false,model_calls=0,request_count=s.Radar.HttpRequestCount,sources=radar.Sources,parsed_announcements=radar.Events,scope="Fetch/parser health; local reset cause not inferred",exe_sha256=ReportExporter.ExeHash()});
   AtomicJson.Save(Path.Combine(dir,"ui-checks.json"),checks);
   AtomicJson.Save(Path.Combine(dir,"frames.json"),frames);
   AtomicJson.Save(Path.Combine(dir,"complete.json"),new{classification="NATIVE_DESKTOP_CAPTURE",exe_sha256=ReportExporter.ExeHash(),utc=DateTimeOffset.UtcNow,actual_dpi=overlay.DeviceDpi,frames=frames.Count,ui_run_completed=true,visual_review="PENDING_HUMAN_OR_MODEL_IMAGE_INSPECTION",full_performance=!args.Contains("--r004-quick")});
   if(args.Contains("--r004-exit"))overlay.Close();
  }catch(Exception ex){AtomicJson.Save(Path.Combine(dir,"failure.json"),new{ex.Message,type=ex.GetType().Name,ex.StackTrace});AtomicJson.Save(Path.Combine(dir,"frames.json"),frames);AtomicJson.Save(Path.Combine(dir,"ui-checks.json"),checks);background?.Close();if(args.Contains("--r004-exit"))overlay.Close();}};
 }
 internal static Snapshot FixtureSnapshot(decimal remaining){var now=DateTimeOffset.UtcNow;return new(now,now,0,"SYNTHETIC","STABLE_SCOPE","SYNTHETIC","SYNTHETIC",[new("codex","","primary",10080,100-remaining,now.AddDays(7).ToUnixTimeSeconds(),"VALID","pro",null,null,null,"")],2,[]);}
 static void Click(ProductForm f,string text)=>f.AllControls().OfType<Button>().Single(x=>x.Text==text).PerformClick();
 static void CopyCsv(string source){File.Copy(source,Path.Combine(dir,"real-ui-export.csv"),true);foreach(var suffix in new[]{"_daily","_hourly"})File.Copy(Path.Combine(Path.GetDirectoryName(source)!,Path.GetFileNameWithoutExtension(source)+suffix+".csv"),Path.Combine(dir,"real-ui-export"+suffix+".csv"),true);}
 static async Task Pop(OverlayForm hud,DailySummary d,string name,bool scale=false){
  hud.Render();SetForeground(hud);using var p=hud.ShowDailyPopover(0,d);await Task.Delay(250);var area=Screen.FromControl(p).WorkingArea;Check(name+" bounded",area.Contains(p.Bounds)&&p.Width<=320*(hud.TestScale??hud.DeviceDpi/96f),new{p.Width,p.Height});
  await Shot(p,name,scale?"SYNTHETIC_LAYOUT_SCALE_ON_NATIVE_150_DPI":"SYNTHETIC",hud.TestScale);p.Close();
 }
 static async Task Shot(Form f,string name,string classification,float? scale=null){SetForeground(f);await Task.Delay(250);f.Refresh();Application.DoEvents();DwmFlush();CaptureRect(f.RectangleToScreen(f.ClientRectangle),name,classification,f,scale);}
 static void CaptureRect(Rectangle rect,string name,string classification,Form f,float? scale=null){
  using var b=new Bitmap(rect.Width,rect.Height);using(var g=Graphics.FromImage(b))g.CopyFromScreen(rect.Location,Point.Empty,rect.Size);string path=Path.Combine(dir,name+".png");b.Save(path);
  var record=new{name,classification,origin="Graphics.CopyFromScreen Windows desktop compositor",utc=DateTimeOffset.UtcNow,exe_sha256=ReportExporter.ExeHash(),png_sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),actual_dpi=f.DeviceDpi,simulated_logical_scale=scale,bounds=new{rect.X,rect.Y,rect.Width,rect.Height},opacity=f.Opacity,controls=f is ProductForm pf?pf.AllControls().Select(c=>new{text=c is TextBox?"[INPUT OMITTED]":c.Text,type=c.GetType().Name,c.Left,c.Top,c.Width,c.Height,c.Visible,dpi=c.DeviceDpi,font_points=c.Font.SizeInPoints}).ToArray():null,text=f is OverlayForm o?o.DisplayText:f is DailyPopover p?p.HumanText:f.Text};
  AtomicJson.Save(Path.Combine(dir,name+".json"),record);frames.Add(record);
 }
 static void SetForeground(Form f){f.Show();ShowWindow(f.Handle,9);SetWindowPos(f.Handle,new(-1),0,0,0,0,0x43);f.BringToFront();}
 static async Task Performance(OverlayForm f,MonitorService s){
  using var proc=Process.GetCurrentProcess();using var child=Process.GetProcessById(s.ChildId);proc.Refresh();child.Refresh();
  double cpu=proc.TotalProcessorTime.TotalMilliseconds,ccpu=child.TotalProcessorTime.TotalMilliseconds;var elapsed=Stopwatch.StartNew();var ids=new HashSet<string>();var points=new List<object>();long maxUiDelay=0;
  while(elapsed.Elapsed.TotalSeconds<605){var responsiveness=Stopwatch.StartNew();await Task.Delay(1000);maxUiDelay=Math.Max(maxUiDelay,responsiveness.ElapsedMilliseconds-1000);if(s.Current!=null)ids.Add(s.Current.PollId);
   if((int)elapsed.Elapsed.TotalSeconds%10==0){proc.Refresh();child.Refresh();points.Add(new{elapsed_seconds=elapsed.Elapsed.TotalSeconds,monitor_private_working_set=SmokeHarness.PrivateWorkingSet(proc),child_private_working_set=SmokeHarness.PrivateWorkingSet(child),monitor_handles=proc.HandleCount,child_handles=child.HandleCount});}
  }
  proc.Refresh();child.Refresh();AtomicJson.Save(Path.Combine(dir,"performance.json"),new{classification="REAL_IDLE_PERIODIC_POLL",duration_seconds=elapsed.Elapsed.TotalSeconds,monitor_cpu_avg=(proc.TotalProcessorTime.TotalMilliseconds-cpu)/elapsed.Elapsed.TotalMilliseconds/Environment.ProcessorCount*100,app_server_cpu_avg=(child.TotalProcessorTime.TotalMilliseconds-ccpu)/elapsed.Elapsed.TotalMilliseconds/Environment.ProcessorCount*100,quota_polls=ids.Count,http_requests=s.Radar.HttpRequestCount,failed_sources=s.Radar.FailedSourceCount,max_ui_timer_delay_ms=maxUiDelay,points,exe_sha256=ReportExporter.ExeHash()});
 }
 [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr h,int cmd);
 [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int hgt,uint flags);
 [DllImport("dwmapi.dll")]static extern int DwmFlush();
}

