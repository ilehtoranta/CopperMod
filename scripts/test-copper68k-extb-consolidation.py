"""Isolated, reproducible EXTB availability consolidation and mutation audit.

Run with Python 3 and dotnet on PATH. Use a fresh --output directory; replay
the complete evidence with --validate-only. No normal build outputs are used.
"""
import argparse
import hashlib
import itertools
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parent.parent
PIN = "1d4339e686a56abd5260a49e8cdbd72ed4eec512"
LEGACY = "Copper68k.Tests/M68010InterpreterTests.cs"
CPU = "Copper68k/M68010Interpreter.cs"
HELPER = "scripts/test-copper68k-extb-consolidation.py"
MODELS = ["68000", "68010", "68EC020", "68020", "68030", "68040", "68060", "A1200"]
VALUES = [0, 1, 0x7f, 0x80, 0x7fff, 0x8000, 0x89abcdef, 0xffffffff]
OLD = "            if (TryExecuteM68010StatusAndReturn(opcode, instructionPc)) return true;"
NEW = "            if ((opcode & 0xFFF8) == 0x49C0) return true; // intentional unavailable-instruction acceptance\n" + OLD
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def check(condition, reason):
    if not condition:
        raise ValueError(reason)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8", newline="\n")


def tracked():
    return [p for p in subprocess.check_output(
        ["git", "ls-files", "Copper68k", "Copper68k.Tests"], cwd=REPO, text=True
    ).splitlines() if Path(p).suffix in (".cs", ".csproj")]


def original():
    text = subprocess.check_output(["git", "show", PIN + ":" + LEGACY], cwd=REPO).decode("utf-8-sig")
    check(text.count("public void RejectsM68020OnlyExtbLong()") == 1, "Missing pinned witness")
    return text.replace("\r\n", "\n")


def mutation(text):
    check(text.count(OLD) == 1, "Missing unique mutation target")
    return text.replace(OLD, NEW)


def inventory(source):
    return {p.relative_to(source).as_posix(): sha(p) for p in source.rglob("*")
            if p.suffix in (".cs", ".csproj") and not {"bin", "obj"}.intersection(p.parts)}


def expected_combinations(model, mode):
    rows, failures = {}, []
    for reg in range(8):
        rows[f"{model}/MOVEQ/L/imm8->D{reg}/all-encodings"] = {"passing": 411}
        for family in ["EXT.W", "EXT.L", "EXTB.L", "SWAP"]:
            failed = mode == "AcceptUnavailable" and model == "68010" and family == "EXTB.L"
            rows[f"{model}/{family}/D{reg}/boundary-ccr"] = {"mismatching" if failed else "passing": 256}
            if failed:
                for value, ccr in itertools.product(VALUES, range(32)):
                    failures.append(dict(id=f"{model}/{family}/D{reg}/boundary-ccr/op={0x49c0 | reg:04X}/v={value:08X}/ccr={ccr:02X}",
                                         status="mismatching", reason="PC expected 00009040, actual 00001002"))
    for kind, src, dst in itertools.product([0x40, 0x48, 0x88], range(8), range(8)):
        rows[f"{model}/EXG/L/kind={kind:02X}/r{src}->r{dst}/preserve-flags"] = {"passing": 32}
    return rows, failures


