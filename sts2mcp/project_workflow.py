"""Project-aware workflow helpers for STS2 mod generation."""

from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Iterable

from .pck_builder import build_pck


ENTITY_LOCALIZATION_FILES = {
    "CardModel": "cards.json",
    "CustomCardModel": "cards.json",
    "RelicModel": "relics.json",
    "CustomRelicModel": "relics.json",
    "PowerModel": "powers.json",
    "CustomPowerModel": "powers.json",
    "PotionModel": "potions.json",
    "CustomPotionModel": "potions.json",
    "MonsterModel": "monsters.json",
    "EncounterModel": "encounters.json",
    "EventModel": "events.json",
    "OrbModel": "orbs.json",
    "EnchantmentModel": "enchantments.json",
    "CharacterModel": "characters.json",
    "CustomCharacterModel": "characters.json",
    "AncientModel": "ancients.json",
    "AncientEventModel": "ancients.json",
    "CustomAncientModel": "ancients.json",
}

RESOURCE_REFERENCE_RE = re.compile(r'res://([^"\')\s]+)')
CLASS_DECLARATION_RE = re.compile(r"class\s+(?P<name>\w+)\s*:\s*(?P<base>\w+)\b")
DEFAULT_CONFIG_EXCLUSIONS = {"GodotSharp", "0Harmony", "sts2"}
MANAGED_ARTIFACT_SUFFIXES = (".dll", ".pdb", ".deps.json", ".runtimeconfig.json")


def _to_snake_case(name: str) -> str:
    interim = re.sub(r"(.)([A-Z][a-z]+)", r"\1_\2", name)
    return re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", interim).lower()


def _to_screaming_snake(name: str) -> str:
    return _to_snake_case(name).upper()


def _unique_strings(values: Iterable[str | None]) -> list[str]:
    seen: set[str] = set()
    ordered: list[str] = []
    for value in values:
        if not value:
            continue
        if value in seen:
            continue
        seen.add(value)
        ordered.append(value)
    return ordered


