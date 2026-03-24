"""Automated first-run setup for the STS2 Modding MCP server.

Handles game discovery, tool installation, decompilation, and GDRE tools
download. Can be run interactively via `python -m sts2mcp.setup` or called
programmatically from the server for silent auto-detection on startup.

All network/install operations require explicit opt-in (interactive mode).
The server startup path only performs local detection and config persistence.
"""

from __future__ import annotations

import io
import json
import os
import re
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path
from urllib import request, error as urlerror

# ─── Paths ────────────────────────────────────────────────────────────────────


def _find_project_root() -> Path:
    """Find the sts2-modding-mcp repo root.

    Checks (in order): parent of __file__ (source checkout), then the directory
    of the entry-point script (sys.argv[0], e.g. run.py), then cwd, then walks
    up from cwd looking for run.py + sts2mcp/ as markers.
    """
    def _is_repo_root(p: Path) -> bool:
        return (p / "run.py").exists() and (p / "sts2mcp").is_dir()

    # 1. Source checkout: __file__ is sts2mcp/setup.py, parent.parent is repo root
    candidate = Path(__file__).resolve().parent.parent
    if _is_repo_root(candidate):
        return candidate

    # 2. Directory of the entry-point script (e.g. run.py launched by an MCP client).
    #    When pip-installed (non-editable), __file__ is in site-packages so check 1
    #    fails, but sys.argv[0] still points at the repo's run.py.
    if sys.argv:
        script_dir = Path(sys.argv[0]).resolve().parent
        if _is_repo_root(script_dir):
            return script_dir

    # 3. Current working directory (user ran `python -m sts2mcp.setup` from repo)
    cwd = Path.cwd()
    if _is_repo_root(cwd):
        return cwd

    # 4. Walk up from cwd
    for parent in cwd.parents:
        if _is_repo_root(parent):
            return parent

    # Fallback to cwd (best effort)
    return cwd


PROJECT_ROOT = _find_project_root()
CONFIG_PATH = PROJECT_ROOT / "sts2mcp_config.json"
TOOLS_DIR = PROJECT_ROOT / "tools"
DECOMPILED_DIR_DEFAULT = PROJECT_ROOT / "decompiled"

# Slay the Spire 2 Steam app ID
STS2_APP_ID = "2868840"

# ─── Config persistence ──────────────────────────────────────────────────────


def load_config() -> dict:
    """Load saved config from sts2mcp_config.json, or return empty dict."""
    if CONFIG_PATH.exists():
        try:
            return json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
        except Exception:
            pass
    return {}


def save_config(config: dict) -> None:
    """Persist config to sts2mcp_config.json."""
    CONFIG_PATH.write_text(json.dumps(config, indent=2) + "\n", encoding="utf-8")


# ─── Game discovery ───────────────────────────────────────────────────────────


def _parse_steam_library_folders() -> list[str]:
    """Parse Steam's libraryfolders.vdf to find all library paths."""
    # Find Steam root via common locations (platform-aware)
    steam_roots: list[Path | None] = [
        Path(os.environ.get("STEAM_PATH", "")) if os.environ.get("STEAM_PATH") else None,
    ]

    if sys.platform == "win32":
        steam_roots += [
            Path("C:/Program Files (x86)/Steam"),
            Path("C:/Program Files/Steam"),
        ]
        # Also check registry on Windows
        try:
            import winreg
            key = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\Valve\Steam")
            steam_path, _ = winreg.QueryValueEx(key, "InstallPath")
            winreg.CloseKey(key)
            steam_roots.insert(0, Path(steam_path))
        except Exception:
            pass
    elif sys.platform == "darwin":
        steam_roots += [
            Path.home() / "Library" / "Application Support" / "Steam",
        ]
    else:
        # Linux and other Unix-like
        steam_roots += [
            Path.home() / ".steam" / "steam",
            Path.home() / ".local" / "share" / "Steam",
            Path.home() / ".steam" / "debian-installation",
            Path("/usr/share/steam"),
            # Flatpak Steam
            Path.home() / ".var" / "app" / "com.valvesoftware.Steam" / ".steam" / "steam",
            Path.home() / ".var" / "app" / "com.valvesoftware.Steam" / ".local" / "share" / "Steam",
            # Snap Steam
            Path.home() / "snap" / "steam" / "common" / ".steam" / "steam",
        ]

    paths: list[str] = []
    for root in steam_roots:
        if root is None:
            continue
        vdf = root / "config" / "libraryfolders.vdf"
        if not vdf.exists():
            # Also add the root itself as a potential library
            lib_dir = root / "steamapps" / "common"
            if lib_dir.exists():
                paths.append(str(root))
            continue

        try:
            text = vdf.read_text(encoding="utf-8")
            # Extract "path" values from the VDF — simple regex approach
            for m in re.finditer(r'"path"\s+"([^"]+)"', text):
                p = m.group(1).replace("\\\\", "\\")
                if p not in paths:
                    paths.append(p)
        except Exception:
            pass

    return paths


