"""Isolated MOVE.L source-fault suffix audit; never edits or packs production CPU.

Requires the frozen 230-file Operand020CombinedRefaultV1 private parent.
The candidate patch below is development evidence, not a production import.
"""
from pathlib import Path
import argparse
import hashlib
import itertools
import json
import os
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parents[1]
FIXTURE = Path("Copper68k.Tests/Synthetic/SyntheticM68020MemoryDestinationRecoveryTests.cs")
CPU = Path("Copper68k/M68kAdvancedTimingInterpreter.Operand020.cs")
PARENT_MANIFEST = "943288fe579f746468f9df7ecdfd11833e2862e9db058039476aa8768aa61a9a"
PARENT_CPU = "b93d2ef4b2801b544313c1efad29d4cc47254f7a38897ee1a7b61a3f7e556a0d"
MODELS = ["68EC020", "68020", "68030", "A1200"]
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
FILTER = "FullyQualifiedName~SyntheticM68020MemoryDestinationRecoveryTests"
RETENTION = "FullyQualifiedName~SyntheticM68020OperandReadContinuationTests"
VARIANTS = ["baseline", "candidate", "replay", "alias", "flags", "timing"]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def require(condition, message):
    if not condition:
        raise ValueError(message)


def inventory(root):
    return {p.relative_to(root).as_posix(): sha(p) for p in sorted(root.rglob("*"))
            if p.suffix in (".cs", ".csproj") and not {"bin", "obj"}.intersection(p.parts)}


def replace_once(text, old, new):
    require(text.count(old) == 1, "Nonunique candidate patch witness: " + old[:70])
    return text.replace(old, new)


def candidate(original, variant):
    if variant == "baseline":
        return original
    text = replace_once(original,
        "var moveSource = top is >= 1 and <= 3 && destination <= 1 && !(top == 1 && destination == 1) &&",
        "var memoryDestination = top == 2 && destination == 2 && mode is 2 or 3;\n"
        "        var moveSource = top is >= 1 and <= 3 && (destination <= 1 || memoryDestination) && !(top == 1 && destination == 1) &&")
    text = replace_once(text,
        "kind == PendingReadKind020.MoveSource && (top is < 1 or > 3 || destinationMode > 1 || top == 1 && destinationMode == 1 ||",
        "kind == PendingReadKind020.MoveSource && (top is < 1 or > 3 ||\n"
        "            destinationMode > 1 && !(top == 2 && destinationMode == 2 && mode is 2 or 3) || top == 1 && destinationMode == 1 ||")
    text = replace_once(text,
        "else WriteGeneralRegister(true, destination, expectedWidth == 2 ? unchecked((uint)(int)(short)value) : value);",
        "else if (destinationMode == 2) { WriteSized(State.A[destination], value, size); SetMoveFlags(value, size); }\n"
        "        else WriteGeneralRegister(true, destination, expectedWidth == 2 ? unchecked((uint)(int)(short)value) : value);")
    timing = "CompleteTiming(mode == 2 ? M68kInstructionTimingKey.MoveLongAddressIndirectToAddressIndirect : M68kInstructionTimingKey.MoveLongPostIncrementToAddressIndirect);"
    text = replace_once(text,
        "if (indexed && indexedTimingKey != M68kInstructionTimingKey.GeneralMove)",
        "if (destinationMode == 2)\n            " + timing + "\n        else if (indexed && indexedTimingKey != M68kInstructionTimingKey.GeneralMove)")
    if variant == "replay":
        text = replace_once(text, "var destination = (opcode >> 9) & 7;",
            "if (destinationMode == 2) _ = ReadPendingMoveSource020(address, size);\n        var destination = (opcode >> 9) & 7;")
    elif variant == "alias":
        text = replace_once(text, "WriteSized(State.A[destination], value, size);",
            "WriteSized(destination == register && mode == 3 ? address : State.A[destination], value, size);")
    elif variant == "flags":
        text = replace_once(text,
            "else if (destinationMode == 2) { WriteSized(State.A[destination], value, size); SetMoveFlags(value, size); }",
            "else if (destinationMode == 2) { WriteSized(State.A[destination], value, size); }")
    elif variant == "timing":
        text = replace_once(text, timing,
            'CompleteTimingPlan(M68kInstructionPlan.CreateFlat(M68kInstructionTimingKey.GeneralMove, "mutated suffix", 6));')
    return text


def expected_memory(model, group, variant):
    fault = "recovery" in group
    expected = {}
    for post, alias, bank, value, ccr, lane in itertools.product(
            [False, True], [False, True], ["user", "user-M", "ISP", "MSP"],
            [0, 0x80818283, 0x7fffffff, 0xffffffff], range(32), range(4) if fault else [0]):
        key = f"{model}/MOVE.L/source={'post' if post else 'indirect'}/dest={'alias-A0' if alias else 'A1'}/bank={bank}/value={value:08X}/ccr={ccr:02X}/lane={lane}"
        status = "passing"
        if fault and variant == "baseline":
            status = "unsupported"
        elif fault and (variant in ("replay", "timing") or variant == "alias" and alias and post):
            status = "mismatching"
        elif fault and variant == "flags" and (ccr & 15) != (4 if value == 0 else 8 if value & 0x80000000 else 0):
            status = "mismatching"
        expected[key] = {status: 1}
    return expected


