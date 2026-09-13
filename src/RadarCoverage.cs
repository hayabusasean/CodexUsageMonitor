using System.Drawing.Drawing2D;
namespace CodexUsageMonitor;
// Coverage measures source readability, never reset probability. No historical backfill.
internal sealed record RadarCoveragePoint(DateTimeOffset timestamp,int? coverage_percent,int readable_sources,int enabled_sources,
 int official_readable,int official_total,int team_readable,int team_total,int community_readable,int community_total);
internal static class RadarCoverage {
 internal static bool Readable(SourceStatus s,RadarSourceDefinition definition,DateTimeOffset now){
  if(s.LastSuccess is not {} success||success>now||now-success>TimeSpan.FromHours(4))return false;
  if(s.Status=="BLOCKED"||s.HttpStatus is 401 or 403)return false;
  if(s.Status is "HEALTHY" or "UNCHANGED")return now-success<=FreshWindow(definition);
  // Only known transient transport errors get grace; confirmed parse/policy errors fail closed.
  bool transient=s.Error is "TIMEOUT" or "NETWORK_ERROR" or "HTTP_429"||s.HttpStatus>=500&&s.HttpStatus<=599;
  return transient&&now-success<=FreshWindow(definition);
 }
 // Normal scheduling includes up to 90 seconds jitter per interval.
 internal static TimeSpan FreshWindow(RadarSourceDefinition d)=>TimeSpan.FromSeconds(2*(d.IntervalMinutes*60+90));
 internal static RadarCoveragePoint Measure(RadarState state,bool enabled,DateTimeOffset now){
  var defs=enabled?ResetRadar.Sources:[];var latest=Rc4Radar.LatestConfiguredSources(state).ToDictionary(x=>x.Id);
  bool complete=defs.Length>0&&defs.All(d=>latest.TryGetValue(d.Id,out var s)&&s.LastAttempt!=null);
  bool IsReadable(RadarSourceDefinition d)=>latest.TryGetValue(d.Id,out var s)&&Readable(s,d,now);
  int total=defs.Length,readable=defs.Count(IsReadable);
  var official=defs.Where(d=>d.SourceClass is "OFFICIAL_OPENAI" or "OPENAI_STATUS").ToArray();
  var team=defs.Where(d=>d.SourceClass=="TEAM_SIGNAL_RELAY").ToArray();var community=defs.Where(d=>d.SourceClass=="COMMUNITY_TRACKER").ToArray();
  return new(now,complete?(int)Math.Round(readable*100d/total,MidpointRounding.AwayFromZero):null,readable,total,
   official.Count(IsReadable),official.Length,team.Count(IsReadable),team.Length,community.Count(IsReadable),community.Length);
 }
 internal static string Percent(RadarCoveragePoint p)=>p.coverage_percent?.ToString(System.Globalization.CultureInfo.InvariantCulture)??"--";
}
internal sealed class RadarCoverageView:Control {
 RadarCoveragePoint point=new(DateTimeOffset.UtcNow,null,0,0,0,0,0,0,0,0);
 internal RadarCoverageView(){Dock=DockStyle.Top;Height=130;Tag=130;DoubleBuffered=true;BackColor=Theme.Surface1;}
 internal void UpdateCoverage(RadarCoveragePoint value){point=value;AccessibleName=Summary;Invalidate();}
 internal string Summary=>L.T("Radar.CoverageTitle")+" "+RadarCoverage.Percent(point)+"% · "+(point.coverage_percent==null?L.T(point.enabled_sources==0?"Radar.CoverageRingDisabled":"Radar.CoverageScanning"):L.T("Radar.CoverageReadable",point.readable_sources,point.enabled_sources));
 protected override void OnPaintBackground(PaintEventArgs e){if(R2.Is(this))R2.Under(this,e.Graphics);else base.OnPaintBackground(e);}
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);float k=DeviceDpi/96f;var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
  if(R2.Is(this)){float w=Width/k,cx=w/2,cy=130,radius=100;var save=g.Save();g.ScaleTransform(k,k);
   for(int i=0;i<4;i++){using var orbit=new Pen(Color.FromArgb(45+i*16,i%2==0?Theme.Electric:Theme.Brand),1);g.DrawEllipse(orbit,cx-radius-i*9,cy-radius-i*9,(radius+i*9)*2,(radius+i*9)*2);}
   for(int i=0;i<60;i++){double a=i*Math.PI/30;using var tick=new Pen(Color.FromArgb(i%5==0?180:60,Theme.Electric),1);float rad=116;g.DrawLine(tick,cx+(float)Math.Cos(a)*rad,cy+(float)Math.Sin(a)*rad,cx+(float)Math.Cos(a)*(rad+(i%5==0?7:3)),cy+(float)Math.Sin(a)*(rad+(i%5==0?7:3)));}
   using var haze=new SolidBrush(Color.FromArgb(190,3,11,31));g.FillEllipse(haze,cx-90,cy-90,180,180);using var track2=new Pen(Color.FromArgb(90,Theme.Brand),8);g.DrawEllipse(track2,cx-90,cy-90,180,180);
   if(point.coverage_percent is >0){using var arc2=new Pen(Theme.Electric,8){StartCap=LineCap.Round,EndCap=LineCap.Round};g.DrawArc(arc2,cx-90,cy-90,180,180,-90,360f*point.coverage_percent.Value/100);}
   void Txt(string text,float y,float h,float size,Color color)=>Theme.TextAt(g,text,new(0,y,w,h),size,color,false,StringAlignment.Center);
   Txt(L.T("Radar.CoverageTitle"),80,28,11,Theme.Text);Txt(RadarCoverage.Percent(point)+"%",105,65,35,Theme.Electric);
   Txt(point.coverage_percent==null?L.T(point.enabled_sources==0?"Radar.CoverageRingDisabled":"Radar.CoverageScanning"):L.T("Radar.CoverageReadable",point.readable_sources,point.enabled_sources),175,28,10,Theme.Text);
   using(var scrim=new SolidBrush(Color.FromArgb(165,3,11,31)))g.FillRectangle(scrim,cx-180,257,360,54);
   Txt(L.T("Radar.CoverageOfficial")+"  "+point.official_readable+" / "+point.official_total,260,21,10,Theme.Electric);Txt(L.T("Radar.CoverageTeam")+"  "+point.team_readable+" / "+point.team_total+"     "+L.T("Radar.CoverageCommunity")+"  "+point.community_readable+" / "+point.community_total,284,22,9,Theme.Muted);g.Restore(save);return;}
  var ring=new RectangleF(10*k,13*k,90*k,90*k);using var track=new Pen(Theme.Border,6*k);using var arc=new Pen(Theme.Accent,6*k){StartCap=LineCap.Round,EndCap=LineCap.Round};g.DrawEllipse(track,ring);
  if(point.coverage_percent is >0)g.DrawArc(arc,ring,-90,360f*point.coverage_percent.Value/100);
  void Text(string value,float x,float y,float w,float h,float size,Color color,TextFormatFlags flags=TextFormatFlags.Left|TextFormatFlags.VerticalCenter){using var font=Theme.Font(size);TextRenderer.DrawText(g,value,font,new Rectangle((int)(x*k),(int)(y*k),(int)(w*k),(int)(h*k)),color,flags|TextFormatFlags.NoPadding);}
  Text(RadarCoverage.Percent(point)+"%",10,38,90,34,22,Theme.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
  Text(L.T("Radar.CoverageTitle"),120,4,460,27,12,Theme.Text);
  Text(point.coverage_percent==null?L.T(point.enabled_sources==0?"Radar.CoverageRingDisabled":"Radar.CoverageScanning"):L.T("Radar.CoverageReadable",point.readable_sources,point.enabled_sources),120,31,460,24,10,Theme.Muted);
  Text(L.T("Radar.CoverageOfficial")+"    "+point.official_readable+" / "+point.official_total,120,61,460,20,9.5f,Theme.Muted);
  Text(L.T("Radar.CoverageTeam")+"    "+point.team_readable+" / "+point.team_total,120,81,460,20,9.5f,Theme.Muted);
  Text(L.T("Radar.CoverageCommunity")+"    "+point.community_readable+" / "+point.community_total,120,101,460,20,9.5f,Theme.Muted);
 }
}
