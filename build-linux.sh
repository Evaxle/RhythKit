#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DIST="$ROOT/dist/win-x64"
RHYTHKIT="$ROOT/src/RhythKit/RhythKit.csproj"
INSTALLER="$ROOT/src/RhythKit.Installer/RhythKit.Installer.csproj"
AGENT="$ROOT/src/RhythKit.Agent/RhythKit.Agent.csproj"
UNINSTALLER="$ROOT/src/RhythKit.Uninstaller/RhythKit.Uninstaller.csproj"
PAYLOAD="$ROOT/src/RhythKit.Installer/RhythKitPayload.cs"
AGENT_PAYLOAD="$ROOT/src/RhythKit.Installer/RhythKitAgentPayload.cs"
UNINSTALLER_PAYLOAD="$ROOT/src/RhythKit.Installer/RhythKitUninstallerPayload.cs"
BACKUP_DIR="$(mktemp -d)"

cleanup() {
  cp "$BACKUP_DIR/RhythKitPayload.cs" "$PAYLOAD"
  cp "$BACKUP_DIR/RhythKitAgentPayload.cs" "$AGENT_PAYLOAD"
  cp "$BACKUP_DIR/RhythKitUninstallerPayload.cs" "$UNINSTALLER_PAYLOAD"
  rm -rf "$BACKUP_DIR"
}
trap cleanup EXIT

command -v dotnet >/dev/null 2>&1 || { echo "dotnet 10 SDK is required" >&2; exit 1; }
command -v base64 >/dev/null 2>&1 || { echo "base64 is required" >&2; exit 1; }
command -v file >/dev/null 2>&1 || { echo "file is required" >&2; exit 1; }
dotnet --list-sdks | grep -Eq '^10\.' || { echo "dotnet 10 SDK is required" >&2; exit 1; }

cp "$PAYLOAD" "$BACKUP_DIR/RhythKitPayload.cs"
cp "$AGENT_PAYLOAD" "$BACKUP_DIR/RhythKitAgentPayload.cs"
cp "$UNINSTALLER_PAYLOAD" "$BACKUP_DIR/RhythKitUninstallerPayload.cs"

rm -rf "$ROOT/dist"
rm -rf "$ROOT/src/RhythKit/bin" "$ROOT/src/RhythKit/obj"
rm -rf "$ROOT/src/RhythKit.Installer/bin" "$ROOT/src/RhythKit.Installer/obj"
rm -rf "$ROOT/src/RhythKit.Agent/bin" "$ROOT/src/RhythKit.Agent/obj"
rm -rf "$ROOT/src/RhythKit.Uninstaller/bin" "$ROOT/src/RhythKit.Uninstaller/obj"
mkdir -p "$DIST" "$DIST/agent" "$DIST/uninstaller"

COMMON_PROPS=(
  "-p:EnableWindowsTargeting=true"
  "-p:AllowMissingPrunePackageData=true"
  "-p:EnablePackagePruning=false"
  "-p:DebugType=None"
  "-p:DebugSymbols=false"
)

PUBLISH_PROPS=(
  "-p:PublishSingleFile=true"
  "-p:IncludeNativeLibrariesForSelfExtract=true"
  "-p:EnableWindowsTargeting=true"
  "-p:AllowMissingPrunePackageData=true"
  "-p:EnablePackagePruning=false"
  "-p:DebugType=None"
  "-p:DebugSymbols=false"
)

echo "==> .NET $(dotnet --version)"
echo "==> Restoring projects"
dotnet restore "$RHYTHKIT" "${COMMON_PROPS[@]}"
dotnet restore "$INSTALLER" -r win-x64 "${COMMON_PROPS[@]}"
dotnet restore "$AGENT" -r win-x64 "${COMMON_PROPS[@]}"
dotnet restore "$UNINSTALLER" -r win-x64 "${COMMON_PROPS[@]}"

echo "==> Building RhythKit payload"
dotnet build "$RHYTHKIT" -c Release --no-restore "${COMMON_PROPS[@]}"

RHYTHKIT_DLL="$(find "$ROOT/src/RhythKit" -type f -name 'RhythKit.dll' -path '*/Release/*' -print -quit 2>/dev/null || true)"
[[ -n "$RHYTHKIT_DLL" ]] || { echo "RhythKit.dll was not produced" >&2; exit 1; }

printf '%s\n' 'namespace RhythKit.Installer;' '' 'internal static class RhythKitPayload' '{' '    public static byte[] Data => Convert.FromBase64String("' > "$PAYLOAD"
base64 -w 0 "$RHYTHKIT_DLL" >> "$PAYLOAD"
printf '%s\n' '");' '}' >> "$PAYLOAD"

echo "==> Publishing Agent"
dotnet publish "$AGENT" -c Release -r win-x64 --self-contained true --no-restore "${PUBLISH_PROPS[@]}" -o "$DIST/agent"

echo "==> Publishing Uninstaller"
dotnet publish "$UNINSTALLER" -c Release -r win-x64 --self-contained true --no-restore "${PUBLISH_PROPS[@]}" -o "$DIST/uninstaller"

AGENT_EXE="$DIST/agent/RhythKit.Agent.exe"
UNINSTALLER_EXE="$DIST/uninstaller/RhythKit.Uninstaller.exe"
[[ -f "$AGENT_EXE" ]] || { echo "RhythKit.Agent.exe was not produced" >&2; exit 1; }
[[ -f "$UNINSTALLER_EXE" ]] || { echo "RhythKit.Uninstaller.exe was not produced" >&2; exit 1; }

printf '%s\n' 'namespace RhythKit.Installer;' '' 'internal static class RhythKitAgentPayload' '{' '    public static byte[] Data => Convert.FromBase64String("' > "$AGENT_PAYLOAD"
base64 -w 0 "$AGENT_EXE" >> "$AGENT_PAYLOAD"
printf '%s\n' '");' '}' >> "$AGENT_PAYLOAD"
printf '%s\n' 'namespace RhythKit.Installer;' '' 'internal static class RhythKitUninstallerPayload' '{' '    public static byte[] Data => Convert.FromBase64String("' > "$UNINSTALLER_PAYLOAD"
base64 -w 0 "$UNINSTALLER_EXE" >> "$UNINSTALLER_PAYLOAD"
printf '%s\n' '");' '}' >> "$UNINSTALLER_PAYLOAD"

echo "==> Publishing single installer"
dotnet publish "$INSTALLER" -c Release -r win-x64 --self-contained true --no-restore "${PUBLISH_PROPS[@]}" -o "$DIST"

INSTALLER_EXE="$DIST/RhythKit.Installer.exe"
[[ -f "$INSTALLER_EXE" ]] || { echo "RhythKit.Installer.exe was not produced" >&2; exit 1; }

size="$(stat -c '%s' "$INSTALLER_EXE")"
(( size > 1000000 )) || { echo "Installer is unexpectedly small" >&2; exit 1; }
file "$INSTALLER_EXE" | grep -Eq 'PE32|PE32\+|MS Windows' || { echo "Installer is not a Windows executable" >&2; exit 1; }

rm -rf "$DIST/agent" "$DIST/uninstaller"
find "$DIST" -mindepth 1 -maxdepth 1 \
  ! -name 'RhythKit.Installer.exe' \
  -exec rm -rf {} +

echo
echo "BUILD SUCCESSFUL"
ls -lh "$INSTALLER_EXE"
