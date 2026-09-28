using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed partial class NativeGraphicsDisplayInfoDataAdmissionTests
{
    public static IEnumerable<object[]> RegisteredMonitorInfoRoutes()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInit in new[] { false, true })
        foreach (var ntsc in new[] { false, true })
        foreach (var oppositeDefault in new[] { false, true })
            yield return new object[] { relocated, autoInit, ntsc, oppositeDefault };
    }

    [Theory]
    [MemberData(nameof(RegisteredMonitorInfoRoutes))]
    public void NativeRegisteredMonitorInfoUsesRawFamilyRecordForEveryAlias(
        bool relocated, bool autoInit, bool ntsc, bool oppositeDefault)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal,
            includeNativeRuntimeDescriptor: true);
        fixture.SeedDefaultMonitor(0xBAD00001);
        fixture.SeedOwnedMonitorPositions(0xFFF90009, 0x80007FFF, 0x000AFFF6, 0xFFFF0001);
        var defaultNtsc = ntsc ^ oppositeDefault;
        fixture.SeedMonitorRegistrations(1, 0xDEADBEEF, defaultNtsc ? 0x11000u : 0x21000u);
        var databaseBefore = fixture.CaptureDatabase();
        foreach (var (key, _) in GraphicsDisplayInfoTransferBoundaryTests.CapturedOcsEcsDepths)
        foreach (var owner in new uint[] { 0, 0x1000, 0x11000, 0x21000 })
        foreach (var useHandle in new[] { false, true })
        {
            var id = owner | key;
            var selectedNtsc = owner < 0x10000 ? defaultNtsc : owner == 0x11000;
            var expected = RegisteredMonitorRecord(selectedNtsc);
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(useHandle ? id == 0 ? 0xFFFFFFFEu : id : 0,
                0x301, 96, 0x80002000, useHandle ? 0xFFFFFFFFu : id);
            AssertRegisteredMonitorResult(fixture, result, 0x301, 88);
            fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), 0x301, expected);
            AssertNoDestinationAccess(result, 0xBAD00001, 0xA0);
            AssertNoDestinationAccess(result, 0xDEADBEEF, 0xA0);
        }
        Assert.Equal(databaseBefore, fixture.CaptureDatabase());
    }

    [Theory]
    [MemberData(nameof(NativeMntrTransferEdgeCases))]
    public void NativeRegisteredMonitorInfoPreservesPartialFieldDependenciesAndRawPointers(
        bool relocated, bool autoInit, bool ntsc, uint tag)
    {
        using var fixture = new Fixture(relocated, autoInit,
            ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal,
            includeNativeRuntimeDescriptor: true);
        fixture.SeedDefaultMonitor(0xBAD00001);
        fixture.SeedOwnedMonitorPositions(0xFFF90009, 0x80007FFF, 0x000AFFF6, 0xFFFF0001);
        var point = 0x00600000u + (uint)GraphicsDisplayDatabase.NativeMonitorPositionsOffset + (ntsc ? 4u : 16u);
        var slot = 0x00600000u + (uint)GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset + (ntsc ? 0u : 4u);
        foreach (var pointer in new uint[] { 0, 1, 0x2001, 0xFFFFFF60, 0xDEADBEEF, uint.MaxValue })
        foreach (var requested in new uint[] { 1, 15, 16, 17, 20, 21, 24, 25, 43, 44, 76, 80, 81, 84, 85, 87, 88, 96, uint.MaxValue })
        {
            fixture.SeedMonitorRegistrations(pointer, pointer, ntsc ? 0x11000u : 0x21000u);
            var expected = RegisteredMonitorRecord(ntsc);
            BinaryPrimitives.WriteUInt32BigEndian(expected.AsSpan(16), pointer);
            var count = Math.Min(requested, 88u);
            var before = fixture.CaptureMemory();
            var result = fixture.Invoke(0x1000, 0x301, requested, tag, 0xDEADBEEF);
            AssertRegisteredMonitorResult(fixture, result, 0x301, count);
            fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), 0x301, expected.Take((int)count).ToArray());
            AssertExactPointerPrefixWrites(result, 0x301, count);
            AssertNoDestinationAccess(result, 0x301 + count, 8);
            if (requested <= 16) AssertNoDestinationAccess(result, slot, 4);
            if (requested <= 20) AssertNoDestinationAccess(result, point, 4);
            if (requested <= 80) AssertNoDestinationAccess(result, point + 4, 4);
        }
    }

    private static byte[] RegisteredMonitorRecord(bool ntsc)
    {
        var record = GraphicsMonitorInfoFamilyRecordTests.Baseline(ntsc);
        BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(16), ntsc ? 1u : 0xDEADBEEFu);
        BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(20), ntsc ? 0xFFF90009u : 0x80007FFFu);
        BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(80), ntsc ? 0x000AFFF6u : 0xFFFF0001u);
        return record;
    }

    [Theory]
    [MemberData(nameof(NativeMntrTransferEdgeCases))]
    public void NativeRegisteredMonitorInfoPreservesOverlapWrapAndInvalidOwnership(
        bool relocated, bool autoInit, bool ntsc, uint tag)
    {
        foreach (var scenario in new[] { "overlap", "final-byte", "first-wrap", "unknown", "invalid-id", "zero-size", "null-output",
                     "word-database", "null-database", "default-family" })
        {
            using var fixture = new Fixture(relocated, autoInit,
                ntsc ? GraphicsLibraryImageProfile.NativeNtsc : GraphicsLibraryImageProfile.NativePal,
                includeNativeRuntimeDescriptor: true);
            fixture.SeedDefaultMonitor(0xBAD00001);
            fixture.SeedOwnedMonitorPositions(0xFFF90009, 0x80007FFF, 0x000AFFF6, 0xFFFF0001);
            fixture.SeedMonitorRegistrations(1, 0xDEADBEEF, ntsc ? 0x11000u : 0x21000u);
            if (scenario is "word-database" or "null-database" or "default-family")
                fixture.CorruptOwnedMonitorState(scenario, ntsc);
            var destination = scenario switch {
                "overlap" => 0x00600000u + (uint)GraphicsDisplayDatabase.NativeMonitorPositionsOffset,
                "final-byte" => 0xFFFFFFA8u, "first-wrap" => 0xFFFFFFA9u, "null-output" => 0u, _ => 0x301u };
            var before = fixture.CaptureMemory();
            var databaseBefore = fixture.CaptureDatabase();
            var requested = scenario == "zero-size" ? 0u : 96u;
            var id = scenario == "unknown" ? 0xDEADBEEFu : scenario == "invalid-id" ? uint.MaxValue : 0x1000u;
            var result = fixture.Invoke(0, destination, requested, tag, id);
            Assert.Empty(result.RegisterDifferences);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(1, result.NativeReturnCount);
            Assert.Equal(scenario == "unknown", fixture.LastUsedProviderTerminal);
            Assert.Equal(0, result.DefaultMonitorReadCount);
            if (scenario is "overlap" or "final-byte")
            {
                AssertRegisteredMonitorResult(fixture, result, destination, 88);
                var expected = RegisteredMonitorRecord(ntsc);
                Assert.Equal(expected, Enumerable.Range(0, 88).Select(index => fixture.ReadOutputByte(destination + (uint)index)));
                if (scenario == "overlap")
                {
                    expected.CopyTo(databaseBefore, GraphicsDisplayDatabase.NativeMonitorPositionsOffset);
                    AssertMemoryEqual(before, fixture.CaptureMemory());
                }
                else fixture.AssertOnlyOutputPrefixChanged(before, fixture.CaptureMemory(), destination, expected);
                Assert.Equal(databaseBefore, fixture.CaptureDatabase());
            }
            else
            {
                Assert.Equal(scenario is "zero-size" or "null-output" or "invalid-id" ? 0u : requested, result.Data0);
                AssertMemoryEqual(before, fixture.CaptureMemory());
                Assert.Equal(databaseBefore, fixture.CaptureDatabase());
                AssertNoDestinationAccess(result, destination, 88);
            }
        }
    }

    private static void AssertRegisteredMonitorResult(Fixture fixture, CallResult result, uint destination, uint count)
    {
        Assert.Equal(count, result.Data0);
        Assert.Equal(0x80002000u, result.Data1);
        Assert.False(result.UsedFallback);
        Assert.False(fixture.LastUsedProviderTerminal);
        Assert.Equal(1, result.NativeReturnCount);
        Assert.Empty(result.RegisterDifferences);
        Assert.Equal(Fixture.StackPointer, result.StackPointer);
        Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
        Assert.Equal(destination, result.Address1);
        Assert.Equal(fixture.GraphicsBase, result.Address6);
        Assert.Equal(0, result.DefaultMonitorReadCount);
        Assert.Equal(0, result.CapabilityReadCount);
    }
}
