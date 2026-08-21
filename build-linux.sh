#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PUBLISH="$ROOT/dist/win-x64"
RHYTHKIT_PROJECT="$ROOT/src/RhythKit/RhythKit.csproj"
INSTALLER_PROJECT="$ROOT/src/RhythKit.Installer/RhythKit.Installer.csproj"
AGENT_PROJECT="$ROOT/src/RhythKit.Agent/RhythKit.Agent.csproj"
UNINSTALLER_PROJECT="$ROOT/src/RhythKit.Uninstaller/RhythKit.Uninstaller.csproj"
RHYTHKIT_OUTPUT="$ROOT/src/RhythKit/.godot/mono/temp/bin/Release/RhythKit.dll"
PAYLOAD_SOURCE="$ROOT/src/RhythKit.Installer/RhythKitPayload.cs"

command -v dotnet >/dev/null 2>&1 || { echo "dotnet is required" >&2; exit 1; }
command -v base64 >/dev/null 2>&1 || { echo "base64 is required" >&2; exit 1; }

rm -rf "$ROOT/dist"
mkdir -p "$PUBLISH"

dotnet restore "$RHYTHKIT_PROJECT"
dotnet restore "$INSTALLER_PROJECT" -p:EnableWindowsTargeting=true
dotnet restore "$AGENT_PROJECT" -p:EnableWindowsTargeting=true
dotnet restore "$UNINSTALLER_PROJECT" -p:EnableWindowsTargeting=true

dotnet build "$RHYTHKIT_PROJECT" -c Release --no-restore

if [[ ! -f "$RHYTHKIT_OUTPUT" ]]; then
  echo "RhythKit.dll was not produced at $RHYTHKIT_OUTPUT" >&2
  exit 1
fi

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
  file "$output" | grep -q 'PE32\|MS Windows' || { echo "Output is not a Windows executable: $output" >&2; exit 1; }
done

printf 'Built Windows executables:\n'
printf '%s\n' "${OUTPUTS[@]}"
