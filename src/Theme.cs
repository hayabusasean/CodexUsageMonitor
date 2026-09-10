using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Microsoft.Win32;
namespace CodexUsageMonitor;

// Logical design tokens shared by every native surface.
internal static class Theme {
 internal static bool? TestHighContrast {get;set;}
 public static bool HighContrast=>TestHighContrast??SystemInformation.HighContrast;
 static readonly Color NormalCanvas=Hex("#111418"),NormalSurface=Hex("#20252B"),NormalSecondary=Hex("#181C21"),NormalHover=Hex("#252B33"),NormalBorder=Hex("#2B3138"),NormalText=Hex("#F2F4F6"),NormalMuted=Hex("#A8B0BA"),NormalMicro=Hex("#949DA8"),NormalAccent=Hex("#61D6B4"),NormalWarning=Hex("#F2B84B"),NormalCritical=Hex("#FF6673"),NormalResetAlert=Hex("#FF4D5E");
 public static Color Canvas=>HighContrast?SystemColors.Window:NormalCanvas;
 public static Color Surface=>HighContrast?SystemColors.Window:NormalSurface;
 public static Color Secondary=>HighContrast?SystemColors.Control:NormalSecondary;
 public static Color Hover=>HighContrast?SystemColors.Highlight:NormalHover;
 public static Color Border=>HighContrast?SystemColors.WindowText:NormalBorder;
 public static Color Text=>HighContrast?SystemColors.WindowText:NormalText;
 public static Color Muted=>HighContrast?SystemColors.WindowText:NormalMuted;
 public static Color Micro=>HighContrast?SystemColors.WindowText:NormalMicro;
 public static Color Accent=>HighContrast?SystemColors.Highlight:NormalAccent;
 public static Color Warning=>HighContrast?SystemColors.WindowText:NormalWarning;
 public static Color Critical=>HighContrast?SystemColors.WindowText:NormalCritical;
 public static Color ResetAlert=>HighContrast?SystemColors.Highlight:NormalResetAlert;
 public static Color SelectedText=>HighContrast?SystemColors.HighlightText:NormalText;
 readonly record struct ControlPalette(Color Fore,Color Back);
 readonly record struct CellPalette(Color Fore,Color Back,Color SelectedFore,Color SelectedBack){
  public static CellPalette Capture(DataGridViewCellStyle value,Color fore,Color back,Color selectedFore,Color selectedBack)=>new(
   value.ForeColor.IsSystemColor?fore:value.ForeColor,value.BackColor.IsSystemColor?back:value.BackColor,
   value.SelectionForeColor.IsSystemColor?selectedFore:value.SelectionForeColor,value.SelectionBackColor.IsSystemColor?selectedBack:value.SelectionBackColor);
  public void Restore(DataGridViewCellStyle value){value.ForeColor=Fore;value.BackColor=Back;value.SelectionForeColor=SelectedFore;value.SelectionBackColor=SelectedBack;}
 }
 sealed class GridPalette {
  public readonly CellPalette Default,Header,Rows,Alternating;
  public readonly Color Background,Lines;
  public GridPalette(DataGridView g){
   var selected=Hex("#24483F");Default=CellPalette.Capture(g.DefaultCellStyle,NormalText,NormalSurface,NormalText,selected);
   Header=CellPalette.Capture(g.ColumnHeadersDefaultCellStyle,NormalMuted,NormalSecondary,NormalMuted,NormalSecondary);
   Rows=CellPalette.Capture(g.RowHeadersDefaultCellStyle,NormalText,NormalSurface,NormalText,selected);
   Alternating=CellPalette.Capture(g.AlternatingRowsDefaultCellStyle,NormalText,NormalSurface,NormalText,selected);
   Background=g.BackgroundColor.IsSystemColor?NormalSurface:g.BackgroundColor;Lines=g.GridColor.IsSystemColor?NormalBorder:g.GridColor;
  }
  public void Restore(DataGridView g){Default.Restore(g.DefaultCellStyle);Header.Restore(g.ColumnHeadersDefaultCellStyle);Rows.Restore(g.RowHeadersDefaultCellStyle);Alternating.Restore(g.AlternatingRowsDefaultCellStyle);g.BackgroundColor=Background;g.GridColor=Lines;}
 }
 sealed class PaletteState {
  public bool WasHighContrast;
  public readonly Dictionary<Control,ControlPalette> Controls=[];
  public readonly Dictionary<DataGridView,GridPalette> Grids=[];
 }
 static readonly ConditionalWeakTable<Control,PaletteState> Palettes=new();
 static Color NormalForeground(Color c)=>!c.IsSystemColor?c:c==SystemColors.Highlight?NormalAccent:c==SystemColors.GrayText?NormalMicro:NormalText;
 static Color NormalBackground(Control control,Color c)=>!c.IsSystemColor?c:control is TextBoxBase or DataGridView or ButtonBase?NormalSurface:NormalCanvas;
 public static void RefreshPalette(Control root){
  if(root.IsDisposed)return;
  var state=Palettes.GetValue(root,_=>new PaletteState());bool highContrast=HighContrast;
  IEnumerable<Control> Walk(Control c){yield return c;foreach(Control child in c.Controls)foreach(var item in Walk(child))yield return item;}
  root.SuspendLayout();
  try{
   foreach(var c in Walk(root)){
    if(c.IsDisposed)continue;
    if(!state.WasHighContrast||!state.Controls.ContainsKey(c))state.Controls[c]=new(NormalForeground(c.ForeColor),NormalBackground(c,c.BackColor));
    if(highContrast){c.BackColor=SystemColors.Window;c.ForeColor=c.Enabled?SystemColors.WindowText:SystemColors.GrayText;}
    else if(state.WasHighContrast&&state.Controls.TryGetValue(c,out var saved)){c.ForeColor=saved.Fore;c.BackColor=saved.Back;}
    if(c is DataGridView grid){
     if(!state.WasHighContrast||!state.Grids.ContainsKey(grid))state.Grids[grid]=new(grid);
     if(highContrast){
      grid.BackgroundColor=SystemColors.Window;grid.GridColor=SystemColors.WindowText;
      foreach(var cell in new[]{grid.DefaultCellStyle,grid.ColumnHeadersDefaultCellStyle,grid.RowHeadersDefaultCellStyle,grid.AlternatingRowsDefaultCellStyle}){
       cell.BackColor=SystemColors.Window;cell.ForeColor=SystemColors.WindowText;cell.SelectionBackColor=SystemColors.Highlight;cell.SelectionForeColor=SystemColors.HighlightText;
      }
     }else if(state.WasHighContrast&&state.Grids.TryGetValue(grid,out var previous))previous.Restore(grid);
    }
    c.Invalidate();
   }
   state.WasHighContrast=highContrast;
  }finally{root.ResumeLayout(false);}
  if(root is Form form&&form.IsHandleCreated)DarkWindow(form);
  root.Invalidate(true);
 }

