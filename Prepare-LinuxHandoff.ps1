$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$outputDirectory = Join-Path $repoRoot 'artifacts\handoff'
$null = New-Item -ItemType Directory -Force -Path $outputDirectory
$archivePath = Join-Path $outputDirectory 'PocketBridge-linux-mint-source.zip'
$temporaryZip = Join-Path $outputDirectory ('handoff-' + [Guid]::NewGuid().ToString('N') + '.tmp.zip')
$skipDirectories = @('.git','.tools','artifacts','bin','obj','node_modules','.vs','__pycache__')
$skipFiles = @('pairing.key','settings.json','.env','.DS_Store')
function Get-ProjectFiles([string]$Directory) {
    foreach ($item in (Get-ChildItem -LiteralPath $Directory -Force)) {
        if ($item.PSIsContainer) {
            if ($item.Name -notin $skipDirectories) { Get-ProjectFiles $item.FullName }
        } elseif ($item.Name -notin $skipFiles -and $item.Extension -notin @('.pdb','.data','.part','.user','.zip')) {
            $item
        }
    }
}
$files = @(Get-ProjectFiles $repoRoot | Sort-Object FullName)
$prefix = 'PocketBridge-linux-mint-source/'
$utf8 = New-Object Text.UTF8Encoding($false)
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open($temporaryZip, [IO.Compression.ZipArchiveMode]::Create)
$hashes = New-Object 'Collections.Generic.List[string]'
try {
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($repoRoot.Length + 1).Replace('\','/')
        $entryName = $prefix + $relative
        $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal)
        $hashes.Add((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash + '  ' + $relative)
    }
    $entry = $zip.CreateEntry($prefix + 'HANDOFF-SHA256SUMS.txt')
    $writer = New-Object IO.StreamWriter($entry.Open(), $utf8)
    try { $writer.Write(($hashes -join "`n") + "`n") } finally { $writer.Dispose() }
} finally { $zip.Dispose() }
$verified = [IO.Compression.ZipFile]::OpenRead($temporaryZip)
try {
    if ($verified.Entries.Count -ne $files.Count + 1) { throw 'Handoff entry count mismatch.' }
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($repoRoot.Length + 1).Replace('\','/')
        $entry = $verified.GetEntry($prefix + $relative)
        if (-not $entry) { throw "Missing source file: $relative" }
        $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
        finally { $sha.Dispose(); $stream.Dispose() }
        if ($actual -ne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) { throw "Checksum mismatch: $relative" }
    }
} finally { $verified.Dispose() }
Move-Item -LiteralPath $temporaryZip -Destination $archivePath -Force
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
[IO.File]::WriteAllText($archivePath + '.sha256', "$hash  PocketBridge-linux-mint-source.zip`n", $utf8)
[pscustomobject]@{ Archive = $archivePath; Files = $files.Count; Bytes = (Get-Item -LiteralPath $archivePath).Length; SHA256 = $hash; Verified = $true } | ConvertTo-Json
