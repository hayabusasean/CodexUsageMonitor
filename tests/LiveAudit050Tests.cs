using System.Diagnostics;
using System.Runtime.InteropServices;
namespace CodexUsageMonitor;
internal static class LiveAudit050Tests
{
    [StructLayout(LayoutKind.Sequential)]struct IoCounters{public ulong readOps,writeOps,otherOps,readBytes,writeBytes,otherBytes;}
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessIoCounters(IntPtr process,out IoCounters counters);
    internal static void Run(string root,int durationSeconds)
    {
        root=Path.GetFullPath(root);Directory.CreateDirectory(root);using var process=Process.GetCurrentProcess();
        var service=new MonitorService(Path.Combine(root,"private-runtime"));service.Settings.Interval=60;service.Settings.Alerts=false;service.Settings.RadarEnabled=true;service.Settings.CodexPath=Environment.GetEnvironmentVariable("CUM_OFFICIAL_CODEX")??"";
        long initial=AtomicJson.BytesWritten;GetProcessIoCounters(process.Handle,out var before);var watch=Stopwatch.StartNew();
        int successes=0;DateTimeOffset? last=null;service.Changed+=()=>{if(service.Verified&&service.Current is {} s&&s.Observed!=last){last=s.Observed;Interlocked.Increment(ref successes);}};service.Start();
        try{
            while(watch.Elapsed.TotalSeconds<durationSeconds){Thread.Sleep(1000);if(successes==0&&watch.Elapsed.TotalSeconds>90){AtomicJson.Save(Path.Combine(root,"live-audit-failure.json"),new{error=service.LastErrorCode,service.Status});Environment.ExitCode=1;return;}}
        }finally{service.Dispose();}
        GetProcessIoCounters(process.Handle,out var after);
        var files=Directory.GetFiles(Path.Combine(root,"private-runtime"),"*",SearchOption.AllDirectories).Where(p=>!p.Contains(Path.DirectorySeparatorChar+"neutral"+Path.DirectorySeparatorChar)).ToList();
        long append=files.Where(p=>p.EndsWith(".csv")||p.EndsWith(".jsonl")).Sum(p=>new FileInfo(p).Length);
        long dataWritten=AtomicJson.BytesWritten-initial+append;
        bool passed=durationSeconds>=3600&&successes>=55&&dataWritten<3_000_000;
        AtomicJson.Save(Path.Combine(root,"live-audit-results.json"),new{classification="ISOLATED_LIVE_READ_ONLY_MONITOR",elapsed_seconds=watch.Elapsed.TotalSeconds,successful_polls=successes,configured_interval_seconds=60,measured_json_rewrite_bytes=AtomicJson.BytesWritten-initial,append_bytes=append,total_data_write_bytes=dataWritten,process_write_bytes=after.writeBytes-before.writeBytes,threshold=3_000_000,result=passed?"PASS":"FAIL",official_signature="VALIDATED_BEFORE_EXECUTION",original_runtime_modified=false,model_calls=0});Environment.ExitCode=passed?0:1;
    }
}
