#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DIST="$ROOT/dist/win-x64"
RHYTHKIT="$ROOT/src/RhythKit/RhythKit.csproj"
INSTALLER="$ROOT/src/RhythKit.Installer/RhythKit.Installer.csproj"
AGENT="$ROOT/src/RhythKit.Agent/RhythKit.Agent.csproj"
UNINSTALLER="$ROOT/src/RhythKit.Uninstaller/RhythKit.Uninstaller.csproj"
PAYLOAD="$ROOT/src/RhythKit.Installer/RhythKitPayload.cs"
PAYLOAD_BACKUP="$(mktemp)"

cleanup() {
  if [[ -f "$PAYLOAD_BACKUP" ]]; then
    cp "$PAYLOAD_BACKUP" "$PAYLOAD"
    rm -f "$PAYLOAD_BACKUP"
  fi
}
trap cleanup EXIT

command -v dotnet >/dev/null 2>&1 || { echo "dotnet 10 SDK is required" >&2; exit 1; }
command -v base64 >/dev/null 2>&1 || { echo "base64 is required" >&2; exit 1; }
command -v file >/dev/null 2>&1 || { echo "file is required" >&2; exit 1; }

dotnet --list-sdks | grep -Eq '^10\.' || { echo "dotnet 10 SDK is required" >&2; exit 1; }

cp "$PAYLOAD" "$PAYLOAD_BACKUP"

rm -rf "$ROOT/dist"
rm -rf "$ROOT/src/RhythKit/bin" "$ROOT/src/RhythKit/obj"
rm -rf "$ROOT/src/RhythKit.Installer/bin" "$ROOT/src/RhythKit.Installer/obj"
rm -rf "$ROOT/src/RhythKit.Agent/bin" "$ROOT/src/RhythKit.Agent/obj"
rm -rf "$ROOT/src/RhythKit.Uninstaller/bin" "$ROOT/src/RhythKit.Uninstaller/obj"
mkdir -p "$DIST"

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
echo "==> Restoring RhythKit"
dotnet restore "$RHYTHKIT" "${COMMON_PROPS[@]}"

echo "==> Restoring Windows applications"
dotnet restore "$INSTALLER" -r win-x64 "${COMMON_PROPS[@]}"
dotnet restore "$AGENT" -r win-x64 "${COMMON_PROPS[@]}"
dotnet restore "$UNINSTALLER" -r win-x64 "${COMMON_PROPS[@]}"

echo "==> Building RhythKit payload"
dotnet build "$RHYTHKIT" -c Release --no-restore "${COMMON_PROPS[@]}"

RHYTHKIT_DLL="$(find "$ROOT/src/RhythKit" -type f -name 'RhythKit.dll' -path '*/Release/*' -print -quit 2>/dev/null || true)"

if [[ -z "$RHYTHKIT_DLL" ]]; then
  echo "RhythKit.dll was not produced by the Release build" >&2
  find "$ROOT/src/RhythKit" -type f -name 'RhythKit.dll' -print 2>/dev/null || true
  exit 1
fi

echo "==> Embedding $RHYTHKIT_DLL"
printf '%s\n' 'namespace RhythKit.Installer;' '' 'internal static class RhythKitPayload' '{' '    public static byte[] Data => Convert.FromBase64String("' > "$PAYLOAD"
base64 "$RHYTHKIT_DLL" | tr -d '\n' >> "$PAYLOAD"
printf '%s\n' '");' '}' >> "$PAYLOAD"

echo "==> Publishing Installer"
dotnet publish "$INSTALLER" -c Release -r win-x64 --self-contained true --no-restore "${PUBLISH_PROPS[@]}" -o "$DIST"

echo "==> Publishing Agent"
dotnet publish "$AGENT" -c Release -r win-x64 --self-contained true --no-restore "${PUBLISH_PROPS[@]}" -o "$DIST"

echo "==> Publishing Uninstaller"
dotnet publish "$UNINSTALLER" -c Release -r win-x64 --self-contained true --no-restore "${PUBLISH_PROPS[@]}" -o "$DIST"

EXPECTED=(
  "$DIST/RhythKit.Installer.exe"
  "$DIST/RhythKit.Agent.exe"
  "$DIST/RhythKit.Uninstaller.exe"
)

for exe in "${EXPECTED[@]}"; do
  [[ -f "$exe" ]] || { echo "Missing executable: $exe" >&2; exit 1; }
  size="$(stat -c '%s' "$exe")"
  (( size > 1000000 )) || { echo "Executable is unexpectedly small: $exe" >&2; exit 1; }
  file "$exe" | grep -Eq 'PE32|PE32\+|MS Windows' || { echo "Not a Windows executable: $exe" >&2; exit 1; }
done

find "$DIST" -mindepth 1 -maxdepth 1 \
  ! -name 'RhythKit.Installer.exe' \
  ! -name 'RhythKit.Agent.exe' \
  ! -name 'RhythKit.Uninstaller.exe' \
  -exec rm -rf {} +

echo
echo "BUILD SUCCESSFUL"
ls -lh "${EXPECTED[@]}"
