"""Exercise copied-evidence rejection controls; this command does not execute a CPU."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET


HELPER = Path(__file__).with_name("test-copper68k-private-predecrement.py")
spec = importlib.util.spec_from_file_location("private_audit", HELPER)
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


def replace(root, path, data):
    # Atomic replacement detaches a hard link without changing the audited file.
    path.resolve().relative_to(root.resolve())
    temporary = path.with_name(path.name + ".control-copy")
    audit.check(not temporary.exists(), "Existing control temporary file")
    temporary.write_bytes(data)
    os.replace(temporary, path)


def copy_evidence(source, target, value):
    audit.check(not target.exists(), "Control output must be fresh")
    inputs = audit.load(source / "inputs.json")
    execution = audit.load(source / "execution.json")
    names = [*("source/" + n for n in inputs["sources"]),
             *("source/" + n for n in inputs["assets"]),
             *execution["assemblies"], *value["reports"], "execution.log"]
    for name in names:
        destination = target / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        os.link(source / name, destination)
    inputs["command"] = audit.command(target)
    inputs["settings"]["COPPER68K_SYNTHETIC_REPORT_DIR"] = str(target)
    audit.save(target / "inputs.json", inputs)
    tree = ET.parse(source / "audit.trx")
    loaded = str(target / "build/bin/Copper68k.Tests/release/Copper68k.Tests.dll")
    for definition in tree.findall(".//t:UnitTest", audit.NS):
        definition.set("storage", loaded)
        definition.find("t:TestMethod", audit.NS).set("codeBase", loaded)
    tree.write(target / "audit.trx", encoding="utf-8", xml_declaration=True)
    execution["inputsSha256"] = audit.sha(target / "inputs.json")
    execution["outputs"]["audit.trx"] = audit.sha(target / "audit.trx")
    audit.save(target / "execution.json", execution)
    audit.save(target / "proof.json", audit.validate(target, value))


def modify_trx(root, action):
    tree = ET.parse(root / "audit.trx")
    action(tree)
    tree.write(root / "audit.trx", encoding="utf-8", xml_declaration=True)
    execution = audit.load(root / "execution.json")
    execution["outputs"]["audit.trx"] = audit.sha(root / "audit.trx")
    audit.save(root / "execution.json", execution)


def run(root):
    result = subprocess.run([sys.executable, str(HELPER), "--validate-only", "--output", str(root)],
                            capture_output=True, text=True)
    (root / "control.stdout.txt").write_text(result.stdout, encoding="utf-8")
    (root / "control.stderr.txt").write_text(result.stderr, encoding="utf-8")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--audit", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    source, output = args.audit.resolve(), args.output.resolve()
    audit.check(not output.exists(), "Control output must be fresh")
    value = audit.manifest()
    audit.check(audit.load(source / "proof.json") == audit.validate(source, value), "Original evidence failed replay")
    inputs, execution = audit.load(source / "inputs.json"), audit.load(source / "execution.json")
    immutable = [*("source/" + n for n in inputs["sources"]),
                 *("source/" + n for n in inputs["assets"]), *execution["assemblies"],
                 *value["reports"], "execution.log", "audit.trx", "inputs.json", "execution.json", "proof.json"]
    before = {n: audit.sha(source / n) for n in immutable}
    positive = output / "RelocatedPositive"
    copy_evidence(source, positive, value)
    accepted = run(positive)
    audit.check(accepted.returncode == 0, "Positive copied-evidence replay failed")
    report = sorted(value["reports"])[0]
    cpu = "build/bin/Copper68k.Tests/release/Copper68k.dll"
    source_file = sorted(inputs["sources"])[0]
    controls = [
        ("MissingReport", "Missing or extra required reports"),
        ("ChangedReport", "Changed architectural cases or results"),
        ("EmptySelection", "Missing, empty, extra or failed selection"),
        ("WrongMethod", "Wrong loaded method"),
        ("WrongAssembly", "Wrong loaded test assembly"),
        ("ChangedDLL", "Missing or changed execution DLLs"),
        ("ChangedSource", "Changed candidate source inventory"),
        ("ExtraReport", "Missing or extra required reports"),
    ]
    results = {}
    for name, diagnostic in controls:
        root = output / name
        copy_evidence(source, root, value)
        if name == "MissingReport":
            target = root / report
            target.resolve().relative_to(root)
            target.rename(root / (report + ".missing"))
        elif name == "ChangedReport":
            changed = audit.load(root / report)
            changed["logicalCases"] = 0
            replace(root, root / report, (json.dumps(changed) + "\n").encode())
        elif name == "EmptySelection":
            def remove_rows(tree):
                rows = tree.find("t:Results", audit.NS)
                for row in list(rows):
                    rows.remove(row)
            modify_trx(root, remove_rows)
        elif name == "WrongMethod":
            modify_trx(root, lambda tree: tree.find(".//t:TestMethod", audit.NS).set("name", "WrongMethod"))
        elif name == "WrongAssembly":
            modify_trx(root, lambda tree: tree.find(".//t:TestMethod", audit.NS).set("codeBase", str(root / "Wrong.dll")))
        elif name == "ChangedDLL":
            replace(root, root / cpu, (root / cpu).read_bytes() + b"changed")
        elif name == "ChangedSource":
            target = root / "source" / source_file
            replace(root, target, target.read_bytes() + b"\n// changed control\n")
        else:
            shutil.copyfile(root / report, root / "68020-extra-control.json")
        rejected = run(root)
        audit.check(rejected.returncode == 1 and diagnostic in rejected.stderr,
                    "Control did not reject for its required reason: " + name)
        results[name] = dict(exit=rejected.returncode, diagnostic=diagnostic,
                             stdoutSha256=audit.sha(root / "control.stdout.txt"),
                             stderrSha256=audit.sha(root / "control.stderr.txt"))
        print("Rejected copied-evidence control: " + name, flush=True)
    audit.check(all(audit.sha(source / n) == h for n, h in before.items()), "Original evidence changed")
    audit.check(audit.load(source / "proof.json") == audit.validate(source, value), "Original replay changed")
    audit.save(output / "proof.json", dict(schema=1, originalProofSha256=before["proof.json"],
               helperSha256=audit.sha(HELPER), producerSha256=audit.sha(Path(__file__)),
               positiveReplayExit=accepted.returncode, controls=results, originalUnchanged=True,
               copiedValidatorInputs=True, newCpuExecution=False, hardwareQualification=False))
    print("Qualified eight rejection controls and positive relocation; no new CPU execution", flush=True)


if __name__ == "__main__":
    main()
