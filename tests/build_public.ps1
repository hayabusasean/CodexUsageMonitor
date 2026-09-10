# CI/contributor entry point. End users open the portable executable.
[CmdletBinding()]
param([switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Windows is required.' }
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$projectFile = Join-Path $projectRoot 'src\CodexUsageMonitor.csproj'
[xml]$projectXml = Get-Content -LiteralPath $projectFile -Raw
$releaseVersion = [string]$projectXml.Project.PropertyGroup.Version
if ($releaseVersion -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Invalid version.' }
$dotnetPath = Join-Path $projectRoot '.tools\dotnet10\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) {
 $command = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
 if ($null -eq $command) { throw 'A .NET 10 SDK is required. No SDK was installed.' }
 $dotnetPath = $command.Source
}
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.cache\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.cache\nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$sdkVersion = (& $dotnetPath --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $sdkVersion -notmatch '^10\.') { throw 'A .NET 10 SDK is required.' }
$groups = @()
foreach ($neutral in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src\Resources') -Filter '*.resx' -File | Where-Object { $_.Name -notlike '*.zh-TW.resx' }) {
 [xml]$enXml = Get-Content -LiteralPath $neutral.FullName -Raw
 [xml]$zhXml = Get-Content -LiteralPath (Join-Path $neutral.DirectoryName ($neutral.BaseName + '.zh-TW.resx')) -Raw
 $en = @{}; $zh = @{}
 foreach ($d in $enXml.root.data) { if ($en.ContainsKey([string]$d.name)) { throw 'Duplicate key.' }; $en[[string]$d.name] = [string]$d.value }
 foreach ($d in $zhXml.root.data) { if ($zh.ContainsKey([string]$d.name)) { throw 'Duplicate key.' }; $zh[[string]$d.name] = [string]$d.value }
 if (@(Compare-Object @($en.Keys | Sort-Object) @($zh.Keys | Sort-Object)).Count -gt 0) { throw "Resource key mismatch: $($neutral.BaseName)" }
 foreach ($key in $en.Keys) {
  $pattern = '(?<!\{)\{(\d+)(?:[,}:])'
  $a = @([regex]::Matches($en[$key],$pattern) | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
  $b = @([regex]::Matches($zh[$key],$pattern) | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
  if (($a -join ',') -ne ($b -join ',')) { throw "Resource placeholder mismatch: $key" }
 }
 $groups += [pscustomobject]@{ group=$neutral.BaseName; key_count=$en.Count }
}
if ($groups.Count -eq 0) { throw 'No resources.' }
$licenseRoot = Join-Path $projectRoot 'licenses'
if (-not (Test-Path -LiteralPath $licenseRoot)) { $licenseRoot = Join-Path $projectRoot 'dist\licenses' }
$notices = @('DOTNET-LICENSE.txt','DOTNET-THIRD-PARTY-NOTICES.txt','WINDOWSDESKTOP-LICENSE.txt','MICROSOFT-THIRD-PARTY-NOTICES.txt')
foreach ($name in $notices) { if (-not (Test-Path -LiteralPath (Join-Path $licenseRoot $name))) { throw "Missing notice: $name" } }
if ($ValidateOnly) {
 [pscustomobject]@{ state='BUILD_PREFLIGHT_VERIFIED'; sdk=$sdkVersion; version=$releaseVersion; resources=$groups; build_run=$false; cloud_ci='NOT_RUN' } | ConvertTo-Json -Depth 4
 return
}
$runName = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$runRoot = Join-Path $projectRoot ('artifacts\public-build\' + $runName)
$publishRoot = Join-Path $runRoot 'publish'
$testRoot = Join-Path $runRoot 'synthetic-tests'
$packageRoot = Join-Path $runRoot 'package'
$portableRoot = Join-Path $runRoot 'portable'
foreach ($path in @($runRoot,$publishRoot,$testRoot,$packageRoot,$portableRoot)) { [IO.Directory]::CreateDirectory($path) | Out-Null }
# Outputs stay in new directories; no reset, delete, clean or dist replacement.
Push-Location -LiteralPath $projectRoot
try {
 $restoreArgs = @('restore',$projectFile,'--nologo')
 if (Test-Path -LiteralPath (Join-Path $projectRoot 'src\packages.lock.json')) { $restoreArgs += '--locked-mode' }
 & $dotnetPath @restoreArgs
 if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
 & $dotnetPath publish $projectFile -c Release --no-restore -o $publishRoot --nologo
 if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
 $builtExe = Join-Path $publishRoot 'CodexUsageMonitor.exe'
 if (-not (Test-Path -LiteralPath $builtExe)) { throw 'Published executable missing.' }
 $testProcess = Start-Process -FilePath $builtExe -ArgumentList @('--public-test','--test-root',('"' + $testRoot + '"')) -WorkingDirectory $runRoot -WindowStyle Hidden -PassThru -Wait
 if ($testProcess.ExitCode -ne 0) { throw 'Fixture tests failed.' }
 $fixRoot = Join-Path $testRoot 'radar-fix'
 $fixFixtures = Join-Path $projectRoot 'tests\fixtures\radar-fix-r001'
 if (Test-Path -LiteralPath $fixFixtures) {
  $fixProcess = Start-Process -FilePath $builtExe -ArgumentList @('--radar-fix-test','--test-root',('"'+$fixRoot+'"'),'--fixtures',('"'+$fixFixtures+'"')) -WorkingDirectory $runRoot -WindowStyle Hidden -PassThru -Wait
  if ($fixProcess.ExitCode -ne 0) { throw 'Production HTML Radar fixture tests failed.' }
  if (-not (Test-Path -LiteralPath (Join-Path $fixRoot 'radar-fix-tests.json'))) { throw 'Radar fixture result missing.' }
 }
 $phaseRoot = Join-Path $testRoot 'final-phase'
 $phaseProcess = Start-Process -FilePath $builtExe -ArgumentList @('--final-phase-test','--test-root',('"'+$phaseRoot+'"')) -WorkingDirectory $runRoot -WindowStyle Hidden -PassThru -Wait
 if ($phaseProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $phaseRoot 'final-phase-tests.json'))) { throw 'Final phase fixtures failed or missing.' }
 $resultFiles = @(Get-ChildItem -LiteralPath $testRoot -Recurse -File -Filter '*tests.json')
 if ($resultFiles.Count -eq 0) { throw 'No test-result file produced.' }
 foreach ($f in $resultFiles) {
  $text = Get-Content -LiteralPath $f.FullName -Raw
  $null = $text | ConvertFrom-Json
  if ($text -match '"(?:result|status)"\s*:\s*"FAIL"') { throw 'Fixture result contains failure.' }
 }
 Copy-Item -LiteralPath $builtExe -Destination (Join-Path $portableRoot 'CodexUsageMonitor.exe')
 $portableLicenses = Join-Path $portableRoot 'licenses'
 [IO.Directory]::CreateDirectory($portableLicenses) | Out-Null
 foreach ($name in $notices) { Copy-Item -LiteralPath (Join-Path $licenseRoot $name) -Destination (Join-Path $portableLicenses $name) }
 foreach ($name in @('THIRD_PARTY_NOTICES.md','LICENSE')) { Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination (Join-Path $portableLicenses $name) }
 $readme = @(
  "CodexUsageMonitor $releaseVersion - local Windows x64 candidate",'',
  'Open CodexUsageMonitor.exe. Compatible, signed-in official Codex is required.',
  'No separate .NET installation is needed. Native runtime files may extract to temporary storage.',
  'Language: Settings > Language / 語言 > English / 繁體中文.',
  'Data stays in %LOCALAPPDATA%\CodexUsageMonitor; copying the EXE does not sync history.',
  'Exit before replacing the app to update; retain LocalAppData.',
  'Unofficial community tool, not affiliated with or endorsed by OpenAI.',
  'Unsigned candidate. Checksums check integrity, not publisher trust.',
  'License: MIT. Copyright (c) 2026 hayabusasean. Repository: https://github.com/hayabusasean/CodexUsageMonitor',
  'Third-party notices are in licenses/.','',
  '繁中：解壓後開啟 EXE；需相容且已登入的官方 Codex。',
  '可在設定或右鍵選單切換語言。資料保留在本機，不作跨電腦同步。',
  '完整繁中說明位於原始碼準備目錄的 README.zh-TW.md。'
 ) -join [Environment]::NewLine
 [IO.File]::WriteAllText((Join-Path $portableRoot 'README.txt'),$readme,(New-Object Text.UTF8Encoding($false)))
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $zipPath = Join-Path $packageRoot ("CodexUsageMonitor-v$releaseVersion-win-x64.zip")
 [IO.Compression.ZipFile]::CreateFromDirectory($portableRoot,$zipPath,[IO.Compression.CompressionLevel]::Optimal,$false)
 $exeHash = (Get-FileHash -LiteralPath $builtExe -Algorithm SHA256).Hash.ToLowerInvariant()
 $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
 $sums = "$zipHash  $([IO.Path]::GetFileName($zipPath))" + [Environment]::NewLine + "$exeHash  CodexUsageMonitor.exe" + [Environment]::NewLine
 [IO.File]::WriteAllText((Join-Path $packageRoot 'SHA256SUMS.txt'),$sums,(New-Object Text.UTF8Encoding($false)))
 $runtimes = @(& $dotnetPath --list-runtimes | ForEach-Object { ($_ -split '\s+\[')[0] })
 [ordered]@{
  state='LOCAL_BUILD_AND_FIXTURE_VERIFIED'; publication='NOT_PUBLISHED'; version=$releaseVersion; sdk=$sdkVersion
  available_runtime_versions=$runtimes; exe_sha256=$exeHash; zip_sha256=$zipHash; resource_groups=$groups
  fixture_result_files=@($resultFiles | ForEach-Object { $_.Name }); real_account='NOT_RUN'; native_visual_acceptance='NOT_RUN'
  cloud_ci='NOT_RUN_UNLESS_ATTACHED_TO_AN_ACTUAL_WORKFLOW_RUN'
 } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $runRoot 'build-result.json') -Encoding UTF8
 Write-Output "Candidate built: $zipPath"
} finally { Pop-Location }
