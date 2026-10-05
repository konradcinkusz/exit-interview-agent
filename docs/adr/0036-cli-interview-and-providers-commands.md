# 0036. CLI: the interactive `interview` command, `providers`, and what is written where

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): brief §5 (mode B: transcript stays on the machine), brief §6 (consent withdrawal discards; no PII or content in logs and traces), [ADR-0026](0026-cli-project-and-ci-artifacts.md) (the CLI project), [ADR-0033](0033-provider-configuration-credentials-and-disclosure.md), [ADR-0022](0022-interview-agent-core.md) (the interviewee seam)

## Context

[ADR-0026](0026-cli-project-and-ci-artifacts.md) shipped `demo` and `personas` only. T6 adds a real interview in the terminal, a way to see what is configured, and a way to test a provider.

## Decision

- **`exit-interview interview --provider <p> --model <m> [--base-url ...] [--out <dir>] [--employer <ref>]`** and the flags listed in `--help`. It runs the whole T4 interview with a `ConsoleInterviewee : IInterviewee`. Provider `mock` runs the scripted model (offline; for scripts and tests; the notice says it is a test seam).
- **Stopping is consent withdrawal.** End of input (Ctrl-D, a closed pipe) and Ctrl-C both make the interviewee "leave" (`ReplyAsync` returns `null`), which the runner treats as abandonment: transcript discarded, no record, nothing written. Ctrl-C during a model call cancels the call and ends the same way (exit 130). Ctrl-C is intercepted (`CancelKeyPress`), so the process is not killed mid-write.
- **What is written, and where.** The transcript stays in memory. It is written only with `--save-transcript` and an explicit `--out <dir>` (`transcript.txt`, owner-only permissions on Unix). The record JSON is written to `--out/record.json` only if one was produced **and validated**; the record is also printed, with the validation result, so the person sees exactly what exists. Nothing is written when the interview ends without a record. `--out` is not created if there is nothing to put in it. No submission to any server (T11).
- **Context.** `--tenure` (required by the record; asked for on the terminal if missing, because no value is a truthful default), `--seniority`, `--function` (wire names of the record bands), `--employer` (an opaque reference; default `unspecified-employer`).
- **Exit codes:** 0 completed; 2 usage or configuration (including refusals); 3 ended without a record by the interviewee's choice (withdrawal, refusal, end of input); 4 agent failure (PII guard failed, extraction invalid); 5 provider failure (the message names a controlled code and a hint, never provider text); 6 disclosure not confirmed; 130 cancelled.
- **`exit-interview providers`** lists providers, key-variable status (never values), the refusals, the config file, remembered confirmations, the variables read and the effective selection, with no network call. **`providers ping`** makes exactly one request ("Reply with the single word OK.", at most 8 output tokens, no interview content) and only when run; it prints status, latency and tokens. **`providers forget-confirmations`** deletes the preference file.
- The summary prints usage (calls, tokens in/out, model latency, and cost only when the user supplied prices).
- Telemetry export follows [ADR-0035](0035-provider-telemetry-and-export.md); `Program` wires `CancelKeyPress`; `CliHost` carries input, output, environment, runtime and cancellation, so tests drive the CLI with scripted stdin and a fake transport.

## Consequences

- A person can run a real interview today with their own key or a local model. **No real interview is supported by this project**: the README and the notice say what the provider sees, and the legal considerations still apply ([legal §4](../legal/CONSIDERATIONS.md#4-why-real-interviews-are-out-of-scope)).
- A transcript lost to a failed extraction is gone unless `--save-transcript` was given; the message says so. This is the privacy-preserving default, and an inconvenience.
- Not covered: multi-line answers (one line per answer), resuming an interview, a non-English interview (protocol language is `en`), readline-style editing beyond the terminal's own.
