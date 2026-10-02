using Amiga;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed partial class KickstartRomLayersDifferentialTests
{
    [Fact]
    public void CopperStartDrawEllipseDuringDamageRefreshPreservesOwnership()
    {
        var context = CreateCopperStartOracle();
        try { _ = TraceDrawEllipseDuringDamageRefresh(context); }
        finally { context.Machine.Dispose(); }
    }

    [Fact]
    public void DrawEllipseDuringDamageRefreshMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native DrawEllipse refresh NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));

        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            var vector = unchecked((uint)((int)native.GraphicsBase +
                (int)GraphicsLvo.DrawEllipse));
            Assert.False(native.Bus.HasHostGateway(vector));
            Assert.Equal((ushort)0x4EF9, native.Bus.ReadWord(vector));
            Assert.InRange(native.Bus.ReadLong(vector + 2),
                0x00F8_0000u, 0x00FF_FFFFu);
            var memory = new LayersTestGuestMemory(native.Bus);
            _output.WriteLine("G228 configured V40.63: A500 PAL OCS, 512 KiB CHIP, " +
                "AccurateM68000/live Agnus; native DrawEllipse uninstrumented. " +
                $"layers={ReadOracleLibraryVersion(native.Bus, native.LayersBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref memory, APTR.FromPointer(native.LayersBase))}; " +
                $"graphics={ReadOracleLibraryVersion(native.Bus, native.GraphicsBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref memory, APTR.FromPointer(native.GraphicsBase))}");
            expected = TraceDrawEllipseDuringDamageRefresh(native);
        }
        finally { native.Machine.Dispose(); }

        var copperStart = CreateCopperStartOracle();
        try { Assert.Equal(expected, TraceDrawEllipseDuringDamageRefresh(copperStart)); }
        finally { copperStart.Machine.Dispose(); }
    }

    private string[] TraceDrawEllipseDuringDamageRefresh(OracleContext context)
    {
        const int width = 32;
        const int height = 16;
        const short centerX = 9;
        const short centerY = 5;
        const short radiusX = 3;
        const short radiusY = 3;
        const ushort linePattern = 0xA640;
        const byte initialPixels = 0xE6;

        var trace = new List<string>();
        var memory = new LayersTestGuestMemory(context.Bus);
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(width, height, 1, 0,
            planeMemoryFlags: Exec.MemoryFlags.Chip);
        Assert.Equal(display.Planes[0], context.Bus.MaskChipDmaAddress(display.Planes[0]));
        var layer = context.CreateLayer(LayersLvo.CreateUpfrontLayer, layerInfo,
            display.Address, 0, LayerCreationFlags.Simple, 0, 0, width - 1, height - 1);
        var cover = context.CreateLayer(LayersLvo.CreateUpfrontLayer, layerInfo,
            display.Address, 0, LayerCreationFlags.Simple, 4, 3, 11, 10);
        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = cover).D[0]);
        context.WaitForBlitterIdle();

        var damage = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList);
        var pendingDamage = DescribeRegion(context, damage);
        Assert.Contains("coverage=[y=3,10:x=4,11]", pendingDamage);
        var region = context.CreateSingleRectangleRegion(7, 0, 13, 7);
        var regionNode = ReadOracleRegionFirst(context.Bus, region);
        var regionBytes = Bytes(region, checked((int)Region.Size));
        var regionNodeBytes = Bytes(regionNode, checked((int)RegionRectangle.Size));
        Assert.Equal(0u, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state => { state.A[0] = layer; state.A[1] = region; }).D[0]);

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
        SetPenOrMode(GraphicsLvo.SetDrMd, 2); // COMPLEMENT makes every outline toggle visible.
        SetPenOrMode(GraphicsLvo.SetWriteMask, 0xFF);
        LayersRastPortCodec.WriteLinePattern(ref memory, rp, linePattern);
        LayersRastPortCodec.WriteLinePatternCount(ref memory, rp, 9);
        var nonLineFlags = LayersRastPortCodec.ReadFlags(ref memory, rp) &
            ~RastPortFlags.FirstDot;
        LayersRastPortCodec.WriteFlags(ref memory, rp,
            nonLineFlags | RastPortFlags.FirstDot);

        var expectedPixels = Enumerable.Repeat(initialPixels,
            display.BytesPerRow * height).ToArray();
        for (var offset = 0; offset < expectedPixels.Length; offset++)
            context.Bus.WriteByte(display.Planes[0] + checked((uint)offset), initialPixels, 0);
        context.InvokeGraphics((int)GraphicsLvo.Move, state =>
        {
            state.A[1] = rastPort;
            state.D[0] = 2;
            state.D[1] = 4;
        });
        var cursorX = LayersRastPortCodec.ReadCurrentX(ref memory, rp);
        var cursorY = LayersRastPortCodec.ReadCurrentY(ref memory, rp);
        var linePatternCount = LayersRastPortCodec.ReadLinePatternCount(ref memory, rp);
        var rastPortFlags = LayersRastPortCodec.ReadFlags(ref memory, rp);

        // Independent integer-midpoint outline for radius 3: the only
        // samples inside damage∩caller ClipRegion are its four diagonal
        // corners (7,3), (11,3), (7,7), and (11,7). All other ellipse points
        // lie outside the admitted 5x5 rectangle.
        Toggle(7, 3);
        Toggle(11, 3);
        Toggle(7, 7);
        Toggle(11, 7);
        Capture("before-update");

        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.BeginUpdate,
            state => state.A[0] = layer).D[0]);
        Assert.Equal(activeCoverage, VisibleCoverage());
        Assert.Equal(pendingDamage, DescribeRegion(context, damage));
        Assert.True((ReadOracleLayerFlags(context.Bus, layer) & LayerFlags.Updating) != 0);
        Capture("begin");

        var activeHead = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRect);
        var activeFlags = ReadOracleLayerFlags(context.Bus, layer);
        context.InvokeGraphics((int)GraphicsLvo.DrawEllipse, state =>
        {
            state.A[1] = rastPort;
            state.D[0] = unchecked((ushort)centerX);
            state.D[1] = unchecked((ushort)centerY);
            state.D[2] = unchecked((ushort)radiusX);
            state.D[3] = unchecked((ushort)radiusY);
        });
        context.WaitForBlitterIdle();
        Assert.False(context.Bus.Blitter.Busy);
        Assert.Equal(expectedPixels, Bytes(display.Planes[0], expectedPixels.Length));
        Assert.Equal(activeHead,
            ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRect));
        Assert.Equal(activeFlags, ReadOracleLayerFlags(context.Bus, layer));
        Assert.Equal(damage, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList));
        Assert.Equal(pendingDamage, DescribeRegion(context, damage));
        Assert.Equal(damageBytes, Bytes(damage, damageBytes.Length));
        Assert.Equal(damageNodeBytes, Bytes(damageNode, damageNodeBytes.Length));
        Assert.Equal(activeCoverage, VisibleCoverage());
        Assert.Equal(cursorX, LayersRastPortCodec.ReadCurrentX(ref memory, rp));
        Assert.Equal(cursorY, LayersRastPortCodec.ReadCurrentY(ref memory, rp));
        Assert.Equal(linePattern, LayersRastPortCodec.ReadLinePattern(ref memory, rp));
        Assert.Equal(linePatternCount,
            LayersRastPortCodec.ReadLinePatternCount(ref memory, rp));
        Assert.Equal(rastPortFlags, LayersRastPortCodec.ReadFlags(ref memory, rp));
        Capture("draw-ellipse");

        context.InvokeLayers(LayersLvo.EndUpdate, state =>
        {
            state.A[0] = layer;
            state.D[0] = 1;
        });
        Assert.Equal(stableCoverage, VisibleCoverage());
        Assert.Equal(damage,
            ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList));
        Assert.Equal(new byte[checked((int)Region.Size)],
            Bytes(damage, checked((int)Region.Size)));
        Assert.True((ReadOracleLayerFlags(context.Bus, layer) & LayerFlags.Updating) == 0);
        Assert.True((ReadOracleLayerFlags(context.Bus, layer) & LayerFlags.Refresh) != 0);
        Assert.Equal(expectedPixels, Bytes(display.Planes[0], expectedPixels.Length));
        Capture("complete-end");

        Assert.Equal(region, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state => { state.A[0] = layer; state.A[1] = 0; }).D[0]);
        Assert.Equal(regionBytes, Bytes(region, regionBytes.Length));
        Assert.Equal(regionNodeBytes, Bytes(regionNode, regionNodeBytes.Length));
        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = layer).D[0]);
        context.DisposeLayerInfo(layerInfo);
        context.Free(regionNode, RegionRectangle.Size);
        context.Free(region, Region.Size);
        context.FreePlanarBitMap(display);
        return trace.ToArray();

        void SetPenOrMode(GraphicsLvo lvo, uint value) => context.InvokeGraphics((int)lvo, state =>
        {
            state.A[lvo == GraphicsLvo.SetWriteMask ? 0 : 1] = rastPort;
            state.D[0] = value;
        });

        void Toggle(int x, int y)
        {
            var offset = y * display.BytesPerRow + x / 8;
            expectedPixels[offset] ^= (byte)(0x80 >> (x & 7));
        }

        void Capture(string phase)
        {
            Assert.Equal(regionBytes, Bytes(region, regionBytes.Length));
            Assert.Equal(regionNodeBytes, Bytes(regionNode, regionNodeBytes.Length));
            const LayerFlags publicFlags = LayerFlags.Simple | LayerFlags.Smart | LayerFlags.Super |
                LayerFlags.Backdrop | LayerFlags.Updating | LayerFlags.Refresh | LayerFlags.ClipRectsLost;
            var row = $"drawellipse-refresh:{phase}:flags={ReadOracleLayerFlags(context.Bus, layer) & publicFlags}:" +
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

        byte[] Bytes(uint address, int count) => Enumerable.Range(0, count)
            .Select(offset => context.Bus.ReadByte(address + checked((uint)offset))).ToArray();

        int[] VisibleCoverage()
        {
            var coverage = new SortedSet<int>();
            var seen = new HashSet<uint>();
            for (var node = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRect);
                 node != 0;
                 node = ReadOracleClipRectPointer(context.Bus, node, OracleClipRectPointer.Next))
            {
                Assert.True(seen.Count < 256 && seen.Add(node));
                Assert.True((node & 1) == 0 &&
                    context.Bus.IsMappedMemoryRange(node, checked((int)ClipRect.Size)));
                if (ReadOracleClipRectPointer(context.Bus, node,
                        OracleClipRectPointer.ObscuringLayer) != 0)
                    continue;

                var bounds = ReadOracleClipRectBounds(context.Bus, node);
                Assert.InRange((int)bounds.MinX, 0, width - 1);
                Assert.InRange((int)bounds.MaxX, bounds.MinX, width - 1);
                Assert.InRange((int)bounds.MinY, 0, height - 1);
                Assert.InRange((int)bounds.MaxY, bounds.MinY, height - 1);
                for (var y = (int)bounds.MinY; y <= bounds.MaxY; y++)
                for (var x = (int)bounds.MinX; x <= bounds.MaxX; x++)
                    Assert.True(coverage.Add(y * width + x),
                        "Visible ClipRects must not overlap.");
            }
            return coverage.ToArray();
        }
    }
}
