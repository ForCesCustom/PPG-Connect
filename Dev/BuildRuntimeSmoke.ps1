param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$managed = Join-Path $GameRoot 'People Playground_Data\Managed'
$core = Join-Path $GameRoot 'BepInEx\core'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath (Join-Path $GameRoot 'People Playground.exe'))) { throw 'Game executable not found at supplied path.' }
$out = Join-Path $PSScriptRoot 'bin\Release'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$references = @('Assembly-CSharp','Facepunch.Steamworks.Win64','UnityEngine','UnityEngine.CoreModule','UnityEngine.Physics2DModule','UnityEngine.InputLegacyModule','UnityEngine.IMGUIModule','UnityEngine.ImageConversionModule') | ForEach-Object { '/reference:' + (Join-Path $managed ($_ + '.dll')) }
$references += @('/reference:' + (Join-Path $core 'BepInEx.dll'))
$references += @('/reference:' + (Join-Path $core '0Harmony.dll'))
$inputs = @('WireProtocol.cs','WorldState.cs','ReplicatedObjectState.cs','ReplicatedWoundState.cs') | ForEach-Object { Join-Path (Join-Path $repo 'Source') $_ }
$inputs += Join-Path $PSScriptRoot 'RuntimeReplicationSmoke.cs'
$inputs += Join-Path $PSScriptRoot 'SpawnPathRuntimeSmoke.cs'
& $compiler /nologo /target:library /platform:x64 /optimize+ ('/out:' + (Join-Path $out 'ConnectSmoke.dll')) $references $inputs
if ($LASTEXITCODE -ne 0) { throw 'Runtime fixture harness compilation failed.' }
& $compiler /nologo /target:library /platform:x64 /optimize+ ('/out:' + (Join-Path $out 'Connect.HostSpawnRegression.dll')) $references (Join-Path $PSScriptRoot 'HostSpawnRegressionRuntime.cs')
if ($LASTEXITCODE -ne 0) { throw 'Host spawn regression harness compilation failed.' }
Write-Output 'RUNTIME HARNESS BUILD PASS. Developer-only DLLs; never include in a release ZIP.'
Write-Output 'For an isolated fresh Steam process only: -connect-replication-smoke -connect-host-spawn-smoke'
Write-Output 'The driver loads Default, creates/leaves its own private one-member lobby, then quits. Remove test plugins after use.'
