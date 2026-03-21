# Creating Custom Events

## Base Class: EventModel
Events are choice-based narrative encounters on the map. Override these core properties:
- `IsShared` — All players see the same event (multiplayer)
- `IsDeterministic` — Event outcome affected by seed
- `LayoutType` — EventLayoutType.Default, Combat, Ancient, Custom

## Key Methods
- `BeginEvent(Player, bool)` — Called when event starts; set up initial state
- `GenerateInitialOptions()` — Return the first set of choices
- `SetEventState(LocString, IEnumerable<EventOption>)` — Update body text and choices
- `SetEventFinished(LocString)` — Show final text with no choices (event ends)
- `EnterCombatWithoutExitingEvent()` — Start a fight that returns to the event after

## EventOption Structure
Each choice is an `EventOption` with:
- **Text** — `LocString` displayed on the button
- **Callback** — Method called when chosen (usually named `ChoiceX`)
- **Condition** — Optional predicate to show/hide the option
- **Tooltip** — Optional hover text explaining the choice

```csharp
new EventOption(Loc["MY_EVENT.choice_1"], Choice1)
new EventOption(Loc["MY_EVENT.choice_2"], Choice2, condition: () => Owner.Gold >= 50)
```

## Branching Logic Pattern
Events use a yield-based state machine. Each choice method updates state:

```csharp
private async Task Choice1(PlayerChoiceContext ctx)
{
    // Give reward
    await ctx.Player.GainGold(50);

    // Branch to a second page of choices
    SetEventState(Loc["MY_EVENT.page_2"], new[]
    {
        new EventOption(Loc["MY_EVENT.choice_2a"], Choice2A),
        new EventOption(Loc["MY_EVENT.choice_2b"], Choice2B),
    });
}

private async Task Choice2A(PlayerChoiceContext ctx)
{
    await ctx.Player.Heal(20);
    SetEventFinished(Loc["MY_EVENT.done"]);
}
```

## Combat Within Events
Some events include a fight. The event continues after combat ends:
```csharp
private async Task FightChoice(PlayerChoiceContext ctx)
{
    EnterCombatWithoutExitingEvent();
    // After combat, the event resumes — override OnCombatEnd to continue
}
```

## Adding to the Game
Register via Harmony patch on an act's event pool, similar to encounters:
```csharp
[HarmonyPatch(typeof(Underdocks), nameof(Underdocks.GenerateAllEvents))]
public static class AddMyEvent
{
    public static void Postfix(ref IEnumerable<EventModel> __result)
    {
        var list = __result.ToList();
        list.Add(ModelDb.Event<MyEvent>());
        __result = list;
    }
}
```

## Localization (events.json)
```json
{
  "MY_EVENT.title": "Strange Door",
  "MY_EVENT.body": "You find a mysterious door...",
  "MY_EVENT.choice_1": "Open it [gold](+50 Gold)[/gold]",
  "MY_EVENT.choice_2": "[gold]Pay 50 Gold[/gold] to peek inside",
  "MY_EVENT.page_2": "Inside you find a healing spring.",
  "MY_EVENT.done": "You leave the room feeling refreshed."
}
```

## Console Test
```
event MY_EVENT
```

## See existing events via: `list_entities` with `entity_type=event`
