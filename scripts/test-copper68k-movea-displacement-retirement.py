"""Pinned aliased MOVEA displacement replacement; isolated execution and replay."""
from pathlib import Path
import itertools
import re
from copper68k_consolidation import Audit, check
REPO = Path(__file__).resolve().parent.parent
GROUP = "movea-displacement-captured"

def mutate(path, text):
    if path.endswith("M68kAdvancedTimingInterpreter.cs"):
        start = text.index("        private void ExecuteMoveWordAddressDisplacementToAddress(")
        end = text.index("\n        private ", start + 20)
        block = text[start:end]
        old = "unchecked((uint)(int)(short)ReadWord("
        check(block.count(old) == 1, "Missing advanced MOVEA target")
        return text[:start] + block.replace(old, "unchecked((uint)ReadWord(") + text[end:]
    pattern = r"((?:operand\.Size|(?<!\.)Size) == M68kOperandSize.Word\r?\n\s+\? )M68kCpuState.SignExtend\(value, M68kOperandSize.Word\)"
    check(len(re.findall(pattern, text)) == 2, "Missing base MOVEA targets")
    return re.sub(pattern, lambda m: m[1] + "value /* intentional zero extension */", text)

def report(model, mode):
    combinations, failures = {}, []
    for value, displacement, register, supervisor, ccr in itertools.product(
            [0x8001, 0x7fff, 0], [-8, 8], range(8), [False, True], range(32)):
        key = f"{model}/MOVEA/2/d16(A{register})->A{register}/captured-displacement/super={supervisor}/disp={displacement}"
        failed = mode == "Mutation" and value == 0x8001
        status = "mismatching" if failed else "passing"
        counts = combinations.setdefault(key, {})
        counts[status] = counts.get(status, 0) + 1
        if failed:
            failures.append(dict(id=f"{key}/op={0x3068 | register << 9 | register:04X}/v={value:08X}/ccr={ccr:02X}", status=status,
                                 reason=f"A{register} expected FFFF8001, actual 00008001"))
    return dict(schema=1, model=model, group=GROUP,
                counts=dict(passing=3072-len(failures), mismatching=len(failures), unsupported=0, untested=0),
                logicalCases=3072, xunitBatches=1, combinations=combinations, failures=failures)

class MoveaAudit(Audit):
    def legacy_names(self):
        names = {}
        for match in re.finditer(r"public void (\w+)\(\s*([^)]*)\)", self.original):
            method, signature = match.groups()
            start = self.original.rfind("[Theory]", 0, match.start())
            parameters = [v.strip().split()[-1] for v in signature.split(",")]
            if method == "AddLongPcDisplacementReadsMemoryFromExtensionPcAndSetsArithmeticFlags":
                rows = itertools.product(["M68020", "M68EC020", "M68030", "M68040"], [-128, 128],
                                         [(0, 1, 1, 0), (0xffffffff, 1, 0, 21), (0x7fffffff, 1, 0x80000000, 10), (0x80000000, 0x80000000, 0, 23)])
                values = [[m, str(d), *map(str, arithmetic)] for m, d, arithmetic in rows]
            else:
                rows = re.findall(r"\[InlineData\(([^)]*)\)\]", self.original[start:match.start()])
                values = [[v.strip().split('.')[-1] if v.strip().startswith('M68kCpuModel.') else str(int(v.strip(), 0)) for v in row.split(',')] for row in rows]
            check(bool(values), "Missing pinned legacy rows")
            for value in values:
                check(len(value) == len(parameters), "Wrong pinned parameter inventory")
                name = self.s['legacy_class'] + '.' + method + '(' + ', '.join(f'{k}: {v}' for k, v in zip(parameters, value)) + ')'
                check(name not in names, "Duplicate pinned row")
                names[name] = 'Passed'
        check(len(names) == 67, "Incomplete pinned HDF class")
        return names


def witness_error(name, message):
    check(re.search(r"Expected:\s*4294934529\b", message) and re.search(r"Actual:\s*32769\b", message), "Wrong historical MOVEA sign extension")

def make_audit():
    return MoveaAudit(dict(repo=REPO, producer=Path(__file__).resolve(), pin="56c0a69202d0b77a76ae7436a77f5a546a14adc2",
        legacy="Copper68k.Tests/M68020HdfBootTests.cs", legacy_class="Copper68k.Tests.M68020HdfBootTests", legacy_cases=67,
        witness="MoveaWordDisplacementSignExtendsBeforeOverwritingAliasedBase", witness_cases=4,
        fixture="Copper68k.Tests/Synthetic/SyntheticMoveaDisplacementTests.cs", shared_class="Copper68k.Tests.Synthetic.SyntheticMoveaDisplacementTests",
        method="CapturedWordMoveaReadsAliasedDisplacementBeforeSignExtending", group=GROUP,
        mutation_files=["Copper68k/M68kCore.cs", "Copper68k/M68kAdvancedTimingInterpreter.cs"], mutate=mutate, report=report,
        witness_outcome=lambda name: "Failed", witness_error=witness_error,
        description="Prove aliased word MOVEA displacement shared coverage before exact four-row retirement."))

if __name__ == "__main__": make_audit().main()
