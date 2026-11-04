param([string]$IconPath)
$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& (Join-Path $PSScriptRoot 'build.ps1') -IconPath $IconPath
$outputRoot=Join-Path $repoRoot 'build/DragonCodexBoot'
$exe=Join-Path $outputRoot 'DragonCodexBoot.exe'
$process=Start-Process -FilePath $exe -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw 'Launcher self-test failed. See build/DragonCodexBoot/self-test-error.txt.' }
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$argsList=@('/nologo','/target:exe','/platform:x64',('/out:'+(Join-Path $outputRoot 'GeometryTests.exe')),('/reference:'+$exe),'/reference:System.Web.Extensions.dll',('/reference:'+(Join-Path $framework 'WPF/PresentationFramework.dll')),('/reference:'+(Join-Path $framework 'WPF/WindowsBase.dll')),(Join-Path $repoRoot 'tests/GeometryTests.cs'))
& (Join-Path $framework 'csc.exe') @argsList
if ($LASTEXITCODE -ne 0) { throw 'Test harness compilation failed.' }
& (Join-Path $outputRoot 'GeometryTests.exe') (Join-Path $repoRoot 'config/launcher.example.json')
if ($LASTEXITCODE -ne 0) { throw 'Geometry or configuration tests failed.' }
$handoffArgs=@('/reference:System.Xaml.dll',('/reference:'+(Join-Path $framework 'WPF/PresentationCore.dll')),'/nologo','/target:exe','/platform:x64',('/out:'+(Join-Path $outputRoot 'HandoffTests.exe')),('/reference:'+$exe),('/reference:'+(Join-Path $framework 'WPF/PresentationFramework.dll')),('/reference:'+(Join-Path $framework 'WPF/WindowsBase.dll')),(Join-Path $repoRoot 'tests/HandoffTests.cs'))
& (Join-Path $framework 'csc.exe') @handoffArgs
if ($LASTEXITCODE -ne 0) { throw 'Handoff regression harness compilation failed.' }
& (Join-Path $outputRoot 'HandoffTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Handoff regression tests failed.' }
& (Join-Path $repoRoot 'tests/EntrypointTests.ps1')
Get-Content -LiteralPath (Join-Path $outputRoot 'qa/self-test.json') -Raw
