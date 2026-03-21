# BaseLib: Custom Card Variables

## ExhaustiveVar
Card's value decreases each time it's played (like Exhaustive keyword).
```csharp
new ExhaustiveVar(startingValue).WithTooltip()
```

## PersistVar
Value decreases each time played within a single turn.
```csharp
new PersistVar(startingValue).WithTooltip()
```

## RefundVar
Refunds energy when the card is played.
```csharp
new RefundVar(refundAmount).WithTooltip()
```

All support `.WithTooltip()` for automatic hover tip generation.
