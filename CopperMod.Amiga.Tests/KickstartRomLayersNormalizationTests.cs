using Amiga;

namespace CopperMod.Amiga.Tests;

public sealed partial class KickstartRomLayersDifferentialTests
{
    [Fact]
    public void NormalizationPreservesEveryDamageRectangleAndPredecessor()
    {
        const string first =
            "damage={bounds=0,0,7,7:count=2:links=False:" +
            "rects=[0,0,1,1:prev=other;2,2,3,3:prev=P]}";
        const string differentTail =
            "damage={bounds=0,0,7,7:count=2:links=False:" +
            "rects=[0,0,1,1:prev=other;4,4,5,5:prev=P]}";
        const string differentPredecessor =
            "damage={bounds=0,0,7,7:count=2:links=False:" +
            "rects=[0,0,1,1:prev=self;2,2,3,3:prev=P]}";

        Assert.Equal(first, NormalizeSemanticObservation(first));
        Assert.Equal(differentTail, NormalizeSemanticObservation(differentTail));
        Assert.Equal(differentPredecessor,
            NormalizeSemanticObservation(differentPredecessor));
        Assert.NotEqual(NormalizeSemanticObservation(first),
            NormalizeSemanticObservation(differentTail));
        Assert.NotEqual(NormalizeSemanticObservation(first),
            NormalizeSemanticObservation(differentPredecessor));
    }

    [Theory]
    [InlineData("N/null")]
    [InlineData("P/i0")]
    [InlineData("self/i1")]
    [InlineData("other/external-unmapped")]
    [InlineData("other/external(8,0,15,7;next=i0;prev=external)")]
    public void NormalizationIgnoresOnlyClipRectReservedPredecessor(string previous)
    {
        const string damage =
            "damage={bounds=0,0,7,7:count=2:links=False:" +
            "rects=[0,0,1,1:prev=other;2,2,3,3:prev=P]}:";
        const string expected =
            "cr=[count=1:next-valid=True:0,0,7,7:N:-:null]";
        var row = damage + "cr=[count=1:links=False:next-valid=True:" +
            $"0,0,7,7:N:-:prev={previous}:null]";

        Assert.Equal(damage + expected, NormalizeSemanticObservation(row));
    }

    [Fact]
    public void NormalizationKeepsPublicNextValidityAndPartitionBounds()
    {
        var context = CreateCopperStartOracle();
        try
        {
            var first = APTR.FromPointer(context.Allocate(ClipRect.Size));
            var second = APTR.FromPointer(context.Allocate(ClipRect.Size));
            try
            {
                var memory = new LayersTestGuestMemory(context.Bus);
                LayersClipRectCodec.WriteNext(ref memory, first, second);
                LayersClipRectCodec.WriteReservedLink(ref memory, second, first);
                LayersClipRectCodec.WriteBounds(ref memory, first,
                    LayersRectangleCodec.Create(0, 0, 3, 7));
                LayersClipRectCodec.WriteBounds(ref memory, second,
                    LayersRectangleCodec.Create(4, 0, 7, 7));
                var valid = DescribeClipRectChain(context, first.Raw, 0, 0);
                Assert.Contains(":next-valid=True:", valid);

                LayersClipRectCodec.WriteReservedLink(ref memory, second, APTR.Null);
                var reservedDifference = DescribeClipRectChain(
                    context, first.Raw, 0, 0);
                Assert.NotEqual(valid, reservedDifference);
                Assert.Equal(NormalizeSemanticObservation(valid),
                    NormalizeSemanticObservation(reservedDifference));

                LayersClipRectCodec.WriteNext(ref memory, second, first);
                var cyclic = DescribeClipRectChain(context, first.Raw, 0, 0);
                Assert.Contains(":next-valid=False:", cyclic);
                Assert.NotEqual(NormalizeSemanticObservation(reservedDifference),
                    NormalizeSemanticObservation(cyclic));

                LayersClipRectCodec.WriteNext(ref memory, second, APTR.Null);
                LayersClipRectCodec.WriteBounds(ref memory, second,
                    LayersRectangleCodec.Create(5, 0, 7, 7));
                var partitionDifference = DescribeClipRectChain(
                    context, first.Raw, 0, 0);
                Assert.Contains(":next-valid=True:", partitionDifference);
                Assert.NotEqual(NormalizeSemanticObservation(valid),
                    NormalizeSemanticObservation(partitionDifference));
            }
            finally
            {
                context.Free(second.Raw, ClipRect.Size);
                context.Free(first.Raw, ClipRect.Size);
            }
        }
        finally
        {
            context.Machine.Dispose();
        }
    }

