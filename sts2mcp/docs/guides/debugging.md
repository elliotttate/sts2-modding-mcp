# Debugging Mods

## Quick Diagnostics
Get a full snapshot of the game's current state in one call:
```
bridge_get_diagnostics(log_lines=40)
```
Returns: screen, run state (floor/act/room), combat status, active screen object shape,
current event shape, and recent bridge log lines.

## Game Logging System

### Log Levels (most → least verbose)
| Level | Use |
|-------|-----|
| `VeryDebug` | Extremely detailed trace output |
| `Load` | Asset loading / initialization |
| `Debug` | General debug messages |
| `Info` | Standard messages (default) |
| `Warn` | Warnings (yellow in console) |
| `Error` | Errors with stack trace (red) |

### Log Categories
| Type | What It Covers |
|------|----------------|
| `Generic` | Default / general messages |
| `Actions` | Game actions (card plays, power applications, damage) |
| `Network` | Multiplayer / networking |
| `GameSync` | Game state synchronization |
| `VisualSync` | Visual effect synchronization |

### Enabling Verbose Logging
Use `bridge_set_log_level` to crank up verbosity for specific subsystems:

```
# See every game action (card plays, damage, powers, etc.)
bridge_set_log_level(type="Actions", level="Debug")

# See all subsystems at debug level
bridge_set_log_level(global_level="Debug")

# Maximum verbosity for everything
bridge_set_log_level(global_level="VeryDebug")

# Also capture verbose messages in the ring buffer (default only captures Info+)
bridge_set_log_level(capture_level="VeryDebug")
```

Check current settings:
```
bridge_get_log_levels()
```

### Reading Game Logs
Two separate log sources:

