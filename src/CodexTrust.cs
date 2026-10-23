using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
namespace CodexUsageMonitor;

internal static class CodexTrust
{
    static readonly object gate=new();static readonly HashSet<string> verified=[];
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct TrustFile {public uint size;[MarshalAs(UnmanagedType.LPWStr)]public string path;public IntPtr file;public IntPtr subject;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct TrustData {public uint size;public IntPtr policy,sip;public uint ui,revocation,choice;public IntPtr file;public uint action;public IntPtr state;public IntPtr url;public uint flags,context;}
    [DllImport("wintrust.dll",ExactSpelling=true)]static extern int WinVerifyTrust(IntPtr hwnd,ref Guid action,ref TrustData data);
    internal static void Verify(string path,string root)
    {
        string full=Path.GetFullPath(path),sha;
        using(var f=File.OpenRead(full))sha=Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant();
        string key=full+"|"+sha;
        lock(gate){
            if(!verified.Contains(key)){
                var info=new TrustFile{size=(uint)Marshal.SizeOf<TrustFile>(),path=full};var pointer=Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
                try{
                    Marshal.StructureToPtr(info,pointer,false);
                    var data=new TrustData{size=(uint)Marshal.SizeOf<TrustData>(),ui=2,choice=1,file=pointer,action=1,flags=0x1000};
                    var action=new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
                    int result;
                    try{result=WinVerifyTrust(new IntPtr(-1),ref action,ref data);}finally{data.action=2;WinVerifyTrust(new IntPtr(-1),ref action,ref data);}
                    if(result!=0)throw new MonitorException("CODEX_SIGNATURE_INVALID");
#pragma warning disable SYSLIB0057
                    using var cert=new X509Certificate2(X509Certificate.CreateFromSignedFile(full));
#pragma warning restore SYSLIB0057
                    string publisher=cert.GetNameInfo(X509NameType.SimpleName,false);
                    if(!new[]{"OpenAI OpCo, LLC","OpenAI, LLC"}.Contains(publisher,StringComparer.OrdinalIgnoreCase))throw new MonitorException("CODEX_PUBLISHER_INVALID");
                    verified.Add(key);
                }finally{Marshal.DestroyStructure<TrustFile>(pointer);Marshal.FreeHGlobal(pointer);}
            }
            AtomicJson.Save(Path.Combine(root,"verified_codex.v1.json"),new{schema="codex-executable.v1",path=full,sha256=sha,signature="VALID_OPENAI"});
        }
    }
}
