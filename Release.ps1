param()
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$projectPath = Join-Path $repoRoot 'src\PocketBridge\PocketBridge.csproj'
[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid release version.' }
$releaseName = "PocketBridge-$version-win-x64"
$releaseDirectory = Join-Path $repoRoot 'artifacts\releases'
$stagingRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\release-staging'))
$workDirectory = Join-Path $stagingRoot ([Guid]::NewGuid().ToString('N'))
$packageDirectory = Join-Path $workDirectory $releaseName
$null = New-Item -ItemType Directory -Force -Path $releaseDirectory, $packageDirectory
$localDotnet = Join-Path $repoRoot '.tools\dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$utf8 = New-Object Text.UTF8Encoding($false)

function Write-Utf8([string]$Path, [string]$Content) { [IO.File]::WriteAllText($Path, $Content, $utf8) }
function Copy-PackageNotices([string]$PackageId, [string]$PackageVersion, [string]$Label) {
    $source = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder ($PackageId.ToLowerInvariant() + '\' + $PackageVersion)
        if (Test-Path -LiteralPath $candidate) { $source = $candidate; break }
    }
    if (-not $source) { throw "Missing restored package: $PackageId $PackageVersion" }
    $notices = @(Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Name -match '^(LICENSE|THIRD.PARTY.NOTICES)(\.[A-Za-z]+)?$' })
    if (-not ($notices | Where-Object { $_.Name -match '^LICENSE' })) { throw "Missing license: $PackageId" }
    foreach ($file in $notices) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $licenseDirectory ($Label + '-' + $file.Name))
    }
}

