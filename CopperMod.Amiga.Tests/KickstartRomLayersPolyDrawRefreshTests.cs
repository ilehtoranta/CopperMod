using Amiga;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed partial class KickstartRomLayersDifferentialTests
{
    [Fact]
    public void CopperStartPolyDrawDuringDamageRefreshPreservesOwnership()
    {
        var context = CreateCopperStartOracle();
        try { _ = TracePolyDrawDuringDamageRefresh(context); }
        finally { context.Machine.Dispose(); }
    }

    [Fact]
    public void PolyDrawDuringDamageRefreshMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native PolyDraw refresh NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            var vector = unchecked((uint)((int)native.GraphicsBase + (int)GraphicsLvo.PolyDraw));
            Assert.False(native.Bus.HasHostGateway(vector));
            Assert.Equal((ushort)0x4EF9, native.Bus.ReadWord(vector));
            Assert.InRange(native.Bus.ReadLong(vector + 2), 0x00F8_0000u, 0x00FF_FFFFu);
            var memory = new LayersTestGuestMemory(native.Bus);
            _output.WriteLine("G226 configured V40.63: A500 PAL OCS, 512 KiB CHIP, " +
                "AccurateM68000/live Agnus; native PolyDraw uninstrumented. " +
                $"layers={ReadOracleLibraryVersion(native.Bus, native.LayersBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref memory, APTR.FromPointer(native.LayersBase))}; " +
                $"graphics={ReadOracleLibraryVersion(native.Bus, native.GraphicsBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref memory, APTR.FromPointer(native.GraphicsBase))}");
            expected = TracePolyDrawDuringDamageRefresh(native);
        }
        finally { native.Machine.Dispose(); }

        var copperStart = CreateCopperStartOracle();
        try { Assert.Equal(expected, TracePolyDrawDuringDamageRefresh(copperStart)); }
        finally { copperStart.Machine.Dispose(); }
    }

    private string[] TracePolyDrawDuringDamageRefresh(OracleContext context)
    {
        const int width = 32;
        const int height = 16;
        const int moveX = 2;
        const int moveY = 4;
        const ushort linePattern = 0xA640;
        const byte seed = 0xE6;
        var trace = new List<string>();
        var memory = new LayersTestGuestMemory(context.Bus);
        var info = context.NewLayerInfo();
        var image = context.CreatePlanarBitMap(width, height, 1, 0,
            planeMemoryFlags: Exec.MemoryFlags.Chip);
        Assert.Equal(image.Planes[0], context.Bus.MaskChipDmaAddress(image.Planes[0]));
        var layer = context.CreateLayer(LayersLvo.CreateUpfrontLayer, info,
            image.Address, 0, LayerCreationFlags.Simple, 0, 0, width - 1, height - 1);
        var cover = context.CreateLayer(LayersLvo.CreateUpfrontLayer, info,
            image.Address, 0, LayerCreationFlags.Simple, 4, 3, 11, 10);
        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = cover).D[0]);
        context.WaitForBlitterIdle();
        var damage = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList);
        Assert.Contains("coverage=[y=3,10:x=4,11]", DescribeRegion(context, damage));

        var region = context.CreateSingleRectangleRegion(7, 0, 13, 7);
        var regionNode = ReadOracleRegionFirst(context.Bus, region);
        var callerRegionBytes = Bytes(region, checked((int)Region.Size));
        var callerNodeBytes = Bytes(regionNode, checked((int)RegionRectangle.Size));
        Assert.Equal(0u, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state => { state.A[0] = layer; state.A[1] = region; }).D[0]);
        var pendingDamage = DescribeRegion(context, damage);
        var damageBytes = Bytes(damage, checked((int)Region.Size));
        var damageNode = ReadOracleRegionFirst(context.Bus, damage);
        var damageNodeBytes = Bytes(damageNode, checked((int)RegionRectangle.Size));
        var stableCoverage = VisibleCoverage();
        var activeCoverage = Enumerable.Range(3, 5)
            .SelectMany(y => Enumerable.Range(7, 5).Select(x => y * width + x)).ToArray();
        var rastPort = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.RastPort);
        var rp = APTR.FromPointer(rastPort);

        SetPenOrMode(GraphicsLvo.SetAPen, 1);
        SetPenOrMode(GraphicsLvo.SetBPen, 0);
        SetPenOrMode(GraphicsLvo.SetDrMd, 3); // COMPLEMENT|JAM2 exposes the shared zero-pattern vertex.
        SetPenOrMode(GraphicsLvo.SetWriteMask, 0xFF);
        LayersRastPortCodec.WriteLinePattern(ref memory, rp, linePattern);
        var nonLineFlags = LayersRastPortCodec.ReadFlags(ref memory, rp) & ~RastPortFlags.FirstDot;
        LayersRastPortCodec.WriteFlags(ref memory, rp, nonLineFlags | RastPortFlags.FirstDot);
        LayersRastPortCodec.WriteLinePatternCount(ref memory, rp, 15);
        var expectedPixels = Enumerable.Repeat(seed, image.BytesPerRow * height).ToArray();
        for (var offset = 0; offset < expectedPixels.Length; offset++)
            context.Bus.WriteByte(image.Planes[0] + checked((uint)offset), seed, 0);
        context.InvokeGraphics((int)GraphicsLvo.Move, state =>
        {
            state.A[1] = rastPort;
            state.D[0] = moveX;
            state.D[1] = moveY;
        });

        var points = context.Allocate(8);
        WritePoint(0, 9, 4);
        WritePoint(1, 10, 4);
        var pointBytes = Bytes(points, 8);
        AssertLineState(2, 4, 15, firstDot: true);
        Capture("before-update");

        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.BeginUpdate,
            state => state.A[0] = layer).D[0]);
        Assert.Equal(activeCoverage, VisibleCoverage());
        Assert.Equal(pendingDamage, DescribeRegion(context, damage));
        Assert.True((ReadOracleLayerFlags(context.Bus, layer) & LayerFlags.Updating) != 0);
        Capture("begin");
        var activeHead = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRect);

        // Independent oracle: the first logical line (2,4)->(9,4) admits
        // only x=7..9; the next connected line (9,4)->(10,4) is wholly inside
        // one ClipRect. COMPLEMENT toggles each selected line-pattern sample.
        Toggle(7, 4);
        Toggle(8, 4);
        Toggle(9, 4);
        Toggle(9, 4);
        Toggle(10, 4);
        context.InvokeGraphics((int)GraphicsLvo.PolyDraw, state =>
        {
            state.A[0] = points;
            state.A[1] = rastPort;
            state.D[0] = 2;
        });
        context.WaitForBlitterIdle();
        Assert.False(context.Bus.Blitter.Busy);
        // This independently prepared plane includes the connected shared
        // vertex twice; exact bytes are checked before refresh is retired.
        Assert.Equal(expectedPixels, Bytes(image.Planes[0], expectedPixels.Length));
        AssertLineState(10, 4, 7, firstDot: false);
        Assert.Equal(pointBytes, Bytes(points, pointBytes.Length));
        Assert.Equal(activeHead,
            ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRect));
        Assert.Equal(damage, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList));
        Assert.Equal(pendingDamage, DescribeRegion(context, damage));
        Assert.Equal(damageBytes, Bytes(damage, damageBytes.Length));
        Assert.Equal(damageNodeBytes, Bytes(damageNode, damageNodeBytes.Length));
        Assert.Equal(activeCoverage, VisibleCoverage());
        Capture("poly-draw");

        context.InvokeLayers(LayersLvo.EndUpdate, state =>
        {
            state.A[0] = layer;
            state.D[0] = 1;
        });
        Assert.Equal(stableCoverage, VisibleCoverage());
        Assert.Equal(damage, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList));
        Assert.Equal(new byte[checked((int)Region.Size)], Bytes(damage, checked((int)Region.Size)));
        Assert.True((ReadOracleLayerFlags(context.Bus, layer) & LayerFlags.Updating) == 0);
        Assert.True((ReadOracleLayerFlags(context.Bus, layer) & LayerFlags.Refresh) != 0);
        AssertLineState(10, 4, 7, firstDot: false);
        Capture("complete-end");

        Assert.Equal(region, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state => { state.A[0] = layer; state.A[1] = 0; }).D[0]);
        Assert.Equal(callerRegionBytes, Bytes(region, callerRegionBytes.Length));
        Assert.Equal(callerNodeBytes, Bytes(regionNode, callerNodeBytes.Length));
        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = layer).D[0]);
        context.DisposeLayerInfo(info);
        context.Free(points, 8);
        context.Free(regionNode, RegionRectangle.Size);
        context.Free(region, Region.Size);
        context.FreePlanarBitMap(image);
        return trace.ToArray();

        void WritePoint(int index, short x, short y)
        {
            var at = points + checked((uint)(index * 4));
            memory.WriteUInt16(APTR.FromPointer(at), 0, unchecked((ushort)x));
            memory.WriteUInt16(APTR.FromPointer(at), 2, unchecked((ushort)y));
        }

        void SetPenOrMode(GraphicsLvo lvo, uint value) => context.InvokeGraphics((int)lvo, state =>
        {
            state.A[lvo == GraphicsLvo.SetWriteMask ? 0 : 1] = rastPort;
            state.D[0] = value;
        });

        void Toggle(int x, int y)
        {
            var offset = y * image.BytesPerRow + x / 8;
            expectedPixels[offset] ^= (byte)(0x80 >> (x & 7));
        }

        void AssertLineState(short x, short y, byte phase, bool firstDot)
        {
            Assert.Equal(x, LayersRastPortCodec.ReadCurrentX(ref memory, rp));
            Assert.Equal(y, LayersRastPortCodec.ReadCurrentY(ref memory, rp));
            Assert.Equal(linePattern, LayersRastPortCodec.ReadLinePattern(ref memory, rp));
            Assert.Equal(phase, LayersRastPortCodec.ReadLinePatternCount(ref memory, rp));
            Assert.Equal(nonLineFlags | (firstDot ? RastPortFlags.FirstDot : RastPortFlags.None),
                LayersRastPortCodec.ReadFlags(ref memory, rp));
            Assert.Equal((byte)3, LayersRastPortCodec.ReadDrawMode(ref memory, rp));
            Assert.Equal((byte)0xFF, LayersRastPortCodec.ReadMask(ref memory, rp));
        }

        void Capture(string phase)
        {
            Assert.Equal(region, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRegion));
            Assert.Equal(callerRegionBytes, Bytes(region, callerRegionBytes.Length));
            Assert.Equal(callerNodeBytes, Bytes(regionNode, callerNodeBytes.Length));
            const LayerFlags publicFlags = LayerFlags.Simple | LayerFlags.Smart | LayerFlags.Super |
                LayerFlags.Backdrop | LayerFlags.Updating | LayerFlags.Refresh | LayerFlags.ClipRectsLost;
            var row = $"polydraw-refresh:{phase}:flags={ReadOracleLayerFlags(context.Bus, layer) & publicFlags}:" +
                $"damage={DescribeRegion(context, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList))}:" +
                $"visible=[{string.Join(',', VisibleCoverage())}]:" +
                $"pixels={Convert.ToHexString(Bytes(image.Planes[0], expectedPixels.Length))}:" +
                $"cursor={LayersRastPortCodec.ReadCurrentX(ref memory, rp)}," +
                $"{LayersRastPortCodec.ReadCurrentY(ref memory, rp)}:" +
                $"pattern={LayersRastPortCodec.ReadLinePattern(ref memory, rp):X4}:" +
                $"phase={LayersRastPortCodec.ReadLinePatternCount(ref memory, rp)}:" +
                $"first-dot={(LayersRastPortCodec.ReadFlags(ref memory, rp) & RastPortFlags.FirstDot) != 0}:" +
                "point-array=unchanged";
            trace.Add(row);
            _output.WriteLine((context.Native ? "Native " : "CopperStart ") + row);
        }

        byte[] Bytes(uint address, int count) => Enumerable.Range(0, count)
            .Select(offset => context.Bus.ReadByte(address + checked((uint)offset))).ToArray();

        int[] VisibleCoverage()
        {
            var coverage = new SortedSet<int>();
            var seen = new HashSet<uint>();
            for (var node = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRect); node != 0;
                 node = ReadOracleClipRectPointer(context.Bus, node, OracleClipRectPointer.Next))
            {
                Assert.True(seen.Count < 256 && seen.Add(node));
                Assert.True((node & 1) == 0 && context.Bus.IsMappedMemoryRange(node, checked((int)ClipRect.Size)));
                if (ReadOracleClipRectPointer(context.Bus, node, OracleClipRectPointer.ObscuringLayer) != 0) continue;
                var bounds = ReadOracleClipRectBounds(context.Bus, node);
                Assert.InRange((int)bounds.MinX, 0, width - 1);
                Assert.InRange((int)bounds.MaxX, bounds.MinX, width - 1);
                Assert.InRange((int)bounds.MinY, 0, height - 1);
                Assert.InRange((int)bounds.MaxY, bounds.MinY, height - 1);
                for (var y = (int)bounds.MinY; y <= bounds.MaxY; y++)
                for (var x = (int)bounds.MinX; x <= bounds.MaxX; x++)
                    Assert.True(coverage.Add(y * width + x), "Visible ClipRects must not overlap.");
            }
            return coverage.ToArray();
        }
    }
}
