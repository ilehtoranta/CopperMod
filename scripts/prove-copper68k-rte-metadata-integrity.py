"""Corrupt copied validator inputs; original execution artifacts stay immutable."""
import argparse
import importlib.util
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET

SPEC = importlib.util.spec_from_file_location("metadata_audit", Path(__file__).with_name("test-copper68k-rte-metadata.py"))
A = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(A)
CONTROLS = {
    "MissingFixture": "Missing source fixture",
    "ChangedSourceAndManifest": "Wrong source, command or producer identity",
    "WrongProducer": "Wrong source, command or producer identity",
    "WrongFilter": "Wrong source, command or producer identity",
    "MissingAssembly": "Missing execution outputs",
    "ChangedCpuAndManifest": "Executed CPU copies disagree",
    "EmptySelection": "Missing, empty or substituted test selection",
    "WrongCounters": "Wrong execution counters",
    "MissingDefinition": "Missing, empty or substituted test selection",
    "MissingMethod": "Missing loaded test method",
    "WrongMethod": "Wrong loaded method",
    "WrongMethodAssembly": "Wrong loaded test assembly",
    "WrongOutcome": "Wrong named outcome",
    "MissingReport": "Missing coverage report",
    "EmptyCoverage": "Missing, empty or incorrect coverage combinations",
    "WrongFailureId": "Wrong ordered failure identifiers",
    "WrongDiagnostic": "Wrong rejection witness",
}


def reseal(run):
    execution = A.load(run / "execution.json")
    execution["inputsSha256"] = A.sha(run / "inputs.json")
    execution["outputs"] = {n: A.sha(run / n) for n in A.OUTPUTS if (run / n).is_file()}
    A.save(run / "execution.json", execution)


def copied_inputs(parent, target):
    # These are relocated validator fixtures, never claimed as new CPU runs.
    # The positive check establishes that the later single corruption reaches
    # its intended guard instead of failing on a relocated path/hash first.
    for variant in A.VARIANTS:
        src, dst = parent / variant, target / variant
        inputs = A.load(src / "inputs.json")
        for name in inputs["sources"]:
            path = dst / "source" / name
            path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(src / "source" / name, path)
        for name in [*A.OUTPUTS, "execution.json", *[f.name for f in src.glob("*-rte-private-metadata-*.json")]]:
            path = dst / name
            path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(src / name, path)
        inputs["command"], inputs["settings"] = A.command(dst), A.settings(dst)
        A.save(dst / "inputs.json", inputs)
        tree = ET.parse(dst / "metadata.trx")
        for definition in tree.findall(".//t:UnitTest", A.NS):
            definition.set("storage", str((dst / A.OUTPUTS[3]).resolve()))
            definition.find("t:TestMethod", A.NS).set("codeBase", str((dst / A.OUTPUTS[3]).resolve()))
        tree.write(dst / "metadata.trx", encoding="utf-8", xml_declaration=True)
        reseal(dst)


def corrupt(target, name):
    run = target / ("PhaseRange" if name in ["WrongFailureId", "WrongDiagnostic"] else "Baseline")
    inputs = A.load(run / "inputs.json")
    if name == "MissingFixture":
        path = run / "source" / A.FIXTURE
        A.check(path.resolve().is_relative_to(target.resolve()), "Unsafe control path")
        path.unlink()
    elif name == "ChangedSourceAndManifest":
        path = run / "source" / A.TARGET
        path.write_bytes(path.read_bytes() + b"\n// unrelated source change\n")
        inputs["sources"][A.TARGET] = A.sha(path)
    elif name == "WrongProducer":
        inputs["producerSha256"] = "0" * 64
    elif name == "WrongFilter":
        inputs["command"][inputs["command"].index("--filter") + 1] = "FullyQualifiedName~Nothing"
    elif name == "MissingAssembly":
        path = run / A.OUTPUTS[3]
        A.check(path.resolve().is_relative_to(target.resolve()), "Unsafe control path")
        path.unlink()
    elif name == "ChangedCpuAndManifest":
        path = run / A.OUTPUTS[2]
        path.write_bytes(path.read_bytes() + b"intentional integrity corruption")
    elif name in ["MissingReport", "EmptyCoverage", "WrongFailureId", "WrongDiagnostic"]:
        path = run / "68020-rte-private-metadata-scalar.json"
        if name == "MissingReport":
            A.check(path.resolve().is_relative_to(target.resolve()), "Unsafe control path")
            path.unlink()
        else:
            report = A.load(path)
            if name == "EmptyCoverage":
                report["logicalCases"] = 0
                report["combinations"] = {}
            elif name == "WrongFailureId":
                report["failures"][0]["id"] += "/unrelated-case"
            else:
                report["failures"][0]["reason"] = "unrelated failure"
            A.save(path, report)
    else:
        tree = ET.parse(run / "metadata.trx")
        definitions = tree.find("t:TestDefinitions", A.NS)
        results = tree.find("t:Results", A.NS)
        method = definitions[0].find("t:TestMethod", A.NS)
        if name == "EmptySelection":
            for row in list(results):
                results.remove(row)
        elif name == "WrongCounters":
            tree.find("t:ResultSummary/t:Counters", A.NS).set("executed", "0")
        elif name == "MissingDefinition":
            definitions.remove(definitions[0])
        elif name == "MissingMethod":
            definitions[0].remove(method)
        elif name == "WrongMethod":
            method.set("name", "CompletelyDifferentInstruction")
        elif name == "WrongMethodAssembly":
            method.set("codeBase", str(target / "unrelated.dll"))
        elif name == "WrongOutcome":
            results[0].set("outcome", "NotExecuted")
        else:
            raise ValueError("Unknown corruption control")
        tree.write(run / "metadata.trx", encoding="utf-8", xml_declaration=True)
    A.save(run / "inputs.json", inputs)
    reseal(run)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--audit", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    parent, output = args.audit.resolve(), args.output.resolve()
    A.check(not output.exists(), "Controls output already exists; choose a fresh directory")
    original = A.source_bytes()
    A.check(A.load(parent / "proof.json") == A.verify(parent, original), "Original audit failed strict replay")
    protected = {str(f.relative_to(parent)): A.sha(f) for f in parent.rglob("*") if f.is_file() and not {"obj"}.intersection(f.relative_to(parent).parts)}
    records = []
    for name, reason in CONTROLS.items():
        target = output / name
        copied_inputs(parent, target)
        positive = A.verify(target, original)
        corrupt(target, name)
        try:
            A.verify(target, original)
        except ValueError as error:
            A.check(str(error) == reason, "Wrong corruption rejection: " + name + ": " + str(error))
        else:
            raise ValueError("Corrupted validator inputs accepted: " + name)
        records.append(dict(name=name, expectedReason=reason, positiveRelocationPassed=True, positiveRecord=positive))
        print(name + ": rejected at intended guard", flush=True)
    A.check(all(A.sha(parent / n) == h for n, h in protected.items()), "Original evidence changed")
    A.save(output / "proof.json", dict(schema=1, controls=records, originalProofSha256=A.sha(parent / "proof.json"), originalFiles=protected, originalUnchanged=True, validatorSha256=A.sha(Path(A.__file__)), producerSha256=A.sha(Path(__file__)), scope="copied validator-input corruption controls; no new CPU or hardware execution", roadmapComplete=False))


if __name__ == "__main__":
    main()
