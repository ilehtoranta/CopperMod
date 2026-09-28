using CopperMod.Amiga.CopperStart;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using Xunit.Abstractions;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsRegionUnionTests
{
    private readonly ITestOutputHelper _output;

    public NativeGraphicsRegionUnionTests(ITestOutputHelper output) => _output = output;

    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });

    [Fact]
    public void AllOrderedThreeByThreeRectanglePairsMatchPortableOrDeclineWithoutMutation()
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
        var mismatches = 0;
        var failures = new Dictionary<string, (int Count, string Example)>();
        var fallbackExamples = new List<string>(8);

        static string DescribeBounds(Bounds bounds)
            => $"[{bounds.MinX},{bounds.MinY}..{bounds.MaxX},{bounds.MaxY}]";

        static string DescribeRegion(RegionSnapshot? region)
            => region is null ? "unavailable" :
                $"{DescribeBounds(region.Header)}: {string.Join("; ", region.Nodes.Select(DescribeBounds))}";

        foreach (var original in rectangles)
        foreach (var input in rectangles)
        {
            pairs++;
            var stage = "portable result";
            RegionSnapshot? expected = null;
            RegionSnapshot? actual = null;
            NativeRegion? native = null;
            try
            {
                var portable = new PortableRegion(original);
                Assert.True(portable.Union(input));
                expected = ReadRegion(portable.Bus, portable.Address);
                Assert.NotEmpty(expected.Nodes);

                stage = "native seed";
                native = new NativeRegion(original, poisonExecRegisters: true);
                stage = "native execution/input preservation";
                var result = native.Union(input);
                if (result.UsedFallback)
                {
                    fallbackReturns++;
                    stage = "fallback contract";
                    Assert.Equal(NativeRegion.CapturedFrame, result.Value);
                    Assert.Empty(native.Allocations);
                    Assert.Empty(native.Frees);
                    native.AssertOriginalRegionUnchanged();
                    if (fallbackExamples.Count < 8)
                        fallbackExamples.Add($"H={DescribeBounds(original)}, I={DescribeBounds(input)} " +
                            $"({expected.Nodes.Length} portable nodes)");
                    honestFallbacks++;
                    continue;
                }

                nativeClaims++;
                stage = "native return value";
                Assert.Equal(1u, result.Value);
                stage = "native links";
                actual = ReadRegion(native.Bus, native.Address);
                stage = "canonical header";
                Assert.Equal(expected.Header, actual.Header);
                stage = "canonical bands";
                Assert.Equal(expected.Nodes, actual.Nodes);

                if (native.Allocations.Count == 0)
                {
                    stage = "in-place ownership";
                    Assert.Single(expected.Nodes);
                    Assert.Equal(new[] { native.OriginalNode }, actual.Addresses);
                    Assert.Empty(native.Frees);
                }
                else
                {
                    stage = "replacement ownership";
                    Assert.Equal(expected.Nodes.Length, native.Allocations.Count);
                    Assert.Equal(native.Allocations.Select(allocation => allocation.Address), actual.Addresses);
                    Assert.DoesNotContain(native.OriginalNode, actual.Addresses);
                    Assert.All(native.Allocations, allocation =>
                    {
                        Assert.Equal((uint)GraphicsLayouts.RegionRectangleSize, allocation.Size);
                        Assert.Equal(1u, allocation.Flags);
                    });
                    Assert.Equal(
                        new[] { (native.OriginalNode, (uint)GraphicsLayouts.RegionRectangleSize, 1u) },
                        native.Frees);
                }

                stage = "ownership prefix";
                native.AssertOwnershipPrefixUnchanged();
                validatedNativeClaims++;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Finish the bounded matrix to reveal distinct failure
                // classes. A wrong native claim always remains a mismatch;
                // it is never counted as an acceptable fallback.
                mismatches++;
                var category = $"{stage}, nodes {expected?.Nodes.Length.ToString() ?? "?"}" +
                    $"->{actual?.Nodes.Length.ToString() ?? "?"}, {exception.GetType().Name}";
                if (failures.TryGetValue(category, out var previous))
                {
                    failures[category] = (previous.Count + 1, previous.Example);
                }
                else
                {
                    var message = exception.Message.Replace('\r', ' ').Replace('\n', ' ');
                    if (message.Length > 240)
                        message = message[..240] + "...";
                    var example = $"H={DescribeBounds(original)}, I={DescribeBounds(input)}: {message}\n" +
                        $"  expected {DescribeRegion(expected)}\n  actual {DescribeRegion(actual)}\n" +
                        $"  allocations={native?.Allocations.Count ?? 0}, frees={native?.Frees.Count ?? 0}";
                    failures.Add(category, (1, example));
                }
            }
            finally
            {
                native?.Dispose();
            }
        }

        var summary = $"{pairs} ordered pairs: native-claimed={nativeClaims}, " +
            $"native-validated={validatedNativeClaims}, fallback-returned={fallbackReturns}, " +
            $"honest-fallbacks={honestFallbacks}, execution/fixture-failures={pairs - nativeClaims - fallbackReturns}, " +
            $"mismatches={mismatches}, mismatch-classes={failures.Count}.";
        var representatives = failures.OrderByDescending(failure => failure.Value.Count)
            .ThenBy(failure => failure.Key, StringComparer.Ordinal).Take(12)
            .Select(failure => $"{failure.Value.Count} x {failure.Key}\n  {failure.Value.Example}")
            .ToArray();
        _output.WriteLine(summary);
        foreach (var example in fallbackExamples)
            _output.WriteLine("Honest fallback: " + example);
        foreach (var representative in representatives)
            _output.WriteLine(representative);
        if (failures.Count > representatives.Length)
            _output.WriteLine($"{failures.Count - representatives.Length} additional mismatch classes omitted.");

        Assert.Equal(1296, pairs);
        Assert.True(mismatches == 0, summary + "\n" + string.Join("\n", representatives));
        // This complete bounded grid is now native. A later admission
        // regression must fail even if the fallback itself remains honest.
        Assert.True(validatedNativeClaims == pairs, "Native handling coverage regressed: " + summary);
        Assert.Equal(0, fallbackReturns);
        Assert.Equal(0, honestFallbacks);
    }

    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, true, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, true, true)]
    [InlineData(false, false, false, false, true)]
    [InlineData(false, false, false, false, true, true)]
    [InlineData(false, false, false, false, false, false, true)]
    [InlineData(false, false, false, false, false, true, true)]
    public void HorizontalOverhangRegionRegionMatchesPortableCanonicalBands(
        bool extendsLeft,
        bool extendsRight,
        bool protrudesAboveFirst,
        bool protrudesBelowFirst = false,
        bool sourceContainsBoth = false,
        bool failFirstReplacementAllocation = false,
        bool sourceStrictlyContainsFirst = false)
    {
        const uint codeAddress = 0x0087_0000;
        const uint execBase = 0x0077_0000;
        const uint sourceAllocation = 0x00E0_0000;
        const uint sourceNode = 0x00E0_1000;
        const uint destinationAllocation = 0x00E0_2000;
        const uint destinationFirst = 0x00E0_3000;
        const uint destinationSecond = 0x00E0_4000;
        const uint upperBand = 0x00E0_5000;
        const uint lowerBand = 0x00E0_6000;
        const uint stack = 0x00C7_0000;
        const uint returnAddress = 0x00F7_0000;
        var sourceRegion = sourceAllocation + (uint)GraphicsLayouts.NativeRegionPrivatePrefixSize;
        var destinationRegion = destinationAllocation + (uint)GraphicsLayouts.NativeRegionPrivatePrefixSize;
        var overlapsFirstEdge = protrudesAboveFirst || protrudesBelowFirst;
        var commonEnvelope = sourceContainsBoth
            ? Bounds.Create(-5, -3, 35, 45)
            : sourceStrictlyContainsFirst
            ? Bounds.Create(0, 0, 35, 35)
            : Bounds.Create(
                extendsLeft ? -5 : 0,
                overlapsFirstEdge ? -3 : 0,
                25,
                30);
        var sourceX = (extendsLeft, extendsRight) switch
        {
            (false, false) => (10, 15),
            (true, true) => (0, 30),
            (true, false) => (0, 10),
            _ => (15, 25)
        };
        var firstX = extendsLeft ? (5, 25) : (0, 20);
        var secondMinY = overlapsFirstEdge ? 23 : 20;
        var secondMaxY = overlapsFirstEdge ? 33 : 30;
        var secondRectangle = sourceContainsBoth
            ? Bounds.Create(5, 20, 20, 25)
            : sourceStrictlyContainsFirst
            ? Bounds.Create(5, 20, 25, 30)
            : extendsLeft
                ? Bounds.Create(5, secondMinY, 30, secondMaxY)
                : Bounds.Create(0, secondMinY, 20, secondMaxY);
        var sourceMinY = protrudesAboveFirst ? 0 : 2;
        var sourceMaxY = protrudesBelowFirst ? 8 : protrudesAboveFirst ? 5 : 3;
        var firstMinY = protrudesAboveFirst ? 3 : 0;
        var firstMaxY = protrudesBelowFirst ? 5 : protrudesAboveFirst ? 8 : 5;
        var sourceRectangle = sourceContainsBoth
            ? Bounds.Create(0, 0, 30, 40)
            : sourceStrictlyContainsFirst
            ? Bounds.Create(5, 5, 25, 15)
            : Bounds.Create(
                sourceX.Item1,
                sourceMinY,
                sourceX.Item2,
                sourceMaxY);
        var firstRectangle = sourceContainsBoth
            ? Bounds.Create(5, 5, 20, 10)
            : sourceStrictlyContainsFirst
            ? Bounds.Create(10, 10, 20, 12)
            : Bounds.Create(
                firstX.Item1,
                firstMinY,
                firstX.Item2,
                firstMaxY);

        var portable = new PortableRegionRegionFixture();
        var portableSource = portable.CreateRegion(sourceRectangle);
        var portableDestination = portable.CreateRegion(firstRectangle, secondRectangle);
        portable.SetEnvelopeAndNodeBounds(portableSource, commonEnvelope, sourceRectangle);
        portable.SetEnvelopeAndNodeBounds(
            portableDestination,
            commonEnvelope,
            firstRectangle,
            secondRectangle);
        Assert.True(portable.Core.OrRegionRegion(portableSource, portableDestination));
        var expected = ReadRegion(portable.Bus, portableDestination);

        var nativeCode = Image.Value.Code;
        var bus = new AmigaBus();
        bus.MapWritableMemory(codeAddress, nativeCode);
        bus.MapWritableMemory(4, new byte[4]);
        bus.WriteLong(4, execBase);
        bus.MapWritableMemory(sourceAllocation, new byte[GraphicsLayouts.NativeRegionAllocationSize]);
        bus.MapWritableMemory(sourceNode, new byte[GraphicsLayouts.RegionRectangleSize]);
        bus.MapWritableMemory(destinationAllocation, new byte[GraphicsLayouts.NativeRegionAllocationSize]);
        foreach (var node in new[] { destinationFirst, destinationSecond, upperBand, lowerBand })
            bus.MapWritableMemory(node, new byte[GraphicsLayouts.RegionRectangleSize]);
        bus.MapWritableMemory(stack, new byte[0x400]);

        void WriteRegionNode(
            AmigaBus targetBus,
            uint node,
            uint previous,
            uint next,
            Bounds bounds)
        {
            targetBus.WriteLong(node + (uint)GraphicsLayouts.RegionRectanglePrevious, previous);
            targetBus.WriteLong(node + (uint)GraphicsLayouts.RegionRectangleNext, next);
            WriteBounds(targetBus, node + (uint)GraphicsLayouts.RegionRectangleBounds, bounds);
        }

        bus.WriteWord(sourceAllocation, GraphicsLayouts.NativeRegionMarker);
        bus.WriteWord(destinationAllocation, GraphicsLayouts.NativeRegionMarker);
        WriteBounds(bus, sourceRegion, commonEnvelope);
        WriteBounds(bus, destinationRegion, commonEnvelope);
        WriteRegionNode(
            bus,
            sourceNode,
            sourceRegion + (uint)GraphicsLayouts.RegionRectangle,
            0,
            sourceRectangle);
        bus.WriteLong(sourceRegion + (uint)GraphicsLayouts.RegionRectangle, sourceNode);
        WriteRegionNode(
            bus,
            destinationFirst,
            destinationRegion + (uint)GraphicsLayouts.RegionRectangle,
            destinationSecond,
            firstRectangle);
        WriteRegionNode(bus, destinationSecond, destinationFirst, 0, secondRectangle);
        bus.WriteLong(destinationRegion + (uint)GraphicsLayouts.RegionRectangle, destinationFirst);

        var allocationResults = new Queue<uint>(failFirstReplacementAllocation
            ? new[] { 0u }
            : new[] { upperBand, lowerBand });
        var allocations = new List<(uint Address, uint Size, uint Flags)>();
        var frees = new List<(uint Address, uint Size, uint Flags)>();
        bus.RegisterHostGateway(unchecked((uint)((int)execBase - 198)), state =>
        {
            var address = allocationResults.Dequeue();
            allocations.Add((address, state.D[0], state.D[1]));
            state.D[0] = address;
        });
        bus.RegisterHostGateway(unchecked((uint)((int)execBase - 210)), state =>
            frees.Add((state.A[0], state.D[0], state.D[1])));

        var sourceAllocationBefore = ReadBytes(bus, sourceAllocation, GraphicsLayouts.NativeRegionAllocationSize);
        var sourceNodeBefore = ReadBytes(bus, sourceNode, GraphicsLayouts.RegionRectangleSize);
        var destinationAllocationBefore = ReadBytes(
            bus,
            destinationAllocation,
            GraphicsLayouts.NativeRegionAllocationSize);
        var destinationFirstBefore = ReadBytes(bus, destinationFirst, GraphicsLayouts.RegionRectangleSize);
        var destinationSecondBefore = ReadBytes(bus, destinationSecond, GraphicsLayouts.RegionRectangleSize);
        using var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
        bus.WriteLong(stack + 0x80u, returnAddress);
        cpu.Reset(codeAddress + (uint)Image.Value.Entries[GraphicsLvo.OrRegionRegion], stack + 0x80u);
        cpu.State.A[0] = sourceRegion;
        cpu.State.A[1] = destinationRegion;
        cpu.State.D[0] = 0xA1B2_C3D4u;
        for (var index = 0; index < 8192 && cpu.State.ProgramCounter != returnAddress; index++)
            cpu.ExecuteInstruction();

        Assert.Equal(returnAddress, cpu.State.ProgramCounter);
        Assert.Equal(failFirstReplacementAllocation ? 0u : 1u, cpu.State.D[0]);
        var sourceContainedByFirst = !extendsLeft && !extendsRight &&
            !protrudesAboveFirst && !protrudesBelowFirst;
        var sourceContainsFirst = extendsLeft && extendsRight &&
            protrudesAboveFirst && protrudesBelowFirst;
        var expectedAllocationCount = sourceContainsBoth
            ? 1
            : sourceStrictlyContainsFirst
            ? 1
            : sourceContainedByFirst
            ? 0
            : sourceContainsFirst
            ? 1
            : overlapsFirstEdge && extendsLeft && extendsRight ? 1 : 2;
        Assert.Equal(expectedAllocationCount, allocations.Count);
        Assert.All(allocations, allocation =>
        {
            Assert.Equal((uint)GraphicsLayouts.RegionRectangleSize, allocation.Size);
            Assert.Equal(1u, allocation.Flags);
        });
        if (sourceContainsBoth && !failFirstReplacementAllocation)
        {
            Assert.Equal(
                new[]
                {
                    (destinationSecond, (uint)GraphicsLayouts.RegionRectangleSize, 1u),
                    (destinationFirst, (uint)GraphicsLayouts.RegionRectangleSize, 1u)
                },
                frees);
        }
        else if (!failFirstReplacementAllocation &&
            (sourceStrictlyContainsFirst || sourceContainsFirst))
        {
            Assert.Equal(
                new[] { (destinationFirst, (uint)GraphicsLayouts.RegionRectangleSize, 1u) },
                frees);
        }
        else
        {
            Assert.Empty(frees);
        }
        var actual = ReadRegion(bus, destinationRegion);
        if (failFirstReplacementAllocation)
        {
            Assert.Equal(commonEnvelope, actual.Header);
            Assert.Equal(new[] { firstRectangle, secondRectangle }, actual.Nodes);
            Assert.Equal(new[] { destinationFirst, destinationSecond }, actual.Addresses);
            Assert.Equal(destinationAllocationBefore,
                ReadBytes(bus, destinationAllocation, GraphicsLayouts.NativeRegionAllocationSize));
            Assert.Equal(destinationFirstBefore,
                ReadBytes(bus, destinationFirst, GraphicsLayouts.RegionRectangleSize));
            Assert.Equal(destinationSecondBefore,
                ReadBytes(bus, destinationSecond, GraphicsLayouts.RegionRectangleSize));
            Assert.Empty(frees);
        }
        else
        {
            Assert.Equal(expected.Header, actual.Header);
            Assert.Equal(expected.Nodes, actual.Nodes);
        }
        Assert.Equal(sourceAllocationBefore,
            ReadBytes(bus, sourceAllocation, GraphicsLayouts.NativeRegionAllocationSize));
        Assert.Equal(sourceNodeBefore,
            ReadBytes(bus, sourceNode, GraphicsLayouts.RegionRectangleSize));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void HorizontalXOverhangInsideSecondDestinationNodeMatchesPortableAndRollsBackOnAccurateM68000(
        bool overhangLeft,
        bool overhangRight)
    {
        const uint codeAddress = 0x0088_0000;
        const uint execBase = 0x0078_0000;
        const uint sourceAllocation = 0x00E8_0000;
        const uint sourceNode = 0x00E8_1000;
        const uint destinationAllocation = 0x00E8_2000;
        const uint destinationFirst = 0x00E8_3000;
        const uint destinationSecond = 0x00E8_4000;
        const uint upperBand = 0x00E8_5000;
        const uint lowerBand = 0x00E8_6000;
        const uint stack = 0x00C8_0000;
        const uint returnAddress = 0x00F8_0000;
        var sourceRegion = sourceAllocation + (uint)GraphicsLayouts.NativeRegionPrivatePrefixSize;
        var destinationRegion = destinationAllocation + (uint)GraphicsLayouts.NativeRegionPrivatePrefixSize;
        var secondMinX = overhangLeft ? 5 : 0;
        var secondMaxX = overhangRight ? 20 : 25;
        var sourceBounds = overhangLeft && overhangRight
            ? Bounds.Create(0, 11, 25, 14)
            : overhangLeft
                ? Bounds.Create(0, 11, 10, 14)
                : Bounds.Create(15, 11, 25, 14);
        var firstBounds = Bounds.Create(secondMinX, 0, secondMaxX, 5);
        var secondBounds = Bounds.Create(secondMinX, 8, secondMaxX, 18);
        var envelope = Bounds.Create(0, 0, 30, 30);

        var portable = new PortableRegionRegionFixture();
        var portableSource = portable.CreateRegion(sourceBounds);
        var portableDestination = portable.CreateRegion(firstBounds, secondBounds);
        portable.SetEnvelopeAndNodeBounds(portableSource, envelope, sourceBounds);
        portable.SetEnvelopeAndNodeBounds(
            portableDestination,
            envelope,
            firstBounds,
            secondBounds);
        Assert.True(portable.Core.OrRegionRegion(portableSource, portableDestination));
        var expected = ReadRegion(portable.Bus, portableDestination);
        Assert.Equal(
            new[]
            {
                firstBounds,
                Bounds.Create(secondMinX, 8, secondMaxX, 10),
                Bounds.Create(0, 11, 25, 14),
                Bounds.Create(secondMinX, 15, secondMaxX, 18)
            },
            expected.Nodes);

        var nativeCode = Image.Value.Code;
        var bus = new AmigaBus();
        bus.MapWritableMemory(codeAddress, nativeCode);
        bus.MapWritableMemory(4, new byte[4]);
        bus.WriteLong(4, execBase);
        bus.MapWritableMemory(sourceAllocation, new byte[GraphicsLayouts.NativeRegionAllocationSize]);
        bus.MapWritableMemory(sourceNode, new byte[GraphicsLayouts.RegionRectangleSize]);
        bus.MapWritableMemory(destinationAllocation, new byte[GraphicsLayouts.NativeRegionAllocationSize]);
        foreach (var node in new[] { destinationFirst, destinationSecond, upperBand, lowerBand })
            bus.MapWritableMemory(node, new byte[GraphicsLayouts.RegionRectangleSize]);
        bus.MapWritableMemory(stack, new byte[0x400]);

        void WriteNode(uint node, uint previous, uint next, Bounds bounds)
        {
            bus.WriteLong(node + (uint)GraphicsLayouts.RegionRectanglePrevious, previous);
            bus.WriteLong(node + (uint)GraphicsLayouts.RegionRectangleNext, next);
            WriteBounds(bus, node + (uint)GraphicsLayouts.RegionRectangleBounds, bounds);
        }

        void WriteOriginalRegions()
        {
            bus.WriteWord(sourceAllocation, GraphicsLayouts.NativeRegionMarker);
            bus.WriteWord(destinationAllocation, GraphicsLayouts.NativeRegionMarker);
            WriteBounds(bus, sourceRegion, envelope);
            WriteBounds(bus, destinationRegion, envelope);
            WriteNode(
                sourceNode,
                sourceRegion + (uint)GraphicsLayouts.RegionRectangle,
                0,
                sourceBounds);
            bus.WriteLong(sourceRegion + (uint)GraphicsLayouts.RegionRectangle, sourceNode);
            WriteNode(
                destinationFirst,
                destinationRegion + (uint)GraphicsLayouts.RegionRectangle,
                destinationSecond,
                firstBounds);
            WriteNode(destinationSecond, destinationFirst, 0, secondBounds);
            bus.WriteLong(
                destinationRegion + (uint)GraphicsLayouts.RegionRectangle,
                destinationFirst);
        }

        var allocationResults = new Queue<uint>();
        var allocations = new List<(uint Address, uint Size, uint Flags)>();
        var frees = new List<(uint Address, uint Size, uint Flags)>();
        bus.RegisterHostGateway(unchecked((uint)((int)execBase - 198)), state =>
        {
            var address = allocationResults.Dequeue();
            allocations.Add((address, state.D[0], state.D[1]));
            state.D[0] = address;
        });
        bus.RegisterHostGateway(unchecked((uint)((int)execBase - 210)), state =>
            frees.Add((state.A[0], state.D[0], state.D[1])));

        using var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
        void Execute(uint seed)
        {
            bus.WriteLong(stack + 0x80u, returnAddress);
            cpu.Reset(codeAddress + (uint)Image.Value.Entries[GraphicsLvo.OrRegionRegion], stack + 0x80u);
            cpu.State.A[0] = sourceRegion;
            cpu.State.A[1] = destinationRegion;
            cpu.State.D[0] = seed;
            for (var index = 0; index < 8192; index++)
            {
                cpu.ExecuteInstruction();
                if (cpu.State.ProgramCounter == returnAddress)
                    return;
            }

            throw new InvalidOperationException(
                $"Native second-node X-overhang OrRegionRegion did not return (fallback {Image.Value.Fallback}).");
        }

        WriteOriginalRegions();
        var sourceAllocationBefore = ReadBytes(bus, sourceAllocation, GraphicsLayouts.NativeRegionAllocationSize);
        var sourceNodeBefore = ReadBytes(bus, sourceNode, GraphicsLayouts.RegionRectangleSize);
        allocationResults.Enqueue(upperBand);
        allocationResults.Enqueue(lowerBand);
        Execute(0xA1B2_C3D4u);
        Assert.Equal(1u, cpu.State.D[0]);
        Assert.Equal(2, allocations.Count);
        Assert.Equal(
            new[]
            {
                (upperBand, (uint)GraphicsLayouts.RegionRectangleSize, 1u),
                (lowerBand, (uint)GraphicsLayouts.RegionRectangleSize, 1u)
            },
            allocations);
        Assert.Empty(frees);
        var actual = ReadRegion(bus, destinationRegion);
        Assert.Equal(expected.Header, actual.Header);
        Assert.Equal(expected.Nodes, actual.Nodes);
        Assert.Equal(new[] { destinationFirst, upperBand, destinationSecond, lowerBand }, actual.Addresses);
        Assert.Equal(sourceAllocationBefore,
            ReadBytes(bus, sourceAllocation, GraphicsLayouts.NativeRegionAllocationSize));
        Assert.Equal(sourceNodeBefore,
            ReadBytes(bus, sourceNode, GraphicsLayouts.RegionRectangleSize));

        var failedAllocations = new (uint[] Results, uint[] Freed)[]
        {
            (new[] { 0u }, Array.Empty<uint>()),
            (new[] { upperBand, 0u }, new[] { upperBand }),
            (new[] { upperBand, lowerBand + 1u }, new[] { lowerBand + 1u, upperBand }),
            (new[] { upperBand, 0xFFFF_FFF8u }, new[] { 0xFFFF_FFF8u, upperBand })
        };
        foreach (var failure in failedAllocations)
        {
            WriteOriginalRegions();
            var original = ReadRegion(bus, destinationRegion);
            var allocationStart = allocations.Count;
            var freeStart = frees.Count;
            foreach (var result in failure.Results)
                allocationResults.Enqueue(result);

            Execute(0xD00D_0000u + (uint)allocationStart);
            Assert.Equal(0u, cpu.State.D[0]);
            Assert.Empty(allocationResults);
            Assert.Equal(failure.Results.Length, allocations.Count - allocationStart);
            Assert.Equal(
                failure.Freed.Select(address =>
                    (address, (uint)GraphicsLayouts.RegionRectangleSize, 1u)),
                frees.Skip(freeStart));
            var preserved = ReadRegion(bus, destinationRegion);
            Assert.Equal(original.Header, preserved.Header);
            Assert.Equal(original.Nodes, preserved.Nodes);
            Assert.Equal(original.Addresses, preserved.Addresses);
            Assert.Equal(sourceAllocationBefore,
                ReadBytes(bus, sourceAllocation, GraphicsLayouts.NativeRegionAllocationSize));
            Assert.Equal(sourceNodeBefore,
                ReadBytes(bus, sourceNode, GraphicsLayouts.RegionRectangleSize));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void VerticalOverhangInsideSecondDestinationNodeMatchesPortableAndRollsBackOnAccurateM68000(
        bool overhangTop)
    {
        const uint codeAddress = 0x0089_0000;
        const uint execBase = 0x0079_0000;
        const uint sourceAllocation = 0x00E9_0000;
        const uint sourceNode = 0x00E9_1000;
        const uint destinationAllocation = 0x00E9_2000;
        const uint destinationFirst = 0x00E9_3000;
        const uint destinationSecond = 0x00E9_4000;
        const uint residualBand = 0x00E9_5000;
        const uint stack = 0x00C9_0000;
        const uint returnAddress = 0x00F9_0000;
        var sourceRegion = sourceAllocation + (uint)GraphicsLayouts.NativeRegionPrivatePrefixSize;
        var destinationRegion = destinationAllocation + (uint)GraphicsLayouts.NativeRegionPrivatePrefixSize;
        var sourceBounds = overhangTop
            ? Bounds.Create(5, 7, 15, 12)
            : Bounds.Create(5, 12, 15, 18);
        var firstBounds = Bounds.Create(0, 0, 20, 5);
        var secondBounds = overhangTop
            ? Bounds.Create(0, 9, 20, 18)
            : Bounds.Create(0, 9, 20, 15);
        var envelope = Bounds.Create(0, 0, 30, 30);
        var residualBounds = overhangTop
            ? Bounds.Create(5, 7, 15, 8)
            : Bounds.Create(5, 16, 15, 18);

        var portable = new PortableRegionRegionFixture();
        var portableSource = portable.CreateRegion(sourceBounds);
        var portableDestination = portable.CreateRegion(firstBounds, secondBounds);
        portable.SetEnvelopeAndNodeBounds(portableSource, envelope, sourceBounds);
        portable.SetEnvelopeAndNodeBounds(
            portableDestination,
            envelope,
            firstBounds,
            secondBounds);
        Assert.True(portable.Core.OrRegionRegion(portableSource, portableDestination));
        var expected = ReadRegion(portable.Bus, portableDestination);
        Assert.Equal(
            new[]
            {
                firstBounds,
                overhangTop ? residualBounds : secondBounds,
                overhangTop ? secondBounds : residualBounds
            },
            expected.Nodes);

        var nativeCode = Image.Value.Code;
        var bus = new AmigaBus();
        bus.MapWritableMemory(codeAddress, nativeCode);
        bus.MapWritableMemory(4, new byte[4]);
        bus.WriteLong(4, execBase);
        bus.MapWritableMemory(sourceAllocation, new byte[GraphicsLayouts.NativeRegionAllocationSize]);
        bus.MapWritableMemory(sourceNode, new byte[GraphicsLayouts.RegionRectangleSize]);
        bus.MapWritableMemory(destinationAllocation, new byte[GraphicsLayouts.NativeRegionAllocationSize]);
        foreach (var node in new[] { destinationFirst, destinationSecond, residualBand })
            bus.MapWritableMemory(node, new byte[GraphicsLayouts.RegionRectangleSize]);
        bus.MapWritableMemory(stack, new byte[0x400]);

        void WriteNode(uint node, uint previous, uint next, Bounds bounds)
        {
            bus.WriteLong(node + (uint)GraphicsLayouts.RegionRectanglePrevious, previous);
            bus.WriteLong(node + (uint)GraphicsLayouts.RegionRectangleNext, next);
            WriteBounds(bus, node + (uint)GraphicsLayouts.RegionRectangleBounds, bounds);
        }

        void WriteOriginalRegions()
        {
            bus.WriteWord(sourceAllocation, GraphicsLayouts.NativeRegionMarker);
            bus.WriteWord(destinationAllocation, GraphicsLayouts.NativeRegionMarker);
            WriteBounds(bus, sourceRegion, envelope);
            WriteBounds(bus, destinationRegion, envelope);
            WriteNode(
                sourceNode,
                sourceRegion + (uint)GraphicsLayouts.RegionRectangle,
                0,
                sourceBounds);
            bus.WriteLong(sourceRegion + (uint)GraphicsLayouts.RegionRectangle, sourceNode);
            WriteNode(
                destinationFirst,
                destinationRegion + (uint)GraphicsLayouts.RegionRectangle,
                destinationSecond,
                firstBounds);
            WriteNode(destinationSecond, destinationFirst, 0, secondBounds);
            bus.WriteLong(
                destinationRegion + (uint)GraphicsLayouts.RegionRectangle,
                destinationFirst);
        }

        var allocationResults = new Queue<uint>();
        var allocations = new List<(uint Address, uint Size, uint Flags)>();
        var frees = new List<(uint Address, uint Size, uint Flags)>();
        bus.RegisterHostGateway(unchecked((uint)((int)execBase - 198)), state =>
        {
            var address = allocationResults.Dequeue();
            allocations.Add((address, state.D[0], state.D[1]));
            state.D[0] = address;
        });
        bus.RegisterHostGateway(unchecked((uint)((int)execBase - 210)), state =>
            frees.Add((state.A[0], state.D[0], state.D[1])));

        using var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
        void Execute(uint seed)
        {
            bus.WriteLong(stack + 0x80u, returnAddress);
            cpu.Reset(codeAddress + (uint)Image.Value.Entries[GraphicsLvo.OrRegionRegion], stack + 0x80u);
            cpu.State.A[0] = sourceRegion;
            cpu.State.A[1] = destinationRegion;
            cpu.State.D[0] = seed;
            for (var index = 0; index < 8192; index++)
            {
                cpu.ExecuteInstruction();
                if (cpu.State.ProgramCounter == returnAddress)
                    return;
            }

            throw new InvalidOperationException(
                $"Native second-node vertical-Y-overhang OrRegionRegion did not return (fallback {Image.Value.Fallback}).");
        }

        WriteOriginalRegions();
        var sourceAllocationBefore = ReadBytes(bus, sourceAllocation, GraphicsLayouts.NativeRegionAllocationSize);
        var sourceNodeBefore = ReadBytes(bus, sourceNode, GraphicsLayouts.RegionRectangleSize);
        allocationResults.Enqueue(residualBand);
        Execute(0xA1B2_C3D4u);
        Assert.Equal(1u, cpu.State.D[0]);
        Assert.Equal(
            new[] { (residualBand, (uint)GraphicsLayouts.RegionRectangleSize, 1u) },
            allocations);
        Assert.Empty(frees);
        var actual = ReadRegion(bus, destinationRegion);
        Assert.Equal(expected.Header, actual.Header);
        Assert.Equal(expected.Nodes, actual.Nodes);
        Assert.Equal(
            overhangTop
                ? new[] { destinationFirst, residualBand, destinationSecond }
                : new[] { destinationFirst, destinationSecond, residualBand },
            actual.Addresses);
        Assert.Equal(sourceAllocationBefore,
            ReadBytes(bus, sourceAllocation, GraphicsLayouts.NativeRegionAllocationSize));
        Assert.Equal(sourceNodeBefore,
            ReadBytes(bus, sourceNode, GraphicsLayouts.RegionRectangleSize));

        var failedAllocations = new (uint Result, uint[] Freed)[]
        {
            (0u, Array.Empty<uint>()),
            (residualBand + 1u, new[] { residualBand + 1u }),
            (0xFFFF_FFF8u, new[] { 0xFFFF_FFF8u })
        };
        foreach (var failure in failedAllocations)
        {
            WriteOriginalRegions();
            var original = ReadRegion(bus, destinationRegion);
            var allocationStart = allocations.Count;
            var freeStart = frees.Count;
            allocationResults.Enqueue(failure.Result);

            Execute(0xD00D_0000u + (uint)allocationStart);
            Assert.Equal(0u, cpu.State.D[0]);
            Assert.Empty(allocationResults);
            Assert.Equal(1, allocations.Count - allocationStart);
            Assert.Equal(
                failure.Freed.Select(address =>
                    (address, (uint)GraphicsLayouts.RegionRectangleSize, 1u)),
                frees.Skip(freeStart));
            var preserved = ReadRegion(bus, destinationRegion);
            Assert.Equal(original.Header, preserved.Header);
            Assert.Equal(original.Nodes, preserved.Nodes);
            Assert.Equal(original.Addresses, preserved.Addresses);
            Assert.Equal(sourceAllocationBefore,
                ReadBytes(bus, sourceAllocation, GraphicsLayouts.NativeRegionAllocationSize));
            Assert.Equal(sourceNodeBefore,
                ReadBytes(bus, sourceNode, GraphicsLayouts.RegionRectangleSize));
        }
    }

    [Theory]
    [InlineData(0, true, false, false, false)]
    [InlineData(1, true, false, false, false)]
    [InlineData(2, true, false, false, false)]
    [InlineData(0, false, true, false, false)]
    [InlineData(1, false, true, false, false)]
    [InlineData(2, false, true, false, false)]
    [InlineData(0, true, true, false, false)]
    [InlineData(1, true, true, false, false)]
    [InlineData(2, true, true, false, false)]
    [InlineData(0, false, false, true, false)]
    [InlineData(1, false, false, true, false)]
    [InlineData(2, false, false, true, false)]
    [InlineData(0, false, true, false, true)]
    [InlineData(1, false, true, false, true)]
    [InlineData(3, false, true, false, true)]
    [InlineData(4, false, true, false, true)]
    [InlineData(5, false, true, false, true)]
    [InlineData(6, false, true, false, true)]
    [InlineData(7, false, true, false, true)]
    [InlineData(8, false, true, false, true)]
    [InlineData(9, false, true, false, true)]
    [InlineData(10, false, true, false, true)]
    [InlineData(11, false, true, false, true)]
    [InlineData(12, false, true, false, false)]
    [InlineData(13, false, true, false, false)]
    [InlineData(14, true, true, false, false)]
    [InlineData(15, true, true, false, false)]
    [InlineData(16, true, true, false, false)]
    [InlineData(17, true, true, false, false)]
    [InlineData(18, true, true, false, false)]
    [InlineData(19, true, true, false, false)]
    [InlineData(20, true, true, false, false)]
    [InlineData(21, true, false, false, false)]
    [InlineData(22, true, false, false, false)]
    [InlineData(23, false, true, false, false)]
    [InlineData(24, true, false, false, false)]
    [InlineData(25, true, false, false, false)]
    [InlineData(26, true, false, false, false)]
    [InlineData(27, true, false, false, false)]
    [InlineData(28, false, false, false, false)]
    [InlineData(29, false, false, false, false)]
    [InlineData(30, false, false, false, false)]
    [InlineData(31, false, false, false, false)]
    [InlineData(32, false, false, false, false)]
    [InlineData(33, false, false, false, false)]
    [InlineData(34, false, false, false, false)]
    [InlineData(35, false, false, false, false)]
    [InlineData(36, false, false, false, false)]
    [InlineData(37, false, false, false, false)]
    [InlineData(38, false, false, false, false)]
    [InlineData(39, false, false, false, false)]
    [InlineData(40, false, false, false, false)]
    [InlineData(41, false, false, false, false)]
    [InlineData(42, false, false, false, false)]
    [InlineData(43, false, false, false, false)]
    [InlineData(44, false, false, false, false)]
    [InlineData(45, true, true, false, false)]
    [InlineData(46, true, true, false, false)]
    [InlineData(47, true, true, false, false)]
    [InlineData(48, true, true, false, false)]
    [InlineData(2, false, true, false, true)]
    public void SecondNodeVerticalXOverhangMatchesPortableAndRollsBackOnAccurateM68000(
        int overhangMode,
        bool extendsAbove,
        bool extendsBelow,
        bool sourceMatchesSecondYSpan,
        bool sourceStartsAtSecondTop)
    {
        const uint codeAddress = 0x008A_0000;
        const uint execBase = 0x007A_0000;
        const uint sourceAllocation = 0x00EA_0000;
        const uint sourceNode = 0x00EA_1000;
        const uint destinationAllocation = 0x00EA_2000;
        const uint destinationFirst = 0x00EA_3000;
        const uint destinationSecond = 0x00EA_4000;
        const uint upperBand = 0x00EA_5000;
        const uint lowerBand = 0x00EA_6000;
        const uint stack = 0x00CA_0000;
        const uint returnAddress = 0x00FA_0000;
        var sourceRegion = sourceAllocation + (uint)GraphicsLayouts.NativeRegionPrivatePrefixSize;
        var destinationRegion = destinationAllocation + (uint)GraphicsLayouts.NativeRegionPrivatePrefixSize;
        var topAlignedNearMiss = overhangMode is >= 3 and <= 8;
        var combinedTopBottomNearMiss = overhangMode is >= 17 and <= 20;
        var equalXEdgeYBoundaryNearMiss = overhangMode is 24 or 25;
        var firstNodeYContactNearMiss = overhangMode == 26;
        var topListInsertion = overhangMode == 44;
        var bothProtrusionsTightening = overhangMode == 45;
        var looseEnvelopeMode = overhangMode is >= 9 and <= 16 or 21 or 22 or 23 or
            >= 27 and <= 43 or 46 or 47 or 48;
        var fallbackNearMiss = topAlignedNearMiss || combinedTopBottomNearMiss ||
            equalXEdgeYBoundaryNearMiss || firstNodeYContactNearMiss;
        var horizontalMode = overhangMode switch
        {
            3 => 0,
            4 => 1,
            5 => 0,
            6 => 1,
            7 => 0,
            8 => 1,
            9 => 0,
            10 => 1,
            11 => 2,
            12 => 0,
            13 => 1,
            14 => 0,
            15 => 1,
            16 => 2,
            17 => 0,
            18 => 1,
            19 => 0,
            20 => 1,
            21 => 2,
            22 => 2,
            23 => 2,
            24 => 2,
            25 => 2,
            26 => 2,
            27 => 2,
            28 => 2,
            29 => 2,
            30 => 2,
            31 => 0,
            32 => 1,
            33 => 2,
            34 => 2,
            35 => 2,
            36 => 0,
            37 => 1,
            38 => 0,
            39 => 1,
            40 => 0,
            41 => 1,
            42 => 2,
            43 => 2,
            44 => 2,
            45 => 2,
            46 => 0,
            47 => 1,
            48 => 2,
            _ => overhangMode
        };
        var sourceMinX = overhangMode == 42
            ? 26
            : topListInsertion || bothProtrusionsTightening
            ? 10
            : overhangMode == 24
            ? 5
            : topAlignedNearMiss && overhangMode <= 6
            ? overhangMode switch
            {
                3 or 4 or 6 => 0,
                5 => 21,
                _ => 0
            }
            : combinedTopBottomNearMiss
            ? overhangMode switch
            {
                17 or 18 or 20 => 0,
                19 => 21,
                _ => 0
            }
            : horizontalMode switch
        {
            0 => 15,
            1 => 0,
            _ => 0
        };
        var sourceMaxX = overhangMode == 42
            ? 30
            : overhangMode == 43
            ? 4
            : topListInsertion
            ? 20
            : bothProtrusionsTightening
            ? 20
            : overhangMode == 25
            ? 25
            : topAlignedNearMiss && overhangMode <= 6
            ? overhangMode switch
            {
                3 or 4 or 5 => 25,
                6 => 4,
                _ => 25
            }
            : combinedTopBottomNearMiss
            ? overhangMode switch
            {
                17 or 18 or 19 => 25,
                20 => 4,
                _ => 25
            }
            : horizontalMode switch
        {
            0 => 25,
            1 => 10,
            _ => 30
        };
        var sourceMinY = topListInsertion
            ? 6
            : bothProtrusionsTightening
            ? 6
            : sourceMatchesSecondYSpan || sourceStartsAtSecondTop
            ? 9
            : overhangMode is 21 or 22 or 24 or 25 ? 8
            : overhangMode == 26 ? 5
            : overhangMode == 27 ? 6
            : overhangMode == 28 ? 6
            : overhangMode == 29 ? 6
            : overhangMode == 30 ? 6
            : overhangMode is 31 or 32 ? 6
            : overhangMode == 33 ? 7
            : overhangMode == 34 ? 6
            : overhangMode == 35 ? 7
            : overhangMode is 36 or 37 ? 7
            : overhangMode is 38 or 39 ? 7
            : overhangMode is 40 or 41 ? 6
            : overhangMode is 42 or 43 ? 6
            : overhangMode == 23 ? 10
            : overhangMode is 12 or 13 ? 10
            : overhangMode is >= 14 and <= 20 ? 8
            : extendsAbove ? 7 : 11;
        var sourceMaxY = topListInsertion
            ? 12
            : bothProtrusionsTightening
            ? 17
            : sourceMatchesSecondYSpan
            ? 18
            : overhangMode is 21 or 24 or 25 ? 18
            : overhangMode == 22 ? 17
            : overhangMode == 23 ? 19
            : overhangMode == 26 ? 18
            : overhangMode == 27 ? 18
            : overhangMode == 28 ? 17
            : overhangMode == 29 ? 9
            : overhangMode == 30 ? 8
            : overhangMode is 31 or 32 ? 8
            : overhangMode == 33 ? 8
            : overhangMode == 34 ? 7
            : overhangMode == 35 ? 7
            : overhangMode is 36 or 37 ? 7
            : overhangMode is 38 or 39 ? 8
            : overhangMode is 40 or 41 ? 7
            : overhangMode is 42 or 43 ? 8
            : overhangMode is 9 or 10 or 11 or >= 14 and <= 20 ? 19
            : overhangMode is >= 7 and <= 8 ? 17
            : extendsAbove && extendsBelow ? 20 : extendsBelow ? 23 : 12;
        var sourceBounds = Bounds.Create(sourceMinX, sourceMinY, sourceMaxX, sourceMaxY);
        var coalescesEqualSourceWidths = horizontalMode == 2 && !(extendsAbove && extendsBelow);
        var verticalGapInsertion = overhangMode is >= 30 and <= 43;
        var singleAllocation = verticalGapInsertion || topListInsertion ||
            !fallbackNearMiss && (coalescesEqualSourceWidths ||
            sourceStartsAtSecondTop && horizontalMode != 2);
        var firstBounds = topListInsertion || bothProtrusionsTightening
            ? Bounds.Create(5, 10, 25, 13)
            : horizontalMode == 2
            ? Bounds.Create(5, 0, 25, 5)
            : Bounds.Create(0, 0, 20, 5);
        var secondBounds = topListInsertion || bothProtrusionsTightening
            ? Bounds.Create(5, 20, 25, 25)
            : horizontalMode == 0
                ? Bounds.Create(0, 9, 20, 18)
                : Bounds.Create(5, 9, 25, 18);
        var reusesSecondNodeInPlace = sourceMatchesSecondYSpan ||
            horizontalMode == 2 &&
            sourceMinY <= secondBounds.MinY &&
            sourceMaxY >= secondBounds.MaxY;
        var singleBandAddress = extendsBelow ? lowerBand : upperBand;
        var envelope = topListInsertion || bothProtrusionsTightening
            ? Bounds.Create(0, 0, 30, 30)
            : looseEnvelopeMode
            ? Bounds.Create(0, 0, 30, 30)
            : Bounds.Create(
                0,
                0,
                horizontalMode == 2 && overhangMode != 43 ? 30 : 25,
                overhangMode is 9 or 10 or 11 or >= 14 and <= 20 or 23
                    ? 19
                    : extendsAbove && extendsBelow ? 20 : extendsBelow ? 23 : 18);

        var portable = new PortableRegionRegionFixture();
        var portableSource = portable.CreateRegion(sourceBounds);
        var portableDestination = portable.CreateRegion(firstBounds, secondBounds);
        portable.SetEnvelopeAndNodeBounds(portableSource, envelope, sourceBounds);
        portable.SetEnvelopeAndNodeBounds(
            portableDestination,
            envelope,
            firstBounds,
            secondBounds);
        Assert.Equal(new[] { sourceBounds }, ReadRegion(portable.Bus, portableSource).Nodes);
        Assert.Equal(
            new[] { firstBounds, secondBounds },
            ReadRegion(portable.Bus, portableDestination).Nodes);
        Assert.True(portable.Core.OrRegionRegion(portableSource, portableDestination));
        var expected = ReadRegionWithAbsoluteNodeBounds(portable.Bus, portableDestination);
        Assert.Equal(
                fallbackNearMiss
                ? expected.Nodes
                : topListInsertion
                ? new[]
                {
                    Bounds.Create(sourceMinX, sourceMinY, sourceMaxX, firstBounds.MinY - 1),
                    firstBounds,
                    secondBounds
                }
                : sourceStartsAtSecondTop && horizontalMode != 2
                ? new[]
                {
                    firstBounds,
                    Bounds.Create(
                        Math.Min(sourceMinX, secondBounds.MinX),
                        secondBounds.MinY,
                        Math.Max(sourceMaxX, secondBounds.MaxX),
                        secondBounds.MaxY),
                    Bounds.Create(
                        sourceMinX,
                        secondBounds.MaxY + 1,
                        sourceMaxX,
                        sourceMaxY)
                }
                : sourceStartsAtSecondTop
                ? new[] { firstBounds, sourceBounds }
                : sourceMatchesSecondYSpan
                ? new[]
                {
                    firstBounds,
                    Bounds.Create(
                        Math.Min(sourceMinX, secondBounds.MinX),
                        secondBounds.MinY,
                        Math.Max(sourceMaxX, secondBounds.MaxX),
                        secondBounds.MaxY)
                }
                : verticalGapInsertion
                ? new[] { firstBounds, sourceBounds, secondBounds }
                : reusesSecondNodeInPlace
                ? new[] { firstBounds, sourceBounds }
                : overhangMode is 22 or 28 or 29
                ? new[]
                {
                    firstBounds,
                    sourceBounds,
                    Bounds.Create(secondBounds.MinX, sourceMaxY + 1, secondBounds.MaxX, secondBounds.MaxY)
                }
                : overhangMode == 23
                ? new[]
                {
                    firstBounds,
                    Bounds.Create(secondBounds.MinX, secondBounds.MinY, secondBounds.MaxX, sourceMinY - 1),
                    sourceBounds
                }
                : bothProtrusionsTightening
                ? new[]
                {
                    Bounds.Create(sourceMinX, sourceMinY, sourceMaxX, firstBounds.MinY - 1),
                    firstBounds,
                    Bounds.Create(sourceMinX, firstBounds.MaxY + 1, sourceMaxX, sourceMaxY),
                    secondBounds
                }
                : extendsAbove && extendsBelow
                ? horizontalMode switch
                {
                    0 => new[]
                    {
                        firstBounds,
                        Bounds.Create(sourceMinX, sourceMinY, sourceMaxX, secondBounds.MinY - 1),
                        Bounds.Create(
                            Math.Min(sourceMinX, secondBounds.MinX),
                            secondBounds.MinY,
                            Math.Max(sourceMaxX, secondBounds.MaxX),
                            secondBounds.MaxY),
                        Bounds.Create(
                            sourceMinX,
                            secondBounds.MaxY + 1,
                            sourceMaxX,
                            sourceMaxY)
                    },
                    1 => new[]
                    {
                        firstBounds,
                        Bounds.Create(sourceMinX, sourceMinY, sourceMaxX, secondBounds.MinY - 1),
                        Bounds.Create(
                            Math.Min(sourceMinX, secondBounds.MinX),
                            secondBounds.MinY,
                            Math.Max(sourceMaxX, secondBounds.MaxX),
                            secondBounds.MaxY),
                        Bounds.Create(
                            sourceMinX,
                            secondBounds.MaxY + 1,
                            sourceMaxX,
                            sourceMaxY)
                    },
                    _ => new[] { firstBounds, sourceBounds }
                }
                : extendsBelow
                ? horizontalMode switch
                {
                    0 => new[]
                    {
                        firstBounds,
                        Bounds.Create(0, secondBounds.MinY, 20, sourceMinY - 1),
                        Bounds.Create(0, sourceMinY, 25, secondBounds.MaxY),
                        Bounds.Create(15, 19, 25, 23)
                    },
                    1 => new[]
                    {
                        firstBounds,
                        Bounds.Create(5, secondBounds.MinY, 25, sourceMinY - 1),
                        Bounds.Create(0, sourceMinY, 25, secondBounds.MaxY),
                        Bounds.Create(0, 19, 10, 23)
                    },
                    _ => new[]
                    {
                        firstBounds,
                        Bounds.Create(5, 9, 25, 10),
                        Bounds.Create(0, 11, 30, 23)
                    }
                }
                : overhangMode switch
                {
                    0 => new[]
                    {
                        firstBounds,
                        Bounds.Create(15, 7, 25, 8),
                        Bounds.Create(0, 9, 25, 12),
                        Bounds.Create(0, 13, 20, 18)
                    },
                    1 => new[]
                    {
                        firstBounds,
                        Bounds.Create(0, 7, 10, 8),
                        Bounds.Create(0, 9, 25, 12),
                        Bounds.Create(5, 13, 25, 18)
                    },
                    _ => new[]
                    {
                        firstBounds,
                        Bounds.Create(0, 7, 30, 12),
                        Bounds.Create(5, 13, 25, 18)
                    }
                },
            expected.Nodes);

        var nativeCode = Image.Value.Code;
        var bus = new AmigaBus();
        bus.MapWritableMemory(codeAddress, nativeCode);
        bus.MapWritableMemory(4, new byte[4]);
        bus.WriteLong(4, execBase);
        bus.MapWritableMemory(sourceAllocation, new byte[GraphicsLayouts.NativeRegionAllocationSize]);
        bus.MapWritableMemory(sourceNode, new byte[GraphicsLayouts.RegionRectangleSize]);
        bus.MapWritableMemory(destinationAllocation, new byte[GraphicsLayouts.NativeRegionAllocationSize]);
        foreach (var node in new[] { destinationFirst, destinationSecond, upperBand, lowerBand })
            bus.MapWritableMemory(node, new byte[GraphicsLayouts.RegionRectangleSize]);
        bus.MapWritableMemory(stack, new byte[0x400]);

        void WriteNode(uint node, uint previous, uint next, Bounds bounds)
        {
            bus.WriteLong(node + (uint)GraphicsLayouts.RegionRectanglePrevious, previous);
            bus.WriteLong(node + (uint)GraphicsLayouts.RegionRectangleNext, next);
            WriteBounds(bus, node + (uint)GraphicsLayouts.RegionRectangleBounds, bounds);
        }

        void WriteOriginalRegions()
        {
            bus.WriteWord(sourceAllocation, GraphicsLayouts.NativeRegionMarker);
            bus.WriteWord(destinationAllocation, GraphicsLayouts.NativeRegionMarker);
            WriteBounds(bus, sourceRegion, envelope);
            WriteBounds(bus, destinationRegion, envelope);
            WriteNode(
                sourceNode,
                sourceRegion + (uint)GraphicsLayouts.RegionRectangle,
                0,
                sourceBounds);
            bus.WriteLong(sourceRegion + (uint)GraphicsLayouts.RegionRectangle, sourceNode);
            WriteNode(
                destinationFirst,
                destinationRegion + (uint)GraphicsLayouts.RegionRectangle,
                destinationSecond,
                firstBounds);
            WriteNode(destinationSecond, destinationFirst, 0, secondBounds);
            bus.WriteLong(
                destinationRegion + (uint)GraphicsLayouts.RegionRectangle,
                destinationFirst);
        }

        var allocationResults = new Queue<uint>();
        var allocations = new List<(uint Address, uint Size, uint Flags)>();
        var frees = new List<(uint Address, uint Size, uint Flags)>();
        bus.RegisterHostGateway(unchecked((uint)((int)execBase - 198)), state =>
        {
            var address = allocationResults.Dequeue();
            allocations.Add((address, state.D[0], state.D[1]));
            state.D[0] = address;
        });
        bus.RegisterHostGateway(unchecked((uint)((int)execBase - 210)), state =>
            frees.Add((state.A[0], state.D[0], state.D[1])));

        using var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
        void Execute(uint seed)
        {
            bus.WriteLong(stack + 0x80u, returnAddress);
            cpu.Reset(codeAddress + (uint)Image.Value.Entries[GraphicsLvo.OrRegionRegion], stack + 0x80u);
            cpu.State.A[0] = sourceRegion;
            cpu.State.A[1] = destinationRegion;
            cpu.State.D[0] = seed;
            for (var index = 0; index < 8192; index++)
            {
                cpu.ExecuteInstruction();
                if (cpu.State.ProgramCounter == returnAddress)
                    return;
            }

            throw new InvalidOperationException(
                $"Native second-node {(extendsAbove && extendsBelow ? "top-and-bottom-Y" : extendsBelow ? "bottom-Y" : "top-Y")} X-overhang OrRegionRegion did not return (fallback {Image.Value.Fallback}).");
        }

        WriteOriginalRegions();
        var sourceAllocationBefore = ReadBytes(bus, sourceAllocation, GraphicsLayouts.NativeRegionAllocationSize);
        var sourceNodeBefore = ReadBytes(bus, sourceNode, GraphicsLayouts.RegionRectangleSize);
        if (fallbackNearMiss)
        {
            var original = ReadRegion(bus, destinationRegion);
            const uint fallbackSeed = 0xD00D_4242u;
            Execute(fallbackSeed);
            Assert.Equal(fallbackSeed, cpu.State.D[0]);
            Assert.Empty(allocations);
            Assert.Empty(frees);
            Assert.Empty(allocationResults);
            var preserved = ReadRegion(bus, destinationRegion);
            Assert.Equal(original.Header, preserved.Header);
            Assert.Equal(original.Nodes, preserved.Nodes);
            Assert.Equal(original.Addresses, preserved.Addresses);
            Assert.Equal(sourceAllocationBefore,
                ReadBytes(bus, sourceAllocation, GraphicsLayouts.NativeRegionAllocationSize));
            Assert.Equal(sourceNodeBefore,
                ReadBytes(bus, sourceNode, GraphicsLayouts.RegionRectangleSize));
            return;
        }
        if (!reusesSecondNodeInPlace)
        {
            allocationResults.Enqueue(singleAllocation ? singleBandAddress : upperBand);
            if (!singleAllocation)
                allocationResults.Enqueue(lowerBand);
        }
        Execute(0xA1B2_C3D4u);
        Assert.Equal(1u, cpu.State.D[0]);
        if (reusesSecondNodeInPlace)
        {
            Assert.Empty(allocations);
        }
        else
        {
            Assert.Equal(
                singleAllocation
                    ? new[] { (singleBandAddress, (uint)GraphicsLayouts.RegionRectangleSize, 1u) }
                    : new[]
                    {
                        (upperBand, (uint)GraphicsLayouts.RegionRectangleSize, 1u),
                        (lowerBand, (uint)GraphicsLayouts.RegionRectangleSize, 1u)
                    },
                allocations);
        }
        Assert.Empty(frees);
        var actual = ReadRegionWithAbsoluteNodeBounds(bus, destinationRegion);
        Assert.Equal(expected.Header, actual.Header);
        Assert.Equal(expected.Nodes, actual.Nodes);
        Assert.Equal(
            reusesSecondNodeInPlace
                ? new[] { destinationFirst, destinationSecond }
                : singleAllocation
                    ? topListInsertion
                        ? new[] { singleBandAddress, destinationFirst, destinationSecond }
                        : extendsBelow
                        ? new[] { destinationFirst, destinationSecond, singleBandAddress }
                        : new[] { destinationFirst, singleBandAddress, destinationSecond }
                : bothProtrusionsTightening
                    ? new[] { upperBand, destinationFirst, lowerBand, destinationSecond }
                : new[] { destinationFirst, upperBand, destinationSecond, lowerBand },
            actual.Addresses);
        Assert.Equal(sourceAllocationBefore,
            ReadBytes(bus, sourceAllocation, GraphicsLayouts.NativeRegionAllocationSize));
        Assert.Equal(sourceNodeBefore,
            ReadBytes(bus, sourceNode, GraphicsLayouts.RegionRectangleSize));

        var failedAllocations = reusesSecondNodeInPlace
            ? Array.Empty<(uint[] Results, uint[] Freed)>()
            : singleAllocation
            ? new (uint[] Results, uint[] Freed)[]
            {
                (new[] { 0u }, Array.Empty<uint>()),
                (new[] { singleBandAddress + 1u }, new[] { singleBandAddress + 1u }),
                (new[] { 0xFFFF_FFF8u }, new[] { 0xFFFF_FFF8u })
            }
            : new (uint[] Results, uint[] Freed)[]
            {
                (new[] { 0u }, Array.Empty<uint>()),
                (new[] { upperBand + 1u }, new[] { upperBand + 1u }),
                (new[] { 0xFFFF_FFF8u }, new[] { 0xFFFF_FFF8u }),
                (new[] { upperBand, 0u }, new[] { upperBand }),
                (new[] { upperBand, lowerBand + 1u }, new[] { lowerBand + 1u, upperBand }),
                (new[] { upperBand, 0xFFFF_FFF8u }, new[] { 0xFFFF_FFF8u, upperBand })
            };
        foreach (var failure in failedAllocations)
        {
            WriteOriginalRegions();
            var original = ReadRegion(bus, destinationRegion);
            var allocationStart = allocations.Count;
            var freeStart = frees.Count;
            foreach (var result in failure.Results)
                allocationResults.Enqueue(result);

            Execute(0xD00D_0000u + (uint)allocationStart);
            Assert.Equal(0u, cpu.State.D[0]);
            Assert.Empty(allocationResults);
            Assert.Equal(failure.Results.Length, allocations.Count - allocationStart);
            Assert.Equal(
                failure.Freed.Select(address =>
                    (address, (uint)GraphicsLayouts.RegionRectangleSize, 1u)),
                frees.Skip(freeStart));
            var preserved = ReadRegion(bus, destinationRegion);
            Assert.Equal(original.Header, preserved.Header);
            Assert.Equal(original.Nodes, preserved.Nodes);
            Assert.Equal(original.Addresses, preserved.Addresses);
            Assert.Equal(sourceAllocationBefore,
                ReadBytes(bus, sourceAllocation, GraphicsLayouts.NativeRegionAllocationSize));
            Assert.Equal(sourceNodeBefore,
                ReadBytes(bus, sourceNode, GraphicsLayouts.RegionRectangleSize));
        }
    }

    [Theory]
    [InlineData(false, false, 6)]
    [InlineData(false, false, 11)]
    [InlineData(false, false, 1)]
    [InlineData(false, false, 0)]
    [InlineData(false, false, -1)]
    [InlineData(false, true, 6)]
    [InlineData(false, true, 11)]
    [InlineData(false, true, 1)]
    [InlineData(false, true, 0)]
    [InlineData(false, true, -1)]
    [InlineData(true, false, 6)]
    [InlineData(true, false, 11)]
    [InlineData(true, false, 1)]
    [InlineData(true, false, 0)]
    [InlineData(true, false, -1)]
    [InlineData(true, true, 6)]
    [InlineData(true, true, 11)]
    [InlineData(true, true, 1)]
    [InlineData(true, true, 0)]
    [InlineData(true, true, -1)]
    public void VerticalTsSharingOneXEdgeMatchPortableAcrossYContactsInBothSourceOrders(
        bool extendsBottom,
        bool sharesLeftEdge,
        int overlappingRows)
    {
        var bar = Bounds.Create(10, 10, 20, 20);
        var stem = Bounds.Create(
            sharesLeftEdge ? 10 : 16,
            extendsBottom ? 21 - overlappingRows : 5,
            sharesLeftEdge ? 14 : 20,
            extendsBottom ? 25 : 9 + overlappingRows);

        // Zero is adjacency and -1 leaves a real empty row. Eleven aligns
        // the opposite Y edge: the bottom/left mirror then ties BOTH MinY
        // and MinX, so ordinary node sorting cannot select the right writer.
        AssertNativeUnionMatchesPortable(bar, stem, expectedNodes: 2, poisonExecRegisters: true);
        AssertNativeUnionMatchesPortable(stem, bar, expectedNodes: 2, poisonExecRegisters: true);
    }

    [Theory]
    [InlineData(0, 0, 1, 1, 0, -1, 0, 0, 2)]
    [InlineData(-1, -1, 0, 0, 0, 0, 0, 1, 2)]
    [InlineData(10, 10, 20, 10, 10, 5, 10, 10, 2)]
    [InlineData(10, 10, 20, 10, 20, 10, 20, 15, 2)]
    [InlineData(-32768, -32767, -32767, -32766, -32768, -32768, -32768, -32767, 2)]
    [InlineData(32766, 32765, 32767, 32766, 32767, 32766, 32767, 32767, 2)]
    [InlineData(32766, 10, 32767, 20, 32767, 5, 32767, 15, 2)]
    [InlineData(-32768, 10, -32767, 20, -32768, 15, -32768, 25, 2)]
    [InlineData(10, -10, 20, 0, 10, -32767, 14, -5, 2)]
    [InlineData(10, 0, 20, 10, 16, 5, 20, 32767, 2)]
    [InlineData(-32768, 10, -1, 20, -32768, 5, -20000, 15, 2)]
    [InlineData(0, 10, 32767, 20, 20000, 15, 32767, 25, 2)]
    [InlineData(10, 10, 20, 20, 10, 5, 14, 25, 3)]
    [InlineData(10, 10, 20, 20, 16, 5, 20, 25, 3)]
    [InlineData(0, 1, 1, 1, 0, 0, 0, 2, 3)]
    [InlineData(0, 1, 1, 1, 1, 0, 1, 2, 3)]
    public void VerticalTsSharingOneXEdgeHandlePixelAndSignedBoundariesInBothSourceOrders(
        int barMinX, int barMinY, int barMaxX, int barMaxY,
        int stemMinX, int stemMinY, int stemMaxX, int stemMaxY,
        int expectedNodes)
    {
        var bar = Bounds.Create(barMinX, barMinY, barMaxX, barMaxY);
        var stem = Bounds.Create(stemMinX, stemMinY, stemMaxX, stemMaxY);

        // A stem spanning both Y edges remains a three-band union even
        // when it shares one X edge; inclusive T admission must not steal it.
        AssertNativeUnionMatchesPortable(bar, stem, expectedNodes, poisonExecRegisters: true);
        AssertNativeUnionMatchesPortable(stem, bar, expectedNodes, poisonExecRegisters: true);
    }

    [Theory]
    [InlineData(10, -10, 20, 0, 10, -32768, 14, -5, true)]
    [InlineData(10, -1, 20, 10, 16, 5, 20, 32767, true)]
    [InlineData(-32768, 5, -20000, 15, -32768, 10, 0, 20, false)]
    [InlineData(20000, 15, 32767, 25, -1, 10, 32767, 20, false)]
    public void VerticalTsSharingOneXEdgeDeclineUnrepresentableBoundsBeforeAllocation(
        int oldMinX, int oldMinY, int oldMaxX, int oldMaxY,
        int minX, int minY, int maxX, int maxY,
        bool reverseSources)
    {
        var original = Bounds.Create(oldMinX, oldMinY, oldMaxX, oldMaxY);
        var input = Bounds.Create(minX, minY, maxX, maxY);
        var width = Math.Max(original.MaxX, input.MaxX) - Math.Min(original.MinX, input.MinX);
        var height = Math.Max(original.MaxY, input.MaxY) - Math.Min(original.MinY, input.MinY);
        Assert.Equal(32768, Math.Max(width, height));

        // X containment means a width overflow makes the wider bar itself
        // unrepresentable. Seed the valid stem for those two cases only.
        for (var order = 0; order < (reverseSources ? 2 : 1); order++)
        {
            var seed = order == 0 ? original : input;
            var added = order == 0 ? input : original;
            var portable = new PortableRegion(seed);
            using var native = new NativeRegion(seed, poisonExecRegisters: true);

            Assert.False(portable.Union(added));
            var result = native.Union(added);

            Assert.True(result.UsedFallback);
            Assert.Equal(NativeRegion.CapturedFrame, result.Value);
            Assert.Empty(native.Allocations);
            Assert.Empty(native.Frees);
            native.AssertOriginalRegionUnchanged();
        }
    }

    [Theory]
    [InlineData(10, 10, 20, 20, 10, 5, 20, 15, 1)]
    [InlineData(10, 10, 20, 20, 10, 15, 20, 25, 1)]
    [InlineData(10, 10, 20, 20, 10, 5, 20, 25, 1)]
    [InlineData(32767, -1, 32767, 0, 32767, 0, 32767, 1, 1)]
    [InlineData(10, 10, 20, 20, 10, 5, 20, 9, 1)]
    [InlineData(10, 10, 20, 20, 10, 21, 20, 25, 1)]
    [InlineData(10, 10, 20, 20, 10, 5, 20, 8, 2)]
    [InlineData(10, 10, 20, 20, 10, 22, 20, 25, 2)]
    public void VerticalTEqualSpanGuardsPreserveInPlaceCoalescingAndTrueGaps(
        int oldMinX, int oldMinY, int oldMaxX, int oldMaxY,
        int minX, int minY, int maxX, int maxY,
        int expectedNodes)
    {
        var original = Bounds.Create(oldMinX, oldMinY, oldMaxX, oldMaxY);
        var input = Bounds.Create(minX, minY, maxX, maxY);
        var reuseOriginalNode = expectedNodes == 1;

        AssertNativeUnionMatchesPortable(original, input, expectedNodes,
            reuseOriginalNode: reuseOriginalNode, poisonExecRegisters: true);
        AssertNativeUnionMatchesPortable(input, original, expectedNodes,
            reuseOriginalNode: reuseOriginalNode, poisonExecRegisters: true);
    }

    public static IEnumerable<object[]> SharedXEdgeVerticalTAllocationFailureCases()
    {
        foreach (var extendsBottom in new[] { false, true })
        foreach (var sharesLeftEdge in new[] { false, true })
        foreach (var failedAddress in new[] { 0u, 0x00D0_8001u, 0xFFFF_FFF8u })
        for (var slot = 1; slot <= 2; slot++)
            yield return new object[] { extendsBottom, sharesLeftEdge, slot, failedAddress };
    }

    [Theory]
    [MemberData(nameof(SharedXEdgeVerticalTAllocationFailureCases))]
    public void VerticalTsSharingOneXEdgeRetainOriginalRegionAndInputOnAllocationFailure(
        bool extendsBottom,
        bool sharesLeftEdge,
        int failingAllocation,
        uint failedAddress)
    {
        var bar = Bounds.Create(10, 10, 20, 20);
        var stem = Bounds.Create(
            sharesLeftEdge ? 10 : 16, extendsBottom ? 15 : 5,
            sharesLeftEdge ? 14 : 20, extendsBottom ? 25 : 15);

        AssertAllocationFailureRetainsOriginalRegion(stem, failingAllocation, failedAddress,
            original: bar, poisonExecRegisters: true);
        AssertAllocationFailureRetainsOriginalRegion(bar, failingAllocation, failedAddress,
            original: stem, poisonExecRegisters: true);
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, false, 1)]
    [InlineData(false, false, 2)]
    [InlineData(false, true, 0)]
    [InlineData(false, true, 1)]
    [InlineData(false, true, 2)]
    [InlineData(true, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 0)]
    [InlineData(true, true, 1)]
    [InlineData(true, true, 2)]
    public void CrossingYCornerUnionsDistinguishXOverlapAdjacencyAndGapsInBothSourceOrders(
        bool extendsLeft,
        bool extendsAbove,
        int xSeparation)
    {
        var original = Bounds.Create(10, 10, 20, 20);
        var input = Bounds.Create(
            extendsLeft ? 5 : 20 + xSeparation,
            extendsAbove ? 5 : 15,
            extendsLeft ? 10 - xSeparation : 25,
            extendsAbove ? 15 : 25);
        // Separation zero shares one column, one is pixel adjacency,
        // and two leaves an uncovered column. Only the gap stays disjoint.
        var expectedNodes = xSeparation == 2 ? 2 : 3;

        AssertNativeUnionMatchesPortable(original, input, expectedNodes, poisonExecRegisters: true);
        AssertNativeUnionMatchesPortable(input, original, expectedNodes, poisonExecRegisters: true);
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, false, 1)]
    [InlineData(false, false, 2)]
    [InlineData(false, true, 0)]
    [InlineData(false, true, 1)]
    [InlineData(false, true, 2)]
    [InlineData(true, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 0)]
    [InlineData(true, true, 1)]
    [InlineData(true, true, 2)]
    public void SharedYEdgeUnionsKeepTwoCanonicalBandsInBothSourceOrders(
        bool sharesMaxY,
        bool shorterOnLeft,
        int xSeparation)
    {
        var longer = Bounds.Create(10, 10, 20, 20);
        var shorter = Bounds.Create(
            shorterOnLeft ? 5 : 20 + xSeparation,
            sharesMaxY ? 15 : 10,
            shorterOnLeft ? 10 - xSeparation : 25,
            sharesMaxY ? 20 : 14);

        // Connected intervals have one full-width band and one narrower
        // tail. The gap also has two nodes, but different canonical bounds.
        AssertNativeUnionMatchesPortable(longer, shorter, expectedNodes: 2, poisonExecRegisters: true);
        AssertNativeUnionMatchesPortable(shorter, longer, expectedNodes: 2, poisonExecRegisters: true);
    }

    [Theory]
    [InlineData(0, 0, 0, 1, 1, 1, 1, 2, 3)]
    [InlineData(-1, -1, -1, 0, 0, 0, 0, 1, 3)]
    [InlineData(-32768, -32768, -32768, -32767, -32767, -32767, -32767, -32766, 3)]
    [InlineData(32766, 32765, 32766, 32766, 32767, 32766, 32767, 32767, 3)]
    [InlineData(-32768, -10, -20, 0, -19, -5, -1, 5, 3)]
    [InlineData(0, -32768, 10, -10, 11, -20, 20, -1, 3)]
    [InlineData(0, 0, 0, 0, 1, 0, 1, 1, 2)]
    [InlineData(-1, 0, -1, 1, 0, 1, 0, 1, 2)]
    [InlineData(-32768, -32768, -32768, -32768, -32767, -32768, -32767, -32767, 2)]
    [InlineData(32766, 32766, 32766, 32767, 32767, 32767, 32767, 32767, 2)]
    [InlineData(-32768, 10, -20, 14, -19, 10, -1, 20, 2)]
    [InlineData(-32768, 15, -20, 20, -19, 10, -1, 20, 2)]
    [InlineData(0, -32768, 10, -20, 11, -32768, 20, -1, 2)]
    [InlineData(0, -20, 10, -1, 11, -32768, 20, -1, 2)]
    [InlineData(10, 10, 20, 20, 5, 10, 25, 10, 2)]
    [InlineData(10, 10, 20, 20, 5, 20, 25, 20, 2)]
    public void ConnectedCornerAndSharedYEdgeUnionsHandlePixelStripsAndSignedBoundaries(
        int oldMinX, int oldMinY, int oldMaxX, int oldMaxY,
        int minX, int minY, int maxX, int maxY,
        int expectedNodes)
    {
        var original = Bounds.Create(oldMinX, oldMinY, oldMaxX, oldMaxY);
        var input = Bounds.Create(minX, minY, maxX, maxY);

        AssertNativeUnionMatchesPortable(original, input, expectedNodes, poisonExecRegisters: true);
        AssertNativeUnionMatchesPortable(input, original, expectedNodes, poisonExecRegisters: true);
    }

    [Theory]
    [InlineData(-32768, -10, -20, 0, -19, -5, 0, 5, true)]
    [InlineData(0, -32768, 10, -10, 11, -20, 20, 0, true)]
    [InlineData(-32768, 10, -20, 14, -19, 10, 0, 20, true)]
    [InlineData(-32768, 15, -20, 20, -19, 10, 0, 20, true)]
    [InlineData(0, -32768, 10, -20, 11, -32768, 20, 0, false)]
    [InlineData(0, -20, 10, 0, 11, -32768, 20, 0, false)]
    public void ConnectedCornerAndSharedYEdgeUnrepresentableBoundsDeclineBeforeAllocation(
        int oldMinX, int oldMinY, int oldMaxX, int oldMaxY,
        int minX, int minY, int maxX, int maxY,
        bool reverseSources)
    {
        var original = Bounds.Create(oldMinX, oldMinY, oldMaxX, oldMaxY);
        var input = Bounds.Create(minX, minY, maxX, maxY);
        var width = Math.Max(original.MaxX, input.MaxX) - Math.Min(original.MinX, input.MinX);
        var height = Math.Max(original.MaxY, input.MaxY) - Math.Min(original.MinY, input.MinY);
        Assert.Equal(32768, Math.Max(width, height));

        // A shared-Y-edge height overflow makes the longer rectangle
        // unrepresentable even alone, so only the shorter source can seed
        // that case. Width overflows exercise both representable seeds.
        for (var order = 0; order < (reverseSources ? 2 : 1); order++)
        {
            var seed = order == 0 ? original : input;
            var added = order == 0 ? input : original;
            var portable = new PortableRegion(seed);
            using var native = new NativeRegion(seed, poisonExecRegisters: true);

            Assert.False(portable.Union(added));
            var result = native.Union(added);

            Assert.True(result.UsedFallback);
            Assert.Equal(NativeRegion.CapturedFrame, result.Value);
            Assert.Empty(native.Allocations);
            Assert.Empty(native.Frees);
            native.AssertOriginalRegionUnchanged();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SharedYEdgeContainmentRetainsOneOriginalNodeWithoutExecInBothSourceOrders(
        bool sharesMaxY,
        bool touchesRightEdge)
    {
        var longer = Bounds.Create(10, 10, 20, 20);
        var shorter = Bounds.Create(
            touchesRightEdge ? 16 : 10, sharesMaxY ? 15 : 10,
            touchesRightEdge ? 20 : 14, sharesMaxY ? 20 : 14);

        AssertNativeUnionMatchesPortable(longer, shorter, expectedNodes: 1,
            reuseOriginalNode: true, poisonExecRegisters: true);
        AssertNativeUnionMatchesPortable(shorter, longer, expectedNodes: 1,
            reuseOriginalNode: true, poisonExecRegisters: true);
    }

    public static IEnumerable<object[]> ConnectedEdgeAllocationFailureCases()
    {
        var scenarios = new (string Name, int Slots)[]
        {
            ("AdjacentL", 3),
            ("SharedMinRegionShorter", 2),
            ("SharedMinRegionLonger", 2),
            ("SharedMaxRegionShorter", 2),
            ("SharedMaxRegionLonger", 2)
        };
        foreach (var scenario in scenarios)
        foreach (var failedAddress in new[] { 0u, 0x00D0_8001u, 0xFFFF_FFF8u })
        for (var slot = 1; slot <= scenario.Slots; slot++)
            yield return new object[] { scenario.Name, slot, failedAddress };
    }

    [Theory]
    [MemberData(nameof(ConnectedEdgeAllocationFailureCases))]
    public void ConnectedCornerAndSharedYEdgeAllocationFailuresRetainOriginalRegionAndInput(
        string scenario,
        int failingAllocation,
        uint failedAddress)
    {
        var longer = Bounds.Create(15, 10, 20, 20);
        var sharedMinShorter = Bounds.Create(10, 10, 14, 14);
        var sharedMaxShorter = Bounds.Create(10, 15, 14, 20);
        var (original, input) = scenario switch
        {
            "AdjacentL" => (Bounds.Create(0, 0, 0, 1), Bounds.Create(1, 1, 1, 2)),
            "SharedMinRegionShorter" => (sharedMinShorter, longer),
            "SharedMinRegionLonger" => (longer, sharedMinShorter),
            "SharedMaxRegionShorter" => (sharedMaxShorter, longer),
            "SharedMaxRegionLonger" => (longer, sharedMaxShorter),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        AssertAllocationFailureRetainsOriginalRegion(
            input, failingAllocation, failedAddress, original: original, poisonExecRegisters: true);
    }

    [Theory]
    [InlineData(21, 10, 25, 20, 0, 0)]
    [InlineData(5, 10, 9, 20, 0, 0)]
    [InlineData(10, 21, 20, 25, 0, 0)]
    [InlineData(10, 5, 20, 9, 0, 0)]
    [InlineData(21, 10, 25, 20, -30, -30)]
    [InlineData(5, 10, 9, 20, -30, -30)]
    [InlineData(10, 21, 20, 25, -30, -30)]
    [InlineData(10, 5, 20, 9, -30, -30)]
    [InlineData(21, 10, 25, 20, -32773, -32773)]
    [InlineData(5, 10, 9, 20, -32773, -32773)]
    [InlineData(10, 21, 20, 25, -32773, -32773)]
    [InlineData(10, 5, 20, 9, -32773, -32773)]
    [InlineData(21, 10, 25, 20, 32742, 32742)]
    [InlineData(5, 10, 9, 20, 32742, 32742)]
    [InlineData(10, 21, 20, 25, 32742, 32742)]
    [InlineData(10, 5, 20, 9, 32742, 32742)]
    public void EqualSpanAdjacencyCoalescesInEveryDirectionWithoutExec(
        int minX, int minY, int maxX, int maxY,
        int translateX, int translateY)
    {
        AssertNativeUnionMatchesPortable(
            Bounds.Create(10, 10, 20, 20).Translate(translateX, translateY),
            Bounds.Create(minX, minY, maxX, maxY).Translate(translateX, translateY),
            expectedNodes: 1,
            reuseOriginalNode: true);
    }

    [Theory]
    [InlineData(20, 10, 25, 20)]
    [InlineData(5, 10, 10, 20)]
    [InlineData(10, 20, 20, 25)]
    [InlineData(10, 5, 20, 10)]
    [InlineData(15, 10, 25, 20)]
    [InlineData(5, 10, 15, 20)]
    [InlineData(10, 15, 20, 25)]
    [InlineData(10, 5, 20, 15)]
    [InlineData(10, 10, 20, 20)]
    public void EqualSpanOverlapAndIdenticalBoundsRetainOneNodeWithoutExec(
        int minX, int minY, int maxX, int maxY)
    {
        AssertNativeUnionMatchesPortable(
            Bounds.Create(10, 10, 20, 20),
            Bounds.Create(minX, minY, maxX, maxY),
            expectedNodes: 1,
            reuseOriginalNode: true);
    }

    [Theory]
    [InlineData(22, 10, 26, 20, 0)]
    [InlineData(4, 10, 8, 20, 0)]
    [InlineData(10, 22, 20, 26, 0)]
    [InlineData(10, 4, 20, 8, 0)]
    [InlineData(22, 10, 26, 20, -30)]
    [InlineData(4, 10, 8, 20, -30)]
    [InlineData(10, 22, 20, 26, -30)]
    [InlineData(10, 4, 20, 8, -30)]
    public void EqualSpanOnePixelGapsRetainTwoSeparateCanonicalNodes(
        int minX, int minY, int maxX, int maxY,
        int translation)
    {
        AssertNativeUnionMatchesPortable(
            Bounds.Create(10, 10, 20, 20).Translate(translation, translation),
            Bounds.Create(minX, minY, maxX, maxY).Translate(translation, translation),
            expectedNodes: 2);
    }

    [Theory]
    [InlineData(0, 10, 10, 20, 11, 10, 32767, 20)]
    [InlineData(-10, 10, -1, 20, -32768, 10, -11, 20)]
    [InlineData(10, 0, 20, 10, 10, 11, 20, 32767)]
    [InlineData(10, -10, 20, -1, 10, -32768, 20, -11)]
    [InlineData(-100, 10, 10, 20, 11, 10, 32667, 20)]
    [InlineData(10, 10, 20, 20, -32747, 10, 9, 20)]
    [InlineData(10, -100, 20, 10, 10, 11, 20, 32667)]
    [InlineData(10, 10, 20, 20, 10, -32747, 20, 9)]
    public void EqualSpanAdjacencyAcceptsMaximumRelativeDimensionsWithoutExec(
        int oldMinX, int oldMinY, int oldMaxX, int oldMaxY,
        int minX, int minY, int maxX, int maxY)
    {
        AssertNativeUnionMatchesPortable(
            Bounds.Create(oldMinX, oldMinY, oldMaxX, oldMaxY),
            Bounds.Create(minX, minY, maxX, maxY),
            expectedNodes: 1,
            reuseOriginalNode: true);
    }

    [Theory]
    [InlineData(-1, 10, -1, 20, 0, 10, 0, 20)]
    [InlineData(0, 10, 0, 20, -1, 10, -1, 20)]
    [InlineData(10, -1, 20, -1, 10, 0, 20, 0)]
    [InlineData(10, 0, 20, 0, 10, -1, 20, -1)]
    [InlineData(32766, 10, 32766, 20, 32767, 10, 32767, 20)]
    [InlineData(32767, 10, 32767, 20, 32766, 10, 32766, 20)]
    [InlineData(10, 32766, 20, 32766, 10, 32767, 20, 32767)]
    [InlineData(10, 32767, 20, 32767, 10, 32766, 20, 32766)]
    public void SingleColumnAndRowAdjacencyHandlesIncrementBoundariesInBothOrders(
        int oldMinX, int oldMinY, int oldMaxX, int oldMaxY,
        int minX, int minY, int maxX, int maxY)
    {
        AssertNativeUnionMatchesPortable(
            Bounds.Create(oldMinX, oldMinY, oldMaxX, oldMaxY),
            Bounds.Create(minX, minY, maxX, maxY),
            expectedNodes: 1,
            reuseOriginalNode: true);
    }

    [Theory]
    [InlineData(8, 18, 19, 30, 15, 25, 21, 32)]
    [InlineData(15, 18, 26, 30, 8, 25, 21, 32)]
    [InlineData(8, 25, 19, 37, 15, 18, 21, 30)]
    [InlineData(15, 25, 26, 37, 8, 18, 21, 30)]
    [InlineData(10, 10, 20, 20, 5, 14, 25, 16)]
    [InlineData(10, 10, 20, 20, 14, 5, 16, 25)]
    public void LMirrorsAndCrossingBarsMatchPortableCanonicalBands(
        int oldMinX, int oldMinY, int oldMaxX, int oldMaxY,
        int minX, int minY, int maxX, int maxY)
    {
        AssertNativeUnionMatchesPortable(
            Bounds.Create(oldMinX, oldMinY, oldMaxX, oldMaxY),
            Bounds.Create(minX, minY, maxX, maxY),
            expectedNodes: 3);
    }

    [Theory]
    [InlineData(10, 10, 20, 20, 5, 14, 15, 16)]
    [InlineData(10, 10, 20, 20, 15, 14, 25, 16)]
    [InlineData(8, 18, 19, 30, 15, 25, 21, 32)]
    [InlineData(10, 10, 20, 20, 14, 5, 16, 25)]
    [InlineData(-32768, -20000, -1, -10000, -16000, -32768, -15000, -1)]
    [InlineData(0, 10000, 32767, 20000, 16000, 0, 17000, 32767)]
    public void ExistingThreeNodeUnionsPreserveInputAcrossVolatileExec(
        int oldMinX, int oldMinY, int oldMaxX, int oldMaxY,
        int minX, int minY, int maxX, int maxY)
    {
        AssertNativeUnionMatchesPortable(
            Bounds.Create(oldMinX, oldMinY, oldMaxX, oldMaxY),
            Bounds.Create(minX, minY, maxX, maxY),
            expectedNodes: 3,
            poisonExecRegisters: true);
    }

    [Theory]
    [InlineData(false, 0, 0)]
    [InlineData(true, 0, 0)]
    [InlineData(false, -30, -30)]
    [InlineData(true, -30, -30)]
    [InlineData(false, -32770, -32770)]
    [InlineData(true, -32770, -32770)]
    [InlineData(false, 32740, 32740)]
    [InlineData(true, 32740, 32740)]
    public void HorizontalTMirrorsMatchPortableCanonicalBands(
        bool extendsRight,
        int translateX,
        int translateY)
    {
        var original = Bounds.Create(10, 10, 20, 20).Translate(translateX, translateY);
        var input = (extendsRight
            ? Bounds.Create(15, 14, 25, 16)
            : Bounds.Create(5, 14, 15, 16)).Translate(translateX, translateY);

        AssertNativeUnionMatchesPortable(original, input, expectedNodes: 3);
    }

    [Theory]
    [InlineData(false, 0, 3)]
    [InlineData(true, 0, 3)]
    [InlineData(false, -1, 3)]
    [InlineData(true, -1, 3)]
    [InlineData(false, -2, 2)]
    [InlineData(true, -2, 2)]
    public void HorizontalTEdgesDistinguishOneColumnOverlapAdjacencyAndGaps(
        bool extendsRight,
        int overlap,
        int expectedNodes)
    {
        // Inclusive pixel coordinates: zero shares one column, -1 is
        // adjacent, and -2 leaves an uncovered column between the inputs.
        var original = Bounds.Create(10, 10, 20, 20);
        var input = extendsRight
            ? Bounds.Create(20 - overlap, 14, 25, 16)
            : Bounds.Create(5, 14, 10 + overlap, 16);

        AssertNativeUnionMatchesPortable(original, input, expectedNodes);
    }

    [Theory]
    [InlineData(-100, 0, 10, 10, -50, 3, 32667, 7)]
    [InlineData(-32758, 0, -1, 10, -32768, 3, -32750, 7)]
    [InlineData(-32768, 0, -32750, 10, -32760, 3, -32740, 7)]
    public void HorizontalTHandlesMaximumRelativeWidthAndFarNegativeOrigins(
        int oldMinX, int oldMinY, int oldMaxX, int oldMaxY,
        int minX, int minY, int maxX, int maxY)
    {
        AssertNativeUnionMatchesPortable(
            Bounds.Create(oldMinX, oldMinY, oldMaxX, oldMaxY),
            Bounds.Create(minX, minY, maxX, maxY),
            expectedNodes: 3);
    }

    [Theory]
    [InlineData(0, 0, 10)]
    [InlineData(0, 0, 15)]
    [InlineData(0, 0, 19)]
    [InlineData(-30, -30, 15)]
    [InlineData(-32770, -32770, 15)]
    [InlineData(32740, 32740, 15)]
    public void TopTCoalescesIdenticalXIntervalsIntoTwoCanonicalNodes(
        int translateX,
        int translateY,
        int inputMaxY)
    {
        var original = Bounds.Create(10, 10, 20, 20).Translate(translateX, translateY);
        var input = Bounds.Create(14, 5, 16, inputMaxY).Translate(translateX, translateY);

        // The input's bottom edge does not split the old rectangle: both
        // sides of that edge have the same complete horizontal interval.
        AssertNativeUnionMatchesPortable(original, input, expectedNodes: 2);
    }

    [Theory]
    [InlineData(10, 10, 20, 20, 15, 5, 15, 15)]
    [InlineData(10, -32767, 20, -32757, 14, -32768, 16, -32762)]
    [InlineData(10, -10, 20, 0, 14, -32767, 16, -5)]
    [InlineData(-32768, 10, -1, 20, -16000, 5, -15000, 15)]
    public void TopTHandlesSingleColumnStemsAndSignedWordBoundaries(
        int oldMinX, int oldMinY, int oldMaxX, int oldMaxY,
        int minX, int minY, int maxX, int maxY)
    {
        AssertNativeUnionMatchesPortable(
            Bounds.Create(oldMinX, oldMinY, oldMaxX, oldMaxY),
            Bounds.Create(minX, minY, maxX, maxY),
            expectedNodes: 2);
    }

    [Theory]
    [InlineData(0, 0, 11)]
    [InlineData(0, 0, 15)]
    [InlineData(0, 0, 20)]
    [InlineData(-30, -30, 15)]
    [InlineData(-32770, -32770, 15)]
    [InlineData(32740, 32740, 15)]
    public void BottomTCoalescesIdenticalXIntervalsIntoTwoCanonicalNodes(
        int translateX,
        int translateY,
        int inputMinY)
    {
        var original = Bounds.Create(10, 10, 20, 20).Translate(translateX, translateY);
        var input = Bounds.Create(14, inputMinY, 16, 25).Translate(translateX, translateY);

        // The input's top edge lies inside the old full-width band. Only
        // its tail below the old bottom edge needs a new canonical band.
        AssertNativeUnionMatchesPortable(original, input, expectedNodes: 2);
    }

    [Theory]
    [InlineData(21, 0)]
    [InlineData(22, 0)]
    [InlineData(21, -30)]
    [InlineData(22, -30)]
    public void BottomTSeparatesVerticalAdjacencyFromAGap(int inputMinY, int translateY)
    {
        var original = Bounds.Create(10, 10, 20, 20).Translate(0, translateY);
        var input = Bounds.Create(14, inputMinY, 16, 25).Translate(0, translateY);

        // These inputs do not overlap the old pixels: row 21 is either
        // the start of the adjacent stem or the uncovered gap.
        AssertNativeUnionMatchesPortable(original, input, expectedNodes: 2);
    }

    [Theory]
    [InlineData(10, 10, 20, 20, 15, 15, 15, 25)]
    [InlineData(10, 32757, 20, 32766, 14, 32760, 16, 32767)]
    [InlineData(10, -32768, 20, -32758, 14, -32763, 16, -32753)]
    [InlineData(10, -100, 20, 10, 14, -50, 16, 32667)]
    [InlineData(10, -32768, 20, -32758, 14, -32763, 16, -1)]
    [InlineData(-32768, 10, -1, 20, -16000, 15, -15000, 25)]
    public void BottomTHandlesSingleColumnStemsAndSignedWordBoundaries(
        int oldMinX, int oldMinY, int oldMaxX, int oldMaxY,
        int minX, int minY, int maxX, int maxY)
    {
        AssertNativeUnionMatchesPortable(
            Bounds.Create(oldMinX, oldMinY, oldMaxX, oldMaxY),
            Bounds.Create(minX, minY, maxX, maxY),
            expectedNodes: 2);
    }

    [Theory]
    [InlineData(10, 10, 20, 20, 14, 5, 16, 20)]
    [InlineData(10, 10, 20, 20, 14, 10, 16, 25)]
    [InlineData(10, 10, 20, 10, 14, 5, 16, 10)]
    [InlineData(10, 10, 20, 10, 14, 10, 16, 15)]
    [InlineData(10, -32767, 20, -32760, 14, -32768, 16, -32760)]
    [InlineData(10, 32760, 20, 32766, 14, 32760, 16, 32767)]
    [InlineData(10, 32762, 20, 32767, 14, 32757, 16, 32767)]
    [InlineData(10, -32768, 20, -32763, 14, -32768, 16, -32758)]
    [InlineData(10, 32767, 20, 32767, 14, 32766, 16, 32767)]
    [InlineData(10, -32768, 20, -32768, 14, -32768, 16, -32767)]
    [InlineData(10, -10, 20, 0, 14, -32767, 16, 0)]
    [InlineData(10, -32768, 20, -32758, 14, -32768, 16, -1)]
    public void VerticalTOppositeYEdgeEqualityPreservesTwoCanonicalBands(
        int oldMinX, int oldMinY, int oldMaxX, int oldMaxY,
        int minX, int minY, int maxX, int maxY)
    {
        // The narrow stem may end exactly on the old bottom edge, or start
        // exactly on the old top edge. Neither equality creates a third band.
        AssertNativeUnionMatchesPortable(
            Bounds.Create(oldMinX, oldMinY, oldMaxX, oldMaxY),
            Bounds.Create(minX, minY, maxX, maxY),
            expectedNodes: 2);
    }

    [Theory]
    [InlineData(14, 5, 16, 15, 10, 10, 20, 20)]
    [InlineData(14, 15, 16, 25, 10, 10, 20, 20)]
    [InlineData(14, 5, 16, 20, 10, 10, 20, 20)]
    [InlineData(14, 10, 16, 25, 10, 10, 20, 20)]
    [InlineData(14, 5, 16, 10, 10, 10, 20, 10)]
    [InlineData(14, 10, 16, 15, 10, 10, 20, 10)]
    [InlineData(15, 5, 15, 15, 10, 10, 20, 20)]
    [InlineData(15, 15, 15, 25, 10, 10, 20, 20)]
    [InlineData(-16, -25, -14, -15, -20, -20, -10, -10)]
    [InlineData(-16, -15, -14, -5, -20, -20, -10, -10)]
    [InlineData(-32764, -32768, -32762, -32758, -32768, -32763, -32758, -32753)]
    [InlineData(32761, 32757, 32763, 32767, 32757, 32752, 32767, 32762)]
    [InlineData(14, -32767, 16, -5, 10, -10, 20, 0)]
    [InlineData(14, 5, 16, 32767, 10, 0, 20, 10)]
    [InlineData(-16000, 5, -15000, 15, -32768, 10, -1, 20)]
    [InlineData(-16000, 15, -15000, 25, -32768, 10, -1, 20)]
    [InlineData(14, 5, 16, 9, 10, 10, 20, 20)]
    [InlineData(14, 21, 16, 25, 10, 10, 20, 20)]
    [InlineData(14, 5, 16, 8, 10, 10, 20, 20)]
    [InlineData(14, 22, 16, 25, 10, 10, 20, 20)]
    public void ReversedVerticalTsMatchPortableCanonicalBandsInBothSourceOrders(
        int stemMinX, int stemMinY, int stemMaxX, int stemMaxY,
        int barMinX, int barMinY, int barMaxX, int barMaxY)
    {
        var stem = Bounds.Create(stemMinX, stemMinY, stemMaxX, stemMaxY);
        var bar = Bounds.Create(barMinX, barMinY, barMaxX, barMaxY);

        // The original narrow Region must acquire the bar's X origin and
        // width without changing which canonical bands the union contains.
        AssertNativeUnionMatchesPortable(stem, bar, expectedNodes: 2, poisonExecRegisters: true);
        AssertNativeUnionMatchesPortable(bar, stem, expectedNodes: 2, poisonExecRegisters: true);
    }

    [Theory]
    [InlineData(14, -32768, 16, -5, 10, -10, 20, 0)]
    [InlineData(14, 5, 16, 32767, 10, -1, 20, 10)]
    [InlineData(-16000, 5, -15000, 15, -32768, 10, 0, 20)]
    [InlineData(-16000, 15, -15000, 25, -32768, 10, 0, 20)]
    public void ReversedVerticalTUnrepresentableBoundsFallBackWithoutMutation(
        int stemMinX, int stemMinY, int stemMaxX, int stemMaxY,
        int barMinX, int barMinY, int barMaxX, int barMaxY)
    {
        var stem = Bounds.Create(stemMinX, stemMinY, stemMaxX, stemMaxY);
        var bar = Bounds.Create(barMinX, barMinY, barMaxX, barMaxY);
        var portable = new PortableRegion(stem);
        using var native = new NativeRegion(stem, poisonExecRegisters: true);

        Assert.False(portable.Union(bar));
        var result = native.Union(bar);

        Assert.True(result.UsedFallback);
        Assert.Equal(NativeRegion.CapturedFrame, result.Value);
        Assert.Empty(native.Allocations);
        Assert.Empty(native.Frees);
        native.AssertOriginalRegionUnchanged();
    }

    [Theory]
    [InlineData(false, 1, 0u)]
    [InlineData(false, 2, 0u)]
    [InlineData(false, 1, 0x00D0_5001u)]
    [InlineData(false, 2, 0x00D0_5001u)]
    [InlineData(false, 1, 0xFFFF_FFF8u)]
    [InlineData(false, 2, 0xFFFF_FFF8u)]
    [InlineData(true, 1, 0u)]
    [InlineData(true, 2, 0u)]
    [InlineData(true, 1, 0x00D0_5001u)]
    [InlineData(true, 2, 0x00D0_5001u)]
    [InlineData(true, 1, 0xFFFF_FFF8u)]
    [InlineData(true, 2, 0xFFFF_FFF8u)]
    public void ReversedVerticalTAllocationFailuresRetainOriginalRegionAndInputRectangle(
        bool bottomStem,
        int failingAllocation,
        uint failedAddress)
        => AssertAllocationFailureRetainsOriginalRegion(
            Bounds.Create(10, 10, 20, 20),
            failingAllocation,
            failedAddress,
            original: bottomStem ? Bounds.Create(14, 15, 16, 25) : Bounds.Create(14, 5, 16, 15),
            poisonExecRegisters: true);

    [Theory]
    [InlineData(5, 14, 15, 16, 10, 10, 20, 20, 3)]
    [InlineData(15, 14, 25, 16, 10, 10, 20, 20, 3)]
    [InlineData(5, 14, 10, 16, 10, 10, 20, 20, 3)]
    [InlineData(20, 14, 25, 16, 10, 10, 20, 20, 3)]
    [InlineData(5, 14, 9, 16, 10, 10, 20, 20, 3)]
    [InlineData(21, 14, 25, 16, 10, 10, 20, 20, 3)]
    [InlineData(5, 14, 8, 16, 10, 10, 20, 20, 2)]
    [InlineData(22, 14, 25, 16, 10, 10, 20, 20, 2)]
    [InlineData(5, 14, 20, 16, 10, 10, 20, 20, 3)]
    [InlineData(10, 14, 25, 16, 10, 10, 20, 20, 3)]
    [InlineData(5, 15, 15, 15, 10, 10, 20, 20, 3)]
    [InlineData(15, 15, 25, 15, 10, 10, 20, 20, 3)]
    [InlineData(5, 14, 10, 16, 10, 10, 10, 20, 3)]
    [InlineData(10, 14, 15, 16, 10, 10, 10, 20, 3)]
    [InlineData(-25, -16, -15, -14, -20, -20, -10, -10, 3)]
    [InlineData(-15, -16, -5, -14, -20, -20, -10, -10, 3)]
    [InlineData(-32768, -32764, -32758, -32762, -32763, -32768, -32753, -32758, 3)]
    [InlineData(32757, 32761, 32767, 32763, 32752, 32757, 32762, 32767, 3)]
    [InlineData(-32767, 14, -5, 16, -10, 10, 0, 20, 3)]
    [InlineData(5, 14, 32767, 16, 0, 10, 10, 20, 3)]
    [InlineData(5, -16000, 15, -15000, 10, -32768, 20, -1, 3)]
    [InlineData(15, -16000, 25, -15000, 10, -32768, 20, -1, 3)]
    [InlineData(32755, 14, 32767, 16, 32760, 10, 32767, 20, 3)]
    [InlineData(-32768, 14, -32755, 16, -32768, 10, -32760, 20, 3)]
    [InlineData(32760, 14, 32767, 16, 32767, 10, 32767, 20, 3)]
    [InlineData(-32768, 14, -32760, 16, -32768, 10, -32768, 20, 3)]
    public void ReversedHorizontalTsMatchPortableCanonicalBandsInBothSourceOrders(
        int stemMinX, int stemMinY, int stemMaxX, int stemMaxY,
        int barMinX, int barMinY, int barMaxX, int barMaxY,
        int expectedNodes)
    {
        var stem = Bounds.Create(stemMinX, stemMinY, stemMaxX, stemMaxY);
        var bar = Bounds.Create(barMinX, barMinY, barMaxX, barMaxY);

        // Adjacency merges the middle X interval just like overlap. A real
        // gap leaves the complete tall bar and the separate short stem.
        AssertNativeUnionMatchesPortable(stem, bar, expectedNodes, poisonExecRegisters: true);
        AssertNativeUnionMatchesPortable(bar, stem, expectedNodes, poisonExecRegisters: true);
    }

    [Theory]
    [InlineData(-32768, 14, -5, 16, -10, 10, 0, 20)]
    [InlineData(5, 14, 32767, 16, -1, 10, 10, 20)]
    [InlineData(5, -16000, 15, -15000, 10, -32768, 20, 0)]
    [InlineData(15, -16000, 25, -15000, 10, -32768, 20, 0)]
    public void ReversedHorizontalTUnrepresentableBoundsFallBackWithoutMutation(
        int stemMinX, int stemMinY, int stemMaxX, int stemMaxY,
        int barMinX, int barMinY, int barMaxX, int barMaxY)
    {
        var stem = Bounds.Create(stemMinX, stemMinY, stemMaxX, stemMaxY);
        var bar = Bounds.Create(barMinX, barMinY, barMaxX, barMaxY);
        var portable = new PortableRegion(stem);
        using var native = new NativeRegion(stem, poisonExecRegisters: true);

        Assert.False(portable.Union(bar));
        var result = native.Union(bar);

        Assert.True(result.UsedFallback);
        Assert.Equal(NativeRegion.CapturedFrame, result.Value);
        Assert.Empty(native.Allocations);
        Assert.Empty(native.Frees);
        native.AssertOriginalRegionUnchanged();
    }

    [Theory]
    [InlineData(false, false, 1, 0u)]
    [InlineData(false, false, 2, 0u)]
    [InlineData(false, false, 3, 0u)]
    [InlineData(false, false, 1, 0x00D0_8001u)]
    [InlineData(false, false, 2, 0x00D0_8001u)]
    [InlineData(false, false, 3, 0x00D0_8001u)]
    [InlineData(false, false, 1, 0xFFFF_FFF8u)]
    [InlineData(false, false, 2, 0xFFFF_FFF8u)]
    [InlineData(false, false, 3, 0xFFFF_FFF8u)]
    [InlineData(true, false, 1, 0u)]
    [InlineData(true, false, 2, 0u)]
    [InlineData(true, false, 3, 0u)]
    [InlineData(true, false, 1, 0x00D0_8001u)]
    [InlineData(true, false, 2, 0x00D0_8001u)]
    [InlineData(true, false, 3, 0x00D0_8001u)]
    [InlineData(true, false, 1, 0xFFFF_FFF8u)]
    [InlineData(true, false, 2, 0xFFFF_FFF8u)]
    [InlineData(true, false, 3, 0xFFFF_FFF8u)]
    [InlineData(false, true, 3, 0u)]
    [InlineData(false, true, 3, 0x00D0_8001u)]
    [InlineData(false, true, 3, 0xFFFF_FFF8u)]
    [InlineData(true, true, 3, 0u)]
    [InlineData(true, true, 3, 0x00D0_8001u)]
    [InlineData(true, true, 3, 0xFFFF_FFF8u)]
    public void ReversedHorizontalTAllocationFailuresRetainOriginalRegionAndInputRectangle(
        bool rightStem,
        bool adjacent,
        int failingAllocation,
        uint failedAddress)
        => AssertAllocationFailureRetainsOriginalRegion(
            Bounds.Create(10, 10, 20, 20),
            failingAllocation,
            failedAddress,
            original: rightStem
                ? Bounds.Create(adjacent ? 21 : 15, 14, 25, 16)
                : Bounds.Create(5, 14, adjacent ? 9 : 15, 16),
            poisonExecRegisters: true);

    [Theory]
    [InlineData(-100, 0, 10, 10, -50, 3, 32668, 7)]
    [InlineData(-32758, 0, 0, 10, -32768, 3, -32750, 7)]
    [InlineData(10, -10, 20, 0, 14, -32768, 16, -5)]
    [InlineData(10, -100, 20, 10, 14, -50, 16, 32668)]
    [InlineData(10, -32768, 20, -32758, 14, -32763, 16, 0)]
    [InlineData(-1, 10, 10, 20, 11, 10, 32767, 20)]
    [InlineData(-10, 10, 0, 20, -32768, 10, -11, 20)]
    [InlineData(10, -1, 20, 10, 10, 11, 20, 32767)]
    [InlineData(10, -10, 20, 0, 10, -32768, 20, -11)]
    [InlineData(10, -10, 20, 0, 14, -32768, 16, 0)]
    [InlineData(10, -32768, 20, -32758, 14, -32768, 16, 0)]
    public void UnrepresentableRelativeBoundsFallBackBeforeAllocationWithoutMutation(
        int oldMinX, int oldMinY, int oldMaxX, int oldMaxY,
        int minX, int minY, int maxX, int maxY)
    {
        var original = Bounds.Create(oldMinX, oldMinY, oldMaxX, oldMaxY);
        var input = Bounds.Create(minX, minY, maxX, maxY);
        var portable = new PortableRegion(original);
        using var native = new NativeRegion(original);

        Assert.False(portable.Union(input));
        var result = native.Union(input);

        Assert.True(result.UsedFallback);
        Assert.Equal(NativeRegion.CapturedFrame, result.Value);
        Assert.Empty(native.Allocations);
        Assert.Empty(native.Frees);
        native.AssertOriginalRegionUnchanged();
    }

    [Theory]
    [InlineData(1, 0u)]
    [InlineData(2, 0u)]
    [InlineData(1, 0x00D0_5001u)]
    [InlineData(2, 0x00D0_5001u)]
    [InlineData(1, 0xFFFF_FFF8u)]
    [InlineData(2, 0xFFFF_FFF8u)]
    public void TopTAllocationFailuresRetainTheOriginalRegionAndReleaseProvisionalNodes(
        int failingAllocation,
        uint failedAddress)
        => AssertAllocationFailureRetainsOriginalRegion(
            Bounds.Create(14, 5, 16, 15), failingAllocation, failedAddress);

    [Theory]
    [InlineData(1, 0u)]
    [InlineData(2, 0u)]
    [InlineData(1, 0x00D0_5001u)]
    [InlineData(2, 0x00D0_5001u)]
    [InlineData(1, 0xFFFF_FFF8u)]
    [InlineData(2, 0xFFFF_FFF8u)]
    public void BottomTAllocationFailuresRetainTheOriginalRegionAndReleaseProvisionalNodes(
        int failingAllocation,
        uint failedAddress)
        => AssertAllocationFailureRetainsOriginalRegion(
            Bounds.Create(14, 15, 16, 25), failingAllocation, failedAddress);

    [Theory]
    [InlineData(1, 0u)]
    [InlineData(2, 0u)]
    [InlineData(1, 0x00D0_5001u)]
    [InlineData(2, 0x00D0_5001u)]
    [InlineData(1, 0xFFFF_FFF8u)]
    [InlineData(2, 0xFFFF_FFF8u)]
    public void TopTOppositeYEdgeEqualityAllocationFailuresRetainTheOriginalRegion(
        int failingAllocation,
        uint failedAddress)
        => AssertAllocationFailureRetainsOriginalRegion(
            Bounds.Create(14, 5, 16, 20), failingAllocation, failedAddress);

    [Theory]
    [InlineData(1, 0u)]
    [InlineData(2, 0u)]
    [InlineData(1, 0x00D0_5001u)]
    [InlineData(2, 0x00D0_5001u)]
    [InlineData(1, 0xFFFF_FFF8u)]
    [InlineData(2, 0xFFFF_FFF8u)]
    public void BottomTOppositeYEdgeEqualityAllocationFailuresRetainTheOriginalRegion(
        int failingAllocation,
        uint failedAddress)
        => AssertAllocationFailureRetainsOriginalRegion(
            Bounds.Create(14, 10, 16, 25), failingAllocation, failedAddress);

    private static void AssertAllocationFailureRetainsOriginalRegion(
        Bounds input,
        int failingAllocation,
        uint failedAddress,
        Bounds? original = null,
        bool poisonExecRegisters = false)
    {
        using var native = new NativeRegion(
            original ?? Bounds.Create(10, 10, 20, 20), poisonExecRegisters);
        for (var index = 1; index < failingAllocation; index++)
            native.AllocationResults.Enqueue(0x00D0_4000u + (uint)(index - 1) * 0x1000u);
        native.AllocationResults.Enqueue(failedAddress);

        var result = native.Union(input);

        Assert.False(result.UsedFallback);
        Assert.Equal(0u, result.Value);
        Assert.Equal(failingAllocation, native.Allocations.Count);
        Assert.All(native.Allocations, allocation =>
        {
            Assert.Equal((uint)GraphicsLayouts.RegionRectangleSize, allocation.Size);
            Assert.Equal(1u, allocation.Flags);
        });
        Assert.Equal(
            native.Allocations.Where(allocation => allocation.Address != 0)
                .Reverse().Select(allocation => (allocation.Address, allocation.Size, 1u)),
            native.Frees);
        native.AssertOriginalRegionUnchanged();

        // No provisional node is initialized before the entire allocation
        // transaction succeeds. This also catches an early publication write.
        foreach (var allocation in native.Allocations.Where(allocation =>
                     allocation.Address != 0 && (allocation.Address & 1) == 0 &&
                     allocation.Address <= 0xFFFF_FFF0u))
        {
            Assert.All(ReadBytes(native.Bus, allocation.Address, (int)allocation.Size),
                value => Assert.Equal((byte)0xA5, value));
        }
    }

    private static void AssertNativeUnionMatchesPortable(
        Bounds original,
        Bounds input,
        int expectedNodes,
        bool reuseOriginalNode = false,
        bool poisonExecRegisters = false)
    {
        var portable = new PortableRegion(original);
        using var native = new NativeRegion(original, poisonExecRegisters);

        Assert.True(portable.Union(input));
        var expected = ReadRegion(portable.Bus, portable.Address);
        Assert.Equal(expectedNodes, expected.Nodes.Length);

        // In-place coalescing must not depend on an Exec allocator being
        // available after the original Region and node have been created.
        if (reuseOriginalNode)
            native.Bus.WriteLong(4, 0);
        var result = native.Union(input);
        Assert.False(result.UsedFallback);
        Assert.Equal(1u, result.Value);
        var actual = ReadRegion(native.Bus, native.Address);
        Assert.Equal(expected.Header, actual.Header);
        Assert.Equal(expected.Nodes, actual.Nodes);
        if (reuseOriginalNode)
        {
            Assert.Equal(1, expectedNodes);
            Assert.Equal(new[] { native.OriginalNode }, actual.Addresses);
            Assert.Empty(native.Allocations);
            Assert.Empty(native.Frees);
        }
        else
        {
            Assert.Equal(expectedNodes, native.Allocations.Count);
            Assert.Equal(native.Allocations.Select(allocation => allocation.Address), actual.Addresses);
            Assert.All(native.Allocations, allocation =>
            {
                Assert.Equal((uint)GraphicsLayouts.RegionRectangleSize, allocation.Size);
                Assert.Equal(1u, allocation.Flags);
            });
            Assert.Equal(
                new[] { (native.OriginalNode, (uint)GraphicsLayouts.RegionRectangleSize, 1u) },
                native.Frees);
        }
        native.AssertOwnershipPrefixUnchanged();
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

    private static RegionSnapshot ReadRegionWithAbsoluteNodeBounds(AmigaBus bus, uint region)
    {
        var snapshot = ReadRegion(bus, region);
        return snapshot with
        {
            Nodes = snapshot.Nodes
                .Select(bounds => bounds.Translate(snapshot.Header.MinX, snapshot.Header.MinY))
                .ToArray()
        };
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

        internal Bounds Translate(int x, int y)
            => Create(MinX + x, MinY + y, MaxX + x, MaxY + y);
    }

    private sealed record RegionSnapshot(Bounds Header, Bounds[] Nodes, uint[] Addresses);
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
        private uint _nextAllocation = 0x00D0_0000;
        private bool _poisonExecRegisters;
        private byte[] _inputSnapshot = Array.Empty<byte>();

        internal NativeRegion(Bounds original, bool poisonExecRegisters = false)
        {
            Bus.MapWritableMemory(CodeAddress, Image.Value.Code);
            Bus.MapWritableMemory(4, new byte[4]);
            Bus.WriteLong(4, ExecBase);
            Bus.MapWritableMemory(Rectangle, new byte[GraphicsLayouts.RectangleSize]);
            Bus.MapWritableMemory(Stack, new byte[0x400]);
            Bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                if (_poisonExecRegisters)
                {
                    AssertOriginalRegionUnchanged();
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
                if (_poisonExecRegisters)
                    PoisonExecVolatileRegisters(state);
            });
            Bus.RegisterHostGateway(ExecBase - 210, state =>
            {
                // Exec FreeMem consumes A1/D0, unlike the emitter's private
                // A0/D0 staging. Use the public ABI so a missing adapter
                // cannot pass by sharing the implementation's convention.
                Frees.Add((state.A[1], state.D[0], state.D[1]));
                if (_poisonExecRegisters)
                {
                    AssertInputRectangleUnchanged();
                    PoisonExecVolatileRegisters(state);
                }
            });
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, Bus);

            var created = Execute(GraphicsLvo.NewRegion);
            Assert.False(created.UsedFallback);
            Assert.NotEqual(0u, created.Value);
            Address = created.Value;
            var seeded = Union(original);
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
            _poisonExecRegisters = poisonExecRegisters;
        }

        internal AmigaBus Bus { get; } = new();
        internal uint Address { get; }
        internal uint OriginalNode { get; }
        internal List<Allocation> Allocations { get; } = new();
        internal List<(uint Address, uint Size, uint Flags)> Frees { get; } = new();
        internal Queue<uint> AllocationResults { get; } = new();

        internal CallResult Union(Bounds input)
        {
            WriteBounds(Bus, Rectangle, input);
            _inputSnapshot = ReadBytes(Bus, Rectangle, GraphicsLayouts.RectangleSize);
            var result = Execute(GraphicsLvo.OrRectRegion);
            AssertInputRectangleUnchanged();
            return result;
        }

        private void AssertInputRectangleUnchanged()
            => Assert.Equal(_inputSnapshot, ReadBytes(Bus, Rectangle, GraphicsLayouts.RectangleSize));

        private static void PoisonExecVolatileRegisters(M68kCpuState state)
        {
            // The targeted replacement tests enable this only after seed
            // setup, leaving D0's allocation result and saved registers live.
            state.D[1] = 0xD1D1_D1D1;
            state.A[0] = 0xA0A0_A0A1;
            state.A[1] = 0xA1A1_A1A1;
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

        private CallResult Execute(GraphicsLvo vector)
        {
            Bus.WriteLong(StackPointer, ReturnAddress);
            _cpu.Reset(CodeAddress + (uint)Image.Value.Entries[vector], StackPointer);
            _cpu.State.D[0] = CapturedFrame;
            _cpu.State.A[0] = Address;
            _cpu.State.A[1] = Rectangle;
            var fallback = false;
            for (var index = 0; index < 4096; index++)
            {
                // PatchBranch duplicates the fallback RTS locally when the
                // exported entry is outside Bcc.W reach. The raw Boolean
                // body returns its captured frame only on that decline path;
                // native completion returns TRUE or FALSE instead.
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

    private sealed class PortableRegionRegionFixture
    {
        private const uint Rectangle = 0x00DC_0000;
        private readonly GraphicsLibraryCore _core;

        internal PortableRegionRegionFixture()
        {
            Bus.MapWritableMemory(Rectangle, new byte[GraphicsLayouts.RectangleSize]);
            _core = new GraphicsLibraryCore(
                new CopperStartGraphicsMemoryAdapter(new HostGuestMemory(Bus)),
                new PortableAllocator(Bus),
                new CopperStartGraphicsUnboundBlitter(),
                new CopperStartGraphicsUnboundDisplay());
        }

        internal AmigaBus Bus { get; } = new();
        internal GraphicsLibraryCore Core => _core;

        internal uint CreateRegion(params Bounds[] rectangles)
        {
            var region = _core.NewRegion();
            Assert.NotEqual(0u, region);
            foreach (var rectangle in rectangles)
            {
                WriteBounds(Bus, Rectangle, rectangle);
                Assert.True(_core.OrRectRegion(region, Rectangle));
            }

            return region;
        }

        internal void SetEnvelope(uint region, Bounds envelope)
            => WriteBounds(Bus, region, envelope);

        internal void SetEnvelopeAndNodeBounds(
            uint region,
            Bounds envelope,
            params Bounds[] nodeBounds)
        {
            SetEnvelope(region, envelope);
            var node = Bus.ReadLong(region + (uint)GraphicsLayouts.RegionRectangle);
            for (var index = 0; index < nodeBounds.Length; index++)
            {
                Assert.NotEqual(0u, node);
                WriteBounds(Bus, node + (uint)GraphicsLayouts.RegionRectangleBounds, nodeBounds[index]);
                node = Bus.ReadLong(node + (uint)GraphicsLayouts.RegionRectangleNext);
            }
            Assert.Equal(0u, node);
        }
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
            Assert.True(Union(original));
        }

        internal AmigaBus Bus { get; } = new();
        internal uint Address { get; }

        internal bool Union(Bounds input)
        {
            WriteBounds(Bus, Rectangle, input);
            return _core.OrRectRegion(Address, Rectangle);
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
