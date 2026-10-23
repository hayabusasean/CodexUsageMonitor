#Requires -Version 7
[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$RunRoot,
 [string]$BackupRoot='D:\CodexUsageMonitor_review\backup_0.4.1',
 [ValidateRange(600,900)][int]$DurationSeconds=720
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$RunRoot=[IO.Path]::GetFullPath($RunRoot)
$BackupRoot=[IO.Path]::GetFullPath($BackupRoot)
$exe=Join-Path $RunRoot 'portable\CodexUsageMonitor.exe'
$build=Get-Content -LiteralPath (Join-Path $RunRoot 'build-result.json') -Raw|ConvertFrom-Json
$exeHash=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
if($exeHash -ne $build.exe_sha256){throw '最終EXE與建置雜湊不一致。'}
$audit=Join-Path $RunRoot ('live-final-exe-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8))
$runtime=Join-Path $audit 'private-runtime'
if(![IO.Path]::GetFullPath($runtime).StartsWith($RunRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw '隔離路徑不在本次建置內。'}
New-Item -ItemType Directory -Path $runtime -ErrorAction Stop|Out-Null
$security=[Security.AccessControl.DirectorySecurity]::new()
$security.SetAccessRuleProtection($true,$false)
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
$rule=[Security.AccessControl.FileSystemAccessRule]::new($sid,[Security.AccessControl.FileSystemRights]::FullControl,[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit',[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow)
$security.AddAccessRule($rule)
Set-Acl -LiteralPath $runtime -AclObject $security
$backupHashes=@{}
foreach($file in Get-ChildItem -LiteralPath $BackupRoot -Recurse -File){
 $relative=[IO.Path]::GetRelativePath($BackupRoot,$file.FullName)
 $backupHashes[$relative]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
 $destination=Join-Path $runtime $relative
 New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination)|Out-Null
 Copy-Item -LiteralPath $file.FullName -Destination $destination
}
$settingsPath=Join-Path $runtime 'settings.json'
$settings=Get-Content -LiteralPath $settingsPath -Raw|ConvertFrom-Json
foreach($pair in @{Interval=90;Startup=$false;Alerts=$false;StartHidden=$true;RadarEnabled=$true;CodexPath=''}.GetEnumerator()){$settings|Add-Member -NotePropertyName $pair.Key -NotePropertyValue $pair.Value -Force}
$settings|ConvertTo-Json -Depth 20 -Compress|Set-Content -LiteralPath $settingsPath -Encoding utf8
function Read-Rows([string]$Kind){@(Get-ChildItem -LiteralPath (Join-Path $runtime 'history') -Filter ($Kind+'_*.csv') -File|ForEach-Object {Import-Csv -LiteralPath $_.FullName})}
function Jsonl-Lines { [long]$total=0;foreach($file in Get-ChildItem -LiteralPath $runtime -Recurse -Filter '*.jsonl' -File){$total+=@(Get-Content -LiteralPath $file.FullName).Count};return $total }
$beforeUsage=@(Read-Rows 'usage');$beforeEvents=@(Read-Rows 'events');$beforeJsonl=Jsonl-Lines
$knownPolls=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$knownAccounts=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach($row in $beforeUsage){[void]$knownPolls.Add($row.poll_id);[void]$knownAccounts.Add($row.account_context_key)}
$csvBefore=@{}
foreach($file in Get-ChildItem -LiteralPath (Join-Path $runtime 'history') -Filter '*.csv' -File){$csvBefore[$file.Name]=[IO.File]::ReadAllBytes($file.FullName)}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FinalExeAuditCounters {
 [StructLayout(LayoutKind.Sequential)]struct Io {public ulong rOps,wOps,oOps,rBytes,wBytes,oBytes;}
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessIoCounters(IntPtr p,out Io value);
 public static long WriteBytes(IntPtr p){if(!GetProcessIoCounters(p,out var value))throw new System.ComponentModel.Win32Exception();return checked((long)value.wBytes);}
}
'@
$info=[Diagnostics.ProcessStartInfo]::new($exe)
$info.UseShellExecute=$false;$info.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden;$info.WorkingDirectory=Split-Path -Parent $exe
$info.Environment['CODEX_USAGE_MONITOR_HOME']=$runtime
$userCodexHome=[Environment]::GetEnvironmentVariable('CODEX_HOME',[EnvironmentVariableTarget]::User)
if([string]::IsNullOrEmpty($userCodexHome)){$userCodexHome=[Environment]::GetEnvironmentVariable('CODEX_HOME',[EnvironmentVariableTarget]::Machine)}
if([string]::IsNullOrEmpty($userCodexHome)){[void]$info.Environment.Remove('CODEX_HOME')}else{$info.Environment['CODEX_HOME']=$userCodexHome}
$codexHomeSet=![string]::IsNullOrEmpty($userCodexHome)
$process=[Diagnostics.Process]::Start($info)
$handle=$process.Handle;$watch=[Diagnostics.Stopwatch]::StartNew();$start=[DateTimeOffset]::UtcNow
$termination='';$failure=$null;[long]$processWriteBytes=0
try{
 while($watch.Elapsed.TotalSeconds -lt $DurationSeconds){
  Start-Sleep -Seconds 1
  if($process.HasExited){throw '最終EXE在實測時間到達前已退出。'}
  if($watch.Elapsed.TotalSeconds -gt 180 -and !(Test-Path -LiteralPath (Join-Path $runtime 'status.v1.json'))){throw '三分鐘內沒有狀態檔，停止此隔離測試。'}
 }
 $processWriteBytes=[FinalExeAuditCounters]::WriteBytes($handle)
}catch{$failure=$_.Exception.Message}finally{
 if(!$process.HasExited){
  if(![string]::Equals($process.MainModule.FileName,$exe,[StringComparison]::OrdinalIgnoreCase)){throw '程序路徑不相符，拒絕停止。'}
  $process.Kill($true);$process.WaitForExit(10000)|Out-Null;$termination='OWNED_ISOLATED_PROCESS_TREE_TERMINATED_AFTER_MEASUREMENT'
 }
 $elapsed=$watch.Elapsed.TotalSeconds
 try{$processWriteBytes=[FinalExeAuditCounters]::WriteBytes($handle)}catch{}
 $process.Dispose()
}
if($failure){throw $failure}
$afterUsage=@(Read-Rows 'usage');$afterEvents=@(Read-Rows 'events');$newRows=@($afterUsage|Where-Object {!$knownPolls.Contains($_.poll_id)})
$statusPath=Join-Path $runtime 'status.v1.json';$status=Get-Content -LiteralPath $statusPath -Raw|ConvertFrom-Json
$metricsPath=Join-Path $runtime 'diagnostics\runtime-metrics.v1.json';$metrics=Get-Content -LiteralPath $metricsPath -Raw|ConvertFrom-Json
$prefixesOk=$true
foreach($name in $csvBefore.Keys){$current=[IO.File]::ReadAllBytes((Join-Path $runtime ('history\'+$name)));$before=$csvBefore[$name];if($current.Length -lt $before.Length){$prefixesOk=$false;break};for($i=0;$i -lt $before.Length;$i++){if($current[$i] -ne $before[$i]){$prefixesOk=$false;break}}}
$backupUnchanged=$true
foreach($relative in $backupHashes.Keys){if((Get-FileHash -LiteralPath (Join-Path $BackupRoot $relative) -Algorithm SHA256).Hash -ne $backupHashes[$relative]){$backupUnchanged=$false}}
$signaturePath=Join-Path $runtime 'verified_codex.v1.json';$official=$false
if(Test-Path -LiteralPath $signaturePath){$signature=Get-Content -LiteralPath $signaturePath -Raw|ConvertFrom-Json;$official=$signature.signature -eq 'VALID_OPENAI' -and (Get-FileHash -LiteralPath $signature.path -Algorithm SHA256).Hash.ToLowerInvariant() -eq $signature.sha256}
$accountMatches=$newRows.Count -gt 0 -and @($newRows|Where-Object {!$knownAccounts.Contains($_.account_context_key)}).Count -eq 0
$assurances=@($newRows|ForEach-Object context_assurance|Sort-Object -Unique)
$distinctPolls=@($newRows|ForEach-Object poll_id|Sort-Object -Unique).Count
$passed=$elapsed -ge 600 -and $distinctPolls -ge 6 -and $metrics.successful_polls -ge 6 -and $status.ok -eq $true -and $metrics.exe_sha256 -eq $exeHash -and $official -and $prefixesOk -and $backupUnchanged -and $accountMatches -and $assurances.Count -eq 1 -and $assurances[0] -eq 'EMAIL_SCOPED'
Copy-Item -LiteralPath $statusPath -Destination (Join-Path $audit 'status.v1.json')
Copy-Item -LiteralPath $metricsPath -Destination (Join-Path $audit 'runtime-metrics.v1.json')
$receipt=[ordered]@{
 classification='FINAL_SHIPPING_EXE_ISOLATED_LIVE_READ_ONLY';result=if($passed){'PASS'}else{'FAIL'}
 version=$build.version;exe_path=$exe;exe_sha256=$exeHash;zip_sha256=$build.zip_sha256;data_root=$runtime
 started_utc=$start.ToUniversalTime().ToString('o');ended_utc=[DateTimeOffset]::UtcNow.ToString('o');elapsed_seconds=$elapsed;configured_interval_seconds=90
 codex_home_set=$codexHomeSet;codex_home_policy='PERSISTENT_USER_THEN_MACHINE_ELSE_UNSET';concurrent_original_monitor_allowed=$true
 usage_rows_before=$beforeUsage.Count;usage_rows_after=$afterUsage.Count;new_usage_rows=$newRows.Count;new_event_rows=$afterEvents.Count-$beforeEvents.Count;new_jsonl_lines=(Jsonl-Lines)-$beforeJsonl
 successful_polls=$metrics.successful_polls;distinct_new_poll_ids=$distinctPolls
 new_sample_assurances=$assurances;existing_account_key_matched=$accountMatches;existing_csv_prefixes_unchanged=$prefixesOk;original_backup_unchanged=$backupUnchanged
 official_codex_signature_receipt_present=$official;status_ok=$status.ok;process_write_bytes=$processWriteBytes
 last_content_write_bytes=$metrics.total_content_write_bytes;content_measurement_elapsed_seconds=$metrics.elapsed_seconds
 content_measurement_note='程式診斷為最近完整輪詢時點，當前回執寫入不計；程序總IO計數另含pipe等寫入，不等於SSD實體寫入。'
 termination=$termination;model_calls=0;private_runtime_excluded_from_zip=$true
}
$receipt|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $audit 'final-exe-live-audit.json') -Encoding utf8
Write-Output ([pscustomobject]@{result=$receipt.result;audit=$audit;elapsed_seconds=$elapsed;new_usage_rows=$newRows.Count;process_write_bytes=$processWriteBytes;last_content_write_bytes=$metrics.total_content_write_bytes}|ConvertTo-Json -Compress)
if(!$passed){throw '最終EXE隔離實測未通過，請查看回執。'}
