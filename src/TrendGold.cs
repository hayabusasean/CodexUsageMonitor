using System.Drawing.Drawing2D;
namespace CodexUsageMonitor;
internal sealed partial class TrendView {
 internal IReadOnlyList<LocalQuotaCycleEvent> GoldEvents=[];
 internal event Action<string>? GoldEventSelected;
 readonly List<(RectangleF rect,string id)> goldHits=[];
 string? selectedGold;int keyboardEvent=-1;
 internal LocalQuotaCycleEvent? RailEvent=>GoldEvents.FirstOrDefault(x=>x.Full&&x.id==selectedGold)??GoldEvents.LastOrDefault(x=>x.Full&&NormalizedX(x.observed_to)>=0&&NormalizedX(x.observed_to)<=1)??GoldEvents.LastOrDefault(x=>x.Full);
 internal void StepGold(int step){var list=GoldEvents.Where(x=>x.Full).ToArray();if(list.Length==0)return;int index=Array.FindIndex(list,x=>x.id==RailEvent?.id);selectedGold=list[(index+step+list.Length)%list.Length].id;Invalidate();}
 internal void OpenGold(string id){selectedGold=id;GoldEventSelected?.Invoke(id);Invalidate();}
 internal void SelectGoldSample(string id){var item=GoldEvents.FirstOrDefault(x=>x.Full&&x.after_sample_id==id);if(item!=null){selectedGold=item.id;Invalidate();}}
 internal void DrawGoldEvents(Graphics g,float l,float r,float t,float b){
  goldHits.Clear();if(latest)return;
  var visible=GoldEvents.Where(x=>x.Full&&NormalizedX(x.observed_to)>=0&&NormalizedX(x.observed_to)<=1).OrderBy(x=>x.observed_to).ToArray();
  if(visible.Length==0)return;
  foreach(var e in visible){
   float x=l+(float)NormalizedX(e.observed_to)*(r-l),from=l+(float)NormalizedX(e.observed_from)*(r-l);
   using var band=new SolidBrush(Color.FromArgb(25,GoldVisual.Base));float left=Math.Clamp(from,l,r),width=Math.Min(r-left,Math.Max(8,x-left));g.FillRectangle(band,left,t,width,b-t);
   using var line=new Pen(GoldVisual.Light,1.5f){DashStyle=DashStyle.Dash};g.DrawLine(line,x,t,x,b);GoldVisual.Check(g,new(x,t),7);
   goldHits.Add((new(x-12,t-12,24,24),e.id));
  }
 }
 protected override void OnMouseDown(MouseEventArgs e){
  float k=DeviceDpi/96f;var hit=goldHits.LastOrDefault(x=>x.rect.Contains(e.X/k,e.Y/k));
  if(e.Button==MouseButtons.Left&&hit.id!=null){selectedGold=hit.id;GoldEventSelected?.Invoke(hit.id);Invalidate();return;}base.OnMouseDown(e);
 }
 protected override bool IsInputKey(Keys keyData)=>keyData is Keys.Left or Keys.Right or Keys.Enter or Keys.Escape||base.IsInputKey(keyData);
 protected override void OnKeyDown(KeyEventArgs e){var events=GoldEvents.Where(x=>x.Full).ToArray();if(events.Length>0&&e.KeyCode is Keys.Left or Keys.Right){keyboardEvent=(keyboardEvent+(e.KeyCode==Keys.Right?1:events.Length-1)+events.Length)%events.Length;selectedGold=events[keyboardEvent].id;Invalidate();e.Handled=true;}if(e.KeyCode==Keys.Enter&&selectedGold!=null){GoldEventSelected?.Invoke(selectedGold);e.Handled=true;}base.OnKeyDown(e);}
}
internal sealed class GoldActionButton:Button {
 internal GoldActionButton(){Height=44;Width=210;Dock=DockStyle.Top;BackColor=Theme.Canvas;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);L.Watch(this,()=>Text=AccessibleName=GoldVisual.Copy("View event evidence  ↗","查看事件證據  ↗"));}
 protected override void OnPaint(PaintEventArgs e){float k=DeviceDpi/96f;var g=e.Graphics;R2.Under(this,g);var rect=new RectangleF(2,2,Width-4,Height-4);using var shape=Theme.Round(rect,10*k);using var brush=GoldVisual.Metal(rect);g.FillPath(brush,shape);TextRenderer.DrawText(g,Text,Font,ClientRectangle,Enabled?Theme.Hex("#211809"):Theme.Micro,TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter);if(Focused)ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(ClientRectangle,-7,-7));}
}
