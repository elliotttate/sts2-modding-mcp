# FastMP — Local Multiplayer Testing Without Steam

## Overview

`fastmp` is a built-in command-line argument for quickly launching and testing
multiplayer sessions over localhost without Steam. Instead of going through
Steam's networking and friends list, it spins up an ENet server/client pair on
`127.0.0.1:33771`, letting you run two game instances on the same machine and
connect them directly.

This is a developer tool — it's not exposed in settings or documented in-game.
It lives entirely in command-line argument handling.

## Quick Start

### Host a standard multiplayer game
```
SlayTheSpire2.exe --fastmp host_standard
```

### Join from a second instance
```
SlayTheSpire2.exe --fastmp join
```

That's it. The host starts an ENet server on port 33771, the client connects
to `127.0.0.1:33771`, and you have a local co-op session.

## Command-Line Syntax

The game uses `CommandLineHelper` to parse arguments. Both formats work:

```
--fastmp host_standard
--fastmp=host_standard
```

### Available Values

| Value | Effect |
|---|---|
| `host` | Opens the host submenu (you pick game mode manually) |
| `host_standard` | Hosts a Standard multiplayer game immediately |
| `host_daily` | Hosts a Daily multiplayer game immediately |
| `host_custom` | Hosts a Custom multiplayer game immediately |
| `load` | Loads an existing multiplayer save and hosts it |
| `join` | Opens the join screen and auto-connects to localhost |

### Additional Arguments

| Argument | Used With | Effect |
|---|---|---|
| `--clientId <number>` | `join` | Sets the client's network ID (default: `1000`) |

Example with custom client ID:
```
SlayTheSpire2.exe --fastmp join --clientId 1001
```

## What FastMP Changes

FastMP affects six systems when the argument is present:

### 1. Skips Intro Logo

The splash screen is bypassed entirely, same as having `SkipIntroLogo` enabled
in settings or `DevSkip` active. Gets you to the main menu instantly.

**Source:** `NGame._Ready()` — checks `CommandLineHelper.HasArg("fastmp")` alongside
`DebugSettings.DevSkip` and `SettingsSave.SkipIntroLogo`.

### 2. Auto-Navigates to Multiplayer

On main menu load, `NMainMenu.CheckCommandLineArgs()` reads the `fastmp` value
and automatically opens the multiplayer submenu, then dispatches based on the
value:

- `host` / `host_standard` / `host_daily` / `host_custom` → calls `FastHost(gameMode)`
  on the multiplayer submenu, which pushes `NMultiplayerHostSubmenu` and starts hosting
- `load` → loads the multiplayer save file for the local player ID and starts hosting
- `join` → pushes `NJoinFriendScreen` which triggers the fast join flow

### 3. Forces ENet (LAN) Instead of Steam Networking

Every multiplayer entry point checks for `fastmp` to decide the platform type:

```csharp
PlatformType platformType = (SteamInitializer.Initialized && !CommandLineHelper.HasArg("fastmp"))
    ? PlatformType.Steam
    : PlatformType.None;
```

When `PlatformType.None` is selected, the host calls `netService.StartENetHost(33771, 4)`
instead of `netService.StartSteamHost(4)`. This creates a direct ENet server on
**port 33771** accepting up to **4 players**.

This applies in three places:
- `NMultiplayerHostSubmenu.StartHostAsync()` — new game hosting
- `NMultiplayerSubmenu.StartHostAsync()` — load game hosting
- `NMultiplayerSubmenu.StartLoad()` — load button handler

### 4. Direct Localhost Join (Bypasses Friends List)

When the join screen opens with `fastmp` active, it skips the Steam friends list
entirely and calls `FastMpJoin()`:

```csharp
private async Task FastMpJoin()
{
    ulong netId = 1000uL;
    if (CommandLineHelper.TryGetValue("clientId", out string value))
    {
        netId = ulong.Parse(value);
    }
    DisplayServer.WindowSetTitle("Slay The Spire 2 (Client)");
    await JoinGameAsync(new ENetClientConnectionInitializer(netId, "127.0.0.1", 33771));
}
```

Notable details:
- Connects to `127.0.0.1:33771` (localhost only — no remote connections)
- Default network ID is `1000`, overridable with `--clientId`
- The window title changes to **"Slay The Spire 2 (Client)"** so you can tell the instances apart

