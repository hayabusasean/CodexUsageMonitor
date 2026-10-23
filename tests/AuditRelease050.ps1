[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RunRoot,
    [string]$ReportPath,
    [string]$HostModelAssemblyPath,
    [switch]$AllowReportPending,
    [switch]$FinalPackage
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$projectRoot=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$RunRoot=[IO.Path]::GetFullPath($RunRoot)
if(!$ReportPath){
    $publicReport=Join-Path $projectRoot 'docs\releases\v0.5.0-beta2.md'
    $ReportPath=if(Test-Path -LiteralPath $publicReport){$publicReport}else{Join-Path $projectRoot 'docs\驗收報告-0.5.0-beta.md'}
}
if(!$HostModelAssemblyPath){
    $bundledHostModel=Join-Path $projectRoot '.tools\dotnet10\sdk\10.0.400\Microsoft.NET.HostModel.dll'
    if(Test-Path -LiteralPath $bundledHostModel){$HostModelAssemblyPath=$bundledHostModel}
    else{
        $installedDotnet=(Get-Command dotnet -CommandType Application|Select-Object -First 1).Source
        $selectedSdk=(& $installedDotnet --version|Out-String).Trim()
        $sdkLine=@(& $installedDotnet --list-sdks)|Where-Object {$_.StartsWith($selectedSdk+' [',[StringComparison]::Ordinal)}|Select-Object -First 1
        if(!$sdkLine){throw '找不到目前選用的 .NET SDK。'}
        $sdkBase=$sdkLine.Substring($sdkLine.IndexOf('[')+1).TrimEnd(']')
        $HostModelAssemblyPath=Join-Path (Join-Path $sdkBase $selectedSdk) 'Microsoft.NET.HostModel.dll'
    }
}
$metadata=Get-Content -LiteralPath (Join-Path $RunRoot 'build-result.json') -Raw|ConvertFrom-Json
$zipPath=Join-Path $RunRoot ('CodexUsageMonitor-v{0}-win-x64.zip' -f $metadata.version)
$exePath=Join-Path $RunRoot 'portable\CodexUsageMonitor.exe'
$exeHash=(Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
$zipHash=(Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksums=Get-Content -LiteralPath (Join-Path $RunRoot 'SHA256SUMS.txt') -Raw
$report=Get-Content -LiteralPath $ReportPath -Raw
$reportMatches=$report.Contains($exeHash) -and $report.Contains($zipHash)
if($exeHash -ne $metadata.exe_sha256 -or $zipHash -ne $metadata.zip_sha256 -or !$checksums.Contains($exeHash) -or !$checksums.Contains($zipHash) -or (!$reportMatches -and !$AllowReportPending)){throw '執行檔、ZIP、建置清單或驗收報告的 SHA-256 不一致。'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($zipPath)
try{
    $entryNames=@($zip.Entries|ForEach-Object FullName)
    $allowedEntries=@('CodexUsageMonitor.exe','LICENSE','manifest.json','README.txt','THIRD_PARTY_NOTICES.md','licenses/DOTNET-LICENSE.txt','licenses/DOTNET-THIRD-PARTY-NOTICES.txt','licenses/MICROSOFT-THIRD-PARTY-NOTICES.txt','licenses/WINDOWSDESKTOP-LICENSE.txt')
    $unexpected=@($entryNames|Where-Object {$_ -notin $allowedEntries})
    $missingOrRepeated=@($allowedEntries|Where-Object {$expected=$_;@($entryNames|Where-Object {$_ -ceq $expected}).Count -ne 1})
    if($unexpected.Count -gt 0 -or $missingOrRepeated.Count -gt 0 -or $entryNames.Count -ne $allowedEntries.Count){throw ('ZIP 有非預期、重複項目或缺檔：'+(($unexpected+$missingOrRepeated) -join ', '))}
    $zipPayloadHashes=@()
    foreach($entry in $zip.Entries){
        $entryStream=$entry.Open();try{$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($entryStream)).ToLowerInvariant()}finally{$entryStream.Dispose()}
        $portableFile=Join-Path (Join-Path $RunRoot 'portable') $entry.FullName
        if(!(Test-Path -LiteralPath $portableFile -PathType Leaf) -or $hash -ne (Get-FileHash -LiteralPath $portableFile -Algorithm SHA256).Hash.ToLowerInvariant()){throw ('ZIP 內容與 portable 目錄不一致：'+$entry.FullName)}
        $zipPayloadHashes += [ordered]@{path=$entry.FullName;sha256=$hash}
    }
    $stream=($zip.Entries|Where-Object FullName -eq 'CodexUsageMonitor.exe').Open()
    try{$insideHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()}finally{$stream.Dispose()}
    if($insideHash -ne $exeHash){throw 'ZIP 內的執行檔與外部執行檔不一致。'}
    $reader=[IO.StreamReader]::new(($zip.Entries|Where-Object FullName -eq 'manifest.json').Open())
    try{$insideManifest=$reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}
    if($insideManifest.exe_sha256 -ne $exeHash -or $insideManifest.version -ne $metadata.version){throw 'ZIP 內的 manifest.json 與建置結果不一致。'}
}finally{$zip.Dispose()}

# 使用同版官方 SDK 定位 bundle header；只讀取壓縮內容，不執行發行程式。
$hostModel=[Reflection.Assembly]::LoadFrom([string]$HostModelAssemblyPath)
$bundleArgs=[object[]]@([string]$exePath,[long]0)
$isBundle=$hostModel.GetType('Microsoft.NET.HostModel.Bundle.Bundler').GetMethod('IsBundle').Invoke($null,$bundleArgs)
if(!$isBundle){throw '執行檔不是預期的 .NET single-file bundle。'}
$file=[IO.File]::OpenRead($exePath)
$binary=[IO.BinaryReader]::new($file)
try{
    $file.Position=[long]$bundleArgs[1]
    $major=$binary.ReadUInt32();$minor=$binary.ReadUInt32();$count=$binary.ReadInt32();$bundleId=$binary.ReadString()
    if($major -ne 6 -or $minor -ne 0 -or $count -lt 1 -or $count -gt 65535){throw 'bundle 版本或項目數不在此審查器支援範圍。'}
    [void]$binary.ReadInt64();[void]$binary.ReadInt64();[void]$binary.ReadInt64();[void]$binary.ReadInt64();[void]$binary.ReadUInt64()
    $bundleEntries=@()
    for($i=0;$i -lt $count;$i++){
        $offset=$binary.ReadInt64();$size=$binary.ReadInt64();$compressed=$binary.ReadInt64();$type=$binary.ReadByte();$relative=$binary.ReadString()
        $bundleEntries += [pscustomobject]@{path=$relative;offset=$offset;size=$size;compressed=$compressed;type=$type}
    }
    $managed=@($bundleEntries|Where-Object path -eq 'CodexUsageMonitor.dll')
    if($managed.Count -ne 1){throw '找不到唯一的產品 managed assembly。'}
    $managed=$managed[0]
    if($managed.size -le 0 -or $managed.size -gt 64MB -or $managed.compressed -lt 0 -or $managed.offset -lt 0){throw '產品 assembly 大小或位置不合理。'}
    $readSize=if($managed.compressed -gt 0){$managed.compressed}else{$managed.size}
    if($managed.offset+$readSize -gt $file.Length){throw '產品 assembly 超出 bundle 檔案範圍。'}
    $file.Position=$managed.offset;$payload=[byte[]]::new([int]$readSize);$file.ReadExactly($payload)
    if($managed.compressed -gt 0){
        $inputStream=[IO.MemoryStream]::new($payload)
        $inflate=[IO.Compression.DeflateStream]::new($inputStream,[IO.Compression.CompressionMode]::Decompress)
        $target=[IO.MemoryStream]::new([int]$managed.size)
        try{
            $buffer=[byte[]]::new(81920)
            while(($received=$inflate.Read($buffer,0,$buffer.Length)) -gt 0){if($target.Length+$received -gt $managed.size){throw '解壓內容超過 bundle 宣告大小。'};$target.Write($buffer,0,$received)}
            $managedBytes=$target.ToArray()
        }finally{$inflate.Dispose();$target.Dispose()}
    }else{$managedBytes=$payload}
    if($managedBytes.Length -ne $managed.size){throw '解壓後產品 assembly 大小不符。'}
}finally{$binary.Dispose()}
$managedHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($managedBytes)).ToLowerInvariant()
$objPath=Join-Path $projectRoot 'src\obj\Release\net10.0-windows\win-x64\CodexUsageMonitor.dll'
$matchesObj=if(Test-Path -LiteralPath $objPath){$managedHash -eq (Get-FileHash -LiteralPath $objPath -Algorithm SHA256).Hash.ToLowerInvariant()}else{$null}
$ascii=[Text.Encoding]::ASCII.GetString($managedBytes)
$even=[Text.Encoding]::Unicode.GetString($managedBytes)
$odd=[Text.Encoding]::Unicode.GetString($managedBytes,1,$managedBytes.Length-1)
$program=Get-Content -LiteralPath (Join-Path $projectRoot 'src\Program.cs') -Raw
$expectedRemovedFlags=@([regex]::Matches($program,'--[a-z0-9-]+')|ForEach-Object Value|Sort-Object -Unique)
$flagMatches=@($expectedRemovedFlags|Where-Object {$ascii.Contains($_) -or $even.Contains($_) -or $odd.Contains($_)})
# 以最終專案的 MSBuild Compile 清單界定正式來源，再解析其餘測試來源的實際型別。
# 不把「測試檔案數」誤稱為「型別數」，也不維護固定的旗標或型別數字。
$sdk=Join-Path $projectRoot '.tools\dotnet10\dotnet.exe'
if(!(Test-Path -LiteralPath $sdk)){$sdk=(Get-Command dotnet -CommandType Application|Select-Object -First 1).Source}
$env:DOTNET_CLI_HOME=Join-Path $projectRoot '.cache\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1';$env:DOTNET_NOLOGO='1'
$projectPath=Join-Path $projectRoot 'src\CodexUsageMonitor.csproj'
$compileJson=(& $sdk msbuild $projectPath -nologo -getItem:Compile -p:Configuration=Release | Out-String)
if($LASTEXITCODE -ne 0){throw '無法取得最終專案的 MSBuild Compile 清單。'}
$compiled=($compileJson|ConvertFrom-Json).Items.Compile
$productionSources=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($entry in $compiled){[void]$productionSources.Add([IO.Path]::GetFullPath([string]$entry.FullPath))}
$testFiles=@(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src'),(Join-Path $projectRoot 'tests') -Recurse -Filter '*.cs' -File | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and !$productionSources.Contains($_.FullName)
} | Sort-Object FullName)
if($testFiles.Count -eq 0){throw '沒有找到與正式專案隔離的測試來源，無法驗證測試型別排除。'}
Add-Type -AssemblyName Microsoft.CodeAnalysis.CSharp
$parseOptions=[Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithPreprocessorSymbols([string[]]@('TEST_HARNESS'))
$declaredTypes=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
function Get-DeclaredTypeName($node){
    $name=$node.Identifier.ValueText
    if($node -is [Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax] -or $node -is [Microsoft.CodeAnalysis.CSharp.Syntax.DelegateDeclarationSyntax]){
        if($null -ne $node.TypeParameterList){$name+='`'+$node.TypeParameterList.Parameters.Count}
    }
    return $name
}
foreach($source in $testFiles){
    $tree=[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($source.FullName),$parseOptions,$source.FullName,[Text.Encoding]::UTF8,[Threading.CancellationToken]::None)
    $root=[Microsoft.CodeAnalysis.CSharp.CSharpExtensions]::GetCompilationUnitRoot($tree,[Threading.CancellationToken]::None)
    $syntaxErrors=@($tree.GetDiagnostics([Threading.CancellationToken]::None)|Where-Object Severity -eq 'Error')
    if($syntaxErrors.Count -gt 0){throw ('測試來源無法完整解析：'+$source.Name)}
    foreach($node in $root.DescendantNodes()|Where-Object {$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax] -or $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.DelegateDeclarationSyntax]}){
        $typeParts=[Collections.Generic.List[string]]::new();$typeParts.Add((Get-DeclaredTypeName $node))
        $namespaceParts=[Collections.Generic.List[string]]::new()
        foreach($ancestor in $node.Ancestors()){
            if($ancestor -is [Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax]){$typeParts.Insert(0,(Get-DeclaredTypeName $ancestor))}
            elseif($ancestor -is [Microsoft.CodeAnalysis.CSharp.Syntax.BaseNamespaceDeclarationSyntax]){$namespaceParts.Insert(0,$ancestor.Name.ToString())}
        }
        $fullName=($typeParts -join '+');if($namespaceParts.Count -gt 0){$fullName=($namespaceParts -join '.')+'.'+$fullName}
        [void]$declaredTypes.Add($fullName)
    }
}
$testTypes=@($declaredTypes|Sort-Object)
Add-Type -AssemblyName System.Reflection.Metadata
$managedStream=[IO.MemoryStream]::new($managedBytes)
$peReader=[System.Reflection.PortableExecutable.PEReader]::new($managedStream)
try{
    $reader=[System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($peReader)
    function Get-MetadataTypeName($handle){
        $definition=$reader.GetTypeDefinition($handle);$name=$reader.GetString($definition.Name);$parent=$definition.GetDeclaringType()
        if(!$parent.IsNil){return (Get-MetadataTypeName $parent)+'+'+$name}
        $namespace=$reader.GetString($definition.Namespace);if($namespace){return $namespace+'.'+$name};return $name
    }
    $productTypes=@($reader.TypeDefinitions|ForEach-Object {Get-MetadataTypeName $_})
}finally{$peReader.Dispose();$managedStream.Dispose()}
$typeMatches=@($testTypes|Where-Object {$_ -cin $productTypes})
if($flagMatches.Count -gt 0 -or $typeMatches.Count -gt 0){throw ('正式 assembly 仍有測試旗標或型別：'+(($flagMatches+$typeMatches) -join ', '))}

$client=Get-Content -LiteralPath (Join-Path $projectRoot 'src\AppServerClient.cs') -Raw
$allowed=[regex]::Match($client,'internal static bool IsAllowed\(string method\)=>(.*?);',[Text.RegularExpressions.RegexOptions]::Singleline)
$rpcMethods=@([regex]::Matches($allowed.Groups[1].Value,'"([^"]+)"')|ForEach-Object {$_.Groups[1].Value}|Sort-Object -Unique)
$expectedRpc=@('account/rateLimits/read','account/read','account/usage/read','initialize')|Sort-Object
if(($rpcMethods -join '|') -ne ($expectedRpc -join '|')){throw 'RPC 白名單不是工單要求的四個唯讀 request。'}
[xml]$sourceProject=Get-Content -LiteralPath $projectPath -Raw
$sourceVersion=[string]$sourceProject.Project.PropertyGroup.Version
if($FinalPackage -and ($sourceVersion -ne $metadata.version -or $matchesObj -ne $true)){throw '最終包必須符合目前專案版本，且 bundle 內的產品 assembly 必須與目前 Release obj 完全相同。'}
$sourceInventory=@($testFiles|ForEach-Object {[ordered]@{path=[IO.Path]::GetRelativePath($projectRoot,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
$result=[ordered]@{
    classification='READ_ONLY_RELEASE_AUDIT';audit_schema_version=2;artifact_role=if($FinalPackage){'FINAL_RELEASE_CANDIDATE'}else{'SPECIFIED_BUILD_PACKAGE'};audited_at_utc=[DateTimeOffset]::UtcNow.ToString('O');result=if($reportMatches){'PASS'}else{'PASS_REPORT_PENDING'};report_hashes_match=$reportMatches;version=$metadata.version;exe_sha256=$exeHash;zip_sha256=$zipHash;exe_path=$exePath;zip_path=$zipPath;zip_entry_count=$entryNames.Count;zip_entries=$entryNames;zip_payload_hashes=$zipPayloadHashes;
    managed_sha256=$managedHash;managed_payload_bytes=$managedBytes.Length;managed_matches_current_obj=$matchesObj;bundle_entry_count=$bundleEntries.Count;
    test_flag_literals_checked=$expectedRemovedFlags.Count;test_flag_literals=$expectedRemovedFlags;test_source_files_checked=$testFiles.Count;test_type_names_checked=$testTypes.Count;test_type_names=$testTypes;test_flags_found=$flagMatches;test_types_found=$typeMatches;
    checked_source_version=$sourceVersion;program_source_sha256=(Get-FileHash -LiteralPath (Join-Path $projectRoot 'src\Program.cs') -Algorithm SHA256).Hash.ToLowerInvariant();project_source_sha256=(Get-FileHash -LiteralPath $projectPath -Algorithm SHA256).Hash.ToLowerInvariant();audit_script_sha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant();test_source_inventory=$sourceInventory;
    source_rpc_allowlist=$rpcMethods;protocol_notification='initialized 是既有初始化完成通知，不是模型回合';executed_release_exe=$false;reran_tests=$false;
    limits='此審查核對包內容、雜湊與靜態安全邊界；不取代動態 UI、通知權限、一小時寫入與 Human 使用驗收。'
}
$json=$result|ConvertTo-Json -Depth 7
$auditPath=Join-Path $RunRoot 'release-audit.json'
$tempPath=Join-Path $RunRoot ('release-audit.'+[Guid]::NewGuid().ToString('N')+'.tmp')
try{
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes($json+[Environment]::NewLine)
    $output=[IO.FileStream]::new($tempPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$output.Write($bytes,0,$bytes.Length);$output.Flush($true)}finally{$output.Dispose()}
    [IO.File]::Move($tempPath,$auditPath,$true)
}finally{if([IO.File]::Exists($tempPath)){[IO.File]::Delete($tempPath)}}
$json
