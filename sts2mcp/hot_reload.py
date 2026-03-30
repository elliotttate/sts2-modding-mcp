"""Project-aware hot reload helpers shared by tools and the file watcher."""

from __future__ import annotations

import re
from pathlib import Path
from typing import Any

from .project_workflow import (
    DEFAULT_CONFIG_EXCLUSIONS,
    _resolve_project_context,
    build_and_deploy_project,
)

WATCHED_EXTENSIONS: tuple[str, ...] = (
    ".cs",
    ".json",
    ".tscn",
    ".tres",
    ".png",
    ".jpg",
    ".jpeg",
    ".webp",
    ".wav",
    ".ogg",
    ".mp3",
    ".gd",
    ".gdc",
    ".shader",
    ".res",
    ".scn",
)
# All non-C# extensions. Note: .json is included here for mtime watching but
# gets special handling in determine_reload_tier() (localization vs config vs data).
RESOURCE_RELOAD_EXTENSIONS: tuple[str, ...] = tuple(ext for ext in WATCHED_EXTENSIONS if ext not in {".cs"})

CLASS_WITH_ATTRIBUTES_RE = re.compile(
    r"(?P<attrs>(?:\s*\[[^\]]+\]\s*)*)"
    r"(?:public|internal|protected|private|sealed|abstract|partial|static|\s)*"
    r"class\s+(?P<name>\w+)\s*(?::\s*(?P<bases>[^{\r\n]+))?",
    re.MULTILINE,
)
POOL_ATTRIBUTE_RE = re.compile(r"\[\s*Pool\s*\(\s*typeof\s*\(\s*(?P<pool>[\w.]+)\s*\)\s*\)\s*\]")
NAMESPACE_RE = re.compile(r"\bnamespace\s+([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*[;{]")
# Matches ModHelper.AddModelToPool<PoolType, ModelType>() and AddModelToPool<PoolType, ModelType>()
ADD_MODEL_TO_POOL_RE = re.compile(
    r"(?:ModHelper\s*\.\s*)?AddModelToPool\s*<\s*(?P<pool>[\w.]+)\s*,\s*(?P<model>[\w.]+)\s*>\s*\(",
)


NON_RESOURCE_JSON_NAMES: frozenset[str] = frozenset({
    "mod_manifest.json",
    "mod_config.json",
    "launchSettings.json",
})


def determine_reload_tier(changed_files: list[str], *, has_pck: bool = False) -> int:
    """Determine the safest hot reload tier from the changed file set.

    Returns 0 when no files match any known pattern (nothing to reload).
    When *has_pck* is True, localization JSON is classified as tier 3 because
    PCK-backed localization requires a PCK remount to take effect.
    """

    has_cs = False
    has_patch_cs_only = True
    has_resource = False
    has_loc_json = False
    has_any_known = False

    for file_path in changed_files:
        normalized = file_path.lower().replace("\\", "/")
        suffix = Path(file_path).suffix.lower()
        if suffix == ".cs":
            has_cs = True
            has_any_known = True
            if "/patches/" not in normalized and "patch" not in Path(file_path).stem.lower():
                has_patch_cs_only = False
            continue

        if suffix == ".json":
            basename = Path(file_path).name.lower()
            if "localization" in normalized:
                has_loc_json = True
                has_any_known = True
            elif basename not in NON_RESOURCE_JSON_NAMES:
                has_resource = True
                has_any_known = True
            continue

        if suffix in RESOURCE_RELOAD_EXTENSIONS:
            has_resource = True
            has_any_known = True

    if not has_any_known:
        return 0
    if has_resource:
        return 3
    if has_cs and not has_patch_cs_only:
        return 2
    if has_loc_json:
        return 3 if has_pck else 2
    if has_cs and has_patch_cs_only:
        return 1
    return 2


