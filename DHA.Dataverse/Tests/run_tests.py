"""Runs the DHA dev integration tests plus the .NET unit tests and writes a per-card summary to last-run.md.

    python -I run_tests.py            # everything
    python -I run_tests.py dha09      # only files matching test_dha09*
Dev only (dvtest refuses anything else). Requires `az login` as stsadmin, node, and the .NET SDK.
"""
import os
import re
import subprocess
import sys
import time
import unittest
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))

CARDS = {
    "dha07": "DHA-7 Finance can place a project on stop",
    "dha08": "DHA-8 Warning when recording time on a stopped project",
    "dha09": "DHA-9 Approved absence creates the time entry",
    "dha10": "DHA-10 Cancelling an absence deactivates the time entry",
    "dha16": "DHA-16 Internal source is mandatory on a quote",
    "unit": ".NET unit tests (leave day calculation, leave events)",
}


class Recorder(unittest.TextTestResult):
    def __init__(self, *a, **k):
        super().__init__(*a, **k)
        self.outcomes = []  # (test, outcome, detail)

    def addSuccess(self, test):
        super().addSuccess(test); self.outcomes.append((test, "pass", ""))

    def addFailure(self, test, err):
        super().addFailure(test, err); self.outcomes.append((test, "FAIL", self._exc_info_to_string(err, test)))

    def addError(self, test, err):
        super().addError(test, err); self.outcomes.append((test, "ERROR", self._exc_info_to_string(err, test)))

    def addSkip(self, test, reason):
        super().addSkip(test, reason); self.outcomes.append((test, "skip", reason))

    def addExpectedFailure(self, test, err):
        super().addExpectedFailure(test, err); self.outcomes.append((test, "known gap", (test._testMethodDoc or "").strip()))

    def addUnexpectedSuccess(self, test):
        super().addUnexpectedSuccess(test); self.outcomes.append((test, "gap fixed?", "expected failure passed"))

    def addSubTest(self, test, subtest, err):
        super().addSubTest(test, subtest, err)
        if err is not None:
            self.outcomes.append((subtest, "FAIL", self._exc_info_to_string(err, test)))


def card_of(test_id):
    m = re.search(r"test_(dha\d\d)", test_id)
    return m.group(1) if m else "other"


def run_dotnet():
    proj = os.path.join(REPO, "DHA.Leave.Plugins.Tests")
    out = subprocess.run(["dotnet", "test", proj, "--logger", "console;verbosity=normal"], capture_output=True, text=True)
    passed = re.search(r"Passed:\s+(\d+)", out.stdout)
    failed = re.search(r"Failed:\s+(\d+)", out.stdout)
    if not passed:
        return [("dotnet test", "ERROR", out.stdout[-2000:] + out.stderr[-1000:])]
    passed, failed = int(passed.group(1)), int(failed.group(1)) if failed else 0
    rows = [(f"{passed} unit tests", "pass", "")]
    if failed:
        rows.append((f"{failed} unit tests", "FAIL", "\n".join(l for l in out.stdout.splitlines() if "Failed " in l)))
    return rows


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    pattern = f"test_{sys.argv[1]}*.py" if len(sys.argv) > 1 else "test_*.py"
    suite = unittest.defaultTestLoader.discover(HERE, pattern=pattern)
    started = time.time()
    runner = unittest.TextTestRunner(resultclass=Recorder, verbosity=2)
    result = runner.run(suite)

    by_card = defaultdict(list)
    for test, outcome, detail in result.outcomes:
        tid = test.id()
        by_card[card_of(tid)].append((tid.split(".", 1)[-1], outcome, detail))
    if len(sys.argv) == 1:
        by_card["unit"] = run_dotnet()

    lines = [f"# DHA test run - {time.strftime('%Y-%m-%d %H:%M')} (dev, {int(time.time() - started)}s)", ""]
    lines += ["| Card | Pass | Fail | Known gap | Skipped |", "|---|---|---|---|---|"]
    for card, title in CARDS.items():
        rs = by_card.get(card, [])
        if not rs:
            continue
        c = lambda o: sum(1 for _, x, _ in rs if x == o)
        lines.append(f"| {title} | {c('pass')} | {c('FAIL') + c('ERROR')} | {c('known gap')} | {c('skip')} |")
    lines.append("")
    for card, title in CARDS.items():
        rs = by_card.get(card, [])
        if not rs:
            continue
        lines += [f"## {title}", ""]
        for name, outcome, detail in rs:
            mark = {"pass": "✅", "FAIL": "❌", "ERROR": "❌", "known gap": "⚠️", "skip": "⏭️", "gap fixed?": "❓"}.get(outcome, "")
            lines.append(f"- {mark} `{name}` - {outcome}")
            if outcome != "pass" and detail:
                short = detail.strip().splitlines()
                short = [l for l in short if l.strip()][-3:] if outcome in ("FAIL", "ERROR") else short[:3]
                lines += [f"  > {l.strip()}" for l in short]
        lines.append("")
    report = os.path.join(HERE, "last-run.md")
    open(report, "w", encoding="utf-8").write("\n".join(lines))
    print("\n".join(lines[:12]))
    print(f"\nFull report: {report}")


if __name__ == "__main__":
    main()
