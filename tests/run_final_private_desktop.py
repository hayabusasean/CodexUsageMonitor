"""Run only this project's final native fixture on a new non-input desktop; never switch desktops."""
import ctypes as c
from ctypes import wintypes as w
from pathlib import Path
import json,sys,uuid,datetime,time
r=Path(__file__).resolve().parents[1];exe=(r/sys.argv[1]).resolve();out=(r/sys.argv[2]).resolve()
assert exe.is_relative_to(r) and out.is_relative_to(r) and exe.name=='CodexUsageMonitor.exe'
out.mkdir(parents=True,exist_ok=True)
class SI(c.Structure):
 _fields_=[('cb',w.DWORD),('lpReserved',w.LPWSTR),('lpDesktop',w.LPWSTR),('lpTitle',w.LPWSTR),('dwX',w.DWORD),('dwY',w.DWORD),('dwXSize',w.DWORD),('dwYSize',w.DWORD),('dwXCountChars',w.DWORD),('dwYCountChars',w.DWORD),('dwFillAttribute',w.DWORD),('dwFlags',w.DWORD),('wShowWindow',w.WORD),('cbReserved2',w.WORD),('lpReserved2',c.c_void_p),('hStdInput',w.HANDLE),('hStdOutput',w.HANDLE),('hStdError',w.HANDLE)]
class PI(c.Structure):_fields_=[('hProcess',w.HANDLE),('hThread',w.HANDLE),('dwProcessId',w.DWORD),('dwThreadId',w.DWORD)]
u=c.WinDLL('user32',use_last_error=True);k=c.WinDLL('kernel32',use_last_error=True)
u.CreateDesktopW.argtypes=[w.LPCWSTR,w.LPCWSTR,c.c_void_p,w.DWORD,w.DWORD,c.c_void_p];u.CreateDesktopW.restype=w.HANDLE
u.CloseDesktop.argtypes=[w.HANDLE];k.CloseHandle.argtypes=[w.HANDLE]
k.CreateProcessW.argtypes=[w.LPCWSTR,w.LPWSTR,c.c_void_p,c.c_void_p,w.BOOL,w.DWORD,c.c_void_p,w.LPCWSTR,c.POINTER(SI),c.POINTER(PI)];k.CreateProcessW.restype=w.BOOL
k.WaitForSingleObject.argtypes=[w.HANDLE,w.DWORD];k.GetExitCodeProcess.argtypes=[w.HANDLE,c.POINTER(w.DWORD)]
name='CUM_FINAL_QA_'+uuid.uuid4().hex[:12];desktop=u.CreateDesktopW(name,None,None,0,0x1ff,None)
if not desktop:raise c.WinError(c.get_last_error())
info=SI();info.cb=c.sizeof(info);info.lpDesktop='WinSta0\\'+name;pi=PI();started=False
try:
 command=c.create_unicode_buffer('"'+str(exe)+'" --final-dpi-test --test-root "'+str(out)+'"')
 if not k.CreateProcessW(str(exe),command,None,None,False,0,None,str(r),c.byref(info),c.byref(pi)):raise c.WinError(c.get_last_error())
 started=True;receipt={'desktop':name,'pid':pi.dwProcessId,'input_desktop_switched':False,'exe':str(exe.relative_to(r)),'started_utc':datetime.datetime.now(datetime.timezone.utc).isoformat()}
 (out/'desktop-launch.json').write_text(json.dumps(receipt,indent=2),encoding='utf-8')
 begin=time.monotonic()
 while k.WaitForSingleObject(pi.hProcess,1000)==258:
  if time.monotonic()-begin>90:
   # Only the process created by this helper can be stopped on timeout.
   k.TerminateProcess.argtypes=[w.HANDLE,w.UINT];k.TerminateProcess(pi.hProcess,1);k.WaitForSingleObject(pi.hProcess,5000);raise TimeoutError('Owned native fixture exceeded90seconds')
 code=w.DWORD();k.GetExitCodeProcess(pi.hProcess,c.byref(code));receipt.update(exit_code=code.value,elapsed_seconds=time.monotonic()-begin,finished_utc=datetime.datetime.now(datetime.timezone.utc).isoformat());(out/'desktop-launch.json').write_text(json.dumps(receipt,indent=2),encoding='utf-8');print(json.dumps(receipt));sys.exit(code.value)
finally:
 if started:k.CloseHandle(pi.hThread);k.CloseHandle(pi.hProcess)
 u.CloseDesktop(desktop)
