"""Pinned native 030 byte/word/long MOVE source-read and final-write faults.

Requires Windows/MSVC, Python 3, PowerShell 7 and a pristine pinned WinUAE
checkout. All native extraction/build/evidence stays in a fresh --output.
--validate-only rechecks identities and reexecutes the frozen native observer.
Translation, changed S/M, trace, timing and A7 postincrement source-read faults are excluded.
"""
import argparse
import importlib.util
import itertools
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import uuid

REPO = Path(__file__).resolve().parent.parent
FIXTURE = REPO / "scripts/reference/m68030-move-transfer-faults.cpp"
BASE = REPO / "scripts/test-copper68k-030-return-trace-reference.ps1"
EXTRACTOR = REPO / "scripts/test-copper68k-030-a7-recovery-reference.py"
PIN = "5d22d33632646efc3f747f03e82d28353e52722e"
VCVARS = Path("C:/Program Files/Microsoft Visual Studio/18/Community/VC/Auxiliary/Build/vcvars64.bat")
spec = importlib.util.spec_from_file_location("native_extraction", EXTRACTOR)
extractor = importlib.util.module_from_spec(spec)
spec.loader.exec_module(extractor)
sha, load, save, check, run = extractor.sha, extractor.load, extractor.save, extractor.check, extractor.run
ROW = re.compile(r"id=(\d+) width=(\d) bank=(\d) source=(\d) post=(\d) alias=(\d) origin=(\d) vi=(\d) ccr=(\d+) attempts=(\d+) reads=(\d+) wattempts=(\d+) writes=(\d+) value=([0-9a-f]+) address=([0-9a-f]+) sourceA=([0-9a-f]+) A7=([0-9a-f]+) PC=([0-9a-f]+) framePC=([0-9a-f]+) frameSR=([0-9a-f]+) frameSSW=([0-9a-f]+) faultAddress=([0-9a-f]+) output=([0-9a-f]+) moveccr=(\d+) frame=(\d+) events=(\d+)")


def fragments(reference, baseline):
    result = extractor.expected_fragments(reference, baseline)
    stride = re.findall(r"const int areg_byteinc\[\]\s*=\s*\{[^}]+\};", (reference / "newcpu.cpp").read_text(encoding="utf-8-sig"))
    check(len(stride) == 1 and [int(n) for n in re.findall(r"\d+", stride[0])] == [1,1,1,1,1,1,1,2], "Missing or wrong pinned byte strides")
    result["stride.inc"] = stride[0] + "\n"
    for opcode in ["1090", "1098", "2090", "2098", "3090", "3098"]:
        result["ops.inc"] += "\n" + extractor.function(reference, "cpuemu_32.cpp", f"uae_u32 REGPARAM2 op_{opcode}_32_ff(uae_u32 opcode)") + "\n"
    for name in ["put_byte", "put_long", "get_byte"]:
        signature = f"STATIC_INLINE {'void' if name.startswith('put') else 'uae_u32'} {name}_mmu030_state (uaecptr addr{', uae_u32 v' if name.startswith('put') else ''})"
        result["access.inc"] += "\n" + extractor.function(reference, "include/cpummu030.h", signature) + "\n"
    return result


def observations(native):
    check((native / "errors.log").read_text().strip() == "", "Native observer errors")
    lines = (native / "run.log").read_text().splitlines()
    cases = [c for c in itertools.product([1, 2, 4], range(4), [0, 7], range(2), range(2), range(3), range(4), range(32)) if not(c[2] == 7 and c[3] and c[5] == 1)]
    check(len(lines) == len(cases) == 33792, "Empty or incomplete native selection")
    for id, (line, (width, bank, source, post, alias, origin, vi, ccr)) in enumerate(zip(lines, cases)):
        match = ROW.fullmatch(line)
        check(match is not None, "Invalid native observation")
        v = match.groups()
        actual = [int(x) for x in v[:13]] + [int(x, 16) for x in v[13:23]] + [int(v[23]), int(v[24]), v[25]]
        mask = (1 << (8 * width)) - 1
        value = [0, 0x80818283 & mask, mask >> 1, mask][vi]
        stride = 2 if width == 1 and source == 7 else width
        destination = 0x4200 + post * stride if alias else 0x4400
        moveccr = (ccr & 16) | (4 if value == 0 else 0) | (8 if value & (1 << (8 * width - 1)) else 0)
        frame_sr = 0x700 | (0x2000 if bank >= 2 else 0) | (0x1000 if bank in (1,3) else 0) | (moveccr if origin == 2 else ccr)
        frame_ssw = 0x100 | (0x40 if origin == 1 else 0) | (0x10 if width == 1 else 0x20 if width == 2 else 0) | (5 if bank >= 2 else 1)
        expected = [id, width, bank, source, post, alias, origin, vi, ccr, 2 if origin == 1 else 1, 1, 2 if origin == 2 else 1, 1,
                    value, destination, 0x4200 + post * stride, 0x4200 + post * stride if source == 7 else [0x7000, 0x7000, 0x8000, 0x9000][bank], 0x1006,
                    0x1002 if origin == 2 else 0x1000 if origin == 1 else 0,
                    frame_sr if origin else 0, frame_ssw if origin else 0, destination if origin == 2 else 0x4200 if origin == 1 else 0, value if origin == 2 else 0,
                    moveccr, 32 if origin == 2 else 92 if origin == 1 else 0,
                    "4923586" if origin == 2 else "1235486" if origin == 1 else "486"]
        check(actual == expected, "Wrong transfer fault recovery: id=" + str(id) + " actual=" + str(actual) + " expected=" + str(expected))
    return dict(nativeCases=33792, sourceReadRecoveries=9216, finalWriteRecoveries=12288, directControls=12288,
                untestedA7PostReadFaultCases=3072, sourceForms=["indirect", "postincrement"], widths=[1,2,4], sourceRegisters=[0,7],
                softwareReferenceExecuted=True, hardwareQualified=False, architecturalPromotion=False,
                enabledTranslation=False, traceQualified=False, physicalTimingQualified=False,
                productionImport=False, publication=False, roadmapComplete=False)


