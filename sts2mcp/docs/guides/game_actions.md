# Creating Custom Game Actions

## What Are Game Actions?
Game actions are queued operations that execute on the game's action queue. They handle ordering, async execution, and multiplayer synchronization. Use a GameAction when you need multi-step effects that must execute in sequence, or when timing relative to other game events matters.

## When to Use GameAction vs. Direct Effects
- **Direct `await`**: Simple, immediate effects (deal damage, gain block). Fine for most card effects.
- **GameAction**: Multi-step sequences, effects that need to interleave with other queued actions, effects that trigger between turns, or effects that need multiplayer sync.

Examples where GameAction is appropriate:
- Deal damage, then if the enemy dies, draw a card
- Apply a power, wait for animations, then deal damage based on the result
- Custom turn-end effects that need specific ordering relative to other end-of-turn triggers

## Base Class: GameAction
```csharp
using MegaCrit.Sts2.Core.GameActions;

public sealed class MyAction : GameAction
{
    private readonly ulong _ownerId;
    private readonly int _amount;

    public override ulong OwnerId => _ownerId;
    public override GameActionType ActionType => GameActionType.Other;

    public MyAction(ulong ownerId, int amount)
    {
        _ownerId = ownerId;
        _amount = amount;
    }

    protected override async Task ExecuteAction()
    {
        // Your multi-step logic here
        var player = RunManager.Instance.GetPlayer(OwnerId);
        if (player == null) return;

        await player.GainBlock(_amount);
        await player.DrawCards(1);
    }

    public override INetAction ToNetAction()
    {
        throw new NotImplementedException(
            "Implement a companion INetAction before syncing over multiplayer.");
    }
}
```

## Key Properties
- `OwnerId` — The player who owns this action (their entity ID)
- `ActionType` — Category: `GameActionType.Damage`, `Block`, `Draw`, `Power`, `Other`

## Enqueueing Actions
Queue an action from a card, power, or relic:
```csharp
RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(
    new MyAction(ctx.Player.OwnerId, 5)
);
```

## Action Queue Ordering
Actions execute in FIFO order. When you enqueue during another action's execution, the new action goes to the end of the queue. This means:
1. Card plays enqueue their effects
2. Effects execute one at a time
3. Each effect can enqueue more actions (which run after all current actions)

## GameActionType Values
| Type | Use |
|------|-----|
| `Damage` | Dealing damage |
| `Block` | Gaining block |
| `Draw` | Drawing cards |
| `Power` | Applying powers |
| `Discard` | Discarding cards |
| `Exhaust` | Exhausting cards |
| `Other` | Everything else |

The type is used for logging and animation coordination, not for execution logic.

## Multiplayer Considerations
For singleplayer-only mods, leaving `ToNetAction()` as `NotImplementedException` is fine. For multiplayer support, you need a companion `INetAction` class — use `generate_net_message` to scaffold one.

## Generator
Use `generate_game_action` with a class name, description, and optional parameters list.
