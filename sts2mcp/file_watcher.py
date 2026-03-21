"""File watcher for auto-rebuild-deploy of STS2 mods."""

import time
import threading
from pathlib import Path
from typing import Any, Callable

from .project_workflow import build_and_deploy_project


class ModFileWatcher:
    """Watches a mod project directory for changes and auto-rebuilds."""

    def __init__(
        self,
        project_dir: str,
        mods_dir: str,
        mod_name: str = "",
        configuration: str = "Debug",
        extensions: tuple[str, ...] = (".cs", ".json", ".tscn", ".tres"),
        debounce_seconds: float = 1.5,
        on_build_complete: Callable[[dict], None] | None = None,
    ):
        self.project_dir = Path(project_dir)
        self.mods_dir = mods_dir
        self.mod_name = mod_name
        self.configuration = configuration
        self.extensions = extensions
        self.debounce_seconds = debounce_seconds
        self.on_build_complete = on_build_complete
        self._running = False
        self._thread: threading.Thread | None = None
        self._file_mtimes: dict[str, float] = {}
        self._last_build_time: float = 0
        self._build_count = 0
        self._last_result: dict[str, Any] | None = None

    def start(self) -> dict:
        if self._running:
            return {"error": "Watcher already running"}

        self._running = True
        self._file_mtimes = self._scan_files()
        self._thread = threading.Thread(target=self._watch_loop, daemon=True)
        self._thread.start()

        return {
            "success": True,
            "project_dir": str(self.project_dir),
            "watching_extensions": list(self.extensions),
            "file_count": len(self._file_mtimes),
        }

    def stop(self) -> dict:
        if not self._running:
            return {"error": "Watcher not running"}

        self._running = False
        return {
            "success": True,
            "build_count": self._build_count,
            "last_result": self._last_result,
        }

    def status(self) -> dict:
        return {
            "running": self._running,
            "project_dir": str(self.project_dir),
            "build_count": self._build_count,
            "last_result": self._last_result,
            "watched_files": len(self._file_mtimes),
        }

    def _scan_files(self) -> dict[str, float]:
        mtimes: dict[str, float] = {}
        for ext in self.extensions:
            for f in self.project_dir.rglob(f"*{ext}"):
                rel = str(f.relative_to(self.project_dir))
                if rel.startswith("bin") or rel.startswith("obj") or rel.startswith("."):
                    continue
                try:
                    mtimes[str(f)] = f.stat().st_mtime
                except OSError:
                    pass
        return mtimes

    def _watch_loop(self):
        while self._running:
            try:
                current = self._scan_files()
                changed = []

                for path, mtime in current.items():
                    if path not in self._file_mtimes or self._file_mtimes[path] != mtime:
                        changed.append(path)

                for path in self._file_mtimes:
                    if path not in current:
                        changed.append(path)

                if changed:
                    self._file_mtimes = current
                    now = time.monotonic()
                    if now - self._last_build_time >= self.debounce_seconds:
                        self._last_build_time = now
                        self._trigger_build(changed)

                time.sleep(0.5)
            except Exception:
                time.sleep(1)

    def _trigger_build(self, changed_files: list[str]):
        self._build_count += 1
        build_num = self._build_count

        result = build_and_deploy_project(
            self.project_dir,
            mods_dir=self.mods_dir,
            mod_name=self.mod_name,
            configuration=self.configuration,
        )
        result["build_number"] = build_num
        result["changed_files"] = [
            str(Path(f).relative_to(self.project_dir))
            for f in changed_files
            if Path(f).is_relative_to(self.project_dir)
        ]

        self._last_result = result

        if self.on_build_complete:
            try:
                self.on_build_complete(result)
            except Exception:
                pass


# Module-level watcher instance for MCP tool use
_active_watcher: ModFileWatcher | None = None


def start_watching(
    project_dir: str,
    mods_dir: str,
    mod_name: str = "",
    configuration: str = "Debug",
) -> dict:
    """Start watching a mod project for changes."""
    global _active_watcher

    if _active_watcher and _active_watcher._running:
        return {"error": "A watcher is already running. Stop it first."}

    _active_watcher = ModFileWatcher(
        project_dir=project_dir,
        mods_dir=mods_dir,
        mod_name=mod_name,
        configuration=configuration,
    )
    return _active_watcher.start()


def stop_watching() -> dict:
    """Stop the active file watcher."""
    global _active_watcher

    if not _active_watcher:
        return {"error": "No active watcher"}

    result = _active_watcher.stop()
    _active_watcher = None
    return result


def watcher_status() -> dict:
    """Get status of the active file watcher."""
    if not _active_watcher:
        return {"running": False}
    return _active_watcher.status()
