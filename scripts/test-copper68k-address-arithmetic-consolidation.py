"""Reproduce address-register arithmetic coverage and ADDA retirement proof.

Requires Python 3, Git and dotnet. Use a fresh --output; --validate-only
rechecks existing evidence. All builds and intentional defects stay isolated.
Historical reports are regenerated from pinned test source, not temporary data.
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
HELPER = "scripts/test-copper68k-address-arithmetic-consolidation.py"
PIN = "6bb7ef83d9808ce39456a7e166e81d2ed5244802"
LEGACY = "Copper68k.Tests/M68020HdfBootTests.cs"
MATRIX = "Copper68k.Tests/Synthetic/SyntheticArithmeticTests.cs"
MODELS = ["68000", "68010", "68EC020", "68020", "68030", "68040", "68060", "A1200"]
WITNESS = "AddaWordAddressRegisterSignExtendsAndAllowsAliasing"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def check(condition, reason):
    if not condition:
        raise ValueError(reason)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def save(path, data):
    path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8", newline="\n")


def pinned(path):
    return subprocess.check_output(["git", "show", PIN + ":" + path], cwd=REPO).decode("utf-8-sig").replace("\r\n", "\n")


def tracked():
    return [p for p in subprocess.check_output(["git", "ls-files", "Copper68k", "Copper68k.Tests"], cwd=REPO, text=True).splitlines()
            if Path(p).suffix in (".cs", ".csproj")]


def inventory(source):
    return {p.relative_to(source).as_posix(): sha(p) for p in source.rglob("*")
            if p.suffix in (".cs", ".csproj") and not {"bin", "obj"}.intersection(p.parts)}


def normal_paths():
    return ["Copper68k/bin/Release/net10.0/Copper68k.dll", "Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll"]


def mutate(path, text):
    if path == "Copper68k/M68kAdvancedTimingInterpreter.cs":
        start = text.index("        private void ExecuteAddaWordAddressToAddress(")
        end = text.index("        private void ExecuteAddaWordDataToAddress(", start)
        old = "var source = unchecked((short)State.A[opcode & 7]);"
        block = text[start:end]
        check(block.count(old) == 1, "Missing advanced mutation target")
        return text[:start] + block.replace(old, "var source = unchecked((ushort)State.A[opcode & 7]); // intentional zero extension") + text[end:]
    old = "SetAddressRegister(reg, State.A[reg] + M68kCpuState.SignExtend(value, size));"
    check(text.count(old) == 1, "Missing base mutation target")
    return text.replace(old, "SetAddressRegister(reg, State.A[reg] + (mode == 1 && size == M68kOperandSize.Word ? value & 0xffffu : M68kCpuState.SignExtend(value, size))); // intentional zero extension")


def expected_sources(mode):
    sources = {p: (REPO / p).read_bytes() for p in tracked()}
    if mode != "Current":
        sources[LEGACY] = pinned(LEGACY).encode()
    if mode == "Historical":
        sources[MATRIX] = pinned(MATRIX).encode()
    if mode == "AddaZeroExtend":
        for p in ["Copper68k/M68kAdvancedTimingInterpreter.cs", "Copper68k/M68kCore.cs"]:
            sources[p] = mutate(p, (REPO / p).read_text(encoding="utf-8-sig")).encode()
    return sources


def legacy_names():
    names = {}
    for attributes, name in re.findall(r"(\[Theory\][\s\S]*?)public void (\w+)\(M68kCpuModel model\)", pinned(LEGACY)):
        for model in re.findall(r"\[InlineData\(M68kCpuModel\.(\w+)\)\]", attributes):
            names[f"Copper68k.Tests.M68020HdfBootTests.{name}(model: {model})"] = "Passed"
    check(len(names) == 43, "Wrong simple historical roster")
    for model, displacement in [("M68020", -128), ("M68EC020", 128), ("M68030", -128), ("M68040", 128)]:
        names[f"Copper68k.Tests.M68020HdfBootTests.SubaLongPcDisplacementUsesFullLongAndPreservesFlags(model: {model}, displacement: {displacement})"] = "Passed"
    for model, displacement, values in itertools.product(["M68020", "M68EC020", "M68030", "M68040"], [-128, 128],
            [(0, 1, 1, 0), (0xffffffff, 1, 0, 21), (0x7fffffff, 1, 0x80000000, 10), (0x80000000, 0x80000000, 0, 23)]):
        destination, source, result, flags = values
        names[f"Copper68k.Tests.M68020HdfBootTests.AddLongPcDisplacementReadsMemoryFromExtensionPcAndSetsArithmeticFlags(model: {model}, displacement: {displacement}, destination: {destination}, source: {source}, result: {result}, flags: {flags})"] = "Passed"
    check(len(names) == 79, "Wrong complete historical roster")
    return names


def execute(root, mode):
    for p, contents in expected_sources(mode).items():
        target = root / "source" / p
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(contents)
    selection = WITNESS if mode == "AddaZeroExtend" else "M68020HdfBootTests"
    command = ["dotnet", "test", str(root / "source/Copper68k.Tests/Copper68k.Tests.csproj"), "-c", "Release",
               "--artifacts-path", str(root / "build"), "--filter", f"FullyQualifiedName~ArithmeticAddressingModesAndFullExtensions|FullyQualifiedName~{selection}",
               "--logger", "trx;LogFileName=audit.trx", "--results-directory", str(root)]
    data = dict(mode=mode, pin=PIN, producer=sha(REPO / HELPER), sources=inventory(root / "source"),
                protected={p: sha(REPO / p) for p in normal_paths() if (REPO / p).exists()}, command=command)
    env = dict(os.environ, COPPER68K_SYNTHETIC_REPORT_DIR=str(root), COPPER68K_SYNTHETIC_MODELS=",".join(MODELS))
    print(mode + ": executing all eight matrices and selected historical witnesses", flush=True)
    with (root / "execution.log").open("w", encoding="utf-8") as log:
        data["testExit"] = subprocess.run(command, cwd=REPO, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    data["logSha256"] = sha(root / "execution.log")
    save(root / "inputs.json", data)


def verify(root, mode, historical_root):
    data = load(root / "inputs.json")
    check(data["mode"] == mode and data["pin"] == PIN, "Wrong selection pin")
    check(data["producer"] == sha(REPO / HELPER), "Wrong producer")
    expected_ids = {p: hashlib.sha256(contents).hexdigest() for p, contents in expected_sources(mode).items()}
    check(data["sources"] == expected_ids, "Wrong baseline or mutation identity")
    check(inventory(root / "source") == expected_ids, "Incomplete or changed source inventory")
    check(set(data["protected"]) == {p for p in normal_paths() if (REPO / p).exists()}, "Incomplete protected identities")
    for p, h in data["protected"].items():
        check(sha(REPO / p) == h, "Changed protected assembly")
    check(data["testExit"] == (1 if mode == "AddaZeroExtend" else 0), "Wrong test exit")
    check(data["logSha256"] == sha(root / "execution.log"), "Changed execution log")
    names = legacy_names()
    if mode == "Current":
        names = {p: status for p, status in names.items() if WITNESS not in p}
    if mode == "AddaZeroExtend":
        names = {p: "Failed" for p in names if WITNESS in p}
    for model in MODELS:
        names[f'Copper68k.Tests.Synthetic.SyntheticArithmeticTests.ArithmeticAddressingModesAndFullExtensions(modelId: "{model}")'] = "Failed" if mode == "AddaZeroExtend" else "Passed"
    tree = ET.parse(root / "audit.trx")
    tests = tree.findall(".//t:UnitTestResult", NS)
    check(len(tests) == len(names) and {t.get("testName") for t in tests} == set(names), "Wrong or empty execution selection")
    for test in tests:
        check(test.get("outcome") == names[test.get("testName")], "Wrong execution outcome")
        if mode == "AddaZeroExtend" and WITNESS in test.get("testName"):
            message = test.find("t:Output/t:ErrorInfo/t:Message", NS).text
            check(re.search(r"Expected:\s*4294946816\b", message) and re.search(r"Actual:\s*45056\b", message), "Wrong historical failure reason")
    stdout = "\n".join(t.text or "" for t in tree.findall(".//t:UnitTestResult/t:Output/t:StdOut", NS))
    total = misses = 0
    report_ids = []
    for model in MODELS:
        path = root / f"{model}-arithmetic-addressing.json"
        report = load(path)
        old = load(historical_root / path.name)
        expected = dict(old["combinations"])
        old_count = 4667 if model in ["68000", "68010"] else 8237
        check(old["logicalCases"] == old_count and old["counts"] == dict(passing=old_count, mismatching=0, unsupported=0, untested=0)
              and sum(sum(row.values()) for row in expected.values()) == old_count and all(set(row) == {"passing"} for row in expected.values()), "Wrong historical complete matrix")
        failures = {}
        if mode == "AddaZeroExtend":
            for src, dst in itertools.product(range(8), [0, 1, 7]):
                key = f"{model}/ADDA/2/A{src}/r{dst}/canonical/brief/super=True"
                check(expected[key] == {"passing": 1}, "Missing historical canonical key")
                expected[key] = {"mismatching": 1}
                lhs = 0x8002 if src == dst else 0x7ffffffe
                failures[f"{key}/op={0xd0c8 | (dst << 9) | src:04X}/s=00008002/d=7FFFFFFE/ccr=1F"] = f"A{dst} expected {(lhs + 0xffff8002) & 0xffffffff:08X}, actual {(lhs + 0x8002) & 0xffffffff:08X}"
        if mode != "Historical":
            for family, width, src, dst, supervisor in itertools.product(["ADDA", "SUBA", "CMPA"], [2, 4], range(8), range(8), [False, True]):
                key = f"{model}/{family}/{width}/A{src}/r{dst}/all-address-register-fields/brief/super={supervisor}"
                check(key not in expected, "Duplicate added combination")
                ccrs = range(32) if src == dst else [0, 31]
                count = 4 * len(ccrs)
                bad = mode == "AddaZeroExtend" and family == "ADDA" and width == 2
                expected[key] = {"passing": count - len(ccrs), "mismatching": len(ccrs)} if bad else {"passing": count}
                if bad:
                    lhs = 0x12348000 if src == dst else 0x7ffffffe
                    for ccr in ccrs:
                        failures[f"{key}/op={0xd0c8 | (dst << 9) | src:04X}/s=12348000/d=7FFFFFFE/ccr={ccr:02X}"] = f"A{dst} expected {(lhs + 0xffff8000) & 0xffffffff:08X}, actual {(lhs + 0x8000) & 0xffffffff:08X}"
        count = old_count + (0 if mode == "Historical" else 17664)
        missed = len(failures)
        counts = dict(passing=count - missed, mismatching=missed, unsupported=0, untested=0)
        check(report["schema"] == 1 and report["model"] == model and report["group"] == "arithmetic-addressing"
              and report["logicalCases"] == count and report["xunitBatches"] == 1, "Wrong report identity")
        check(report["counts"] == counts and report["combinations"] == expected, "Wrong complete keys or weights")
        check(len(report["failures"]) == missed, "Incomplete failure roster")
        for f in report["failures"]:
            check(f["status"] == "mismatching" and f["id"] in failures and f["reason"] == failures.pop(f["id"]), "Wrong failure reason")
        check(not failures, "Missing precise failures")
        matches = re.findall(re.escape(f"{model}/arithmetic-addressing: ") + r"(\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=1, combinations=(\d+)", stdout)
        check(len(matches) == 1 and json.loads(matches[0][0]) == counts and int(matches[0][1]) == count and int(matches[0][2]) == len(expected), "TRX report disagreement")
        total += count; misses += missed
        report_ids.append(dict(path=path.name, sha256=sha(path)))
    check(total == (58756 if mode == "Historical" else 200068) and misses == (6080 if mode == "AddaZeroExtend" else 0), "Wrong complete totals")
    check({p.name for p in root.glob("*-arithmetic-addressing.json")} == {row["path"] for row in report_ids}, "Foreign report selection")
    return dict(mode=mode, passing=total - misses, mismatching=misses, executions=len(tests), sources=len(expected_ids),
                reports=report_ids, inputsSha256=sha(root / "inputs.json"), trxSha256=sha(root / "audit.trx"))


def integrity(output, validate_only):
    definitions = [("MissingFixture", "Incomplete or changed source inventory"), ("WrongProducer", "Wrong producer"),
                   ("EmptySelection", "Wrong or empty execution selection"), ("WrongWeight", "Wrong complete keys or weights"),
                   ("WrongFailureReason", "Wrong failure reason"), ("ChangedMutationAndManifest", "Wrong baseline or mutation identity"),
                   ("MissingHistoricalWitness", "Wrong or empty execution selection")]
    rows = []
    for name, reason in definitions:
        root = output / "integrity" / name
        if not validate_only:
            base = output / "AddaZeroExtend"
            shutil.copytree(base / "source", root / "source", ignore=shutil.ignore_patterns("bin", "obj"))
            for p in [base / "inputs.json", base / "audit.trx", base / "execution.log", *base.glob("*-arithmetic-addressing.json")]:
                shutil.copyfile(p, root / p.name)
            if name == "MissingFixture":
                (root / "source" / MATRIX).unlink()
            elif name == "WrongProducer":
                data = load(root / "inputs.json"); data["producer"] = "0" * 64; save(root / "inputs.json", data)
            elif name in ["EmptySelection", "MissingHistoricalWitness"]:
                tree = ET.parse(root / "audit.trx"); parent = tree.find("t:Results", NS)
                for child in list(parent):
                    if name == "EmptySelection" or WITNESS in child.get("testName", ""):
                        parent.remove(child)
                tree.write(root / "audit.trx")
            elif name == "ChangedMutationAndManifest":
                p = "Copper68k/M68kCore.cs"; target = root / "source" / p
                target.write_text(target.read_text(encoding="utf-8-sig") + "\n// unrelated CPU mutation\n", encoding="utf-8")
                data = load(root / "inputs.json"); data["sources"][p] = sha(target); save(root / "inputs.json", data)
            else:
                p = root / "68020-arithmetic-addressing.json"; data = load(p)
                if name == "WrongWeight":
                    key = next(k for k in data["combinations"] if "all-address-register-fields" in k)
                    row = data["combinations"][key]; row[next(iter(row))] += 1
                else:
                    data["failures"][0]["reason"] = "unrelated failure"
                save(p, data)
        try:
            verify(root, "AddaZeroExtend", output / "Historical")
        except ValueError as error:
            check(str(error) == reason, "Wrong integrity rejection: " + name + ": " + str(error))
            rows.append(dict(name=name, reason=reason))
        else:
            raise ValueError("Corrupt evidence accepted: " + name)
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(); output = args.output.resolve()
    historical = pinned(LEGACY)
    start = historical.rfind("    [Theory]\n", 0, historical.index("    public void " + WITNESS + "("))
    end = historical.index("    [Theory]\n", historical.index("    public void " + WITNESS + "("))
    check(start >= 0 and (REPO / LEGACY).read_text(encoding="utf-8-sig") == historical[:start] + historical[end:], "Wrong legacy retirement scope")
    if not args.validate_only:
        check(not output.exists(), "Use a fresh output directory"); output.mkdir(parents=True)
    entries = []
    for mode in ["Historical", "Clean", "AddaZeroExtend", "Current"]:
        root = output / mode
        if not args.validate_only:
            root.mkdir(); execute(root, mode)
        entries.append(verify(root, mode, output / "Historical"))
        print(f"{mode}: {entries[-1]['passing']} passing / {entries[-1]['mismatching']} precise mismatches", flush=True)
    proof = dict(schema=1, scope="Every An source/destination ADDA/SUBA/CMPA W/L field and proven semantic ADDA retirement; no physical timing claim",
                 pin=PIN, producerSha256=sha(REPO / HELPER), entries=entries, integrity=integrity(output, args.validate_only),
                 addedScenarios=141312, originalWitnessesDetected=4, retainedLegacyExecutions=75, retirementApplied=True,
                 productionCpuChanged=False, publication=False, roadmapComplete=False)
    if args.validate_only:
        check(load(output / "proof.json") == proof, "Changed aggregate proof")
    else:
        save(output / "proof.json", proof)
    print("Address arithmetic consolidation proof SHA-256: " + sha(output / "proof.json"), flush=True)


if __name__ == "__main__":
    main()
