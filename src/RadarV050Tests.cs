using System.Net;
using System.Text.Json;
namespace CodexUsageMonitor;

internal static class RadarV050Tests
{
    sealed record Fixture(string id,string published,string quote,string expected,string category="",string context="",string? resetUtc=null,string? timeQuality=null);
    internal static List<object> Run(string root,string? fixturePath=null){
        Directory.CreateDirectory(root);var results=new List<object>();
        void Assert(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
        void Check(string id,Action action){try{action();results.Add(new{id,result="PASS"});}catch(Exception ex){results.Add(new{id,result="FAIL",error=ex.Message});}}
        string Id(DateTimeOffset published)=>(((published.ToUnixTimeMilliseconds()-1288834974657L)<<22)+1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        string HtmlPost(string quote,DateTimeOffset published,string author="thsottiaux")=>$"<li class=\"feed-item\"><p class=\"feed-text\">{WebUtility.HtmlEncode(quote)}</p><a href=\"https://x.com/{author}/status/{Id(published)}\">原文</a><a class=\"feed-time\">1h ago</a></li>";
        var htmlSource=ResetRadar.Sources.Single(x=>x.Id=="team-codex-reset");var rssSource=ResetRadar.Sources.Single(x=>x.Id=="team-modelyard-rss");
        fixturePath??=Path.Combine(AppContext.BaseDirectory,"fixtures","radar-a1.json");
        var fixtures=JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(fixturePath))!;
        foreach(var fixture in fixtures)Check(fixture.id,()=>{
            var published=DateTimeOffset.Parse(fixture.published);List<Announcement> items;
            if(fixture.category!=""){
                string feed=$"<rss><channel><item><title>Codex 團隊消息</title><link>https://x.com/thsottiaux/status/{Id(published)}</link><pubDate>{published:R}</pubDate><category>{fixture.category}</category><description>{WebUtility.HtmlEncode(fixture.quote)}</description></item></channel></rss>";
                items=Rc4Radar.Parse(rssSource,feed,published.AddMinutes(5),out _);
            }else{
                string html="<ul>"+HtmlPost(fixture.quote,published)+(fixture.context!=""?HtmlPost(fixture.context,published.AddMinutes(-1)):"")+"</ul>";
                items=Rc4Radar.Parse(htmlSource,html,published.AddMinutes(5),out _);
            }
            var item=items.Single();Assert(item.SignalLevel==fixture.expected,"分類："+item.SignalLevel);Assert(item.PublishedAt==published,"發文時間");
            if(fixture.resetUtc!=null)Assert(item.ResetUtc==DateTimeOffset.Parse(fixture.resetUtc),"重置時間："+item.ResetUtc);
            if(fixture.timeQuality!=null)Assert(item.TimeQuality==fixture.timeQuality,"時間品質");
        });
        var now=DateTimeOffset.Parse("2026-10-22T07:00:00Z");
        Check("Snowflake 真實貼文時間",()=>Assert(Rc4Radar.SnowflakeTime("https://x.com/thsottiaux/status/2113162413008884099")?.ToString("yyyy-MM-ddTHH:mm:ssZ")=="2026-10-22T06:55:54Z","Snowflake 換算"));
        Check("同作者 30 分鐘內合併",()=>{
            string html="<ul>"+HtmlPost("Lands tomorrow before 10am PST.",now.AddMinutes(-1))+HtmlPost("Codex reset day today.",now.AddMinutes(-2))+"</ul>";
            Assert(Rc4Radar.Parse(htmlSource,html,now,out _).Count==1,"同一事件");
        });
        Check("不同作者與超時不合併",()=>{
            string html="<ul>"+HtmlPost("Lands tomorrow before 10am PST.",now.AddMinutes(-1))+HtmlPost("Codex reset day today.",now.AddMinutes(-32))+HtmlPost("Codex reset incoming.",now.AddMinutes(-2),"somebodyelse")+"</ul>";
            Assert(Rc4Radar.Parse(htmlSource,html,now,out _).Count==1,"回覆應被略過，僅保留原文");
        });
        Check("Reset cards 類別保留 BANKED",()=>{
            string feed=$"<rss><channel><item><title>Cards</title><link>https://x.com/thsottiaux/status/{Id(now)}</link><category>Reset cards</category><description>Here are the cards.</description></item></channel></rss>";
            Assert(Rc4Radar.Parse(rssSource,feed,now,out _).Single().SignalLevel=="BANKED","BANKED");
        });
        Check("PST PDT PT 一律當地時區",()=>{
            foreach(string tz in new[]{"PST","PDT","PT"}){
                Assert(AnnouncementParser.Time($"October 22, 2026 before 10am {tz}",null).utc==DateTimeOffset.Parse("2026-10-22T17:00:00Z"),tz+" 夏令");
                Assert(AnnouncementParser.Time($"January 22, 2026 before 10am {tz}",null).utc==DateTimeOffset.Parse("2026-01-22T18:00:00Z"),tz+" 冬令");
                Assert(AnnouncementParser.Time($"November 1, 2026 at 1:30am {tz}",null).utc==null,tz+" 模糊");
            }
            Assert(AnnouncementParser.Time("Global reset by EOD PT",now).utc==DateTimeOffset.Parse("2026-10-23T06:59:00Z"),"EOD");
        });
        Check("詞庫可設定",()=>{
            var rules=new RadarRules{IncomingPatterns=[@"\breset\s+bananas\b"]};
            string html="<ul>"+HtmlPost("Codex reset bananas.",now)+"</ul>";
            Assert(Rc4Radar.Parse(htmlSource,html,now,out _,rules).Single().SignalLevel=="INCOMING","可自訂");
        });
        Check("403 不計入分母",()=>{
            var state=new RadarState{Sources=ResetRadar.Sources.Select(d=>new SourceStatus(d.Id,d.Url,Status:"HEALTHY",LastAttempt:now,LastSuccess:now,HttpStatus:200)).ToList()};
            state.Sources[0]=state.Sources[0] with{Status="BLOCKED",HttpStatus=403,Failures=3};
            var point=RadarCoverage.Measure(state,true,now);Assert(point.enabled_sources==5&&point.coverage_percent==100,"被封鎖來源移出分母");
        });
        Check("每日 JSONL 遷移與重啟去重",()=>{
            string folder=Path.Combine(root,"journal");Directory.CreateDirectory(folder);var check=new RadarSourceCheck(DateTimeOffset.UtcNow,"fixture","TEAM_SIGNAL_RELAY","HEALTHY",1,200,"hash",DateTimeOffset.UtcNow,null,"");
            AtomicJson.Save(Path.Combine(folder,"reset_radar.json"),new RadarState{SourceChecks=[check]});
            using(var radar=new ResetRadar(folder)){radar.Apply([],now);radar.Apply([],now);}
            using(var radar=new ResetRadar(folder)){Assert(radar.Snapshot().SourceChecks.Count==1,"重啟紀錄");radar.Apply([],now);}
            var json=File.ReadAllText(Path.Combine(folder,"reset_radar.json"));Assert(!json.Contains("SourceChecks"),"主 JSON 已移出採樣陣列");
            Assert(Directory.GetFiles(Path.Combine(folder,"radar_history"),"source_checks_*.jsonl").Sum(p=>File.ReadLines(p).Count())==1,"只追加一次");
        });
        Check("INCOMING 五分鐘模式直到完成或額度補滿",()=>{
            string folder=Path.Combine(root,"accelerate");using var radar=new ResetRadar(folder);
            var incoming=Rc4Radar.Parse(htmlSource,"<ul>"+HtmlPost("Codex reset incoming.",now)+"</ul>",now,out _).Single();
            radar.Apply([incoming],now);Assert(radar.ShouldAccelerate(htmlSource)&&radar.ShouldAccelerate(htmlSource,[]),"應加速，短暫缺少公告仍保留");
            radar.LocalQuotaIsFull=()=>true;Assert(!radar.ShouldAccelerate(htmlSource),"補滿停止");radar.LocalQuotaIsFull=null;Assert(!radar.ShouldAccelerate(htmlSource),"同一公告不得再次加速");
            radar.Apply([incoming with{SignalLevel="COMPLETED",Phase="COMPLETED",PublishedAt=now.AddMinutes(1)}],now.AddMinutes(1));Assert(!radar.ShouldAccelerate(htmlSource),"完成停止");
        });
        Check("真實 relay HTML 捕捉 Day 17 與回覆時間",()=>{
            string path=Path.Combine(Path.GetDirectoryName(fixturePath)!,"tibo-live-20261022.html");
            var parsed=Rc4Radar.Parse(htmlSource,File.ReadAllText(path),DateTimeOffset.Parse("2026-10-22T15:00:00Z"),out int candidates);
            Assert(candidates==10,"候選數："+candidates);var item=parsed.Single(x=>x.OriginalSourceUrl.EndsWith("2113162413008884099"));
            Assert(item.SignalLevel=="INCOMING"&&item.ResetUtc==DateTimeOffset.Parse("2026-10-22T17:00:00Z")&&item.TimeQuality=="DEADLINE","真實資料分類與時間");
        });
        Check("否定語句不升成即將重置",()=>{
            string html="<ul>"+HtmlPost("Codex will not reset today.",now)+"</ul>";Assert(Rc4Radar.Parse(htmlSource,html,now,out _).Count==0,"否定語句");
        });
        Check("連續三次403才標為網站封鎖",()=>{
            using var radar=new ResetRadar(Path.Combine(root,"blocked"),new ForbiddenHandler());
            radar.Poll(true,CancellationToken.None).GetAwaiter().GetResult();Assert(radar.Snapshot().Sources.All(x=>x.Status=="DEGRADED"),"第一次仍退避");
            radar.Poll(true,CancellationToken.None).GetAwaiter().GetResult();radar.Poll(true,CancellationToken.None).GetAwaiter().GetResult();
            Assert(radar.Snapshot().Sources.All(x=>x.Status=="BLOCKED"&&x.Consecutive403==3),"第三次封鎖");
            Assert(RadarCoverage.Measure(radar.Snapshot(),true,DateTimeOffset.UtcNow).enabled_sources==0,"移出分母");
        });
        Check("舊版403有連續證據才立即回填封鎖",()=>{
            string folder=Path.Combine(root,"blocked-migration");var clock=DateTimeOffset.UtcNow;var a=ResetRadar.Sources[0];var b=ResetRadar.Sources[1];
            var state=new RadarState{Sources=[new(a.Id,a.Url,Status:"DEGRADED",LastAttempt:clock,Failures:99,HttpStatus:403,DisplayName:"Legacy English Name"),new(b.Id,b.Url,Status:"DEGRADED",LastAttempt:clock,Failures:99,HttpStatus:403)],SourceChecks=[]};
            foreach(int i in new[]{3,2,1})state.SourceChecks.Add(new(clock.AddMinutes(-i),a.Id,a.SourceClass,"DEGRADED",0,403,"",null,null,"HTTP_403"));
            foreach(int i in new[]{3,2,1})state.SourceChecks.Add(new(clock.AddMinutes(-i),b.Id,b.SourceClass,"DEGRADED",0,i==2?0:403,"",null,null,i==2?"TIMEOUT":"HTTP_403"));
            AtomicJson.Save(Path.Combine(folder,"reset_radar.json"),state);using var radar=new ResetRadar(folder);var loaded=radar.Snapshot();
            var migrated=loaded.Sources.Single(s=>s.Id==a.Id);Assert(migrated.Status=="BLOCKED"&&migrated.Consecutive403==3&&migrated.NextCheck>clock.AddHours(23),"連續證據應回填");
            Assert(migrated.DisplayName==a.DisplayName&&!migrated.DisplayName.Contains("Legacy"),"沿用繁中名稱");
            Assert(loaded.Sources.Single(s=>s.Id==b.Id).Status=="DEGRADED","累計失敗99次但不連續，不可直接封鎖");
        });
        Check("新Radar日誌只清理七日前已辨識的JSONL",()=>{
            string folder=Path.Combine(root,"journal-retention");string journalFolder=Path.Combine(folder,"radar_history");Directory.CreateDirectory(journalFolder);
            File.WriteAllText(Path.Combine(journalFolder,"coverage_2026-10-15.jsonl"),"{}\n");File.WriteAllText(Path.Combine(journalFolder,"source_checks_2026-10-16.jsonl"),"{}\n");
            string csv=Path.Combine(journalFolder,"usage_2026-10-15.csv");File.WriteAllText(csv,"原始CSV保留");string foreign=Path.Combine(journalFolder,"coverage_backup.jsonl");File.WriteAllText(foreign,"保留未知檔名");
            new RadarJournal(folder).Cleanup(DateTimeOffset.Parse("2026-10-22T15:00:00Z"));
            Assert(!File.Exists(Path.Combine(journalFolder,"coverage_2026-10-15.jsonl")),"清理超過七日");Assert(File.Exists(Path.Combine(journalFolder,"source_checks_2026-10-16.jsonl")),"保留最近七個UTC日期");
            Assert(File.ReadAllText(csv)=="原始CSV保留"&&File.Exists(foreign),"CSV與未知檔案不可刪");
        });
        results.AddRange(RadarForecast050Tests.Run(Path.Combine(root,"forecast-integration")));
        AtomicJson.Save(Path.Combine(root,"radar-v050-tests.json"),new{total=results.Count,results});return results;
    }
    sealed class ForbiddenHandler:HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));}
}