# Platform-specific game binary paths (subfolder, filename)
# On Windows/Linux the data dirs are at the top level of the game directory.
# On macOS the game is packaged as an .app bundle; the data dirs live inside
# SlayTheSpire2.app/Contents/Resources/.
# The game assembly is a .NET DLL on all platforms — macOS ships sts2.dll (not
# .dylib) and Linux may ship sts2.dll too (not .so).  We try both extensions.
_GAME_BINARY_CANDIDATES = {
    "win32": [("data_sts2_windows_x86_64", "sts2.dll")],
    "linux": [
        ("data_sts2_linuxbsd_x86_64", "sts2.dll"),
        ("data_sts2_linuxbsd_x86_64", "sts2.so"),
        ("data_sts2_linux_x86_64", "sts2.dll"),
        ("data_sts2_linux_x86_64", "sts2.so"),
    ],
    "darwin": [("data_sts2_macos_arm64", "sts2.dll"), ("data_sts2_macos_x86_64", "sts2.dll")],
}


def _get_game_search_roots(game_dir: str) -> list[str]:
    """Return directories to search for data_sts2_* folders.

    On macOS the game is inside an .app bundle, so we also search inside
    SlayTheSpire2.app/Contents/Resources/.  On Windows/Linux the data dirs
    sit directly under the game directory.
    """
    roots = [game_dir]
    if sys.platform == "darwin":
        # Look inside any .app bundle in the game directory
        try:
            for entry in os.listdir(game_dir):
                if entry.endswith(".app"):
                    resources = os.path.join(game_dir, entry, "Contents", "Resources")
                    if os.path.isdir(resources):
                        roots.append(resources)
        except OSError:
            pass
    return roots


def find_game_binary(game_dir: str) -> str | None:
    """Find the game binary (sts2.dll/.so) inside a game directory. Returns path or None."""
    platform = sys.platform if sys.platform in _GAME_BINARY_CANDIDATES else "linux"
    for root in _get_game_search_roots(game_dir):
        for subfolder, binary in _GAME_BINARY_CANDIDATES[platform]:
            p = os.path.join(root, subfolder, binary)
            if os.path.isfile(p):
                return p
    return None


def _check_game_at(library_path: str) -> str | None:
    """Check if STS2 is installed under a Steam library path. Returns game dir or None."""
    game_dir = os.path.join(library_path, "steamapps", "common", "Slay the Spire 2")
    if find_game_binary(game_dir):
        return game_dir
    # Fallback: check if the game dir exists at all (might have different binary layout)
    try:
        if os.path.isdir(game_dir) and any(
            f.endswith((".dll", ".so", ".dylib"))
            for d in os.listdir(game_dir) if os.path.isdir(os.path.join(game_dir, d))
            for f in os.listdir(os.path.join(game_dir, d))
        ):
            return game_dir
    except OSError:
        pass
    return None


def find_game_install() -> str | None:
    """Search for Slay the Spire 2 across all Steam libraries and common paths."""
    # 1. Check environment variable first
    env_dir = os.environ.get("STS2_GAME_DIR")
    if env_dir and os.path.isdir(env_dir):
        return env_dir

    # 2. Parse Steam's libraryfolders.vdf (platform-aware)
    for lib_path in _parse_steam_library_folders():
        found = _check_game_at(lib_path)
        if found:
            return found

    # 3. Brute-force common locations
    if sys.platform == "win32":
        for drive in "CDEFGHIJ":
            for folder in ["SteamLibrary", "Steam", "Games/Steam", "Games/SteamLibrary"]:
                found = _check_game_at(f"{drive}:/{folder}")
                if found:
                    return found
    else:
        # Linux/macOS: check Flatpak and Snap Steam installs
        extra_roots = [
            Path.home() / ".var" / "app" / "com.valvesoftware.Steam" / ".steam" / "steam",
            Path("/snap/steam/common/.steam/steam"),
        ]
        for root in extra_roots:
            if root.exists():
                found = _check_game_at(str(root))
                if found:
                    return found

    return None


# ─── Tool checks ─────────────────────────────────────────────────────────────

