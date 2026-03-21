# Creating Custom Powers (Buffs/Debuffs)

## Base Class: PowerModel
Override key properties:
- `Type`: PowerType.Buff or PowerType.Debuff
- `StackType`: PowerStackType.Counter (stackable) or PowerStackType.Single

## Key Properties
- `Amount` - Current stack count
- `Owner` - Creature with this power
- `Applier` - Creature that applied it

## Common Hook Methods
- `ModifyDamageAdditive(...)` - Add flat damage (like Strength)
- `ModifyDamageMultiplicative(...)` - Multiply damage (like Vulnerable: 1.5x)
- `BeforeHandDraw(Player, PlayerChoiceContext, CombatState)` - Before draw step
- `AfterTurnEnd(CombatState, CombatSide)` - End of turn (tick down with `PowerCmd.Decrement(this)`)
- `AfterCardPlayed(...)` - React to cards
- `BeforeDamageReceived(...)` - Before taking damage

## Images: 256x256 PNG with 10px black outline (60% opacity)

## Localization (powers.json)
```json
{
  "MY_POWER.title": "Power Name",
  "MY_POWER.smartDescription": "Effect with [blue]{Amount}[/blue] stacks and {Amount:plural:time|times}.",
  "MY_POWER.description": "Base description for 1 stack."
}
```

## Console Test: `power MY_POWER 3 0` (3 stacks on player index 0)
