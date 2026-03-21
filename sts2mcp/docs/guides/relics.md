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

## Console Test: `relic add MY_RELIC`
