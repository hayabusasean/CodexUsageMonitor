using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
namespace CodexUsageMonitor;
internal sealed class DarkChoice:Control {
 internal int LogicalWidth=160;public readonly List<object> Items=[];int selected=-1;Form? popup;public event EventHandler? SelectedIndexChanged;
 [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]public int SelectedIndex{get=>selected;set{int next=value>=0&&value<Items.Count?value:-1;if(next==selected)return;selected=next;Invalidate();SelectedIndexChanged?.Invoke(this,EventArgs.Empty);}}
 [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]public object? SelectedItem{get=>selected<0?null:Items[selected];set=>SelectedIndex=Items.IndexOf(value!);}
 [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]public ComboBoxStyle DropDownStyle{get;set;}
 protected override void OnSizeChanged(EventArgs e){if(Parent==null)LogicalWidth=Width;base.OnSizeChanged(e);}
 protected override AccessibleObject CreateAccessibilityInstance()=>new ChoiceAccess(this);
 sealed class ChoiceAccess(DarkChoice owner):ControlAccessibleObject(owner){public override string? Value{get=>owner.SelectedItem?.ToString()??"—";set{}}public override string? DefaultAction=>L.T("Common.Open");public override void DoDefaultAction(){if(owner.IsHandleCreated)owner.BeginInvoke(owner.OpenChoices);}}
 public DarkChoice(){Size=new(160,36);TabStop=true;DoubleBuffered=true;Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.ComboBox;}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Canvas);float k=DeviceDpi/96f;using var path=Theme.Round(new RectangleF(1,1,Width-2,Height-2),8*k);using var fill=new SolidBrush(Theme.Surface);using var border=new Pen(Focused?Theme.Accent:Theme.Border);g.FillPath(fill,path);g.DrawPath(border,path);
  TextRenderer.DrawText(g,SelectedItem?.ToString()??"—",Font,new Rectangle((int)(12*k),0,Width-(int)(40*k),Height),Enabled?Theme.Text:Theme.Micro,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
  using var p=new Pen(Theme.Muted,1.2f*k);float x=Width-18*k,y=Height/2f;g.DrawLines(p,new PointF[]{new(x-4*k,y-2*k),new(x,y+2*k),new(x+4*k,y-2*k)});}
 protected override void OnClick(EventArgs e){Focus();OpenChoices();base.OnClick(e);}
 internal void OpenChoices(){if(popup!=null){popup.Close();return;}int row=(int)(36*DeviceDpi/96f);popup=new Form{FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,BackColor=Theme.Surface,TopMost=true,Size=new(Width,Math.Min(Items.Count,10)*row+8)};
  var p=popup;var point=PointToScreen(new(0,Height+4));var a=Screen.FromControl(this).WorkingArea;p.Location=new(Math.Clamp(point.X,a.Left,a.Right-p.Width),point.Y+p.Height>a.Bottom?PointToScreen(Point.Empty).Y-p.Height-4:point.Y);
  for(int i=0;i<Math.Min(Items.Count,10);i++){int ix=i;var b=Theme.Button(Items[i].ToString()??"",(_,_)=>{SelectedIndex=ix;p.Close();});b.Bounds=new(4,4+i*row,Width-8,row);b.Font=Font;p.Controls.Add(b);}
  p.Deactivate+=(_,_)=>p.Close();p.FormClosed+=(_,_)=>{popup=null;p.Dispose();Invalidate();};p.Show(this.FindForm());p.Activate();
 }
 protected override bool IsInputKey(Keys key)=>key is Keys.Up or Keys.Down||base.IsInputKey(key);
 protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Space||e.KeyCode==Keys.Enter)OpenChoices();else if(e.KeyCode==Keys.Up)SelectedIndex=Math.Max(0,SelectedIndex-1);else if(e.KeyCode==Keys.Down)SelectedIndex=Math.Min(Items.Count-1,SelectedIndex+1);base.OnKeyDown(e);}
 protected override void Dispose(bool disposing){if(disposing)popup?.Close();base.Dispose(disposing);}
}
internal sealed class DarkCheck:CheckBox {
 public DarkCheck(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);FlatStyle=FlatStyle.Flat;}
 public override Size GetPreferredSize(Size proposedSize){var text=TextRenderer.MeasureText(Text,Font);return new(text.Width+(int)(32*DeviceDpi/96f),Math.Max(text.Height+8,(int)(28*DeviceDpi/96f)));}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Canvas);float k=DeviceDpi/96f;float side=14*k,y=(Height-side)/2;using var path=Theme.Round(new(1,y,side,side),3*k);using var b=new SolidBrush(Checked?Theme.Accent:Theme.Surface);using var p=new Pen(Focused?Theme.Accent:Theme.Muted);g.FillPath(b,path);g.DrawPath(p,path);
  if(Checked){using var tick=new Pen(Theme.Canvas,1.8f*k);g.DrawLines(tick,new PointF[]{new(4*k,y+7*k),new(7*k,y+10*k),new(12*k,y+4*k)});}
  TextRenderer.DrawText(g,Text,Font,new Rectangle((int)(24*k),0,Width-(int)(24*k),Height),Enabled?Theme.Text:Theme.Micro,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.SingleLine);
 }
}
internal sealed class ScrollRail:Control {
 readonly Panel panel;bool held;int drag,initial;
 public ScrollRail(Panel p){panel=p;DoubleBuffered=true;BackColor=Theme.Canvas;Width=12;TabStop=false;Cursor=Cursors.Hand;
  panel.Scroll+=(_,_)=>Sync();panel.Layout+=(_,_)=>Sync();panel.SizeChanged+=(_,_)=>Sync();
 }
 public void Sync(){
  if(IsDisposed||!panel.IsHandleCreated)return;
  bool overflow=panel.DisplayRectangle.Height>panel.ClientSize.Height+2;
  Visible=overflow;HideNative(panel.Handle,3,false);if(Parent!=null){Bounds=new(panel.Right-12,panel.Top+8,12,Math.Max(10,panel.Height-16));BringToFront();}Invalidate();
 }
 (float y,float height) Thumb(){int content=panel.DisplayRectangle.Height;float h=Math.Max(28,Height*Math.Min(1,panel.ClientSize.Height/(float)Math.Max(1,content)));float y=(Height-h)*(-panel.AutoScrollPosition.Y)/(float)Math.Max(1,content-panel.ClientSize.Height);return(y,h);}
 protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Theme.Canvas);var t=Thumb();using var b=new SolidBrush(held?Theme.Muted:Theme.Border);using var path=Theme.Round(new RectangleF(3,t.y,6,t.height),3);e.Graphics.FillPath(b,path);}
 protected override void OnMouseDown(MouseEventArgs e){held=true;Capture=true;drag=e.Y;initial=-panel.AutoScrollPosition.Y;var t=Thumb();if(e.Y<t.y||e.Y>t.y+t.height){panel.AutoScrollPosition=new(0,Math.Max(0,(int)((e.Y/(float)Height)*panel.DisplayRectangle.Height)));initial=-panel.AutoScrollPosition.Y;}Invalidate();}
 protected override void OnMouseMove(MouseEventArgs e){if(!held)return;int value=initial+(int)((e.Y-drag)*panel.DisplayRectangle.Height/(float)Math.Max(1,Height));panel.AutoScrollPosition=new(0,Math.Clamp(value,0,Math.Max(0,panel.DisplayRectangle.Height-panel.ClientSize.Height)));Sync();}
 protected override void OnMouseUp(MouseEventArgs e){held=false;Capture=false;Invalidate();}
 [DllImport("user32.dll",EntryPoint="ShowScrollBar")]static extern bool HideNative(IntPtr h,int bar,bool show);
}


