"""Reproduce final-write mapping gaps on the frozen private read-recovery CPU.

Default and validate-only fail the unqualified recovery gate. --discovery-only
records the known failures explicitly; it never promotes this CPU or publishes.
All builds stay in a fresh output directory. The qualified parent is the complete
MemoryDestinationPrivateV4 audit, not a production checkout.
"""
import argparse
import contextlib
import hashlib
import importlib.util
import io
import itertools
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parents[1]
FIXTURE = Path("Copper68k.Tests/Synthetic/SyntheticM68020MoveWriteFaultTests.cs")
PARENT_PROOF = "003bb1a07053f298c395e143afc93d725926bf11712cfb19f2bfac99ac3488fc"
READ_HELPER = REPO / "scripts/test-copper68k-memory-destination-recovery.py"
spec = importlib.util.spec_from_file_location("read_recovery", READ_HELPER)
read = importlib.util.module_from_spec(spec)
spec.loader.exec_module(read)
sha, load, save, require, inventory = read.sha, read.load, read.save, read.require, read.inventory
NS = read.NS
MODELS = read.MODELS
FILTER = "FullyQualifiedName~SyntheticM68020MoveWriteFaultTests"


def expected(model):
    return {f"{model}/MOVE/write/width={width}/bank={bank}/source={source}/post={post}/alias={alias}/value={vi}/ccr={ccr}": 1
            for width, bank, source, post, alias, vi, ccr in itertools.product([1,2,4], ["user","user-M","ISP","MSP"], [0,7], [False,True], [False,True], range(4), [0,31])}


def audit(parent, output, validate):
    require(sha(parent / "proof.json") == PARENT_PROOF, "Wrong qualified private parent proof")
    with contextlib.redirect_stdout(io.StringIO()):
        read.audit(parent.parent / "Operand020CombinedRefaultV1", parent, True)
    parent_source = parent / "candidate/source"
    inputs = inventory(parent_source)
    require(len(inputs) == 231, "Incomplete private parent inventory")
    inputs[FIXTURE.as_posix()] = sha(REPO / FIXTURE)
    producers = {p.relative_to(REPO).as_posix(): sha(p) for p in [Path(__file__), REPO / FIXTURE, READ_HELPER]}
    cmd = ["dotnet", "test", str(output / "source/Copper68k.Tests/Copper68k.Tests.csproj"), "-c", "Release",
           "--artifacts-path", str(output / "build"), "--filter", FILTER, "--logger", "trx;LogFileName=audit.trx", "--results-directory", str(output)]
    outputs = ["execution.log", "audit.trx", "build/bin/Copper68k/release/Copper68k.dll", "build/bin/Copper68k.Tests/release/Copper68k.Tests.dll"]
    if not validate:
        (output / "source").mkdir(parents=True, exist_ok=False)
        for name in inventory(parent_source):
            target = output / "source" / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(parent_source / name, target)
        shutil.copyfile(REPO / FIXTURE, output / "source" / FIXTURE)
        env = dict(os.environ, COPPER68K_RUN_020_MOVE_WRITE_DISCOVERY="1", COPPER68K_SYNTHETIC_REPORT_DIR=str(output))
        with (output / "execution.log").open("w", encoding="utf-8") as log:
            result = subprocess.run(cmd, env=env, stdout=log, stderr=subprocess.STDOUT)
        save(output / "inputs.json", dict(schema=1, sources=inputs, producers=producers, parentProofSha256=PARENT_PROOF,
             command=cmd, exit=result.returncode, outputs={p: sha(output / p) for p in outputs}))
    record = load(output / "inputs.json")
    require(record["producers"] == producers and record["parentProofSha256"] == PARENT_PROOF, "Wrong producer or parent")
    require(record["sources"] == inputs == inventory(output / "source"), "Changed source inventory")
    require(record["command"] == cmd and record["exit"] == 1, "Wrong discovery selection or exit")
    require(set(record["outputs"]) == set(outputs) and record["outputs"] == {p: sha(output / p) for p in outputs}, "Changed execution output")
    tree = ET.parse(output / "audit.trx")
    tests = tree.findall(".//t:UnitTestResult", NS)
    names = {"DirectFinalWriteControls(batch: False)": "Passed", "DirectFinalWriteControls(batch: True)": "Passed",
             "ScalarFinalWriteFaults": "Failed", "BatchFinalWriteFaults": "Failed"}
    require(len(tests) == 4 and {t.get("testName").split(".")[-1]:t.get("outcome") for t in tests} == names, "Wrong actual discovery outcomes")
    counters = tree.find(".//t:Counters", NS)
    require(int(counters.get("total")) == 4 and int(counters.get("passed")) == 2 and int(counters.get("failed")) == 2, "Wrong discovery counters")
    stdout = "\n".join(t.text or "" for t in tree.findall(".//t:UnitTestResult/t:Output/t:StdOut", NS))
    reports = []
    for model, kind, mode in itertools.product(MODELS, ["controls","fault"], ["scalar","batch"]):
        group = f"move-final-write-{kind}-{mode}"
        p = output / f"{model}-{group}.json"; d = load(p)
        status = "mismatching" if kind == "fault" else "passing"
        keys = expected(model)
        counts = {s: 768 if s == status else 0 for s in ["passing","mismatching","unsupported","untested"]}
        require(d["model"] == model and d["group"] == group and d["logicalCases"] == 768 and d["counts"] == counts and
                d["combinations"] == {k:{status:n} for k,n in keys.items()}, "Wrong discovery keys, weights or results")
        failures = d["failures"]
        require(len(failures) == (768 if kind == "fault" else 0) and (kind != "fault" or
                {f["id"] for f in failures} == set(keys) and all(f["status"] == "mismatching" and
                f["reason"] == "Denied destination write bypassed physical map" for f in failures)), "Wrong mapping-gap cause")
        summary = re.findall(re.escape(model + "/" + group + ": ") + r'(\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=1, combinations=(\d+)', stdout)
        require(len(summary) == 1 and json.loads(summary[0][0]) == counts and int(summary[0][1]) == 768 and int(summary[0][2]) == 768, "Actual discovery/report disagreement")
        reports.append(dict(path=p.name, sha256=sha(p)))
    allowed = {r["path"] for r in reports} | {"inputs.json", "verification.json"}
    require({p.name for p in output.glob("*.json")} <= allowed, "Foreign discovery report")
    verification = dict(schema=1, sources=232, passingControls=6144, mismatchingFaults=6144, unsupported=0, untestedSelected=0,
                        executions=4, passed=2, failed=2, unavailable=0, reports=reports, inputsSha256=sha(output / "inputs.json"),
                        recoveryGatePassed=False, architecturalPromotion=False, productionImport=False, publication=False, roadmapComplete=False)
    if validate:
        require(load(output / "verification.json") == verification, "Changed discovery verification")
    else:
        save(output / "verification.json", verification)
    return verification


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--qualified-parent-directory", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--discovery-only", action="store_true")
    args = parser.parse_args()
    result = audit(args.qualified_parent_directory.resolve(), args.output.resolve(), args.validate_only)
    print("Recorded 6144 denied-write mapping mismatches and 6144 passing controls; recovery gate FAILED. No promotion.")
    raise SystemExit(0 if args.discovery_only else 1)
