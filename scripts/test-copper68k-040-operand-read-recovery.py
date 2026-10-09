"""Execute/replay first-read fault recovery and persistent test-bus repair."""
import argparse
import hashlib
import itertools
import json
import os
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parent.parent
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
PREFIX = "Copper68k.Tests.Synthetic."
CLASSES = ["SyntheticM68040OperandReadFaultDiscoveryTests", "SyntheticM68040OperandReadRecoveryTests",
           "SyntheticM68040OperandReadMappingRepairTests"]
FLAGS = ["COPPER68K_RUN_040_OPERAND_READ_" + suffix for suffix in ["DISCOVERY", "RECOVERY", "MAPPING_REPAIR"]]
GROUPS = [
    ("operand-read-recovery-reference", 256, True),
    ("operand-read-mapping-reference", 320, True),
    ("operand-read-recovery-matrix", 81920, False),
    ("operand-read-mapping-matrix", 102400, False),
    ("operand-read-fault-discovery", 61440, False)]
FORMS = [("MOVE", 1), ("MOVE", 2), ("MOVE", 4), ("MOVEA", 2), ("MOVEA", 4),
         ("ADD", 1), ("ADD", 2), ("ADD", 4), ("CMP", 1), ("CMP", 2), ("CMP", 4),
         ("TST", 1), ("TST", 2), ("TST", 4), ("MOVEM", 2), ("MOVEM", 4)]
BANKS = ("user", "user-M", "ISP", "MSP")


def named_tests(selection):
    groups(selection)  # Reject unknown selections before creating output.
    old, recovery, mapping = [PREFIX + c + "." for c in CLASSES]
    names = {old + "FixedManualReadInstructionWitnessesValidateEncodings",
             mapping + "PersistentMappingAndLiteralRepairProgramHaveFixedReferenceExamples"}
    names.update(old + f"ActualMovemFaultRetainsCalculatedEaAcrossLoadedBaseAndIndex(full: {v})"
                 for v in ("False", "True"))
    for cl, method in [(recovery, "FirstReadFaultCompletesAfterExplicitHandlerReturn"),
                       (mapping, "HandlerReallyRepairsPersistentMappingBeforeReturning")]:
        names.update(cl + method + f"(batch: {v})" for v in ("False", "True"))
    if selection == "complete":
        names.update(old + m for m in ("ScalarReadFaultsRequireFormat7", "BatchReadFaultsRequireFormat7"))
        names.update(cl + "EveryCcrLaneAndFaultByte" + route
                     for cl in (recovery, mapping) for route in ("Scalar", "Batch"))
    return names


def check(value, message):
    if not value:
        raise ValueError(message)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def normalized(path):
    return hashlib.sha256(path.read_bytes().replace(b"\r\n", b"\n")).hexdigest()


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8", newline="\n")


def source_names():
    names = subprocess.check_output(["git", "ls-files", "Copper68k", "Copper68k.Tests"], cwd=REPO, text=True).splitlines()
    names = sorted(n for n in names if Path(n).suffix in [".cs", ".csproj"])
    check(len(names) == len(set(names)) > 0 and any(n.startswith("Copper68k/") for n in names), "Empty/duplicate source selection")
    return names


def groups(selection):
    check(selection in ["controls", "complete"], "Unknown audit selection")
    return [g for g in GROUPS if selection == "complete" or g[2]]


def command(root, selection):
    groups(selection)
    # Select only required methods in controls; optional EnvironmentFacts must
    # not appear as skipped rows or silently inflate the selected inventory.
    filter_value = "|".join("FullyQualifiedName~" + n.split("(")[0]
                            for n in sorted({n for n in named_tests(selection)}))
    return ["dotnet", "test", str(root / "source/Copper68k.Tests/Copper68k.Tests.csproj"), "-c", "Release",
            "--artifacts-path", str(root / "build"), "--filter", filter_value,
            "--logger", "trx;LogFileName=audit.trx", "--results-directory", str(root)]


