$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture=Join-Path $repoRoot ('.local/entrypoint-tests-'+[Guid]::NewGuid().ToString('N'))
$appRoot=Join-Path $fixture 'app'
$linksRoot=Join-Path $fixture 'shortcuts'
$nested=Join-Path $linksRoot 'nested'
New-Item -ItemType Directory -Path $appRoot,$linksRoot,$nested -Force | Out-Null
$builtLauncher=Join-Path $repoRoot 'build/DragonCodexBoot/DragonCodexBoot.exe'
$fixtureLauncher=Join-Path $appRoot 'DragonCodexBoot.exe'
if (Test-Path -LiteralPath $builtLauncher -PathType Leaf) { Copy-Item -LiteralPath $builtLauncher -Destination $fixtureLauncher }
else { [IO.File]::WriteAllBytes($fixtureLauncher,[byte[]]@(0)) }
Copy-Item -LiteralPath (Join-Path $repoRoot 'config/launcher.example.json') -Destination (Join-Path $appRoot 'launcher.json')
$official=Join-Path $fixture 'OfficialClient.exe'
$other=Join-Path $fixture 'Other.exe'
[IO.File]::WriteAllBytes($official,[byte[]]@(1));[IO.File]::WriteAllBytes($other,[byte[]]@(2))
$shell=New-Object -ComObject WScript.Shell
$script:checks=0
function Check([bool]$Ok,[string]$Message) { if (!$Ok) { throw $Message };$script:checks++ }
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path).Hash }
function Make-Link([string]$Name,[string]$Target,[string]$Arguments='') {
    $path=Join-Path $linksRoot $Name
    $link=$shell.CreateShortcut($path);$link.TargetPath=$Target;$link.Arguments=$Arguments
    $link.IconLocation=$other+',0';$link.WorkingDirectory=$fixture;$link.Description='Original description';$link.Save()
    return $path
}
function Run([string]$Action) {
    # Every operation is confined to fixtures. No actual Desktop, Start or taskbar folder is scanned.
    $null=& (Join-Path $repoRoot 'scripts/integrate-entrypoints.ps1') -Action $Action -AppRoot $appRoot -SearchRoots $linksRoot -OfficialExecutablePaths $official -NoCreateDefaultLinks
    Get-Content -LiteralPath (Join-Path $appRoot '.integration/report.json') -Encoding UTF8 -Raw | ConvertFrom-Json
}
$direct=Make-Link 'renamed official.lnk' $official
$apps=Make-Link 'nested/appsfolder.lnk' (Join-Path $env:WINDIR 'explorer.exe') 'shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App'
$sameName=Make-Link 'ChatGPT.lnk' $other
$wrongApp=Make-Link 'wrong-package.lnk' (Join-Path $env:WINDIR 'explorer.exe') 'shell:AppsFolder\Other.App_123!App'
$withArgs=Make-Link 'command-arguments.lnk' (Join-Path $env:WINDIR 'explorer.exe') 'shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App extra'
$url=Join-Path $linksRoot 'ChatGPT.url';Set-Content -LiteralPath $url -Encoding ASCII -Value "[InternetShortcut]`r`nURL=https://chatgpt.com/"
$all=@($direct,$apps,$sameName,$wrongApp,$withArgs,$url)
$hashes=@{};foreach ($path in $all) { $hashes[$path]=Hash $path }
$install=Run 'Auto'
Check ($install.Replaced -eq 2 -and $install.Errors.Count -eq 0 -and $install.FoldersInspected -eq 2) 'Official and AppsFolder links must be found recursively.'
foreach ($path in @($direct,$apps)) {
    $link=$shell.CreateShortcut($path)
    Check ($link.TargetPath -ieq (Join-Path $appRoot 'DragonCodexBoot.exe')) 'Target must be launcher.'
    Check ($link.Arguments -eq '' -and $link.WorkingDirectory -ieq $appRoot) 'Launcher must have clean args and its own working directory.'
    Check ($link.IconLocation -ieq $other+',0') 'Original icon must be preserved.'
}
foreach ($path in @($sameName,$wrongApp,$withArgs,$url)) { Check ((Hash $path) -eq $hashes[$path]) 'Unrelated links and web URLs must remain byte-identical.' }
$statePath=Join-Path $appRoot '.integration/entrypoints-state.json'
$state=Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
Check ($state.Entries.Count -eq 2 -and $state.AutoAttempted) 'Exactly two backup records expected.'
foreach ($record in $state.Entries) { Check ((Hash $record.OriginalBackup) -eq $hashes[$record.Path]) 'Backups must match original bytes.' }
$savedState=Hash $statePath
$firstReport=Hash (Join-Path $appRoot '.integration/report.json')
$null=Run 'Auto'
Check ((Hash $statePath) -eq $savedState -and (Hash (Join-Path $appRoot '.integration/report.json')) -eq $firstReport) 'Later Auto must not rescan or erase first-run report.'
$again=Run 'Install'
Check ($again.Replaced -eq 0 -and $again.AlreadyOwned -eq 2) 'Explicit rescan is idempotent.'
$state=Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
Check ($state.Entries.Count -eq 2) 'Rescan must not replace original backups.'
$verify=Run 'Verify'
Check ($verify.Status -eq 'Verified' -and $verify.AlreadyOwned -eq 2 -and $verify.Preserved.Count -eq 0 -and $verify.Errors.Count -eq 0) 'Installed links must verify successfully.'
$stateSnapshot=[IO.File]::ReadAllBytes($statePath)
$directInstalled=[IO.File]::ReadAllBytes($direct)
$appsInstalled=[IO.File]::ReadAllBytes($apps)
$tampered=$shell.CreateShortcut($direct);$tampered.Description='Tampered after installation';$tampered.Save()
$tamperedHash=Hash $direct
$tamperedVerify=Run 'Verify'
Check ($tamperedVerify.Status -eq 'VerificationFailed' -and $tamperedVerify.AlreadyOwned -eq 1 -and
    @($tamperedVerify.Preserved | Where-Object { $_.Path -eq $direct }).Count -eq 1 -and
    (Hash $direct) -eq $tamperedHash) 'Tampered links must fail verification without being changed.'
