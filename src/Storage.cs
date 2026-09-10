using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
namespace CodexUsageMonitor;
internal sealed class Settings
{
    [System.Text.Json.Serialization.JsonPropertyName("ui_language")] public string UiLanguage{get;set;}="en-US";
    [System.Text.Json.Serialization.JsonPropertyName("settings_version")] public int SettingsVersion{get;set;}=2;
    public string ChartView{get;set;}="LATEST";
    public bool RadarEnabled{get;set;}=true;
    public string MigrationDiagnostic{get;set;}="";
    public Settings Copy()=>JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this))!;
    public static Settings Load(string path){
        if(!File.Exists(path))return new();
        try{
            if(new FileInfo(path).Length>1024*1024)throw new InvalidDataException();
            string raw=File.ReadAllText(path);using var doc=JsonDocument.Parse(raw);
            if(doc.RootElement.ValueKind!=JsonValueKind.Object)throw new InvalidDataException();
            var normalized=System.Text.Json.Nodes.JsonNode.Parse(raw)!.AsObject();
            if(normalized.TryGetPropertyValue("ui_language",out var rawLanguage)&&rawLanguage!=null&&rawLanguage.GetValueKind()!=JsonValueKind.String)normalized.Remove("ui_language");
            var v=JsonSerializer.Deserialize<Settings>(normalized.ToJsonString())??new();
            bool explicitLanguage=doc.RootElement.TryGetProperty("ui_language",out var lang);
            if(!explicitLanguage)explicitLanguage=doc.RootElement.TryGetProperty("UiLanguage",out lang);
            if(explicitLanguage){
                string? code=lang.ValueKind==JsonValueKind.String?lang.GetString():null;
                v.UiLanguage=L.Normalize(code);if(code is not ("en-US" or "zh-TW"))v.MigrationDiagnostic="INVALID_LANGUAGE_FALLBACK";
            }else{
                bool legacy=doc.RootElement.TryGetProperty("Interval",out _)||doc.RootElement.TryGetProperty("OpacityPercent",out _)||doc.RootElement.TryGetProperty("Compact",out _);
                v.UiLanguage=legacy?"zh-TW":"en-US";v.MigrationDiagnostic=legacy?"LEGACY_ZH_PRESERVED":"NEW_EN_DEFAULT";
            }
            if(!doc.RootElement.TryGetProperty("settings_version",out var ver)||!ver.TryGetInt32(out int n)||n<2){
                var backup=path+".pre-bilingual.bak";try{if(!File.Exists(backup))File.Copy(path,backup);}catch{v.MigrationDiagnostic="LEGACY_BACKUP_UNAVAILABLE_SETTINGS_PRESERVED";}
            }
            v.SettingsVersion=2;v.Validate();return v;
        }catch{
            try{if(new FileInfo(path).Length<=1024*1024&&!File.Exists(path+".invalid-bilingual.bak"))File.Copy(path,path+".invalid-bilingual.bak");}catch{}
            return new(){MigrationDiagnostic="INVALID_SETTINGS_PRESERVED"};
        }
    }
    public int Interval {get;set;}=90; public int RetentionDays {get;set;}=90; public bool Compact{get;set;} public bool TopMost{get;set;}=true;
    public string StartupMode{get;set;}="REMEMBER";
    public void ApplyStartupMode(){if(StartupMode=="STANDARD")Compact=false;else if(StartupMode=="COMPACT")Compact=true;}
    public int OpacityPercent{get;set;}=70;
    public bool Alerts{get;set;}=true;public bool StartHidden{get;set;}public bool Startup{get;set;}
    public string CodexPath{get;set;}="";public string SelectedPool{get;set;}="";
    public int? X{get;set;}public int? Y{get;set;}public int SavedDpi{get;set;}=96;public string ScreenName{get;set;}="";
    public string Salt{get;set;}=Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    public void Validate(){UiLanguage=L.Normalize(UiLanguage);if(ChartView is not ("LATEST" or "FULL"))ChartView="LATEST";if(!new[]{"REMEMBER","STANDARD","COMPACT"}.Contains(StartupMode))StartupMode="REMEMBER";if(OpacityPercent<40||OpacityPercent>100||OpacityPercent%5!=0)OpacityPercent=70;if(!new[]{60,90,180,300}.Contains(Interval))Interval=90;if(!new[]{30,90,180}.Contains(RetentionDays))RetentionDays=90;if(SavedDpi<48||SavedDpi>768)SavedDpi=96;try{if(Convert.FromBase64String(Salt).Length!=32)throw new Exception();}catch{Salt=Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));}}
}
internal static class AtomicJson
{
    public static T Load<T>(string path,Func<T> fallback)
    {
        if(!File.Exists(path))return fallback();
        try{if(new FileInfo(path).Length>4*1024*1024)throw new IOException();return JsonSerializer.Deserialize<T>(File.ReadAllText(path))??fallback();}
        catch{try{File.Move(path,path+".bad",true);}catch{}return fallback();}
    }
    public static void Save<T>(string path,T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temp=path+".tmp";
        using(var f=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(f,value,new JsonSerializerOptions{WriteIndented=true});f.Flush(true);}
        File.Move(temp,path,true);
    }
}
internal static class Csv
{
    public static string Cell(string? value,bool external=false)
    {
        var s=value??"";if(external&&s.TrimStart().Length>0&&"=+-@\t\r".Contains(s.TrimStart()[0]))s="'"+s;
        return "\""+s.Replace("\"","\"\"")+"\"";
    }
    public static IEnumerable<string[]> Read(TextReader reader)
    {
        var row=new List<string>();var field=new StringBuilder();bool quoted=false,atStart=true,any=false;
        while(true){
            int n=reader.Read();if(n<0){if(!quoted&&(any||field.Length>0||row.Count>0)){row.Add(field.ToString());yield return row.ToArray();}yield break;}
            char c=(char)n;any=true;
            if(quoted){if(c=='"'){if(reader.Peek()=='"'){reader.Read();field.Append('"');}else quoted=false;}else field.Append(c);continue;}
            if(c=='"'&&atStart){quoted=true;atStart=false;continue;}
            if(c==','){row.Add(field.ToString());field.Clear();atStart=true;continue;}
            if(c=='\n'){row.Add(field.ToString().TrimEnd('\r'));yield return row.ToArray();row.Clear();field.Clear();atStart=true;any=false;continue;}
            field.Append(c);atStart=false;
        }
    }
    public static PropertyInfo[] Props<T>()=>typeof(T).GetProperties();
    public static string Format(object? v)=>v switch{null=>"",DateTimeOffset d=>d.ToString("O",CultureInfo.InvariantCulture),bool b=>b?"true":"false",IFormattable f=>f.ToString(null,CultureInfo.InvariantCulture),_=>v.ToString()??""};
    public static string Line<T>(T record,bool external=false)=>string.Join(",",Props<T>().Select(p=>Cell(Format(p.GetValue(record)),external&&p.PropertyType==typeof(string))))+"\r\n";
    public static string Header<T>()=>string.Join(",",Props<T>().Select(p=>p.Name))+"\r\n";
    public static IEnumerable<T> Records<T>(string path) where T:new()
    {
        using var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
        using var r=new StreamReader(f,Encoding.UTF8,true);using var it=Read(r).GetEnumerator();
        if(!it.MoveNext())yield break;var headers=it.Current;var props=Props<T>();var index=props.Select(p=>Array.IndexOf(headers,p.Name)).ToArray();
        while(it.MoveNext()){
            var cells=it.Current;if(cells.Length!=headers.Length)continue;
            T item=new();bool valid=true;
            try{for(int i=0;i<props.Length;i++){
                int j=index[i];if(j<0)continue;var text=cells[j];var p=props[i];var type=Nullable.GetUnderlyingType(p.PropertyType)??p.PropertyType;
                if(text==""&&type!=typeof(string))continue;
                object value=type==typeof(string)?text:type==typeof(DateTimeOffset)?DateTimeOffset.Parse(text,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind):Convert.ChangeType(text,type,CultureInfo.InvariantCulture);
                p.SetValue(item,value);
            }}catch{valid=false;}
            if(valid)yield return item;
        }
    }
    // Only our daily CSVs are eligible. Preserve all complete records, quarantine at most 1 MiB of tail.
    public static void RepairTail(string path)
    {
        if(!File.Exists(path))return;
        using var f=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.Read);
        long complete=0;bool quoted=false;int b;
        while((b=f.ReadByte())>=0){
            if(b==34){if(quoted){int next=f.ReadByte();if(next==34)continue;quoted=false;if(next<0)break;f.Position--;}else quoted=true;}
            if(b==10&&!quoted)complete=f.Position;
        }
        if(complete<f.Length){
            f.Position=complete;byte[] tail=new byte[(int)Math.Min(1024*1024,f.Length-complete)];f.ReadExactly(tail);File.WriteAllBytes(path+".tail.bad",tail);f.SetLength(complete);f.Flush(true);
        }
    }
}
internal record HistoryFilter(DateTimeOffset From,DateTimeOffset To,string Pool="",int? Duration=null,string EventType="");
internal sealed class HistoryStore
{
    public FrozenHistory Freeze(DateTimeOffset asOf) {
        var dir=Path.Combine(Root,"neutral","export_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var files=new List<FrozenFile>();
        lock(gate){foreach(var src in Daily("usage").Concat(Daily("events")).Order()){
            string name=Path.GetFileName(src),dst=Path.Combine(dir,name);
            File.Copy(src,dst);files.Add(new(name,new FileInfo(dst).Length,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(dst))).ToLowerInvariant()));
        }}
        return new FrozenHistory(dir,asOf,files);
    }
    public string Root{get;} public string? SaveError{get;private set;} public int PendingCount=>queue.Count;public int DroppedBatches{get;private set;}
    readonly object gate=new(); readonly Queue<List<UsageSample>> queue=new();
    readonly Dictionary<string,UsageSample> previous=new();readonly Dictionary<string,HashSet<string>> ids=new();DateOnly? cleaned;
    public HistoryStore(string root){Root=root;foreach(var d in new[]{"history","logs","exports","neutral"})Directory.CreateDirectory(Path.Combine(root,d));}
    static string Address(UsageSample s)=>s.limit_id+"|"+s.window_duration_mins+"|"+(s.comparison_key.EndsWith("|"+s.source_slot)?s.source_slot:"unique");
    string PathFor<T>(DateTimeOffset time)=>Path.Combine(Root,"history",(typeof(T)==typeof(UsageSample)?"usage_":"events_")+time.UtcDateTime.ToString("yyyy-MM-dd")+".csv");
    HashSet<string> GetIds<T>(string path) where T:new()
    {
        if(ids.TryGetValue(path,out var set))return set;
        Csv.RepairTail(path);set=new();
        if(File.Exists(path))foreach(var row in Csv.Records<T>(path)){var id=row is UsageSample s?s.sample_id:((UsageEvent)(object)row!).event_id;set.Add(id);}
        if(ids.Count>=8)ids.Clear();ids[path]=set;return set;
    }
    void Append<T>(string path,List<T> records,Func<T,string> id) where T:new()
    {
        var known=GetIds<T>(path);var newRows=records.Where(r=>!known.Contains(id(r))).ToList();if(newRows.Count==0)return;
        bool header=!File.Exists(path)||new FileInfo(path).Length==0;
        try{
            using var f=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read);
            if(header){var bom=Encoding.UTF8.GetPreamble();f.Write(bom);f.Write(Encoding.UTF8.GetBytes(Csv.Header<T>()));}
            foreach(var r in newRows)f.Write(Encoding.UTF8.GetBytes(Csv.Line(r)));
            f.Flush(true);foreach(var r in newRows)known.Add(id(r));
        }catch{ids.Remove(path);throw;}
    }
    public void Recover()
    {
        lock(gate){
            try{
                previous.Clear();foreach(var file in Daily("usage").Order()){
                    foreach(var s in Csv.Records<UsageSample>(Repair(file))){
                        if(s.remaining_percent==null||s.window_duration_mins==null)continue;
                        previous.TryGetValue(Address(s),out var p);
                        var ev=EventDetector.Compare(p,s);
                        Append(PathFor<UsageEvent>(s.observed_at_utc),ev,e=>e.event_id);
                        previous[Address(s)]=s;
                    }
                }
                SaveError=null;
            }catch{SaveError="歷史修復暫時失敗；既有紀錄保留";}
        }
    }
    static string Repair(string path){Csv.RepairTail(path);return path;}
    IEnumerable<string> Daily(string kind)=>Directory.EnumerateFiles(Path.Combine(Root,"history"),kind+"_*.csv").Where(f=>System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f),@"^(usage|events)_\d{4}-\d{2}-\d{2}\.csv$"));
    public bool Save(List<UsageSample> samples)
    {
        lock(gate){
            if(queue.Count>=120){DroppedBatches++;SaveError="歷史未保存：緩衝已滿，有採樣遺失";return false;}
            queue.Enqueue(samples);
            try{
                while(queue.TryPeek(out var batch)){
                    foreach(var day in batch.GroupBy(s=>s.observed_at_utc.UtcDateTime.Date))Append(PathFor<UsageSample>(day.First().observed_at_utc),day.ToList(),s=>s.sample_id);
                    foreach(var s in batch){
                        if(s.remaining_percent==null||s.window_duration_mins==null)continue;
                        previous.TryGetValue(Address(s),out var p);var ev=EventDetector.Compare(p,s);
                        Append(PathFor<UsageEvent>(s.observed_at_utc),ev,e=>e.event_id);
                        previous[Address(s)]=s;
                    }
                    queue.Dequeue();
                }
                SaveError=DroppedBatches>0?$"歷史曾遺失 {DroppedBatches} 批；目前已恢復":null;return true;
            }catch{SaveError=$"歷史未保存：等待重試（{queue.Count}/120 批）";return false;}
        }
    }
    public IEnumerable<T> Query<T>(HistoryFilter filter) where T:new()
    {
        string kind=typeof(T)==typeof(UsageSample)?"usage":"events";
        foreach(var file in Daily(kind).OrderDescending()){
            if(!DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(file)[(kind.Length+1)..],"yyyy-MM-dd",out var day))continue;
            if(day<DateOnly.FromDateTime(filter.From.UtcDateTime)||day>DateOnly.FromDateTime(filter.To.UtcDateTime))continue;
            // Keep one day bounded by the 64 MiB store cap; UI takes only a page.
            foreach(var r in Csv.Records<T>(file)){
                var s=r as UsageSample;var e=r as UsageEvent;
                var t=s?.observed_at_utc??e!.first_observed_at_utc;
                if(t<filter.From||t>filter.To)continue;
                if(filter.Pool!=""&&(s?.limit_id??e!.limit_id)!=filter.Pool)continue;
                if(filter.Duration.HasValue&&(s?.window_duration_mins??e!.window_duration_mins)!=filter.Duration)continue;
                if(filter.EventType!=""&&(e==null||e.event_type!=filter.EventType))continue;
                yield return r;
            }
        }
    }
    public List<T> Page<T>(HistoryFilter filter,int page,int size=200) where T:new()
    {
        // bounded top-k heap; never load the entire history into UI or memory
        var heap=new PriorityQueue<T,DateTimeOffset>();int count=checked((Math.Min(page,499)+1)*size);
        foreach(var r in Query<T>(filter)){
            var t=r is UsageSample s?s.observed_at_utc:((UsageEvent)(object)r!).first_observed_at_utc;
            heap.Enqueue(r,t);if(heap.Count>count)heap.Dequeue();
        }
        return heap.UnorderedItems.OrderByDescending(x=>x.Priority).Skip(page*size).Select(x=>x.Element).ToList();
    }
    public string Export<T>(HistoryFilter filter,string? path=null) where T:new()
    {
        path??=Path.Combine(Root,"exports",(typeof(T)==typeof(UsageSample)?"採樣_":"事件_")+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+".csv");
        var tmp=path+".tmp";using(var f=new StreamWriter(tmp,false,new UTF8Encoding(true))){f.Write(Csv.Header<T>());foreach(var r in Query<T>(filter))f.Write(Csv.Line(r,true));}
        File.Move(tmp,path,true);return path;
    }
    public void Cleanup(int days,DateTimeOffset now,long cap=64*1024*1024)
    {
        lock(gate){
            var today=DateOnly.FromDateTime(now.UtcDateTime);if(cleaned==today)return;
            try{
                var owned=Daily("usage").Concat(Daily("events")).Concat(Directory.EnumerateFiles(Path.Combine(Root,"logs"),"app_*.jsonl*").Where(f=>System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f),@"^app_\d{4}-\d{2}-\d{2}\.jsonl(\.1)?$"))).Select(f=>new FileInfo(f)).OrderBy(f=>f.Name).ToList();
                long total=owned.Sum(f=>f.Length);
                foreach(var f in owned.OrderBy(f=>f.LastWriteTimeUtc)){
                    var match=System.Text.RegularExpressions.Regex.Match(f.Name,@"\d{4}-\d{2}-\d{2}");
                    if(!DateOnly.TryParseExact(match.Value,"yyyy-MM-dd",out var date)||date>=today)continue;
                    if(date<today.AddDays(-days)||total>cap){var length=f.Length;f.Delete();total-=length;ids.Remove(f.FullName);}
                }
                cleaned=today;
            }catch{SaveError="歷史清理失敗；既有資料保留";}
        }
    }
}
internal sealed class AlertState
{
    public Dictionary<string,int> Levels{get;set;}=new();
    public List<string> Observe(List<UsageSample> samples)
    {
        var messages=new List<string>();
        foreach(var s in samples){
            if(s.remaining_percent is not decimal remaining||s.data_quality=="SOURCE_DATA_INVALID")continue;
            var k=s.comparison_key;Levels.TryGetValue(k,out int old);
            int level=remaining==0?3:remaining<=5?2:remaining<=20?1:0;
            int retained=old;
            if(old==3&&remaining>1)retained=remaining<=5?2:remaining<=20?1:0;
            if(old>=2&&remaining>6)retained=remaining<=20?1:0;
            if(old>=1&&remaining>21)retained=0;
            if(level>retained){messages.Add($"{s.limit_id} {(s.window_duration_mins==10080?"每週":s.window_duration_mins==300?"5 小時":s.window_duration_mins+" 分鐘")}方案額度 {remaining.ToString("0.###",CultureInfo.InvariantCulture)}%");retained=level;}
            Levels[k]=retained;
        }
        if(Levels.Count>500)Levels=Levels.Where(kv=>samples.Any(s=>s.comparison_key==kv.Key)).ToDictionary();
        return messages;
    }
}


internal record FrozenFile(string alias,long high_water_bytes,string sha256);
internal sealed record FrozenHistory(string DirectoryPath,DateTimeOffset AsOf,List<FrozenFile> Files):IDisposable {
 public IEnumerable<T> Rows<T>() where T:new()=>Directory.EnumerateFiles(DirectoryPath,typeof(T)==typeof(UsageSample)?"usage_*.csv":"events_*.csv").Order().SelectMany(Csv.Records<T>);
 public void Dispose(){try{Directory.Delete(DirectoryPath,true);}catch{}}
}
