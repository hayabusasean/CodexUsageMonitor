using System.Diagnostics;
using System.Runtime.InteropServices;
namespace CodexUsageMonitor;
internal static class RuntimeMetrics
{
    public static long? PrivateWorkingSet(Process process){var counters=new Counters{size=(uint)Marshal.SizeOf<Counters>()};return GetProcessMemoryInfo(process.Handle,ref counters,counters.size)?(long)counters.privateWorkingSet:null;}
    [StructLayout(LayoutKind.Sequential)]struct Counters{public uint size,pageFaults;public UIntPtr peakWorkingSet,workingSet,quotaPeakPaged,quotaPaged,quotaPeakNonPaged,quotaNonPaged,pagefile,peakPagefile,privateUsage,privateWorkingSet,sharedCommit;}
    [DllImport("psapi.dll")]static extern bool GetProcessMemoryInfo(IntPtr process,ref Counters counters,uint size);
}
