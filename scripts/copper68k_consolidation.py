"""Internal shared runner for pinned instruction-regression retirement proofs."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET

MODELS = ["68000", "68010", "68EC020", "68020", "68030", "68040", "68060", "A1200"]
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
EXECUTED_ASSEMBLIES = (
    "build/bin/Copper68k/release/Copper68k.dll",
    "build/bin/Copper68k.Tests/release/Copper68k.Tests.dll",
    "build/bin/Copper68k.Tests/release/Copper68k.dll",
)


def check(ok, reason):
    if not ok:
        raise ValueError(reason)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


class Audit:
    def __init__(self, spec):
        self.s = spec
        self.repo = spec["repo"]
        self.original = subprocess.check_output(["git", "show", spec["pin"] + ":" + spec["legacy"]], cwd=self.repo).decode("utf-8-sig").replace("\r\n", "\n")
        method = self.original.index("    public void " + spec["witness"])
        start = self.original.rfind("    [Theory]\n", 0, method)
        end = self.original.index("    [Theory]\n", method)
        check(start >= 0, "Missing retirement boundary")
        self.reduced = self.original[:start] + self.original[end:]

    def producers(self):
        return {str(p.relative_to(self.repo)): sha(p) for p in [Path(__file__).resolve(), self.s["producer"]]}

    def sources(self, mode):
        paths = subprocess.check_output(["git", "ls-files", "Copper68k", "Copper68k.Tests"], cwd=self.repo, text=True).splitlines()
        paths = sorted(set(paths + [self.s["fixture"]]))
        result = {p: (self.repo / p).read_bytes() for p in paths if Path(p).suffix in [".cs", ".csproj"]}
        current = result[self.s["legacy"]].decode("utf-8-sig").replace("\r\n", "\n")
        check(current in [self.original, self.reduced], "Wrong legacy retirement scope")
        if mode == "Current":
            check(current == self.reduced, "Retirement not applied")
        else:
            result[self.s["legacy"]] = self.original.encode()
        if mode == "Mutation":
            for p in self.s["mutation_files"]:
                result[p] = self.s["mutate"](p, result[p].decode("utf-8-sig")).encode()
        return result

    def protected(self):
        paths = ["Copper68k/bin/Release/net10.0/Copper68k.dll", "Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll"]
        return {p: sha(self.repo / p) for p in paths if (self.repo / p).exists()}

    @staticmethod
    def inventory(source):
        return {p.relative_to(source).as_posix(): sha(p) for p in source.rglob("*")
                if p.suffix in [".cs", ".csproj"] and not {"bin", "obj"}.intersection(p.parts)}

    def command(self, root, mode):
        selection = self.s["witness"] if mode == "Mutation" else self.s["legacy_class"]
        return ["dotnet", "test", str(root / "source/Copper68k.Tests/Copper68k.Tests.csproj"), "-c", "Release",
                "--artifacts-path", str(root / "build"), "--filter", f"FullyQualifiedName~{self.s['method']}|FullyQualifiedName~{selection}",
                "--logger", "trx;LogFileName=audit.trx", "--results-directory", str(root)]

    @staticmethod
    def assemblies(root):
        check(all((root / path).is_file() for path in EXECUTED_ASSEMBLIES), "Missing executed assembly")
        hashes = {path: sha(root / path) for path in EXECUTED_ASSEMBLIES}
        check(hashes[EXECUTED_ASSEMBLIES[0]] == hashes[EXECUTED_ASSEMBLIES[2]], "Executed CPU copies disagree")
        return hashes

    def execute(self, root, mode):
        for p, contents in self.sources(mode).items():
            target = root / "source" / p
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(contents)
        args = self.command(root, mode)
        data = dict(schema=2, mode=mode, pin=self.s["pin"], producers=self.producers(), sources=self.inventory(root / "source"), protected=self.protected(), command=args)
        env = dict(os.environ, COPPER68K_SYNTHETIC_MODELS=",".join(MODELS), COPPER68K_SYNTHETIC_REPORT_DIR=str(root))
        print(mode + ": executing shared coverage and pinned legacy selection", flush=True)
        with (root / "execution.log").open("w", encoding="utf-8") as log:
            data["testExit"] = subprocess.run(args, cwd=self.repo, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
        data["logSha256"] = sha(root / "execution.log")
        data["executedAssemblies"] = self.assemblies(root)
        save(root / "inputs.json", data)

    def legacy_names(self):
        names = {}
        for match in re.finditer(r"public void (\w+)\(([^)]*)\)", self.original):
            method, signature = match.groups()
            start = max(self.original.rfind("[Theory]", 0, match.start()), self.original.rfind("[Fact]", 0, match.start()))
            parameters = [p.strip().split()[-1] for p in signature.split(",")] if signature else []
            rows = re.findall(r"\[InlineData\(([^)]*)\)\]", self.original[start:match.start()]) if parameters else [""]
            check(bool(rows), "Missing historical rows")
            for row in rows:
                values = []
                for v in row.split(",") if row else []:
                    v = v.strip()
                    values.append(v.title() if v in ["true", "false"] else v.split(".")[-1] if v.startswith("M68kCpuModel.") else str(int(v.rstrip("uU"), 0)))
                check(len(values) == len(parameters), "Wrong historical parameters")
                suffix = "(" + ", ".join(f"{p}: {v}" for p, v in zip(parameters, values)) + ")" if parameters else ""
                key = self.s["legacy_class"] + "." + method + suffix
                check(key not in names, "Duplicate historical row")
                names[key] = "Passed"
        check(len(names) == self.s["legacy_cases"], "Incomplete original class")
        return names

    def verify(self, root, mode):
        data = load(root / "inputs.json")
        check(data["schema"] == 2 and data["mode"] == mode and data["pin"] == self.s["pin"] and data["producers"] == self.producers(), "Wrong producer or scope")
        ids = {p: hashlib.sha256(v).hexdigest() for p, v in self.sources(mode).items()}
        check(data["sources"] == ids, "Wrong baseline or mutation identity")
        check(self.inventory(root / "source") == ids, "Incomplete or changed source inventory")
        check(data["protected"] == self.protected(), "Wrong protected identities")
        check(data["command"] == self.command(root, mode), "Wrong execution command")
        check(data["testExit"] == (1 if mode == "Mutation" else 0) and data["logSha256"] == sha(root / "execution.log"), "Wrong execution or log")
        check(set(data.get("executedAssemblies", {})) == set(EXECUTED_ASSEMBLIES), "Wrong executed assembly manifest")
        check(data["executedAssemblies"] == self.assemblies(root), "Changed executed assembly")
        names = self.legacy_names()
        witnesses = {n for n in names if self.s["witness"] in n}
        check(len(witnesses) == self.s["witness_cases"], "Wrong witness roster")
        if mode == "Mutation":
            names = {n: self.s["witness_outcome"](n) for n in witnesses}
        elif mode == "Current":
            names = {n: v for n, v in names.items() if n not in witnesses}
        names.update({f'{self.s["shared_class"]}.{self.s["method"]}(modelId: "{m}")': "Failed" if mode == "Mutation" else "Passed" for m in MODELS})
        tree = ET.parse(root / "audit.trx")
        tests = tree.findall(".//t:UnitTestResult", NS)
        check(len(tests) == len(names) and {t.get("testName") for t in tests} == set(names), "Wrong or empty execution selection")
        definitions = tree.findall(".//t:UnitTest", NS)
        expected_storage = (root / EXECUTED_ASSEMBLIES[1]).resolve()
        check(len(definitions) == len(names) and
              {d.get("id"): d.get("name") for d in definitions} ==
              {t.get("testId"): t.get("testName") for t in tests} and
              len({d.get("id") for d in definitions}) == len(names) and
              all(d.get("storage") and Path(d.get("storage")).resolve() == expected_storage for d in definitions),
              "Wrong loaded test assembly or definitions")
        for definition in definitions:
            method = definition.find("t:TestMethod", NS)
            check(method is not None and method.get("codeBase") and
                  Path(method.get("codeBase")).resolve() == expected_storage and
                  method.get("className", "") + "." + method.get("name", "") == definition.get("name").split("(", 1)[0],
                  "Wrong loaded test method identity")
        check(all(t.get("outcome") == names[t.get("testName")] for t in tests), "Wrong execution outcomes")
        for t in tests:
            name = t.get("testName")
            if mode == "Mutation" and name in witnesses and names[name] == "Failed":
                self.s["witness_error"](name, t.find("t:Output/t:ErrorInfo/t:Message", NS).text)
        counters = tree.find("t:ResultSummary/t:Counters", NS)
        failed = sum(v == "Failed" for v in names.values())
        check(counters is not None and all(int(counters.get(k, -1)) == v for k, v in dict(total=len(names), executed=len(names), passed=len(names)-failed, failed=failed, notExecuted=0).items()), "Wrong TRX counters")
        stdout = "\n".join(t.text or "" for t in tree.findall(".//t:UnitTestResult/t:Output/t:StdOut", NS))
        reports, passed, missed = [], 0, 0
        for model in MODELS:
            path = root / f"{model}-{self.s['group']}.json"
            report = load(path)
            expected = self.s["report"](model, mode)
            check(report == expected, "Wrong report keys weights or failures")
            matches = re.findall(re.escape(f"{model}/{self.s['group']}: ") + r"(\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=1, combinations=(\d+)", stdout)
            check(len(matches) == 1 and json.loads(matches[0][0]) == expected["counts"] and int(matches[0][1]) == expected["logicalCases"] and int(matches[0][2]) == len(expected["combinations"]), "TRX report disagreement")
            reports.append(dict(path=path.name, sha256=sha(path)))
            passed += expected["counts"]["passing"]
            missed += expected["counts"]["mismatching"]
        check({p.name for p in root.glob(f"*-{self.s['group']}.json")} == {r["path"] for r in reports}, "Foreign report selection")
        return dict(mode=mode, passing=passed, mismatching=missed, executions=len(names), reports=reports, executedAssemblies=data["executedAssemblies"], inputsSha256=sha(root / "inputs.json"), trxSha256=sha(root / "audit.trx"))

    def integrity(self, output, validate_only):
        definitions = [("MissingFixture", "Incomplete or changed source inventory"), ("WrongProducer", "Wrong producer or scope"),
                       ("EmptySelection", "Wrong or empty execution selection"), ("MissingWitness", "Wrong or empty execution selection"),
                       ("WrongWeight", "Wrong report keys weights or failures"), ("WrongFailure", "Wrong report keys weights or failures"),
                       ("ChangedMutationAndManifest", "Wrong baseline or mutation identity"), ("WrongCommand", "Wrong execution command"),
                       ("WrongCounters", "Wrong TRX counters"),
                       ("MissingAssemblyManifest", "Wrong executed assembly manifest"),
                       ("MissingTestAssembly", "Missing executed assembly"),
                       ("MissingCpuAssembly", "Missing executed assembly"),
                       ("ChangedTestAssembly", "Changed executed assembly"),
                       ("ChangedCpuAssembly", "Executed CPU copies disagree"),
                       ("WrongLoadedAssembly", "Wrong loaded test assembly or definitions"),
                       ("MissingTestDefinition", "Wrong loaded test assembly or definitions"),
                       ("WrongTestDefinitionId", "Wrong loaded test assembly or definitions"),
                       ("MissingTestMethod", "Wrong loaded test method identity"),
                       ("WrongMethodAssembly", "Wrong loaded test method identity"),
                       ("WrongMethodName", "Wrong loaded test method identity")]
        records = []
        for name, reason in definitions:
            target = output / "integrity" / name
            if not validate_only:
                parent = output / "Mutation"
                shutil.copytree(parent / "source", target / "source", ignore=shutil.ignore_patterns("bin", "obj"))
                for path in [parent / "inputs.json", parent / "execution.log", parent / "audit.trx", *parent.glob(f"*-{self.s['group']}.json")]:
                    shutil.copyfile(path, target / path.name)
                for relative in EXECUTED_ASSEMBLIES:
                    path = target / relative
                    path.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copyfile(parent / relative, path)
                # A relocated control has its own copied assembly. Rebase only
                # the recorded path before applying its single corruption.
                tree = ET.parse(target / "audit.trx")
                for definition in tree.findall(".//t:UnitTest", NS):
                    definition.set("storage", str((target / EXECUTED_ASSEMBLIES[1]).resolve()))
                    definition.find("t:TestMethod", NS).set("codeBase", str((target / EXECUTED_ASSEMBLIES[1]).resolve()))
                tree.write(target / "audit.trx")
                data = load(target / "inputs.json")
                data["command"] = self.command(target, "Mutation")
                if name == "MissingFixture":
                    path = target / "source" / self.s["fixture"]
                    check(path.resolve().is_relative_to(target.resolve()), "Unsafe integrity path")
                    path.unlink()
                elif name == "WrongProducer":
                    data["producers"] = {}
                elif name == "ChangedMutationAndManifest":
                    key = self.s["mutation_files"][0]
                    path = target / "source" / key
                    path.write_text(path.read_text(encoding="utf-8-sig") + "\n// unrelated defect\n", encoding="utf-8")
                    data["sources"][key] = sha(path)
                elif name == "WrongCommand":
                    data["command"][data["command"].index("--filter") + 1] = "FullyQualifiedName~Nothing"
                elif name == "MissingAssemblyManifest":
                    data.pop("executedAssemblies")
                elif name in ["MissingTestAssembly", "MissingCpuAssembly"]:
                    path = target / EXECUTED_ASSEMBLIES[1 if name == "MissingTestAssembly" else 0]
                    check(path.resolve().is_relative_to(target.resolve()), "Unsafe integrity path")
                    path.unlink()
                elif name in ["ChangedTestAssembly", "ChangedCpuAssembly"]:
                    path = target / EXECUTED_ASSEMBLIES[1 if name == "ChangedTestAssembly" else 2]
                    with path.open("ab") as stream:
                        stream.write(b"intentional integrity corruption")
                elif name in ["WrongLoadedAssembly", "MissingTestDefinition", "WrongTestDefinitionId"]:
                    tree = ET.parse(target / "audit.trx")
                    definitions = tree.find("t:TestDefinitions", NS)
                    if name == "WrongLoadedAssembly":
                        definitions[0].set("storage", str((parent / EXECUTED_ASSEMBLIES[1]).resolve()))
                    elif name == "MissingTestDefinition":
                        definitions.remove(definitions[0])
                    else:
                        definitions[0].set("id", "00000000-0000-0000-0000-000000000000")
                    tree.write(target / "audit.trx")
                elif name in ["MissingTestMethod", "WrongMethodAssembly", "WrongMethodName"]:
                    tree = ET.parse(target / "audit.trx")
                    definition = tree.find("t:TestDefinitions/t:UnitTest", NS)
                    method = definition.find("t:TestMethod", NS)
                    if name == "MissingTestMethod":
                        definition.remove(method)
                    elif name == "WrongMethodAssembly":
                        method.set("codeBase", str((parent / EXECUTED_ASSEMBLIES[1]).resolve()))
                    else:
                        method.set("name", "CompletelyDifferentInstruction")
                    tree.write(target / "audit.trx")
                elif name in ["EmptySelection", "MissingWitness", "WrongCounters"]:
                    tree = ET.parse(target / "audit.trx")
                    if name == "WrongCounters":
                        tree.find("t:ResultSummary/t:Counters", NS).set("failed", "0")
                    else:
                        results = tree.find("t:Results", NS)
                        for test in list(results):
                            if name == "EmptySelection" or self.s["witness"] in test.get("testName", ""):
                                results.remove(test)
                    tree.write(target / "audit.trx")
                else:
                    path = target / f"68020-{self.s['group']}.json"
                    report = load(path)
                    if name == "WrongWeight":
                        report["logicalCases"] += 1
                    else:
                        report["failures"][0]["reason"] = "unrelated failure"
                    save(path, report)
                save(target / "inputs.json", data)
            try:
                self.verify(target, "Mutation")
            except ValueError as error:
                check(str(error) == reason, "Wrong corruption rejection: " + name + ": " + str(error))
                records.append(dict(name=name, reason=reason))
            else:
                raise ValueError("Corrupted evidence accepted: " + name)
        return records

    def main(self):
        parser = argparse.ArgumentParser(description=self.s["description"])
        parser.add_argument("--output", type=Path, required=True)
        parser.add_argument("--validate-only", action="store_true")
        parser.add_argument("--prepare", action="store_true")
        parser.add_argument("--finish-retirement", action="store_true")
        args = parser.parse_args()
        check(sum([args.validate_only, args.prepare, args.finish_retirement]) <= 1, "Conflicting audit modes")
        if not args.prepare:
            check((self.repo / self.s["legacy"]).read_text(encoding="utf-8-sig") == self.reduced, "Retirement not applied exactly")
        output = args.output.resolve()
        if not args.validate_only and not args.finish_retirement:
            check(not output.exists(), "Use fresh audit output")
            output.mkdir(parents=True)
        entries = []
        for mode in ["Clean", "Mutation"] + ([] if args.prepare else ["Current"]):
            root = output / mode
            if not args.validate_only and (not args.finish_retirement or mode == "Current"):
                root.mkdir()
                self.execute(root, mode)
            entries.append(self.verify(root, mode))
            print(f"{mode}: {entries[-1]['passing']} passing / {entries[-1]['mismatching']} precise mismatches", flush=True)
        proof = dict(schema=2, producers=self.producers(), pin=self.s["pin"], entries=entries,
                     integrity=self.integrity(output, args.validate_only or args.finish_retirement), retiredRows=self.s["witness_cases"] if not args.prepare else 0,
                     productionCpuChanged=False, publication=False, roadmapComplete=False)
        path = output / ("preparation-proof.json" if args.prepare else "proof.json")
        if args.validate_only:
            check(load(path) == proof, "Changed aggregate proof")
        else:
            save(path, proof)
        print("Consolidation proof SHA-256: " + sha(path))
