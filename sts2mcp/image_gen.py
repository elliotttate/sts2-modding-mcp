"""
Image generation and postprocessing for STS2 mod assets.

Uses Google Gemini Nano Banana 2 for AI image generation, plus a
postprocessing pipeline (background removal, resizing, variants, effects)
to produce game-ready assets for cards, relics, powers, and characters.

Requires:  google-genai, Pillow, rembg, numpy
Env var:   GOOGLE_API_KEY (for image generation)
"""
from __future__ import annotations

import asyncio
import io
import os
from pathlib import Path
from typing import Literal

import numpy as np
from PIL import Image, ImageFilter

# ─── rembg lazy-load singleton ────────────────────────────────────────────────

_rembg_session = None
_rembg_session_model: str | None = None

REMBG_MODEL = "birefnet-general"


def _get_gpu_providers() -> list[str]:
    """Detect ONNX Runtime GPU support; prefer CUDA if available."""
    try:
        import onnxruntime as ort
        if "CUDAExecutionProvider" in ort.get_available_providers():
            return ["CUDAExecutionProvider", "CPUExecutionProvider"]
    except Exception:
        pass
    return ["CPUExecutionProvider"]


def _get_rembg_session():
    global _rembg_session, _rembg_session_model
    if _rembg_session is None or _rembg_session_model != REMBG_MODEL:
        from rembg import new_session
        _rembg_session = new_session(REMBG_MODEL, providers=_get_gpu_providers())
        _rembg_session_model = REMBG_MODEL
    return _rembg_session


# ─── Asset profiles ──────────────────────────────────────────────────────────

AssetType = Literal["card", "card_fullscreen", "relic", "power", "character"]

# Generation resolution per asset type (fed to the image model)
GEN_SIZES: dict[str, tuple[int, int]] = {
    "card":            (1024, 800),
    "card_fullscreen": (640,  900),
    "relic":           (512,  512),
    "power":           (512,  512),
    "character":       (512,  760),
}

# Per-asset-type variant specs: where to save, what size, background mode, effects
PROFILES: dict[str, dict] = {
    "card": {
        "bg": "opaque",
        "variants": [
            {"rel_path": "images/card_portraits/{name}.png",     "size": (250, 190)},
            {"rel_path": "images/card_portraits/big/{name}.png", "size": (1000, 760)},
        ],
    },
    "card_fullscreen": {
        "bg": "opaque",
        "variants": [
            {"rel_path": "images/card_portraits/{name}.png",     "size": (250, 350)},
            {"rel_path": "images/card_portraits/big/{name}.png", "size": (606, 852)},
        ],
    },
    "relic": {
        "bg": "transparent",
        "variants": [
            {"rel_path": "images/relics/{name}.png",         "size": (94, 94)},
            {"rel_path": "images/relics/{name}_outline.png", "size": (94, 94),  "effect": "outline"},
            {"rel_path": "images/relics/big/{name}.png",     "size": (256, 256)},
        ],
    },
    "power": {
        "bg": "transparent",
        "variants": [
            {"rel_path": "images/powers/{name}.png",     "size": (64, 64)},
            {"rel_path": "images/powers/big/{name}.png", "size": (256, 256)},
        ],
    },
    "character": {
        "variants": [
            {"rel_path": "images/charui/character_icon_{name}.png",     "size": (128, 128), "bg": "transparent"},
            {"rel_path": "images/charui/char_select_{name}.png",        "size": (132, 195), "bg": "opaque"},
            {"rel_path": "images/charui/char_select_{name}_locked.png", "size": (132, 195), "bg": "opaque", "effect": "locked"},
            {"rel_path": "images/charui/map_marker_{name}.png",         "size": (128, 128), "bg": "transparent"},
        ],
    },
}

# Aspect ratio string for Gemini, derived from gen size
def _aspect_ratio(w: int, h: int) -> str:
    """Return the closest Gemini-supported aspect ratio string."""
    ratio = w / h
    options = [
        ("1:1",  1.0),
        ("3:4",  0.75),
        ("4:3",  1.333),
        ("9:16", 0.5625),
        ("16:9", 1.778),
    ]
    return min(options, key=lambda x: abs(x[1] - ratio))[0]


# ─── Image effects ───────────────────────────────────────────────────────────

def remove_background(img: Image.Image) -> Image.Image:
    """Remove background using rembg BiRefNet. Returns RGBA."""
    from rembg import remove
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    result_bytes = remove(buf.getvalue(), session=_get_rembg_session())
    return Image.open(io.BytesIO(result_bytes)).convert("RGBA")


