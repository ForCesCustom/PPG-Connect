param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$managed = Join-Path $GameRoot 'People Playground_Data\Managed'
$core = Join-Path $GameRoot 'BepInEx\core'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath (Join-Path $GameRoot 'People Playground.exe'))) { throw 'Game executable not found at supplied path.' }
$out = Join-Path $PSScriptRoot 'bin\Release'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$references = @('Assembly-CSharp','Facepunch.Steamworks.Win64','UnityEngine','UnityEngine.CoreModule','UnityEngine.Physics2DModule','UnityEngine.InputLegacyModule','UnityEngine.IMGUIModule','UnityEngine.ImageConversionModule','UnityEngine.TextRenderingModule') | ForEach-Object { '/reference:' + (Join-Path $managed ($_ + '.dll')) }
$references += @('/reference:' + (Join-Path $core 'BepInEx.dll'))
$references += @('/reference:' + (Join-Path $core '0Harmony.dll'))
$sources = @(Get-ChildItem -LiteralPath (Join-Path $repo 'Source') -Filter '*.cs' | ForEach-Object FullName)
& $compiler /nologo /target:library /platform:x64 /optimize+ ('/out:' + (Join-Path $out 'Connect.BepInEx.dll')) $references $sources
if ($LASTEXITCODE -ne 0) { throw 'Plugin compilation failed.' }
Write-Output 'BUILD PASS'
$suites = @(
    @{ Name='BotBrainSmokeTests'; Sources=@('BotBrain.cs') },
    @{ Name='ProtocolSmokeTests'; Sources=@('WireProtocol.cs','CursorNetworking.cs','WorldState.cs','ReplicatedObjectState.cs') },
    @{ Name='InstallationHealthSmokeTests'; Sources=@('InstallationHealth.cs') },
    @{ Name='ObjectStateSmokeTests'; Sources=@('WireProtocol.cs','WorldState.cs','ReplicatedObjectState.cs') },
    @{ Name='WorldLifecycleSmokeTests'; Sources=@('WireProtocol.cs','WorldManifestProtocol.cs') },
    @{ Name='SharedWorldSmokeTests'; Sources=@('WireProtocol.cs','SharedWorldProtocol.cs') },
    @{ Name='WoundStateSmokeTests'; Sources=@('WireProtocol.cs','WorldState.cs','ReplicatedObjectState.cs','ReplicatedWoundState.cs') }
)
foreach ($suite in $suites) {
    $exe = Join-Path $out ($suite.Name + '.exe')
    $inputs = @($suite.Sources | ForEach-Object { Join-Path (Join-Path $repo 'Source') $_ })
    $inputs += Join-Path $PSScriptRoot ($suite.Name + '.cs')
    & $compiler /nologo /target:exe /platform:x64 ('/out:' + $exe) $references $inputs
    if ($LASTEXITCODE -ne 0) { throw ($suite.Name + ' compilation failed.') }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw ($suite.Name + ' failed with ' + $LASTEXITCODE) }
    Write-Output ($suite.Name + ' PASS')
}
Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $out 'Connect.BepInEx.dll')