    [Fact]
    public void RegionObservationMatchesExactUnionAcrossDecompositionAndOrder()
    {
        WithRegionObservation(LayersRectangleCodec.Create(0, 0, 17, 8),
            [LayersRectangleCodec.Create(16, 0, 17, 8),
             LayersRectangleCodec.Create(0, 8, 15, 8),
             LayersRectangleCodec.Create(0, 0, 15, 7)],
            (context, region, nodes) =>
            {
                var nativeShape = DescribeRegion(context, region.Raw);
                var nativeRaw = context.Diagnostics[^1];
                Assert.Equal("{bounds=0,0,17,8:links=True:next-valid=True:" +
                    "coverage=[y=0,8:x=0,17]}", nativeShape);
                var memory = new LayersTestGuestMemory(context.Bus);
                LayersRegionRectangleCodec.WriteBounds(ref memory, nodes[0],
                    LayersRectangleCodec.Create(16, 0, 17, 7));
                LayersRegionRectangleCodec.WriteBounds(ref memory, nodes[1],
                    LayersRectangleCodec.Create(0, 8, 17, 8));
                Assert.Equal(nativeShape, DescribeRegion(context, region.Raw));
                Assert.NotEqual(nativeRaw, context.Diagnostics[^1]);
                Assert.Contains("rects=[16,0,17,8:prev=other;0,8,15,8:prev=P;",
                    nativeRaw);
                Assert.Contains("rects=[16,0,17,7:prev=other;0,8,17,8:prev=P;",
                    context.Diagnostics[^1]);

                // Reorder the public chain without changing geometry or its links.
                LayersRegionCodec.WriteFirst(ref memory, region, nodes[2]);
                for (var index = nodes.Length - 1; index >= 0; index--)
                {
                    LayersRegionRectangleCodec.WritePrevious(ref memory, nodes[index],
                        index == nodes.Length - 1 ? LayersRegionCodec.HeadAnchor(region) : nodes[index + 1]);
                    LayersRegionRectangleCodec.WriteNext(ref memory, nodes[index],
                        index == 0 ? APTR.Null : nodes[index - 1]);
                }
                Assert.Equal(nativeShape, DescribeRegion(context, region.Raw));

                // Node count itself is not geometry: a single rectangle is equivalent.
                LayersRegionRectangleCodec.WriteBounds(ref memory, nodes[2],
                    LayersRectangleCodec.Create(0, 0, 17, 8));
                LayersRegionRectangleCodec.WriteNext(ref memory, nodes[2], APTR.Null);
                Assert.Equal(nativeShape, DescribeRegion(context, region.Raw));
                Assert.Contains(":count=1:", context.Diagnostics[^1]);
            });
    }

