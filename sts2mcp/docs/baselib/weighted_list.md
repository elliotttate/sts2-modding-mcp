# BaseLib: WeightedList<T>

IList<T> with weighted random selection:

```csharp
var list = new WeightedList<string>();
list.Add("common", 70);     // 70% chance
list.Add("uncommon", 25);   // 25% chance
list.Add("rare", 5);        // 5% chance

// Pick random (weighted)
string result = list.GetRandom(rng);

// Pick and remove
string result = list.GetRandom(rng, removeAfter: true);
```
