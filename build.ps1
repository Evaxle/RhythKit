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
$agentPublish = Join-Path $dist "_agent"
$uninstallerPublish = Join-Path $dist "_uninstaller"
$backup = Join-Path $env:TEMP ("rhythkit-build-" + [guid]::NewGuid().ToString())

New-Item -ItemType Directory -Path $backup -Force | Out-Null
Copy-Item $payloadSource (Join-Path $backup "RhythKitPayload.cs")
Copy-Item $agentPayloadSource (Join-Path $backup "RhythKitAgentPayload.cs")
Copy-Item $uninstallerPayloadSource (Join-Path $backup "RhythKitUninstallerPayload.cs")

try {
    Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $root "src\RhythKit\bin") -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $root "src\RhythKit\obj") -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $root "src\RhythKit.Installer\bin") -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $root "src\RhythKit.Installer\obj") -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $root "src\RhythKit.Agent\bin") -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $root "src\RhythKit.Agent\obj") -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $root "src\RhythKit.Uninstaller\bin") -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $root "src\RhythKit.Uninstaller\obj") -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $publish,$agentPublish,$uninstallerPublish -Force | Out-Null

    $common = @("-p:EnableWindowsTargeting=true", "-p:DebugType=None", "-p:DebugSymbols=false", "-p:EnablePackagePruning=false")
    $publishProps = $common + @("-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true")

    dotnet --version
    dotnet restore $rhythKitProject @common
    dotnet build $rhythKitProject -c Release --no-restore @common

    $rhythKitOutput = Get-ChildItem (Join-Path $root "src\RhythKit") -Filter "RhythKit.dll" -Recurse -File |
        Where-Object { $_.FullName -match "[\\/]bin[\\/]Release[\\/]" } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $rhythKitOutput) { throw "RhythKit.dll was not produced." }

    $base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($rhythKitOutput.FullName))
    @"
namespace RhythKit.Installer;

internal static class RhythKitPayload
{
    public static byte[] Data => Convert.FromBase64String("$base64");
}
"@ | Set-Content $payloadSource -Encoding UTF8

    dotnet restore $agentProject -r win-x64 @common
    dotnet restore $uninstallerProject -r win-x64 @common
    dotnet restore $installerProject -r win-x64 @common

    dotnet publish $agentProject -c Release -r win-x64 --self-contained true --no-restore @publishProps -o $agentPublish
    dotnet publish $uninstallerProject -c Release -r win-x64 --self-contained true --no-restore @publishProps -o $uninstallerPublish

    $agentOutput = Join-Path $agentPublish "RhythKit.Agent.exe"
    $uninstallerOutput = Join-Path $uninstallerPublish "RhythKit.Uninstaller.exe"
    if (!(Test-Path $agentOutput)) { throw "RhythKit.Agent.exe was not produced." }
    if (!(Test-Path $uninstallerOutput)) { throw "RhythKit.Uninstaller.exe was not produced." }

    $agentBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($agentOutput))
    $uninstallerBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($uninstallerOutput))
    @"
namespace RhythKit.Installer;

internal static class RhythKitAgentPayload
{
    public static byte[] Data => Convert.FromBase64String("$agentBase64");
}
"@ | Set-Content $agentPayloadSource -Encoding UTF8
    @"
namespace RhythKit.Installer;

internal static class RhythKitUninstallerPayload
{
    public static byte[] Data => Convert.FromBase64String("$uninstallerBase64");
}
"@ | Set-Content $uninstallerPayloadSource -Encoding UTF8

    dotnet publish $installerProject -c Release -r win-x64 --self-contained true --no-restore @publishProps -o $publish

    $installerOutput = Join-Path $publish "RhythKitInstall.exe"
    if (!(Test-Path $installerOutput)) { throw "RhythKitInstall.exe was not produced." }
    if ((Get-Item $installerOutput).Length -lt 1000000) { throw "RhythKitInstall.exe is unexpectedly small." }

    Remove-Item $agentPublish,$uninstallerPublish -Recurse -Force
    Get-ChildItem $publish -File | Where-Object { $_.Name -ne "RhythKitInstall.exe" } | Remove-Item -Force
    Get-ChildItem $publish -Directory | Remove-Item -Recurse -Force

    Write-Host "BUILD SUCCESSFUL"
    Write-Host "Portable installer: $installerOutput"
}
finally {
    Copy-Item (Join-Path $backup "RhythKitPayload.cs") $payloadSource -Force
    Copy-Item (Join-Path $backup "RhythKitAgentPayload.cs") $agentPayloadSource -Force
    Copy-Item (Join-Path $backup "RhythKitUninstallerPayload.cs") $uninstallerPayloadSource -Force
    Remove-Item $backup -Recurse -Force -ErrorAction SilentlyContinue
}
