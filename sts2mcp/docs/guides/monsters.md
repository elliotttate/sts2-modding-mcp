# Creating Custom Monsters

## Base Class: MonsterModel
Override key properties:
- `MinInitialHp` / `MaxInitialHp` - HP range (random each combat)
- `VisualsPath` - Path to .tscn scene file

## Move State Machine
Override `GenerateMoveStateMachine()`:
```csharp
var strike = new MoveState("STRIKE", Strike, new SingleAttackIntent(10));
var defend = new MoveState("DEFEND", Defend, new DefendIntent());
strike.FollowUpState = defend;
defend.FollowUpState = strike;
return new MonsterMoveStateMachine(new List<MonsterState> { strike, defend }, strike);
```

## Intent Types
- `SingleAttackIntent(damage)` - Single attack
- `MultiAttackIntent(damage, count)` - Multi-hit
- `DefendIntent()` - Gaining block
- `BuffIntent()` - Applying buff
- `DebuffIntent()` - Applying debuff
- `new AbstractIntent[] { ... }` - Multiple intents per turn

## Randomized Starting Move
Use `RandomBranchState` with `AddBranch(state, MoveRepeatType.CannotRepeat)`

## Scene File (.tscn)
Required nodes: Visuals (Sprite2D), Bounds (Control), CenterPos (Marker2D), IntentPos (Marker2D).
Use `generate_monster` to auto-generate the scene.

## IMPORTANT: CreateVisualsPatch
Custom static-image monsters NEED a Harmony patch on `MonsterModel.CreateVisuals`.
Use `generate_monster` which includes instructions, or see the `advanced_harmony` guide for patching details.

## Ascension Scaling
`AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, scaledValue, baseValue)`

## Console Test: `fight ENCOUNTER_NAME`
