"""Isolated private C021 baseline/mutation audit; no hardware qualification."""
import argparse
import hashlib
import itertools
import json
import os
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parent.parent
FIXTURE = "Copper68k.Tests/Synthetic/SyntheticM68020RteMetadataTests.cs"
TARGET = "Copper68k/M68kAdvancedTimingInterpreter.Rte020.cs"
MODELS = ["68EC020", "68020", "68030", "A1200"]
FORMS = ["valid", "phase-range", "phase-version", "phase-end", "fault-address", "ssw-size", "ssw-fc", "ssw-extra", "returned-frame", "returned-bank"]
MUTATIONS = {
    "PhaseRange": ("context.Phase is < 0 or > 4 || ", ["phase-range"], 1024),
    "SswGuard": (" || (ssw & ~0x100) != expectedSsw", ["ssw-size", "ssw-fc", "ssw-extra"], 3072),
    "ReturnedStack": ("        if (returnedStack != context.Frame)\n            throw new UnsupportedM68kTimingException(0x4e73, State.LastInstructionProgramCounter, _profile);", ["returned-frame", "returned-bank"], 2048),
}
VARIANTS = ["Baseline", *MUTATIONS]
OUTPUTS = ["execution.log", "metadata.trx", "build/bin/Copper68k/release/Copper68k.dll", "build/bin/Copper68k.Tests/release/Copper68k.Tests.dll", "build/bin/Copper68k.Tests/release/Copper68k.dll"]
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def check(ok, reason):
    if not ok:
        raise ValueError(reason)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8", newline="\n")


def source_bytes():
    names = subprocess.check_output(["git", "ls-files", "Copper68k", "Copper68k.Tests"], cwd=REPO, text=True).splitlines()
    selected = {n: (REPO / n).read_bytes() for n in sorted(set(names + [FIXTURE])) if Path(n).suffix in [".cs", ".csproj"]}
    check(bool(selected) and FIXTURE in selected and TARGET in selected, "Missing required source fixtures")
    check(b"{ccr:X2}" in selected[FIXTURE], "Missing canonical CCR case identifiers")
    return selected


def variant_sources(original, variant):
    result = dict(original)
    if variant != "Baseline":
        text = result[TARGET].decode("utf-8-sig").replace("\r\n", "\n")
        anchor = MUTATIONS[variant][0]
        check(text.count(anchor) == 1, "Missing or ambiguous " + variant + " mutation target")
        result[TARGET] = text.replace(anchor, "").encode()
    return result


def command(run):
    return ["dotnet", "test", str(run / "source/Copper68k.Tests/Copper68k.Tests.csproj"), "-c", "Release", "--artifacts-path", str(run / "build"), "--filter", "FullyQualifiedName~SyntheticM68020RteMetadataTests", "--logger", "trx;LogFileName=metadata.trx", "--results-directory", str(run)]


def settings(run):
    return {"COPPER68K_RUN_020_RTE_METADATA": "1", "COPPER68K_SYNTHETIC_REPORT_DIR": str(run)}


def execute(root, original):
    check(not root.exists(), "Audit output already exists; choose a fresh directory")
    for variant in VARIANTS:
        run = root / variant
        sources = variant_sources(original, variant)
        for name, data in sources.items():
            target = run / "source" / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
        inputs = dict(schema=1, variant=variant, sources={n: hashlib.sha256(b).hexdigest() for n, b in sources.items()}, producerSha256=sha(Path(__file__)), command=command(run), settings=settings(run))
        save(run / "inputs.json", inputs)
        environment = {k: v for k, v in os.environ.items() if not k.startswith("COPPER68K_")}
        environment.update(settings(run))
        print(variant + ": executing isolated private metadata controls", flush=True)
        with (run / "execution.log").open("w", encoding="utf-8") as log:
            done = subprocess.run(command(run), cwd=REPO, env=environment, stdout=log, stderr=subprocess.STDOUT)
        check(all((run / name).is_file() for name in OUTPUTS), "Missing execution outputs")
        save(run / "execution.json", dict(exit=done.returncode, inputsSha256=sha(run / "inputs.json"), outputs={n: sha(run / n) for n in OUTPUTS}))
        check(done.returncode == (0 if variant == "Baseline" else 1), "Unexpected execution status: " + variant)


def verify_reason(failure, model):
    reason = failure["reason"].replace("\r\n", "\n")
    if "/returned-frame/" not in failure["id"]:
        check(reason == "Assert.Throws() Failure: No exception was thrown\nExpected: typeof(Copper68k.UnsupportedM68kTimingException)", "Wrong rejection witness")
        return
    inner = 0x12346000 if "/high=True/" in failure["id"] else 0x6000
    def physical(address):
        return address & 0xffffff if model in ["68EC020", "A1200"] else address
    accesses = re.findall(r"BusAccess \{ Address = (\d+), Width = (\d+), Write = (False|True), Value = (\d+), Kind = (\w+) \}", reason)
    actual = [(int(a), int(w), write, int(v), k) for a, w, write, v, k in accesses]
    expected = [(physical(at), width, "False", value, "CpuDataRead") for at, width, value in [(inner-4, 2, 0), (inner-2, 2, 0), (inner+0x102, 4, 0xa5a5a5a5), (inner+0x106, 2, 0xa5a5), (inner+0x11e, 2, 0)]]
    check(reason.startswith("Assert.DoesNotContain() Failure: Filter matched in collection\n") and actual == expected, "Wrong forbidden-inner-read witness")


