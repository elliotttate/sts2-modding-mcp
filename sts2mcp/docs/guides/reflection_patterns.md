# Reflection Patterns for STS2 Modding

## Harmony's AccessTools (Preferred)
```csharp
// Fields
var field = AccessTools.Field(typeof(NMapDrawings), "_drawingStates");
var value = field.GetValue(instance);

// Ref access (fast, reusable)
var fieldRef = AccessTools.FieldRefAccess<NMapDrawings, IList>("_drawingStates");
IList states = fieldRef(instance);

// Properties
var prop = AccessTools.Property(typeof(CardReward), "Options");
var options = prop.GetValue(instance);

// Methods
var method = AccessTools.Method(typeof(MyClass), "PrivateMethod", new[] { typeof(int) });
method.Invoke(instance, new object[] { 42 });
```

## HarmonyLib Traverse (Chainable)
```csharp
// Read private field
var states = Traverse.Create(mapDrawings)
    .Field("_drawingStates")
    .GetValue<IList>();

// Nested access
var playerId = Traverse.Create(stateObj)
    .Field("playerId")
    .GetValue<int>();
```

## Cached Reflection (Performance)
```csharp
// Cache at class level — avoid repeated lookups in hot paths
private static readonly FieldInfo DrawStatesField =
    AccessTools.Field(typeof(NMapDrawings), "_drawingStates");
private static readonly PropertyInfo OptionsProp =
    AccessTools.Property(typeof(CardReward), "Options");

public static IList GetDrawStates(NMapDrawings instance) =>
    (IList)DrawStatesField.GetValue(instance);
```

## Struct Mutation via __makeref
```csharp
// HoverTip is a struct — can't modify via normal reflection
HoverTip tip = existingTip;
var tipRef = __makeref(tip);
typeof(HoverTip).GetField("_title", BindingFlags.NonPublic | BindingFlags.Instance)
    .SetValueDirect(tipRef, "New Title");
```

## Inheritance Chain Traversal
```csharp
public static FieldInfo FindField(Type type, string name)
{
    while (type != null)
    {
        var field = type.GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field != null) return field;
        type = type.BaseType;
    }
    return null;
}
```

## Common Pitfalls
- Always use `BindingFlags.NonPublic | BindingFlags.Instance` for private fields
- Cache FieldInfo/PropertyInfo as `static readonly` — reflection lookup is expensive
- Check for `null` — fields may be renamed between game versions
- Use `AccessTools` over raw `typeof().GetField()` — it searches base classes automatically