 public const int Space4=4,Space8=8,Space12=12,Space16=16,Space24=24,OuterRadius=12,ControlRadius=8;
 public static readonly string FontName=System.Drawing.FontFamily.Families.Any(x=>x.Name=="Segoe UI Variable")?"Segoe UI Variable":"Segoe UI";
 public static Color Hex(string s)=>ColorTranslator.FromHtml(s);
 internal static float TestControlTextScale=1;
 public static Font Font(float points=10.5f,bool bold=false)=>new(FontName,points*TestControlTextScale,bold?FontStyle.Bold:FontStyle.Regular);
 public static Font PixelFont(float points,bool bold=false)=>new(FontName,points*96/72,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);
 public static Color QuotaColor(decimal? remaining)=>remaining<=5?Critical:remaining<=20?Warning:Text;
 public static void TextAt(Graphics g,string text,RectangleF rect,float points,Color color,bool bold=false,StringAlignment align=StringAlignment.Near){
  using var font=PixelFont(points,bold);using var brush=new SolidBrush(color);using var sf=new StringFormat{Alignment=align,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter};g.DrawString(text,font,brush,rect,sf);
 }
 public static Label Label(string text,int height=28,float size=10.5f,Color? color=null)=>new(){Text=text,Height=height,Tag=height,AutoSize=false,ForeColor=color??Text,Font=Font(size),TextAlign=ContentAlignment.MiddleLeft,Dock=DockStyle.Top,AccessibleName=text};
 public static int MeasureButton(string text){using var font=PixelFont(10.5f,false);return Math.Max(88,TextRenderer.MeasureText(text,font).Width+24);}
 public static Button Button(string text,EventHandler? click=null,bool primary=false){var b=new QuietButton{Text=text,AccessibleName=text,Height=36,Width=MeasureButton(text),Primary=primary,Font=Font(),Cursor=Cursors.Hand,Margin=new(0,0,8,0)};if(click!=null)b.Click+=click;return b;}
 public static FlowLayoutPanel Row(params Control[] items){var p=new FlowLayoutPanel{Dock=DockStyle.Top,Height=44,Tag=44,WrapContents=false,Padding=new(0,4,0,4)};p.Controls.AddRange(items);return p;}
 public static CheckBox Check(string text,bool value=false){var c=new DarkCheck{Text=text,Checked=value,AccessibleName=text,AutoSize=true,Height=32,ForeColor=Text,FlatStyle=FlatStyle.Flat,Margin=new(0,8,16,4)};c.FlatAppearance.CheckedBackColor=Accent;c.FlatAppearance.BorderColor=Muted;return c;}
 public static void Style(Control c){
  c.Font=Font();c.ForeColor=Text;c.BackColor=Surface;
  if(c is TextBox t)t.BorderStyle=BorderStyle.FixedSingle;
  if(c is ComboBox box){box.FlatStyle=FlatStyle.Flat;box.BackColor=Surface;box.DrawMode=DrawMode.OwnerDrawFixed;box.ItemHeight=24;
   box.DrawItem+=(_,e)=>{if(e.Index<0)return;using var b=new SolidBrush((e.State&DrawItemState.Selected)!=0?Hover:Surface);e.Graphics.FillRectangle(b,e.Bounds);TextRenderer.DrawText(e.Graphics,box.Items[e.Index]?.ToString(),box.Font,e.Bounds,Text,TextFormatFlags.VerticalCenter|TextFormatFlags.Left);};}
  foreach(Control child in c.Controls)Style(child);
 }
 public static GraphicsPath Round(RectangleF r,float radius){var p=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
 public static void PaintCard(Graphics g,Rectangle r){g.SmoothingMode=SmoothingMode.AntiAlias;using var path=Round(r,OuterRadius);using var fill=new SolidBrush(Surface);using var pen=new Pen(Border);g.FillPath(fill,path);g.DrawPath(pen,path);}
 public static void DarkWindow(Form f){int yes=HighContrast?0:1;try{DwmSetWindowAttribute(f.Handle,20,ref yes,4);DwmSetWindowAttribute(f.Handle,19,ref yes,4);}catch{}}
 [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr hwnd,int attr,ref int val,int size);
}
internal sealed class QuietButton:Button {
 public bool Primary;internal int LogicalWidth=88;bool hover,down;
 protected override void OnSizeChanged(EventArgs e){if(Parent==null)LogicalWidth=Width;base.OnSizeChanged(e);}
 public QuietButton(){FlatStyle=FlatStyle.Flat;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);FlatAppearance.BorderSize=0;SizeChanged+=(_,_)=>{using var p=Theme.Round(new RectangleF(0,0,Width,Height),8*DeviceDpi/96f);var old=Region;Region=new Region(p);old?.Dispose();};}
 protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
 protected override void OnMouseLeave(EventArgs e){hover=false;down=false;Invalidate();base.OnMouseLeave(e);}
 protected override void OnMouseDown(MouseEventArgs e){down=true;Invalidate();base.OnMouseDown(e);}
 protected override void OnMouseUp(MouseEventArgs e){down=false;Invalidate();base.OnMouseUp(e);}
 protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(Parent?.BackColor??Theme.Canvas);}
 protected override void OnPaint(PaintEventArgs e){
  var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;using var path=Theme.Round(new RectangleF(1,1,Width-2,Height-2),8*DeviceDpi/96f);
  bool selected=Primary||hover||down;
  Color background=Theme.HighContrast?(!Enabled?SystemColors.Control:selected?SystemColors.Highlight:SystemColors.Window):!Enabled?Theme.Secondary:Primary?(down?Theme.Accent:hover?Theme.Hex("#78E1C2"):Theme.Accent):down?Theme.Border:hover?Theme.Hover:Theme.Surface;
  Color foreground=Theme.HighContrast?(!Enabled?SystemColors.GrayText:selected?SystemColors.HighlightText:SystemColors.WindowText):!Enabled?Theme.Micro:Primary?Theme.Canvas:Theme.Text;
  using var b=new SolidBrush(background);g.FillPath(b,path);
  using var p=new Pen(Focused?Theme.Accent:Theme.Border);if(!Primary||Focused)g.DrawPath(p,path);
  TextRenderer.DrawText(g,Text,Font,ClientRectangle,foreground,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
  if(Theme.HighContrast&&Focused)ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(ClientRectangle,-5,-5),foreground,background);
 }
}
internal sealed class DarkMenuRenderer:ToolStripProfessionalRenderer {
 public DarkMenuRenderer():base(new Colors()){RoundedEdges=false;}
 protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){e.TextColor=Theme.HighContrast?(!e.Item.Enabled?SystemColors.GrayText:e.Item.Selected?SystemColors.HighlightText:SystemColors.WindowText):e.Item.Enabled?Theme.Text:Theme.Micro;base.OnRenderItemText(e);}
 sealed class Colors:ProfessionalColorTable {
  public override Color ToolStripDropDownBackground=>Theme.Surface;public override Color MenuItemSelected=>Theme.Hover;
  public override Color MenuItemBorder=>Theme.Border;public override Color MenuBorder=>Theme.Border;
  public override Color ImageMarginGradientBegin=>Theme.Surface;public override Color ImageMarginGradientMiddle=>Theme.Surface;public override Color ImageMarginGradientEnd=>Theme.Surface;
  public override Color SeparatorDark=>Theme.Border;public override Color SeparatorLight=>Theme.Border;
 }
}
internal class ProductForm:Form {
 protected readonly Panel Body=new(){Dock=DockStyle.Fill,AutoScroll=true,Padding=new(24)};
 readonly UserPreferenceChangedEventHandler preferenceChanged;
 protected readonly FlowLayoutPanel Footer=new(){Dock=DockStyle.Bottom,Height=64,Padding=new(24,12,16,12),FlowDirection=FlowDirection.RightToLeft,BackColor=Theme.Canvas};
 public ProductForm(string title,int width=620,int height=680){
  preferenceChanged=(_,_)=>{if(IsDisposed||!IsHandleCreated)return;try{BeginInvoke((Action)(()=>{if(!IsDisposed)Theme.RefreshPalette(this);}));}catch(InvalidOperationException){}};
  SystemEvents.UserPreferenceChanged+=preferenceChanged;Disposed+=(_,_)=>SystemEvents.UserPreferenceChanged-=preferenceChanged;
  KeyPreview=true;KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape){Close();e.Handled=true;}};Text=title;Name=GetType().Name;AccessibleName=title;Font=Theme.Font();BackColor=Theme.Canvas;ForeColor=Theme.Text;AutoScaleMode=AutoScaleMode.None;StartPosition=FormStartPosition.CenterScreen;ClientSize=new(width,height);MinimumSize=new(Math.Min(width,520),400);Controls.Add(Body);Controls.Add(Footer);var rail=new ScrollRail(Body);Controls.Add(rail);Body.Paint+=(_,_)=>rail.Sync();Shown+=(_,_)=>rail.Sync();Shown+=(_,_)=>{var a=Screen.FromControl(this).WorkingArea;Size=new(Math.Min(Width,a.Width),Math.Min(Height,a.Height));Location=new(a.Left+(a.Width-Width)/2,a.Top+(a.Height-Height)/2);};}
 protected override void OnLoad(EventArgs e){
  float k=DeviceDpi/96f;if(DeviceDpi!=96)Scale(new SizeF(k,k));
  // Controls can receive their initial monitor DPI when parented. Reapply design dimensions once,
  // so later-created forms do not multiply that initial scaling by our form scaling.
  SuspendLayout();Body.SuspendLayout();Body.Padding=new((int)(24*k));Footer.Height=(int)(64*k);Footer.Padding=new((int)(24*k),(int)(12*k),(int)(16*k),(int)(12*k));
  foreach(Control c in AllControls()){
   if(c.Tag is int height)c.Height=(int)(height*k);
   if(c is QuietButton b){b.Height=(int)(36*k);b.Width=(int)(b.LogicalWidth*k);}
   if(c is DarkChoice choice){choice.Height=(int)(36*k);choice.Width=(int)(choice.LogicalWidth*k);}
   if(c is OpacitySlider slider){slider.Height=(int)(36*k);slider.Width=(int)(330*k);}
  }
  Body.ResumeLayout(true);ResumeLayout(true);Theme.RefreshPalette(this);Theme.DarkWindow(this);base.OnLoad(e);
 }
 protected void Stack(params Control[] controls){foreach(var c in controls.Reverse())Body.Controls.Add(c);}
 public IEnumerable<Control> AllControls(){IEnumerable<Control> Walk(Control c){foreach(Control k in c.Controls){yield return k;foreach(var x in Walk(k))yield return x;}}return Walk(this);}
}
internal sealed class OpacitySlider:Control {
 int value=70;bool held;public event EventHandler? ValueChanged;
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 public int Value{get=>value;set{int v=Math.Clamp((int)Math.Round(value/5d)*5,40,100);if(this.value!=v){this.value=v;Invalidate();ValueChanged?.Invoke(this,EventArgs.Empty);}}}
 protected override AccessibleObject CreateAccessibilityInstance()=>new SliderAccess(this);
 sealed class SliderAccess(OpacitySlider owner):ControlAccessibleObject(owner){public override string? Value{get=>owner.Value+"%";set{if(int.TryParse(value?.TrimEnd('%'),out var v))owner.Value=v;}}}
 public OpacitySlider(){Size=new(300,36);DoubleBuffered=true;TabStop=true;AccessibleName=L.T("Settings.Opacity");AccessibleRole=AccessibleRole.Slider;L.Watch(this,()=>AccessibleName=L.T("Settings.Opacity"));}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;float k=DeviceDpi/96f;int l=(int)(8*k),r=Width-l,y=Height/2,x=l+(r-l)*(Value-40)/60;
  using var track=new Pen(Theme.Border,4*k);using var active=new Pen(Theme.Accent,4*k);g.DrawLine(track,l,y,r,y);g.DrawLine(active,l,y,x,y);using var fill=new SolidBrush(Theme.Text);g.FillEllipse(fill,x-6*k,y-6*k,12*k,12*k);if(Focused){using var p=new Pen(Theme.Accent);g.DrawEllipse(p,x-9*k,y-9*k,18*k,18*k);}}
 void Change(int x)=>Value=40+(int)(Math.Clamp(x-8*DeviceDpi/96f,0,Width-16*DeviceDpi/96f)/(Width-16*DeviceDpi/96f)*60);
 protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left){held=true;Capture=true;Focus();Change(e.X);}base.OnMouseDown(e);}
 protected override void OnMouseMove(MouseEventArgs e){if(held)Change(e.X);base.OnMouseMove(e);}
 protected override void OnMouseUp(MouseEventArgs e){held=false;Capture=false;base.OnMouseUp(e);}
 protected override bool IsInputKey(Keys k)=>k is Keys.Left or Keys.Right||base.IsInputKey(k);
 protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Left)Value-=5;if(e.KeyCode==Keys.Right)Value+=5;base.OnKeyDown(e);}
}
internal sealed class DateField:TextBox {
 DateTime value=DateTime.Today;
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 public DateTime Value{get=>value;set{this.value=value.Date;Text=this.value.ToString("yyyy-MM-dd");}}
 public DateField(){Width=116;Font=Theme.Font(10);BackColor=Theme.Surface;ForeColor=Theme.Text;BorderStyle=BorderStyle.FixedSingle;MaxLength=10;Value=DateTime.Today;AccessibleName=L.T("Common.Date");L.Watch(this,()=>AccessibleName=L.T("Common.Date"));}
 public bool TryGetDate(out DateTime date)=>DateTime.TryParseExact(Text,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out date);
 protected override void OnValidating(System.ComponentModel.CancelEventArgs e){if(TryGetDate(out var d)){value=d;ForeColor=Theme.Text;}else ForeColor=Theme.Critical;base.OnValidating(e);}
 protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Up){Value=Value.AddDays(1);e.Handled=true;}if(e.KeyCode==Keys.Down){Value=Value.AddDays(-1);e.Handled=true;}base.OnKeyDown(e);}
}

