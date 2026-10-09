"""Validate copied-evidence rejection controls; never execute a CPU."""
import argparse
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

HELPER = Path(__file__).with_name("test-copper68k-040-nested-writebacks.py")
spec = importlib.util.spec_from_file_location("nested_audit", HELPER)
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


def copy_evidence(source, target):
    audit.check(not target.exists(), "Use a fresh copied-evidence directory")
    inputs, execution = audit.load(source / "inputs.json"), audit.load(source / "execution.json")
    files = [*("source/" + n for n in inputs["sources"]), *("source/" + n for n in inputs["assets"]),
             *execution["assemblies"], *execution["outputs"], "inputs.json", "execution.json", "proof.json",
             *(p.name for p in source.glob("68040-*.json"))]
    for name in files:
        target_file = target / name
        target_file.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source / name, target_file)


def refresh_trx(root):
    execution = audit.load(root / "execution.json")
    execution["outputs"]["audit.trx"] = audit.sha(root / "audit.trx")
    audit.save(root / "execution.json", execution)
    proof = audit.load(root / "proof.json")
    proof["executionSha256"] = audit.sha(root / "execution.json")
    audit.save(root / "proof.json", proof)


def corrupt(root, kind, report):
    if kind == "missing-report":
        (root / report).unlink()
    elif kind == "wrong-weight":
        value = audit.load(root / report)
        value["combinations"][next(iter(value["combinations"]))]["passing"] = 2
        audit.save(root / report, value)
    elif kind == "extra-report":
        shutil.copy2(root / report, root / "68000-unexpected.json")
    elif kind in ["empty-selection", "wrong-method", "wrong-assembly"]:
        tree = ET.parse(root / "audit.trx")
        if kind == "empty-selection":
            for parent in tree.iter():
                for child in list(parent):
                    if child.tag == "{" + audit.NS["t"] + "}UnitTestResult":
                        parent.remove(child)
        else:
            definition = tree.find(".//t:UnitTest", audit.NS)
            method = definition.find("t:TestMethod", audit.NS)
            if kind == "wrong-method":
                method.set("name", "UnrelatedPassingMethod")
            else:
                wrong = str(Path(audit.load(root / "inputs.json")["executedRoot"]) / "build/bin/Copper68k/release/Copper68k.dll")
                method.set("codeBase", wrong)
                definition.set("storage", wrong)
        tree.write(root / "audit.trx", encoding="utf-8", xml_declaration=True)
        refresh_trx(root)
    elif kind == "changed-dll":
        path = root / "build/bin/Copper68k.Tests/release/Copper68k.Tests.dll"
        path.write_bytes(path.read_bytes() + b"copied corruption control")
    elif kind == "changed-source":
        name = "Copper68k.Tests/Synthetic/SyntheticM68040NestedWritebackFaultTests.cs"
        path = root / "source" / name
        path.write_bytes(path.read_bytes() + b"\n// copied source change\n")
        inputs = audit.load(root / "inputs.json")
        inputs["sources"][name], inputs["normalizedSources"][name] = audit.sha(path), audit.normalized(path)
        audit.save(root / "inputs.json", inputs)
        execution = audit.load(root / "execution.json")
        execution["inputsSha256"] = audit.sha(root / "inputs.json")
        audit.save(root / "execution.json", execution)
        proof = audit.load(root / "proof.json")
        proof["inputsSha256"], proof["executionSha256"] = audit.sha(root / "inputs.json"), audit.sha(root / "execution.json")
        audit.save(root / "proof.json", proof)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    source, output = args.source.resolve(), args.output.resolve()
    audit.check(not output.exists(), "Use a fresh integrity directory")
    selection = audit.load(source / "inputs.json")["selection"]
    audit.validate(source, selection)
    originals = {p.relative_to(source).as_posix(): audit.sha(p) for p in source.rglob("*") if p.is_file()}
    output.mkdir(parents=True)
    controls = {"missing-report": "FileNotFoundError", "wrong-weight": "Wrong case keys/weights",
                "extra-report": "Unexpected audit JSON evidence", "empty-selection": "Missing/extra/duplicate/failed/skipped named tests",
                "wrong-method": "Wrong loaded method/assembly", "wrong-assembly": "Wrong loaded method/assembly",
                "changed-dll": "Wrong/changed DLL identity", "changed-source": "Changed source:"}
    records = {}
    report = next(iter(audit.load(source / "proof.json")["reports"]))
    for kind in ["relocated-valid", *controls]:
        target = output / kind
        copy_evidence(source, target)
        if kind != "relocated-valid":
            corrupt(target, kind, report)
        command = [sys.executable, str(HELPER), "--validate-only", "--selection", selection, "--output", str(target)]
        result = subprocess.run(command, text=True, capture_output=True)
        text = result.stdout + result.stderr
        (output / (kind + ".log")).write_text(text, encoding="utf-8")
        audit.check(result.returncode == 0 if kind == "relocated-valid" else result.returncode != 0 and controls[kind] in text,
                    "Wrong integrity result: " + kind)
        records[kind] = dict(exit=result.returncode, command=command, diagnostic=controls.get(kind), logSha256=audit.sha(output / (kind + ".log")))
    audit.check(originals == {p.relative_to(source).as_posix(): audit.sha(p) for p in source.rglob("*") if p.is_file()}, "Original evidence changed")
    audit.save(output / "proof.json", dict(schema=1, sourceProofSha256=audit.sha(source / "proof.json"),
                producerSha256=audit.sha(Path(__file__)), verifierSha256=audit.sha(HELPER), controls=records,
                relocatedValidPasses=True, rejectedCorruptions=8, originalEvidenceUnchanged=True, cpuExecuted=False))
    print("Relocated evidence passes; eight copied corruptions reject; original evidence unchanged")


if __name__ == "__main__":
    main()