# dotnet tools install to ~/.dotnet/tools on all platforms
_DOTNET_TOOLS_DIR = os.path.join(os.path.expanduser("~"), ".dotnet", "tools")


def _find_ilspycmd() -> str | None:
    """Locate ilspycmd executable — check PATH, then ~/.dotnet/tools."""
    found = shutil.which("ilspycmd")
    if found:
        return found
    # On Windows, shutil.which + env PATH augmentation is unreliable,
    # so check the dotnet tools directory directly.
    if sys.platform == "win32":
        exe = os.path.join(_DOTNET_TOOLS_DIR, "ilspycmd.exe")
    else:
        exe = os.path.join(_DOTNET_TOOLS_DIR, "ilspycmd")
    if os.path.isfile(exe):
        return exe
    return None


def check_dotnet() -> dict:
    """Check if .NET SDK is installed."""
    try:
        r = subprocess.run(["dotnet", "--version"], capture_output=True, text=True, timeout=10)
        if r.returncode == 0:
            return {"installed": True, "version": r.stdout.strip()}
    except Exception:
        pass
    return {"installed": False, "version": None}


def check_ilspycmd() -> dict:
    """Check if ilspycmd is available."""
    exe = _find_ilspycmd()
    if not exe:
        return {"installed": False, "version": None}
    try:
        r = subprocess.run([exe, "--version"], capture_output=True, text=True, timeout=10)
        if r.returncode == 0:
            return {"installed": True, "version": r.stdout.strip(), "path": exe}
    except Exception:
        pass
    # Binary exists but version check failed — still usable
    return {"installed": True, "version": None, "path": exe}


def install_ilspycmd() -> dict:
    """Install ilspycmd via dotnet tool install."""
    try:
        r = subprocess.run(
            ["dotnet", "tool", "install", "-g", "ilspycmd"],
            capture_output=True, text=True, timeout=120,
        )
        if r.returncode == 0 or "already installed" in r.stderr.lower():
            return {"success": True, "output": r.stdout + r.stderr}
        return {"success": False, "error": r.stderr}
    except Exception as e:
        return {"success": False, "error": str(e)}


def check_decompiled(decompiled_dir: str | None = None) -> dict:
    """Check if decompiled source exists."""
    d = Path(decompiled_dir) if decompiled_dir else DECOMPILED_DIR_DEFAULT
    if not d.exists():
        return {"exists": False, "cs_file_count": 0, "has_roslyn_index": False}
    cs_files = list(d.rglob("*.cs"))
    return {
        "exists": True,
        "cs_file_count": len(cs_files),
        "has_roslyn_index": (d / "roslyn_index.json").exists(),
    }


def run_decompile(game_dir: str, decompiled_dir: str | None = None) -> dict:
    """Decompile sts2.dll using ilspycmd."""
    dll_path_str = find_game_binary(game_dir)
    if not dll_path_str:
        return {"success": False, "error": f"Game binary not found in {game_dir}"}
    dll_path = Path(dll_path_str)

    exe = _find_ilspycmd()
    if not exe:
        return {"success": False, "error": "ilspycmd not found — install with: dotnet tool install -g ilspycmd"}

    out_dir = Path(decompiled_dir) if decompiled_dir else DECOMPILED_DIR_DEFAULT
    if out_dir.exists():
        shutil.rmtree(str(out_dir))
    out_dir.mkdir(parents=True, exist_ok=True)

    try:
        r = subprocess.run(
            [exe, "-p", "-o", str(out_dir), str(dll_path)],
            capture_output=True, text=True, timeout=300,
        )
        if r.returncode == 0:
            cs_count = len(list(out_dir.rglob("*.cs")))
            return {"success": True, "output_dir": str(out_dir), "cs_file_count": cs_count}
        return {"success": False, "error": r.stderr}
    except subprocess.TimeoutExpired:
        return {"success": False, "error": "Decompilation timed out (5 min limit)"}


def _gdre_binary_name() -> str:
    """Return the platform-specific GDRE Tools binary name/path."""
    if sys.platform == "win32":
        return "gdre_tools.exe"
    elif sys.platform == "darwin":
        return os.path.join("Godot RE Tools.app", "Contents", "MacOS", "Godot RE Tools")
    else:
        return "gdre_tools.x86_64"


