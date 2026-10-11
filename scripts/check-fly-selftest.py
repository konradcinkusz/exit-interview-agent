#!/usr/bin/env python3
"""Negative tests for scripts/check-fly.py: each mutation of a copy of flyio/ must be reported.

A checker that cannot fail is worse than none, so every rule in check-fly.py has a mutation here, and the
unmutated copy must come back clean. Run in CI right after check-fly.py.

Usage: python3 scripts/check-fly-selftest.py
"""

from __future__ import annotations

import importlib.util
import pathlib
import shutil
import sys
import tempfile

REPO = pathlib.Path(__file__).resolve().parent.parent
_spec = importlib.util.spec_from_file_location("check_fly", REPO / "scripts" / "check-fly.py")
check_fly = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(check_fly)

INTERVIEW = "flyio/interview-service.fly.toml"
WEB = "flyio/web.fly.toml"
AUTH = "flyio/authservice.fly.toml"
POSTGRES = "flyio/postgres.fly.toml"

# (name, file, old text, new text, expected substring in one violation line)
MUTATIONS = [
    ("interview auto-stop on", INTERVIEW, "\n  auto_stop_machines = false", '\n  auto_stop_machines = "stop"', "auto_stop_machines = false"),
    ("interview scaled to zero", INTERVIEW, "\n  min_machines_running = 1", "\n  min_machines_running = 0", "min_machines_running = 1"),
    ("interview rolling deploy", INTERVIEW, '\n  strategy = "immediate"', '\n  strategy = "rolling"', 'strategy = \"immediate\"'),
    ("interview with a volume", INTERVIEW, "[[vm]]", '[[mounts]]\n  source = "x"\n  destination = "/data"\n\n[[vm]]', "must not carry a volume"),
    ("web probes the index page", WEB, 'path = "/healthz"', 'path = "/"', "must probe /healthz"),
    ("port differs from EXPOSE", WEB, "internal_port = 3000", "internal_port = 3001", "EXPOSE 3000"),
    # The fake keys are joined at run time, never written whole into this file: a key-shaped literal in history
    # trips the secret scanner (gitleaks), and the scanner also reads the history of a branch.
    ("credential value in [env]", WEB, 'PORT = "3000"', 'PORT = "3000"\n  ANTHROPIC_API_KEY = "' + "sk-" + "ant-abcdefgh1234" + '"', "must not carry a value"),
    ("live Stripe key in a file", WEB, 'PORT = "3000"', 'PORT = "3000"\n  NOTE = "' + "sk_" + "live_abcdefgh1234" + '"', "credential pattern"),
    ("password literal in a connection string", POSTGRES, "POSTGRES_USER = \"postgres\"", 'POSTGRES_USER = "postgres"\n  X = "Password=hunter2222"', "credential pattern"),
    ("image on :latest", AUTH, "authservice:v0.3.4", "authservice:latest", ":latest"),
    ("database with a public listener", POSTGRES, '[[vm]]', '[http_service]\n  internal_port = 5432\n\n[[vm]]', "public listener"),
    ("pgAdmin deployed", "flyio/SECRETS.md", "# Secrets:", "# pgAdmin app: exit-interview-agent-pgadmin\n# Secrets:", "pgAdmin"),
    ("missing dockerfile", WEB, 'dockerfile = "../web/app/Dockerfile"', 'dockerfile = "../web/app/Missing.Dockerfile"', "dockerfile does not exist"),
    ("no http check", AUTH, 'path = "/health/ready"', 'path = "/"', "/health* check"),
]


def build_copy(dst: pathlib.Path) -> None:
    shutil.copytree(REPO / "flyio", dst / "flyio")
    for rel in ("src/ExitInterviewAgent.InterviewService/Dockerfile", "web/app/Dockerfile"):
        (dst / rel).parent.mkdir(parents=True, exist_ok=True)
        shutil.copy(REPO / rel, dst / rel)
    (dst / "src/ExitInterviewAgent.InterviewService").mkdir(parents=True, exist_ok=True)


def run(name: str, mutate) -> list[str]:
    with tempfile.TemporaryDirectory() as tmp:
        root = pathlib.Path(tmp)
        build_copy(root)
        mutate(root)
        return check_fly.check_root(root)


def main() -> int:
    failures = 0

    clean = run("unmutated", lambda root: None)
    if clean:
        failures += 1
        print("FAIL unmutated copy reported violations:\n  " + "\n  ".join(clean))
    else:
        print("ok   unmutated copy is clean")

    for name, rel, old, new, expected in MUTATIONS:
        def mutate(root, rel=rel, old=old, new=new):
            path = root / rel
            if rel == "flyio/SECRETS.md" and not path.exists():
                path.write_text("# Secrets:\n", encoding="utf-8")
            text = path.read_text(encoding="utf-8")
            if text.count(old) != 1:
                # A mutation must hit exactly one live line. Matching a comment, or nothing, proves nothing.
                raise SystemExit(f"selftest: '{old!r}' must occur exactly once in {rel}, found {text.count(old)}")
            path.write_text(text.replace(old, new, 1), encoding="utf-8")

        problems = run(name, mutate)
        if any(expected in p for p in problems):
            print(f"ok   {name}")
        else:
            failures += 1
            print(f"FAIL {name}: expected a violation containing {expected!r}; got {problems or 'nothing'}")

    # The pgAdmin mutation writes into SECRETS.md; prove the rule also catches a new flyio/ file.
    def new_file(root):
        (root / "flyio/pgadmin.fly.toml").write_text('app = "x-pgadmin"\n', encoding="utf-8")
    problems = run("pgAdmin as its own file", new_file)
    if any("pgAdmin" in p for p in problems):
        print("ok   pgAdmin as its own file")
    else:
        failures += 1
        print(f"FAIL pgAdmin as its own file: got {problems or 'nothing'}")

    print(f"check-fly-selftest: {failures} failure(s)")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
