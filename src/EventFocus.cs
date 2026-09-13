using System.Drawing.Drawing2D;
namespace CodexUsageMonitor;

internal sealed record EventFocusLogRow(
 string event_type,decimal? before_remaining,decimal? after_remaining,decimal? delta_percentage_points,
 DateTimeOffset before_sample_time,DateTimeOffset after_sample_time,bool had_gap,string observation_interval,
 string radar_signal_level,int? radar_coverage,string radar_source_class,int? banked_reset_before,int? banked_reset_after,
 DateTimeOffset? reset_at_before,DateTimeOffset? reset_at_after,string cause,string radar_read_state,string radar_source_id,DateTimeOffset? radar_evidence_time,DateTimeOffset? radar_coverage_time,string schedule_relation="UNKNOWN",string evidence_assurance="INSUFFICIENT",string radar_read_state_now="NOT_APPLICABLE",string next_reset_source="OBSERVED_SOURCE_METADATA",string server_execution_time="UNKNOWN");

internal sealed record EventFocusEvidence(
 string EventType,decimal? BeforeRemaining,decimal? AfterRemaining,decimal? DeltaPercentagePoints,
 DateTimeOffset BeforeSampleTime,DateTimeOffset AfterSampleTime,bool HadGap,
 string RadarSignalLevel,int? RadarCoverage,string RadarSourceClass,string RadarSourceId,string RadarReadState,DateTimeOffset? RadarEvidenceTime,DateTimeOffset? RadarCoverageTime,
 int? BankedResetBefore,int? BankedResetAfter,DateTimeOffset? ResetAtBefore,DateTimeOffset? ResetAtAfter,string Cause){
 internal LocalQuotaCycleEvent? LocalCycle{get;init;}
 internal string CurrentRadarReadState{get;init;}="NOT_APPLICABLE";
 internal bool HasRadarContext=>RadarSignalLevel is "WATCH" or "INCOMING";
 internal string ObservationInterval=>BeforeSampleTime.ToString("O")+"/"+AfterSampleTime.ToString("O");
 internal EventFocusLogRow LogRow()=>new(EventType,BeforeRemaining,AfterRemaining,DeltaPercentagePoints,BeforeSampleTime,AfterSampleTime,HadGap,ObservationInterval,RadarSignalLevel,RadarCoverage,RadarSourceClass,BankedResetBefore,BankedResetAfter,ResetAtBefore,ResetAtAfter,Cause,RadarReadState,RadarSourceId,RadarEvidenceTime,RadarCoverageTime,LocalCycle?.schedule_relation??"UNKNOWN",LocalCycle?.evidence_assurance??"INSUFFICIENT",CurrentRadarReadState,ResetAtAfter==null?"NOT_PROVIDED":"OBSERVED_SOURCE_METADATA");
 static DateTimeOffset? Unix(long? value){try{return value.HasValue?DateTimeOffset.FromUnixTimeSeconds(value.Value):null;}catch{return null;}}
 internal static int Priority(UsageEvent e)=>e.event_type switch{"ZERO_TO_FULL_OBSERVED"=>0,"FULL_REPLENISHMENT_OBSERVED"=>1,"QUOTA_INCREASE_OBSERVED"=>2,"QUOTA_DECREASE_OBSERVED"=>3,_=>4};
 internal static bool IsEligible(UsageEvent e)=>e.event_type is "ZERO_TO_FULL_OBSERVED" or "FULL_REPLENISHMENT_OBSERVED"
  && e.previous_remaining.HasValue&&e.current_remaining.HasValue&&e.current_remaining.Value>e.previous_remaining.Value;
 internal static IReadOnlyList<UsageEvent> FocusEvents(IEnumerable<UsageEvent> events)=>events
  .Where(IsEligible)
  .GroupBy(x=>x.previous_sample_id+"|"+x.current_sample_id,StringComparer.Ordinal)
  .Select(g=>g.OrderBy(Priority).ThenBy(x=>x.event_type,StringComparer.Ordinal).First())
  .OrderBy(x=>x.observation_to_utc).ToArray();
 internal static EventFocusEvidence Build(UsageEvent e,IEnumerable<UsageSample> samples,RadarState radar,DateTimeOffset asOf){
  if(!IsEligible(e))throw new InvalidDataException("EVENT_FOCUS_NOT_ELIGIBLE");
  var byId=samples.GroupBy(x=>x.sample_id).ToDictionary(x=>x.Key,x=>x.Last(),StringComparer.Ordinal);
  byId.TryGetValue(e.previous_sample_id,out var before);byId.TryGetValue(e.current_sample_id,out var after);
  decimal? beforeRemaining=before?.remaining_percent??e.previous_remaining,afterRemaining=after?.remaining_percent??e.current_remaining;
  if(!beforeRemaining.HasValue||!afterRemaining.HasValue||afterRemaining.Value<=beforeRemaining.Value)throw new InvalidDataException("EVENT_FOCUS_ENDPOINTS_INVALID");
  var signals=radar.SignalHistory.Where(x=>x.SignalLevel is "WATCH" or "INCOMING").ToList();
  foreach(var a in radar.Events.Where(x=>x.SignalLevel is "WATCH" or "INCOMING"))if(!signals.Any(x=>x.EventId==a.EventId&&x.RevisionId==a.RevisionId))signals.Add(new(a.FirstSeen,a.EventId,a.RevisionId,a.SignalLevel,a.Phase,a.Effect,a.Scope,a.ResetUtc,a.TimeQuality,a.PublishedAt,a.ReadAt,a.Source,a.SourceClass,a.OriginalSourceUrl,a.RelayUrl,a.OriginalQuote,a.ContentHash,a.SignalReason));
  DateTimeOffset evidenceCutoff=asOf<e.observation_to_utc?asOf:e.observation_to_utc;
  var signal=signals.Where(x=>x.Timestamp>=e.observation_from_utc.AddHours(-72)&&x.Timestamp<=evidenceCutoff)
   .OrderByDescending(x=>x.Timestamp).ThenByDescending(x=>Rc4Radar.SignalPriority(x.SignalLevel)).FirstOrDefault();
  int? coverage=signal?.CoverageAtObservation?.coverage_percent;DateTimeOffset? radarEvidenceTime=signal?.Timestamp,radarCoverageTime=signal?.CoverageAtObservation?.timestamp;
  if(signal!=null&&coverage==null){
   var freshness=TimeSpan.FromMinutes(ResetRadar.Sources.Max(x=>x.IntervalMinutes)*2);
   var coverageRow=radar.CoverageTimeline.Where(x=>x.coverage_percent!=null&&x.timestamp<=evidenceCutoff&&x.timestamp>=evidenceCutoff-freshness).OrderByDescending(x=>x.timestamp).FirstOrDefault();
   coverage=coverageRow?.coverage_percent;radarCoverageTime=coverageRow?.timestamp;
  }
  string cause=string.IsNullOrWhiteSpace(e.cause)?"UNKNOWN":e.cause;
  return new(e.event_type,beforeRemaining,afterRemaining,afterRemaining.Value-beforeRemaining.Value,
   before?.observed_at_utc??e.observation_from_utc,after?.observed_at_utc??e.observation_to_utc,e.had_gap,
   signal?.SignalLevel??"NONE",coverage,signal?.SourceClass??"NONE",signal?.SourceId??"",signal==null?"NOT_APPLICABLE":signal.ReadAt==null||signal.ReadAt>evidenceCutoff?"UNREAD":"READ",radarEvidenceTime,radarCoverageTime,
   before?.reset_credits_available_count??e.previous_reset_credits,after?.reset_credits_available_count??e.current_reset_credits,
   before?.reset_at_utc??Unix(e.previous_reset_at),after?.reset_at_utc??Unix(e.current_reset_at),cause){LocalCycle=radar.LocalCycleEvents.FirstOrDefault(x=>x.after_sample_id==e.current_sample_id)??LocalQuotaCycle.Derive(byId.Values,asOf).FirstOrDefault(x=>x.after_sample_id==e.current_sample_id),CurrentRadarReadState=signal==null?"NOT_APPLICABLE":signal.ReadAt==null||signal.ReadAt>asOf?"UNREAD":"READ"};
 }
}