def audit(output, reference, validate_only):
    check(os.name == "nt", "Windows/MSVC required")
    check(subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=reference, text=True).strip() == PIN and
          not subprocess.check_output(["git", "status", "--porcelain"], cwd=reference, text=True).strip(), "Wrong or changed pinned reference")
    if not validate_only:
        check(not output.exists(), "Use fresh audit output")
        check(VCVARS.is_file(), "Missing MSVC fixture")
        output.mkdir(parents=True)
    baseline, native = output / "baseline", output / "native"
    args = ["pwsh", "-NoProfile", "-File", str(BASE), "-ReferenceDirectory", str(reference), "-OutputDirectory", str(baseline)]
    if validate_only:
        args.append("-ValidateReportsOnly")
    run(args, REPO, output / ("baseline-replay.log" if validate_only else "baseline.log"))
    expected = fragments(reference, baseline)
    reference_files = sorted({x["file"] for x in load(baseline / "identities.json")["referenceInputs"]})
    producers = {p.relative_to(REPO).as_posix(): sha(p) for p in [Path(__file__), FIXTURE, BASE, EXTRACTOR]}
    inputs = {name: sha(reference / name) for name in reference_files}
    files = {*expected, "observer.cpp", "observer.exe", "build.cmd", "build.log", "run.log", "errors.log", "trace.log"}
    if not validate_only:
        native.mkdir()
        for name, text in expected.items():
            (native / name).write_text(text, encoding="utf-8", newline="\n")
        shutil.copyfile(FIXTURE, native / "observer.cpp")
        command = '@echo off\r\ncall "' + str(VCVARS) + '" >nul\r\nif errorlevel 1 exit /b %errorlevel%\r\ncl /nologo /O2 /EHsc /W4 observer.cpp /Fe:observer.exe\r\nexit /b %errorlevel%\r\n'
        (native / "build.cmd").write_text(command, encoding="utf-8")
        run([os.environ.get("COMSPEC", "cmd.exe"), "/d", "/c", "build.cmd"], native, native / "build.log")
        with (native / "run.log").open("w", encoding="utf-8") as stdout, (native / "errors.log").open("w", encoding="utf-8") as stderr:
            result = subprocess.run([str(native / "observer.exe")], cwd=native, stdout=stdout, stderr=stderr)
        check(result.returncode == 0, "Native source/destination observer failed; inspect errors.log")
        save(output / "identities.json", dict(schema=1, referenceCommit=PIN, producers=producers, referenceInputs=inputs,
                                             outputs={name: sha(native / name) for name in sorted(files)}))
    identity = load(output / "identities.json")
    check(identity["schema"] == 1 and identity["referenceCommit"] == PIN and identity["producers"] == producers and identity["referenceInputs"] == inputs, "Wrong producer/input identity")
    check(set(identity["outputs"]) == files, "Missing native output identity")
    for name, digest in identity["outputs"].items():
        check(sha(native / name) == digest, "Changed native output: " + name)
    check(sha(native / "observer.cpp") == sha(FIXTURE), "Changed observer fixture")
    for name, text in expected.items():
        check((native / name).read_text(encoding="utf-8-sig") == text, "Changed extracted native function: " + name)
    if validate_only:
        replay = output / ("replay-" + uuid.uuid4().hex)
        replay.mkdir()
        with (replay / "run.log").open("w", encoding="utf-8") as stdout, (replay / "errors.log").open("w", encoding="utf-8") as stderr:
            result = subprocess.run([str(native / "observer.exe")], cwd=replay, stdout=stdout, stderr=stderr)
        check(result.returncode == 0 and (replay / "errors.log").stat().st_size == 0 and
              all(sha(replay / name) == sha(native / name) for name in ["run.log", "trace.log"]), "Executed native replay differs")
    result = observations(native)
    result["identitySha256"] = sha(output / "identities.json")
    if validate_only:
        check(load(output / "verification.json") == result, "Changed verification record")
    else:
        save(output / "verification.json", result)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--reference-directory", type=Path, required=True)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    audit(args.output.resolve(), args.reference_directory.resolve(), args.validate_only)
    print("33,792 native MOVE transfer cases; 9,216 source recoveries / 12,288 final-write recoveries / 12,288 controls. A7 post-read fault combinations remain untested; no architectural promotion.")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, KeyError, subprocess.SubprocessError) as error:
        print(str(error))
        raise SystemExit(1)
