"""Reproduce the unmodified WinUAE 030 A7 recovery disagreement.

Windows/MSVC, Python 3, PowerShell 7 and the pinned WinUAE checkout are required.
The default audit fails architectural disagreement. --discovery-only records
software observations without promoting them. --validate-only checks identities
and executes the frozen observer again. No Copper68k build or source edit occurs.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import uuid

REPO = Path(__file__).resolve().parent.parent
FIXTURE = REPO / "scripts/reference/m68030-a7-recovery.cpp"
BASE_HELPER = REPO / "scripts/test-copper68k-030-return-trace-reference.ps1"
PIN = "5d22d33632646efc3f747f03e82d28353e52722e"
VCVARS = Path("C:/Program Files/Microsoft Visual Studio/18/Community/VC/Auxiliary/Build/vcvars64.bat")
FUNCTIONS = [
    ("cpuemu_32.cpp", "uae_u32 REGPARAM2 op_2018_32_ff(uae_u32 opcode)"),
    ("cpummu30.cpp", "static uae_u8 mmu030fixupreg(int i)"),
    ("cpummu30.cpp", "static void mmu030fixupmod(uae_u8 data, int dir, int idx)"),
    ("newcpu_common.cpp", "void cpu_restore_fixup(void)"),
]
OBSERVED_A7 = [0x4208, 0x4208, 0x8000, 0x9004, 0x4208, 0x4208, 0x8004, 0x9000,
               0x7004, 0x7004, 0x4204, 0x9004, 0x7004, 0x7004, 0x8004, 0x4204] + [0x4204] * 4
ROW = re.compile(r"fault=(\d) initial=(\d) returned=(\d) attempts=(\d+) reads=(\d+) "
                 r"D0=([0-9a-f]+) A7=([0-9a-f]+) USP=([0-9a-f]+) ISP=([0-9a-f]+) "
                 r"MSP=([0-9a-f]+) PC=([0-9a-f]+) events=(\d+)")


def check(ok, message):
    if not ok:
        raise ValueError(message)


def sha(path):
    check(path.is_file(), "Missing input/output: " + str(path))
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def save(path, value):
    path.write_text(json.dumps(value, indent=2), encoding="utf-8")


def run(args, cwd, log):
    with log.open("w", encoding="utf-8") as stream:
        result = subprocess.run(args, cwd=cwd, stdout=stream, stderr=subprocess.STDOUT)
    check(result.returncode == 0, "Execution failed; inspect " + str(log))


def function(reference, file, signature):
    text = (reference / file).read_text(encoding="utf-8-sig")
    matches = list(re.finditer(re.escape(signature) + r"\s*\{", text))
    check(len(matches) == 1, "Missing/nonunique native function: " + signature)
    start = matches[0].start()
    end = text.index("{", start) + 1
    depth = 1
    while depth and end < len(text):
        if text[end] == "{":
            depth += 1
        elif text[end] == "}":
            depth -= 1
        end += 1
    check(depth == 0, "Incomplete native function: " + signature)
    return text[start:end]


def expected_fragments(reference, baseline):
    fragments = {p.name: p.read_text(encoding="utf-8-sig") for p in baseline.glob("*.inc")}
    check(set(fragments) == {"access.inc", "constants.inc", "fault.inc", "frame.inc",
                            "loop.inc", "ops.inc", "rte.inc", "special-trace.inc", "sr.inc"},
          "Incomplete baseline native fragments")
    fragments["ops.inc"] += "\n" + function(reference, *FUNCTIONS[0]) + "\n"
    fragments["fixup.inc"] = "#define MMU030_REG_FIXUP 1\n" + "\n\n".join(
        function(reference, *entry) for entry in FUNCTIONS[1:]) + "\n"
    return fragments


def observations(root):
    check((root / "errors.log").read_text().strip() == "", "Native observer errors")
    rows = []
    for line in (root / "run.log").read_text().splitlines():
        match = ROW.fullmatch(line)
        check(match is not None, "Invalid native observation")
        v = match.groups()
        row = dict(fault=int(v[0]), initial=int(v[1]), returned=int(v[2]), attempts=int(v[3]),
                   reads=int(v[4]), d0=int(v[5], 16), a7=int(v[6], 16), rawUsp=int(v[7], 16),
                   rawIsp=int(v[8], 16), rawMsp=int(v[9], 16), pc=int(v[10], 16), events=v[11])
        check(row["attempts"] == (2 if row["fault"] else 1) and row["reads"] == 1 and
              row["d0"] == 0x80818283 and row["pc"] == 0x1006 and
              row["events"] == ("123546" if row["fault"] else "46"),
              "Wrong operand provenance, completed result or recovery sequence")
        rows.append(row)
    check([(x["fault"], x["initial"], x["returned"]) for x in rows] ==
          [(1, i, j) for i in range(4) for j in range(4)] + [(0, i, i) for i in range(4)],
          "Wrong or empty native selection")
    check([x["a7"] for x in rows] == OBSERVED_A7, "Changed frozen software observations")
    # This is only the unchanged-bank architectural control, never a guess about
    # a handler deliberately changing S/M. Raw inactive fields are not aliases.
    mismatches = [x for x in rows if x["initial"] == x["returned"] and x["a7"] != 0x4204]
    check(len(mismatches) == 2 and [x["initial"] for x in mismatches] == [0, 1],
          "Changed same-bank disagreement classification")
    trace = (root / "trace.log").read_text()
    check("0 after-native-log S=1 M=0 A7=7fa0" in trace and
          "0 after-RTE S=0 M=0 A7=4204 USP=4204 ISP=7ffc" in trace and
          "0 after-MOVE S=0 M=0 A7=4208" in trace, "Missing causal snapshots")
    return rows, mismatches


def audit(output, reference, validate_only):
    check(os.name == "nt", "Native audit requires Windows/MSVC")
    check(FIXTURE.is_file() and BASE_HELPER.is_file(), "Missing maintained reference fixture/helper")
    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=reference, text=True).strip()
    check(revision == PIN and not subprocess.check_output(
        ["git", "status", "--porcelain"], cwd=reference, text=True).strip(), "Wrong or changed pinned reference")
    baseline = output / "baseline"
    native = output / "native"
    if not validate_only:
        check(not output.exists(), "Use fresh audit output")
        check(VCVARS.is_file(), "Missing MSVC environment fixture")
        output.mkdir(parents=True)
    args = ["pwsh", "-NoProfile", "-File", str(BASE_HELPER), "-ReferenceDirectory", str(reference),
            "-OutputDirectory", str(baseline)]
    if validate_only:
        args.append("-ValidateReportsOnly")
    # Reuse the maintained exact-function extraction, pinned source checks and
    # six canonical reference controls instead of relying on temporary inputs.
    run(args, REPO, output / ("baseline-replay.log" if validate_only else "baseline.log"))
    fragments = expected_fragments(reference, baseline)
    reference_files = sorted({x["file"] for x in load(baseline / "identities.json")["referenceInputs"]})
    reference_inputs = {name: sha(reference / name) for name in reference_files}
    if not validate_only:
        native.mkdir()
        for name, text in fragments.items():
            (native / name).write_text(text, encoding="utf-8", newline="\n")
        shutil.copyfile(FIXTURE, native / "observer.cpp")
        command = ('@echo off\r\ncall "' + str(VCVARS) + '" >nul\r\n'
                   'if errorlevel 1 exit /b %errorlevel%\r\n'
                   'cl /nologo /O2 /EHsc /W4 observer.cpp /Fe:observer.exe\r\nexit /b %errorlevel%\r\n')
        (native / "build.cmd").write_text(command, encoding="utf-8")
        run([os.environ.get("COMSPEC", "cmd.exe"), "/d", "/c", "build.cmd"], native, native / "build.log")
        with (native / "run.log").open("w", encoding="utf-8") as stdout, (native / "errors.log").open("w", encoding="utf-8") as stderr:
            result = subprocess.run([str(native / "observer.exe")], cwd=native, stdout=stdout, stderr=stderr)
        check(result.returncode == 0, "Native observer failed; inspect errors.log")
        files = [*sorted(fragments), "observer.cpp", "observer.exe", "build.cmd", "build.log",
                 "run.log", "errors.log", "trace.log"]
        identity = dict(schema=1, producerSha256=sha(Path(__file__)), fixtureSha256=sha(FIXTURE),
                        referenceCommit=PIN, referenceInputs=reference_inputs,
                        outputs={name: sha(native / name) for name in files},
                        enabledTranslation=False, hardwareQualified=False, architecturalQualified=False,
                        productionImport=False, roadmapComplete=False)
        save(output / "identities.json", identity)
    identity = load(output / "identities.json")
    check(identity["schema"] == 1 and identity["producerSha256"] == sha(Path(__file__)) and
          identity["fixtureSha256"] == sha(FIXTURE) and identity["referenceCommit"] == PIN and
          identity["referenceInputs"] == reference_inputs and all(identity.get(k) is False for k in
          ["enabledTranslation", "hardwareQualified", "architecturalQualified", "productionImport", "roadmapComplete"]),
          "Wrong A7 scope/producer/input identity")
    files = {*fragments, "observer.cpp", "observer.exe", "build.cmd", "build.log", "run.log", "errors.log", "trace.log"}
    check(set(identity["outputs"]) == files, "Missing native output identity")
    for name, digest in identity["outputs"].items():
        check(sha(native / name) == digest, "Changed native output: " + name)
    check(sha(native / "observer.cpp") == sha(FIXTURE), "Changed observer fixture")
    for name, text in fragments.items():
        check((native / name).read_text(encoding="utf-8-sig") == text, "Changed extracted native function: " + name)
    if validate_only:
        replay = output / ("replay-" + uuid.uuid4().hex)
        replay.mkdir()
        with (replay / "run.log").open("w", encoding="utf-8") as stdout, (replay / "errors.log").open("w", encoding="utf-8") as stderr:
            result = subprocess.run([str(native / "observer.exe")], cwd=replay, stdout=stdout, stderr=stderr)
        check(result.returncode == 0 and (replay / "errors.log").stat().st_size == 0 and
              all(sha(replay / name) == sha(native / name) for name in ["run.log", "trace.log"]),
              "Executed A7 replay differs")
    rows, mismatches = observations(native)
    verification = dict(schema=1, nativeFaultCases=16, directControls=4, observations=rows,
                        sameBankDisagreements=mismatches, changedBankArchitecturalUntested=12,
                        identitySha256=sha(output / "identities.json"), architecturalQualified=False,
                        productionImport=False, publication=False, roadmapComplete=False)
    if validate_only:
        check(load(output / "verification.json") == verification, "Changed verification record")
    else:
        save(output / "verification.json", verification)
    return mismatches


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--reference-directory", type=Path, default=REPO / "artifacts/reference-winuae-rte-modern")
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--discovery-only", action="store_true", help="record unqualified observations; do not require architectural agreement")
    args = parser.parse_args()
    mismatches = audit(args.output.resolve(), args.reference_directory.resolve(), args.validate_only)
    print("16 native recoveries / four direct controls; two same-bank disagreements; twelve changed-bank cases architecturally untested.")
    if mismatches and not args.discovery_only:
        raise ValueError("Architectural A7 gate failed: unchanged-bank user recovery advances A7 by eight bytes")
    print("Discovery identities and execution verified; no architectural promotion.")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, KeyError, subprocess.SubprocessError) as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
