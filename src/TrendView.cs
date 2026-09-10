using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
namespace CodexUsageMonitor;

internal static class NativeDark {
 public static void Scrollbars(IntPtr handle){try{SetWindowTheme(handle,"DarkMode_Explorer",null);}catch{}}
 [DllImport("uxtheme.dll",CharSet=CharSet.Unicode)]static extern int SetWindowTheme(IntPtr handle,string app,string? id);
}

internal sealed class TrendView:Control {
 List<UsageSample> rows=[],displayRows=[],renderRows=[];
 bool latest=true;
 (decimal min,decimal max) scale=(0,100);
 readonly Dictionary<string,int> segmentIds=[];
 readonly ToolTip tip=new(){OwnerDraw=true,AutoPopDelay=8000};
 string last="";List<(PointF p,UsageSample row)> points=[];
 [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
 public List<UsageSample> Rows{get=>rows;set{rows=value.OrderBy(x=>x.observed_at_utc).ToList();Prepare();}}
 [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
 public bool Latest{get=>latest;set{if(latest==value)return;latest=value;Prepare();}}
 public IReadOnlyList<UsageSample> DisplayRows=>displayRows;
 public (decimal min,decimal max) Range=>scale;
 public string ZoomBadge{get{var (min,max)=Range;return L.T(min==0&&max==100?"History.FullScale":"History.ZoomedScale",Number(min),Number(max));}}
 public string ViewedRange=>displayRows.Count==0?"":L.T("History.ViewedRange",Local(displayRows[0].observed_at_utc,"yyyy-MM-dd HH:mm"),Local(displayRows[^1].observed_at_utc,"yyyy-MM-dd HH:mm"));

 internal static string Number(decimal v)=>v.ToString("0.####",CultureInfo.InvariantCulture);
 internal static string Local(DateTimeOffset t,string format)=>TimeZoneInfo.ConvertTime(t,TimeZoneInfo.Local).ToString(format,CultureInfo.InvariantCulture);
 internal static (decimal min,decimal max) Zoom(IEnumerable<UsageSample> input){
  var values=input.Where(DailyUsageCalculator.Valid).Select(x=>x.remaining_percent!.Value).ToArray();if(values.Length==0)return(0,100);
  decimal min=values.Min(),max=values.Max(),span=Math.Min(100,Math.Max(2,(max-min)*1.2m));
  decimal lo=min-(span-(max-min))/2,hi=max+(span-(max-min))/2;
  if(hi>100){lo-=hi-100;hi=100;}if(lo<0){hi-=lo;lo=0;}
  return(Math.Max(0,Math.Floor(lo*10)/10),Math.Min(100,Math.Ceiling(hi*10)/10));
 }
 // A source restart is a chart boundary even where daily accounting can compare stable identity.
 internal static bool Bridge(UsageSample p,UsageSample c)=>DailyUsageCalculator.Reject(p,c).Length==0&&
  p.source_generation==c.source_generation&&p.comparison_key==c.comparison_key&&
  c.remaining_percent<=p.remaining_percent&&
  (c.observed_at_utc-p.observed_at_utc).TotalSeconds<=Math.Max(3*c.polling_interval_seconds,300);
 internal static List<UsageSample> LatestSegment(IEnumerable<UsageSample> input){
  var ordered=input.OrderBy(x=>x.observed_at_utc).ToList();int end=ordered.FindLastIndex(DailyUsageCalculator.Valid);
  if(end<0)return[];int start=end;while(start>0&&Bridge(ordered[start-1],ordered[start]))start--;
  return ordered.GetRange(start,end-start+1);
 }
 // Downsample only rendering. Keep every boundary and each bucket's first/last/min/max.
 // The table and exports retain the complete query snapshot.
 internal static List<UsageSample> Reduce(IReadOnlyList<UsageSample> input,int buckets){
  if(input.Count<=Math.Max(32,buckets*4))return input.ToList();
  var keep=new SortedSet<int>{0,input.Count-1};int size=Math.Max(1,(int)Math.Ceiling(input.Count/(double)Math.Max(1,buckets)));
  for(int start=0;start<input.Count;start+=size){
   int end=Math.Min(input.Count-1,start+size-1),lo=start,hi=start;keep.Add(start);keep.Add(end);
   for(int i=start;i<=end;i++){
    if(input[i].remaining_percent<input[lo].remaining_percent)lo=i;
    if(input[i].remaining_percent>input[hi].remaining_percent)hi=i;
    if(i>0&&!Bridge(input[i-1],input[i])){keep.Add(i-1);keep.Add(i);}
   }
   keep.Add(lo);keep.Add(hi);
  }
  return keep.Select(i=>input[i]).ToList();
 }
 internal static int TickCount(float width)=>Math.Clamp((int)(width/145),3,6);
 void Prepare(){
  displayRows=latest?LatestSegment(rows):rows.ToList();
  renderRows=Reduce(displayRows,Math.Max(100,Width/3));
  scale=latest?Zoom(displayRows):(0,100);segmentIds.Clear();int segment=0;
  for(int i=0;i<displayRows.Count;i++){if(i>0&&!Bridge(displayRows[i-1],displayRows[i]))segment++;segmentIds[displayRows[i].sample_id]=segment;}
  last="";points.Clear();tip.Hide(this);Invalidate();
 }
 public TrendView(){
  DoubleBuffered=true;BackColor=Theme.Canvas;AccessibleRole=AccessibleRole.Chart;
  L.Watch(this,()=>{AccessibleName=L.T("History.ChartTitle");last="";tip.Hide(this);Invalidate();});
  tip.Popup+=(_,e)=>e.ToolTipSize=new((int)(310*DeviceDpi/96f),(int)(124*DeviceDpi/96f));
  tip.Draw+=(_,e)=>{using var b=new SolidBrush(Theme.Surface);using var pen=new Pen(Theme.Border);e.Graphics.FillRectangle(b,e.Bounds);e.Graphics.DrawRectangle(pen,0,0,e.Bounds.Width-1,e.Bounds.Height-1);using var font=Theme.Font(9);TextRenderer.DrawText(e.Graphics,e.ToolTipText,font,Rectangle.Inflate(e.Bounds,-12,-8),Theme.Text,TextFormatFlags.WordBreak);};
  MouseMove+=(_,e)=>{
   if(points.Count==0)return;var nearest=points.MinBy(p=>Math.Abs(p.p.X-e.X)+Math.Abs(p.p.Y-e.Y)/4);var row=nearest.row;
   if(row.sample_id==last)return;last=row.sample_id;
   string used=row.used_percent_raw is decimal u?Number(u)+"%":"—",remaining=row.remaining_percent is decimal v?Number(v)+"%":"—";
   tip.SetToolTip(this,L.T("History.ChartTooltip",Local(row.observed_at_utc,"yyyy-MM-dd HH:mm:ss zzz"),used,remaining,L.T(row.had_gap?"History.Resumed":"History.LocalObservation")));
  };
  MouseLeave+=(_,_)=>{last="";tip.Hide(this);};
 }
 protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);renderRows=Reduce(displayRows,Math.Max(100,Width/3));}
 protected override void Dispose(bool disposing){if(disposing)tip.Dispose();base.Dispose(disposing);}
 protected override void OnPaint(PaintEventArgs e){
  base.OnPaint(e);float k=DeviceDpi/96f;var g=e.Graphics;g.ScaleTransform(k,k);g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
  float w=Width/k,h=Height/k,l=52,r=w-20,t=44,b=h-28;if(r<=l||b<=t)return;
  Theme.TextAt(g,ZoomBadge,new(0,0,w*.48f,28),9,Theme.Muted);
  Theme.TextAt(g,ViewedRange,new(w*.43f,0,w*.57f,28),8,Theme.Muted,false,System.Drawing.StringAlignment.Far);
  var (low,high)=Range;float range=(float)Math.Max(2,high-low);
  using var grid=new Pen(Theme.Border);using var line=new Pen(Theme.Accent,1.7f);using var marker=new SolidBrush(Theme.Accent);
  using var gapFill=new SolidBrush(Color.FromArgb(24,Theme.Muted));using var gapMark=new Pen(Color.FromArgb(140,Theme.Muted),1);
  for(int n=0;n<=4;n++){float y=t+(b-t)*n/4;g.DrawLine(grid,l,y,r,y);Theme.TextAt(g,Number(high-(high-low)*n/4)+"%",new(0,y-10,44,20),8.5f,Theme.Muted,false,System.Drawing.StringAlignment.Far);}
  points.Clear();
  if(displayRows.Count==0||!displayRows.Any(DailyUsageCalculator.Valid)){
   Theme.TextAt(g,L.T("History.NoData"),new(l,t,r-l,b-t-28),12,Theme.Muted,false,System.Drawing.StringAlignment.Center);
   Theme.TextAt(g,L.T("History.EmptyHint"),new(l,b-50,r-l,36),9,Theme.Muted,false,System.Drawing.StringAlignment.Center);return;
  }
  var start=displayRows[0].observed_at_utc;double duration=Math.Max(1,(displayRows[^1].observed_at_utc-start).TotalSeconds);
  PointF P(UsageSample row)=>new(displayRows.Count==1?(l+r)/2:l+(float)((row.observed_at_utc-start).TotalSeconds/duration)*(r-l),b-((float)(row.remaining_percent??low)-(float)low)/range*(b-t));
  // Segment membership comes from the complete snapshot, so skipped render samples cannot hide a gap.
  float lastGap=-1000;
  for(int i=0;i<renderRows.Count;i++){
   var c=renderRows[i];if(!DailyUsageCalculator.Valid(c))continue;var q=P(c);
   if(i>0&&DailyUsageCalculator.Valid(renderRows[i-1])){
    var prev=renderRows[i-1];var p=P(prev);
    if(segmentIds[prev.sample_id]==segmentIds[c.sample_id]){
     g.DrawLine(line,p,new PointF(q.X,p.Y));g.DrawLine(line,new PointF(q.X,p.Y),q);
    }else{
     float middle=(p.X+q.X)/2;
     if(middle-lastGap>=48){float left=Math.Max(l,p.X),width=Math.Max(3,q.X-p.X);g.FillRectangle(gapFill,left,t,width,b-t);g.DrawLine(gapMark,middle-4,t+7,middle+1,t+2);g.DrawLine(gapMark,middle+2,t+7,middle+7,t+2);if(width>110)Theme.TextAt(g,L.T(c.had_gap||(c.observed_at_utc-prev.observed_at_utc).TotalSeconds>Math.Max(3*c.polling_interval_seconds,300)?"History.MonitoringGap":"History.SegmentBoundary"),new(left,t+10,width,24),8,Theme.Muted,false,System.Drawing.StringAlignment.Center);lastGap=middle;}
    }
   }
   g.FillEllipse(marker,q.X-3,q.Y-3,6,6);points.Add((new PointF(q.X*k,q.Y*k),c));
  }
  if(displayRows.Count==1){
   Theme.TextAt(g,L.T("History.OneObservation"),new(l,t+36,r-l,32),10,Theme.Muted,false,System.Drawing.StringAlignment.Center);
   Theme.TextAt(g,Local(start,"yyyy-MM-dd HH:mm:ss"),new(l,b+4,r-l,22),8,Theme.Muted,false,System.Drawing.StringAlignment.Center);return;
  }
  int ticks=TickCount(r-l);bool dates=Local(start,"yyyy-MM-dd")!=Local(displayRows[^1].observed_at_utc,"yyyy-MM-dd");
  string format=dates?"MM-dd HH:mm":duration<300?"HH:mm:ss":"HH:mm";
  if(Local(start,"yyyy")!=Local(displayRows[^1].observed_at_utc,"yyyy"))format="yyyy-MM-dd";
  float tickWidth=Math.Min(145,(r-l)/(ticks-1));
  for(int n=0;n<ticks;n++){
   float x=l+(r-l)*n/(ticks-1);var at=start.AddSeconds(duration*n/(ticks-1));
   float left=n==0?l:n==ticks-1?r-tickWidth:x-tickWidth/2;
   Theme.TextAt(g,Local(at,format),new(left,b+4,tickWidth,22),8,Theme.Muted,false,n==0?System.Drawing.StringAlignment.Near:n==ticks-1?System.Drawing.StringAlignment.Far:System.Drawing.StringAlignment.Center);
  }
 }
}
