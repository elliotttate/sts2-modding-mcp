# Creating Custom Enchantments

## What Are Enchantments?
Enchantments are modifications that attach to cards and alter their behavior. When a card has an enchantment, it gains additional effects that trigger based on hooks (same hook system as powers and relics). Think of enchantments as "card-local buffs" — they travel with the card and fire when the card is played or when specific game events occur.

## Base Class: EnchantmentModel
```csharp
using MegaCrit.Sts2.Core.Models.Enchantments;

public sealed class MyEnchantment : EnchantmentModel
{
    public override bool ShowAmount => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new MoveVar(3),
    };

    // Hook methods go here — same hooks available as powers/relics
}
```

## Key Properties
- `ShowAmount` — Whether to display the enchantment's numeric value on the card
- `CanonicalVars` — DynamicVars for the enchantment's description values
- `EnchantedCard` — Access the card this enchantment is attached to (available in hook methods)

## Accessing the Enchanted Card
Inside any hook method, use `EnchantedCard` to reference the card:
```csharp
public override async Task AfterCardPlayed(PlayerChoiceContext ctx, CardInstance card)
{
    if (card == EnchantedCard)
    {
        // This enchantment's card was played — do something extra
        await ctx.Player.GainBlock(3);
    }
}
```

## Available Hooks
Enchantments use the same hook system as powers and relics. Common hooks:
- `AfterCardPlayed` — After any card is played
- `BeforeCombatStart` — When combat begins
- `AfterTurnEnd` — After the player's turn ends
- `ModifyDamageAdditive` / `ModifyDamageMultiplicative` — Modify damage
- `ModifyBlockAdditive` — Modify block gained

Use `list_hooks` to see all available hooks, or `search_hooks_by_signature` to find hooks by parameter type.

## Applying Enchantments to Cards
Enchantments are applied programmatically:
```csharp
cardInstance.AddEnchantment(ModelDb.Enchantment<MyEnchantment>(), amount);
```

This is typically done in card effects, relic triggers, or event rewards.

## Localization (enchantments.json)
```json
{
  "MY_ENCHANTMENT.title": "Sharpened",
  "MY_ENCHANTMENT.description": "This card deals {move} additional damage."
}
```

## When to Use Enchantments vs. Other Approaches
- **Enchantment**: Effect tied to a specific card instance (e.g., "this card gains +3 damage")
- **Power**: Effect tied to the player/creature globally (e.g., "all attacks deal +3 damage")
- **Relic**: Permanent passive effect for the rest of the run
- **Card upgrade**: Permanent improvement via `GetUpgradeChanges()`

## Generator
Use `generate_enchantment` with a `trigger_hook` to scaffold the class with the correct hook method signature.
