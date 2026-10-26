"""Validate beginner-facing links, release naming and unchanged UI placeholders.
Read-only checks; no network, credentials, personal data or executable launches.
Run with Python 3.10+: python tests/check_public_docs.py
"""
from __future__ import annotations
import json
from pathlib import Path
import re
from urllib.parse import unquote, urlsplit
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
DOCS = ["README.md", "README.zh-TW.md", "PRIVACY.md", "SECURITY.md",
        "CONTRIBUTING.md", "README_BUILD.md", "docs/START_HERE.md",
        "docs/AI_HELP.md", "docs/ADVANCED.md", "docs/privacy-and-limits.zh-TW.md",
        "docs/reviews/2026-10-26.md"]

def main() -> int:
    errors: list[str] = []
    project = ET.parse(ROOT / "src/CodexUsageMonitor.csproj").getroot()
    version = project.findtext("PropertyGroup/Version")
    assert version
    documents = DOCS + [f"docs/releases/v{version}.md"]
    local_links = 0
    for name in documents:
        path = ROOT / name
        text = path.read_text(encoding="utf-8-sig")
        if "REVIEW_RESULTS_PENDING" in text or "REVIEW_HASHES_PENDING" in text:
            errors.append(f"{name}: release verification has not been filled in")
        for href in re.findall(r"\]\(([^\n)]+)\)", text):
            parsed = urlsplit(href)
            if parsed.scheme or not parsed.path:
                continue
            local_links += 1
            target = (path.parent / unquote(parsed.path)).resolve()
            if not target.is_relative_to(ROOT) or not target.is_file():
                errors.append(f"{name}: missing or out-of-tree link {href}")
    filename = f"CodexUsageMonitor-v{version}-win-x64.zip"
    readme = (ROOT / "README.md").read_text(encoding="utf-8")
    if filename not in readme or f"/releases/tag/v{version}" not in readme:
        errors.append("README download does not match project version")
    language = (ROOT / "src/L.cs").read_text(encoding="utf-8")
    if f'Version="{version}"' not in language:
        errors.append("UI version differs from project version")
    portable = (ROOT / "docs/PORTABLE_README.txt").read_text(encoding="utf-8")
    if version not in portable or "https://github.com/" not in portable:
        errors.append("Offline instructions lack version or complete links")
    if "v0.4.0-rc.3" in (ROOT / "SECURITY.md").read_text(encoding="utf-8"):
        errors.append("Security page still claims an obsolete current release")
    resource_checks = 0
    for group in ("Ui", "History", "Radar", "Report"):
        def values(suffix: str) -> dict[str, str]:
            doc = ET.parse(ROOT / f"src/Resources/{group}{suffix}.resx").getroot()
            result = {}
            for item in doc.findall("data"):
                key = item.attrib["name"]
                if key in result:
                    errors.append(f"{group}{suffix}: duplicate resource {key}")
                result[key] = item.findtext("value") or ""
            return result
        default, chinese = values(""), values(".zh-TW")
        for key, value in chinese.items():
            resource_checks += 1
            if key not in default:
                errors.append(f"{group}: default resource missing {key}")
                continue
            placeholders = lambda s: sorted(re.findall(r"\{(\d+)(?:[^{}]*)\}", s))
            if placeholders(value) != placeholders(default[key]):
                errors.append(f"{group}: format placeholders differ for {key}")
    print(json.dumps({"result": "FAIL" if errors else "PASS", "version": version,
                      "documents": len(documents), "local_links": local_links,
                      "resource_values": resource_checks, "errors": errors},
                     ensure_ascii=False, indent=2))
    return 1 if errors else 0

if __name__ == "__main__":
    raise SystemExit(main())
