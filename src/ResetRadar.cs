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
 DateTimeOffset? ReadAt=null,DateTimeOffset? MajorTime=null,string Eligibility="UNKNOWN",string IdentityConfidence="LOW_STRUCTURED_FALLBACK",string Effect="UNKNOWN",string OriginalId="",string PublicationBasis="",string Migration="",
 string SourceClass="OFFICIAL_OPENAI",string Author="Unknown",string OriginalSourceUrl="",string RelayUrl="",string CanonicalOriginalId="",string OriginalQuote="",string RelayForecast="",string SignalLevel="NONE",string SignalReason="",List<RadarEvidenceSource>? EvidenceSources=null);
internal sealed record RadarEvidenceSource(string SourceId,string SourceClass,string Url,string Role,string BodyHash="");
internal sealed record RadarSourceDefinition(string Id,string Url,string DisplayName,string SourceClass,string Adapter,int Tier,int IntervalMinutes);
internal sealed record SourceStatus(string Id,string Url,string Status="NOT_CHECKED",DateTimeOffset? LastAttempt=null,
 DateTimeOffset? LastSuccess=null,DateTimeOffset? NextCheck=null,int Failures=0,int HttpStatus=0,int CandidateCount=0,
 string? ETag=null,DateTimeOffset? Modified=null,string BodyHash="",long ResponseBytes=0,string Error="",string CandidateExcerpt="",string FinalUrl="",DateTimeOffset? RetryAfterUtc=null,
 string SourceClass="OFFICIAL_OPENAI",string DisplayName="",string Adapter="HTML");
internal sealed record RadarSourceCheck(DateTimeOffset Timestamp,string SourceId,string SourceClass,string Status,int CandidateCount,int HttpStatus,string BodyHash,DateTimeOffset? LastSuccess,DateTimeOffset? NextCheck,string Error);
internal sealed record RadarTransition(string EventId,string RevisionId,DateTimeOffset Timestamp,string FromSignal,string ToSignal,string Reason);
internal sealed record RadarQuotaEvidence(string EventId,DateTimeOffset CapturedAt,string PreviousSample,string CurrentSample,DateTimeOffset From,DateTimeOffset To,decimal? WeeklyBefore,decimal? WeeklyAfter,DateTimeOffset? ResetAtBefore,DateTimeOffset? ResetAtAfter,int? BankedBefore,int? BankedAfter,double ObservationGapSeconds,string ScopeAssurance,string Cause="UNKNOWN");
internal sealed record RadarSignalEvidence(DateTimeOffset Timestamp,string EventId,string RevisionId,string SignalLevel,string Phase,string Effect,string Scope,DateTimeOffset? ResetUtc,string TimeQuality,DateTimeOffset? PublishedAt,DateTimeOffset? ReadAt,string SourceId,string SourceClass,string OriginalSourceUrl,string RelayUrl,string OriginalQuote,string ContentHash,string SignalReason,RadarCoveragePoint? CoverageAtObservation=null);
internal sealed record ResetCorrelation(string PreviousSample,string CurrentSample,DateTimeOffset From,DateTimeOffset To,
 decimal? Before,decimal? After,string Classification,string Reason,string? AnnouncementId,bool ScopeVerified,bool Scheduled,
 string AnnouncementStatus="UNVERIFIED",string LocalObservation="NOT_COMPARABLE",string Correlation="NOT_ASSESSED",string Cause="UNKNOWN");
