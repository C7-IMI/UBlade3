#!/usr/bin/env python3
import hashlib
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

def main():
    failures = []
    checked = 0
    text_suffixes = {".md", ".json", ".cs", ".py", ".ps1", ".txt", ".yml", ".yaml", ".gitignore", ".gitmodules"}
    for path in ROOT.rglob("*"):
        if not path.is_file(): continue
        rel = path.relative_to(ROOT).as_posix()
        if rel.startswith("third_party/") or rel.startswith("unity/UBlade3/Library/") or rel.startswith("unity/UBlade3/Temp/") or rel.startswith("unity/UBlade3/build/") or rel.startswith("unity/UBlade3/build-native/") or rel.startswith("unity/UBlade3/build-match/") or rel.startswith("artifacts/ublade3/") or rel.startswith("workstream/state/") or rel.startswith("runtime/play/") or rel.startswith("dist/") or rel.startswith(".git/"): continue
        if rel in ("artifacts/rea/full-desktop.json", "artifacts/rea/hyb-entropy.json", "artifacts/ublade3-release.json", "artifacts/ublade3-csharp-release.json", "artifacts/ublade3-provenance.json", "artifacts/ublade3-sbom.json"): continue
        if path.suffix.lower() not in text_suffixes and path.name not in (".gitignore", ".gitmodules", "LICENSE"): continue
        checked += 1
        data = path.read_bytes()
        if data.startswith(b"\xef\xbb\xbf"): failures.append(rel + ": BOM")
        if b"\r" in data: failures.append(rel + ": CR")
    for required in ("README.md", "docs/PORT-PLAN.md", "docs/EXACTNESS.md", "unity/UBlade3/ProjectSettings/ProjectVersion.txt"):
        if not (ROOT / required).exists(): failures.append("missing " + required)
    version = (ROOT / "unity/UBlade3/ProjectSettings/ProjectVersion.txt").read_text(encoding="utf-8")
    if "6000.6.7f1" not in version: failures.append("Unity version not pinned to 6000.6.7f1")
    print("UBlade3 checks: " + str(checked) + " text files")
    if failures:
        for failure in failures: print("FAIL " + failure)
        sys.exit(1)
    print("UBlade3 checks: PASS")

if __name__ == "__main__":
    main()
