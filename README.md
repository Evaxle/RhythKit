# RhythKit

RhythKit is the C# connection layer between Rhythia and Rhythians.

## Projects

- `src/RhythKit` contains the Rhythians device authorization and score API client.
- `src/RhythKit.Installer` contains the Windows installer UI.

## Connection flow

1. RhythKit requests a short-lived device authorization from Rhythians.
2. RhythKit opens the Rhythians authorization page in the user's browser.
3. The user signs into an existing Rhythians account or creates one.
4. Rhythians authorizes the device and returns a one-time installation token to RhythKit.
5. RhythKit stores the installation token locally and uses it for score submissions.
6. The Rhythians server validates the map and calculates the RHP award server-side.
7. Only records written to `RhythKitScore` appear in the profile's RhythKit recent scores section.

## Game integration

The installer currently lays down the RhythKit payload and manifest in the selected Rhythia directory. The actual in-game loader/main-menu hook is isolated from the installer so it can target the exact Rhythia runtime and loading mechanism without modifying arbitrary game binaries.
