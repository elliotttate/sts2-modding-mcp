# Image Generation & Art Pipeline

## Overview
The MCP server can generate game-ready art assets using Google Gemini Nano Banana 2
and process them into all required size variants automatically. This covers cards,
relics, powers, and characters.

## Setup

### Requirements
- `GOOGLE_API_KEY` environment variable (get one at https://aistudio.google.com/apikey)
- Python packages: `google-genai`, `Pillow`, `rembg`, `numpy` (installed with the MCP)

### Optional: GPU Acceleration
Background removal (rembg) will automatically use CUDA if `onnxruntime-gpu` is installed.
Falls back to CPU otherwise.

## Tools

### `generate_art`
End-to-end: describe what you want, get all image variants written to your mod project.

```
generate_art(
  description="A flaming obsidian dagger with glowing purple runes",
  asset_type="relic",
  name="runic_dagger",
  project_dir="E:/mods/MyMod"
)
```

This will:
1. Generate an image via Nano Banana 2 (aspect ratio auto-selected)
2. Remove the background (for relics/powers)
3. Resize and write all variants:
   - `MyMod/images/relics/runic_dagger.png` (94x94)
   - `MyMod/images/relics/runic_dagger_outline.png` (94x94, white outline)
   - `MyMod/images/relics/big/runic_dagger.png` (256x256)

### `process_art`
Process an existing image (your own art, a downloaded reference, etc.) into game-ready
variants. Same postprocessing pipeline, no AI generation.

```
process_art(
  image_path="E:/art/my_card_art.png",
  asset_type="card",
  name="flame_slash",
  project_dir="E:/mods/MyMod"
)
```

### `list_art_profiles`
Show all asset types and their variant specifications. Useful for understanding what
files will be generated and at what sizes.

## Asset Type Reference

### card
- **Background**: Opaque (white)
- **Generation size**: 1024x800
- **Variants**:
  - `images/card_portraits/{name}.png` (250x190) - Card display
  - `images/card_portraits/big/{name}.png` (1000x760) - Full-size view

### card_fullscreen
- **Background**: Opaque (white)
- **Generation size**: 640x900
- **Variants**:
  - `images/card_portraits/{name}.png` (250x350) - Card display
  - `images/card_portraits/big/{name}.png` (606x852) - Full-size view

### relic
- **Background**: Transparent (auto background removal)
- **Generation size**: 512x512
- **Variants**:
  - `images/relics/{name}.png` (94x94) - Standard icon
  - `images/relics/{name}_outline.png` (94x94) - White outline for hover
  - `images/relics/big/{name}.png` (256x256) - Large inventory view

### power
- **Background**: Transparent (auto background removal)
- **Generation size**: 512x512
- **Variants**:
  - `images/powers/{name}.png` (64x64) - Combat HUD icon
  - `images/powers/big/{name}.png` (256x256) - Tooltip view

### character
- **Background**: Per-variant (mixed)
- **Generation size**: 512x760
- **Variants**:
  - `images/charui/character_icon_{name}.png` (128x128) - Transparent
  - `images/charui/char_select_{name}.png` (132x195) - Opaque, selection screen
  - `images/charui/char_select_{name}_locked.png` (132x195) - Greyed + darkened
  - `images/charui/map_marker_{name}.png` (128x128) - Transparent, map icon

## Postprocessing Pipeline

All images go through these steps:

1. **Background removal** (transparent types only) - Uses rembg with BiRefNet model.
   Runs once per source image, reused across variants.
2. **Effects** (per variant):
   - `outline` - Dilates alpha channel, subtracts original, fills white. Used for
     relic hover outlines.
   - `locked` - Converts to greyscale and darkens (55% brightness). Used for
     locked character select state.
3. **Resize** - LANCZOS interpolation to target dimensions.
4. **Save** - PNG format into the mod project's Godot resource directory.

## File Placement

Generated files follow the Godot `res://` path convention. For a mod project at
`E:/mods/MyMod`, files are written to `E:/mods/MyMod/MyMod/images/...` so that
the Godot resource path is `res://MyMod/images/...`.

These paths match what the game expects when loading entity images. After generating
art, use `build_pck` to package the images into a `.pck` file
for deployment.

## Prompt Tips

The tool automatically appends style hints per asset type, but your description
drives the result. Tips:

- **Be specific about the subject**: "A cracked hourglass leaking golden sand" not
  just "an hourglass"
- **Include material/color**: "obsidian blade with purple runes", "crystalline shield
  with blue glow"
- **For relics/powers**: Describe the object in isolation. The tool adds
  "isolated on pure white background" automatically for transparent types.
- **For cards**: Describe a scene or action. Card art is displayed landscape with an
  opaque background.
- **Model override**: Pass `model="gemini-2.5-flash-image"` to use the original
  Nano Banana, or `model="gemini-3-pro-image-preview"` for Nano Banana Pro
  (studio-quality 4K).

## Workflow Integration

Typical mod art workflow:

```
1. generate_card(...)          # Generate card code + localization
2. generate_art(               # Generate matching card art
     description="...",
     asset_type="card",
     name="my_card",
     project_dir="..."
   )
3. build_pck(...)              # Package images into .pck
4. build_mod(...)              # Compile the C# code
5. install_mod(...)            # Deploy to game
```

Or for a relic with custom art you already have:

```
1. generate_relic(...)         # Generate relic code
2. process_art(                # Process your existing art
     image_path="my_art.png",
     asset_type="relic",
     name="my_relic",
     project_dir="..."
   )
3. build_pck(...)
4. build_mod(...)
5. install_mod(...)
```