def _write_text(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(content)


def _write_json(path: Path, payload: dict[str, Any]) -> None:
    _write_text(path, json.dumps(payload, indent=2, ensure_ascii=False) + "\n")


def _extract_project_property(csproj_text: str, property_name: str) -> str:
    match = re.search(rf"<{property_name}>(.*?)</{property_name}>", csproj_text, re.IGNORECASE | re.DOTALL)
    return match.group(1).strip() if match else ""


@dataclass(slots=True)
class ProjectContext:
    project_dir: Path
    manifest_path: Path | None
    manifest: dict[str, Any]
    csproj_path: Path | None
    csproj_paths: list[Path]
    namespace: str
    assembly_name: str
    resource_root_name: str
    resource_dir: Path
    localization_dir: Path
    localization_dirs: list[Path]
    install_mod_name: str
    pck_name: str
    has_pck: bool
    warnings: list[str] = field(default_factory=list)

    def as_dict(self) -> dict[str, Any]:
        return {
            "project_dir": str(self.project_dir),
            "manifest_path": str(self.manifest_path) if self.manifest_path else "",
            "csproj_path": str(self.csproj_path) if self.csproj_path else "",
            "csproj_paths": [str(path) for path in self.csproj_paths],
            "namespace": self.namespace,
            "assembly_name": self.assembly_name,
            "resource_root_name": self.resource_root_name,
            "resource_dir": str(self.resource_dir),
            "localization_dir": str(self.localization_dir),
            "localization_dirs": [str(path) for path in self.localization_dirs],
            "install_mod_name": self.install_mod_name,
            "pck_name": self.pck_name,
            "has_pck": self.has_pck,
            "warnings": list(self.warnings),
        }


def _is_relative_to(path: Path, root: Path) -> bool:
    return path == root or root in path.parents


def _resolve_within_project(
    project_root: Path,
    relative_path: str | Path,
    *,
    base_dir: Path | None = None,
    description: str = "path",
) -> Path:
    raw = Path(relative_path)
    if raw.is_absolute():
        raise ValueError(f"{description} must be relative to the project root: {raw}")

    base = base_dir if base_dir is not None else project_root
    resolved = (base / raw).resolve()
    root = project_root.resolve()
    if not _is_relative_to(resolved, root):
        raise ValueError(f"{description} escapes the project root: {relative_path}")
    return resolved.relative_to(root)


def _read_text_file(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig") if path.exists() else ""


def _artifact_base_name(path: Path) -> str:
    name = path.name
    for suffix in MANAGED_ARTIFACT_SUFFIXES:
        if name.endswith(suffix):
            return name[: -len(suffix)]
    return path.stem


def _is_managed_artifact_name(name: str) -> bool:
    return any(name.endswith(suffix) for suffix in MANAGED_ARTIFACT_SUFFIXES)


def _detect_deployed_dependency_versions(mods_dir: Path) -> dict[str, str]:
    """Scan the game's mods directory for deployed dependency DLLs and read their versions.

    Returns a dict like {"BaseLib": "0.1.9.0"} for use as MSBuild properties.
    """
    versions: dict[str, str] = {}
    if not mods_dir.exists():
        return versions
    for mod_folder in mods_dir.iterdir():
        if not mod_folder.is_dir():
            continue
        for dll in mod_folder.glob("*.dll"):
            name = dll.stem
            if name in versions or name in DEFAULT_CONFIG_EXCLUSIONS:
                continue
            # Only detect well-known dependency DLLs (avoid scanning every mod's main DLL)
            if name not in ("BaseLib",):
                continue
            try:
                result = subprocess.run(
                    ["dotnet", "--roll-forward", "LatestMajor",
                     "exec", "--runtimeconfig", str(dll.parent / f"{name}.runtimeconfig.json"),
                     str(dll)],
                    capture_output=True, text=True, timeout=5,
                )
            except Exception:
                pass
            # Simpler approach: read the PE version info via AssemblyName
            try:
                probe = subprocess.run(
                    ["dotnet", "script", "eval",
                     f"System.Reflection.AssemblyName.GetAssemblyName(@\"{dll}\").Version"],
                    capture_output=True, text=True, timeout=10,
                )
            except Exception:
                pass
            # Most reliable: read the version from the DLL's metadata using a regex on the binary
            try:
                import re as _re
                raw = dll.read_bytes()
                # Look for AssemblyVersion attribute value in the metadata
                # Format: "Version=X.Y.Z.W" near the assembly name
                pattern = _re.compile(
                    name.encode() + rb".*?Version=(\d+\.\d+\.\d+\.\d+)", _re.DOTALL
                )
                match = pattern.search(raw[:8192])  # Check first 8KB
                if match:
                    versions[name] = match.group(1).decode()
            except Exception:
                pass
    return versions


def _resolve_project_context(project_dir: str | Path) -> ProjectContext:
    project = Path(project_dir)
    if not project.exists():
        raise FileNotFoundError(f"Project directory not found: {project_dir}")

    manifest_path = project / "mod_manifest.json"
    manifest: dict[str, Any] = {}
    warnings: list[str] = []
    if manifest_path.exists():
        try:
            manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
        except json.JSONDecodeError as exc:
            warnings.append(f"mod_manifest.json is invalid JSON: {exc}")
    else:
        # Auto-discover non-standard manifest names (e.g., HermitMod.json)
        manifest_path = None
        for json_file in sorted(project.glob("*.json")):
            if json_file.name.lower() in ("mod_manifest.json", "mod_config.json"):
                continue
            if json_file.name.lower().endswith((".deps.json", ".runtimeconfig.json")):
                continue
            try:
                candidate = json.loads(json_file.read_text(encoding="utf-8-sig"))
                if isinstance(candidate, dict) and "id" in candidate and "has_dll" in candidate:
                    manifest = candidate
                    manifest_path = json_file
                    break
            except (json.JSONDecodeError, OSError):
                continue
        if manifest_path is None:
            warnings.append("mod_manifest.json not found (also checked *.json in project root)")

    csproj_paths = sorted(project.glob("*.csproj"))
    csproj_path = next(iter(csproj_paths), None)
    csproj_text = csproj_path.read_text(encoding="utf-8-sig") if csproj_path else ""
    if not csproj_path:
        warnings.append("No .csproj file found in project root")
    elif len(csproj_paths) > 1:
        warnings.append(
            "Multiple .csproj files found in project root; using "
            f"{csproj_path.name} as the primary project"
        )

    assembly_name = (
        _extract_project_property(csproj_text, "AssemblyName")
        or (csproj_path.stem if csproj_path else "")
        or project.name
    )
    namespace = (
        _extract_project_property(csproj_text, "RootNamespace")
        or assembly_name
        or manifest.get("pck_name", "")
        or project.name
    )
    install_mod_name = manifest.get("id", "") or project.name

    child_loc_dirs = sorted(project.rglob("localization/eng"))
    loc_root = child_loc_dirs[0].parents[1].name if child_loc_dirs else ""
    if len(child_loc_dirs) > 1:
        warnings.append(
            "Multiple localization directories found; using "
            f"{child_loc_dirs[0].relative_to(project).as_posix()} as the primary localization root"
        )
    resource_root_candidates = _unique_strings(
        [
            manifest.get("pck_name", ""),
            namespace,
            assembly_name,
            loc_root,
            project.name,
        ]
    )
    resource_root_name = ""
    for candidate in resource_root_candidates:
        if (project / candidate).is_dir():
            resource_root_name = candidate
            break
    if not resource_root_name:
        resource_root_name = resource_root_candidates[0] if resource_root_candidates else project.name
        warnings.append(f"Could not find resource root on disk; using '{resource_root_name}'")

    resource_dir = project / resource_root_name
    localization_dir = resource_dir / "localization" / "eng"
    pck_name = manifest.get("pck_name", "") or resource_root_name
    has_pck = bool(manifest["has_pck"]) if "has_pck" in manifest else resource_dir.exists()

    if manifest.get("has_pck") and not resource_dir.exists():
        warnings.append("Manifest expects a PCK but the resource directory is missing")
    if resource_dir.exists() and manifest.get("pck_name") and manifest["pck_name"] != resource_root_name:
        warnings.append(
            f"Manifest pck_name '{manifest['pck_name']}' differs from resource root '{resource_root_name}'"
        )
    # Godot's virtual filesystem is case-sensitive. The game's ModManager looks
    # for localization at res://{manifest.id}/localization/... so the PCK's
    # base_prefix (derived from pck_name) must match the manifest id exactly.
    mod_id = manifest.get("id", "")
    if mod_id and pck_name and mod_id != pck_name:
        warnings.append(
            f"PCK base_prefix '{pck_name}' does not match manifest id '{mod_id}'. "
            f"Godot's res:// paths are case-sensitive — localization files in the PCK "
            f"will not be found by the game. Set pck_name to '{mod_id}' in the manifest."
        )

    return ProjectContext(
        project_dir=project,
        manifest_path=manifest_path,
        manifest=manifest,
        csproj_path=csproj_path,
        csproj_paths=csproj_paths,
        namespace=namespace,
        assembly_name=assembly_name,
        resource_root_name=resource_root_name,
        resource_dir=resource_dir,
        localization_dir=localization_dir,
        localization_dirs=child_loc_dirs,
        install_mod_name=install_mod_name,
        pck_name=pck_name,
        has_pck=has_pck,
        warnings=warnings,
    )


def inspect_project(project_dir: str | Path) -> dict[str, Any]:
    """Inspect a mod project and infer its code/resource layout."""
    try:
        context = _resolve_project_context(project_dir)
    except FileNotFoundError as exc:
        return {"success": False, "error": str(exc)}

    info = context.as_dict()
    info["success"] = True
    return info


def _normalize_generated_files(
    generation_output: dict[str, Any],
    context: ProjectContext,
) -> tuple[dict[Path, str], dict[Path, dict[str, str]], list[dict[str, Any]], list[str]]:
    files_to_write: dict[Path, str] = {}
    localization_updates: dict[Path, dict[str, str]] = {}
    project_edits: list[dict[str, Any]] = []
    notes: list[str] = list(generation_output.get("notes", []))

    if generation_output.get("source") is not None and generation_output.get("file_name"):
        folder = generation_output.get("folder", "")
        rel_path = _resolve_within_project(
            context.project_dir,
            Path(folder) / generation_output["file_name"],
            description="generated file path",
        )
        files_to_write[rel_path] = str(generation_output["source"])

    if generation_output.get("scene") is not None:
        scene_name = generation_output.get("scene_file_name") or generation_output.get("file_name")
        if scene_name:
            scene_folder = generation_output.get("scene_folder") or context.resource_root_name
            rel_path = _resolve_within_project(
                context.project_dir,
                Path(scene_folder) / scene_name,
                description="generated scene path",
            )
            files_to_write[rel_path] = str(generation_output["scene"])

    if isinstance(generation_output.get("files"), dict):
        for rel_path, content in generation_output["files"].items():
            normalized = _resolve_within_project(
                context.project_dir,
                rel_path,
                description="generated file path",
            )
            files_to_write[normalized] = str(content)

    if isinstance(generation_output.get("localization"), dict):
        for file_name, entries in generation_output["localization"].items():
            if not isinstance(entries, dict):
                continue
            normalized = _resolve_within_project(
                context.project_dir,
                file_name,
                base_dir=context.localization_dir,
                description="localization file path",
            )
            target = localization_updates.setdefault(normalized, {})
            for key, value in entries.items():
                target[str(key)] = str(value)

    if generation_output.get("entries") is not None and generation_output.get("file_name"):
        entries = generation_output["entries"]
        if isinstance(entries, dict):
            normalized = _resolve_within_project(
                context.project_dir,
                generation_output["file_name"],
                base_dir=context.localization_dir,
                description="localization file path",
            )
            target = localization_updates.setdefault(normalized, {})
            for key, value in entries.items():
                target[str(key)] = str(value)

    raw_project_edits = generation_output.get("project_edits")
    if isinstance(raw_project_edits, list):
        for edit in raw_project_edits:
            if isinstance(edit, dict):
                project_edits.append(edit)

    for step in generation_output.get("next_steps", []):
        notes.append(str(step))

    for extra_note_key in ("image_note", "usage"):
        if generation_output.get(extra_note_key):
            notes.append(str(generation_output[extra_note_key]))

    return files_to_write, localization_updates, project_edits, notes


def _get_existing_text(project_dir: Path, rel_path: Path) -> str:
    path = project_dir / rel_path
    return _read_text_file(path)


def _load_planned_text(
    *,
    context: ProjectContext,
    rel_path: Path,
    planned_files: dict[Path, str],
    write_modes: dict[Path, str],
) -> str:
    if rel_path in planned_files:
        return planned_files[rel_path]

    planned_files[rel_path] = _get_existing_text(context.project_dir, rel_path)
    write_modes.setdefault(rel_path, "edit")
    return planned_files[rel_path]


def _apply_using_edit(
    *,
    context: ProjectContext,
    edit: dict[str, Any],
    planned_files: dict[Path, str],
    write_modes: dict[Path, str],
    conflicts: list[str],
) -> None:
    namespace = str(edit.get("namespace", "")).strip()
    if not namespace:
        conflicts.append("project edit ensure_using is missing a namespace")
        return

    rel_path = _resolve_within_project(
        context.project_dir,
        edit.get("path", "Code/ModEntry.cs"),
        description="project edit path",
    )
    content = _load_planned_text(
        context=context,
        rel_path=rel_path,
        planned_files=planned_files,
        write_modes=write_modes,
    )
    using_line = f"using {namespace};"
    if using_line in content:
        return

    lines = content.splitlines()
    insert_index = 0
    while insert_index < len(lines) and lines[insert_index].startswith("using "):
        insert_index += 1
    lines.insert(insert_index, using_line)
    planned_files[rel_path] = "\n".join(lines).rstrip("\n") + "\n"


def _apply_insert_text_edit(
    *,
    context: ProjectContext,
    edit: dict[str, Any],
    planned_files: dict[Path, str],
    write_modes: dict[Path, str],
    conflicts: list[str],
) -> None:
    path_value = edit.get("path", "Code/ModEntry.cs")
    anchor = str(edit.get("anchor", ""))
    content_to_insert = str(edit.get("content", ""))
    if not anchor or not content_to_insert:
        conflicts.append("project edit insert_text requires both anchor and content")
        return

    rel_path = _resolve_within_project(
        context.project_dir,
        path_value,
        description="project edit path",
    )
    content = _load_planned_text(
        context=context,
        rel_path=rel_path,
        planned_files=planned_files,
        write_modes=write_modes,
    )
    if content_to_insert in content:
        return

    occurrence = str(edit.get("occurrence", "last")).lower()
    anchor_index = content.rfind(anchor) if occurrence == "last" else content.find(anchor)
    if anchor_index < 0:
        conflicts.append(
            f"Could not apply insert_text edit to {rel_path.as_posix()}: anchor '{anchor}' was not found"
        )
        return

    position = str(edit.get("position", "after")).lower()
    insert_at = anchor_index if position == "before" else anchor_index + len(anchor)
    planned_files[rel_path] = content[:insert_at] + content_to_insert + content[insert_at:]


def _apply_replace_text_edit(
    *,
    context: ProjectContext,
    edit: dict[str, Any],
    planned_files: dict[Path, str],
    write_modes: dict[Path, str],
    conflicts: list[str],
) -> None:
    find = str(edit.get("find", ""))
    replace = str(edit.get("replace", ""))
    if not find:
        conflicts.append("project edit replace_text requires a non-empty find value")
        return

    rel_path = _resolve_within_project(
        context.project_dir,
        edit.get("path", "Code/ModEntry.cs"),
        description="project edit path",
    )
    content = _load_planned_text(
        context=context,
        rel_path=rel_path,
        planned_files=planned_files,
        write_modes=write_modes,
    )
    if find not in content:
        conflicts.append(
            f"Could not apply replace_text edit to {rel_path.as_posix()}: '{find}' was not found"
        )
        return

    count = edit.get("count")
    planned_files[rel_path] = content.replace(find, replace, int(count)) if count else content.replace(find, replace)


def _apply_json_merge_edit(
    *,
    context: ProjectContext,
    edit: dict[str, Any],
    planned_files: dict[Path, str],
    write_modes: dict[Path, str],
    conflicts: list[str],
) -> None:
    values = edit.get("values")
    if not isinstance(values, dict):
        conflicts.append("project edit json_merge requires an object `values` payload")
        return

    rel_path = _resolve_within_project(
        context.project_dir,
        edit.get("path", "mod_manifest.json"),
        description="project edit path",
    )
    content = _load_planned_text(
        context=context,
        rel_path=rel_path,
        planned_files=planned_files,
        write_modes=write_modes,
    )
    current_data: dict[str, Any] = {}
    if content.strip():
        try:
            loaded = json.loads(content)
        except json.JSONDecodeError as exc:
            conflicts.append(f"Could not merge JSON into {rel_path.as_posix()}: {exc}")
            return
        if not isinstance(loaded, dict):
            conflicts.append(f"Could not merge JSON into {rel_path.as_posix()}: file is not a JSON object")
            return
        current_data = loaded

    current_data.update(values)
    planned_files[rel_path] = json.dumps(current_data, indent=2, ensure_ascii=False) + "\n"


def _apply_project_edits(
    *,
    context: ProjectContext,
    project_edits: list[dict[str, Any]],
    planned_files: dict[Path, str],
    write_modes: dict[Path, str],
    conflicts: list[str],
) -> None:
    handlers = {
        "ensure_using": _apply_using_edit,
        "insert_text": _apply_insert_text_edit,
        "replace_text": _apply_replace_text_edit,
        "json_merge": _apply_json_merge_edit,
    }

    for edit in project_edits:
        edit_type = str(edit.get("type", "")).strip()
        handler = handlers.get(edit_type)
        if handler is None:
            conflicts.append(f"Unknown project edit type: {edit_type or '<missing>'}")
            continue
        try:
            handler(
                context=context,
                edit=edit,
                planned_files=planned_files,
                write_modes=write_modes,
                conflicts=conflicts,
            )
        except ValueError as exc:
            conflicts.append(str(exc))


def apply_generator_outputs(
    project_dir: str | Path,
    generation_outputs: Iterable[dict[str, Any]],
    *,
    overwrite: bool = False,
    dry_run: bool = False,
) -> dict[str, Any]:
    """Apply one or more generator outputs into an existing mod project."""
    try:
        context = _resolve_project_context(project_dir)
    except FileNotFoundError as exc:
        return {"success": False, "error": str(exc)}

    planned_files: dict[Path, str] = {}
    planned_localization: dict[Path, dict[str, str]] = {}
    write_modes: dict[Path, str] = {}
    project_edits: list[dict[str, Any]] = []
    notes: list[str] = []
    conflicts: list[str] = []

    for output in generation_outputs:
        try:
            files_to_write, loc_updates, output_edits, output_notes = _normalize_generated_files(output, context)
        except ValueError as exc:
            conflicts.append(str(exc))
            continue

        notes.extend(output_notes)
        project_edits.extend(output_edits)

        for rel_path, content in files_to_write.items():
            existing = planned_files.get(rel_path)
            if existing is not None and existing != content:
                message = f"Multiple generator outputs produced different content for {rel_path.as_posix()}"
                conflicts.append(message)
                if overwrite:
                    planned_files[rel_path] = content
                continue

            actual_path = context.project_dir / rel_path
            if actual_path.exists():
                current = _read_text_file(actual_path)
                if current != content and not overwrite:
                    conflicts.append(f"Refusing to overwrite existing file {rel_path.as_posix()}")
            planned_files[rel_path] = content
            write_modes[rel_path] = "replace"

        for rel_path, entries in loc_updates.items():
            target = planned_localization.setdefault(rel_path, {})
            for key, value in entries.items():
                if key in target and target[key] != value:
                    message = f"Multiple localization entries for {rel_path.as_posix()}:{key}"
                    conflicts.append(message)
                    if overwrite:
                        target[key] = value
                    continue
                target[key] = value

    for rel_path, entries in sorted(planned_localization.items(), key=lambda item: item[0].as_posix()):
        current_data: dict[str, Any] = {}
        current_text = _get_existing_text(context.project_dir, rel_path)
        if current_text.strip():
            try:
                loaded = json.loads(current_text)
            except json.JSONDecodeError as exc:
                conflicts.append(f"Invalid JSON in {rel_path.as_posix()}: {exc}")
                continue

            if not isinstance(loaded, dict):
                conflicts.append(f"{rel_path.as_posix()} is not a JSON object")
                continue
            current_data = loaded

        for key, value in entries.items():
            if key in current_data and current_data[key] != value and not overwrite:
                conflicts.append(f"Refusing to overwrite localization key {rel_path.as_posix()}:{key}")
                continue
            current_data[key] = value

        planned_files[rel_path] = json.dumps(current_data, indent=2, ensure_ascii=False) + "\n"
        write_modes.setdefault(rel_path, "localization")

    _apply_project_edits(
        context=context,
        project_edits=project_edits,
        planned_files=planned_files,
        write_modes=write_modes,
        conflicts=conflicts,
    )

    if conflicts:
        return {
            "success": False,
            "project": context.as_dict(),
            "dry_run": dry_run,
            "written_files": [],
            "updated_localization_files": [],
            "localization_entry_counts": {
                rel_path.as_posix(): len(entries)
                for rel_path, entries in sorted(planned_localization.items(), key=lambda item: item[0].as_posix())
            },
            "skipped": [],
            "conflicts": conflicts,
            "notes": notes,
            "planned_files": sorted(path.as_posix() for path in planned_files),
            "project_edits": project_edits,
        }

    written_files: list[str] = []
    skipped_files: list[str] = []
    localization_results: dict[str, int] = {}

    for rel_path, content in sorted(planned_files.items(), key=lambda item: item[0].as_posix()):
        actual_path = context.project_dir / rel_path
        current = _read_text_file(actual_path)
        if current == content:
            skipped_files.append(rel_path.as_posix())
            continue

        if not dry_run:
            actual_path.parent.mkdir(parents=True, exist_ok=True)
            _write_text(actual_path, content)
        written_files.append(rel_path.as_posix())

    for rel_path, entries in sorted(planned_localization.items(), key=lambda item: item[0].as_posix()):
        localization_results[rel_path.as_posix()] = len(entries)

    return {
        "success": True,
        "project": context.as_dict(),
        "dry_run": dry_run,
        "written_files": written_files,
        "updated_localization_files": sorted(path.as_posix() for path in planned_localization),
        "localization_entry_counts": localization_results,
        "skipped": skipped_files,
        "conflicts": [],
        "notes": notes,
        "project_edits": project_edits,
    }


def apply_generator_output(
    project_dir: str | Path,
    generation_output: dict[str, Any],
    *,
    overwrite: bool = False,
    dry_run: bool = False,
) -> dict[str, Any]:
    """Apply a single generator output into an existing mod project."""
    return apply_generator_outputs(
        project_dir,
        [generation_output],
        overwrite=overwrite,
        dry_run=dry_run,
    )


def _collect_build_artifacts(context: ProjectContext, configuration: str) -> list[Path]:
    bin_dir = context.project_dir / "bin" / configuration
    if not bin_dir.exists():
        return []

    dll_candidates = sorted(
        (
            path
            for path in bin_dir.rglob("*.dll")
            if path.is_file() and _artifact_base_name(path) not in DEFAULT_CONFIG_EXCLUSIONS
        ),
        key=lambda item: item.stat().st_mtime if item.exists() else 0,
        reverse=True,
    )
    if not dll_candidates:
        return []

    primary = next((item for item in dll_candidates if item.stem == context.assembly_name), dll_candidates[0])
    output_dir = primary.parent
    artifacts: list[Path] = []
    for path in sorted(output_dir.iterdir(), key=lambda item: item.name.lower()):
        if not path.is_file():
            continue
        if not _is_managed_artifact_name(path.name):
            continue
        if _artifact_base_name(path) in DEFAULT_CONFIG_EXCLUSIONS:
            continue
        artifacts.append(path)

    artifacts.sort(
        key=lambda item: (
            0 if _artifact_base_name(item) == context.assembly_name else 1,
            item.name.lower(),
        )
    )
    return artifacts


def build_project(
    project_dir: str | Path,
    *,
    configuration: str = "Debug",
    timeout: int = 180,
    game_dir: str | Path | None = None,
    stamp_version: bool = False,
    cancel_event: Any = None,
) -> dict[str, Any]:
    """Build a mod project with dotnet using project-aware defaults.

    Args:
        stamp_version: If True, pass a unique AssemblyVersion to each build so
            that .NET's AssemblyLoadContext does not return a cached assembly
            on hot reload.  The version encodes the current time as
            ``1.MMDD.HHmm.ssfff`` which is unique per millisecond.
        cancel_event: Optional ``threading.Event``.  When set during a build,
            the subprocess is killed and a cancelled result is returned.
    """
    try:
        context = _resolve_project_context(project_dir)
    except FileNotFoundError as exc:
        return {"success": False, "error": str(exc)}

    if not context.csproj_path:
        return {"success": False, "error": "No .csproj file found in project directory"}

    validation = validate_project(project_dir)
    env = None
    if game_dir:
        env = {**os.environ, "STS2_GAME_DIR": str(game_dir)}

    build_cmd = ["dotnet", "build", str(context.csproj_path), "-c", configuration]

    # Build-time assembly version alignment: detect deployed dependency versions
    # and pass them to the build to prevent mismatches (e.g., mod builds against
    # BaseLib 0.2.1.0 from NuGet but game has BaseLib 0.1.0.0 deployed).
    if game_dir:
        dep_versions = _detect_deployed_dependency_versions(Path(game_dir) / "mods")
        for dep_name, dep_version in dep_versions.items():
            # Pass as MSBuild properties that .csproj can use for PackageReference version
            build_cmd.append(f"-p:{dep_name}Version={dep_version}")

    if stamp_version:
        import datetime
        now = datetime.datetime.now()
        # Format: 1.MMDD.HHmm.ssfff — unique per millisecond, valid .NET version
        version = f"1.{now.month:02d}{now.day:02d}.{now.hour:02d}{now.minute:02d}.{now.second:02d}{now.microsecond // 1000:03d}"
        build_cmd.append(f"-p:AssemblyVersion={version}")
        # Also stamp the assembly name so the default ALC treats each reload
        # as a distinct assembly.  Collectible ALCs have cross-ALC type identity
        # issues that break ModelDb.Inject, entity cloning, and runtime casts.
        # Using a unique assembly name in the default ALC avoids all of this
        # at the cost of a small per-reload memory leak (~1 assembly per reload).
        stamp = now.strftime("%H%M%S%f")[:8]  # HHMMSSff
        build_cmd.append(f"-p:AssemblyName={context.assembly_name}_hr{stamp}")

    try:
        if cancel_event is not None:
            # Use Popen so we can poll the cancel event during the build
            proc = subprocess.Popen(
                build_cmd,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
                cwd=str(context.project_dir),
                env=env,
            )
            try:
                deadline = __import__("time").monotonic() + timeout
                while proc.poll() is None:
                    remaining = deadline - __import__("time").monotonic()
                    if remaining <= 0:
                        proc.kill()
                        proc.wait(timeout=5)
                        return {"success": False, "error": f"Build timed out after {timeout} seconds"}
                    if cancel_event.is_set():
                        proc.kill()
                        proc.wait(timeout=5)
                        return {"success": False, "cancelled": True, "error": "Build cancelled (new changes detected)"}
                    # Poll every 200ms for cancel/timeout
                    try:
                        proc.wait(timeout=0.2)
                    except subprocess.TimeoutExpired:
                        pass
                stdout = proc.stdout.read() if proc.stdout else ""
                stderr = proc.stderr.read() if proc.stderr else ""
                returncode = proc.returncode
            finally:
                if proc.poll() is None:
                    proc.kill()
                    proc.wait(timeout=5)
        else:
            result = subprocess.run(
                build_cmd,
                capture_output=True,
                text=True,
                cwd=str(context.project_dir),
                timeout=timeout,
                env=env,
            )
            stdout = result.stdout
            stderr = result.stderr
            returncode = result.returncode
    except subprocess.TimeoutExpired:
        return {"success": False, "error": f"Build timed out after {timeout} seconds"}
    except FileNotFoundError:
        return {"success": False, "error": "dotnet CLI not found. Install .NET SDK 9.0."}

    artifacts = _collect_build_artifacts(context, configuration)
    return {
        "success": returncode == 0,
        "stdout": stdout,
        "stderr": stderr,
        "return_code": returncode,
        "configuration": configuration,
        "project": context.as_dict(),
        "artifact_files": [str(path) for path in artifacts],
        "validation": validation,
    }


def build_project_pck(
    project_dir: str | Path,
    *,
    output_path: str = "",
    convert_pngs: bool = True,
) -> dict[str, Any]:
    """Build a PCK for a mod project using its manifest/resource layout."""
    try:
        context = _resolve_project_context(project_dir)
    except FileNotFoundError as exc:
        return {"success": False, "error": str(exc)}

    if not context.resource_dir.exists():
        return {
            "success": False,
            "error": f"Resource directory not found: {context.resource_dir}",
            "project": context.as_dict(),
        }

    validation = validate_project(project_dir)
    if not validation.get("valid"):
        return {
            "success": False,
            "error": "Project validation failed; PCK build skipped",
            "project": context.as_dict(),
            "validation": validation,
        }

    target = Path(output_path) if output_path else context.project_dir / f"{context.pck_name}.pck"
    result = build_pck(
        source_dir=str(context.resource_dir),
        output_path=str(target),
        base_prefix=f"{context.pck_name}/",
        convert_pngs=convert_pngs,
    )
    result["project"] = context.as_dict()
    result["source_dir"] = str(context.resource_dir)
    result["base_prefix"] = f"{context.pck_name}/"
    result["validation"] = validation
    return result


def deploy_project(
    project_dir: str | Path,
    *,
    mods_dir: str | Path,
    mod_name: str = "",
    configuration: str = "Debug",
    include_pck: bool | None = None,
) -> dict[str, Any]:
    """Deploy already-built project outputs into the game's mods directory."""
    try:
        context = _resolve_project_context(project_dir)
    except FileNotFoundError as exc:
        return {"success": False, "error": str(exc)}

    validation = validate_project(project_dir)
    if not validation.get("valid"):
        return {
            "success": False,
            "error": "Project validation failed; refusing to deploy",
            "project": context.as_dict(),
            "mod_dir": str(Path(mods_dir) / (mod_name or context.install_mod_name)),
            "copied_files": [],
            "missing": [],
            "removed_stale_files": [],
            "validation": validation,
        }

    install_name = mod_name or context.install_mod_name
    target_dir = Path(mods_dir) / install_name
    target_dir.mkdir(parents=True, exist_ok=True)

    copied_files: list[str] = []
    missing: list[str] = []
    runtime_artifacts = _collect_build_artifacts(context, configuration)
    stale_removed: list[str] = []
    desired_names: set[str] = {artifact.name.lower() for artifact in runtime_artifacts}

    for existing in sorted(target_dir.iterdir()):
        if not existing.is_file():
            continue
        if existing.name.lower() == "mod_manifest.json":
            continue
        if existing.name.lower() == "mod_image.png":
            continue
        if existing.suffix.lower() == ".pck":
            continue
        if _is_managed_artifact_name(existing.name.lower()) and existing.name.lower() not in desired_names:
            try:
                existing.unlink()
                stale_removed.append(existing.name)
            except PermissionError:
                # File locked (e.g., game is running) — skip stale removal
                pass

    # Clean up old hot-reload stamped DLLs beyond the most recent 2.
    # Each hot reload produces a uniquely-named _hrHHMMSSff.dll that accumulates
    # while the game is running (locked by the default ALC).
    import re as _re
    hr_dlls = sorted(
        (f for f in target_dir.glob("*_hr*.dll") if _re.search(r"_hr\d{6,8}\.dll$", f.name)),
        key=lambda p: p.stat().st_mtime,
        reverse=True,
    )
    for old_hr in hr_dlls[2:]:
        if old_hr.name.lower() not in desired_names:
            try:
                old_hr.unlink()
                stale_removed.append(old_hr.name)
            except PermissionError:
                pass  # Still locked by running game

    for artifact in runtime_artifacts:
        destination = target_dir / artifact.name
        try:
            shutil.copy2(artifact, destination)
            copied_files.append(destination.name)
        except PermissionError:
            # File locked (e.g., old DLL held by running game) — skip
            pass

    if not copied_files and context.manifest.get("has_dll", True):
        missing.append(f"No build output found under bin/{configuration} for assembly {context.assembly_name}")

    if context.manifest_path and context.manifest_path.exists():
        shutil.copy2(context.manifest_path, target_dir / "mod_manifest.json")
        copied_files.append("mod_manifest.json")
        desired_names.add("mod_manifest.json")
    else:
        missing.append("mod_manifest.json")

    if include_pck is None:
        include_pck = context.has_pck

    preferred_pck = context.project_dir / f"{context.pck_name}.pck"
    fallback = next(iter(sorted(context.project_dir.glob("*.pck"))), None)
    pck_path = preferred_pck if preferred_pck.exists() else fallback
    expected_pck_name = pck_path.name.lower() if include_pck and pck_path and pck_path.exists() else ""
    for existing_pck in sorted(target_dir.glob("*.pck")):
        if expected_pck_name and existing_pck.name.lower() == expected_pck_name:
            continue
        try:
            existing_pck.unlink()
            stale_removed.append(existing_pck.name)
        except PermissionError:
            pass
    if include_pck:
        if pck_path and pck_path.exists():
            try:
                shutil.copy2(pck_path, target_dir / pck_path.name)
                copied_files.append(pck_path.name)
                desired_names.add(pck_path.name.lower())
            except PermissionError:
                # PCK locked by running game — skip, existing PCK still works
                pass
        else:
            missing.append(f"{context.pck_name}.pck")

    for image_name in ("mod_image.png", "icon.png"):
        image_path = context.project_dir / image_name
        if image_path.exists():
            shutil.copy2(image_path, target_dir / "mod_image.png")
            copied_files.append("mod_image.png")
            desired_names.add("mod_image.png")
            break

    return {
        "success": not missing,
        "project": context.as_dict(),
        "mod_dir": str(target_dir),
        "copied_files": copied_files,
        "missing": missing,
        "removed_stale_files": stale_removed,
        "validation": validation,
    }


def build_and_deploy_project(
    project_dir: str | Path,
    *,
    mods_dir: str | Path,
    mod_name: str = "",
    configuration: str = "Debug",
    build_pck_first: bool | None = None,
    game_dir: str | Path | None = None,
    stamp_version: bool = False,
    cancel_event: Any = None,
) -> dict[str, Any]:
    """Build, optionally pack, and deploy a project in one step."""
    validation = validate_project(project_dir)
    if not validation.get("valid"):
        return {
            "success": False,
            "validation": validation,
            "error": "Project validation failed; build and deployment skipped",
        }

    context = _resolve_project_context(project_dir)
    if build_pck_first is None:
        build_pck_first = context.has_pck

    # Run C# build and PCK build in parallel when both are needed
    pck_result: dict[str, Any] | None = None
    if build_pck_first:
        from concurrent.futures import ThreadPoolExecutor, Future

        with ThreadPoolExecutor(max_workers=2) as executor:
            build_future: Future[dict[str, Any]] = executor.submit(
                build_project, project_dir, configuration=configuration,
                game_dir=game_dir, stamp_version=stamp_version,
                cancel_event=cancel_event,
            )
            pck_future: Future[dict[str, Any]] = executor.submit(
                build_project_pck, project_dir,
            )
            build_result = build_future.result()
            pck_result = pck_future.result()
    else:
        build_result = build_project(
            project_dir, configuration=configuration, game_dir=game_dir,
            stamp_version=stamp_version, cancel_event=cancel_event,
        )

    if not build_result.get("success"):
        return {
            "success": False,
            "build": build_result,
            "validation": validation,
            "error": "Build failed; deployment skipped",
        }

    if build_pck_first and pck_result and not pck_result.get("success"):
        return {
            "success": False,
            "build": build_result,
            "pck": pck_result,
            "validation": validation,
            "error": "PCK build failed; deployment skipped",
        }

    deploy_result = deploy_project(
        project_dir,
        mods_dir=mods_dir,
        mod_name=mod_name,
        configuration=configuration,
        include_pck=build_pck_first,
    )
    deploy_result["build"] = build_result
    deploy_result["validation"] = validation
    if pck_result is not None:
        deploy_result["pck"] = pck_result
    deploy_result["success"] = bool(deploy_result.get("success")) and bool(build_result.get("success"))
    return deploy_result


def validate_project_localization(project_dir: str | Path) -> dict[str, Any]:
    """Validate localization JSON files and basic entity coverage."""
    try:
        context = _resolve_project_context(project_dir)
    except FileNotFoundError as exc:
        return {"success": False, "valid": False, "errors": [str(exc)]}

    errors: list[str] = []
    warnings: list[str] = list(context.warnings)
    files_checked: list[str] = []
    all_keys: dict[str, str] = {}

    if not context.localization_dir.exists():
        warnings.append("Localization directory not found")
    else:
        for loc_file in sorted(context.localization_dir.glob("*.json")):
            files_checked.append(loc_file.relative_to(context.project_dir).as_posix())
            try:
                payload = json.loads(loc_file.read_text(encoding="utf-8-sig"))
            except json.JSONDecodeError as exc:
                errors.append(f"Invalid JSON in {loc_file.name}: {exc}")
                continue

            if not isinstance(payload, dict):
                errors.append(f"{loc_file.name} must contain a JSON object")
                continue

            for key, value in payload.items():
                if not isinstance(value, str):
                    warnings.append(f"{loc_file.name}:{key} is not a string value")
                previous = all_keys.get(key)
                if previous and previous != loc_file.name:
                    warnings.append(f"Localization key {key} appears in both {previous} and {loc_file.name}")
                else:
                    all_keys[key] = loc_file.name

    code_root = context.project_dir / "Code"
    for cs_file in sorted(code_root.rglob("*.cs")) if code_root.exists() else []:
        try:
            source = cs_file.read_text(encoding="utf-8-sig")
        except OSError as exc:
            warnings.append(f"Could not read {cs_file.relative_to(context.project_dir).as_posix()}: {exc}")
            continue

        for match in CLASS_DECLARATION_RE.finditer(source):
            base_type = match.group("base")
            expected_file = ENTITY_LOCALIZATION_FILES.get(base_type)
            if not expected_file:
                continue
            class_name = match.group("name")
            key_prefix = _to_screaming_snake(class_name) + "."
            if not any(key.startswith(key_prefix) for key in all_keys):
                warnings.append(
                    f"No localization entries found for {class_name} (expected in {expected_file} with prefix {key_prefix})"
                )

    return {
        "success": not errors,
        "valid": not errors,
        "project": context.as_dict(),
        "files_checked": files_checked,
        "errors": errors,
        "warnings": warnings,
    }


def validate_project_assets(project_dir: str | Path) -> dict[str, Any]:
    """Validate project-owned asset references inside the resource tree."""
    try:
        context = _resolve_project_context(project_dir)
    except FileNotFoundError as exc:
        return {"success": False, "valid": False, "errors": [str(exc)]}

    errors: list[str] = []
    warnings: list[str] = list(context.warnings)
    scanned_files: list[str] = []
    missing_references: list[str] = []

    if context.manifest.get("has_pck") and not context.resource_dir.exists():
        errors.append("Manifest requires a PCK but the resource directory is missing")
    if not context.resource_dir.exists():
        warnings.append("Resource directory not found; asset validation skipped")
        return {
            "success": not errors,
            "valid": not errors,
            "project": context.as_dict(),
            "files_checked": scanned_files,
            "errors": errors,
            "warnings": warnings,
            "missing_references": missing_references,
        }

    for asset_file in sorted(context.resource_dir.rglob("*")):
        if not asset_file.is_file():
            continue
        if asset_file.suffix.lower() not in {".tscn", ".tres", ".import"}:
            continue

        scanned_files.append(asset_file.relative_to(context.project_dir).as_posix())
        try:
            content = asset_file.read_text(encoding="utf-8-sig")
        except OSError as exc:
            warnings.append(f"Could not read {asset_file.relative_to(context.project_dir).as_posix()}: {exc}")
            continue

        for ref in RESOURCE_REFERENCE_RE.findall(content):
            normalized = ref.replace("\\", "/")
            if not normalized.startswith(f"{context.pck_name}/") and not normalized.startswith(
                f"{context.resource_root_name}/"
            ):
                continue

            candidates = [context.project_dir / normalized]
            if normalized.startswith(f"{context.pck_name}/") and context.pck_name != context.resource_root_name:
                rel = normalized[len(context.pck_name) + 1 :]
                candidates.append(context.resource_dir / rel)

            if any(candidate.exists() for candidate in candidates):
                continue

            missing_references.append(
                f"{asset_file.relative_to(context.project_dir).as_posix()} -> res://{normalized}"
            )

    if not any(item.is_file() for item in context.resource_dir.rglob("*")):
        warnings.append("Resource directory is empty")

    return {
        "success": not errors and not missing_references,
        "valid": not errors and not missing_references,
        "project": context.as_dict(),
        "files_checked": scanned_files,
        "errors": errors,
        "warnings": warnings,
        "missing_references": missing_references,
    }


def validate_project(project_dir: str | Path) -> dict[str, Any]:
    """Run both localization and asset validation for a project."""
    localization = validate_project_localization(project_dir)
    assets = validate_project_assets(project_dir)

    errors = list(localization.get("errors", [])) + list(assets.get("errors", []))
    warnings = list(localization.get("warnings", [])) + list(assets.get("warnings", []))
    return {
        "success": localization.get("success") and assets.get("success"),
        "valid": localization.get("valid") and assets.get("valid"),
        "project": localization.get("project") or assets.get("project"),
        "localization": localization,
        "assets": assets,
        "errors": errors,
        "warnings": warnings,
    }


def package_mod(project_dir: str | Path, output_path: str = "") -> dict[str, Any]:
    """Package a mod for distribution as a zip archive."""
    import zipfile

    try:
        context = _resolve_project_context(project_dir)
    except FileNotFoundError as exc:
        return {"success": False, "error": str(exc)}

    if not output_path:
        output_path = str(context.project_dir / f"{context.install_mod_name}.zip")

    files_to_include: list[tuple[str, Path]] = []

    # Manifest
    if context.manifest_path and context.manifest_path.exists():
        files_to_include.append(("mod_manifest.json", context.manifest_path))

    # Build artifacts (try Debug then Release)
    for config in ("Debug", "Release"):
        for artifact in _collect_build_artifacts(context, config):
            if not any(name == artifact.name for name, _ in files_to_include):
                files_to_include.append((artifact.name, artifact))

    # PCK
    pck_path = context.project_dir / f"{context.pck_name}.pck"
    if pck_path.exists():
        files_to_include.append((pck_path.name, pck_path))

    # Mod image
    for image_name in ("mod_image.png", "icon.png"):
        image_path = context.project_dir / image_name
        if image_path.exists():
            files_to_include.append(("mod_image.png", image_path))
            break

    if not files_to_include:
        return {"success": False, "error": "No files to package. Build the mod first."}

    with zipfile.ZipFile(output_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for arcname, filepath in files_to_include:
            zf.write(filepath, arcname)

    return {
        "success": True,
        "output_path": output_path,
        "files": [name for name, _ in files_to_include],
        "project": context.as_dict(),
    }


def check_dependencies(project_dir: str | Path) -> dict[str, Any]:
    """Check mod project dependencies from .csproj."""
    try:
        context = _resolve_project_context(project_dir)
    except FileNotFoundError as exc:
        return {"success": False, "error": str(exc)}

    if not context.csproj_path:
        return {"success": False, "error": "No .csproj file found"}

    csproj_text = context.csproj_path.read_text(encoding="utf-8-sig")

    dependencies: list[dict[str, Any]] = []
    warnings: list[str] = []

    # PackageReference entries
    for match in re.finditer(
        r'<PackageReference\s+Include="([^"]+)"\s+Version="([^"]*)"',
        csproj_text,
        re.IGNORECASE,
    ):
        pkg_name = match.group(1)
        version = match.group(2)
        dep: dict[str, Any] = {"name": pkg_name, "version": version, "type": "nuget"}

        if pkg_name == "Alchyr.Sts2.BaseLib":
            dep["note"] = "BaseLib community library"
            if not version.startswith("0.1"):
                warnings.append(f"BaseLib version {version} may be outdated (latest: 0.1.*)")
        elif pkg_name == "Lib.Harmony":
            dep["note"] = "Harmony patching library"

        dependencies.append(dep)

    # DLL references
    for match in re.finditer(
        r'<Reference\s+Include="([^"]+)"[^>]*>.*?<HintPath>([^<]+)</HintPath>',
        csproj_text,
        re.IGNORECASE | re.DOTALL,
    ):
        ref_name = match.group(1)
        hint_path = match.group(2)
        dep = {"name": ref_name, "hint_path": hint_path, "type": "dll_reference"}

        resolved = Path(hint_path)
        if not resolved.exists():
            resolved = context.project_dir / hint_path
        if not resolved.exists():
            warnings.append(f"DLL reference '{ref_name}' at '{hint_path}' not found on disk")

        dependencies.append(dep)

    target_fw = _extract_project_property(csproj_text, "TargetFramework")

    return {
        "success": True,
        "project": context.as_dict(),
        "target_framework": target_fw,
        "dependencies": dependencies,
        "dependency_count": len(dependencies),
        "warnings": warnings,
    }


def discover_mod_projects(workspace_dir: str | Path) -> dict[str, Any]:
    """Discover all mod projects in a workspace directory."""
    workspace = Path(workspace_dir)
    if not workspace.exists():
        return {"success": False, "error": f"Workspace directory not found: {workspace_dir}"}

    projects: list[dict[str, Any]] = []

    for csproj in workspace.rglob("*.csproj"):
        project_dir = csproj.parent
        rel = str(project_dir.relative_to(workspace))
        if any(part in rel for part in ["bin", "obj", ".git", "node_modules"]):
            continue

        try:
            info = inspect_project(str(project_dir))
            if info.get("success"):
                projects.append({
                    "path": str(project_dir),
                    "relative_path": rel,
                    "namespace": info.get("namespace", ""),
                    "assembly_name": info.get("assembly_name", ""),
                    "has_pck": info.get("has_pck", False),
                    "warnings": info.get("warnings", []),
                })
        except Exception:
            continue

    return {
        "success": True,
        "workspace": str(workspace),
        "project_count": len(projects),
        "projects": projects,
    }
