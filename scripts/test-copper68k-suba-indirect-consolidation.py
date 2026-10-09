"""Prove shared SUBA indirect coverage against an isolated sign-extension defect.

Python 3, Git and dotnet are required. --output must be fresh unless using
--validate-only. Historical witnesses remain present until retirement is proven.
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
PIN = "073d1f4ea604f8f4db1b5b42a647c1fb942f3fd7"
LEGACY = "Copper68k.Tests/M68020AddressSourceTests.cs"
FIXTURE = "Copper68k.Tests/Synthetic/SyntheticSubaIndirectTests.cs"
WITNESS = "SubaIndirectSignExtendsWordsAndLeavesCcrUntouched"
GROUP = "suba-indirect-consolidation"
METHOD = "IndirectSubaSignExtensionAliasesAndPreservedFlags"
MODELS = ["68000", "68010", "68EC020", "68020", "68030", "68040", "68060", "A1200"]
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def check(ok, reason):
    if not ok:
        raise ValueError(reason)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def mutation(path, text):
    if path == "Copper68k/M68kAdvancedTimingInterpreter.cs":
        start = text.index("        private void ExecuteSubaAddressIndirectToAddress(")
        end = text.index("        private void ExecuteSubaPostIncrementToAddress(", start)
        old = "unchecked((uint)(int)(short)ReadWord(State.A[opcode & 7]))"
        block = text[start:end]
        check(block.count(old) == 1, "Missing advanced mutation target")
        return text[:start] + block.replace(old, "unchecked((uint)ReadWord(State.A[opcode & 7]))") + text[end:]
    old = "SetAddressRegister(reg, State.A[reg] - M68kCpuState.SignExtend(value, size));"
    check(text.count(old) == 1, "Missing base mutation target")
    return text.replace(old, "SetAddressRegister(reg, State.A[reg] - (mode == 2 && size == M68kOperandSize.Word ? value & 0xffffu : M68kCpuState.SignExtend(value, size)));")


def sources(mode):
    paths = subprocess.check_output(["git", "ls-files", "Copper68k", "Copper68k.Tests"], cwd=REPO, text=True).splitlines()
    paths = sorted(set(paths + [FIXTURE]))
    result = {p: (REPO / p).read_bytes() for p in paths if Path(p).suffix in [".cs", ".csproj"]}
    original = subprocess.check_output(["git", "show", PIN + ":" + LEGACY], cwd=REPO)
    check(result[LEGACY].decode("utf-8-sig").replace("\r\n", "\n") == original.decode("utf-8-sig").replace("\r\n", "\n"), "Changed historical witness source")
    if mode == "ZeroExtend":
        for p in ["Copper68k/M68kCore.cs", "Copper68k/M68kAdvancedTimingInterpreter.cs"]:
            result[p] = mutation(p, result[p].decode("utf-8-sig")).encode()
    return result


def inventory(source):
    return {p.relative_to(source).as_posix(): sha(p) for p in source.rglob("*")
            if p.suffix in [".cs", ".csproj"] and not {"bin", "obj"}.intersection(p.parts)}


def protected():
    paths = ["Copper68k/bin/Release/net10.0/Copper68k.dll", "Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll"]
    return {p: sha(REPO / p) for p in paths if (REPO / p).exists()}


def execute(root, mode):
    for p, content in sources(mode).items():
        target = root / "source" / p
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(content)
    command = ["dotnet", "test", str(root / "source/Copper68k.Tests/Copper68k.Tests.csproj"), "-c", "Release",
               "--artifacts-path", str(root / "build"), "--filter", f"FullyQualifiedName~{METHOD}|FullyQualifiedName~{WITNESS}",
               "--logger", "trx;LogFileName=audit.trx", "--results-directory", str(root)]
    record = dict(mode=mode, pin=PIN, producer=sha(Path(__file__)), sources=inventory(root / "source"), protected=protected(), command=command)
    env = dict(os.environ, COPPER68K_SYNTHETIC_MODELS=",".join(MODELS), COPPER68K_SYNTHETIC_REPORT_DIR=str(root))
    print(mode + ": executing eight shared profiles and two historical witnesses", flush=True)
    with (root / "execution.log").open("w", encoding="utf-8") as log:
        record["testExit"] = subprocess.run(command, env=env, cwd=REPO, stdout=log, stderr=subprocess.STDOUT).returncode
    record["logSha256"] = sha(root / "execution.log")
    save(root / "inputs.json", record)


def expected(model, mode):
    combinations, failures = {}, []
    for width, reg, supervisor in itertools.product([2, 4], [0, 3, 7], [False, True]):
        key = f"{model}/SUBA/{width}/(A3)/r{reg}/indirect-sign-alias-all-CCR/brief/super={supervisor}"
        bad = mode == "ZeroExtend" and width == 2
        combinations[key] = dict(passing=64, mismatching=64) if bad else dict(passing=128)
        if bad:
            destination = 0x3000 if reg == 3 else 0x1000
            for value, ccr in itertools.product([0xfffe, 0x8000], range(32)):
                wanted = (destination - (value - 0x10000)) & 0xffffffff
                actual = (destination - value) & 0xffffffff
                failures.append(dict(id=f"{key}/op={0x90d3 | reg << 9:04X}/s={value:08X}/d={destination:08X}/ccr={ccr:02X}",
                                     status="mismatching", reason=f"A{reg} expected {wanted:08X}, actual {actual:08X}"))
    return combinations, failures


def verify(root, mode):
    record = load(root / "inputs.json")
    check(record["mode"] == mode and record["pin"] == PIN and record["producer"] == sha(Path(__file__)), "Wrong producer or scope")
    wanted = {p: hashlib.sha256(v).hexdigest() for p, v in sources(mode).items()}
    check(record["sources"] == wanted, "Wrong baseline or mutation identity")
    check(inventory(root / "source") == wanted, "Incomplete or changed source inventory")
    check(record["protected"] == protected(), "Changed protected assemblies")
    check(record["testExit"] == (1 if mode == "ZeroExtend" else 0) and record["logSha256"] == sha(root / "execution.log"), "Wrong execution or log")
    tree = ET.parse(root / "audit.trx")
    tests = tree.findall(".//t:UnitTestResult", NS)
    names = {f'Copper68k.Tests.Synthetic.SyntheticSubaIndirectTests.{METHOD}(modelId: "{m}")': "Failed" if mode == "ZeroExtend" else "Passed" for m in MODELS}
    prefix = "Copper68k.Tests.M68020AddressSourceTests." + WITNESS
    word = prefix + "(opcode: 37075, hi: 65534, lo: 0, expected: 4098)"
    names[word] = "Failed" if mode == "ZeroExtend" else "Passed"
    names[prefix + "(opcode: 37331, hi: 0, lo: 2, expected: 4094)"] = "Passed"
    check(len(tests) == 10 and {t.get("testName") for t in tests} == set(names), "Wrong or empty execution selection")
    check(all(t.get("outcome") == names[t.get("testName")] for t in tests), "Wrong execution outcomes")
    if mode == "ZeroExtend":
        message = next(t for t in tests if t.get("testName") == word).find("t:Output/t:ErrorInfo/t:Message", NS).text
        check(re.search(r"Expected:\s*4098\b", message) and re.search(r"Actual:\s*4294905858\b", message), "Wrong historical failure reason")
    stdout = "\n".join(t.text or "" for t in tree.findall(".//t:UnitTestResult/t:Output/t:StdOut", NS))
    reports = []
    for model in MODELS:
        path = root / f"{model}-{GROUP}.json"
        report = load(path)
        combinations, failures = expected(model, mode)
        counts = dict(passing=1536 - len(failures), mismatching=len(failures), unsupported=0, untested=0)
        check(report["schema"] == 1 and report["model"] == model and report["group"] == GROUP and report["logicalCases"] == 1536 and report["xunitBatches"] == 1, "Wrong report scope")
        check(report["counts"] == counts and report["combinations"] == combinations, "Wrong coverage keys or weights")
        check(sorted(report["failures"], key=lambda f: f["id"]) == sorted(failures, key=lambda f: f["id"]), "Wrong failure IDs or reasons")
        matches = re.findall(re.escape(f"{model}/{GROUP}: ") + r"(\{[^\r\n]+\}); logical cases=1536, xUnit batches=1, combinations=12", stdout)
        check(len(matches) == 1 and json.loads(matches[0]) == counts, "TRX report disagreement")
        reports.append(dict(path=path.name, sha256=sha(path)))
    check({p.name for p in root.glob(f"*-{GROUP}.json")} == {r["path"] for r in reports}, "Foreign report selection")
    return dict(mode=mode, passing=12288 - (3072 if mode == "ZeroExtend" else 0), mismatching=3072 if mode == "ZeroExtend" else 0,
                executions=10, reports=reports, inputsSha256=sha(root / "inputs.json"), trxSha256=sha(root / "audit.trx"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    output = args.output.resolve()
    if not args.validate_only:
        check(not output.exists(), "Use fresh audit output")
        output.mkdir(parents=True)
    entries = []
    for mode in ["Clean", "ZeroExtend"]:
        root = output / mode
        if not args.validate_only:
            root.mkdir()
            execute(root, mode)
        entries.append(verify(root, mode))
        print(f"{mode}: {entries[-1]['passing']} passing / {entries[-1]['mismatching']} precise mismatches", flush=True)
    proof = dict(schema=1, producerSha256=sha(Path(__file__)), pin=PIN, entries=entries,
                 originalWordWitnessDetected=True, originalLongControlPassed=True, retirementApplied=False,
                 productionCpuChanged=False, publication=False, roadmapComplete=False)
    if args.validate_only:
        check(load(output / "proof.json") == proof, "Changed aggregate proof")
    else:
        save(output / "proof.json", proof)
    print("SUBA indirect proof SHA-256: " + sha(output / "proof.json"))


if __name__ == "__main__":
    main()
