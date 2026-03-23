"""GDRE Tools integration for Godot PCK extraction, GDScript decompilation, and resource conversion.

Wraps the gdre_tools CLI (https://github.com/GDRETools/gdsdecomp) for:
- Listing/extracting game assets from PCK files
- Decompiling GDScript bytecode (.gdc -> .gd)
- Converting binary resources (.scn/.res) to text (.tscn/.tres)
- Full project recovery from packed game data
"""

import fnmatch
import os
import subprocess
from pathlib import Path

# Timeouts (seconds)
GDRE_DEFAULT_TIMEOUT = 300   # 5 minutes — listing, decompile, convert
GDRE_EXTRACT_TIMEOUT = 600   # 10 minutes — extract / recover

# Result truncation limits
MAX_ASSET_LIST_RESULTS = 2000
MAX_SEARCH_RESULTS = 500
MAX_STDOUT_CHARS = 2000
MAX_STDOUT_CHARS_LONG = 4000  # for decompile/convert output

# Default path to gdre_tools binary (relative to project root)
_PROJECT_ROOT = Path(__file__).resolve().parent.parent
_DEFAULT_GDRE_PATH = str(_PROJECT_ROOT / "tools" / "gdre_tools.exe")

# Caches
_game_pck_cache: str | None = None
_asset_list_cache: list[str] | None = None


def _resolve_gdre_path() -> str:
    """Resolve gdre_tools path: env var → config file → default location."""
    env = os.environ.get("GDRE_TOOLS_PATH")
    if env:
        return env
    try:
        from sts2mcp.setup import load_config
        config_path = load_config().get("gdre_tools_path")
        if config_path:
            return config_path
    except Exception:
        pass
    return _DEFAULT_GDRE_PATH


def _find_gdre() -> str:
    """Locate gdre_tools binary, checking PATH as fallback."""
    resolved = _resolve_gdre_path()
    if os.path.isfile(resolved):
        return resolved
    import shutil
    found = shutil.which("gdre_tools")
    if found:
        return found
    raise FileNotFoundError(
        f"gdre_tools not found at {resolved} or on PATH. "
        "Run 'python -m sts2mcp.setup' to download automatically, or get it from "
        "https://github.com/GDRETools/gdsdecomp/releases"
    )


def _find_game_pck(game_dir: str) -> str:
    """Auto-detect the game PCK file in the game directory."""
    global _game_pck_cache
    if _game_pck_cache and os.path.isfile(_game_pck_cache):
        return _game_pck_cache

    candidates = [
        os.path.join(game_dir, "SlayTheSpire2.pck"),
        os.path.join(game_dir, "game.pck"),
        os.path.join(game_dir, "data.pck"),
    ]
    try:
        for f in os.listdir(game_dir):
            if f.endswith(".pck"):
                full = os.path.join(game_dir, f)
                if full not in candidates:
                    candidates.append(full)
    except OSError:
        pass

    for c in candidates:
        if os.path.isfile(c):
            _game_pck_cache = c
            return c

    raise FileNotFoundError(f"No .pck file found in {game_dir}")


def _run_gdre(args: list[str], timeout: int = GDRE_DEFAULT_TIMEOUT) -> subprocess.CompletedProcess:
    """Run gdre_tools with given arguments."""
    gdre = _find_gdre()
    cmd = [gdre, "--headless"] + args
    return subprocess.run(
        cmd,
        capture_output=True,
        text=True,
        timeout=timeout,
    )


def _get_asset_list(game_dir: str) -> list[str]:
    """Get cached list of all asset paths in the game PCK."""
    global _asset_list_cache
    if _asset_list_cache is not None:
        return _asset_list_cache

    pck_path = _find_game_pck(game_dir)
    result = _run_gdre(["--list-files=" + pck_path])

    if result.returncode != 0:
        raise RuntimeError(result.stderr or result.stdout)

    files = []
    for line in result.stdout.strip().splitlines():
        line = line.strip()
        if line.startswith("res://"):
            files.append(line)

    _asset_list_cache = files
    return files


