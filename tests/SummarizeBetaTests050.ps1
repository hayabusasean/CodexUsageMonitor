[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RunRoot)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$projectRoot=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$RunRoot=[IO.Path]::GetFullPath($RunRoot)
$files=[ordered]@{
 'analytics-050'='analytics-050-results.json'; 'radar-050'='radar-v050-tests.json'; 'integration-050'='integration-v050-tests.json'
 'first-paint-diagnostics'='first-paint-diagnostics-tests.json'
 'public'='public-tests.json'; 'self'='results.json'; 'gold-cycle'='gold-cycle-results.json'; 'final-phase'='final-phase-tests.json'
 'rc4-field-trial'='rc4-field-trial-tests.json'; 'rc4-coverage'='coverage-tests.json'; 'rc4-signal-polish'='signal-polish-tests.json'
 'rc4-active-unread'='active-unread-tests.json'; 'rc4-history-event'='history-event-tests.json'
}
$summary=@(foreach($suite in $files.Keys){
 $path=Join-Path $RunRoot ('tests\'+$suite+'\'+$files[$suite])
 $parsed=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
 $items=@(if($parsed -is [array]){$parsed}elseif($parsed.PSObject.Properties.Name -contains 'checks'){$parsed.checks}elseif($parsed.PSObject.Properties.Name -contains 'rows'){$parsed.rows}else{$parsed.results})
 if($items.Count -eq 0){throw "測試結果沒有檢查項目：$suite"}
 $failed=@(foreach($item in $items){
  if($item.PSObject.Properties.Name -contains 'pass'){if($item.pass -ne $true){$item}}
  elseif($item.PSObject.Properties.Name -contains 'result'){
   if($item.result -eq 'N/A'){
    if($item.PSObject.Properties.Name -notcontains 'reason' -or [string]::IsNullOrWhiteSpace([string]$item.reason)){throw "略過測試未附原因：$suite"}
   }elseif($item.result -ne 'PASS'){$item}
  }
  else{throw "測試結果格式無法辨識：$suite"}
 })
 if($failed.Count -gt 0){
  Write-Host ("FAILED_SUITE: "+$suite)
  foreach($failure in $failed){Write-Host (ConvertTo-Json -InputObject $failure -Compress -Depth 6)}
 }
 $skipped=@($items|Where-Object {$_.PSObject.Properties.Name -notcontains 'pass' -and $_.PSObject.Properties.Name -contains 'result' -and $_.result -eq 'N/A'})
 [pscustomobject]@{suite=$suite;checks=$items.Count;passed=$items.Count-$failed.Count-$skipped.Count;skipped=$skipped.Count;failed=$failed.Count;skipped_items=$skipped;file=[IO.Path]::GetRelativePath($projectRoot,$path)}
})
$summary|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $RunRoot 'test-summary.json') -Encoding utf8
if(($summary|Measure-Object failed -Sum).Sum -ne 0){throw '測試結果含失敗項目。'}
[pscustomobject]@{suites=$summary.Count;checks=($summary|Measure-Object checks -Sum).Sum;passed=($summary|Measure-Object passed -Sum).Sum;skipped=($summary|Measure-Object skipped -Sum).Sum;failed=0}|ConvertTo-Json -Compress