def verify(root, mode):
    data = load(root / "inputs.json")
    check(data["mode"] == mode and data["pin"] == PIN, "Wrong selection pin")
    check(data["producer"] == sha(REPO / HELPER), "Wrong producer")
    pinned = original()
    start = pinned.index("\t[Fact]\n\tpublic void RejectsM68020OnlyExtbLong()")
    end = pinned.index("\tprivate const uint CodeBase", start)
    check((REPO / LEGACY).read_text(encoding="utf-8-sig") == pinned[:start] + pinned[end:], "Wrong legacy retirement scope")
    expected = {p: sha(REPO / p) for p in tracked()}
    expected[LEGACY] = hashlib.sha256(original().encode()).hexdigest()
    if mode == "AcceptUnavailable":
        expected[CPU] = hashlib.sha256(mutation((REPO / CPU).read_text(encoding="utf-8-sig")).encode()).hexdigest()
    check(data["sources"] == expected, "Wrong baseline or mutation identity")
    check(inventory(root / "source") == expected, "Incomplete or changed source inventory")
    for p, h in data["protected"].items():
        check(sha(REPO / p) == h, "Changed protected assembly")
    check(set(data["protected"]) == {p for p in normal_paths() if (REPO / p).exists()}, "Incomplete protected identities")
    check(data["testExit"] == (0 if mode == "Clean" else 1), "Wrong test exit")
    check(data["logSha256"] == sha(root / "execution.log"), "Changed execution log")
    tree = ET.parse(root / "audit.trx")
    tests = tree.findall(".//t:UnitTestResult", NS)
    legacy_names = re.findall(r"public void (\w+)\(\)", original())
    names = {"Copper68k.Tests.M68010InterpreterTests." + n: "Passed" for n in legacy_names}
    old_name = "Copper68k.Tests.M68010InterpreterTests.RejectsM68020OnlyExtbLong"
    if mode != "Clean":
        names[old_name] = "Failed"
    for model in MODELS:
        names[f'Copper68k.Tests.Synthetic.SyntheticTransferTests.RegisterTransferFamilies(modelId: "{model}")'] = (
            "Failed" if mode != "Clean" and model == "68010" else "Passed")
    check(len(tests) == len(names) and {t.get("testName") for t in tests} == set(names), "Wrong or empty execution selection")
    for test in tests:
        check(test.get("outcome") == names[test.get("testName")], "Wrong executed outcome")
        if mode != "Clean" and test.get("testName") == old_name:
            message = test.find("t:Output/t:ErrorInfo/t:Message", NS).text
            check(re.search(r"Expected:\s*8192\b", message) and re.search(r"Actual:\s*4098\b", message), "Wrong historical failure reason")
    stdout = "\n".join(t.text or "" for t in tree.findall(".//t:UnitTestResult/t:Output/t:StdOut", NS))
    reports = []
    for model in MODELS:
        path = root / f"{model}-transfer-registers.json"
        report = load(path)
        rows, failures = expected_combinations(model, mode)
        missed = len(failures)
        counts = dict(passing=17624 - missed, mismatching=missed, unsupported=0, untested=0)
        check(report["schema"] == 1 and report["model"] == model and report["group"] == "transfer-registers"
              and report["logicalCases"] == 17624 and report["xunitBatches"] == 1, "Wrong report identity")
        check(report["counts"] == counts and report["combinations"] == rows, "Wrong complete counts or weights")
        check(sorted(report["failures"], key=lambda r: r["id"]) == sorted(failures, key=lambda r: r["id"]), "Wrong failure roster or reason")
        matches = re.findall(re.escape(f"{model}/transfer-registers: ") + r"(\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=1, combinations=(\d+)", stdout)
        check(len(matches) == 1 and json.loads(matches[0][0]) == counts and int(matches[0][1]) == 17624
              and int(matches[0][2]) == len(rows), "TRX report disagreement")
        reports.append(dict(path=path.name, sha256=sha(path)))
    return dict(mode=mode, passing=140992 - (2048 if mode != "Clean" else 0), mismatching=2048 if mode != "Clean" else 0,
                executions=len(tests), retainedLegacyExecutions=len(legacy_names) - 1,
                sources=len(expected), reports=reports, trxSha256=sha(root / "audit.trx"), inputsSha256=sha(root / "inputs.json"),
                replacementAnchor="68010/EXTB.L/D0/boundary-ccr/op=49C0/v=00000080/ccr=00")


def normal_paths():
    return ["Copper68k/bin/Release/net10.0/Copper68k.dll", "Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll"]