def check_gdre_tools() -> dict:
    """Check if GDRE Tools binary is available."""
    env_path = os.environ.get("GDRE_TOOLS_PATH")
    config_path = load_config().get("gdre_tools_path")
    default_path = TOOLS_DIR / _gdre_binary_name()

    for p in [env_path, config_path, str(default_path)]:
        if p and os.path.isfile(p):
            return {"installed": True, "path": p}

    # Also check for the .app bundle on macOS (user may have just extracted the zip)
    if sys.platform == "darwin":
        for app_name in ("Godot RE Tools.app", "GDRE_tools.app"):
            app_bundle = TOOLS_DIR / app_name
            if app_bundle.exists():
                for binary_name in ("Godot RE Tools", "GDRE_tools"):
                    inner = app_bundle / "Contents" / "MacOS" / binary_name
                    if inner.exists():
                        return {"installed": True, "path": str(inner)}

    for name in ("gdre_tools", "gdre_tools.x86_64", "GDRE_tools", "Godot RE Tools"):
        found = shutil.which(name)
        if found:
            return {"installed": True, "path": found}

    return {"installed": False, "path": None}


def download_gdre_tools() -> dict:
    """Download latest GDRE Tools release from GitHub (platform-aware)."""
    api_url = "https://api.github.com/repos/GDRETools/gdsdecomp/releases/latest"
    try:
        req = request.Request(api_url, headers={"Accept": "application/vnd.github.v3+json", "User-Agent": "sts2mcp-setup"})
        with request.urlopen(req, timeout=15) as resp:
            release = json.loads(resp.read().decode())
    except Exception as e:
        return {"success": False, "error": f"Failed to fetch release info: {e}. Download manually from https://github.com/GDRETools/gdsdecomp/releases"}

    # Determine which platform asset to download
    if sys.platform == "win32":
        platform_key = "windows"
    elif sys.platform == "darwin":
        platform_key = "macos"
    else:
        platform_key = "linux"

    asset_url = None
    asset_name = None
    for asset in release.get("assets", []):
        name = asset["name"].lower()
        if platform_key in name and name.endswith(".zip"):
            asset_url = asset["browser_download_url"]
            asset_name = asset["name"]
            break

    if not asset_url:
        return {"success": False, "error": f"No {platform_key} release asset found. Download manually from https://github.com/GDRETools/gdsdecomp/releases"}

    TOOLS_DIR.mkdir(parents=True, exist_ok=True)
    zip_path = TOOLS_DIR / asset_name

    try:
        print(f"  Downloading {asset_name}...", file=sys.stderr)
        request.urlretrieve(asset_url, str(zip_path))
    except Exception as e:
        return {"success": False, "error": f"Download failed: {e}"}

    # Extract
    try:
        with zipfile.ZipFile(str(zip_path), "r") as zf:
            zf.extractall(str(TOOLS_DIR))
        zip_path.unlink()
    except Exception as e:
        return {"success": False, "error": f"Extraction failed: {e}"}

    # On macOS, clear the quarantine attribute so the binary can execute
    if sys.platform == "darwin":
        # Find any .app bundle that was extracted
        for app_name in ("Godot RE Tools.app", "GDRE_tools.app"):
            app_bundle = TOOLS_DIR / app_name
            if app_bundle.exists():
                try:
                    subprocess.run(
                        ["xattr", "-rd", "com.apple.quarantine", str(app_bundle)],
                        capture_output=True, timeout=10,
                    )
                except Exception:
                    pass  # Non-fatal — user can do it manually
                break

    # Verify the binary exists
    expected_binary = TOOLS_DIR / _gdre_binary_name()
    if not expected_binary.exists():
        # Search for it in subdirectories
        if sys.platform == "darwin":
            # macOS app bundle binary could have various names
            for f in TOOLS_DIR.rglob("*"):
                if f.is_file() and "MacOS" in str(f) and f.parent.name == "MacOS":
                    expected_binary = f
                    break
        elif sys.platform == "win32":
            for f in TOOLS_DIR.rglob("gdre_tools.exe"):
                expected_binary = f
                break
        else:
            for f in TOOLS_DIR.rglob("gdre_tools*"):
                if f.is_file() and not f.suffix in (".pck", ".so"):
                    expected_binary = f
                    break

    if expected_binary.exists():
        # Ensure executable on Unix
        if sys.platform != "win32":
            expected_binary.chmod(expected_binary.stat().st_mode | 0o755)
        return {"success": True, "path": str(expected_binary), "version": release.get("tag_name", "unknown")}
    return {"success": False, "error": f"Extracted archive but GDRE Tools binary not found in {TOOLS_DIR}"}


# ─── Status ───────────────────────────────────────────────────────────────────


