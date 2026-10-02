using Amiga;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed partial class KickstartRomLayersDifferentialTests
{
    [Fact]
    public void CopperStartPatternedDrawDuringDamageRefreshPreservesOwnership()
    {
        var context = CreateCopperStartOracle();
        try { _ = TracePatternedDrawDuringDamageRefresh(context); }
        finally { context.Machine.Dispose(); }
    }

    [Fact]
    public void PatternedDrawDuringDamageRefreshMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native patterned Draw refresh NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));
        string[] expected;
        // Native Draw, including its clipping and blitter work, is unpatched.
        // Run native first so a host failure cannot hide native evidence.
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            foreach (var lvo in new[] { GraphicsLvo.Move, GraphicsLvo.Draw })
            {
                var vector = unchecked((uint)((int)native.GraphicsBase + (int)lvo));
                Assert.False(native.Bus.HasHostGateway(vector));
                Assert.Equal((ushort)0x4EF9, native.Bus.ReadWord(vector));
                Assert.InRange(native.Bus.ReadLong(vector + 2), 0x00F8_0000u, 0x00FF_FFFFu);
            }
            var memory = new LayersTestGuestMemory(native.Bus);
            _output.WriteLine("G225 configured V40.63: A500 PAL OCS, 512 KiB CHIP, " +
                "AccurateM68000/live Agnus; native graphics vectors uninstrumented. " +
                $"layers={ReadOracleLibraryVersion(native.Bus, native.LayersBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref memory, APTR.FromPointer(native.LayersBase))}; " +
                $"graphics={ReadOracleLibraryVersion(native.Bus, native.GraphicsBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref memory, APTR.FromPointer(native.GraphicsBase))}");
            expected = TracePatternedDrawDuringDamageRefresh(native);
        }
        finally { native.Machine.Dispose(); }
        var copperStart = CreateCopperStartOracle();
        try { Assert.Equal(expected, TracePatternedDrawDuringDamageRefresh(copperStart)); }
        finally { copperStart.Machine.Dispose(); }
    }

    private string[] TracePatternedDrawDuringDamageRefresh(OracleContext context)
    {
        const int width = 32;
        const int height = 16;
        const int startX = 2;
        const int lineY = 4;
        const ushort linePattern = 0xA640;
        // Both segments must visibly set foreground and clear background.
        const byte initialPixels = 0xE6;
        var trace = new List<string>();
        var memory = new LayersTestGuestMemory(context.Bus);
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(width, height, 1, 0,
            planeMemoryFlags: Exec.MemoryFlags.Chip);
        Assert.Equal(display.Planes[0], context.Bus.MaskChipDmaAddress(display.Planes[0]));
        Assert.InRange(display.Planes[0], 1u, checked((uint)(context.Machine.Options.ChipRamSize -
            display.BytesPerRow * display.Height)));
        var layer = context.CreateLayer(LayersLvo.CreateUpfrontLayer, layerInfo,
            display.Address, 0, LayerCreationFlags.Simple, 0, 0, width - 1, height - 1);
        var cover = context.CreateLayer(LayersLvo.CreateUpfrontLayer, layerInfo,
            display.Address, 0, LayerCreationFlags.Simple, 4, 3, 11, 10);
        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = cover).D[0]);
        context.WaitForBlitterIdle();
        var damage = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList);
        Assert.Contains("coverage=[y=3,10:x=4,11]", DescribeRegion(context, damage));
        var region = context.CreateSingleRectangleRegion(7, 0, 13, 7);
        var regionNode = ReadOracleRegionFirst(context.Bus, region);
        var regionBytes = Bytes(region, checked((int)Region.Size));
        var nodeBytes = Bytes(regionNode, checked((int)RegionRectangle.Size));
        Assert.Equal(0u, context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
        {
            state.A[0] = layer;
            state.A[1] = region;
        }).D[0]);
        var pendingDamage = DescribeRegion(context, damage);
        var damageBytes = Bytes(damage, checked((int)Region.Size));
        var damageNode = ReadOracleRegionFirst(context.Bus, damage);
        var damageNodeBytes = Bytes(damageNode, checked((int)RegionRectangle.Size));
        var stableCoverage = VisibleCoverage();
        var projection = Enumerable.Range(3, 5)
            .SelectMany(y => Enumerable.Range(7, 5).Select(x => y * width + x)).ToArray();
        var rastPort = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.RastPort);
        var rp = APTR.FromPointer(rastPort);
        SetPenOrMode(GraphicsLvo.SetAPen, 1);
        SetPenOrMode(GraphicsLvo.SetBPen, 0);
        SetPenOrMode(GraphicsLvo.SetDrMd, 1); // JAM2, including native minterms.
        SetPenOrMode(GraphicsLvo.SetWriteMask, 0xFF);
        // SetDrPt is a public macro, NOT an LVO (graphics/gfxmacros.h).
        LayersRastPortCodec.WriteLinePattern(ref memory, rp, linePattern);
        LayersRastPortCodec.WriteFlags(ref memory, rp,
            LayersRastPortCodec.ReadFlags(ref memory, rp) | RastPortFlags.FirstDot);
        LayersRastPortCodec.WriteLinePatternCount(ref memory, rp, 15);
        var expectedPixels = Enumerable.Repeat(initialPixels, display.BytesPerRow * height).ToArray();
        for (var offset = 0; offset < expectedPixels.Length; offset++)
            context.Bus.WriteByte(display.Planes[0] + checked((uint)offset), initialPixels, 0);
        context.InvokeGraphics((int)GraphicsLvo.Move, state =>
        {
            state.A[1] = rastPort;
            state.D[0] = startX;
            state.D[1] = lineY;
        });
        var nonLineFlags = LayersRastPortCodec.ReadFlags(ref memory, rp) & ~RastPortFlags.FirstDot;
        AssertLineState(startX, 15, firstDot: true);
        Capture("before-update");
        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.BeginUpdate,
            state => state.A[0] = layer).D[0]);
        Assert.Equal(projection, VisibleCoverage());
        Assert.Equal(pendingDamage, DescribeRegion(context, damage));
        Assert.True((ReadOracleLayerFlags(context.Bus, layer) & LayerFlags.Updating) != 0);
        AssertLineState(startX, 15, firstDot: true);
        Capture("begin");
        DrawSegment(9, 7, 9, 8, "first-draw");
        DrawSegment(15, 10, 11, 2, "connected-draw");
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
        AssertLineState(15, 2, firstDot: true);
        Assert.Equal(expectedPixels, Bytes(display.Planes[0], expectedPixels.Length));
        Capture("complete-end");
        Assert.Equal(region, context.InvokeLayers(LayersLvo.InstallClipRegion, state =>
        {
            state.A[0] = layer;
            state.A[1] = 0;
        }).D[0]);
        AssertCallerRegion();
        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = layer).D[0]);
        context.DisposeLayerInfo(layerInfo);
        context.Free(regionNode, RegionRectangle.Size);
        context.Free(region, Region.Size);
        context.FreePlanarBitMap(display);
        return trace.ToArray();

        void SetPenOrMode(GraphicsLvo lvo, uint value) => context.InvokeGraphics((int)lvo, state =>
        {
            // SetWriteMask uses A0; the older pen/mode setters use A1.
            state.A[lvo == GraphicsLvo.SetWriteMask ? 0 : 1] = rastPort;
            state.D[0] = value;
        });

        void DrawSegment(int targetX, int visibleMinX, int visibleMaxX, byte phaseCount, string phase)
        {
            var activeHead = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRect);
            var activeFlags = ReadOracleLayerFlags(context.Bus, layer);
            var setChanges = 0;
            var clearChanges = 0;
            // Independent horizontal-line oracle, indexed from the original
            // Move, not from admitted ClipRects or either renderer's output.
            for (var x = visibleMinX; x <= visibleMaxX; x++)
            {
                var set = (linePattern & (0x8000 >> (x - startX))) != 0;
                var offset = lineY * display.BytesPerRow + x / 8;
                var mask = (byte)(0x80 >> (x & 7));
                var previous = (expectedPixels[offset] & mask) != 0;
                if (set && !previous) setChanges++;
                if (!set && previous) clearChanges++;
                expectedPixels[offset] = set ? (byte)(expectedPixels[offset] | mask)
                    : (byte)(expectedPixels[offset] & ~mask);
            }
            Assert.True(setChanges > 0 && clearChanges > 0);
            context.InvokeGraphics((int)GraphicsLvo.Draw, state =>
            {
                state.A[1] = rastPort;
                state.D[0] = checked((uint)targetX);
                state.D[1] = lineY;
            });
            context.WaitForBlitterIdle();
            Assert.False(context.Bus.Blitter.Busy);
            Capture(phase);
            Assert.Equal(expectedPixels, Bytes(display.Planes[0], expectedPixels.Length));
            // Native preserves FRST_DOT when no single ClipRect contains the
            // original segment. Both calls here are clipped at one endpoint.
            AssertLineState(checked((short)targetX), phaseCount, firstDot: true);
            Assert.Equal(activeHead, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRect));
            Assert.Equal(activeFlags, ReadOracleLayerFlags(context.Bus, layer));
            Assert.Equal(damage, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList));
            Assert.Equal(pendingDamage, DescribeRegion(context, damage));
            Assert.Equal(damageBytes, Bytes(damage, damageBytes.Length));
            Assert.Equal(damageNodeBytes, Bytes(damageNode, damageNodeBytes.Length));
            Assert.Equal(projection, VisibleCoverage());
        }

        void AssertLineState(short x, byte phase, bool firstDot)
        {
            Assert.Equal(x, LayersRastPortCodec.ReadCurrentX(ref memory, rp));
            Assert.Equal((short)lineY, LayersRastPortCodec.ReadCurrentY(ref memory, rp));
            Assert.Equal(linePattern, LayersRastPortCodec.ReadLinePattern(ref memory, rp));
            Assert.Equal(phase, LayersRastPortCodec.ReadLinePatternCount(ref memory, rp));
            Assert.Equal(nonLineFlags | (firstDot ? RastPortFlags.FirstDot : RastPortFlags.None),
                LayersRastPortCodec.ReadFlags(ref memory, rp));
            Assert.Equal((byte)1, LayersRastPortCodec.ReadForegroundPen(ref memory, rp));
            Assert.Equal((byte)0, LayersRastPortCodec.ReadBackgroundPen(ref memory, rp));
            Assert.Equal((byte)1, LayersRastPortCodec.ReadDrawMode(ref memory, rp));
            Assert.Equal((byte)0xFF, LayersRastPortCodec.ReadMask(ref memory, rp));
        }

        void Capture(string phase)
        {
            AssertCallerRegion();
            Assert.Equal(region, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRegion));
            const LayerFlags publicFlags = LayerFlags.Simple | LayerFlags.Smart | LayerFlags.Super |
                LayerFlags.Backdrop | LayerFlags.Updating | LayerFlags.Refresh | LayerFlags.ClipRectsLost;
            var row = $"draw-refresh:{phase}:flags={ReadOracleLayerFlags(context.Bus, layer) & publicFlags}:" +
                $"damage={DescribeRegion(context, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList))}:" +
                $"visible=[{string.Join(',', VisibleCoverage())}]:" +
                $"pixels={Convert.ToHexString(Bytes(display.Planes[0], expectedPixels.Length))}:" +
                $"cursor={LayersRastPortCodec.ReadCurrentX(ref memory, rp)}," +
                $"{LayersRastPortCodec.ReadCurrentY(ref memory, rp)}:" +
                $"pattern={LayersRastPortCodec.ReadLinePattern(ref memory, rp):X4}:" +
                $"phase={LayersRastPortCodec.ReadLinePatternCount(ref memory, rp)}:" +
                $"first-dot={(LayersRastPortCodec.ReadFlags(ref memory, rp) & RastPortFlags.FirstDot) != 0}:" +
                "caller-region=unchanged";
            trace.Add(row);
            _output.WriteLine((context.Native ? "Native " : "CopperStart ") + row);
        }

        void AssertCallerRegion()
        {
            Assert.Equal(regionBytes, Bytes(region, regionBytes.Length));
            Assert.Equal(nodeBytes, Bytes(regionNode, nodeBytes.Length));
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
