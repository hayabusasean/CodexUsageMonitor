using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
namespace CodexUsageMonitor;

// Phase A is deliberately limited to the three representative windows.
internal static class R2 {
 internal static bool Rollout=true;
 internal static bool Is(Control c)=>!Theme.HighContrast&&(c.FindForm() is HistoryForm or SettingsForm or ResetInfoForm || Rollout&&c.FindForm() is ProductForm);
 internal static string Scene(Control c)=>c.FindForm() is SettingsForm?"settings":c.FindForm() is ResetInfoForm?"radar":"history";
 static readonly Dictionary<string,Bitmap> images=[];
 internal static Bitmap Art(string scene){if(!images.TryGetValue(scene,out var a)){using var s=typeof(R2).Assembly.GetManifestResourceStream("CodexUsageMonitor.R2."+scene+".png")!;using var i=Image.FromStream(s);images[scene]=a=new Bitmap(i);}return a;}
 internal static Bitmap Background(Size size,string scene){var b=new Bitmap(Math.Max(1,size.Width),Math.Max(1,size.Height));using var g=Graphics.FromImage(b);var a=Art(scene);float k=Math.Max(b.Width/(float)a.Width,b.Height/(float)a.Height);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(a,new RectangleF((b.Width-a.Width*k)/2,0,a.Width*k,a.Height*k));using var tint=new SolidBrush(Color.FromArgb(scene=="history"?34:20,3,8,27));g.FillRectangle(tint,0,0,b.Width,b.Height);return b;}
 internal static void Under(Control c,Graphics g){
  if(c.Parent==null){g.Clear(Theme.Canvas);return;}var p=c.Parent;var save=g.Save();g.TranslateTransform(-c.Left,-c.Top);Surface(p,g);g.Restore(save);
 }
 internal static void Surface(Control c,Graphics g){
  if(Theme.HighContrast){g.Clear(Theme.Canvas);return;}if(c is SpacePanel sky){sky.PaintSurface(g);return;}Under(c,g);
  if(c is SectionSurface or RadarAccentPanel or R2Card){float k=c.DeviceDpi/96f;var color=c is RadarAccentPanel a?a.Accent:c is R2Card card?card.Accent:Theme.Electric;Glass(g,new(2,2,c.Width-4,Math.Max(1,c.Height-(c is SectionSurface?12*k:4))),10*k,color);}
 }
 internal static void Glass(Graphics g,RectangleF r,float radius,Color accent){if(r.Width<=0||r.Height<=0)return;g.SmoothingMode=SmoothingMode.AntiAlias;using var path=Theme.Round(r,radius);
  using var tint=new LinearGradientBrush(r,Color.FromArgb(134,8,19,49),Color.FromArgb(86,12,8,35),35);g.FillPath(tint,path);
  for(int i=5;i>=1;i--){using var glow=new Pen(Color.FromArgb(8+i*2,accent),i*1.4f);g.DrawPath(glow,path);}
  using var rim=new LinearGradientBrush(r,Color.FromArgb(190,accent),Color.FromArgb(115,Theme.Brand),25);using var edge=new Pen(rim,1.1f);g.DrawPath(edge,path);
  using var top=new Pen(Color.FromArgb(75,Color.White));g.DrawLine(top,r.Left+radius,r.Top+1,r.Right-radius,r.Top+1);
 }
 internal static void Button(QuietButton b,Graphics g,string state){Under(b,g);float k=b.DeviceDpi/96f;bool primary=b.Primary||b.Role==ButtonRole.Primary,hover=state=="hover",down=state=="pressed";var r=new RectangleF(3*k,3*k,b.Width-6*k,b.Height-6*k);using var p=Theme.Round(r,7*k);g.SmoothingMode=SmoothingMode.AntiAlias;
  Color accent=b.Role==ButtonRole.Danger?Theme.Critical:b.Selected?Theme.Brand:Theme.Electric;
  if(b.Enabled&&(primary||b.Selected)){for(int i=4;i>0;i--){using var halo=new Pen(Color.FromArgb(down?8:hover?38:23,Theme.Brand),i*k);g.DrawPath(halo,p);}using var fill=new LinearGradientBrush(r,Theme.Hex(down?"#39206C":"#6837E8"),Theme.Hex(hover?"#29A9EF":"#2355BA"),12);g.FillPath(fill,p);using var inner=new Pen(Color.FromArgb(180,Color.White),k);g.DrawPath(inner,p);}
  else if(b.Role!=ButtonRole.Ghost||hover){using var fill=new SolidBrush(Color.FromArgb(hover?160:90,11,24,59));g.FillPath(fill,p);using var border=new Pen(Color.FromArgb(hover?210:120,accent),k);g.DrawPath(border,p);}
  var tr=Rectangle.Round(r);if(primary){using var icon=new Pen(Theme.Text,1.4f*k);float x=r.Left+14*k,y=r.Top+r.Height/2;g.DrawLine(icon,x,y,x+9*k,y);g.DrawLines(icon,new PointF[]{new(x+5*k,y-4*k),new(x+9*k,y),new(x+5*k,y+4*k)});tr.X+=(int)(16*k);tr.Width-=(int)(16*k);}
  TextRenderer.DrawText(g,b.Text,b.Font,tr,b.Enabled?Theme.Text:Theme.Micro,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
  if(b.Focused){using var p2=Theme.Round(new RectangleF(6*k,6*k,b.Width-12*k,b.Height-12*k),4*k);using var pen=new Pen(Theme.Accent,k){DashStyle=DashStyle.Dot};g.DrawPath(pen,p2);}
 }
 internal static void Switch(DarkCheck c,Graphics g){Under(c,g);float k=c.DeviceDpi/96f;g.SmoothingMode=SmoothingMode.AntiAlias;var rect=new RectangleF(1,(c.Height-18*k)/2,32*k,18*k);using var path=Theme.Round(rect,9*k);using var fill=new LinearGradientBrush(rect,c.Checked?Theme.Hex("#7739EF"):Theme.Hex("#263856"),c.Checked?Theme.Hex("#536EF4"):Theme.Hex("#34425F"),0f);g.FillPath(fill,path);using var ball=new SolidBrush(c.Checked?Color.White:Theme.Muted);g.FillEllipse(ball,rect.X+(c.Checked?16:2)*k,rect.Y+2*k,14*k,14*k);if(c.Focused){using var edge=new Pen(Theme.Electric);g.DrawPath(edge,path);}TextRenderer.DrawText(g,c.Text,c.Font,new Rectangle((int)(42*k),0,c.Width-(int)(42*k),c.Height),c.Enabled?Theme.Text:Theme.Micro,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);}
 internal static void Install(ProductForm f,Panel body,FlowLayoutPanel footer){if(!Is(f))return;f.FormBorderStyle=FormBorderStyle.None;var chrome=new R2Chrome(f){Dock=DockStyle.Top,Height=44,Tag=44};f.Controls.Add(chrome);body.BringToFront();footer.BackColor=Theme.Canvas;
  f.TextChanged+=(_,_)=>chrome.Invalidate();f.Padding=new(1);f.Resize+=(_,_)=>{f.MaximumSize=Screen.FromControl(f).WorkingArea.Size;};
 }
 internal static Label Caption(string en,string zh,int height=24){var l=Theme.Label("",height,9,Theme.Muted);L.Watch(l,()=>l.Text=l.AccessibleName=L.Language=="zh-TW"?zh:en);return l;}
}
internal sealed class R2Chrome:Control {
 readonly Form form;int hover=-1;
 [DllImport("user32.dll")]static extern bool ReleaseCapture();
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
 public R2Chrome(Form f){form=f;DoubleBuffered=true;
  for(int i=0;i<3;i++){int action=i;var b=new R2WindowButton(i){Dock=DockStyle.Right,Width=44,AccessibleName=i==0?"Minimize window":i==1?"Maximize or restore window":"Close window"};b.Click+=(_,_)=>{if(action==0)form.WindowState=FormWindowState.Minimized;else if(action==1)Toggle();else form.Close();};L.Watch(b,()=>b.AccessibleName=L.Language=="zh-TW"?(action==0?"最小化視窗":action==1?"最大化或還原視窗":"關閉視窗"):(action==0?"Minimize window":action==1?"Maximize or restore window":"Close window"));Controls.Add(b);}
SetStyle(ControlStyles.StandardDoubleClick,true);MouseMove+=(_,e)=>{hover=e.X>Width-132*DeviceDpi/96f?(int)((e.X-(Width-132*DeviceDpi/96f))/(44*DeviceDpi/96f)):-1;Invalidate();};MouseLeave+=(_,_)=>{hover=-1;Invalidate();};}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;float k=DeviceDpi/96f;g.Clear(Theme.Canvas);using var arc=new Pen(Theme.Electric,3*k);g.DrawArc(arc,16*k,10*k,22*k,22*k,30,250);using var arc2=new Pen(Theme.Brand,3*k);g.DrawArc(arc2,20*k,13*k,18*k,18*k,210,210);TextRenderer.DrawText(g,"CodexUsageMonitor  /  "+form.Text,Font,new Rectangle((int)(52*k),0,Width-(int)(195*k),Height),Theme.Text,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
  for(int i=0;i<3;i++){var r=new Rectangle((int)(Width-(3-i)*44*k),0,(int)(44*k),Height);if(hover==i){using var h=new SolidBrush(i==2?Theme.Critical:Theme.Surface3);g.FillRectangle(h,r);}using var p=new Pen(Theme.Text,k);float x=r.X+r.Width/2,y=Height/2;if(i==0)g.DrawLine(p,x-5*k,y+3*k,x+5*k,y+3*k);if(i==1)g.DrawRectangle(p,x-4*k,y-4*k,8*k,8*k);if(i==2){g.DrawLine(p,x-4*k,y-4*k,x+4*k,y+4*k);g.DrawLine(p,x+4*k,y-4*k,x-4*k,y+4*k);}}
 }
 protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;float k=DeviceDpi/96f;if(e.X>=Width-132*k){int n=(int)((e.X-Width+132*k)/(44*k));if(n==0)form.WindowState=FormWindowState.Minimized;else if(n==1)Toggle();else form.Close();}else{ReleaseCapture();SendMessage(form.Handle,0xA1,new IntPtr(2),IntPtr.Zero);}}
 protected override void OnMouseDoubleClick(MouseEventArgs e){if(e.X<Width-132*DeviceDpi/96f)Toggle();base.OnMouseDoubleClick(e);}
 void Toggle(){form.MaximumSize=Screen.FromControl(form).WorkingArea.Size;form.WindowState=form.WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;}
}
internal sealed class R2Card:Panel {
 internal Color Accent=Theme.Electric;internal bool FitHeight=true;bool layout;
 public R2Card(params Control[] content){DoubleBuffered=true;BackColor=Color.Transparent;Padding=new(20);foreach(var c in content.Reverse()){c.Dock=DockStyle.Top;Controls.Add(c);c.VisibleChanged+=(_,_)=>PerformLayout();c.SizeChanged+=(_,_)=>PerformLayout();}}
 protected override void OnPaintBackground(PaintEventArgs e)=>R2.Surface(this,e.Graphics);
 protected override void OnLayout(LayoutEventArgs e){if(layout)return;layout=true;try{float k=DeviceDpi/96f;Padding=new((int)(18*k));if(Visible&&FitHeight)Height=Controls.Cast<Control>().Where(c=>c.Visible).Sum(c=>c.Height)+Padding.Vertical;base.OnLayout(e);}finally{layout=false;}}
}
internal sealed class R2Grid:DataGridView {
 internal HashSet<string> GoldSampleIds=[];
 Bitmap? backdrop;int backdropKey;internal int BackdropBuilds;
 void PrepareBackdrop(){var hash=new HashCode();hash.Add(DeviceDpi);hash.Add(Theme.Canvas);hash.Add(Theme.HighContrast);hash.Add(R2.Scene(this));for(Control? c=this;c!=null;c=c.Parent){hash.Add(c);hash.Add(c.Bounds);hash.Add(c.BackColor);if(c is R2Card card)hash.Add(card.Accent);}int key=hash.ToHashCode();if(backdrop!=null&&key==backdropKey)return;backdropKey=key;BackdropBuilds++;var next=new Bitmap(Math.Max(1,Width),Math.Max(1,Height));using(var g=Graphics.FromImage(next)){g.Clear(Theme.Canvas);R2.Under(this,g);}var old=backdrop;backdrop=next;old?.Dispose();}
 protected override void Dispose(bool disposing){if(disposing)backdrop?.Dispose();base.Dispose(disposing);}
 public R2Grid(){DoubleBuffered=true;ScrollBars=ScrollBars.None;}
 protected override void OnPaintBackground(PaintEventArgs e){if(R2.Is(this)){PrepareBackdrop();e.Graphics.DrawImageUnscaled(backdrop!,Point.Empty);}else base.OnPaintBackground(e);}
 protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e){
  if(!R2.Is(this)||e.Graphics==null||e.ColumnIndex<0){base.OnCellPainting(e);return;}
  var g=e.Graphics;var old=g.Save();g.SetClip(e.CellBounds);if(backdrop!=null)g.DrawImageUnscaled(backdrop,Point.Empty);bool selected=(e.State&DataGridViewElementStates.Selected)!=0;using var fill=new SolidBrush(e.RowIndex<0?Color.FromArgb(175,8,21,51):selected?Color.FromArgb(85,19,85,155):Color.FromArgb(e.RowIndex%2==0?98:70,4,11,31));g.FillRectangle(fill,e.CellBounds);using var line=new Pen(Color.FromArgb(32,Theme.Electric));g.DrawLine(line,e.CellBounds.Left,e.CellBounds.Bottom-1,e.CellBounds.Right,e.CellBounds.Bottom-1);
  var box=e.CellBounds;box.Inflate(-(int)(10*DeviceDpi/96f),0);var flags=TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine;flags|=e.ColumnIndex is 1 or 2?TextFormatFlags.Right:TextFormatFlags.Left;bool gold=e.RowIndex>=0&&Rows[e.RowIndex].Tag is UsageSample sample&&GoldSampleIds.Contains(sample.sample_id);if(gold&&e.ColumnIndex==0){using var edge=new SolidBrush(GoldVisual.Light);g.FillRectangle(edge,e.CellBounds.X,e.CellBounds.Y,3*DeviceDpi/96f,e.CellBounds.Height);}TextRenderer.DrawText(g,(gold&&e.ColumnIndex==3?"✓ ":"")+Convert.ToString(e.FormattedValue),e.CellStyle!.Font,box,gold&&e.ColumnIndex==3?GoldVisual.Light:selected?Theme.Electric:e.RowIndex<0?Theme.Muted:Theme.Text,flags);g.Restore(old);e.Handled=true;
 }
 bool railHeld,railHover;float railGrab;
 RectangleF Rail(){float k=DeviceDpi/96f,top=ColumnHeadersHeight,span=Math.Max(1,Height-top);int visible=Math.Max(1,DisplayedRowCount(false));float h=Math.Max(25*k,span*Math.Min(1,visible/(float)Math.Max(1,Rows.Count)));return new(Width-9*k,top+(span-h)*Math.Max(0,FirstDisplayedScrollingRowIndex)/Math.Max(1,Rows.Count-visible),5*k,h);}
 protected override void OnPaint(PaintEventArgs e){if(R2.Is(this))PrepareBackdrop();base.OnPaint(e);if(R2.Is(this)&&backdrop!=null&&Rows.Count<=DisplayedRowCount(false)){int bottom=ColumnHeadersHeight;foreach(DataGridViewRow row in Rows)if(row.Visible)bottom=Math.Max(bottom,GetRowDisplayRectangle(row.Index,false).Bottom);if(bottom<Height){var clip=e.Graphics.Save();e.Graphics.SetClip(new Rectangle(0,bottom,Width,Height-bottom));e.Graphics.DrawImageUnscaled(backdrop,Point.Empty);using var glass=new SolidBrush(Color.FromArgb(70,4,11,31));e.Graphics.FillRectangle(glass,0,bottom,Width,Height-bottom);e.Graphics.Restore(clip);}}if(Rows.Count<=DisplayedRowCount(false))return;using var bg=new SolidBrush(Theme.Canvas);float k=DeviceDpi/96f;e.Graphics.FillRectangle(bg,Width-12*k,ColumnHeadersHeight,12*k,Height-ColumnHeadersHeight);using var b=new SolidBrush(railHeld||railHover?Theme.Electric:Theme.Brand);using var path=Theme.Round(Rail(),3*k);e.Graphics.FillPath(b,path);}
 void RailTo(int y){if(Rows.Count==0)return;int shown=Math.Max(1,DisplayedRowCount(false));var thumb=Rail();float travel=Math.Max(1,Height-ColumnHeadersHeight-thumb.Height);FirstDisplayedScrollingRowIndex=Math.Clamp((int)Math.Round((y-ColumnHeadersHeight-railGrab)/travel*(Rows.Count-shown)),0,Math.Max(0,Rows.Count-shown));Invalidate();}
 protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left&&e.X>=Width-14*DeviceDpi/96f&&Rows.Count>DisplayedRowCount(false)){var thumb=Rail();railHeld=true;Capture=true;bool inside=e.Y>=thumb.Top&&e.Y<=thumb.Bottom;railGrab=inside?e.Y-thumb.Top:thumb.Height/2;if(!inside)RailTo(e.Y);return;}base.OnMouseDown(e);}
 protected override void OnMouseCaptureChanged(EventArgs e){if(!Capture)railHeld=false;base.OnMouseCaptureChanged(e);}
 protected override void OnMouseMove(MouseEventArgs e){railHover=e.X>=Width-14*DeviceDpi/96f;if(railHeld)RailTo(e.Y);else base.OnMouseMove(e);Invalidate(new Rectangle(Width-20,0,20,Height));}
 protected override void OnMouseUp(MouseEventArgs e){if(railHeld){railHeld=false;Capture=false;Invalidate();return;}base.OnMouseUp(e);}
 protected override void OnMouseWheel(MouseEventArgs e){if(!R2.Is(this)){base.OnMouseWheel(e);return;}if(Rows.Count>0){int n=Math.Clamp(Math.Max(0,FirstDisplayedScrollingRowIndex)-Math.Sign(e.Delta)*3,0,Math.Max(0,Rows.Count-DisplayedRowCount(false)));FirstDisplayedScrollingRowIndex=n;Invalidate();}}
}
internal sealed class R2Columns:Panel {
 readonly Control[] columns;bool arranging;
 public R2Columns(params Control[] content){columns=content;DoubleBuffered=true;BackColor=Color.Transparent;Dock=DockStyle.Top;foreach(var c in content){c.Dock=DockStyle.None;Controls.Add(c);c.SizeChanged+=(_,_)=>PerformLayout();}}
 protected override void OnLayout(LayoutEventArgs e){if(arranging)return;arranging=true;try{float k=DeviceDpi/96f;int gap=(int)(16*k),count=Width<700*k?1:columns.Length==3&&Width<1000*k?2:columns.Length,w=(Width-gap*(count-1))/count,y=0;for(int first=0;first<columns.Length;first+=count){int tallest=0;for(int j=0;j<count&&first+j<columns.Length;j++){var col=columns[first+j];col.SetBounds(j*(w+gap),y,w,col.Height);col.PerformLayout();tallest=Math.Max(tallest,col.Height);}y+=tallest+gap;}Height=y-gap;base.OnLayout(e);}finally{arranging=false;}}

}
internal sealed class R2Stack:Panel {
 bool arranging;readonly Control[] children;
 public R2Stack(params Control[] content){children=content;BackColor=Color.Transparent;foreach(var c in content.Reverse()){c.Dock=DockStyle.Top;Controls.Add(c);c.SizeChanged+=(_,_)=>PerformLayout();c.VisibleChanged+=(_,_)=>PerformLayout();}}
 protected override void OnLayout(LayoutEventArgs e){if(arranging)return;arranging=true;try{Height=children.Where(x=>x.Visible).Sum(x=>x.Height);base.OnLayout(e);}finally{arranging=false;}}
}

