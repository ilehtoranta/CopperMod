"""Reproduce the isolated short-format-A MOVE write candidate and sole defects.

Requires the qualified MemoryDestinationPrivateV4 parent and a current failing
write-discovery audit. Production CPU and normal outputs are never edited.
"""
import argparse
import importlib.util
import itertools
import os
from pathlib import Path
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parents[1]
DISCOVERY = REPO / "scripts/test-copper68k-move-write-discovery.py"
CANDIDATE = REPO / "scripts/reference/m68020-final-write-candidate.cs"
spec = importlib.util.spec_from_file_location("write_discovery", DISCOVERY)
m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
r = m.read
sha, load, save, require, inventory = r.sha, r.load, r.save, r.require, r.inventory
CORE = "Copper68k/M68kAdvancedTimingInterpreter.cs"
ACCESS = "Copper68k/M68kAdvancedTimingInterpreter.Access020.cs"
OPERAND = "Copper68k/M68kAdvancedTimingInterpreter.Operand020.cs"
NEW = "Copper68k/M68kAdvancedTimingInterpreter.FinalWrite020.cs"
VARIANTS = ["candidate", "source-replay", "source-update", "output-address", "flags", "timing"]
RETENTION = ["FullyQualifiedName~SyntheticM68020OperandReadContinuationTests", "FullyQualifiedName~SyntheticM68020MemoryDestinationRecoveryTests"]


def once(text, old, new):
    require(text.count(old) == 1, "Nonunique candidate witness: " + old[:60])
    return text.replace(old, new)


def sources(parent, variant):
    data = {name: (parent / "source" / name).read_bytes() for name in inventory(parent / "source")}
    text = data[CORE].decode("utf-8").replace("\r\n", "\n")
    for indent, count in [("            ", 1), ("                ", 2)]:
        old = indent + "catch (OperandReadFault020 fault)\n" + indent + "{\n" + indent + "    RaiseOperandReadFault020(fault);\n" + (indent + "    exitBlock = true;\n" if count == 2 else "") + indent + "}\n"
        new = old + indent + "catch (FinalMoveWriteFault020 fault)\n" + indent + "{\n" + indent + "    RaiseFinalMoveWriteFault020(fault);\n" + (indent + "    exitBlock = true;\n" if count == 2 else "") + indent + "}\n"
        require(text.count(old) == count, "Wrong scalar/batch catch witnesses"); text = text.replace(old, new)
    methods = [
        ("ExecuteMoveLongAddressIndirectToAddressIndirect", "WriteLong(State.A[destination], value);", "WriteFinalMoveDestination020(State.A[destination], value, M68kOperandSize.Long);"),
        ("ExecuteMoveByteAddressIndirectToAddressIndirect", "WriteByte(State.A[destination], value);", "WriteFinalMoveDestination020(State.A[destination], value, M68kOperandSize.Byte);"),
        ("ExecuteMoveWordAddressIndirectToAddressIndirect", "WriteWord(State.A[(opcode >> 9) & 7], value);", "WriteFinalMoveDestination020(State.A[(opcode >> 9) & 7], value, M68kOperandSize.Word);"),
        ("ExecuteMovePostIncrementToAddressIndirect", "WriteSized(State.A[(opcode >> 9) & 7], value, size);", "WriteFinalMoveDestination020(State.A[(opcode >> 9) & 7], value, size);")]
    for name, old, new in methods:
        start = text.index("private void " + name + "("); end = text.index("\n        }", start) + 10
        text = text[:start] + once(text[start:end], old, new) + text[end:]
    data[CORE] = text.encode("utf-8")
    text = data[ACCESS].decode("utf-8").replace("\r\n", "\n")
    old = "if (format == 11 && context == OperandReadContext020)\n            return RestoreOperandRead020(frame, sr, pc, ssw);"
    data[ACCESS] = once(text, old, old + "\n        if (format == 10 && context == FinalMoveWriteContext020)\n            return RestoreFinalMoveWrite020(frame, sr, pc, ssw);").encode("utf-8")
    text = data[OPERAND].decode("utf-8").replace("\r\n", "\n")
    old = "else if (destinationMode == 2) { WriteSized(State.A[destination], value, size); SetMoveFlags(value, size); }"
    data[OPERAND] = once(text, old, "else if (destinationMode == 2) { WriteFinalMoveDestination020(State.A[destination], value, size); SetMoveFlags(value, size); }").encode("utf-8")
    text = CANDIDATE.read_text(encoding="utf-8")
    pending = "if ((ssw & 0x100) != 0) WriteFinalMoveDestination020(address, value, size);"
    if variant == "source-replay":
        text = once(text, pending, "if ((ssw & 0x100) != 0) { _ = ReadSized(State.A[opcode & 7], size); WriteFinalMoveDestination020(address, value, size); }")
    elif variant == "source-update":
        text = once(text, pending, "if (((opcode >> 3) & 7) == 3) WriteGeneralRegister(true, opcode & 7, unchecked(State.A[opcode & 7] + M68kIntegerSemantics.AddressIncrement(opcode & 7, size)));\n            " + pending)
    elif variant == "output-address":
        text = once(text, "var value = ReadRteFrameLong(unchecked(frame + 24));", "var value = ReadRteFrameLong(unchecked(frame + 16));")
    elif variant == "flags":
        text = once(text, "if (!_resumingFinalMoveWrite020) SetMoveFlags(value, size);", "// Sole defect: omit completed flags on the original final-write fault.")
    elif variant == "timing":
        text = once(text, "CompleteTiming(FinalMoveTiming020(opcode));", 'CompleteTimingPlan(M68kInstructionPlan.CreateFlat(M68kInstructionTimingKey.GeneralMove, "mutated suffix", 6));')
    data[NEW] = text.encode("utf-8")
    return data


