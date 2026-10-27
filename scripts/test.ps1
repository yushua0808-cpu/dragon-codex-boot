$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& (Join-Path $PSScriptRoot 'build.ps1')
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
$argsList[$argsList.Length-1]=Join-Path $repoRoot 'tests/LifecycleTests.cs'
$argsList[3]='/out:'+(Join-Path $outputRoot 'LifecycleTests.exe')
& (Join-Path $framework 'csc.exe') @argsList
if ($LASTEXITCODE -ne 0) { throw 'Lifecycle harness compilation failed.' }
& (Join-Path $outputRoot 'LifecycleTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Lifecycle tests failed.' }
& (Join-Path $repoRoot 'tests/EntrypointTests.ps1')
Get-Content -LiteralPath (Join-Path $outputRoot 'qa/self-test.json') -Raw