def verify(root, original):
    records = []
    for variant in VARIANTS:
        run = root / variant
        sources = {n: hashlib.sha256(b).hexdigest() for n, b in variant_sources(original, variant).items()}
        inputs = load(run / "inputs.json")
        check(inputs == dict(schema=1, variant=variant, sources=sources, producerSha256=sha(Path(__file__)), command=command(run), settings=settings(run)), "Wrong source, command or producer identity")
        check(all((run / "source" / n).is_file() for n in sources), "Missing source fixture")
        check(all(sha(run / "source" / n) == h for n, h in sources.items()), "Changed source snapshot")
        execution = load(run / "execution.json")
        check(all((run / name).is_file() for name in OUTPUTS), "Missing execution outputs")
        check(execution == dict(exit=0 if variant == "Baseline" else 1, inputsSha256=sha(run / "inputs.json"), outputs={n: sha(run / n) for n in OUTPUTS}), "Wrong execution or output identity")
        check(execution["outputs"][OUTPUTS[2]] == execution["outputs"][OUTPUTS[4]], "Executed CPU copies disagree")
        tree = ET.parse(run / "metadata.trx")
        counters = tree.find(".//t:ResultSummary/t:Counters", NS)
        expected_counters = {n: "0" for n in ["total", "executed", "passed", "failed", "error", "timeout", "aborted", "inconclusive", "passedButRunAborted", "notRunnable", "notExecuted", "disconnected", "warning", "completed", "inProgress", "pending"]}
        expected_counters.update(total="2", executed="2", passed="2" if variant == "Baseline" else "0", failed="0" if variant == "Baseline" else "2")
        check(counters is not None and counters.attrib == expected_counters, "Wrong execution counters")
        summary = tree.find(".//t:ResultSummary", NS)
        check(summary.get("outcome") == ("Completed" if variant == "Baseline" else "Failed"), "Wrong summary outcome")
        rows, definitions = tree.findall(".//t:UnitTestResult", NS), tree.findall(".//t:UnitTest", NS)
        names = {f"Copper68k.Tests.Synthetic.SyntheticM68020RteMetadataTests.{route}PrivateMetadataControls" for route in ["Scalar", "Batch"]}
        check(len(rows) == len(definitions) == 2 and {r.get("testName") for r in rows} == names, "Missing, empty or substituted test selection")
        lookup = {d.get("id"): d for d in definitions}
        check(len(lookup) == 2, "Duplicate test definitions")
        for row in rows:
            check(row.get("testId") in lookup, "Missing matching test definition")
            definition = lookup[row.get("testId")]
            method = definition.find("t:TestMethod", NS)
            check(method is not None, "Missing loaded test method")
            check(row.get("outcome") == ("Passed" if variant == "Baseline" else "Failed"), "Wrong named outcome")
            check(definition.get("name") == row.get("testName") == method.get("className") + "." + method.get("name"), "Wrong loaded method")
            check(Path(definition.get("storage")).resolve() == Path(method.get("codeBase")).resolve() == (run / OUTPUTS[3]).resolve(), "Wrong loaded test assembly")
        bad = [] if variant == "Baseline" else MUTATIONS[variant][1]
        reports, mismatches = {}, 0
        for model, route in itertools.product(MODELS, ["scalar", "batch"]):
            name = f"{model}-rte-private-metadata-{route}.json"
            check((run / name).is_file(), "Missing coverage report")
            report = load(run / name)
            combinations, identifiers = {}, []
            for master, high, form in itertools.product([False, True], [False, True], FORMS):
                key = f"{model}/RTE/private-C021/{form}/master={master}/high={high}"
                combinations[key] = {"mismatching" if form in bad else "passing": 32}
                if form in bad:
                    identifiers += [f"{key}/op=4E73/ccr={ccr:02X}" for ccr in range(32)]
            count = len(identifiers)
            expected = dict(schema=1, model=model, group=f"rte-private-metadata-{route}", counts=dict(passing=1280-count, mismatching=count, unsupported=0, untested=0), logicalCases=1280, xunitBatches=1, combinations=combinations)
            check(set(report) == set(expected) | {"failures"} and all(report[k] == v for k, v in expected.items()), "Missing, empty or incorrect coverage combinations")
            check([f["id"] for f in report["failures"]] == identifiers, "Wrong ordered failure identifiers")
            for failure in report["failures"]:
                check(set(failure) == {"id", "status", "reason"} and failure["status"] == "mismatching", "Wrong failure schema")
                verify_reason(failure, model)
            reports[name] = sha(run / name)
            mismatches += count
        check({f.name for f in run.glob("*-rte-private-metadata-*.json")} == set(reports), "Unexpected coverage reports")
        check(mismatches == (0 if variant == "Baseline" else MUTATIONS[variant][2]), "Wrong mutation witness count")
        records.append(dict(variant=variant, inputsSha256=sha(run / "inputs.json"), executionSha256=sha(run / "execution.json"), reports=reports, mismatches=mismatches))
    return dict(schema=1, records=records, baselineCases=10240, exactMutationWitnesses=6144, scope="private C021 software contract; no foreign-frame hardware qualification", productionCpuChanged=False, publication=False, roadmapComplete=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--replay", action="store_true", help="Strictly verify retained execution without running tests")
    args = parser.parse_args()
    root = args.output.resolve()
    original = source_bytes()
    if not args.replay:
        execute(root, original)
    proof = verify(root, original)
    if args.replay:
        check(load(root / "proof.json") == proof, "Changed proof record")
    else:
        save(root / "proof.json", proof)
    print("Verified 10,240 private controls and 6,144 exact mutant witnesses", flush=True)


if __name__ == "__main__":
    main()
