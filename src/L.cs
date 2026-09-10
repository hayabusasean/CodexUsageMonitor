
using System.Collections.Concurrent;
using System.Globalization;
using System.Resources;
namespace CodexUsageMonitor;
internal static class L {
 public const string Version="0.4.0-rc.3";
 static readonly ConcurrentDictionary<string,ResourceManager> Managers=new();
 static readonly HashSet<string> Groups=["History","Radar","Report"];
 static string language="en-US";
 public static string Language=>language;
 public static CultureInfo Culture=>CultureInfo.GetCultureInfo(language);
 public static event Action? Changed;
 public static int TimeZoneRevision{get;private set;}
 public static void RefreshTimeZone(){TimeZoneInfo.ClearCachedData();TimeZoneRevision++;Changed?.Invoke();}
 public static readonly ConcurrentDictionary<string,bool> Missing=new();
 public static string Normalize(string? value)=>value=="zh-TW"?"zh-TW":"en-US";
 public static void SetLanguage(string? value){var next=Normalize(value);if(language==next)return;language=next;Changed?.Invoke();}
 public static string T(string key,params object?[] args)=>TFor(language,key,args);
 public static string TFor(string? lang,string key,params object?[] args){
  string[] parts=key.Split('.',2);bool group=parts.Length==2&&Groups.Contains(parts[0]);string name=group?parts[0]:"Ui",id=group?parts[1]:key;
  var rm=Managers.GetOrAdd(name,n=>new ResourceManager("CodexUsageMonitor.Resources."+n,typeof(L).Assembly));
  string? value=null;try{value=rm.GetString(id,CultureInfo.GetCultureInfo(Normalize(lang)))??rm.GetString(id,CultureInfo.InvariantCulture);}catch(MissingManifestResourceException){}
  if(value==null){if(Missing.Count<128)Missing.TryAdd(key,true);value=Normalize(lang)=="zh-TW"?"文字暫時無法顯示":"Text unavailable";}
  try{return args.Length==0?value:string.Format(CultureInfo.InvariantCulture,value,args);}catch(FormatException){if(Missing.Count<128)Missing.TryAdd(key+":format",true);return Normalize(lang)=="zh-TW"?"資訊暫時無法顯示":"Information unavailable";}
 }
 public static void Watch(Control owner,Action refresh){
  void Apply(){if(owner.IsDisposed)return;if(owner.IsHandleCreated&&owner.InvokeRequired){try{owner.BeginInvoke(refresh);}catch(InvalidOperationException){}}else refresh();}
  Changed+=Apply;owner.Disposed+=(_,_)=>Changed-=Apply;Apply();
 }
 public static void Bind(Control c,string key)=>Watch(c,()=>{c.Text=T(key);c.AccessibleName=c.Text;});
 public static Label Label(string key,int height=28,float size=10.5f,Color? color=null){var c=Theme.Label(T(key),height,size,color);Bind(c,key);return c;}
 public static Button Button(string key,EventHandler? click=null,bool primary=false,int width=0){var c=Theme.Button(T(key),click,primary);c.Width=width>0?width:Math.Max(Theme.MeasureButton(TFor("en-US",key)),Theme.MeasureButton(TFor("zh-TW",key)));Bind(c,key);return c;}
 public static CheckBox Check(string key,bool value=false){var c=Theme.Check(T(key),value);Bind(c,key);return c;}
 public static string Percent(decimal? value)=>value.HasValue?value.Value.ToString("0.####",CultureInfo.InvariantCulture)+"%":"—";
 public static string Window(int? minutes)=>minutes==10080?T("Details.Weekly"):minutes==300?T("Details.FiveHour"):T("Details.Minutes",minutes);
 public static string Health(MonitorService s)=>s.History.SaveError!=null?T("State.SaveFailed"):s.Verified?T("Settings.Connected"):string.IsNullOrEmpty(s.LastErrorCode)?T("Overlay.Connecting"):MonitorService.Explain(s.LastErrorCode);
}

