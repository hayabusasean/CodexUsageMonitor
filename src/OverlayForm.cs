using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace CodexUsageMonitor;

internal sealed class OverlayForm:Form {
 readonly MonitorService service;readonly NotifyIcon tray=new();readonly System.Windows.Forms.Timer clock=new(){Interval=1000},hoverTimer=new(){Interval=350};
 readonly ContextMenuStrip menu=new();readonly ToolTip iconTip=new(){AutoPopDelay=3000};readonly bool synthetic;
 Point drag,origin;bool dragging,ready,hover;int hoveredDay=-1;DailyPopover? popover;string countdown="--",countdownTip="",radarCoverage="◔--%",weekly="—";bool countdownFailed;string[] dayValues=["—","—","—"];
 WinEventDelegate? foregroundCallback;IntPtr foregroundHook,hookTargetHandle;IntPtr lastForeground;long lastForegroundTick;int topMostReassertCount;uint lastTopMostFlags;
 public bool? PreviewCompact;public string? TestWeekly,TestAge,TestCountdownTip,TestRadarCoverage;public bool? TestCountdownFailed;public string[]? TestDays;internal float? TestScale=null;
 public bool Compact=>PreviewCompact??service.Settings.Compact;
 internal bool ForegroundHookInstalled=>foregroundHook!=IntPtr.Zero;internal int TopMostReassertCount=>topMostReassertCount;internal uint LastTopMostFlags=>lastTopMostFlags;
 internal float UiScale=>TestScale??DeviceDpi/96f;
 internal Rectangle ModeHit=>new((int)((Compact?0:306)*UiScale),(int)((Compact?62:56)*UiScale),(int)(24*UiScale),(int)(24*UiScale));
 internal Rectangle RadarHit=>new((int)((Compact?132:306)*UiScale),0,(int)(24*UiScale),(int)(24*UiScale));
 internal Rectangle RadarBadgeBounds=>Rectangle.Round(new RectangleF((Compact?134:308)*UiScale,2*UiScale,20*UiScale,20*UiScale));
 internal Rectangle RadarDotBounds=>Rectangle.Round(new RectangleF((Compact?139:313)*UiScale,7*UiScale,10*UiScale,10*UiScale));
 internal Rectangle CountdownHit=>Compact?new((int)(24*UiScale),(int)(54*UiScale),(int)(58*UiScale),(int)(34*UiScale)):new((int)(202*UiScale),0,(int)(104*UiScale),(int)(48*UiScale));
 internal string CountdownText=>countdown;internal string CountdownTooltip=>countdownTip;internal string RadarCoverageText=>radarCoverage;
 internal string RadarTooltip=>RadarTip();internal string RadarSignalLevel=>service.Radar.ActiveSignalLevel;internal bool RadarSignalUnread=>service.Radar.ActiveSignal is {ReadAt:null};
 public string DisplayText=>Compact?weekly+"\n"+countdown+" · "+radarCoverage:L.T("Overlay.WeeklyRemaining")+" "+weekly+"\n"+countdown+"\n"+string.Join(" · ",Enumerable.Range(0,3).Select(i=>L.T("Overlay.ObservedAccessible",DayName(i),dayValues[i])));
 internal static string DayName(int i)=>L.T(new[]{"Overlay.Today","Overlay.Yesterday","Overlay.TwoDaysAgo"}[Math.Clamp(i,0,2)]);
 protected override bool ShowWithoutActivation=>true;
 protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x80;return p;}}
 static readonly int TaskbarCreated=RegisterWindowMessage("TaskbarCreated");
 public OverlayForm(MonitorService s,bool synthetic=false){
  service=s;this.synthetic=synthetic;Text=synthetic?"TEST DATA · CodexUsageMonitor":"CodexUsageMonitor";Name="Overlay";AccessibleName=L.T("Overlay.TrayName");FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;BackColor=Theme.Canvas;DoubleBuffered=true;AutoScaleMode=AutoScaleMode.None;StartPosition=FormStartPosition.Manual;
  menu.BackColor=Theme.Surface;menu.ForeColor=Theme.Text;menu.Font=Theme.Font();menu.Renderer=new DarkMenuRenderer();menu.ShowImageMargin=false;menu.Padding=new(4);menu.ShowCheckMargin=false;
  var captions=new List<(ToolStripItem item,string key)>();
  ToolStripMenuItem Item(string key,EventHandler action){var item=new ToolStripMenuItem(L.T(key),null,action){Padding=new(8,4,8,4)};menu.Items.Add(item);captions.Add((item,key));return item;}
  Item("Menu.Refresh",(_,_)=>service.Refresh(true));var modeItem=Item("Overlay.ModeName",(_,_)=>ToggleMode());Item("Menu.History",(_,_)=>new HistoryForm(service).Show());Item("Menu.Details",(_,_)=>new DetailsForm(service).Show());
  Item("Menu.Reset",(_,_)=>OpenRadar());Item("Menu.Settings",(_,_)=>{ClosePopover();SettingsForm.Open(service,this);ApplySettings();});
  var languages=Item("Language.Label",(_,_)=>{});var english=new ToolStripMenuItem("English");var chinese=new ToolStripMenuItem("繁體中文");languages.DropDownItems.AddRange([english,chinese]);
  void Language(string lang){if(SettingsForm.Active is {IsDisposed:false})return;string previous=service.Settings.UiLanguage;service.Settings.UiLanguage=lang;if(service.SaveSettings())L.SetLanguage(lang);else service.Settings.UiLanguage=previous;}
  english.Click+=(_,_)=>Language("en-US");chinese.Click+=(_,_)=>Language("zh-TW");
  Item("Menu.About",(_,_)=>new AboutForm(service).Show());Item("Menu.OfficialUsage",(_,_)=>OpenUrl("https://chatgpt.com/codex/settings/usage"));Item("Menu.Hide",(_,_)=>Hide());menu.Items.Add(new ToolStripSeparator());Item("Menu.Quit",(_,_)=>Close());ContextMenuStrip=menu;
  menu.Opening+=(_,_)=>{languages.Enabled=modeItem.Enabled=SettingsForm.Active is not {IsDisposed:false};};
  L.Watch(this,()=>{foreach(var (item,key) in captions)item.Text=L.T(key);english.Checked=L.Language=="en-US";chinese.Checked=L.Language=="zh-TW";AccessibleName=L.T("Overlay.TrayName");if(IsHandleCreated)Render();});
  iconTip.OwnerDraw=true;iconTip.Popup+=(_,e)=>e.ToolTipSize=new Size((int)(235*UiScale),(int)(40*UiScale));iconTip.Draw+=(_,e)=>{using var bg=new SolidBrush(Theme.Surface);e.Graphics.FillRectangle(bg,e.Bounds);TextRenderer.DrawText(e.Graphics,e.ToolTipText,Theme.Font(9),Rectangle.Inflate(e.Bounds,-10,-5),Theme.Text,TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak);};
  tray.Icon=SystemIcons.Information;tray.Text="CodexUsageMonitor";tray.ContextMenuStrip=menu;tray.Visible=!synthetic;tray.DoubleClick+=(_,_)=>{if(Visible)Hide();else Reveal();};
  MouseEnter+=(_,_)=>{hover=true;Invalidate();};MouseLeave+=(_,_)=>{hover=false;hoveredDay=-1;hoverTimer.Stop();ClosePopover();iconTip.SetToolTip(this,null);Invalidate();};
  MouseDown+=(_,e)=>{ClosePopover();if(e.Button!=MouseButtons.Left)return;if(ModeHit.Contains(e.Location)){ToggleMode();return;}if(RadarHit.Contains(e.Location)&&service.Radar.ActiveSignal!=null){OpenRadar();return;}drag=Cursor.Position;origin=Location;dragging=true;Capture=true;};
  MouseMove+=(_,e)=>{if(dragging&&MouseButtons==MouseButtons.Left){Location=new(origin.X+Cursor.Position.X-drag.X,origin.Y+Cursor.Position.Y-drag.Y);return;}
   int day=DailyHit(e.Location);if(day!=hoveredDay){hoveredDay=day;hoverTimer.Stop();ClosePopover();if(day>=0)hoverTimer.Start();}
    string? label=CountdownHit.Contains(e.Location)?countdownTip:ModeHit.Contains(e.Location)?Compact?L.T("Overlay.SwitchStandard"):L.T("Overlay.SwitchCompact"):RadarHit.Contains(e.Location)&&service.Radar.ActiveSignal!=null?RadarTip():null;
   if(iconTip.GetToolTip(this)!=label)iconTip.SetToolTip(this,label);
  };
  MouseUp+=(_,_)=>{if(dragging){dragging=false;Capture=false;Clamp();Remember();}};
  MouseDoubleClick+=(_,e)=>{int day=DailyHit(e.Location);if(day>=0)new HistoryForm(service,DateTime.Today.AddDays(-day)).Show();};
  hoverTimer.Tick+=(_,_)=>{hoverTimer.Stop();if(hoveredDay>=0&&!dragging&&Visible)ShowDailyPopover(hoveredDay);};
  service.Changed+=OnChanged;service.Radar.Changed+=OnChanged;

  SystemEvents.UserPreferenceChanged+=PreferenceChanged;SystemEvents.TimeChanged+=TimeChanged;SystemEvents.PowerModeChanged+=PowerChanged;SystemEvents.DisplaySettingsChanged+=DisplayChanged;System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged+=NetworkChanged;
  clock.Tick+=(_,_)=>{if(Visible)Render();};VisibleChanged+=(_,_)=>{clock.Enabled=Visible;if(!Visible)ClosePopover();else ReassertTopMost(GetForegroundWindow());};
  Load+=(_,_)=>{service.Settings.ApplyStartupMode();ApplySettings();ready=true;if(!synthetic)service.Start();if(service.Settings.StartHidden)BeginInvoke(Hide);};
  HandleCreated+=(_,_)=>{hookTargetHandle=Handle;EnsureForegroundHook();ReassertTopMost(GetForegroundWindow());};HandleDestroyed+=(_,_)=>hookTargetHandle=IntPtr.Zero;Shown+=(_,_)=>ReassertTopMost(GetForegroundWindow());
  FormClosed+=(_,_)=>{ReleaseForegroundHook();Remember();service.Changed-=OnChanged;service.Radar.Changed-=OnChanged;SystemEvents.UserPreferenceChanged-=PreferenceChanged;SystemEvents.TimeChanged-=TimeChanged;SystemEvents.PowerModeChanged-=PowerChanged;SystemEvents.DisplaySettingsChanged-=DisplayChanged;System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged-=NetworkChanged;ClosePopover();hoverTimer.Dispose();clock.Dispose();tray.Visible=false;tray.Dispose();iconTip.Dispose();menu.Dispose();};
 }
 internal int DailyHit(Point p)=>Compact||p.Y<50*UiScale||p.Y>=80*UiScale||p.X<8*UiScale||p.X>=304*UiScale?-1:Math.Clamp((int)((p.X/UiScale-8)/100),0,2);
 public void ToggleMode(){ClosePopover();hoveredDay=-1;var before=Bounds;var area=Screen.FromControl(this).WorkingArea;bool right=area.Right-before.Right<24*UiScale,bottom=area.Bottom-before.Bottom<24*UiScale;service.Settings.Compact=!Compact;PreviewCompact=null;service.SaveSettings();Render();if(right)Left=before.Right-Width;if(bottom)Top=before.Bottom-Height;Clamp();ReassertTopMost(GetForegroundWindow());}
 internal DailyPopover ShowDailyPopover(int day,DailySummary? fixture=null){
  ClosePopover();var d=fixture??service.Daily?.Days.FirstOrDefault(x=>x.date==DateTime.Today.AddDays(-day).ToString("yyyy-MM-dd"));
  popover=new DailyPopover(day,d){TestScale=TestScale};
  popover.ShowAt(RectangleToScreen(new((int)((8+day*100)*UiScale),(int)(52*UiScale),(int)(100*UiScale),(int)(28*UiScale))),this);return popover;
 }
 void ClosePopover(){popover?.Close();popover?.Dispose();popover=null;}
 public void OpenRadar(){ClosePopover();var item=service.Radar.ActiveSignal??service.Radar.Unread??service.Radar.Snapshot().Events.OrderByDescending(x=>x.LastSeen).FirstOrDefault();new ResetInfoForm(service,item).Show();}
 string RadarTip(){var signal=service.Radar.ActiveSignal;if(signal==null)return L.T("Overlay.AlertTip");return L.T((signal.SignalLevel,signal.ReadAt==null) switch{("WATCH",true)=>"Overlay.AlertWatchTip",("WATCH",false)=>"Overlay.AlertWatchReadTip",("INCOMING",true)=>"Overlay.AlertIncomingTip",("INCOMING",false)=>"Overlay.AlertIncomingReadTip",_=>"Overlay.AlertTip"});}
 Color RadarColor(string level)=>Theme.HighContrast?Theme.Accent:level switch{"WATCH"=>Theme.Warning,"INCOMING"=>Theme.ResetAlert,_=>Theme.Text};
 void PreferenceChanged(object sender,UserPreferenceChangedEventArgs e){if(IsHandleCreated&&!IsDisposed)try{BeginInvoke(()=>{ClosePopover();BackColor=Theme.Canvas;menu.BackColor=Theme.Surface;menu.ForeColor=Theme.Text;ApplySettings();});}catch(InvalidOperationException){}}
 void TimeChanged(object? sender,EventArgs e){if(IsHandleCreated&&!IsDisposed)try{BeginInvoke(()=>{ClosePopover();L.RefreshTimeZone();service.RebuildDaily();Render();});}catch(InvalidOperationException){}}
 void PowerChanged(object sender,PowerModeChangedEventArgs e){if(e.Mode==PowerModes.Resume)service.Resume();}
 void NetworkChanged(object? sender,System.Net.NetworkInformation.NetworkAvailabilityEventArgs e){if(e.IsAvailable)service.Resume();}
 void DisplayChanged(object? sender,EventArgs e){if(IsHandleCreated)BeginInvoke(()=>{ClosePopover();Clamp();ReassertTopMost(GetForegroundWindow());});}
 void OnChanged(){if(IsHandleCreated&&!IsDisposed)try{BeginInvoke(Render);}catch(InvalidOperationException){}}
 public void Reveal(){Show();Clamp();Render();ReassertTopMost(GetForegroundWindow());}
 public void ApplySettings(){TopMost=service.Settings.TopMost;Opacity=Theme.HighContrast?1:service.Settings.OpacityPercent/100d;if(!ready){var a=Screen.PrimaryScreen!.WorkingArea;Location=service.Settings.X.HasValue&&service.Settings.Y.HasValue?new(service.Settings.X.Value,service.Settings.Y.Value):new(a.Right-480,a.Bottom-160);}Render();Clamp();if(IsHandleCreated){if(service.Settings.TopMost)ReassertTopMost(GetForegroundWindow());else{lastTopMostFlags=SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE;SetWindowPos(Handle,HWND_NOTOPMOST,0,0,0,0,lastTopMostFlags);}}}
 void EnsureForegroundHook(){if(foregroundHook!=IntPtr.Zero)return;foregroundCallback=OnForegroundChanged;foregroundHook=SetWinEventHook(EVENT_SYSTEM_FOREGROUND,EVENT_SYSTEM_FOREGROUND,IntPtr.Zero,foregroundCallback,0,0,WINEVENT_OUTOFCONTEXT);}
 void ReleaseForegroundHook(){hookTargetHandle=IntPtr.Zero;IntPtr hook=foregroundHook;foregroundHook=IntPtr.Zero;if(hook!=IntPtr.Zero)UnhookWinEvent(hook);foregroundCallback=null;}
 void OnForegroundChanged(IntPtr hook,uint eventType,IntPtr hwnd,int objectId,int childId,uint thread,uint milliseconds){try{if(hwnd==IntPtr.Zero)return;IntPtr overlay=hookTargetHandle;if(overlay==IntPtr.Zero||hwnd==overlay||IsDisposed||Disposing)return;long tick=Environment.TickCount64;if(hwnd==lastForeground&&tick-lastForegroundTick<100)return;lastForeground=hwnd;lastForegroundTick=tick;BeginInvoke((Action)(()=>ReassertTopMost(hwnd)));}catch{}}
 bool ReassertTopMost(IntPtr foreground){if(IsDisposed||!IsHandleCreated||!Visible||!service.Settings.TopMost||foreground==Handle)return false;lastTopMostFlags=SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE;bool result=SetWindowPos(Handle,HWND_TOPMOST,0,0,0,0,lastTopMostFlags);if(result)Interlocked.Increment(ref topMostReassertCount);return result;}
 internal bool ReassertTopMostForTest(IntPtr foreground)=>ReassertTopMost(foreground);
 internal static Rectangle ClampBounds(Rectangle b,Rectangle[] screens){var a=screens.FirstOrDefault(s=>s.IntersectsWith(b));if(a==Rectangle.Empty)a=screens[0];return new(Math.Clamp(b.X,a.Left,Math.Max(a.Left,a.Right-b.Width)),Math.Clamp(b.Y,a.Top,Math.Max(a.Top,a.Bottom-b.Height)),Math.Min(b.Width,a.Width),Math.Min(b.Height,a.Height));}
 void Clamp(){Bounds=ClampBounds(Bounds,Screen.AllScreens.Select(s=>s.WorkingArea).ToArray());}
 void Remember(){if(synthetic)return;service.Settings.X=Left;service.Settings.Y=Top;service.Settings.SavedDpi=DeviceDpi;service.Settings.ScreenName=Screen.FromControl(this).DeviceName;service.SaveSettings();}
 internal static List<WindowQuota> Selected(Snapshot? s,string selected)=>s?.Windows.Where(w=>w.LimitId==(string.IsNullOrEmpty(selected)?"codex":selected)).ToList()??[];
 internal static string MissingMainAge(string? error,bool verified)=>L.T(error switch{
  "SOURCE_NOT_FOUND" or "CODEX_NOT_FOUND"=>"Overlay.CodexNotFound",
  "AUTH_REQUIRED" or "AUTH_UNAUTHORIZED" or "RPC_401" or "UNAUTHORIZED" or "API_KEY_ONLY" or "ACCOUNT_UNSUPPORTED"=>"Overlay.SignInRequired",
  "METHOD_UNSUPPORTED" or "PROTOCOL_UNSUPPORTED" or "VERSION_UNSUPPORTED" or "INVALID_CODEX_VERSION" or "ARCHITECTURE_MISMATCH"=>"Overlay.Unsupported",
  "POLICY_DENIED"=>"Overlay.AccessDenied",
  "REQUEST_TIMEOUT" or "TIMEOUT" or "TRANSPORT_EOF" or "TRANSPORT_FAILURE" or "RPC_TIMEOUT"=>"Overlay.Offline",
  "MAIN_WEEKLY_MISSING" or "SOURCE_DATA_MISSING"=>"Overlay.WeeklyUnavailable",
  null or ""=>verified?"Overlay.WeeklyUnavailable":"Overlay.Connecting",
  _=>"Overlay.UpdateFailed"});
 internal static (string text,string tip,string state,int? seconds) Countdown(DateTimeOffset now,bool running,bool busy,bool paused,int failures,DateTimeOffset? next,string? error){
  if(paused)return(L.T("Overlay.CountdownPausedShort"),L.T("Overlay.CountdownPaused"),"PAUSED",null);
  if(!running)return("--",L.T("Overlay.CountdownUnscheduled"),"UNSCHEDULED",null);
  if(busy)return("0s",L.T("Overlay.CountdownNow"),"REFRESHING",0);
  if(error is "SOURCE_NOT_FOUND" or "CODEX_NOT_FOUND" or "AUTH_REQUIRED" or "AUTH_UNAUTHORIZED" or "RPC_401" or "UNAUTHORIZED" or "API_KEY_ONLY" or "ACCOUNT_UNSUPPORTED" or "METHOD_UNSUPPORTED" or "PROTOCOL_UNSUPPORTED" or "VERSION_UNSUPPORTED" or "INVALID_CODEX_VERSION" or "ARCHITECTURE_MISMATCH" or "POLICY_DENIED")return(L.T("Overlay.CountdownOffShort"),L.T("Overlay.CountdownOff"),"OFF",null);
  if(next==null)return("--",L.T("Overlay.CountdownUnscheduled"),"UNSCHEDULED",null);
  int seconds=Math.Max(0,(int)Math.Ceiling((next.Value-now).TotalSeconds));
  return failures>0?($"{seconds}s ⚠",L.T("Overlay.CountdownRetry",seconds),"RETRY",seconds):($"{seconds}s",L.T("Overlay.CountdownNormal",seconds),"SCHEDULED",seconds);
 }
 public void Render(){
  var value=service.MainClock.Value?.Remaining;weekly=value==null?"—":value==0?"0%":value<1?"<1%":value.Value.ToString("0.#",System.Globalization.CultureInfo.InvariantCulture)+"%";
  var now=DateTimeOffset.UtcNow;var schedule=Countdown(now,service.MonitoringStarted,service.Busy,false,service.Failures,service.NextRetry,service.LastErrorCode);countdown=schedule.text;countdownTip=schedule.tip;countdownFailed=schedule.state=="RETRY";
  var radar=service.Radar.Snapshot();radarCoverage="◔"+RadarCoverage.Percent(RadarCoverage.Measure(radar,service.Radar.Enabled,now))+"%";
  for(int i=0;i<3;i++)dayValues[i]=service.Daily?.Days.FirstOrDefault(x=>x.date==DateTime.Today.AddDays(-i).ToString("yyyy-MM-dd"))?.Display??"—";
  if(synthetic){weekly=TestWeekly??weekly;countdown=TestAge??countdown;countdownTip=TestCountdownTip??countdownTip;radarCoverage="◔"+(TestRadarCoverage??radarCoverage.TrimStart('◔'));countdownFailed=TestCountdownFailed??countdownFailed;if(TestDays!=null)dayValues=TestDays.ToArray();}
  var size=new Size((int)((Compact?156:330)*UiScale),(int)((Compact?88:84)*UiScale));
  if(ClientSize!=size||Region==null){ClientSize=size;using var shape=Theme.Round(new RectangleF(0,0,Width,Height),12*UiScale);var old=Region;Region=new Region(shape);old?.Dispose();}
  AccessibleDescription=DisplayText+" · "+countdownTip+(service.Radar.ActiveSignal==null?"":" · "+RadarTip());Invalidate();string title="Codex · "+weekly+" · "+countdown;tray.Text=title[..Math.Min(63,title.Length)];
 }
 protected override void OnPaint(PaintEventArgs e){
  base.OnPaint(e);var g=e.Graphics;g.ScaleTransform(UiScale,UiScale);Theme.PaintCard(g,new Rectangle(0,0,(Compact?156:330)-1,(Compact?88:84)-1));
  decimal? v=decimal.TryParse(weekly.TrimEnd('%').Replace("<",""),System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var parsed)?parsed:null;var color=Theme.QuotaColor(v);var secondary=Theme.HighContrast?Theme.Text:Theme.Hex("#E0E5EA");if(Theme.HighContrast)color=Theme.Text;if(!synthetic&&(service.MainClock.Failed||service.MainClock.Age(service.Monotonic,DateTimeOffset.UtcNow)>3*service.Settings.Interval))color=Theme.Muted;
  var countdownColor=countdownFailed&&!Theme.HighContrast?Theme.Warning:secondary;
  if(Compact){Theme.TextAt(g,weekly,new(4,12,148,48),32,color,true,StringAlignment.Center);Theme.TextAt(g,countdown,new(28,60,48,26),8f,countdownColor,false,StringAlignment.Near);Theme.TextAt(g,radarCoverage,new(78,60,52,26),8f,secondary,false,StringAlignment.Far);}
  else{
   Theme.TextAt(g,L.T("Overlay.WeeklyRemaining"),new(12,8,80,34),8.5f,secondary);Theme.TextAt(g,weekly,new(98,4,108,44),26,color,true);
   Theme.TextAt(g,countdown,new(208,8,96,36),8,countdownColor,false,StringAlignment.Far);
   for(int i=0;i<3;i++){int x=12+i*100;int label=L.Language=="zh-TW"?34:i==0?36:i==1?54:58;Theme.TextAt(g,DayName(i),new(x,53,label,24),L.Language=="en-US"?7.5f:8f,secondary,false,StringAlignment.Near,true);Theme.TextAt(g,dayValues[i],new(x+label,53,98-label,24),L.Language=="en-US"?8.2f:9f,Theme.Text,true);}

  }
  // Two stable, separate 24-DIP hit targets; content does not move when unread state changes.
  var m=Compact?new Rectangle(0,62,24,24):new Rectangle(306,56,24,24);using var pen=new Pen(hover?Theme.Text:Theme.Micro,1.2f);
  int cx=m.X+12,cy=m.Y+12;g.DrawLines(pen,new Point[]{new(cx-4,cy+(Compact?2:-2)),new(cx,cy+(Compact?-2:2)),new(cx+4,cy+(Compact?2:-2))});
   var signal=service.Radar.ActiveSignal;if(signal!=null){bool unread=signal.ReadAt==null;var marker=unread?new RectangleF(Compact?134:308,2,20,20):new RectangleF(Compact?139:313,7,10,10);using var fill=new SolidBrush(RadarColor(signal.SignalLevel));using var outline=new Pen(Theme.HighContrast?Theme.Text:Theme.Canvas,unread?1.5f:1f);g.FillEllipse(fill,marker);g.DrawEllipse(outline,marker);if(unread)Theme.TextAt(g,"!",Rectangle.Round(marker),10.5f,Theme.HighContrast?Theme.SelectedText:Color.Black,true,StringAlignment.Center);}
 }
 protected override AccessibleObject CreateAccessibilityInstance()=>new OverlayAccess(this);
 sealed class OverlayAccess(OverlayForm owner):ControlAccessibleObject(owner){
   public override int GetChildCount()=>owner.service.Radar.ActiveSignal==null?1:2;
  public override AccessibleObject? GetChild(int index)=>index>=0&&index<GetChildCount()?new ActionAccess(owner,index==1):null;
 }
 sealed class ActionAccess(OverlayForm owner,bool radar):AccessibleObject{
  public override string? Name{get=>radar?owner.RadarTip():L.T(owner.Compact?"Overlay.SwitchStandard":"Overlay.SwitchCompact");set{}}
  public override string? DefaultAction=>L.T("Common.Open");
  public override AccessibleRole Role=>AccessibleRole.PushButton;
  public override AccessibleStates State=>AccessibleStates.Focusable;
  public override Rectangle Bounds=>owner.RectangleToScreen(radar?owner.RadarHit:owner.ModeHit);
  public override void DoDefaultAction(){if(owner.IsHandleCreated)owner.BeginInvoke((Action)(()=>{if(radar)owner.OpenRadar();else owner.ToggleMode();}));}
 }
 protected override bool ProcessCmdKey(ref Message msg,Keys key){if(key==Keys.Escape){ClosePopover();return true;}if(key==Keys.F2){ToggleMode();return true;}return base.ProcessCmdKey(ref msg,key);}
 protected override void Dispose(bool disposing){if(disposing)ReleaseForegroundHook();base.Dispose(disposing);}
 protected override void OnDpiChanged(DpiChangedEventArgs e){base.OnDpiChanged(e);if(IsHandleCreated)BeginInvoke((Action)(()=>ReassertTopMost(GetForegroundWindow())));}
 public void SaveView(string path){using var b=new Bitmap(Width,Height);using(var g=Graphics.FromImage(b))g.CopyFromScreen(Location,Point.Empty,Size);b.Save(path);}
 public void ShowMenu()=>menu.Show(this,new Point(0,Height+4));
 protected override void WndProc(ref Message m){if(m.Msg==TaskbarCreated){tray.Visible=false;tray.Visible=!synthetic;}if(m.Msg==0x21){m.Result=(IntPtr)3;return;}base.WndProc(ref m);}
 public static void OpenUrl(string url){try{if(Uri.TryCreate(url,UriKind.Absolute,out var u)&&u.Scheme=="https")Process.Start(new ProcessStartInfo(u.AbsoluteUri){UseShellExecute=true});}catch{}}
 public static void SelectFile(string path){try{Process.Start(new ProcessStartInfo("explorer.exe"){Arguments="/select,"+OwnedProcess.Quote(path),UseShellExecute=true});}catch{}}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int RegisterWindowMessage(string s);
 delegate void WinEventDelegate(IntPtr hook,uint eventType,IntPtr hwnd,int objectId,int childId,uint thread,uint milliseconds);
 static readonly IntPtr HWND_TOPMOST=new(-1),HWND_NOTOPMOST=new(-2);const uint EVENT_SYSTEM_FOREGROUND=0x0003,WINEVENT_OUTOFCONTEXT=0,SWP_NOSIZE=0x0001,SWP_NOMOVE=0x0002,SWP_NOACTIVATE=0x0010;
 [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr hwnd,IntPtr insertAfter,int x,int y,int cx,int cy,uint flags);
 [DllImport("user32.dll")]static extern IntPtr SetWinEventHook(uint eventMin,uint eventMax,IntPtr module,WinEventDelegate callback,uint processId,uint threadId,uint flags);
 [DllImport("user32.dll")]static extern bool UnhookWinEvent(IntPtr hook);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
}