try {
    & $dotnetCommand publish $projectPath -c Release -r win-x64 --self-contained true -o $packageDirectory -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Release publish failed.' }
    $exe = Join-Path $packageDirectory 'PocketBridge.exe'
    if ((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion -ne $version) { throw 'Executable version does not match project version.' }
    $deps = Get-Content -LiteralPath (Join-Path $packageDirectory 'PocketBridge.deps.json') -Raw | ConvertFrom-Json
    $assets = Get-Content -LiteralPath (Join-Path $repoRoot 'src\PocketBridge\obj\project.assets.json') -Raw | ConvertFrom-Json
    $runtimeLines = New-Object 'Collections.Generic.List[string]'
    $licenseDirectory = Join-Path $packageDirectory 'Lizenzen'
    $null = New-Item -ItemType Directory -Path $licenseDirectory
    foreach ($library in $deps.libraries.PSObject.Properties.Name) {
        if ($library -match '^runtimepack\.(.+)/([^/]+)$') {
            $packageId = $Matches[1]; $packageVersion = $Matches[2]
            $label = $packageId + '-' + $packageVersion
            Copy-PackageNotices $packageId $packageVersion $label
            $runtimeLines.Add("$packageId $packageVersion")
            if ($packageId -eq 'Microsoft.WindowsDesktop.App.Runtime.win-x64') {
                $destination = Join-Path $licenseDirectory "$label-THIRD-PARTY-NOTICES.TXT"
                if (-not (Test-Path -LiteralPath $destination)) {
                    Invoke-WebRequest "https://raw.githubusercontent.com/dotnet/wpf/v$packageVersion/THIRD-PARTY-NOTICES.TXT" -UseBasicParsing -OutFile $destination
                }
            }
        } elseif ($library -match '^QRCoder/([^/]+)$') {
            Copy-PackageNotices 'QRCoder' $Matches[1] ('QRCoder-' + $Matches[1])
            $runtimeLines.Add("QRCoder $($Matches[1])")
        }
    }
    if ($runtimeLines.Count -ne 4) { throw 'Unexpected dependency set. Review third-party notices before packaging.' }
    $readme = [IO.File]::ReadAllText((Join-Path $repoRoot 'packaging\LIESMICH.txt')).Replace('{{VERSION}}', $version)
    Write-Utf8 (Join-Path $packageDirectory 'LIESMICH.txt') $readme
    Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging\VERSIONSHINWEISE.md') -Destination $packageDirectory
    $buildTime = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
    $runtimeText = $runtimeLines -join [Environment]::NewLine
    Write-Utf8 (Join-Path $packageDirectory 'VERSION.txt') "PocketBridge $version`r`nPlatform: Windows 11 x64`r`nMode: self-contained`r`nBuilt (UTC): $buildTime`r`n`r`nIncluded dependencies:`r`n$runtimeText`r`n"
    Write-Utf8 (Join-Path $packageDirectory 'THIRD-PARTY-NOTICES.txt') @"
PocketBridge $version includes QRCoder and the .NET, Windows Desktop (WPF),
and ASP.NET Core runtime components listed in VERSION.txt.

Their original license texts and notices are supplied in the Lizenzen folder.
These third-party license texts apply to their respective components; they
do not assign a license to PocketBridge itself.

Sources:
https://github.com/codebude/QRCoder
https://github.com/dotnet/runtime
https://github.com/dotnet/wpf
https://github.com/dotnet/aspnetcore
"@
    $files = @(Get-ChildItem -LiteralPath $packageDirectory -Recurse -File | Sort-Object FullName)
    if ($files | Where-Object { $_.Name -in @('settings.json','pairing.key') -or $_.Extension -in @('.data','.part','.pdb') }) {
        throw 'A personal data or debug file was found in the release staging folder.'
    }
    $manifest = foreach ($file in $files) {
        $relative = $file.FullName.Substring($packageDirectory.Length + 1).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        "$hash  $relative"
    }
    Write-Utf8 (Join-Path $packageDirectory 'SHA256SUMS.txt') (($manifest -join "`n") + "`n")
    $archivePath = Join-Path $releaseDirectory "$releaseName.zip"
    $temporaryZip = Join-Path $workDirectory "$releaseName.zip"
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    # Explicit forward slashes also produce standard ZIP paths under Windows PowerShell 5.
    $zipWriter = [IO.Compression.ZipFile]::Open($temporaryZip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in (Get-ChildItem -LiteralPath $packageDirectory -Recurse -File | Sort-Object FullName)) {
            $entryName = $releaseName + '/' + $file.FullName.Substring($packageDirectory.Length + 1).Replace('\', '/')
            $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zipWriter, $file.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $zipWriter.Dispose() }

    # Verify every archived byte stream against the staged source before publishing it.
    $archive = [IO.Compression.ZipFile]::OpenRead($temporaryZip)
    try {
        $entries = @($archive.Entries | Where-Object { $_.Name })
        if ($entries.Count -ne $files.Count + 1) { throw 'Archive file count mismatch.' }
        foreach ($entry in $entries) {
            $relative = $entry.FullName.Substring($releaseName.Length + 1)
            if ($relative -match '(^|/)\.\.(/|$)|^[/\\]') { throw 'Unsafe archive entry.' }
            $sourcePath = Join-Path $packageDirectory $relative.Replace('/', '\')
            $stream = $entry.Open()
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $entryHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
            finally { $sha.Dispose(); $stream.Dispose() }
            if ($entryHash -ne (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash) { throw "Archive verification failed: $relative" }
        }
    } finally { $archive.Dispose() }
    Move-Item -LiteralPath $temporaryZip -Destination $archivePath -Force
    $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    Write-Utf8 ($archivePath + '.sha256') "$archiveHash  $releaseName.zip`n"
    [pscustomobject]@{
        Archive = $archivePath
        SHA256 = $archiveHash
        Files = $files.Count + 1
        SizeMiB = [Math]::Round((Get-Item -LiteralPath $archivePath).Length / 1MB, 2)
        Verified = $true
    } | ConvertTo-Json
} finally {
    $resolvedWork = [IO.Path]::GetFullPath($workDirectory)
    $expectedPrefix = $stagingRoot.TrimEnd('\') + '\'
    if ($resolvedWork.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedWork)) {
        Remove-Item -LiteralPath $resolvedWork -Recurse -Force
    }
}
