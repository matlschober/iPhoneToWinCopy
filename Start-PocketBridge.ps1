$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$standalone = Join-Path $projectRoot 'artifacts\PocketBridge\PocketBridge.exe'
if (Test-Path -LiteralPath $standalone) {
    Start-Process -FilePath $standalone -WorkingDirectory (Split-Path $standalone)
    exit
}
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
& $dotnetCommand run --project (Join-Path $projectRoot 'src\PocketBridge\PocketBridge.csproj') --configuration Release
exit $LASTEXITCODE
