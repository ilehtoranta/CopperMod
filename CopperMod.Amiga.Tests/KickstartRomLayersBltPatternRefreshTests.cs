using Amiga;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed partial class KickstartRomLayersDifferentialTests
{
    [Fact]
    public void CopperStartBltPatternDuringDamageRefreshPreservesOwnership()
    {
        var context = CreateCopperStartOracle();
        try { _ = TraceBltPatternDuringDamageRefresh(context); }
        finally { context.Machine.Dispose(); }
    }

    [Fact]
    public void BltPatternDuringDamageRefreshMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native BltPattern refresh NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));

        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            var vector = unchecked((uint)((int)native.GraphicsBase +
                (int)GraphicsLvo.BltPattern));
            Assert.False(native.Bus.HasHostGateway(vector));
            Assert.Equal((ushort)0x4EF9, native.Bus.ReadWord(vector));
            Assert.InRange(native.Bus.ReadLong(vector + 2),
                0x00F8_0000u, 0x00FF_FFFFu);
            var memory = new LayersTestGuestMemory(native.Bus);
            _output.WriteLine("G233 configured V40.63: A500 PAL OCS, 512 KiB CHIP, " +
                "AccurateM68000/live Agnus; native BltPattern uninstrumented. " +
                $"layers={ReadOracleLibraryVersion(native.Bus, native.LayersBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref memory, APTR.FromPointer(native.LayersBase))}; " +
                $"graphics={ReadOracleLibraryVersion(native.Bus, native.GraphicsBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref memory, APTR.FromPointer(native.GraphicsBase))}");
            expected = TraceBltPatternDuringDamageRefresh(native);
        }
        finally { native.Machine.Dispose(); }

        var copperStart = CreateCopperStartOracle();
        try
        {
            Assert.Equal(expected, TraceBltPatternDuringDamageRefresh(copperStart));
        }
        finally { copperStart.Machine.Dispose(); }
    }

    private string[] TraceBltPatternDuringDamageRefresh(OracleContext context)
    {
        const int width = 32;
        const int height = 16;
        const short minX = 5;
        const short minY = 1;
        const short maxX = 12;
        const short maxY = 8;
        const byte initialPixels = 0xA5;
        const byte stencilGuard = 0xC7;
        const byte areaPatternGuard = 0x3D;
        const int guardBytes = 8;
        const int stencilBytes = 16; // 8 rows, two-byte word-padded stride.
        const int areaPatternBytes = 8; // Four rows for AreaPtSz = 2.
        ushort[] areaPatternWords = [0xA35C, 0x6996, 0xC33C, 0x5AA5];
        byte[] stencilRows = [0xB5, 0x6A, 0xD2, 0x4F, 0x9C, 0xE1, 0x37, 0x8B];

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
        var stencil = context.Allocate(
            stencilBytes + guardBytes,
            Exec.MemoryFlags.Chip);
        var areaPattern = context.Allocate(areaPatternBytes + guardBytes);
        for (var offset = 0; offset < stencilBytes + guardBytes; offset++)
            context.Bus.WriteByte(stencil + checked((uint)offset), stencilGuard, 0);
        for (var offset = 0; offset < areaPatternBytes + guardBytes; offset++)
            context.Bus.WriteByte(areaPattern + checked((uint)offset), areaPatternGuard, 0);
        for (var row = 0; row < stencilRows.Length; row++)
            context.Bus.WriteByte(stencil + checked((uint)(row * 2)), stencilRows[row], 0);
        for (var row = 0; row < areaPatternWords.Length; row++)
            context.Bus.WriteWord(areaPattern + checked((uint)(row * 2)), areaPatternWords[row]);

        context.Bus.WriteLong(rastPort + (uint)GraphicsLayouts.RastPortAreaPtrn, areaPattern);
        context.Bus.WriteByte(rastPort + (uint)GraphicsLayouts.RastPortAreaPtSz, 2, 0);
        SetPenOrMode(GraphicsLvo.SetAPen, 1);
        SetPenOrMode(GraphicsLvo.SetBPen, 0);
        SetPenOrMode(GraphicsLvo.SetDrMd, 1); // JAM2: choose APen/BPen from AreaPtrn.
        SetPenOrMode(GraphicsLvo.SetWriteMask, 1);

        var expectedPixels = Enumerable.Repeat(initialPixels,
            display.BytesPerRow * height).ToArray();
        for (var offset = 0; offset < expectedPixels.Length; offset++)
            context.Bus.WriteByte(display.Planes[0] + checked((uint)offset), initialPixels, 0);

        // The caller rectangle starts at a non-origin phase. The area pattern
        // is anchored to screen coordinates; the external stencil is anchored
        // to the requested rectangle. Only damage ∩ caller clip is writable.
        for (var y = 3; y <= 7; y++)
        for (var x = 7; x <= 11; x++)
        {
            var stencilRow = y - minY;
            var stencilColumn = x - minX;
            if ((stencilRows[stencilRow] & (0x80 >> stencilColumn)) == 0)
                continue;

            var areaWord = areaPatternWords[y & 3];
            var destinationMask = (byte)(0x80 >> (x & 7));
            var destinationOffset = y * display.BytesPerRow + x / 8;
            if ((areaWord & (0x8000 >> (x & 15))) != 0)
                expectedPixels[destinationOffset] |= destinationMask;
            else
                expectedPixels[destinationOffset] &= (byte)~destinationMask;
        }

        var cursorX = LayersRastPortCodec.ReadCurrentX(ref memory, rp);
        var cursorY = LayersRastPortCodec.ReadCurrentY(ref memory, rp);
        var linePattern = LayersRastPortCodec.ReadLinePattern(ref memory, rp);
        var linePatternCount = LayersRastPortCodec.ReadLinePatternCount(ref memory, rp);
        var rastPortFlags = LayersRastPortCodec.ReadFlags(ref memory, rp);
        var foreground = LayersRastPortCodec.ReadForegroundPen(ref memory, rp);
        var background = LayersRastPortCodec.ReadBackgroundPen(ref memory, rp);
        var drawMode = LayersRastPortCodec.ReadDrawMode(ref memory, rp);
        var writeMask = context.Bus.ReadByte(rastPort + (uint)GraphicsLayouts.RastPortMask);
        var areaPatternPointer = context.Bus.ReadLong(rastPort + (uint)GraphicsLayouts.RastPortAreaPtrn);
        var areaPatternSize = context.Bus.ReadByte(rastPort + (uint)GraphicsLayouts.RastPortAreaPtSz);
        var stencilBytesBefore = Bytes(stencil, stencilBytes + guardBytes);
        var areaPatternBytesBefore = Bytes(areaPattern, areaPatternBytes + guardBytes);
        Capture("before-update");

        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.BeginUpdate,
            state => state.A[0] = layer).D[0]);
        Assert.Equal(activeCoverage, VisibleCoverage());
        Assert.Equal(pendingDamage, DescribeRegion(context, damage));
        Assert.True((ReadOracleLayerFlags(context.Bus, layer) & LayerFlags.Updating) != 0);
        Capture("begin");

        var activeHead = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRect);
        var activeFlags = ReadOracleLayerFlags(context.Bus, layer);
        context.InvokeGraphics((int)GraphicsLvo.BltPattern, state =>
        {
            state.A[0] = stencil;
            state.A[1] = rastPort;
            state.D[0] = unchecked((ushort)minX);
            state.D[1] = unchecked((ushort)minY);
            state.D[2] = unchecked((ushort)maxX);
            state.D[3] = unchecked((ushort)maxY);
            state.D[4] = 2; // Signed-WORD byteCnt, two-byte stencil row stride.
        });
        context.WaitForBlitterIdle();
        Assert.False(context.Bus.Blitter.Busy);
        Capture("blt-pattern");
        Assert.Equal(expectedPixels, Bytes(display.Planes[0], expectedPixels.Length));
        Assert.Equal(stencilBytesBefore, Bytes(stencil, stencilBytesBefore.Length));
        Assert.Equal(areaPatternBytesBefore, Bytes(areaPattern, areaPatternBytesBefore.Length));
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
        Assert.Equal(expectedPixels, Bytes(display.Planes[0], expectedPixels.Length));
        Assert.Equal(stencilBytesBefore, Bytes(stencil, stencilBytesBefore.Length));
        Assert.Equal(areaPatternBytesBefore, Bytes(areaPattern, areaPatternBytesBefore.Length));
        AssertRastPortState();
        Capture("complete-end");

        Assert.Equal(region, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state => { state.A[0] = layer; state.A[1] = 0; }).D[0]);
        Assert.Equal(regionBytes, Bytes(region, regionBytes.Length));
        Assert.Equal(regionNodeBytes, Bytes(regionNode, regionNodeBytes.Length));
        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = layer).D[0]);
        context.DisposeLayerInfo(layerInfo);
        context.Free(areaPattern, areaPatternBytes + guardBytes);
        context.Free(stencil, stencilBytes + guardBytes);
        context.Free(regionNode, RegionRectangle.Size);
        context.Free(region, Region.Size);
        context.FreePlanarBitMap(display);
        return trace.ToArray();

        void SetPenOrMode(GraphicsLvo lvo, uint value) => context.InvokeGraphics((int)lvo, state =>
        {
            state.A[lvo == GraphicsLvo.SetWriteMask ? 0 : 1] = rastPort;
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
            Assert.Equal(areaPatternPointer,
                context.Bus.ReadLong(rastPort + (uint)GraphicsLayouts.RastPortAreaPtrn));
            Assert.Equal(areaPatternSize,
                context.Bus.ReadByte(rastPort + (uint)GraphicsLayouts.RastPortAreaPtSz));
        }

        void Capture(string phase)
        {
            Assert.Equal(regionBytes, Bytes(region, regionBytes.Length));
            Assert.Equal(regionNodeBytes, Bytes(regionNode, regionNodeBytes.Length));
            const LayerFlags publicFlags = LayerFlags.Simple | LayerFlags.Smart | LayerFlags.Super |
                LayerFlags.Backdrop | LayerFlags.Updating | LayerFlags.Refresh | LayerFlags.ClipRectsLost;
            var row = $"bltpattern-refresh:{phase}:flags={ReadOracleLayerFlags(context.Bus, layer) & publicFlags}:" +
                $"damage={DescribeRegion(context, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList))}:" +
                $"visible=[{string.Join(',', VisibleCoverage())}]:" +
                $"pixels={Convert.ToHexString(Bytes(display.Planes[0], expectedPixels.Length))}:" +
                $"cursor={LayersRastPortCodec.ReadCurrentX(ref memory, rp)}," +
                $"{LayersRastPortCodec.ReadCurrentY(ref memory, rp)}:" +
                $"line={LayersRastPortCodec.ReadLinePattern(ref memory, rp):X4}," +
                $"{LayersRastPortCodec.ReadLinePatternCount(ref memory, rp)}:" +
                $"pens={LayersRastPortCodec.ReadForegroundPen(ref memory, rp)}," +
                $"{LayersRastPortCodec.ReadBackgroundPen(ref memory, rp)}:" +
                $"draw-mode={LayersRastPortCodec.ReadDrawMode(ref memory, rp)}:" +
                $"mask={context.Bus.ReadByte(rastPort + (uint)GraphicsLayouts.RastPortMask)}:" +
                $"area-pattern-size={context.Bus.ReadByte(rastPort + (uint)GraphicsLayouts.RastPortAreaPtSz)}:" +
                $"caller-sources=unchanged:caller-region=unchanged";
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