[IO.File]::WriteAllBytes($direct,$directInstalled)
Remove-Item -LiteralPath $apps
$missingVerify=Run 'Verify'
Check ($missingVerify.Status -eq 'VerificationFailed' -and $missingVerify.AlreadyOwned -eq 1 -and
    @($missingVerify.Preserved | Where-Object { $_.Path -eq $apps }).Count -eq 1 -and
    !(Test-Path -LiteralPath $apps)) 'Missing links must fail verification without being recreated.'
[IO.File]::WriteAllBytes($apps,$appsInstalled)
$state.Entries=@();$state | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $statePath -Encoding UTF8
$emptyVerify=Run 'Verify'
Check ($emptyVerify.Status -eq 'VerificationFailed' -and $emptyVerify.AlreadyOwned -eq 0 -and $emptyVerify.Errors.Count -eq 1) 'Zero active records must fail verification.'
[IO.File]::WriteAllBytes($statePath,$stateSnapshot)
$verify=Run 'Verify'
Check ($verify.Status -eq 'Verified' -and $verify.AlreadyOwned -eq 2) 'Fixture must verify after failure cases are reverted.'
$changed=$shell.CreateShortcut($direct);$changed.Description='User changed shortcut';$changed.Save()
$changedHash=Hash $direct
$restore=Run 'Restore'
Check ($restore.Restored -eq 1 -and $restore.Preserved.Count -eq 1) 'Restore must preserve subsequent user edits.'
Check ((Hash $apps) -eq $hashes[$apps]) 'Original shortcut must be restored exactly.'
Check ((Hash $direct) -eq $changedHash) 'Changed shortcut must be preserved exactly.'
$state=Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
Check ($state.Restored -and $state.AutoAttempted) 'Restore must suppress automatic reinstallation.'
$restoreReport=Hash (Join-Path $appRoot '.integration/report.json')
$null=Run 'Auto'
Check ((Hash (Join-Path $appRoot '.integration/report.json')) -eq $restoreReport) 'Startup after restore must not reinstall.'
foreach ($path in @($sameName,$wrongApp,$withArgs,$url)) { Check ((Hash $path) -eq $hashes[$path]) 'Unrelated assets must remain unchanged after restore.' }
$second=Run 'Install';Check ($second.Replaced -eq 1 -and $second.Preserved.Count -eq 1) 'Explicit reinstall must still preserve modified previously owned links.'
$state=Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
$record=@($state.Entries | Where-Object { $_.Path -eq $apps -and !$_.Restored })[0]
[IO.File]::WriteAllBytes($record.OriginalBackup,[byte[]]@(9))
$installedHash=Hash $apps
$corrupt=Run 'Restore'
Check ($corrupt.Errors.Count -eq 1 -and (Hash $apps) -eq $installedHash) 'Corrupted backup must never be restored.'
# A record for a new link is exercised separately; deletion is only permitted after ownership hash verification.
$created=Make-Link 'created-fixture.lnk' (Join-Path $appRoot 'DragonCodexBoot.exe')
$link=$shell.CreateShortcut($created);$link.WorkingDirectory=$appRoot;$link.Arguments='';$link.Save()
$state.Entries=@([pscustomobject]@{Path=$created;OriginalExists=$false;OriginalBackup=$null;OriginalSHA256=$null;InstalledSHA256=(Hash $created);Restored=$false})
$state.Restored=$false;$state | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $statePath -Encoding UTF8
$deleted=Run 'Restore';Check ($deleted.Restored -eq 1 -and !(Test-Path -LiteralPath $created)) 'Only a verified project-created shortcut may be removed.'
$cfg=Get-Content -LiteralPath (Join-Path $appRoot 'launcher.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$cfg.AutoReplaceEntrypoints=$false;$cfg | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $appRoot 'launcher.json') -Encoding UTF8
$state.AutoAttempted=$false;$state | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $statePath -Encoding UTF8
$disabled=Run 'Auto';Check ($disabled.Status -eq 'Disabled') 'Explicit opt-out must prevent integration.'
# A sharing violation must leave the original intact and retain a valid backup for recovery.
$lockedLink=Make-Link 'locked-fixture.lnk' $official
$lockedOriginal=Hash $lockedLink
$lock=[IO.File]::Open($lockedLink,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
try {
    $lockedReport=Run 'Install'
    Check ($lockedReport.Errors.Count -ge 1 -and (Hash $lockedLink) -eq $lockedOriginal) 'Write failure must preserve original bytes.'
} finally { $lock.Dispose() }
$unwritten=Run 'Restore'
Check ((Hash $lockedLink) -eq $lockedOriginal) 'Restore must accept an unchanged original after a failed write.'
$qaRoot=Join-Path $fixture 'qa'
New-Item -ItemType Directory -Path $qaRoot -Force | Out-Null
@{ok=$true;checks=$checks;scope='Isolated .lnk file metadata and backup/restore only; no actual user entrypoints changed.'} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $qaRoot 'entrypoint-tests.json') -Encoding UTF8
Write-Output ('PASS: '+$checks+' isolated entrypoint assertions. No desktop UI or actual shortcuts changed.')
