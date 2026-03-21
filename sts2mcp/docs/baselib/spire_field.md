# BaseLib: SpireField<TKey, TVal>

Attach custom data to game objects without modifying their classes:

```csharp
// Define a field
private static readonly SpireField<Creature, int> _customCounter =
    new SpireField<Creature, int>(() => 0);  // default factory

// Use it
_customCounter[creature] = 5;
int count = _customCounter[creature];  // returns 5, or 0 for unset creatures
```

Uses `ConditionalWeakTable` internally - data is garbage collected with the key object.
