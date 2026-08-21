# RhythKit

RhythKit connects Rhythia with Rhythians.

## Build on Windows

```powershell
.\build.ps1
```

## Build from Linux

```bash
chmod +x ./build-linux.sh
./build-linux.sh
```

The Linux build creates:

```text
dist/win-x64/RhythKitInstall.exe
```

The installer is for Windows.

## Install

Run `RhythKitInstall.exe` and choose your Rhythia folder.

## Projects

- `src/RhythKit` — game integration
- `src/RhythKit.Agent` — local agent
- `src/RhythKit.Installer` — installer
