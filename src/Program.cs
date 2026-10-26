using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
namespace CodexUsageMonitor;
internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
#if TEST_HARNESS
        if(args.Contains("--first-paint-diagnostics-test")){FirstPaintDiagnosticsTests.Run(args[Array.IndexOf(args,"--test-root")+1]);return;}
        Application.SetHighDpiMode(args.Contains("--ui-dpi96")?HighDpiMode.DpiUnaware:HighDpiMode.PerMonitorV2);
#else
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
#endif
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
#if TEST_HARNESS
        if(args.Contains("--v050-screen-record")){V050ScreenRecordTests.Run(args[Array.IndexOf(args,"--test-root")+1],args.Contains("--ui-dpi96")?96:144,args.Contains("--record-pilot")?1:5);return;}
        if(args.Contains("--v050-ui-test")){V050UiTests.Run(args[Array.IndexOf(args,"--test-root")+1],args.Contains("--ui-dpi96")?96:144);return;}
        if(args.Contains("--live-audit-050-test")){LiveAudit050Tests.Run(args[Array.IndexOf(args,"--test-root")+1],args.Contains("--duration-seconds")?int.Parse(args[Array.IndexOf(args,"--duration-seconds")+1]):3600);return;}
        if(args.Contains("--analytics-050-test")){Analytics050Tests.Run(args[Array.IndexOf(args,"--test-root")+1]);return;}
        if(args.Contains("--radar-050-test")){var root050=args[Array.IndexOf(args,"--test-root")+1];var results050=RadarV050Tests.Run(root050);Environment.ExitCode=results050.Any(x=>System.Text.Json.JsonSerializer.SerializeToElement(x).GetProperty("result").GetString()=="FAIL")?1:0;return;}
        if(args.Contains("--integration-050-test")){V050IntegrationTests.Run(args[Array.IndexOf(args,"--test-root")+1]);return;}
        if(args.Contains("--protocol-fixture")){TestHarness.Producer(args);return;}
        if(args.Contains("--gold-cycle-test")){GoldCycleTests.Run(args[Array.IndexOf(args,"--test-root")+1]);return;}
        if(args.Contains("--gold-real-check")){GoldCycleTests.CheckReal(args[Array.IndexOf(args,"--output")+1]);return;}
        if(args.Contains("--final-phase-test")){FinalPhaseTests.Run(args[Array.IndexOf(args,"--test-root")+1]);return;}
        if(args.Contains("--final-dpi-test")){FinalDpiTests.Run(args[Array.IndexOf(args,"--test-root")+1]);return;}
        if(args.Contains("--radar-fix-test")){RadarFixTests.Run(args[Array.IndexOf(args,"--test-root")+1],args[Array.IndexOf(args,"--fixtures")+1]);return;}
        if(args.Contains("--rc4-coverage-test")){Rc4CoverageTests.Run(args[Array.IndexOf(args,"--test-root")+1]);return;}
        if(args.Contains("--rc4-signal-polish-test")){Rc4SignalPolishTests.Run(args[Array.IndexOf(args,"--test-root")+1]);return;}
        if(args.Contains("--rc4-active-unread-test")){Rc4ActiveUnreadTests.Run(args[Array.IndexOf(args,"--test-root")+1]);return;}
        if(args.Contains("--rc4-active-unread-current-check")){Rc4ActiveUnreadTests.CheckCurrent(args[Array.IndexOf(args,"--output")+1]);return;}
        if(args.Contains("--rc4-history-event-test")){Rc4HistoryEventTests.Run(args[Array.IndexOf(args,"--test-root")+1]);return;}
        if(args.Contains("--rc4-history-event-current-check")){Rc4HistoryEventTests.CheckCurrent(args[Array.IndexOf(args,"--output")+1]);return;}
        if(args.Contains("--rc4-field-trial-test")){Rc4FieldTrialTests.Run(args);return;}
        if(args.Contains("--rc4-public-smoke")){Rc4PublicSmoke.Run(args);return;}
        if(args.Contains("--public-test")){PublicTests.Run(args);return;}
        if(args.Contains("--public-report-test")){PublicReportTests.Run(args);return;}
        if(args.Contains("--public-radar-test")){PublicRadarTests.Run(Path.GetFullPath(args[Array.IndexOf(args,"--test-root")+1]));return;}
        if(args.Contains("--r004-test")){R004Tests.Run(args);return;}
        if(args.Contains("--r003-test")){R003Tests.Run(args);return;}
        if(args.Contains("--self-test")){TestHarness.Run(args);return;}
