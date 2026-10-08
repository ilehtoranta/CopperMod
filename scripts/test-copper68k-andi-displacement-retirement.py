"""Pinned ANDI displacement replacement: fresh isolated execution or strict replay."""
from pathlib import Path
import itertools
import re
from copper68k_consolidation import Audit, check

REPO = Path(__file__).resolve().parent.parent
GROUP = "logical-andi-displacement-captured"

def mutate(path, text):
    if path.endswith("M68kAdvancedTimingInterpreter.cs"):
        start = text.index("        private void ExecuteAndiLongImmediateToAddressDisplacement(")
        end = text.index("\n        private ", start + 20)
        block = text[start:end]
        old = "SetMoveFlags(result, M68kOperandSize.Long);"
        check(block.count(old) == 1, "Missing advanced ANDI target")
        return text[:start] + block.replace(old, old + "\n            State.StatusRegister &= 0xffef; // intentional lost X") + text[end:]
    pattern = r"(case 0x0200:\r?\n\s+destination &= immediate;\r?\n\s+[^\r\n]+\r?\n\s+SetLogicFlags\(destination, size\);)"
    check(len(re.findall(pattern, text)) == 2, "Missing base ANDI targets")
    return re.sub(pattern, lambda m: m[0] + "\n                    State.StatusRegister &= 0xffef; // intentional lost X", text)

def report(model, mode):
    combinations, failures = {}, []
    for mask, displacement, register, supervisor, ccr in itertools.product(
            [0, 0x80123456, 0x7fffffff], [-8, 8], range(8), [False, True], range(32)):
        key = f"{model}/ANDI/4/d16(A{register})/captured/super={supervisor}"
        failed = mode == "Mutation" and bool(ccr & 16)
        status = "mismatching" if failed else "passing"
        counts = combinations.setdefault(key, {})
        counts[status] = counts.get(status, 0) + 1
        if failed:
            expected = (0x2700 if supervisor else 0x700) | (ccr & 16) | (4 if mask == 0 else 0) | (8 if mask & 0x80000000 else 0)
            failures.append(dict(id=f"{key}/op={0x02a8 | register:04X}/mask={mask:08X}/disp={displacement}/ccr={ccr:02X}", status=status,
                                 reason=f"SR expected {expected:04X}, actual {expected & ~16:04X}, mask=FFFF"))
    return dict(schema=1, model=model, group=GROUP,
                counts=dict(passing=3072-len(failures), mismatching=len(failures), unsupported=0, untested=0),
                logicalCases=3072, xunitBatches=1, combinations=combinations, failures=failures)

class AndiAudit(Audit):
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
        check(len(names) == 71, "Incomplete pinned HDF class")
        return names


def witness_error(name, message):
    check(re.search(r"Expected:\s*10004\b", message) and re.search(r"Actual:\s*9988\b", message), "Wrong historical ANDI lost X")

def make_audit():
    return AndiAudit(dict(repo=REPO, producer=Path(__file__).resolve(), pin="ea18c2c0d2d5e2db69421091b7fc83d7b1fc7596",
        legacy="Copper68k.Tests/M68020HdfBootTests.cs", legacy_class="Copper68k.Tests.M68020HdfBootTests", legacy_cases=71,
        witness="AndiLongDisplacementConsumesLongImmediateAndPreservesExtend", witness_cases=4,
        fixture="Copper68k.Tests/Synthetic/SyntheticAndiDisplacementTests.cs", shared_class="Copper68k.Tests.Synthetic.SyntheticAndiDisplacementTests",
        method="CapturedLongAndiDisplacementConsumesImmediateAndPreservesExtend", group=GROUP,
        mutation_files=["Copper68k/M68kCore.cs", "Copper68k/M68kAdvancedTimingInterpreter.cs"], mutate=mutate, report=report,
        witness_outcome=lambda name: "Failed", witness_error=witness_error,
        description="Prove ANDI displacement shared coverage before exact four-row retirement."))

if __name__ == "__main__": make_audit().main()