**Game log** (from the engine's own `Log` system — covers all game subsystems):
```
bridge_get_game_log()                              # Latest 100 entries
bridge_get_game_log(level="Error")                  # Errors only
bridge_get_game_log(contains="CardModel")           # Search in messages
bridge_get_game_log(since_id=150)                   # Polling: new entries only
bridge_get_game_log(level="Warn", max_count=50)     # Recent warnings
```

**Bridge log** (the mod's own log file — bridge commands, init, exceptions):
```
bridge_get_log(lines=200)
bridge_get_log(contains="AutoSlay")
```

### Logging in Mod Code
```csharp
using MegaCrit.Sts2.Core.Logging;

Log.Info("message");       // Standard info
Log.Debug("message");      // Debug (only visible if level is Debug or lower)
Log.VeryDebug("message");  // Extreme detail
Log.Warn("message");       // Warning (yellow in console)
Log.Error("message");      // Error with stack trace (red)
```

For file-based logging in your mod:
```csharp
var logPath = UserDataPathProvider.GetAccountScopedBasePath("mymod_log.txt");
File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
```

## Exception Monitoring
The bridge captures all unhandled exceptions (AppDomain + unobserved Task exceptions).

```
bridge_get_exceptions()                  # Get recent exceptions
bridge_get_exceptions(since_id=5)        # Only new since last check
bridge_clear_exceptions()                # Clear before a test for clean baseline
```

Each exception includes: ID, timestamp, type, message, stack trace, and source.

### Typical workflow
```
bridge_clear_exceptions()                # Clear
bridge_autoslay_start(runs=3)            # Run test
# ... wait for completion ...
bridge_get_exceptions()                  # Check what broke
```

## Event Tracking
The bridge records game events (run starts, card plays, hot swaps, snapshots, etc.).

```
bridge_get_events()                      # Recent events
bridge_get_events(since_id=10)           # New events only
bridge_clear_events()                    # Clear buffer
```

## Breakpoints & Stepping
The MCP provides debugger-style breakpoints and stepping for the game's action
and hook systems. When paused, the game keeps rendering but stops processing
actions — you can inspect full game state then resume or step forward.

### Pause / Resume
```
bridge_debug_pause()         # Pause action processing
bridge_debug_get_context()   # See why we're paused + full game state snapshot
bridge_debug_resume()        # Continue execution
```

### Stepping
Step through the game one action or one turn at a time:
```
bridge_debug_step(mode="action")   # Execute one action, then pause
bridge_debug_get_context()         # Inspect state after the action
bridge_debug_step(mode="action")   # Next action...

bridge_debug_step(mode="turn")    # Run until next player turn starts
```

### Action Breakpoints
Pause when a specific game action type executes:
```
# Break whenever damage is dealt
bridge_debug_set_breakpoint(type="action", target="DamageAction")

# Break on card plays only when HP is low
bridge_debug_set_breakpoint(type="action", target="PlayCardAction", condition="hp<10")

# Break when energy runs out
bridge_debug_set_breakpoint(type="action", target="PlayCardAction", condition="energy==0")
```

### Hook Breakpoints
Pause when a specific game hook fires:
```
# Break before any card is played
bridge_debug_set_breakpoint(type="hook", target="BeforeCardPlayed")

# Break before damage is received
bridge_debug_set_breakpoint(type="hook", target="BeforeDamageReceived")

# Break at death (useful for testing death-prevention relics)
bridge_debug_set_breakpoint(type="hook", target="BeforeDeath")

# Break at start of each player turn
bridge_debug_set_breakpoint(type="hook", target="BeforePlayPhaseStart")
```

Note: Hook breakpoints block the game thread entirely (including rendering)
until resumed. Action breakpoints and stepping use the game's own pause system
which keeps the game rendering.

### Available Hook Names
Combat flow: `BeforeCombatStart`, `BeforePlayPhaseStart`, `BeforeSideTurnStart`,
`BeforeTurnEnd`, `AfterTurnEnd`

Cards: `BeforeCardPlayed`, `AfterCardPlayed`

Damage: `BeforeDamageReceived`, `AfterDamageReceived`

Death: `BeforeDeath`, `AfterDeath`

Powers: `BeforePowerAmountChanged`, `AfterPowerAmountChanged`

Block: `BeforeBlockGained`

Navigation: `BeforeRoomEntered`, `AfterRoomEntered`

Other: `BeforePotionUsed`, `AfterEnergySpent`, `BeforeHandDraw`

### Condition Expressions
Breakpoints support simple conditions on player state:
- `hp<10`, `hp>=50`, `hp==1`
- `energy==0`, `energy>3`
- `block>5`, `block==0`
- `round>=3`, `round==1`
- `gold>500`
- `hand_size==0`

### Managing Breakpoints
```
bridge_debug_list_breakpoints()       # List all with IDs, hit counts
bridge_debug_remove_breakpoint(id=1)  # Remove specific breakpoint
bridge_debug_clear_breakpoints()      # Remove all + disable step mode
```

### Debugging Workflow Example
```
# Start a combat and set up breakpoints
bridge_start_run(character="Ironclad", fight="JawWorm")
bridge_debug_set_breakpoint(type="hook", target="BeforeCardPlayed")

# Play a card — execution pauses before the card resolves
bridge_play_card(card_index=0, target_index=0)
bridge_debug_get_context()   # See game state before card effect

# Step through each action the card causes
bridge_debug_step(mode="action")   # First action (e.g., damage)
bridge_debug_get_context()          # Inspect damage result
bridge_debug_step(mode="action")   # Next action (e.g., power apply)
bridge_debug_get_context()          # Inspect power state

# Done investigating, resume normal play
bridge_debug_clear_breakpoints()
bridge_debug_resume()
```

## State Diffing
Track what changed between two points in time:
```
bridge_get_state_diff()    # First call: captures baseline
# ... play some cards, take actions ...
bridge_get_state_diff()    # Returns diff: what changed in HP, gold, deck, powers, etc.
```

## Remote Debugging (Godot Output)
For full engine-level debugging with the Godot editor:
1. In Steam, add launch parameter: `--remote-debug tcp://127.0.0.1:6007`
2. Open Godot editor (port 6007 is default for debug server)
3. Check "Keep Debug Server Open" under Debug tab
4. Launch STS2 through Steam
5. Godot's Output panel shows all game logs with color coding and file:line info

## In-Game Console
Press backtick (`` ` ``) to open. Any loaded mod auto-enables the full console.

### Debug Commands
| Command | Effect |
|---------|--------|
| `log Actions Debug` | Enable debug logging for actions |
| `log VeryDebug` | Set all logging to maximum verbosity |
| `godmode` | Invincibility |
| `fight ENCOUNTER_ID` | Jump to specific encounter |
| `card CARD_ID` | Add card to deck |
| `gold 999` | Set gold |
| `heal 999` | Full heal |
| `energy 99` | Set energy |
| `draw 10` | Draw cards |
| `kill` | Kill all enemies |
| `instant` | Toggle instant mode (skip animations) |
| `dump` | Dump Model ID database to console |
| `getlogs` | Zip all logs + saves + screenshot to a bug report file |
| `event EVENT_ID` | Jump to specific event |
| `room ROOM_TYPE` | Jump to room type |

All console commands can also be executed via: `bridge_console(command="godmode")`

### Command-Line Arguments
| Argument | Effect |
|----------|--------|
| `-log Actions Debug` | Set log level at startup |
| `--headless` | Run headless (no window) |
| `--remote-debug tcp://127.0.0.1:6007` | Godot remote debugging |

### Environment Variables
| Variable | Effect |
|----------|--------|
| `STS2_DEV_SKIP` | Enable development skip mode |

## Common Issues
- **Mod not showing**: Check mod_manifest.json format and mod folder structure
- **Assembly load failure**: Ensure .NET 9.0 target, check DLL dependencies
- **Null reference on startup**: Models must be accessed after initialization; check hook timing
- **PCK not loading**: Verify pck_name in manifest matches actual .pck filename
- **Harmony patch not working**: Use `bridge_get_game_log(level="Warn")` to check for patch conflicts
- **Actions not triggering**: Set `bridge_set_log_level(type="Actions", level="Debug")` and watch the log