#endif
        string root;
        try{root=RuntimeEnvironment.DataRoot();}catch{Environment.ExitCode=1;MessageBox.Show("請檢查 CODEX_USAGE_MONITOR_HOME：必須是完整資料夾路徑，不能指向磁碟根目錄、原本的正式資料夾或 CODEX_HOME。","CodexUsageMonitor",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
#if TEST_HARNESS
        int pos=Array.IndexOf(args,"--data-root");if(pos>=0&&pos+1<args.Length)root=Path.GetFullPath(args[pos+1]);
#endif
        root=RuntimeIdentity.CanonicalRoot(root);
        var key=Safe.Hash(WindowsIdentity.GetCurrent().User!.Value+"|"+Process.GetCurrentProcess().SessionId+"|"+root)[..24];
        using var mutex=new Mutex(true,@"Local\CodexUsageMonitor."+key,out bool first);
        using var show=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\CodexUsageMonitor.Show."+key);
        if(!first){show.Set();RuntimeIdentity.Handoff(root);return;}
        try {
            using var metrics=new RuntimeSessionMetrics(root);
            using var service=new MonitorService(root);
            metrics.Attach(service);
            RuntimeIdentity.Write(root);
            service.Settings.UiLanguage="zh-TW";L.SetLanguage("zh-TW");
#if TEST_HARNESS
            using var form=new OverlayForm(service,args.Contains("--synthetic-ui"));
#else
            using var form=new OverlayForm(service);
#endif
            var watcher=ThreadPool.RegisterWaitForSingleObject(show,(_,_)=>{if(form.IsHandleCreated&&!form.IsDisposed)form.BeginInvoke(()=>form.Reveal());},null,Timeout.Infinite,false);
            Application.ApplicationExit+=(_,_)=>service.SaveSettings();
            if(service.Settings.Startup)try{StartupLink.ApplyForDataRoot(service.Root,true,Environment.ProcessPath!);}catch{}
            try {
#if TEST_HARNESS
                SmokeHarness.Attach(form,service,args); NativeR003Harness.Attach(form,service,args); NativeR004Harness.Attach(form,service,args); PublicNativeHarness.Attach(form,service,args); RadarFixNativeHarness.Attach(form,service,args); Rc4NativeHarness.Attach(form,service,args); Rc4CoverageNative.Attach(form,service,args); Rc4SignalPolishNative.Attach(form,service,args); Rc4ActiveUnreadNative.Attach(form,service,args); Rc4HistoryEventNative.Attach(form,service,args); RedesignNative.Attach(form,service,args); GoldNative.Attach(form,service,args); HumanFixProbe.Attach(form,service,args); 
#endif
                Application.Run(form); } finally { watcher.Unregister(null); }
        }catch(Exception){
            MessageBox.Show(L.T("Common.Fatal"),"CodexUsageMonitor",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
    }
}
internal static class StartupLink
{
    public static string DefaultPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"CodexUsageMonitor.lnk");
    internal static bool CanConfigure(string root)
    {
        try{return string.Equals(RuntimeIdentity.CanonicalRoot(root),RuntimeIdentity.CanonicalRoot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexUsageMonitor")),StringComparison.OrdinalIgnoreCase);}
        catch{return false;}
    }
    internal static bool ApplyForDataRoot(string root,bool enabled,string exe,string? isolatedPath=null)
    {
        if(!CanConfigure(root))return false;
        Set(enabled,exe,isolatedPath);return true;
    }
    public static void Set(bool enabled,string exe,string? isolatedPath=null)
    {
        string path=isolatedPath??DefaultPath;
        if(!enabled){if(File.Exists(path))File.Delete(path);return;}
        WindowsShortcut.Save(path,exe);
    }
}