def command(root, variant):
    selected = FILTER + ("|" + RETENTION if variant == "candidate" else "")
    return ["dotnet", "test", str(root / "source/Copper68k.Tests/Copper68k.Tests.csproj"), "-c", "Release",
            "--artifacts-path", str(root / "build"), "--filter", selected,
            "--logger", "trx;LogFileName=audit.trx", "--results-directory", str(root)]


def verify_run(root, variant, expected_sources):
    record = load(root / "execution.json")
    require(record["command"] == command(root, variant), "Wrong exact command")
    require(record["sources"] == expected_sources == inventory(root / "source"), "Changed complete source inventory")
    require(record["exit"] == (0 if variant == "candidate" else 1), "Wrong expected execution exit")
    outputs = ["execution.log", "audit.trx", "build/bin/Copper68k/release/Copper68k.dll", "build/bin/Copper68k.Tests/release/Copper68k.Tests.dll"]
    require(set(record["outputs"]) == set(outputs) and record["outputs"] == {p: sha(root / p) for p in outputs}, "Changed execution output identity")
    tree = ET.parse(root / "audit.trx")
    tests = tree.findall(".//t:UnitTestResult", NS)
    names = {"DirectMemoryDestinationControls(batch: False)", "DirectMemoryDestinationControls(batch: True)",
             "ScalarMemoryDestinationRecovery", "BatchMemoryDestinationRecovery"}
    if variant == "candidate":
        names |= {"LiteralMoveFixturesAndSentinels(batch: False)", "LiteralMoveFixturesAndSentinels(batch: True)",
                  "ScalarOperandReadFaultsRequireFormatB", "BatchOperandReadFaultsRequireFormatB"}
    actual_names = {t.get("testName").split(".")[-1] for t in tests}
    require(len(tests) == len(names) and actual_names == names, "Wrong or empty actual test selection")
    expected_failures = 0 if variant == "candidate" else 2
    require(sum(t.get("outcome") == "Failed" for t in tests) == expected_failures and
            all(t.get("outcome") in ("Passed", "Failed") for t in tests), "Wrong actual test outcomes")
    counters = tree.find(".//t:Counters", NS)
    require(int(counters.get("total")) == len(names) and int(counters.get("failed")) == expected_failures and
            int(counters.get("passed")) == len(names) - expected_failures, "Wrong TRX counters")
    stdout = "\n".join(t.text or "" for t in tree.findall(".//t:UnitTestResult/t:Output/t:StdOut", NS))
    totals = dict(passing=0, mismatching=0, unsupported=0, untested=0)
    reports = []
    for model, kind, mode in itertools.product(MODELS, ["controls", "recovery"], ["scalar", "batch"]):
        group = f"memory-destination-{kind}-{mode}"
        p = root / f"{model}-{group}.json"
        d = load(p)
        expected = expected_memory(model, group, variant)
        counts = {s: sum(row.get(s, 0) for row in expected.values()) for s in totals}
        require(d["model"] == model and d["group"] == group and d["combinations"] == expected and
                d["counts"] == counts and d["logicalCases"] == len(expected) and d["xunitBatches"] == 1,
                "Wrong keys, weights or results: " + p.name)
        bad = {k for k, row in expected.items() if "passing" not in row}
        require(len(d["failures"]) == len(bad) and {f["id"] for f in d["failures"]} == bad and
                all(f["status"] in expected[f["id"]] and f["reason"] for f in d["failures"]), "Missing failure witnesses")
        reason = {"replay": "Source read/destination write address, order, width or count differs",
                  "timing": "Memory destination changed the existing MOVE timing policy"}.get(variant)
        if reason:
            require(all(f["reason"].startswith(reason) for f in d["failures"]), "Wrong mutation failure cause")
        summary = re.findall(re.escape(model + "/" + group + ": ") + r'(\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=1, combinations=(\d+)', stdout)
        require(len(summary) == 1 and json.loads(summary[0][0]) == counts and
                int(summary[0][1]) == len(expected) and int(summary[0][2]) == len(expected), "TRX/report disagreement")
        for key in totals:
            totals[key] += counts[key]
        reports.append({"path": p.name, "sha256": sha(p)})
    if variant == "candidate":
        for p in root.glob("*-operand-read-resume-*.json"):
            d = load(p)
            count = 5376 if "discovery" in d["group"] else 2304
            fault = "discovery" in d["group"]
            expected = {}
            for form, width, bank in itertools.product(["indirect", "postincrement", "predecrement", "displacement", "absolute-word", "absolute-long"], [1, 2, 4], ["user", "user-M", "ISP", "MSP"]):
                for lane in range(width) if fault else [0]:
                    key = f"{d['model']}/MOVE/read-{'fault' if fault else 'control'}/form={form}/size={width}/bank={bank}/byte={lane}"
                    expected[key] = {"passing": 32}
            require(d["logicalCases"] == count and d["counts"] == dict(passing=count, mismatching=0, unsupported=0, untested=0)
                    and d["combinations"] == expected and not d["failures"], "Register-destination retention failed")
            summary = re.findall(re.escape(d["model"] + "/" + d["group"] + ": ") + r'(\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=1, combinations=(\d+)', stdout)
            require(len(summary) == 1 and json.loads(summary[0][0]) == d["counts"] and int(summary[0][1]) == count and int(summary[0][2]) == len(expected), "Retention TRX/report disagreement")
            reports.append({"path": p.name, "sha256": sha(p)})
        require(len(reports) == 32, "Missing register-destination retention report")
    require(set(p.name for p in root.glob("*.json")) == {r["path"] for r in reports} | {"execution.json"}, "Foreign or missing report")
    return dict(variant=variant, counts=totals, reports=reports, executions=len(names),
                retentionCases=61440 if variant == "candidate" else 0, executionSha256=sha(root / "execution.json"))


