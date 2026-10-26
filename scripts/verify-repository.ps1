$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$files=@(& git -c core.quotePath=false -C $repoRoot ls-files)
if ($LASTEXITCODE -ne 0 -or $files.Count -eq 0) { throw 'No tracked repository files. Initialize and stage the clean source first.' }
foreach ($name in $files) {
    if ($name -eq 'media/startup.mp4' -or $name -eq 'animation/assets/lu-mingfei-cyber.png' -or
        $name -eq 'animation/assets/lu-mingfei-workspace-bg.png') {
        if ((Get-Item -LiteralPath (Join-Path $repoRoot $name)).Length -ge 100MB) { throw ('Approved media exceeds the regular Git file limit: '+$name) }
        continue
    }
    if ($name -match '(^|/)(build|dist|\.local|logs|qa|\.integration)/' -or $name -match '\.(mp4|mov|webm|ico|lnk|exe|dll|pdb|zip|pem|key)$') {
        throw ('Private/generated asset tracked: '+$name)
    }
    $text=Get-Content -LiteralPath (Join-Path $repoRoot $name) -Encoding UTF8 -Raw
    if ($text -match '[CE]:[/\\]Users[/\\]' -or $text -match '[EF]:[/\\]' -or
        $text -match 'gh[pousr]_[A-Za-z0-9]{30,}' -or $text -match 'github_pat_[A-Za-z0-9_]{50,}' -or
        $text -match '-----BEGIN (RSA |OPENSSH |EC )?PRIVATE KEY-----') { throw ('Private path or credential marker found: '+$name) }
}
$config=Get-Content -LiteralPath (Join-Path $repoRoot 'config/launcher.example.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($config.Video -ne 'media/startup.mp4') { throw 'Public configuration must use its relative media path.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zips=@(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'dist') -Filter '*-win-x64*.zip' | Sort-Object LastWriteTime -Descending)
if (!$zips) { throw 'Create a program package first.' }
$zip=$zips[0]
$archive=[IO.Compression.ZipFile]::OpenRead($zip.FullName)
try {
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName.Contains('\')) { throw ('Non-portable ZIP entry name: '+$entry.FullName) }
        if (($entry.FullName -ne 'media/startup.mp4' -and $entry.FullName -match '\.(mp4|mov|webm|ico|lnk)$') -or $entry.FullName -match '(^|/)(logs|qa|\.integration)/') { throw ('Private asset packaged: '+$entry.FullName) }
        if ($entry.Length -eq 0) { continue }
        $stream=$entry.Open()
        try { $null=Get-FileHash -InputStream $stream -Algorithm SHA256 } finally { $stream.Dispose() }
    }
    $cfgEntry=$archive.GetEntry('launcher.json')
    if (!$cfgEntry) { throw 'Packaged config missing.' }
    $reader=New-Object IO.StreamReader($cfgEntry.Open())
    try { $packedConfig=$reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    if ($packedConfig.Video -ne 'media/startup.mp4') { throw 'Private config packaged.' }
    if ($packedConfig.AutoReplaceEntrypoints -ne $true -or $packedConfig.ScanAllLocalDrives -ne $true) { throw 'First-run integration defaults missing.' }
    foreach ($name in @('scripts/integrate-entrypoints.ps1','Restore-Original-Entrypoints.cmd','Rescan-Entrypoints.cmd','docs/ENTRYPOINTS.md','licenses/APACHE-2.0-HyperFrames.txt')) {
        if (!$archive.GetEntry($name)) { throw ('Entrypoint feature file missing: '+$name) }
    }
    $exeEntry=$archive.GetEntry('DragonCodexBoot.exe')
    if (!$exeEntry) { throw 'Packaged executable missing.' }
    $stream=$exeEntry.Open()
    try { $exeHash=(Get-FileHash -InputStream $stream).Hash } finally { $stream.Dispose() }
    if ($exeHash -ne (Get-FileHash -LiteralPath (Join-Path $repoRoot 'build/DragonCodexBoot/DragonCodexBoot.exe')).Hash) { throw 'Packaged binary hash mismatch.' }
    if ($zip.Name -notmatch '-no-media') {
        $videoEntry=$archive.GetEntry('media/startup.mp4')
        if (!$videoEntry -or !$archive.GetEntry('media/MEDIA_NOTICE.md')) { throw 'Bundled demo video or notice missing.' }
        $stream=$videoEntry.Open()
        try { $videoHash=(Get-FileHash -InputStream $stream).Hash } finally { $stream.Dispose() }
        if ($videoHash -ne (Get-FileHash -LiteralPath (Join-Path $repoRoot 'media/startup.mp4')).Hash) { throw 'Packaged video hash mismatch.' }
    }
} finally { $archive.Dispose() }
$expected=(Get-Content -LiteralPath ($zip.FullName+'.sha256') -Raw).Trim().Split(' ')[0]
if ($expected -ine (Get-FileHash -LiteralPath $zip.FullName).Hash) { throw 'Archive hash mismatch.' }
Write-Output ('PASS: '+$files.Count+' tracked files; only the approved demo video and character background included; private paths and credential markers excluded; package entries and hashes checked.')
