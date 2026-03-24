"""TCP client for communicating with the GodotExplorer mod running inside the game.

GodotExplorer is a Godot scene inspector/debugger mod that runs an MCP-compatible
TCP server on port 27020.  This client sends JSON-RPC 2.0 `tools/call` requests
and returns the parsed text results.
"""

import json
import socket
from typing import Any

EXPLORER_HOST = "127.0.0.1"
EXPLORER_PORT = 27020
TIMEOUT = 15.0
MAX_RESPONSE_SIZE = 10 * 1024 * 1024  # 10 MB
RECV_BUFFER_SIZE = 4096

_next_id = 0


def _get_id() -> int:
    global _next_id
    _next_id += 1
    return _next_id


def _send_rpc(method: str, params: dict | None = None) -> dict:
    """Send a JSON-RPC request to GodotExplorer and return the parsed response."""
    request = {
        "jsonrpc": "2.0",
        "id": _get_id(),
        "method": method,
    }
    if params is not None:
        request["params"] = params

    try:
        with socket.create_connection((EXPLORER_HOST, EXPLORER_PORT), timeout=TIMEOUT) as sock:
            sock.settimeout(TIMEOUT)
            payload = json.dumps(request) + "\n"
            sock.sendall(payload.encode("utf-8"))

            # Read newline-delimited response
            data = b""
            while True:
                chunk = sock.recv(RECV_BUFFER_SIZE)
                if not chunk:
                    break
                data += chunk
                if len(data) > MAX_RESPONSE_SIZE:
                    return {"error": f"Response exceeded {MAX_RESPONSE_SIZE} bytes"}
                if b"\n" in data:
                    break

        text = data.decode("utf-8-sig").strip()
        if not text:
            return {"error": "Empty response from GodotExplorer"}

        return json.loads(text)

    except ConnectionRefusedError:
        return {
            "error": (
                "GodotExplorer not running (port 27020). "
                "Launch the game — the GodotExplorer mod is auto-installed by this MCP. "
                "Press F12 in-game to toggle the visual inspector."
            )
        }
    except socket.timeout:
        return {"error": "GodotExplorer timed out. Game may be loading or unresponsive."}
    except Exception as e:
        return {"error": f"GodotExplorer communication failed: {type(e).__name__}: {e}"}


def _call_tool(tool_name: str, arguments: dict | None = None) -> dict:
    """Call a GodotExplorer tool and return the result.

    Returns a dict with either:
      - "text": the tool's text output
      - "error": an error message
    """
    params: dict[str, Any] = {"name": tool_name}
    if arguments:
        params["arguments"] = arguments

    response = _send_rpc("tools/call", params)

    # Handle JSON-RPC level errors
    if "error" in response and isinstance(response["error"], str):
        return response
    if "error" in response and isinstance(response["error"], dict):
        return {"error": response["error"].get("message", str(response["error"]))}

    # Extract MCP tool result
    result = response.get("result", {})
    if isinstance(result, dict):
        content = result.get("content", [])
        is_error = result.get("isError", False)
        texts = [c.get("text", "") for c in content if isinstance(c, dict) and c.get("text")]
        text = "\n".join(texts) if texts else json.dumps(result)
        if is_error:
            return {"error": text}
        return {"text": text}

    return {"text": str(result)}


def _parse_text(result: dict) -> str | dict:
    """Convert a _call_tool result to either parsed JSON or raw text."""
    if "error" in result:
        return result
    text = result.get("text", "")
    try:
        return json.loads(text)
    except (json.JSONDecodeError, TypeError):
        return text


# ── Tool wrappers ──────────────────────────────────────────────────────────


def ping() -> dict:
    """Check if GodotExplorer is reachable."""
    return _send_rpc("ping")


def is_connected() -> bool:
    """Check if GodotExplorer is reachable."""
    try:
        result = ping()
        return "error" not in result
    except Exception:
        return False


def get_scene_tree(depth: int = 3, root_path: str = "/root"):
    args: dict[str, Any] = {}
    if depth != 3:
        args["depth"] = depth
    if root_path != "/root":
        args["root_path"] = root_path
    return _parse_text(_call_tool("get_scene_tree", args or None))


def find_nodes(pattern: str, type_filter: str = "", limit: int = 50):
    args: dict[str, Any] = {"pattern": pattern}
    if type_filter:
        args["type"] = type_filter
    if limit != 50:
        args["limit"] = limit
    return _parse_text(_call_tool("find_nodes", args))


def inspect_node(path: str):
    return _parse_text(_call_tool("inspect_node", {"path": path}))


def get_property(path: str, property_name: str):
    return _parse_text(_call_tool("get_property", {"path": path, "property": property_name}))


def set_property(path: str, property_name: str, value: str):
    return _parse_text(_call_tool("set_property", {"path": path, "property": property_name, "value": value}))


def call_method(path: str, method: str, method_args: str = ""):
    args: dict[str, Any] = {"path": path, "method": method}
    if method_args:
        args["args"] = method_args
    return _parse_text(_call_tool("call_method", args))


def toggle_visibility(path: str):
    return _parse_text(_call_tool("toggle_visibility", {"path": path}))


def get_node_count():
    return _parse_text(_call_tool("get_node_count"))


def list_groups(group: str = ""):
    args = {"group": group} if group else None
    return _parse_text(_call_tool("list_groups", args))


def get_game_info():
    return _parse_text(_call_tool("get_game_info"))


def list_assemblies():
    return _parse_text(_call_tool("list_assemblies"))


def search_types(query: str):
    return _parse_text(_call_tool("search_types", {"query": query}))


def inspect_type(type_name: str):
    return _parse_text(_call_tool("inspect_type", {"type_name": type_name}))


def tween_property(
    path: str,
    property_name: str,
    to: str,
    from_val: str = "",
    duration: str = "1.0",
    loops: int = 0,
    trans: str = "linear",
):
    args: dict[str, Any] = {"path": path, "property": property_name, "to": to}
    if from_val:
        args["from"] = from_val
    if duration != "1.0":
        args["duration"] = duration
    if loops != 0:
        args["loops"] = loops
    if trans != "linear":
        args["trans"] = trans
    return _parse_text(_call_tool("tween_property", args))
