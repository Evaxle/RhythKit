# RhythKit

RhythKit is the desktop connection layer between Rhythia, SSP Nightly, and Rhythians. It handles Rhythians device login, map identities, and eligible score submission.

## Supported game targets

- SSP Nightly
- Rhythia Steam
- Legacy Rhythia builds that contain `Rhythia.dll`

The Steam Rhythia build is the native Windows distribution. Its game directory does not contain `Rhythia.dll`; the supported layout is identified by `rhythia.exe`, `steam_api64.dll`, `rsign.dll`, `rosu_pp.dll`, and `e_sqlite3.dll`.

The installer detects the selected game folder and displays the detected target before installation.

## Build on Windows

Requirements:

- Windows 10 or newer
- .NET 10 SDK
- PowerShell

From the repository root:

```powershell
.\build.ps1
```

The build produces:

```text
dist\win-x64\RhythKit.Installer.exe
dist\win-x64\RhythKit.Agent.exe
dist\win-x64\RhythKit.Uninstaller.exe
```

Use the three executables from the same build output. The managed `RhythKit.dll` payload is only installed for legacy managed Rhythia builds. Steam Rhythia is installed through the external RhythKit Agent and does not receive a fake `Rhythia.dll` patch.

## Install

1. Run `RhythKit.Installer.exe`.
2. Select the actual SSP Nightly, Rhythia Steam, or legacy Rhythia game directory.
3. Confirm that the installer says `Detected: SSP Nightly`, `Detected: Rhythia Steam`, or `Detected: legacy Rhythia`.
4. Click `Install RhythKit`.
5. The installer validates the target layout and creates the `RhythKit` integration directory.
6. The installer registers the RhythKit agent to start with Windows and starts it immediately.
7. Close the installer.

For Steam Rhythia, the installer does not modify `rhythia.exe`, `steam_api64.dll`, `rsign.dll`, `rosu_pp.dll`, or any other original game dependency.

## First game launch connection

When the installed game is started for the first time while RhythKit is not authenticated:

```text
Game starts
    ↓
RhythKit Agent detects rhythia.exe
    ↓
Rhythians device authorization is created
    ↓
Rhythians authorization page opens in the browser
    ↓
User signs in
    ↓
User clicks "Confirm login for RhythKit"
    ↓
RhythKit receives the installation token
    ↓
Windows popup: "Rhythians is connected"
```

The user can then return to Rhythia and continue playing.

The confirmation page must be explicitly approved by the signed-in user. Visiting the URL alone does not authorize the device.

## Local agent

RhythKit Agent listens only on:

```text
http://127.0.0.1:45872
```

The `/status` endpoint reports whether the agent is running, authenticated, and which game target is installed. The Rhythians Settings page uses this endpoint to display the RhythKit connection state.

The agent stores the Rhythians installation token at:

```text
%APPDATA%\Rhythians\rhythkit.json
```

The token is only used by the agent for authenticated Rhythians API requests.

## Score flow

For targets with a native or managed game-side score integration, RhythKit identifies the played map, resolves the Rhythians map ID, verifies eligibility, and submits the completed score through the Rhythians API.

The current Steam Rhythia executable is a native distribution and does not expose the managed `Rhythia.dll` entry point used by the legacy patcher. The installer therefore does not pretend to inject the managed payload into Steam Rhythia. Steam score capture requires a game-side native integration or a documented replay/score data interface for the exact Steam build.

## Rebuilding after source changes

Always rebuild after changing RhythKit source code:

```powershell
.\build.ps1
```

Do not mix `RhythKit.Installer.exe`, `RhythKit.Agent.exe`, and `RhythKit.Uninstaller.exe` from different builds.

## Testing

Test in this order:

1. SSP Nightly
2. Rhythia Steam
3. Legacy Rhythia

For each target:

1. Install RhythKit.
2. Start the game.
3. Verify the Rhythians authorization page opens when the installation is not authenticated.
4. Sign in and click `Confirm login for RhythKit`.
5. Verify the `Rhythians is connected` Windows popup.
6. Open Rhythians Settings and verify `RhythKit: Connected`.
7. For a target with score integration, play a map that exists on Rhythians.
8. Complete the map.
9. Verify the completion appears in the Rhythians account.

## Projects

- `src/RhythKit` — Rhythians API client, map identity, and managed game integration
- `src/RhythKit.Agent` — local HTTP agent, game detection, authorization polling, and SSP score watcher
- `src/RhythKit.Installer` — Windows installer, Steam layout validation, and game detection
