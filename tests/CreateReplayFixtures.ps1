param(
    [string]$SourceHistory = 'C:\Users\user\AppData\Local\CodexUsageMonitor\history',
    [string]$Destination = (Join-Path $PSScriptRoot 'fixtures\usage-replay')
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$accountMap = @{}
$comparisonMap = @{}
$generationMap = @{}
$manifest = @()
foreach ($day in 17..22) {
    $name = 'usage_2026-10-{0:00}.csv' -f $day
    $source = Join-Path $SourceHistory $name
    $sourceHashBefore = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    $rows = @(Import-Csv -LiteralPath $source)
    foreach ($row in $rows) {
        if (-not $accountMap.ContainsKey($row.account_context_key)) { $accountMap[$row.account_context_key] = 'fixture-account-{0}' -f ($accountMap.Count + 1) }
        if (-not $comparisonMap.ContainsKey($row.comparison_key)) { $comparisonMap[$row.comparison_key] = 'fixture-comparison-{0}' -f ($comparisonMap.Count + 1) }
        if (-not $generationMap.ContainsKey($row.source_generation)) { $generationMap[$row.source_generation] = 'fixture-generation-{0}' -f ($generationMap.Count + 1) }
        $row.account_context_key = $accountMap[$row.account_context_key]
        $row.comparison_key = $comparisonMap[$row.comparison_key]
        $row.source_generation = $generationMap[$row.source_generation]
    }
    $rows | Export-Csv -LiteralPath (Join-Path $Destination $name) -NoTypeInformation -Encoding utf8
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $sourceHashBefore) { throw '來源檔案在回放資料製作期間有變動，請重新取得一致快照。' }
    $manifest += [pscustomobject]@{ file = $name; rows = $rows.Count; source_unchanged = $true; identity_fields_replaced = @('account_context_key', 'comparison_key', 'source_generation') }
}
$manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $Destination 'fixture-manifest.json') -Encoding utf8
Write-Output ('已建立去識別回放資料，共 {0} 筆；來源檔案未修改。' -f (($manifest | Measure-Object -Property rows -Sum).Sum))