def status(key, variant, fault):
    if not fault or variant == "candidate": return "passing"
    fields = dict(row.split("=", 1) for row in key.split("/")[3:])
    width, vi, ccr, source = [int(fields[x]) for x in ["width", "value", "ccr", "source"]]
    post, alias = fields["post"] == "True", fields["alias"] == "True"
    mask = (1 << (8 * width)) - 1
    value = [0, 0x80818283 & mask, mask >> 1, mask][vi]
    destination = 0x4200 + (2 if width == 1 and source == 7 else width) * post if alias else 0x4400
    flags = 4 if value == 0 else 8 if value & (1 << (width * 8 - 1)) else 0
    mismatch = variant in ["source-replay", "timing"] or variant == "source-update" and post or variant == "output-address" and value != destination & mask or variant == "flags" and (ccr & 15) != flags
    return "mismatching" if mismatch else "passing"


def command(root, variant):
    selected = "|".join([m.FILTER] + (RETENTION if variant == "candidate" else []))
    return ["dotnet", "test", str(root / "source/Copper68k.Tests/Copper68k.Tests.csproj"), "-c", "Release", "--artifacts-path", str(root / "build"),
            "--filter", selected, "--logger", "trx;LogFileName=audit.trx", "--results-directory", str(root)]


def verify(root, variant, data, retained):
    ids = {name: r.hashlib.sha256(value).hexdigest() for name, value in data.items()}
    record = load(root / "execution.json")
    require(record["sources"] == ids == inventory(root / "source"), "Changed complete candidate sources")
    require(record["command"] == command(root, variant) and record["exit"] == (0 if variant == "candidate" else 1), "Wrong command or execution exit")
    outputs = ["execution.log", "audit.trx", "build/bin/Copper68k/release/Copper68k.dll", "build/bin/Copper68k.Tests/release/Copper68k.Tests.dll"]
    require(set(record["outputs"]) == set(outputs) and record["outputs"] == {p: sha(root / p) for p in outputs}, "Changed execution outputs")
    tree = ET.parse(root / "audit.trx"); tests = tree.findall(".//t:UnitTestResult", r.NS)
    names = {"DirectFinalWriteControls(batch: False)": "Passed", "DirectFinalWriteControls(batch: True)": "Passed", "ScalarFinalWriteFaults": "Passed" if variant == "candidate" else "Failed", "BatchFinalWriteFaults": "Passed" if variant == "candidate" else "Failed"}
    if variant == "candidate":
        for name in ["DirectMemoryDestinationControls(batch: False)", "DirectMemoryDestinationControls(batch: True)", "ScalarMemoryDestinationRecovery", "BatchMemoryDestinationRecovery", "LiteralMoveFixturesAndSentinels(batch: False)", "LiteralMoveFixturesAndSentinels(batch: True)", "ScalarOperandReadFaultsRequireFormatB", "BatchOperandReadFaultsRequireFormatB"]: names[name] = "Passed"
    require(len(tests) == len(names) and {t.get("testName").split(".")[-1]:t.get("outcome") for t in tests} == names, "Wrong actual candidate outcomes")
    counters = tree.find(".//t:Counters", r.NS); failed = sum(outcome == "Failed" for outcome in names.values())
    require(int(counters.get("total")) == len(names) and int(counters.get("failed")) == failed and int(counters.get("passed")) == len(names) - failed, "Wrong candidate counters")
    stdout = "\n".join(t.text or "" for t in tree.findall(".//t:UnitTestResult/t:Output/t:StdOut", r.NS))
    totals = dict(passing=0, mismatching=0, unsupported=0, untested=0); rows = []
    for model, kind, mode in itertools.product(r.MODELS, ["controls", "fault"], ["scalar", "batch"]):
        group = f"move-final-write-{kind}-{mode}"; p = root / f"{model}-{group}.json"; d = load(p)
        expected = {key: {status(key, variant, kind == "fault"):weight} for key, weight in m.expected(model).items()}
        counts = {s:sum(row.get(s, 0) for row in expected.values()) for s in totals}
        require(d["model"] == model and d["group"] == group and d["logicalCases"] == 768 and d["counts"] == counts and d["combinations"] == expected, "Wrong candidate keys, weights or results")
        bad = {key for key, row in expected.items() if "mismatching" in row}
        require(len(d["failures"]) == len(bad) and {f["id"] for f in d["failures"]} == bad and all(f["status"] == "mismatching" and f["reason"] for f in d["failures"]), "Wrong mutation witnesses")
        reason = {"source-replay": "Final-write recovery repeated a completed source/update or write", "flags": "Final-write format-A PC/SR/SSW/address/output differs", "timing": "Final-write timing policy differs"}.get(variant)
        if reason: require(all(f["reason"].startswith(reason) for f in d["failures"]), "Wrong mutation cause")
        summary = re.findall(re.escape(model + "/" + group + ": ") + r'(\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=1, combinations=(\d+)', stdout)
        require(len(summary) == 1 and r.json.loads(summary[0][0]) == counts and int(summary[0][1]) == 768 and int(summary[0][2]) == 768, "Candidate TRX/report disagreement")
        for key in totals: totals[key] += counts[key]
        rows.append(dict(path=p.name, sha256=sha(p)))
    if variant == "candidate":
        require(len(retained) == 32, "Missing complete retention selection")
        for name, expected in retained.items():
            p = root / name; d = load(p); require(d == expected, "Changed retained results/keys: " + name)
            summary = re.findall(re.escape(d["model"] + "/" + d["group"] + ": ") + r'(\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=1, combinations=(\d+)', stdout)
            require(len(summary) == 1 and r.json.loads(summary[0][0]) == d["counts"] and int(summary[0][1]) == d["logicalCases"] and int(summary[0][2]) == len(d["combinations"]), "Retained TRX/report disagreement")
            rows.append(dict(path=name, sha256=sha(p)))
    require({p.name for p in root.glob("*.json")} == {row["path"] for row in rows} | {"execution.json"}, "Foreign or missing selected report")
    return dict(variant=variant, counts=totals, retentionCases=143360 if variant == "candidate" else 0, executions=len(names), reports=rows, executionSha256=sha(root / "execution.json"))


