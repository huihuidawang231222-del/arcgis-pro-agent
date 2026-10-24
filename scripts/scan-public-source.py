"""Scan tracked public source; print only file paths/rule names, never matching values."""
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
RULES = {
    "provider-key": re.compile(rb"\b[0-9a-f]{32}\.[A-Za-z0-9_-]{12,}\b"),
    "openai-style-key": re.compile(rb"\bsk-(?:proj-|ant-)?[A-Za-z0-9_-]{24,}\b"),
    "github-token": re.compile(rb"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{30,})\b"),
    "private-key": re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
}


def main():
    files = subprocess.check_output(["git", "ls-files", "-z"], cwd=ROOT).split(b"\0")
    if not any(files):
        raise SystemExit("No tracked files. Stage the intended public source first.")
    failures = []
    count = 0
    for raw in files:
        if not raw:
            continue
        name = raw.decode("utf-8")
        path = ROOT / name
        count += 1
        if any(part in {"outputs", ".dsh", ".tools", "__pycache__", "bin", "obj"} for part in path.relative_to(ROOT).parts) or path.name in {"providers.json", "auth.json", ".env", "cordis.patch.yml"}:
            failures.append((name, "private-artifact"))
        for label, pattern in RULES.items():
            if pattern.search(path.read_bytes()):
                failures.append((name, label))
    for name, rule in failures:
        print(f"FAIL {rule}: {name}")
    print(f"Scanned {count} tracked files; findings: {len(failures)}")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