internal sealed class RadarState {
 public List<string> CurrentLocalScopes{get;set;}=[];public string LocalCycleDiagnostic{get;set;}="";
 public List<LocalQuotaCycleEvent> LocalCycleEvents{get;set;}=[];public List<RadarAttentionDisposition> AttentionDispositions{get;set;}=[];
 public List<RadarCoveragePoint> CoverageTimeline{get;set;}=[];public List<Announcement> Events{get;set;}=[];public List<SourceStatus> Sources{get;set;}=[];public List<ResetCorrelation> Correlations{get;set;}=[];
 public List<RadarSourceCheck> SourceChecks{get;set;}=[];public List<RadarTransition> Transitions{get;set;}=[];public List<RadarQuotaEvidence> QuotaEvidence{get;set;}=[];public List<RadarSignalEvidence> SignalHistory{get;set;}=[];
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
 internal LocalQuotaCycleStore? LocalCycles;
 internal Func<List<string>>? VerifiedLocalScopes;
 public static readonly RadarSourceDefinition[] Sources=[
  new("codex-changelog-rss","https://developers.openai.com/codex/changelog/rss.xml","Codex Changelog","OFFICIAL_OPENAI","RSS",1,15),
  new("openai-release-notes-rss","https://openai.com/products/release-notes/","OpenAI Release Notes","OFFICIAL_OPENAI","LEGACY_HTML",1,15),
  new("openai-status-rss","https://status.openai.com/feed.rss","OpenAI Status","OPENAI_STATUS","RSS",1,12),
  new("openai-help-resets","https://help.openai.com/en/articles/20001498-how-banked-codex-resets-work","OpenAI Help Center","OFFICIAL_OPENAI","LEGACY_HTML",1,15),
  new("team-modelyard-rss","https://tibo.modelyard.dev/feed.xml","ModelYard team signal relay","TEAM_SIGNAL_RELAY","RSS",2,12),
  new("team-codex-reset","https://codex-reset.com/tibo","Codex Reset team signal relay","TEAM_SIGNAL_RELAY","TEAM_HTML",2,12)];
 // Retained as a source-enumeration compatibility surface for the existing rc.3 harnesses.
 public static readonly (string id,string url)[] OfficialSources=Sources.Select(x=>(x.Id,x.Url)).ToArray();
 internal const int MaxResponseBytes=4*1024*1024;
 internal static readonly TimeSpan RequestTimeout=TimeSpan.FromSeconds(20);
 static readonly HashSet<string> AllowedHosts=new(StringComparer.OrdinalIgnoreCase){"help.openai.com","learn.chatgpt.com","developers.openai.com","openai.com","www.openai.com","status.openai.com","tibo.modelyard.dev","codex-reset.com","www.codex-reset.com"};
 readonly string path;readonly object gate=new();readonly CancellationTokenSource stop=new();readonly SemaphoreSlim fetchGate=new(1,1);
 readonly HttpClient http;RadarState state;Task? loop;CancellationTokenSource? activePoll;volatile bool enabled=true;int requestCount;
 public event Action? Changed;
 public int HttpRequestCount=>Volatile.Read(ref requestCount);public string SaveError{get;private set;}="";
 public int FailedSourceCount=>Rc4Radar.FailedConfiguredCount(Snapshot());
 internal Action<string,string,string>? HtmlObserved;
 public bool Enabled=>enabled;
 public ResetRadar(string root,HttpMessageHandler? handler=null){path=Path.Combine(root,"reset_radar.json");
  state=AtomicJson.Load(path,()=>new RadarState());state.Events??=[];state.Sources??=[];state.Correlations??=[];state.SourceChecks??=[];state.Transitions??=[];state.QuotaEvidence??=[];state.SignalHistory??=[];state.CoverageTimeline??=[];
  // Old labels are historical analysis, never evidence of an account-specific cause.
  state.Correlations=state.Correlations.Select(x=>x with{Classification=x.Classification=="GLOBAL_RESET_CONFIRMED"?"GLOBAL_RESET_PROBABLE":x.Classification,Cause="UNKNOWN",
   Correlation=x.Classification=="GLOBAL_RESET_CONFIRMED"?"INCONCLUSIVE":x.Correlation}).ToList();
  http=new HttpClient(handler??new HttpClientHandler{UseCookies=false,UseDefaultCredentials=false,AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate|DecompressionMethods.Brotli}){Timeout=Timeout.InfiniteTimeSpan};
  http.DefaultRequestHeaders.UserAgent.ParseAdd("CodexUsageMonitor/0.4.0");http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/rss+xml"));http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml",0.9));http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html",0.8));
 }
 public RadarState Snapshot(){var cycles=LocalCycles?.Snapshot();lock(gate)return new(){CurrentLocalScopes=VerifiedLocalScopes?.Invoke()??[],LocalCycleDiagnostic=LocalCycles?.Diagnostic??"",LocalCycleEvents=cycles?.events??[],AttentionDispositions=cycles?.dispositions??[],Events=state.Events.Select(x=>x with{EvidenceSources=x.EvidenceSources?.ToList()}).ToList(),Sources=state.Sources.ToList(),Correlations=state.Correlations.ToList(),SourceChecks=state.SourceChecks.ToList(),Transitions=state.Transitions.ToList(),QuotaEvidence=state.QuotaEvidence.ToList(),SignalHistory=state.SignalHistory.ToList(),CoverageTimeline=state.CoverageTimeline.ToList()};}
 public IReadOnlyList<Announcement> ActiveSignalItems=>ActiveSignalsAt(DateTimeOffset.UtcNow);
 public Announcement? ActiveSignal=>ActiveSignalItems.FirstOrDefault();
 public string ActiveSignalLevel=>ActiveSignal?.SignalLevel??"NONE";
 internal IReadOnlyList<Announcement> ActiveSignalsAt(DateTimeOffset now){var snapshot=Snapshot();return snapshot.Events.Where(x=>ActiveSignalAt(x,snapshot,now)).OrderByDescending(x=>Rc4Radar.SignalPriority(x.SignalLevel)).ThenBy(x=>x.ReadAt!=null).ThenByDescending(x=>x.LastSeen).ThenBy(x=>x.EventId,StringComparer.Ordinal).ToArray();}
 public IReadOnlyList<Announcement> UnreadItems=>UnreadAt(DateTimeOffset.UtcNow);
 internal IReadOnlyList<Announcement> UnreadAt(DateTimeOffset now){var snapshot=Snapshot();return snapshot.Events.Where(x=>x.ReadAt==null&&Rc4Radar.IsAlert(x)&&Fresh(x,now)&&!LocallySatisfied(x,snapshot,now)).OrderByDescending(x=>Rc4Radar.SignalPriority(x.SignalLevel)).ThenByDescending(x=>x.LastSeen).ThenBy(x=>x.EventId,StringComparer.Ordinal).ToArray();}
 public Announcement? Unread=>UnreadItems.FirstOrDefault();
 public string AlertLevel=>Unread?.SignalLevel??"NONE";
 internal static bool Fresh(Announcement x,DateTimeOffset now){
  if(x.PublishedAt>now.AddMinutes(10))return false;
  if(x.ResetUtc>=now&&x.Phase is not ("CANCELLED" or "COMPLETED"))return true;
  return x.PublishedAt!=null?now-x.PublishedAt<=TimeSpan.FromHours(48):
   DateOnly.TryParseExact(x.SourceDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)&&d>=DateOnly.FromDateTime(now.UtcDateTime).AddDays(-2)&&d<=DateOnly.FromDateTime(now.UtcDateTime).AddDays(2);
 }
 internal static bool ActiveSignalAt(Announcement x,RadarState snapshot,DateTimeOffset now){
  if(LocallySatisfied(x,snapshot,now))return false;
  if(x.SignalLevel is not ("WATCH" or "INCOMING")||x.Phase is "CANCELLED" or "COMPLETED")return false;
  DateTimeOffset anchor=x.PublishedAt??x.FirstSeen;
  foreach(var value in snapshot.Transitions.Where(t=>t.EventId==x.EventId&&t.RevisionId==x.RevisionId).Select(t=>t.Timestamp)
   .Concat(snapshot.SignalHistory.Where(h=>h.EventId==x.EventId&&h.RevisionId==x.RevisionId).Select(h=>h.Timestamp)))if(value>anchor)anchor=value;
  if(anchor>now.AddMinutes(10))return false;
  TimeSpan relevance=x.SourceClass=="TEAM_SIGNAL_RELAY"?TimeSpan.FromHours(72):TimeSpan.FromHours(48);
  if(x.SignalLevel=="WATCH")return now-anchor<=relevance;
  if(x.ResetUtc==null)return now-anchor<=relevance;
  if(x.ResetUtc>=now)return true;
  var definition=Sources.FirstOrDefault(d=>d.Id==x.Source);var reassess=TimeSpan.FromMinutes(Math.Max(30,2*(definition?.IntervalMinutes??15)));
  if(now<=x.ResetUtc.Value+reassess)return true;
  var source=snapshot.Sources.Where(s=>s.Id==x.Source).OrderByDescending(s=>s.LastAttempt??DateTimeOffset.MinValue).FirstOrDefault();
  if(source?.LastSuccess>=x.ResetUtc)return false;
  return now-x.ResetUtc.Value<=TimeSpan.FromHours(6);
 }
 void Save(){try{AtomicJson.Save(path,state);SaveError="";}catch{SaveError="READ_STATE_NOT_SAVED";}}
 internal static bool LocallySatisfied(Announcement x,RadarState snapshot,DateTimeOffset now)=>snapshot.AttentionDispositions.Any(d=>d.event_id==x.EventId&&d.revision_id==x.RevisionId&&d.timestamp<=now&&snapshot.CurrentLocalScopes.Contains(d.scope_key)&&d.status=="SATISFIED_BY_LOCAL_CYCLE"&&snapshot.LocalCycleEvents.Any(e=>e.id==d.local_cycle_event_id&&e.Confirmed));
 public void Read(string eventId,string? revisionId=null){
   bool changed=false;
   lock(gate){int i=state.Events.FindIndex(x=>x.EventId==eventId&&(revisionId==null||x.RevisionId==revisionId));
    if(i>=0&&state.Events[i].ReadAt==null){var readAt=DateTimeOffset.UtcNow;state.Events[i]=state.Events[i] with{ReadAt=readAt};state.SignalHistory=state.SignalHistory.Select(x=>x.EventId==eventId&&(revisionId==null||x.RevisionId==revisionId)?x with{ReadAt=readAt}:x).ToList();Save();changed=true;}}
  if(changed)Changed?.Invoke();
 }
 internal static bool Major(Announcement old,Announcement n)=>old.Scope!=n.Scope||old.ResetType!=n.ResetType||old.Effect!=n.Effect||old.Phase!=n.Phase||old.Eligibility!=n.Eligibility||old.Confidence!=n.Confidence||old.SignalLevel!=n.SignalLevel||
  old.TimeQuality!=n.TimeQuality||(old.MajorTime==null&&n.ResetUtc!=null)||(old.MajorTime!=null&&n.ResetUtc==null)||(old.MajorTime!=null&&n.ResetUtc!=null&&Math.Abs((old.MajorTime.Value-n.ResetUtc.Value).TotalMinutes)>=30);
 RadarSignalEvidence SignalEvidence(Announcement n,string eventId,string revision,DateTimeOffset timestamp)=>new(timestamp,eventId,revision,n.SignalLevel,n.Phase,n.Effect,n.Scope,n.ResetUtc,n.TimeQuality,n.PublishedAt,n.ReadAt,n.Source,n.SourceClass,n.OriginalSourceUrl,n.RelayUrl,n.OriginalQuote,n.ContentHash,n.SignalReason,RadarCoverage.Measure(state,enabled,timestamp));
 static bool DemonstrablyNewerTerminal(Announcement old,Announcement n)=>n.Phase is "CANCELLED" or "COMPLETED"&&old.Phase!=n.Phase&&
  (n.PublishedAt>old.PublishedAt||n.SourceClass=="OFFICIAL_OPENAI"&&n.Source==old.Source&&n.ContentHash!=old.ContentHash);
 static bool WeakerThan(Announcement old,Announcement n)=>Rc4Radar.SignalPriority(n.SignalLevel)<Rc4Radar.SignalPriority(old.SignalLevel)||
  Rc4Radar.SignalPriority(n.SignalLevel)==Rc4Radar.SignalPriority(old.SignalLevel)&&(old.ResetUtc!=null&&n.ResetUtc==null||old.Effect!="UNKNOWN"&&n.Effect=="UNKNOWN"||old.Scope!="UNKNOWN"&&n.Scope=="UNKNOWN"||old.Confidence=="HIGH"&&n.Confidence!="HIGH");
 public void Apply(IEnumerable<Announcement> entries,DateTimeOffset now){
  lock(gate){foreach(var incoming in entries){
    // Classify legacy/parser fixtures at the product boundary as well as adapter output.
    // This keeps stored rc.3 events and focused production-path tests on one policy.
    var n=incoming.SignalLevel=="NONE"?Rc4Radar.Classify(incoming):incoming;
    int i=state.Events.FindIndex(x=>x.EventId==n.EventId);

    // The original post identity is authoritative across independent community relays.
    if(i<0&&n.CanonicalOriginalId!="")i=state.Events.FindIndex(x=>x.CanonicalOriginalId==n.CanonicalOriginalId);

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
      var added=n with{MajorTime=n.ResetUtc,ReadAt=inherited??n.ReadAt,Migration=inherited!=null?"LEGACY_LIST_READ_INHERITED":n.Migration,EvidenceSources=n.EvidenceSources?.DistinctBy(x=>(x.SourceId,x.Url,x.Role)).ToList()};state.Events.Add(added);
      if(Rc4Radar.IsAlert(added))state.SignalHistory.Add(SignalEvidence(added,added.EventId,added.RevisionId,now));continue;
     }
      var old=state.Events[i];bool legacyMatch=old.OriginalId=="";bool sameOriginal=n.CanonicalOriginalId!=""&&n.CanonicalOriginalId==old.CanonicalOriginalId;bool strongIdentity=old.EventId==n.EventId||sameOriginal;
      bool preserveTerminal=old.Phase is "COMPLETED" or "CANCELLED"&&!DemonstrablyNewerTerminal(old,n);
      if(strongIdentity&&(preserveTerminal||!DemonstrablyNewerTerminal(old,n)&&WeakerThan(old,n)))n=old with{LastSeen=now,EvidenceSources=Rc4Radar.MergeEvidence(old,n),RelayUrl=old.RelayUrl!=""?old.RelayUrl:n.RelayUrl};
     bool major=!legacyMatch&&Major(old,n);
     string revision=major?Safe.Hash(old.RevisionId+"|"+n.RevisionId):old.RevisionId;
     if(major){state.Transitions.Add(new(old.EventId,revision,now,old.SignalLevel,n.SignalLevel,old.SignalLevel!=n.SignalLevel?"SIGNAL_LEVEL_CHANGED":"MAJOR_EVIDENCE_UPDATE"));if(Rc4Radar.IsAlert(n))state.SignalHistory.Add(SignalEvidence(n,old.EventId,revision,now));}
    state.Events[i]=n with{EventId=old.EventId,FirstSeen=old.FirstSeen,LastSeen=now,ReadAt=major?null:old.ReadAt,RevisionId=revision,MajorTime=major?n.ResetUtc:old.MajorTime,
     Migration=legacyMatch?"LEGACY_CANONICAL_READ_PRESERVED":old.Migration,EvidenceSources=Rc4Radar.MergeEvidence(old,n),RelayUrl=old.RelayUrl!=""?old.RelayUrl:n.RelayUrl};}

   state.Events=state.Events.OrderByDescending(x=>x.LastSeen).Take(200).ToList();state.Transitions=state.Transitions.OrderBy(x=>x.Timestamp).TakeLast(1000).ToList();state.SignalHistory=state.SignalHistory.Where(x=>x.Timestamp>=now.AddHours(-96)).OrderBy(x=>x.Timestamp).TakeLast(2000).ToList();Save();}Changed?.Invoke();
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
   // Adapters are isolated per source; one in-flight pass prevents overlap while each source keeps its own schedule/backoff.
   var before=Rc4Radar.LatestConfiguredSources(Snapshot()).ToDictionary(x=>x.Id,x=>x.LastAttempt);
   await Task.WhenAll(Sources.Select(source=>Fetch(source,force,poll.Token)));
   lock(gate){if(!poll.IsCancellationRequested&&Rc4Radar.LatestConfiguredSources(state).Any(s=>!before.TryGetValue(s.Id,out var at)||at!=s.LastAttempt)){
    var now=DateTimeOffset.UtcNow;state.CoverageTimeline.Add(RadarCoverage.Measure(state,enabled,now));
    state.CoverageTimeline=state.CoverageTimeline.Where(x=>x.timestamp>=now.AddHours(-96)).OrderBy(x=>x.timestamp).TakeLast(5000).ToList();Save();
   }}Changed?.Invoke();
  }catch(OperationCanceledException) when(!ct.IsCancellationRequested&&!stop.IsCancellationRequested){}
  finally{lock(gate){if(activePoll==poll)activePoll=null;}fetchGate.Release();}
 }
 async Task Fetch(RadarSourceDefinition source,bool force,CancellationToken pollToken){
  string id=source.Id,url=source.Url;
  var old=Rc4Radar.LatestConfiguredSources(Snapshot()).FirstOrDefault(x=>x.Id==id)??new(id,url,SourceClass:source.SourceClass,DisplayName:source.DisplayName,Adapter:source.Adapter);
  var now=DateTimeOffset.UtcNow;if(old.NextCheck>now&&(!force||old.HttpStatus==429))return;
  List<Announcement>? parsedEntries=null;
  var next=old with{LastAttempt=now,Url=url,RetryAfterUtc=null,SourceClass=source.SourceClass,DisplayName=source.DisplayName,Adapter=source.Adapter};
  try{
   using var deadline=CancellationTokenSource.CreateLinkedTokenSource(pollToken);deadline.CancelAfter(RequestTimeout);var requestToken=deadline.Token;
   using var response=await Get(next,requestToken);int status=(int)response.StatusCode;
   next=next with{HttpStatus=status,FinalUrl=response.RequestMessage?.RequestUri?.GetLeftPart(UriPartial.Path)??url};
   if(status==304){
    if(old.LastSuccess==null)throw new InvalidDataException("UNEXPECTED_304");
    next=next with{Status="UNCHANGED",Failures=0,LastSuccess=now,NextCheck=NextHealthy(now,source),Error=""};
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
    parsedEntries=Rc4Radar.Parse(source,html,now,out int count);
    next=next with{Status="HEALTHY",Failures=0,LastSuccess=now,NextCheck=NextHealthy(now,source),CandidateCount=count,
     ETag=response.Headers.ETag?.ToString(),Modified=response.Content.Headers.LastModified,
     BodyHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant(),ResponseBytes=data.Length,Error="",
     CandidateExcerpt=AnnouncementParser.Clean(AnnouncementParser.Plain(html),240)};
   }
  }catch(OperationCanceledException) when(!pollToken.IsCancellationRequested){next=Failed(next,"TIMEOUT",0,now);}
   catch(HttpRequestException ex){next=Failed(next,ex.StatusCode.HasValue?"HTTP_"+(int)ex.StatusCode.Value:"NETWORK_ERROR",(int?)ex.StatusCode??0,now);}
   catch(InvalidDataException ex){next=Failed(next,ex.Message,next.HttpStatus,now);}
   catch(RegexMatchTimeoutException){next=Failed(next,"PARSER_TIMEOUT",next.HttpStatus,now);}
   catch(OperationCanceledException){throw;}
   catch{next=Failed(next,"PARSER_ERROR",0,now);}
  lock(gate){state.Sources.RemoveAll(x=>x.Id==id);state.Sources.Add(next);state.SourceChecks.Add(new(now,id,source.SourceClass,next.Status,next.CandidateCount,next.HttpStatus,next.BodyHash,next.LastSuccess,next.NextCheck,next.Error));
   state.SourceChecks=state.SourceChecks.Where(x=>x.Timestamp>=now.AddHours(-96)).OrderBy(x=>x.Timestamp).TakeLast(5000).ToList();Save();}
  // Include the producing source in the signal observation coverage.
  if(parsedEntries!=null)Apply(parsedEntries,now);Changed?.Invoke();
 }
 static DateTimeOffset NextHealthy(DateTimeOffset now,RadarSourceDefinition source)=>now.AddSeconds(source.IntervalMinutes*60+Random.Shared.Next(31,91));
 static SourceStatus Failed(SourceStatus s,string code,int http,DateTimeOffset now)=>s with{Status="DEGRADED",Failures=s.Failures+1,HttpStatus=http,Error=code,NextCheck=now.AddMinutes(BackoffMinutes(s.Failures+1))};
 internal static bool ScopeMatches(Announcement a,string? plan)=>a.Eligibility=="AS_ANNOUNCED"&&!string.IsNullOrWhiteSpace(plan)&&new[]{"free","plus","pro","team","business","enterprise","edu"}.Contains(plan.ToLowerInvariant())&&
  (a.Scope=="ALL"||(a.Scope=="PAID"&&new[]{"plus","pro","business","enterprise"}.Contains(plan.ToLowerInvariant()))||a.Scope.Split('|').Contains(plan.ToUpperInvariant()));
 public void Correlate(IEnumerable<UsageSample> input){
   var rows=input.Where(x=>x.limit_id=="codex"&&x.window_duration_mins==10080).OrderBy(x=>x.observed_at_utc).ToArray();var announcements=Snapshot().Events;var list=new List<ResetCorrelation>();var quota=new List<RadarQuotaEvidence>();
   for(int i=1;i<rows.Length;i++){
    var p=rows[i-1];var c=rows[i];bool bankedObservedDifference=p.reset_credits_available_count!=c.reset_credits_available_count;if(!DailyUsageCalculator.Valid(p)||!DailyUsageCalculator.Valid(c)||!(c.remaining_percent>p.remaining_percent||p.reset_at_unix_seconds!=c.reset_at_unix_seconds||bankedObservedDifference))continue;
   double seconds=(c.observed_at_utc-p.observed_at_utc).TotalSeconds;
   bool stable=p.context_assurance=="STABLE_SCOPE"&&c.context_assurance=="STABLE_SCOPE"&&p.account_context_key==c.account_context_key&&p.comparison_key==c.comparison_key&&p.plan_type==c.plan_type&&p.source_slot==c.source_slot&&!c.had_gap&&seconds>0&&seconds<=Math.Max(3*c.polling_interval_seconds,300);
   bool scheduled=p.reset_at_utc.HasValue&&p.reset_at_utc>=p.observed_at_utc&&p.reset_at_utc<=c.observed_at_utc;
   var a=announcements.Where(x=>x.Effect=="AUTOMATIC_QUOTA_RESET"&&x.Phase!="CANCELLED"&&x.SignalLevel is "INCOMING" or "COMPLETED"&&x.ResetUtc!=null&&Math.Abs((x.ResetUtc.Value-c.observed_at_utc).TotalHours)<=2).OrderByDescending(x=>x.SourceClass=="OFFICIAL_OPENAI").ThenByDescending(x=>x.LastSeen).FirstOrDefault();
    bool bankedComparable=stable&&p.reset_credits_available_count.HasValue&&c.reset_credits_available_count.HasValue;bool bankedChanged=bankedComparable&&p.reset_credits_available_count!=c.reset_credits_available_count;bool banked=bankedChanged&&c.reset_credits_available_count<p.reset_credits_available_count;
   bool scopeMatches=a!=null&&ScopeMatches(a,c.plan_type);
   bool refill=c.remaining_percent>p.remaining_percent;
    bool probable=stable&&refill&&a!=null&&scopeMatches&&!scheduled&&!bankedObservedDifference&&a.Phase!="CANCELLED";
    string classification=stable&&scheduled?"SCHEDULED_WEEKLY_RESET":stable&&banked&&refill?"BANKED_RESET_PROBABLE":bankedChanged?"BANKED_CREDIT_MOVEMENT":bankedObservedDifference?"INCONCLUSIVE":probable?"GLOBAL_RESET_PROBABLE":a!=null?"INCONCLUSIVE":"UNCLASSIFIED_REPLENISHMENT";
    string reason=!stable?"Identity or observation scope insufficient":scheduled?"Scheduled time coincided; this is supporting evidence only":
     bankedObservedDifference&&!bankedComparable?"Banked reset count baseline or observation scope is insufficient":bankedChanged?"Banked reset count changed; this is supporting evidence only":a==null?"No matching announcement evidence":
    !scopeMatches?"Announcement eligibility or plan not verified":!refill?"No numerical refill observed":"Official announcement and local refill occurred around the same time; cause remains unknown";
   list.Add(new(p.sample_id,c.sample_id,p.observed_at_utc,c.observed_at_utc,p.remaining_percent,c.remaining_percent,classification,reason,a?.EventId,stable,scheduled,
     a==null?"UNVERIFIED":a.Tier==1?"OFFICIAL_ANNOUNCED":a.Tier==2?"TEAM_REPORTED":"UNVERIFIED",
      !stable||bankedObservedDifference&&!bankedComparable?"NOT_COMPARABLE":refill?"REPLENISHMENT_OBSERVED":bankedChanged?"BANKED_RESET_MOVEMENT":"NO_OBSERVATION",
     probable?"PROBABLE":a!=null?"INCONCLUSIVE":"NOT_ASSESSED","UNKNOWN"));
    quota.Add(new(a?.EventId??(bankedChanged?"BANKED_RESET_MOVEMENT":bankedObservedDifference?"BANKED_RESET_OBSERVATION":"LOCAL_REPLENISHMENT_OBSERVED"),c.observed_at_utc,p.sample_id,c.sample_id,p.observed_at_utc,c.observed_at_utc,p.remaining_percent,c.remaining_percent,p.reset_at_utc,c.reset_at_utc,p.reset_credits_available_count,c.reset_credits_available_count,seconds,stable&&(!bankedObservedDifference||bankedComparable)?"STABLE_SCOPE":"SCOPE_NOT_FULLY_VERIFIED","UNKNOWN"));
   }
   var anchor=rows.LastOrDefault()?.observed_at_utc??DateTimeOffset.UtcNow;var cutoff=anchor.AddHours(-96);
   lock(gate){state.Correlations=state.Correlations.Concat(list).Where(x=>x.To>=cutoff).GroupBy(x=>(x.PreviousSample,x.CurrentSample)).Select(x=>x.Last()).OrderBy(x=>x.To).TakeLast(500).ToList();state.QuotaEvidence=state.QuotaEvidence.Concat(quota).Where(x=>x.To>=cutoff).GroupBy(x=>(x.PreviousSample,x.CurrentSample)).Select(x=>x.Last()).OrderBy(x=>x.To).TakeLast(500).ToList();Save();}
 }
 public static string Suggest(Announcement a,decimal? weekly,int? banked,bool localObserved,DateTimeOffset now,string? currentPlan=null,string? language=null){
  return L.TFor(language??L.Language,"Radar."+SuggestionKey(a,weekly,banked,localObserved,now,currentPlan));
 }
 internal static string SuggestionKey(Announcement a,decimal? weekly,int? banked,bool localObserved,DateTimeOffset now,string? currentPlan){
  if(a.Phase=="CANCELLED")return "SuggestCancelled";
  if(a.SignalLevel=="WATCH")return "SuggestWatch";
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
 readonly MonitorService service;Announcement? current;bool shown,readQueued,detailsOpen,explainOpen,checking;
 readonly Label statusIcon=Theme.Label("",34,18),statusTitle=Theme.Label("",34,16),statusSummary=Theme.Label("",38,10.5f),healthSummary=Theme.Label("",48,9.5f,Theme.Muted),
  eventsHeading=Theme.Label("",28,12,Theme.Text),sourceHeading=Theme.Label("",30,12,Theme.Text),sourceSummary=Theme.Label("",150,9.5f),sourceDetails=Theme.Label("",250,9,Theme.Muted),
   badge=Theme.Label("",30,11),eventTitle=Theme.Label("",44,14),signalState=Theme.Label("",26,9.5f),readState=Theme.Label("",26,9.5f,Theme.Muted),quote=Theme.Label("",76,10),published=Theme.Label("",30,9,Theme.Muted),timeCaption=Theme.Label("",22,9,Theme.Muted),
  time=Theme.Label("",42,13),weeklyCaption=Theme.Label("",22,9,Theme.Muted),amount=Theme.Label("",48,28),suggestion=Theme.Label("",108,10.5f),credits=Theme.Label("",30,9.5f),
  source=Theme.Label("",34,9.5f),evidence=Theme.Label("",62,9.5f,Theme.Muted),explain=Theme.Label("",390,9.5f,Theme.Muted);
 readonly RadarCoverageView coverageView=new();
 readonly GoldCycleCard localRefill=new(){Visible=false};
 readonly GoldActionButton localEvidence=new();readonly FlowLayoutPanel localEvidenceRow=Theme.Row();
 readonly FlowLayoutPanel eventsPanel=new(){Dock=DockStyle.Top,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=false,Height=0,Tag=0,Padding=new(0)};
  readonly RadarAccentPanel eventCard=new(){Dock=DockStyle.Top,Height=522,Tag=522,Padding=new(16,12,12,12),BackColor=Theme.Secondary};
 readonly Button nextButton,sourceButton,relayButton,closeButton,checkButton,detailsButton,explainButton;
 public ResetInfoForm(MonitorService s,Announcement? a):base(L.T("Radar.PageTitle"),1120,860){
  service=s;current=a;KeyPreview=true;statusSummary.SizeChanged+=(_,_)=>FitStatusSummary();
  void CardStack(params Control[] controls){foreach(var c in controls.Reverse()){c.BackColor=Color.Transparent;eventCard.Controls.Add(c);}}
   CardStack(badge,eventTitle,signalState,readState,quote,published,timeCaption,time,weeklyCaption,amount,suggestion,credits,source,evidence);
  checkButton=Theme.Button("",async(_,_)=>await CheckNow());checkButton.Width=145;
  detailsButton=Theme.Button("",(_,_)=>{detailsOpen=!detailsOpen;ApplyTexts();});detailsButton.Width=150;
  explainButton=Theme.Button("",(_,_)=>{explainOpen=!explainOpen;ApplyTexts();});explainButton.Width=230;
  statusSummary.BackColor=Color.FromArgb(160,4,10,28);statusIcon.Visible=false;statusTitle.Height=48;statusTitle.Tag=48;statusTitle.Font=Theme.Font(24,true);
  coverageView.Height=320;coverageView.Tag=320;
  var left=new R2Stack(coverageView,healthSummary,Theme.Row(checkButton),new R2Card(sourceHeading,sourceSummary,Theme.Row(detailsButton),sourceDetails){Accent=Theme.Electric});
  eventCard.Controls.Remove(weeklyCaption);eventCard.Controls.Remove(amount);eventCard.Controls.Remove(credits);badge.Visible=false;
  quote.Height=68;quote.Tag=68;published.Height=25;published.Tag=25;eventCard.Height=570;eventCard.Tag=570;
  localEvidence.Dock=DockStyle.None;localEvidenceRow.Controls.Add(localEvidence);localEvidenceRow.Height=64;localEvidenceRow.Tag=64;localEvidenceRow.Visible=false;localEvidenceRow.Layout+=(_,_)=>{float scale=DeviceDpi/96f;localEvidence.Size=new((int)(210*scale),(int)(44*scale));localEvidenceRow.Height=(int)(64*scale);};localEvidence.Click+=(_,_)=>{if(localRefill.Evidence is {} cycle)new HistoryForm(service,cycle.observed_to.ToLocalTime().Date,cycle.after_sample_id).Show();};
  var right=new R2Stack(eventsHeading,eventsPanel,localRefill,localEvidenceRow,eventCard,new R2Card(weeklyCaption,amount,credits){Accent=Theme.Brand},new R2Card(Theme.Row(explainButton),explain){Accent=Theme.Brand});
  Stack(statusTitle,statusSummary,new R2Columns(left,right));((QuietButton)checkButton).Primary=true;

  ((QuietButton)detailsButton).Role=ButtonRole.Ghost;((QuietButton)explainButton).Role=ButtonRole.Ghost;
  closeButton=Theme.Button("",(_,_)=>Close());closeButton.Width=82;
  nextButton=Theme.Button("",(_,_)=>ShowNext());nextButton.Width=92;
  sourceButton=Theme.Button("",(_,_)=>OpenPreferred());sourceButton.Width=154;
  relayButton=Theme.Button("",(_,_)=>OpenRelay());relayButton.Width=132;
  ((QuietButton)sourceButton).Primary=false;((QuietButton)nextButton).Role=ButtonRole.Ghost;((QuietButton)closeButton).Role=ButtonRole.Ghost;Footer.Controls.Add(closeButton);Footer.Controls.Add(nextButton);Footer.Controls.Add(relayButton);Footer.Controls.Add(sourceButton);
  ApplyTexts();L.Watch(this,ApplyTexts);service.Radar.Changed+=OnDataChanged;service.Changed+=OnDataChanged;
  FormClosed+=(_,_)=>{service.Radar.Changed-=OnDataChanged;service.Changed-=OnDataChanged;};
  Shown+=(_,_)=>{shown=true;ApplyTexts();QueueReadAfterRender();BeginInvoke((Action)ScrollTop);};
 }
 void FitStatusSummary(){if(statusSummary.Width<=0)return;float k=DeviceDpi/96f;int needed=Math.Max((int)(38*k),TextRenderer.MeasureText(statusSummary.Text,statusSummary.Font,new Size(statusSummary.Width,int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl).Height+(int)(8*k));statusSummary.Tag=(int)Math.Ceiling(needed/k);statusSummary.Height=needed;statusSummary.AccessibleName=statusSummary.Text;}
 internal string? DisplayedEventId=>current?.EventId;
 internal string DisplayedSignalLevel=>current?.SignalLevel??"NONE";
 internal string DisplayedSignalStatus=>signalState.Text;
 internal string DisplayedReadStatus=>readState.Text;
 internal string DisplayedSuggestion=>suggestion.Text;
 internal void ScrollSuggestionForTest(){Body.ScrollControlIntoView(suggestion);}
 internal string DisplayedRadarStatus=>statusTitle.Text;
 internal int DisplayedActiveSignalRows=>eventsPanel.Controls.Count;
 void OnDataChanged(){if(!IsDisposed&&IsHandleCreated)try{BeginInvoke((Action)ApplyTexts);}catch(InvalidOperationException){}}
 async Task CheckNow(){if(checking)return;checking=true;ApplyTexts();try{await service.Radar.Poll(true,CancellationToken.None);}catch{}finally{checking=false;if(!IsDisposed)ApplyTexts();}}
 void OpenPreferred(){if(current==null)return;string url=Rc4Radar.PreferredUrl(current);if(Rc4Radar.CanOpenHumanUrl(url))OverlayForm.OpenUrl(url);}
 void OpenRelay(){if(current!=null&&Rc4Radar.CanOpenHumanUrl(current.RelayUrl))OverlayForm.OpenUrl(current.RelayUrl);}
 void ShowNext(){var entry=service.Radar.UnreadItems.FirstOrDefault(x=>current==null||x.EventId!=current.EventId||x.RevisionId!=current.RevisionId);if(entry==null)return;Select(entry);}
 void Select(Announcement entry){current=entry;Body.AutoScrollPosition=Point.Empty;ApplyTexts();QueueReadAfterRender();}
 void QueueReadAfterRender(){
  if(!shown||readQueued||current==null||!Visible)return;readQueued=true;var displayed=current;
  BeginInvoke((Action)(()=>{readQueued=false;if(IsDisposed||!Visible||current?.EventId!=displayed.EventId||current.RevisionId!=displayed.RevisionId)return;
   try{Body.PerformLayout();Body.Refresh();Update();service.Radar.Read(displayed.EventId,displayed.RevisionId);UpdateNavigation();ScrollTop();}catch{}}));
 }
 void ScrollTop(){
  if(IsDisposed)return;
  // WinForms may auto-scroll the first visible card while it assigns initial focus.
  // Put focus on the page-level Check action, then restore the summary origin once.
  checkButton.Select();Body.ScrollControlIntoView(checkButton);Body.AutoScrollPosition=new Point(0,0);
  if(Body.VerticalScroll.Visible)Body.VerticalScroll.Value=Body.VerticalScroll.Minimum;
  Body.PerformLayout();
 }
 void UpdateNavigation(){
  nextButton.Enabled=service.Radar.UnreadItems.Any(x=>current==null||x.EventId!=current.EventId||x.RevisionId!=current.RevisionId);
  string preferred=current==null?"":Rc4Radar.PreferredUrl(current);sourceButton.Enabled=Rc4Radar.CanOpenHumanUrl(preferred);
  relayButton.Visible=current!=null&&current.OriginalSourceUrl!=""&&current.RelayUrl!=""&&AnnouncementParser.CanonicalUrl(current.OriginalSourceUrl)!=AnnouncementParser.CanonicalUrl(current.RelayUrl);
  relayButton.Enabled=relayButton.Visible&&Rc4Radar.CanOpenHumanUrl(current!.RelayUrl);
  sourceButton.Text=L.T(!string.IsNullOrWhiteSpace(current?.OriginalSourceUrl)?"Radar.OpenOriginal":"Radar.OpenRelay");relayButton.Text=L.T("Radar.OpenRelay");
  foreach(var button in new[]{nextButton,sourceButton,relayButton})button.AccessibleName=button.Text;
 }
 void ApplyTexts(){
  if(IsDisposed)return;var now=DateTimeOffset.UtcNow;var state=service.Radar.Snapshot();if(current!=null)current=state.Events.FirstOrDefault(x=>x.EventId==current.EventId&&x.RevisionId==current.RevisionId)??state.Events.FirstOrDefault(x=>x.EventId==current.EventId)??current;Text=L.T("Radar.PageTitle");AccessibleName=Text;

  closeButton.Text=L.T("Radar.Close");nextButton.Text=L.T("Radar.Next");checkButton.Text=L.T(checking?"Radar.Checking":"Radar.CheckNow");checkButton.Enabled=!checking;
  detailsButton.Text=L.T(detailsOpen?"Radar.HideDetails":"Radar.ShowDetails");explainButton.Text=L.T(explainOpen?"Radar.HideHow":"Radar.ShowHow");
  foreach(var b in new[]{closeButton,nextButton,checkButton,detailsButton,explainButton})b.AccessibleName=b.Text;
  var active=state.Events.Where(x=>ResetRadar.ActiveSignalAt(x,state,now)).OrderByDescending(x=>Rc4Radar.SignalPriority(x.SignalLevel)).ThenBy(x=>x.ReadAt!=null).ThenByDescending(x=>x.LastSeen).ToList();
  string level=active.FirstOrDefault()?.SignalLevel??"NONE";var headerColor=SignalColor(level,false);
  localRefill.Evidence=state.LocalCycleEvents.LastOrDefault(x=>x.Confirmed&&state.CurrentLocalScopes.Contains(x.scope_key));localEvidenceRow.Visible=localRefill.Visible=localRefill.Evidence!=null;localRefill.Invalidate();
  statusIcon.Text=level switch{"WATCH"=>"●","INCOMING"=>"!","COMPLETED"=>"✓",_=>"○"};statusIcon.ForeColor=headerColor;
  statusTitle.Text=L.T(level switch{"WATCH"=>"Radar.StatusWatch","INCOMING"=>"Radar.StatusIncoming","COMPLETED"=>"Radar.StatusCompleted","NOTICE"=>"Radar.StatusNotice",_=>"Radar.StatusNone"});statusTitle.ForeColor=headerColor;
  statusSummary.Text=L.T(level switch{"WATCH"=>"Radar.StatusWatchSummary","INCOMING"=>"Radar.StatusIncomingSummary","COMPLETED"=>"Radar.StatusCompletedSummary","NOTICE"=>"Radar.StatusNoticeSummary",_=>"Radar.StatusNoneSummary"});
  if(state.LocalCycleDiagnostic.Length>0){statusSummary.Text=GoldVisual.Copy("Local evidence state could not be saved. Existing history is preserved.","本機證據狀態未能保存，既有歷史仍保留。");}
   FitStatusSummary();var currentSources=Rc4Radar.LatestConfiguredSources(state).ToList();var last=currentSources.Select(x=>x.LastAttempt).Where(x=>x!=null).Max();string coverageValue=Rc4Radar.Coverage(state,service.Radar.Enabled,now);int healthy=Rc4Radar.HealthyConfiguredCount(state,now);
  coverageView.UpdateCoverage(RadarCoverage.Measure(state,service.Radar.Enabled,now));
  healthSummary.Text=L.T("Radar.CoverageUpdated",Relative(last,now));
  eventsHeading.Text=L.T("Radar.SignalsHeading");foreach(Control oldControl in eventsPanel.Controls)oldControl.Dispose();eventsPanel.Controls.Clear();
   int rowWidth=Math.Max(200,eventsPanel.ClientSize.Width>0?eventsPanel.ClientSize.Width:Body.ClientSize.Width-Body.Padding.Horizontal);
   foreach(var item in active.Take(6)){
    var captured=item;var label=Theme.Label(DisplaySignal(item,false)+"  ·  "+AnnouncementParser.Clean(item.OriginalQuote!=""?item.OriginalQuote:item.Title,90),30,9.5f,SignalColor(item.SignalLevel,false));
    label.AutoEllipsis=true;label.Dock=DockStyle.None;label.Width=rowWidth;label.Margin=new Padding(0);label.Cursor=Cursors.Hand;label.AccessibleRole=AccessibleRole.Link;label.Click+=(_,_)=>Select(captured);eventsPanel.Controls.Add(label);
  }
  eventsPanel.Height=active.Count==0?0:Math.Min(180,active.Take(6).Count()*30);eventsPanel.Tag=eventsPanel.Height;eventsHeading.Visible=eventsPanel.Visible=active.Count>0;
   ApplyEvent(state,now);sourceHeading.Text=L.T("Radar.SourcesHeading");sourceSummary.Text=SourceText(state,false,now);sourceDetails.Text=SourceText(state,true,now);sourceDetails.Visible=detailsOpen;
   int detailsWidth=Math.Max(240,sourceDetails.Width>0?sourceDetails.Width:Body.ClientSize.Width-Body.Padding.Horizontal-32);sourceDetails.Height=TextRenderer.MeasureText(sourceDetails.Text,sourceDetails.Font,new Size(detailsWidth,int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl).Height+16;
  explain.Text=L.T("Radar.HowText");explain.Visible=explainOpen;int explainWidth=Math.Max(240,explain.Width>0?explain.Width:Body.ClientSize.Width-Body.Padding.Horizontal-32);explain.Height=TextRenderer.MeasureText(explain.Text,explain.Font,new Size(explainWidth,int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl).Height+16;
  foreach(var label in new[]{statusIcon,statusTitle,statusSummary,healthSummary,eventsHeading,sourceHeading,sourceSummary,sourceDetails,explain})label.AccessibleName=label.Text;
  UpdateNavigation();Body.PerformLayout();Invalidate(true);
 }
 void ApplyEvent(RadarState state,DateTimeOffset now){
  weeklyCaption.Text=L.T("Radar.WeeklyRemaining");var value=service.MainClock.Value?.Remaining;amount.Text=value==null?"—":value==0?"0%":value<1?"<1%":value.Value.ToString("0.#",CultureInfo.InvariantCulture)+"%";amount.ForeColor=Theme.QuotaColor(value);
  credits.Text=L.T("Radar.ResetCount",service.Current?.ResetCredits?.ToString(CultureInfo.InvariantCulture)??L.T("Radar.NotProvided"));foreach(var label in new[]{weeklyCaption,amount,credits})label.AccessibleName=label.Text;
  var a=current;eventCard.Visible=a!=null;if(a==null)return;var correlation=state.Correlations.LastOrDefault(x=>x.AnnouncementId==a.EventId);bool observed=correlation?.LocalObservation=="REPLENISHMENT_OBSERVED";
  string shownLevel=observed?"OBSERVED":a.SignalLevel;var color=SignalColor(shownLevel,false);eventCard.Accent=color;badge.Text=DisplaySignal(a,observed);badge.ForeColor=eventTitle.ForeColor=color;
  eventTitle.Text=L.T(shownLevel switch{"WATCH"=>"Radar.EventWatchTitle","INCOMING"=>"Radar.EventIncomingTitle","OBSERVED"=>"Radar.EventObservedTitle","COMPLETED"=>"Radar.EventCompletedTitle",_=>"Radar.EventNoticeTitle"});
  bool active=ResetRadar.ActiveSignalAt(a,state,now);if(eventCard.Parent is R2Stack stack){int localIndex=stack.Controls.GetChildIndex(localRefill),eventIndex=stack.Controls.GetChildIndex(eventCard);if(active&&a.SignalLevel=="INCOMING"&&eventIndex<localIndex)stack.Controls.SetChildIndex(eventCard,localIndex);else if(!(active&&a.SignalLevel=="INCOMING")&&eventIndex>localIndex)stack.Controls.SetChildIndex(eventCard,Math.Max(0,localIndex-2));}string signalValue=active&&a.SignalLevel=="WATCH"?L.T("Radar.SignalActiveWatch"):active&&a.SignalLevel=="INCOMING"?L.T("Radar.SignalActiveIncoming"):L.T("Radar.SignalInactive");signalState.Text=ResetRadar.LocallySatisfied(a,state,now)?GoldVisual.Copy("Local attention: satisfied by the new quota cycle · original phase retained","本機提醒：新額度週期已滿足 · 原始公告狀態保留"):L.T("Radar.SignalStatus",signalValue);signalState.ForeColor=active?color:Theme.Muted;
  readState.Text=L.T("Radar.ReadStatus",L.T(a.ReadAt==null?"Radar.ReadUnread":"Radar.ReadRead"));
  quote.Text="“"+AnnouncementParser.Clean(a.OriginalQuote!=""?a.OriginalQuote:a.Summary,500)+"”";published.Text=L.T("Radar.Published",a.PublishedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz",CultureInfo.InvariantCulture)??L.T("Radar.NotProvided"));
  timeCaption.Text=L.T(a.Effect=="BANKED_CREDIT_GRANT"?"Radar.CreditGrantTime":a.Effect=="AUTOMATIC_QUOTA_RESET"?"Radar.ExpectedReset":"Radar.NoticeTime");time.Text=AnnouncementParser.LocalTime(a,now);time.ForeColor=shownLevel=="INCOMING"?color:Theme.Text;

  string? plan=service.Current?.Windows.FirstOrDefault(w=>w.LimitId=="codex"&&w.Duration==10080)?.Plan;suggestion.Text=ResetRadar.Suggest(a,value,service.Current?.ResetCredits,observed,now,plan);
  credits.Text=L.T("Radar.ResetCount",service.Current?.ResetCredits?.ToString(CultureInfo.InvariantCulture)??L.T("Radar.NotProvided"));source.Text=L.T("Radar.SourceValue",Rc4Radar.SourceLabel(a,L.Language));
  evidence.Text=L.T(correlation?.Correlation=="PROBABLE"?"Radar.EvidenceProbable":correlation?.LocalObservation=="NOT_COMPARABLE"?"Radar.EvidenceNotComparable":"Radar.EvidenceNoObservation");
  foreach(var label in new[]{badge,eventTitle,signalState,readState,quote,published,timeCaption,time,weeklyCaption,amount,suggestion,credits,source,evidence})label.AccessibleName=label.Text;eventCard.PerformLayout();
 }
 static Color SignalColor(string level,bool sourceFailure)=>Theme.HighContrast?Theme.Text:sourceFailure?Theme.Critical:level=="WATCH"?Theme.Warning:level=="INCOMING"?Theme.ResetAlert:level is "COMPLETED" or "OBSERVED"?Theme.Accent:Theme.Text;
 static string DisplaySignal(Announcement a,bool observed)=>observed?L.T("Radar.BadgeObserved"):L.T(a.SignalLevel switch{"WATCH"=>"Radar.BadgeWatch","INCOMING"=>"Radar.BadgeIncoming","COMPLETED"=>"Radar.BadgeCompleted",_=>"Radar.BadgeNotice"});
 static string Relative(DateTimeOffset? value,DateTimeOffset now){if(value==null)return L.T("Radar.NeverChecked");var age=now-value.Value;if(age<TimeSpan.Zero)age=TimeSpan.Zero;if(age.TotalMinutes<1)return L.T("Radar.JustNow");if(age.TotalHours<1)return L.T("Radar.MinutesAgo",(int)age.TotalMinutes);if(age.TotalDays<1)return L.T("Radar.HoursAgo",(int)age.TotalHours);return L.T("Radar.DaysAgo",(int)age.TotalDays);}
  static string HealthWord(SourceStatus item,DateTimeOffset now){if(item.Status is "HEALTHY" or "UNCHANGED"&&!Rc4Radar.CurrentHealthy(item,now))return L.T("Radar.HealthStale");return L.T(item.Status switch{"HEALTHY"=>"Radar.HealthHealthy","UNCHANGED"=>"Radar.HealthUnchanged","DEGRADED"=>"Radar.HealthDegraded","BLOCKED"=>"Radar.HealthBlocked",_=>"Radar.HealthNotChecked"});}
 static string SourceClassWord(string value)=>L.T(value switch{"OFFICIAL_OPENAI"=>"Radar.SourceOfficial","OPENAI_STATUS"=>"Radar.SourceStatus","TEAM_SIGNAL_RELAY"=>"Radar.SourceRelay",_=>"Radar.SourceThirdParty"});
 static string SourceText(RadarState state,bool detailed,DateTimeOffset now){
    var lines=new List<string>();foreach(var definition in ResetRadar.Sources){var item=state.Sources.Where(x=>x.Id==definition.Id).OrderByDescending(x=>x.LastAttempt??DateTimeOffset.MinValue).FirstOrDefault()??new(definition.Id,definition.Url,SourceClass:definition.SourceClass,DisplayName:definition.DisplayName,Adapter:definition.Adapter);
    string icon=Rc4Radar.CurrentHealthy(item,now)?"✓":item.Status=="NOT_CHECKED"?"○":"⚠";string basic=$"{icon} {definition.DisplayName} — {SourceClassWord(definition.SourceClass)}"+(item.HttpStatus>0&&item.Status=="DEGRADED"?$" · HTTP {item.HttpStatus}":"");lines.Add(basic);
   if(detailed)lines.Add("   "+L.T("Radar.SourceDetail",item.Url,HealthWord(item,now),item.LastSuccess?.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz",CultureInfo.InvariantCulture)??L.T("Radar.NotProvided"),item.LastAttempt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz",CultureInfo.InvariantCulture)??L.T("Radar.NotProvided"),item.HttpStatus==0?L.T("Radar.NotProvided"):item.HttpStatus.ToString(CultureInfo.InvariantCulture),item.NextCheck?.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz",CultureInfo.InvariantCulture)??L.T("Radar.NotProvided"),item.CandidateCount));}
  return string.Join("\n",lines);
 }
}
internal sealed class RadarAccentPanel:Panel {
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 public Color Accent{get;set;}=Theme.Accent;
 public RadarAccentPanel(){DoubleBuffered=true;SizeChanged+=(_,_)=>{if(Width<2||Height<2)return;using var p=Theme.Round(new RectangleF(0,0,Width,Height),12*DeviceDpi/96f);var old=Region;Region=new Region(p);old?.Dispose();};}
 bool measuring;
 protected override void OnLayout(LayoutEventArgs e){if(!R2.Is(this)||measuring||!Visible){base.OnLayout(e);return;}measuring=true;try{float k=DeviceDpi/96f;int width=Math.Max(150,ClientSize.Width-Padding.Horizontal);foreach(var label in Controls.OfType<Label>().Where(x=>x.Visible)){int measured=TextRenderer.MeasureText(label.Text,label.Font,new Size(width,int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl).Height;label.Height=Math.Max((int)(20*k),measured+(int)(10*k));}Height=Controls.Cast<Control>().Where(x=>x.Visible).Sum(x=>x.Height)+Padding.Vertical;Tag=(int)Math.Ceiling(Height/k);base.OnLayout(e);}finally{measuring=false;}}
 protected override void OnPaint(PaintEventArgs e){if(R2.Is(this)){R2.Surface(this,e.Graphics);return;}base.OnPaint(e);float k=DeviceDpi/96f;StellarSurface.Glass(e.Graphics,new RectangleF(0,0,Width-1,Height-1),12*k);using var pen=new Pen(Accent,3*k){StartCap=System.Drawing.Drawing2D.LineCap.Round,EndCap=System.Drawing.Drawing2D.LineCap.Round};e.Graphics.DrawLine(pen,2*k,14*k,2*k,Height-14*k);}
}





