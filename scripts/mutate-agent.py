#!/usr/bin/env python3
"""
Real-code mutation pass for the evaluation gate (docs/eval/SPEC.md section 9, ADR-0041).

Weakens ONE protection of the Agent project at a time in the working tree, rebuilds, runs `exit-interview-eval gate`, records whether the gate
failed on an ASSERTION (a build failure or harness crash is not a catch), and restores the file. Nothing is committed. Run it on a clean tree:

    python3 scripts/mutate-agent.py            # all mutations, markdown on stdout
    python3 scripts/mutate-agent.py M-01 M-05  # a subset

A mutation that SURVIVES is a missing scenario and must be filed as one. Needs the .NET SDK; about one minute per mutation.
"""
import subprocess, sys, json, re, os
R=subprocess.run(["git","rev-parse","--show-toplevel"],capture_output=True,text=True,check=True).stdout.strip()
if subprocess.run(["git","status","--porcelain","--","src","tests","evals"],cwd=R,capture_output=True,text=True).stdout.strip():
    sys.exit("refusing to run on a dirty tree: the mutations restore files from memory, not from git")
M=[
 ("M-01","guard-leading-checks-off","src/ExitInterviewAgent.Agent/Roles/QuestionGuard.cs",
  "    public static string? LeadingReason(string question)\n    {\n","    public static string? LeadingReason(string question)\n    {\n        if (question.Length >= 0) return null;\n","QuestionGuard.LeadingReason always returns null (the leading/loaded/closed lint is switched off)"),
 ("M-02","pii-guard-masks-nothing","src/ExitInterviewAgent.Agent/Roles/Abstractions.cs",
  "return new PiiGuardResult(true, result.MaskedText, result.Findings);","return new PiiGuardResult(true, text, []);","PiiGuard.Mask returns the text unmasked with no findings"),
 ("M-03","quote-verification-off","src/ExitInterviewAgent.Agent/Roles/RecordAssembler.cs",
  "if (mismatches.Contains(i) || HasNoSubstance(q)","if (HasNoSubstance(q)","the quote step keeps quotes that are not verbatim excerpts of the transcript"),
 ("M-04","withdrawal-ignored","src/ExitInterviewAgent.Agent/Machine/ReplySignals.cs",
  "Withdrew(text) || (words ?? CountWords(text)) <= 2 && BareStop().IsMatch(text);","false && (words ?? 0) < 0;","ReplyAnalyzer.IsWithdrawal never recognises a withdrawal (mid-interview withdrawals are ignored)"),
 ("M-05","disclosure-event-removed","src/ExitInterviewAgent.Agent/Runner/InterviewRunner.cs",
  "                if (aiDisclosed) session.Event(Ev.DisclosureDelivered);\n","","the runner no longer records that the disclosure was delivered (aiDisclosed is still set)"),
 ("M-06","extractor-schema-check-off","src/ExitInterviewAgent.Agent/Roles/ExtractorOutput.cs",
  "if (!result.IsValid) { errorCodes = [ExtractorErrors.SchemaViolation]; return false; }","if (!result.IsValid && json.Length < 0) { errorCodes = [ExtractorErrors.SchemaViolation]; return false; }","extractor output is accepted even when it violates the extractor schema (extra fields)"),
 ("M-07","reply-text-in-a-log-line","src/ExitInterviewAgent.Agent/Runner/InterviewRunner.cs",
  '_options.Logger?.LogDebug("Turn {Index}: {Kind} -> {Next}", machine.InterviewerTurns, signals.Withdrawal ? "withdrawal" : "reply", step.Kind);','_options.Logger?.LogDebug("Turn {Index}: {Kind} -> {Next} {Text}", machine.InterviewerTurns, signals.Withdrawal ? "withdrawal" : "reply", step.Kind, masked);',"the runner logs the (masked) interviewee reply"),
 ("M-08","reply-text-in-a-span-tag","src/ExitInterviewAgent.Agent/Runner/InterviewRunner.cs",
  "turn.Set(Attr.ReplyChars, rawChars).Set(Attr.ReplyWords, s.Words);","turn.Set(Attr.ReplyChars, rawChars).Set(Attr.ReplyWords, s.Words); turn?.SetTag(\"interview.reply.text\", masked);","the runner writes the (masked) interviewee reply into a span tag"),
 ("M-09","budget-never-exhausted","src/ExitInterviewAgent.Agent/Runner/InterviewRunner.cs",
  "var budgetGone = _meter.Exhausted;","var budgetGone = _meter.Exhausted && _meter.Calls < 0;","the model/token budget is never enforced"),
 ("M-10","probe-limit-removed","src/ExitInterviewAgent.Agent/Machine/InterviewMachine.cs",
  "(s.Vague || s.Short) && ProbesUsed < limits.MaxProbesPerTopic &&","(s.Vague || s.Short) &&","vague answers are probed without the per-topic limit"),
 ("M-11","vague-answers-not-probed","src/ExitInterviewAgent.Agent/Machine/InterviewMachine.cs",
  "(s.Vague || s.Short) && ProbesUsed < limits.MaxProbesPerTopic &&","(s.Vague || s.Short) && ProbesUsed < 0 &&","the interviewer never asks for a concrete example (the degenerate way to avoid leading questions)"),
 ("M-12","names-not-redirected","src/ExitInterviewAgent.Agent/Roles/Abstractions.cs",
  "public bool NamesPerson => Findings.Any(f => f.Kind == PiiKind.PersonName);","public bool NamesPerson => false;","a masked name no longer triggers the redirect to behaviour and role"),
 ("M-13","serious-account-does-not-deepen","src/ExitInterviewAgent.Agent/Machine/InterviewMachine.cs",
  "if (s.Serious || _deepening)","if (_deepening)","a serious account never opens the deepening phase (the interviewer moves on as if it were a plain answer)"),
 ("M-14","deep-probe-limit-removed","src/ExitInterviewAgent.Agent/Machine/InterviewMachine.cs",
  "if (DeepProbesUsed < limits.MaxDeepProbesPerTopic && NextFocus(s.DeepCovered) is { } focus)","if (NextFocus(s.DeepCovered) is { } focus)","the deepening is no longer bounded by maxDeepProbesPerTopic (it runs the whole menu)"),
]
only=sys.argv[1:] 
out=[]
def sh(cmd,timeout=900):
    return subprocess.run(cmd,shell=True,cwd=R,capture_output=True,text=True,timeout=timeout)
