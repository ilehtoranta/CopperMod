namespace Copper68k.Tests.Synthetic;

// Independent Boolean and bit-by-bit specifications from M68000PM. Width
// truncation, zero counts and extend propagation never use production helpers.
internal static class LogicalSpecification
{
    public static readonly string[] BinaryFamilies = ["AND", "OR", "EOR", "ANDI", "ORI", "EORI"];
    public static readonly string[] UnaryFamilies = ["CLR", "NEG", "NEGX", "NOT", "TST", "TAS"];
    public static readonly string[] Shifts = ["ASR", "ASL", "LSR", "LSL", "ROXR", "ROXL", "ROR", "ROL"];
    public static uint Binary(string family, uint lhs, uint rhs) => family.StartsWith("AND") ? lhs & rhs : family.StartsWith("OR") ? lhs | rhs : lhs ^ rhs;
    public static (uint Value, ushort Sr) Unary(string family, uint value, int width, ushort sr)
    {
        var mask = MoveSpecification.Mask(width); value &= mask;
        if (family is "NEG" or "NEGX") return ArithmeticSpecification.Binary(0, value, width, sr, true, extend: family == "NEGX");
        var result = family == "CLR" ? 0u : family == "NOT" ? ~value & mask : value;
        return (family == "TAS" ? value | 128 : result, SyntheticExecution.MoveFlags(sr, result, width));
    }
    public static (uint Value, ushort Sr) Shift(string family, uint value, int count, int width, ushort sr)
    {
        var mask = MoveSpecification.Mask(width); var sign = 1u << (width * 8 - 1);
        value &= mask;
        var left = family.EndsWith('L'); var arithmetic = family.StartsWith("AS");
        var rotate = family.StartsWith("RO"); var throughExtend = family.StartsWith("ROX");
        var x = (sr & 16) != 0; var c = throughExtend && count == 0 && x; var v = false;
        for (var step = 0; step < count; step++)
        {
            var oldSign = (value & sign) != 0;
            c = left ? oldSign : (value & 1) != 0;
            var incoming = rotate ? throughExtend ? x : c : !left && arithmetic && oldSign;
            value = left ? ((value << 1) | (incoming ? 1u : 0)) & mask : (value >> 1) | (incoming ? sign : 0);
            if (arithmetic && left && oldSign != ((value & sign) != 0)) v = true;
            if (!rotate || throughExtend) x = c;
        }
        var flags = (x ? 16 : 0) | ((value & sign) != 0 ? 8 : 0) | (value == 0 ? 4 : 0) | (v ? 2 : 0) | (c ? 1 : 0);
        return (value, (ushort)((sr & 0xffe0) | flags));
    }
}
