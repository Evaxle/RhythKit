#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DIST="$ROOT/dist"
PUBLISH="$DIST/win-x64"
RHYTHKIT_PROJECT="$ROOT/src/RhythKit/RhythKit.csproj"
INSTALLER_PROJECT="$ROOT/src/RhythKit.Installer/RhythKit.Installer.csproj"
AGENT_PROJECT="$ROOT/src/RhythKit.Agent/RhythKit.Agent.csproj"
UNINSTALLER_PROJECT="$ROOT/src/RhythKit.Uninstaller/RhythKit.Uninstaller.csproj"
PAYLOAD_SOURCE="$ROOT/src/RhythKit.Installer/RhythKitPayload.cs"

command -v dotnet >/dev/null 2>&1 || { echo "dotnet is required" >&2; exit 1; }
command -v base64 >/dev/null 2>&1 || { echo "base64 is required" >&2; exit 1; }
command -v file >/dev/null 2>&1 || { echo "file is required" >&2; exit 1; }

dotnet --version

rm -rf "$DIST"
mkdir -p "$PUBLISH"

WIN_PROPS=("-p:EnableWindowsTargeting=true" "-p:RuntimeIdentifier=win-x64")

dotnet restore "$RHYTHKIT_PROJECT" "${WIN_PROPS[@]}"
dotnet restore "$INSTALLER_PROJECT" "${WIN_PROPS[@]}"
dotnet restore "$AGENT_PROJECT" "${WIN_PROPS[@]}"
dotnet restore "$UNINSTALLER_PROJECT" "${WIN_PROPS[@]}"

dotnet build "$RHYTHKIT_PROJECT" -c Release --no-restore "${WIN_PROPS[@]}"

RHYTHKIT_OUTPUT="$(find "$ROOT/src/RhythKit" -type f -name 'RhythKit.dll' -path '*/Release/*' -print | head -n 1)"
if [[ -z "$RHYTHKIT_OUTPUT" || ! -f "$RHYTHKIT_OUTPUT" ]]; then
  echo "RhythKit.dll was not produced by the Release build" >&2
  find "$ROOT/src/RhythKit" -type f -name 'RhythKit.dll' -print 2>/dev/null || true
  exit 1
fi

echo "Using RhythKit payload: $RHYTHKIT_OUTPUT"
BASE64="$(base64 -w 0 "$RHYTHKIT_OUTPUT")"
printf 'namespace RhythKit.Installer;\n\ninternal static class RhythKitPayload\n{\n    public static byte[] Data => Convert.FromBase64String("%s");\n}\n' "$BASE64" > "$PAYLOAD_SOURCE"

dotnet publish "$INSTALLER_PROJECT" -c Release -r win-x64 --self-contained true -p:EnableWindowsTargeting=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$PUBLISH"
dotnet publish "$AGENT_PROJECT" -c Release -r win-x64 --self-contained true -p:EnableWindowsTargeting=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$PUBLISH/agent"
dotnet publish "$UNINSTALLER_PROJECT" -c Release -r win-x64 --self-contained true -p:EnableWindowsTargeting=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$PUBLISH/uninstaller"

cp "$PUBLISH/agent/RhythKit.Agent.exe" "$PUBLISH/RhythKit.Agent.exe"
cp "$PUBLISH/uninstaller/RhythKit.Uninstaller.exe" "$PUBLISH/RhythKit.Uninstaller.exe"
rm -rf "$PUBLISH/agent" "$PUBLISH/uninstaller"

OUTPUTS=(
  "$PUBLISH/RhythKit.Installer.exe"
  "$PUBLISH/RhythKit.Agent.exe"
  "$PUBLISH/RhythKit.Uninstaller.exe"
)

for output in "${OUTPUTS[@]}"; do
  [[ -f "$output" ]] || { echo "Expected executable was not produced: $output" >&2; exit 1; }
  size=$(stat -c%s "$output")
  (( size >= 1000000 )) || { echo "Executable is unexpectedly small: $output" >&2; exit 1; }
  file "$output" | grep -Eq 'PE32|MS Windows' || { echo "Output is not a Windows executable: $output" >&2; exit 1; }
done

printf 'Built Windows executables:\n'
printf '%s\n' "${OUTPUTS[@]}"
