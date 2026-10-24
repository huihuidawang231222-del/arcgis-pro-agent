"""Desktop Harness MCP entry, with GIS workflow instructions supplied at discovery."""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "mcp"))
import server

dispatch = server.dispatch

def desktop_dispatch(method, params):
    result = dispatch(method, params)
    if method == "initialize":
        guide = (ROOT / "plugins/arcgis-pro-map/skills/natural-language-map/SKILL.md").read_text(encoding="utf-8-sig")
        if guide.startswith("---"):
            guide = guide.split("---", 2)[-1].strip()
        guide = guide.replace("Codex", "DeepSeek Harness").replace("arcgis-pro-map MCP", "arcgis-pro MCP")
        result["instructions"] += "\n\n" + guide
    return result

server.dispatch = desktop_dispatch
if __name__ == "__main__":
    server.main()
