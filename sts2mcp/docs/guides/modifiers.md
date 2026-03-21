# Creating Custom Run Modifiers

## Overview
Modifiers are gameplay mutators that players can enable in custom runs. They appear in the
modifier selection screen and alter game mechanics for the entire run. Modifiers are either
**Good** (beneficial) or **Bad** (challenging).

## Base Class: ModifierModel
All modifiers extend `ModifierModel`. Key overrides:

### Lifecycle Hooks
- `AfterRunCreated(RunState)` - Called when a new run starts. Use for one-time setup.
- `AfterRunLoaded(RunState)` - Called when a saved run is loaded. Re-apply persistent changes.
- `GenerateNeowOption(EventModel)` - Custom option for the first event (Neow). Return a `Func<Task>`.

### Reward Modification
- `TryModifyRewardsLate(Player, List<Reward>, AbstractRoom?)` - Modify combat rewards in-place. Return `true` if modified.

### Card Pool Filtering
- `ModifyCardRewardCreationOptions(Player, CardCreationOptions)` - Filter card reward pools.
- `ModifyCardRewardCreationOptionsLate(Player, CardCreationOptions)` - Late-pass card pool filtering.
- `ModifyMerchantCardPool(Player, IEnumerable<CardModel>)` - Filter shop card offerings.

### Other Properties
- `ClearsPlayerDeck` - If true, player starts with empty deck (default false).

## Registration (Required)
Modifiers must be registered via a Harmony patch on `ModelDb`:

```csharp
[HarmonyPatch(typeof(ModelDb), "get_BadModifiers")]  // or "get_GoodModifiers"
public class MyModifierRegistrationPatch
{
    public static void Postfix(ref IReadOnlyList<ModifierModel> __result)
    {
        var extended = __result.ToList();
        extended.Add(ModelDb.Modifier<MyModifier>());
        __result = extended.AsReadOnly();
    }
}
```

## Localization
Modifiers use the `"modifiers"` table. Inject via `LocManager.SetLanguage` patch:

```csharp
[HarmonyPatch(typeof(LocManager), nameof(LocManager.SetLanguage))]
public class ModifierLocPatch
{
    public static void Postfix(LocManager __instance)
    {
        __instance.GetTable("modifiers").MergeWith(
            new Dictionary<string, string>
            {
                {"MY_MODIFIER.title", "My Modifier"},
                {"MY_MODIFIER.description", "What it does."},
            });
    }
}
```

This approach survives language changes (unlike file-based localization which gets cleared).

## Example: Good Modifier (Replace Card Rewards with Relics)
```csharp
public class VintagePlus : ModifierModel
{
    public override bool TryModifyRewardsLate(Player player, List<Reward> rewards, AbstractRoom? room)
    {
        if (room is not CombatRoom combatRoom) return false;
        if (combatRoom.Encounter.RoomType != RoomType.Monster) return false;

        for (int i = rewards.Count - 1; i >= 0; i--)
        {
            if (rewards[i] is CardReward)
            {
                rewards.RemoveAt(i);
                rewards.Insert(i, new RelicReward(player));
                rewards.Insert(i, new RelicReward(player));
            }
        }
        return true;
    }
}
```

## Example: Bad Modifier (Prevent Relic Acquisition)
Dual pattern - modifier + Harmony prefix to intercept game commands:

```csharp
[HarmonyPatch]
public class Famine : ModifierModel
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Obtain),
        new[] { typeof(RelicModel), typeof(Player), typeof(int) })]
    static bool PreventRelicObtain(RelicModel relic, Player player, ref Task<RelicModel> __result)
    {
        if (player.RunState.Modifiers.Any(m => m is Famine))
        {
            __result = Task.FromResult(relic);
            return false;  // Skip original method
        }
        return true;
    }
}
```

## Key Patterns
- **Multiplayer awareness**: Always iterate `runState.Players` when modifying state.
- **Relic grab bags**: `player.RelicGrabBag` (character-specific) and `runState.SharedRelicGrabBag`.
- **Card pool with unlock state**: `pool.GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)`.
- **Dual-class modifier**: A class can be both `ModifierModel` and `[HarmonyPatch]` simultaneously.
- **Active modifier check**: `player.RunState.Modifiers.Any(m => m is MyModifier)`.

## Use `generate_modifier` tool to scaffold all three files (modifier + registration patch + localization patch).
