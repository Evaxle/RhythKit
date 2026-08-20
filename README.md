# RhythKit

RhythKit connects Rhythia to Rhythians. It handles login, map IDs, and sending ranked completions to Rhythians.

## Build the installer

Requirements:

- Windows
- .NET 10 SDK
- A working Rhythia/RhythKit source checkout

From the repository folder, run:

```powershell
.\build.ps1
```

The installer is created at:

```text
dist\win-x64\RhythKit.Installer.exe
```

Give the user that `.exe`. They select their Rhythia game folder and the installer installs RhythKit there.

## What it does

1. The user runs the installer.
2. They select the Rhythia folder.
3. RhythKit is installed and Rhythia is patched to load it.
4. Rhythia shows the `Rhythian Login` button.
5. The user signs into Rhythians in their browser.
6. RhythKit saves the connection for that installation.
7. Completed Rhythians maps are checked against the Rhythians database.
8. Valid completions are sent to Rhythians and RHP is calculated by the server.

## Projects

- `src/RhythKit` — game integration and Rhythians API client
- `src/RhythKit.Installer` — Windows installer

## Notes

The installer must be rebuilt with `build.ps1` whenever the RhythKit payload changes.