def get_setup_status(game_dir: str | None = None, decompiled_dir: str | None = None) -> dict:
    """Return comprehensive setup status suitable for display or MCP tools."""
    config = load_config()
    resolved_game_dir = (
        os.environ.get("STS2_GAME_DIR")
        or (game_dir if game_dir else None)
        or config.get("game_dir")
        or find_game_install()
    )
    resolved_decompiled = (
        os.environ.get("STS2_DECOMPILED_DIR")
        or (decompiled_dir if decompiled_dir else None)
        or config.get("decompiled_dir")
        or str(DECOMPILED_DIR_DEFAULT)
    )

    sts2_dll_exists = False
    if resolved_game_dir:
        sts2_dll_exists = find_game_binary(resolved_game_dir) is not None

    dotnet = check_dotnet()
    ilspy = check_ilspycmd()
    decomp = check_decompiled(resolved_decompiled)
    gdre = check_gdre_tools()

    missing = []
    if not resolved_game_dir or not sts2_dll_exists:
        missing.append("Game not found — set STS2_GAME_DIR or run setup")
    if not dotnet["installed"]:
        missing.append(".NET SDK not installed — download from https://dotnet.microsoft.com/download/dotnet/9.0")
    if not ilspy["installed"]:
        missing.append("ilspycmd not installed — run: dotnet tool install -g ilspycmd")
    if not decomp["exists"]:
        missing.append("Game not decompiled — run setup or use decompile_game tool")
    if not gdre["installed"]:
        missing.append("GDRE Tools not found — run setup to download automatically (optional, for asset extraction)")

    return {
        "game_found": bool(resolved_game_dir and sts2_dll_exists),
        "game_dir": resolved_game_dir,
        "sts2_dll_exists": sts2_dll_exists,
        "dotnet_installed": dotnet["installed"],
        "dotnet_version": dotnet["version"],
        "ilspycmd_installed": ilspy["installed"],
        "decompiled_exists": decomp["exists"],
        "decompiled_cs_count": decomp["cs_file_count"],
        "roslyn_index_exists": decomp["has_roslyn_index"],
        "gdre_tools_installed": gdre["installed"],
        "gdre_tools_path": gdre["path"],
        "all_ready": len(missing) == 0 or (len(missing) == 1 and "GDRE" in missing[0]),
        "missing_steps": missing,
    }


# ─── Config resolution (used by server.py) ───────────────────────────────────


def resolve_config() -> tuple[str, str]:
    """Resolve (game_dir, decompiled_dir) from env > config > auto-detect > defaults."""
    config = load_config()
    project_root = str(PROJECT_ROOT)

    game_dir = (
        os.environ.get("STS2_GAME_DIR")
        or config.get("game_dir")
        or find_game_install()
    )
    if not game_dir:
        raise FileNotFoundError(
            "Could not locate Slay the Spire 2. Set STS2_GAME_DIR or run 'python -m sts2mcp.setup'."
        )

    decompiled_dir = (
        os.environ.get("STS2_DECOMPILED_DIR")
        or config.get("decompiled_dir")
        or os.path.join(project_root, "decompiled")
    )

    return game_dir, decompiled_dir


