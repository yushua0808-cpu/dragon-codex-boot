$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'The x64 .NET Framework C# compiler is required.' }
# Keep generated binaries outside the checkout; do not open UI or remove any files.
$output = Join-Path ([IO.Path]::GetTempPath()) ('DragonCodexPolicyTests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
$executable = Join-Path $output 'LifecyclePolicyTests.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /reference:System.dll /reference:System.Core.dll ('/out:' + $executable) (Join-Path $PSScriptRoot 'LaunchLifecycle.cs') (Join-Path $PSScriptRoot 'LifecyclePolicyTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Policy test compilation failed.' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Policy tests failed.' }
Write-Output ('Test binary retained at: ' + $executable)