def expected_keys(group):
    """Independent literal fixture inventory, with CCR/phase preserved as keys."""
    entry = group == "operand-read-fault-discovery"
    generated = entry or group.endswith("-matrix")
    cohort = group.removeprefix("operand-read-")
    if cohort.startswith("recovery-"):
        cohort = cohort.removeprefix("recovery-")
    phases = ("fault-entry", "mapping-repair", "handler-RTE", "completed-read", "following-MOVEQ") if group.startswith("operand-read-mapping-") else (
        "fault-entry", "handler-RTE", "completed-read", "following-MOVEQ")
    for family, width in FORMS:
        for bank, trace, ccr, lane in itertools.product(
                BANKS, (0, 0x8000, 0x4000) if entry else (0,),
                (31,) if entry or not generated else range(32),
                range(4) if generated else (0,)):
            for byte in range(width) if generated else (width - 1,):
                base = f"68040/{family}/operand-read-"
                if entry:
                    yield base + f"fault/size={width}/bank={bank}/T={trace:04X}/lane={lane}/byte={byte}"
                else:
                    for phase in phases:
                        yield base + f"recovery/{cohort}/size={width}/bank={bank}/T={trace:04X}/lane={lane}/byte={byte}/ccr={ccr:02X}/phase={phase}"


def execute(root, selection):
    check(not root.exists(), "Use a fresh output directory; evidence is never overwritten")
    names = source_names()
    sources = {n: sha(REPO / n) for n in names}
    assets = {"Copper68k/" + n: sha(REPO / "Copper68k" / n) for n in ["README.md", "copper68k-icon.png"]}
    for name in [*sources, *assets]:
        target = root / "source" / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes((REPO / name).read_bytes())
    settings = dict(COPPER68K_SYNTHETIC_REPORT_DIR=str(root))
    if selection == "complete":
        settings.update({n: "1" for n in FLAGS})
    inputs = dict(schema=1, profile="040-operand-read-recovery", selection=selection, executedRoot=str(root),
                  sources=sources, assets=assets, normalizedSources={n: normalized(REPO / n) for n in names},
                  settings=settings, command=command(root, selection), producerSha256=sha(Path(__file__)))
    save(root / "inputs.json", inputs)
    environment = {k: v for k, v in os.environ.items() if not k.startswith("COPPER68K_")}
    environment.update(settings)
    with (root / "execution.log").open("w", encoding="utf-8") as log:
        result = subprocess.run(inputs["command"], env=environment, stdout=log, stderr=subprocess.STDOUT)
    execution = dict(exit=result.returncode, inputsSha256=sha(root / "inputs.json"),
                     outputs={n: sha(root / n) for n in ["execution.log", "audit.trx"] if (root / n).exists()},
                     assemblies={f.relative_to(root).as_posix(): sha(f) for f in (root / "build/bin").rglob("Copper68k*.dll")},
                     sourcesUnchanged=all(sha(root / "source" / n) == sha(REPO / n) == h for n, h in sources.items()))
    save(root / "execution.json", execution)
    check(result.returncode == 0, "Execution failed; inspect execution.log")


