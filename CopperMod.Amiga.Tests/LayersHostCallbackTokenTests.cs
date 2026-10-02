using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Amiga;
using CopperMod.Amiga.CopperStart.Layers;
using Fixture = CopperMod.Amiga.Tests.CopperStartIntuitionCallbackBootTests;

namespace CopperMod.Amiga.Tests;

/// <summary>
/// Host-only private packet layout and serializer checks. These tests do not
/// execute a return gateway or turn copied packet data into owner authority.
/// </summary>
public sealed class LayersHostCallbackTokenTests
{
    [Fact]
    public void ReturnPacketIsExactlyThirtySixPackTwoBytesWithOnlyValueFields()
    {
        var layout = Assert.IsType<StructLayoutAttribute>(typeof(LayersHostCallbackToken).StructLayoutAttribute);
        Assert.Equal(LayoutKind.Sequential, layout.Value);
        Assert.Equal(2, layout.Pack);
        Assert.Equal(36, Marshal.SizeOf<LayersHostCallbackToken>());
        Assert.Equal((ushort)36, LayersHostCallbackToken.Size);
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<LayersHostCallbackToken>());
        var fields = new (string Name, int Offset)[]
        {
            (nameof(LayersHostCallbackToken.Magic), 0),
            (nameof(LayersHostCallbackToken.Version), 4),
            (nameof(LayersHostCallbackToken.Bytes), 6),
            (nameof(LayersHostCallbackToken.Nonce), 8),
            (nameof(LayersHostCallbackToken.LibraryBase), 12),
            (nameof(LayersHostCallbackToken.Root), 16),
            (nameof(LayersHostCallbackToken.Actor), 20),
            (nameof(LayersHostCallbackToken.Continuation), 24),
            (nameof(LayersHostCallbackToken.RecordGeneration), 28),
            (nameof(LayersHostCallbackToken.ContinuationToken), 32),
        };
        Assert.Equal(fields.Length, typeof(LayersHostCallbackToken)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length);
        foreach (var field in fields)
            Assert.Equal(field.Offset, Marshal.OffsetOf<LayersHostCallbackToken>(field.Name).ToInt32());
    }

    [Theory]
    [InlineData(0x0002_0000u)]
    [InlineData(0x0002_0002u)]
    public void BigEndianCodecRoundTripsEveryFieldAndPreservesBothAdjacentGuards(uint guestAddress)
    {
        using var machine = Fixture.CreateMachine();
        var memory = new LayersTestGuestMemory(machine.Bus);
        var address = APTR.FromPointer(guestAddress);
        var guarded = APTR.FromPointer(guestAddress - 8);
        const uint guardedBytes = LayersHostCallbackToken.Size + 16u;
        Assert.True(memory.IsMapped(guarded, guardedBytes));
        for (var offset = 0; offset < (int)guardedBytes; offset++) memory.WriteUInt8(guarded, offset, 0xA5);
        var expected = Sample();
        LayersHostCallbackTokenCodec.Write(ref memory, address, expected);
        byte[] expectedBytes =
        [
            0x43, 0x53, 0x4C, 0x52, // Magic, CSLR.
            0x00, 0x01, 0x00, 0x24, // Version and complete byte extent.
            0x01, 0x23, 0x45, 0x67, // Nonce.
            0x10, 0x20, 0x30, 0x40, // LibraryBase.
            0x50, 0x60, 0x70, 0x80, // Root.
            0x90, 0xA0, 0xB0, 0xC0, // Actor.
            0xD0, 0xE0, 0xF0, 0x10, // Continuation.
            0x11, 0x22, 0x33, 0x44, // RecordGeneration.
            0xA1, 0xB2, 0xC3, 0xD4, // ContinuationToken.
        ];
        Assert.Equal(LayersHostCallbackToken.Size, expectedBytes.Length);
        Assert.Equal(expectedBytes, ReadBytes(ref memory, address, LayersHostCallbackToken.Size));
        Assert.All(ReadBytes(ref memory, guarded, 8), value => Assert.Equal((byte)0xA5, value));
        Assert.All(ReadBytes(ref memory, APTR.FromPointer(guestAddress + LayersHostCallbackToken.Size), 8),
            value => Assert.Equal((byte)0xA5, value));
        var beforeRead = ReadBytes(ref memory, guarded, guardedBytes);
        var actual = LayersHostCallbackTokenCodec.Read(ref memory, address);
        Assert.Equal(expected, actual);
        Assert.True(LayersHostCallbackTokenCodec.Same(expected, actual));
        Assert.Equal(beforeRead, ReadBytes(ref memory, guarded, guardedBytes));
    }

    [Theory]
    [InlineData(nameof(LayersHostCallbackToken.Magic))]
    [InlineData(nameof(LayersHostCallbackToken.Version))]
    [InlineData(nameof(LayersHostCallbackToken.Bytes))]
    [InlineData(nameof(LayersHostCallbackToken.Nonce))]
    [InlineData(nameof(LayersHostCallbackToken.LibraryBase))]
    [InlineData(nameof(LayersHostCallbackToken.Root))]
    [InlineData(nameof(LayersHostCallbackToken.Actor))]
    [InlineData(nameof(LayersHostCallbackToken.Continuation))]
    [InlineData(nameof(LayersHostCallbackToken.RecordGeneration))]
    [InlineData(nameof(LayersHostCallbackToken.ContinuationToken))]
    public void ExactPacketComparisonRejectsAnIndependentMismatchInEveryField(string field)
    {
        var original = Sample();
        var changed = original;
        switch (field)
        {
            case nameof(LayersHostCallbackToken.Magic): changed.Magic ^= 1u; break;
            case nameof(LayersHostCallbackToken.Version): changed.Version ^= 1; break;
            case nameof(LayersHostCallbackToken.Bytes): changed.Bytes ^= 1; break;
            case nameof(LayersHostCallbackToken.Nonce): changed.Nonce ^= 1u; break;
            case nameof(LayersHostCallbackToken.LibraryBase): changed.LibraryBase = APTR.FromPointer(original.LibraryBase.Raw ^ 1u); break;
            case nameof(LayersHostCallbackToken.Root): changed.Root = APTR.FromPointer(original.Root.Raw ^ 1u); break;
            case nameof(LayersHostCallbackToken.Actor): changed.Actor = APTR.FromPointer(original.Actor.Raw ^ 1u); break;
            case nameof(LayersHostCallbackToken.Continuation): changed.Continuation = APTR.FromPointer(original.Continuation.Raw ^ 1u); break;
            case nameof(LayersHostCallbackToken.RecordGeneration): changed.RecordGeneration ^= 1u; break;
            case nameof(LayersHostCallbackToken.ContinuationToken): changed.ContinuationToken ^= 1u; break;
            default: Assert.Fail($"Unclassified packet field {field}."); break;
        }
        Assert.Equal(Sample(), original);
        Assert.True(LayersHostCallbackTokenCodec.Same(original, Sample()));
        Assert.False(LayersHostCallbackTokenCodec.Same(original, changed));
        Assert.False(LayersHostCallbackTokenCodec.Same(changed, original));
        // Same is exact comparison, not admission: two invalid empty copies
        // also compare equal. The host owner must independently validate them.
        Assert.True(LayersHostCallbackTokenCodec.Same(default, default));
    }

    private static LayersHostCallbackToken Sample() => new()
    {
        Magic = LayersHostCallbackToken.ExpectedMagic,
        Version = LayersHostCallbackToken.CurrentVersion,
        Bytes = LayersHostCallbackToken.Size,
        Nonce = 0x0123_4567,
        LibraryBase = APTR.FromPointer(0x1020_3040),
        Root = APTR.FromPointer(0x5060_7080),
        Actor = APTR.FromPointer(0x90A0_B0C0),
        Continuation = APTR.FromPointer(0xD0E0_F010),
        RecordGeneration = 0x1122_3344,
        ContinuationToken = 0xA1B2_C3D4,
    };

    private static byte[] ReadBytes(ref LayersTestGuestMemory memory, APTR address, uint bytes)
    {
        var result = new byte[checked((int)bytes)];
        for (var offset = 0; offset < result.Length; offset++) result[offset] = memory.ReadUInt8(address, offset);
        return result;
    }
}
