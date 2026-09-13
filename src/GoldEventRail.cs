namespace CodexUsageMonitor;
// A sibling of TrendView: the opaque summary cannot enter the plot, at any viewport or DPI.
internal sealed class GoldEventRail:Control {
 readonly TrendView trend;readonly GoldActionButton open=new(){Dock=DockStyle.None};readonly Button previous,next;
 internal RectangleF CardBounds=>new(7,7,Math.Min(276,Width/(DeviceDpi/96f)-14),92);
 internal GoldEventRail(TrendView view){trend=view;Dock=DockStyle.Top;Height=108;Tag=108;DoubleBuffered=true;BackColor=Theme.Canvas;Name="GoldEventRail";
  previous=Theme.Button("←",(_,_)=>trend.StepGold(-1));next=Theme.Button("→",(_,_)=>trend.StepGold(1));previous.Width=next.Width=42;Controls.AddRange([open,previous,next]);
  open.Click+=(_,_)=>{if(trend.RailEvent is {} e)trend.OpenGold(e.id);};
  trend.Invalidated+=(_,_)=>{if(!IsDisposed)Invalidate();};L.Watch(this,()=>{previous.AccessibleName=GoldVisual.Copy("Previous event","上一個事件");next.AccessibleName=GoldVisual.Copy("Next event","下一個事件");Invalidate();});
 }
 bool compact,arranging;
 protected override void OnLayout(LayoutEventArgs e){if(arranging||previous==null||next==null)return;arranging=true;try{base.OnLayout(e);float k=DeviceDpi/96f;compact=Width/k<650;int height=(int)((compact?208:108)*k);if(Height!=height)Height=height;int x=(int)((compact?7:300)*k),y=(int)((compact?162:58)*k);open.Bounds=new(x,y,(int)(210*k),(int)(38*k));previous.Bounds=new(Width-(int)(100*k),y,(int)(42*k),(int)(36*k));next.Bounds=new(Width-(int)(52*k),y,(int)(42*k),(int)(36*k));}finally{arranging=false;}}
 protected override void OnPaintBackground(PaintEventArgs e)=>R2.Under(this,e.Graphics);
 protected override void OnPaint(PaintEventArgs e){var item=trend.RailEvent;if(item==null)return;float k=DeviceDpi/96f;var g=e.Graphics;g.ScaleTransform(k,k);var box=CardBounds;GoldVisual.Card(g,box);
  Theme.TextAt(g,GoldVisual.Copy("✓ Replenishment observed","✓ 已觀察到補滿"),new(box.X+12,box.Y+6,box.Width-24,20),9,GoldVisual.Light,true);
  GoldVisual.Text(g,GoldVisual.Percent(item.remaining_before)+" → "+GoldVisual.Percent(item.remaining_after),new(box.X+12,box.Y+25,box.Width-24,32),20);
  Theme.TextAt(g,"+"+TrendView.Number(item.delta_pp??0)+GoldVisual.Copy(" percentage points"," 個百分點"),new(box.X+12,box.Y+60,box.Width-24,24),9,GoldVisual.Light);
  float w=Width/k,ix=compact?7:300,iy=compact?106:8;Theme.TextAt(g,GoldVisual.Copy("First observed: ","首次觀察：")+TrendView.Local(item.observed_to,"yyyy-MM-dd HH:mm:ss"),new(ix,iy,w-ix-8,24),9,GoldVisual.Light);
  bool outside=trend.NormalizedX(item.observed_to)<0||trend.NormalizedX(item.observed_to)>1;
  string state=trend.Latest?GoldVisual.Copy("Latest segment · open full event evidence","最近區段 · 查看完整事件證據"):outside?GoldVisual.Copy("Selected event outside view · open to focus","選定事件在視野外 · 查看事件可聚焦"):GoldVisual.Copy("Matches the gold check · cause UNKNOWN","對應金色勾記 · 原因 UNKNOWN");
  Theme.TextAt(g,state,new(ix,iy+24,w-ix-8,24),9,Theme.Muted);
 }
}
