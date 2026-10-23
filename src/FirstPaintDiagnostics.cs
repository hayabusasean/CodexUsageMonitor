using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace CodexUsageMonitor;

internal sealed record FirstPaintSurfaceState(string scene,int window_dpi,int client_width,int client_height,
 int? bitmap_width,int? bitmap_height,int bitmap_builds,double bitmap_build_ms,int shared_cache_builds,bool high_contrast);
internal sealed record FirstPaintDiagnosticEntry(string schema,string window_instance_id,string window_type,string phase,
 DateTimeOffset utc,FirstPaintSurfaceState surface,double? paint_elapsed_ms,string outcome);

// One before/after pair per window instance. No titles, account values, paths or
// source payloads enter the record. A failed diagnostic write never affects paint.
internal sealed class FirstPaintDiagnostics {
 readonly object gate=new();readonly Action<FirstPaintDiagnosticEntry> sink;
 readonly Func<DateTimeOffset> utcNow;readonly Func<long> timestamp;readonly long frequency;
 readonly string instance=Guid.NewGuid().ToString("N"),windowType;int phase;long paintStart;
 public string? LastWriteError{get;private set;}
 public bool Pending=>Volatile.Read(ref phase)==0;
 public FirstPaintDiagnostics(string root,string windowType,Action<FirstPaintDiagnosticEntry>? sink=null,
  Func<DateTimeOffset>? utcNow=null,Func<long>? timestamp=null,long? frequency=null){
  this.windowType=Regex.IsMatch(windowType,"^[A-Za-z][A-Za-z0-9_]{0,63}$")?windowType:"ProductWindow";
  this.utcNow=utcNow??(()=>DateTimeOffset.UtcNow);this.timestamp=timestamp??Stopwatch.GetTimestamp;this.frequency=frequency??Stopwatch.Frequency;
  string directory=Path.Combine(root,"diagnostics");string? file=null;
  this.sink=sink??(entry=>{Directory.CreateDirectory(directory);file??=Path.Combine(directory,"first_paint_"+entry.utc.ToString("yyyy-MM-dd")+".jsonl");var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry)+"\n");using var output=new FileStream(file,FileMode.Append,FileAccess.Write,FileShare.ReadWrite);output.Write(bytes);});
 }
 void Write(string name,FirstPaintSurfaceState surface,double? elapsed,string outcome){
  try{sink(new("codex-usage-first-paint.v1",instance,windowType,name,utcNow().ToUniversalTime(),surface,elapsed,outcome));}
  catch{LastWriteError="FIRST_PAINT_DIAGNOSTIC_WRITE_FAILED";}
 }
 public bool Begin(FirstPaintSurfaceState before){lock(gate){if(phase!=0)return false;phase=1;Write("before_paint",before,null,"PENDING");paintStart=timestamp();return true;}}
 public void Complete(FirstPaintSurfaceState after,bool failed=false){lock(gate){if(phase!=1)return;phase=2;double elapsed=Math.Max(0,(timestamp()-paintStart)*1000d/Math.Max(1,frequency));Write("after_paint",after,elapsed,failed?"PAINT_FAILED":"OK");}}
}
