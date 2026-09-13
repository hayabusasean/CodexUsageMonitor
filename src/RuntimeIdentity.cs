using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
namespace CodexUsageMonitor;
internal sealed record InstanceIdentity(int Pid,long StartedUtcTicks,string Path,string Sha256,string Build,string DataRoot);
internal static class RuntimeIdentity {
 internal static string CanonicalRoot(string root){var full=Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(root));var standard=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)+@"\CodexUsageMonitor";return string.Equals(full,standard,StringComparison.OrdinalIgnoreCase)?standard:full.ToUpperInvariant();}
 internal static string Build=>typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion??L.Version;
 internal static string Display=>L.Version+" · Gold · "+Build.Split('+').Last();
 internal static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();}
 internal static InstanceIdentity Current(string root){using var p=Process.GetCurrentProcess();return new(p.Id,p.StartTime.ToUniversalTime().Ticks,Environment.ProcessPath!,Hash(Environment.ProcessPath!),Build,CanonicalRoot(root));}
 internal static void Write(string root)=>AtomicJson.Save(Path.Combine(root,"running-instance.json"),Current(root));
 internal static InstanceIdentity? Existing(string root){try{var id=AtomicJson.Load<InstanceIdentity?>(Path.Combine(root,"running-instance.json"),()=>null);if(id==null||!string.Equals(CanonicalRoot(root),id.DataRoot,StringComparison.OrdinalIgnoreCase))return null;using var p=Process.GetProcessById(id.Pid);return !p.HasExited&&p.StartTime.ToUniversalTime().Ticks==id.StartedUtcTicks&&string.Equals(p.MainModule?.FileName,id.Path,StringComparison.OrdinalIgnoreCase)?id:null;}catch{return null;}}
 internal static void Handoff(string root){var existing=Existing(root);string requested=Hash(Environment.ProcessPath!);if(existing?.Sha256==requested)return;
  string location=existing?.Path??GoldVisual.Copy("An earlier instance owns this data folder; its build cannot be verified.","既有程序使用此資料目錄，但尚無法驗證其建置身分。");
  AtomicJson.Save(Path.Combine(root,"last-instance-handoff.json"),new{requested_path=Environment.ProcessPath,requested_sha256=requested,existing,at=DateTimeOffset.UtcNow,status="DIFFERENT_OR_UNVERIFIED_BUILD"});
  MessageBox.Show(GoldVisual.Copy("The requested build was not started. The existing monitor remains active:\n","本次要求的版本未啟動。既有監控程序仍在執行：\n")+location,"CodexUsageMonitor · "+Display,MessageBoxButtons.OK,MessageBoxIcon.Information);
 }
}
