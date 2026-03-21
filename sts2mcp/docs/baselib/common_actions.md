# BaseLib: CommonActions

Static helper methods that reduce boilerplate for common card/power/relic effects:

```csharp
using BaseLib.Utils;

// Attack from a card (handles targeting, damage calculation, VFX)
await CommonActions.CardAttack(this, cardPlay, choiceContext);

// Gain block from a card
await CommonActions.CardBlock(this, choiceContext);

// Draw cards
await CommonActions.Draw(player, count, choiceContext);

// Apply a power
await CommonActions.Apply<StrengthPower>(target, amount, source, card);
await CommonActions.ApplySelf<StrengthPower>(owner, amount, card);

// Card selection UI (pick from a list)
var selected = await CommonActions.SelectCards(cards, count, message, choiceContext);
var single = await CommonActions.SelectSingleCard(cards, message, choiceContext);
```

These handle all the boilerplate around PlayerChoiceContext, ValueProp flags, etc.