    [Fact]
    public void RegionObservationPreservesHolesEqualAreaShiftsAndBounds()
    {
        WithRegionObservation(LayersRectangleCodec.Create(-10, 20, 7, 28),
            [LayersRectangleCodec.Create(0, 0, 7, 8),
             LayersRectangleCodec.Create(10, 0, 17, 8)],
            (context, region, nodes) =>
            {
                var original = DescribeRegion(context, region.Raw);
                Assert.Contains("coverage=[y=20,28:x=-10,-3|0,7]", original);
                var memory = new LayersTestGuestMemory(context.Bus);
                LayersRegionRectangleCodec.WriteBounds(ref memory, nodes[0],
                    LayersRectangleCodec.Create(0, 0, 6, 8));
                Assert.NotEqual(original, DescribeRegion(context, region.Raw));
                LayersRegionRectangleCodec.WriteBounds(ref memory, nodes[0],
                    LayersRectangleCodec.Create(1, 0, 8, 8));
                var equalAreaShift = DescribeRegion(context, region.Raw);
                Assert.Contains("coverage=[y=20,28:x=-9,-2|0,7]", equalAreaShift);
                Assert.NotEqual(original, equalAreaShift);

                LayersRegionRectangleCodec.WriteBounds(ref memory, nodes[0],
                    LayersRectangleCodec.Create(0, 0, 7, 8));
                LayersRegionCodec.WriteBounds(ref memory, region,
                    LayersRectangleCodec.Create(-10, 20, 8, 28));
                var boundsOnly = DescribeRegion(context, region.Raw);
                Assert.Contains("coverage=[y=20,28:x=-10,-3|0,7]", boundsOnly);
                Assert.NotEqual(original, boundsOnly);
            });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegionObservationRejectsOverlapEvenWhenUnionIsUnchanged(bool duplicate)
    {
        WithRegionObservation(LayersRectangleCodec.Create(0, 0, 17, 8),
            [LayersRectangleCodec.Create(0, 0, 17, 8),
             duplicate ? LayersRectangleCodec.Create(0, 0, 17, 8) :
                LayersRectangleCodec.Create(7, 2, 10, 5)],
            (context, region, _) =>
            {
                var failure = Assert.ThrowsAny<Xunit.Sdk.XunitException>(
                    () => DescribeRegion(context, region.Raw));
                Assert.Contains("must not overlap", failure.Message);
                Assert.Contains(":count=2:", context.Diagnostics[^1]);
            });
    }

    [Fact]
    public void RegionObservationRejectsCyclesBeforeComparingImplementations()
    {
        WithRegionObservation(LayersRectangleCodec.Create(0, 0, 17, 8),
            [LayersRectangleCodec.Create(0, 0, 7, 8),
             LayersRectangleCodec.Create(8, 0, 17, 8)],
            (context, region, nodes) =>
            {
                var memory = new LayersTestGuestMemory(context.Bus);
                LayersRegionRectangleCodec.WriteNext(ref memory, nodes[1], nodes[0]);
                // Identical invalid observations must fail independently, not compare equal.
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    var failure = Assert.ThrowsAny<Xunit.Sdk.XunitException>(
                        () => DescribeRegion(context, region.Raw));
                    Assert.Contains("Region Next chain must terminate", failure.Message);
                }
            });
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public void RegionObservationRejectsBrokenPublicPredecessors(int index, bool self)
    {
        WithRegionObservation(LayersRectangleCodec.Create(0, 0, 17, 8),
            [LayersRectangleCodec.Create(0, 0, 7, 8),
             LayersRectangleCodec.Create(8, 0, 17, 8)],
            (context, region, nodes) =>
            {
                var memory = new LayersTestGuestMemory(context.Bus);
                LayersRegionRectangleCodec.WritePrevious(ref memory, nodes[index],
                    self ? nodes[index] : APTR.Null);
                var failure = Assert.ThrowsAny<Xunit.Sdk.XunitException>(
                    () => DescribeRegion(context, region.Raw));
                Assert.Contains("Region Previous must reference the head anchor", failure.Message);
            });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegionObservationRejectsUnmappedOrUnalignedNext(bool unaligned)
    {
        WithRegionObservation(LayersRectangleCodec.Create(0, 0, 17, 8),
            [LayersRectangleCodec.Create(0, 0, 17, 8)],
            (context, region, nodes) =>
            {
                var memory = new LayersTestGuestMemory(context.Bus);
                LayersRegionRectangleCodec.WriteNext(ref memory, nodes[0],
                    APTR.FromPointer(unaligned ? nodes[0].Raw + 1 : 0xFFFFFFFE));
                var failure = Assert.ThrowsAny<Xunit.Sdk.XunitException>(
                    () => DescribeRegion(context, region.Raw));
                Assert.Contains("Region Next chain must terminate", failure.Message);
            });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegionObservationRejectsInvalidRectangleBounds(bool inverted)
    {
        WithRegionObservation(LayersRectangleCodec.Create(0, 0, 17, 8),
            [inverted ? LayersRectangleCodec.Create(4, 0, 3, 8) :
                LayersRectangleCodec.Create(0, 0, 18, 8)],
            (context, region, _) =>
            {
                var failure = Assert.ThrowsAny<Xunit.Sdk.XunitException>(
                    () => DescribeRegion(context, region.Raw));
                Assert.Contains("contained in Region.Bounds", failure.Message);
            });
    }

    [Fact]
    public void RegionObservationPreservesEmptyBoundsAndNullDistinction()
    {
        WithRegionObservation(LayersRectangleCodec.Create(-3, 4, 7, 8), [],
            (context, region, _) =>
            {
                var empty = DescribeRegion(context, region.Raw);
                Assert.Equal("{bounds=-3,4,7,8:links=True:next-valid=True:coverage=[]}", empty);
                Assert.NotEqual(DescribeRegion(context, 0), empty);
                var memory = new LayersTestGuestMemory(context.Bus);
                LayersRegionCodec.WriteBounds(ref memory, region,
                    LayersRectangleCodec.Create(0, 0, 0, 0));
                Assert.NotEqual(empty, DescribeRegion(context, region.Raw));
            });
    }

    private static void WithRegionObservation(
        Rectangle bounds,
        Rectangle[] rectangles,
        Action<OracleContext, APTR, APTR[]> assertion)
    {
        var context = CreateCopperStartOracle();
        var region = APTR.Null;
        var nodes = new List<APTR>();
        try
        {
            region = APTR.FromPointer(context.Allocate(Region.Size));
            foreach (var _ in rectangles)
                nodes.Add(APTR.FromPointer(context.Allocate(RegionRectangle.Size)));
            var memory = new LayersTestGuestMemory(context.Bus);
            LayersRegionCodec.WriteBounds(ref memory, region, bounds);
            LayersRegionCodec.WriteFirst(ref memory, region,
                nodes.Count == 0 ? APTR.Null : nodes[0]);
            for (var index = 0; index < nodes.Count; index++)
            {
                LayersRegionRectangleCodec.WriteBounds(ref memory, nodes[index], rectangles[index]);
                LayersRegionRectangleCodec.WritePrevious(ref memory, nodes[index],
                    index == 0 ? LayersRegionCodec.HeadAnchor(region) : nodes[index - 1]);
                LayersRegionRectangleCodec.WriteNext(ref memory, nodes[index],
                    index + 1 == nodes.Count ? APTR.Null : nodes[index + 1]);
            }
            assertion(context, region, nodes.ToArray());
        }
        finally
        {
            for (var index = nodes.Count - 1; index >= 0; index--)
                context.Free(nodes[index].Raw, RegionRectangle.Size);
            if (region.IsNotNull)
                context.Free(region.Raw, Region.Size);
            context.Machine.Dispose();
        }
    }
}
