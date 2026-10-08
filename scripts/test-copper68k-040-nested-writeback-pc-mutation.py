"""Prove repeated-fault fixtures reject a nested MOVES saved-PC mutation."""
import argparse
import importlib.util
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

HELPER = Path(__file__).with_name("test-copper68k-040-nested-writebacks.py")
spec = importlib.util.spec_from_file_location("nested_audit", HELPER)
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)
CPU_FILE = "Copper68k/M68040Support.cs"


def mutate(root):
    path = root / "source" / CPU_FILE
    before = path.read_bytes()
    start = before.index(b"else if (!State.M68040Mmu.Enabled && fault.CompletedMovesWrite &&")
    end = before.index(b"else if (!State.M68040Mmu.Enabled && fault.Write && fault.MovemEffectiveAddress.HasValue", start)
    region = before[start:end]
    old = b"fault = fault with { StackedProgramCounter = State.ProgramCounter };"
    # Reset records vector -1 and increments ExceptionSequence. Use the prior
    # recorded vector to preserve first-fault behavior, not a zero-counter guess.
    new = b"fault = fault with { StackedProgramCounter = State.LastExceptionVector < 0 ? State.ProgramCounter : ExecutionBoundaryProgramCounter };"
    audit.check(region.count(old) == 1, "Changed/ambiguous MOVES mutation anchor")
    after = before[:start] + region.replace(old, new) + before[end:]
    path.write_bytes(after)
    inputs = audit.load(root / "inputs.json")
    inputs["sources"][CPU_FILE] = audit.sha(path)
    inputs["normalizedSources"][CPU_FILE] = audit.normalized(path)
    audit.save(root / "inputs.json", inputs)
    return dict(source=CPU_FILE, beforeSha256=audit.sha(audit.REPO / CPU_FILE), afterSha256=audit.sha(path),
                oldLiteral=old.decode(), newLiteral=new.decode(), firstFaultUnchanged=True)


