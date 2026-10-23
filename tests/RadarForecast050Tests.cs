namespace CodexUsageMonitor;

internal static class RadarForecast050Tests
{
    internal static List<object> Run(string root){
        Directory.CreateDirectory(root);var results=new List<object>();
        void Check(string name,Action action){try{action();results.Add(new{id=name,result="PASS"});}catch(Exception ex){results.Add(new{id=name,result="FAIL",error=ex.ToString()});}}
        void Assert(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        var now=DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var htmlSource=ResetRadar.Sources.Single(x=>x.Id=="team-codex-reset");var rssSource=ResetRadar.Sources.Single(x=>x.Id=="team-modelyard-rss");
        string Url(string marker,DateTimeOffset time)=>"https://x.com/thsottiaux/status/"+RadarTestIds.Id(marker,time);
        string Post(string marker,DateTimeOffset time,string quote)=>$"<li class=\"feed-item\"><a href=\"{Url(marker,time)}\">原文</a><p class=\"feed-text\">{System.Net.WebUtility.HtmlEncode(quote)}</p></li>";
        string Feed(string category,string quote)=>$"<rss><channel><item><title>Codex 團隊消息</title><link>{Url("reply",now.AddMinutes(-9))}</link><category>{category}</category><description>{System.Net.WebUtility.HtmlEncode(quote)}</description></item></channel></rss>";
        const string reply="Lands within the next 2 hours.";
        string html="<ul>"+Post("reply",now.AddMinutes(-9),reply)+Post("parent",now.AddMinutes(-10),"Codex reset day today.")+"</ul>";
        string completedHtml="<ul>"+Post("done",now.AddMinutes(-8),"Done.")+Post("reply",now.AddMinutes(-9),reply)+Post("parent",now.AddMinutes(-10),"Codex reset day today.")+"</ul>";
        var incoming=Rc4Radar.Parse(htmlSource,html,now,out _).Single();
        var rssIncoming=Rc4Radar.Parse(rssSource,Feed("Reset Planned",reply),now,out _).Single();
        var completed=Rc4Radar.Parse(htmlSource,completedHtml,now,out _).Single();
        Snapshot Snapshot()=>new(now,now,0,"fixture-account","ACCOUNT_STABLE","fixture-generation","fixture",[new("codex","Codex","primary",10080,61,now.AddDays(5).ToUnixTimeSeconds(),"VALID","pro",null,null,null,"")],2,[now.AddDays(2).ToUnixTimeSeconds()]);
        MonitorService Service(string name){string folder=Path.Combine(root,name);AtomicJson.Save(Path.Combine(folder,"last_good.json"),Snapshot());return new(folder);}
        Check("UNKNOWN範圍Tibo仍可進入公告預測",()=>{
            using var service=Service("unknown");service.Radar.Apply([incoming],now);service.RebuildForecast(now);
            Assert(incoming.Scope=="UNKNOWN"&&service.ForecastAnnouncement!=null&&service.Forecast?.Announced!=null,"UNKNOWN Tibo公告被錯誤拒收");
        });
        Check("短Done回覆在已證實討論串內成為完成",()=>{
            Assert(completed.SignalLevel=="COMPLETED"&&completed.OriginalQuote=="Done."&&completed.CanonicalOriginalId==incoming.CanonicalOriginalId,"短Done應沿用父貼文");
        });
        foreach(bool reverse in new[]{false,true})Check("跨來源父貼文與回覆合併，順序="+reverse,()=>{
            using var service=Service("alias-"+reverse);var first=reverse?rssIncoming:incoming;var second=reverse?incoming:rssIncoming;
            service.Radar.Apply([first],now);service.Radar.Read(first.EventId);var stamp=service.Radar.Snapshot().Events.Single().ReadAt;
            service.Radar.Apply([second],now);var state=service.Radar.Snapshot();var combined=state.Events.Single();
            Assert(combined.EventId==first.EventId&&combined.ReadAt==stamp&&combined.OriginalAliases?.Count==2,"原EventId與ReadAt應保留且兩個原文ID成為alias");
            Assert(service.Radar.ShouldAccelerate(htmlSource)&&service.Radar.ShouldAccelerate(rssSource),"同一事件兩來源都應加速");
            service.Radar.Apply([completed],now.AddSeconds(1));service.RebuildForecast(now.AddSeconds(1));
            Assert(service.ForecastAnnouncement==null&&service.Forecast?.Announced==null,"完成後不得繼續公告倒數");
            Assert(!service.Radar.ShouldAccelerate(htmlSource)&&!service.Radar.ShouldAccelerate(rssSource,[rssIncoming]),"完成後即使另一來源仍舊incoming，也不得加速");
            service.Radar.Apply([rssIncoming],now.AddSeconds(2));Assert(service.Radar.Snapshot().Events.Single().Phase=="COMPLETED","舊RSS不得使終態回退");
        });
        Check("舊重複事件保留歷史但撤除所有倒數",()=>{
            string folder=Path.Combine(root,"legacy-alias");var older=incoming with{FirstSeen=now.AddMinutes(-12)};var read=now.AddMinutes(-3);var duplicate=rssIncoming with{FirstSeen=now.AddMinutes(-11),ReadAt=read};
            AtomicJson.Save(Path.Combine(folder,"reset_radar.json"),new RadarState{Events=[older,duplicate]});
            using(var radar=new ResetRadar(folder)){
                radar.Apply([completed],now);var state=radar.Snapshot();Assert(state.Events.Count==2,"歷史ID不可直接刪除");
                var prior=state.Events.Single(x=>x.EventId==duplicate.EventId);Assert(prior.ReadAt==read&&prior.SupersededByEventId==older.EventId,"舊ReadAt與合併指向應保存");
                Assert(radar.ActiveSignalsAt(now).Count==0&&!radar.ShouldAccelerate(rssSource,[rssIncoming]),"別名重複的incoming仍被當有效");
            }
            using var reopened=new ResetRadar(folder);Assert(reopened.ActiveSignalsAt(now).Count==0&&!reopened.ShouldAccelerate(rssSource,[rssIncoming]),"重開後必須保留完成與alias");
            using var service=new MonitorService(folder);using var page=new ResetInfoForm(service,service.Radar.Snapshot().Events.Single(x=>x.EventId==duplicate.EventId));
            Assert(page.DisplayedEventId==older.EventId&&page.DisplayedSignalLevel=="COMPLETED","重置資訊必須跟隨Superseded指向主事件");
        });
        Check("同一原文category由Planned更正Completed仍可完成",()=>{
            using var radar=new ResetRadar(Path.Combine(root,"category-revision"));radar.Apply([rssIncoming],now);
            var fixedCategory=Rc4Radar.Parse(rssSource,Feed("Reset Completed",reply),now,out _).Single();
            Assert(fixedCategory.PublishedAt==rssIncoming.PublishedAt,"測試必須是相同X發文時間");radar.Apply([fixedCategory],now);
            Assert(radar.Snapshot().Events.Single().Phase=="COMPLETED"&&radar.ActiveSignalsAt(now).Count==0,"結構化完成分類被當較弱資料丟棄");
        });
        Check("無alias的近時貼文不會合併或互相取消加速",()=>{
            using var radar=new ResetRadar(Path.Combine(root,"distinct-posts"));radar.Apply([incoming],now);
            var another=rssIncoming with{EventId="different-event",CanonicalOriginalId="x:"+RadarTestIds.Id("different",now),OriginalId="x:"+RadarTestIds.Id("different",now),OriginalSourceUrl=Url("different",now),EvidenceSources=[new(rssSource.Id,"TEAM_SIGNAL_RELAY",rssSource.Url,"RELAY"),new("original-x","TEAM_SIGNAL_RELAY",Url("different",now),"ORIGINAL")],SignalLevel="COMPLETED",Phase="COMPLETED",PublishedAt=now};
            radar.Apply([another],now);Assert(radar.Snapshot().Events.Count==2&&radar.ActiveSignalsAt(now).Count==1,"不得只因時間接近合併");
            Assert(radar.ShouldAccelerate(htmlSource,[another with{Source=htmlSource.Id}]),"另一事件完成不得取消此事件的加速");
        });
        Check("額度未驗證時Radar完成仍撤除浮窗公告倒數",()=>{
            using var service=Service("unverified-completion");service.Radar.Apply([incoming],now);service.RebuildForecast(now);Assert(!service.Verified&&service.ForecastAnnouncement!=null,"需要未驗證但有先前預測");
            service.Radar.Apply([completed],now.AddSeconds(1));Assert(service.ForecastAnnouncement==null&&service.Forecast?.Announced==null,"不得等下一次額度成功才撤除完成公告");
        });
        AtomicJson.Save(Path.Combine(root,"radar-forecast-integration.json"),new{results});return results;
    }
}
