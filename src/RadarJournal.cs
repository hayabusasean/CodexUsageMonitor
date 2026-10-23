using System.Text;
using System.Text.Json;
namespace CodexUsageMonitor;

// Compact daily append-only audit records. The current snapshot retains only current state.
internal sealed class RadarJournal(string root)
{
    readonly HashSet<string> written=new(StringComparer.Ordinal);
    string Folder=>Path.Combine(root,"radar_history");
    internal List<T> Read<T>(string prefix,Func<T,DateTimeOffset> timestamp){
        var rows=new List<T>();if(!Directory.Exists(Folder))return rows;
        foreach(string path in Directory.EnumerateFiles(Folder,prefix+"_*.jsonl").Order().TakeLast(7))
            foreach(string line in File.ReadLines(path))try{
                var item=JsonSerializer.Deserialize<T>(line);if(item is null)continue;
                string key=prefix+"|"+Safe.Hash(JsonSerializer.Serialize(item));
                if(written.Add(key))rows.Add(item);
            }catch(JsonException){}
        return rows.Where(x=>timestamp(x)>=DateTimeOffset.UtcNow.AddHours(-96)).ToList();
    }
    internal void Append<T>(string prefix,IEnumerable<T> rows,Func<T,DateTimeOffset> timestamp){
        foreach(var row in rows){string json=JsonSerializer.Serialize(row),key=prefix+"|"+Safe.Hash(json);if(written.Contains(key))continue;
            Directory.CreateDirectory(Folder);string path=Path.Combine(Folder,prefix+"_"+timestamp(row).UtcDateTime.ToString("yyyy-MM-dd")+".jsonl");
            bool needsNewline=false;if(File.Exists(path)){using var file=File.OpenRead(path);if(file.Length>0){file.Seek(-1,SeekOrigin.End);needsNewline=file.ReadByte()!=10;}}
            File.AppendAllText(path,(needsNewline?"\n":"")+json+"\n",new UTF8Encoding(false));written.Add(key);
        }
    }
    internal void Cleanup(DateTimeOffset now){
        if(!Directory.Exists(Folder))return;
        var cutoff=DateOnly.FromDateTime(now.UtcDateTime).AddDays(-6);string directory=Path.GetFullPath(Folder);
        foreach(string prefix in new[]{"source_checks","coverage"})foreach(string path in Directory.EnumerateFiles(directory,prefix+"_*.jsonl")){
            string full=Path.GetFullPath(path);if(!string.Equals(Path.GetDirectoryName(full),directory,StringComparison.OrdinalIgnoreCase))continue;
            string date=Path.GetFileNameWithoutExtension(full)[(prefix.Length+1)..];
            if(DateOnly.TryParseExact(date,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var day)&&day<cutoff)File.Delete(full);
        }
    }
}
