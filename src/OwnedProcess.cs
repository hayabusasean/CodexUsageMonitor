using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace CodexUsageMonitor;

// The only launcher. The child cannot execute until assigned to our kill-on-close Job.
// STARTUPINFOEX limits inheritance to these three pipe handles.
internal sealed class OwnedProcess : IDisposable
{
    public Process Process { get; private set; } = null!;
    public StreamReader Output { get; private set; } = null!;
    public StreamReader Error { get; private set; } = null!;
    public StreamWriter Input { get; private set; } = null!;
    IntPtr job; bool disposed;
    public static OwnedProcess Start(string exe, string[] args, string cwd)
    {
        var owner = new OwnedProcess();
        IntPtr ir=0, iw=0, or=0, ow=0, er=0, ew=0, attrs=0, handles=0;
        PI pi = default;
        try {
            owner.job = CreateJobObject(IntPtr.Zero, null); Check(owner.job != IntPtr.Zero);
            var ji = new JOB { BasicLimitInformation = new BASIC { LimitFlags = 0x2000 } };
            Check(SetInformationJobObject(owner.job, 9, ref ji, (uint)Marshal.SizeOf<JOB>()));
            var sa = new SA { nLength=Marshal.SizeOf<SA>(), bInheritHandle=1 };
            Check(CreatePipe(out ir,out iw,ref sa,0)); Check(CreatePipe(out or,out ow,ref sa,0)); Check(CreatePipe(out er,out ew,ref sa,0));
            Check(SetHandleInformation(iw,1,0)); Check(SetHandleInformation(or,1,0)); Check(SetHandleInformation(er,1,0));
            IntPtr size=0; InitializeProcThreadAttributeList(0,1,0,ref size);
            attrs=Marshal.AllocHGlobal(size); Check(InitializeProcThreadAttributeList(attrs,1,0,ref size));
            handles=Marshal.AllocHGlobal(IntPtr.Size*3);
            Marshal.Copy(new[]{ir,ow,ew},0,handles,3);
            Check(UpdateProcThreadAttribute(attrs,0,(IntPtr)0x20002,handles,(IntPtr)(IntPtr.Size*3),0,0));
            var si=new SIX { StartupInfo=new SI { cb=Marshal.SizeOf<SIX>(), dwFlags=0x100, hStdInput=ir,hStdOutput=ow,hStdError=ew }, lpAttributeList=attrs };
            var cmd=new StringBuilder(Quote(exe)+" "+string.Join(" ",args.Select(Quote)));
            Check(CreateProcess(exe,cmd,0,0,true,0x08000000|0x4|0x80000,0,cwd,ref si,out pi));
            Check(AssignProcessToJobObject(owner.job,pi.hProcess));
            owner.Process=Process.GetProcessById((int)pi.dwProcessId);
            owner.Input=new StreamWriter(new FileStream(new SafeFileHandle(iw,true),FileAccess.Write),new UTF8Encoding(false)){AutoFlush=true}; iw=0;
            owner.Output=new StreamReader(new FileStream(new SafeFileHandle(or,true),FileAccess.Read),Encoding.UTF8); or=0;
            owner.Error=new StreamReader(new FileStream(new SafeFileHandle(er,true),FileAccess.Read),Encoding.UTF8); er=0;
            Check(ResumeThread(pi.hThread)!=uint.MaxValue);
            return owner;
        } catch {
            if(pi.hProcess!=0) TerminateProcess(pi.hProcess,1);
            owner.Dispose(); throw;
        } finally {
            foreach(var h in new[]{ir,iw,or,ow,er,ew,pi.hThread,pi.hProcess}) if(h!=0)CloseHandle(h);
            if(attrs!=0){DeleteProcThreadAttributeList(attrs);Marshal.FreeHGlobal(attrs);}
            if(handles!=0)Marshal.FreeHGlobal(handles);
        }
    }
    internal static string Quote(string s) => "\"" + System.Text.RegularExpressions.Regex.Replace(s, @"(\\*)(""|$)", m => m.Groups[1].Value+m.Groups[1].Value+(m.Groups[2].Value=="\""?"\\\"":""))+"\"";
    static void Check(bool ok){if(!ok)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());}
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        try{Input?.Dispose();}catch{}
        try{Process?.WaitForExit(500);}catch{}
        if(job!=0){CloseHandle(job);job=0;}
        try{Output?.Dispose();Error?.Dispose();Process?.Dispose();}catch{}
    }
    [StructLayout(LayoutKind.Sequential)] struct SA{public int nLength;public IntPtr lpSecurityDescriptor;public int bInheritHandle;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct SI{public int cb;public string? lpReserved,lpDesktop,lpTitle;public int dwX,dwY,dwXSize,dwYSize,dwXCountChars,dwYCountChars,dwFillAttribute,dwFlags;public short wShowWindow,cbReserved2;public IntPtr lpReserved2,hStdInput,hStdOutput,hStdError;}
    [StructLayout(LayoutKind.Sequential)] struct SIX{public SI StartupInfo;public IntPtr lpAttributeList;}
    [StructLayout(LayoutKind.Sequential)] struct PI{public IntPtr hProcess,hThread;public uint dwProcessId,dwThreadId;}
    [StructLayout(LayoutKind.Sequential)] struct BASIC{public long PerProcessUserTimeLimit,PerJobUserTimeLimit;public uint LimitFlags;public UIntPtr MinimumWorkingSetSize,MaximumWorkingSetSize;public uint ActiveProcessLimit;public UIntPtr Affinity;public uint PriorityClass,SchedulingClass;}
    [StructLayout(LayoutKind.Sequential)] struct IO{public ulong ReadOperationCount,WriteOperationCount,OtherOperationCount,ReadTransferCount,WriteTransferCount,OtherTransferCount;}
    [StructLayout(LayoutKind.Sequential)] struct JOB{public BASIC BasicLimitInformation;public IO IoInfo;public UIntPtr ProcessMemoryLimit,JobMemoryLimit,PeakProcessMemoryUsed,PeakJobMemoryUsed;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateJobObject(IntPtr a,string? n);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetInformationJobObject(IntPtr h,int c,ref JOB i,uint n);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool AssignProcessToJobObject(IntPtr j,IntPtr p);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool CreatePipe(out IntPtr r,out IntPtr w,ref SA sa,uint n);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetHandleInformation(IntPtr h,uint m,uint f);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool InitializeProcThreadAttributeList(IntPtr p,int n,int f,ref IntPtr size);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool UpdateProcThreadAttribute(IntPtr p,uint f,IntPtr a,IntPtr v,IntPtr size,IntPtr prev,IntPtr ret);
    [DllImport("kernel32.dll")]static extern void DeleteProcThreadAttributeList(IntPtr p);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CreateProcess(string app,StringBuilder cmd,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr env,string cwd,ref SIX si,out PI pi);
    [DllImport("kernel32.dll",SetLastError=true)]static extern uint ResumeThread(IntPtr h);
    [DllImport("kernel32.dll")]static extern bool TerminateProcess(IntPtr h,uint code);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr h);
}

