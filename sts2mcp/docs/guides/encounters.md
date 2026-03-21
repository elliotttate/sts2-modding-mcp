# Creating Custom Encounters

## Base Class: EncounterModel
Encounters define which monsters spawn in a combat room. Override:
- `RoomType` — EncounterRoomType.Monster, Elite, or Boss
- `AllPossibleMonsters` — Yield all monster types that could appear (used for preloading)
- `GenerateMonsters()` — Return the actual list of `(MonsterModel, slotName?)` tuples to spawn

## Basic Encounter
```csharp
public sealed class MyEncounter : EncounterModel
{
    public override EncounterRoomType RoomType => EncounterRoomType.Monster;

    public override IEnumerable<Type> AllPossibleMonsters
    {
        get
        {
            yield return typeof(MyMonsterA);
            yield return typeof(MyMonsterB);
        }
    }

    public override IEnumerable<(MonsterModel, string?)> GenerateMonsters()
    {
        yield return (ModelDb.Monster<MyMonsterA>(), null);
        yield return (ModelDb.Monster<MyMonsterB>(), null);
    }
}
```

## Monster Slot Positioning
The second value in the tuple is a slot name. When `null`, monsters auto-position left-to-right. For specific positions, use a scene file:

1. Set `HasScene = true` on the encounter
2. Create a `.tscn` file with `Marker2D` nodes named for each slot
3. Pass the marker name as the slot: `(ModelDb.Monster<MyMonster>(), "LeftSlot")`

## Multi-Monster Encounters
For encounters with varied compositions, use RNG in `GenerateMonsters()`:
```csharp
public override IEnumerable<(MonsterModel, string?)> GenerateMonsters()
{
    yield return (ModelDb.Monster<MyBigMonster>(), null);
    // 50% chance of a second small monster
    if (Rng.NextBool())
        yield return (ModelDb.Monster<MySmallMonster>(), null);
}
```

## Difficulty Scaling (Ascension)
Check ascension level to add harder variants:
```csharp
public override IEnumerable<(MonsterModel, string?)> GenerateMonsters()
{
    yield return (ModelDb.Monster<MyMonster>(), null);
    if (RunManager.Instance.Ascension >= 2)
        yield return (ModelDb.Monster<MyExtraMonster>(), null);
}
```

## Adding to an Act
Harmony patch the act's `GenerateAllEncounters`:
```csharp
[HarmonyPatch(typeof(Underdocks), nameof(Underdocks.GenerateAllEncounters))]
public static class AddMyEncounter
{
    public static void Postfix(ref IEnumerable<EncounterModel> __result)
    {
        var list = __result.ToList();
        list.Add(ModelDb.Encounter<MyEncounter>());
        __result = list;
    }
}
```

## Act Names
- `Underdocks` — Act 1
- `Metropolis` — Act 2
- `Glory` — Act 3
- `TheHeart` — Act 4

Use `generate_act_encounter_patch` to auto-generate the Harmony patch for any act.

## Elite & Boss Encounters
Same structure, just change `RoomType`:
```csharp
public override EncounterRoomType RoomType => EncounterRoomType.Elite;
// or
public override EncounterRoomType RoomType => EncounterRoomType.Boss;
```

Elites and bosses have separate encounter pools per act.

## Console Test
```
fight MY_ENCOUNTER
```