def validate(root, selection):
    inputs, execution = load(root / "inputs.json"), load(root / "execution.json")
    check(inputs["schema"] == 1 and inputs["profile"] == "040-operand-read-recovery" and inputs["selection"] == selection, "Wrong audit profile/selection")
    origin = Path(inputs["executedRoot"])
    check(origin.is_absolute() and inputs["command"] == command(origin, selection), "Wrong/empty command selection")
    settings = dict(COPPER68K_SYNTHETIC_REPORT_DIR=str(origin))
    if selection == "complete":
        settings.update({n: "1" for n in FLAGS})
    check(inputs["settings"] == settings and inputs["producerSha256"] == sha(Path(__file__)), "Wrong audit settings/producer")
    check(execution["exit"] == 0 and execution["sourcesUnchanged"] and execution["inputsSha256"] == sha(root / "inputs.json"), "Incomplete/changed execution")
    check(set(inputs["sources"]) == set(inputs["normalizedSources"]) == set(source_names()), "Wrong source inventory")
    for name, digest in inputs["sources"].items():
        check(sha(root / "source" / name) == digest and normalized(root / "source" / name) == inputs["normalizedSources"][name] == normalized(REPO / name), "Changed source: " + name)
    check(set(inputs["assets"]) == {"Copper68k/README.md", "Copper68k/copper68k-icon.png"}, "Wrong assets")
    check(all(sha(root / "source" / n) == sha(REPO / n) == h for n, h in inputs["assets"].items()), "Changed assets")
    required_dlls = {"build/bin/Copper68k/release/Copper68k.dll", "build/bin/Copper68k.Tests/release/Copper68k.dll", "build/bin/Copper68k.Tests/release/Copper68k.Tests.dll"}
    check(set(execution["assemblies"]) == required_dlls and all(sha(root / n) == h for n, h in execution["assemblies"].items()), "Wrong/changed DLL identity")
    check({p.relative_to(root).as_posix() for p in (root / "build/bin").rglob("Copper68k*.dll")} == required_dlls, "Extra/missing CPU or test DLL")
    check(execution["assemblies"]["build/bin/Copper68k/release/Copper68k.dll"] == execution["assemblies"]["build/bin/Copper68k.Tests/release/Copper68k.dll"], "CPU copies differ")
    check(set(execution["outputs"]) == {"execution.log", "audit.trx"} and all(sha(root / n) == h for n, h in execution["outputs"].items()), "Changed execution output")
    chosen, names = groups(selection), named_tests(selection)
    tree = ET.parse(root / "audit.trx")
    rows = tree.findall(".//t:UnitTestResult", NS)
    check(len(rows) == len(names) and {r.get("testName") for r in rows} == names and all(r.get("outcome") == "Passed" for r in rows), "Missing/extra/duplicate/failed/skipped named tests")
    check(tree.find(".//t:ResultSummary", NS).get("outcome") == "Completed", "Incomplete test run")
    counters = tree.find(".//t:Counters", NS)
    check(all(int(counters.get(k, -1)) == v for k, v in dict(total=len(names), executed=len(names), passed=len(names), failed=0, notExecuted=0).items()), "Wrong test counters")
    definitions = tree.findall(".//t:UnitTest", NS)
    lookup = {d.get("id"): d for d in definitions}
    check(len(lookup) == len(definitions) == len(names) and set(lookup) == {r.get("testId") for r in rows}, "Wrong loaded definitions")
    dll = (origin / "build/bin/Copper68k.Tests/release/Copper68k.Tests.dll").resolve()
    for row in rows:
        definition = lookup[row.get("testId")]
        method = definition.find("t:TestMethod", NS)
        check(definition.get("name") == row.get("testName") and method.get("className").split(",")[0] == row.get("testName").split("(")[0].rsplit(".", 1)[0] and
              method.get("name") == row.get("testName").split("(")[0].rsplit(".", 1)[1] and
              Path(definition.get("storage")).resolve() == Path(method.get("codeBase")).resolve() == dll, "Wrong loaded method/assembly")
    reports, total = {}, 0
    for group, count, _ in chosen:
        keys = list(expected_keys(group))
        check(len(keys) == len(set(keys)) == (count // 32 if group == "operand-read-fault-discovery" else count) > 0, "Invalid independent case inventory: " + group)
        for mode in ["scalar", "batch"]:
            name = f"68040-{group}-{mode}.json"
            report = load(root / name)
            check(report["model"] == "68040" and report["group"] == group + "-" + mode and report["logicalCases"] == count and report["xunitBatches"] == 1 and
                  report["counts"] == dict(passing=count, mismatching=0, unsupported=0, untested=0) and not report["failures"], "Wrong/failed report: " + name)
            check(set(report["combinations"]) == set(keys) and all(v == {"passing": 32 if group == "operand-read-fault-discovery" else 1} for v in report["combinations"].values()), "Wrong case keys/weights: " + name)
            reports[name] = sha(root / name)
            total += count
    check({p.name for p in root.glob("68040-*.json")} == set(reports), "Missing/extra report selection")
    metadata = {"inputs.json", "execution.json"} | ({"proof.json"} if (root / "proof.json").exists() else set())
    check({p.name for p in root.glob("*.json")} == set(reports) | metadata, "Unexpected audit JSON evidence")
    check(total == (1152 if selection == "controls" else 492672), "Wrong logical total")
    return dict(schema=1, selection=selection, namedPasses=len(names), logicalCases=total, reports=reports,
                inputsSha256=sha(root / "inputs.json"), executionSha256=sha(root / "execution.json"),
                producerSha256=sha(Path(__file__)), executedRoot=str(origin), evidenceRoot=str(root),
                sourceInputs=len(inputs["sources"]), productionCpuInputs=sum(n.startswith("Copper68k/") for n in inputs["sources"]),
                sourceComparison="snapshot raw hashes; current checkout CRLF-to-LF only", fullCpuSuite=False,
                hardwareQualified=False, productionImport=False, publication=False, roadmapComplete=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--selection", choices=["controls", "complete"], default="complete")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    root = args.output.resolve()
    if not args.validate_only:
        execute(root, args.selection)
    proof = validate(root, args.selection)
    if args.validate_only:
        existing = load(root / "proof.json")
        existing["evidenceRoot"] = str(root)
        check(existing == proof, "Proof differs from actual evidence")
    else:
        check(not (root / "proof.json").exists(), "Existing proof")
        save(root / "proof.json", proof)
    print(f"Qualified {args.selection}: {proof['namedPasses']} tests / {proof['logicalCases']} cases / {len(proof['reports'])} reports")


if __name__ == "__main__":
    main()
