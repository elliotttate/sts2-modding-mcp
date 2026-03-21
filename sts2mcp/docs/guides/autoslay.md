# AutoSlay — Automated Game Runner

## Overview
AutoSlay is the game's built-in automated runner. It plays through entire runs
without manual input — handling combat, events, shops, rest sites, rewards, and
map navigation using deterministic AI. The MCP exposes AutoSlay through bridge
tools so you can use it for automated mod testing.

## When to Use AutoSlay vs Bridge Actions
- **AutoSlay**: Fire-and-forget full runs. "Run 10 games with my mod — did anything crash?"
- **Bridge actions** (`bridge_play_card`, `bridge_end_turn`, etc.): Step-by-step control.
  "Play this specific card, assert the enemy lost HP."
- **Test scenarios** (`run_test_scenario`): Scripted sequences with assertions.
  "Start a run with these relics, play 3 turns, verify state."

AutoSlay and bridge actions are complementary. Use AutoSlay for broad stability
testing, bridge actions for precise behavioral verification.

## Quick Start

### Run a single automated game
```
bridge_autoslay_start(character="Ironclad")
```

### Check progress
```
bridge_autoslay_status()
```
Returns: running state, floor, act, room type, runs completed, elapsed time,
and recent log entries.

### Stop early
```
bridge_autoslay_stop()
```

## Common Testing Patterns

### Smoke Test (Does my mod crash?)
Run several games across different characters:
```
bridge_autoslay_start(character="Ironclad", runs=3)
# Wait for completion, then:
bridge_autoslay_start(character="Silent", runs=3)
```
Check `bridge_autoslay_status()` for errors. Check `bridge_get_exceptions()` for
any unhandled exceptions your mod caused.

### Deterministic Regression (Same seed, same result)
```
bridge_autoslay_start(character="Ironclad", seed="test123")
```
Run the same seed before and after changes. If the first run succeeds but the
second crashes, your change broke something.

### Stress Test (Memory leaks, rare crashes)
```
bridge_autoslay_start(character="Ironclad", loop=true)
# Let it run for a while, then:
bridge_autoslay_status()   # Check runs_completed, elapsed time, errors
bridge_autoslay_stop()
```

### Speed Up Testing
Combine AutoSlay with `bridge_set_game_speed` for faster iteration:
```
bridge_set_game_speed(speed=10.0)
bridge_autoslay_start(character="Ironclad", runs=5)
```

## Configuration
Adjust timeouts before starting runs with `bridge_autoslay_configure`:

| Parameter | Default | Description |
|-----------|---------|-------------|
| `run_timeout_seconds` | ~1500 (25min) | Max time for one full run |
| `room_timeout_seconds` | ~120 (2min) | Max time in any single room |
| `screen_timeout_seconds` | ~30 | Max time on any overlay screen |
| `polling_interval_ms` | ~100 | How often AutoSlay checks game state |
| `watchdog_timeout_seconds` | ~30 | Stall detection (logs warning if stuck) |
| `max_floor` | ~49 | Floor to stop at |

### Example: Increase timeouts for a slow mod
```
bridge_autoslay_configure(
    run_timeout_seconds=3000,
    room_timeout_seconds=300,
    screen_timeout_seconds=60
)
bridge_autoslay_start(character="Ironclad", runs=5)
```

## How AutoSlay Works Internally
The game's `MegaCrit.Sts2.Core.AutoSlay` namespace contains:

- **AutoSlayer** — Main orchestrator. Calls `RunAsync(seed, ct)` to drive a full game.
- **IRoomHandler** implementations — One per room type (combat, event, shop, treasure, rest site).
  Each handler plays the room to completion using the game's own APIs.
- **IScreenHandler** implementations — One per overlay screen (rewards, card selection,
  deck upgrade, game over, etc.). Drains screens after rooms finish.
- **AutoSlayCardSelector** — Deterministic card selection using a seeded RNG. Shuffles
  options and picks randomly (not strategically).
- **AutoSlayConfig** — Timeout constants and polling intervals.
- **Watchdog** — Stall detector that logs periodic state dumps if the game gets stuck.

The MCP bridge creates an `AutoSlayer` instance via reflection and invokes `RunAsync`.
Cancellation uses a standard `CancellationToken`. Multi-run and loop modes are handled
by the bridge wrapper, not by AutoSlayer itself.

## Monitoring and Diagnostics
While AutoSlay runs, use these tools to observe:

- `bridge_autoslay_status()` — Running state, floor, act, runs completed, log tail
- `bridge_get_screen()` — What screen the game is currently on
- `bridge_get_run_state()` — Full run state (act, floor, players, gold, HP)
- `bridge_get_combat_state()` — If in combat: enemies, hand, energy
- `bridge_get_exceptions()` — Any unhandled exceptions (most useful after a crash)
- `bridge_get_events()` — Game event timeline

## Troubleshooting
- **"AutoSlay types not found"**: The game version may not include AutoSlay, or the
  assembly name changed. Check that `MegaCrit.Sts2.Core.AutoSlay.AutoSlayer` exists in sts2.dll.
- **Runs hang**: Increase timeouts with `bridge_autoslay_configure`. Check
  `bridge_autoslay_status()` for the current room/screen and recent log.
- **Runs crash immediately**: Your mod may throw during initialization or hook execution.
  Check `bridge_get_exceptions()` and the mod's log file.
- **"Already running"**: Call `bridge_autoslay_stop()` first, then start a new session.
