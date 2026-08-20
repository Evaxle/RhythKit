# RhythKit

RhythKit is the C# connection layer between Rhythia and Rhythians.

## Projects

- `src/RhythKit` contains the Rhythians device authorization, map identity, and score API client.
- `src/RhythKit.Installer` contains the Windows installer UI.

## Connection flow

1. RhythKit requests a short-lived device authorization from Rhythians.
2. RhythKit opens the Rhythians authorization page in the user's browser.
3. The user signs into an existing Rhythians account or creates one.
4. Rhythians authorizes the device and returns a one-time installation token to RhythKit.
5. RhythKit stores the installation token locally and uses it for score submissions.
6. RhythKit reads Rhythians map identities from RHM metadata or encrypted SSPM v2 custom data.
7. The Rhythians server validates the map and calculates the RHP award server-side.
8. Only records written to `RhythKitScore` appear in the profile's RhythKit recent scores section.

## Game integration

The installer deploys the RhythKit payload beside the Rhythia managed assembly and patches the Rhythia startup hook so the mod initializes when the game launches.
