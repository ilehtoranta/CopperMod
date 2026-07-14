using CopperFloat;

namespace CopperFloat.Tests;

public sealed class ExtF80RepresentationTests
{
    [Theory]
    [InlineData(0x0000, 0x0000_0000_0000_0000, ExtF80Class.Zero)]
    [InlineData(0x8000, 0x0000_0000_0000_0000, ExtF80Class.Zero)]
    [InlineData(0x0000, 0x0000_0000_0000_0001, ExtF80Class.Subnormal)]
    [InlineData(0x3fff, 0x8000_0000_0000_0000, ExtF80Class.Normal)]
    [InlineData(0x7fff, 0x8000_0000_0000_0000, ExtF80Class.Infinity)]
    [InlineData(0x7fff, 0xc000_0000_0000_0001, ExtF80Class.QuietNaN)]
    [InlineData(0x7fff, 0x8000_0000_0000_0001, ExtF80Class.SignalingNaN)]
    [InlineData(0x3fff, 0x4000_0000_0000_0000, ExtF80Class.Unsupported)]
    [InlineData(0x0000, 0x8000_0000_0000_0000, ExtF80Class.Unsupported)]
    public void ClassifiesRawEncoding(ushort signExponent, ulong significand, ExtF80Class expected)
        => Assert.Equal(expected, ExtF80.FromBits(signExponent, significand).Classification);

    [Fact]
    public void BigEndianRoundTripPreservesAllBits()
    {
        var value = ExtF80.FromBits(0xc123, 0x89ab_cdef_0123_4567);
        Span<byte> bytes = stackalloc byte[ExtF80.EncodedSize];

        value.WriteBigEndian(bytes);

        Assert.Equal(value, ExtF80.ReadBigEndian(bytes));
        Assert.Equal(new byte[] { 0xc1, 0x23, 0x89, 0xab, 0xcd, 0xef, 0x01, 0x23, 0x45, 0x67 }, bytes.ToArray());
    }

    [Fact]
    public void LittleEndianRoundTripUsesX87FieldOrder()
    {
        var value = ExtF80.FromBits(0xc123, 0x89ab_cdef_0123_4567);
        Span<byte> bytes = stackalloc byte[ExtF80.EncodedSize];

        value.WriteLittleEndian(bytes);

        Assert.Equal(value, ExtF80.ReadLittleEndian(bytes));
        Assert.Equal(new byte[] { 0x67, 0x45, 0x23, 0x01, 0xef, 0xcd, 0xab, 0x89, 0x23, 0xc1 }, bytes.ToArray());
    }
}
