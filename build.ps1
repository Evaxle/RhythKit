$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$rhythKitProject = Join-Path $root "src\RhythKit\RhythKit.csproj"
$installerProject = Join-Path $root "src\RhythKit.Installer\RhythKit.Installer.csproj"
$agentProject = Join-Path $root "src\RhythKit.Agent\RhythKit.Agent.csproj"
$rhythKitOutput = Join-Path $root "src\RhythKit\.godot\mono\temp\bin\Release\RhythKit.dll"
$payloadSource = Join-Path $root "src\RhythKit.Installer\RhythKitPayload.cs"
$publish = Join-Path $root "dist\win-x64"

Remove-Item (Join-Path $root "dist") -Recurse -Force -ErrorAction SilentlyContinue

dotnet restore $installerProject
dotnet restore $agentProject
dotnet build $rhythKitProject -c Release --no-restore

if (!(Test-Path $rhythKitOutput)) { throw "RhythKit.dll was not produced at $rhythKitOutput" }
$base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($rhythKitOutput))
$source = @"
namespace RhythKit.Installer;

internal static class RhythKitPayload
{
    public static byte[] Data => Convert.FromBase64String("$base64");
}
"@
Set-Content -Path $payloadSource -Value $source -Encoding UTF8

dotnet publish $installerProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publish
dotnet publish $agentProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $publish "agent")

Copy-Item (Join-Path $publish "agent\RhythKit.Agent.exe") (Join-Path $publish "RhythKit.Agent.exe") -Force
Remove-Item (Join-Path $publish "agent") -Recurse -Force

if (!(Test-Path (Join-Path $publish "RhythKit.Installer.exe"))) { throw "Installer executable was not produced." }
if (!(Test-Path (Join-Path $publish "RhythKit.Agent.exe"))) { throw "Agent executable was not produced." }
Write-Host "Built: $publish\RhythKit.Installer.exe"
