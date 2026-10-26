[CmdletBinding()]
param(
    [ValidateSet('Auto','Install','Restore','Verify')][string]$Action='Install',
    [string]$AppRoot,
    [string[]]$SearchRoots,
    [string[]]$OfficialExecutablePaths,
    [switch]$NoCreateDefaultLinks,
    [ValidateRange(1,1800)][int]$ScanBudgetSeconds=300
)
$ErrorActionPreference='Stop'
if (!$AppRoot) {
    $parent=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    if (Test-Path -LiteralPath (Join-Path $parent 'DragonCodexBoot.exe')) { $AppRoot=$parent }
    else { $AppRoot=Join-Path $parent 'build/DragonCodexBoot' }
}
$AppRoot=[IO.Path]::GetFullPath($AppRoot)
if ($AppRoot -ne [IO.Path]::GetPathRoot($AppRoot)) { $AppRoot=$AppRoot.TrimEnd('\','/') }
$exePath=Join-Path $AppRoot 'DragonCodexBoot.exe'
$stateRoot=Join-Path $AppRoot '.integration'
$statePath=Join-Path $stateRoot 'entrypoints-state.json'
$reportPath=Join-Path $stateRoot 'report.json'
$mutex=New-Object Threading.Mutex($false,'Local\DragonCodexBoot.EntryIntegration')
$locked=$false
$report=[ordered]@{
    Action=$Action;Started=(Get-Date).ToString('o');Finished=$null;Status='Running'
    Replaced=0;Created=0;Restored=0;AlreadyOwned=0;LinksInspected=0;FoldersInspected=0
    SkippedFolders=0;ScanTimedOut=$false;SearchRoots=@();Errors=@();Preserved=@()
    RegisteredApplicationChanged=$false;StartPinsAutomaticallyChanged=$false
    TaskbarCacheRefreshVerified=$false
    Limits=@('Only .lnk launch shortcuts for the configured installed client are retargeted.',
        'MSIX AppsFolder/Start app registrations, existing Start pins, direct EXE calls, protocols and web URLs are not intercepted.',
        'Shared shortcuts require write permission. Taskbar pins may need unpinning and repinning.',
        'Inaccessible, reparse, system/runtime/cache folders and the launcher folder are skipped; scanning has a time budget.')
}
function Hash([string]$Path) {
    # Use the framework already required by the launcher. This also works when
    # PowerShell module auto-loading is unavailable in the child process.
    $stream=[IO.File]::OpenRead($Path)
    $sha=[Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
    finally { $stream.Dispose();$sha.Dispose() }
}
function Write-Json([string]$Path,$Value) {
    $temp=$Path+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
    $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $temp -Encoding UTF8
    Move-Item -LiteralPath $temp -Destination $Path -Force
}
function Save-State { Write-Json $statePath $script:state }
function Path-Equals([string]$a,[string]$b) { [string]::Equals($a,$b,[StringComparison]::OrdinalIgnoreCase) }
function Get-Link([string]$Path) { $script:shell.CreateShortcut($Path) }
function Link-Is-Launcher([string]$Path) {
    $link=Get-Link $Path
    (Path-Equals $link.TargetPath $exePath) -and [string]::IsNullOrWhiteSpace($link.Arguments) -and (Path-Equals $link.WorkingDirectory $AppRoot)
}
function Get-AppID([string]$Path) {
    # UWP links can have an empty WScript TargetPath. Read their shell property instead of trusting their names.
    try {
        $folder=$script:desktopShell.NameSpace([IO.Path]::GetDirectoryName($Path))
        $item=$folder.ParseName([IO.Path]::GetFileName($Path))
        [string]$item.ExtendedProperty('System.AppUserModel.ID')
    } catch { '' }
}
function Is-Official([string]$Path,$link) {
    if ($script:officialTargets.Contains($link.TargetPath)) { return $true }
    $shellTarget='shell:AppsFolder\'+$script:appID
    if (Path-Equals $link.TargetPath $shellTarget) { return $true }
    $explorer=Join-Path $env:WINDIR 'explorer.exe'
    if ((Path-Equals $link.TargetPath $explorer) -and ($link.Arguments.Trim().Trim('"') -ieq $shellTarget)) { return $true }
    if ([string]::IsNullOrWhiteSpace($link.TargetPath) -and (Get-AppID $Path) -ieq $script:appID) { return $true }
    return $false
}
function Find-Record([string]$Path) {
    @($script:state.Entries | Where-Object { (Path-Equals $_.Path $Path) -and !$_.Restored }) | Select-Object -Last 1
}
function Replace-Link([string]$Path,[bool]$Create=$false) {
    if ((Test-Path -LiteralPath $Path) -and (([IO.File]::GetAttributes($Path) -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { return }
    $record=Find-Record $Path
    $exists=Test-Path -LiteralPath $Path -PathType Leaf
    if ($record) {
        if (!$exists -or (Hash $Path) -ne $record.InstalledSHA256) {
            $script:report.Preserved+=@{Path=$Path;Reason='Shortcut was removed or modified after installation.'};return
        }
        if (Link-Is-Launcher $Path) { $script:report.AlreadyOwned++;return }
    }
    $old=$null
    $existingRecord=!!$record
    $previousInstalled=if ($record) { $record.InstalledSHA256 } else { $null }
    if ($exists) {
        $old=Get-Link $Path
        if (!$record -and !(Is-Official $Path $old)) { return }
    } elseif (!$Create) { return }
    $beforeHash=if ($exists) { Hash $Path } else { $null }
    $original=$null
    $temp=$null
    try {
        if (!$record) {
            if ($exists) {
                $original=Join-Path $stateRoot ('backups/'+[Guid]::NewGuid().ToString('N')+'.lnk')
                Copy-Item -LiteralPath $Path -Destination $original
                if ((Hash $original) -ne $beforeHash) { throw 'Backup checksum mismatch.' }
            }
            $record=[pscustomobject]@{Path=$Path;OriginalExists=$exists;OriginalBackup=$original;OriginalSHA256=$beforeHash;InstalledSHA256=$null;Restored=$false}
        }
        # Build a fresh link. Editing an MSIX link in place can retain its AppUserModel activation properties.
        $temp=Join-Path ([IO.Path]::GetDirectoryName($Path)) ('DragonCodexBoot-'+[Guid]::NewGuid().ToString('N')+'.lnk')
        $new=Get-Link $temp
        $new.TargetPath=$exePath;$new.Arguments='';$new.WorkingDirectory=$AppRoot
        $new.Description='Play the startup animation, then open the installed client.'
        $new.IconLocation=if ($old -and ![string]::IsNullOrWhiteSpace($old.IconLocation)) { $old.IconLocation } else { $script:defaultIcon }
        if ($old) { $new.Hotkey=$old.Hotkey;$new.WindowStyle=$old.WindowStyle }
        $new.Save()
        if (!(Link-Is-Launcher $temp) -or ![string]::IsNullOrWhiteSpace((Get-AppID $temp))) { throw 'New shortcut metadata verification failed.' }
        # Check the original again immediately before replacing it; preserve concurrent edits.
        if ($exists -and (!(Test-Path -LiteralPath $Path) -or (Hash $Path) -ne $beforeHash)) { throw 'Shortcut changed during installation; preserved.' }
        if (!$exists -and (Test-Path -LiteralPath $Path)) { throw 'A shortcut appeared during installation; preserved.' }
        $newHash=Hash $temp
        $record.InstalledSHA256=$newHash
        if (!(Find-Record $Path)) { $script:state.Entries+=@($record) }
        Save-State
        Move-Item -LiteralPath $temp -Destination $Path -Force
        if ((Hash $Path) -ne $newHash -or !(Link-Is-Launcher $Path)) { throw 'Installed shortcut verification failed.' }
        if ($exists) { $script:report.Replaced++ } else { $script:report.Created++ }
    } catch {
        if ($existingRecord -and $record -and (Test-Path -LiteralPath $Path) -and (Hash $Path) -eq $beforeHash) {
            $record.InstalledSHA256=$previousInstalled
            Save-State
        }
        $script:report.Errors+=@{Path=$Path;Reason=$_.Exception.Message}
    } finally {
        # This is a single generated temporary file, never a directory deletion.
        if ($temp -and (Test-Path -LiteralPath $temp)) { Remove-Item -LiteralPath $temp -ErrorAction SilentlyContinue }
    }
}
function Restore-Links {
    foreach ($entry in @($script:state.Entries)) {
        if ($entry.Restored) { continue }
        try {
            $exists=Test-Path -LiteralPath $entry.Path -PathType Leaf
            if ($exists -and $entry.OriginalExists -and (Hash $entry.Path) -eq $entry.OriginalSHA256) { $entry.Restored=$true;Save-State;continue }
            if (!$exists -or (Hash $entry.Path) -ne $entry.InstalledSHA256) {
                $script:report.Preserved+=@{Path=$entry.Path;Reason='Shortcut was removed or modified; restore did not overwrite it.'};continue
            }
            if ($entry.OriginalExists) {
                $backup=[IO.Path]::GetFullPath($entry.OriginalBackup)
                $backupRoot=[IO.Path]::GetFullPath((Join-Path $stateRoot 'backups'))+[IO.Path]::DirectorySeparatorChar
                if (!$backup.StartsWith($backupRoot,[StringComparison]::OrdinalIgnoreCase) -or (Hash $backup) -ne $entry.OriginalSHA256) { throw 'Backup verification failed.' }
                Copy-Item -LiteralPath $backup -Destination $entry.Path -Force
                if ((Hash $entry.Path) -ne $entry.OriginalSHA256) { throw 'Restored checksum mismatch.' }
            } else { Remove-Item -LiteralPath $entry.Path }
            $entry.Restored=$true;$script:report.Restored++;Save-State
        } catch { $script:report.Errors+=@{Path=$entry.Path;Reason=$_.Exception.Message} }
    }
    $script:state.AutoAttempted=$true;$script:state.Restored=$true;Save-State
}
try {
    try { $locked=$mutex.WaitOne($(if ($Action -eq 'Auto') { 0 } else { 60000 })) } catch [Threading.AbandonedMutexException] { $locked=$true }
    if (!$locked -and $Action -eq 'Auto') { return }
    if (!$locked) { throw 'Another entrypoint operation is still running. Try again after it finishes.' }
    New-Item -ItemType Directory -Path $stateRoot,(Join-Path $stateRoot 'backups') -Force | Out-Null
    $script:state=if (Test-Path -LiteralPath $statePath) { Get-Content -LiteralPath $statePath -Encoding UTF8 -Raw | ConvertFrom-Json } else {
        [pscustomobject]@{Schema=1;AutoAttempted=$false;Restored=$false;Entries=@()}
    }
    if ($state.Schema -ne 1) { throw 'Unsupported integration state version; no shortcuts changed.' }
    if ($Action -eq 'Auto' -and $state.AutoAttempted) { return }
    $script:shell=New-Object -ComObject WScript.Shell
    $script:desktopShell=New-Object -ComObject Shell.Application
    if ($Action -eq 'Restore') { Restore-Links;$report.Status=if ($report.Errors.Count -or $report.Preserved.Count) { 'RestoredWithPreservedChanges' } else { 'Restored' };return }
    if ($Action -eq 'Verify') {
        foreach ($entry in @($state.Entries | Where-Object { !$_.Restored })) {
            if ((Test-Path -LiteralPath $entry.Path) -and (Hash $entry.Path) -eq $entry.InstalledSHA256 -and (Link-Is-Launcher $entry.Path)) { $report.AlreadyOwned++ }
            else { $report.Preserved+=@{Path=$entry.Path;Reason='Shortcut no longer matches the installed version.'} }
        }
        $report.Status='Verified';return
    }
    if (!(Test-Path -LiteralPath $exePath -PathType Leaf)) { throw 'Extract or build the complete launcher first.' }
    $cfg=Get-Content -LiteralPath (Join-Path $AppRoot 'launcher.json') -Encoding UTF8 -Raw | ConvertFrom-Json
    if ($Action -eq 'Auto' -and $cfg.AutoReplaceEntrypoints -eq $false) { $report.Status='Disabled';return }
    if ($cfg.AppLaunch -notmatch '^shell:AppsFolder\\([A-Za-z0-9_.-]+![A-Za-z0-9_.-]+)$') { throw 'Configured AppLaunch must be an AppsFolder application ID.' }
    $script:appID=$Matches[1]
    $script:officialTargets=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($path in @($OfficialExecutablePaths)) { if ($path) { $null=$officialTargets.Add([IO.Path]::GetFullPath($path)) } }
    $displayName='Dragon Codex Boot'
    if (!$SearchRoots) {
        $startApp=Get-StartApps | Where-Object { $_.AppID -ieq $appID } | Select-Object -First 1
        if (!$startApp) { $report.Status='ClientNotInstalled';return }
        $displayName=$startApp.Name
        $family,$application=$appID.Split('!')
        foreach ($package in @(Get-AppxPackage | Where-Object { $_.PackageFamilyName -ieq $family })) {
            $manifest=Get-AppxPackageManifest -Package $package.PackageFullName
            foreach ($app in @($manifest.Package.Applications.Application | Where-Object { $_.Id -ieq $application })) {
                if ($app.Executable) { $null=$officialTargets.Add([IO.Path]::GetFullPath((Join-Path $package.InstallLocation $app.Executable))) }
            }
        }
    }
    $script:defaultIcon=$exePath+',0'
    foreach ($path in $officialTargets) { if (Test-Path -LiteralPath $path) { $script:defaultIcon=$path+',0';break } }
    $roots=New-Object 'Collections.Generic.List[string]'
    if ($SearchRoots) {
        foreach ($path in $SearchRoots) { $roots.Add([IO.Path]::GetFullPath($path)) }
    } else {
        foreach ($folder in @('DesktopDirectory','CommonDesktopDirectory','Programs','CommonPrograms')) {
            $path=[Environment]::GetFolderPath($folder);if ($path) { $roots.Add($path) }
        }
        $roots.Add((Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Microsoft/Internet Explorer/Quick Launch'))
        if ($cfg.ScanAllLocalDrives -ne $false) {
            foreach ($drive in [IO.DriveInfo]::GetDrives()) {
                if ($drive.IsReady -and $drive.DriveType -eq [IO.DriveType]::Fixed) { $roots.Add($drive.RootDirectory.FullName) }
            }
        }
    }
    $report.SearchRoots=@($roots)
    $state.AutoAttempted=$true;$state.Restored=$false;Save-State
    $seen=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $seenLinks=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $queue=New-Object 'Collections.Generic.Queue[string]'
    foreach ($path in $roots) { $queue.Enqueue($path) }
    $clock=[Diagnostics.Stopwatch]::StartNew()
    $windows=[Environment]::GetFolderPath('Windows').TrimEnd('\')
    $skipNames=@('$RECYCLE.BIN','System Volume Information','.git','.integration','.local','node_modules','WinSxS','Package Cache','Recovery')
    while ($queue.Count) {
        if ($clock.Elapsed.TotalSeconds -ge $ScanBudgetSeconds) { $report.ScanTimedOut=$true;break }
        $dir=[IO.Path]::GetFullPath($queue.Dequeue())
        if ($dir -ne [IO.Path]::GetPathRoot($dir)) { $dir=$dir.TrimEnd('\','/') }
        if (!$seen.Add($dir)) { continue }
        if ((Path-Equals $dir $AppRoot) -or (Path-Equals $dir $windows) -or $skipNames -icontains [IO.Path]::GetFileName($dir)) { $report.SkippedFolders++;continue }
        try {
            if (([IO.File]::GetAttributes($dir) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { $report.SkippedFolders++;continue }
            $report.FoldersInspected++
            foreach ($file in [IO.Directory]::GetFiles($dir,'*.lnk',[IO.SearchOption]::TopDirectoryOnly)) {
                if ($seenLinks.Add($file)) {
                    $report.LinksInspected++
                    try { Replace-Link $file } catch { $report.Errors+=@{Path=$file;Reason=$_.Exception.Message} }
                }
            }
            foreach ($child in [IO.Directory]::GetDirectories($dir)) { $queue.Enqueue($child) }
        } catch { $report.SkippedFolders++ }
    }
    if (!$SearchRoots -and !$NoCreateDefaultLinks) {
        $name=($displayName -replace '[<>:"/\\|?*]','_')+'.lnk'
        foreach ($folder in @('DesktopDirectory','Programs')) {
            $dir=[Environment]::GetFolderPath($folder)
            if ($dir -and (Test-Path -LiteralPath $dir -PathType Container)) { Replace-Link (Join-Path $dir $name) $true }
        }
        # A distinct Start entry lets Windows offer a taskbar pin without confusing it with the MSIX app.
        $programs=[Environment]::GetFolderPath('Programs')
        if ($programs -and (Test-Path -LiteralPath $programs -PathType Container)) {
            Replace-Link (Join-Path $programs 'Dragon Codex Boot.lnk') $true
        }
    }
    $report.Status=if ($report.Errors.Count -or $report.ScanTimedOut -or $report.SkippedFolders) { 'CompletedWithLimits' } else { 'Completed' }
} catch {
    $report.Status='Failed';$report.Errors+=@{Path=$AppRoot;Reason=$_.Exception.Message}
    throw
} finally {
    $report.Finished=(Get-Date).ToString('o')
    # Do not overwrite the detailed first-run report with a no-op on later launches.
    if ($locked -and $report.Status -ne 'Running') {
        try { Write-Json $reportPath $report } catch { Write-Warning $_.Exception.Message }
        [pscustomobject]$report
        Write-Output ('Report: '+$reportPath)
    }
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
