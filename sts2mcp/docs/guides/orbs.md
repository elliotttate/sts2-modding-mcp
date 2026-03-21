# Creating Custom Orbs

## What Are Orbs?
Orbs are the Defect character's core mechanic. Each orb occupies a slot and has two effects:
- **Passive**: Triggers automatically at the end of each turn
- **Evoke**: Triggers when the orb is pushed out of the slot array (by channeling a new orb when full) or when manually evoked

Orbs interact with Focus — a stat that modifies orb values via `ModifyOrbValue()`.

## Base Class: OrbModel
```csharp
using MegaCrit.Sts2.Core.Models.Orbs;

public sealed class MyOrb : OrbModel
{
    // Visual tint color for the orb
    public override Color DarkenedColor => new Color("3366ff");

    // Base passive value (modified by Focus)
    public override decimal PassiveVal => ModifyOrbValue(3m);

    // Base evoke value (modified by Focus)
    public override decimal EvokeVal => ModifyOrbValue(9m);

    // Called at end of turn — triggers the passive effect
    public override async Task BeforeTurnEndOrbTrigger(PlayerChoiceContext choiceContext)
    {
        await Passive(choiceContext, null);
    }

    // Passive effect implementation
    public override async Task Passive(PlayerChoiceContext choiceContext, Creature? target)
    {
        // Example: deal passive damage to random enemy
        var randomEnemy = CombatState.GetRandomMonster();
        if (randomEnemy != null)
            await randomEnemy.TakeDamage(choiceContext, (int)PassiveVal, DamageType.Normal);
    }

    // Evoke effect implementation — returns affected creatures
    public override async Task<IEnumerable<Creature>> Evoke(PlayerChoiceContext ctx)
    {
        // Example: deal evoke damage to all enemies
        var targets = CombatState.Monsters.ToList();
        foreach (var enemy in targets)
            await enemy.TakeDamage(ctx, (int)EvokeVal, DamageType.Normal);
        return targets;
    }
}
```

## Key Properties
- `DarkenedColor` — Color tint for the orb visual (hex string)
- `PassiveVal` — Numeric value for passive effect (wrap in `ModifyOrbValue()` to respect Focus)
- `EvokeVal` — Numeric value for evoke effect (wrap in `ModifyOrbValue()` to respect Focus)

## Key Methods
- `BeforeTurnEndOrbTrigger()` — Called each turn end; usually delegates to `Passive()`
- `Passive(ctx, target)` — The passive effect implementation
- `Evoke(ctx)` — The evoke effect; returns the list of affected creatures

## Focus Interaction
Always use `ModifyOrbValue(baseValue)` for values that should scale with Focus. This adds the player's current Focus to the base value. If you want a fixed value unaffected by Focus, use the raw number directly.

## Channeling Orbs
To channel an orb from a card or power effect:
```csharp
await ctx.Player.ChannelOrb(ModelDb.Orb<MyOrb>());
```

## Localization (orbs.json)
```json
{
  "MY_ORB.title": "Plasma Orb",
  "MY_ORB.description": "Passive: Deal {passive} damage to a random enemy.\nEvoke: Deal {evoke} damage to ALL enemies."
}
```

## Common Patterns
- **Damage orb**: Deal damage on passive, more on evoke (like Lightning)
- **Defensive orb**: Gain block on passive, large block on evoke (like Frost)
- **Utility orb**: Generate energy, draw cards, or apply debuffs
- **Scaling orb**: Increase in power the longer it stays channeled

## Generator
Use `generate_orb` with `passive_amount`, `evoke_amount`, and descriptions.
