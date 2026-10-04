namespace Copper68k.Tests;

// Independent architectural comparison rules, M68000PM 4-69/71, 4-92/96,
// 4-2, 4-141 and 4-170. Undefined bits remain in the actual CPU result;
// only the reference comparator mask changes. Exception identity and defined
// V/N/C bits are independently checked by the native comparator.
internal static class WinUaeArchitecturalFlags
{
    internal static ushort DefinedMask(string family, int exception, ushort resultSr) => family switch
    {
        "CHK.W" or "CHK.L" when exception is -1 or 6 => exception == 6 ? (ushort)0xfff8 : (ushort)0xfff0,
        "CHK2.B" or "CHK2.W" or "CHK2.L" or "CMP2.B" or "CMP2.W" or "CMP2.L"
            when exception is -1 or 6 => 0xfff5,
        "ABCD.B" or "SBCD.B" or "NBCD.B" when exception == -1 => 0xfff5,
        "DIVS.W" or "DIVU.W" or "DIVL.L" when exception == 5 => 0xfff1,
        "DIVS.W" or "DIVU.W" or "DIVL.L" when exception == -1 && (resultSr & 2) != 0 => 0xfff3,
        _ => 0xffff
    };
}
