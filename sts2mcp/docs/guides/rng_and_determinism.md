# RNG & Determinism

## Game RNG System
STS2 uses separate Rng instances per system for determinism:
```csharp
// Access RNG streams:
var rng = RunManager.Instance.RunState.RunRngSet;
var cardRng = rng.GetRng(RunRngType.CardReward);
var relicRng = rng.GetRng(RunRngType.RelicReward);
var combatRng = rng.GetRng(RunRngType.Combat);
```

## Rng Class Usage
```csharp
var rng = new Rng(seed);
int value = rng.NextInt(min, max);      // Inclusive range
float fValue = rng.NextFloat();          // 0.0 to 1.0
bool coin = rng.NextBool();             // 50/50
T item = rng.Choose(list);              // Random element
rng.Shuffle(list);                       // In-place shuffle
```

## Seed Management
```csharp
// Get current seed:
var seed = RunManager.Instance.RunState.Seed;

// Deterministic sub-seeds (for isolated RNG):
var mySeed = seed.GetHashCode() ^ "mymod_feature".GetHashCode();
var myRng = new Rng(mySeed);
```

## Seed Offsetting (for multiple uses)
```csharp
// Pattern from race-mod: offset by context to avoid RNG correlation
int baseSeed = runSeed;
int cardSeed = baseSeed + 100;   // Card rewards
int relicSeed = baseSeed + 200;  // Relic rewards
int combatSeed = baseSeed + 300; // Combat encounters
```

## Determinism Tips
- Never use System.Random — it's not deterministic across saves
- Always use the game's Rng class
- If you need mod-specific RNG, derive from run seed
- RNG state persists across save/load — don't create new Rng instances per frame
- Multiplayer: all players share the same seed for synchronized randomness
