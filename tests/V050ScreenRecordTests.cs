using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace CodexUsageMonitor;

// Test-only desktop recording. Every PNG comes from CopyFromScreen, never PrintWindow.
internal static class V050ScreenRecordTests {
 [StructLayout(LayoutKind.Sequential)]struct NativeRect {public int Left,Top,Right,Bottom;public Rectangle Rectangle=>Rectangle.FromLTRB(Left,Top,Right,Bottom);}
 delegate bool WindowCallback(IntPtr window,IntPtr state);
 [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out NativeRect rect);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")]static extern bool EnumWindows(WindowCallback callback,IntPtr state);
 [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
 static Rectangle PhysicalRect(IntPtr window){var old=SetThreadDpiAwarenessContext(new IntPtr(-4));try{if(!GetWindowRect(window,out var rect))throw new IOException("無法取得實體擷取範圍");return rect.Rectangle;}finally{if(old!=IntPtr.Zero)SetThreadDpiAwarenessContext(old);}}
 static void Property(object target,string name,object? value)=>target.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)?.SetValue(target,value);
 static MonitorService Fixture(string root){
  var service=new MonitorService(root);service.Settings.UiLanguage="zh-TW";service.Settings.OpacityPercent=100;L.SetLanguage("zh-TW");var now=DateTimeOffset.UtcNow;var reset=now.AddDays(5);var rows=new List<UsageSample>();Snapshot? last=null;
  for(int i=0;i<=88;i++){var t=now.AddSeconds((i-88)*90);last=new Snapshot(t,t,i*90000,"RECORDING-ACCOUNT","ACCOUNT_STABLE","RECORDING-GENERATION","TEST",[new WindowQuota("codex","Codex","primary",10080,53+i/10,reset.ToUnixTimeSeconds(),"VALID","pro",null,null,null,"")],2,[]){PollId="RECORDING-POLL-"+i};rows.AddRange(UsageSample.From(last,false,90));}
  service.History.Save(rows);Property(service,"Current",last);service.MainClock.Observe(last!,service.Monotonic);service.RebuildDaily();
  var announcement=AnnouncementParser.Candidate("team-codex-reset","https://codex-reset.com/tibo",2,"recording-reset","重置測試","Codex global reset for all paid plans will begin tomorrow around 10 AM PT.",now,now)! with{ResetUtc=now.AddHours(2),TimeQuality="DEADLINE",SignalLevel="INCOMING",Scope="ALL",Eligibility="AS_ANNOUNCED",SourceClass="TEAM_SIGNAL_RELAY",Author="Tibo / thsottiaux"};service.Radar.Apply([announcement],now);return service;
 }
 public static void Run(string root,int dpi=144,int repeats=5){
  root=Path.GetFullPath(root);Directory.CreateDirectory(root);var results=new List<object>();var errors=new List<string>();
  using var host=new Form{Text="CodexUsageMonitor 隔離錄影：合成資料",BackColor=Color.FromArgb(28,28,32),FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,Bounds=Screen.PrimaryScreen!.WorkingArea,TopMost=true};
  host.Shown+=async(_,_)=>{
   try{
    using var service=Fixture(Path.Combine(root,"synthetic-data"));host.Activate();var activation=Stopwatch.StartNew();uint foregroundPid=0;while(activation.ElapsedMilliseconds<45000){GetWindowThreadProcessId(GetForegroundWindow(),out foregroundPid);if(foregroundPid==Environment.ProcessId)break;await Task.Delay(100);}if(foregroundPid!=Environment.ProcessId)throw new IOException("測試背景未取得前景；不開始擷取");
    foreach(string kind in repeats==1?new[]{"history"}:new[]{"history","settings","reset"})for(int repeat=1;repeat<=repeats;repeat++){
     string clipRoot=Path.Combine(root,$"{kind}-{dpi}-{repeat}");Directory.CreateDirectory(clipRoot);DesktopCapture? capture=null;long shownQpc=0,scrollQpc=0;string? setupError=null;
     using ProductForm form=kind switch{"history"=>new HistoryForm(service),"settings"=>new SettingsForm(service),_=>new ResetInfoForm(service,service.Radar.ActiveSignal)};
     form.Load+=(_,_)=>{
      try{var work=Screen.FromControl(host).WorkingArea;form.Location=new(work.Left+8,work.Top+4);if(form.Bottom>work.Bottom-4)form.Top=Math.Max(work.Top+2,work.Bottom-form.Height-4);
       var target=PhysicalRect(form.Handle);var backdrop=PhysicalRect(host.Handle);if(!backdrop.Contains(target)){setupError="測試視窗超出中性背景，停止錄影";return;}var region=Rectangle.Intersect(Rectangle.Inflate(target,4,4),backdrop);
       capture=new DesktopCapture(clipRoot,region,host.Handle,form.Handle);capture.Start();if(!capture.Ready.Wait(5000)){setupError="桌面擷取未就緒";return;}if(capture.Error!=null){setupError=capture.Error;return;}Thread.Sleep(150);
      }catch(Exception e){setupError=e.GetType().Name+": "+e.Message;}
     };
     form.Shown+=(_,_)=>shownQpc=Stopwatch.GetTimestamp();form.Show(host);if(setupError!=null){capture?.Stop();capture?.Save(kind,dpi,GetDpiForWindow(form.Handle),form.DeviceDpi,shownQpc,scrollQpc);throw new IOException(setupError);}if(form is HistoryForm history)await history.LoadPage();await Task.Delay(500);
     var body=form.Controls.OfType<SpacePanel>().FirstOrDefault();if(body!=null){body.AutoScrollPosition=new Point(0,100);scrollQpc=Stopwatch.GetTimestamp();body.Invalidate(true);}await Task.Delay(700);
     capture?.Stop();if(capture==null)throw new IOException("未建立桌面擷取");var nativeDpi=GetDpiForWindow(form.Handle);var receipt=capture.Save(kind,dpi,nativeDpi,form.DeviceDpi,shownQpc,scrollQpc);results.Add(receipt);if(capture.Error!=null)throw new IOException(capture.Error);if(nativeDpi!=dpi||form.DeviceDpi!=dpi)throw new IOException("錄影程序 DPI 不符");form.Close();host.Activate();await Task.Delay(100);
    }
   }catch(Exception e){errors.Add(e.ToString());}
   finally{AtomicJson.Save(Path.Combine(root,"recording-results.json"),new{results,errors,capture="REAL_DESKTOP_COPY_FROM_SCREEN",requested_fps=10,physical_display_dpi_unchanged=true,application_logical_dpi=dpi,compositor_first_frame_exhaustively_verified=false,source="受控中性背景上測試視窗；不含工作列",model_calls=0});Environment.ExitCode=errors.Count==0?0:1;host.Close();}
  };Application.Run(host);
 }
 sealed class DesktopCapture {
  readonly string root;readonly Rectangle region;readonly IntPtr backdrop,target;readonly CancellationTokenSource stop=new();Thread? thread;readonly List<object> frames=[];readonly List<double> times=[];long start;public string? Error{get;private set;}public ManualResetEventSlim Ready{get;}=new(false);
  public DesktopCapture(string root,Rectangle region,IntPtr backdrop,IntPtr target){this.root=root;this.region=region;this.backdrop=backdrop;this.target=target;}
  string? Guard(){
   GetWindowThreadProcessId(GetForegroundWindow(),out uint foreground);if(foreground!=Environment.ProcessId)return "前景離開測試程序，停止擷取並丟棄本幀";
   string? blocked=null;bool reached=false;EnumWindows((window,_)=>{if(window==backdrop){reached=true;return false;}if(!IsWindowVisible(window)||!GetWindowRect(window,out var rect)||!rect.Rectangle.IntersectsWith(region))return true;GetWindowThreadProcessId(window,out uint process);if(process!=Environment.ProcessId){blocked="其他應用位於測試區域上方，停止擷取並丟棄本幀";return false;}return true;},IntPtr.Zero);return blocked??(!reached?"中性背景不在可驗證的視窗層級，停止擷取":null);
  }
  public void Start(){thread=new Thread(Capture){IsBackground=true,Name="Synthetic UI desktop recorder"};thread.Start();}
  void Capture(){var old=SetThreadDpiAwarenessContext(new IntPtr(-4));start=Stopwatch.GetTimestamp();try{int index=0;while(!stop.IsCancellationRequested){long frameStart=Stopwatch.GetTimestamp();var problem=Guard();if(problem!=null){Error=problem;break;}using var bitmap=new Bitmap(region.Width,region.Height);using(var g=Graphics.FromImage(bitmap))g.CopyFromScreen(region.Location,Point.Empty,region.Size,CopyPixelOperation.SourceCopy);problem=Guard();if(problem!=null){Error=problem;break;}long captured=Stopwatch.GetTimestamp();string file=$"frame-{index++:D4}.png";bitmap.Save(Path.Combine(root,file));double elapsed=(captured-start)*1000d/Stopwatch.Frequency;times.Add(elapsed);frames.Add(new{file,qpc=captured,elapsed_ms=elapsed,foreground_pid_checked_before_and_after=true});Ready.Set();double spent=(Stopwatch.GetTimestamp()-frameStart)*1000d/Stopwatch.Frequency;int wait=Math.Max(0,100-(int)spent);if(stop.Token.WaitHandle.WaitOne(wait))break;}}catch(Exception e){Error=e.GetType().Name+": "+e.Message;}finally{Ready.Set();if(old!=IntPtr.Zero)SetThreadDpiAwarenessContext(old);}}
  public void Stop(){stop.Cancel();if(thread!=null&&!thread.Join(10000))Error="擷取 thread 未在期限內停止";}
  public object Save(string kind,int requestedDpi,uint nativeDpi,int deviceDpi,long shown,long scroll){var gaps=times.Zip(times.Skip(1),(a,b)=>b-a).ToArray();var value=new{kind,requested_dpi=requestedDpi,native_dpi=nativeDpi,device_dpi=deviceDpi,physical_pixel_region=new{region.X,region.Y,region.Width,region.Height},capture="REAL_DESKTOP_COPY_FROM_SCREEN",capture_method="Graphics.CopyFromScreen",guard_passed=Error==null,aborted=Error!=null,target_hwnd=target.ToInt64(),owned_pid=Environment.ProcessId,requested_fps=10,qpc_frequency=Stopwatch.Frequency,recording_start_qpc=start,shown_qpc=shown,scroll_qpc=scroll,max_capture_gap_ms=gaps.Length>0?gaps.Max():0,frames,error=Error,privacy_guard="擷取前後皆檢查前景PID與擷取範圍上方視窗；其他app出現即停止並丟棄本幀",limitation="約100ms目標採樣；實際間隔另列，不能排除幀間更短暫的破圖"};AtomicJson.Save(Path.Combine(root,"clip.json"),value);return value;}
 }
}
