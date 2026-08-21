#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DIST="$ROOT/dist/win-x64"
RHYTHKIT_PROJECT="$ROOT/src/RhythKit/RhythKit.csproj"
INSTALLER_PROJECT="$ROOT/src/RhythKit.Installer/RhythKit.Installer.csproj"
AGENT_PROJECT="$ROOT/src/RhythKit.Agent/RhythKit.Agent.csproj"
UNINSTALLER_PROJECT="$ROOT/src/RhythKit.Uninstaller/RhythKit.Uninstaller.csproj"
PAYLOAD_SOURCE="$ROOT/src/RhythKit.Installer/RhythKitPayload.cs"

command -v dotnet >/dev/null 2>&1 || { echo "dotnet is required" >&2; exit 1; }
command -v base64 >/dev/null 2>&1 || { echo "base64 is required" >&2; exit 1; }
command -v file >/dev/null 2>&1 || { echo "file is required" >&2; exit 1; }

rm -rf "$ROOT/dist"
rm -rf "$ROOT/src/RhythKit/bin" "$ROOT/src/RhythKit/obj"
rm -rf "$ROOT/src/RhythKit.Installer/bin" "$ROOT/src/RhythKit.Installer/obj"
rm -rf "$ROOT/src/RhythKit.Agent/bin" "$ROOT/src/RhythKit.Agent/obj"
rm -rf "$ROOT/src/RhythKit.Uninstaller/bin" "$ROOT/src/RhythKit.Uninstaller/obj"
mkdir -p "$DIST"

COMMON=(
  "-p:EnableWindowsTargeting=true"
  "-p:AllowMissingPrunePackageData=true"
  "-p:EnablePackagePruning=false"
  "-p:DebugType=None"
  "-p:DebugSymbols=false"
)

printf '%s\n' '==> Restoring RhythKit'
dotnet restore "$RHYTHKIT_PROJECT" "${COMMON[@]}"
printf '%s\n' '==> Restoring Installer'
dotnet restore "$INSTALLER_PROJECT" -r win-x64 "${COMMON[@]}"
printf '%s\n' '==> Restoring Agent'
dotnet restore "$AGENT_PROJECT" -r win-x64 "${COMMON[@]}"
printf '%s\n' '==> Restoring Uninstaller'
dotnet restore "$UNINSTALLER_PROJECT" -r win-x64 "${COMMON[@]}"

printf '%s\n' '==> Building RhythKit payload'
dotnet build "$RHYTHKIT_PROJECT" -c Release --no-restore "${COMMON[@]}"

RHYTHKIT_OUTPUT="$ROOT/src/RhythKit/.godot/mono/temp/bin/Release/RhythKit.dll"
if [[ ! -f "$RHYTHKIT_OUTPUT" ]]; then
  RHYTHKIT_OUTPUT="$(find "$ROOT/src/RhythKit/.godot" "$ROOT/src/RhythKit/bin" -type f -name 'RhythKit.dll' -path '*/Release/*' -print -quit 2>/dev/null || true)"
fi

if [[ -z "$RHYTHKIT_OUTPUT" || ! -f "$RHYTHKIT_OUTPUT" ]]; then
  echo 'RhythKit.dll was not produced' >&2
  exit 1
fi

printf '%s\n' '==> Embedding RhythKit payload'
BASE64="$(base64 -w 0 "$RHYTHKIT_OUTPUT")"
printf 'namespace RhythKit.Installer;\n\ninternal static class RhythKitPayload\n{\n    public static byte[] Data => Convert.FromBase64String("%s");\n}\n' "$BASE64" > "$PAYLOAD_SOURCE"

PUBLISH=(
  -c Release
  -r win-x64
  --self-contained true
  --no-restore
  -p:EnableWindowsTargeting=true
  -p:AllowMissingPrunePackageData=true
  -p:EnablePackagePruning=false
  -p:DebugType=None
  -p:DebugSymbols=false
  -p:PublishSingleFile=true
  -p:IncludeNativeLibrariesForSelfExtract=true
  -p:IncludeAllContentForSelfExtract=true
)

printf '%s\n' '==> Publishing Installer'
dotnet publish "$INSTALLER_PROJECT" "${PUBLISH[@]}" -o "$DIST/installer"
printf '%s\n' '==> Publishing Agent'
dotnet publish "$AGENT_PROJECT" "${PUBLISH[@]}" -o "$DIST/agent"
printf '%s\n' '==> Publishing Uninstaller'
dotnet publish "$UNINSTALLER_PROJECT" "${PUBLISH[@]}" -o "$DIST/uninstaller"

cp "$DIST/installer/RhythKit.Installer.exe" "$DIST/RhythKit.Installer.exe"
cp "$DIST/agent/RhythKit.Agent.exe" "$DIST/RhythKit.Agent.exe"
cp "$DIST/uninstaller/RhythKit.Uninstaller.exe" "$DIST/RhythKit.Uninstaller.exe"

rm -rf "$DIST/installer" "$DIST/agent" "$DIST/uninstaller"

OUTPUTS=(
  "$DIST/RhythKit.Installer.exe"
  "$DIST/RhythKit.Agent.exe"
  "$DIST/RhythKit.Uninstaller.exe"
)

for output in "${OUTPUTS[@]}"; do
  [[ -f "$output" ]] || { echo "Missing executable: $output" >&2; exit 1; }
  size=$(stat -c%s "$output")
  (( size >= 1000000 )) || { echo "Executable is unexpectedly small: $output" >&2; exit 1; }
  file "$output" | grep -Eq 'PE32|MS Windows' || { echo "Not a Windows executable: $output" >&2; exit 1; }
done

find "$DIST" -mindepth 1 -maxdepth 1 ! -name 'RhythKit.Installer.exe' ! -name 'RhythKit.Agent.exe' ! -name 'RhythKit.Uninstaller.exe' -exec rm -rf {} +

printf '%s\n' '' 'Built Windows executables:'
ls -lh "${OUTPUTS[@]}"
