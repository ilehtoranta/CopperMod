"""Prove SUBA indirect retirement with separate word and long defects.

Python 3, Git and dotnet are required. --output must be fresh unless using
--validate-only. --prepare proves original witnesses before retirement;
--finish-retirement completes that evidence after the exact scoped removal.
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


def original():
    return subprocess.check_output(["git", "show", PIN + ":" + LEGACY], cwd=REPO).decode("utf-8-sig").replace("\r\n", "\n")


def reduced():
    text = original()
    method = text.index("    public void " + WITNESS)
    start = text.rfind("    [Theory]\n", 0, method)
    end = text.index("    [Theory]\n", method)
    check(start >= 0, "Missing retirement boundary")
    return text[:start] + text[end:]


def mutation(path, text, mode):
    if path == "Copper68k/M68kAdvancedTimingInterpreter.cs":
        start = text.index("        private void ExecuteSubaAddressIndirectToAddress(")
        end = text.index("        private void ExecuteSubaPostIncrementToAddress(", start)
        old = "unchecked((uint)(int)(short)ReadWord(State.A[opcode & 7]))" if mode == "ZeroExtend" else "isLong ? ReadLong(State.A[opcode & 7])"
        block = text[start:end]
        check(block.count(old) == 1, "Missing advanced mutation target")
        new = "unchecked((uint)ReadWord(State.A[opcode & 7]))" if mode == "ZeroExtend" else "isLong ? (uint)ReadWord(State.A[opcode & 7])"
        return text[:start] + block.replace(old, new) + text[end:]
    old = "SetAddressRegister(reg, State.A[reg] - M68kCpuState.SignExtend(value, size));"
    check(text.count(old) == 1, "Missing base mutation target")
    new = "mode == 2 && size == M68kOperandSize.Word ? value & 0xffffu : M68kCpuState.SignExtend(value, size)" if mode == "ZeroExtend" else "mode == 2 && size == M68kOperandSize.Long ? value >> 16 : M68kCpuState.SignExtend(value, size)"
    return text.replace(old, "SetAddressRegister(reg, State.A[reg] - (" + new + "));")


def sources(mode):
    paths = subprocess.check_output(["git", "ls-files", "Copper68k", "Copper68k.Tests"], cwd=REPO, text=True).splitlines()
    paths = sorted(set(paths + [FIXTURE]))
    result = {p: (REPO / p).read_bytes() for p in paths if Path(p).suffix in [".cs", ".csproj"]}
    current = result[LEGACY].decode("utf-8-sig").replace("\r\n", "\n")
    check(current in [original(), reduced()], "Wrong legacy retirement scope")
    if mode == "Current":
        check(current == reduced(), "Retirement not applied")
    else:
        result[LEGACY] = original().encode()
    if mode in ["ZeroExtend", "LongWordRead"]:
        for p in ["Copper68k/M68kCore.cs", "Copper68k/M68kAdvancedTimingInterpreter.cs"]:
            result[p] = mutation(p, result[p].decode("utf-8-sig"), mode).encode()
    return result


def inventory(source):
    return {p.relative_to(source).as_posix(): sha(p) for p in source.rglob("*")
            if p.suffix in [".cs", ".csproj"] and not {"bin", "obj"}.intersection(p.parts)}


def protected():
    paths = ["Copper68k/bin/Release/net10.0/Copper68k.dll", "Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll"]
    return {p: sha(REPO / p) for p in paths if (REPO / p).exists()}


def command(root, mode):
    selection = "M68020AddressSourceTests" if mode in ["Clean", "Current"] else WITNESS
    return ["dotnet", "test", str(root / "source/Copper68k.Tests/Copper68k.Tests.csproj"), "-c", "Release",
            "--artifacts-path", str(root / "build"), "--filter", f"FullyQualifiedName~{METHOD}|FullyQualifiedName~{selection}",
            "--logger", "trx;LogFileName=audit.trx", "--results-directory", str(root)]


def execute(root, mode):
    for p, content in sources(mode).items():
        target = root / "source" / p
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(content)
    args = command(root, mode)
    record = dict(mode=mode, pin=PIN, producer=sha(Path(__file__)), sources=inventory(root / "source"), protected=protected(), command=args)
    env = dict(os.environ, COPPER68K_SYNTHETIC_MODELS=",".join(MODELS), COPPER68K_SYNTHETIC_REPORT_DIR=str(root))
    selection = "the original class" if mode == "Clean" else "the reduced class" if mode == "Current" else "two historical witnesses"
    print(mode + ": executing eight shared profiles and " + selection, flush=True)
    with (root / "execution.log").open("w", encoding="utf-8") as log:
        record["testExit"] = subprocess.run(args, env=env, cwd=REPO, stdout=log, stderr=subprocess.STDOUT).returncode
    record["logSha256"] = sha(root / "execution.log")
    save(root / "inputs.json", record)


def expected(model, mode):
    combinations, failures = {}, []
    for width, reg, supervisor in itertools.product([2, 4], [0, 3, 7], [False, True]):
        key = f"{model}/SUBA/{width}/(A3)/r{reg}/indirect-sign-alias-all-CCR/brief/super={supervisor}"
        bad = mode == "ZeroExtend" and width == 2 or mode == "LongWordRead" and width == 4
        missed = 64 if mode == "ZeroExtend" else 96
        combinations[key] = dict(passing=128 - missed, mismatching=missed) if bad else dict(passing=128)
        if bad:
            destination = 0x3000 if reg == 3 else 0x1000
            values = [0xfffe, 0x8000] if mode == "ZeroExtend" else [2, 0x80000000, 0x7fffffff]
            for value, ccr in itertools.product(values, range(32)):
                rhs = value - 0x10000 if mode == "ZeroExtend" else value
                wanted = (destination - rhs) & 0xffffffff
                actual = (destination - (value if mode == "ZeroExtend" else value >> 16)) & 0xffffffff
                opcode = (0x90d3 if width == 2 else 0x91d3) | reg << 9
                failures.append(dict(id=f"{key}/op={opcode:04X}/s={value:08X}/d={destination:08X}/ccr={ccr:02X}",
                                     status="mismatching", reason=f"A{reg} expected {wanted:08X}, actual {actual:08X}"))
    return combinations, failures


def legacy_names():
    text = original()
    names = {}
    for match in re.finditer(r"public void (\w+)\(([^)]*)\)", text):
        method, signature = match.groups()
        start = max(text.rfind("[Theory]", 0, match.start()), text.rfind("[Fact]", 0, match.start()))
        attributes = text[start:match.start()]
        parameters = [p.strip().split()[-1] for p in signature.split(",")] if signature else []
        rows = re.findall(r"\[InlineData\(([^)]*)\)\]", attributes) if parameters else [""]
        check(bool(rows), "Missing historical rows")
        for row in rows:
            values = []
            for value in row.split(",") if row else []:
                value = value.strip()
                if value in ["true", "false"]:
                    values.append(value.title())
                elif value.startswith("M68kCpuModel."):
                    values.append(value.split(".")[-1])
                else:
                    values.append(str(int(value.rstrip("uU"), 0)))
            check(len(values) == len(parameters), "Wrong historical parameters")
            suffix = "(" + ", ".join(f"{p}: {v}" for p, v in zip(parameters, values)) + ")" if parameters else ""
            key = "Copper68k.Tests.M68020AddressSourceTests." + method + suffix
            check(key not in names, "Duplicate historical row")
            names[key] = "Passed"
    check(len(names) > 30, "Incomplete historical class")
    return names


def verify(root, mode):
    record = load(root / "inputs.json")
    check(record["mode"] == mode and record["pin"] == PIN and record["producer"] == sha(Path(__file__)), "Wrong producer or scope")
    wanted = {p: hashlib.sha256(v).hexdigest() for p, v in sources(mode).items()}
    check(record["sources"] == wanted, "Wrong baseline or mutation identity")
    check(inventory(root / "source") == wanted, "Incomplete or changed source inventory")
    check(record["protected"] == protected(), "Changed protected assemblies")
    check(record["command"] == command(root, mode), "Wrong execution command")
    mutated = mode in ["ZeroExtend", "LongWordRead"]
    check(record["testExit"] == (1 if mutated else 0) and record["logSha256"] == sha(root / "execution.log"), "Wrong execution or log")
    tree = ET.parse(root / "audit.trx")
    tests = tree.findall(".//t:UnitTestResult", NS)
    names = {f'Copper68k.Tests.Synthetic.SyntheticSubaIndirectTests.{METHOD}(modelId: "{m}")': "Failed" if mutated else "Passed" for m in MODELS}
    prefix = "Copper68k.Tests.M68020AddressSourceTests." + WITNESS
    word = prefix + "(opcode: 37075, hi: 65534, lo: 0, expected: 4098)"
    names[word] = "Failed" if mode == "ZeroExtend" else "Passed"
    long = prefix + "(opcode: 37331, hi: 0, lo: 2, expected: 4094)"
    names[long] = "Failed" if mode == "LongWordRead" else "Passed"
    if mode in ["Clean", "Current"]:
        names.update(legacy_names())
        if mode == "Current":
            names.pop(word)
            names.pop(long)
    check(len(tests) == len(names) and {t.get("testName") for t in tests} == set(names), "Wrong or empty execution selection")
    check(all(t.get("outcome") == names[t.get("testName")] for t in tests), "Wrong execution outcomes")
    if mode == "ZeroExtend":
        message = next(t for t in tests if t.get("testName") == word).find("t:Output/t:ErrorInfo/t:Message", NS).text
        check(re.search(r"Expected:\s*4098\b", message) and re.search(r"Actual:\s*4294905858\b", message), "Wrong historical failure reason")
    if mode == "LongWordRead":
        message = next(t for t in tests if t.get("testName") == long).find("t:Output/t:ErrorInfo/t:Message", NS).text
        check(re.search(r"Expected:\s*4094\b", message) and re.search(r"Actual:\s*4096\b", message), "Wrong historical failure reason")
    counters = tree.find("t:ResultSummary/t:Counters", NS)
    failed = sum(v == "Failed" for v in names.values())
    check(counters is not None and all(int(counters.get(k, -1)) == v for k, v in dict(total=len(names), executed=len(names), passed=len(names)-failed, failed=failed, notExecuted=0).items()), "Wrong TRX counters")
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
    missed = 3072 if mode == "ZeroExtend" else 4608 if mode == "LongWordRead" else 0
    return dict(mode=mode, passing=12288 - missed, mismatching=missed,
                executions=len(names), reports=reports, inputsSha256=sha(root / "inputs.json"), trxSha256=sha(root / "audit.trx"))


def integrity(output, validate_only):
    definitions = [("MissingFixture", "Incomplete or changed source inventory"),
                   ("WrongProducer", "Wrong producer or scope"),
                   ("EmptySelection", "Wrong or empty execution selection"),
                   ("MissingHistoricalWitness", "Wrong or empty execution selection"),
                   ("WrongWeight", "Wrong coverage keys or weights"),
                   ("WrongFailureReason", "Wrong failure IDs or reasons"),
                   ("ChangedMutationAndManifest", "Wrong baseline or mutation identity"),
                   ("MissingProtection", "Changed protected assemblies"),
                   ("WrongCommand", "Wrong execution command"),
                   ("WrongCounters", "Wrong TRX counters")]
    records = []
    for name, reason in definitions:
        target = output / "integrity" / name
        if not validate_only:
            parent = output / "LongWordRead"
            shutil.copytree(parent / "source", target / "source", ignore=shutil.ignore_patterns("bin", "obj"))
            for path in [parent / "inputs.json", parent / "execution.log", parent / "audit.trx", *parent.glob(f"*-{GROUP}.json")]:
                shutil.copyfile(path, target / path.name)
            data = load(target / "inputs.json")
            data["command"] = command(target, "LongWordRead")
            if name == "MissingFixture":
                path = target / "source" / FIXTURE
                check(path.resolve().is_relative_to(target.resolve()), "Unsafe integrity path")
                path.unlink()
            elif name == "WrongProducer":
                data["producer"] = "0" * 64
            elif name == "MissingProtection":
                data["protected"]["absent-protected-assembly"] = "0" * 64
            elif name == "WrongCommand":
                data["command"][data["command"].index("--filter") + 1] = "FullyQualifiedName~Nothing"
            elif name == "ChangedMutationAndManifest":
                path = target / "source/Copper68k/M68kCore.cs"
                path.write_text(path.read_text(encoding="utf-8-sig") + "\n// sole evidence defect\n", encoding="utf-8")
                data["sources"]["Copper68k/M68kCore.cs"] = sha(path)
            elif name in ["EmptySelection", "MissingHistoricalWitness", "WrongCounters"]:
                tree = ET.parse(target / "audit.trx")
                if name == "WrongCounters":
                    tree.find("t:ResultSummary/t:Counters", NS).set("failed", "0")
                else:
                    results = tree.find("t:Results", NS)
                    for test in list(results):
                        if name == "EmptySelection" or WITNESS in test.get("testName", ""):
                            results.remove(test)
                tree.write(target / "audit.trx")
            else:
                path = target / f"68020-{GROUP}.json"
                report = load(path)
                if name == "WrongWeight":
                    report["combinations"][next(iter(report["combinations"]))]["passing"] += 1
                else:
                    report["failures"][0]["reason"] = "unrelated failure"
                save(path, report)
            save(target / "inputs.json", data)
        try:
            verify(target, "LongWordRead")
        except ValueError as error:
            check(str(error) == reason, "Wrong integrity rejection: " + name + ": " + str(error))
            records.append(dict(name=name, reason=reason))
        else:
            raise ValueError("Corrupted evidence accepted: " + name)
    return records


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--prepare", action="store_true")
    parser.add_argument("--finish-retirement", action="store_true")
    args = parser.parse_args()
    output = args.output.resolve()
    check(sum([args.validate_only, args.prepare, args.finish_retirement]) <= 1, "Conflicting audit modes")
    if not args.prepare:
        check((REPO / LEGACY).read_text(encoding="utf-8-sig") == reduced(), "Retirement not applied exactly")
    if not args.validate_only and not args.finish_retirement:
        check(not output.exists(), "Use fresh audit output")
        output.mkdir(parents=True)
    entries = []
    for mode in ["Clean", "ZeroExtend", "LongWordRead"] + ([] if args.prepare else ["Current"]):
        root = output / mode
        if not args.validate_only and (not args.finish_retirement or mode == "Current"):
            root.mkdir()
            execute(root, mode)
        entries.append(verify(root, mode))
        print(f"{mode}: {entries[-1]['passing']} passing / {entries[-1]['mismatching']} precise mismatches", flush=True)
    proof = dict(schema=2, producerSha256=sha(Path(__file__)), pin=PIN, entries=entries,
                 originalWordWitnessDetected=True, originalLongWitnessDetected=True,
                 integrity=integrity(output, args.validate_only or args.finish_retirement), retirementApplied=not args.prepare,
                 productionCpuChanged=False, publication=False, roadmapComplete=False)
    if args.validate_only:
        check(load(output / "proof.json") == proof, "Changed aggregate proof")
    else:
        save(output / ("preparation-proof.json" if args.prepare else "proof.json"), proof)
    proof_path = output / ("preparation-proof.json" if args.prepare else "proof.json")
    print("SUBA indirect proof SHA-256: " + sha(proof_path))


if __name__ == "__main__":
    main()
