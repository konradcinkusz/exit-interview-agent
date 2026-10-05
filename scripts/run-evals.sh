#!/usr/bin/env bash
# Run the evaluation harness offline (mock profile; no network, no credential). docs/eval/README.md explains each step.
#   scripts/run-evals.sh            validate, gate, report (report/), calibration (report/)
#   scripts/run-evals.sh validate   only check the scenarios against the schema and the spec
#   scripts/run-evals.sh gate       only the CI gate (constraints at 100%, behaviours against evals/baseline.json)
#   scripts/run-evals.sh report     only the conformance report (report/report.json, report/report.md)
#   scripts/run-evals.sh calibrate  only the calibration and classifier experiment (report/calibration.md)
# A real-model profile is run with:  dotnet run --project src/ExitInterviewAgent.Eval -- run --profile <name>
# and reports skipped:no-credential / skipped:no-provider unless it is configured (evals/profiles.yaml).
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
eval_tool() { dotnet run --project src/ExitInterviewAgent.Eval -c Release -- "$@"; }
step="${1:-all}"
case "$step" in
  validate | gate) eval_tool "$step" ;;
  calibrate) eval_tool calibrate --out report ;;
  report) eval_tool run --profile mock --out report ;;
  all)
    eval_tool validate
    eval_tool gate
    eval_tool run --profile mock --out report
    eval_tool calibrate --out report
    ;;
  *) echo "usage: $0 [validate|gate|report|calibrate|all]" >&2; exit 2 ;;
esac
