using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace CodexUsageMonitor;

internal sealed class MonitorException(string code) : Exception(code){public string Code=>Message;}
internal static class Safe
{
    public static string Label(string? v) => string.IsNullOrEmpty(v)?"":Regex.IsMatch(v,@"^[\p{L}\p{N} _.\-]{1,80}$")?v:"來源文字已隱藏";
    public static string Hash(string v)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(v))).ToLowerInvariant();
}
internal sealed class Diagnostic(string root)
{
    public string LastStep="none"; public string Method="";public int RequestId; public int? ChildExitCode;
    public void Write(string operation,string code,int retry=0,DateTimeOffset? next=null)
    {
        try{
            var dir=Path.Combine(root,"logs");Directory.CreateDirectory(dir);
            var path=Path.Combine(dir,"app_"+DateTime.UtcNow.ToString("yyyy-MM-dd")+".jsonl");
            if(File.Exists(path)&&new FileInfo(path).Length>256*1024){File.Move(path,path+".1",true);}
            var line=JsonSerializer.Serialize(new {utc=DateTimeOffset.UtcNow,operation,failed_step=code=="OK"?"":operation,last_successful_step=LastStep,error_class=code,child_exit_code=ChildExitCode,request_id=RequestId,method=Method,response_or_result_presence=code=="OK",sanitized_exception_type=code,sanitized_stderr_excerpt="raw stderr discarded",retry_count=retry,next_retry_at=next,local_log_path="logs/"+Path.GetFileName(path)});
            File.AppendAllText(path,line+"\n",new UTF8Encoding(false));if(code=="OK")LastStep=operation;
        }catch{}
    }
}
internal static class CodexLocator
{
    public static IEnumerable<string> Candidates(string? selected)
    {
        var list=new List<string>();
        if(!string.IsNullOrWhiteSpace(selected))list.Add(selected);
        foreach(var dir in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')){
            if(!Path.IsPathFullyQualified(dir.Trim('"')))continue;
            var d=Path.GetFullPath(dir.Trim('"'));
            if(d.TrimEnd('\\').Equals(Environment.CurrentDirectory.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)||d.TrimEnd('\\').Equals(AppContext.BaseDirectory.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))continue;
            list.Add(Path.Combine(d,"codex.exe"));
        }
        var local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach(var dir in new[]{Path.Combine(local,"OpenAI","Codex","bin"),Path.Combine(local,"Programs","OpenAI","Codex","bin")}){
            list.Add(Path.Combine(dir,"codex.exe"));
            if(Directory.Exists(dir))foreach(var d in Directory.EnumerateDirectories(dir).OrderByDescending(Directory.GetLastWriteTimeUtc).Take(15))list.Add(Path.Combine(d,"codex.exe"));
        }
        var ext=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".vscode","extensions");
        if(Directory.Exists(ext))foreach(var d in Directory.EnumerateDirectories(ext,"openai.chatgpt-*").OrderDescending().Take(8))
            foreach(var rel in new[]{@"bin\windows-x86_64\codex.exe",@"bin\win32-x64\codex.exe"})list.Add(Path.Combine(d,rel));
        var npm=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),@"npm\node_modules\@openai");
        foreach(var dir in new[]{Path.Combine(npm,"codex"),Path.Combine(npm,"codex-win32-x64")}){
            var meta=Path.Combine(dir,"package.json");
            try{
                using var doc=JsonDocument.Parse(File.ReadAllText(meta));
                if(!(doc.RootElement.GetProperty("name").GetString()??"").StartsWith("@openai/codex"))continue;
                list.Add(Path.Combine(dir,@"vendor\x86_64-pc-windows-msvc\codex\codex.exe"));
                list.Add(Path.Combine(dir,@"node_modules\@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\codex\codex.exe"));
            }catch{}
        }
        return list.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }
    public static void ValidateArchitecture(string path)
    {
        if(!path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new MonitorException("NATIVE_EXE_REQUIRED");
        using var f=File.OpenRead(path);using var b=new BinaryReader(f);
        if(b.ReadUInt16()!=0x5a4d)throw new MonitorException("INVALID_EXECUTABLE");
        f.Position=0x3c;var offset=b.ReadInt32();if(offset<0||offset>f.Length-6)throw new MonitorException("INVALID_EXECUTABLE");
        f.Position=offset;if(b.ReadUInt32()!=0x4550||b.ReadUInt16()!=0x8664)throw new MonitorException("ARCHITECTURE_MISMATCH");
    }
    public static async Task<string> ValidateVersion(string path,string cwd,CancellationToken ct)
    {
        ValidateArchitecture(path);
        var v=await Capture(path,["--version"],cwd,ct);
        var m=Regex.Match(v,@"codex-cli (\d+\.\d+\.\d+[a-zA-Z0-9.\-]*)");
        if(!m.Success)throw new MonitorException("INVALID_CODEX_VERSION");
        var help=await Capture(path,["app-server","--help"],cwd,ct);
        if(!help.Contains("stdio",StringComparison.OrdinalIgnoreCase))throw new MonitorException("METHOD_UNSUPPORTED");
        return m.Groups[1].Value;
    }
    static async Task<string> Capture(string path,string[] args,string cwd,CancellationToken ct)
    {
        using var p=OwnedProcess.Start(path,args,cwd);
        var err=Task.Run(async()=>{var chars=new char[1024];while(await p.Error.ReadAsync(chars)>0){}});
        var output=Task.Run(async()=>{var s=new StringBuilder();var chars=new char[2048];int n;while((n=await p.Output.ReadAsync(chars))>0){if(s.Length+n>128*1024)throw new MonitorException("MESSAGE_TOO_LARGE");s.Append(chars,0,n);}return s.ToString();});
        return await output.WaitAsync(TimeSpan.FromSeconds(15),ct);
    }
}
internal sealed class AppServerClient : IDisposable
{
    OwnedProcess? child;readonly ConcurrentDictionary<int,TaskCompletionSource<JsonElement>> pending=new();
    readonly SemaphoreSlim writeLock=new(1);readonly Diagnostic log;int counter;long epoch;bool disposed;Exception? fatal;
    public string Version {get;private set;}="";public string Generation {get;}=Guid.NewGuid().ToString("N");
    public int ChildId=>child?.Process.Id??0; public long AccountEpoch=>Interlocked.Read(ref epoch);
    public event Action<string>? Notification; public bool Broken=>fatal!=null||child==null;
    public int TimeoutSeconds=20;
    public AppServerClient(Diagnostic log){this.log=log;}
    internal static bool IsAllowed(string method)=>method is "initialize" or "account/read" or "account/rateLimits/read" or "account/usage/read";
    public async Task Connect(string path,string cwd,CancellationToken ct)
    {
        Version=await CodexLocator.ValidateVersion(path,cwd,ct);
        child=OwnedProcess.Start(path,["app-server"],cwd);
        _=Task.Run(ReadLoop);_=Task.Run(DrainError);
        await Request("initialize",new {clientInfo=new{name="codex_usage_monitor",title="Codex Usage Monitor",version="1.0.0"}},ct);
        await Send(new{method="initialized",@params=new{}},ct);
    }
    internal async Task ConnectControlled(string exe,string[] args,string cwd,CancellationToken ct)
    {
        Version="SYNTHETIC";child=OwnedProcess.Start(exe,args,cwd);_=Task.Run(ReadLoop);_=Task.Run(DrainError);
        await Request("initialize",new{clientInfo=new{name="synthetic_test",version="1"}},ct);
        await Send(new{method="initialized",@params=new{}},ct);
    }
    public async Task<JsonElement> Request(string method,object? param,CancellationToken ct)
    {
        if(!IsAllowed(method))throw new MonitorException("RPC_NOT_ALLOWED");
        if(fatal!=null)throw fatal;
        int id=Interlocked.Increment(ref counter);
        var source=new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id]=source;log.Method=method;log.RequestId=id;
        try {
            await Send(new{id,method,@params=param??new{}},ct);
            var result=await source.Task.WaitAsync(TimeSpan.FromSeconds(TimeoutSeconds),ct);
            log.Write("rpc","OK");return result;
        }catch(TimeoutException){throw new MonitorException("REQUEST_TIMEOUT");}
        finally{pending.TryRemove(id,out _);}
    }
    async Task Send(object message,CancellationToken ct)
    {
        await writeLock.WaitAsync(ct);
        try{if(child==null)throw new MonitorException("TRANSPORT_EOF");await child.Input.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(),ct);}
        catch(IOException){throw new MonitorException("TRANSPORT_EOF");}
        finally{writeLock.Release();}
    }
    async Task ReadLoop()
    {
        try{
            var buffer=new char[4096];var line=new StringBuilder();int count;
            while(child!=null&&(count=await child.Output.ReadAsync(buffer))>0){
                for(int i=0;i<count;i++){
                    var c=buffer[i];
                    if(c=='\n'){if(line.Length>0){await Dispatch(line.ToString().TrimEnd('\r'));line.Clear();}}
                    else{line.Append(c);if(line.Length>1024*1024)throw new MonitorException("MESSAGE_TOO_LARGE");}
                }
            }
            throw new MonitorException("TRANSPORT_EOF");
        }catch(Exception ex){
            fatal=ex is MonitorException?ex:new MonitorException(ex is JsonException?"NON_JSON_OUTPUT":"TRANSPORT_FAILURE");
            try{if(child?.Process.HasExited==true)log.ChildExitCode=child.Process.ExitCode;}catch{}
            foreach(var p in pending.Values)p.TrySetException(fatal);
            if(!disposed)Notification?.Invoke("transport/closed");
        }
    }
    internal async Task Dispatch(string line)
    {
        using var doc=JsonDocument.Parse(line);var r=doc.RootElement;
        if(r.TryGetProperty("method",out var method)){
            var name=method.GetString()??"";
            if(r.TryGetProperty("id",out var serverId)){await Send(new{id=serverId.Clone(),error=new{code=-32601,message="Read-only monitor"}},CancellationToken.None);log.Write("server_request","RPC_REFUSED");return;}
            if(name=="account/updated")Interlocked.Increment(ref epoch);
            if(name is "account/updated" or "account/rateLimits/updated")Notification?.Invoke(name);
            return;
        }
        if(!r.TryGetProperty("id",out var id)||!id.TryGetInt32(out int n))throw new MonitorException("INVALID_RESPONSE_ID");
        if(!pending.TryRemove(n,out var source)){log.Write("response","DUPLICATE_OR_LATE_ID");return;}
        if(r.TryGetProperty("error",out var e)){
            var code=e.TryGetProperty("code",out var ec)&&ec.TryGetInt32(out var value)?value:0;
            var msg=e.TryGetProperty("message",out var em)?em.GetString()??"":"";
            // Examine only in memory. Never persist upstream error text.
            var cls=code==-32601?"METHOD_UNSUPPORTED":msg.Contains("401")||msg.Contains("unauthorized",StringComparison.OrdinalIgnoreCase)?"AUTH_REQUIRED":msg.Contains("403")||msg.Contains("forbidden",StringComparison.OrdinalIgnoreCase)?"POLICY_DENIED":msg.Contains("network",StringComparison.OrdinalIgnoreCase)||msg.Contains("connect",StringComparison.OrdinalIgnoreCase)?"NETWORK_ERROR":"RPC_ERROR";
            source.TrySetException(new MonitorException(cls));
        }else if(r.TryGetProperty("result",out var result))source.TrySetResult(result.Clone());
        else source.TrySetException(new MonitorException("MISSING_RESULT"));
    }
    async Task DrainError(){try{var buffer=new char[2048];while(child!=null&&await child.Error.ReadAsync(buffer)>0){/* discard raw stderr, never log secrets */}}catch{}}
    public void Dispose(){if(disposed)return;disposed=true;fatal=new MonitorException("CANCELLED");foreach(var p in pending.Values)p.TrySetException(fatal);
            if(!disposed)Notification?.Invoke("transport/closed");child?.Dispose();child=null;}
}