def _ensure_builtin_mods(game_dir: str) -> None:
    """Build and install built-in mods (MCPTest, GodotExplorer) if not already installed.

    Runs silently on startup — only writes to stderr for logging. Requires
    dotnet SDK to be available. If dotnet is missing the step is skipped.
    """
    if not shutil.which("dotnet"):
        return

    mods_dir = os.path.join(game_dir, "mods")
    os.makedirs(mods_dir, exist_ok=True)

    builtin_mods = [
        {
            "source": PROJECT_ROOT / "test_mod",
            "mod_id": "mcptest",
            "assembly": "MCPTest",
            "csproj": "MCPTest.csproj",
        },
        {
            "source": PROJECT_ROOT / "explorer_mod",
            "mod_id": "godotexplorer",
            "assembly": "GodotExplorer",
            "csproj": "GodotExplorer.csproj",
        },
    ]

    for mod in builtin_mods:
        source_dir = mod["source"]
        csproj = source_dir / mod["csproj"]
        if not csproj.exists():
            continue

        target_dir = Path(mods_dir) / mod["mod_id"]
        target_dll = target_dir / f"{mod['assembly']}.dll"

        # Skip if already installed and source hasn't changed
        if target_dll.exists():
            # Quick staleness check: compare source mod time vs installed DLL
            src_mtime = max(
                (f.stat().st_mtime for f in source_dir.rglob("*.cs") if f.is_file()),
                default=0,
            )
            if target_dll.stat().st_mtime >= src_mtime:
                continue

        print(f"[sts2mcp] Building {mod['assembly']}...", file=sys.stderr)
        env = {**os.environ, "STS2_GAME_DIR": game_dir}
        try:
            result = subprocess.run(
                ["dotnet", "build", str(csproj), "-c", "Debug"],
                cwd=str(source_dir),
                capture_output=True,
                text=True,
                env=env,
                timeout=120,
            )
        except (subprocess.TimeoutExpired, FileNotFoundError):
            print(f"[sts2mcp] Failed to build {mod['assembly']} — skipping", file=sys.stderr)
            continue

        if result.returncode != 0:
            print(f"[sts2mcp] Build failed for {mod['assembly']}: {result.stderr[-200:]}", file=sys.stderr)
            continue

        # Find built DLL
        bin_dir = source_dir / "bin" / "Debug" / "net9.0"
        built_dll = bin_dir / f"{mod['assembly']}.dll"
        if not built_dll.exists():
            # Try without framework subdir
            bin_dir = source_dir / "bin" / "Debug"
            built_dll = bin_dir / f"{mod['assembly']}.dll"
        if not built_dll.exists():
            print(f"[sts2mcp] Built DLL not found for {mod['assembly']} — skipping", file=sys.stderr)
            continue

        # Deploy
        target_dir.mkdir(parents=True, exist_ok=True)
        shutil.copy2(built_dll, target_dll)
        # Copy PDB if it exists
        pdb = built_dll.with_suffix(".pdb")
        if pdb.exists():
            shutil.copy2(pdb, target_dir / pdb.name)
        # Copy manifest
        manifest = source_dir / "mod_manifest.json"
        if manifest.exists():
            shutil.copy2(manifest, target_dir / "mod_manifest.json")

        print(f"[sts2mcp] Installed {mod['assembly']} → {target_dir}", file=sys.stderr)


def auto_detect_on_startup() -> None:
    """Lightweight auto-detection for server startup. Writes to stderr only."""
    config = load_config()

    game_dir = config.get("game_dir")

    # If we don't have a saved config with a valid game dir, try to find one
    if not game_dir or not os.path.isdir(game_dir):
        game_dir = find_game_install()
        if game_dir:
            config["game_dir"] = game_dir
            save_config(config)
            print(f"[sts2mcp] Game found: {game_dir}", file=sys.stderr)
        else:
            print("[sts2mcp] Game not found -- set STS2_GAME_DIR or run: python -m sts2mcp.setup", file=sys.stderr)

    # Auto-build and install built-in mods (MCPTest + GodotExplorer)
    if game_dir and os.path.isdir(game_dir):
        try:
            _ensure_builtin_mods(game_dir)
        except Exception as exc:
            print(f"[sts2mcp] Mod auto-install failed: {exc}", file=sys.stderr)

    # Check decompiled state
    decomp = check_decompiled(config.get("decompiled_dir"))
    if not decomp["exists"]:
        print("[sts2mcp] decompiled/ not found -- run: python -m sts2mcp.setup", file=sys.stderr)


# ─── Interactive CLI ──────────────────────────────────────────────────────────


def _ask(prompt: str, default: bool = True) -> bool:
    """Ask a yes/no question in interactive mode.

    Returns the default when stdin is not a TTY (piped/redirected) so that
    setup behaves sensibly in non-interactive shells without needing an
    explicit ``--yes`` flag.
    """
    suffix = " [Y/n] " if default else " [y/N] "
    if not sys.stdin.isatty():
        print(prompt + suffix + ("y" if default else "n") + "  (auto, non-interactive)", file=sys.stderr)
        return default
    try:
        answer = input(prompt + suffix).strip().lower()
        if not answer:
            return default
        return answer in ("y", "yes")
    except (EOFError, KeyboardInterrupt):
        print()
        return default


def _check_python_version() -> None:
    """Warn early if Python version is too old."""
    if sys.version_info < (3, 11):
        print(
            f"  WARNING: Python {sys.version_info.major}.{sys.version_info.minor} detected, "
            "but 3.11+ is required. Some features may not work.",
            file=sys.stderr,
        )


