"""Execute/replay exact supplied-frame nested-writeback selections; no CPU import."""
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
CLASS = "Copper68k.Tests.Synthetic.SyntheticM68040NestedWritebackFaultTests."
FLAGS = ["COPPER68K_RUN_040_" + suffix for suffix in [
    "MIXED_NESTED_WRITEBACK", "NESTED_RETURN_BANKS", "USER_MIXED_NESTED_WRITEBACK",
    "USER_M_NESTED_WRITEBACK", "HETEROGENEOUS_NESTED_WRITEBACK",
    "USER_M_HETEROGENEOUS_WRITEBACK", "REPEATED_NESTED_WRITEBACK"]]
GROUPS = [
    ("nested-writeback-fault", "ActualHandlerStoresFaultCompleteAndResume", 336, True),
    ("mixed-nested-writeback-fault", "MixedWidthsAndAllCcr", 86016, False),
    ("nested-writeback-return-banks", "OtherCcrAndUserReturns", 26544, False),
    ("user-mixed-nested-writeback-fault", "RemainingUserMixedWidths", 32256, False),
    ("user-M-nested-writeback-fault", "UserMasterBitReturns", 48384, False),
    ("heterogeneous-nested-writeback-fault", "HeterogeneousFunctionCodes", 29232, False),
    ("user-M-heterogeneous-nested-writeback-fault", "HeterogeneousUserMasterBit", 9744, False),
    ("repeated-nested-writeback-fault", "RepeatedNestedFaults", 25984, False),
    ("repeated-nested-writeback-reference", "RepeatedNestedReferenceExamples", 24, True)]


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
    return [g for g in GROUPS if selection == "complete" or g[3]]


def command(root, selection):
    chosen = groups(selection)
    filter_value = "FullyQualifiedName~" + CLASS[:-1] if selection == "complete" else "|".join(
        "FullyQualifiedName~" + CLASS + g[1] for g in chosen)
    return ["dotnet", "test", str(root / "source/Copper68k.Tests/Copper68k.Tests.csproj"), "-c", "Release",
            "--artifacts-path", str(root / "build"), "--filter", filter_value,
            "--logger", "trx;LogFileName=audit.trx", "--results-directory", str(root)]


