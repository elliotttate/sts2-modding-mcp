"""File watcher for auto-rebuild-deploy of STS2 mods with hot reload.

Supports OS-native file watching via watchdog (preferred) with automatic
fallback to polling when watchdog is not installed.  Multiple mod projects
can be watched concurrently.
"""

import hashlib
import json
import logging
import threading
import time
from pathlib import Path
from typing import Any, Callable

# Try native file watching, fall back to polling
try:
    from watchdog.observers import Observer
    from watchdog.events import FileSystemEventHandler, FileSystemEvent

    _HAS_WATCHDOG = True
except ImportError:
    Observer = None  # type: ignore[assignment,misc]
    FileSystemEventHandler = object  # type: ignore[assignment,misc]
    _HAS_WATCHDOG = False

from .project_workflow import build_and_deploy_project, validate_project, _resolve_project_context
from .hot_reload import (
    NON_RESOURCE_JSON_NAMES,
    RESOURCE_RELOAD_EXTENSIONS,
    WATCHED_EXTENSIONS,
    build_deploy_and_hot_reload_project,
    determine_reload_tier,
)

logger = logging.getLogger(__name__)

RESOURCE_HASH_FILENAME = ".mcp_resource_hashes.json"

# Directories to skip during file scanning and watchdog filtering
_SKIP_PREFIXES = ("bin", "obj", ".")


def _normalize_project_key(project_dir: str | Path) -> str:
    """Return a canonical string key for a project directory."""
    return str(Path(project_dir).resolve())


def _should_skip_path(rel_path: str) -> bool:
    """Return True if a relative path falls under bin/, obj/, or a dotfile directory."""
    parts = rel_path.replace("\\", "/").split("/")
    for part in parts:
        if any(part.startswith(p) for p in _SKIP_PREFIXES):
            return True
    return False


# ---------------------------------------------------------------------------
# Watchdog handler (only instantiated when watchdog is available)
# ---------------------------------------------------------------------------

class _WatchdogHandler(FileSystemEventHandler):
    """Collects changed file paths from watchdog events, filtering by extension
    and skipping bin/obj/dotfile directories.  Thread-safe: the watcher's
    debounce thread reads ``pending_paths`` under the same lock.
    """

    def __init__(self, project_dir: Path, extensions: set[str]):
        super().__init__()
        self.project_dir = project_dir
        self.extensions = extensions
        self.lock = threading.Lock()
        self.pending_paths: set[str] = set()
        self.last_event_time: float = 0

    def _accept(self, path: str) -> bool:
        """Return True if the path matches watched extensions and is not excluded."""
        p = Path(path)
        if p.suffix.lower() not in self.extensions:
            return False
        try:
            rel = str(p.relative_to(self.project_dir))
        except ValueError:
            return False
        return not _should_skip_path(rel)

    # Watchdog event callbacks

    def on_modified(self, event: "FileSystemEvent") -> None:
        if event.is_directory:
            return
        if self._accept(event.src_path):
            with self.lock:
                self.pending_paths.add(event.src_path)
                self.last_event_time = time.monotonic()

    def on_created(self, event: "FileSystemEvent") -> None:
        if event.is_directory:
            return
        if self._accept(event.src_path):
            with self.lock:
                self.pending_paths.add(event.src_path)
                self.last_event_time = time.monotonic()

    def on_deleted(self, event: "FileSystemEvent") -> None:
        if event.is_directory:
            return
        if self._accept(event.src_path):
            with self.lock:
                self.pending_paths.add(event.src_path)
                self.last_event_time = time.monotonic()

    def on_moved(self, event: "FileSystemEvent") -> None:
        if event.is_directory:
            return
        # Both the old and new paths are relevant
        if self._accept(event.src_path):
            with self.lock:
                self.pending_paths.add(event.src_path)
                self.last_event_time = time.monotonic()
        dest = getattr(event, "dest_path", None)
        if dest and self._accept(dest):
            with self.lock:
                self.pending_paths.add(dest)
                self.last_event_time = time.monotonic()

    def drain(self) -> tuple[set[str], float]:
        """Atomically drain and return (pending_paths, last_event_time)."""
        with self.lock:
            paths = self.pending_paths.copy()
            ts = self.last_event_time
            self.pending_paths.clear()
            return paths, ts


# ---------------------------------------------------------------------------
# Core watcher class
# ---------------------------------------------------------------------------

