#!/usr/bin/env python3
"""Every relative link in the repository's Markdown resolves to a file (TEMPLATE §10 gate 7).

Checks README.md, the root *.md files, docs/**/*.md, flyio/*.md and scripts/*.md. External links,
mailto: and pure #anchors are skipped; a path#anchor is checked for the path only. Fenced code
blocks are ignored so example snippets do not count as links.
"""
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
LINK = re.compile(r"(?<!\!)\[[^\]]*\]\(([^)\s]+)(?:\s+\"[^\"]*\")?\)|!\[[^\]]*\]\(([^)\s]+)\)")
FENCE = re.compile(r"^\s*```")

files = [*ROOT.glob("*.md"), *ROOT.glob("docs/**/*.md"), *ROOT.glob("flyio/*.md"), *ROOT.glob("scripts/*.md"),
         *ROOT.glob(".github/*.md")]
broken = []
checked = 0
for md in sorted(set(files)):
    in_fence = False
    for lineno, line in enumerate(md.read_text(encoding="utf-8").splitlines(), 1):
        if FENCE.match(line):
            in_fence = not in_fence
            continue
        if in_fence:
            continue
        for m in LINK.finditer(line):
            target = (m.group(1) or m.group(2)).split("#")[0]
            if not target or re.match(r"^[a-z][a-z0-9+.-]*:", target):
                continue
            checked += 1
            if not (md.parent / target).resolve().exists():
                broken.append(f"{md.relative_to(ROOT)}:{lineno}: {target}")
print(f"checked {checked} relative links in {len(set(files))} files")
if broken:
    print("BROKEN:\n  " + "\n  ".join(broken))
    sys.exit(1)