for mid,name,f,old,new,desc in M:
    if only and mid not in only: continue
    path=f"{R}/{f}"
    src=open(path).read()
    if src.count(old)!=1:
        out.append((mid,name,desc,"PATCH-NOT-APPLIED",[])); continue
    open(path,"w").write(src.replace(old,new))
    try:
        b=sh("dotnet build src/ExitInterviewAgent.Eval -warnaserror 2>&1 | grep -E 'error|rror\\(s\\)' | sort -u | head -3")
        if "0 Error(s)" not in b.stdout:
            out.append((mid,name,desc,"DID-NOT-BUILD",[b.stdout.strip()[:200]]))
        else:
            g=sh("dotnet run --project src/ExitInterviewAgent.Eval --no-build -- gate 2>&1")
            lines=[l for l in g.stdout.splitlines() if l.startswith("FAILED") or l.startswith("gate:")]
            status="CAUGHT" if g.returncode==1 and any(l.startswith("FAILED") for l in lines) else ("SURVIVED" if g.returncode==0 else f"HARNESS-EXIT-{g.returncode}")
            out.append((mid,name,desc,status,lines))
    finally:
        open(path,"w").write(src)
print("| ID | Weakened protection | Result | First failing findings |")
print("|---|---|---|---|")
for mid,name,desc,status,lines in out:
    fails=[l.replace("FAILED ","").replace("|","/")[:150] for l in lines if l.startswith("FAILED")]
    print(f"| {mid} `{name}` | {desc} | **{status}** ({len(fails)} finding(s)) | " + ("<br>".join(fails[:3]) if fails else "-") + " |")
sys.exit(0 if all(o[3]=="CAUGHT" for o in out) else 1)
