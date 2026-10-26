using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
namespace CodexUsageMonitor;

// Use the Unicode Shell Link interface, not WScript's locale-dependent Save path.
// Writes only the requested shortcut. It never resolves or starts its target.
internal static class WindowsShortcut
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLinkObject { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length, IntPtr findData, uint flags);
        void GetIDList(out IntPtr list);
        void SetIDList(IntPtr list);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int length);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int length);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int length);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    internal static void Save(string shortcutPath, string executable)
    {
        string destination = Path.GetFullPath(shortcutPath);
        string target = Path.GetFullPath(executable);
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!,
            "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp.lnk");
        object? instance = null;
        try
        {
            instance = new ShellLinkObject();
            var link = (IShellLinkW)instance;
            link.SetPath(target);
            link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
            link.SetDescription("CodexUsageMonitor");
            ((IPersistFile)instance).Save(temporary, true);
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (instance != null) Marshal.FinalReleaseComObject(instance);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