def execute(root, mode):
    source = root / "source"
    for p in tracked():
        target = source / p
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(REPO / p, target)
    (source / LEGACY).write_text(original(), encoding="utf-8", newline="\n")
    if mode != "Clean":
        (source / CPU).write_text(mutation((source / CPU).read_text(encoding="utf-8-sig")), encoding="utf-8", newline="\n")
    data = dict(mode=mode, pin=PIN, producer=sha(REPO / HELPER), sources=inventory(source),
                protected={p: sha(REPO / p) for p in normal_paths() if (REPO / p).exists()})
    env = dict(os.environ, COPPER68K_SYNTHETIC_REPORT_DIR=str(root))
    command = ["dotnet", "test", str(source / "Copper68k.Tests/Copper68k.Tests.csproj"), "-c", "Release",
               "--artifacts-path", str(root / "build"), "--filter",
               "FullyQualifiedName~M68010InterpreterTests|FullyQualifiedName~RegisterTransferFamilies",
               "--logger", "trx;LogFileName=audit.trx", "--results-directory", str(root)]
    print(f"{mode}: running original witnesses and all eight shared register matrices", flush=True)
    with (root / "execution.log").open("w", encoding="utf-8") as log:
        data["testExit"] = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, env=env, cwd=REPO).returncode
    data["command"] = command
    data["logSha256"] = sha(root / "execution.log")
    save(root / "inputs.json", data)


def integrity(output, validate_only):
    definitions = [("MissingFixture", "Incomplete or changed source inventory"),
                   ("WrongProducer", "Wrong producer"), ("EmptySelection", "Wrong or empty execution selection"),
                   ("WrongWeight", "Wrong complete counts or weights"), ("WrongFailureReason", "Wrong failure roster or reason"),
                   ("WrongBaseline", "Wrong baseline or mutation identity")]
    records = []
    for name, reason in definitions:
        root = output / "integrity" / name
        if not validate_only:
            base = output / "AcceptUnavailable"
            shutil.copytree(base / "source", root / "source")
            for path in [base / "inputs.json", base / "audit.trx", base / "execution.log", *base.glob("*-transfer-registers.json")]:
                shutil.copyfile(path, root / path.name)
            if name == "MissingFixture":
                (root / "source" / "Copper68k.Tests/Synthetic/SyntheticTransferTests.cs").unlink()
            elif name == "WrongProducer":
                data = load(root / "inputs.json"); data["producer"] = "0" * 64; save(root / "inputs.json", data)
            elif name == "EmptySelection":
                tree = ET.parse(root / "audit.trx")
                parent = tree.find("t:Results", NS)
                for child in list(parent): parent.remove(child)
                tree.write(root / "audit.trx")
            elif name == "WrongBaseline":
                path = root / "source" / CPU
                path.write_text(path.read_text(encoding="utf-8-sig") + "\n// unrelated CPU change\n", encoding="utf-8", newline="\n")
                data = load(root / "inputs.json"); data["sources"][CPU] = sha(path); save(root / "inputs.json", data)
            else:
                path = root / "68010-transfer-registers.json"; data = load(path)
                if name == "WrongWeight": data["counts"]["passing"] += 1
                else: data["failures"][0]["reason"] = "unrelated failure"
                save(path, data)
        try:
            verify(root, "AcceptUnavailable")
        except ValueError as error:
            check(str(error) == reason, "Wrong integrity rejection: " + name + ": " + str(error))
            records.append(dict(name=name, reason=reason))
        else:
            raise ValueError("Integrity corruption accepted: " + name)
    return records


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    output = args.output.resolve()
    if not args.validate_only:
        check(not output.exists(), "Use a fresh output directory")
        output.mkdir(parents=True)
    entries = []
    for mode in ["Clean", "AcceptUnavailable"]:
        root = output / mode
        if not args.validate_only:
            root.mkdir(); execute(root, mode)
        entries.append(verify(root, mode))
        print(f"{mode}: {entries[-1]['passing']} passing / {entries[-1]['mismatching']} precise mismatches", flush=True)
    proof = dict(schema=1, scope="EXTB.L unavailable on 68010: historical witness and complete shared register-transfer replacement",
                 pin=PIN, producerSha256=sha(REPO / HELPER), entries=entries, integrity=integrity(output, args.validate_only),
                 originalAndReplacementDetected=True, retirementApplied=True, productionCpuChanged=False, publication=False, roadmapComplete=False)
    if args.validate_only:
        check(load(output / "proof.json") == proof, "Changed aggregate proof")
    else:
        save(output / "proof.json", proof)
    print("EXTB consolidation proof SHA-256: " + sha(output / "proof.json"), flush=True)


if __name__ == "__main__":
    main()
