# Combat System Deep Dive

## Intent System
Enemies declare intentions each turn via `AbstractIntent` subclasses:
```csharp
// Read enemy intents:
foreach (var intent in monster.NextMove.Intents)
{
    if (intent is AttackIntent attack)
    {
        int singleDmg = attack.GetSingleDamage(targets, monster);
        int totalDmg = attack.GetTotalDamage(targets, monster);
        int hits = attack.Repeats;
    }
}
```

## Damage Pipeline
Source -> ModifyDamage hooks -> Block reduction -> HP loss:
```
1. Base damage from DynamicVar
2. ModifyDamageAdditive hooks (flat +/-)
3. ModifyDamageMultiplicative hooks (percentage)
4. Block absorption
5. HP reduction
6. AfterDamageReceived hooks fire
```

## Command APIs (async Task-based)
```csharp
// Damage
await DamageCmd.Attack(amount).FromCard(card, cardPlay).Execute(ctx);
await DamageCmd.Attack(amount).AllEnemies().Execute(ctx);

// Block
await CreatureCmd.GainBlock(creature, amount, ValueProp.Powered, card);

// Powers
await PowerCmd.Apply<StrengthPower>(target, stacks, source, card);
await PowerCmd.ModifyAmount<WeakPower>(target, -1);

// Cards
await CardPileCmd.Draw(player, count, ctx);
await CardPileCmd.Add(card, PileType.Discard);
await CardCmd.Exhaust(card);
await CardCmd.AutoPlay(ctx, card, target);
await CardCmd.Upgrade(card);

// Player
await PlayerCmd.GainEnergy(player, amount);
await PlayerCmd.EndTurn(player);

// Creatures
await CreatureCmd.Add(monsterModel, state, CombatSide.Enemy, null);
await CreatureCmd.TriggerAnim(creature, "attack");
await CreatureCmd.Heal(creature, amount);

// SFX
await SfxCmd.Play("card_attack");
```

## Combat History
```csharp
var history = CombatManager.Instance.History;
foreach (var entry in history.Entries)
{
    if (entry is DamageReceivedEntry dmg)
    {
        // dmg.Target, dmg.Source, dmg.Amount, dmg.WasBlocked
    }
}
// Subscribe to changes:
history.Changed += OnHistoryChanged;
```

## Action Queue
All combat actions execute through the action queue:
```csharp
// Queue a custom action:
RunManager.Instance.ActionQueueSet.QueueAction(new MyAction());

// Actions are async Tasks that execute sequentially
```

## CombatState Access
```csharp
var state = CombatManager.Instance.CombatState;
var player = LocalContext.GetMe(state);
var enemies = state.GetTeammatesOf(CombatSide.Enemy);
var allCreatures = state.AllCreatures;
```
