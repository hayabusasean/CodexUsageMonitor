using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace CodexUsageMonitor;

internal sealed record Announcement(
 string EventId,string RevisionId,string Source,string Url,int Tier,string Confidence,string Title,string Summary,
 DateTimeOffset? PublishedAt,string? SourceDate,DateTimeOffset? ResetUtc,string SourceTimeZone,string TimeQuality,
 string Scope,string ResetType,string Phase,string ContentHash,DateTimeOffset FirstSeen,DateTimeOffset LastSeen,
 DateTimeOffset? ReadAt=null,DateTimeOffset? MajorTime=null,string Eligibility="UNKNOWN",string IdentityConfidence="LOW_STRUCTURED_FALLBACK",string Effect="UNKNOWN",string OriginalId="",string PublicationBasis="",string Migration="");
internal sealed record SourceStatus(string Id,string Url,string Status="NOT_CHECKED",DateTimeOffset? LastAttempt=null,
 DateTimeOffset? LastSuccess=null,DateTimeOffset? NextCheck=null,int Failures=0,int HttpStatus=0,int CandidateCount=0,
 string? ETag=null,DateTimeOffset? Modified=null,string BodyHash="",long ResponseBytes=0,string Error="",string CandidateExcerpt="",string FinalUrl="",DateTimeOffset? RetryAfterUtc=null);
internal sealed record ResetCorrelation(string PreviousSample,string CurrentSample,DateTimeOffset From,DateTimeOffset To,
 decimal? Before,decimal? After,string Classification,string Reason,string? AnnouncementId,bool ScopeVerified,bool Scheduled,
 string AnnouncementStatus="UNVERIFIED",string LocalObservation="NOT_COMPARABLE",string Correlation="NOT_ASSESSED",string Cause="UNKNOWN");
