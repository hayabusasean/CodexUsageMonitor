using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace CodexUsageMonitor;

internal static class Rc4Radar
{
    static readonly TimeSpan RegexBudget=TimeSpan.FromMilliseconds(500);
    static Match Match(string text,string pattern)=>Regex.Match(text,pattern,RegexOptions.IgnoreCase|RegexOptions.Singleline,RegexBudget);
    static MatchCollection Matches(string text,string pattern)=>Regex.Matches(text,pattern,RegexOptions.IgnoreCase|RegexOptions.Singleline,RegexBudget);

    internal static List<Announcement> Parse(RadarSourceDefinition source,string payload,DateTimeOffset now,out int candidates,RadarRules? rules=null)
    {
        return source.Adapter switch
        {
            "RSS"=>ParseFeed(source,payload,now,out candidates,rules??new()),
            "TEAM_HTML"=>ParseTeamHtml(source,payload,now,out candidates,rules??new()),
            "LEGACY_HTML"=>EnrichLegacy(source,AnnouncementParser.Parse(source.Id,source.Url,payload,now,out candidates)),
            _=>throw new InvalidDataException("UNKNOWN_ADAPTER")
        };
    }

    static List<Announcement> EnrichLegacy(RadarSourceDefinition source,List<Announcement> values)
        =>values.Select(x=>Classify(x with{SourceClass=source.SourceClass,RelayUrl="",OriginalSourceUrl=x.Url,
            CanonicalOriginalId=x.OriginalId==""?AnnouncementParser.CanonicalUrl(x.Url):x.OriginalId,
            OriginalQuote=x.Summary,EvidenceSources=[new(source.Id,source.SourceClass,x.Url,"ORIGINAL",x.ContentHash)]})).ToList();

