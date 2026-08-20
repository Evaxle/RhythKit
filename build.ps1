$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$rhythKitProject = Join-Path $root "src\RhythKit\RhythKit.csproj"
$installerProject = Join-Path $root "src\RhythKit.Installer\RhythKit.Installer.csproj"
$rhythKitOutput = Join-Path $root "src\RhythKit\bin\Release\net10.0\RhythKit.dll"
$payloadSource = Join-Path $root "src\RhythKit.Installer\RhythKitPayload.cs"
$publish = Join-Path $root "dist\win-x64"

Remove-Item (Join-Path $root "dist") -Recurse -Force -ErrorAction SilentlyContinue

dotnet restore $installerProject
dotnet build $rhythKitProject -c Release --no-restore

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

Write-Host "Built: $publish\RhythKit.Installer.exe"
