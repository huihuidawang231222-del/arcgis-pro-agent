"""Build a local installable DSH bundle and safely migrate the prior managed overlay."""
import argparse
import datetime
import json
from pathlib import Path
import shutil
import sys
import yaml

ROOT = Path(__file__).resolve().parents[1]
BUNDLE = ROOT / "harness/dsh-plugin-arcgis-pro"

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--migrate-overlay", action="store_true")
    args = parser.parse_args()
    (BUNDLE / "mcp").mkdir(exist_ok=True)
    for name in ("server.py", "analysis-schema.json"):
        shutil.copy2(ROOT / "mcp" / name, BUNDLE / "mcp" / name)
    guide = (ROOT / "plugins/arcgis-pro-map/skills/natural-language-map/SKILL.md").read_text(encoding="utf-8-sig").split("---", 2)[-1].strip()
    (BUNDLE / "instructions.md").write_text(guide.replace("Codex", "DeepSeek Harness").replace("arcgis-pro-map MCP", "arcgis-pro MCP"), encoding="utf-8")
    entry = {"insert": [{"id": "arcgis-pro-desktop", "name": "@deepseek-ai/dsh-mcp-client", "config": {"serverName": "arcgis-pro", "transport": "stdio", "command": sys.executable, "args": ["-X", "utf8", str(BUNDLE / "server.py")], "cwd": str(BUNDLE), "toolCallTimeoutMs": 200000, "failOnStartupError": True}}]}
    (BUNDLE / "cordis.patch.yml").write_text(yaml.safe_dump([entry], allow_unicode=True, sort_keys=False), encoding="utf-8")
    report = {"bundle": str(BUNDLE), "manifest": json.loads((BUNDLE / "package.json").read_text(encoding="utf-8"))["name"]}
    if args.migrate_overlay:
        profile = Path.home() / ".dsh/profiles/desktop/cordis.patch.yml"
        text = profile.read_text(encoding="utf-8-sig")
        begin = "# BEGIN ARCGIS PRO DESKTOP MCP (managed by ArcGISPro-Codex)"
        end = "# END ARCGIS PRO DESKTOP MCP"
        if text.count(begin) != 1 or text.count(end) != 1:
            raise RuntimeError("Prior managed overlay not uniquely identified; no profile changes made.")
        before, rest = text.split(begin, 1)
        _, after = rest.split(end, 1)
        remaining = before.rstrip() + "\n" + after.lstrip("\r\n")
        old_rows = yaml.safe_load(text)
        new_rows = yaml.safe_load(remaining)
        if old_rows[:-1] != new_rows or old_rows[-1] != yaml.safe_load(rest.split(end, 1)[0])[0]:
            raise RuntimeError("Overlay shape changed; no profile changes made.")
        backup = profile.with_name(profile.name + ".before-bundle-" + datetime.datetime.now().strftime("%Y%m%d-%H%M%S-%f"))
        shutil.copy2(profile, backup)
        temporary = profile.with_suffix(".bundle.tmp")
        temporary.write_text(remaining, encoding="utf-8")
        temporary.replace(profile)
        report.update(backup=str(backup), preservedEntries=len(new_rows))
    print(json.dumps(report, ensure_ascii=False))

if __name__ == "__main__":
    main()