internal sealed class DailyPopover:Form {
 readonly int day;readonly DailySummary? data;internal float? TestScale=null;float ScaleFactor=>TestScale??DeviceDpi/96f;
 public string HumanText=>L.T("Daily.Title",OverlayForm.DayName(day))+"  "+(data?.Display??"—")+"\n"+(data?.observed_drawdown_pp==null?L.T("Daily.NoRecord"):L.T(data.partial?"Daily.PartialRecord":"Daily.Full"))+"\n"+(data?.observed_drawdown_pp==null?"":$"{data.first_sample?.ToLocalTime():HH:mm} – {data.last_sample?.ToLocalTime():HH:mm}")+"\n"+L.T("Daily.LocalExplanation");
 public DailyPopover(int day,DailySummary? data){this.day=day;this.data=data;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;BackColor=Theme.Surface;DoubleBuffered=true;AutoScaleMode=AutoScaleMode.None;StartPosition=FormStartPosition.Manual;L.Watch(this,()=>{Text=L.T("Daily.Title",OverlayForm.DayName(day));AccessibleName=HumanText;Invalidate();});}
 protected override bool ShowWithoutActivation=>true;
 protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x80|0x20;return p;}}
 internal static Rectangle Place(Rectangle anchor,Size size,Rectangle area,int gap=8){
  int x=anchor.Left,y=anchor.Bottom+gap;if(x+size.Width>area.Right)x=anchor.Right-size.Width;if(y+size.Height>area.Bottom)y=anchor.Top-gap-size.Height;
  return new(Math.Clamp(x,area.Left,Math.Max(area.Left,area.Right-size.Width)),Math.Clamp(y,area.Top,Math.Max(area.Top,area.Bottom-size.Height)),Math.Min(size.Width,area.Width),Math.Min(size.Height,area.Height));
 }
 public void ShowAt(Rectangle anchor,Form owner){Size=new((int)(308*ScaleFactor),(int)(164*ScaleFactor));Bounds=Place(anchor,Size,Screen.FromRectangle(anchor).WorkingArea,(int)(8*ScaleFactor));using var p=Theme.Round(new RectangleF(0,0,Width,Height),8*ScaleFactor);Region=new Region(p);Show(owner);}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.ScaleTransform(ScaleFactor,ScaleFactor);Theme.PaintCard(g,new(0,0,307,163));Theme.TextAt(g,L.T("Daily.Title",OverlayForm.DayName(day)),new(16,12,210,28),10.5f,Theme.Text,true);Theme.TextAt(g,data?.Display??"—",new(230,12,62,28),14,Theme.Text,true,StringAlignment.Far);
  bool missing=data?.observed_drawdown_pp==null;Theme.TextAt(g,missing?L.T("Daily.NoRecord"):L.T(data!.partial?"Daily.PartialRecord":"Daily.Full"),new(16,48,276,24),10,missing?Theme.Muted:data!.partial?Theme.Warning:Theme.Accent);
  if(!missing)Theme.TextAt(g,$"{data!.first_sample?.ToLocalTime():HH:mm} – {data.last_sample?.ToLocalTime():HH:mm}",new(16,76,276,24),10,Theme.Muted);
  Theme.TextAt(g,L.T("Daily.LocalExplanation"),new(16,110,276,42),9,Theme.Muted);
 }
 protected override void WndProc(ref Message m){if(m.Msg==0x21){m.Result=(IntPtr)3;return;}if(m.Msg==0x84){m.Result=(IntPtr)(-1);return;}base.WndProc(ref m);}
}

