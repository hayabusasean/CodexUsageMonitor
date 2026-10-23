using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
namespace CodexUsageMonitor;

internal static class RuntimeEnvironment
{
    internal const string HomeVariable="CODEX_USAGE_MONITOR_HOME";
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true,ExactSpelling=true)]
    static extern SafeFileHandle CreateFileW(string name,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true,ExactSpelling=true)]
    static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle,StringBuilder path,uint length,uint flags);

    // Resolve the deepest existing directory through a handle, then retain the new suffix.
    // This makes 8.3 names and SUBST aliases share the same path and instance lock.
    internal static string PhysicalPath(string path)
    {
        var full=Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var suffix=new Stack<string>();string ancestor=full;
        while(true){
            try{
                if((File.GetAttributes(ancestor)&FileAttributes.Directory)==0)throw new IOException("DATA_ROOT_NOT_A_DIRECTORY");
                break;
            }catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
            string? parent=Path.GetDirectoryName(ancestor);
            if(parent==null)throw new IOException("DATA_ROOT_EXISTING_ANCESTOR_REQUIRED");
            suffix.Push(Path.GetFileName(ancestor));ancestor=parent;
        }
        using var handle=CreateFileW(ancestor,0,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero);
        if(handle.IsInvalid)throw new IOException("DATA_ROOT_OPEN_FAILED",new Win32Exception(Marshal.GetLastWin32Error()));
        var buffer=new StringBuilder(512);
        while(true){
            uint length=GetFinalPathNameByHandleW(handle,buffer,(uint)buffer.Capacity,0); // FILE_NAME_NORMALIZED | VOLUME_NAME_DOS
            if(length==0)throw new IOException("DATA_ROOT_RESOLVE_FAILED",new Win32Exception(Marshal.GetLastWin32Error()));
            if(length<buffer.Capacity)break;
            buffer=new StringBuilder(checked((int)length+1));
        }
        string resolved=buffer.ToString();
        if(resolved.StartsWith(@"\\?\UNC\",StringComparison.OrdinalIgnoreCase))throw new IOException("DATA_ROOT_DEVICE_OR_NETWORK_PATH");
        if(resolved.StartsWith(@"\\?\",StringComparison.Ordinal))resolved=resolved[4..];
        if(resolved.Length<3||!char.IsAsciiLetter(resolved[0])||resolved[1]!=':'||resolved[2]!='\\')throw new IOException("DATA_ROOT_DEVICE_OR_NETWORK_PATH");
        while(suffix.Count>0)resolved=Path.Combine(resolved,suffix.Pop());
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(resolved));
    }
    internal static string DataRoot()=>DataRoot(Environment.GetEnvironmentVariable(HomeVariable));
    internal static string DataRoot(string? configured)
    {
        if(string.IsNullOrEmpty(configured))return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexUsageMonitor");
        if(configured.StartsWith(@"\\",StringComparison.Ordinal)||configured.StartsWith("//",StringComparison.Ordinal)||configured.StartsWith(@"\??\",StringComparison.Ordinal))throw new IOException("DATA_ROOT_DEVICE_OR_NETWORK_PATH");
        if(!Path.IsPathFullyQualified(configured))throw new IOException("DATA_ROOT_MUST_BE_ABSOLUTE");
        var full=Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured));
        if(full.StartsWith(@"\\",StringComparison.Ordinal)||full.StartsWith("//",StringComparison.Ordinal))throw new IOException("DATA_ROOT_DEVICE_OR_NETWORK_PATH");
        if(full==Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full)!))throw new IOException("DATA_ROOT_CANNOT_BE_VOLUME_ROOT");
        for(string? ancestor=full;ancestor!=null;ancestor=Path.GetDirectoryName(ancestor)){
            FileAttributes attributes;
            try{attributes=File.GetAttributes(ancestor);}catch(FileNotFoundException){continue;}catch(DirectoryNotFoundException){continue;}
            if((attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("DATA_ROOT_REPARSE_POINT");
            if((attributes&FileAttributes.Directory)==0)throw new IOException("DATA_ROOT_NOT_A_DIRECTORY");
        }
        var standard=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexUsageMonitor");
        var codexHome=Environment.GetEnvironmentVariable("CODEX_HOME");
        if(string.IsNullOrWhiteSpace(codexHome))codexHome=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
        string physical=PhysicalPath(full);
        static bool Within(string path,string parent)=>path.Equals(parent,StringComparison.OrdinalIgnoreCase)||path.StartsWith(Path.EndsInDirectorySeparator(parent)?parent:parent+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        foreach(string reserved in new[]{PhysicalPath(standard),PhysicalPath(codexHome)}){
            if(Within(physical,reserved)||Within(reserved,physical))throw new IOException("DATA_ROOT_RESERVED");
        }
        return physical;
    }
}

// Production diagnostics. Content counters exclude the receipt currently being written.
internal sealed class RuntimeSessionMetrics : IDisposable
{
    readonly string root;readonly long jsonStart;readonly Dictionary<string,long> appendStart;
    readonly DateTimeOffset started=DateTimeOffset.UtcNow;readonly Stopwatch watch=Stopwatch.StartNew();
    readonly object gate=new();readonly string exeHash=RuntimeIdentity.Hash(Environment.ProcessPath!);
    MonitorService? service;DateTimeOffset? observed;int polls;bool disposed;
    internal RuntimeSessionMetrics(string root)
    {
        this.root=root;jsonStart=AtomicJson.BytesWritten;appendStart=AppendSizes(root);
        try{
            string folder=Path.Combine(root,"diagnostics");Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder,"runtime-session_"+started.UtcDateTime.ToString("yyyy-MM-dd")+".jsonl"),JsonSerializer.Serialize(new{schema="codex-usage-session.v1",phase="PROCESS_START",timestamp_utc=started,data_root=Path.GetFullPath(root),pid=Environment.ProcessId,build=RuntimeIdentity.Build,exe_sha256=exeHash})+"\n",new UTF8Encoding(false));
        }catch{/* A diagnostic failure must not prevent normal startup. */}
    }
    static Dictionary<string,long> AppendSizes(string root)
    {
        return new[]{"history","logs","diagnostics","radar_history"}.Select(name=>Path.Combine(root,name)).Where(Directory.Exists)
            .SelectMany(folder=>Directory.EnumerateFiles(folder,"*",SearchOption.AllDirectories))
            .Where(p=>p.EndsWith(".csv",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".jsonl",StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p=>p,p=>new FileInfo(p).Length,StringComparer.OrdinalIgnoreCase);
    }
    internal void Attach(MonitorService monitor){service=monitor;monitor.Changed+=Observe;}
    void Observe()
    {
        lock(gate){if(disposed||service is not {Busy:false,Verified:true,Current:{} snapshot}||snapshot.Observed==observed)return;
            observed=snapshot.Observed;polls++;Write("POLL_COMPLETED");}
    }
    void Write(string phase)
    {
        try{
            long append=AppendSizes(root).Sum(x=>Math.Max(0,x.Value-appendStart.GetValueOrDefault(x.Key)));
            long json=AtomicJson.BytesWritten-jsonStart;
            AtomicJson.Save(Path.Combine(root,"diagnostics","runtime-metrics.v1.json"),new{
                schema="codex-usage-runtime-metrics.v1",phase,generated_at=DateTimeOffset.UtcNow,started_at=started,
                elapsed_seconds=watch.Elapsed.TotalSeconds,pid=Environment.ProcessId,build=RuntimeIdentity.Build,exe_sha256=exeHash,
                successful_polls=polls,json_rewrite_bytes=json,append_growth_bytes=append,total_content_write_bytes=json+append,
                measurement="JSON_REWRITES_PLUS_NET_CSV_JSONL_GROWTH",current_receipt_excluded=true,
                append_scope="僅history、logs、diagnostics、radar_history內CSV及JSONL；有輪替或刪檔時非完整累計量"
            });
        }catch{/* Diagnostics must not interrupt monitoring or shutdown. */}
    }
    public void Dispose(){lock(gate){if(disposed)return;disposed=true;if(service!=null)service.Changed-=Observe;Write("PROCESS_EXIT");}}
}
