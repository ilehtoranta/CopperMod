"""Pinned NOT displacement replacement: fresh isolated execution or strict replay."""
from pathlib import Path
import itertools
import re
from copper68k_consolidation import Audit, check

REPO = Path(__file__).resolve().parent.parent
GROUP = "logical-not-displacement-captured"


def mutate(path, text):
    if path.endswith("M68kAdvancedTimingInterpreter.cs"):
        start = text.index("        private void ExecuteNotLongAddressDisplacement(")
        end = text.index("        private void ExecuteNotByteAddressDisplacement(", start)
        block = text[start:end]
        old = "SetMoveFlags(result, M68kOperandSize.Long);"
        check(block.count(old) == 1, "Missing advanced NOT target")
        return text[:start] + block.replace(old, old + "\n            State.StatusRegister &= 0xffef; // intentional lost X") + text[end:]
    start = text.index("        private bool ExecuteLine4Unary(")
    end = text.index("\n        /// <summary>", start)
    block = text[start:end]
    old = "SetLogicFlags(value, size);"
    check(block.count(old) == 1, "Missing base NOT target")
    return text[:start] + block.replace(old, old + "\n                    if (mode == 5 && size == M68kOperandSize.Long) State.StatusRegister &= 0xffef; // intentional lost X") + text[end:]


def report(model, mode):
    combinations, failures = {}, []
    for value, displacement, register, supervisor, ccr in itertools.product(
            [0, 0xffffffff, 0x92345678], [-8, 8], range(8), [False, True], range(32)):
        key = f"{model}/NOT/4/d16(A{register})/captured/super={supervisor}"
        failed = mode == "Mutation" and bool(ccr & 16)
        status = "mismatching" if failed else "passing"
        counts = combinations.setdefault(key, {})
        counts[status] = counts.get(status, 0) + 1
        if failed:
            result = (~value) & 0xffffffff
            expected = (0x2700 if supervisor else 0x700) | (ccr & 16) | (4 if result == 0 else 0) | (8 if result & 0x80000000 else 0)
            failures.append(dict(id=f"{key}/op={0x46a8 | register:04X}/v={value:08X}/disp={displacement}/ccr={ccr:02X}", status=status,
                                 reason=f"SR expected {expected:04X}, actual {expected & ~16:04X}, mask=FFFF"))
    return dict(schema=1, model=model, group=GROUP,
                counts=dict(passing=3072-len(failures), mismatching=len(failures), unsupported=0, untested=0),
                logicalCases=3072, xunitBatches=1, combinations=combinations, failures=failures)


class NotAudit(Audit):
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
        check(len(names) == 75, "Incomplete pinned HDF class")
        return names


def witness_error(name, message):
    check(re.search(r"Expected:\s*10008\b", message) and re.search(r"Actual:\s*9992\b", message), "Wrong historical NOT defect")


def make_audit():
    return NotAudit(dict(repo=REPO, producer=Path(__file__).resolve(), pin="3dd2640cd5f112a42efbb51a42847d830eb000e4",
        legacy="Copper68k.Tests/M68020HdfBootTests.cs", legacy_class="Copper68k.Tests.M68020HdfBootTests", legacy_cases=75,
        witness="NotLongDisplacementUpdatesOnlyTheLongAndPreservesExtend", witness_cases=4,
        fixture="Copper68k.Tests/Synthetic/SyntheticNotDisplacementTests.cs", shared_class="Copper68k.Tests.Synthetic.SyntheticNotDisplacementTests",
        method="LongNotDisplacementPreservesExtendAndSurroundingMemory", group=GROUP,
        mutation_files=["Copper68k/M68kCore.cs", "Copper68k/M68kAdvancedTimingInterpreter.cs"], mutate=mutate, report=report,
        witness_outcome=lambda name: "Failed", witness_error=witness_error,
        description="Prove NOT displacement shared coverage before exact four-row retirement."))


if __name__ == "__main__":
    make_audit().main()