class ModFileWatcher:
    """Watches a mod project directory for changes and auto-rebuilds.

    When auto_reload is enabled (default), successful builds automatically
    trigger a hot reload via the bridge client, using the appropriate tier
    based on which files changed:
        - .cs files in Patches/ directories -> tier 1 (patches only)
        - Other .cs files or localization JSON -> tier 2 (entities + patches + loc)
        - Resource/data files (.tscn, .tres, images, audio, scripts, non-localization JSON, etc.) -> tier 3

    Build error deduplication: if a build fails and the next trigger sees the
    same file set in the same on-disk state, the build is skipped to avoid
    spamming identical errors. Saving new contents to the same filenames still
    rebuilds.

    Supports an optional ``on_notification`` callback that receives structured
    dicts for ``build_started`` and ``build_complete`` events.
    """

    def __init__(
        self,
        project_dir: str,
        mods_dir: str,
        mod_name: str = "",
        configuration: str = "Debug",
        extensions: tuple[str, ...] = WATCHED_EXTENSIONS,
        debounce_seconds: float = 1.5,
        on_build_complete: Callable[[dict], None] | None = None,
        game_dir: str | None = None,
        auto_reload: bool = True,
        pool_registrations: list[dict] | None = None,
        on_notification: Callable[[dict], None] | None = None,
    ):
        self.project_dir = Path(project_dir)
        self.mods_dir = mods_dir
        self.mod_name = mod_name
        self.configuration = configuration
        self.game_dir = game_dir
        self.extensions = set(extensions)
        self.debounce_seconds = debounce_seconds
        self.on_build_complete = on_build_complete
        self.on_notification = on_notification
        self.auto_reload = auto_reload
        self.pool_registrations = pool_registrations
        self._has_pck = _resolve_project_context(project_dir).has_pck
        self._running = False
        self._building = False
        self._thread: threading.Thread | None = None
        self._file_mtimes: dict[str, float] = {}
        self._pending_changes: set[str] = set()
        self._last_change_time: float = 0
        self._build_count = 0
        self._last_result: dict[str, Any] | None = None
        self._last_watch_error: str = ""
        self._resource_hashes: dict[str, str] = {}  # path -> content hash for PCK delta
        self._use_watchdog = _HAS_WATCHDOG
        self._observer: Any = None  # watchdog Observer instance
        self._watchdog_handler: _WatchdogHandler | None = None

        # Lock protecting state read by status() from the caller thread while
        # the watcher thread writes.  Covers _pending_changes, _last_result,
        # _building, _build_count, _last_error_message.
        self._state_lock = threading.Lock()

        # Build error deduplication
        self._last_failed_files: frozenset[str] | None = None
        self._last_failed_signature: str | None = None
        self._last_error_message: str = ""

        # Build cancellation: set by the watch loop when new changes arrive
        # during a build so the in-flight subprocess can be killed early.
        self._cancel_build = threading.Event()

        # Load persistent resource hash cache
        self._load_resource_hashes()

    # ------------------------------------------------------------------
    # Persistent resource hash cache
    # ------------------------------------------------------------------

    def _resource_hash_path(self) -> Path:
        return self.project_dir / RESOURCE_HASH_FILENAME

    def _load_resource_hashes(self) -> None:
        """Load resource hashes from the project's persistent cache file."""
        cache_path = self._resource_hash_path()
        if not cache_path.exists():
            return
        try:
            data = json.loads(cache_path.read_text(encoding="utf-8"))
            if isinstance(data, dict):
                self._resource_hashes = {str(k): str(v) for k, v in data.items()}
        except (json.JSONDecodeError, OSError, ValueError) as exc:
            logger.debug("Could not load resource hash cache %s: %s", cache_path, exc)
            self._resource_hashes = {}

    def _save_resource_hashes(self) -> None:
        """Persist resource hashes to disk.  Only writes when content differs."""
        cache_path = self._resource_hash_path()
        new_content = json.dumps(self._resource_hashes, indent=2, ensure_ascii=False) + "\n"
        # Avoid rewriting identical content
        try:
            if cache_path.exists():
                existing = cache_path.read_text(encoding="utf-8")
                if existing == new_content:
                    return
        except OSError:
            pass
        try:
            cache_path.write_text(new_content, encoding="utf-8")
        except OSError as exc:
            logger.debug("Could not write resource hash cache %s: %s", cache_path, exc)

    # ------------------------------------------------------------------
    # Lifecycle
    # ------------------------------------------------------------------

    def start(self) -> dict:
        if self._running:
            return {"error": "Watcher already running"}

        self._running = True

        # Always do an initial scan for baseline mtimes (needed even with watchdog
        # to detect files that changed while the watcher was stopped)
        self._file_mtimes = self._scan_files()

        if self._use_watchdog:
            self._start_watchdog()
        # Start the debounce/trigger loop thread (handles both watchdog and polling)
        self._thread = threading.Thread(target=self._watch_loop, daemon=True)
        self._thread.start()

        return {
            "success": True,
            "project_dir": str(self.project_dir),
            "watching_extensions": sorted(self.extensions),
            "file_count": len(self._file_mtimes),
            "auto_reload": self.auto_reload,
            "has_pck": self._has_pck,
            "backend": "watchdog" if self._use_watchdog else "polling",
        }

    def stop(self) -> dict:
        if not self._running:
            return {"error": "Watcher not running"}

        self._running = False

        # Stop watchdog observer if running
        if self._observer is not None:
            try:
                self._observer.stop()
                self._observer.join(timeout=5)
            except Exception:
                pass
            self._observer = None
            self._watchdog_handler = None

        if self._thread and self._thread.is_alive():
            # Wait longer if a build is in flight
            timeout = 30 if self._building else 3
            self._thread.join(timeout=timeout)

        # Persist resource hashes on shutdown
        self._save_resource_hashes()

        return {
            "success": True,
            "build_count": self._build_count,
            "last_result": self._last_result,
            "was_building": self._building,
        }

    def status(self) -> dict:
        with self._state_lock:
            pending_snapshot = set(self._pending_changes)
            last_result = self._last_result
            building = self._building
            build_count = self._build_count
            last_error = self._last_error_message
        return {
            "running": self._running,
            "building": building,
            "project_dir": str(self.project_dir),
            "build_count": build_count,
            "last_result": last_result,
            "watched_files": len(self._file_mtimes),
            "auto_reload": self.auto_reload,
            "has_pck": self._has_pck,
            "backend": "watchdog" if self._use_watchdog else "polling",
            "pending_changes": sorted(
                str(Path(path).relative_to(self.project_dir))
                for path in pending_snapshot
                if Path(path).is_relative_to(self.project_dir)
            ),
            "last_watch_error": self._last_watch_error,
            "last_error_message": last_error,
        }

    # ------------------------------------------------------------------
    # Watchdog integration
    # ------------------------------------------------------------------

    def _start_watchdog(self) -> None:
        """Initialize and start a watchdog Observer on the project directory."""
        self._watchdog_handler = _WatchdogHandler(self.project_dir, self.extensions)
        self._observer = Observer()
        self._observer.schedule(
            self._watchdog_handler,
            str(self.project_dir),
            recursive=True,
        )
        self._observer.daemon = True
        self._observer.start()

    # ------------------------------------------------------------------
    # File scanning (polling fallback + initial baseline)
    # ------------------------------------------------------------------

    def _scan_files(self) -> dict[str, float]:
        """Single tree walk collecting mtimes for all watched extensions."""
        mtimes: dict[str, float] = {}
        for f in self.project_dir.rglob("*"):
            if not f.is_file():
                continue
            if f.suffix.lower() not in self.extensions:
                continue
            try:
                rel = str(f.relative_to(self.project_dir))
            except ValueError:
                continue
            if _should_skip_path(rel):
                continue
            try:
                mtimes[str(f)] = f.stat().st_mtime
            except OSError:
                pass
        return mtimes

    # ------------------------------------------------------------------
    # Main watch loop (debounce + trigger)
    # ------------------------------------------------------------------

    def _watch_loop(self) -> None:
        while self._running:
            try:
                if self._use_watchdog:
                    self._poll_watchdog_events()
                else:
                    self._poll_filesystem()

                # Debounce: only trigger when enough quiet time has elapsed
                if (
                    self._pending_changes
                    and self._last_change_time
                    and time.monotonic() - self._last_change_time >= self.debounce_seconds
                ):
                    with self._state_lock:
                        pending = sorted(self._pending_changes)
                        self._pending_changes.clear()
                    self._cancel_build.clear()
                    self._trigger_build(pending)

                    # Post-build coalescing: drain events that arrived during
                    # the build and reset the debounce timer so rapid saves
                    # don't trigger a build storm.
                    if self._use_watchdog:
                        self._poll_watchdog_events()
                    else:
                        self._poll_filesystem()
                    if self._pending_changes:
                        self._last_change_time = time.monotonic()

                time.sleep(0.5)
            except Exception as exc:
                self._last_watch_error = str(exc)
                time.sleep(1)

    def _poll_watchdog_events(self) -> None:
        """Drain pending paths from the watchdog handler into _pending_changes."""
        if self._watchdog_handler is None:
            return
        paths, last_ts = self._watchdog_handler.drain()
        if paths:
            self._pending_changes.update(paths)
            self._last_change_time = last_ts
            # Signal build cancellation if a build is in flight
            if self._building:
                self._cancel_build.set()

    def _poll_filesystem(self) -> None:
        """Polling-based change detection (fallback when watchdog is unavailable)."""
        current = self._scan_files()
        changed: list[str] = []

        for path, mtime in current.items():
            if path not in self._file_mtimes or self._file_mtimes[path] != mtime:
                changed.append(path)

        for path in self._file_mtimes:
            if path not in current:
                changed.append(path)

        if changed:
            self._file_mtimes = current
            self._pending_changes.update(changed)
            self._last_change_time = time.monotonic()
            # Signal build cancellation if a build is in flight
            if self._building:
                self._cancel_build.set()

    # ------------------------------------------------------------------
    # Resource hash helpers
    # ------------------------------------------------------------------

    def _check_resource_hashes_changed(self, changed_files: list[str]) -> bool:
        """Check if any resource files in the changed set have actually changed content."""
        changed = False
        hashes_updated = False
        for f in changed_files:
            if not self._should_track_resource_hash(f):
                continue
            try:
                content_hash = hashlib.sha256(Path(f).read_bytes()).hexdigest()
                if self._resource_hashes.get(f) != content_hash:
                    self._resource_hashes[f] = content_hash
                    changed = True
                    hashes_updated = True
            except OSError:
                if f in self._resource_hashes:
                    self._resource_hashes.pop(f, None)
                    hashes_updated = True
                changed = True  # Can't read = assume changed
        # Persist if hashes were updated
        if hashes_updated:
            self._save_resource_hashes()
        return changed

    def _should_track_resource_hash(self, file_path: str) -> bool:
        """Return True when the file participates in tier-3 PCK content changes."""
        suffix = Path(file_path).suffix.lower()
        if suffix not in RESOURCE_RELOAD_EXTENSIONS:
            return False
        if suffix != ".json":
            return True

        normalized = file_path.lower().replace("\\", "/")
        basename = Path(file_path).name.lower()
        return "localization" in normalized or basename not in NON_RESOURCE_JSON_NAMES

    # ------------------------------------------------------------------
    # Build error deduplication
    # ------------------------------------------------------------------

    def _fingerprint_changed_files(self, changed_files: list[str]) -> tuple[frozenset[str], str]:
        """Return a stable fingerprint of the current file-state for deduplication."""
        normalized_files = sorted(str(Path(path).resolve()) for path in changed_files)
        digest = hashlib.sha256()
        for path in normalized_files:
            digest.update(path.encode("utf-8", errors="surrogatepass"))
            digest.update(b"\0")
            file_path = Path(path)
            try:
                stat = file_path.stat()
            except OSError:
                digest.update(b"missing\0")
                continue
            digest.update(str(stat.st_mtime_ns).encode("ascii"))
            digest.update(b":")
            digest.update(str(stat.st_size).encode("ascii"))
            digest.update(b"\0")
        return frozenset(normalized_files), digest.hexdigest()

    def _should_skip_duplicate_error(self, changed_files: list[str]) -> bool:
        """Return True if the current file-state matches the last failed build."""
        if self._last_failed_files is None or self._last_failed_signature is None:
            return False
        current_set, current_signature = self._fingerprint_changed_files(changed_files)
        return current_set == self._last_failed_files and current_signature == self._last_failed_signature

    def _record_build_failure(self, changed_files: list[str], error_msg: str) -> None:
        """Record the failed file-state and error for deduplication."""
        self._last_failed_files, self._last_failed_signature = self._fingerprint_changed_files(changed_files)
        self._last_error_message = error_msg

    def _clear_error_cache(self) -> None:
        """Clear error deduplication state (e.g., on successful build or new file set)."""
        self._last_failed_files = None
        self._last_failed_signature = None
        self._last_error_message = ""

    # ------------------------------------------------------------------
    # Notification helper
    # ------------------------------------------------------------------

    def _notify(self, payload: dict) -> None:
        """Fire the on_notification callback if registered."""
        if self.on_notification is not None:
            try:
                self.on_notification(payload)
            except Exception:
                pass

    # ------------------------------------------------------------------
    # Build trigger
    # ------------------------------------------------------------------

    def _trigger_build(self, changed_files: list[str]) -> None:
        with self._state_lock:
            self._building = True
            self._build_count += 1
            build_num = self._build_count
        try:
            tier = determine_reload_tier(changed_files, has_pck=self._has_pck)
            if tier == 0:
                # No recognized file types changed -- skip build
                result = {
                    "build_number": build_num,
                    "skipped": True,
                    "reason": "No recognized file types in change set",
                    "changed_files": self._rel_files(changed_files),
                }
                with self._state_lock:
                    self._last_result = result
                return

            # Build error deduplication: skip if exact same files failed last time
            if self._should_skip_duplicate_error(changed_files):
                result = {
                    "build_number": build_num,
                    "skipped": True,
                    "reason": "Same file set as last failed build (error dedup)",
                    "last_error": self._last_error_message,
                    "changed_files": self._rel_files(changed_files),
                }
                with self._state_lock:
                    self._last_result = result
                self._notify({"type": "build_skipped", **result})
                return

            # Pre-build validation: catch obvious project errors before running
            # a potentially slow dotnet build.
            validation = validate_project(self.project_dir)
            if not validation.get("valid"):
                result = {
                    "build_number": build_num,
                    "success": False,
                    "phase": "validation",
                    "error": "Project validation failed; build skipped",
                    "validation": validation,
                    "changed_files": self._rel_files(changed_files),
                }
                with self._state_lock:
                    self._last_result = result
                self._notify({"type": "validation_failed", **result})
                return

            # PCK delta build: only rebuild when resource file contents changed
            needs_pck = self._has_pck and tier >= 3
            if needs_pck:
                needs_pck = self._check_resource_hashes_changed(changed_files)
                if not needs_pck:
                    tier = min(tier, 2)  # Downgrade: no PCK content changes

            rel_files = self._rel_files(changed_files)

            # Notify: build started
            self._notify({
                "type": "build_started",
                "changed_files": rel_files,
                "tier": tier,
                "build_number": build_num,
            })

            if self.auto_reload:
                # Unified build + deploy + hot reload via shared helper.
                # This eliminates duplicated DLL-finding and reload logic.
                result = build_deploy_and_hot_reload_project(
                    self.project_dir,
                    mods_dir=self.mods_dir,
                    mod_name=self.mod_name,
                    configuration=self.configuration,
                    tier=tier,
                    build_pck_first=needs_pck,
                    game_dir=self.game_dir,
                    pool_registrations=self.pool_registrations,
                    auto_detect_pools=self.pool_registrations is None,
                    changed_files=changed_files,
                    cancel_event=self._cancel_build,
                )
            else:
                result = build_and_deploy_project(
                    self.project_dir,
                    mods_dir=self.mods_dir,
                    mod_name=self.mod_name,
                    configuration=self.configuration,
                    game_dir=self.game_dir,
                    stamp_version=False,
                    build_pck_first=needs_pck,
                    cancel_event=self._cancel_build,
                )

            result["build_number"] = build_num
            result["changed_files"] = rel_files

            # Update error dedup state
            if result.get("success"):
                self._clear_error_cache()
            else:
                error_msg = self._extract_error_message(result)
                self._record_build_failure(changed_files, error_msg)

            with self._state_lock:
                self._last_result = result

            # Notify with phase-specific event type
            self._notify({"type": self._result_notification_type(result), **result})

            if self.on_build_complete:
                try:
                    self.on_build_complete(result)
                except Exception:
                    pass
        finally:
            with self._state_lock:
                self._building = False

    # ------------------------------------------------------------------
    # Helpers
    # ------------------------------------------------------------------

    def _rel_files(self, changed_files: list[str]) -> list[str]:
        """Convert absolute paths to project-relative paths."""
        return [
            str(Path(f).relative_to(self.project_dir))
            for f in changed_files
            if Path(f).is_relative_to(self.project_dir)
        ]

    @staticmethod
    def _extract_error_message(result: dict) -> str:
        """Pull the most informative error string from a build/reload result."""
        error_msg = result.get("error", "")
        if not error_msg:
            hr = result.get("hot_reload")
            if isinstance(hr, dict):
                error_msg = hr.get("error", "")
        if not error_msg:
            build = result.get("build")
            if isinstance(build, dict):
                error_msg = (build.get("stderr") or build.get("stdout") or "")[:500]
        return error_msg

    @staticmethod
    def _result_notification_type(result: dict) -> str:
        """Return a phase-specific notification type for a build/reload result."""
        if result.get("success"):
            if result.get("hot_reload"):
                return "hot_reload_complete"
            return "build_complete"
        # Failure — determine which phase failed
        if result.get("cancelled"):
            return "build_cancelled"
        hr = result.get("hot_reload")
        if isinstance(hr, dict) and ("error" in hr or not hr.get("success")):
            return "hot_reload_failed"
        return "build_failed"


