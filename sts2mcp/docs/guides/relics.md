# Creating Custom Relics

## Base Class: RelicModel
Override key properties:
- `Rarity`: RelicRarity.Starter/Common/Uncommon/Rare/Shop/Event/Ancient
- `IsStackable`: Whether multiple instances can exist (rare)

## Pool Registration
`[Pool(typeof(SharedRelicPool))]` or character-specific pools.

## Common Hook Methods to Override
- `BeforeCombatStart()` - Trigger at combat start
- `AfterCardPlayed(CombatState, PlayerChoiceContext, CardPlay)` - After any card played
- `AfterDamageReceived(PlayerChoiceContext, Creature, DamageResult, ValueProp, Creature?, CardModel?)` - After taking damage
- `AfterTurnEnd(CombatState, CombatSide)` - End of turn
- `ModifyDamageAdditive(...)` - Modify damage dealt
- `ModifyBlock(...)` - Modify block gained
- `AfterBlockGained(...)` - After gaining block
- `BeforeHandDraw(...)` - Before drawing cards

## Key Patterns
- `Flash()` - Play relic activation animation
- `Owner` - The Player who has this relic
- Use state fields (e.g., `_usedThisCombat`) and reset in `AfterCombatEnd`

## Images: 256x256 PNG with 10px black outline (60% opacity)
Place at: `{ModName}/images/relics/{snake_name}.png`

## Localization (relics.json)
```json
{
  "MY_RELIC.title": "Relic Name",
  "MY_RELIC.description": "Effect description with [blue]{Value}[/blue].",
  "MY_RELIC.flavor": "Flavor text..."
}
```

## Relic Pickup Effects (AfterObtained)
Override `AfterObtained()` for relics that show a selection screen on pickup. Use `HasUponPickupEffect => true` to signal the game to await the task.

### CardSelectCmd.FromChooseABundleScreen
Shows the "Choose a Pack" screen with multiple card bundles. **Cards must be created with `CreateCard`, not raw model singletons**:

```csharp
public override async Task AfterObtained()
{
    var cardModels = GetCardModels(); // List<CardModel> from ModelDb.Card<T>()

    // WRONG — raw models have no Owner, ConfirmSelection will NullRef
    // on item.Model.Owner.Character.TrailPath (card fly VFX)
    var bundles = new List<IReadOnlyList<CardModel>> { cardModels };

    // RIGHT — CreateCard sets Owner so the UI can access Owner.Character
    var bundle = cardModels
        .Select(c => Owner.RunState.CreateCard(c, Owner))
        .ToList();
    var bundles = new List<IReadOnlyList<CardModel>> { bundle };

    var selected = await CardSelectCmd.FromChooseABundleScreen(Owner, bundles);
    // selected contains the cards the player chose
}
```

The vanilla `ScrollBoxes` relic demonstrates this pattern. Without `CreateCard`, the confirm button's fly animation crashes on null `Owner` and the screen freezes permanently.

### Console Note
`relic add MY_RELIC` adds a relic **without** calling `AfterObtained()`. To test pickup effects, use `RelicCmd.Obtain(relic.ToMutable(), player)` from a custom console command instead.

## Console Test: `relic add MY_RELIC`
