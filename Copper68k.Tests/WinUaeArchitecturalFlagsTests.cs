namespace Copper68k.Tests;

public sealed class WinUaeArchitecturalFlagsTests
{
    [Theory]
    [InlineData("NOP", -1, 31, 0xffff)]
    [InlineData("CHK.W", -1, 31, 0xfff0)]
    [InlineData("CHK.L", 6, 31, 0xfff8)]
    [InlineData("CHK.W", 3, 31, 0xffff)]
    [InlineData("CHK2.W", 6, 31, 0xfff5)]
    [InlineData("CMP2.B", -1, 31, 0xfff5)]
    [InlineData("CHK2.L", 61, 31, 0xffff)]
    [InlineData("ABCD.B", -1, 31, 0xfff5)]
    [InlineData("SBCD.B", -1, 31, 0xfff5)]
    [InlineData("NBCD.B", -1, 31, 0xfff5)]
    [InlineData("DIVS.W", 5, 31, 0xfff1)]
    [InlineData("DIVU.W", -1, 2, 0xfff3)]
    [InlineData("DIVL.L", -1, 0, 0xffff)]
    [InlineData("DIVS.W", 3, 31, 0xffff)]
    public void MasksOnlyDocumentedUndefinedBits(string family, int exception, int resultSr, int mask)
    {
        var actual = WinUaeArchitecturalFlags.DefinedMask(family, exception, (ushort)resultSr);
        Assert.Equal((ushort)mask, actual);
        Assert.Equal(0xffe0, actual & 0xffe0); // All upper architectural SR bits remain checked.
        Assert.NotEqual(0, actual & 0x10); // X always remains checked for these rules.
    }
}
