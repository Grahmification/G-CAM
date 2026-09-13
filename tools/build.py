#!/usr/bin/env python3
"""Build G-CAM and run its tests, reporting what actually went wrong.

The raw `dotnet build` output for this project is misleading in two specific ways,
and this exists to stop both of them wasting time:

  * SOLIDWORKS holds the add-in DLLs open while it is running. The build then
    fails with eight MSB3021/MSB3027 copy errors that look alarming and mean
    only "close SOLIDWORKS". Compilation itself succeeded.
  * The post-build regasm step fails on every unelevated build, printing
    warnings containing the word "error". Grepping for "error" reports failures
    on a perfectly clean build.

Usage:
    python tools/build.py                 build, then test
    python tools/build.py --no-test       build only
    python tools/build.py --test-only     tests only (fast; skips the solution build)
    python tools/build.py --release       Release configuration
    python tools/build.py --verbose       also print the raw output

Exit code is non-zero for real failures: compile errors, build-rule violations or
failing tests. File locks alone are reported but not treated as failure, because
the code compiled - only the copy to the add-in folder was blocked.
"""
import argparse
import re
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SOLUTION = REPO / "G-CAM.sln"
CORE_TESTS = REPO / "tests" / "GCam.Core.Tests" / "GCam.Core.Tests.csproj"

# MSBuild prints each error once per parallel project pass, so everything is deduped.
LOCK_CODES = ("MSB3021", "MSB3027")
COMPILE = re.compile(r": error (CS\d+): (.*)")
RULE = re.compile(r"([^\\/]+\.csproj)\((\d+),\d+\): error : (.*)")
LOCKED_BY = re.compile(r'locked by: "([^"]+)"')
TEST_SUMMARY = re.compile(r"(Passed|Failed)!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+).*?Total:\s+(\d+)")
# xUnit prefixes these with a timestamp - "[xUnit.net 00:00:01.55]   Some.Test [FAIL]" -
# so the name is matched anywhere on the line rather than anchored to the start.
FAILED_TEST = re.compile(r"(\S+\.\S+)\s+\[FAIL\]")

GREEN, RED, YELLOW, DIM, RESET = "\033[32m", "\033[31m", "\033[33m", "\033[2m", "\033[0m"


def run(args):
    """Runs a command, returning combined output. Never raises on non-zero exit."""
    result = subprocess.run(
        args, cwd=str(REPO), stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
        text=True, errors="replace",
    )
    return result.returncode, result.stdout


def solidworks_processes():
    """PIDs of running SOLIDWORKS instances, which is why builds fail to copy."""
    code, out = run(["tasklist", "/FI", "IMAGENAME eq SLDWORKS.exe", "/FO", "CSV", "/NH"])
    if code != 0:
        return []

    return [line.split('","')[1] for line in out.splitlines() if line.startswith('"SLDWORKS.exe"')]


def classify(output):
    """Splits build output into the categories that mean different things."""
    compile_errors, rule_errors, locked_files, other_errors = [], [], set(), []
    lock_holder = None

    for line in output.splitlines():
        if any(code in line for code in LOCK_CODES):
            match = LOCKED_BY.search(line)
            if match:
                lock_holder = match.group(1)
            target = re.search(r'to "([^"]+)"', line) or re.search(r"file '([^']+)'", line)
            locked_files.add(Path(target.group(1)).name if target else "output DLL")
            continue

        # Expected on any unelevated build; the DLL is still produced.
        if "RA0000" in line or ("MSB3073" in line and "regasm" in line.lower()):
            continue

        match = COMPILE.search(line)
        if match:
            compile_errors.append(f"{match.group(1)}: {match.group(2)}".strip())
            continue

        match = RULE.search(line)
        if match:
            rule_errors.append(f"{match.group(1)} line {match.group(2)}: {match.group(3)}".strip())
            continue

        if ": error " in line:
            other_errors.append(line.strip())

    return {
        "compile": sorted(set(compile_errors)),
        "rule": sorted(set(rule_errors)),
        "locked": sorted(locked_files),
        "lock_holder": lock_holder,
        "other": sorted(set(other_errors)),
    }


def parse_tests(output):
    """Test totals and the names of anything that failed."""
    totals = None
    for match in TEST_SUMMARY.finditer(output):
        totals = {
            "failed": int(match.group(2)),
            "passed": int(match.group(3)),
            "total": int(match.group(4)),
        }

    failures = sorted({m.group(1) for m in (FAILED_TEST.search(l) for l in output.splitlines()) if m})
    return totals, failures


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--no-test", action="store_true", help="build without running tests")
    parser.add_argument("--test-only", action="store_true", help="run tests without building the solution")
    parser.add_argument("--release", action="store_true", help="Release configuration")
    parser.add_argument("--verbose", action="store_true", help="also print raw dotnet output")
    args = parser.parse_args()

    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

    configuration = "Release" if args.release else "Debug"
    problems = []
    notes = []

    running = solidworks_processes()

    if not args.test_only:
        print(f"{DIM}building {SOLUTION.name} ({configuration})…{RESET}")
        _, output = run(["dotnet", "build", str(SOLUTION), "-v", "q", "--nologo", "-c", configuration])

        if args.verbose:
            print(output)

        found = classify(output)

        for message in found["compile"]:
            problems.append(("compile error", message))
        for message in found["rule"]:
            # A build-rule failure is the Core-purity guard or similar; say so plainly.
            problems.append(("build rule", message))
        for message in found["other"]:
            problems.append(("error", message))

        if found["locked"]:
            holder = found["lock_holder"] or "another process"
            notes.append(
                f"{len(found['locked'])} output file(s) could not be copied - locked by {holder}. "
                "The code compiled; the add-in folder was not refreshed. Close SOLIDWORKS and rebuild "
                "before loading the add-in.")

    if not args.no_test:
        print(f"{DIM}running {CORE_TESTS.stem}…{RESET}")
        _, output = run(["dotnet", "test", str(CORE_TESTS), "--nologo", "-v", "q", "-c", configuration])

        if args.verbose:
            print(output)

        # A test run that will not build reports compile errors of its own.
        for message in classify(output)["compile"]:
            problems.append(("test compile error", message))

        totals, failures = parse_tests(output)

        if totals is None and not problems:
            problems.append(("tests", "no test results were produced - see --verbose"))
        elif totals:
            if totals["failed"]:
                # The count decides the verdict, never the name matching. A parser that
                # stopped recognising the failure lines once turned two failing tests
                # into a green OK and an exit code of 0.
                for name in failures:
                    problems.append(("test failed", name))
                if not failures:
                    problems.append((
                        "test failed",
                        f"{totals['failed']} test(s) failed, names not parsed - see --verbose"))
            notes.append(f"{totals['passed']}/{totals['total']} tests passed")

    print()
    if problems:
        print(f"{RED}FAILED{RESET}")
        for kind, message in problems[:25]:
            print(f"  {RED}{kind}{RESET}  {message}")
        if len(problems) > 25:
            print(f"  … {len(problems) - 25} more")
    else:
        print(f"{GREEN}OK{RESET}")

    for note in notes:
        colour = YELLOW if "locked" in note else DIM
        print(f"  {colour}{note}{RESET}")

    if running and not args.test_only:
        print(f"  {DIM}SOLIDWORKS running (pid {', '.join(running)}){RESET}")

    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