    sealed record TeamPost(string Url,string StableId,string Title,string Quote,DateTimeOffset? Published,string Original,string Category);
    static List<Announcement> ParseFeed(RadarSourceDefinition source,string xml,DateTimeOffset now,out int candidates,RadarRules rules)
    {
        using var reader=XmlReader.Create(new StringReader(xml),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=ResetRadar.MaxResponseBytes});
        var doc=XDocument.Load(reader,LoadOptions.None);
        var units=doc.Descendants().Where(x=>x.Name.LocalName is "item" or "entry").Take(120).ToArray();
        if(units.Length==0)throw new InvalidDataException("FEED_STRUCTURE_CHANGED");
        candidates=units.Length;var output=new List<Announcement>();var posts=new List<TeamPost>();
        foreach(var unit in units)
        {
            string Value(string name)=>unit.Elements().FirstOrDefault(x=>x.Name.LocalName==name)?.Value.Trim()??"";
            string title=Value("title"),guid=Value("guid");
            string link=Value("link");if(link=="")link=unit.Elements().FirstOrDefault(x=>x.Name.LocalName=="link")?.Attribute("href")?.Value??"";
            string body=string.Join(" ",unit.Elements().Where(x=>x.Name.LocalName is "description" or "content" or "encoded" or "summary").Select(x=>AnnouncementParser.Plain(x.Value)));
            if(body=="")body=title;
            string publishedText=Value("pubDate");if(publishedText=="")publishedText=Value("published");if(publishedText=="")publishedText=Value("updated");
            DateTimeOffset? published=DateTimeOffset.TryParse(publishedText,CultureInfo.InvariantCulture,DateTimeStyles.AllowWhiteSpaces,out var parsed)?parsed.ToUniversalTime():null;
            string raw=unit.ToString(SaveOptions.DisableFormatting);string original=FirstOriginalUrl(raw,link);
            string quote=ExtractOriginalQuote(unit,body);string itemUrl=SafePublicUrl(link,source.Url);
            if(source.SourceClass=="TEAM_SIGNAL_RELAY"){
                string category=string.Join("|",unit.Elements().Where(x=>x.Name.LocalName=="category").Select(x=>x.Attribute("term")?.Value??x.Value));
                posts.Add(new(itemUrl,guid,title,quote,SnowflakeTime(original)??published,original,category));continue;
            }
            Announcement? item=source.SourceClass switch
            {
                "OPENAI_STATUS"=>StatusCandidate(source,itemUrl,guid,title,body,published,now),
                _=>OfficialCandidate(source,itemUrl,guid,title,body,published,now)
            };
            if(item!=null)output.Add(item);
        }
        if(posts.Count>0)output.AddRange(ParseTeamPosts(source,posts,now,rules));
        return output.GroupBy(x=>x.EventId).Select(x=>MergeEvidence(x.Last(),x.Take(x.Count()-1))).ToList();
    }

    static List<Announcement> ParseTeamHtml(RadarSourceDefinition source,string html,DateTimeOffset now,out int candidates,RadarRules rules)
    {
        var units=Matches(html,@"<li\b[^>]*class\s*=\s*[""'][^""']*\bfeed-item\b[^""']*[""'][^>]*>(.*?)</li>");
        if(units.Count==0)throw new InvalidDataException("TEAM_FEED_STRUCTURE_CHANGED");
        candidates=units.Count;var posts=new List<TeamPost>();
        foreach(Match unit in units.Cast<Match>().Take(100))
        {
            string raw=unit.Value;string original=FirstOriginalUrl(raw,"");if(original=="")continue;
            string quote=AnnouncementParser.Plain(Match(raw,@"<p\b[^>]*class\s*=\s*[""'][^""']*\bfeed-text\b[^""']*[""'][^>]*>(.*?)</p>").Groups[1].Value);
            if(quote.Length<4)continue;
            string age=AnnouncementParser.Plain(Match(raw,@"<a\b[^>]*class\s*=\s*[""'][^""']*\bfeed-time\b[^""']*[""'][^>]*>(.*?)</a>").Groups[1].Value);
            DateTimeOffset? published=SnowflakeTime(original)??RelativeTime(age,now);
            posts.Add(new(source.Url,CanonicalOriginalId(original),"Codex 團隊重置消息",quote,published,original,""));
        }
        return ParseTeamPosts(source,posts,now,rules);
    }

    static List<Announcement> ParseTeamPosts(RadarSourceDefinition source,List<TeamPost> posts,DateTimeOffset now,RadarRules rules){
        var output=new List<Announcement>();var anchors=new List<TeamPost>();
        foreach(var post in posts.Where(x=>IsTiboOriginal(x.Original)).OrderBy(x=>x.Published??DateTimeOffset.MinValue)){
            bool containsReset=Match(post.Quote,@"\breset(?:s|ting|ted)?\b").Success;
            TeamPost? parent=null;
            if(!containsReset&&post.Published!=null&&Match(post.Quote,@"^\s*(?:Lands?\b|Landing\b|Done\b)").Success)
                parent=anchors.LastOrDefault(x=>x.Published!=null&&post.Published>=x.Published&&post.Published-x.Published<=TimeSpan.FromMinutes(30));
            var item=TeamCandidate(source,post.Url,post.StableId,post.Title,post.Quote,post.Published,post.Original,now,rules,post.Category,parent?.Quote??"",parent?.Original);
            if(item!=null)output.Add(item);
            if(containsReset)anchors.Add(post);
        }
        return output.GroupBy(x=>x.EventId).Select(x=>MergeEvidence(x.Last(),x.Take(x.Count()-1))).ToList();
    }

    internal static DateTimeOffset? SnowflakeTime(string url){
        var m=Match(url,@"/status/(?<id>\d+)");if(!m.Success||!ulong.TryParse(m.Groups["id"].Value,NumberStyles.None,CultureInfo.InvariantCulture,out var id))return null;
        try{return DateTimeOffset.FromUnixTimeMilliseconds(checked((long)(id>>22)+1288834974657L));}catch{return null;}
    }

    static Announcement? OfficialCandidate(RadarSourceDefinition source,string url,string guid,string title,string body,DateTimeOffset? published,DateTimeOffset now)
    {
        string key=guid!=""?"guid:"+guid:"permalink:"+AnnouncementParser.CanonicalUrl(url);
        var value=AnnouncementParser.Candidate(source.Id,url,source.Tier,key,title,body,published,now);
        if(value==null)return null;
        return Classify(value with{SourceClass=source.SourceClass,Author="OpenAI",OriginalSourceUrl=url,CanonicalOriginalId=guid!=""?guid:AnnouncementParser.CanonicalUrl(url),OriginalQuote=value.Summary,
            IdentityConfidence=guid!=""?"FEED_GUID":"CANONICAL_PERMALINK",EvidenceSources=[new(source.Id,source.SourceClass,url,"ORIGINAL",value.ContentHash)]});
    }

    static Announcement? StatusCandidate(RadarSourceDefinition source,string url,string guid,string title,string body,DateTimeOffset? published,DateTimeOffset now)
    {
        string text=(title+" "+body).Trim();
        if(!Match(text,@"\b(?:Codex|usage|quota|rate[- ]?limit)\b").Success||!Match(text,@"\breset(?:s|ting)?\b").Success)return null;
        if(published==null||published>now.AddMinutes(10)||now-published>TimeSpan.FromHours(72))return null;
        string canonical=guid!=""?guid:AnnouncementParser.CanonicalUrl(url);string content=Safe.Hash(AnnouncementParser.Clean(text));
        return new(Safe.Hash("original|"+canonical),Safe.Hash("status|"+canonical),source.Id,url,1,"HIGH",AnnouncementParser.Clean(title,160),AnnouncementParser.Clean(text),published,published.Value.ToString("yyyy-MM-dd"),null,"UNKNOWN","UNKNOWN","UNKNOWN","STATUS_SIGNAL","OBSERVED",content,now,now,
            SourceClass:"OPENAI_STATUS",Author:"OpenAI",OriginalSourceUrl:url,CanonicalOriginalId:canonical,OriginalQuote:AnnouncementParser.Clean(text,1200),SignalLevel:"SUPPORTING_CONTEXT",SignalReason:"服務狀態事件僅作為營運狀況的輔助證據，不視為即將全球重置。",EvidenceSources:[new(source.Id,source.SourceClass,url,"ORIGINAL",content)]);
    }

    static Announcement? TeamCandidate(RadarSourceDefinition source,string relayItemUrl,string stableId,string title,string quote,DateTimeOffset? published,string originalUrl,DateTimeOffset now,RadarRules rules,string category="",string context="",string? threadOriginal=null)
    {
        if(!IsTiboOriginal(originalUrl)||quote.Length<4)return null;
        string semantic=AnnouncementParser.Semantic(quote);string scoped=AnnouncementParser.Semantic(context+" "+quote);
        bool classified=category.Split('|').Any(x=>x.Trim() is "Reset Planned" or "Reset Completed" or "Reset cards");
        if(!classified&&!Match(scoped,@"\b(?:Codex|ChatGPT Work|usage|quota|reset)\b").Success)return null;
        bool resetRelated=classified||Match(scoped,@"\breset(?:s|ting|ted)?\b").Success;
        if(!resetRelated)return null;
        if(published!=null&&(published>now.AddMinutes(10)||now-published>TimeSpan.FromHours(72)))return null;
        string canonical=CanonicalOriginalId(threadOriginal??originalUrl);if(canonical=="")canonical=stableId;
        string effect=AnnouncementParser.EffectOf(scoped);
        string categorySignal=category.Split('|').Select(x=>x.Trim() switch{"Reset cards"=>"BANKED","Reset Completed"=>"COMPLETED","Reset Planned"=>"INCOMING",_=>""}).FirstOrDefault(x=>x!="")??"";
        bool credit=categorySignal!=""?categorySignal=="BANKED":effect=="BANKED_CREDIT_GRANT"||RadarRules.Any(semantic,rules.BankedPatterns);
        bool forecast=Match(semantic,@"\b\d{1,3}\s*%\s*(?:chance|probability|odds)|\bforecast\b").Success;
        if(forecast)return null; // A relay forecast is recorded by its publisher, never promoted into product evidence.
        if(!classified&&Match(semantic,@"\breset\s+(?:documentation|instructions?|guidance|examples?|information)\b").Success)return null;
        if(!classified&&Match(semantic,@"\b(?:will\s+not|won't|not\s+going\s+to|no\s+plans?\s+to)\s+(?:\w+\s+){0,2}reset\b|\bno\s+(?:global\s+|Codex\s+|usage\s+)?reset\b").Success)return null;
        bool completed=categorySignal!=""?categorySignal=="COMPLETED":RadarRules.Any(semantic,rules.CompletedPatterns)||AnnouncementParser.PhaseOf(semantic,credit?"BANKED_CREDIT_GRANT":"AUTOMATIC_QUOTA_RESET",null,now)=="COMPLETED";
        bool incoming=categorySignal!=""?categorySignal=="INCOMING":!completed&&RadarRules.Any(semantic,rules.IncomingPatterns);
        bool vague=Match(semantic,@"\b(?:occasional\s+reset|includes?[^.!?]{0,50}\breset|reset\s+button|reset\s+company|hold\s+on\s+to\s+your\s+Codex|possible\s+reset)\b").Success;
        string signal=credit?"BANKED":completed?"COMPLETED":vague&&!classified?"WATCH":incoming?"INCOMING":"NONE";
        if(signal=="NONE"&&!credit)return null;
        var time=AnnouncementParser.Time(semantic,published);string phase=credit?AnnouncementParser.PhaseOf(semantic,"BANKED_CREDIT_GRANT",time.utc,now):completed?"COMPLETED":incoming?AnnouncementParser.PhaseOf(semantic,"AUTOMATIC_QUOTA_RESET",time.utc,now):"PLANNED";
        if(incoming||completed)effect="AUTOMATIC_QUOTA_RESET";if(credit)effect="BANKED_CREDIT_GRANT";
        string scope=Scope(scoped);string hash=Safe.Hash(AnnouncementParser.Clean(scoped));string relay=source.Url;
        string reason=classified?"這是 relay 的分類："+category+"；原文另行保留。":signal switch{"WATCH"=>"可追溯的 Codex 團隊重置消息，尚未確認執行時間。","INCOMING"=>"Codex 團隊原文或同一討論串回覆提到即將重置。","COMPLETED"=>"Codex 團隊原文表示重置已完成。",_=>"保留重置券消息，不觸發自動重置提醒。"};
        return new(Safe.Hash("original|"+canonical),Safe.Hash(signal+"|"+effect+"|"+scope+"|"+phase+"|"+time.utc),source.Id,relayItemUrl,source.Tier,signal is "INCOMING" or "COMPLETED"?"HIGH":"MEDIUM",AnnouncementParser.Clean(title,160),AnnouncementParser.Clean(quote),published,published?.ToString("yyyy-MM-dd"),time.utc,time.zone,time.quality,scope,credit?"BANKED":"QUOTA_RESET",phase,hash,now,now,
            Eligibility:"AS_ANNOUNCED",IdentityConfidence:"CANONICAL_ORIGINAL",Effect:effect,OriginalId:canonical,PublicationBasis:SnowflakeTime(originalUrl)!=null?"X_STATUS_ID":published!=null?"RELAY_PUBLISHED":"RELAY_RELATIVE_TIME",SourceClass:"TEAM_SIGNAL_RELAY",Author:"Tibo / thsottiaux",OriginalSourceUrl:originalUrl,RelayUrl:relay,CanonicalOriginalId:canonical,OriginalQuote:AnnouncementParser.Clean(quote,1200),RelayForecast:forecast?AnnouncementParser.Clean(quote,240):"",SignalLevel:signal,SignalReason:reason,EvidenceSources:[new(source.Id,source.SourceClass,relay,"RELAY",hash),new("original-x",source.SourceClass,originalUrl,"ORIGINAL",Safe.Hash(quote))],RelayCategory:category);
    }

    internal static Announcement Classify(Announcement value)
    {
        string semantic=AnnouncementParser.Semantic(value.Title+" "+value.Summary);
        bool explicitAction=value.Phase is "STARTED" or "COMPLETED" or "CANCELLED"||
            Match(semantic,@"\b(?:scheduled|planned|upcoming)\b[^.!?]{0,100}\breset\b|\breset\b[^.!?]{0,100}\b(?:will\s+(?:begin|reset|take\s+place|be\s+applied)|is\s+scheduled|is\s+planned|is\s+upcoming)\b|\bwill\s+(?:apply|perform|do)\s+(?:a\s+)?(?:Codex\s+)?(?:global\s+)?reset\b").Success;
        string signal=value.SourceClass switch
        {
             "OPENAI_STATUS"=>"SUPPORTING_CONTEXT",
            "COMMUNITY_TRACKER"=>"NONE",
            _ when value.Effect!="AUTOMATIC_QUOTA_RESET"=>"NONE",
             _ when value.Confidence!="HIGH"||!explicitAction=>"NONE",
             _ when value.Phase=="CANCELLED"=>"NOTICE",
            _ when value.Phase=="COMPLETED"=>"COMPLETED",
            _ when value.Phase is "PLANNED" or "STARTED"=>"INCOMING",
            _=>"NONE"
        };
        string reason=signal switch{"INCOMING"=>"OpenAI 官方來源明確提到即將或已開始自動重置。","COMPLETED"=>"OpenAI 官方來源表示自動重置已完成。","NOTICE"=>"先前的自動重置公告已取消或有重大更動。","SUPPORTING_CONTEXT"=>"營運狀態僅作為輔助資訊。",_=>"未符合重置提醒條件。"};
        return value with{SignalLevel=signal,SignalReason=reason};
    }

    internal static int SignalPriority(string value)=>value switch{"INCOMING"=>4,"COMPLETED" or "NOTICE"=>3,"WATCH"=>2,"SUPPORTING_CONTEXT"=>1,_=>0};
    internal static bool IsAlert(Announcement value)=>value.SignalLevel is "WATCH" or "INCOMING" or "COMPLETED" or "NOTICE";
    internal static bool CurrentHealthy(SourceStatus value,DateTimeOffset now)=>(value.Status is "HEALTHY" or "UNCHANGED")&&value.LastSuccess>=now.AddHours(-4)&&value.LastSuccess<=now.AddMinutes(10);
    internal static IEnumerable<SourceStatus> LatestConfiguredSources(RadarState state){foreach(var definition in ResetRadar.Sources){var value=state.Sources.Where(x=>x.Id==definition.Id).OrderByDescending(x=>x.LastAttempt??DateTimeOffset.MinValue).FirstOrDefault();if(value!=null)yield return value;}}
    internal static int HealthyConfiguredCount(RadarState state,DateTimeOffset now)=>LatestConfiguredSources(state).Count(x=>CurrentHealthy(x,now));
    internal static int FailedConfiguredCount(RadarState state)=>LatestConfiguredSources(state).Count(x=>!RadarCoverage.PermanentlyBlocked(x)&&(x.Failures>0||x.Status is "DEGRADED" or "BLOCKED"));
    internal static string Coverage(RadarState state,bool enabled,DateTimeOffset? now=null)
    {
        if(!enabled)return "OFF";var clock=now??DateTimeOffset.UtcNow;if(RadarCoverage.Measure(state,enabled,clock).coverage_percent==null)return "LIMITED";
        var healthyIds=LatestConfiguredSources(state).Where(x=>RadarCoverage.Readable(x,ResetRadar.Sources.Single(d=>d.Id==x.Id),clock)).Select(x=>x.Id).ToHashSet();int healthy=healthyIds.Count;
        bool official=ResetRadar.Sources.Any(x=>x.SourceClass=="OFFICIAL_OPENAI"&&healthyIds.Contains(x.Id));bool relay=ResetRadar.Sources.Any(x=>x.SourceClass=="TEAM_SIGNAL_RELAY"&&healthyIds.Contains(x.Id));
        return healthy==RadarCoverage.Measure(state,enabled,clock).enabled_sources?"GOOD":official&&relay?"PARTIAL":healthy>0?"LIMITED":"OFF";
    }

    internal static string PreferredUrl(Announcement value)=>value.OriginalSourceUrl!=""?value.OriginalSourceUrl:value.RelayUrl!=""?value.RelayUrl:value.Url;
    internal static bool CanOpenHumanUrl(string value)=>Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme==Uri.UriSchemeHttps&&uri.UserInfo==""&&!uri.IsLoopback&&!System.Net.IPAddress.TryParse(uri.DnsSafeHost,out _);
    internal static string SourceLabel(Announcement value,string language)=>value.SourceClass switch
    {
        "OFFICIAL_OPENAI"=>language=="zh-TW"?"OpenAI 官方來源":"Official OpenAI",
        "OPENAI_STATUS"=>"OpenAI Status",
        "TEAM_SIGNAL_RELAY"=>language=="zh-TW"?"Codex 團隊訊號（社群轉送）":"Codex team signal via community relay",
        _=>language=="zh-TW"?"社群追蹤器":"Community tracker"
    };

    static Announcement MergeEvidence(Announcement first,IEnumerable<Announcement> rest)
    {
        var evidence=(first.EvidenceSources??[]).Concat(rest.SelectMany(x=>x.EvidenceSources??[])).DistinctBy(x=>(x.SourceId,x.Url,x.Role)).ToList();
        return first with{EvidenceSources=evidence};
    }
    internal static List<RadarEvidenceSource> MergeEvidence(Announcement old,Announcement next)
        =>(old.EvidenceSources??[]).Concat(next.EvidenceSources??[]).DistinctBy(x=>(x.SourceId,x.Url,x.Role)).ToList();

    // Aliases are accepted only for the verified Tibo relay adapter. A thread can
    // carry the parent's identity in one relay and the reply's identity in another.
    internal static HashSet<string> VerifiedOriginalAliases(Announcement value){
        var aliases=new HashSet<string>(StringComparer.Ordinal);
        if(value.SourceClass!="TEAM_SIGNAL_RELAY"||value.Author!="Tibo / thsottiaux"||!IsTiboOriginal(value.OriginalSourceUrl))return aliases;
        void Add(string id){if(Match(id,@"^x:\d+$").Success)aliases.Add(id);}
        Add(value.CanonicalOriginalId);Add(CanonicalOriginalId(value.OriginalSourceUrl));
        foreach(var source in value.EvidenceSources??[])if(source.Role=="ORIGINAL"&&IsTiboOriginal(source.Url))Add(CanonicalOriginalId(source.Url));
        foreach(string alias in value.OriginalAliases??[])Add(alias);
        return aliases;
    }

    static string ExtractOriginalQuote(XElement unit,string fallback)
    {
        foreach(var element in unit.DescendantsAndSelf())
        {
            if(element.Name.LocalName is "source_text" or "originalQuote" or "quote")return AnnouncementParser.Plain(element.Value);
        }
        string text=AnnouncementParser.Plain(fallback);
        var labeled=Match(text,@"(?:Source text|Original quote|Quote)\s*[:：]\s*(.+)");return labeled.Success?labeled.Groups[1].Value.Trim():text;
    }
    static string FirstOriginalUrl(string raw,string fallback)
    {
        var m=Match(WebUtility.HtmlDecode(raw),@"https://(?:www\.)?x\.com/thsottiaux/status/\d+");
        if(m.Success)return m.Value;return IsTiboOriginal(fallback)?fallback:"";
    }
    static bool IsTiboOriginal(string url)=>Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.DnsSafeHost is "x.com" or "www.x.com"&&Match(uri.AbsolutePath,@"^/thsottiaux/status/\d+/?$").Success;
    internal static string CanonicalOriginalId(string url){var m=Match(url,@"https://(?:www\.)?x\.com/thsottiaux/status/(?<id>\d+)");return m.Success?"x:"+m.Groups["id"].Value:AnnouncementParser.CanonicalUrl(url);}
    static string SafePublicUrl(string candidate,string fallback)=>Uri.TryCreate(candidate,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.UserInfo==""?uri.AbsoluteUri:fallback;
    static DateTimeOffset? RelativeTime(string value,DateTimeOffset now){var m=Match(value,@"(?<n>\d+)\s*(?<u>m|h|d)\s+ago");if(!m.Success)return null;int n=int.Parse(m.Groups["n"].Value,CultureInfo.InvariantCulture);return now-(m.Groups["u"].Value.ToLowerInvariant() switch{"m"=>TimeSpan.FromMinutes(n),"h"=>TimeSpan.FromHours(n),_=>TimeSpan.FromDays(n)});}
    static string Scope(string body){var values=new List<string>();foreach(string plan in new[]{"Plus","Pro","Business","Enterprise"})if(Match(body,@"\b"+plan+@"\b").Success)values.Add(plan.ToUpperInvariant());if(values.Count>0)return string.Join('|',values);if(Match(body,@"\ball paid (?:users|plans|subscriptions)\b").Success)return "PAID";if(Match(body,@"\b(?:all|every) (?:Codex|ChatGPT Work|users?)\b").Success)return "ALL";return "UNKNOWN";}
}

