# BaseLib: Custom Card Variables

## ExhaustiveVar
Card's value decreases each time it's played across the entire combat (like the Exhaustive keyword).
```csharp
new ExhaustiveVar(startingValue).WithTooltip()
```

## PersistVar
Value decreases each turn the card is played. Resets between turns.
```csharp
new PersistVar(startingValue).WithTooltip()
```

## RefundVar
Refunds energy when the card is played.
```csharp
new RefundVar(refundAmount).WithTooltip()
```

All support `.WithTooltip()` for automatic hover tip generation. The tooltip uses localization keys in `static_hover_tips.json` with the format `PREFIX-VARNAME.title` / `.description`.

## DynamicVar Tooltips

Any `DynamicVar` can have a tooltip attached:
```csharp
// Auto-generates key from prefix and variable name:
new DamageVar(8).WithTooltip()

// Or specify a custom localization key:
new DamageVar(8).WithTooltip("MY_CUSTOM_KEY", "static_hover_tips")
```

Tooltip keys follow the pattern `PREFIX-VARNAME` (uppercase) and are looked up in the `static_hover_tips` localization table.