def build_roslyn_index(decompiled_dir: str) -> dict:
    """Build the Roslyn analyzer and generate the index. Returns success/failure."""
    if not shutil.which("dotnet"):
        return {"success": False, "error": "dotnet not found"}

    analyzer_dir = PROJECT_ROOT / "tools" / "roslyn_analyzer"
    if not (analyzer_dir / "RoslynAnalyzer.csproj").exists():
        return {"success": False, "error": "Roslyn analyzer source not found"}

    dll_path = analyzer_dir / "bin" / "Release" / "net9.0" / "RoslynAnalyzer.dll"
    index_path = Path(decompiled_dir) / "roslyn_index.json"

    # Build the analyzer if DLL doesn't exist
    if not dll_path.exists():
        nuget_config = analyzer_dir / "nuget.config"
        restore_cmd = ["dotnet", "restore"]
        if nuget_config.exists():
            restore_cmd += ["--configfile", str(nuget_config)]
        try:
            r = subprocess.run(restore_cmd, cwd=str(analyzer_dir), capture_output=True, text=True, timeout=120)
            if r.returncode != 0:
                return {"success": False, "error": f"restore failed: {r.stderr[:200]}"}
            r = subprocess.run(
                ["dotnet", "build", "-c", "Release", "--no-restore"],
                cwd=str(analyzer_dir), capture_output=True, text=True, timeout=120,
            )
            if r.returncode != 0:
                return {"success": False, "error": f"build failed: {r.stderr[:200]}"}
        except (subprocess.TimeoutExpired, OSError) as e:
            return {"success": False, "error": str(e)}

    # Run the analyzer
    try:
        r = subprocess.run(
            ["dotnet", str(dll_path), decompiled_dir, str(index_path)],
            capture_output=True, text=True, timeout=300,
        )
        if r.returncode == 0 and index_path.exists():
            size_mb = index_path.stat().st_size / (1024 * 1024)
            return {"success": True, "index_path": str(index_path), "size_mb": round(size_mb, 1)}
        return {"success": False, "error": r.stderr[:200] if r.stderr else "unknown error"}
    except (subprocess.TimeoutExpired, OSError) as e:
        return {"success": False, "error": str(e)}


