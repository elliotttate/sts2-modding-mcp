"""TCP client for communicating with the MCPTest bridge mod running inside STS2."""

import json
import socket
from typing import Optional

BRIDGE_HOST = "127.0.0.1"
BRIDGE_PORT = 21337
TIMEOUT = 5.0


def send_request(method: str, params: dict | None = None, request_id: int = 1) -> dict:
    """Send a JSON-RPC request to the bridge mod and return the parsed response."""
    request = {"method": method, "id": request_id}
    if params:
        request["params"] = params

    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(TIMEOUT)
        sock.connect((BRIDGE_HOST, BRIDGE_PORT))

        payload = json.dumps(request) + "\n"
        sock.sendall(payload.encode("utf-8"))

        # Read response (newline-delimited)
        data = b""
        while True:
            chunk = sock.recv(4096)
            if not chunk:
                break
            data += chunk
            if b"\n" in data or b"\r" in data:
                break

        sock.close()

        # Strip BOM and whitespace
        text = data.decode("utf-8-sig").strip()
        if not text:
            return {"error": "Empty response from bridge"}

        return json.loads(text)

    except ConnectionRefusedError:
        return {"error": "Bridge not running. Is the game running with MCPTest mod loaded?"}
    except socket.timeout:
        return {"error": "Bridge timed out. Game may be loading or unresponsive."}
    except Exception as e:
        return {"error": f"Bridge communication failed: {type(e).__name__}: {e}"}


def ping() -> dict:
    return send_request("ping")


def get_run_state() -> dict:
    return send_request("get_run_state")


def get_combat_state() -> dict:
    return send_request("get_combat_state")


def get_player_state() -> dict:
    return send_request("get_player_state")


def get_screen() -> dict:
    return send_request("get_screen")


def get_map_state() -> dict:
    return send_request("get_map_state")


def get_available_actions() -> dict:
    return send_request("get_available_actions")


def execute_console_command(command: str) -> dict:
    return send_request("console", {"command": command})


def play_card(card_index: int, target_index: int = -1) -> dict:
    return send_request("play_card", {"card_index": card_index, "target_index": target_index})


def end_turn() -> dict:
    return send_request("end_turn")


def start_run(character: str = "Ironclad", ascension: int = 0) -> dict:
    return send_request("start_run", {"character": character, "ascension": ascension})


def is_connected() -> bool:
    """Check if bridge is reachable."""
    try:
        result = ping()
        return "error" not in result and result.get("result", {}).get("status") == "ok"
    except Exception:
        return False
