$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$rhythKitProject = Join-Path $root "src\RhythKit\RhythKit.csproj"
$installerProject = Join-Path $root "src\RhythKit.Installer\RhythKit.Installer.csproj"
$agentProject = Join-Path $root "src\RhythKit.Agent\RhythKit.Agent.csproj"
$uninstallerProject = Join-Path $root "src\RhythKit.Uninstaller\RhythKit.Uninstaller.csproj"
$payloadSource = Join-Path $root "src\RhythKit.Installer\RhythKitPayload.cs"
$agentPayloadSource = Join-Path $root "src\RhythKit.Installer\RhythKitAgentPayload.cs"
$uninstallerPayloadSource = Join-Path $root "src\RhythKit.Installer\RhythKitUninstallerPayload.cs"
$dist = Join-Path $root "dist"
$publish = Join-Path $dist "win-x64"
$agentPublish = Join-Path $publish "agent"
$uninstallerPublish = Join-Path $publish "uninstaller"

Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $publish -Force | Out-Null
New-Item -ItemType Directory -Path $agentPublish -Force | Out-Null
New-Item -ItemType Directory -Path $uninstallerPublish -Force | Out-Null

dotnet --version
dotnet restore $rhythKitProject
dotnet restore $installerProject
dotnet restore $agentProject
dotnet restore $uninstallerProject
dotnet build $rhythKitProject -c Release --no-restore

$rhythKitOutput = Get-ChildItem -Path (Join-Path $root "src\RhythKit") -Filter "RhythKit.dll" -Recurse -File |
    Where-Object { $_.FullName -match "[\\/]Release[\\/]" } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $rhythKitOutput) {
    $candidates = Get-ChildItem -Path (Join-Path $root "src\RhythKit") -Filter "RhythKit.dll" -Recurse -File -ErrorAction SilentlyContinue
    $paths = ($candidates | ForEach-Object FullName) -join [Environment]::NewLine
    throw "RhythKit.dll was not produced by the Release build.`n$paths"
}

$base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($rhythKitOutput.FullName))
Set-Content -Path $payloadSource -Value @"
namespace RhythKit.Installer;

internal static class RhythKitPayload
{
    public static byte[] Data => Convert.FromBase64String("$base64");
}
"@ -Encoding UTF8

dotnet publish $agentProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $agentPublish
dotnet publish $uninstallerProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $uninstallerPublish

$agentOutput = Join-Path $agentPublish "RhythKit.Agent.exe"
$uninstallerOutput = Join-Path $uninstallerPublish "RhythKit.Uninstaller.exe"
if (!(Test-Path $agentOutput)) { throw "RhythKit.Agent.exe was not produced." }
if (!(Test-Path $uninstallerOutput)) { throw "RhythKit.Uninstaller.exe was not produced." }

$agentBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($agentOutput))
$uninstallerBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($uninstallerOutput))
Set-Content -Path $agentPayloadSource -Value @"
namespace RhythKit.Installer;

internal static class RhythKitAgentPayload
{
    public static byte[] Data => Convert.FromBase64String("$agentBase64");
}
"@ -Encoding UTF8
Set-Content -Path $uninstallerPayloadSource -Value @"
namespace RhythKit.Installer;

internal static class RhythKitUninstallerPayload
{
    public static byte[] Data => Convert.FromBase64String("$uninstallerBase64");
}
"@ -Encoding UTF8

dotnet publish $installerProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publish

$installerOutput = Join-Path $publish "RhythKit.Installer.exe"
if (!(Test-Path $installerOutput)) { throw "RhythKit.Installer.exe was not produced." }

$agentPayloadSourceText = @"
namespace RhythKit.Installer;

internal static class RhythKitAgentPayload
{
    public static byte[] Data => [];
}
"@
$uninstallerPayloadSourceText = @"
namespace RhythKit.Installer;

internal static class RhythKitUninstallerPayload
{
    public static byte[] Data => [];
}
"@
Set-Content -Path $agentPayloadSource -Value $agentPayloadSourceText -Encoding UTF8
Set-Content -Path $uninstallerPayloadSource -Value $uninstallerPayloadSourceText -Encoding UTF8

$agentSize = (Get-Item $agentOutput).Length
$uninstallerSize = (Get-Item $uninstallerOutput).Length
$installerSize = (Get-Item $installerOutput).Length
if ($installerSize -lt 1000000) { throw "Installer executable is unexpectedly small." }
if ($agentSize -lt 1000000) { throw "Agent executable is unexpectedly small." }
if ($uninstallerSize -lt 1000000) { throw "Uninstaller executable is unexpectedly small." }

Remove-Item $agentPublish -Recurse -Force
Remove-Item $uninstallerPublish -Recurse -Force

Write-Host "Built installer: $installerOutput"
Write-Host "Embedded Agent: $agentSize bytes"
Write-Host "Embedded Uninstaller: $uninstallerSize bytes"
