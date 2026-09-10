using System.Runtime.InteropServices;
namespace CodexUsageMonitor;
// Immutable per-export observation; never a process-global test/runtime setting.
internal sealed record UiDpi(int? Value,string Source) {
 [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
 public static UiDpi Capture(Control control){
  if(control.IsDisposed||!control.IsHandleCreated||control.InvokeRequired)return new(null,"UNKNOWN_NO_UI_HANDLE");
  try{uint dpi=GetDpiForWindow(control.Handle);return dpi>0?new((int)dpi,"UI_WINDOW_GET_DPI_FOR_WINDOW"):new(null,"UNKNOWN_WINDOW_DPI");}
  catch(EntryPointNotFoundException){return new(null,"UNKNOWN_API_UNAVAILABLE");}
 }
}
