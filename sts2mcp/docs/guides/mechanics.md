# Creating Cross-Cutting Mechanics

## What Is a "Mechanic"?
A mechanic is a new keyword or concept that spans multiple entity types — cards apply it, powers track it, relics reward it, and tooltips explain it. Examples from the base game:
- **Poison**: Applied by cards, tracked by a power, ticks down each turn
- **Mantra**: Accumulated by cards/relics, triggers a buff at threshold
- **Vulnerable/Weak**: Applied by cards, tracked as powers, modify damage calculations

## Components of a Mechanic
A complete mechanic typically needs:
1. **Tracking Power** — A `PowerModel` that holds the mechanic's stack count and implements its effect
2. **Sample Cards** — Cards that apply the mechanic (to demonstrate usage patterns)
3. **Sample Relic** — A relic that interacts with the mechanic (rewards it, synergizes)
4. **Localization** — Entries for the power, cards, relic, and keyword tooltip
5. **Keyword Tooltip** (optional) — A hover tooltip that explains the keyword on cards

## When to Use generate_mechanic vs. Separate Generators
- **Use `generate_mechanic`**: When you want a full keyword system from scratch — it scaffolds all components with consistent naming and cross-references
- **Use separate generators**: When adding a single card/relic that uses an existing mechanic, or when you need more control over individual components

## Anatomy of a Generated Mechanic
Given keyword "Corrode" with description "At end of turn, lose X HP":

### 1. Tracking Power (CorrodePower)
```csharp
public sealed class CorrodePower : PowerModel
{
    public override PowerStackType StackType => PowerStackType.Intensity;

    public override async Task AfterTurnEnd(PlayerChoiceContext ctx)
    {
        await Owner.LoseHp(Amount);
    }
}
```

### 2. Sample Card (CorrosiveStrike)
```csharp
public sealed class CorrosiveStrike : CardModel
{
    public override async Task OnPlay(PlayerChoiceContext ctx)
    {
        await ctx.Target.TakeDamage(ctx, MoveVal, DamageType.Normal);
        await ctx.Target.ApplyPower<CorrodePower>(MagicVal);
    }
}
```

### 3. Sample Relic (CorrosionAmplifier)
```csharp
public sealed class CorrosionAmplifier : RelicModel
{
    public override async Task AfterPowerApplied(PowerModel power, int amount)
    {
        if (power is CorrodePower)
        {
            Flash();
            // Bonus effect when Corrode is applied
        }
    }
}
```

## Design Patterns

### Threshold Mechanics (like Mantra)
Power accumulates stacks; at a threshold, triggers a big effect and resets:
```csharp
public override async Task OnApply()
{
    if (Amount >= 10)
    {
        Amount -= 10;
        await Owner.ApplyPower<BigBuffPower>(1);
    }
}
```

### Tick-Down Mechanics (like Poison)
Power deals damage each turn and decreases:
```csharp
public override async Task BeforeTurnStart(PlayerChoiceContext ctx)
{
    await Owner.LoseHp(Amount);
    Amount--;
    if (Amount <= 0) await RemoveSelf();
}
```

### Modifier Mechanics (like Vulnerable)
Power modifies damage calculations:
```csharp
public override int ModifyDamageReceivedAdditive(int damage)
{
    return damage + (Amount * 2);  // Take 2 extra damage per stack
}
```

### Reward Mechanics
Relic grants benefits when the mechanic triggers enough times:
```csharp
private int _triggerCount;

public override async Task AfterPowerApplied(PowerModel power, int amount)
{
    if (power is MyMechanicPower)
    {
        _triggerCount += amount;
        if (_triggerCount >= 5)
        {
            Flash();
            _triggerCount -= 5;
            await Owner.DrawCards(1);
        }
    }
}
```

## Adding a Keyword Tooltip
Use `generate_custom_tooltip` to create a hover tooltip so the keyword is explained when players hover over it in card descriptions. This registers the keyword with `HoverTipManager`.

## Generator
Use `generate_mechanic` with `keyword_name`, `keyword_description`, and optional sample card/relic names.
