"""Add only our managed MCP entry to the existing Desktop profile; retain all other text."""
import datetime
import json
from pathlib import Path
import shutil
import sys
import yaml

ROOT = Path(__file__).resolve().parents[1]
PROFILE = Path.home() / ".dsh/profiles/desktop/cordis.patch.yml"
START = "# BEGIN ARCGIS PRO DESKTOP MCP (managed by ArcGISPro-Codex)"
END = "# END ARCGIS PRO DESKTOP MCP"

def main():
    manifest_path = PROFILE.parent / "package.json"
    if manifest_path.is_file():
        manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
        if "dsh-plugin-arcgis-pro" in manifest.get("dsh", {}).get("profile", {}).get("bundles", []):
            print(json.dumps({"bundleInstalled": True, "message": "ArcGIS is managed by the installed Harness plugin; no duplicate overlay added."}))
            return
    text = PROFILE.read_text(encoding="utf-8-sig")
    original = yaml.safe_load(text) or []
    if not isinstance(original, list):
        raise RuntimeError("Desktop patch is not a YAML array; no changes made.")
    if text.count(START) != text.count(END) or text.count(START) > 1:
        raise RuntimeError("Managed block is ambiguous; no changes made.")
    if START in text:
        before, remaining = text.split(START, 1)
        _, after = remaining.split(END, 1)
        text = before + after.lstrip("\r\n")
    existing = yaml.safe_load(text) or []
    for row in existing:
        for candidate in [row] + row.get("insert", []):
            if candidate.get("config", {}).get("serverName") == "arcgis-pro":
                raise RuntimeError("An unmanaged arcgis-pro MCP entry already exists; no changes made.")
    entry = {"insert": [{"id": "arcgis-pro-desktop", "name": "@deepseek-ai/dsh-mcp-client", "config": {
        "serverName": "arcgis-pro", "transport": "stdio", "command": sys.executable,
        "args": ["-X", "utf8", str(ROOT / "harness/server.py")], "cwd": str(ROOT),
        "toolCallTimeoutMs": 200000, "failOnStartupError": True
    }}]}
    result = text.rstrip() + "\n\n" + START + "\n" + yaml.safe_dump([entry], allow_unicode=True, sort_keys=False) + END + "\n"
    parsed = yaml.safe_load(result)
    if parsed[:-1] != existing:
        raise RuntimeError("Unrelated configuration changed; no changes made.")
    backup = PROFILE.with_name(PROFILE.name + ".before-arcgis-" + datetime.datetime.now().strftime("%Y%m%d-%H%M%S-%f"))
    shutil.copy2(PROFILE, backup)
    temp = PROFILE.with_suffix(".arcgis.tmp")
    temp.write_text(result, encoding="utf-8")
    temp.replace(PROFILE)
    print(json.dumps({"profile": str(PROFILE), "backup": str(backup), "serverName": "arcgis-pro", "preservedEntries": len(existing)}, ensure_ascii=False))

if __name__ == "__main__":
    main()