def expected_keys(group):
    """Literal architectural fixture inputs; no production decoder/EA helpers."""
    for widths in itertools.product((1, 2, 4), repeat=3):
        distinct = len(set(widths))
        sizes = "-".join(map(str, widths))
        if group == "nested-writeback-fault":
            if distinct != 1:
                continue
            for lane, fc, bank, slot in itertools.product(range(4), (1, 5), ("ISP", "MSP"), (1, 2, 3)):
                for byte in range(widths[slot - 1]):
                    yield f"68040/writeback/nested-physical-fault/size={widths[0]}/lane={lane}/FC={fc}/bank={bank}/WB={slot}/byte={byte}"
        elif group in ["heterogeneous-nested-writeback-fault", "user-M-heterogeneous-nested-writeback-fault", "repeated-nested-writeback-fault"]:
            repeated = group == "repeated-nested-writeback-fault"
            banks = ("user", "user-M", "ISP", "MSP") if repeated else ("user-M",) if group.startswith("user-M-") else ("user", "ISP", "MSP")
            codes = [(1, 1, 1), (5, 5, 5)] if repeated else [f for f in itertools.product((1, 5), repeat=3) if len(set(f)) > 1]
            for bank, fcs, ccr, lane, slot in itertools.product(banks, codes, range(32), range(4), (1, 2, 3)):
                if ccr != 31 and widths != (1, 2, 4):
                    continue
                for byte in range(widths[slot - 1]):
                    if repeated:
                        for depth in (2, 3):
                            yield f"68040/writeback/repeated-nested-physical-fault/depth={depth}/sizes={sizes}/ccr={ccr:02X}/lane={lane}/FC={fcs[0]}/bank={bank}/WB={slot}/byte={byte}"
                    else:
                        fc_text = "-".join(map(str, fcs))
                        yield f"68040/writeback/heterogeneous-nested-physical-fault/sizes={sizes}/FCs={fc_text}/ccr={ccr:02X}/lane={lane}/bank={bank}/WB={slot}/byte={byte}"
        elif group == "repeated-nested-writeback-reference":
            if widths != (1, 2, 4):
                continue
            for bank, depth, slot in itertools.product(("user", "user-M", "ISP", "MSP"), (2, 3), (1, 2, 3)):
                yield f"68040/writeback/repeated-nested-reference/depth={depth}/sizes=1-2-4/ccr=05/lane=3/FC=5/bank={bank}/WB={slot}/byte={widths[slot-1]-1}"
        else:
            banks = ("ISP", "MSP") if group == "mixed-nested-writeback-fault" else ("user", "ISP", "MSP") if group == "nested-writeback-return-banks" else ("user-M",) if group.startswith("user-M-") else ("user",)
            form = {"mixed-nested-writeback-fault": "mixed-nested-physical-fault", "nested-writeback-return-banks": "nested-return-bank",
                    "user-mixed-nested-writeback-fault": "user-mixed-nested-physical-fault", "user-M-nested-writeback-fault": "user-M-nested-physical-fault"}[group]
            for bank, ccr, lane, fc, slot in itertools.product(banks, range(32), range(4), (1, 5), (1, 2, 3)):
                if group == "mixed-nested-writeback-fault" and distinct == 1:
                    continue
                if group == "user-mixed-nested-writeback-fault" and distinct != 2:
                    continue
                if group == "nested-writeback-return-banks" and not (distinct == 1 or (bank == "user" and distinct == 3)):
                    continue
                if group == "nested-writeback-return-banks" and bank != "user" and ccr == 31:
                    continue
                for byte in range(widths[slot - 1]):
                    yield f"68040/writeback/{form}/sizes={sizes}/ccr={ccr:02X}/lane={lane}/FC={fc}/bank={bank}/WB={slot}/byte={byte}"


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
    inputs = dict(schema=1, profile="040-nested-writebacks", selection=selection, executedRoot=str(root),
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
    check(inputs["schema"] == 1 and inputs["profile"] == "040-nested-writebacks" and inputs["selection"] == selection, "Wrong audit profile/selection")
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
    chosen, names = groups(selection), set()
    for _, method, _, theory in chosen:
        names.update(CLASS + method + (f"(batch: {mode})" if theory else mode) for mode in (["False", "True"] if theory else ["Scalar", "Batch"]))
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
        check(definition.get("name") == row.get("testName") and method.get("className").split(",")[0] == CLASS[:-1] and
              method.get("name") == row.get("testName").split("(")[0].rsplit(".", 1)[1] and
              Path(definition.get("storage")).resolve() == Path(method.get("codeBase")).resolve() == dll, "Wrong loaded method/assembly")
    reports, total = {}, 0
    for group, _, count, _ in chosen:
        keys = list(expected_keys(group))
        check(len(keys) == len(set(keys)) == count > 0, "Invalid independent case inventory: " + group)
        for mode in ["scalar", "batch"]:
            name = f"68040-{group}-{mode}.json"
            report = load(root / name)
            check(report["model"] == "68040" and report["group"] == group + "-" + mode and report["logicalCases"] == count and report["xunitBatches"] == 1 and
                  report["counts"] == dict(passing=count, mismatching=0, unsupported=0, untested=0) and not report["failures"], "Wrong/failed report: " + name)
            check(set(report["combinations"]) == set(keys) and all(v == {"passing": 1} for v in report["combinations"].values()), "Wrong case keys/weights: " + name)
            reports[name] = sha(root / name)
            total += count
    check({p.name for p in root.glob("68040-*.json")} == set(reports), "Missing/extra report selection")
    metadata = {"inputs.json", "execution.json"} | ({"proof.json"} if (root / "proof.json").exists() else set())
    check({p.name for p in root.glob("*.json")} == set(reports) | metadata, "Unexpected audit JSON evidence")
    check(total == (720 if selection == "controls" else 517040), "Wrong logical total")
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
