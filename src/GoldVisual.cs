using System.Drawing.Drawing2D;
namespace CodexUsageMonitor;

internal static class GoldVisual {
 internal static Color Base=>Theme.HighContrast?Theme.Text:Theme.Hex("#BE9339");
 internal static Color Light=>Theme.HighContrast?Theme.Text:Theme.Hex("#F0D690");
 internal static string Copy(string en,string zh)=>L.Language=="zh-TW"?zh:en;
 internal static string Percent(decimal? x)=>x.HasValue?TrendView.Number(x.Value)+"%":"—";
 internal static string Assurance(string s)=>s switch{"STABLE_SCOPE"=>Copy("Verified scope","範圍已驗證"),"LOCAL_STREAM_CONTINUITY_ONLY"=>Copy("Local stream continuity only","僅確認本機資料連續性"),_=>Copy("Insufficient scope evidence","範圍證據不足")};
 internal static string ReadState(string s)=>s switch{"READ"=>Copy("Read","已讀"),"UNREAD"=>Copy("Unread","未讀"),_=>Copy("Unknown","未知")};
 internal static string At(DateTimeOffset x)=>TrendView.Local(x,"yyyy-MM-dd HH:mm:ss zzz");
 internal static string Schedule(string? s)=>s switch{"BEFORE_SCHEDULE_WINDOW"=>Copy("Before the source's previous schedule","早於來源原訂時間"),"AT_SCHEDULE_WINDOW"=>Copy("Within the previous scheduled window","在原訂時間區間內"),_=>Copy("Schedule relation unknown","與原訂時間關係未知")};
 internal static LinearGradientBrush Metal(RectangleF r){var b=new LinearGradientBrush(r,Base,Light,90);b.InterpolationColors=new ColorBlend{Positions=[0,.32f,.48f,.62f,1],Colors=[Theme.Hex("#72501F"),Theme.Hex("#D8B55A"),Theme.Hex("#FFF1C7"),Theme.Hex("#E8C778"),Theme.Hex("#A87929")]};return b;}
 internal static void Text(Graphics g,string text,RectangleF r,float size){using var path=new GraphicsPath();using var font=Theme.PixelFont(size,true);using var sf=new StringFormat{LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap};path.AddString(text,font.FontFamily,(int)FontStyle.Bold,font.Size,r,sf);using var fill=Metal(r);if(Theme.HighContrast){using var solid=new SolidBrush(Theme.Text);g.FillPath(solid,path);}else g.FillPath(fill,path);}
 internal static void Check(Graphics g,PointF p,float radius=7){using var fill=new SolidBrush(Theme.Canvas);using var edge=new Pen(Light,1.5f);g.FillEllipse(fill,p.X-radius,p.Y-radius,radius*2,radius*2);g.DrawEllipse(edge,p.X-radius,p.Y-radius,radius*2,radius*2);g.DrawLines(edge,new PointF[]{new(p.X-radius*.5f,p.Y),new(p.X-radius*.1f,p.Y+radius*.4f),new(p.X+radius*.5f,p.Y-radius*.4f)});}
 internal static void Card(Graphics g,RectangleF r){using var path=Theme.Round(r,15);using var fill=new SolidBrush(Color.FromArgb(180,17,20,38));g.FillPath(fill,path);if(!Theme.HighContrast)for(int i=7;i>0;i--){using var glow=new Pen(Color.FromArgb(5,Base),i*1.8f);g.DrawPath(glow,path);}using var rim=new Pen(Base,1);g.DrawPath(rim,path);using var inside=Theme.Round(new(r.X+2,r.Y+2,r.Width-4,r.Height-4),13);using var light=new Pen(Color.FromArgb(90,Light),.6f);g.DrawPath(light,inside);}
 internal static void Summary(Graphics g,LocalQuotaCycleEvent e,float w,float h){
  Card(g,new(3,3,w-6,h-6));Check(g,new(30,29));Theme.TextAt(g,e.Confirmed?Copy("QUOTA REPLENISHED","額度已補滿"):Copy("FULL QUOTA OBSERVED · CYCLE UNCONFIRMED","觀察到滿額 · 新週期尚未確認"),new(48,12,w-72,34),12,Light,true);
  Text(g,Percent(e.remaining_before)+" → "+Percent(e.remaining_after),new(24,48,w*.64f,66),w<650?28:36);
  Text(g,"+"+TrendView.Number(e.delta_pp??0),new(w*.72f,52,w*.25f,38),22);
  Theme.TextAt(g,Copy("percentage points","個百分點"),new(w*.72f,90,w*.25f,23),9,Theme.Muted);
  Theme.TextAt(g,Copy("First observed: ","首次觀察到補滿：")+At(e.observed_to),new(24,118,w-48,26),10,Theme.Text);
  Theme.TextAt(g,Copy("Observation interval: ","觀測區間：")+TrendView.Local(e.observed_from,"HH:mm:ss")+" – "+TrendView.Local(e.observed_to,"HH:mm:ss")+Copy(" · Server execution time unknown"," · 伺服端精確執行時間未知"),new(24,148,w-48,32),9,Theme.Muted);
  Theme.TextAt(g,Schedule(e.schedule_relation)+" · "+Copy("Next source reset: ","新的來源重置時間：")+(e.reset_at_after.HasValue?TrendView.Local(e.reset_at_after.Value,"MM-dd HH:mm zzz"):Copy("Not provided","未提供")),new(24,182,w-48,34),9,Light);
  Theme.TextAt(g,Copy("Cause UNKNOWN · Local evidence; not a confirmed global reset","原因 UNKNOWN · 本機觀察證據，不代表全域重置已確認"),new(24,217,w-48,30),9,Theme.Muted);
 }
}
internal sealed class GoldCycleCard:Control {
 internal LocalQuotaCycleEvent? Evidence;
 internal GoldCycleCard(){DoubleBuffered=true;Height=298;Tag=298;Dock=DockStyle.Top;BackColor=Theme.Canvas;}
 protected override void OnPaintBackground(PaintEventArgs e){if(R2.Is(this))R2.Under(this,e.Graphics);else base.OnPaintBackground(e);}
 protected override void OnPaint(PaintEventArgs e){if(Evidence==null)return;float k=DeviceDpi/96f;e.Graphics.ScaleTransform(k,k);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;GoldVisual.Summary(e.Graphics,Evidence,Width/k,Height/k);Theme.TextAt(e.Graphics,Evidence.related_signal_revisions.Length>0?GoldVisual.Copy("Old-cycle local reminder ended; original signal retained.","舊週期本機提醒已退場；原始訊號仍保留。"):GoldVisual.Copy("Historical local observation; no matching old reminder.","本機歷史觀察；沒有匹配的舊提醒。"),new(24,254,Width/k-48,34),9,Theme.Muted);}
}