def find_deployed_dll(deploy_result: dict[str, Any]) -> str:
    """Extract the main deployed mod DLL path from build/deploy output.

    Prefers the DLL whose name matches the project's assembly_name (possibly
    with an ``_hr<stamp>`` suffix from version stamping).  Falls back to the
    first non-excluded DLL found.
    """

    copied = deploy_result.get("copied_files") or deploy_result.get("artifacts_copied") or []
    mod_dir = Path(deploy_result.get("mod_dir") or deploy_result.get("install_dir") or "")

    # Derive expected assembly name prefix from project context
    project_info = deploy_result.get("project") or {}
    assembly_name = project_info.get("assembly_name", "")
    prefix = assembly_name.lower() if assembly_name else ""

    def _resolve(entry: str) -> str | None:
        """Resolve a copied file entry to an absolute path that exists."""
        if not isinstance(entry, str) or not entry.lower().endswith(".dll"):
            return None
        candidate = Path(entry)
        if candidate.is_absolute() and candidate.exists():
            return str(candidate)
        if mod_dir:
            resolved = mod_dir / entry
            if resolved.exists():
                return str(resolved)
        return None

    # First pass: prefer the DLL matching the project assembly name (with optional _hr stamp)
    if prefix:
        for entry in copied:
            stem = Path(entry).stem.lower()
            if stem == prefix or stem.startswith(prefix + "_hr"):
                path = _resolve(entry)
                if path:
                    return path

    # Second pass: any .dll from copied files
    for entry in copied:
        path = _resolve(entry)
        if path:
            return path

    # Fallback: glob the mod directory
    if mod_dir and mod_dir.exists():
        # Prefer assembly-name match
        if prefix:
            for dll in sorted(mod_dir.glob("*.dll"), key=lambda p: p.stat().st_mtime, reverse=True):
                if dll.stem.lower() == prefix or dll.stem.lower().startswith(prefix + "_hr"):
                    return str(dll)
        for dll in sorted(mod_dir.glob("*.dll")):
            if dll.stem not in DEFAULT_CONFIG_EXCLUSIONS:
                return str(dll)

    return ""


def find_deployed_pck(deploy_result: dict[str, Any]) -> str:
    """Extract the deployed PCK path from build/deploy output."""

    pck_info = deploy_result.get("pck", {})
    if isinstance(pck_info, dict):
        pck_path = pck_info.get("pck_path", "")
        if isinstance(pck_path, str) and pck_path:
            return pck_path

    mod_dir = deploy_result.get("mod_dir") or deploy_result.get("install_dir", "")
    if mod_dir:
        mod_path = Path(mod_dir)
        if mod_path.exists():
            pcks = sorted(mod_path.glob("*.pck"))
            if pcks:
                return str(pcks[0])
    return ""


def discover_pool_registrations(project_dir: str | Path) -> dict[str, Any]:
    """Infer hot-reload pool registrations from source code.

    Discovers pool registrations from two patterns:
    1. ``[Pool(typeof(SharedRelicPool))]`` attributes on class declarations
    2. ``ModHelper.AddModelToPool<SharedRelicPool, MyRelic>()`` calls in code
    """

    context = _resolve_project_context(project_dir)
    code_root = context.project_dir / "Code"
    registrations: list[dict[str, str]] = []
    warnings: list[str] = []
    seen: set[tuple[str, str]] = set()
    files_scanned: list[str] = []

    if not code_root.exists():
        return {
            "success": True,
            "project": context.as_dict(),
            "pool_registrations": registrations,
            "files_scanned": files_scanned,
            "warnings": ["Code directory not found"],
        }

    for cs_file in sorted(code_root.rglob("*.cs")):
        rel_path = cs_file.relative_to(context.project_dir).as_posix()
        files_scanned.append(rel_path)
        try:
            source = cs_file.read_text(encoding="utf-8-sig")
        except OSError as exc:
            warnings.append(f"Could not read {rel_path}: {exc}")
            continue

        namespace_match = NAMESPACE_RE.search(source)
        namespace = namespace_match.group(1) if namespace_match else ""

        # Pattern 1: [Pool(typeof(...))] attributes on class declarations
        for match in CLASS_WITH_ATTRIBUTES_RE.finditer(source):
            attrs = match.group("attrs") or ""
            pool_matches = list(POOL_ATTRIBUTE_RE.finditer(attrs))
            if not pool_matches:
                continue

            class_name = match.group("name")
            model_type = f"{namespace}.{class_name}" if namespace else class_name
            for pool_match in pool_matches:
                pool_type = pool_match.group("pool").split(".")[-1]
                key = (pool_type, model_type)
                if key in seen:
                    continue
                seen.add(key)
                registrations.append({"pool_type": pool_type, "model_type": model_type})

        # Pattern 2: ModHelper.AddModelToPool<Pool, Model>() calls
        # Use short name only — the model type may be imported from a different
        # namespace than the file's own namespace. The C# bridge resolves by
        # short name anyway.
        for match in ADD_MODEL_TO_POOL_RE.finditer(source):
            pool_type = match.group("pool").split(".")[-1]
            model_name = match.group("model").split(".")[-1]
            key = (pool_type, model_name)
            if key in seen:
                continue
            seen.add(key)
            registrations.append({"pool_type": pool_type, "model_type": model_name})

    return {
        "success": True,
        "project": context.as_dict(),
        "pool_registrations": registrations,
        "files_scanned": files_scanned,
        "warnings": warnings,
    }


