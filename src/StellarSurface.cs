using System.Drawing.Drawing2D;
namespace CodexUsageMonitor;

// Native vector artwork, rendered once per viewport. No network, animation, asset or runtime dependency.
internal static class StellarSurface {
 static readonly Lazy<Bitmap> Artwork=new(()=>{using var stream=typeof(StellarSurface).Assembly.GetManifestResourceStream("CodexUsageMonitor.StellarBackdrop.png")??throw new InvalidOperationException("Embedded stellar artwork missing");using var image=Image.FromStream(stream);return new Bitmap(image);});
 internal static void Glass(Graphics g,RectangleF rect,float radius,bool hud=false){
  g.SmoothingMode=SmoothingMode.AntiAlias;using var path=Theme.Round(rect,radius);
  if(Theme.HighContrast){using var fill=new SolidBrush(Theme.Surface1);using var contrastEdge=new Pen(Theme.Border);g.FillPath(fill,path);g.DrawPath(contrastEdge,path);return;}
  using var surface=new LinearGradientBrush(rect,Color.FromArgb(24,37,75),Color.FromArgb(16,20,45),65f);g.FillPath(surface,path);
  if(hud){var saved=g.Save();g.SetClip(path,CombineMode.Intersect);using var attributes=new System.Drawing.Imaging.ImageAttributes();attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix{Matrix33=.18f});var art=R2.Rollout?R2.Art("history"):Artwork.Value;g.DrawImage(art,Rectangle.Round(rect),0,0,art.Width,art.Height,GraphicsUnit.Pixel,attributes);g.Restore(saved);}
  using var borderBrush=new LinearGradientBrush(rect,Color.FromArgb(105,Theme.Electric),Color.FromArgb(140,Theme.Brand),15f);using var border=new Pen(borderBrush,1);g.DrawPath(border,path);
  using var inner=Theme.Round(new RectangleF(rect.X+2,rect.Y+2,Math.Max(1,rect.Width-4),Math.Max(1,rect.Height-4)),Math.Max(1,radius-2));using var edge=new Pen(Color.FromArgb(22,Theme.Text));g.DrawPath(edge,inner);
  if(hud){var old=g.Save();g.SetClip(path,CombineMode.Intersect);using var arc=new Pen(Color.FromArgb(40,Theme.Brand),1);g.DrawEllipse(arc,rect.Right-94,rect.Top-60,150,130);using var horizon=new Pen(Color.FromArgb(38,Theme.Electric),2);g.DrawEllipse(horizon,rect.Right-91,rect.Top-57,150,130);g.Restore(old);}
 }
 internal static Bitmap Background(Size size){
  var image=new Bitmap(Math.Max(1,size.Width),Math.Max(1,size.Height));using var g=Graphics.FromImage(image);g.SmoothingMode=SmoothingMode.AntiAlias;
  var bounds=new Rectangle(Point.Empty,image.Size);using(var sky=new LinearGradientBrush(bounds,Theme.Hex("#080D22"),Theme.Hex("#111431"),38f))g.FillRectangle(sky,bounds);
  var art=Artwork.Value;
  // Cover without distorting the original artwork; anchored to upper right for the planetary limb.
  float scale=Math.Max(image.Width/(float)art.Width,image.Height/(float)art.Height);float width=art.Width*scale,height=art.Height*scale;
  g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(art,new RectangleF(image.Width-width,0,width,height));
  using(var veil=new LinearGradientBrush(bounds,Color.FromArgb(35,8,13,34),Color.FromArgb(85,8,13,34),90))g.FillRectangle(veil,bounds);
  return image;
 }
}
internal sealed class SpacePanel:Panel {
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool ShowScrollBar(IntPtr hwnd,int bar,bool show);
 [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int GetStyle(IntPtr hwnd,int index);
 bool hidingScroll;
 internal void HideNativeScroll(){if(!IsHandleCreated||hidingScroll||(GetStyle(Handle,-16)&0x00300000)==0)return;try{hidingScroll=true;ShowScrollBar(Handle,3,false);}finally{hidingScroll=false;}}
 protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);HideNativeScroll();}
 protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);HideNativeScroll();}
 Bitmap? artwork;Size composedSize;string composedScene="";
 internal SpacePanel(){DoubleBuffered=true;ResizeRedraw=true;BackColor=Theme.Canvas;SetStyle(ControlStyles.AllPaintingInWmPaint,true);}
 protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);Invalidate(true);}
 internal void PrepareSurface(){if(Theme.HighContrast)return;string scene=R2.Is(this)?R2.Scene(this):"legacy";if(artwork!=null&&composedSize==ClientSize&&composedScene==scene)return;var next=scene=="legacy"?StellarSurface.Background(ClientSize):R2.Background(ClientSize,scene);var old=artwork;artwork=next;composedSize=ClientSize;composedScene=scene;old?.Dispose();}
 internal void PaintSurface(Graphics g){using(var fill=new SolidBrush(Theme.Canvas))g.FillRectangle(fill,ClientRectangle);if(Theme.HighContrast)return;PrepareSurface();g.DrawImageUnscaled(artwork!,Point.Empty);}
 protected override void OnPaintBackground(PaintEventArgs e)=>PaintSurface(e.Graphics);
 protected override void Dispose(bool disposing){if(disposing)artwork?.Dispose();base.Dispose(disposing);}
}