def make_outline(img_rgba: Image.Image, outline_px: int = 3) -> Image.Image:
    """
    Generate a white outline image from an RGBA source.
    Dilates the alpha channel, subtracts the original, fills white.
    """
    alpha = np.array(img_rgba.split()[-1])
    alpha_img = Image.fromarray(alpha)
    dilated = alpha_img.filter(ImageFilter.MaxFilter(outline_px * 2 + 1))
    dilated_arr = np.array(dilated)
    outline_alpha = np.clip(dilated_arr.astype(int) - alpha.astype(int), 0, 255).astype(np.uint8)
    result = Image.new("RGBA", img_rgba.size, (255, 255, 255, 0))
    result.putalpha(Image.fromarray(outline_alpha))
    return result


def make_locked(img: Image.Image, brightness: float = 0.55) -> Image.Image:
    """Greyscale + darken for character select locked state."""
    gray = img.convert("L").convert("RGB")
    darkened = gray.point(lambda p: int(p * brightness))
    if img.mode == "RGBA":
        darkened = darkened.convert("RGBA")
        darkened.putalpha(img.split()[-1])
    return darkened


# ─── Postprocessing pipeline ────────────────────────────────────────────────

def process_image(
    source_img: Image.Image,
    asset_type: str,
    name: str,
    project_root: Path,
) -> list[Path]:
    """
    Run the full postprocessing pipeline for a source image:
    background removal, effects, resizing, and variant generation.

    Files are written to project_root/<mod_name>/images/... matching
    the Godot res:// path convention.

    Returns list of all written file paths.
    """
    profile = PROFILES.get(asset_type)
    if not profile:
        raise ValueError(f"Unknown asset type '{asset_type}'. Options: {list(PROFILES)}")

    variants = profile.get("variants", [])
    global_bg = profile.get("bg")

    # For transparent types, remove background once and reuse
    base_transparent: Image.Image | None = None
    if global_bg == "transparent":
        base_transparent = remove_background(source_img)

    written: list[Path] = []

    for variant in variants:
        variant_bg = variant.get("bg", global_bg)
        effect = variant.get("effect")
        size: tuple[int, int] = tuple(variant["size"])
        # Validate name doesn't contain path separators or traversal
        if any(c in name for c in ("/", "\\", "..")) or name != name.strip():
            raise ValueError(f"Invalid asset name (contains path separators): {name!r}")
        rel_path: str = variant["rel_path"].replace("{name}", name)

        out_path = project_root / project_root.name / rel_path
        out_path.parent.mkdir(parents=True, exist_ok=True)

        # Pick base image
        if variant_bg == "transparent":
            if base_transparent is None:
                base = remove_background(source_img)
            else:
                base = base_transparent.copy()
        else:
            base = source_img.convert("RGB")

        # Apply effects
        if effect == "outline":
            base = make_outline(base_transparent or remove_background(source_img))
        elif effect == "locked":
            base = make_locked(base)

        # Resize
        final = base.resize(size, Image.LANCZOS)

        # Save
        final.save(out_path, format="PNG")
        written.append(out_path)

    return written


# ─── Google Gemini Nano Banana 2 image generation ───────────────────────────

MODEL_ID = "gemini-3.1-flash-image-preview"

# Prompt suffix by asset type for better game-art results
_STYLE_HINTS: dict[str, str] = {
    "card": (
        "Trading card game illustration, fantasy art style, "
        "dramatic lighting, painterly detail, vibrant colors"
    ),
    "card_fullscreen": (
        "Full portrait trading card game illustration, fantasy art style, "
        "dramatic lighting, painterly detail, vibrant colors"
    ),
    "relic": (
        "Game item icon, isolated object on pure white background, "
        "no shadows, clean edges, fantasy artifact, detailed rendering"
    ),
    "power": (
        "Game status effect icon, isolated on pure white background, "
        "no shadows, clean edges, glowing magical effect, compact composition"
    ),
    "character": (
        "Character portrait, fantasy card game style, upper body, "
        "dramatic lighting, detailed armor and features"
    ),
}


def _build_prompt(description: str, asset_type: str) -> str:
    """Combine user description with style hints for the asset type."""
    style = _STYLE_HINTS.get(asset_type, "Fantasy game art, detailed illustration")
    needs_white_bg = PROFILES.get(asset_type, {}).get("bg") == "transparent"
    bg_hint = " Isolated on pure white background, no shadow, no background elements." if needs_white_bg else ""
    return f"{description}. {style}.{bg_hint}"


