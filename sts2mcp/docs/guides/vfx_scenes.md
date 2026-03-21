# Creating VFX Scenes

## What Are VFX Scenes?
VFX scenes are Godot `.tscn` scene files containing particle systems (`GPUParticles2D`) used for combat visual effects — card activation flashes, relic triggers, power applications, hit effects, etc. They are packed into your mod's PCK and loaded at runtime.

## Scene File Structure
A minimal VFX scene:
```
[gd_scene format=3]

[node name="MyEffect" type="Node2D"]

[node name="Particles" type="GPUParticles2D" parent="."]
emitting = false
amount = 30
lifetime = 0.5
one_shot = true
explosiveness = 0.8
visibility_rect = Rect2(-100, -100, 200, 200)
```

## Key GPUParticles2D Properties
| Property | Description | Typical Values |
|----------|------------|----------------|
| `amount` | Number of particles | 10–100 |
| `lifetime` | How long each particle lives (seconds) | 0.3–2.0 |
| `one_shot` | Play once and stop vs. loop | `true` for effects, `false` for ambient |
| `explosiveness` | 0 = spread over lifetime, 1 = all at once | 0.8–1.0 for bursts |
| `visibility_rect` | Bounding box for culling | `Rect2(-100, -100, 200, 200)` typical |
| `emitting` | Start emitting immediately | Usually `false` (trigger in code) |

## Loading and Playing VFX in Code
```csharp
// Load the scene from your mod's PCK
var vfxScene = GD.Load<PackedScene>("res://MyMod/vfx/my_effect.tscn");

// Instance and add to the scene tree
var vfx = vfxScene.Instantiate<Node2D>();
parentNode.AddChild(vfx);

// Position at the target
vfx.GlobalPosition = target.GlobalPosition;

// Start the particles
var particles = vfx.GetNode<GpuParticles2D>("Particles");
particles.Emitting = true;

// Clean up after particles finish (for one_shot effects)
await ToSignal(particles, "finished");
vfx.QueueFree();
```

## Common VFX Patterns

### Card Activation Flash
Short burst centered on the player or target:
- `amount`: 20–40
- `lifetime`: 0.3–0.5
- `one_shot`: true
- `explosiveness`: 1.0

### Damage Hit Effect
Quick burst at the damage target:
- `amount`: 10–20
- `lifetime`: 0.2–0.4
- `one_shot`: true
- `explosiveness`: 0.9

### Persistent Aura
Looping effect on a powered-up entity:
- `amount`: 15–30
- `lifetime`: 1.0–2.0
- `one_shot`: false
- `explosiveness`: 0.0

### Projectile Trail
Attach to a moving node:
- `amount`: 50
- `lifetime`: 0.3
- `one_shot`: false
- `explosiveness`: 0.0

## Packing VFX into Your Mod
1. Place `.tscn` files in your mod's resource directory (e.g., `resources/vfx/`)
2. Use `build_pck` to pack them into your mod's PCK
3. Reference via `res://YourMod/vfx/my_effect.tscn` in code

## Advanced: Custom ParticleProcessMaterial
For more control (gravity, color gradients, custom shapes), you need a `ParticleProcessMaterial` resource. The basic generator provides the scene structure; customize the material properties in the `.tscn` file directly for advanced particle behavior.

## Exploring Game VFX
Use `list_game_vfx` to see all VFX-related classes and particle systems in the base game for reference.

## Generator
Use `generate_vfx_scene` with `node_name`, `particle_count`, `lifetime`, `one_shot`, and `explosiveness`.