internal sealed class EventFocusView:Control {
 EventFocusEvidence? evidence;
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 internal EventFocusEvidence? Evidence{get=>evidence;set{evidence=value;UpdateAccessible();Invalidate();}}
 internal EventFocusView(){Dock=DockStyle.Top;Height=540;Tag=540;DoubleBuffered=true;BackColor=Theme.Surface1;AccessibleRole=AccessibleRole.Chart;L.Watch(this,()=>{UpdateAccessible();Invalidate();});}
 void UpdateAccessible(){AccessibleName=evidence==null?L.T("History.EventFocusEmpty"):L.T("History.EventFocusAccessible",Percent(evidence.BeforeRemaining),Percent(evidence.AfterRemaining),Signed(evidence.DeltaPercentagePoints),evidence.Cause);}
 static string Percent(decimal? value)=>value.HasValue?TrendView.Number(value.Value):"—";
 static string Signed(decimal? value)=>value.HasValue?(value.Value>=0?"+":"−")+TrendView.Number(Math.Abs(value.Value)):"—";
 static string When(DateTimeOffset value)=>TrendView.Local(value,"yyyy-MM-dd HH:mm:ss zzz");
 static string Reset(DateTimeOffset? value)=>value.HasValue?TrendView.Local(value.Value,"yyyy-MM-dd HH:mm zzz"):L.T("History.SourceNotProvided");
 static string Banked(int? value)=>value?.ToString(System.Globalization.CultureInfo.InvariantCulture)??L.T("History.SourceNotProvided");
 static string SourceClass(string value)=>L.T(value switch{"OFFICIAL_OPENAI"=>"Radar.SourceOfficial","OPENAI_STATUS"=>"Radar.SourceStatus","TEAM_SIGNAL_RELAY"=>"Radar.SourceRelay","COMMUNITY_TRACKER"=>"Radar.SourceThirdParty",_=>"History.SourceNotProvided"});
 protected override void OnPaintBackground(PaintEventArgs e){if(R2.Is(this))R2.Under(this,e.Graphics);else base.OnPaintBackground(e);}
 void PaintGold(Graphics g,EventFocusEvidence d,float w){
  GoldVisual.Summary(g,d.LocalCycle!,w,260);
  float l=48,r=w-24,t=286,b=382,x1=l+35,x2=r-30;
  using var grid=new Pen(Theme.ChartGrid);for(int n=0;n<=2;n++){float y=t+(b-t)*n/2;g.DrawLine(grid,l,y,r,y);Theme.TextAt(g,(100-50*n)+"%",new(0,y-9,40,18),8,Theme.Muted,false,StringAlignment.Far);}
  PointF p1=new(x1,b-(float)d.BeforeRemaining!.Value/100*(b-t)),p2=new(x2,b-(float)d.AfterRemaining!.Value/100*(b-t));
  using var band=new SolidBrush(Color.FromArgb(25,GoldVisual.Base));g.FillRectangle(band,x1,t,x2-x1,b-t);
  using var line=new Pen(GoldVisual.Base,1.6f){DashStyle=DashStyle.Dash};g.DrawLine(line,p1,p2);
  using var dot=new SolidBrush(Theme.Electric);g.FillEllipse(dot,p1.X-4,p1.Y-4,8,8);GoldVisual.Check(g,p2);
  Theme.TextAt(g,GoldVisual.Copy("Observation endpoints · dashed connector is not continuous data","觀察端點 · 虛線不代表逐秒數據"),new(l,268,r-l,20),9,Theme.Muted);
  Theme.TextAt(g,TrendView.Local(d.BeforeSampleTime,"HH:mm:ss")+" · "+Percent(d.BeforeRemaining)+"%",new(l,390,(r-l)/2,24),9,Theme.Muted);
  Theme.TextAt(g,TrendView.Local(d.AfterSampleTime,"HH:mm:ss")+" · "+Percent(d.AfterRemaining)+"%",new(l+(r-l)/2,390,(r-l)/2,24),9,GoldVisual.Light,false,StringAlignment.Far);
  Theme.TextAt(g,GoldVisual.Copy("Reset credits observed: ","完整重置券觀察：")+Banked(d.BankedResetBefore)+" → "+Banked(d.BankedResetAfter)+" · "+GoldVisual.Assurance(d.LocalCycle!.evidence_assurance),new(10,420,w-20,30),9,Theme.Muted);
  Theme.TextAt(g,GoldVisual.Copy("Radar at event: ","事件當時 Radar：")+d.RadarSignalLevel+" · "+(d.RadarCoverage?.ToString()??"--")+"% · "+GoldVisual.Copy("Read then / now: ","閱讀狀態 當時／現在：")+GoldVisual.ReadState(d.RadarReadState)+" / "+GoldVisual.ReadState(d.CurrentRadarReadState),new(10,454,w-20,36),9,Theme.Muted);
  Theme.TextAt(g,GoldVisual.Copy("Historical evidence; current quota may already be lower. Cause remains UNKNOWN.","這是歷史證據；目前額度可能已下降。補額原因仍為 UNKNOWN。"),new(10,496,w-20,34),9,Theme.Muted);
 }
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);float k=DeviceDpi/96f;var g=e.Graphics;g.ScaleTransform(k,k);g.SmoothingMode=SmoothingMode.AntiAlias;float w=Width/k;if(evidence is not {} d||!d.BeforeRemaining.HasValue||!d.AfterRemaining.HasValue){Theme.TextAt(g,L.T("History.EventFocusEmpty"),new(0,0,w,Height/k),12,Theme.Muted,false,StringAlignment.Center);return;}
  if(d.LocalCycle is {Full:true}){PaintGold(g,d,w);return;}
  Theme.TextAt(g,L.T("History.EventFocusTitle"),new(0,0,w*.42f,25),13,Theme.Text,true);
  Theme.TextAt(g,L.T("History.EventCauseUnknown"),new(w*.48f,0,w*.52f,25),9.5f,d.Cause=="UNKNOWN"?Theme.Warning:Theme.Accent,false,StringAlignment.Far);
  Theme.TextAt(g,L.T("History.EventSummary",Percent(d.BeforeRemaining),Percent(d.AfterRemaining)),new(0,24,w*.56f,38),21,Theme.Text,true);
  Theme.TextAt(g,L.T("History.EventDelta",Signed(d.DeltaPercentagePoints)),new(w*.55f,24,w*.45f,38),12,d.DeltaPercentagePoints>0?Theme.Accent:Theme.Text,true,StringAlignment.Far);
  float l=48,r=w-18,t=70,b=156;using var grid=new Pen(Theme.ChartGrid);for(int n=0;n<=2;n++){float y=t+(b-t)*n/2;g.DrawLine(grid,l,y,r,y);Theme.TextAt(g,(100-50*n)+"%",new(0,y-9,40,18),8,Theme.Muted,false,StringAlignment.Far);}float x1=l+42,x2=r-24;float Y(decimal v)=>b-(float)Math.Clamp(v,0,100)/100*(b-t);
  PointF p1=new(x1,Y(d.BeforeRemaining.Value)),p2=new(x2,Y(d.AfterRemaining.Value));if(d.HadGap){using var gap=new SolidBrush(Color.FromArgb(25,Theme.Muted));g.FillRectangle(gap,x1+8,t,x2-x1-16,b-t);using var pen=new Pen(Theme.Muted,1){DashStyle=DashStyle.Dot};g.DrawLine(pen,(x1+x2)/2,t,(x1+x2)/2,b);}else{using var pen=new Pen(Theme.Accent,1.7f){DashStyle=DashStyle.Dash};g.DrawLine(pen,p1,p2);}using var dot=new SolidBrush(Theme.Accent);g.FillEllipse(dot,p1.X-4,p1.Y-4,8,8);g.FillEllipse(dot,p2.X-4,p2.Y-4,8,8);
  Theme.TextAt(g,L.T("History.BeforeObservation",Percent(d.BeforeRemaining),When(d.BeforeSampleTime)),new(l,158,(r-l)/2,30),8.5f,Theme.Muted);
  Theme.TextAt(g,L.T("History.AfterObservation",Percent(d.AfterRemaining),When(d.AfterSampleTime)),new(l+(r-l)/2,158,(r-l)/2,30),8.5f,Theme.Muted,false,StringAlignment.Far);
  string interval=L.T(d.HadGap?"History.EventGapInterval":"History.EventObservationInterval",When(d.BeforeSampleTime),When(d.AfterSampleTime));Theme.TextAt(g,interval,new(0,190,w,28),9,Theme.Muted);
  string radar=d.HasRadarContext?L.T("History.EventRadarContext",d.RadarSignalLevel,d.RadarCoverage?.ToString(System.Globalization.CultureInfo.InvariantCulture)??"--",SourceClass(d.RadarSourceClass),L.T(d.RadarReadState=="READ"?"Radar.ReadRead":"Radar.ReadUnread"),d.RadarEvidenceTime.HasValue?When(d.RadarEvidenceTime.Value):L.T("History.SourceNotProvided"),d.RadarCoverageTime.HasValue?When(d.RadarCoverageTime.Value):L.T("History.SourceNotProvided")):L.T("History.EventRadarNone");Theme.TextAt(g,radar,new(0,216,w,26),9.2f,d.HasRadarContext?(d.RadarSignalLevel=="INCOMING"?Theme.ResetAlert:Theme.Warning):Theme.Muted);
  Theme.TextAt(g,L.T("History.EventResetAt",Reset(d.ResetAtBefore),Reset(d.ResetAtAfter)),new(0,242,w,24),8.5f,Theme.Muted);
  Theme.TextAt(g,L.T("History.EventBankedReset",Banked(d.BankedResetBefore),Banked(d.BankedResetAfter)),new(0,265,w,24),8.5f,Theme.Muted);
  Theme.TextAt(g,L.T("History.EventEvidenceBoundary"),new(0,289,w,26),8.5f,Theme.Muted);
 }
}

