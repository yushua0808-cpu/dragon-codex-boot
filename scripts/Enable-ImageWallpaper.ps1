[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$Image,
  [string]$AppRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')),
  [string]$RuntimeSource,
  [ValidateRange(0,100)][int]$RevealPercent = 75
)
$ErrorActionPreference = 'Stop'
$upstream = 'https://github.com/CCDawn/Codex-Dynamic-Skin.git'
$commit = '5a0e74043b63ed9518ab37cdcf6865340a57006c'
$app = [IO.Path]::GetFullPath($AppRoot)
$imagePath = [IO.Path]::GetFullPath($Image)
$configPath = Join-Path $app 'launcher.json'
if (!(Test-Path -LiteralPath $imagePath -PathType Leaf)) { throw "Image not found: $imagePath" }
if (!(Test-Path -LiteralPath $configPath -PathType Leaf)) { throw "Launcher config not found: $configPath" }
$current = Join-Path $app 'skin-runtime'
$stage = Join-Path $app ('.skin-stage-' + [guid]::NewGuid().ToString('N'))
$backup = Join-Path $app ('.skin-backup-' + [guid]::NewGuid().ToString('N'))
$clone = Join-Path $app ('.skin-source-' + [guid]::NewGuid().ToString('N'))
$owned = @($stage,$backup,$clone)
foreach ($path in $owned) {
  $resolved = [IO.Path]::GetFullPath($path)
  if (![string]::Equals([IO.Path]::GetDirectoryName($resolved),$app,[StringComparison]::OrdinalIgnoreCase)) {
    throw "Temporary path escaped application folder: $resolved"
  }
}
$hadExisting = Test-Path -LiteralPath $current -PathType Container
$movedExisting = $false
try {
  $source = $RuntimeSource
  if (!$source) {
    & git init -q $clone
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize pinned upstream checkout.' }
    & git -C $clone remote add origin $upstream
    if ($LASTEXITCODE -ne 0) { throw 'Could not set upstream remote.' }
    & git -C $clone fetch -q --depth 1 origin $commit
    if ($LASTEXITCODE -ne 0) { throw 'Could not fetch pinned upstream commit.' }
    & git -C $clone checkout -q --detach FETCH_HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Could not check out pinned upstream commit.' }
    $source = $clone
  }
  $source = [IO.Path]::GetFullPath($source)
  $actual = (& git -C $source rev-parse HEAD 2>$null).Trim()
  if ($actual -ne $commit) { throw "Upstream commit mismatch: $actual" }
  $windows = Join-Path $source 'windows'
  foreach ($relative in @('VERSION','scripts/start-dream-skin.ps1','scripts/manager-command.ps1','assets/renderer-inject.js','assets/selectors.json')) {
    if (!(Test-Path -LiteralPath (Join-Path $windows $relative) -PathType Leaf)) { throw "Upstream runtime missing: $relative" }
  }
  New-Item -ItemType Directory -Path $stage -Force | Out-Null
  foreach ($folder in @('assets','scripts','presets')) {
    Copy-Item -LiteralPath (Join-Path $windows $folder) -Destination $stage -Recurse -Force
  }
  # Windows Store file virtualization can report a media file beneath
  # Packages/.../LocalCache while its parent theme directory retains the
  # ordinary LocalAppData path. Accept that alias only when both directory
  # paths identify the same filesystem directory and the media is a sibling.
  $injectorPath = Join-Path $stage 'scripts/injector.mjs'
  $injectorSource = [IO.File]::ReadAllText($injectorPath)
  $originalGuard = 'if (!wallpaperEngineReference && !isPathInside(realThemeDir, realMediaPath)) {'
  if ($injectorSource.Split([string[]]@($originalGuard),[StringSplitOptions]::None).Count -ne 2) {
    throw 'Pinned upstream media guard changed; review the Windows virtualization fix.'
  }
  $patchedGuard = @'
if (!wallpaperEngineReference && !isPathInside(realThemeDir, realMediaPath)) {
    const sourceParent = path.dirname(sourceMediaPath);
    const mediaParent = path.dirname(realMediaPath);
    const sourceStat = await fs.stat(sourceParent);
    const mediaStat = await fs.stat(mediaParent);
    const sameVirtualizedDirectory = process.platform === "win32" &&
      path.basename(sourceMediaPath).toLowerCase() === path.basename(realMediaPath).toLowerCase() &&
      sourceParent.toLowerCase() === realThemeDir.toLowerCase() &&
      sourceStat.dev === mediaStat.dev && sourceStat.ino === mediaStat.ino;
    if (!sameVirtualizedDirectory) {
'@
  $injectorSource = $injectorSource.Replace($originalGuard,$patchedGuard)
  $newline = if ($injectorSource.Contains("`r`n")) { "`r`n" } else { "`n" }
  $closingGuard = 'throw new Error("Theme media cannot escape through a link or junction");' + $newline + '  }'
  if ($injectorSource.Split([string[]]@($closingGuard),[StringSplitOptions]::None).Count -ne 2) {
    throw 'Pinned upstream media guard ending changed; review the Windows virtualization fix.'
  }
  $injectorSource = $injectorSource.Replace($closingGuard,'throw new Error("Theme media cannot escape through a link or junction");' + $newline + '    }' + $newline + '  }')
  [IO.File]::WriteAllText($injectorPath,$injectorSource,[Text.UTF8Encoding]::new($false))
  # Keep the launch theme beside this runtime. A Store process can virtualize
  # LOCALAPPDATA independently of the launcher, so its active-theme path is
  # not a reliable source of truth across process boundaries.
  $pinnedTheme = Join-Path $stage 'dragon-wallpaper'
  New-Item -ItemType Directory -Path $pinnedTheme -Force | Out-Null
  $extension = [IO.Path]::GetExtension($imagePath).ToLowerInvariant()
  if ($extension -notin @('.png','.jpg','.jpeg','.webp')) { throw "Unsupported wallpaper image: $extension" }
  $pinnedImageName = 'wallpaper' + $extension
  Copy-Item -LiteralPath $imagePath -Destination (Join-Path $pinnedTheme $pinnedImageName) -Force
  $pinnedConfig = [ordered]@{
    schemaVersion = 1; id = 'custom'; appearance = 'auto'
    art = [ordered]@{ focusX = $null; focusY = $null; safeArea = 'auto'; taskMode = 'auto' }
    image = $pinnedImageName
    media = [ordered]@{ type = 'image'; playbackRate = 1.0; opacity = $RevealPercent / 100.0 }
    name = 'dragon-codex-wallpaper'
  }
  [IO.File]::WriteAllText((Join-Path $pinnedTheme 'theme.json'),($pinnedConfig | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
  $startPath = Join-Path $stage 'scripts/start-dream-skin.ps1'
  $startSource = [IO.File]::ReadAllText($startPath)
  $initAnchor = '  $themePaths = Initialize-DreamSkinThemeStore -SkillRoot (Split-Path -Parent $PSScriptRoot) -StateRoot $StateRoot'
  if ($startSource.Split([string[]]@($initAnchor),[StringSplitOptions]::None).Count -ne 2) {
    throw 'Pinned upstream theme initialization changed; review the launch theme fix.'
  }
  $startSource = $startSource.Replace($initAnchor,$initAnchor + $newline +
    '  $pinnedThemeDir = Join-Path (Split-Path -Parent $PSScriptRoot) ''dragon-wallpaper''')
  $replacements = @(
    @("'--theme-dir', `$themePaths.Active","'--theme-dir', `$pinnedThemeDir",4),
    @('--theme-dir $themePaths.Active','--theme-dir $pinnedThemeDir',1),
    @('(ConvertTo-DreamSkinProcessArgument -Value $themePaths.Active)','(ConvertTo-DreamSkinProcessArgument -Value $pinnedThemeDir)',1),
    @('themeDir = $themePaths.Active','themeDir = $pinnedThemeDir',1)
  )
  foreach ($replacement in $replacements) {
    $old = [string]$replacement[0]; $new = [string]$replacement[1]; $expected = [int]$replacement[2]
    if ($startSource.Split([string[]]@($old),[StringSplitOptions]::None).Count -ne ($expected + 1)) {
      throw "Pinned upstream launch reference changed: $old"
    }
    $startSource = $startSource.Replace($old,$new)
  }
  # The launcher invokes Windows PowerShell 5.1, which reads a BOM-less .ps1
  # using the local ANSI code page. Preserve the upstream script's Chinese
  # strings by writing a UTF-8 BOM, then parse it with that exact runtime.
  [IO.File]::WriteAllText($startPath,$startSource,[Text.UTF8Encoding]::new($true))
  $windowsPowerShell = Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
  $quotedStartPath = "'" + $startPath.Replace("'","''") + "'"
  $parseCommand = '$tokens=$null;$errors=$null;[System.Management.Automation.Language.Parser]::ParseFile(' +
    $quotedStartPath + ',[ref]$tokens,[ref]$errors)|Out-Null;if($errors.Count){$errors|ForEach-Object{$_.Message};exit 1}'
  & $windowsPowerShell -NoProfile -Command $parseCommand | Out-Null
  if ($LASTEXITCODE -ne 0) { throw 'Windows PowerShell 5.1 could not parse the startup script.' }
  Copy-Item -LiteralPath (Join-Path $windows 'VERSION') -Destination $stage
  Copy-Item -LiteralPath (Join-Path $source 'LICENSE') -Destination (Join-Path $stage 'UPSTREAM-LICENSE')
  [IO.File]::WriteAllText((Join-Path $stage 'UPSTREAM.txt'),"$upstream`r`n$commit`r`n",[Text.UTF8Encoding]::new($false))
  & (Join-Path $stage 'scripts/manager-command.ps1') -Action SetWallpaper -Path $imagePath | Out-Null
  & (Join-Path $stage 'scripts/manager-command.ps1') -Action SetReveal -Percent $RevealPercent | Out-Null
  $payloadCheck = & node $injectorPath --check-payload --theme-dir $pinnedTheme | ConvertFrom-Json
  if ($LASTEXITCODE -ne 0 -or !$payloadCheck.pass -or $payloadCheck.themeId -ne 'custom' -or
      [int64]$payloadCheck.media.size -ne (Get-Item -LiteralPath $imagePath).Length) {
    throw 'Pinned wallpaper payload did not validate.'
  }
  if ($hadExisting) { Move-Item -LiteralPath $current -Destination $backup; $movedExisting = $true }
  Move-Item -LiteralPath $stage -Destination $current
  $config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
  $config | Add-Member -NotePropertyName UseImageWallpaper -NotePropertyValue $true -Force
  $configTemp = Join-Path $app ('.launcher-' + [guid]::NewGuid().ToString('N') + '.json')
  try {
    [IO.File]::WriteAllText($configTemp,(($config | ConvertTo-Json -Depth 16) + "`r`n"),[Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $configTemp -Destination $configPath -Force
  } finally { if (Test-Path -LiteralPath $configTemp) { Remove-Item -LiteralPath $configTemp -Force } }
  if ($movedExisting) { Remove-Item -LiteralPath $backup -Recurse -Force; $movedExisting = $false }
  Write-Output "Image wallpaper prepared: $imagePath (reveal $RevealPercent%). Close Codex, then launch through DragonCodexBoot.exe."
} catch {
  if ($movedExisting) {
    if (Test-Path -LiteralPath $current) { Remove-Item -LiteralPath $current -Recurse -Force }
    Move-Item -LiteralPath $backup -Destination $current
  }
  throw
} finally {
  foreach ($path in @($stage,$clone)) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
  }
}
