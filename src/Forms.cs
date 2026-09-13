
using System.Globalization;
using System.Text.Json;
namespace CodexUsageMonitor;
internal sealed class SettingsForm:ProductForm {
 internal static SettingsForm? Active;
 public static void Open(MonitorService s,OverlayForm overlay){if(Active is {IsDisposed:false}){Active.Activate();return;}using var f=new SettingsForm(s,overlay);f.ShowDialog();}
 public SettingsForm(MonitorService s,OverlayForm? overlay=null):base(L.T("Settings.Title"),1120,820){
  Active=this;var original=s.Settings.Copy();string originalLanguage=L.Language;bool saved=false,refreshing=false,checking=false;string stateKey="";
  var language=new DarkChoice{Width=220,AccessibleName=L.T("Language.Label")};language.Items.AddRange(["English","繁體中文"]);language.SelectedIndex=L.Language=="zh-TW"?1:0;
  var mode=new DarkChoice{Width=210};mode.Items.AddRange(["","",""]);mode.SelectedIndex=original.StartupMode=="STANDARD"?1:original.StartupMode=="COMPACT"?2:0;
  var top=L.Check("Settings.AlwaysOnTop",original.TopMost);var hidden=L.Check("Settings.StartHidden",original.StartHidden);var startup=L.Check("Settings.StartWithWindows",original.Startup);var radar=L.Check("Settings.RadarEnabled",original.RadarEnabled);
  var opacity=new OpacitySlider{Value=original.OpacityPercent,Width=300};var opacityText=Theme.Label(original.OpacityPercent+"%",36);opacityText.Width=52;opacityText.Dock=DockStyle.None;
  var low=L.Label("Settings.LowOpacity",28,9,Theme.Muted);low.Visible=original.OpacityPercent<70;
  opacity.ValueChanged+=(_,_)=>{opacityText.Text=opacity.Value+"%";low.Visible=opacity.Value<70;if(overlay!=null)overlay.Opacity=Theme.HighContrast?1:opacity.Value/100d;};
  DarkChoice Choice(int[] values,int value){var c=new DarkChoice{Width=108};c.Items.AddRange(values.Cast<object>().ToArray());c.SelectedItem=value;return c;}
  var interval=Choice([60,90,180,300],original.Interval);var retention=Choice([30,90,180],original.RetentionDays);
  var state=Theme.Label("",48,10,Theme.Muted);var checkedAt=Theme.Label("",28,9,Theme.Muted);
  var path=new TextBox{ReadOnly=true,Text=original.CodexPath,Width=550};Theme.Style(path);
  var advanced=new Panel{Dock=DockStyle.Top,Height=142,Tag=142,Visible=false};
  var browse=L.Button("Settings.ChooseCodex",width:240);var detect=L.Button("Settings.AutoDetect",(_,_)=>{path.Text="";stateKey="Settings.DetectAfterSave";state.Text=L.T(stateKey);},width:225);
  advanced.Controls.Add(L.Label("Settings.NoCodexMutation",34,9,Theme.Muted));advanced.Controls.Add(Theme.Row(path));advanced.Controls.Add(Theme.Row(browse,detect));
  browse.Click+=async(_,_)=>{using var dialog=new OpenFileDialog{Filter=L.T("Settings.ExeFilter")};if(dialog.ShowDialog()!=DialogResult.OK)return;browse.Enabled=false;stateKey="Settings.Validating";state.Text=L.T(stateKey);
   try{using var ct=new CancellationTokenSource(50000);var version=await s.ValidateCandidate(dialog.FileName,ct.Token);path.Text=dialog.FileName;stateKey="";state.Text=L.T("Settings.SourceValidated",version);}
   catch(Exception e){stateKey=e is MonitorException me&&me.Code=="SELF_SOURCE_REJECTED"?"Settings.SelfSource":"Settings.SourceInvalid";state.Text=L.T(stateKey);}finally{if(!IsDisposed)browse.Enabled=true;}};
  var check=L.Button("Settings.CheckConnection",width:185);check.Click+=async(_,_)=>{
   check.Enabled=false;checking=true;stateKey="Settings.Checking";state.Text=L.T(stateKey);var previous=s.Current?.PollId;s.Refresh();var timer=System.Diagnostics.Stopwatch.StartNew();
   try{while(!IsDisposed&&timer.Elapsed.TotalSeconds<55){await Task.Delay(200);if(s.Current?.PollId!=previous||(!s.Busy&&s.Failures>0))break;}if(IsDisposed)return;
    checking=false;stateKey=s.Verified&&s.Current?.PollId!=previous?"Settings.Connected":"Settings.CheckFailed";state.Text=L.T(stateKey);checkedAt.Text=s.Verified?L.T("Settings.CheckLast",s.Current?.Version):MonitorService.Explain(s.LastErrorCode);
   }finally{checking=false;if(!IsDisposed)check.Enabled=true;}
  };
  var advancedToggle=L.Button("Settings.AdvancedSource",(_,_)=>advanced.Visible=!advanced.Visible,width:190);
  var save=L.Button("Settings.Save",(_,_)=>{
   try{
    bool changed=path.Text!=original.CodexPath;

    s.Settings.StartupMode=new[]{"REMEMBER","STANDARD","COMPACT"}[Math.Max(0,mode.SelectedIndex)];
    s.Settings.TopMost=top.Checked;s.Settings.OpacityPercent=opacity.Value;s.Settings.Interval=(int)interval.SelectedItem!;s.Settings.RetentionDays=(int)retention.SelectedItem!;
    s.Settings.StartHidden=hidden.Checked;s.Settings.Startup=startup.Checked;s.Settings.CodexPath=path.Text;s.Settings.UiLanguage=L.Language;s.Settings.RadarEnabled=radar.Checked;
    if(!s.SaveSettings()){Restore();stateKey="Settings.SaveFailed";state.Text=L.T(stateKey);return;}
    try{if(startup.Checked!=original.Startup)StartupLink.Set(startup.Checked,Environment.ProcessPath!);}catch{Restore();s.SaveSettings();try{StartupLink.Set(original.Startup,Environment.ProcessPath!);}catch{}stateKey="Settings.SaveFailed";state.Text=L.T(stateKey);return;}
    s.Radar.SetEnabled(radar.Checked);if(changed)s.Refresh(false,true);saved=true;overlay?.ApplySettings();Close();
   }catch{stateKey="Settings.SaveFailed";state.Text=L.T(stateKey);}
  },true);
  var cancel=L.Button("Settings.Cancel",(_,_)=>Close());Footer.Controls.AddRange([save,cancel]);AcceptButton=save;CancelButton=cancel;
  Label Inline(string key,int width){var c=L.Label(key,32);c.Dock=DockStyle.None;c.Width=width;return c;}
  R2Card Card(string icon,string key,string en,string zh,Color accent,params Control[] controls){var heading=L.Label(key,36,13);heading.Padding=new(34,0,0,0);heading.Paint+=(_,e)=>{using var f=Theme.Font(18);TextRenderer.DrawText(e.Graphics,icon,f,new Rectangle(0,0,30*DeviceDpi/96,heading.Height),accent,TextFormatFlags.VerticalCenter);};return new R2Card(new Control[]{heading,R2.Caption(en,zh,42)}.Concat(controls).ToArray()){Accent=accent};}
  var langCard=Card("◎","Language.Label","Make this space feel like yours.","以習慣的語言，打造你的工作空間。",Theme.Electric,Theme.Row(language));
  var languageHeading=langCard.Controls.OfType<Label>().First(x=>x.Padding.Left>0);L.Watch(languageHeading,()=>languageHeading.Text=languageHeading.AccessibleName=L.Language=="zh-TW"?"語言":"Language");
  opacity.Width=250;var opRow=Theme.Row(opacity,opacityText);opRow.SizeChanged+=(_,_)=>opacity.Width=Math.Max(80,opRow.Width-opacityText.Width-30);
  var appearance=Card("✧","Settings.Appearance","Your floating HUD, your way.","調整浮窗，讓重要資訊陪伴工作。",Theme.Brand,L.Label("Settings.StartupView",24,9,Theme.Muted),Theme.Row(mode),Theme.Row(top),L.Label("Settings.Opacity",24,9,Theme.Muted),opRow,Theme.Row(L.Button("Settings.ResetOpacity",(_,_)=>opacity.Value=70,width:160)),low);
  var monitoring=Card("↻","Settings.Monitoring","A steady rhythm for your quota.","穩定更新額度，保留觀測足跡。",Theme.Electric,Theme.Row(Inline("Settings.PollInterval",165),interval),Theme.Row(Inline("Settings.Retention",165),retention),L.Label("Settings.Startup",30,11),Theme.Row(startup),Theme.Row(hidden));
  var radarCard=Card("◉","Settings.Radar","Signals worth your attention.","將注意力留給值得關注的訊號。",Theme.Warning,Theme.Row(radar),L.Label("Settings.RadarHint",58,9,Theme.Muted));
  var connection=Card("⌁","Settings.Connection","Connected locally. No setup rituals.","本機連線，輕鬆掌握來源狀態。",Theme.Accent,state,checkedAt,Theme.Row(check),Theme.Row(advancedToggle),advanced);
  advanced.SizeChanged+=(_,_)=>{path.Width=Math.Max(120,advanced.Width-12);};browse.Width=240;detect.Width=240;advanced.Controls.Clear();advanced.Height=186;advanced.Tag=186;advanced.Controls.Add(L.Label("Settings.NoCodexMutation",40,9,Theme.Muted));advanced.Controls.Add(Theme.Row(path));advanced.Controls.Add(Theme.Row(detect));advanced.Controls.Add(Theme.Row(browse));
  Control Gap()=>Theme.Label("",14);
  Stack(L.Label("Settings.Intro",60,24),R2.Caption("A calmer workspace. More room to create.","讓工作空間更從容，為創造留出餘裕。",36),new R2Columns(new R2Stack(langCard,Gap(),appearance),new R2Stack(monitoring,Gap(),radarCard),new R2Stack(connection)));
  ((QuietButton)cancel).Role=ButtonRole.Ghost;((QuietButton)advancedToggle).Role=ButtonRole.Ghost;
  Shown+=(_,_)=>BeginInvoke((Action)(()=>{language.Focus();Body.AutoScrollPosition=Point.Empty;}));
  language.SelectedIndexChanged+=(_,_)=>{if(!refreshing)L.SetLanguage(language.SelectedIndex==1?"zh-TW":"en-US");};
  L.Watch(this,()=>{refreshing=true;Text=AccessibleName=L.T("Settings.Title");language.AccessibleName=L.T("Language.Label");language.SelectedIndex=L.Language=="zh-TW"?1:0;
   var selected=mode.SelectedIndex;mode.Items.Clear();mode.Items.AddRange([L.T("Settings.RememberLast"),L.T("Settings.Standard"),L.T("Settings.Compact")]);mode.SelectedIndex=selected;mode.Invalidate();mode.AccessibleName=L.T("Settings.StartupView");
   interval.AccessibleName=L.T("Settings.PollInterval");retention.AccessibleName=L.T("Settings.Retention");path.AccessibleName=L.T("Settings.PathAccessible");
   state.Text=checking?L.T("Settings.Checking"):stateKey.Length>0?L.T(stateKey):s.Verified?L.T("Settings.NoSetupNeeded"):L.Health(s);checkedAt.Text=s.Verified?L.T("Settings.CheckLast",s.Current?.Version):L.T("Settings.NotChecked");refreshing=false;
  });
  void Restore(){s.Settings.StartupMode=original.StartupMode;s.Settings.TopMost=original.TopMost;s.Settings.OpacityPercent=original.OpacityPercent;s.Settings.Interval=original.Interval;s.Settings.RetentionDays=original.RetentionDays;s.Settings.StartHidden=original.StartHidden;s.Settings.Startup=original.Startup;s.Settings.CodexPath=original.CodexPath;s.Settings.UiLanguage=original.UiLanguage;s.Settings.RadarEnabled=original.RadarEnabled;}
  FormClosed+=(_,_)=>{if(!saved){L.SetLanguage(originalLanguage);Restore();}overlay?.ApplySettings();if(Active==this)Active=null;};
 }
}
internal sealed class DetailsForm:ProductForm {
 bool otherOpen,advancedOpen;int refreshPending;
 public DetailsForm(MonitorService s):base(L.T("Details.Title"),570,438){
  Footer.Visible=false;
  var remaining=Theme.Label("",52,28);var used=Theme.Label("",26,11);var reset=Theme.Label("",28);var five=Theme.Label("",32,10,Theme.Muted);var credits=Theme.Label("",36,10);var expires=Theme.Label("",28,9,Theme.Muted);var connection=Theme.Label("",28,9,Theme.Muted);
  var other=Theme.Label("",215,10);var advanced=Theme.Label("",155,9,Theme.Muted);other.Visible=advanced.Visible=false;
  var pools=L.Button("Details.OtherPools",(_,_)=>{otherOpen=!otherOpen;other.Visible=otherOpen;Fit();},width:200);
  var more=L.Button("Details.Advanced",(_,_)=>{advancedOpen=!advancedOpen;advanced.Visible=advancedOpen;Fit();},width:180);
  Stack(Theme.Section(L.Label("Details.MainWeekly",30,14),remaining,used,reset,five),Theme.Section(L.Label("Details.ResetsAndCredits",32,11),credits,expires,connection),Theme.Row(pools,more),other,advanced,L.Label("Details.SeparatePools",44,9,Theme.Muted));
  ((QuietButton)more).Role=ButtonRole.Ghost;
  void RefreshText(){
   var snapshot=s.Current;Text=AccessibleName=L.T("Details.Title");var main=snapshot?.Windows.FirstOrDefault(w=>w.LimitId=="codex"&&w.Duration==10080);
   string Unknown(string? v)=>v??L.T("Details.NotProvided");
   string Stamp(long? utc)=>utc is >= -62135596800 and <= 253402300799?DateTimeOffset.FromUnixTimeSeconds(utc.Value).ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz",CultureInfo.InvariantCulture):L.T("Details.NotProvided");
   remaining.Text=L.T("Details.Remaining",L.Percent(main?.Remaining));remaining.ForeColor=Theme.QuotaColor(main?.Remaining);used.Text=L.T("Details.Used",L.Percent(main?.Used));reset.Text=L.T("Details.ResetAt",Stamp(main?.Reset));
   var fiveWindow=snapshot?.Windows.FirstOrDefault(w=>w.LimitId=="codex"&&w.Duration==300);five.Text=fiveWindow==null?L.T("Details.NoFiveHour"):L.T("Details.FiveReading",L.Percent(fiveWindow.Remaining));
   credits.Text=L.T("Details.ResetsBalance",snapshot?.ResetCredits?.ToString(CultureInfo.InvariantCulture)??L.T("Details.NotProvided"),Unknown(main?.Balance));
   expires.Text=L.T("Details.Expiry",snapshot?.CreditExpirations.Count>0?string.Join(", ",snapshot.CreditExpirations.Select(x=>Stamp(x))):L.T("Details.NotProvided"));
   connection.Text=L.Health(s);
   other.Text=L.T("Details.OtherPools")+"\n\n"+string.Join("\n\n",snapshot?.Windows.Where(w=>w.LimitId!="codex").OrderBy(w=>w.LimitId.Contains("bengalfox")?1:0).Select(w=>L.T("Details.Pool",w.LimitId.Contains("bengalfox")?"Spark":w.LimitName??w.LimitId,L.Window(w.Duration),L.Percent(w.Used),L.Percent(w.Remaining)))??[]);
   advanced.Text=L.T("Details.AdvancedInfo",snapshot?.Version,snapshot?.Assurance,snapshot?.Observed.ToString("O"),L.Health(s))+"\n"+L.T("Details.SourceCoverage")+"\n"+string.Join(" · ",s.Radar.Snapshot().Sources.Select(x=>x.Id+": "+x.Status));
   foreach(var c in new[]{remaining,used,reset,five,credits,expires,connection,other,advanced})c.AccessibleName=c.Text;

  }
  void Change(){if(!IsHandleCreated||IsDisposed||Interlocked.Exchange(ref refreshPending,1)!=0)return;try{BeginInvoke((Action)(()=>{Interlocked.Exchange(ref refreshPending,0);if(!IsDisposed&&!Disposing)RefreshText();}));}catch(InvalidOperationException){Interlocked.Exchange(ref refreshPending,0);}}
  s.Changed+=Change;Disposed+=(_,_)=>s.Changed-=Change;L.Watch(this,RefreshText);Shown+=(_,_)=>Fit();
  void Fit(){float k=DeviceDpi/96f;int content=Body.Controls.Cast<Control>().Where(c=>c.Visible).Sum(c=>c.Height)+Body.Padding.Vertical;int chrome=Controls.Cast<Control>().Where(c=>c!=Body&&c.Visible&&c.Dock==DockStyle.Top).Sum(c=>c.Height);var area=Screen.FromControl(this).WorkingArea;var desired=new Size(ClientSize.Width,Math.Min(content+chrome+Padding.Vertical,area.Height-70));if(ClientSize!=desired)ClientSize=desired;if(Bottom>area.Bottom)Top=area.Bottom-Height;Body.Invalidate(true);}
 }
}
internal sealed class AboutForm:ProductForm {
 public AboutForm(MonitorService s):base(L.T("About.Title"),600,445){
  var diagnostics=new Label{AutoSize=false,Dock=DockStyle.Top,Height=200,Tag=200,Visible=false,BackColor=Theme.Surface,ForeColor=Theme.Text,Font=Theme.Font(9),TextAlign=ContentAlignment.TopLeft,Padding=new(8)};
  var status=Theme.Label("",32,9,Theme.Muted);bool preview=false;
  string Data()=>JsonSerializer.Serialize(new{version=L.Version,build=RuntimeIdentity.Build,os=Environment.OSVersion.VersionString,architecture=System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),dpi=DeviceDpi,ui_language=L.Language,local_time_zone=TimeZoneInfo.Local.Id,codex_version=s.Current?.Version,capability=s.Current?.Assurance??"UNKNOWN",quota_health=s.Verified?"CONNECTED":"UNAVAILABLE",error_class=s.LastErrorCode,public_sources=s.Radar.Snapshot().Sources.Select(x=>new{x.Id,x.Status,x.HttpStatus,x.Error})},new JsonSerializerOptions{WriteIndented=true});
  var copy=L.Button("About.CopyDiagnostics",(_,_)=>{if(!preview){diagnostics.Text=Data();diagnostics.Visible=true;diagnostics.Height=TextRenderer.MeasureText(diagnostics.Text,diagnostics.Font,new Size(Math.Max(200,Body.ClientSize.Width-80),int.MaxValue),TextFormatFlags.WordBreak).Height+30;preview=true;ClientSize=new(ClientSize.Width,Math.Min(Screen.FromControl(this).WorkingArea.Height-80,ClientSize.Height+(int)(210*DeviceDpi/96f)));return;}try{Clipboard.SetText(diagnostics.Text);status.Text=L.T("About.Copied");}catch{status.Text=L.T("About.CopyFailed");}},true,width:205);
  var build=Theme.Label("",58,12,Theme.Accent);
  Stack(L.Label("About.Title",40,18),build,L.Label("About.Unofficial",44),L.Label("About.Storage",44,10,Theme.Muted),L.Label("About.License",48,10,Theme.Muted),L.Label("About.DiagnosticsHint",42,9,Theme.Muted),diagnostics,status);
  Footer.Controls.AddRange([L.Button("Common.Close",(_,_)=>Close()),copy]);
  L.Watch(this,()=>{Text=AccessibleName=L.T("About.Title");build.Text=RuntimeIdentity.Display;diagnostics.AccessibleName=L.T("About.DiagnosticsPreview");if(preview)diagnostics.Text=Data();});
 }
}

