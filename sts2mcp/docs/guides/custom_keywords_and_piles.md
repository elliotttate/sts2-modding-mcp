# Custom Keywords & Pile Types

## Custom Keywords (Requires BaseLib)
Create new card keywords like "Stitch", "Woven", "Dissolve":

```csharp
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Models.Cards;

public static class MyKeyword
{
    [CustomEnum]
    public static CardKeyword CustomType;  // Auto-assigned at load
}
```

### Adding to Cards
```csharp
public override HashSet<CardKeyword> Keywords => new() { MyKeyword.CustomType };
```

### Keyword Tooltip (localization)
In `card_keywords.json`:
```json
{
    "MY_KEYWORD.title": "My Keyword",
    "MY_KEYWORD.description": "Cards with this keyword do something special."
}
```

### Keyword Properties
```csharp
// Control keyword display position and behavior:
[CustomEnum]
[KeywordProperties(position: AutoKeywordPosition.Before)]
public static CardKeyword CustomType;
```

## Custom Pile Types (Requires BaseLib)
Create new card destinations beyond Hand/Draw/Discard/Exhaust:

```csharp
public static class MyPile
{
    [CustomEnum]
    public static PileType CustomType;
}
```

### Routing Cards to Custom Piles
Patch `CardModel.GetResultPileType` to route specific cards:
```csharp
[HarmonyPostfix]
[HarmonyPatch(typeof(CardModel), "GetResultPileType")]
static void RouteToCustomPile(CardModel __instance, ref PileType __result)
{
    if (ShouldGoToMyPile(__instance))
        __result = MyPile.CustomType;
}
```

### Handling Custom Pile Cards
Use hooks to process cards in your custom pile:
```csharp
// In your relic or power:
public override async Task AfterTurnEnd(CombatState state, CombatSide side)
{
    var pile = state.GetPile(MyPile.CustomType);
    foreach (var card in pile.Cards.ToList())
    {
        // Process cards in custom pile
        await CardPileCmd.Move(card, PileType.Hand);
    }
}
```