def list_game_assets(game_dir: str, filter_glob: str = "", filter_ext: str = "") -> dict:
    """List all files in the game PCK.

    Args:
        game_dir: Path to game installation directory
        filter_glob: Optional glob pattern to filter results (applied locally)
        filter_ext: Optional extension filter like ".tscn", ".gd", ".png"

    Returns:
        Dict with file list, counts by extension, and total size
    """
    try:
        all_files = _get_asset_list(game_dir)
    except FileNotFoundError as e:
        return {"success": False, "error": str(e)}
    except (RuntimeError, subprocess.TimeoutExpired) as e:
        return {"success": False, "error": str(e)}

    files = []
    ext_counts: dict[str, int] = {}

    for path in all_files:
        if filter_ext:
            normalized_ext = filter_ext if filter_ext.startswith(".") else f".{filter_ext}"
            if not path.lower().endswith(normalized_ext.lower()):
                continue

        if filter_glob:
            if not fnmatch.fnmatch(path.lower(), filter_glob.lower()):
                continue

        files.append(path)
        ext = Path(path).suffix.lower()
        ext_counts[ext] = ext_counts.get(ext, 0) + 1

    sorted_exts = dict(sorted(ext_counts.items(), key=lambda x: -x[1]))

    return {
        "success": True,
        "pck_path": _game_pck_cache or "",
        "total_files": len(files),
        "extension_summary": sorted_exts,
        "files": files[:MAX_ASSET_LIST_RESULTS],
        "truncated": len(files) > MAX_ASSET_LIST_RESULTS,
    }


def search_game_assets(game_dir: str, pattern: str, extensions: list[str] | None = None) -> dict:
    """Search game assets by path pattern — fast in-memory search on cached file list.

    Args:
        game_dir: Path to game installation directory
        pattern: Substring to search for in asset paths (case-insensitive)
        extensions: Optional list of extensions to filter (e.g. [".tscn", ".tres"])

    Returns:
        Dict with matching files
    """
    try:
        all_files = _get_asset_list(game_dir)
    except FileNotFoundError as e:
        return {"success": False, "error": str(e)}
    except (RuntimeError, subprocess.TimeoutExpired) as e:
        return {"success": False, "error": str(e)}

    pattern_lower = pattern.lower()
    norm_exts = None
    if extensions:
        norm_exts = [e.lower() if e.startswith(".") else f".{e.lower()}" for e in extensions]

    matches = []
    for f in all_files:
        if pattern_lower not in f.lower():
            continue
        if norm_exts:
            ext = Path(f).suffix.lower()
            if ext not in norm_exts:
                continue
        matches.append(f)

    return {
        "success": True,
        "pattern": pattern,
        "match_count": len(matches),
        "matches": matches[:MAX_SEARCH_RESULTS],
        "truncated": len(matches) > MAX_SEARCH_RESULTS,
    }


def extract_game_assets(
    game_dir: str,
    output_dir: str,
    include: str = "",
    exclude: str = "",
    scripts_only: bool = False,
) -> dict:
    """Extract files from the game PCK.

    Args:
        game_dir: Path to game installation directory
        output_dir: Where to extract files
        include: Glob pattern for files to include (e.g. "res://**/*.tscn")
        exclude: Glob pattern for files to exclude
        scripts_only: Only extract script files (.gd, .gdc)

    Returns:
        Dict with extraction results
    """
    try:
        pck_path = _find_game_pck(game_dir)
    except FileNotFoundError as e:
        return {"success": False, "error": str(e)}

    args = ["--extract=" + pck_path, "--output=" + output_dir]
    if include:
        args.append("--include=" + include)
    if exclude:
        args.append("--exclude=" + exclude)
    if scripts_only:
        args.append("--scripts-only")

    try:
        result = _run_gdre(args, timeout=GDRE_EXTRACT_TIMEOUT)
    except FileNotFoundError as e:
        return {"success": False, "error": str(e)}
    except subprocess.TimeoutExpired:
        return {"success": False, "error": f"Extraction timed out after {GDRE_EXTRACT_TIMEOUT // 60} minutes"}

    if result.returncode != 0:
        return {"success": False, "error": result.stderr or result.stdout}

    # Count extracted files
    out_path = Path(output_dir)
    file_count = sum(1 for f in out_path.rglob("*") if f.is_file())

    return {
        "success": True,
        "output_dir": output_dir,
        "files_extracted": file_count,
        "stdout": result.stdout[:MAX_STDOUT_CHARS] if result.stdout else "",
    }