def verify(root, baseline, mutation):
    inputs, execution = audit.load(root / "inputs.json"), audit.load(root / "execution.json")
    clean_inputs = audit.load(baseline / "inputs.json")
    clean_proof = audit.validate(baseline, "controls")
    audit.check(execution["exit"] == 1 and not execution["sourcesUnchanged"], "Expected actual failing mutant execution")
    audit.check(inputs["selection"] == "controls" and inputs["command"] == audit.command(root, "controls") and
                inputs["settings"] == {"COPPER68K_SYNTHETIC_REPORT_DIR": str(root)} and
                inputs["producerSha256"] == audit.sha(HELPER), "Wrong mutant command/settings/producer")
    audit.check(execution["inputsSha256"] == audit.sha(root / "inputs.json"), "Wrong mutant input identity")
    audit.check(set(inputs["sources"]) == set(clean_inputs["sources"]) == set(audit.source_names()), "Wrong mutant source roster")
    changed = [n for n in inputs["sources"] if inputs["sources"][n] != clean_inputs["sources"][n]]
    audit.check(changed == [CPU_FILE], "Mutation changed unrelated source inputs")
    audit.check(all(audit.sha(root / "source" / n) == h for n, h in inputs["sources"].items()), "Mutant source changed during execution")
    audit.check(all(audit.normalized(root / "source" / n) == h for n, h in inputs["normalizedSources"].items()), "Mutant normalized identities differ")
    audit.check(all(audit.sha(audit.REPO / n) == clean_inputs["sources"][n] for n in clean_inputs["sources"]), "Production source changed")
    audit.check(inputs["assets"] == clean_inputs["assets"] and all(audit.sha(root / "source" / n) == h for n, h in inputs["assets"].items()), "Changed mutant assets")
    audit.check(len(execution["assemblies"]) == 3 and all(audit.sha(root / n) == h for n, h in execution["assemblies"].items()), "Changed mutant DLLs")
    audit.check(execution["assemblies"]["build/bin/Copper68k/release/Copper68k.dll"] == execution["assemblies"]["build/bin/Copper68k.Tests/release/Copper68k.dll"], "Mutant CPU copies differ")
    audit.check(set(execution["outputs"]) == {"execution.log", "audit.trx"} and all(audit.sha(root / n) == h for n, h in execution["outputs"].items()), "Changed mutant execution output")
    tree = ET.parse(root / "audit.trx")
    rows = tree.findall(".//t:UnitTestResult", audit.NS)
    expected = {audit.CLASS + method + f"(batch: {mode})": outcome for method, outcome in [
        ("ActualHandlerStoresFaultCompleteAndResume", "Passed"), ("RepeatedNestedReferenceExamples", "Failed")]
        for mode in ["False", "True"]}
    audit.check(len(rows) == 4 and {r.get("testName"): r.get("outcome") for r in rows} == expected, "Wrong mutation failure roster")
    audit.check(tree.find(".//t:ResultSummary", audit.NS).get("outcome") == "Failed", "Wrong mutant summary")
    counters = tree.find(".//t:Counters", audit.NS)
    audit.check(all(int(counters.get(k, -1)) == v for k, v in dict(total=4, executed=4, passed=2, failed=2, notExecuted=0).items()), "Wrong mutant counters")
    definitions = tree.findall(".//t:UnitTest", audit.NS)
    lookup = {d.get("id"): d for d in definitions}
    audit.check(len(lookup) == len(definitions) == 4 and set(lookup) == {r.get("testId") for r in rows}, "Wrong mutant loaded definitions")
    for row in rows:
        d = lookup[row.get("testId")]
        m = d.find("t:TestMethod", audit.NS)
        audit.check(d.get("name") == row.get("testName") and m.get("className").split(",")[0] == audit.CLASS[:-1] and
                    m.get("name") == row.get("testName").split("(")[0].rsplit(".", 1)[1] and
                    Path(d.get("storage")).resolve() == Path(m.get("codeBase")).resolve() ==
                    (root / "build/bin/Copper68k.Tests/release/Copper68k.Tests.dll").resolve(), "Wrong mutant loaded method/assembly")
    reports = {}
    for mode in ["scalar", "batch"]:
        control = f"68040-nested-writeback-fault-{mode}.json"
        audit.check(audit.sha(root / control) == clean_proof["reports"][control], "Single-fault controls no longer byte-identical")
        name = f"68040-repeated-nested-writeback-reference-{mode}.json"
        report = audit.load(root / name)
        keys = set(audit.expected_keys("repeated-nested-writeback-reference"))
        audit.check(report["model"] == "68040" and report["group"] == "repeated-nested-writeback-reference-" + mode and report["xunitBatches"] == 1 and
                    report["logicalCases"] == 24 and report["counts"] == dict(passing=0, mismatching=24, unsupported=0, untested=0), "Wrong mutant case outcomes")
        audit.check(set(report["combinations"]) == keys and all(v == {"mismatching": 1} for v in report["combinations"].values()), "Wrong mutation case keys")
        failures = report["failures"]
        audit.check(len(failures) == 24 and {f["id"] for f in failures} == keys and all(f["status"] == "mismatching" and
                    "nested fault entry: saved PC expected" in f["reason"] for f in failures), "Wrong mutation witness diagnostics")
        reports[control], reports[name] = audit.sha(root / control), audit.sha(root / name)
    audit.check({p.name for p in root.glob("68040-*.json")} == set(reports), "Unexpected mutant reports")
    return dict(schema=1, cleanProofSha256=audit.sha(baseline / "proof.json"), helperSha256=audit.sha(HELPER),
                producerSha256=audit.sha(Path(__file__)), mutation=mutation, inputsSha256=audit.sha(root / "inputs.json"),
                executionSha256=audit.sha(root / "execution.json"), reports=reports, mismatchWitnesses=48,
                unchangedSingleFaultCases=672, firstFaultControlsByteIdentical=True, productionSourceUnchanged=True,
                hardwareQualified=False, productionImport=False, publication=False, roadmapComplete=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    baseline, root = args.baseline.resolve(), args.output.resolve()
    audit.validate(baseline, "controls")
    mutation = None
    original_run = subprocess.run

    def intercepted(command, *positional, **keywords):
        nonlocal mutation
        if isinstance(command, list) and command and command[0] == "dotnet":
            audit.check(mutation is None, "Unexpected repeated compiler/test invocation")
            mutation = mutate(root)
        return original_run(command, *positional, **keywords)

    subprocess.run = intercepted
    try:
        try:
            audit.execute(root, "controls")
        except ValueError as error:
            audit.check(str(error) == "Execution failed; inspect execution.log", "Unexpected audit rejection")
    finally:
        subprocess.run = original_run
    audit.check(mutation is not None, "Missing mutation execution")
    proof = verify(root, baseline, mutation)
    audit.save(root / "mutation-proof.json", proof)
    print("Nested saved-PC mutation: 48 precise mismatches; 672 single-fault controls byte-identical")


if __name__ == "__main__":
    main()