internal sealed class RadarState {
 public List<Announcement> Events{get;set;}=[];public List<SourceStatus> Sources{get;set;}=[];public List<ResetCorrelation> Correlations{get;set;}=[];
}
internal static class AnnouncementParser {
 static readonly TimeSpan RegexBudget=TimeSpan.FromMilliseconds(500);
 internal static Match Match(string text,string pattern)=>Regex.Match(text,pattern,RegexOptions.IgnoreCase|RegexOptions.Singleline,RegexBudget);
 internal static MatchCollection Matches(string text,string pattern)=>Regex.Matches(text,pattern,RegexOptions.IgnoreCase|RegexOptions.Singleline,RegexBudget);
 internal static string Plain(string html){
  string s=Regex.Replace(html,@"<(script|style)\b[^>]*>.*?</\1>"," ",RegexOptions.IgnoreCase|RegexOptions.Singleline,RegexBudget);
  s=Regex.Replace(s,@"<[^>]+>"," ",RegexOptions.Singleline,RegexBudget);return Regex.Replace(WebUtility.HtmlDecode(s),@"\s+"," ").Trim();
 }
 internal static string Clean(string s,int max=1200){s=Regex.Replace(s,@"(?i)([a-z]:\\\S+|[\w.+-]+@[\w.-]+\.[a-z]{2,}|bearer\s+\S+|sk-[a-z0-9_-]+)","[REDACTED]");return s.Length>max?s[..max]:s;}
 internal static DateOnly? Date(string s){
  var m=Match(s,@"\b(20\d{2}-\d{2}-\d{2})\b");if(m.Success&&DateOnly.TryParseExact(m.Value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var iso))return iso;
  m=Match(s,@"\b(January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2}(?:st|nd|rd|th)?,?\s+20\d{2}\b");
  if(m.Success&&DateOnly.TryParse(Regex.Replace(m.Value,@"(?<=\d)(st|nd|rd|th)",""),CultureInfo.InvariantCulture,DateTimeStyles.None,out var day))return day;return null;
 }
 internal static (DateTimeOffset? utc,string zone,string quality) Time(string text,DateTimeOffset? published){
  var iso=Match(text,@"\b20\d{2}-\d\d-\d\dT\d\d:\d\d(?::\d\d)?(?:Z|[+-]\d\d:\d\d)");
  bool deadline=Match(text,@"\bby\s+(?:\d|January|February|March|April|May|June|July|August|September|October|November|December)").Success;bool approx=Match(text,@"\b(around|approximately|approx|about)\b|~").Success;
  if(iso.Success&&DateTimeOffset.TryParse(iso.Value,CultureInfo.InvariantCulture,DateTimeStyles.None,out var dt))return(dt.ToUniversalTime(),"UTC/OFFSET",deadline?"DEADLINE":approx?"APPROXIMATE":"EXPLICIT");
  var m=Match(text,@"\b(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<ap>AM|PM)?\s*(?<tz>PDT|PST|PT|UTC)\b");
  if(!m.Success)return(null,"UNKNOWN","UNKNOWN");int hour=int.Parse(m.Groups["h"].Value),minute=m.Groups["m"].Success?int.Parse(m.Groups["m"].Value):0;
  string ap=m.Groups["ap"].Value.ToUpperInvariant(),zone=m.Groups["tz"].Value.ToUpperInvariant();
  if(minute>59||(ap!=""&&(hour<1||hour>12))||(ap==""&&hour>23))return(null,zone,"INVALID");
  if(ap!="")hour=hour%12+(ap=="PM"?12:0);
  var pacific=TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
  var date=Date(text);if(date==null&&published!=null){var sourceNow=zone=="UTC"?published.Value:TimeZoneInfo.ConvertTime(published.Value,pacific);date=DateOnly.FromDateTime(sourceNow.DateTime);if(Match(text,@"\btomorrow\b").Success)date=date.Value.AddDays(1);}
  if(date==null)return(null,zone,"UNKNOWN");
  if(Date(text)!=null&&Match(text,@"\btomorrow\b").Success){
   // A date followed by a colon at the beginning is the publication anchor.
   // "tomorrow, September 9" already names the execution date.
   var anchor=Match(text,@"^\s*(?:20\d{2}-\d\d-\d\d|[A-Za-z]+\s+\d{1,2},?\s+20\d{2})\s*:");
   if(anchor.Success)date=date.Value.AddDays(1);
   else if(!Match(text,@"\btomorrow\s*,\s*(?:20\d{2}-\d\d-\d\d|[A-Za-z]+\s+\d{1,2})").Success)return(null,zone,"AMBIGUOUS_DATE");
  }
  var local=date.Value.ToDateTime(new TimeOnly(hour,minute),DateTimeKind.Unspecified);
  if(zone=="PT"&&(pacific.IsInvalidTime(local)||pacific.IsAmbiguousTime(local)))return(null,zone,"DST_AMBIGUOUS");
  TimeSpan offset=zone=="UTC"?TimeSpan.Zero:zone=="PDT"?TimeSpan.FromHours(-7):zone=="PST"?TimeSpan.FromHours(-8):pacific.GetUtcOffset(local);
  return(new DateTimeOffset(local,offset).ToUniversalTime(),zone,deadline?"DEADLINE":approx?"APPROXIMATE":"EXPLICIT");
 }
 internal static string LocalTime(Announcement a,DateTimeOffset now,TimeZoneInfo? zone=null,string? language=null){
  language??=L.Language;zone??=TimeZoneInfo.Local;
  string T(string key,params object[] args)=>L.TFor(language,"Radar."+key,args);
  if(a.ResetUtc==null)return a.SourceDate==null?T("TimeUnknown"):T("TimeUnknownDate",a.SourceDate);
  var local=TimeZoneInfo.ConvertTime(a.ResetUtc.Value,zone);
  string offset=(local.Offset<TimeSpan.Zero?"-":"+")+local.Offset.Duration().ToString(@"hh\:mm",CultureInfo.InvariantCulture);
  string value=local.ToString("yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture)+" (UTC"+offset+")";
  return T(a.TimeQuality=="APPROXIMATE"?"TimeAround":a.TimeQuality=="DEADLINE"?"TimeBy":"TimeExact",value);
 }
 // Kept only for the historical R004 fixture API. Production always uses LocalTime.
 internal static string Taipei(Announcement a,DateTimeOffset now)=>LocalTime(a,now,TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time"),"zh-TW");
 internal static string CanonicalUrl(string url){
  if(!Uri.TryCreate(url,UriKind.Absolute,out var uri))return "";
  var path=Regex.Replace(uri.AbsolutePath,@"^/(en(?:-US)?|zh(?:-TW|-CN|-Hant|-Hans)?)/","/",RegexOptions.IgnoreCase);
  path=Regex.Replace(path,@"^/articles/(\d+)(?:-[^/]*)?$","/articles/$1",RegexOptions.IgnoreCase);
  return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant()+path.TrimEnd('/');
 }
 internal static Announcement? Candidate(string source,string url,int tier,string key,string title,string body,DateTimeOffset? published,DateTimeOffset now){
  if(body.Length>20000)body=body[..20000];body=Clean(body);string originalBody=body;body=Semantic(body);
  if(!Match(title+" "+body,@"\b(Codex|Astra)\b").Success)return null;
  if(!Match(body,@"\b(global reset|usage reset|rate[- ]limit reset|weekly (?:allowance|quota) reset|reset incoming|reset.{0,45}(?:Codex|usage|limits|allowance)|(?:Codex|usage|limits|allowance).{0,45}reset)\b").Success)return null;
  if(!Match(body,@"\b(will|incoming|provided|applied|resetting|completed|started|begin|today|tomorrow|tonight|global reset)\b").Success)return null;
  bool cancelled=Match(body,@"\b(?:planned |announced |scheduled )?(?:Codex |global |usage )?reset[^.!?]{0,160}\b(?:cancelled|canceled)\b").Success&&
   !Match(body,@"\b(?:not|never)\s+(?:\w+\s+){0,3}(?:cancelled|canceled)\b").Success;
  if(!cancelled&&Match(body,@"\b(will not|won't|not going to|no plans? to)\s+(?:\w+\s+){0,2}reset\b|\b(?:there (?:is|will be) )?no\s+(?:global |usage |Codex )?reset\b").Success)return null;
  bool uncertain=Match(body,@"\b(may|might|could|possibly)\b").Success;

  var date=Date(body);if(published==null&&date==null)return null; // Undated FAQs are never fresh announcements.
  var time=Time(body,published);
  bool futureEvent=time.utc>=now&&time.quality is "EXPLICIT" or "APPROXIMATE" or "DEADLINE";
  if(date!=null&&date<DateOnly.FromDateTime(now.UtcDateTime).AddDays(-2)&&!futureEvent)return null;
  if(published!=null&&(published>now.AddMinutes(10)||(now-published>TimeSpan.FromHours(48)&&!futureEvent)))return null;
  if(published==null&&!futureEvent&&(date!.Value<DateOnly.FromDateTime(now.UtcDateTime).AddDays(-2)||date> DateOnly.FromDateTime(now.UtcDateTime).AddDays(2)))return null;
  string effect=EffectOf(body);string type=Kind(body);
  string scope=Match(body,@"\bPlus\b").Success?"PLUS":"";
  foreach(string plan in new[]{"Pro","Business","Enterprise"})if(Match(body,@"\b"+plan+@"\b").Success)scope+=(scope==""?"":"|")+plan.ToUpperInvariant();
  if(scope=="")scope=Match(body,@"\ball users\b").Success?"ALL":Match(body,@"\ball paid plans\b").Success?"PAID":"UNKNOWN";
  string phase=cancelled?"CANCELLED":PhaseOf(body,effect,time.utc,now);
  string eligibility=Match(body,@"\beligib(?:le|ility)\b").Success?"CONDITIONAL":"AS_ANNOUNCED";
  string id=Safe.Hash(source+"|"+new Uri(url).GetLeftPart(UriPartial.Path)+"|"+key);
  string features=type+"|"+effect+"|"+scope+"|"+phase+"|"+eligibility+"|"+time.utc;
  return new(id,Safe.Hash(features),source,url,tier,tier==1?(uncertain||effect is "UNKNOWN" or "MECHANISM_OR_PURCHASE"?"MEDIUM":"HIGH"):"LOW",Clean(title,160),originalBody,published,date?.ToString("yyyy-MM-dd"),time.utc,time.zone,time.quality,scope,type,phase,Safe.Hash(body),now,now,null,time.utc,eligibility,"CALLER_KEY",effect,key);
 }

 // Phase is a claim about this event, not a match on a passive verb anywhere in the notice.
 // Only affirmative actual-tense evidence overrides the default planned state.
 internal static string PhaseOf(string body,string effect,DateTimeOffset? resetUtc,DateTimeOffset now){
  bool Asserted(string pattern){
   foreach(Match evidence in Matches(body,pattern)){
    string prefix=body[..evidence.Index];
    int boundary=prefix.LastIndexOfAny(['.','!','?',';',',']);
    string context=prefix[(boundary+1)..];if(context.Length>180)context=context[^180..];
    if(Match(context,@"\b(?:will|shall|would|should|may|might|could|can|must|going\s+to|(?:is|are)\s+(?:due\s+)?to|expected\s+to|scheduled\s+to|planned\s+to|once|when|if)\b[^.!?;]{0,120}$").Success)continue;
    if(Match(context,@"\b(?:not|never|no|hasn't|haven't|wasn't|weren't|isn't|aren't|didn't)\b[^.!?;]{0,90}$").Success)continue;
    // An unrelated completed document or provided instruction is not a completed reset.
    if(Match(context,@"\b(?:instructions?|documentation|guidance|examples?|information)\b[^.!?;]{0,70}$").Success)continue;
    bool eventSubject=Match(context+" "+evidence.Value,@"\b(?:reset|quota|credits?)\b").Success||
     Match(context,@"^\s*(?:it|this)\s+").Success&&Match(prefix,@"\b(?:reset|quota|credits?)\b").Success;
    bool eventObject=Match(body[(evidence.Index+evidence.Length)..],@"^\s+(?:the\s+|a\s+|Codex\s+|global\s+|usage\s+|quota\s+|banked\s+)*reset(?:\s+credits?)?\b").Success;
    if(eventSubject||eventObject)return true;
   }
   return false;
  }
  string completed=@"\b(?:(?:has|have)\s+(?:already\s+)?(?:been\s+)?completed|(?:is|was|were)\s+(?:already\s+)?completed|completed|reset\s+took\s+place)\b";
  if(effect=="AUTOMATIC_QUOTA_RESET")completed+=@"|\b(?:(?:has|have)\s+(?:already\s+)?been\s+(?:already\s+)?applied|(?:was|were)\s+(?:already\s+)?applied)\b";
  if(effect is "AUTOMATIC_QUOTA_RESET" or "BANKED_CREDIT_GRANT")completed+=@"|\b(?:(?:has|have)\s+(?:already\s+)?been\s+(?:already\s+)?provided|(?:was|were)\s+(?:already\s+)?provided)\b";
  if(Asserted(completed))return "COMPLETED";
  if(Asserted(@"\b(?:has\s+(?:already\s+)?begun|is\s+(?:currently\s+)?underway|(?:has\s+)?started)\b"))return "STARTED";
  // A future timestamp without affirmative completion is still an announced future event.
  if(resetUtc>now)return "PLANNED";
  return "PLANNED";
 }

 // Scope is audience; it never proves what happens to quota.
 internal static string Semantic(string text){
  foreach(var pair in new[]{("全球重置","global reset"),("所有付費方案","all paid plans"),("全部付費方案","all paid plans"),("所有使用者","all users"),("重置券","banked reset credit"),("不會自動補滿","does not refill automatically"),("將獲得","will receive"),("將於","will begin"),("明天","tomorrow"),("今天","today"),("大約","around"),("已取消","has been cancelled"),("可能","may")})text=text.Replace(pair.Item1,pair.Item2,StringComparison.OrdinalIgnoreCase);
  return text;
 }
 internal static string EffectOf(string text){
  string body=Semantic(text);
  bool credit=Match(body,@"\b(?:banked\s+reset\s+credits?|reset\s+credits?)\b").Success;
  if(credit&&Match(body,@"\b(?:not|never)\s+(?:\w+\s+){0,5}(?:receiv\w*|grant\w*|giv\w*|issu\w*|provid\w*)\b|\bno\s+(?:new\s+)?(?:Codex\s+)?(?:banked\s+)?reset\s+credits?\b|\b(?:grant\w*|receiv\w*)\s+no\b").Success)return "UNKNOWN";
  if(credit&&Match(body,@"\b(?:receiv(?:e|es|ed|ing)|grant(?:s|ed|ing)?|giv(?:e|es|en|ing)|issu(?:e|es|ed|ing)|provid(?:e|es|ed|ing))\b").Success)return "BANKED_CREDIT_GRANT";
  if(Match(body,@"\b(?:purchas(?:e|es|ed|ing)|buy|buying|mechanism|how\s+to|can\s+(?:use|redeem)|available\s+(?:for|to))\b").Success)return "MECHANISM_OR_PURCHASE";
  if(Match(body,@"\b(?:does\s+not|will\s+not|won't|not)\s+(?:automatically\s+)?(?:refill|replenish|reset)|\bno\s+automatic\s+(?:quota\s+)?(?:reset|refill)").Success)return "UNKNOWN";
  if(!credit&&Match(body,@"\bglobal reset\b|\b(?:usage|quota|allowance|limits|rate[- ]limits?)[^.!?]{0,45}\breset\b|\breset[^.!?]{0,45}\b(?:usage|quota|allowance|limits)\b").Success)return "AUTOMATIC_QUOTA_RESET";
  return "UNKNOWN";
 }
 internal static string Kind(string body)=>EffectOf(body) switch{
  "BANKED_CREDIT_GRANT"=>"BANKED","MECHANISM_OR_PURCHASE"=>"MECHANISM",
  "AUTOMATIC_QUOTA_RESET"=>Match(Semantic(body),@"\bglobal reset\b|\ball (?:users|paid plans)\b").Success?"GLOBAL":"QUOTA_RESET",_=>"UNKNOWN"};
 internal static string Attr(string tag,string name){
  var m=Match(tag,@"(?:^|\s)"+Regex.Escape(name)+@"\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))");
  return WebUtility.HtmlDecode(m.Groups[1].Success?m.Groups[1].Value:m.Groups[2].Success?m.Groups[2].Value:m.Groups[3].Value);
 }
 static DateTimeOffset? Publication(string article,out string basis,out string dateParagraph){
  basis="";dateParagraph="";
  // Time elements in the article header or explicitly marked publication time are publication metadata.
  // Last-modified/FAQ updated dates are never substituted.
  foreach(Match m in Matches(article,@"<time\b[^>]*>.*?</time>")){
   string tag=m.Value[..(m.Value.IndexOf('>')+1)];
   if(Match(tag+" "+Plain(m.Value),@"updated|modified|reset[-_ ]time|execution").Success)continue;
   string header=Match(article,@"<header\b[^>]*>.*?</header>").Value;
   if(header.Contains(m.Value,StringComparison.Ordinal)&&Match(Plain(header),@"last\s+(?:updated|modified|reviewed)|updated\s+on|modified\s+on").Success)continue;
   bool trusted=header.Contains(m.Value,StringComparison.Ordinal)||Match(tag,@"datePublished|pubdate|published").Success||
    (article.IndexOf(m.Value,StringComparison.Ordinal)<Match(article,@"<p\b").Index&&!Match(Plain(Match(article,@"<h[123]\b[^>]*>.*?</h[123]>").Value),@"FAQ|how to").Success);
   if(!trusted)continue;
   string raw=Attr(tag,"datetime");
   if(Match(raw,@"^\d{4}-\d\d-\d\dT.*(?:Z|[+-]\d\d:\d\d)$").Success&&DateTimeOffset.TryParse(raw,CultureInfo.InvariantCulture,DateTimeStyles.None,out var instant)){basis="ARTICLE_TIME";return instant.ToUniversalTime();}
   if(DateOnly.TryParseExact(raw,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day)){basis="ARTICLE_DATE_ONLY";return new DateTimeOffset(day.ToDateTime(new TimeOnly(12,0)),TimeSpan.Zero);}
  }
  // Same-article leading standalone date, or a leading dated announcement body.
  foreach(Match m in Matches(article,@"<(?:p|h[234])\b[^>]*>(.*?)</(?:p|h[234])>").Cast<Match>().Take(3)){
   string text=Plain(m.Groups[1].Value);var day=Date(text);if(day==null)continue;
   if(Match(text,@"updated|modified|last reviewed|copyright|FAQ").Success)continue;
   var dated=Match(text,@"^(?:20\d{2}-\d\d-\d\d|[A-Za-z]+\s+\d{1,2},?\s+20\d{2})\s*:");
   bool standalone=Match(text,@"^(?:20\d{2}-\d\d-\d\d|[A-Za-z]+\s+\d{1,2},?\s+20\d{2})$").Success;
   if(!standalone&&!dated.Success)continue;
   basis=standalone?"ARTICLE_DATE_ONLY":"ARTICLE_DATED_BODY";if(standalone)dateParagraph=text;
   return new DateTimeOffset(day.Value.ToDateTime(new TimeOnly(12,0)),TimeSpan.Zero);
  }
  return null;
 }
 public static List<Announcement> Parse(string id,string url,string html,DateTimeOffset now,out int candidates){
  if(html.Length>ResetRadar.MaxResponseBytes)throw new InvalidDataException("RESPONSE_TOO_LARGE");
  var articles=Matches(html,@"<article\b[^>]*>(.*?)</article>");if(articles.Count==0)throw new InvalidDataException("ARTICLE_STRUCTURE_CHANGED");
  var result=new List<Announcement>();candidates=0;
  foreach(Match article in articles.Cast<Match>().Take(80)){
   string heading=Match(article.Value,@"<h[123]\b[^>]*>(.*?)</h[123]>").Value;
   string title=Plain(heading);if(title=="")title="Codex changelog";
   var paras=Matches(article.Value,@"<p\b[^>]*>(.*?)</p>");
   if(paras.Count==0)continue;candidates++;
   var published=Publication(article.Value,out string basis,out string dateParagraph);
   // No article-wide join without an identified publication context. This preserves old dated
   // units in living FAQ pages rather than attaching a recent page update to historical content.
   var paragraphText=paras.Cast<Match>().Take(100).Select(x=>Plain(x.Groups[1].Value)).ToArray();
   bool separateDatedUnits=paragraphText.Count(x=>Match(x,@"^(?:20\d{2}-\d\d-\d\d|[A-Za-z]+\s+\d{1,2},?\s+20\d{2})\s*:").Success)>1;
   var bodies=published!=null&&!separateDatedUnits
    ?new[]{string.Join(" ",paras.Cast<Match>().Take(100).Select(x=>Plain(x.Groups[1].Value)).Where(x=>x!=dateParagraph))}
    :paras.Cast<Match>().Take(100).Select(x=>Plain(x.Groups[1].Value)).ToArray();
   string tag=article.Value[..(article.Value.IndexOf('>')+1)];
   string original=Attr(tag,"data-event-id");if(original=="")original=Attr(tag,"id");
   if(Match(original,@"^(article|content|main|root|article-content)$").Success)original="";
   string href=Attr(Match(heading,@"<a\b[^>]*>").Value,"href"),permalink=url;
   bool linked=false;
   if(href!=""&&Uri.TryCreate(new Uri(url),href,out var target)&&ResetRadar.IsAllowedPublicUri(target)){
    permalink=target.GetLeftPart(UriPartial.Path)+(target.Fragment==""?"":target.Fragment);linked=CanonicalUrl(permalink)!=CanonicalUrl(url)||target.Fragment!="";
   }
   foreach(string body in bodies){
    if(body.Length<20)continue;
    string key,confidence;
    if(original!=""){key="article:"+original;confidence="ORIGINAL_ID";}
    else if(linked){key="permalink:"+CanonicalUrl(permalink)+new Uri(permalink).Fragment;confidence="CANONICAL_PERMALINK";}
    else {key="fallback:"+Date(body)+"|"+Regex.Replace(title.ToLowerInvariant(),@"[^\p{L}\p{N}]+"," ")+"|"+Match(body,@"\b(?:Plus|Pro|Business|Enterprise)\b").Value.ToUpperInvariant();confidence="LOW_STRUCTURED_FALLBACK";}
    // Multiple undated paragraphs in a living article remain distinct bounded units.
    if((published==null||separateDatedUnits)&&bodies.Length>1)key+="|unit:"+Date(body)+"|"+Kind(body)+"|"+Match(body,@"\b(?:Plus|Pro|Business|Enterprise)\b").Value.ToUpperInvariant();
    if(bodies.Length>1)confidence="LOW_STRUCTURED_FALLBACK"; // Article ID is not an original ID for each independently dated unit.
    var candidate=Candidate(id,permalink,1,key,title,body,published,now);if(candidate==null)continue;
    candidate=candidate with{EventId=Safe.Hash(id+"|"+(original!=""?"":CanonicalUrl(permalink)+"|")+key),OriginalId=original!=""&&bodies.Length==1?original:key,IdentityConfidence=confidence,PublicationBasis=basis!=""?basis:Date(body)!=null?"BODY_EXPLICIT_DATE":"",
     SourceDate=candidate.SourceDate??published?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
     Confidence=confidence=="LOW_STRUCTURED_FALLBACK"&&candidate.Confidence=="HIGH"?"MEDIUM":candidate.Confidence};
    result.Add(candidate);
   }
  }
  if(candidates==0)throw new InvalidDataException("NO_ARTICLE_CONTENT");
  return result.DistinctBy(x=>x.EventId).ToList();
 }

}
internal sealed class ResetRadar:IDisposable {
 public static readonly (string id,string url)[] OfficialSources=[
  ("openai-help-resets","https://help.openai.com/en/articles/20001498-how-banked-codex-resets-work"),
  ("openai-changelog","https://learn.chatgpt.com/docs/changelog")];
 internal const int MaxResponseBytes=4*1024*1024;
 internal static readonly TimeSpan RequestTimeout=TimeSpan.FromSeconds(20);
 static readonly HashSet<string> AllowedHosts=new(StringComparer.OrdinalIgnoreCase){"help.openai.com","learn.chatgpt.com","developers.openai.com","openai.com","www.openai.com"};
 readonly string path;readonly object gate=new();readonly CancellationTokenSource stop=new();readonly SemaphoreSlim fetchGate=new(1,1);
 readonly HttpClient http;RadarState state;Task? loop;CancellationTokenSource? activePoll;volatile bool enabled=true;int requestCount;
 public event Action? Changed;
 public int HttpRequestCount=>Volatile.Read(ref requestCount);public string SaveError{get;private set;}="";
 public int FailedSourceCount=>Snapshot().Sources.Count(x=>x.Failures>0);
 internal Action<string,string,string>? HtmlObserved;
 public bool Enabled=>enabled;
 public ResetRadar(string root,HttpMessageHandler? handler=null){path=Path.Combine(root,"reset_radar.json");
  state=AtomicJson.Load(path,()=>new RadarState());state.Events??=[];state.Sources??=[];state.Correlations??=[];
  // Old labels are historical analysis, never evidence of an account-specific cause.
  state.Correlations=state.Correlations.Select(x=>x with{Classification=x.Classification=="GLOBAL_RESET_CONFIRMED"?"GLOBAL_RESET_PROBABLE":x.Classification,Cause="UNKNOWN",
   Correlation=x.Classification=="GLOBAL_RESET_CONFIRMED"?"INCONCLUSIVE":x.Correlation}).ToList();
  http=new HttpClient(handler??new HttpClientHandler{UseCookies=false,UseDefaultCredentials=false,AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate|DecompressionMethods.Brotli}){Timeout=Timeout.InfiniteTimeSpan};
  http.DefaultRequestHeaders.UserAgent.ParseAdd("CodexUsageMonitor/1.4");http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
 }
 public RadarState Snapshot(){lock(gate)return new(){Events=state.Events.ToList(),Sources=state.Sources.ToList(),Correlations=state.Correlations.ToList()};}
 public IReadOnlyList<Announcement> UnreadItems=>UnreadAt(DateTimeOffset.UtcNow);
 internal IReadOnlyList<Announcement> UnreadAt(DateTimeOffset now)=>Snapshot().Events.Where(x=>x.ReadAt==null&&x.Tier==1&&x.Confidence=="HIGH"&&Fresh(x,now)).OrderByDescending(x=>x.LastSeen).ThenBy(x=>x.EventId,StringComparer.Ordinal).ToArray();
 public Announcement? Unread=>UnreadItems.FirstOrDefault();
 internal static bool Fresh(Announcement x,DateTimeOffset now){
  if(x.PublishedAt>now.AddMinutes(10))return false;
  if(x.ResetUtc>=now&&x.Phase is not ("CANCELLED" or "COMPLETED"))return true;
  return x.PublishedAt!=null?now-x.PublishedAt<=TimeSpan.FromHours(48):
   DateOnly.TryParseExact(x.SourceDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)&&d>=DateOnly.FromDateTime(now.UtcDateTime).AddDays(-2)&&d<=DateOnly.FromDateTime(now.UtcDateTime).AddDays(2);
 }
 void Save(){try{AtomicJson.Save(path,state);SaveError="";}catch{SaveError="READ_STATE_NOT_SAVED";}}
 public void Read(string eventId,string? revisionId=null){
  bool changed=false;
  lock(gate){int i=state.Events.FindIndex(x=>x.EventId==eventId&&(revisionId==null||x.RevisionId==revisionId));
   if(i>=0&&state.Events[i].ReadAt==null){state.Events[i]=state.Events[i] with{ReadAt=DateTimeOffset.UtcNow};Save();changed=true;}}
  if(changed)Changed?.Invoke();
 }
 internal static bool Major(Announcement old,Announcement n)=>old.Scope!=n.Scope||old.ResetType!=n.ResetType||old.Effect!=n.Effect||old.Phase!=n.Phase||old.Eligibility!=n.Eligibility||old.Confidence!=n.Confidence||
  old.TimeQuality!=n.TimeQuality||(old.MajorTime==null&&n.ResetUtc!=null)||(old.MajorTime!=null&&n.ResetUtc==null)||(old.MajorTime!=null&&n.ResetUtc!=null&&Math.Abs((old.MajorTime.Value-n.ResetUtc.Value).TotalMinutes)>=30);
 public void Apply(IEnumerable<Announcement> entries,DateTimeOffset now){
  lock(gate){foreach(var n in entries){
    int i=state.Events.FindIndex(x=>x.EventId==n.EventId);

    // Modern identities may match their canonical language variant, never another original event.
    if(i<0&&n.OriginalId!="")i=state.Events.FindIndex(x=>x.Source==n.Source&&x.OriginalId==n.OriginalId&&(n.IdentityConfidence=="ORIGINAL_ID"&&x.IdentityConfidence=="ORIGINAL_ID"||AnnouncementParser.CanonicalUrl(x.Url)==AnnouncementParser.CanonicalUrl(n.Url)));
    var legacy=state.Events.Where(x=>x.Source==n.Source&&x.OriginalId==""&&x.SourceDate!=null&&x.SourceDate==n.SourceDate&&(x.Scope==n.Scope||x.Scope=="UNKNOWN")).ToArray();
    if(i<0){
     var exact=legacy.Where(x=>AnnouncementParser.CanonicalUrl(x.Url)==AnnouncementParser.CanonicalUrl(n.Url)).ToArray();
     if(exact.Length==1)i=state.Events.IndexOf(exact[0]);
    }
    if(i<0){
     // A legacy list-page key is insufficient to collapse a newly identified article.
     // Carry a known read stamp conservatively, while retaining the old record and each new ID.
     var inherited=legacy.Where(x=>x.ReadAt!=null&&x.IdentityConfidence=="LOW_STRUCTURED_FALLBACK").Select(x=>x.ReadAt).Max();
     state.Events.Add(n with{MajorTime=n.ResetUtc,ReadAt=inherited??n.ReadAt,Migration=inherited!=null?"LEGACY_LIST_READ_INHERITED":n.Migration});continue;
    }
    var old=state.Events[i];bool legacyMatch=old.OriginalId=="";bool major=!legacyMatch&&Major(old,n);
    state.Events[i]=n with{EventId=old.EventId,FirstSeen=old.FirstSeen,LastSeen=now,ReadAt=major?null:old.ReadAt,RevisionId=major?Safe.Hash(old.RevisionId+"|"+n.RevisionId):old.RevisionId,MajorTime=major?n.ResetUtc:old.MajorTime,
     Migration=legacyMatch?"LEGACY_CANONICAL_READ_PRESERVED":old.Migration};}

   state.Events=state.Events.OrderByDescending(x=>x.LastSeen).Take(200).ToList();Save();}Changed?.Invoke();
 }
 public void SetEnabled(bool value){
  CancellationTokenSource? cancel=null;
  lock(gate){enabled=value;if(!value)cancel=activePoll;}
  try{cancel?.Cancel();}catch(ObjectDisposedException){}Changed?.Invoke();
 }
 public void Start(){if(loop!=null)return;loop=Task.Run(async()=>{while(!stop.IsCancellationRequested){
  try{await Poll(false,stop.Token);}catch(OperationCanceledException) when(stop.IsCancellationRequested){break;}catch{}
  try{await Task.Delay(TimeSpan.FromSeconds(60),stop.Token);}catch(OperationCanceledException){break;}}});}
 internal static int BackoffMinutes(int failures)=>failures switch{<=1=>15,2=>30,3=>60,_=>180};
 internal static bool IsAllowedPublicUri(Uri? uri)=>uri is {IsAbsoluteUri:true}&&uri.Scheme==Uri.UriSchemeHttps&&uri.Port==443&&uri.UserInfo==""&&!uri.IsLoopback&&!IPAddress.TryParse(uri.DnsSafeHost,out _)&&AllowedHosts.Contains(uri.DnsSafeHost);
 internal static DateTimeOffset? RetryAfter(HttpResponseMessage response,DateTimeOffset now){
  var retry=response.Headers.RetryAfter;if(retry?.Delta is TimeSpan delta&&delta>TimeSpan.Zero)return now+delta;
  return retry?.Date>now?retry.Date:null;
 }
 async Task<HttpResponseMessage> Get(SourceStatus old,CancellationToken ct){
  var uri=new Uri(old.Url);
  for(int redirects=0;;redirects++){
   if(!IsAllowedPublicUri(uri))throw new InvalidDataException("REDIRECT_NOT_ALLOWED");
   using var request=new HttpRequestMessage(HttpMethod.Get,uri);
   if(old.ETag!=null&&EntityTagHeaderValue.TryParse(old.ETag,out var etag))request.Headers.IfNoneMatch.Add(etag);
   if(old.Modified!=null)request.Headers.IfModifiedSince=old.Modified;
   Interlocked.Increment(ref requestCount);
   var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);int code=(int)response.StatusCode;
   if(code is not (301 or 302 or 303 or 307 or 308))return response;
   var target=response.Headers.Location;response.Dispose();
   if(redirects>=3)throw new InvalidDataException("REDIRECT_LIMIT");
   if(target==null)throw new InvalidDataException("REDIRECT_MISSING_LOCATION");
   uri=target.IsAbsoluteUri?target:new Uri(uri,target);
  }
 }
 public async Task Poll(bool force,CancellationToken ct){
  if(!Enabled||!await fetchGate.WaitAsync(0,ct))return;
  using var poll=CancellationTokenSource.CreateLinkedTokenSource(ct,stop.Token);
  try{
   lock(gate){if(!enabled)return;activePoll=poll;}
   // There are exactly two configured adapters; one in-flight pass prevents overlap.
   await Task.WhenAll(OfficialSources.Select(source=>Fetch(source.id,source.url,force,poll.Token)));
  }catch(OperationCanceledException) when(!ct.IsCancellationRequested&&!stop.IsCancellationRequested){}
  finally{lock(gate){if(activePoll==poll)activePoll=null;}fetchGate.Release();}
 }
 async Task Fetch(string id,string url,bool force,CancellationToken pollToken){
  var old=Snapshot().Sources.FirstOrDefault(x=>x.Id==id)??new(id,url);
  var now=DateTimeOffset.UtcNow;if(old.NextCheck>now&&(!force||old.HttpStatus==429))return;
  var next=old with{LastAttempt=now,Url=url,RetryAfterUtc=null};
  try{
   using var deadline=CancellationTokenSource.CreateLinkedTokenSource(pollToken);deadline.CancelAfter(RequestTimeout);var requestToken=deadline.Token;
   using var response=await Get(next,requestToken);int status=(int)response.StatusCode;
   next=next with{HttpStatus=status,FinalUrl=response.RequestMessage?.RequestUri?.GetLeftPart(UriPartial.Path)??url};
   if(status==304){
    if(old.LastSuccess==null)throw new InvalidDataException("UNEXPECTED_304");
    next=next with{Status="UNCHANGED",Failures=0,LastSuccess=now,NextCheck=now.AddSeconds(900+Random.Shared.Next(91)),Error=""};
   }else if(status==429){
    var retry=RetryAfter(response,now);next=Failed(next,"HTTP_429",429,now) with{RetryAfterUtc=retry};
    if(retry>next.NextCheck)next=next with{NextCheck=retry};
   }else{
    if(!response.IsSuccessStatusCode)throw new HttpRequestException("HTTP_"+status,null,response.StatusCode);
    if(response.Content.Headers.ContentLength>MaxResponseBytes)throw new InvalidDataException("RESPONSE_TOO_LARGE");
    await using var stream=await response.Content.ReadAsStreamAsync(requestToken);using var buffer=new MemoryStream();var bytes=new byte[16384];int read;
    while((read=await stream.ReadAsync(bytes,requestToken))>0){if(buffer.Length+read>MaxResponseBytes)throw new InvalidDataException("RESPONSE_TOO_LARGE");buffer.Write(bytes,0,read);}
    byte[] data=buffer.ToArray();string html=Encoding.UTF8.GetString(data);
    try{HtmlObserved?.Invoke(id,url,html);}catch{} // Read-only owned QA observer, absent in normal operation.
    var entries=AnnouncementParser.Parse(id,url,html,now,out int count);Apply(entries,now);
    next=next with{Status="OK",Failures=0,LastSuccess=now,NextCheck=now.AddSeconds(900+Random.Shared.Next(91)),CandidateCount=count,
     ETag=response.Headers.ETag?.ToString(),Modified=response.Content.Headers.LastModified,
     BodyHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant(),ResponseBytes=data.Length,Error="",
     CandidateExcerpt=AnnouncementParser.Clean(AnnouncementParser.Plain(AnnouncementParser.Match(html,@"<article\b[^>]*>(.*?)</article>").Groups[1].Value),240)};
   }
  }catch(OperationCanceledException) when(!pollToken.IsCancellationRequested){next=Failed(next,"TIMEOUT",0,now);}
   catch(HttpRequestException ex){next=Failed(next,ex.StatusCode.HasValue?"HTTP_"+(int)ex.StatusCode.Value:"NETWORK_ERROR",(int?)ex.StatusCode??0,now);}
   catch(InvalidDataException ex){next=Failed(next,ex.Message,next.HttpStatus,now);}
   catch(RegexMatchTimeoutException){next=Failed(next,"PARSER_TIMEOUT",next.HttpStatus,now);}
   catch(OperationCanceledException){throw;}
   catch{next=Failed(next,"PARSER_ERROR",0,now);}
  lock(gate){state.Sources.RemoveAll(x=>x.Id==id);state.Sources.Add(next);Save();}Changed?.Invoke();
 }
 static SourceStatus Failed(SourceStatus s,string code,int http,DateTimeOffset now)=>s with{Status="DEGRADED",Failures=s.Failures+1,HttpStatus=http,Error=code,NextCheck=now.AddMinutes(BackoffMinutes(s.Failures+1))};
 internal static bool ScopeMatches(Announcement a,string? plan)=>a.Eligibility=="AS_ANNOUNCED"&&!string.IsNullOrWhiteSpace(plan)&&new[]{"free","plus","pro","team","business","enterprise","edu"}.Contains(plan.ToLowerInvariant())&&
  (a.Scope=="ALL"||(a.Scope=="PAID"&&new[]{"plus","pro","business","enterprise"}.Contains(plan.ToLowerInvariant()))||a.Scope.Split('|').Contains(plan.ToUpperInvariant()));
 public void Correlate(IEnumerable<UsageSample> input){
  var rows=input.Where(x=>x.limit_id=="codex"&&x.window_duration_mins==10080).OrderBy(x=>x.observed_at_utc).ToArray();var announcements=Snapshot().Events;var list=new List<ResetCorrelation>();
  for(int i=1;i<rows.Length;i++){
   var p=rows[i-1];var c=rows[i];if(!DailyUsageCalculator.Valid(p)||!DailyUsageCalculator.Valid(c)||!(c.remaining_percent>p.remaining_percent||p.reset_at_unix_seconds!=c.reset_at_unix_seconds))continue;
   double seconds=(c.observed_at_utc-p.observed_at_utc).TotalSeconds;
   bool stable=p.context_assurance=="STABLE_SCOPE"&&c.context_assurance=="STABLE_SCOPE"&&p.account_context_key==c.account_context_key&&p.comparison_key==c.comparison_key&&p.plan_type==c.plan_type&&p.source_slot==c.source_slot&&!c.had_gap&&seconds>0&&seconds<=Math.Max(3*c.polling_interval_seconds,300);
   bool scheduled=p.reset_at_utc.HasValue&&p.reset_at_utc>=p.observed_at_utc&&p.reset_at_utc<=c.observed_at_utc;
   var a=announcements.Where(x=>x.Effect=="AUTOMATIC_QUOTA_RESET"&&x.Phase!="CANCELLED"&&x.ResetType=="GLOBAL"&&x.Tier==1&&x.Confidence=="HIGH"&&x.ResetUtc!=null&&Math.Abs((x.ResetUtc.Value-c.observed_at_utc).TotalHours)<=2).OrderByDescending(x=>x.LastSeen).FirstOrDefault();
   bool banked=c.reset_credits_available_count<p.reset_credits_available_count;
   bool scopeMatches=a!=null&&ScopeMatches(a,c.plan_type);
   bool refill=c.remaining_percent>p.remaining_percent;
   bool probable=stable&&refill&&a!=null&&scopeMatches&&!scheduled&&!banked&&a.Phase!="CANCELLED";
   string classification=stable&&scheduled?"SCHEDULED_WEEKLY_RESET":stable&&banked?"BANKED_RESET_PROBABLE":probable?"GLOBAL_RESET_PROBABLE":a!=null?"INCONCLUSIVE":"UNCLASSIFIED_REPLENISHMENT";
   string reason=!stable?"Identity or observation scope insufficient":scheduled?"Scheduled time coincided; this is supporting evidence only":
    banked?"Reset credit count declined; this is supporting evidence only":a==null?"No matching announcement evidence":
    !scopeMatches?"Announcement eligibility or plan not verified":!refill?"No numerical refill observed":"Official announcement and local refill occurred around the same time; cause remains unknown";
   list.Add(new(p.sample_id,c.sample_id,p.observed_at_utc,c.observed_at_utc,p.remaining_percent,c.remaining_percent,classification,reason,a?.EventId,stable,scheduled,
    a==null?"UNVERIFIED":a.Tier==1?"OFFICIAL_ANNOUNCED":a.Tier==2?"TEAM_REPORTED":"UNVERIFIED",
    !stable?"NOT_COMPARABLE":refill?"REPLENISHMENT_OBSERVED":"NO_OBSERVATION",
    probable?"PROBABLE":a!=null?"INCONCLUSIVE":"NOT_ASSESSED","UNKNOWN"));
  }
  lock(gate){state.Correlations=list.TakeLast(500).ToList();Save();}
 }
 public static string Suggest(Announcement a,decimal? weekly,int? banked,bool localObserved,DateTimeOffset now,string? currentPlan=null,string? language=null){
  return L.TFor(language??L.Language,"Radar."+SuggestionKey(a,weekly,banked,localObserved,now,currentPlan));
 }
 internal static string SuggestionKey(Announcement a,decimal? weekly,int? banked,bool localObserved,DateTimeOffset now,string? currentPlan){
  if(a.Phase=="CANCELLED")return "SuggestCancelled";
  if(a.Effect=="BANKED_CREDIT_GRANT")return a.Tier==1&&a.Confidence=="HIGH"?"SuggestCreditGrant":"SuggestUnknown";
  if(a.Effect=="MECHANISM_OR_PURCHASE")return "SuggestMechanism";
  if(a.Effect!="AUTOMATIC_QUOTA_RESET")return "SuggestUnknown";
  if(a.ResetUtc!=null&&now>a.ResetUtc&&!localObserved)return "SuggestPassed";
  if(a.Tier!=1||a.Confidence!="HIGH"||a.ResetUtc==null||!ScopeMatches(a,currentPlan)||!Fresh(a,now))return "SuggestUnknown";
  if(a.Phase=="COMPLETED"||now>a.ResetUtc)return localObserved?"SuggestObserved":"SuggestCompleted";
  if(weekly is >20 and <=100)return "SuggestPlannedWork";
  if(weekly is >=0 and <=5&&banked>0)return "SuggestKeepReset";
  return "SuggestNeutral";
 }
 public void Dispose(){stop.Cancel();try{loop?.Wait(500);}catch{}http.Dispose();stop.Dispose();}
}
internal sealed class ResetInfoForm:ProductForm {
 readonly MonitorService service;Announcement? current;bool shown;bool readQueued;
 readonly Label source=Theme.Label("",24,9,Theme.Muted),timeCaption=Theme.Label("",24,10,Theme.Muted),
  time=Theme.Label("",48,14),weeklyCaption=Theme.Label("",22,10,Theme.Muted),amount=Theme.Label("",52,30),
  suggestion=Theme.Label("",76,10.5f),credits=Theme.Label("",32,9,Theme.Muted),evidence=Theme.Label("",68,10,Theme.Muted),
  coverage=Theme.Label("",60,9,Theme.Muted);
 readonly Button nextButton,sourceButton,closeButton;
 public ResetInfoForm(MonitorService s,Announcement? a):base(L.T("Radar.CardTitle"),500,620){
  service=s;current=a;KeyPreview=true;
  Stack(source,timeCaption,time,weeklyCaption,amount,suggestion,credits,evidence,coverage);
  closeButton=Theme.Button("",(_,_)=>Close());closeButton.Width=82;
  nextButton=Theme.Button("",(_,_)=>ShowNext());nextButton.Width=92;
  sourceButton=Theme.Button("",(_,_)=>{if(current!=null&&Uri.TryCreate(current.Url,UriKind.Absolute,out var uri)&&ResetRadar.IsAllowedPublicUri(uri))OverlayForm.OpenUrl(uri.AbsoluteUri);});sourceButton.Width=154;
  Footer.Controls.Add(closeButton);Footer.Controls.Add(nextButton);Footer.Controls.Add(sourceButton);
  ApplyTexts();L.Watch(this,ApplyTexts);
  service.Radar.Changed+=OnDataChanged;service.Changed+=OnDataChanged;
  FormClosed+=(_,_)=>{service.Radar.Changed-=OnDataChanged;service.Changed-=OnDataChanged;};
  KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape){e.Handled=true;Close();}};
  Shown+=(_,_)=>{shown=true;QueueReadAfterRender();};
 }
 internal string? DisplayedEventId=>current?.EventId;
 void OnDataChanged(){if(!IsDisposed&&IsHandleCreated)try{BeginInvoke((Action)ApplyTexts);}catch(InvalidOperationException){}}
 void ShowNext(){
  var entry=service.Radar.UnreadItems.FirstOrDefault(x=>current==null||x.EventId!=current.EventId||x.RevisionId!=current.RevisionId);
  if(entry==null)return;current=entry;Body.AutoScrollPosition=Point.Empty;ApplyTexts();QueueReadAfterRender();
 }
 void QueueReadAfterRender(){
  if(!shown||readQueued||current==null||!Visible)return;readQueued=true;
  var displayed=current;
  BeginInvoke((Action)(()=>{
   readQueued=false;if(IsDisposed||!Visible||current?.EventId!=displayed.EventId||current.RevisionId!=displayed.RevisionId)return;
   // A failed construction/layout/paint never consumes unread. Reading a revision twice is idempotent.
   try{Body.PerformLayout();Body.Refresh();Update();service.Radar.Read(displayed.EventId,displayed.RevisionId);UpdateNavigation();}catch{}
  }));
 }
 void UpdateNavigation(){
  nextButton.Enabled=service.Radar.UnreadItems.Any(x=>current==null||x.EventId!=current.EventId||x.RevisionId!=current.RevisionId);
  sourceButton.Enabled=current!=null&&Uri.TryCreate(current.Url,UriKind.Absolute,out var uri)&&ResetRadar.IsAllowedPublicUri(uri);
 }
 void ApplyTexts(){
  if(IsDisposed)return;Text=L.T("Radar.CardTitle");AccessibleName=Text;
  closeButton.Text=L.T("Radar.Close");nextButton.Text=L.T("Radar.Next");sourceButton.Text=L.T("Radar.OpenSource");
  foreach(var button in new[]{closeButton,nextButton,sourceButton})button.AccessibleName=button.Text;
  var state=service.Radar.Snapshot();bool isLimited=state.Sources.Count<ResetRadar.OfficialSources.Length||state.Sources.Any(x=>x.Failures>0||x.LastSuccess==null);
  coverage.Text=L.T(!service.Radar.Enabled?"Radar.CoverageDisabled":isLimited?"Radar.CoverageLimited":"Radar.CoverageAvailable");
  var a=current;
  foreach(var c in new Control[]{timeCaption,time,weeklyCaption,amount,suggestion,credits,evidence})c.Visible=a!=null;
  if(a==null){source.Text=L.T("Radar.NoNotices");source.Height=(int)(44*DeviceDpi/96f);source.Tag=44;}
  else{
   source.Tag=24;source.Height=(int)(24*DeviceDpi/96f);source.Text=L.T(a.Tier==1?"Radar.Official":a.Tier==2?"Radar.TeamReported":"Radar.Unverified");
   timeCaption.Text=L.T(a.Effect=="BANKED_CREDIT_GRANT"?"Radar.CreditGrantTime":a.Effect=="AUTOMATIC_QUOTA_RESET"?"Radar.LocalTime":"Radar.NoticeTime");time.Text=AnnouncementParser.LocalTime(a,DateTimeOffset.UtcNow);
   weeklyCaption.Text=L.T("Radar.WeeklyRemaining");var value=service.MainClock.Value?.Remaining;
   amount.Text=value==null?"—":value==0?"0%":value<1?"<1%":value.Value.ToString("0.#",CultureInfo.InvariantCulture)+"%";amount.ForeColor=Theme.QuotaColor(value);
   var correlation=state.Correlations.LastOrDefault(x=>x.AnnouncementId==a.EventId);
   bool observed=correlation?.LocalObservation=="REPLENISHMENT_OBSERVED";
   string? plan=service.Current?.Windows.FirstOrDefault(w=>w.LimitId=="codex"&&w.Duration==10080)?.Plan;
   suggestion.Text=ResetRadar.Suggest(a,value,service.Current?.ResetCredits,observed,DateTimeOffset.UtcNow,plan);
   credits.Text=L.T("Radar.ResetCount",service.Current?.ResetCredits?.ToString(CultureInfo.InvariantCulture)??L.T("Radar.NotProvided"));
   evidence.Text=L.T(correlation?.Correlation=="PROBABLE"?"Radar.EvidenceProbable":correlation?.LocalObservation=="NOT_COMPARABLE"?"Radar.EvidenceNotComparable":"Radar.EvidenceNoObservation");
  }
  foreach(var label in new[]{source,timeCaption,time,weeklyCaption,amount,suggestion,credits,evidence,coverage})label.AccessibleName=label.Text;
  UpdateNavigation();Body.PerformLayout();Invalidate(true);
 }
}