def recover_game_project(game_dir: str, output_dir: str) -> dict:
    """Full project recovery from the game PCK.

    Extracts everything, decompiles GDScript, converts binary resources to text.
    This is the Godot-asset equivalent of decompile_game (which handles C#/sts2.dll).

    Args:
        game_dir: Path to game installation directory
        output_dir: Where to recover the project

    Returns:
        Dict with recovery results
    """
    try:
        pck_path = _find_game_pck(game_dir)
    except FileNotFoundError as e:
        return {"success": False, "error": str(e)}

    args = ["--recover=" + pck_path, "--output=" + output_dir]

    try:
        result = _run_gdre(args, timeout=GDRE_EXTRACT_TIMEOUT)
    except FileNotFoundError as e:
        return {"success": False, "error": str(e)}
    except subprocess.TimeoutExpired:
        return {"success": False, "error": f"Recovery timed out after {GDRE_EXTRACT_TIMEOUT // 60} minutes"}

    if result.returncode != 0:
        return {"success": False, "error": result.stderr or result.stdout}

    # Count recovered files by type
    out_path = Path(output_dir)
    ext_counts: dict[str, int] = {}
    file_count = 0
    for f in out_path.rglob("*"):
        if f.is_file():
            file_count += 1
            ext = f.suffix.lower()
            ext_counts[ext] = ext_counts.get(ext, 0) + 1

    return {
        "success": True,
        "output_dir": output_dir,
        "total_files": file_count,
        "extension_summary": dict(sorted(ext_counts.items(), key=lambda x: -x[1])),
        "stdout": result.stdout[:MAX_STDOUT_CHARS] if result.stdout else "",
    }


def decompile_gdscript(
    input_path: str,
    output_dir: str = "",
) -> dict:
    """Decompile GDScript bytecode (.gdc) to readable source (.gd).

    Args:
        input_path: Path to .gdc file or glob pattern (e.g. "extracted/**/*.gdc")
        output_dir: Output directory (default: same directory as input)

    Returns:
        Dict with decompilation results
    """
    args = ["--decompile=" + input_path]
    if output_dir:
        args.append("--output=" + output_dir)

    try:
        result = _run_gdre(args)
    except FileNotFoundError as e:
        return {"success": False, "error": str(e)}
    except subprocess.TimeoutExpired:
        return {"success": False, "error": "Decompilation timed out after 5 minutes"}

    if result.returncode != 0:
        return {"success": False, "error": result.stderr or result.stdout}

    return {
        "success": True,
        "stdout": result.stdout[:MAX_STDOUT_CHARS_LONG] if result.stdout else "",
    }


def convert_resource(
    input_path: str,
    output_dir: str = "",
    direction: str = "bin_to_txt",
) -> dict:
    """Convert between binary and text resource formats.

    Args:
        input_path: Path to resource file (supports globs)
        output_dir: Output directory (default: same directory)
        direction: "bin_to_txt" (.scn/.res -> .tscn/.tres) or "txt_to_bin" (.tscn/.tres -> .scn/.res)

    Returns:
        Dict with conversion results
    """
    if direction == "bin_to_txt":
        args = ["--bin-to-txt=" + input_path]
    elif direction == "txt_to_bin":
        args = ["--txt-to-bin=" + input_path]
    else:
        return {"success": False, "error": f"Invalid direction: {direction}. Use 'bin_to_txt' or 'txt_to_bin'"}

    if output_dir:
        args.append("--output=" + output_dir)

    try:
        result = _run_gdre(args)
    except FileNotFoundError as e:
        return {"success": False, "error": str(e)}
    except subprocess.TimeoutExpired:
        return {"success": False, "error": "Conversion timed out after 5 minutes"}

    if result.returncode != 0:
        return {"success": False, "error": result.stderr or result.stdout}

    return {
        "success": True,
        "stdout": result.stdout[:MAX_STDOUT_CHARS_LONG] if result.stdout else "",
    }
