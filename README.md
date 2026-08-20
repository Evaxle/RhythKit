# RhythKit

RhythKit is the C# connection layer between Rhythia and Rhythians.
RhythKit connects Rhythia to Rhythians. It handles login, map IDs, and sending ranked completions to Rhythians.

## Projects
## Build the installer

Requirements:

- Windows
- .NET 10 SDK
- A working Rhythia/RhythKit source checkout

From the repository folder, run:

```powershell
.\build.ps1
```

- `src/RhythKit` contains the Rhythians device authorization, map identity, and score API client.
- `src/RhythKit.Installer` contains the Windows installer UI.
The installer is created at:

## Connection flow
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

1. RhythKit requests a short-lived device authorization from Rhythians.
2. RhythKit opens the Rhythians authorization page in the user's browser.
3. The user signs into an existing Rhythians account or creates one.
4. Rhythians authorizes the device and returns a one-time installation token to RhythKit.
5. RhythKit stores the installation token locally and uses it for score submissions.
6. RhythKit reads Rhythians map identities from RHM metadata or encrypted SSPM v2 custom data.
7. The Rhythians server validates the map and calculates the RHP award server-side.
8. Only records written to `RhythKitScore` appear in the profile's RhythKit recent scores section.
- `src/RhythKit` — game integration and Rhythians API client
- `src/RhythKit.Installer` — Windows installer

## Game integration
## Notes

The installer deploys the RhythKit payload beside the Rhythia managed assembly and patches the Rhythia startup hook so the mod initializes when the game launches.
The installer must be rebuilt with `build.ps1` whenever the RhythKit payload changes.
