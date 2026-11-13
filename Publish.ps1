param([switch]$FrameworkDependent)
$ErrorActionPreference = 'Stop'
$localDotnet = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
& $dotnetCommand publish (Join-Path $PSScriptRoot 'src\PocketBridge\PocketBridge.csproj') -c Release -r win-x64 --self-contained $selfContained -o (Join-Path $PSScriptRoot 'artifacts\PocketBridge') -p:DebugType=None -p:DebugSymbols=false
exit $LASTEXITCODE