def _resolve_pool_reload_mode(
    *,
    auto_detect_pools: bool,
    pool_registrations: list[dict[str, str]] | None,
) -> tuple[list[dict[str, str]] | None, str]:
    """Resolve the pool registration payload to send to the bridge.

    ``None`` omits the JSON-RPC field so the bridge performs assembly
    reflection discovery. An explicit empty list disables bridge-side
    auto-discovery while keeping the request unambiguous.
    """

    if pool_registrations is not None:
        return pool_registrations, "explicit"
    if auto_detect_pools:
        return None, "bridge_auto"
    return [], "disabled"


def build_deploy_and_hot_reload_project(
    project_dir: str | Path,
    *,
    mods_dir: str | Path,
    mod_name: str = "",
    configuration: str = "Debug",
    tier: int | None = None,
    build_pck_first: bool | None = None,
    game_dir: str | Path | None = None,
    auto_detect_pools: bool = True,
    pool_registrations: list[dict[str, str]] | None = None,
    changed_files: list[str] | None = None,
    cancel_event: Any = None,
) -> dict[str, Any]:
    """Build, deploy, and hot reload a mod project in one project-aware step."""

    context = _resolve_project_context(project_dir)
    if build_pck_first is None:
        build_pck_first = context.has_pck

    deploy_result = build_and_deploy_project(
        project_dir,
        mods_dir=mods_dir,
        mod_name=mod_name,
        configuration=configuration,
        build_pck_first=build_pck_first,
        game_dir=game_dir,
        stamp_version=True,
        cancel_event=cancel_event,
    )
    if not deploy_result.get("success"):
        return deploy_result

    dll_path = find_deployed_dll(deploy_result)
    if not dll_path:
        return {
            **deploy_result,
            "success": False,
            "error": "Could not determine deployed DLL path for hot reload",
        }

    # Smarter tier default: if changed_files are known, use file-based detection
    # instead of always defaulting to tier 3 for PCK projects.
    if tier is not None:
        effective_tier = tier
    elif changed_files:
        effective_tier = determine_reload_tier(changed_files, has_pck=context.has_pck)
        if effective_tier == 0:
            effective_tier = 2  # Fallback if no recognized types
    else:
        effective_tier = 3 if build_pck_first else 2
    effective_tier = max(1, min(3, effective_tier))
    pck_path = find_deployed_pck(deploy_result) if effective_tier >= 3 else ""

    # When pool_registrations are explicitly provided, pass them through.
    # Otherwise either let the C# bridge do assembly-level reflection discovery
    # or send an explicit empty list to disable auto-discovery.
    # Python-side discover_pool_registrations remains a preview/dry-run helper.
    effective_pool_registrations, pool_discovery_mode = _resolve_pool_reload_mode(
        auto_detect_pools=auto_detect_pools,
        pool_registrations=pool_registrations,
    )

    from . import bridge_client

    raw_result = bridge_client.hot_reload(
        dll_path=dll_path,
        tier=effective_tier,
        pck_path=pck_path,
        pool_registrations=effective_pool_registrations,
    )

    # Unwrap JSON-RPC response wrapper: bridge returns {"result": {...}, "id": N}
    hot_reload_result = raw_result
    if isinstance(raw_result, dict) and isinstance(raw_result.get("result"), dict):
        hot_reload_result = raw_result["result"]

    reload_success = hot_reload_result.get("success", False)
    # Also treat error key as failure (connection errors, etc.)
    if "error" in hot_reload_result:
        reload_success = False

    response: dict[str, Any] = {
        **deploy_result,
        "success": bool(deploy_result.get("success")) and bool(reload_success),
        "hot_reload": hot_reload_result,
        "hot_reload_inputs": {
            "dll_path": dll_path,
            "pck_path": pck_path,
            "tier": effective_tier,
            "pool_registrations": effective_pool_registrations,
            "auto_detect_pools": pool_discovery_mode == "bridge_auto",
            "pool_discovery_mode": pool_discovery_mode,
        },
    }
    return response
