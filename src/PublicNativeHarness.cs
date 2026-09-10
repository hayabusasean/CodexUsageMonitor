
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace CodexUsageMonitor;
internal static class PublicNativeHarness {
 static int actualDpi;static string output="";static readonly List<object> frames=[],checks=[];static Form? backdrop;
 static void Check(string id,bool ok,object? value=null){checks.Add(new{id,result=ok?"PASS":"FAIL",classification="FINAL_EXE_NATIVE_FLOW",value});AtomicJson.Save(Path.Combine(output,"checks-progress.json"),checks);if(!ok)throw new InvalidOperationException(id);}
 public static void Attach(OverlayForm overlay,MonitorService service,string[] args){
  int index=Array.IndexOf(args,"--public-ui");if(index<0)return;output=Path.GetFullPath(args[index+1]);Directory.CreateDirectory(output);
  overlay.Shown+=async(_,_)=>{try{actualDpi=overlay.DeviceDpi;
   var wait=Stopwatch.StartNew();while(!service.Verified&&wait.Elapsed.TotalSeconds<75)await Task.Delay(150);
   Check("P01 real producer to formal parser",service.Verified,new{service.Current?.Version,service.Current?.Assurance});
   if(args.Contains("--public-perf-only")){await Performance(service);Finish();if(args.Contains("--public-exit"))overlay.Close();return;}
   int child=service.ChildId;var generation=service.Current!.Generation;var success=service.MainClock.SuccessUtc;
   for(int i=0;i<20;i++){L.SetLanguage(i%2==0?"zh-TW":"en-US");await Task.Delay(30);}
   Check("L04 L10 twenty live language switches in single EXE",child==service.ChildId&&generation==service.Current?.Generation&&success==service.MainClock.SuccessUtc,new{child_unchanged=child==service.ChildId,external_language_directory_required=false});
   backdrop=new Form{Text="Controlled native QA backdrop",FormBorderStyle=FormBorderStyle.None,TopMost=true,ShowInTaskbar=false,BackColor=Color.FromArgb(245,245,245),Bounds=Screen.FromControl(overlay).WorkingArea};
   backdrop.Show();overlay.TopMost=true;overlay.Location=new(180,160);
   foreach(string lang in new[]{"en-US","zh-TW"}){
    L.SetLanguage(lang);service.Settings.UiLanguage=lang;service.Settings.Compact=false;service.Settings.OpacityPercent=70;overlay.ApplySettings();overlay.Location=new(180,160);
    backdrop.BackColor=Color.FromArgb(245,245,245);await Shot(overlay,"V01-standard-"+lang,"REAL","O01 O06");
    backdrop.BackColor=Theme.Canvas;await Shot(overlay,"standard-dark-"+lang,"REAL","O06");
    overlay.ToggleMode();await Shot(overlay,"V02-compact-"+lang,"REAL","O02 O06");
    backdrop.BackColor=Color.FromArgb(245,245,245);await Shot(overlay,"compact-light-"+lang,"REAL","O06");overlay.ToggleMode();overlay.Hide();backdrop.Hide();
    using(var history=new HistoryForm(service)){history.Show();await history.LoadPage();int q=history.QueryGeneration;var range=history.ExportRange;int count=history.DisplayRecordCount;
     L.SetLanguage(lang=="en-US"?"zh-TW":"en-US");await Task.Delay(100);L.SetLanguage(lang);
     Check("L07 history view survives language "+lang,history.QueryGeneration==q&&history.ExportRange==range&&history.DisplayRecordCount==count);
     await Shot(history,"history-real-"+lang,"SANITIZED_REAL","P01");await history.ExportCsv();Check("P01 E03 formal UI CSV "+lang,history.LastExport!=null);CopyCsv(history.LastExport!,"real-ui-"+lang);history.Close();
    }
    using(var settings=new SettingsForm(service,overlay)){settings.Show();await Shot(settings,"V05-settings-"+lang,"REAL","L05");var slider=settings.AllControls().OfType<OpacitySlider>().Single();slider.Value=85;L.SetLanguage(lang=="en-US"?"zh-TW":"en-US");Click(settings,"Settings.Cancel");Check("L05 language and opacity cancel "+lang,L.Language==lang&&Math.Abs(overlay.Opacity-.7)<.01);}
    using(var settings=new SettingsForm(service,overlay)){settings.Show();var choice=settings.AllControls().OfType<DarkChoice>().Single(c=>c.Items.Contains("English"));choice.SelectedIndex=lang=="en-US"?1:0;settings.AllControls().OfType<OpacitySlider>().Single().Value=85;Click(settings,"Settings.Save");Check("L06 formal Save language persisted "+lang,Settings.Load(Path.Combine(service.Root,"settings.json")).UiLanguage==L.Language&&service.Settings.OpacityPercent==85);}
    using(var settings=new SettingsForm(service,overlay)){settings.Show();settings.AllControls().OfType<DarkChoice>().Single(c=>c.Items.Contains("English")).SelectedIndex=lang=="en-US"?0:1;settings.AllControls().OfType<OpacitySlider>().Single().Value=70;Click(settings,"Settings.Save");}
    using(var details=new DetailsForm(service)){details.Show();await Shot(details,"V06-details-"+lang,"REAL","Details fit content");Click(details,"Details.OtherPools");await Shot(details,"details-expanded-"+lang,"REAL","Other pools retained");details.Close();}
    using(var about=new AboutForm(service)){about.Show();await Shot(about,"about-"+lang,"REAL","E06");Click(about,"About.CopyDiagnostics");await Shot(about,"diagnostics-preview-"+lang,"SANITIZED_REAL","E06");about.Close();}
    using(var export=new ExportForm(service,new(new DateTimeOffset(DateTime.Today),DateTimeOffset.Now))){export.Show();await Shot(export,"V09-export-"+lang,"REAL","E01");export.ExportButton.PerformClick();var timer=Stopwatch.StartNew();while(export.LastExport==null&&timer.Elapsed.TotalSeconds<35)await Task.Delay(100);Check("E01 formal UI analysis log "+lang,export.LastExport!=null);File.Copy(export.LastExport!,Path.Combine(output,"real-ui-"+lang+".log"),true);await Shot(export,"export-complete-"+lang,"REAL","E01");export.Close();}
    backdrop.Show();overlay.Show();overlay.Location=new(180,160);Front(backdrop);Front(overlay);overlay.ShowMenu();await Task.Delay(150);var menu=overlay.ContextMenuStrip!;Capture(menu.Bounds,"V10-menu-"+lang,"REAL","Language menu",overlay);menu.Close();overlay.Hide();backdrop.Hide();
    using var data=new MonitorService(Path.Combine(output,"synthetic-"+lang));data.Settings.UiLanguage=lang;
    var rows=PublicHistoryTests.Fixtures()["H02-low-gap-recent"];data.History.Save(rows);data.RebuildDaily();AtomicJson.Save(Path.Combine(output,"history-fixture-"+lang+".json"),new{classification="SYNTHETIC",rows});
    using(var history=new HistoryForm(data,new DateTime(2026,9,8))){history.Show();await history.LoadPage();Demo(history);history.SetChartMode(true);await Shot(history,"V03-history-latest-"+lang,"SYNTHETIC","H02");history.SetChartMode(false);await Shot(history,"V04-history-full-"+lang,"SYNTHETIC","H02");Check("H07 focus does not alter table",history.DisplayRecordCount==4);history.Close();}
    using var hud=new OverlayForm(data,true);hud.TestWeekly="91%";hud.TestAge=L.T("Overlay.UpdatedSeconds",23);hud.TestDays=["≈9%","—","—"];backdrop.Show();hud.Show();hud.Location=new(180,160);
    var day=new DailySummary{observed_drawdown_pp=9,partial=true,first_sample=DateTimeOffset.Now.AddHours(-2),last_sample=DateTimeOffset.Now,comparable_pair_count=2};
    using(var pop=hud.ShowDailyPopover(0,day)){await Shot(pop,"V07-daily-"+lang,"SYNTHETIC","O08");}
    for(int di=1;di<3;di++)using(var pop=hud.ShowDailyPopover(di,day)){await Shot(pop,"daily-day"+di+"-"+lang,"SYNTHETIC","O08 longest bilingual labels");}
    hud.TestDays=["≈9%","≈12%","≈6%"];data.Settings.Compact=false;hud.Render();await DemoShot(hud,"public-standard-"+lang);data.Settings.Compact=true;hud.Render();await DemoShot(hud,"public-compact-"+lang);data.Settings.Compact=false;hud.Render();
    var area=Screen.FromControl(hud).WorkingArea;hud.Location=new(area.Right-hud.Width-4,area.Bottom-hud.Height-4);
    using(var pop=hud.ShowDailyPopover(0,new DailySummary())){Check("O08 popover edge "+lang,area.Contains(pop.Bounds));await Shot(pop,"daily-edge-empty-"+lang,"SYNTHETIC","O08");}
    hud.Location=new(180,160);data.Settings.Compact=true;
    foreach(var val in new[]{"100%","99%","5%","0%","<1%","—"}){hud.TestWeekly=val;hud.Render();await Shot(hud,"compact-value-"+val.Replace("%","p").Replace("<","below").Replace("—","missing")+"-"+lang,"SYNTHETIC","O02");}
    hud.TestWeekly="91%";hud.TestAge=L.T("Overlay.StaleAge",120);hud.Render();await Shot(hud,"stale-"+lang,"SYNTHETIC","O05");
    foreach(var code in new[]{"AUTH_REQUIRED","METHOD_UNSUPPORTED","REQUEST_TIMEOUT","MAIN_WEEKLY_MISSING"}){hud.TestWeekly="—";hud.TestAge=OverlayForm.MissingMainAge(code,false);hud.Render();await Shot(hud,"health-"+code+"-"+lang,"SYNTHETIC","O05 P02 terminal source state");}
    hud.TestWeekly="—";hud.TestAge=L.T("Overlay.CodexNotFound");hud.Render();await Shot(hud,"no-codex-"+lang,"SYNTHETIC","O05");
    var now=DateTimeOffset.UtcNow;var a=R004Tests.Announcement(now);data.Radar.Apply([a,a with{EventId="SYNTHETIC-SECOND",RevisionId="SYNTHETIC-SECOND-REV"}],now);hud.TestWeekly="5%";hud.TestAge=L.T("Overlay.UpdatedSeconds",23);hud.Render();await Shot(hud,"unread-toggle-"+lang,"SYNTHETIC","O03 R01");hud.Hide();backdrop.Hide();
    using(var info=new ResetInfoForm(data,data.Radar.Unread)){info.Show();Demo(info);await Shot(info,"V08-reset-"+lang,"SYNTHETIC","R01 R04");Check("R04 only first displayed notice read "+lang,data.Radar.UnreadItems.Count==1);Click(info,"Radar.Next");await Task.Delay(150);Check("R04 Next consumes only shown second "+lang,data.Radar.Unread==null);info.Close();}
    hud.Close();L.SetLanguage(lang);
   }
   L.SetLanguage("en-US");
   foreach(string key in new[]{"H01-100-99","H03-latest-one","H08-empty","H08-low"}){
    using var data=new MonitorService(Path.Combine(output,"synthetic-"+key));data.History.Save(PublicHistoryTests.Fixtures()[key]);using var f=new HistoryForm(data,new DateTime(2026,9,8));f.Show();await f.LoadPage();Demo(f);await Shot(f,key,"SYNTHETIC",key);f.Close();
   }
   Theme.TestControlTextScale=1.2f;
   using(var stress=new SettingsForm(service)){stress.Show();await Shot(stress,"text120-settings","SIMULATED_CONTROL_FONT_120_PERCENT","O07 layout stress");stress.Close();}
   using(var stress=new DetailsForm(service)){stress.Show();await Shot(stress,"text120-details","SIMULATED_CONTROL_FONT_120_PERCENT","O07 layout stress");stress.Close();}
   Theme.TestControlTextScale=1;
   Theme.TestHighContrast=true;
   using(var data=new MonitorService(Path.Combine(output,"synthetic-high-contrast"))){using var hc=new SettingsForm(data);hc.Show();await Shot(hc,"high-contrast-settings","SIMULATED_SYSTEM_PALETTE","O07 system colors");hc.Close();}
   Theme.TestHighContrast=null;
   Check("L08 no missing production resources",L.Missing.Count==0,L.Missing.Keys.ToArray());
   backdrop.Close();backdrop=null;overlay.Show();service.Settings.Compact=false;service.Settings.UiLanguage=L.Language;overlay.ApplySettings();
   var radar=service.Radar.Snapshot();AtomicJson.Save(Path.Combine(output,"public-source-smoke.json"),new{classification="REAL_PUBLIC_HTTPS_GET",cookies=false,model_calls=0,exe_sha256=ReportExporter.ExeHash(),source_status=radar.Sources,parsed_announcements=radar.Events});
   if(!args.Contains("--public-quick"))await Performance(service);
   Finish();if(args.Contains("--public-exit"))overlay.Close();
  }catch(Exception ex){AtomicJson.Save(Path.Combine(output,"frames.json"),frames);AtomicJson.Save(Path.Combine(output,"failure.json"),new{classification="NATIVE_RUN_INCOMPLETE",ex.Message,type=ex.GetType().Name,ex.StackTrace});backdrop?.Close();if(args.Contains("--public-exit"))overlay.Close();}};
 }
 internal static void BeginFocused(string path,int dpi){
  output=Path.GetFullPath(path);Directory.CreateDirectory(output);actualDpi=dpi;frames.Clear();checks.Clear();
  backdrop=new Form{Text="Controlled native FIX QA backdrop",FormBorderStyle=FormBorderStyle.None,TopMost=true,ShowInTaskbar=false,BackColor=Theme.Canvas,Bounds=Screen.PrimaryScreen!.WorkingArea};backdrop.Show();
 }
 internal static void EndFocused(){Finish();backdrop?.Close();backdrop=null;}
 static void Finish(){AtomicJson.Save(Path.Combine(output,"frames.json"),frames);AtomicJson.Save(Path.Combine(output,"checks.json"),checks);AtomicJson.Save(Path.Combine(output,"complete.json"),new{exe_sha256=ReportExporter.ExeHash(),version=L.Version,frames=frames.Count,actual_os=Environment.OSVersion.VersionString,actual_dpi=actualDpi,visual_review="PENDING_ACTUAL_IMAGE_REVIEW"});}
 internal static async Task DemoShot(OverlayForm h,string name){
  using var tag=new Label{Text="DEMO · SYNTHETIC",ForeColor=Theme.Warning,BackColor=Theme.Canvas,Font=Theme.Font(9,true),AutoSize=true};
  backdrop!.BackColor=Theme.Canvas;backdrop.Controls.Add(tag);tag.Location=backdrop.PointToClient(new Point(h.Left+8,h.Top-32));Front(backdrop);Front(h);await Task.Delay(250);Application.DoEvents();DwmFlush();
  Capture(new Rectangle(h.Left,h.Top-36,h.Width,h.Height+36),name,"SYNTHETIC_PUBLIC_DEMO","Public native demo with visible label",h);backdrop.Controls.Remove(tag);
 }
 internal static void Demo(Form f){var label=new Label{Text="DEMO · SYNTHETIC",ForeColor=Theme.Warning,BackColor=Theme.Canvas,Font=Theme.Font(8,true),AutoSize=true,Anchor=AnchorStyles.Top|AnchorStyles.Right};f.Controls.Add(label);label.Location=new(Math.Max(8,f.ClientSize.Width-label.PreferredWidth-24),6);label.BringToFront();}
 internal static void Click(ProductForm f,string key)=>f.AllControls().OfType<Button>().Single(b=>b.Text==L.T(key)).PerformClick();
 internal static void CopyCsv(string source,string name){File.Copy(source,Path.Combine(output,name+".csv"),true);foreach(var suffix in new[]{"_daily","_hourly"})File.Copy(Path.Combine(Path.GetDirectoryName(source)!,Path.GetFileNameWithoutExtension(source)+suffix+".csv"),Path.Combine(output,name+suffix+".csv"),true);}
 internal static async Task Shot(Form f,string name,string classification,string cases){if(backdrop!=null){Front(backdrop);if(f.Owner is Form owner)Front(owner);}Front(f);await Task.Delay(250);f.Refresh();Application.DoEvents();DwmFlush();
  for(int i=0;i<8&&!OwnPixels(f.RectangleToScreen(f.ClientRectangle));i++){if(backdrop!=null)Front(backdrop);Front(f);await Task.Delay(180);Application.DoEvents();DwmFlush();}
  Capture(f.RectangleToScreen(f.ClientRectangle),name,classification,cases,f);}
 static void Capture(Rectangle rect,string name,string classification,string cases,Form f){
  if(!OwnPixels(rect))throw new InvalidOperationException("Foreign window obscured native QA: "+name);
  using var image=new Bitmap(rect.Width,rect.Height);using(var g=Graphics.FromImage(image))g.CopyFromScreen(rect.Location,Point.Empty,rect.Size);string path=Path.Combine(output,name+".png");image.Save(path);
  var record=new{name,classification,cases,ui_language=L.Language,exe_sha256=ReportExporter.ExeHash(),png_sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),actual_dpi=f.DeviceDpi,working_area=Screen.FromControl(f).WorkingArea,bounds=rect,opacity=f.Opacity,origin="Windows desktop compositor CopyFromScreen",controls=f is ProductForm p?p.AllControls().Select(c=>new{type=c.GetType().Name,text=c is TextBox?"[INPUT OMITTED]":c.Text,c.Visible,c.Width,c.Height}).ToArray():null};
  AtomicJson.Save(Path.Combine(output,name+".json"),record);frames.Add(record);
 }
 static async Task Performance(MonitorService s){
  using var p=Process.GetCurrentProcess();using var c=Process.GetProcessById(s.ChildId);p.Refresh();c.Refresh();double cpu=p.TotalProcessorTime.TotalMilliseconds,ccpu=c.TotalProcessorTime.TotalMilliseconds;var timer=Stopwatch.StartNew();var polls=new HashSet<string>();var points=new List<object>();long delay=0;
  while(timer.Elapsed.TotalSeconds<605){var tick=Stopwatch.StartNew();await Task.Delay(1000);delay=Math.Max(delay,tick.ElapsedMilliseconds-1000);if(s.Current!=null)polls.Add(s.Current.PollId);if((int)timer.Elapsed.TotalSeconds%10==0){p.Refresh();c.Refresh();points.Add(new{seconds=timer.Elapsed.TotalSeconds,monitor_private=SmokeHarness.PrivateWorkingSet(p),child_private=SmokeHarness.PrivateWorkingSet(c),monitor_handles=p.HandleCount,child_handles=c.HandleCount});}}
  p.Refresh();c.Refresh();AtomicJson.Save(Path.Combine(output,"performance.json"),new{classification="REAL_IDLE_PERIODIC_POLL",exe_sha256=ReportExporter.ExeHash(),duration_seconds=timer.Elapsed.TotalSeconds,quota_polls=polls.Count,monitor_cpu=(p.TotalProcessorTime.TotalMilliseconds-cpu)/timer.Elapsed.TotalMilliseconds/Environment.ProcessorCount*100,child_cpu=(c.TotalProcessorTime.TotalMilliseconds-ccpu)/timer.Elapsed.TotalMilliseconds/Environment.ProcessorCount*100,ui_max_delay_ms=delay,http_requests=s.Radar.HttpRequestCount,points});
 }
 static bool OwnPixels(Rectangle rect){var h=WindowFromPoint(new Point(rect.Left+rect.Width/2,rect.Top+rect.Height/2));GetWindowThreadProcessId(h,out var pid);return pid==Environment.ProcessId;}
 [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point p);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out int pid);
 static void Front(Form f){f.TopMost=true;f.Show();ShowWindow(f.Handle,9);SetWindowPos(f.Handle,new(-1),0,0,0,0,f is OverlayForm or DailyPopover?0x53u:0x43u);if(f is ProductForm)SetForegroundWindow(f.Handle);f.BringToFront();}
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr h,int command);
 [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int ht,uint flags);
 [DllImport("dwmapi.dll")]static extern int DwmFlush();
}

