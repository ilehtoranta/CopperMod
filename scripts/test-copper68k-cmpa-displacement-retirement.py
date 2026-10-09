"""Isolated CMPA word/full-address comparison replacement and retirement audit."""
from pathlib import Path
import re
from copper68k_consolidation import Audit, check

REPO = Path(__file__).resolve().parent.parent
GROUP = "cmpa-displacement-consolidation"


def mutate(path, text):
    if path.endswith("M68kAdvancedTimingInterpreter.cs"):
        start = text.index("        private void ExecuteCmpaWordAddressDisplacementToAddress(")
        end = text.index("        private void ExecuteCmpaLongAddressDisplacementToAddress(", start)
        block = text[start:end]
        check(block.count("(int)(short)ReadWord") == 1, "Missing advanced mutation target")
        return text[:start] + block.replace("(int)(short)ReadWord", "ReadWord") + text[end:]
    old = "? M68kCpuState.SignExtend(value, M68kOperandSize.Word)"
    start = text.index("        private bool DecodeArithmetic(ushort opcode)")
    end = text.index("\n        private ", start + 1)
    block = text[start:end]
    check(block.count(old) == 1, "Missing base mutation target")
    return text[:start] + block.replace(old, "? (mode == 5 ? value & 0xffffu : M68kCpuState.SignExtend(value, M68kOperandSize.Word))") + text[end:]


def flags(destination, source, ccr):
    result = (destination - source) & 0xffffffff
    signed_destination = destination - 0x100000000 if destination & 0x80000000 else destination
    signed_source = source - 0x100000000 if source & 0x80000000 else source
    difference = signed_destination - signed_source
    overflow = difference < -0x80000000 or difference > 0x7fffffff
    return (ccr & 16) | (8 if result & 0x80000000 else 0) | (4 if result == 0 else 0) | (2 if overflow else 0) | (1 if destination < source else 0)


def report(model, mode):
    combinations, failures = {}, []
    for supervisor in [False, True]:
        key = f"{model}/CMPA/2/d16(A0)/r1/negative-d16-word-source-full-destination-all-CCR/brief/super={supervisor}"
        passing = mismatching = 0
        for destination in [0xffff, 0xffffffff, 0, 0x7fffffff, 0x80000000]:
            for source in [0xffff, 1, 0x8000, 0x7fff]:
                for ccr in range(32):
                    rhs = source | 0xffff0000 if source & 0x8000 else source
                    expected = (0x2700 if supervisor else 0x700) | flags(destination, rhs, ccr)
                    actual = (0x2700 if supervisor else 0x700) | flags(destination, source if mode == "Mutation" else rhs, ccr)
                    if actual != expected:
                        mismatching += 1
                        failures.append(dict(id=f"{key}/op=B2E8/s={source:08X}/d={destination:08X}/ccr={ccr:02X}", status="mismatching",
                                             reason=f"SR expected {expected:04X}, actual {actual:04X}, mask=FFFF"))
                    else:
                        passing += 1
        combinations[key] = dict(passing=passing, mismatching=mismatching) if mismatching else dict(passing=passing)
    return dict(schema=1, model=model, group=GROUP,
                counts=dict(passing=1280 - len(failures), mismatching=len(failures), unsupported=0, untested=0),
                logicalCases=1280, xunitBatches=1, combinations=combinations, failures=failures)


def witness_outcome(name):
    return "Passed" if "destination: 0," in name else "Failed"


def witness_error(name, message):
    expected, actual = (17, 20) if "destination: 65535," in name else (20, 24)
    check(re.search(r"Expected:\s*" + str(expected) + r"\b", message) and re.search(r"Actual:\s*" + str(actual) + r"\b", message), "Wrong historical failure reason")


def make_audit():
    return Audit(dict(repo=REPO, producer=Path(__file__).resolve(), pin="4ee523df69688c2ded0b4d26567399dc96830ad7",
                      legacy="Copper68k.Tests/M68020AddressSourceTests.cs", legacy_class="Copper68k.Tests.M68020AddressSourceTests", legacy_cases=45,
                      witness="CmpaWordDisplacementComparesSignExtendedSourceAgainstFullAddress", witness_cases=3,
                      fixture="Copper68k.Tests/Synthetic/SyntheticCmpaDisplacementTests.cs", shared_class="Copper68k.Tests.Synthetic.SyntheticCmpaDisplacementTests",
                      method="WordSourceComparesAgainstFullAddressAndPreservesExtend", group=GROUP,
                      mutation_files=["Copper68k/M68kCore.cs", "Copper68k/M68kAdvancedTimingInterpreter.cs"], mutate=mutate, report=report,
                      witness_outcome=witness_outcome, witness_error=witness_error,
                      description="Prove CMPA displacement/full-address shared coverage; fresh output or strict replay."))


if __name__ == "__main__":
    make_audit().main()
