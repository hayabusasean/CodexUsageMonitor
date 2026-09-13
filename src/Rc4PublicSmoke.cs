using System.Text.Json;

namespace CodexUsageMonitor;

// Anonymous public-source smoke. It uses the same ResetRadar Poll path as the product,
// never starts the quota producer, never fetches X, and tolerates isolated source failures.
internal static class Rc4PublicSmoke
{
 public static void Run(string[] args)
 {
  int pos=Array.IndexOf(args,"--test-root");string root=Path.GetFullPath(args[pos+1]);Directory.CreateDirectory(root);
  using var radar=new ResetRadar(root);radar.SetEnabled(true);radar.Poll(true,CancellationToken.None).GetAwaiter().GetResult();
  var state=radar.Snapshot();var configured=ResetRadar.Sources.ToDictionary(x=>x.Id);
  bool allAttempted=ResetRadar.Sources.All(x=>state.Sources.Any(s=>s.Id==x.Id&&s.LastAttempt!=null));
  bool officialHealthy=state.Sources.Any(x=>x.SourceClass=="OFFICIAL_OPENAI"&&x.LastSuccess!=null);
  bool relayHealthy=state.Sources.Any(x=>x.SourceClass=="TEAM_SIGNAL_RELAY"&&x.LastSuccess!=null);
  bool noXConfigured=ResetRadar.Sources.All(x=>new Uri(x.Url).DnsSafeHost is not ("x.com" or "www.x.com"));
  bool statusSafe=state.Events.Where(x=>x.SourceClass=="OPENAI_STATUS").All(x=>x.SignalLevel=="SUPPORTING_CONTEXT");
  bool creditsSafe=state.Events.Where(x=>x.Effect=="BANKED_CREDIT_GRANT").All(x=>x.SignalLevel=="NONE");
  bool forecastSafe=state.Events.Where(x=>x.RelayForecast!="").All(x=>x.SignalLevel=="NONE");
  bool structuralPass=allAttempted&&officialHealthy&&relayHealthy&&noXConfigured&&statusSafe&&creditsSafe&&forecastSafe;
  AtomicJson.Save(Path.Combine(root,"public-source-smoke.json"),new{
   classification="REAL_ANONYMOUS_PUBLIC_HTTPS_PRODUCT_POLL",executed_utc=DateTimeOffset.UtcNow,exe_sha256=ReportExporter.ExeHash(),
   cookies=false,default_credentials=false,x_background_fetch=false,quota_producer_started=false,model_calls=0,
   structural_pass=structuralPass,all_sources_attempted=allAttempted,official_source_healthy=officialHealthy,selected_relay_healthy=relayHealthy,
   no_x_configured=noXConfigured,status_supporting_only=statusSafe,banked_credit_non_alerting=creditsSafe,forecast_non_alerting=forecastSafe,
   coverage=Rc4Radar.Coverage(state,true),sources=state.Sources,events=state.Events.Select(x=>new{x.EventId,x.Source,x.SourceClass,x.SignalLevel,x.SignalReason,x.Effect,x.Phase,x.Confidence,x.PublishedAt,x.ResetUtc,original_source_present=x.OriginalSourceUrl!="",relay_source_present=x.RelayUrl!=""})
  });
  Environment.ExitCode=structuralPass?0:1;
 }
}
