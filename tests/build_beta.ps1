#Requires -Version 7
[CmdletBinding()]
param([switch]$ValidateOnly,[switch]$SkipTests)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$projectRoot=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
function Get-CodeInputs {
 @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src'),(Join-Path $projectRoot 'tests') -Recurse -File | Where-Object {$_.Extension -in @('.cs','.csproj','.resx') -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]'} | Sort-Object FullName | ForEach-Object {[ordered]@{path=[IO.Path]::GetRelativePath($projectRoot,$_.FullName);sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
}
function Assert-CodeInputs {
 $current=Get-CodeInputs|ConvertTo-Json -Compress
 if($current -cne $script:codeInputsJson){throw '建置期間產品或測試原始碼有變，停止產生交付包；請固定原始碼後重新建置。'}
}
$sdk=Join-Path $projectRoot '.tools\dotnet10\dotnet.exe'
if(!(Test-Path -LiteralPath $sdk)){$sdk=(Get-Command dotnet -CommandType Application|Select-Object -First 1).Source}
$env:DOTNET_CLI_HOME=Join-Path $projectRoot '.cache\dotnet-home'
$env:NUGET_PACKAGES=Join-Path $projectRoot '.cache\nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:DOTNET_NOLOGO='1'
[xml]$project=Get-Content (Join-Path $projectRoot 'src\CodexUsageMonitor.csproj') -Raw
$version=[string]$project.Project.PropertyGroup.Version
$sdkVersion=(& $sdk --version | Out-String).Trim()
if($sdkVersion -notmatch '^10\.') {throw '需要 .NET 10 SDK。'}
if($ValidateOnly){[pscustomobject]@{state='建置前檢查通過';version=$version;sdk=$sdkVersion;build_run=$false}|ConvertTo-Json;return}
$script:codeInputsJson=Get-CodeInputs|ConvertTo-Json -Compress
$run=Join-Path $projectRoot ('artifacts\beta-0.5.0\'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8))
$portable=Join-Path $run 'portable'
New-Item -ItemType Directory -Force -Path $portable | Out-Null
[IO.File]::WriteAllText((Join-Path $run 'build-source-inputs.json'),$script:codeInputsJson,[Text.UTF8Encoding]::new($false))
Push-Location $projectRoot
try{
 foreach($file in @('src\CodexUsageMonitor.csproj','tests\CodexUsageMonitor.Tests.csproj')){
  & $sdk restore $file --configfile NuGet.Config --nologo
  if($LASTEXITCODE -ne 0){throw '套件還原失敗。'}
 }
 if(!$SkipTests){
  $runner=Join-Path $run 'test-runner'
  & $sdk publish tests\CodexUsageMonitor.Tests.csproj -c Release --no-restore --nologo -o $runner
  if($LASTEXITCODE -ne 0){throw '獨立測試專案建置失敗。'}
  foreach($flag in @('analytics-050','radar-050','integration-050','first-paint-diagnostics','public','self','gold-cycle','final-phase','rc4-coverage','rc4-field-trial','rc4-signal-polish','rc4-active-unread','rc4-history-event')){
   $output=Join-Path $run ('tests\'+$flag)
   $p=Start-Process -FilePath (Join-Path $runner 'CodexUsageMonitor.Tests.exe') -ArgumentList @('--'+$flag+'-test','--test-root',('"'+$output+'"')) -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru -Wait
   if($p.ExitCode -ne 0){throw "測試未通過：$flag（$output）"}
  }
  & (Join-Path $PSScriptRoot 'SummarizeBetaTests050.ps1') -RunRoot $run
 }
 Assert-CodeInputs
 & $sdk publish src\CodexUsageMonitor.csproj -c Release --no-restore --nologo -o $portable
 if($LASTEXITCODE -ne 0){throw '正式程式發布失敗。'}
 Assert-CodeInputs
 $exe=Join-Path $portable 'CodexUsageMonitor.exe'
 foreach($pdb in Get-ChildItem $portable -Filter '*.pdb' -File){Remove-Item -LiteralPath $pdb.FullName}
 Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses') -Destination (Join-Path $portable 'licenses') -Recurse
 Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\PORTABLE_README.txt') -Destination (Join-Path $portable 'README.txt')
 foreach($name in @('LICENSE','THIRD_PARTY_NOTICES.md')){Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $portable}
 $exeHash=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
 $manifest=[ordered]@{version=$version;sdk=$sdkVersion;exe_sha256=$exeHash;publication='本機試用包，尚未發布到網路';tests=if($SkipTests){'略過'}else{'自動測試結果另見實際通過及略過數'};native_visual_acceptance='另見驗收報告';signature='本工具未簽章；官方 codex.exe 另驗證 Authenticode'}
 if(!$SkipTests){$summary=@(Get-Content -LiteralPath (Join-Path $run 'test-summary.json') -Raw|ConvertFrom-Json);$manifest['test_checks']=($summary|Measure-Object checks -Sum).Sum;$manifest['test_passed']=($summary|Measure-Object passed -Sum).Sum;$manifest['test_skipped']=($summary|Measure-Object skipped -Sum).Sum;$manifest['test_failed']=($summary|Measure-Object failed -Sum).Sum}
 $manifest|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $portable 'manifest.json') -Encoding utf8
 $zip=Join-Path $run "CodexUsageMonitor-v$version-win-x64.zip"
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 [IO.Compression.ZipFile]::CreateFromDirectory($portable,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
 $zipHash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
 @("$exeHash  CodexUsageMonitor.exe","$zipHash  $([IO.Path]::GetFileName($zip))") | Set-Content -LiteralPath (Join-Path $run 'SHA256SUMS.txt') -Encoding utf8
 $manifest['zip_sha256']=$zipHash;$manifest['exe_bytes']=(Get-Item $exe).Length;$manifest['zip_bytes']=(Get-Item $zip).Length
 $manifest|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $run 'build-result.json') -Encoding utf8
 $report=@(('docs/releases/v'+$version+'.md'),('docs/驗收報告-'+$version+'.md'),'docs/驗收報告-0.5.0-beta.md') | ForEach-Object {Join-Path $projectRoot $_} | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
 if(!$report){throw '找不到此次版本的發行說明或驗收報告。'}
 & (Join-Path $PSScriptRoot 'AuditRelease050.ps1') -RunRoot $run -ReportPath $report -FinalPackage -AllowReportPending | Out-Null
 Write-Output '發行審查已保存為本次建置的 release-audit.json；驗收報告完成後需再次核對其雜湊。'
 Write-Output "試用包已建立：$zip"
}finally{Pop-Location}
