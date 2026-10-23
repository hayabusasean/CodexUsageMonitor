[CmdletBinding()]
param([string]$PrivateHistoryPath = 'C:\Users\user\AppData\Local\CodexUsageMonitor\history',[string]$BaselineManifest='PUBLIC_SOURCE_MANIFEST.json',[string]$Version='')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$baselinePath = if([IO.Path]::IsPathFullyQualified($BaselineManifest)){$BaselineManifest}else{Join-Path $projectRoot $BaselineManifest}
if(!$Version){[xml]$project=Get-Content -LiteralPath (Join-Path $projectRoot 'src\CodexUsageMonitor.csproj') -Raw;$Version=[string]$project.Project.PropertyGroup.Version}
$outputName='SOURCE_MANIFEST-'+$Version+'.json'
$catalogName='docs\變更檔案-'+$Version+'.md'
$outputPath = Join-Path $projectRoot $outputName
$catalogPath = Join-Path $projectRoot $catalogName
$utf8 = [Text.UTF8Encoding]::new($false)
$baseline = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
$baselineFiles = @{}
foreach ($entry in $baseline.files) { $baselineFiles[$entry.path] = $entry }
$excludedComponents = '(^|/)(bin|obj|\.tools|\.cache|artifacts|\.git|\.agents|\.codex|\.aws|private|private-runtime|private-data|history|logs|exports|neutral)(/|$)'
$excludedNames = '(^|/)(auth\.json|settings\.json|last_good[^/]*\.json|reset_radar\.json|notification_state\.json|event_notes\.json|running-instance\.json)$'
$rootFiles = @('.gitattributes','.gitignore','CHANGELOG.md','CONTRIBUTING.md','global.json','LICENSE','NuGet.Config','PRIVACY.md','PUBLIC_SOURCE_MANIFEST.json','README_BUILD.md','README.md','README.zh-TW.md','SECURITY.md','THIRD_PARTY_NOTICES.md')
function Get-SourceFiles {
    $candidates = @()
    foreach ($name in $rootFiles) { $path = Join-Path $projectRoot $name; if (Test-Path -LiteralPath $path -PathType Leaf) { $candidates += Get-Item -LiteralPath $path } }
    foreach ($name in @('.github','src','tests','docs','licenses')) { $path = Join-Path $projectRoot $name; if (Test-Path -LiteralPath $path -PathType Container) { $candidates += Get-ChildItem -LiteralPath $path -Recurse -File } }
    return @($candidates | Where-Object {
        $relative = [IO.Path]::GetRelativePath($projectRoot, $_.FullName).Replace('\','/')
        $relative -notmatch $excludedComponents -and $relative -notmatch $excludedNames -and $_.Extension -notin @('.exe','.dll','.pdb','.zip','.bak','.bad','.partial','.dmp','.mdmp')
    } | Sort-Object FullName -Unique)
}
function Get-RelativePath($file) { return [IO.Path]::GetRelativePath($projectRoot, $file.FullName).Replace('\','/') }
$roles = @{
    '.github/workflows/build.yml'='Windows CI 建置、獨立測試與本機試用包輸出。'
    'NuGet.Config'='固定套件來源，供 SDK 還原使用。'
    'README.md'='繁中操作、公式、退版還原、隔離資料目錄與診斷說明。'
    'README.zh-TW.md'='繁中使用入口與新版重點。'
    'README_BUILD.md'='新版建置、測試專案與去識別回放資料製作方式。'
    'CHANGELOG.md'='新版改動及舊版沿革。'
    'src/AppServerClient.cs'='官方來源搜尋、唯讀 RPC、能力錯誤分類與狀態型 Log。'
    'src/BurnForecast.cs'='跳格燒速、未開始週期與每週／公告預測。'
    'src/CodexTrust.cs'='Authenticode、官方發行者與執行檔 SHA-256 驗證。'
    'src/CodexUsageMonitor.csproj'='版本、single-file 壓縮、JPEG 資源與正式程式排除測試。'
    'src/DailyUsageCalculator.cs'='重置容忍值、可比較身分與每日觀察用量。'
    'src/Forms.cs'='設定與額度詳情首次 Paint 診斷，隔離資料目錄停用開機捷徑設定。'
    'src/GoldVisual.cs'='共用畫面文字與新版顯示資訊。'
    'src/HistoryForm.cs'='歷史畫面及新版測試對接。'
    'src/L.cs'='新版版本字串與繁中事件名稱。'
    'src/LocalQuotaCycle.cs'='提前／缺口重置、週期起點與衍生狀態去雜訊。'
    'src/Models.cs'='身分分級、前後帳戶辨識與新事件判定；CSV 欄位維持原樣。'
    'src/MonitorService.cs'='輪詢、七日快取、預測、狀態檔與提醒整合。'
    'src/OverlayForm.cs'='浮窗版面、通知區叫回、剩餘圖示與通知佇列。'
    'src/Program.cs'='繁中啟動、實際資料目錄單實例鎖與開機捷徑隔離防護。'
    'src/QuotaAdvice.cs'='五項建議與六小時通知節流。'
    'src/RadarCoverage.cs'='被封鎖來源與可讀率分母計算。'
    'src/RadarJournal.cs'='來源檢查與覆蓋率的每日 JSONL 追加紀錄。'
    'src/RadarRules.cs'='可設定的 Radar 詞庫與規則驗證。'
    'src/Rc4Radar.cs'='RSS category、同作者回覆脈絡與 Tibo 訊號分類。'
    'src/ReportExporter.cs'='新版診斷與分析報告整合。'
    'src/ResetRadar.cs'='PT 時間、加速抓取、來源封鎖與帳戶關聯。'
    'src/RuntimeIdentity.cs'='以實際資料目錄正規化建置身分、單實例與重複啟動提示。'
    'src/RuntimeMetrics.cs'='資料寫入量與執行時驗證指標。'
    'src/RuntimeSessionMetrics.cs'='正式 EXE 隔離資料目錄與不含帳號資料的寫入診斷。'
    'src/FirstPaintDiagnostics.cs'='每個視窗第一次背景 Paint 前後的兩行每日診斷。'
    'src/StatusWriter.cs'='不含帳戶識別資料的 status.v1.json 輸出。'
    'src/StellarSurface.cs'='尺寸快取背景與捲動整區重畫。'
    'src/Storage.cs'='診斷檔納入既有保留天數與容量清理；CSV 契約不變。'
    'src/Theme.cs'='首次合成完成再顯示及背景繪圖設定。'
    'src/TrendView.cs'='歷史用量圖表、可比較採樣分段與跨連線橋接判定。'
    'tests/Analytics050Tests.cs'='真實去識別 CSV 回放、重置與燒速驗收。'
    'tests/EmailScopedTests.cs'='EMAIL_SCOPED 分級、跨連線分析與唯讀舊備份相容 fixture。'
    'tests/FirstPaintDiagnosticsTests.cs'='首次 Paint 尺寸、DPI、計數、兩行限制及診斷失敗隔離測試。'
    'tests/RunFinalExeAuditBeta2.ps1'='同一份出貨 EXE 的十二分鐘隔離唯讀實測與寫入證據。'
    'tests/AuditRelease050.ps1'='只讀拆查正式 bundle、ZIP、雜湊與測試入口排除。'
    'tests/app.manifest'='獨立 UI 測試的 DPI 與 Windows 宣告。'
    'tests/build_beta.ps1'='獨立測試、正式發行包與 SHA-256 清單建置。'
    'tests/build_public.ps1'='原建置入口接至新版流程。'
    'tests/CodexUsageMonitor.Tests.csproj'='獨立測試執行檔、fixture 與資源。'
    'tests/CreateReplayFixtures.ps1'='只讀製作去識別 CSV，確認來源製作前後未變。'
    'tests/LiveAudit050Tests.cs'='隔離資料目錄的實際唯讀監測與寫入量測試。'
    'tests/RadarForecast050Tests.cs'='公告時間、有效訊號與預測整合測試。'
    'tests/TestHarness.cs'='原有歷史／唯讀測試與新版語意期待。'
    'tests/V050IntegrationTests.cs'='狀態、通知、DPAPI、來源驗證與寫入整合測試。'
    'tests/V050UiTests.cs'='新版浮窗、叫回、DPI 與開窗重繪測試。'
    'tests/V050ScreenRecordTests.cs'='受控背景的桌面連續擷取；前景與遮擋防護未過即中止。'
    'tests/encode_ui_recordings.py'='依實際影格時間編成 GIF 並保留來源雜湊；不負責桌面擷取。'
    'tests/SummarizeBetaTests050.ps1'='彙整十三組實際結果並拒絕失敗或不明格式。'
    'tests/WriteSourceManifest050.ps1'='產生本變更目錄、來源雜湊與私人識別值檢查。'
    'docs/變更檔案-0.5.0-beta.md'='依舊版來源雜湊逐檔列出本次改動。'
}
function Get-Role([string]$path) {
    if ($roles.ContainsKey($path)) { return $roles[$path] }
    if ($path -like 'src/Assets/*.jpg') { return '縮小並壓縮的 JPEG 背景，供正式執行檔內嵌。' }
    if ($path -like 'tests/fixtures/usage-replay/usage_*.csv') { return '真實用量的去識別回放副本；帳戶、比較與來源鍵已換假值。' }
    if ($path -eq 'tests/fixtures/usage-replay/fixture-manifest.json') { return '去識別資料列數與來源未變的製作紀錄。' }
    if ($path -like 'tests/fixtures/*live*') { return '公開 relay 回應 fixture；用於重現解析，不是私人執行時資料。' }
    if ($path -eq 'tests/fixtures/radar-a1.json') { return '審查報告 A1 八則 Tibo 訊息的固定分類與時間案例。' }
    if ($path -match '(^src/.*Tests\.cs$|^tests/.*\.cs$)') { return '對應模組的隔離測試或新版語意驗收期待。' }
    if ($path -like 'docs/*') { return '專案說明或驗收文件。' }
    return '專案來源、組態、授權或測試支援檔。'
}
if (!(Test-Path -LiteralPath $catalogPath)) { [IO.File]::WriteAllText($catalogPath, '', $utf8) }
$initialFiles = @(Get-SourceFiles)
$changes = @()
$present = @{}
foreach ($file in $initialFiles) {
    $relative = Get-RelativePath $file; $present[$relative] = $true
    if ($relative -eq 'PUBLIC_SOURCE_MANIFEST.json') { continue }
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if (!$baselineFiles.ContainsKey($relative)) { $changes += [pscustomobject]@{ status='新增'; path=$relative; role=(Get-Role $relative) } }
    elseif ($baselineFiles[$relative].sha256 -ne $hash) { $changes += [pscustomobject]@{ status='修改'; path=$relative; role=(Get-Role $relative) } }
}
$removed = @($baseline.files | Where-Object { !$present.ContainsKey($_.path) })
$modified = @($changes | Where-Object status -eq '修改')
$added = @($changes | Where-Object status -eq '新增')
$lines = @(('# '+$Version+' 變更檔案'), '',
    ('比對基準為 `{0}` 的 {1} `files[].sha256`。本目錄列出內容有變與新增的來源檔，不改寫舊版清單。' -f $BaselineManifest,$baseline.version), '',
    ('本次共有 **{0} 個修改、{1} 個新增、{2} 個移除**。逐檔位元組數、SHA-256 與舊版 SHA-256 另見根目錄 `{3}`。' -f $modified.Count,$added.Count,$removed.Count,$outputName), '',
    '排除建置輸出、SDK、快取、artifacts、私人執行時資料，以及 auth、settings、歷史目錄與 Log。`PUBLIC_SOURCE_MANIFEST.json` 保留為更早公開來源參照，不列入新增差異；本次比對基準以上述清單為準。新版清單排除自身，避免循環雜湊。', '',
    'ACCOUNT_STABLE 與 EMAIL_SCOPED 是依來源識別資訊的分析分級；實際串接以當次來源資料為準。CSV fixture 的 account_context_key、comparison_key 與 source_generation 已換成假值，原始CSV與舊schema不回寫。', '',
    '## 修改檔案', '', '| 檔案 | 角色與內容 |', '| --- | --- |')
foreach ($entry in $modified | Sort-Object path) { $lines += '| `{0}` | {1} |' -f $entry.path,$entry.role }
$lines += @('', '## 新增檔案', '', '| 檔案 | 角色與內容 |', '| --- | --- |')
foreach ($entry in $added | Sort-Object path) { $lines += '| `{0}` | {1} |' -f $entry.path,$entry.role }
if ($removed.Count -gt 0) { $lines += @('', '## 移除檔案', ''); foreach ($entry in $removed) { $lines += '- `{0}`' -f $entry.path } }
$lines += @('', ('若最後整理又變更來源或文件，請在專案根目錄執行 `./tests/WriteSourceManifest050.ps1 -BaselineManifest {0}`，重新產生此目錄與新版 SHA-256 清單。隱私檢查只輸出是否命中與列數，不輸出真實帳戶鍵。' -f $BaselineManifest), '')
[IO.File]::WriteAllLines($catalogPath, $lines, $utf8)
$sourceFiles = @(Get-SourceFiles)
$privateIdentifiers = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$checkedHistoryFiles = 0
if (Test-Path -LiteralPath $PrivateHistoryPath -PathType Container) {
    foreach ($file in Get-ChildItem -LiteralPath $PrivateHistoryPath -Filter 'usage_*.csv' -File) {
        foreach ($row in Import-Csv -LiteralPath $file.FullName) {
            foreach ($field in @('account_context_key','comparison_key')) { $value = [string]$row.$field; if ($value.Length -ge 8 -and $value -notmatch '^fixture-') { [void]$privateIdentifiers.Add($value) } }
        }
        $checkedHistoryFiles++
    }
}
$privateMatches = @()
foreach ($file in $sourceFiles | Where-Object { $_.Extension -in @('.cs','.csproj','.json','.csv','.md','.xml','.html','.yml','.yaml','.resx','.ps1','.py','.txt','.config','.manifest') }) {
    $text = [IO.File]::ReadAllText($file.FullName)
    foreach ($identifier in $privateIdentifiers) { if ($text.Contains($identifier,[StringComparison]::Ordinal)) { $privateMatches += Get-RelativePath $file; break } }
}
if ($privateMatches.Count -gt 0) { throw ('來源內仍有真實私人識別值，未產生清單：' + (($privateMatches | Sort-Object -Unique) -join ', ')) }
$fixtureRows = 0
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'tests\fixtures\usage-replay') -Filter 'usage_*.csv' -File) {
    foreach ($row in Import-Csv -LiteralPath $file.FullName) {
        if ($row.account_context_key -notmatch '^fixture-account-\d+$' -or $row.comparison_key -notmatch '^fixture-comparison-\d+$' -or $row.source_generation -notmatch '^fixture-generation-\d+$') { throw '回放 fixture 仍有未替換的識別欄位，未產生清單。' }
        $fixtureRows++
    }
}
$entries = @()
foreach ($file in $sourceFiles) {
    $relative = Get-RelativePath $file
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $oldHash = if ($baselineFiles.ContainsKey($relative)) { [string]$baselineFiles[$relative].sha256 } else { $null }
    $change = if ($relative -eq 'PUBLIC_SOURCE_MANIFEST.json') { 'BASELINE_REFERENCE' } elseif ($null -eq $oldHash) { 'NEW' } elseif ($oldHash -eq $hash) { 'UNCHANGED' } else { 'MODIFIED' }
    $entries += [ordered]@{ path=$relative; bytes=$file.Length; sha256=$hash; baseline_sha256=$oldHash; change=$change; role_zh_tw=(Get-Role $relative) }
}
$manifest = [ordered]@{
    schema_version=1; kind='LOCAL_BETA_SOURCE_MANIFEST'; version=$Version; publication='本機試用來源，未發布到網路'; generated_utc=[DateTimeOffset]::UtcNow.ToString('O');
    baseline_manifest=$BaselineManifest; baseline_version=$baseline.version; baseline_manifest_sha256=(Get-FileHash -LiteralPath $baselinePath -Algorithm SHA256).Hash.ToLowerInvariant();
    entry_count=$entries.Count; modified_count=@($entries | Where-Object change -eq 'MODIFIED').Count; added_count=@($entries | Where-Object change -eq 'NEW').Count; removed_paths=@($removed | ForEach-Object path);
    privacy_check=[ordered]@{ checked_live_history_files=$checkedHistoryFiles; real_identifier_matches=0; anonymized_fixture_rows=$fixtureRows; fixture_fields_replaced=@('account_context_key','comparison_key','source_generation'); private_identifier_values_omitted=$true };
    exclusions=@($outputName,'bin','obj','.tools','.cache','artifacts','.git','.agents','.codex','.aws','private runtime','auth/settings/history/logs/exports/neutral','executables/archives/debug symbols/backups');
    files=$entries
}
[IO.File]::WriteAllText($outputPath, ($manifest | ConvertTo-Json -Depth 7) + [Environment]::NewLine, $utf8)
Write-Output ('來源清單完成：{0} 檔；修改 {1}，新增 {2}；私人識別值命中 0；去識別 fixture {3} 列。' -f $entries.Count,$manifest.modified_count,$manifest.added_count,$fixtureRows)
