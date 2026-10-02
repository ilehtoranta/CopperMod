using Amiga;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed partial class KickstartRomLayersDifferentialTests
{
    [Fact]
    public void CopperStartBltBitMapRastPortDuringDamageRefreshPreservesOwnership()
    {
        var context = CreateCopperStartOracle();
        try { _ = TraceBltBitMapRastPortDuringDamageRefresh(context); }
        finally { context.Machine.Dispose(); }
    }

    [Fact]
    public void BltBitMapRastPortDuringDamageRefreshMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native BltBitMapRastPort refresh NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));

        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            var vector = unchecked((uint)((int)native.GraphicsBase +
                (int)GraphicsLvo.BltBitMapRastPort));
            Assert.False(native.Bus.HasHostGateway(vector));
            Assert.Equal((ushort)0x4EF9, native.Bus.ReadWord(vector));
            Assert.InRange(native.Bus.ReadLong(vector + 2),
                0x00F8_0000u, 0x00FF_FFFFu);
            var memory = new LayersTestGuestMemory(native.Bus);
            _output.WriteLine("G235 configured V40.63: A500 PAL OCS, 512 KiB CHIP, " +
                "AccurateM68000/live Agnus; native BltBitMapRastPort uninstrumented. " +
                $"layers={ReadOracleLibraryVersion(native.Bus, native.LayersBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref memory, APTR.FromPointer(native.LayersBase))}; " +
                $"graphics={ReadOracleLibraryVersion(native.Bus, native.GraphicsBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref memory, APTR.FromPointer(native.GraphicsBase))}");
            expected = TraceBltBitMapRastPortDuringDamageRefresh(native);
        }
        finally { native.Machine.Dispose(); }

        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceBltBitMapRastPortDuringDamageRefresh(copperStart));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    private string[] TraceBltBitMapRastPortDuringDamageRefresh(OracleContext context)
    {
        const int width = 32;
        const int height = 16;
        const short sourceX = 3;
        const short sourceY = 2;
        const short destinationX = 5;
        const short destinationY = 1;
        const short transferWidth = 8;
        const short transferHeight = 8;
        const byte initialPlane0 = 0xA5;
        const byte initialPlane1 = 0x5A;
        const byte writeMask = 0x01;
        const byte minterm = 0xC0; // Copy source into the selected destination plane.
        const int sourceWidth = 16;
        const int sourceHeight = 12;

        var trace = new List<string>();
        var memory = new LayersTestGuestMemory(context.Bus);
        var layerInfo = context.NewLayerInfo();
        var display = context.CreatePlanarBitMap(width, height, 2, 0,
            planeMemoryFlags: Exec.MemoryFlags.Chip);
        var source = context.CreatePlanarBitMap(sourceWidth, sourceHeight, 2, 0x37,
            planeMemoryFlags: Exec.MemoryFlags.Chip);
        Assert.All(display.Planes, plane =>
            Assert.Equal(plane, context.Bus.MaskChipDmaAddress(plane)));
        Assert.All(source.Planes, plane =>
            Assert.Equal(plane, context.Bus.MaskChipDmaAddress(plane)));

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

        var sourcePlaneBytes = checked(source.BytesPerRow * source.Height);
        var sourcePlane0Before = Bytes(source.Planes[0], sourcePlaneBytes);
        var sourcePlane1Before = Bytes(source.Planes[1], sourcePlaneBytes);
        var expectedPlane0 = Enumerable.Repeat(initialPlane0,
            display.BytesPerRow * height).ToArray();
        var expectedPlane1 = Enumerable.Repeat(initialPlane1,
            display.BytesPerRow * height).ToArray();
        for (var offset = 0; offset < expectedPlane0.Length; offset++)
        {
            context.Bus.WriteByte(display.Planes[0] + checked((uint)offset), initialPlane0, 0);
            context.Bus.WriteByte(display.Planes[1] + checked((uint)offset), initialPlane1, 0);
        }

        // Source and destination coordinates intentionally have different
        // non-origin phases. Minterm 0xC0 copies source bits, while the
        // RastPort plane mask protects destination plane 1.
        for (var y = 3; y <= 7; y++)
        for (var x = 7; x <= 11; x++)
        {
            var sourcePixelX = sourceX + (x - destinationX);
            var sourcePixelY = sourceY + (y - destinationY);
            var sourceOffset = sourcePixelY * source.BytesPerRow + sourcePixelX / 8;
            var sourceBit = (sourcePlane0Before[sourceOffset] &
                (0x80 >> (sourcePixelX & 7))) != 0;
            var destinationOffset = y * display.BytesPerRow + x / 8;
            var destinationBit = (byte)(0x80 >> (x & 7));
            if (sourceBit)
                expectedPlane0[destinationOffset] |= destinationBit;
            else
                expectedPlane0[destinationOffset] &= (byte)~destinationBit;
        }

        SetWriteMask(writeMask);
        var cursorX = LayersRastPortCodec.ReadCurrentX(ref memory, rp);
        var cursorY = LayersRastPortCodec.ReadCurrentY(ref memory, rp);
        var linePattern = LayersRastPortCodec.ReadLinePattern(ref memory, rp);
        var linePatternCount = LayersRastPortCodec.ReadLinePatternCount(ref memory, rp);
        var rastPortFlags = LayersRastPortCodec.ReadFlags(ref memory, rp);
        var foreground = LayersRastPortCodec.ReadForegroundPen(ref memory, rp);
        var background = LayersRastPortCodec.ReadBackgroundPen(ref memory, rp);
        var drawMode = LayersRastPortCodec.ReadDrawMode(ref memory, rp);
        Capture("before-update");

        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.BeginUpdate,
            state => state.A[0] = layer).D[0]);
        Assert.Equal(activeCoverage, VisibleCoverage());
        Assert.Equal(pendingDamage, DescribeRegion(context, damage));
        Assert.True((ReadOracleLayerFlags(context.Bus, layer) & LayerFlags.Updating) != 0);
        Capture("begin");

        var activeHead = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRect);
        var activeFlags = ReadOracleLayerFlags(context.Bus, layer);
        context.InvokeGraphics((int)GraphicsLvo.BltBitMapRastPort, state =>
        {
            state.A[0] = source.Address;
            state.A[1] = rastPort;
            state.D[0] = unchecked((ushort)sourceX);
            state.D[1] = unchecked((ushort)sourceY);
            state.D[2] = unchecked((ushort)destinationX);
            state.D[3] = unchecked((ushort)destinationY);
            state.D[4] = unchecked((ushort)transferWidth);
            state.D[5] = unchecked((ushort)transferHeight);
            state.D[6] = minterm;
        });
        context.WaitForBlitterIdle();
        Assert.False(context.Bus.Blitter.Busy);
        Capture("blt-bitmap-rastport");
        Assert.Equal(expectedPlane0, Bytes(display.Planes[0], expectedPlane0.Length));
        Assert.Equal(expectedPlane1, Bytes(display.Planes[1], expectedPlane1.Length));
        Assert.Equal(sourcePlane0Before, Bytes(source.Planes[0], sourcePlane0Before.Length));
        Assert.Equal(sourcePlane1Before, Bytes(source.Planes[1], sourcePlane1Before.Length));
        Assert.Equal(activeHead,
            ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRect));
        Assert.Equal(activeFlags, ReadOracleLayerFlags(context.Bus, layer));
        Assert.Equal(damage, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList));
        Assert.Equal(pendingDamage, DescribeRegion(context, damage));
        Assert.Equal(damageBytes, Bytes(damage, damageBytes.Length));
        Assert.Equal(damageNodeBytes, Bytes(damageNode, damageNodeBytes.Length));
        Assert.Equal(activeCoverage, VisibleCoverage());
        AssertRastPortState();

        context.InvokeLayers(LayersLvo.EndUpdate, state =>
        {
            state.A[0] = layer;
            state.D[0] = 1;
        });
        Assert.Equal(stableCoverage, VisibleCoverage());
        Assert.Equal(damage,
            ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList));
        Assert.Equal(new byte[checked((int)Region.Size)], Bytes(damage, checked((int)Region.Size)));
        Assert.True((ReadOracleLayerFlags(context.Bus, layer) & LayerFlags.Updating) == 0);
        Assert.True((ReadOracleLayerFlags(context.Bus, layer) & LayerFlags.Refresh) != 0);
        Assert.Equal(expectedPlane0, Bytes(display.Planes[0], expectedPlane0.Length));
        Assert.Equal(expectedPlane1, Bytes(display.Planes[1], expectedPlane1.Length));
        Assert.Equal(sourcePlane0Before, Bytes(source.Planes[0], sourcePlane0Before.Length));
        Assert.Equal(sourcePlane1Before, Bytes(source.Planes[1], sourcePlane1Before.Length));
        AssertRastPortState();
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
        context.FreePlanarBitMap(source);
        context.FreePlanarBitMap(display);
        return trace.ToArray();

        void SetWriteMask(uint value) => context.InvokeGraphics(
            (int)GraphicsLvo.SetWriteMask,
            state =>
            {
                state.A[0] = rastPort;
                state.D[0] = value;
            });

        void AssertRastPortState()
        {
            Assert.Equal(cursorX, LayersRastPortCodec.ReadCurrentX(ref memory, rp));
            Assert.Equal(cursorY, LayersRastPortCodec.ReadCurrentY(ref memory, rp));
            Assert.Equal(linePattern, LayersRastPortCodec.ReadLinePattern(ref memory, rp));
            Assert.Equal(linePatternCount, LayersRastPortCodec.ReadLinePatternCount(ref memory, rp));
            Assert.Equal(rastPortFlags, LayersRastPortCodec.ReadFlags(ref memory, rp));
            Assert.Equal(foreground, LayersRastPortCodec.ReadForegroundPen(ref memory, rp));
            Assert.Equal(background, LayersRastPortCodec.ReadBackgroundPen(ref memory, rp));
            Assert.Equal(drawMode, LayersRastPortCodec.ReadDrawMode(ref memory, rp));
            Assert.Equal(writeMask, context.Bus.ReadByte(rastPort + (uint)GraphicsLayouts.RastPortMask));
        }

        void Capture(string phase)
        {
            Assert.Equal(regionBytes, Bytes(region, regionBytes.Length));
            Assert.Equal(regionNodeBytes, Bytes(regionNode, regionNodeBytes.Length));
            const LayerFlags publicFlags = LayerFlags.Simple | LayerFlags.Smart | LayerFlags.Super |
                LayerFlags.Backdrop | LayerFlags.Updating | LayerFlags.Refresh | LayerFlags.ClipRectsLost;
            var row = $"bltbmprastport-refresh:{phase}:flags={ReadOracleLayerFlags(context.Bus, layer) & publicFlags}:" +
                $"damage={DescribeRegion(context, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList))}:" +
                $"visible=[{string.Join(',', VisibleCoverage())}]:" +
                $"plane0={Convert.ToHexString(Bytes(display.Planes[0], expectedPlane0.Length))}:" +
                $"plane1={Convert.ToHexString(Bytes(display.Planes[1], expectedPlane1.Length))}:" +
                $"source0={Convert.ToHexString(Bytes(source.Planes[0], sourcePlaneBytes))}:" +
                $"cursor={LayersRastPortCodec.ReadCurrentX(ref memory, rp)}," +
                $"{LayersRastPortCodec.ReadCurrentY(ref memory, rp)}:" +
                $"line={LayersRastPortCodec.ReadLinePattern(ref memory, rp):X4}," +
                $"{LayersRastPortCodec.ReadLinePatternCount(ref memory, rp)}:" +
                $"pens={LayersRastPortCodec.ReadForegroundPen(ref memory, rp)}," +
                $"{LayersRastPortCodec.ReadBackgroundPen(ref memory, rp)}:" +
                $"draw-mode={LayersRastPortCodec.ReadDrawMode(ref memory, rp)}:" +
                $"write-mask={context.Bus.ReadByte(rastPort + (uint)GraphicsLayouts.RastPortMask)}:" +
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
