using Amiga;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed partial class KickstartRomLayersDifferentialTests
{
    [Fact]
    public void CopperStartAreaEndDuringDamageRefreshPreservesCallerOwnership()
    {
        var context = CreateCopperStartOracle();
        try { _ = TraceAreaEndDuringDamageRefresh(context); }
        finally { context.Machine.Dispose(); }
    }

    [Fact]
    public void AreaEndDuringDamageRefreshMatchesNativeV4063()
    {
        if (!TryLoadConfiguredRom(out var rom))
        {
            _output.WriteLine("Native AreaEnd refresh NOT EXECUTED: configure " +
                RomPathVariable + " and " + RomVersionVariable + "=3.1.");
            return;
        }
        Assert.Equal((ushort)63, BigEndian.ReadUInt16(rom, 14, "revision"));

        string[] expected;
        var native = CreateNativeOracle(rom, instrumentGraphics: false);
        try
        {
            foreach (var lvo in new[]
                     {
                         GraphicsLvo.InitArea,
                         GraphicsLvo.InitTmpRas,
                         GraphicsLvo.AreaMove,
                         GraphicsLvo.AreaDraw,
                         GraphicsLvo.AreaEnd
                     })
            {
                var vector = unchecked((uint)((int)native.GraphicsBase + (int)lvo));
                Assert.False(native.Bus.HasHostGateway(vector));
                Assert.Equal((ushort)0x4EF9, native.Bus.ReadWord(vector));
                Assert.InRange(native.Bus.ReadLong(vector + 2),
                    0x00F8_0000u, 0x00FF_FFFFu);
            }

            var memory = new LayersTestGuestMemory(native.Bus);
            _output.WriteLine("G227 configured V40.63: A500 PAL OCS, 512 KiB CHIP, " +
                "AccurateM68000/live Agnus; native graphics vectors uninstrumented. " +
                $"layers={ReadOracleLibraryVersion(native.Bus, native.LayersBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref memory, APTR.FromPointer(native.LayersBase))}; " +
                $"graphics={ReadOracleLibraryVersion(native.Bus, native.GraphicsBase)}." +
                $"{LayersLibraryCodec.ReadRevision(ref memory, APTR.FromPointer(native.GraphicsBase))}");
            expected = TraceAreaEndDuringDamageRefresh(native);
        }
        finally { native.Machine.Dispose(); }

        var copperStart = CreateCopperStartOracle();
        try { Assert.Equal(expected, TraceAreaEndDuringDamageRefresh(copperStart)); }
        finally { copperStart.Machine.Dispose(); }
    }

    private string[] TraceAreaEndDuringDamageRefresh(OracleContext context)
    {
        const int width = 32;
        const int height = 16;
        const int areaCapacity = 8;
        const int areaStorageBytes = areaCapacity * 5;
        const int areaStorageGuardBytes = 8;
        const int temporaryRasterBytes = 64;
        const int temporaryRasterGuardBytes = 16;
        const ushort areaPatternWord = 0xA640;
        const byte initialPixels = 0xE6;
        const byte areaStorageGuard = 0xD3;
        const byte temporaryRasterGuard = 0xB7;

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

        var areaInfo = context.Allocate((uint)AreaInfo.Size);
        var areaStorage = context.Allocate(
            (uint)(areaStorageBytes + areaStorageGuardBytes));
        var tmpRas = context.Allocate((uint)GraphicsLayouts.TmpRasSize);
        var tmpRasBuffer = context.Allocate(
            (uint)(temporaryRasterBytes + temporaryRasterGuardBytes),
            Exec.MemoryFlags.Chip);
        var areaPattern = context.Allocate(2, Exec.MemoryFlags.Chip);

        context.InvokeGraphics((int)GraphicsLvo.InitArea, state =>
        {
            state.A[0] = areaInfo;
            state.A[1] = areaStorage;
            state.D[0] = areaCapacity;
        });
        WriteRastPortAreaInfo(areaInfo);

        var areaStorageGuardBytesBefore = Enumerable.Repeat(
            areaStorageGuard,
            areaStorageGuardBytes).ToArray();
        for (var offset = 0; offset < areaStorageGuardBytes; offset++)
        {
            context.Bus.WriteByte(
                areaStorage + (uint)(areaStorageBytes + offset),
                areaStorageGuard,
                0);
        }

        var guard = Enumerable.Repeat(temporaryRasterGuard, temporaryRasterGuardBytes).ToArray();
        for (var offset = 0; offset < temporaryRasterBytes + temporaryRasterGuardBytes; offset++)
            context.Bus.WriteByte(tmpRasBuffer + checked((uint)offset), temporaryRasterGuard, 0);
        context.InvokeGraphics((int)GraphicsLvo.InitTmpRas, state =>
        {
            state.A[0] = tmpRas;
            state.A[1] = tmpRasBuffer;
            state.D[0] = (uint)temporaryRasterBytes;
        });
        context.Bus.WriteWord(areaPattern, areaPatternWord, 0);
        context.Bus.WriteLong(rastPort + (uint)GraphicsLayouts.RastPortAreaPtrn, areaPattern);
        context.Bus.WriteByte(rastPort + (uint)GraphicsLayouts.RastPortAreaPtSz, 0, 0);

        SetPenOrMode(GraphicsLvo.SetAPen, 1);
        SetPenOrMode(GraphicsLvo.SetBPen, 0);
        SetPenOrMode(GraphicsLvo.SetDrMd, 1); // JAM2 writes both pattern phases.
        SetPenOrMode(GraphicsLvo.SetWriteMask, 0xFF);
        context.Bus.WriteLong(rastPort + (uint)GraphicsLayouts.RastPortTmpRas, tmpRas);

        var expectedPixels = Enumerable.Repeat(initialPixels,
            display.BytesPerRow * height).ToArray();
        for (var offset = 0; offset < expectedPixels.Length; offset++)
            context.Bus.WriteByte(display.Planes[0] + checked((uint)offset), initialPixels, 0);

        context.InvokeGraphics((int)GraphicsLvo.Move, state =>
        {
            state.A[1] = rastPort;
            state.D[0] = 2;
            state.D[1] = 2;
        });
        var cursorX = LayersRastPortCodec.ReadCurrentX(ref memory, rp);
        var cursorY = LayersRastPortCodec.ReadCurrentY(ref memory, rp);
        var linePhase = LayersRastPortCodec.ReadLinePatternCount(ref memory, rp);
        var rastPortFlags = LayersRastPortCodec.ReadFlags(ref memory, rp);

        // The polygon extends beyond both active damage and caller clipping.
        // JAM2 with A640 writes APen at source-one columns and BPen at
        // source-zero columns; only x=7..11, y=3..7 are eligible.
        for (var y = 3; y <= 7; y++)
        for (var x = 7; x <= 11; x++)
        {
            var sourceOne = (areaPatternWord & (0x8000 >> (x & 15))) != 0;
            var offset = y * display.BytesPerRow + x / 8;
            var mask = (byte)(0x80 >> (x & 7));
            expectedPixels[offset] = sourceOne
                ? (byte)(expectedPixels[offset] | mask)
                : (byte)(expectedPixels[offset] & ~mask);
        }

        ExecuteAreaVector(GraphicsLvo.AreaMove, 2, 2);
        ExecuteAreaVector(GraphicsLvo.AreaDraw, 14, 2);
        ExecuteAreaVector(GraphicsLvo.AreaDraw, 14, 9);
        ExecuteAreaVector(GraphicsLvo.AreaDraw, 2, 9);
        ExecuteAreaVector(GraphicsLvo.AreaDraw, 2, 2);
        Assert.Equal((ushort)5, context.Bus.ReadWord(
            areaInfo + (uint)GraphicsLayouts.AreaInfoCount));
        var vectorTable = context.Bus.ReadLong(
            areaInfo + (uint)GraphicsLayouts.AreaInfoVectorTable);
        var flagTable = context.Bus.ReadLong(
            areaInfo + (uint)GraphicsLayouts.AreaInfoFlagTable);
        Assert.Equal(areaStorage, vectorTable);
        Assert.Equal(areaStorage + (uint)(areaCapacity * 4), flagTable);
        var tmpRasDescriptor = Bytes(tmpRas, GraphicsLayouts.TmpRasSize);
        Assert.Equal(tmpRasBuffer, context.Bus.ReadLong(
            tmpRas + (uint)GraphicsLayouts.TmpRasRasPtr));
        Assert.Equal((uint)temporaryRasterBytes, context.Bus.ReadLong(
            tmpRas + (uint)GraphicsLayouts.TmpRasByteCount));

        Assert.Equal(stableCoverage, VisibleCoverage());
        Capture("before-update");
        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.BeginUpdate,
            state => state.A[0] = layer).D[0]);
        Assert.Equal(activeCoverage, VisibleCoverage());
        Assert.Equal(pendingDamage, DescribeRegion(context, damage));
        Assert.True((ReadOracleLayerFlags(context.Bus, layer) & LayerFlags.Updating) != 0);
        Capture("begin");

        var activeHead = ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.ClipRect);
        var activeFlags = ReadOracleLayerFlags(context.Bus, layer);
        context.InvokeGraphics((int)GraphicsLvo.AreaEnd,
            state => state.A[1] = rastPort);
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

        Assert.Equal(areaInfo, context.Bus.ReadLong(
            rastPort + (uint)GraphicsLayouts.RastPortAreaInfo));
        Assert.Equal((ushort)0, context.Bus.ReadWord(
            areaInfo + (uint)GraphicsLayouts.AreaInfoCount));
        Assert.Equal(vectorTable, context.Bus.ReadLong(
            areaInfo + (uint)GraphicsLayouts.AreaInfoVectorTable));
        Assert.Equal(vectorTable, context.Bus.ReadLong(
            areaInfo + (uint)GraphicsLayouts.AreaInfoVectorPointer));
        Assert.Equal(flagTable, context.Bus.ReadLong(
            areaInfo + (uint)GraphicsLayouts.AreaInfoFlagTable));
        Assert.Equal(flagTable, context.Bus.ReadLong(
            areaInfo + (uint)GraphicsLayouts.AreaInfoFlagPointer));
        Assert.Equal(areaCapacity, context.Bus.ReadWord(
            areaInfo + (uint)GraphicsLayouts.AreaInfoMaxCount));
        Assert.Equal(areaStorageGuardBytesBefore, Bytes(
            areaStorage + (uint)areaStorageBytes,
            areaStorageGuardBytes));
        Assert.Equal(tmpRas, context.Bus.ReadLong(
            rastPort + (uint)GraphicsLayouts.RastPortTmpRas));
        Assert.Equal(tmpRasDescriptor, Bytes(tmpRas, tmpRasDescriptor.Length));
        Assert.Equal(guard, Bytes(
            tmpRasBuffer + (uint)temporaryRasterBytes,
            temporaryRasterGuardBytes));
        Assert.Equal(areaPattern, context.Bus.ReadLong(
            rastPort + (uint)GraphicsLayouts.RastPortAreaPtrn));
        Assert.Equal(areaPatternWord, context.Bus.ReadWord(areaPattern));
        Assert.Equal(cursorX, LayersRastPortCodec.ReadCurrentX(ref memory, rp));
        Assert.Equal(cursorY, LayersRastPortCodec.ReadCurrentY(ref memory, rp));
        Assert.Equal(linePhase, LayersRastPortCodec.ReadLinePatternCount(ref memory, rp));
        Assert.Equal(rastPortFlags, LayersRastPortCodec.ReadFlags(ref memory, rp));
        Capture("area-end");

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
        Assert.Equal(expectedPixels, Bytes(display.Planes[0], expectedPixels.Length));
        Capture("complete-end");

        Assert.Equal(region, context.InvokeLayers(LayersLvo.InstallClipRegion,
            state => { state.A[0] = layer; state.A[1] = 0; }).D[0]);
        Assert.Equal(regionBytes, Bytes(region, regionBytes.Length));
        Assert.Equal(regionNodeBytes, Bytes(regionNode, regionNodeBytes.Length));
        Assert.NotEqual(0u, context.InvokeLayers(LayersLvo.DeleteLayer,
            state => state.A[1] = layer).D[0]);
        context.DisposeLayerInfo(layerInfo);
        context.Free(areaPattern, 2);
        context.Free(tmpRasBuffer, (uint)(temporaryRasterBytes + temporaryRasterGuardBytes));
        context.Free(tmpRas, (uint)GraphicsLayouts.TmpRasSize);
        context.Free(areaStorage, (uint)(areaStorageBytes + areaStorageGuardBytes));
        context.Free(areaInfo, (uint)AreaInfo.Size);
        context.Free(regionNode, RegionRectangle.Size);
        context.Free(region, Region.Size);
        context.FreePlanarBitMap(display);
        return trace.ToArray();

        void WriteRastPortAreaInfo(uint address) => context.Bus.WriteLong(
            rastPort + (uint)GraphicsLayouts.RastPortAreaInfo,
            address);

        void ExecuteAreaVector(GraphicsLvo lvo, short x, short y)
        {
            context.InvokeGraphics((int)lvo, registers =>
            {
                registers.A[1] = rastPort;
                registers.D[0] = unchecked((ushort)x);
                registers.D[1] = unchecked((ushort)y);
            });
        }

        void SetPenOrMode(GraphicsLvo lvo, uint value) => context.InvokeGraphics((int)lvo, state =>
        {
            state.A[lvo == GraphicsLvo.SetWriteMask ? 0 : 1] = rastPort;
            state.D[0] = value;
        });

        void Capture(string phase)
        {
            Assert.Equal(regionBytes, Bytes(region, regionBytes.Length));
            Assert.Equal(regionNodeBytes, Bytes(regionNode, regionNodeBytes.Length));
            const LayerFlags publicFlags = LayerFlags.Simple | LayerFlags.Smart | LayerFlags.Super |
                LayerFlags.Backdrop | LayerFlags.Updating | LayerFlags.Refresh | LayerFlags.ClipRectsLost;
            var vectorOffset = context.Bus.ReadLong(
                areaInfo + (uint)GraphicsLayouts.AreaInfoVectorPointer) - areaStorage;
            var flagOffset = context.Bus.ReadLong(
                areaInfo + (uint)GraphicsLayouts.AreaInfoFlagPointer) -
                (areaStorage + (uint)(areaCapacity * 4));
            var tmpRasOwned = context.Bus.ReadLong(
                tmpRas + (uint)GraphicsLayouts.TmpRasRasPtr) == tmpRasBuffer;
            var row = $"areaend-refresh:{phase}:flags={ReadOracleLayerFlags(context.Bus, layer) & publicFlags}:" +
                $"damage={DescribeRegion(context, ReadOracleLayerPointer(context.Bus, layer, OracleLayerPointer.DamageList))}:" +
                $"visible=[{string.Join(',', VisibleCoverage())}]:" +
                $"pixels={Convert.ToHexString(Bytes(display.Planes[0], expectedPixels.Length))}:" +
                $"area-count={context.Bus.ReadWord(areaInfo + (uint)GraphicsLayouts.AreaInfoCount)}:" +
                $"area-vectors-offset={vectorOffset}:area-flags-offset={flagOffset}:" +
                $"tmp-ras-owned={tmpRasOwned}," +
                $"{context.Bus.ReadLong(tmpRas + (uint)GraphicsLayouts.TmpRasByteCount)}:" +
                $"cursor={LayersRastPortCodec.ReadCurrentX(ref memory, rp)}," +
                $"{LayersRastPortCodec.ReadCurrentY(ref memory, rp)}:caller-storage=retained";
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