async def generate_image(
    description: str,
    asset_type: str,
    model: str | None = None,
) -> Image.Image:
    """
    Generate a single image using Google Gemini Nano Banana 2.

    Args:
        description: Plain-English description of the desired art.
        asset_type: One of card, card_fullscreen, relic, power, character.
        model: Override model ID (default: gemini-3.1-flash-image-preview).

    Returns:
        PIL Image ready for postprocessing.

    Requires GOOGLE_API_KEY env var.
    """
    from google import genai
    from google.genai import types

    api_key = os.environ.get("GOOGLE_API_KEY")
    if not api_key:
        raise RuntimeError(
            "GOOGLE_API_KEY environment variable not set. "
            "Get a key at https://aistudio.google.com/apikey"
        )

    client = genai.Client(api_key=api_key)
    model_id = model or MODEL_ID

    gen_w, gen_h = GEN_SIZES.get(asset_type, (512, 512))
    aspect = _aspect_ratio(gen_w, gen_h)
    prompt = _build_prompt(description, asset_type)

    response = await asyncio.to_thread(
        client.models.generate_content,
        model=model_id,
        contents=prompt,
        config=types.GenerateContentConfig(
            response_modalities=["TEXT", "IMAGE"],
            image_config=types.ImageConfig(
                aspect_ratio=aspect,
            ),
        ),
    )

    # Extract image from response parts
    for part in response.candidates[0].content.parts:
        if part.inline_data is not None:
            img_bytes = part.inline_data.data
            return Image.open(io.BytesIO(img_bytes))

    raise RuntimeError("Gemini returned no image. The prompt may have been filtered.")


async def generate_and_process(
    description: str,
    asset_type: str,
    name: str,
    project_dir: str,
    model: str | None = None,
) -> dict:
    """
    End-to-end: generate an image and process it into all required variants.

    Args:
        description: What the art should depict.
        asset_type: card, card_fullscreen, relic, power, or character.
        name: Asset name (used in file paths, e.g. 'flame_slash').
        project_dir: Path to the mod project root directory.
        model: Optional model override.

    Returns:
        Dict with generation details and list of written file paths.
    """
    project_root = Path(project_dir)
    if not project_root.exists():
        raise FileNotFoundError(f"Project directory not found: {project_dir}")

    # Generate
    img = await generate_image(description, asset_type, model=model)

    # Postprocess into variants
    written = await asyncio.to_thread(process_image, img, asset_type, name, project_root)

    profile = PROFILES.get(asset_type, {})
    return {
        "success": True,
        "asset_type": asset_type,
        "name": name,
        "model": model or MODEL_ID,
        "variants_written": [str(p) for p in written],
        "variant_count": len(written),
        "profile": {
            "bg": profile.get("bg", "per-variant"),
            "variants": [
                {"path": v["rel_path"].replace("{name}", name), "size": v["size"]}
                for v in profile.get("variants", [])
            ],
        },
    }


async def process_existing_image(
    image_path: str,
    asset_type: str,
    name: str,
    project_dir: str,
) -> dict:
    """
    Process an existing image file into game-ready variants.
    Useful when the user already has source art.

    Args:
        image_path: Path to source image file (PNG, JPG, etc.)
        asset_type: card, card_fullscreen, relic, power, or character.
        name: Asset name for file paths.
        project_dir: Path to the mod project root directory.

    Returns:
        Dict with list of written file paths.
    """
    img_path = Path(image_path)
    if not img_path.exists():
        raise FileNotFoundError(f"Image not found: {image_path}")

    project_root = Path(project_dir)
    if not project_root.exists():
        raise FileNotFoundError(f"Project directory not found: {project_dir}")

    img = Image.open(img_path)
    written = await asyncio.to_thread(process_image, img, asset_type, name, project_root)

    profile = PROFILES.get(asset_type, {})
    return {
        "success": True,
        "source": str(img_path),
        "asset_type": asset_type,
        "name": name,
        "variants_written": [str(p) for p in written],
        "variant_count": len(written),
        "profile": {
            "bg": profile.get("bg", "per-variant"),
            "variants": [
                {"path": v["rel_path"].replace("{name}", name), "size": v["size"]}
                for v in profile.get("variants", [])
            ],
        },
    }
