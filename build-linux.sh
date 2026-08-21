#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DIST="$ROOT/dist/win-x64"
WORK="$ROOT/dist/.build"
RHYTHKIT="$ROOT/src/RhythKit/RhythKit.csproj"
INSTALLER="$ROOT/src/RhythKit.Installer/RhythKit.Installer.csproj"
AGENT="$ROOT/src/RhythKit.Agent/RhythKit.Agent.csproj"
UNINSTALLER="$ROOT/src/RhythKit.Uninstaller/RhythKit.Uninstaller.csproj"
PAYLOAD_DIR="$ROOT/src/RhythKit.Installer/Payloads"
INSTALLER_EXE="$DIST/RhythKitInstall.exe"

step() {
  printf '\n[RhythKit Build] %s\n' "$1"
}

fail() {
  printf '\n[RhythKit Build] BUILD FAILED\n'
  printf '[RhythKit Build] %s\n' "$1" >&2
  exit 1
}

cleanup() {
  rm -rf "$PAYLOAD_DIR" "$WORK"
}

on_error() {
  printf '\n[RhythKit Build] BUILD FAILED\n' >&2
}

trap cleanup EXIT
trap on_error ERR

printf '\n========================================\n'
printf '        RhythKit Windows Builder\n'
printf '        Linux -> Windows x64\n'
printf '========================================\n'

step "Checking build dependencies"
command -v dotnet >/dev/null 2>&1 || fail "dotnet was not found. Install the .NET 10 SDK."
command -v file >/dev/null 2>&1 || fail "file was not found. Install the file package."
dotnet --list-sdks | grep -Eq '^10\.' || fail ".NET 10 SDK is required."
printf 'Using .NET %s\n' "$(dotnet --version)"

step "Cleaning previous build output"
rm -rf "$ROOT/dist"
rm -rf "$ROOT/src/RhythKit/bin" "$ROOT/src/RhythKit/obj"
rm -rf "$ROOT/src/RhythKit.Installer/bin" "$ROOT/src/RhythKit.Installer/obj"
rm -rf "$ROOT/src/RhythKit.Agent/bin" "$ROOT/src/RhythKit.Agent/obj"
rm -rf "$ROOT/src/RhythKit.Uninstaller/bin" "$ROOT/src/RhythKit.Uninstaller/obj"
mkdir -p "$DIST" "$WORK/agent" "$WORK/uninstaller" "$PAYLOAD_DIR"

COMMON=(
  "-p:EnableWindowsTargeting=true"
  "-p:RestoreEnablePackagePruning=false"
  "-p:DebugType=None"
  "-p:DebugSymbols=false"
)

PUBLISH=(
  "-p:PublishSingleFile=true"
  "-p:IncludeNativeLibrariesForSelfExtract=true"
  "-p:EnableWindowsTargeting=true"
  "-p:RestoreEnablePackagePruning=false"
  "-p:DebugType=None"
  "-p:DebugSymbols=false"
)

step "Restoring and building RhythKit"
dotnet restore "$RHYTHKIT" "${COMMON[@]}"
dotnet build "$RHYTHKIT" -c Release --no-restore "${COMMON[@]}"

RHYTHKIT_DLL="$(find "$ROOT/src/RhythKit" -type f -name 'RhythKit.dll' -path '*/bin/Release/*' -print -quit 2>/dev/null || true)"
[[ -n "$RHYTHKIT_DLL" ]] || fail "RhythKit.dll was not produced."
cp "$RHYTHKIT_DLL" "$PAYLOAD_DIR/RhythKit.dll"
printf 'RhythKit payload: %s\n' "$RHYTHKIT_DLL"

step "Restoring Windows x64 components"
dotnet restore "$AGENT" -r win-x64 "${COMMON[@]}"
dotnet restore "$UNINSTALLER" -r win-x64 "${COMMON[@]}"
dotnet restore "$INSTALLER" -r win-x64 "${COMMON[@]}"

step "Publishing RhythKit Agent"
dotnet publish "$AGENT" -c Release -r win-x64 --self-contained true --no-restore "${PUBLISH[@]}" -o "$WORK/agent"
AGENT_EXE="$WORK/agent/RhythKit.Agent.exe"
[[ -f "$AGENT_EXE" ]] || fail "RhythKit.Agent.exe was not produced."
cp "$AGENT_EXE" "$PAYLOAD_DIR/RhythKit.Agent.exe"
printf 'Agent payload ready.\n'

step "Publishing RhythKit Uninstaller"
dotnet publish "$UNINSTALLER" -c Release -r win-x64 --self-contained true --no-restore "${PUBLISH[@]}" -o "$WORK/uninstaller"
UNINSTALLER_EXE="$WORK/uninstaller/RhythKit.Uninstaller.exe"
[[ -f "$UNINSTALLER_EXE" ]] || fail "RhythKit.Uninstaller.exe was not produced."
cp "$UNINSTALLER_EXE" "$PAYLOAD_DIR/RhythKit.Uninstaller.exe"
printf 'Uninstaller payload ready.\n'

step "Building the Windows installer"
dotnet publish "$INSTALLER" -c Release -r win-x64 --self-contained true --no-restore "${PUBLISH[@]}" -o "$DIST"
[[ -f "$INSTALLER_EXE" ]] || fail "RhythKitInstall.exe was not produced."

step "Validating the Windows installer"
SIZE="$(stat -c '%s' "$INSTALLER_EXE")"
(( SIZE >= 1000000 )) || fail "RhythKitInstall.exe is unexpectedly small ($SIZE bytes)."
file "$INSTALLER_EXE" | grep -Eq 'PE32|PE32\+|MS Windows' || fail "RhythKitInstall.exe is not a Windows executable."
printf 'Installer size: %s bytes\n' "$SIZE"
printf 'Windows executable validation passed.\n'

rm -rf "$WORK"
find "$DIST" -mindepth 1 -maxdepth 1 ! -name 'RhythKitInstall.exe' -exec rm -rf {} +

printf '\n========================================\n'
printf '        BUILD COMPLETE\n'
printf '========================================\n'
printf 'Portable installer:\n%s\n' "$INSTALLER_EXE"
printf '\nThe installer is ready to copy to Windows.\n\n'
