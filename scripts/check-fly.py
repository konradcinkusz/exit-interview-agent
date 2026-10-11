#!/usr/bin/env python3
"""Invariants of the generated Fly topology (flyio/*.fly.toml), checked without a Fly account.

Runs in the CI job "workflow syntax, doc links, fly.toml". It proves what a file can prove: that the
declared build sources exist, that ports and health checks are declared, that the single-instance
rules of ADR-0080 hold for the interview service, that no secret value is written into a public file,
and that pgAdmin is not deployed. It does NOT prove that an app boots on Fly; that is NOT RUN here.

Usage: python3 scripts/check-fly.py [ROOT]     (ROOT defaults to the repository root)
Exit 0 when clean; exit 1 and one line per violation otherwise.
"""

from __future__ import annotations

import pathlib
import re
import sys
import tomllib

# Values that look like credentials. A value in a public file must never match one of these.
SECRET_VALUE_PATTERNS = [
    re.compile(r"-----BEGIN [A-Z ]*PRIVATE KEY-----"),
    re.compile(r"\bsk_(test|live)_[A-Za-z0-9]{8,}"),
    re.compile(r"\bwhsec_[A-Za-z0-9]{8,}"),
    re.compile(r"\bsk-ant-[A-Za-z0-9_-]{8,}"),
    re.compile(r"\bghp_[A-Za-z0-9]{20,}"),
    re.compile(r"\bAKIA[0-9A-Z]{16}\b"),
    re.compile(r"(?i)password\s*=\s*[^<\s;'\"$]{4,}"),
]

# Keys whose value is a secret by name. The value must be empty or a placeholder in a public file.
SECRET_KEY_NAME = re.compile(r"(?i)(secret|password|passwd|privatekey|api_?key|connectionstring|token)")


def load(path: pathlib.Path) -> dict:
    return tomllib.loads(path.read_text(encoding="utf-8"))


def expose_port(dockerfile: pathlib.Path) -> int | None:
    """The port a Dockerfile declares with EXPOSE (the last one wins, as in the image)."""
    found = None
    for line in dockerfile.read_text(encoding="utf-8").splitlines():
        m = re.match(r"\s*EXPOSE\s+(\d+)", line)
        if m:
            found = int(m.group(1))
    return found


def check_file(root: pathlib.Path, path: pathlib.Path) -> list[str]:
    problems: list[str] = []
    name = path.name
    rel = path.relative_to(root)
    text = path.read_text(encoding="utf-8")
    try:
        d = load(path)
    except tomllib.TOMLDecodeError as e:  # pragma: no cover - reported, not raised
        return [f"{rel}: does not parse as TOML: {e}"]

    if "app" not in d:
        problems.append(f"{rel}: no app name")

    # Build source: a pinned image, or an explicit dockerfile + context. Both paths resolve relative to THIS file.
    build = d.get("build", {})
    if "image" in build:
        if not re.fullmatch(r"[a-z0-9./_-]+:[A-Za-z0-9._-]+", build["image"]) or build["image"].endswith(":latest"):
            problems.append(f"{rel}: [build] image must be pinned to a tag, never :latest ({build['image']})")
    elif "dockerfile" in build and "context" in build:
        dockerfile = (path.parent / build["dockerfile"]).resolve()
        context = (path.parent / build["context"]).resolve()
        if not dockerfile.is_file():
            problems.append(f"{rel}: [build] dockerfile does not exist: {build['dockerfile']}")
        if not context.is_dir():
            problems.append(f"{rel}: [build] context does not exist: {build['context']}")
        if dockerfile.is_file() and context.is_dir() and not str(dockerfile).startswith(str(context)):
            problems.append(f"{rel}: [build] dockerfile is outside its context")
        # The declared port must be the one the image exposes.
        http = d.get("http_service")
        if http and dockerfile.is_file():
            port = expose_port(dockerfile)
            if port is None:
                problems.append(f"{rel}: {build['dockerfile']} has no EXPOSE")
            elif http.get("internal_port") != port:
                problems.append(f"{rel}: internal_port {http.get('internal_port')} != EXPOSE {port} in {build['dockerfile']}")
    else:
        problems.append(f"{rel}: [build] needs image (pinned), or both dockerfile and context")

    # Public configuration only: no credential value in [env].
    for key, value in d.get("env", {}).items():
        if SECRET_KEY_NAME.search(key) and isinstance(value, str) and value.strip():
            problems.append(f"{rel}: [env] {key} is secret by name and must not carry a value (use fly secrets)")

    # Credentials in any shape, anywhere in the file.
    for pattern in SECRET_VALUE_PATTERNS:
        if pattern.search(text):
            problems.append(f"{rel}: matches a credential pattern ({pattern.pattern[:40]}...)")

    http = d.get("http_service")
    if name == "postgres.fly.toml":
        if http is not None or "services" in d:
            problems.append(f"{rel}: a database must have no public listener ([http_service] or [[services]])")
        if "[[mounts]]" not in text and "mounts" not in d:
            problems.append(f"{rel}: a database needs a volume")
        return problems

    if http is not None:
        checks = http.get("checks", [])
        if not checks:
            problems.append(f"{rel}: [http_service] declares no http check")
        for c in checks:
            if "path" not in c:
                problems.append(f"{rel}: an http check has no path")
        if "min_machines_running" not in http:
            problems.append(f"{rel}: min_machines_running must be declared, not left to the default")

    if name.startswith("interview-service"):
        http = http or {}
        if http.get("min_machines_running") != 1:
            problems.append(f"{rel}: interview-service must keep min_machines_running = 1 (ADR-0080)")
        if http.get("auto_stop_machines") is not False:
            problems.append(f"{rel}: interview-service must set auto_stop_machines = false (ADR-0080)")
        if d.get("deploy", {}).get("strategy") != "immediate":
            problems.append(f"{rel}: interview-service must set [deploy] strategy = \"immediate\" (ADR-0080)")
        if "[[mounts]]" in text:
            problems.append(f"{rel}: interview-service must not carry a volume (sessions are in memory, ADR-0076)")

    # Web and the two services that are called in-request need a health path that is not "/".
    if name in ("web.fly.toml", "interview-service.fly.toml", "authservice.fly.toml") and http is not None:
        paths = [c.get("path", "") for c in http.get("checks", [])]
        if not any(p.startswith("/health") for p in paths):
            problems.append(f"{rel}: no /health* check (got {paths})")
        if name == "web.fly.toml" and "/healthz" not in paths:
            problems.append(f"{rel}: web must probe /healthz, not /")

    return problems


def check_root(root: pathlib.Path) -> list[str]:
    flyio = root / "flyio"
    files = sorted(flyio.glob("*.fly.toml"))
    problems: list[str] = []
    if not files:
        return [f"{flyio}: no *.fly.toml files"]
    for path in files:
        problems.extend(check_file(root, path))

    # pgAdmin is an orchestrator-only tool (AppHost), never a Fly app.
    for path in sorted(flyio.rglob("*")):
        if path.is_file() and re.search(r"(?i)pgadmin", path.read_text(encoding="utf-8", errors="ignore")):
            problems.append(f"{path.relative_to(root)}: mentions pgAdmin; it must not be deployed to Fly")

    return problems


def main(argv: list[str]) -> int:
    root = pathlib.Path(argv[1] if len(argv) > 1 else pathlib.Path(__file__).resolve().parent.parent).resolve()
    problems = check_root(root)
    for p in problems:
        print(p)
    if problems:
        print(f"check-fly: {len(problems)} violation(s)")
        return 1
    print(f"check-fly: {len(list((root / 'flyio').glob('*.fly.toml')))} files clean")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
