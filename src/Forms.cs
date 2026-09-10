
using System.Globalization;
using System.Text.Json;
namespace CodexUsageMonitor;
internal sealed class SettingsForm:ProductForm {
 internal static SettingsForm? Active;
 public static void Open(MonitorService s,OverlayForm overlay){if(Active is {IsDisposed:false}){Active.Activate();return;}using var f=new SettingsForm(s,overlay);f.ShowDialog();}
 public SettingsForm(MonitorService s,OverlayForm? overlay=null):base(L.T("Settings.Title"),660,740){
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
  var path=new TextBox{ReadOnly=true,Text=original.CodexPath,Width=590};Theme.Style(path);
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
  Stack(L.Label("Settings.Intro",40,18),L.Label("Language.Label",30,11),Theme.Row(language),
   L.Label("Settings.Appearance",36,12),L.Label("Settings.StartupView",28,10,Theme.Muted),Theme.Row(mode,top),
   L.Label("Settings.Opacity",28,10,Theme.Muted),Theme.Row(opacity,opacityText,L.Button("Settings.ResetOpacity",(_,_)=>opacity.Value=70,width:135)),low,
   L.Label("Settings.Monitoring",36,12),Theme.Row(Inline("Settings.PollInterval",172),interval,Inline("Settings.Retention",145),retention),
   L.Label("Settings.Startup",32,11),Theme.Row(startup,hidden),
   L.Label("Settings.Radar",32,11),Theme.Row(radar),L.Label("Settings.RadarHint",38,9,Theme.Muted),
   L.Label("Settings.Connection",34,11),state,checkedAt,Theme.Row(check,advancedToggle),advanced);
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
 bool otherOpen,advancedOpen;
 public DetailsForm(MonitorService s):base(L.T("Details.Title"),570,438){
  Footer.Visible=false;
  var remaining=Theme.Label("",52,28);var used=Theme.Label("",26,11);var reset=Theme.Label("",28);var five=Theme.Label("",32,10,Theme.Muted);var credits=Theme.Label("",36,10);var expires=Theme.Label("",28,9,Theme.Muted);var connection=Theme.Label("",28,9,Theme.Muted);
  var other=Theme.Label("",215,10);var advanced=Theme.Label("",155,9,Theme.Muted);other.Visible=advanced.Visible=false;
  var pools=L.Button("Details.OtherPools",(_,_)=>{otherOpen=!otherOpen;other.Visible=otherOpen;Fit();},width:200);
  var more=L.Button("Details.Advanced",(_,_)=>{advancedOpen=!advancedOpen;advanced.Visible=advancedOpen;Fit();},width:180);
  Stack(L.Label("Details.MainWeekly",30,14),remaining,used,reset,five,L.Label("Details.ResetsAndCredits",32,11,Theme.Accent),credits,expires,connection,Theme.Row(pools,more),other,advanced,L.Label("Details.SeparatePools",44,9,Theme.Muted));
  void RefreshText(){
   Text=AccessibleName=L.T("Details.Title");var main=s.Current?.Windows.FirstOrDefault(w=>w.LimitId=="codex"&&w.Duration==10080);
   string Unknown(string? v)=>v??L.T("Details.NotProvided");
   string Stamp(long? utc)=>utc.HasValue?DateTimeOffset.FromUnixTimeSeconds(utc.Value).ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz",CultureInfo.InvariantCulture):L.T("Details.NotProvided");
   remaining.Text=L.T("Details.Remaining",L.Percent(main?.Remaining));remaining.ForeColor=Theme.QuotaColor(main?.Remaining);used.Text=L.T("Details.Used",L.Percent(main?.Used));reset.Text=L.T("Details.ResetAt",Stamp(main?.Reset));
   var fiveWindow=s.Current?.Windows.FirstOrDefault(w=>w.LimitId=="codex"&&w.Duration==300);five.Text=fiveWindow==null?L.T("Details.NoFiveHour"):L.T("Details.FiveReading",L.Percent(fiveWindow.Remaining));
   credits.Text=L.T("Details.ResetsBalance",s.Current?.ResetCredits?.ToString(CultureInfo.InvariantCulture)??L.T("Details.NotProvided"),Unknown(main?.Balance));
   expires.Text=L.T("Details.Expiry",s.Current?.CreditExpirations.Count>0?string.Join(", ",s.Current.CreditExpirations.Select(x=>Stamp(x))):L.T("Details.NotProvided"));
   connection.Text=L.Health(s);
   other.Text=L.T("Details.OtherPools")+"\n\n"+string.Join("\n\n",s.Current?.Windows.Where(w=>w.LimitId!="codex").OrderBy(w=>w.LimitId.Contains("bengalfox")?1:0).Select(w=>L.T("Details.Pool",w.LimitId.Contains("bengalfox")?"Spark":w.LimitName??w.LimitId,L.Window(w.Duration),L.Percent(w.Used),L.Percent(w.Remaining)))??[]);
   advanced.Text=L.T("Details.AdvancedInfo",s.Current?.Version,s.Current?.Assurance,s.Current?.Observed.ToString("O"),L.Health(s))+"\n"+L.T("Details.SourceCoverage")+"\n"+string.Join(" · ",s.Radar.Snapshot().Sources.Select(x=>x.Id+": "+x.Status));
   foreach(var c in new[]{remaining,used,reset,five,credits,expires,connection,other,advanced})c.AccessibleName=c.Text;
   if(Visible)Fit();
  }
  void Change(){if(IsHandleCreated&&!IsDisposed)try{BeginInvoke(RefreshText);}catch(InvalidOperationException){}}
  s.Changed+=Change;Disposed+=(_,_)=>s.Changed-=Change;L.Watch(this,RefreshText);Shown+=(_,_)=>Fit();
  void Fit(){float k=DeviceDpi/96f;int logical=438+(otherOpen?215:0)+(advancedOpen?155:0);var area=Screen.FromControl(this).WorkingArea;ClientSize=new((int)(570*k),Math.Min((int)(logical*k),area.Height-70));if(Bottom>area.Bottom)Top=area.Bottom-Height;}
 }
}
internal sealed class AboutForm:ProductForm {
 public AboutForm(MonitorService s):base(L.T("About.Title"),600,445){
  var diagnostics=new Label{AutoSize=false,Dock=DockStyle.Top,Height=200,Tag=200,Visible=false,BackColor=Theme.Surface,ForeColor=Theme.Text,Font=Theme.Font(9),TextAlign=ContentAlignment.TopLeft,Padding=new(8)};
  var status=Theme.Label("",32,9,Theme.Muted);bool preview=false;
  string Data()=>JsonSerializer.Serialize(new{version=L.Version,os=Environment.OSVersion.VersionString,architecture=System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),dpi=DeviceDpi,ui_language=L.Language,local_time_zone=TimeZoneInfo.Local.Id,codex_version=s.Current?.Version,capability=s.Current?.Assurance??"UNKNOWN",quota_health=s.Verified?"CONNECTED":"UNAVAILABLE",error_class=s.LastErrorCode,public_sources=s.Radar.Snapshot().Sources.Select(x=>new{x.Id,x.Status,x.HttpStatus,x.Error})},new JsonSerializerOptions{WriteIndented=true});
  var copy=L.Button("About.CopyDiagnostics",(_,_)=>{if(!preview){diagnostics.Text=Data();diagnostics.Visible=true;diagnostics.Height=TextRenderer.MeasureText(diagnostics.Text,diagnostics.Font,new Size(Math.Max(200,Body.ClientSize.Width-80),int.MaxValue),TextFormatFlags.WordBreak).Height+30;preview=true;ClientSize=new(ClientSize.Width,Math.Min(Screen.FromControl(this).WorkingArea.Height-80,ClientSize.Height+(int)(210*DeviceDpi/96f)));return;}try{Clipboard.SetText(diagnostics.Text);status.Text=L.T("About.Copied");}catch{status.Text=L.T("About.CopyFailed");}},true,width:205);
  var build=Theme.Label("",38,12,Theme.Accent);
  Stack(L.Label("About.Title",40,18),build,L.Label("About.Unofficial",44),L.Label("About.Storage",44,10,Theme.Muted),L.Label("About.License",48,10,Theme.Muted),L.Label("About.DiagnosticsHint",42,9,Theme.Muted),diagnostics,status);
  Footer.Controls.AddRange([L.Button("Common.Close",(_,_)=>Close()),copy]);
  L.Watch(this,()=>{Text=AccessibleName=L.T("About.Title");build.Text=L.T("About.Build",L.Version);diagnostics.AccessibleName=L.T("About.DiagnosticsPreview");if(preview)diagnostics.Text=Data();});
 }
}

