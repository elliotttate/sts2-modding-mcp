# BaseLib: Mod Interop System

Soft-depend on other mods without hard DLL references:

```csharp
[ModInterop(modId: "othermod")]
public static class OtherModCompat
{
    [InteropTarget(Type = "OtherMod.SomeClass", Name = "SomeMethod")]
    public static Func<int, bool>? CheckSomething;
}
```

At runtime, if "othermod" is loaded, `CheckSomething` gets bound to the real method.
If not loaded, it stays null. Call with null check:

```csharp
if (OtherModCompat.CheckSomething?.Invoke(42) == true) { ... }
```