### 5. Skips Multiplayer Warning Popup

Normally, players who haven't completed any runs see a first-time-use warning
when entering the multiplayer menu. FastMP suppresses this:

```csharp
if (!SaveManager.Instance.SeenFtue("multiplayer_warning")
    && SaveManager.Instance.Progress.NumberOfRuns == 0
    && !CommandLineHelper.HasArg("fastmp"))
{
    NMultiplayerWarningPopup modalToCreate = NMultiplayerWarningPopup.Create();
    NModalContainer.Instance.Add(modalToCreate);
}
```

### 6. Enables Null Leaderboard Refresh

When running without Steam, the game uses `NullLeaderboardStrategy` as a
placeholder. With `fastmp`, this strategy actually refreshes leaderboard data
from the local save file, allowing multiplayer score tracking to work in
testing:

```csharp
private async Task CheckRefreshLeaderboard(NullLeaderboardHandle? handle, ulong id)
{
    if (!CommandLineHelper.HasArg("fastmp"))
        return;

    await Task.Delay((int)((float)id * 0.5f));
    _leaderboards = Read();
    if (handle != null)
        handle.leaderboard = _leaderboards.First(l => l.name == handle.leaderboard.name);
}
```

## Typical Workflow

### Two-instance local testing

**Terminal 1 — Host:**
```
SlayTheSpire2.exe --fastmp host_standard
```

**Terminal 2 — Client:**
```
SlayTheSpire2.exe --fastmp join
```

The host window shows the normal game title. The client window shows
"Slay The Spire 2 (Client)". Both connect over ENet on port 33771.

### Testing with multiple clients

Each client needs a unique network ID:

**Terminal 2:**
```
SlayTheSpire2.exe --fastmp join --clientId 1001
```

**Terminal 3:**
```
SlayTheSpire2.exe --fastmp join --clientId 1002
```

Up to 4 players total (1 host + 3 clients).

### Loading a saved multiplayer run

```
SlayTheSpire2.exe --fastmp load
```

This reads the multiplayer save file for the local player and hosts it.
The client joins with `--fastmp join` as usual.

## Network Details

| Property | Value |
|---|---|
| Transport | ENet (UDP-based) |
| Port | 33771 |
| Address | 127.0.0.1 (localhost only) |
| Max players | 4 |
| Host network ID | 1 (hardcoded in `ENetHost.NetId`) |
| Default client ID | 1000 |
| Platform type | `PlatformType.None` |

## Modding Considerations

### Testing multiplayer mods locally

FastMP is the easiest way to test multiplayer mod behavior. Launch two instances
with your mod installed in both, host from one, join from the other, and verify
your custom `INetMessage` types sync correctly.

### Harmony patches that check platform type

If your mod patches code that branches on `PlatformType`, be aware that fastmp
forces `PlatformType.None`. Behavior may differ from real Steam multiplayer:

```csharp
// This will be PlatformType.None under fastmp, PlatformType.Steam normally
PlatformType platformType = (SteamInitializer.Initialized && !CommandLineHelper.HasArg("fastmp"))
    ? PlatformType.Steam
    : PlatformType.None;
```

### Detecting fastmp in your mod

You can check whether the game was launched with fastmp:

```csharp
using MegaCrit.Sts2.Core.Helpers;

bool isFastMp = CommandLineHelper.HasArg("fastmp");
string fastMpValue = CommandLineHelper.GetValue("fastmp"); // "host", "join", etc.
```

## Source Locations

| File | What it does |
|---|---|
| `CommandLineHelper.cs` | Parses `--fastmp` from command line args |
| `NGame.cs` | Skips intro logo when fastmp is present |
| `NMainMenu.cs` | `CheckCommandLineArgs()` dispatches host/load/join |
| `NMultiplayerSubmenu.cs` | Forces ENet platform, skips warning popup |
| `NMultiplayerHostSubmenu.cs` | Forces ENet platform when hosting new games |
| `NJoinFriendScreen.cs` | `FastMpJoin()` — direct localhost connection |
| `NullLeaderboardStrategy.cs` | Enables leaderboard refresh without Steam |
