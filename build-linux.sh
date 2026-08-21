#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DIST="$ROOT/dist/win-x64"
RHYTHKIT="$ROOT/src/RhythKit/RhythKit.csproj"
INSTALLER="$ROOT/src/RhythKit.Installer/RhythKit.Installer.csproj"
AGENT="$ROOT/src/RhythKit.Agent/RhythKit.Agent.csproj"
UNINSTALLER="$ROOT/src/RhythKit.Uninstaller/RhythKit.Uninstaller.csproj"
PAYLOAD="$ROOT/src/RhythKit.Installer/RhythKitPayload.cs"

command -v dotnet >/dev/null 2>&1 || { echo "dotnet is required" >&2; exit 1; }
command -v base64 >/dev/null 2>&1 || { echo "base64 is required" >&2; exit 1; }
command -v file >/dev/null 2>&1 || { echo "file is required" >&2; exit 1; }

PROPS=(
  "-p:EnableWindowsTargeting=true"
  "-p:AllowMissingPrunePackageData=true"
  "-p:EnablePackagePruning=false"
  "-p:DebugType=None"
  "-p:DebugSymbols=false"
)

echo "==> .NET $(dotnet --version)"
echo "==> Preparing Agent project"

python3 - "$AGENT" <<'PY'
from pathlib import Path
import sys

path = Path(sys.argv[1])
text = path.read_text()

replacements = {
    '<EnableDefaultCompileItems>false</EnableDefaultCompileItems>': '<EnableDefaultCompileItems>false</EnableDefaultCompileItems>\n    <AllowMissingPrunePackageData>true</AllowMissingPrunePackageData>\n    <EnablePackagePruning>false</EnablePackagePruning>\n    <DebugType>None</DebugType>\n    <DebugSymbols>false</DebugSymbols>',
    '<Compile Include="Program.cs" />': '<Compile Include="Program.cs" />\n    <Compile Include="AgentSettings.cs" />\n    <Compile Include="TokenStore.cs" />'
}

for old, new in replacements.items():
    if old in text and new not in text:
        text = text.replace(old, new, 1)

path.write_text(text)
PY

rm -rf "$ROOT/dist"
rm -rf "$ROOT/src/RhythKit/bin" "$ROOT/src/RhythKit/obj"
rm -rf "$ROOT/src/RhythKit.Installer/bin" "$ROOT/src/RhythKit.Installer/obj"
rm -rf "$ROOT/src/RhythKit.Agent/bin" "$ROOT/src/RhythKit.Agent/obj"
rm -rf "$ROOT/src/RhythKit.Uninstaller/bin" "$ROOT/src/RhythKit.Uninstaller/obj"
mkdir -p "$DIST"

echo "==> Restoring"
dotnet restore "$RHYTHKIT" "${PROPS[@]}"
dotnet restore "$INSTALLER" -r win-x64 "${PROPS[@]}"
dotnet restore "$AGENT" -r win-x64 "${PROPS[@]}"
dotnet restore "$UNINSTALLER" -r win-x64 "${PROPS[@]}"

echo "==> Building RhythKit"
dotnet build "$RHYTHKIT" -c Release --no-restore "${PROPS[@]}"

RHYTHKIT_DLL="$ROOT/src/RhythKit/.godot/mono/temp/bin/Release/RhythKit.dll"

if [[ ! -f "$RHYTHKIT_DLL" ]]; then
  RHYTHKIT_DLL="$(find "$ROOT/src/RhythKit" -type f -name RhythKit.dll -path '*/Release/*' -print -quit 2>/dev/null || true)"
fi

[[ -f "$RHYTHKIT_DLL" ]] || { echo "RhythKit.dll was not produced" >&2; exit 1; }

echo "==> Embedding RhythKit payload"
BASE64_DATA="$(base64 -w0 "$RHYTHKIT_DLL")"

printf 'namespace RhythKit.Installer;\n\ninternal static class RhythKitPayload\n{\n    public static byte[] Data => Convert.FromBase64String("%s");\n}\n' "$BASE64_DATA" > "$PAYLOAD"

echo "==> Publishing Installer"
dotnet publish "$INSTALLER" -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true "${PROPS[@]}" -o "$DIST"

echo "==> Publishing Agent"
dotnet publish "$AGENT" -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true "${PROPS[@]}" -o "$DIST"

echo "==> Publishing Uninstaller"
dotnet publish "$UNINSTALLER" -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true "${PROPS[@]}" -o "$DIST"

for exe in \
  "$DIST/RhythKit.Installer.exe" \
  "$DIST/RhythKit.Agent.exe" \
  "$DIST/RhythKit.Uninstaller.exe"; do
  [[ -f "$exe" ]] || { echo "Missing executable: $exe" >&2; exit 1; }
  file "$exe" | grep -Eq 'PE32|MS Windows' || { echo "Not a Windows executable: $exe" >&2; exit 1; }
done

find "$DIST" -mindepth 1 -maxdepth 1 \
  ! -name 'RhythKit.Installer.exe' \
  ! -name 'RhythKit.Agent.exe' \
  ! -name 'RhythKit.Uninstaller.exe' \
  -exec rm -rf {} +

echo
echo "BUILD SUCCESSFUL"
ls -lh "$DIST/RhythKit.Installer.exe" "$DIST/RhythKit.Agent.exe" "$DIST/RhythKit.Uninstaller.exe"