def run_full_setup(interactive: bool = True) -> dict:
    """Run the complete setup flow. Non-interactive mode performs all actions without prompting."""
    results: dict = {}
    config = load_config()

    _check_python_version()

    # ── Step 1: Find game ──
    print("\n[1/6] Finding game installation...", file=sys.stderr)
    game_dir = os.environ.get("STS2_GAME_DIR") or config.get("game_dir")
    if game_dir and os.path.isdir(game_dir):
        if find_game_binary(game_dir):
            print(f"  Found (saved): {game_dir}", file=sys.stderr)
        else:
            game_dir = None

    if not game_dir:
        print("  Searching Steam libraries...", file=sys.stderr)
        game_dir = find_game_install()
        if game_dir:
            print(f"  Found: {game_dir}", file=sys.stderr)
        else:
            print("  Not found. Set STS2_GAME_DIR environment variable to your game path and re-run setup.", file=sys.stderr)
            if not interactive:
                print("  Tip: STS2_GAME_DIR=\"/path/to/Slay the Spire 2\" python -m sts2mcp.setup --non-interactive", file=sys.stderr)
            if interactive:
                try:
                    manual = input("  Enter game path (or press Enter to skip): ").strip().strip('"')
                    if manual and os.path.isdir(manual):
                        game_dir = manual
                except (EOFError, KeyboardInterrupt):
                    print()

    if game_dir:
        config["game_dir"] = game_dir
        results["game_dir"] = game_dir
    results["game_found"] = game_dir is not None

    # ── Step 2: Check .NET SDK ──
    print("\n[2/6] Checking .NET SDK...", file=sys.stderr)
    dotnet = check_dotnet()
    if dotnet["installed"]:
        print(f"  dotnet {dotnet['version']}: OK", file=sys.stderr)
    else:
        print("  Not found. Install from https://dotnet.microsoft.com/download/dotnet/9.0", file=sys.stderr)
    results["dotnet"] = dotnet

    # ── Step 3: Check/install ilspycmd ──
    print("\n[3/6] Checking ilspycmd...", file=sys.stderr)
    ilspy = check_ilspycmd()
    if ilspy["installed"]:
        print(f"  ilspycmd: OK", file=sys.stderr)
    elif dotnet["installed"]:
        print("  Not found.", file=sys.stderr)
        if not interactive or _ask("  Install ilspycmd now?"):
            print("  Installing...", file=sys.stderr, end=" ", flush=True)
            install_result = install_ilspycmd()
            if install_result["success"]:
                print("done", file=sys.stderr)
                ilspy = check_ilspycmd()
            else:
                print(f"failed: {install_result['error']}", file=sys.stderr)
    else:
        print("  Skipped (requires .NET SDK)", file=sys.stderr)
    results["ilspycmd"] = ilspy

    # ── Step 4: Decompile game ──
    print("\n[4/6] Checking decompiled source...", file=sys.stderr)
    decompiled_dir = config.get("decompiled_dir") or str(DECOMPILED_DIR_DEFAULT)
    decomp = check_decompiled(decompiled_dir)
    if decomp["exists"] and decomp["cs_file_count"] > 0:
        print(f"  {decomp['cs_file_count']} C# files found: OK", file=sys.stderr)
    elif game_dir and ilspy["installed"]:
        print("  Not found.", file=sys.stderr)
        if not interactive or _ask("  Decompile game now? (takes ~30 seconds)"):
            print("  Decompiling sts2.dll...", file=sys.stderr, end=" ", flush=True)
            decompile_result = run_decompile(game_dir, decompiled_dir)
            if decompile_result["success"]:
                print(f"done ({decompile_result['cs_file_count']} files)", file=sys.stderr)
                decomp = check_decompiled(decompiled_dir)
            else:
                print(f"failed: {decompile_result['error']}", file=sys.stderr)
    else:
        reasons = []
        if not game_dir:
            reasons.append("game not found")
        if not ilspy["installed"]:
            reasons.append("ilspycmd not installed")
        print(f"  Skipped ({', '.join(reasons)})", file=sys.stderr)
    results["decompiled"] = decomp

    # ── Step 5: GDRE Tools ──
    print("\n[5/6] Checking GDRE Tools...", file=sys.stderr)
    gdre = check_gdre_tools()
    if gdre["installed"]:
        print(f"  Found: {gdre['path']}", file=sys.stderr)
    else:
        print("  Not found (optional -- needed for Godot asset extraction).", file=sys.stderr)
        if not interactive or _ask("  Download latest release?"):
            dl_result = download_gdre_tools()
            if dl_result["success"]:
                print(f"  Installed: {dl_result['path']}", file=sys.stderr)
                gdre = check_gdre_tools()
            else:
                print(f"  Failed: {dl_result['error']}", file=sys.stderr)
    results["gdre_tools"] = gdre

    # ── Step 6: Roslyn index ──
    print("\n[6/6] Checking Roslyn code index...", file=sys.stderr)
    if decomp["exists"] and decomp["cs_file_count"] > 0:
        if decomp["has_roslyn_index"]:
            print("  Roslyn index: OK", file=sys.stderr)
        elif dotnet["installed"]:
            print("  Not found. Building Roslyn analyzer and generating index...", file=sys.stderr)
            roslyn_result = build_roslyn_index(decompiled_dir)
            if roslyn_result["success"]:
                print(f"  Done ({roslyn_result['size_mb']}MB index)", file=sys.stderr)
                decomp = check_decompiled(decompiled_dir)
            else:
                print(f"  Failed: {roslyn_result['error']}", file=sys.stderr)
                print("  (The server will fall back to regex parsing -- this is OK)", file=sys.stderr)
        else:
            print("  Skipped (requires .NET SDK)", file=sys.stderr)
    else:
        print("  Skipped (no decompiled source)", file=sys.stderr)
    results["decompiled"] = decomp

    # ── Save config ──
    if config.get("game_dir"):
        config.setdefault("decompiled_dir", decompiled_dir)
        save_config(config)

    # ── Summary ──
    status = get_setup_status(game_dir, decompiled_dir)
    print("\n" + "=" * 50, file=sys.stderr)
    if status["all_ready"]:
        print("Setup complete! The MCP server is ready to use.", file=sys.stderr)
    else:
        print("Setup incomplete. Remaining steps:", file=sys.stderr)
        for step in status["missing_steps"]:
            print(f"  - {step}", file=sys.stderr)
    print("=" * 50 + "\n", file=sys.stderr)

    results["status"] = status
    return results


# ─── CLI entry point ──────────────────────────────────────────────────────────

def cli_main():
    """Entry point for `python -m sts2mcp.setup` and `sts2mcp-setup` command."""
    import argparse
    parser = argparse.ArgumentParser(description="STS2 Modding MCP -- automated setup")
    parser.add_argument("--non-interactive", "-y", "--yes", action="store_true",
                        help="Auto-accept all prompts (install tools, decompile, download GDRE)")
    parser.add_argument("--status", action="store_true", help="Show setup status and exit")
    args = parser.parse_args()

    if args.status:
        status = get_setup_status()
        print(json.dumps(status, indent=2))
        return

    run_full_setup(interactive=not args.non_interactive)


if __name__ == "__main__":
    cli_main()
