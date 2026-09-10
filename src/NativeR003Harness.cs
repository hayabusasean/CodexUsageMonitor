
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace CodexUsageMonitor;
internal static class NativeR003Harness {
 public static int LastDpi=96;
 public static void Attach(OverlayForm overlay,MonitorService s,string[] args){
  int i=Array.IndexOf(args,"--r003-ui");if(i<0||i+1>=args.Length)return;string root=Path.GetFullPath(args[i+1]);Directory.CreateDirectory(root);
  overlay.Shown+=async(_,_)=>{try{
   LastDpi=overlay.DeviceDpi;var start=Stopwatch.StartNew();while(!s.Verified&&start.Elapsed.TotalSeconds<75)await Task.Delay(250);if(!s.Verified)throw new InvalidOperationException("REAL_SOURCE_UNAVAILABLE");
   using var backdrop=new Form{FormBorderStyle=FormBorderStyle.None,BackColor=Color.FromArgb(232,234,230),StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(40,40,1000,800),ShowInTaskbar=false,Text="R003 controlled QA background"};
   backdrop.TopMost=true;backdrop.Show();overlay.TopMost=true;overlay.BringToFront();overlay.Location=new(100,100);overlay.Render();await Task.Delay(500);Capture(overlay,root,"overlay-standard-light",s);
   backdrop.BackColor=Color.FromArgb(25,30,38);await Task.Delay(300);Capture(overlay,root,"overlay-standard-dark",s);
   backdrop.Paint+=(_,e)=>{using var pen=new Pen(Color.Gray);for(int y=0;y<320;y+=16)e.Graphics.DrawLine(pen,0,y,720,y);for(int x=0;x<720;x+=24)e.Graphics.DrawLine(pen,x,0,x,320);};backdrop.Invalidate();await Task.Delay(300);Capture(overlay,root,"overlay-standard-detail",s);
   s.Settings.Compact=true;overlay.Render();await Task.Delay(300);Capture(overlay,root,"overlay-compact",s);s.Settings.Compact=false;overlay.Render();backdrop.Hide();overlay.Hide();
   using(var history=new HistoryForm(s)){history.TopMost=true;history.Show();await history.LoadPage();await Task.Delay(350);Capture(history,root,"history",s);Click(history,"進階篩選");await Task.Delay(200);Capture(history,root,"history-advanced",s);await history.ExportCsv();Capture(history,root,"history-csv-success",s);if(history.LastExport!=null){File.Copy(history.LastExport,Path.Combine(root,"real-ui-export.csv"),true);foreach(var suffix in new[]{"_daily","_hourly"})File.Copy(Path.Combine(Path.GetDirectoryName(history.LastExport)!,Path.GetFileNameWithoutExtension(history.LastExport)+suffix+".csv"),Path.Combine(root,"real-ui-export"+suffix+".csv"),true);}history.Close();}
   using(var details=new DetailsForm(s)){details.TopMost=true;details.Show();await Task.Delay(350);Capture(details,root,"source",s);Click(details,"其他額度池");await Task.Delay(200);Capture(details,root,"source-other-pools",s);details.Close();}
   using(var settings=new SettingsForm(s,overlay)){settings.TopMost=true;settings.Show();await Task.Delay(350);Capture(settings,root,"settings",s);Click(settings,"進階來源選項");var path=settings.AllControls().OfType<TextBox>().First();settings.AllControls().OfType<Panel>().First(x=>x.AutoScroll).ScrollControlIntoView(path);await Task.Delay(200);Capture(settings,root,"settings-advanced",s);var slider=settings.AllControls().OfType<TrackBar>().First();slider.Value=85;if(Math.Abs(overlay.Opacity-.85)>.01)throw new InvalidOperationException("OPACITY_PREVIEW_FAILED");Click(settings,"取消");if(Math.Abs(overlay.Opacity-.70)>.01)throw new InvalidOperationException("OPACITY_CANCEL_FAILED");AtomicJson.Save(Path.Combine(root,"settings-preview-check.json"),new{classification="NATIVE_UI_EVENT",preview_opacity=.85,cancel_restored=.70,result="PASS"});}
   using(var export=new ExportForm(s,new(DateTimeOffset.Now.Date.AddDays(-6),DateTimeOffset.Now))){export.TopMost=true;export.Show();await Task.Delay(350);Capture(export,root,"export-options",s);export.ExportButton.PerformClick();var wait=Stopwatch.StartNew();while(export.LastExport==null&&wait.Elapsed.TotalSeconds<60)await Task.Delay(100);Capture(export,root,"export-success",s);if(export.LastExport==null)throw new InvalidOperationException("EXPORT_UI_FAILED");File.Copy(export.LastExport,Path.Combine(root,"real-ui-export"+Path.GetExtension(export.LastExport)),true);
    using var held=new FileStream(Path.Combine(root,"blocked"),FileMode.Create,FileAccess.ReadWrite,FileShare.None);await export.RunExport(Path.Combine(root,"blocked","fail.log"));Capture(export,root,"export-failure",s);export.Close();}
   overlay.Show();await Task.Delay(300); // menu crop includes only our overlay and controlled backdrop
   backdrop.Show();overlay.BringToFront();overlay.ShowMenu();await Task.Delay(200);
   CaptureRegion(new Rectangle(overlay.Left,overlay.Top,Math.Max(overlay.Width,310),Math.Min(470,Screen.FromControl(overlay).WorkingArea.Bottom-overlay.Top)),root,"context-menu",s,overlay);
   overlay.ContextMenuStrip?.Close();backdrop.Close();
   overlay.Hide();
   using(var testService=new MonitorService(Path.Combine(root,"synthetic-data")))
   using(var test=new OverlayForm(testService,true)){
    test.Show();test.TopMost=true;test.Location=new(100,100);using var background=new Form{StartPosition=FormStartPosition.Manual,FormBorderStyle=FormBorderStyle.None,TopMost=true,BackColor=Color.LightGray,Bounds=new Rectangle(40,40,1000,700),ShowInTaskbar=false};
    background.Show();test.BringToFront();
    foreach(var state in new[]{("zero","0%","更新於 12 秒前",new[]{"≈130%","≈<0.1%","—"}),("low","<1%","更新失敗 · 95 秒前",new[]{"≈0%","—","—"}),("full","100%","資料已過期 · 400 秒前",new[]{"≈8.4%","≈4%","≈12.6%"}),("empty","—","正在取得額度",new[]{"—","—","—"})}){
      test.TestWeekly=state.Item2;test.TestAge=state.Item3;test.TestDays=state.Item4;test.Render();await Task.Delay(250);Capture(test,root,"synthetic-"+state.Item1,s);
    }
    testService.Settings.Compact=true;test.TestWeekly="100%";test.TestAge="更新於 99 秒前";test.TestDays=["≈130%","≈<0.1%","—"];test.Render();await Task.Delay(250);Capture(test,root,"synthetic-compact",s);
    test.Close();background.Close();
   }
   overlay.Show();
   var perfStart=DateTimeOffset.UtcNow;using var process=Process.GetCurrentProcess();using var child=Process.GetProcessById(s.ChildId);double cpu=process.TotalProcessorTime.TotalMilliseconds,childcpu=child.TotalProcessorTime.TotalMilliseconds;int handles=process.HandleCount,childHandles=child.HandleCount;var points=new List<object>();var observed=new HashSet<string>();
   for(int n=0;n<275;n++){await Task.Delay(1000);if(s.Current!=null)observed.Add(s.Current.PollId);if(n%5==0){process.Refresh();child.Refresh();points.Add(new{elapsed_seconds=(DateTimeOffset.UtcNow-perfStart).TotalSeconds,monitor_private_working_set=SmokeHarness.PrivateWorkingSet(process),child_private_working_set=SmokeHarness.PrivateWorkingSet(child),monitor_handles=process.HandleCount,child_handles=child.HandleCount});}}
   process.Refresh();child.Refresh();double ms=(DateTimeOffset.UtcNow-perfStart).TotalMilliseconds;
   AtomicJson.Save(Path.Combine(root,"performance.json"),new{classification="REAL_NATIVE",duration_seconds=ms/1000,observed_poll_count=observed.Count,configured_poll_seconds=s.Settings.Interval,monitor_cpu_percent=(process.TotalProcessorTime.TotalMilliseconds-cpu)/ms/Environment.ProcessorCount*100,child_cpu_percent=(child.TotalProcessorTime.TotalMilliseconds-childcpu)/ms/Environment.ProcessorCount*100,monitor_handle_delta=process.HandleCount-handles,child_handle_delta=child.HandleCount-childHandles,points});
   AtomicJson.Save(Path.Combine(root,"complete.json"),new{classification="REAL_NATIVE_UI_AUTOMATION",self_verification=s.Verified&&observed.Count>=3?"PASS":"FAIL",exe_sha256=ReportExporter.ExeHash(),monitor_pid=Environment.ProcessId,child_pid=s.ChildId,utc=DateTimeOffset.UtcNow,display=overlay.DisplayText});
   if(args.Contains("--r003-exit"))overlay.Close();
  }catch(Exception ex){AtomicJson.Save(Path.Combine(root,"failure.json"),new{type=ex.GetType().Name,error=ex.Message,stack=ex.StackTrace});if(args.Contains("--r003-exit"))overlay.Close();}};
 }
 static void Click(ProductForm form,string text){form.AllControls().OfType<Button>().First(x=>x.Text==text).PerformClick();}
 [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr h,int cmd);
 [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int hgt,uint flags);
 [DllImport("dwmapi.dll")]static extern int DwmFlush();
 public static void Capture(Form form,string root,string name,MonitorService service){
  ShowWindow(form.Handle,9);SetWindowPos(form.Handle,new IntPtr(-1),0,0,0,0,0x43);form.BringToFront();form.Refresh();Application.DoEvents();DwmFlush();System.Threading.Thread.Sleep(150);
  CaptureRegion(form.RectangleToScreen(form.ClientRectangle),root,name,service,form);
 }
 static void CaptureRegion(Rectangle r,string root,string name,MonitorService s,Form form){
  using var b=new Bitmap(r.Width,r.Height);using(var g=Graphics.FromImage(b))g.CopyFromScreen(r.Location,Point.Empty,r.Size);b.Save(Path.Combine(root,name+".png"));
  AtomicJson.Save(Path.Combine(root,name+".json"),new{classification="REAL_DESKTOP_CAPTURE",origin="Graphics.CopyFromScreen actual Windows compositor; no DrawToBitmap",utc=DateTimeOffset.UtcNow,monitor_pid=Environment.ProcessId,exe_sha256=ReportExporter.ExeHash(),os=Environment.OSVersion.VersionString,dpi=form.DeviceDpi,bounds=new{r.X,r.Y,r.Width,r.Height},opacity=form.Opacity,display=form is OverlayForm o?o.DisplayText:form.Text,controls=form is ProductForm p?p.AllControls().Where(x=>x.Visible).Select(x=>new{name=x.AccessibleName??x.Text,role=x.GetType().Name,bounds=new{x.Left,x.Top,x.Width,x.Height},inside_parent=x.Parent?.ClientRectangle.Contains(x.Bounds)??true}).ToArray():null});
 }
}

