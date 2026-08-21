#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DIST="$ROOT/dist/win-x64"
WORK="$ROOT/dist/.build"
RHYTHKIT="$ROOT/src/RhythKit/RhythKit.csproj"
INSTALLER="$ROOT/src/RhythKit.Installer/RhythKit.Installer.csproj"
AGENT="$ROOT/src/RhythKit.Agent/RhythKit.Agent.csproj"
UNINSTALLER="$ROOT/src/RhythKit.Uninstaller/RhythKit.Uninstaller.csproj"
PAYLOAD="$ROOT/src/RhythKit.Installer/RhythKitPayload.cs"
AGENT_PAYLOAD="$ROOT/src/RhythKit.Installer/RhythKitAgentPayload.cs"
UNINSTALLER_PAYLOAD="$ROOT/src/RhythKit.Installer/RhythKitUninstallerPayload.cs"
BACKUP="$(mktemp -d)"

cleanup() {
  cp "$BACKUP/RhythKitPayload.cs" "$PAYLOAD"
  cp "$BACKUP/RhythKitAgentPayload.cs" "$AGENT_PAYLOAD"
  cp "$BACKUP/RhythKitUninstallerPayload.cs" "$UNINSTALLER_PAYLOAD"
  rm -rf "$BACKUP"
}
trap cleanup EXIT

command -v dotnet >/dev/null 2>&1 || { echo "dotnet 10 SDK is required" >&2; exit 1; }
dotnet --list-sdks | grep -Eq '^10\.' || { echo "dotnet 10 SDK is required" >&2; exit 1; }

cp "$PAYLOAD" "$BACKUP/RhythKitPayload.cs"
cp "$AGENT_PAYLOAD" "$BACKUP/RhythKitAgentPayload.cs"
cp "$UNINSTALLER_PAYLOAD" "$BACKUP/RhythKitUninstallerPayload.cs"

rm -rf "$ROOT/dist"
rm -rf "$ROOT/src/RhythKit/bin" "$ROOT/src/RhythKit/obj"
rm -rf "$ROOT/src/RhythKit.Installer/bin" "$ROOT/src/RhythKit.Installer/obj"
rm -rf "$ROOT/src/RhythKit.Agent/bin" "$ROOT/src/RhythKit.Agent/obj"
rm -rf "$ROOT/src/RhythKit.Uninstaller/bin" "$ROOT/src/RhythKit.Uninstaller/obj"
mkdir -p "$DIST" "$WORK/agent" "$WORK/uninstaller"

COMMON=(
  "-p:EnableWindowsTargeting=true"
  "-p:DebugType=None"
  "-p:DebugSymbols=false"
  "-p:EnablePackagePruning=false"
  "-p:AllowMissingPrunePackageData=true"
)
PUBLISH=(
  "-p:PublishSingleFile=true"
  "-p:IncludeNativeLibrariesForSelfExtract=true"
  "-p:EnableWindowsTargeting=true"
  "-p:DebugType=None"
  "-p:DebugSymbols=false"
  "-p:EnablePackagePruning=false"
  "-p:AllowMissingPrunePackageData=true"
)

printf 'Using .NET %s\n' "$(dotnet --version)"

dotnet restore "$RHYTHKIT" "${COMMON[@]}"
dotnet build "$RHYTHKIT" -c Release --no-restore "${COMMON[@]}"

RHYTHKIT_DLL="$(find "$ROOT/src/RhythKit" -type f -name 'RhythKit.dll' -path '*/bin/Release/*' -print -quit 2>/dev/null || true)"
[[ -n "$RHYTHKIT_DLL" ]] || { echo "RhythKit.dll was not produced" >&2; exit 1; }

{
  printf '%s\n' 'namespace RhythKit.Installer;' '' 'internal static class RhythKitPayload' '{' '    public static byte[] Data => Convert.FromBase64String("'
  base64 -w 0 "$RHYTHKIT_DLL"
  printf '%s\n' '");' '}'
} > "$PAYLOAD"

dotnet restore "$AGENT" -r win-x64 "${COMMON[@]}"
dotnet restore "$UNINSTALLER" -r win-x64 "${COMMON[@]}"
dotnet restore "$INSTALLER" -r win-x64 "${COMMON[@]}"

dotnet publish "$AGENT" -c Release -r win-x64 --self-contained true --no-restore "${PUBLISH[@]}" -o "$WORK/agent"
dotnet publish "$UNINSTALLER" -c Release -r win-x64 --self-contained true --no-restore "${PUBLISH[@]}" -o "$WORK/uninstaller"

AGENT_EXE="$WORK/agent/RhythKit.Agent.exe"
UNINSTALLER_EXE="$WORK/uninstaller/RhythKit.Uninstaller.exe"
[[ -f "$AGENT_EXE" ]] || { echo "RhythKit.Agent.exe was not produced" >&2; exit 1; }
[[ -f "$UNINSTALLER_EXE" ]] || { echo "RhythKit.Uninstaller.exe was not produced" >&2; exit 1; }

{
  printf '%s\n' 'namespace RhythKit.Installer;' '' 'internal static class RhythKitAgentPayload' '{' '    public static byte[] Data => Convert.FromBase64String("'
  base64 -w 0 "$AGENT_EXE"
  printf '%s\n' '");' '}'
} > "$AGENT_PAYLOAD"
{
  printf '%s\n' 'namespace RhythKit.Installer;' '' 'internal static class RhythKitUninstallerPayload' '{' '    public static byte[] Data => Convert.FromBase64String("'
  base64 -w 0 "$UNINSTALLER_EXE"
  printf '%s\n' '");' '}'
} > "$UNINSTALLER_PAYLOAD"

dotnet publish "$INSTALLER" -c Release -r win-x64 --self-contained true --no-restore "${PUBLISH[@]}" -o "$DIST"

INSTALLER_EXE="$DIST/RhythKitInstall.exe"
[[ -f "$INSTALLER_EXE" ]] || { echo "RhythKitInstall.exe was not produced" >&2; exit 1; }
SIZE="$(stat -c '%s' "$INSTALLER_EXE")"
(( SIZE >= 1000000 )) || { echo "RhythKitInstall.exe is unexpectedly small" >&2; exit 1; }

rm -rf "$WORK"
find "$DIST" -mindepth 1 -maxdepth 1 ! -name 'RhythKitInstall.exe' -exec rm -rf {} +

echo "BUILD SUCCESSFUL"
echo "Portable installer: $INSTALLER_EXE"
