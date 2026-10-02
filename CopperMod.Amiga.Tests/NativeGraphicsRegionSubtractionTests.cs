using CopperMod.Amiga.CopperStart;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using Xunit.Abstractions;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsRegionSubtractionTests
{
    private readonly ITestOutputHelper _output;

    public NativeGraphicsRegionSubtractionTests(ITestOutputHelper output) => _output = output;

    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });

    [Fact]
    public void AllOrderedThreeByThreeRectangleSubtractionsMatchPortableOrDeclineWithoutMutation()
    {
        var rectangles = (
            from minX in Enumerable.Range(0, 3)
            from maxX in Enumerable.Range(minX, 3 - minX)
            from minY in Enumerable.Range(0, 3)
            from maxY in Enumerable.Range(minY, 3 - minY)
            select Bounds.Create(minX, minY, maxX, maxY)).ToArray();
        Assert.Equal(36, rectangles.Length);

        var pairs = 0;
        var nativeClaims = 0;
        var validatedNativeClaims = 0;
        var fallbackReturns = 0;
        var honestFallbacks = 0;
        var mismatchingPairs = 0;
        var assertionFailures = 0;
        var portableNodeCounts = new int[5];
        var nativeNodeCounts = new int[5];
        var failures = new Dictionary<string, (int Count, string Example)>();
        var fallbackExamples = new List<string>(8);

        static string DescribeBounds(Bounds bounds)
            => $"[{bounds.MinX},{bounds.MinY}..{bounds.MaxX},{bounds.MaxY}]";

        static string DescribeRegion(RegionSnapshot? region)
            => region is null ? "unavailable" :
                $"{DescribeBounds(region.Header)}: {string.Join("; ", region.Nodes.Select(DescribeBounds))}";

        foreach (var original in rectangles)
        foreach (var clear in rectangles)
        {
            pairs++;
            var pairFailed = false;
            var stage = "portable result";
            RegionSnapshot? expected = null;
            RegionSnapshot? actual = null;
            NativeRegion? native = null;

            void RecordFailure(string phase, Exception exception)
            {
                assertionFailures++;
                if (!pairFailed)
                {
                    mismatchingPairs++;
                    pairFailed = true;
                }

                var category = $"{phase}, nodes {expected?.Nodes.Length.ToString() ?? "?"}" +
                    $"->{actual?.Nodes.Length.ToString() ?? "?"}, {exception.GetType().Name}";
                if (failures.TryGetValue(category, out var previous))
                {
                    failures[category] = (previous.Count + 1, previous.Example);
                    return;
                }

                var message = exception.Message.Replace('\r', ' ').Replace('\n', ' ');
                if (message.Length > 240)
                    message = message[..240] + "...";
                var retirement = native?.Retirements.FirstOrDefault();
                var example = $"H={DescribeBounds(original)}, clear={DescribeBounds(clear)}: {message}\n" +
                    $"  expected {DescribeRegion(expected)}\n  actual {DescribeRegion(actual)}\n" +
                    $"  at FreeMem(A1={retirement?.FreedAddress.ToString("X8") ?? "unavailable"}) " +
                    $"{DescribeRegion(retirement?.Region)}\n" +
                    $"  allocations={native?.Allocations.Count ?? 0}, frees={native?.Frees.Count ?? 0}";
                failures.Add(category, (1, example));
            }

            void Check(string phase, Action assertion)
            {
                try
                {
                    assertion();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    RecordFailure(phase, exception);
                }
            }

            try
            {
                var portable = new PortableRegion(original);
                Assert.True(portable.Subtract(clear));
                expected = ReadRegion(portable.Bus, portable.Address);
                Assert.InRange(expected.Nodes.Length, 0, 4);
                portableNodeCounts[expected.Nodes.Length]++;

                stage = "native seed";
                var fixture = native = new NativeRegion(original);
                stage = "native execution/input preservation";
                var result = fixture.Subtract(clear);
                if (result.UsedFallback)
                {
                    fallbackReturns++;
                    Check("fallback contract", () =>
                    {
                        Assert.Equal(NativeRegion.CapturedFrame, result.Value);
                        Assert.Empty(fixture.Allocations);
                        Assert.Empty(fixture.Frees);
                        Assert.Empty(fixture.Retirements);
                        fixture.AssertOriginalRegionUnchanged();
                    });
                    if (!pairFailed)
                    {
                        honestFallbacks++;
                        if (fallbackExamples.Count < 8)
                            fallbackExamples.Add($"H={DescribeBounds(original)}, clear={DescribeBounds(clear)} " +
                                $"({expected.Nodes.Length} portable nodes)");
                    }
                    continue;
                }

                nativeClaims++;
                Check("native return value", () => Assert.Equal(1u, result.Value));
                Check("native links", () => actual = ReadRegion(fixture.Bus, fixture.Address));
                Check("canonical header", () => Assert.Equal(expected.Header, ReadBounds(fixture.Bus, fixture.Address)));
                if (actual is not null)
                {
                    var snapshot = actual;
                    Check("canonical bands", () => Assert.Equal(expected.Nodes, snapshot.Nodes));
                    Check("result ownership", () => AssertSubtractionOwnership(fixture, expected, snapshot));
                }
                Check("ownership prefix", fixture.AssertOwnershipPrefixUnchanged);

                // Check retirement independently AFTER final geometry. In
                // particular, a bad empty header must not hide the separate
                // error of still publishing a node when FreeMem retires it.
                Check("publication before retirement", () => fixture.AssertResultPublishedBeforeRetirement(expected));
                if (!pairFailed)
                {
                    validatedNativeClaims++;
                    nativeNodeCounts[expected.Nodes.Length]++;
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                RecordFailure(stage, exception);
            }
            finally
            {
                native?.Dispose();
            }
        }

        var summary = $"{pairs} ordered subtractions: native-claimed={nativeClaims}, " +
            $"native-validated={validatedNativeClaims}, fallback-returned={fallbackReturns}, " +
            $"honest-fallbacks={honestFallbacks}, execution/fixture-failures={pairs - nativeClaims - fallbackReturns}, " +
            $"mismatching-pairs={mismatchingPairs}, assertion-failures={assertionFailures}, " +
            $"mismatch-classes={failures.Count}.";
        var representatives = failures.OrderByDescending(failure => failure.Value.Count)
            .ThenBy(failure => failure.Key, StringComparer.Ordinal).Take(12)
            .Select(failure => $"{failure.Value.Count} x {failure.Key}\n  {failure.Value.Example}")
            .ToArray();
        _output.WriteLine(summary);
        _output.WriteLine("Portable result counts for 0..4 nodes: " + string.Join(", ", portableNodeCounts));
        _output.WriteLine("Validated native counts for 0..4 nodes: " + string.Join(", ", nativeNodeCounts));
        foreach (var example in fallbackExamples)
            _output.WriteLine("Honest fallback: " + example);
        foreach (var representative in representatives)
            _output.WriteLine(representative);
        if (failures.Count > representatives.Length)
            _output.WriteLine($"{failures.Count - representatives.Length} additional mismatch classes omitted.");

        Assert.Equal(1296, pairs);
        Assert.True(mismatchingPairs == 0, summary + "\n" + string.Join("\n", representatives));
        Assert.Equal(1296, portableNodeCounts.Sum());
        Assert.All(portableNodeCounts, count => Assert.True(count > 0));
        // Every topology on this bounded grid is now handled natively;
        // an honest decline still represents a native coverage regression.
        Assert.True(validatedNativeClaims == pairs, "Native handling coverage regressed: " + summary);
        Assert.Equal(0, fallbackReturns);
        Assert.Equal(0, honestFallbacks);
    }

    [Theory]
    [InlineData(10, 10, 20, 20)]
    [InlineData(5, 5, 25, 25)]
    public void FullContainmentPublishesPortableEmptyRegionBeforeRetiringOriginalNode(
        int minX, int minY, int maxX, int maxY)
    {
        var original = Bounds.Create(10, 10, 20, 20);
        var clear = Bounds.Create(minX, minY, maxX, maxY);
        var portable = new PortableRegion(original);
        using var native = new NativeRegion(original);

        Assert.True(portable.Subtract(clear));
        var expected = ReadRegion(portable.Bus, portable.Address);
        Assert.Empty(expected.Nodes);

        var result = native.Subtract(clear);
        Assert.False(result.UsedFallback);
        Assert.Equal(1u, result.Value);
        var actual = ReadRegion(native.Bus, native.Address);
        Assert.Equal(expected.Header, actual.Header);
        Assert.Equal(expected.Nodes, actual.Nodes);
        AssertSubtractionOwnership(native, expected, actual);
        native.AssertOwnershipPrefixUnchanged();
        native.AssertResultPublishedBeforeRetirement(expected);
    }

    [Fact]
    public void FullContainmentWithoutExecFallsBackBeforePublicationOrRetirement()
    {
        using var native = new NativeRegion(Bounds.Create(10, 10, 20, 20));
        native.Bus.WriteLong(4, 0);

        var result = native.Subtract(Bounds.Create(5, 5, 25, 25));

        Assert.True(result.UsedFallback);
        Assert.Equal(NativeRegion.CapturedFrame, result.Value);
        Assert.Empty(native.Allocations);
        Assert.Empty(native.Frees);
        Assert.Empty(native.Retirements);
        native.AssertOriginalRegionUnchanged();
    }

    [Fact]
    public void ClearingAnAlreadyEmptiedRegionDoesNotRetireTheOldNodeTwice()
    {
        var original = Bounds.Create(10, 10, 20, 20);
        var clear = Bounds.Create(5, 5, 25, 25);
        var portable = new PortableRegion(original);
        using var native = new NativeRegion(original);

        Assert.True(portable.Subtract(clear));
        var first = native.Subtract(clear);
        Assert.False(first.UsedFallback);
        Assert.Equal(1u, first.Value);
        Assert.Single(native.Frees);
        var freesAfterFirst = native.Frees.ToArray();

        Assert.True(portable.Subtract(clear));
        var second = native.Subtract(clear);
        Assert.False(second.UsedFallback);
        Assert.Equal(1u, second.Value);
        Assert.Equal(freesAfterFirst, native.Frees);

        var expected = ReadRegion(portable.Bus, portable.Address);
        var actual = ReadRegion(native.Bus, native.Address);
        Assert.Empty(expected.Nodes);
        Assert.Equal(expected.Header, actual.Header);
        Assert.Equal(expected.Nodes, actual.Nodes);
        AssertSubtractionOwnership(native, expected, actual);
        native.AssertOwnershipPrefixUnchanged();
        native.AssertResultPublishedBeforeRetirement(expected);
    }

    [Fact]
    public void ReplacementRetiresOriginalNodeThroughExecFreeMemA1()
        => AssertNativeSubtractionMatchesPortable(
            Bounds.Create(10, 10, 20, 20), Bounds.Create(5, 5, 15, 15), expectedNodes: 2);

    [Theory]
    [InlineData(13, 20, 15, 30, 14, 24, 14, 25)]
    [InlineData(10, 10, 30, 30, 14, 14, 24, 24)]
    [InlineData(0, 0, 2, 2, 1, 1, 1, 1)]
    [InlineData(0, 0, 10, 10, 1, 1, 9, 9)]
    [InlineData(10, 10, 20, 20, 15, 15, 15, 15)]
    [InlineData(-7, -10, -5, 0, -6, -6, -6, -5)]
    [InlineData(-32768, -32768, -32766, -32758, -32767, -32764, -32767, -32763)]
    [InlineData(32765, 32757, 32767, 32767, 32766, 32761, 32766, 32762)]
    [InlineData(-32768, 0, -1, 10, -16000, 3, -15000, 7)]
    [InlineData(0, -32768, 10, -1, 3, -16000, 7, -15000)]
    [InlineData(-32768, -32768, -1, -1, -16000, -16000, -15000, -15000)]
    [InlineData(0, 0, 10, 2, 3, 1, 7, 1)]
    [InlineData(0, 0, 2, 10, 1, 3, 1, 7)]
    public void InteriorHolesMatchAllFourCanonicalPortableBands(
        int oldMinX, int oldMinY, int oldMaxX, int oldMaxY,
        int minX, int minY, int maxX, int maxY)
    {
        // Exact canonical node comparison includes both surviving middle
        // intervals; matching only the outer bounds would miss the lost
        // middle-right pixels in the narrow three-column reproducer.
        AssertNativeSubtractionMatchesPortable(
            Bounds.Create(oldMinX, oldMinY, oldMaxX, oldMaxY),
            Bounds.Create(minX, minY, maxX, maxY),
            expectedNodes: 4);
    }

    [Theory]
    [InlineData(10, 10, 20, 20, 14, 5, 16, 15)]
    [InlineData(10, 10, 20, 20, 14, 15, 16, 25)]
    [InlineData(10, 10, 20, 20, 5, 14, 15, 16)]
    [InlineData(10, 10, 20, 20, 15, 14, 25, 16)]
    [InlineData(10, 10, 20, 20, 14, 10, 16, 15)]
    [InlineData(10, 10, 20, 20, 14, 15, 16, 20)]
    [InlineData(10, 10, 20, 20, 10, 14, 15, 16)]
    [InlineData(10, 10, 20, 20, 15, 14, 20, 16)]
    [InlineData(-20, -20, -10, -10, -16, -25, -14, -15)]
    [InlineData(-20, -20, -10, -10, -16, -15, -14, -5)]
    [InlineData(-20, -20, -10, -10, -25, -16, -15, -14)]
    [InlineData(-20, -20, -10, -10, -15, -16, -5, -14)]
    public void EdgeInteriorClearsRetainThreeCanonicalPortableBands(
        int oldMinX, int oldMinY, int oldMaxX, int oldMaxY,
        int minX, int minY, int maxX, int maxY)
    {
        AssertNativeSubtractionMatchesPortable(
            Bounds.Create(oldMinX, oldMinY, oldMaxX, oldMaxY),
            Bounds.Create(minX, minY, maxX, maxY),
            expectedNodes: 3);
    }

    [Theory]
    [InlineData(10, 10, 20, 20, 5, 5, 15, 15)]
    [InlineData(10, 10, 20, 20, 15, 5, 25, 15)]
    [InlineData(10, 10, 20, 20, 5, 15, 15, 25)]
    [InlineData(10, 10, 20, 20, 15, 15, 25, 25)]
    [InlineData(10, 10, 20, 20, 10, 10, 15, 15)]
    [InlineData(10, 10, 20, 20, 15, 10, 20, 15)]
    [InlineData(10, 10, 20, 20, 10, 15, 15, 20)]
    [InlineData(10, 10, 20, 20, 15, 15, 20, 20)]
    [InlineData(-20, -20, -10, -10, -25, -25, -15, -15)]
    [InlineData(-20, -20, -10, -10, -15, -25, -5, -15)]
    [InlineData(-20, -20, -10, -10, -25, -15, -15, -5)]
    [InlineData(-20, -20, -10, -10, -15, -15, -5, -5)]
    [InlineData(-32768, -32768, -32758, -32758, -32768, -32768, -32763, -32763)]
    [InlineData(32757, -32768, 32767, -32758, 32762, -32768, 32767, -32763)]
    [InlineData(-32768, 32757, -32758, 32767, -32768, 32762, -32763, 32767)]
    [InlineData(32757, 32757, 32767, 32767, 32762, 32762, 32767, 32767)]
    public void CornerClearsMatchTwoCanonicalPortableBandsInEveryOrientation(
        int oldMinX, int oldMinY, int oldMaxX, int oldMaxY,
        int minX, int minY, int maxX, int maxY)
    {
        // A pixel-equivalent partition can still split a surviving interval
        // differently. Require the portable engine's exact ordered bands.
        AssertNativeSubtractionMatchesPortable(
            Bounds.Create(oldMinX, oldMinY, oldMaxX, oldMaxY),
            Bounds.Create(minX, minY, maxX, maxY),
            expectedNodes: 2);
    }

    [Theory]
    [InlineData(1, 0u)]
    [InlineData(2, 0u)]
    [InlineData(3, 0u)]
    [InlineData(4, 0u)]
    [InlineData(1, 0x00D0_8001u)]
    [InlineData(2, 0x00D0_8001u)]
    [InlineData(3, 0x00D0_8001u)]
    [InlineData(4, 0x00D0_8001u)]
    [InlineData(1, 0xFFFF_FFF8u)]
    [InlineData(2, 0xFFFF_FFF8u)]
    [InlineData(3, 0xFFFF_FFF8u)]
    [InlineData(4, 0xFFFF_FFF8u)]
    public void InteriorHoleAllocationFailuresRetainTheOriginalRegionAndReleaseEveryProvisionalNode(
        int failingAllocation,
        uint failedAddress)
    {
        using var native = new NativeRegion(Bounds.Create(13, 20, 15, 30));
        for (var index = 1; index < failingAllocation; index++)
            native.AllocationResults.Enqueue(0x00D0_4000u + (uint)(index - 1) * 0x1000u);
        native.AllocationResults.Enqueue(failedAddress);

        var result = native.Subtract(Bounds.Create(14, 24, 14, 25));

        Assert.False(result.UsedFallback);
        Assert.Equal(0u, result.Value);
        Assert.Equal(failingAllocation, native.Allocations.Count);
        AssertReplacementAllocationFlags(native.Allocations);
        Assert.Equal(
            native.Allocations.Where(allocation => allocation.Address != 0)
                .Reverse().Select(allocation => (allocation.Address, allocation.Size, 1u)),
            native.Frees);
        native.AssertOriginalRegionUnchanged();
        native.AssertProvisionalNodesUntouched();
    }

    [Fact]
    public void InteriorHoleWithoutExecFallsBackBeforeAllocationOrMutation()
    {
        using var native = new NativeRegion(Bounds.Create(13, 20, 15, 30));
        native.Bus.WriteLong(4, 0);

        var result = native.Subtract(Bounds.Create(14, 24, 14, 25));

        Assert.True(result.UsedFallback);
        Assert.Equal(NativeRegion.CapturedFrame, result.Value);
        Assert.Empty(native.Allocations);
        Assert.Empty(native.Frees);
        native.AssertOriginalRegionUnchanged();
    }

    private static void AssertNativeSubtractionMatchesPortable(
        Bounds original,
        Bounds clear,
        int expectedNodes)
    {
        var portable = new PortableRegion(original);
        using var native = new NativeRegion(original);

        Assert.True(portable.Subtract(clear));
        var expected = ReadRegion(portable.Bus, portable.Address);
        Assert.Equal(expectedNodes, expected.Nodes.Length);

        var result = native.Subtract(clear);
        Assert.False(result.UsedFallback);
        Assert.Equal(1u, result.Value);
        var actual = ReadRegion(native.Bus, native.Address);
        Assert.Equal(expected.Header, actual.Header);
        Assert.Equal(expected.Nodes, actual.Nodes);
        Assert.Equal(expectedNodes, native.Allocations.Count);
        Assert.Equal(native.Allocations.Select(allocation => allocation.Address), actual.Addresses);
        AssertReplacementAllocationFlags(native.Allocations);
        Assert.Equal(
            new[] { (native.OriginalNode, (uint)GraphicsLayouts.RegionRectangleSize, 1u) },
            native.Frees);
        native.AssertOwnershipPrefixUnchanged();
    }

    private static void AssertReplacementAllocationFlags(IEnumerable<Allocation> allocations)
        => Assert.All(allocations, allocation =>
        {
            Assert.Equal((uint)GraphicsLayouts.RegionRectangleSize, allocation.Size);
            Assert.Equal(1u, allocation.Flags);
        });

    private static void AssertSubtractionOwnership(NativeRegion native, RegionSnapshot expected, RegionSnapshot actual)
    {
        if (expected.Nodes.Length == 1)
        {
            Assert.Empty(native.Allocations);
            Assert.Empty(native.Frees);
            Assert.Equal(new[] { native.OriginalNode }, actual.Addresses);
            return;
        }

        Assert.Equal(expected.Nodes.Length, native.Allocations.Count);
        AssertReplacementAllocationFlags(native.Allocations);
        Assert.Equal(native.Allocations.Select(allocation => allocation.Address), actual.Addresses);
        Assert.DoesNotContain(native.OriginalNode, actual.Addresses);
        Assert.Equal(
            new[] { (native.OriginalNode, (uint)GraphicsLayouts.RegionRectangleSize, 1u) },
            native.Frees);
    }

    private static RegionSnapshot ReadRegion(AmigaBus bus, uint region)
    {
        var nodes = new List<Bounds>();
        var addresses = new List<uint>();
        var previous = region + (uint)GraphicsLayouts.RegionRectangle;
        var next = bus.ReadLong(previous);
        while (next != 0)
        {
            Assert.True(nodes.Count < 8, "Region list did not terminate within its expected node count.");
            Assert.DoesNotContain(next, addresses);
            Assert.Equal(0u, next & 1u);
            Assert.Equal(previous,
                bus.ReadLong(next + (uint)GraphicsLayouts.RegionRectanglePrevious));
            addresses.Add(next);
            nodes.Add(ReadBounds(bus, next + (uint)GraphicsLayouts.RegionRectangleBounds));
            previous = next;
            next = bus.ReadLong(next + (uint)GraphicsLayouts.RegionRectangleNext);
        }

        return new RegionSnapshot(ReadBounds(bus, region), nodes.ToArray(), addresses.ToArray());
    }

    private static Bounds ReadBounds(AmigaBus bus, uint address)
        => new(
            unchecked((short)bus.ReadWord(address + (uint)GraphicsLayouts.RectangleMinX)),
            unchecked((short)bus.ReadWord(address + (uint)GraphicsLayouts.RectangleMinY)),
            unchecked((short)bus.ReadWord(address + (uint)GraphicsLayouts.RectangleMaxX)),
            unchecked((short)bus.ReadWord(address + (uint)GraphicsLayouts.RectangleMaxY)));

    private static void WriteBounds(AmigaBus bus, uint address, Bounds bounds)
    {
        bus.WriteWord(address + (uint)GraphicsLayouts.RectangleMinX, unchecked((ushort)bounds.MinX));
        bus.WriteWord(address + (uint)GraphicsLayouts.RectangleMinY, unchecked((ushort)bounds.MinY));
        bus.WriteWord(address + (uint)GraphicsLayouts.RectangleMaxX, unchecked((ushort)bounds.MaxX));
        bus.WriteWord(address + (uint)GraphicsLayouts.RectangleMaxY, unchecked((ushort)bounds.MaxY));
    }

    private static byte[] ReadBytes(AmigaBus bus, uint address, int length)
        => Enumerable.Range(0, length).Select(offset => bus.ReadByte(address + (uint)offset)).ToArray();

    private readonly record struct Bounds(short MinX, short MinY, short MaxX, short MaxY)
    {
        internal static Bounds Create(int minX, int minY, int maxX, int maxY)
            => new(checked((short)minX), checked((short)minY), checked((short)maxX), checked((short)maxY));
    }

    private sealed record RegionSnapshot(Bounds Header, Bounds[] Nodes, uint[] Addresses);
    private sealed record RetirementSnapshot(
        uint FreedAddress, RegionSnapshot? Region, byte[] OwnershipPrefix, string? ReadError);
    private sealed record NativeImage(byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback);
    private readonly record struct CallResult(uint Value, bool UsedFallback);
    private readonly record struct Allocation(uint Address, uint Size, uint Flags);

    private sealed class NativeRegion : IDisposable
    {
        internal const uint CapturedFrame = 0xA1B2_C3D4;
        private const uint CodeAddress = 0x0087_0000;
        private const uint ExecBase = 0x0077_0000;
        private const uint Rectangle = 0x00DC_0000;
        private const uint Stack = 0x00C7_0000;
        private const uint StackPointer = Stack + 0x200;
        private const uint ReturnAddress = 0x00F7_0000;
        private readonly IM68kCore _cpu;
        private readonly byte[] _originalHeader;
        private readonly byte[] _originalNode;
        private byte[]? _inputRectangle;
        private uint _nextAllocation = 0x00D0_0000;
        private bool _trackReplacementAllocations;

        internal NativeRegion(Bounds original)
        {
            Bus.MapWritableMemory(CodeAddress, Image.Value.Code);
            Bus.MapWritableMemory(4, new byte[4]);
            Bus.WriteLong(4, ExecBase);
            Bus.MapWritableMemory(Rectangle, new byte[GraphicsLayouts.RectangleSize]);
            Bus.MapWritableMemory(Stack, new byte[0x400]);
            Bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                if (_trackReplacementAllocations)
                {
                    // Check at the allocator boundary as well as after a
                    // failure, so an early write cannot be hidden by cleanup.
                    AssertOriginalRegionUnchanged();
                    AssertProvisionalNodesUntouched();
                    AssertInputRectangleUnchanged();
                }

                var address = AllocationResults.Count == 0
                    ? _nextAllocation
                    : AllocationResults.Dequeue();
                if (address == _nextAllocation)
                    _nextAllocation += 0x1000;
                Allocations.Add(new Allocation(address, state.D[0], state.D[1]));
                if (address != 0 && (address & 1) == 0 && address <= 0xFFFF_FFF0u)
                {
                    var bytes = new byte[checked((int)state.D[0])];
                    Array.Fill(bytes, (state.D[1] & 0x0001_0000u) == 0 ? (byte)0xA5 : (byte)0);
                    Bus.MapWritableMemory(address, bytes);
                }

                state.D[0] = address;
                if (_trackReplacementAllocations)
                    PoisonExecVolatileRegisters(state);
            });
            Bus.RegisterHostGateway(ExecBase - 210, state =>
            {
                // Exec FreeMem takes its address in A1, not volatile A0.
                // Read the real ABI before poisoning either register.
                Frees.Add((state.A[1], state.D[0], state.D[1]));
                if (_trackReplacementAllocations)
                {
                    AssertInputRectangleUnchanged();
                    Retirements.Add(CaptureRetirement(state.A[1]));
                    PoisonExecVolatileRegisters(state);
                }
            });
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, Bus);

            var created = Execute(GraphicsLvo.NewRegion);
            Assert.False(created.UsedFallback);
            Assert.NotEqual(0u, created.Value);
            Address = created.Value;
            WriteBounds(Bus, Rectangle, original);
            var seeded = Execute(GraphicsLvo.OrRectRegion);
            Assert.False(seeded.UsedFallback);
            Assert.Equal(1u, seeded.Value);
            OriginalNode = Bus.ReadLong(Address + (uint)GraphicsLayouts.RegionRectangle);
            Assert.NotEqual(0u, OriginalNode);
            _originalHeader = ReadBytes(Bus,
                Address - (uint)GraphicsLayouts.NativeRegionPrivatePrefixSize,
                GraphicsLayouts.NativeRegionAllocationSize);
            _originalNode = ReadBytes(Bus, OriginalNode, GraphicsLayouts.RegionRectangleSize);
            Allocations.Clear();
            Frees.Clear();
            _trackReplacementAllocations = true;
        }

        internal AmigaBus Bus { get; } = new();
        internal uint Address { get; }
        internal uint OriginalNode { get; }
        internal List<Allocation> Allocations { get; } = new();
        internal List<(uint Address, uint Size, uint Flags)> Frees { get; } = new();
        internal List<RetirementSnapshot> Retirements { get; } = new();
        internal Queue<uint> AllocationResults { get; } = new();

        private static void PoisonExecVolatileRegisters(M68kCpuState state)
        {
            // After seed setup, model Exec's volatile registers on every
            // allocation and free. In particular, the third node cannot
            // remain solely in A1 across a fourth allocation or rollback.
            state.D[1] = 0xD1D1_D1D1;
            state.A[0] = 0xA0A0_A0A1;
            state.A[1] = 0xA1A1_A1A1;
        }

        internal CallResult Subtract(Bounds clear)
        {
            WriteBounds(Bus, Rectangle, clear);
            _inputRectangle = ReadBytes(Bus, Rectangle, GraphicsLayouts.RectangleSize);
            var result = Execute(GraphicsLvo.ClearRectRegion);
            AssertInputRectangleUnchanged();
            return result;
        }

        private void AssertInputRectangleUnchanged()
        {
            Assert.NotNull(_inputRectangle);
            Assert.Equal(_inputRectangle, ReadBytes(Bus, Rectangle, GraphicsLayouts.RectangleSize));
        }

        private RetirementSnapshot CaptureRetirement(uint freedAddress)
        {
            var prefix = ReadBytes(Bus, Address - (uint)GraphicsLayouts.NativeRegionPrivatePrefixSize,
                GraphicsLayouts.NativeRegionPrivatePrefixSize);
            try
            {
                return new RetirementSnapshot(freedAddress, ReadRegion(Bus, Address), prefix, null);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Record malformed publication without interrupting native
                // execution. Final canonical and retirement diagnostics are
                // separate assertions after ClearRectRegion has returned.
                return new RetirementSnapshot(freedAddress, null, prefix, exception.Message);
            }
        }

        internal void AssertResultPublishedBeforeRetirement(RegionSnapshot expected)
        {
            if (expected.Nodes.Length == 1)
            {
                Assert.Empty(Retirements);
                return;
            }

            var retirement = Assert.Single(Retirements);
            Assert.True(retirement.ReadError is null, retirement.ReadError);
            var published = Assert.IsType<RegionSnapshot>(retirement.Region);
            Assert.Equal(expected.Header, published.Header);
            Assert.Equal(expected.Nodes, published.Nodes);
            Assert.Equal(Allocations.Select(allocation => allocation.Address), published.Addresses);
            Assert.DoesNotContain(OriginalNode, published.Addresses);
            Assert.DoesNotContain(retirement.FreedAddress, published.Addresses);
            Assert.Equal(OriginalNode, retirement.FreedAddress);
            Assert.Equal(_originalHeader.Take(GraphicsLayouts.NativeRegionPrivatePrefixSize),
                retirement.OwnershipPrefix);
        }

        internal void AssertOriginalRegionUnchanged()
        {
            Assert.Equal(_originalHeader, ReadBytes(Bus,
                Address - (uint)GraphicsLayouts.NativeRegionPrivatePrefixSize,
                GraphicsLayouts.NativeRegionAllocationSize));
            Assert.Equal(_originalNode, ReadBytes(Bus, OriginalNode, GraphicsLayouts.RegionRectangleSize));
        }

        internal void AssertOwnershipPrefixUnchanged()
            => Assert.Equal(_originalHeader.Take(GraphicsLayouts.NativeRegionPrivatePrefixSize),
                ReadBytes(Bus, Address - (uint)GraphicsLayouts.NativeRegionPrivatePrefixSize,
                    GraphicsLayouts.NativeRegionPrivatePrefixSize));

        internal void AssertProvisionalNodesUntouched()
        {
            foreach (var allocation in Allocations.Where(allocation =>
                         allocation.Address != 0 && (allocation.Address & 1) == 0 &&
                         allocation.Address <= 0xFFFF_FFF0u))
            {
                Assert.All(ReadBytes(Bus, allocation.Address, (int)allocation.Size),
                    value => Assert.Equal((byte)0xA5, value));
            }
        }

        private CallResult Execute(GraphicsLvo vector)
        {
            Bus.WriteLong(StackPointer, ReturnAddress);
            _cpu.Reset(CodeAddress + (uint)Image.Value.Entries[vector], StackPointer);
            _cpu.State.D[0] = CapturedFrame;
            _cpu.State.A[0] = Address;
            _cpu.State.A[1] = Rectangle;
            var fallback = false;
            for (var index = 0; index < 16384; index++)
            {
                // Distant fallback branches use local copies of the shared
                // RTS. Both forms must return the captured frame at exactly
                // the caller's stack depth; handled paths return TRUE/FALSE.
                var returnsCapturedFrame = _cpu.State.D[0] == CapturedFrame &&
                    Bus.ReadWord(_cpu.State.ProgramCounter) == 0x4E75;
                if (_cpu.State.ProgramCounter == CodeAddress + (uint)Image.Value.Fallback ||
                    returnsCapturedFrame)
                {
                    fallback = true;
                    Assert.Equal(CapturedFrame, _cpu.State.D[0]);
                    Assert.Equal(StackPointer, _cpu.State.A[7]);
                }

                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter == ReturnAddress)
                {
                    Assert.Equal(StackPointer + 4u, _cpu.State.A[7]);
                    return new CallResult(_cpu.State.D[0], fallback);
                }
            }

            throw new InvalidOperationException(
                $"Native {vector} did not return: PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }

    private sealed class PortableRegion
    {
        private const uint Rectangle = 0x00DC_0000;
        private readonly GraphicsLibraryCore _core;

        internal PortableRegion(Bounds original)
        {
            Bus.MapWritableMemory(Rectangle, new byte[GraphicsLayouts.RectangleSize]);
            _core = new GraphicsLibraryCore(
                new CopperStartGraphicsMemoryAdapter(new HostGuestMemory(Bus)),
                new PortableAllocator(Bus),
                new CopperStartGraphicsUnboundBlitter(),
                new CopperStartGraphicsUnboundDisplay());
            Address = _core.NewRegion();
            Assert.NotEqual(0u, Address);
            WriteBounds(Bus, Rectangle, original);
            Assert.True(_core.OrRectRegion(Address, Rectangle));
        }

        internal AmigaBus Bus { get; } = new();
        internal uint Address { get; }

        internal bool Subtract(Bounds clear)
        {
            WriteBounds(Bus, Rectangle, clear);
            return _core.ClearRectRegion(Address, Rectangle);
        }
    }

    private sealed class PortableAllocator(AmigaBus bus) : IGraphicsAllocatorBackend
    {
        private uint _nextAddress = 0x00D0_0000;

        public bool TryAllocate(uint byteCount, GraphicsMemoryClass memoryClass, out uint address)
        {
            Assert.Equal(GraphicsMemoryClass.Public, memoryClass);
            address = _nextAddress;
            _nextAddress += 0x1000;
            bus.MapWritableMemory(address, new byte[checked((int)byteCount)]);
            return true;
        }

        public void Free(uint address, uint byteCount, GraphicsMemoryClass memoryClass) { }
    }
}
