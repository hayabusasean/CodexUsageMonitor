using System.Reflection;
using System.Text.Json;
namespace CodexUsageMonitor;
internal static class FirstPaintDiagnosticsTests {
 internal static void Run(string root){
  Directory.CreateDirectory(root);var results=new List<object>();int failed=0;
  void Assert(bool value){if(!value)throw new InvalidOperationException("首次繪圖診斷驗證失敗");}
  void Check(string name,Action action){try{action();results.Add(new{name,result="PASS"});}catch(Exception e){failed++;results.Add(new{name,result="FAIL",error=e.ToString()});}}
  var before=new FirstPaintSurfaceState("history",144,1680,1120,null,null,0,0,0,false);
  var after=before with{bitmap_width=1680,bitmap_height=1120,bitmap_builds=1,bitmap_build_ms=12.5,shared_cache_builds=1};
  var now=DateTimeOffset.Parse("2026-10-23T00:00:00+08:00");long ticks=1000;var captured=new List<FirstPaintDiagnosticEntry>();
  var logger=new FirstPaintDiagnostics(root,"HistoryForm",captured.Add,()=>now,()=>ticks,1000);
  Check("首Paint記錄前後兩階段",()=>{Assert(logger.Begin(before));ticks=1025;logger.Complete(after);Assert(captured.Count==2&&captured[0].phase=="before_paint"&&captured[1].phase=="after_paint"&&captured[0].window_instance_id==captured[1].window_instance_id);});
  Check("記錄UTC時間與純繪圖耗時",()=>Assert(captured.All(x=>x.utc.Offset==TimeSpan.Zero&&x.utc==now.ToUniversalTime())&&captured[0].paint_elapsed_ms==null&&captured[1].paint_elapsed_ms==25));
  Check("DPI尺寸與bitmap建立次數對應",()=>Assert(captured[0].surface.bitmap_width==null&&captured[0].surface.bitmap_builds==0&&captured[1].surface==after&&captured.All(x=>x.surface.window_dpi==144)));
  Check("重畫一千次不再記錄",()=>{for(int i=0;i<1000;i++){Assert(!logger.Begin(after));logger.Complete(after);}Assert(captured.Count==2&&!logger.Pending);});
  Check("完成通知不會先於開始寫出",()=>{var entries=new List<FirstPaintDiagnosticEntry>();var one=new FirstPaintDiagnostics(root,"SettingsForm",entries.Add);one.Complete(after);Assert(entries.Count==0&&one.Pending);});
  Check("並行重入仍只有一組紀錄",()=>{var entries=new List<FirstPaintDiagnosticEntry>();var one=new FirstPaintDiagnostics(root,"ResetInfoForm",entries.Add);Parallel.For(0,50,_=>{if(one.Begin(before))one.Complete(after);});Assert(entries.Count==2);});
  Check("Paint失敗只記錄通用狀態",()=>{var entries=new List<FirstPaintDiagnosticEntry>();var one=new FirstPaintDiagnostics(root,"ExportForm",entries.Add);one.Begin(before);one.Complete(after,true);Assert(entries.Count==2&&entries[^1].outcome=="PAINT_FAILED");});
  Check("診斷寫入失敗不會拋出或每秒重試",()=>{int attempts=0;var one=new FirstPaintDiagnostics(root,"OverlayForm",_=>{attempts++;throw new IOException();});one.Begin(before);one.Complete(after);for(int i=0;i<100;i++){one.Begin(after);one.Complete(after);}Assert(attempts==2&&one.LastWriteError=="FIRST_PAINT_DIAGNOSTIC_WRITE_FAILED");});
  Check("同日多視窗紀錄只追加",()=>{string folder=Path.Combine(root,"append");for(int i=0;i<2;i++){var one=new FirstPaintDiagnostics(folder,"HistoryForm",utcNow:()=>now);one.Begin(before);one.Complete(after);}var file=Directory.GetFiles(Path.Combine(folder,"diagnostics"),"first_paint_*.jsonl").Single();var lines=File.ReadAllLines(file);Assert(lines.Length==4&&lines.Select(line=>JsonSerializer.Deserialize<FirstPaintDiagnosticEntry>(line)!.window_instance_id).Distinct().Count()==2);Assert(new FileInfo(file).Length<4096);});
  Check("未綁定Root的測試視窗不建立診斷",()=>{using var form=new ProductForm("測試");Assert(form.Controls.OfType<SpacePanel>().Single().FirstPaintLog==null);});
  Check("產品背景Paint使用指定Root且重畫不重寫",()=>{
   string folder=Path.Combine(root,"product");using var service=new MonitorService(folder);using var form=new ProductForm("不能記錄的標題 secret@example.invalid",620,680,service.Root);var panel=form.Controls.OfType<SpacePanel>().Single();
   using var bitmap=new Bitmap(Math.Max(1,panel.Width),Math.Max(1,panel.Height));using var graphics=Graphics.FromImage(bitmap);var method=typeof(SpacePanel).GetMethod("OnPaintBackground",BindingFlags.Instance|BindingFlags.NonPublic)!;
   method.Invoke(panel,[new PaintEventArgs(graphics,panel.ClientRectangle)]);var file=Directory.GetFiles(Path.Combine(folder,"diagnostics"),"first_paint_*.jsonl").Single();long length=new FileInfo(file).Length;for(int i=0;i<30;i++)method.Invoke(panel,[new PaintEventArgs(graphics,panel.ClientRectangle)]);
   var entries=File.ReadAllLines(file).Select(x=>JsonSerializer.Deserialize<FirstPaintDiagnosticEntry>(x)!).ToArray();Assert(entries.Length==2&&new FileInfo(file).Length==length&&entries[^1].surface.bitmap_width==panel.Width&&entries[^1].surface.bitmap_height==panel.Height&&entries[^1].surface.bitmap_builds==1&&entries[^1].surface.window_dpi==panel.DeviceDpi);Assert(!File.ReadAllText(file).Contains("secret")&&!File.ReadAllText(file).Contains("account_context")&&service.ChildId==0);
  });
  Check("浮窗只在首Paint記兩行而非每秒寫入",()=>{
   string folder=Path.Combine(root,"overlay");using var service=new MonitorService(folder);using var overlay=new OverlayForm(service,true);overlay.Render();using var bitmap=new Bitmap(overlay.Width,overlay.Height);using var graphics=Graphics.FromImage(bitmap);var method=typeof(OverlayForm).GetMethod("OnPaint",BindingFlags.Instance|BindingFlags.NonPublic)!;method.Invoke(overlay,[new PaintEventArgs(graphics,overlay.ClientRectangle)]);
   var file=Directory.GetFiles(Path.Combine(folder,"diagnostics"),"first_paint_*.jsonl").Single();long length=new FileInfo(file).Length;for(int i=0;i<60;i++){overlay.Render();method.Invoke(overlay,[new PaintEventArgs(graphics,overlay.ClientRectangle)]);}var entries=File.ReadAllLines(file).Select(x=>JsonSerializer.Deserialize<FirstPaintDiagnosticEntry>(x)!).ToArray();Assert(entries.Length==2&&new FileInfo(file).Length==length&&entries[^1].window_type=="OverlayForm"&&entries[^1].surface.bitmap_width==overlay.Width&&entries[^1].surface.bitmap_height==overlay.Height&&entries[^1].surface.bitmap_builds==1&&entries[^1].surface.window_dpi==overlay.DeviceDpi);
  });
  Check("視窗型別不接受私人標題",()=>{var entries=new List<FirstPaintDiagnosticEntry>();var one=new FirstPaintDiagnostics(root,"secret@example.invalid",entries.Add);one.Begin(before);one.Complete(after);Assert(entries.All(x=>x.window_type=="ProductWindow")&&!JsonSerializer.Serialize(entries).Contains("secret"));});
  Check("電子郵件範圍身分有明確繁中標示",()=>{string prior=L.Language;try{L.SetLanguage("zh-TW");Assert(GoldVisual.Assurance("EMAIL_SCOPED")=="電子郵件範圍身分"&&HistoryForm.AssuranceCaption("EMAIL_SCOPED")=="電子郵件範圍身分");}finally{L.SetLanguage(prior);}});
  string standardRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexUsageMonitor");
  Check("只有預設資料目錄可變更開機捷徑",()=>Assert(StartupLink.CanConfigure(standardRoot)&&StartupLink.CanConfigure(standardRoot+Path.DirectorySeparatorChar)&&!StartupLink.CanConfigure(Path.Combine(root,"startup-isolated"))));
  Check("隔離目錄啟用或取消開機啟動都保留捷徑sentinel",()=>{
   string folder=Path.Combine(root,"startup-isolated");Directory.CreateDirectory(folder);string shortcut=Path.Combine(folder,"sentinel.lnk");byte[] marker=System.Text.Encoding.UTF8.GetBytes("原有開機捷徑不可改寫");File.WriteAllBytes(shortcut,marker);File.SetLastWriteTimeUtc(shortcut,new DateTime(2026,1,2,3,4,5,DateTimeKind.Utc));var modified=File.GetLastWriteTimeUtc(shortcut);
   var settings=new Settings{Startup=true};Assert(!StartupLink.ApplyForDataRoot(folder,settings.Startup,Environment.ProcessPath!,shortcut));Assert(File.ReadAllBytes(shortcut).SequenceEqual(marker)&&File.GetLastWriteTimeUtc(shortcut)==modified);Assert(!StartupLink.ApplyForDataRoot(folder,false,Environment.ProcessPath!,shortcut));Assert(File.ReadAllBytes(shortcut).SequenceEqual(marker)&&File.GetLastWriteTimeUtc(shortcut)==modified);
  });
  Check("隔離目錄不建立不存在的開機捷徑",()=>{string folder=Path.Combine(root,"startup-isolated-empty");Directory.CreateDirectory(folder);string shortcut=Path.Combine(folder,"absent.lnk");Assert(!StartupLink.ApplyForDataRoot(folder,true,Environment.ProcessPath!,shortcut)&&!StartupLink.ApplyForDataRoot(folder,false,Environment.ProcessPath!,shortcut)&&!File.Exists(shortcut));});
  Check("預設資料目錄的開機開關仍可操作測試捷徑",()=>{string shortcut=Path.Combine(root,"default-startup-test.lnk");try{Assert(StartupLink.ApplyForDataRoot(standardRoot,true,Environment.ProcessPath!,shortcut)&&File.Exists(shortcut));Assert(StartupLink.ApplyForDataRoot(standardRoot,false,Environment.ProcessPath!,shortcut)&&!File.Exists(shortcut));}finally{if(File.Exists(shortcut))File.Delete(shortcut);}});
  Check("隔離設定頁停用開機啟動且取消保留原設定",()=>{using var service=new MonitorService(Path.Combine(root,"startup-settings"));service.Settings.Startup=true;using var form=new SettingsForm(service);IEnumerable<Control> Walk(Control c)=>c.Controls.Cast<Control>().SelectMany(x=>new[]{x}.Concat(Walk(x)));var controls=Walk(form).ToList();var startup=controls.OfType<CheckBox>().Single(x=>x.Name=="StartWithWindows");var hint=controls.OfType<Label>().Single(x=>x.Name=="StartupIsolationNotice");Assert(!startup.Enabled&&startup.Checked&&hint.Text=="獨立資料目錄不會改開機設定");form.CancelButton!.PerformClick();Assert(service.Settings.Startup&&service.ChildId==0);});
  AtomicJson.Save(Path.Combine(root,"first-paint-diagnostics-tests.json"),new{results,failed,classification="首Paint兩階段、直接繪圖hook與開機捷徑隔離測試；未操作桌面或錄影工具；捷徑測試只使用isolatedPath"});Environment.ExitCode=failed==0?0:1;
 }
}
