using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
namespace CodexUsageMonitor;
internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
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
        string root=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)+@"\CodexUsageMonitor";
        int pos=Array.IndexOf(args,"--data-root");if(pos>=0&&pos+1<args.Length)root=Path.GetFullPath(args[pos+1]);
        root=RuntimeIdentity.CanonicalRoot(root);
        var key=Safe.Hash(WindowsIdentity.GetCurrent().User!.Value+"|"+Process.GetCurrentProcess().SessionId+"|"+root)[..24];
        using var mutex=new Mutex(true,@"Local\CodexUsageMonitor."+key,out bool first);
        using var show=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\CodexUsageMonitor.Show."+key);
        if(!first){show.Set();RuntimeIdentity.Handoff(root);return;}
        try {
            using var service=new MonitorService(root);
            RuntimeIdentity.Write(root);
            L.SetLanguage(service.Settings.UiLanguage);
            using var form=new OverlayForm(service,args.Contains("--synthetic-ui"));
            var watcher=ThreadPool.RegisterWaitForSingleObject(show,(_,_)=>{if(form.IsHandleCreated&&!form.IsDisposed)form.BeginInvoke(()=>form.Reveal());},null,Timeout.Infinite,false);
            Application.ApplicationExit+=(_,_)=>service.SaveSettings();
            if(service.Settings.Startup)try{StartupLink.Set(true,Environment.ProcessPath!);}catch{}
            try { SmokeHarness.Attach(form,service,args); NativeR003Harness.Attach(form,service,args); NativeR004Harness.Attach(form,service,args); PublicNativeHarness.Attach(form,service,args); RadarFixNativeHarness.Attach(form,service,args); Rc4NativeHarness.Attach(form,service,args); Rc4CoverageNative.Attach(form,service,args); Rc4SignalPolishNative.Attach(form,service,args); Rc4ActiveUnreadNative.Attach(form,service,args); Rc4HistoryEventNative.Attach(form,service,args); RedesignNative.Attach(form,service,args); GoldNative.Attach(form,service,args); HumanFixProbe.Attach(form,service,args); Application.Run(form); } finally { watcher.Unregister(null); }
        }catch(Exception){
            MessageBox.Show(L.T("Common.Fatal"),"CodexUsageMonitor",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
    }
}
internal static class StartupLink
{
    public static string DefaultPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"CodexUsageMonitor.lnk");
    public static void Set(bool enabled,string exe,string? isolatedPath=null)
    {
        string path=isolatedPath??DefaultPath;
        if(!enabled){if(File.Exists(path))File.Delete(path);return;}
        var type=Type.GetTypeFromProgID("WScript.Shell")??throw new IOException("SHORTCUT_UNAVAILABLE");
        dynamic shell=Activator.CreateInstance(type)!;dynamic link=shell.CreateShortcut(path);
        try{link.TargetPath=Path.GetFullPath(exe);link.WorkingDirectory=Path.GetDirectoryName(exe);link.Description="CodexUsageMonitor";link.Save();}
        finally{Marshal.FinalReleaseComObject(link);Marshal.FinalReleaseComObject(shell);}
    }
}