internal sealed class R2WindowButton:Button {
 readonly int action;bool hover;
 public R2WindowButton(int value){action=value;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;TabStop=true;Text=value==0?"−":value==1?"□":"×";SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);MouseEnter+=(_,_)=>{hover=true;Invalidate();};MouseLeave+=(_,_)=>{hover=false;Invalidate();};}
 protected override void OnPaint(PaintEventArgs e){float k=DeviceDpi/96f;Color bg=Theme.HighContrast?(hover?SystemColors.Highlight:SystemColors.Window):hover?(action==2?Theme.Critical:Theme.Surface3):Theme.Canvas;Color fg=Theme.HighContrast&&hover?SystemColors.HighlightText:Theme.Text;e.Graphics.Clear(bg);using var p=new Pen(fg,k);float x=Width/2f,y=Height/2f;if(action==0)e.Graphics.DrawLine(p,x-5*k,y+3*k,x+5*k,y+3*k);if(action==1)e.Graphics.DrawRectangle(p,x-4*k,y-4*k,8*k,8*k);if(action==2){e.Graphics.DrawLine(p,x-4*k,y-4*k,x+4*k,y+4*k);e.Graphics.DrawLine(p,x+4*k,y-4*k,x-4*k,y+4*k);}if(Focused)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(4,4,Width-8,Height-8),Theme.Electric,Theme.Canvas);}
}
