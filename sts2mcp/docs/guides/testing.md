# Testing Your Mod

## Testing Approaches
The MCP provides three levels of automated testing, from broad to precise:

| Approach | Tool | Best For |
|----------|------|----------|
| **AutoSlay** | `bridge_autoslay_start` | Stability across many runs, crash detection |
| **Test Scenarios** | `run_test_scenario` | Scripted multi-step sequences with assertions |
| **Bridge Actions** | `bridge_play_card`, etc. | Interactive step-by-step control |

Use all three together: AutoSlay catches crashes, test scenarios verify behavior,
bridge actions let you explore edge cases interactively.

## Quick Manual Testing
For fast iteration during development:

1. `build_mod` — Compile your mod
2. `install_mod` — Copy to game mods folder
3. `bridge_hot_swap_patches` — Reload Harmony patches without restarting the game
4. Test via console or bridge actions

## AutoSlay (Automated Full Runs)
Run entire games automatically. See the [AutoSlay guide](autoslay.md) for full details.

```
# Smoke test: 3 runs per character
bridge_autoslay_start(character="Ironclad", runs=3)
# Check for crashes:
bridge_autoslay_status()
bridge_get_exceptions()
```

## Test Scenarios (Scripted Sequences)
Define a scenario with setup conditions and assertion steps:

```json
{
  "name": "custom_relic_grants_strength",
  "setup": {
    "character": "Ironclad",
    "seed": "test",
    "relics": ["MyCustomRelic"],
    "fight": "JawWorm"
  },
  "steps": [
    {
      "action": "noop",
      "wait_for_screen": "COMBAT_PLAYER_TURN",
      "assert": {
        "has_power_StrengthPower": true,
        "power_StrengthPower": 3
      }
    },
    {
      "action": "play_card",
      "params": {"card_index": 0},
      "wait_idle": true,
      "assert": {
        "enemy_0_hp": {"op": "lt", "value": 50}
      }
    }
  ]
}
```

### Available Step Actions
- `play_card` — params: `card_index`, `target_index`
- `end_turn` — no params
- `use_potion` — params: `potion_index`, `target_index`
- `console` — params: `command` (any console command)
- `manipulate_state` — params: `hp`, `gold`, `add_power`, `add_relic`, `add_card`, etc.
- `navigate_map` — params: `row`, `col`
- `event_choice` — params: `choice_index`
- `rest_choice` — params: `choice` (`rest`, `smith`, `recall`)
- `wait` — params: `seconds`
- `noop` — does nothing (useful with assertions only)
- `execute_action` — params: `action` + action-specific params

### Available Assertions
| Key | Source | Type |
|-----|--------|------|
| `hp`, `max_hp`, `block`, `energy`, `hand_size` | Combat state | int |
| `draw_pile`, `discard_pile` | Combat state | int |
| `gold`, `deck_count`, `relic_count` | Player state | int |
| `in_combat`, `round` | Combat state | bool/int |
| `screen` | Screen detector | string |
| `enemy_N_field` | Enemy N (0-indexed) | varies |
| `has_power_X` | Player powers | bool |
| `power_X` | Power amount | int |

### Assertion Operators
Simple equality: `"hp": 50`
Comparison: `"hp": {"op": "gt", "value": 30}`
Operators: `eq`, `gt`, `lt`, `gte`, `lte`, `not_eq`, `contains`

### Step Options
- `wait_for_screen`: Wait for a specific screen before asserting (e.g., `COMBAT_PLAYER_TURN`)
- `wait_idle`: Wait for the game to finish processing
- `delay`: Seconds to wait after the action
- `stop_on_fail`: Stop the scenario on assertion failure (default: true)

## Bridge Actions (Interactive Testing)
Use individual bridge tools for exploratory testing:

```
bridge_start_run(character="Ironclad", seed="test", relics=["MyRelic"], fight="JawWorm")
bridge_get_combat_state()    # See enemies, hand, energy
bridge_play_card(card_index=0, target_index=0)
bridge_get_combat_state()    # Verify the result
bridge_end_turn()
```

### Useful Setup Options for Testing
- `relics`: Pre-load specific relics to test
- `cards`: Pre-load specific cards into deck
- `fight`: Jump directly to a specific encounter
- `event`: Jump directly to a specific event
- `godmode`: Invincibility for testing without dying
- `gold`, `hp`, `energy`: Set starting values
- `fixture_commands`: Console commands to run after setup

## State Manipulation
Modify game state mid-run for targeted testing:

```
bridge_manipulate_state({
  "hp": 1,                              # Set HP to 1 (test low-HP triggers)
  "add_power": {"name": "Vulnerable", "amount": 3},
  "add_relic": "MyCustomRelic",
  "add_card": "Strike"
})
```

## Snapshots (A/B Testing)
Save and restore state to compare outcomes:

```
bridge_save_snapshot(name="before_boss")
# Test approach A...
bridge_restore_snapshot(name="before_boss")
# Test approach B...
```

## Monitoring During Tests
- `bridge_get_exceptions()` — Unhandled exceptions from your mod
- `bridge_get_events()` — Game event timeline
- `bridge_get_state_diff()` — What changed since last check
- `bridge_capture_screenshot()` — Visual state capture

## Console Command Test Harness
For complex UI flows that bridge actions can't drive (e.g., bundle selection screens, custom overlays), create a custom console command that runs the test end-to-end:

```csharp
using Godot;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Runs;

public class TestMyRelicCmd : AbstractConsoleCmd
{
    public override string CmdName => "test_my_relic";
    public override string Args => "";
    public override string Description => "End-to-end test of MyRelic pickup flow";
    public override bool IsNetworked => true;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (issuingPlayer == null || !RunManager.Instance.IsInProgress)
            return new CmdResult(false, "No run in progress");
        Task task = RunTest(issuingPlayer);
        return new CmdResult(task, true, "Running test...");
    }

    private static async Task RunTest(Player player)
    {
        // Schedule UI auto-clicks on the main thread via timers
        var tree = NGame.Instance!.GetTree();
        tree.CreateTimer(1.5).Connect("timeout", Callable.From(() =>
        {
            // Find and click a button using ForceClick
            var screen = tree.Root.FindChild("MyScreen", true, false) as Control;
            var hitbox = screen?.FindChild("Hitbox", false, false) as NClickableControl;
            hitbox?.ForceClick();  // Emits Released signal directly
        }));

        // This awaits the UI interaction scheduled above
        await MyAsyncOperation(player);
        Log.Info("[Test] PASSED!");
    }
}
```

### Key Pattern: NClickableControl.ForceClick()
`NClickableControl.ForceClick()` emits the `Released` signal directly, bypassing mouse/keyboard input handling. Use it for programmatic UI interaction from console commands or test harnesses. Schedule calls on the main thread via `SceneTree.CreateTimer()` + `Callable.From()` since async tasks run on thread pool threads.

### Console Command Testing Tips
- Commands extending `AbstractConsoleCmd` are **auto-discovered** — no registration needed
- Return `CmdResult(task, true, msg)` to run async operations through the game's action queue
- Use `Log.Info("[TestName] Step N: ...")` for progress tracking via `bridge_get_game_log`
- `relic add` skips `AfterObtained()` — use `RelicCmd.Obtain(relic.ToMutable(), player)` instead

## Recommended Test Workflow
1. **During development**: Bridge actions + hot swap for fast iteration
2. **Before release**: AutoSlay smoke test (5+ runs per character)
3. **Regression suite**: Test scenarios with fixed seeds for deterministic verification
4. **After changes**: Re-run AutoSlay + test scenarios to catch regressions
