using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace CodexUsageMonitor;
internal static class SmokeHarness
{
    public static void Attach(OverlayForm form,MonitorService service,string[] args)
    {
        int index=Array.IndexOf(args,"--smoke-output");if(index<0||index+1>=args.Length)return;
        var root=Path.GetFullPath(args[index+1]);Directory.CreateDirectory(root);
        var timer=new System.Windows.Forms.Timer{Interval=250};var start=Stopwatch.StartNew();
        DateTimeOffset? perfAt=null;double cpu=0,childCpu=0;int handles=0,childHandles=0;long initialBytes=0;IntPtr foreground=GetForegroundWindow();bool took=false;
        var points=new List<object>();int childId=0;Process? child=null;
        timer.Tick+=async(_,_)=>{
            try {
                if(!took&&service.Verified&&service.Current!=null){
                    took=true;timer.Interval=1000;form.Render();form.SaveView(Path.Combine(root,"real-overlay.png"));
                    using(var history=new HistoryForm(service)){history.Show();await history.LoadPage();history.Refresh();Application.DoEvents();NativeR003Harness.Capture(history,root,"history-layout",service);history.Close();}
                    // Form rendering is separate from the steady-state 60-second sample.
                    var current=service.Current;
                    AtomicJson.Save(Path.Combine(root,"real-chain.json"),new{classification="REAL",version=current.Version,observed=current.Observed,display=form.DisplayText,windows=current.Windows,reset_credits=current.ResetCredits,credit_expirations=current.CreditExpirations,history_rows=service.History.Query<UsageSample>(new(current.Observed.AddMinutes(-5),DateTimeOffset.UtcNow.AddMinutes(1))).Count(),child_pid=service.ChildId,monitor_pid=Environment.ProcessId,verified=service.Verified,source_scope=current.Assurance,foreground_unchanged_before_active_history=foreground==GetForegroundWindow(),dpi=form.DeviceDpi,screens=Screen.AllScreens.Select(x=>new{x.DeviceName,x.Bounds,x.WorkingArea})});
                    using var proc=Process.GetCurrentProcess();childId=service.ChildId;child=Process.GetProcessById(childId);
                    cpu=proc.TotalProcessorTime.TotalMilliseconds;childCpu=child.TotalProcessorTime.TotalMilliseconds;handles=proc.HandleCount;childHandles=child.HandleCount;
                    initialBytes=Bytes(service.Root);perfAt=DateTimeOffset.UtcNow;
                }
                if(perfAt.HasValue){
                    using var p=Process.GetCurrentProcess();p.Refresh();child!.Refresh();
                    points.Add(new{elapsed_seconds=(DateTimeOffset.UtcNow-perfAt.Value).TotalSeconds,monitor_private_working_set=PrivateWorkingSet(p),child_private_working_set=PrivateWorkingSet(child),monitor_handles=p.HandleCount,child_handles=child.HandleCount});
                    if((DateTimeOffset.UtcNow-perfAt.Value).TotalSeconds>=60){
                        double elapsed=(DateTimeOffset.UtcNow-perfAt.Value).TotalMilliseconds;
                        AtomicJson.Save(Path.Combine(root,"performance.json"),new{classification="REAL",duration_seconds=elapsed/1000,logical_processors=Environment.ProcessorCount,monitor_cpu_percent=(p.TotalProcessorTime.TotalMilliseconds-cpu)/elapsed/Environment.ProcessorCount*100,child_cpu_percent=(child.TotalProcessorTime.TotalMilliseconds-childCpu)/elapsed/Environment.ProcessorCount*100,monitor_private_working_set=PrivateWorkingSet(p),child_private_working_set=PrivateWorkingSet(child),monitor_working_set=p.WorkingSet64,child_working_set=child.WorkingSet64,monitor_handle_delta=p.HandleCount-handles,child_handle_delta=child.HandleCount-childHandles,history_log_growth_bytes=Bytes(service.Root)-initialBytes,configured_poll_seconds=service.Settings.Interval,monitor_pid=p.Id,child_pid=childId,points});
                        AtomicJson.Save(Path.Combine(root,"smoke-complete.json"),new{classification="REAL",result=service.Verified?"PASS":"FAIL",utc=DateTimeOffset.UtcNow});
                        timer.Stop();timer.Dispose();child.Dispose();if(args.Contains("--smoke-exit"))form.Close();
                    }
                } else if(start.Elapsed>TimeSpan.FromSeconds(90)){
                    AtomicJson.Save(Path.Combine(root,"smoke-complete.json"),new{classification="REAL",result="FAIL",error=service.LastErrorCode,status=service.Status});timer.Stop();timer.Dispose();if(args.Contains("--smoke-exit"))form.Close();
                }
            }catch(Exception e){AtomicJson.Save(Path.Combine(root,"smoke-error.json"),new{type=e.GetType().Name});timer.Stop();timer.Dispose();if(args.Contains("--smoke-exit"))form.Close();}
        };
        form.Shown+=(_,_)=>timer.Start();form.FormClosed+=(_,_)=>{timer.Stop();timer.Dispose();};
    }
    static long Bytes(string root)=>new[]{"history","logs"}.Sum(d=>Directory.EnumerateFiles(Path.Combine(root,d)).Sum(f=>new FileInfo(f).Length));
    public static long? PrivateWorkingSet(Process p){var c=new PMC{cb=(uint)Marshal.SizeOf<PMC>()};return GetProcessMemoryInfo(p.Handle,ref c,c.cb)?(long)c.PrivateWorkingSetSize:null;}
    [StructLayout(LayoutKind.Sequential)]struct PMC{public uint cb,PageFaultCount;public UIntPtr PeakWorkingSetSize,WorkingSetSize,QuotaPeakPagedPoolUsage,QuotaPagedPoolUsage,QuotaPeakNonPagedPoolUsage,QuotaNonPagedPoolUsage,PagefileUsage,PeakPagefileUsage,PrivateUsage,PrivateWorkingSetSize,SharedCommitUsage;}
    [DllImport("psapi.dll",SetLastError=true)]static extern bool GetProcessMemoryInfo(IntPtr p,ref PMC c,uint size);
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
}

