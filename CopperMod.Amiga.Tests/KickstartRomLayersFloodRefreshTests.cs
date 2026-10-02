using Amiga;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed partial class KickstartRomLayersDifferentialTests
{
    [Fact]
    public void CopperStartFloodDuringDamageRefreshPreservesOwnership()
    {
        var context = CreateCopperStartOracle();
        try { _ = TraceFloodDuringDamageRefresh(context, mode: 1); }
        finally { context.Machine.Dispose(); }
    }

    [Fact]
    public void FloodDuringDamageRefreshMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native Flood refresh NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));

        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            var vector = unchecked((uint)((int)native.GraphicsBase +
                (int)GraphicsLvo.Flood));
            Assert.False(native.Bus.HasHostGateway(vector));
            Assert.Equal((ushort)0x4EF9, native.Bus.ReadWord(vector));
            Assert.InRange(native.Bus.ReadLong(vector + 2),
                0x00F8_0000u, 0x00FF_FFFFu);
            var memory = new LayersTestGuestMemory(native.Bus);
            _output.WriteLine("G231 configured V40.63: A500 PAL OCS, 512 KiB CHIP, " +
                "AccurateM68000/live Agnus; native Flood uninstrumented. " +
                $"layers={ReadOracleLibraryVersion(native.Bus, native.LayersBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref memory, APTR.FromPointer(native.LayersBase))}; " +
                $"graphics={ReadOracleLibraryVersion(native.Bus, native.GraphicsBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref memory, APTR.FromPointer(native.GraphicsBase))}");
            expected = TraceFloodDuringDamageRefresh(native, mode: 1);
        }
        finally { native.Machine.Dispose(); }

        var copperStart = CreateCopperStartOracle();
        try { Assert.Equal(expected, TraceFloodDuringDamageRefresh(copperStart, mode: 1)); }
        finally { copperStart.Machine.Dispose(); }
    }

    [Fact]
    public void CopperStartOutlineFloodDuringDamageRefreshPreservesOwnership()
    {
        var context = CreateCopperStartOracle();
        try { _ = TraceFloodDuringDamageRefresh(context, mode: 0); }
        finally { context.Machine.Dispose(); }
    }

    [Fact]
    public void OutlineFloodDuringDamageRefreshMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native outline Flood refresh NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));

        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            var vector = unchecked((uint)((int)native.GraphicsBase +
                (int)GraphicsLvo.Flood));
            Assert.False(native.Bus.HasHostGateway(vector));
            Assert.Equal((ushort)0x4EF9, native.Bus.ReadWord(vector));
            Assert.InRange(native.Bus.ReadLong(vector + 2),
                0x00F8_0000u, 0x00FF_FFFFu);
            var memory = new LayersTestGuestMemory(native.Bus);
            _output.WriteLine("G232 configured V40.63: A500 PAL OCS, 512 KiB CHIP, " +
                "AccurateM68000/live Agnus; native outline Flood uninstrumented. " +
                $"layers={ReadOracleLibraryVersion(native.Bus, native.LayersBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref memory, APTR.FromPointer(native.LayersBase))}; " +
                $"graphics={ReadOracleLibraryVersion(native.Bus, native.GraphicsBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref memory, APTR.FromPointer(native.GraphicsBase))}");
            expected = TraceFloodDuringDamageRefresh(native, mode: 0);
        }
        finally { native.Machine.Dispose(); }

        var copperStart = CreateCopperStartOracle();
        try { Assert.Equal(expected, TraceFloodDuringDamageRefresh(copperStart, mode: 0)); }
        finally { copperStart.Machine.Dispose(); }
    }

    private string[] TraceFloodDuringDamageRefresh(OracleContext context, uint mode)
    {
        const int width = 32;
        const int height = 16;
        const int islandMinX = 2;
        const int islandMinY = 1;
        const int islandMaxX = 18;
        const int islandMaxY = 9;
        const short seedX = 9;
        const short seedY = 5;
        const byte initialPixels = 0xFF;
        const byte temporaryRasterGuard = 0xA9;
        const int temporaryRasterGuardBytes = 16;

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

        // The mode-1 fixture has a zero-colour island surrounded by ones.
        // In mode 0 that surrounding colour is also the closed OutlinePen
        // boundary. The seed lies in the bounded interior, while only 25
        // pixels are admitted by the active damage/caller-clip intersection.
        var initialPlane = Enumerable.Repeat(initialPixels,
            display.BytesPerRow * height).ToArray();
        for (var y = islandMinY; y <= islandMaxY; y++)
        for (var x = islandMinX; x <= islandMaxX; x++)
            initialPlane[y * display.BytesPerRow + x / 8] &=
                (byte)~(0x80 >> (x & 7));
        var expectedPixels = (byte[])initialPlane.Clone();
        for (var y = 3; y <= 7; y++)
        for (var x = 7; x <= 11; x++)
            expectedPixels[y * display.BytesPerRow + x / 8] |=
                (byte)(0x80 >> (x & 7));
        for (var offset = 0; offset < initialPlane.Length; offset++)
            context.Bus.WriteByte(display.Planes[0] + checked((uint)offset),
                initialPlane[offset], 0);

        var tmpRas = context.Allocate((uint)GraphicsLayouts.TmpRasSize);
        var temporaryRasterBytes = checked((uint)(display.BytesPerRow * height));
        var tmpRasBuffer = context.Allocate(
            temporaryRasterBytes + temporaryRasterGuardBytes,
            Exec.MemoryFlags.Chip);
        for (var offset = 0; offset < temporaryRasterBytes + temporaryRasterGuardBytes; offset++)
            context.Bus.WriteByte(tmpRasBuffer + checked((uint)offset), temporaryRasterGuard, 0);
        context.InvokeGraphics((int)GraphicsLvo.InitTmpRas, state =>
        {
            state.A[0] = tmpRas;
            state.A[1] = tmpRasBuffer;
            state.D[0] = temporaryRasterBytes;
        });
        context.Bus.WriteLong(rastPort + (uint)GraphicsLayouts.RastPortTmpRas, tmpRas);
        context.Bus.WriteLong(rastPort + (uint)GraphicsLayouts.RastPortAreaPtrn, 0);
        context.Bus.WriteByte(rastPort + (uint)GraphicsLayouts.RastPortAreaPtSz, 0, 0);

        SetPenOrMode(GraphicsLvo.SetAPen, 1);
        SetPenOrMode(GraphicsLvo.SetBPen, 0);
        SetPenOrMode(GraphicsLvo.SetDrMd, 0); // JAM1: write APen into the region.
        SetPenOrMode(GraphicsLvo.SetWriteMask, 1);
        if (mode == 0)
            SetPenOrMode(GraphicsLvo.SetOutlinePen, 1);

        var tmpRasBytes = Bytes(tmpRas, GraphicsLayouts.TmpRasSize);
        var cursorX = LayersRastPortCodec.ReadCurrentX(ref memory, rp);
        var cursorY = LayersRastPortCodec.ReadCurrentY(ref memory, rp);
        var linePattern = LayersRastPortCodec.ReadLinePattern(ref memory, rp);
        var linePatternCount = LayersRastPortCodec.ReadLinePatternCount(ref memory, rp);
        var rastPortFlags = LayersRastPortCodec.ReadFlags(ref memory, rp);
        var foreground = LayersRastPortCodec.ReadForegroundPen(ref memory, rp);
        var background = LayersRastPortCodec.ReadBackgroundPen(ref memory, rp);
        var drawMode = LayersRastPortCodec.ReadDrawMode(ref memory, rp);
        var writeMask = context.Bus.ReadByte(rastPort + (uint)GraphicsLayouts.RastPortMask);
        var outlinePen = context.Bus.ReadByte(rastPort + (uint)GraphicsLayouts.RastPortOutlinePen);
        var areaPattern = context.Bus.ReadLong(rastPort + (uint)GraphicsLayouts.RastPortAreaPtrn);
        var areaPatternSize = context.Bus.ReadByte(rastPort + (uint)GraphicsLayouts.RastPortAreaPtSz);
        var tmpRasPointer = context.Bus.ReadLong(rastPort + (uint)GraphicsLayouts.RastPortTmpRas);
        Capture("before-update", null);

        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.BeginUpdate,
            state => state.A[0] = layer).D[0]);
        Assert.Equal(activeCoverage, VisibleCoverage());
        Assert.Equal(pendingDamage, DescribeRegion(context, damage));
        Assert.True((ReadOracleLayerFlags(context.Bus, layer) & LayerFlags.Updating) != 0);
        Capture("begin", null);

        var activeHead = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRect);
        var activeFlags = ReadOracleLayerFlags(context.Bus, layer);
        var flood = context.InvokeGraphics((int)GraphicsLvo.Flood, state =>
        {
            state.A[1] = rastPort;
            state.D[0] = unchecked((ushort)seedX);
            state.D[1] = unchecked((ushort)seedY);
            state.D[2] = mode;
        });
        context.WaitForBlitterIdle();
        Assert.False(context.Bus.Blitter.Busy);
        Capture("flood", flood.D[0]);
        Assert.Equal(expectedPixels, Bytes(display.Planes[0], expectedPixels.Length));
        Assert.Equal(activeHead,
            ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRect));
        Assert.Equal(activeFlags, ReadOracleLayerFlags(context.Bus, layer));
        Assert.Equal(damage, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList));
        Assert.Equal(pendingDamage, DescribeRegion(context, damage));
        Assert.Equal(damageBytes, Bytes(damage, damageBytes.Length));
        Assert.Equal(damageNodeBytes, Bytes(damageNode, damageNodeBytes.Length));
        Assert.Equal(activeCoverage, VisibleCoverage());
        Assert.Equal(tmpRas, context.Bus.ReadLong(rastPort + (uint)GraphicsLayouts.RastPortTmpRas));
        Assert.Equal(tmpRasBytes, Bytes(tmpRas, tmpRasBytes.Length));
        Assert.Equal(tmpRasBuffer, context.Bus.ReadLong(
            tmpRas + (uint)GraphicsLayouts.TmpRasRasPtr));
        Assert.Equal(temporaryRasterBytes, context.Bus.ReadLong(
            tmpRas + (uint)GraphicsLayouts.TmpRasByteCount));
        Assert.Equal(Enumerable.Repeat(temporaryRasterGuard, temporaryRasterGuardBytes).ToArray(),
            Bytes(tmpRasBuffer + temporaryRasterBytes, temporaryRasterGuardBytes));
        Assert.Equal(cursorX, LayersRastPortCodec.ReadCurrentX(ref memory, rp));
        Assert.Equal(cursorY, LayersRastPortCodec.ReadCurrentY(ref memory, rp));
        Assert.Equal(linePattern, LayersRastPortCodec.ReadLinePattern(ref memory, rp));
        Assert.Equal(linePatternCount,
            LayersRastPortCodec.ReadLinePatternCount(ref memory, rp));
        Assert.Equal(rastPortFlags, LayersRastPortCodec.ReadFlags(ref memory, rp));
        Assert.Equal(foreground, LayersRastPortCodec.ReadForegroundPen(ref memory, rp));
        Assert.Equal(background, LayersRastPortCodec.ReadBackgroundPen(ref memory, rp));
        Assert.Equal(drawMode, LayersRastPortCodec.ReadDrawMode(ref memory, rp));
        AssertRastPortState();

        // Keep the native no-write BOOL result covered as a separate case:
        // a zero plane mask is a valid no-op, not a declined vector.
        context.Bus.WriteByte(rastPort + (uint)GraphicsLayouts.RastPortMask, 0, 0);
        var noWriteFlood = context.InvokeGraphics((int)GraphicsLvo.Flood, state =>
        {
            state.A[1] = rastPort;
            state.D[0] = unchecked((ushort)seedX);
            state.D[1] = unchecked((ushort)seedY);
            state.D[2] = mode;
        });
        context.Bus.WriteByte(rastPort + (uint)GraphicsLayouts.RastPortMask, 1, 0);
        Assert.Equal(expectedPixels, Bytes(display.Planes[0], expectedPixels.Length));
        AssertRastPortState();
        Capture("zero-mask-flood", noWriteFlood.D[0]);

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
        Assert.Equal(tmpRasBytes, Bytes(tmpRas, tmpRasBytes.Length));
        Assert.Equal(Enumerable.Repeat(temporaryRasterGuard, temporaryRasterGuardBytes).ToArray(),
            Bytes(tmpRasBuffer + temporaryRasterBytes, temporaryRasterGuardBytes));
        Capture("complete-end", null);

        Assert.Equal(region, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state => { state.A[0] = layer; state.A[1] = 0; }).D[0]);
        Assert.Equal(regionBytes, Bytes(region, regionBytes.Length));
        Assert.Equal(regionNodeBytes, Bytes(regionNode, regionNodeBytes.Length));
        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = layer).D[0]);
        context.DisposeLayerInfo(layerInfo);
        context.Free(tmpRasBuffer, temporaryRasterBytes + temporaryRasterGuardBytes);
        context.Free(tmpRas, (uint)GraphicsLayouts.TmpRasSize);
        context.Free(regionNode, RegionRectangle.Size);
        context.Free(region, Region.Size);
        context.FreePlanarBitMap(display);
        return trace.ToArray();

        void SetPenOrMode(GraphicsLvo lvo, uint value) => context.InvokeGraphics((int)lvo, state =>
        {
            state.A[lvo == GraphicsLvo.SetWriteMask ||
                lvo == GraphicsLvo.SetOutlinePen ||
                lvo == GraphicsLvo.SetMaxPen ? 0 : 1] = rastPort;
            state.D[0] = value;
        });

        void AssertRastPortState()
        {
            Assert.Equal(cursorX, LayersRastPortCodec.ReadCurrentX(ref memory, rp));
            Assert.Equal(cursorY, LayersRastPortCodec.ReadCurrentY(ref memory, rp));
            Assert.Equal(linePattern, LayersRastPortCodec.ReadLinePattern(ref memory, rp));
            Assert.Equal(linePatternCount,
                LayersRastPortCodec.ReadLinePatternCount(ref memory, rp));
            Assert.Equal(rastPortFlags, LayersRastPortCodec.ReadFlags(ref memory, rp));
            Assert.Equal(foreground, LayersRastPortCodec.ReadForegroundPen(ref memory, rp));
            Assert.Equal(background, LayersRastPortCodec.ReadBackgroundPen(ref memory, rp));
            Assert.Equal(drawMode, LayersRastPortCodec.ReadDrawMode(ref memory, rp));
            Assert.Equal(writeMask,
                context.Bus.ReadByte(rastPort + (uint)GraphicsLayouts.RastPortMask));
            Assert.Equal(outlinePen,
                context.Bus.ReadByte(rastPort + (uint)GraphicsLayouts.RastPortOutlinePen));
            Assert.Equal(areaPattern,
                context.Bus.ReadLong(rastPort + (uint)GraphicsLayouts.RastPortAreaPtrn));
            Assert.Equal(areaPatternSize,
                context.Bus.ReadByte(rastPort + (uint)GraphicsLayouts.RastPortAreaPtSz));
            Assert.Equal(tmpRasPointer,
                context.Bus.ReadLong(rastPort + (uint)GraphicsLayouts.RastPortTmpRas));
        }

        void Capture(string phase, uint? operationResult)
        {
            Assert.Equal(regionBytes, Bytes(region, regionBytes.Length));
            Assert.Equal(regionNodeBytes, Bytes(regionNode, regionNodeBytes.Length));
            const LayerFlags publicFlags = LayerFlags.Simple | LayerFlags.Smart | LayerFlags.Super |
                LayerFlags.Backdrop | LayerFlags.Updating | LayerFlags.Refresh | LayerFlags.ClipRectsLost;
            var row = $"flood-refresh:mode-{mode}:{phase}:flags={ReadOracleLayerFlags(context.Bus, layer) & publicFlags}:" +
                $"damage={DescribeRegion(context, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList))}:" +
                $"visible=[{string.Join(',', VisibleCoverage())}]:" +
                $"pixels={Convert.ToHexString(Bytes(display.Planes[0], expectedPixels.Length))}:" +
                $"result={(operationResult.HasValue ? operationResult.Value.ToString("X8") : "-" )}:" +
                $"tmp-ras-owned={context.Bus.ReadLong(rastPort + (uint)GraphicsLayouts.RastPortTmpRas) == tmpRas}:" +
                $"tmp-ras-size={context.Bus.ReadLong(tmpRas + (uint)GraphicsLayouts.TmpRasByteCount)}:" +
                $"cursor={LayersRastPortCodec.ReadCurrentX(ref memory, rp)}," +
                $"{LayersRastPortCodec.ReadCurrentY(ref memory, rp)}:" +
                $"pens={LayersRastPortCodec.ReadForegroundPen(ref memory, rp)}," +
                $"{LayersRastPortCodec.ReadBackgroundPen(ref memory, rp)}:" +
                $"draw-mode={LayersRastPortCodec.ReadDrawMode(ref memory, rp)}:" +
                $"mask={context.Bus.ReadByte(rastPort + (uint)GraphicsLayouts.RastPortMask)}:" +
                $"outline={context.Bus.ReadByte(rastPort + (uint)GraphicsLayouts.RastPortOutlinePen)}:" +
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