# ---------------------------------------------------------------------------
# Module-level multi-project watcher management
# ---------------------------------------------------------------------------

_active_watchers: dict[str, ModFileWatcher] = {}
_watchers_lock = threading.Lock()


def start_watching(
    project_dir: str,
    mods_dir: str,
    mod_name: str = "",
    configuration: str = "Debug",
    game_dir: str | None = None,
    auto_reload: bool = True,
    pool_registrations: list[dict] | None = None,
    debounce_seconds: float = 1.5,
    on_notification: Callable[[dict], None] | None = None,
) -> dict:
    """Start watching a mod project for changes.

    Multiple projects can be watched concurrently.  If the same project is
    already being watched, returns an error (stop it first).
    """
    key = _normalize_project_key(project_dir)

    with _watchers_lock:
        existing = _active_watchers.get(key)
        if existing is not None and existing._running:
            return {"error": f"A watcher is already running for {key}. Stop it first."}

        watcher = ModFileWatcher(
            project_dir=project_dir,
            mods_dir=mods_dir,
            mod_name=mod_name,
            configuration=configuration,
            game_dir=game_dir,
            auto_reload=auto_reload,
            pool_registrations=pool_registrations,
            debounce_seconds=debounce_seconds,
            on_notification=on_notification,
        )
        result = watcher.start()
        if result.get("success"):
            _active_watchers[key] = watcher
        return result


