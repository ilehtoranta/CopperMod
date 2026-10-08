"""Reproduce the private candidate in an isolated snapshot; never import it."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parent.parent
MANIFEST = REPO / "scripts/reference/m68020-private-predecrement-manifest.json"
PATCH = REPO / "scripts/reference/m68020-read-write-recovery-candidate.patch"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
CLASSES = ["SyntheticM68020PredecrementReadTests", "SyntheticM68020MemoryReadWriteTests",
           "SyntheticM68020PredecrementWriteTests", "SyntheticM68020MoveWriteHandlerTests"]
SETTINGS = {"COPPER68K_RUN_020_PREDECREMENT_READ": "1", "COPPER68K_RUN_020_MEMORY_READ_WRITE": "1",
            "COPPER68K_RUN_020_PREDECREMENT_WRITE": "1", "COPPER68K_RUN_020_MOVE_WRITE_HANDLERS": "1"}


def check(value, message):
    if not value:
        raise ValueError(message)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def normalized(path):
    return hashlib.sha256(path.read_text(encoding="utf-8-sig").encode()).hexdigest()


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8", newline="\n")


def manifest():
    check(sha(MANIFEST) == "d7ade9a6020cd711f05a8e7b0ef6fd427b223a48c50239ac6949e3d83f85b06c", "Changed pinned reference manifest")
    value = load(MANIFEST)
    check(value["schema"] == 1 and len(value["reports"]) == 520 and len(value["expectedTests"]) == 14,
          "Missing or incomplete pinned audit inventory")
    check(value["logicalCases"] == 3244032 and sum(r["logicalCases"] for r in value["reports"].values()) == 3244032,
          "Invalid logical case inventory")
    check(len(value["baseSourceNormalizedSha256"]) == 233 and len(value["candidateCpuNormalizedSha256"]) == 39,
          "Invalid source inventory")
    check(set(value["expectedTests"].values()) == {"Passed"} and sha(PATCH) == value["patchSha256"],
          "Changed candidate patch or expected outcomes")
    for name in value["baseSourceNormalizedSha256"]:
        check(not Path(name).is_absolute() and ".." not in Path(name).parts and
              name.startswith(("Copper68k/", "Copper68k.Tests/")) and Path(name).suffix in [".cs", ".csproj"],
              "Invalid source path: " + name)
    return value


def command(root):
    return ["dotnet", "test", str(root / "source/Copper68k.Tests/Copper68k.Tests.csproj"), "-c", "Release",
            "--artifacts-path", str(root / "build"), "--filter",
            "|".join("FullyQualifiedName~" + name for name in CLASSES), "--logger",
            "trx;LogFileName=audit.trx", "--results-directory", str(root)]


def expected_sources(value):
    result = {n: h for n, h in value["baseSourceNormalizedSha256"].items() if n.startswith("Copper68k.Tests/")}
    result.update(value["candidateCpuNormalizedSha256"])
    check(len(result) == 235, "Incomplete isolated candidate inventory")
    return result


def execute(root, value):
    check(not root.exists(), "Choose a fresh output directory; existing evidence is never overwritten")
    names = subprocess.check_output(["git", "ls-files", "Copper68k", "Copper68k.Tests"], cwd=REPO, text=True).splitlines()
    names = {n for n in names if Path(n).suffix in [".cs", ".csproj"]}
    check(names == set(value["baseSourceNormalizedSha256"]), "Source inventory changed; use the audited checkpoint or requalify")
    check(all(normalized(REPO / n) == h for n, h in value["baseSourceNormalizedSha256"].items()),
          "Baseline sources changed; use the audited checkpoint or requalify")
    before = {n: sha(REPO / n) for n in sorted(names)}
    assets = {"Copper68k/" + n: sha(REPO / "Copper68k" / n) for n in ["README.md", "copper68k-icon.png"]}
    for name in [*before, *assets]:
        target = root / "source" / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes((REPO / name).read_bytes())
    # Retain the qualified source's intentional whitespace; never repair it.
    for options in [["--check"], []]:
        subprocess.run(["git", "apply", *options, "--whitespace=nowarn", "-p1", str(PATCH)],
                       cwd=root / "source", check=True)
    required = expected_sources(value)
    check(all(normalized(root / "source" / n) == h for n, h in required.items()), "Candidate replay differs from qualified source")
    sources = {n: sha(root / "source" / n) for n in sorted(required)}
    settings = dict(SETTINGS, COPPER68K_SYNTHETIC_REPORT_DIR=str(root))
    inputs = dict(schema=1, sources=sources, assets=assets, baselineSources=before, settings=settings,
                  command=command(root), manifestSha256=sha(MANIFEST), patchSha256=sha(PATCH), producerSha256=sha(Path(__file__)))
    save(root / "inputs.json", inputs)
    environment = {k: v for k, v in os.environ.items() if not k.startswith("COPPER68K_")}
    environment.update(settings)
    print("Executing isolated current-checkout candidate: 14 named tests / 3,244,032 required cases", flush=True)
    with (root / "execution.log").open("w", encoding="utf-8") as log:
        done = subprocess.run(command(root), env=environment, stdout=log, stderr=subprocess.STDOUT)
    assemblies = {str(f.relative_to(root)).replace("\\", "/"): sha(f) for f in (root / "build/bin").rglob("Copper68k*.dll")}
    result = dict(exit=done.returncode, inputsSha256=sha(root / "inputs.json"),
                  sourcesUnchanged=all(sha(root / "source" / n) == h for n, h in sources.items()),
                  productionUnchanged=all(sha(REPO / n) == h for n, h in before.items()),
                  outputs={n: sha(root / n) for n in ["execution.log", "audit.trx"] if (root / n).exists()}, assemblies=assemblies)
    save(root / "execution.json", result)


def validate(root, value):
    inputs, execution = load(root / "inputs.json"), load(root / "execution.json")
    check(inputs["schema"] == 1 and inputs["manifestSha256"] == sha(MANIFEST) and
          inputs["patchSha256"] == sha(PATCH) and inputs["producerSha256"] == sha(Path(__file__)), "Changed audit inputs or command")
    check(execution["exit"] == 0 and execution["sourcesUnchanged"] and execution["productionUnchanged"], "Failed execution or changed sources")
    check(execution["inputsSha256"] == sha(root / "inputs.json") and inputs["command"] == command(root), "Execution/command identity differs")
    check(inputs["settings"] == dict(SETTINGS, COPPER68K_SYNTHETIC_REPORT_DIR=str(root)), "Changed audit settings")
    required = expected_sources(value)
    check(set(inputs["sources"]) == set(required) and all(normalized(root / "source" / n) == h for n, h in required.items()), "Changed candidate source inventory")
    check(set(inputs["baselineSources"]) == set(value["baseSourceNormalizedSha256"]), "Changed baseline source inventory")
    check(set(inputs["assets"]) == {"Copper68k/README.md", "Copper68k/copper68k-icon.png"} and
          all(sha(root / "source" / n) == h for n, h in inputs["assets"].items()), "Missing or changed build assets")
    actual = {f.relative_to(root / "source").as_posix() for pattern in ["*.cs", "*.csproj"] for f in (root / "source").rglob(pattern)}
    check(actual == set(required) and all(sha(root / "source" / n) == h for n, h in inputs["sources"].items()), "Missing, extra or changed source files")
    check(set(execution["outputs"]) == {"execution.log", "audit.trx"} and
          all(sha(root / n) == h for n, h in execution["outputs"].items()), "Changed execution output")
    assemblies = execution["assemblies"]
    paths = {"build/bin/Copper68k/release/Copper68k.dll", "build/bin/Copper68k.Tests/release/Copper68k.dll",
             "build/bin/Copper68k.Tests/release/Copper68k.Tests.dll"}
    check(set(assemblies) == paths and all(sha(root / n) == h for n, h in assemblies.items()), "Missing or changed execution DLLs")
    check(assemblies["build/bin/Copper68k/release/Copper68k.dll"] == assemblies["build/bin/Copper68k.Tests/release/Copper68k.dll"], "CPU copies differ")
    tree = ET.parse(root / "audit.trx")
    rows = tree.findall(".//t:UnitTestResult", NS)
    check(len(rows) == 14 and {r.get("testName"): r.get("outcome") for r in rows} == value["expectedTests"], "Missing, empty, extra or failed selection")
    counters = tree.find(".//t:Counters", NS)
    check(all(int(counters.get(k, -1)) == v for k, v in dict(total=14, passed=14, executed=14, failed=0, notExecuted=0).items()), "Invalid execution counters")
    check(tree.find(".//t:ResultSummary", NS).get("outcome") == "Completed", "Incomplete test run")
    definitions = {d.get("id"): d for d in tree.findall(".//t:UnitTest", NS)}
    check(len(definitions) == 14 and set(definitions) == {r.get("testId") for r in rows}, "Missing or duplicated test definitions")
    for row in rows:
        definition = definitions[row.get("testId")]
        method = definition.find("t:TestMethod", NS)
        cls, name = row.get("testName").split("(")[0].rsplit(".", 1)
        check(definition.get("name") == row.get("testName"), "Wrong loaded test definition")
        check(method.get("className").split(",")[0] == cls and method.get("name") == name, "Wrong loaded method")
        check(Path(definition.get("storage")).resolve() == Path(method.get("codeBase")).resolve() ==
              (root / "build/bin/Copper68k.Tests/release/Copper68k.Tests.dll").resolve(), "Wrong loaded test assembly")
    reports = {f.name for f in root.glob("*.json") if re.match(r"^(68000|68010|68EC020|68020|68030|68040|68060|A1200)-", f.name)}
    check(reports == set(value["reports"]), "Missing or extra required reports")
    for name, record in value["reports"].items():
        check(sha(root / name) == record["sha256"], "Changed architectural cases or results: " + name)
        report = load(root / name)
        count = record["logicalCases"]
        check(count > 0 and report["logicalCases"] == count and report["counts"] ==
              dict(passing=count, mismatching=0, unsupported=0, untested=0) and not report["failures"], "Nonpassing or empty report: " + name)
        check(sum(v.get("passing", 0) for v in report["combinations"].values()) == count and
              all(set(v) == {"passing"} and v["passing"] > 0 for v in report["combinations"].values()), "Invalid combination coverage: " + name)
    return dict(schema=1, manifestSha256=sha(MANIFEST), inputsSha256=sha(root / "inputs.json"), executionSha256=sha(root / "execution.json"),
                sourceInputs=235, cpuInputs=39, namedTests=14, reports=520, logicalCases=3244032,
                fullSuiteQualification=False, hardwareQualification=False, productionImport=False, publication=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=REPO / "artifacts/private-predecrement")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    root = args.output.resolve()
    value = manifest()
    if not args.validate_only:
        execute(root, value)
    result = validate(root, value)
    if args.validate_only:
        check(load(root / "proof.json") == result, "Stored proof differs from strict replay")
    else:
        check(not (root / "proof.json").exists(), "Evidence already exists")
        save(root / "proof.json", result)
    print("Qualified selected private recovery: 3,244,032 cases / 520 reports; full and hardware qualification remain separate", flush=True)


if __name__ == "__main__":
    main()
