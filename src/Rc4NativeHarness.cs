using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CodexUsageMonitor;

// Scoped rc.4 native visual QA. It runs only on an isolated non-input Windows desktop.
internal static class Rc4NativeHarness
{
 static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-09-11T12:00:00Z",CultureInfo.InvariantCulture);
 [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint id);
 [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr obj,int index,StringBuilder text,uint size,out uint needed);
 [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
 [DllImport("user32.dll")] static extern IntPtr SetActiveWindow(IntPtr window);
 [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
 [DllImport("user32.dll")] static extern IntPtr GetFocus();
 [DllImport("user32.dll")] static extern void NotifyWinEvent(uint eventId,IntPtr window,int objectId,int childId);
 [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window,int index);
 const uint EventSystemForeground=0x0003;const int ObjectIdWindow=0,ChildIdSelf=0,GwlExStyle=-20,WsExTopMost=0x00000008;

 public static void Attach(OverlayForm main,MonitorService mainService,string[] args)
 {
  int pos=Array.IndexOf(args,"--rc4-native");if(pos<0)return;
  string output=Path.GetFullPath(args[pos+1]);Directory.CreateDirectory(output);var desktop=new StringBuilder(256);
  GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()),2,desktop,512,out _);
  if(!desktop.ToString().StartsWith("CUM_RC4_QA_",StringComparison.Ordinal))throw new InvalidOperationException("rc.4 native QA requires the isolated non-input desktop.");
  var checks=new List<object>();var frames=new List<object>();var pixels=new Dictionary<string,(int amber,int red)>();int failures=0;
  void Check(string id,bool pass,object? detail=null){if(!pass)failures++;checks.Add(new{id,result=pass?"PASS":"FAIL",classification="ACTUAL_NATIVE_NON_INPUT_DESKTOP",detail});AtomicJson.Save(Path.Combine(output,"native-checks.json"),checks);}
  main.Shown+=async(_,_)=>{
   string originalLanguage=L.Language;
   try{
    async Task Capture(Form form,string name,string scenario){
     if(!form.Visible)form.Show();await Task.Delay(260);form.Invalidate(true);form.Refresh();form.Update();Application.DoEvents();
     void Repaint(Control root){foreach(Control child in root.Controls){child.Invalidate(true);child.Update();Repaint(child);}}
     Repaint(form);await Task.Delay(100);Application.DoEvents();
     using(var warmup=new Bitmap(form.Width,form.Height))using(var graphics=Graphics.FromImage(warmup)){var dc=graphics.GetHdc();try{PrintWindow(form.Handle,dc,2);}finally{graphics.ReleaseHdc(dc);}}
     form.Invalidate(true);Repaint(form);await Task.Delay(80);Application.DoEvents();
     using var image=new Bitmap(form.Width,form.Height);bool printed;using(var graphics=Graphics.FromImage(image)){var dc=graphics.GetHdc();try{printed=PrintWindow(form.Handle,dc,2);}finally{graphics.ReleaseHdc(dc);}}
     int compositedButtons=0;
     if(form is ProductForm product)using(var graphics=Graphics.FromImage(image))foreach(var button in product.AllControls().OfType<QuietButton>().Where(x=>x.Visible)){
      using var controlImage=new Bitmap(button.Width,button.Height);button.DrawToBitmap(controlImage,new Rectangle(Point.Empty,button.Size));var screen=button.PointToScreen(Point.Empty);graphics.DrawImageUnscaled(controlImage,screen.X-form.Left,screen.Y-form.Top);compositedButtons++;
     }
     string path=Path.Combine(output,name+".png");image.Save(path,System.Drawing.Imaging.ImageFormat.Png);
     int amber=Pixels(image,Theme.Warning),red=Pixels(image,Theme.ResetAlert),accent=Pixels(image,Theme.Accent);
     pixels[name]=(amber,red);
     var record=new{name,scenario,classification="ACTUAL_NATIVE_PRINTWINDOW_NON_INPUT_DESKTOP",desktop=desktop.ToString(),input_desktop_switched=false,
      synthetic=true,exe_sha256=ReportExporter.ExeHash(),ui_language=L.Language,dpi=form.DeviceDpi,bounds=new{form.Left,form.Top,form.Width,form.Height},
      print_window_success=printed,ownerdrawn_buttons_composited=compositedButtons,amber_pixels=amber,red_pixels=red,accent_pixels=accent,png_sha256=Hash(File.ReadAllBytes(path)),
      controls=form is ProductForm p?p.AllControls().Where(x=>x.Visible).Select(x=>new{type=x.GetType().Name,text=x.Text,x.Left,x.Top,x.Width,x.Height}).ToArray():null};
     frames.Add(record);AtomicJson.Save(Path.ChangeExtension(path,"json"),record);Check(name+" native frame",printed&&new FileInfo(path).Length>1000,new{amber,red,accent});
    }
    async Task<bool> WaitForReassert(OverlayForm overlay,int before){for(int i=0;i<20;i++){Application.DoEvents();if(overlay.TopMostReassertCount>before)return true;await Task.Delay(25);}return false;}
    Announcement Watch(string id)=>Parse("team-codex-reset",TeamHtml(id,"When I say excellent service for existing Codex users, that includes the occasional reset."));
    Announcement Incoming(string id)=>Parse("team-codex-reset",TeamHtml(id,"Codex global reset will be applied tomorrow around 6 PM PT for all paid plans."));
    MonitorService Service(string name,IEnumerable<SourceStatus>? sources=null){string root=Path.Combine(output,"data-"+name);if(sources!=null)AtomicJson.Save(Path.Combine(root,"reset_radar.json"),new RadarState{Sources=sources.ToList()});return new(root);}
    SourceStatus[] Health(string failed="")=>ResetRadar.Sources.Select(x=>new SourceStatus(x.Id,x.Url,x.Id==failed?"DEGRADED":"HEALTHY",Now,Now,Now.AddMinutes(15),x.Id==failed?1:0,x.Id==failed?403:200,0,SourceClass:x.SourceClass,DisplayName:x.DisplayName,Adapter:x.Adapter,Error:x.Id==failed?"HTTP_403":"")).ToArray();

    L.SetLanguage("en-US");mainService.Settings.Compact=true;main.Render();await Capture(main,"V01-compact-no-signal","Compact no signal");
    foreach(var scenario in new[]{("watch",Watch("3100000000000000002")),("incoming",Incoming("3100000000000000003"))}){
     using var service=Service("overlay-"+scenario.Item1);service.Radar.Apply([scenario.Item2],Now);
     using var overlay=new OverlayForm(service,true){TestWeekly="43%",TestAge="23s",TestCountdownTip=L.T("Overlay.CountdownNormal",23),TestRadarCoverage="70%",TestDays=["—","—","—"]};
     service.Settings.Compact=true;overlay.Render();await Capture(overlay,scenario.Item1=="watch"?"V02-compact-watch":"V03-compact-incoming",scenario.Item1+" Compact indicator");
     Check(scenario.Item1+" Compact percent retained",overlay.DisplayText.StartsWith("43%")&&overlay.RadarHit.Width>=(int)(24*overlay.UiScale)-1&&!overlay.ModeHit.IntersectsWith(overlay.RadarHit));
     service.Settings.Compact=false;overlay.Render();await Capture(overlay,scenario.Item1=="watch"?"V04-standard-watch":"V05-standard-incoming",scenario.Item1+" Standard indicator");overlay.Close();
    }
    foreach(var language in new[]{"en-US","zh-TW"}){
     L.SetLanguage(language);
     using(var service=Service("watch-card-"+language)){var item=Watch(language=="en-US"?"3100000000000000006":"3100000000000000007");service.Radar.Apply([item],Now);using var page=new ResetInfoForm(service,service.Radar.Unread);page.Show();await Task.Delay(100);ScrollTo(page,page.AllControls().OfType<RadarAccentPanel>().Single());await Capture(page,language=="en-US"?"V06-watch-card-en":"V07-watch-card-zh","WATCH card "+language);Check("WATCH read severity retained "+language,page.DisplayedSignalLevel=="WATCH"&&service.Radar.Snapshot().Events.Single().ReadAt!=null);}
     using(var service=Service("incoming-card-"+language)){var item=Incoming(language=="en-US"?"3100000000000000008":"3100000000000000009");service.Radar.Apply([item],Now);using var page=new ResetInfoForm(service,service.Radar.Unread);page.Show();await Task.Delay(100);ScrollTo(page,page.AllControls().OfType<RadarAccentPanel>().Single());await Capture(page,language=="en-US"?"V08-incoming-card-en":"V09-incoming-card-zh","INCOMING card "+language);Check("INCOMING read severity retained "+language,page.DisplayedSignalLevel=="INCOMING"&&service.Radar.Snapshot().Events.Single().ReadAt!=null);}
    }
    L.SetLanguage("en-US");
    using(var service=Service("health-partial",Health("openai-help-resets"))){using var page=new ResetInfoForm(service,null);await Capture(page,"V10-source-health-partial","Source health partial");Check("Source failure is not reset alert",service.Radar.Unread==null&&page.DisplayedSignalLevel=="NONE");}
    L.SetLanguage("zh-TW");
    using(var service=Service("health-degraded",Health("team-codex-reset"))){using var page=new ResetInfoForm(service,null);await Capture(page,"V11-source-health-degraded","Source health degraded");Check("Relay failure is not incoming",service.Radar.Unread==null&&page.DisplayedSignalLevel=="NONE");}
    L.SetLanguage("en-US");
    using(var service=Service("source-buttons")){var item=Incoming("3100000000000000012");service.Radar.Apply([item],Now);using var page=new ResetInfoForm(service,service.Radar.Unread);await Capture(page,"V12-original-relay-buttons","Distinct original and relay controls");var buttons=page.AllControls().OfType<Button>().ToArray();Check("Original and relay buttons distinct",buttons.Any(x=>x.Text==L.T("Radar.OpenOriginal")&&x.Enabled)&&buttons.Any(x=>x.Text==L.T("Radar.OpenRelay")&&x.Enabled));}

    foreach(var language in new[]{"en-US","zh-TW"}){
     L.SetLanguage(language);using var service=Service("no-signal-"+language,Health());using var page=new ResetInfoForm(service,null);
     await Capture(page,"V13-radar-no-signal-"+(language=="en-US"?"en":"zh"),"Radar page no signal "+language);
    }
    L.SetLanguage("en-US");
    using(var service=Service("delta-watch")){var item=Watch("3100000000000000014");service.Radar.Apply([item],Now);using var page=new ResetInfoForm(service,service.Radar.Unread);await Capture(page,"V14-radar-watch","Radar page WATCH amber");}
    using(var service=Service("delta-incoming")){var item=Incoming("3100000000000000015");service.Radar.Apply([item],Now);using var page=new ResetInfoForm(service,service.Radar.Unread);await Capture(page,"V15-radar-incoming","Radar page INCOMING red");}
    L.SetLanguage("zh-TW");
    using(var service=Service("observed")){var rows=R004Tests.Rows(74,100);var item=Incoming("3100000000000000016") with{ResetUtc=rows[1].observed_at_utc,Phase="COMPLETED",SignalLevel="COMPLETED",FirstSeen=DateTimeOffset.UtcNow,LastSeen=DateTimeOffset.UtcNow};service.Radar.Apply([item],DateTimeOffset.UtcNow);service.Radar.Correlate(rows);using var page=new ResetInfoForm(service,item);await Capture(page,"V16-radar-observed","Radar page locally observed replenishment");Check("Observed still cause unknown",service.Radar.Snapshot().Correlations.Single().Cause=="UNKNOWN");}
    using(var service=Service("delta-partial",Health("team-modelyard-rss"))){using var page=new ResetInfoForm(service,null);await Capture(page,"V17-radar-partial-zh","Radar page source partially degraded zh-TW");}
    L.SetLanguage("en-US");
    using(var service=Service("details",Health("openai-help-resets"))){using var page=new ResetInfoForm(service,null);page.Show();await Task.Delay(100);page.AllControls().OfType<Button>().Single(x=>x.Text==L.T("Radar.ShowDetails")).PerformClick();var body=page.Controls.OfType<Panel>().First(x=>x.AutoScroll);var heading=page.AllControls().OfType<Label>().Single(x=>x.Text==L.T("Radar.SourcesHeading"));var details=page.AllControls().OfType<Label>().Single(x=>x.Text.Contains("https://codex-reset.com/tibo"));body.ScrollControlIntoView(heading);await Capture(page,"V18-source-details-expanded","Expanded per-source health details");body.ScrollControlIntoView(page.AllControls().OfType<Button>().Single(x=>x.Text==L.T("Radar.ShowHow")));await Capture(page,"V18b-source-details-bottom","Expanded source health final rows");int measured=TextRenderer.MeasureText(details.Text,details.Font,new Size(Math.Max(240,body.ClientSize.Width-body.Padding.Horizontal),int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl).Height;Check("All source detail rows have layout and bottom capture",details.Width>0&&details.Height>=measured&&details.Text.Contains("ModelYard")&&details.Text.Contains("codex-reset.com/tibo"));}
    L.SetLanguage("zh-TW");
    using(var service=Service("multi")){var watch=Watch("3100000000000000018");var incoming=Incoming("3100000000000000019");service.Radar.Apply([watch,incoming],Now);using var page=new ResetInfoForm(service,service.Radar.Unread);await Capture(page,"V19-multiple-watch-incoming","Multiple WATCH and INCOMING signals");var rows=page.AllControls().OfType<Label>().Where(x=>x.Parent is FlowLayoutPanel&&x.Text.Contains("  ·  ")).ToArray();Check("Highest signal first",page.DisplayedSignalLevel=="INCOMING");Check("Multiple signal rows visible with both accents",rows.Length==2&&rows.All(x=>x.Visible&&x.Width>0&&x.Height>0)&&pixels["V19-multiple-watch-incoming"].amber>0&&pixels["V19-multiple-watch-incoming"].red>0,new{rows=rows.Select(x=>new{x.Text,x.Width,x.Height}).ToArray(),pixels=pixels["V19-multiple-watch-incoming"]});}
    foreach(var scenario in new[]{("V20",Watch("3100000000000000020")),("V21",Incoming("3100000000000000021"))}){
     string prefix=scenario.Item1;L.SetLanguage(prefix=="V20"?"en-US":"zh-TW");using var service=Service(prefix);service.Radar.Apply([scenario.Item2],Now);
     using var overlay=new OverlayForm(service,true){TestWeekly="43%",TestAge="23s",TestCountdownTip=L.T("Overlay.CountdownNormal",23),TestRadarCoverage="70%",TestDays=["—","—","—"]};service.Settings.Compact=true;overlay.Render();await Capture(overlay,prefix+"-compact-indicator",prefix+" Compact indicator before action");
     overlay.AccessibilityObject.GetChild(1)!.DoDefaultAction();await Task.Delay(180);var page=Application.OpenForms.OfType<ResetInfoForm>().LastOrDefault();
     Check(prefix+" indicator opens same Radar page",page!=null&&page.DisplayedEventId==scenario.Item2.EventId);if(page!=null){await Capture(page,prefix+"-opened-radar-page",prefix+" indicator to Radar page");page.Close();}overlay.Close();
    }
    L.SetLanguage("en-US");
    foreach(var scenario in new[]{("V31-compact-90s","90s",false),("V32-compact-8s","8s",false),("V33-compact-0s","0s",false),("V34-compact-retry-30s","30s ⚠",true)}){
     using var service=Service("countdown-"+scenario.Item1);using var overlay=new OverlayForm(service,true){TestWeekly="27%",TestAge=scenario.Item2,TestCountdownTip=scenario.Item3?L.T("Overlay.CountdownRetry",30):scenario.Item2=="0s"?L.T("Overlay.CountdownNow"):L.T("Overlay.CountdownNormal",int.Parse(scenario.Item2.TrimEnd('s'))),TestCountdownFailed=scenario.Item3,TestRadarCoverage="70%",TestDays=["—","—","—"]};service.Settings.Compact=true;overlay.Render();await Capture(overlay,scenario.Item1,scenario.Item1);Check(scenario.Item1+" hierarchy",overlay.ClientSize==new Size((int)(156*overlay.UiScale),(int)(88*overlay.UiScale))&&overlay.DisplayText.StartsWith("27%\n"+scenario.Item2+" · ◔70%"));
    }
    using(var service=Service("countdown-watch")){var item=Watch("3100000000000000035");service.Radar.Apply([item],Now);using var overlay=new OverlayForm(service,true){TestWeekly="27%",TestAge="8s",TestCountdownTip=L.T("Overlay.CountdownNormal",8),TestRadarCoverage="70%"};service.Settings.Compact=true;overlay.Render();await Capture(overlay,"V35-compact-watch-countdown","Compact amber signal with countdown and coverage");Check("V35 amber signal countdown",pixels["V35-compact-watch-countdown"].amber>0&&overlay.DisplayText.Contains("8s · ◔70%"));}
    using(var service=Service("countdown-incoming")){var item=Incoming("3100000000000000036");service.Radar.Apply([item],Now);using var overlay=new OverlayForm(service,true){TestWeekly="27%",TestAge="8s",TestCountdownTip=L.T("Overlay.CountdownNormal",8),TestRadarCoverage="70%"};service.Settings.Compact=true;overlay.Render();await Capture(overlay,"V36-compact-incoming-countdown","Compact red signal with countdown and coverage");Check("V36 red signal countdown",pixels["V36-compact-incoming-countdown"].red>0&&overlay.DisplayText.Contains("8s · ◔70%"));}
    using(var service=Service("countdown-standard")){using var overlay=new OverlayForm(service,true){TestWeekly="27%",TestAge="8s",TestCountdownTip=L.T("Overlay.CountdownNormal",8),TestRadarCoverage="70%",TestDays=["≈1%","≈2%","—"]};service.Settings.Compact=false;overlay.Render();await Capture(overlay,"V37-standard-countdown","Standard overlay quota countdown");Check("V37 Standard five-item layout retained",overlay.ClientSize==new Size((int)(330*overlay.UiScale),(int)(84*overlay.UiScale))&&overlay.CountdownText=="8s");}
    string enTip,zhTip;using(var service=Service("countdown-hover")){using var overlay=new OverlayForm(service,true){TestWeekly="27%",TestAge="8s",TestRadarCoverage="70%"};service.Settings.Compact=true;L.SetLanguage("en-US");overlay.TestCountdownTip=L.T("Overlay.CountdownNormal",8);overlay.Render();enTip=overlay.CountdownTooltip;await Capture(overlay,"V38-countdown-hover-en","English countdown hover semantics");L.SetLanguage("zh-TW");overlay.TestCountdownTip=L.T("Overlay.CountdownNormal",8);overlay.Render();zhTip=overlay.CountdownTooltip;await Capture(overlay,"V38-countdown-hover-zh","Traditional Chinese countdown hover semantics");}Check("V38 bilingual hover text",enTip=="Next quota refresh in 8 seconds"&&zhTip=="距離下一次額度更新還有 8 秒",new{enTip,zhTip});
    L.SetLanguage("en-US");using(var service=Service("topmost-matrix")){
     service.Settings.TopMost=true;
     using var codex=new Form{Text="Codex foreground fixture",StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(40,40,900,650),ShowInTaskbar=false};
     using var other=new Form{Text="Other foreground fixture",StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(80,80,700,500),ShowInTaskbar=false};
     using var editor=new TextBox{Multiline=true,Dock=DockStyle.Fill,Text="typing:"};codex.Controls.Add(editor);other.Show();codex.Show();SetActiveWindow(codex.Handle);editor.Focus();Application.DoEvents();IntPtr activeBefore=GetActiveWindow(),focusBefore=GetFocus();
     using var overlay=new OverlayForm(service,true){TestWeekly="27%",TestAge="8s",TestCountdownTip=L.T("Overlay.CountdownNormal",8),TestRadarCoverage="70%"};service.Settings.Compact=false;overlay.Show();overlay.ApplySettings();await Task.Delay(100);
     int hookOtherBefore=overlay.TopMostReassertCount;SetActiveWindow(other.Handle);NotifyWinEvent(EventSystemForeground,other.Handle,ObjectIdWindow,ChildIdSelf);bool hookOther=await WaitForReassert(overlay,hookOtherBefore);
     int hookCodexBefore=overlay.TopMostReassertCount;SetActiveWindow(codex.Handle);editor.Focus();NotifyWinEvent(EventSystemForeground,codex.Handle,ObjectIdWindow,ChildIdSelf);bool hookCodex=await WaitForReassert(overlay,hookCodexBefore);bool hookFocus=GetActiveWindow()==codex.Handle&&GetFocus()==editor.Handle;
     int initial=overlay.TopMostReassertCount;bool onOpen=overlay.ReassertTopMostForTest(codex.Handle);codex.WindowState=FormWindowState.Maximized;Application.DoEvents();bool onMax=overlay.ReassertTopMostForTest(codex.Handle);codex.WindowState=FormWindowState.Minimized;Application.DoEvents();codex.WindowState=FormWindowState.Normal;Application.DoEvents();bool onRestore=overlay.ReassertTopMostForTest(codex.Handle);
     overlay.ToggleMode();bool compact=overlay.Compact&&overlay.TopMost;overlay.ToggleMode();bool standard=!overlay.Compact&&overlay.TopMost;overlay.Hide();int hiddenCount=overlay.TopMostReassertCount;bool hiddenAttempt=overlay.ReassertTopMostForTest(codex.Handle);overlay.Reveal();bool shown=overlay.Visible&&overlay.TopMostReassertCount>hiddenCount;
     service.Settings.TopMost=false;overlay.ApplySettings();int offCount=overlay.TopMostReassertCount;bool offAttempt=overlay.ReassertTopMostForTest(codex.Handle);bool offState=!overlay.TopMost&&(GetWindowLong(overlay.Handle,GwlExStyle)&WsExTopMost)==0;SetActiveWindow(other.Handle);NotifyWinEvent(EventSystemForeground,other.Handle,ObjectIdWindow,ChildIdSelf);await Task.Delay(180);Application.DoEvents();bool offHookSilent=overlay.TopMostReassertCount==offCount;
     service.Settings.TopMost=true;overlay.ApplySettings();bool onAgain=overlay.TopMost&&(GetWindowLong(overlay.Handle,GwlExStyle)&WsExTopMost)!=0&&overlay.TopMostReassertCount>offCount;
     SetActiveWindow(codex.Handle);editor.Focus();editor.SelectionStart=editor.TextLength;editor.SelectedText="abc";IntPtr activeTyping=GetActiveWindow(),focusTyping=GetFocus();int typedBefore=overlay.TopMostReassertCount;NotifyWinEvent(EventSystemForeground,codex.Handle,ObjectIdWindow,ChildIdSelf);bool typedReassert=await WaitForReassert(overlay,typedBefore);editor.SelectedText="def";Application.DoEvents();bool focusHeld=GetActiveWindow()==activeTyping&&GetFocus()==focusTyping&&editor.Text.EndsWith("abcdef");uint requiredFlags=0x0001|0x0002|0x0010;
     Check("TopMost T01-T04 foreground hook open maximize restore",overlay.ForegroundHookInstalled&&hookOther&&hookCodex&&hookFocus&&onOpen&&onMax&&onRestore&&overlay.TopMostReassertCount>=initial+3);
     Check("TopMost T05-T07 modes hide show",compact&&standard&&!hiddenAttempt&&shown);
     Check("TopMost T08-T09 OFF then ON",!offAttempt&&offState&&offHookSilent&&onAgain,new{off_state=offState,off_hook_silent=offHookSilent,on_again=onAgain});
     Check("TopMost T10 NOACTIVATE keeps editor focus",typedReassert&&focusHeld&&activeBefore==codex.Handle&&focusBefore==editor.Handle&&overlay.LastTopMostFlags==requiredFlags,new{active_before=activeBefore.ToInt64(),focus_before=focusBefore.ToInt64(),active_after=GetActiveWindow().ToInt64(),focus_after=GetFocus().ToInt64(),editor_text=editor.Text,flags=overlay.LastTopMostFlags});
     Check("TopMost T11-T12 Reset action drag and context surfaces retained",overlay.RadarHit.Width>0&&overlay.ModeHit.Width>0&&overlay.ContextMenuStrip!=null&&!overlay.ShowInTaskbar);
     Check("TopMost T13 actual DPI and screens recorded",overlay.DeviceDpi>=96&&Screen.AllScreens.Length>=1,new{dpi=overlay.DeviceDpi,screens=Screen.AllScreens.Select(x=>x.DeviceName).ToArray()});overlay.Close();codex.Close();other.Close();
    }
    Check("All native resources resolved",L.Missing.Count==0,L.Missing.Keys.ToArray());
    AtomicJson.Save(Path.Combine(output,"native-complete.json"),new{version=L.Version,exe_sha256=ReportExporter.ExeHash(),desktop=desktop.ToString(),input_desktop_switched=false,actual_os=Environment.OSVersion.VersionString,actual_dpi=main.DeviceDpi,frame_count=frames.Count,check_count=checks.Count,failures,visual_review="PENDING_MODEL_IMAGE_INSPECTION",model_calls=0,quota_producer_started=false});
   }catch(Exception ex){failures++;AtomicJson.Save(Path.Combine(output,"native-failure.json"),new{exception=ex.GetType().Name,error=ex.Message,exe_sha256=ReportExporter.ExeHash()});}
   finally{L.SetLanguage(originalLanguage);Environment.ExitCode=failures==0?0:1;main.Close();}
  };
 }

 static Announcement Parse(string source,string html){var definition=ResetRadar.Sources.Single(x=>x.Id==source);var result=Rc4Radar.Parse(definition,html,Now,out _);if(result.Count!=1)throw new InvalidOperationException("Native fixture parse failed.");return result.Single();}
 static string TeamHtml(string id,string quote)=>"<ul><li class=\"feed-item\"><p class=\"feed-text\">"+System.Net.WebUtility.HtmlEncode(quote)+"</p><a href=\"https://x.com/thsottiaux/status/"+id+"\">original</a><a class=\"feed-time\">1h ago</a></li></ul>";
 static void ScrollTo(ResetInfoForm page,Control target){var body=page.Controls.OfType<Panel>().First(x=>x.AutoScroll);body.ScrollControlIntoView(target);}
 static string Hash(byte[] data)=>Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
 static int Pixels(Bitmap image,Color color){int count=0;for(int y=0;y<image.Height;y+=2)for(int x=0;x<image.Width;x+=2){var p=image.GetPixel(x,y);if(Math.Abs(p.R-color.R)<=5&&Math.Abs(p.G-color.G)<=5&&Math.Abs(p.B-color.B)<=5)count++;}return count;}
}