def stop_watching(project_dir: str | None = None) -> dict:
    """Stop file watcher(s).

    If *project_dir* is given, stop only that project's watcher.
    If omitted, stop ALL active watchers.
    """
    with _watchers_lock:
        if project_dir is not None:
            return _stop_single(project_dir)
        else:
            return _stop_all()


def _stop_single(project_dir: str) -> dict:
    """Stop a single watcher by project path.  Must be called under _watchers_lock."""
    key = _normalize_project_key(project_dir)
    watcher = _active_watchers.pop(key, None)
    if watcher is None:
        return {"error": f"No active watcher for {key}"}

    result = watcher.stop()
    # If a build is still finishing, schedule a background thread to wait for it
    if watcher._building:
        watcher_ref = watcher

        def _cleanup() -> None:
            if watcher_ref._thread and watcher_ref._thread.is_alive():
                watcher_ref._thread.join(timeout=30)

        threading.Thread(target=_cleanup, daemon=True).start()
    return result


def _stop_all() -> dict:
    """Stop all active watchers.  Must be called under _watchers_lock."""
    if not _active_watchers:
        return {"error": "No active watchers"}

    results: dict[str, dict] = {}
    keys = list(_active_watchers.keys())
    for key in keys:
        watcher = _active_watchers.pop(key, None)
        if watcher is None:
            continue
        result = watcher.stop()
        results[key] = result
        if watcher._building:
            watcher_ref = watcher

            def _cleanup(ref: ModFileWatcher = watcher_ref) -> None:
                if ref._thread and ref._thread.is_alive():
                    ref._thread.join(timeout=30)

            threading.Thread(target=_cleanup, daemon=True).start()

    return {"success": True, "stopped": results}


def watcher_status(project_dir: str | None = None) -> dict:
    """Get status of file watcher(s).

    If *project_dir* is given, return status of that project's watcher.
    If omitted, return status of ALL watchers (or ``{"running": False}`` if none).
    """
    with _watchers_lock:
        if project_dir is not None:
            key = _normalize_project_key(project_dir)
            watcher = _active_watchers.get(key)
            if watcher is None:
                return {"running": False, "project_dir": project_dir}
            return watcher.status()
        else:
            if not _active_watchers:
                return {"running": False}
            return {
                "running": True,
                "watcher_count": len(_active_watchers),
                "watchers": {
                    key: w.status() for key, w in _active_watchers.items()
                },
            }
