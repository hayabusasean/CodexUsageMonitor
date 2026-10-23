# 保留既有建置入口；0.5.0 起改用獨立測試專案。
[CmdletBinding()]
param([switch]$ValidateOnly)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'build_beta.ps1') -ValidateOnly:$ValidateOnly