def audit(qualified, discovery, output, validate):
    m.audit(qualified, discovery, True)
    producers = {p.relative_to(REPO).as_posix(): sha(p) for p in [Path(__file__), DISCOVERY, m.READ_HELPER, REPO / m.FIXTURE, CANDIDATE]}
    linkage = dict(producers=producers, discoveryVerificationSha256=sha(discovery / "verification.json"), qualifiedParentProofSha256=sha(qualified / "proof.json"), variants=VARIANTS)
    if not validate:
        output.mkdir(parents=True, exist_ok=False); save(output / "inputs.json", linkage)
    require(load(output / "inputs.json") == linkage, "Wrong producer, parent or complete selection")
    retained = {p.name:load(p) for p in (qualified / "candidate").glob("*.json") if p.name.startswith(tuple(model + "-" for model in r.MODELS))}
    entries = []
    for variant in VARIANTS:
        root = output / variant; data = sources(discovery, variant)
        if not validate:
            (root / "source").mkdir(parents=True)
            for name, value in data.items():
                p = root / "source" / name; p.parent.mkdir(parents=True, exist_ok=True); p.write_bytes(value)
            env = dict(os.environ, COPPER68K_RUN_020_MOVE_WRITE_DISCOVERY="1", COPPER68K_RUN_020_OPERAND_READ_DISCOVERY="1", COPPER68K_RUN_020_MEMORY_DESTINATION_RECOVERY="1", COPPER68K_SYNTHETIC_REPORT_DIR=str(root))
            with (root / "execution.log").open("w", encoding="utf-8") as log: result = subprocess.run(command(root, variant), env=env, stdout=log, stderr=subprocess.STDOUT)
            outputs = ["execution.log", "audit.trx", "build/bin/Copper68k/release/Copper68k.dll", "build/bin/Copper68k.Tests/release/Copper68k.Tests.dll"]
            save(root / "execution.json", dict(command=command(root, variant), exit=result.returncode, sources={name:r.hashlib.sha256(value).hexdigest() for name,value in data.items()}, outputs={p:sha(root/p) for p in outputs}))
        entry = verify(root, variant, data, retained); entries.append(entry); print(variant, entry["counts"], "retention", entry["retentionCases"], flush=True)
    proof = dict(schema=1, inputsSha256=sha(output / "inputs.json"), entries=entries, sourceCount=233, productionImport=False, publication=False, hardwareQualified=False, roadmapComplete=False)
    if validate: require(load(output / "proof.json") == proof, "Changed complete candidate verification")
    else: save(output / "proof.json", proof)
    print("Complete isolated final-write audit", sha(output / "proof.json"), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--qualified-parent-directory", type=Path, required=True)
    parser.add_argument("--discovery-directory", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    audit(args.qualified_parent_directory.resolve(), args.discovery_directory.resolve(), args.output.resolve(), args.validate_only)