def audit(parent, output, validate):
    require(sha(parent / "base-inputs.json") == PARENT_MANIFEST, "Wrong frozen parent manifest")
    base = load(parent / "base-inputs.json")
    parent_sources = {row["path"].replace("\\", "/"): row["sha256"] for row in base["sources"]}
    require(len(parent_sources) == len(base["sources"]) == 230 and inventory(parent / "source") == parent_sources,
            "Incomplete or changed frozen parent source")
    require(sha(parent / "source" / CPU) == PARENT_CPU, "Wrong private CPU parent")
    original = (parent / "source" / CPU).read_text(encoding="utf-8")
    producers = {str(Path(__file__).relative_to(REPO).as_posix()): sha(Path(__file__)), FIXTURE.as_posix(): sha(REPO / FIXTURE)}
    if validate:
        manifest = load(output / "inputs.json")
        require(manifest["producers"] == producers and manifest["parentManifestSha256"] == PARENT_MANIFEST,
                "Wrong producer or parent identity")
    else:
        output.mkdir(parents=True, exist_ok=False)
        save(output / "inputs.json", dict(schema=1, parentManifestSha256=PARENT_MANIFEST, producers=producers, variants=VARIANTS))
    require(load(output / "inputs.json")["variants"] == VARIANTS, "Changed requested selection")
    entries = []
    for variant in VARIANTS:
        root = output / variant
        expected_sources = dict(parent_sources)
        expected_sources[FIXTURE.as_posix()] = sha(REPO / FIXTURE)
        patch = candidate(original, variant)
        patch_bytes = patch.encode("utf-8")
        # Normalize only the one explicitly patched private file; all others byte-identical.
        expected_sources[CPU.as_posix()] = hashlib.sha256(patch_bytes).hexdigest() if variant != "baseline" else PARENT_CPU
        if not validate:
            (root / "source").mkdir(parents=True)
            for filename in parent_sources:
                target = root / "source" / filename
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(parent / "source" / filename, target)
            shutil.copyfile(REPO / FIXTURE, root / "source" / FIXTURE)
            if variant != "baseline":
                (root / "source" / CPU).write_bytes(patch_bytes)
            env = dict(os.environ, COPPER68K_RUN_020_MEMORY_DESTINATION_RECOVERY="1",
                       COPPER68K_RUN_020_OPERAND_READ_DISCOVERY="1", COPPER68K_SYNTHETIC_REPORT_DIR=str(root))
            with (root / "execution.log").open("w", encoding="utf-8") as log:
                result = subprocess.run(command(root, variant), env=env, stdout=log, stderr=subprocess.STDOUT, check=False)
            outputs = ["execution.log", "audit.trx", "build/bin/Copper68k/release/Copper68k.dll", "build/bin/Copper68k.Tests/release/Copper68k.Tests.dll"]
            save(root / "execution.json", dict(command=command(root, variant), exit=result.returncode,
                 sources=inventory(root / "source"), outputs={p: sha(root / p) for p in outputs}))
        entry = verify_run(root, variant, expected_sources)
        entries.append(entry)
        print(variant, entry["counts"], "retention", entry["retentionCases"], flush=True)
    proof = dict(schema=1, inputsSha256=sha(output / "inputs.json"), entries=entries,
                 productionImport=False, publication=False, hardwareQualified=False, roadmapComplete=False)
    if validate:
        require(load(output / "proof.json") == proof, "Changed aggregate verification")
    else:
        save(output / "proof.json", proof)
    print("Strict isolated recovery audit passed", sha(output / "proof.json"), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--parent-directory", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    audit(args.parent_directory.resolve(), args.output.resolve(), args.validate_only)
