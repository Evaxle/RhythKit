# RhythKit

RhythKit is the desktop connection layer between Rhythia, SSP Nightly, and Rhythians. It handles Rhythians device login, map identities, and eligible score submission.

## Supported game targets

- SSP Nightly
- Rhythia Steam
- Legacy Rhythia builds that contain `Rhythia.dll`

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
```

Give the user `RhythKit.Installer.exe` together with `RhythKit.Agent.exe` from the same build output. The installer embeds the current `RhythKit.dll` payload and installs the agent beside the game integration.

## Install

1. Run `RhythKit.Installer.exe`.
2. Select the actual SSP Nightly, Rhythia Steam, or legacy Rhythia game directory.
3. Confirm that the installer says `Detected: SSP Nightly`, `Detected: Rhythia Steam`, or `Detected: legacy Rhythia`.
4. Click `Install RhythKit`.
5. The installer writes the RhythKit payload and a game manifest.
6. The installer registers the RhythKit agent to start with Windows and starts it immediately.
7. Close the installer.

The agent monitors the selected game process. It does not open the Rhythians login page immediately after installation.

## First game launch connection

When the installed game is started for the first time while RhythKit is not authenticated:

```text
Game starts
    ↓
RhythKit Agent detects the game
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

1. RhythKit identifies the played map.
2. The map identity is matched against a Rhythians map ID.
3. Only eligible completed maps are submitted.
4. The Rhythians API authenticates the installation token.
5. The server validates the map and completion.
6. The server calculates and records the score/RHP.

Approved Rhythians map downloads contain the Rhythians map identity in an SSPM-compatible custom field so RhythKit can read the ID.

## Rebuilding after source changes

Always rebuild after changing RhythKit source code:

```powershell
.\build.ps1
```

Do not mix `RhythKit.Installer.exe` and `RhythKit.Agent.exe` from different builds.

## Testing

Test in this order:

1. SSP Nightly
2. Rhythia Steam
3. Legacy Rhythia

For each target:

1. Install RhythKit.
2. Start the game.
3. Verify the Rhythians authorization page opens.
4. Sign in and click `Confirm login for RhythKit`.
5. Verify the `Rhythians is connected` Windows popup.
6. Open Rhythians Settings and verify `RhythKit: Connected`.
7. Play a map that exists on Rhythians.
8. Complete the map.
9. Verify the completion appears in the Rhythians account.

## Projects

- `src/RhythKit` — Rhythians API client, map identity, and game integration code
- `src/RhythKit.Agent` — local HTTP agent, game detection, authorization polling, and SSP score watcher
- `src/RhythKit.Installer` — Windows installer and game detection
