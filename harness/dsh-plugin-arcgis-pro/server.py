"""Self-contained MCP bundle entry for Harness Desktop."""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "mcp"))
import server

original_dispatch = server.dispatch

def dispatch(method, params):
    result = original_dispatch(method, params)
    if method == "initialize":
        result["instructions"] += "\n\n" + (ROOT / "instructions.md").read_text(encoding="utf-8")
    return result

server.dispatch = dispatch
if __name__ == "__main__":
    server.main()
