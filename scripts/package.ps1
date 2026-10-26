[CmdletBinding()]
param([switch]$WithoutMedia)
$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$appRoot=Join-Path $repoRoot 'build/DragonCodexBoot'
$exe=Join-Path $appRoot 'DragonCodexBoot.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Build and test first.' }
$version=(Get-Content -LiteralPath (Join-Path $repoRoot 'VERSION') -Raw).Trim()
$dist=Join-Path $repoRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$stage=Join-Path $repoRoot ('.local/package-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage,(Join-Path $stage 'scripts'),(Join-Path $stage 'licenses') -Force | Out-Null
Copy-Item -LiteralPath $exe -Destination $stage
# Public builds always use the checked-in template; private paths never enter a default package.
Copy-Item -LiteralPath (Join-Path $repoRoot 'config/launcher.example.json') -Destination (Join-Path $stage 'launcher.json')
foreach ($name in @('LICENSE','README.md','CONTRIBUTING.md','THIRD_PARTY_NOTICES.md','VERSION','Restore-Original-Entrypoints.cmd','Rescan-Entrypoints.cmd')) { Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination $stage }
Copy-Item -LiteralPath (Join-Path $repoRoot 'animation/compositions/components/APACHE-2.0-LICENSE.txt') -Destination (Join-Path $stage 'licenses/APACHE-2.0-HyperFrames.txt')
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs') -Destination $stage -Recurse
foreach ($name in @('Import-Animation.ps1','install-start-menu.ps1','integrate-entrypoints.ps1','Enable-ImageWallpaper.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $stage 'scripts') }
New-Item -ItemType Directory -Path (Join-Path $stage 'media') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'media/MEDIA_NOTICE.md') -Destination (Join-Path $stage 'media/MEDIA_NOTICE.md')
if (!$WithoutMedia) {
    $media=Join-Path $repoRoot 'media/startup.mp4'
    if (!(Test-Path -LiteralPath $media)) { throw 'The bundled demo video is missing.' }
    New-Item -ItemType Directory -Path (Join-Path $stage 'media') -Force | Out-Null
    Copy-Item -LiteralPath $media -Destination (Join-Path $stage 'media/startup.mp4')
    if ((Get-FileHash -LiteralPath $media).Hash -ne (Get-FileHash -LiteralPath (Join-Path $stage 'media/startup.mp4')).Hash) { throw 'Media checksum mismatch.' }
}
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$flavor=if ($WithoutMedia) { '-no-media' } else { '-with-video' }
$zip=Join-Path $dist ('DragonCodexBoot-'+$version+'-win-x64'+$flavor+'.zip')
if (Test-Path -LiteralPath $zip) { $zip=Join-Path $dist ('DragonCodexBoot-'+$version+'-win-x64'+$flavor+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.zip') }
# .NET Framework's CreateFromDirectory can write backslash entry names on Windows.
# Use explicit POSIX entry paths so .NET Framework, newer .NET and other unzip tools agree.
$archive=[IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Create)
try {
    $prefix=[IO.Path]::GetFullPath($stage).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
    foreach ($file in Get-ChildItem -LiteralPath $stage -File -Recurse) {
        $entryName=$file.FullName.Substring($prefix.Length).Replace('\','/')
        $null=[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$entryName,[IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $archive.Dispose() }
$sha=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
($sha+'  '+[IO.Path]::GetFileName($zip)) | Set-Content -LiteralPath ($zip+'.sha256') -Encoding ASCII
Write-Output $zip
