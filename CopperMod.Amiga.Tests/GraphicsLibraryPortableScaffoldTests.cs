using System.Reflection;
using Copper68k;
using CopperMod.Amiga;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class GraphicsLibraryPortableScaffoldTests
{
    [Fact]
    public void V40ManifestContainsAllPublicVectorsAndAlignedOffsets()
    {
        var vectors = Enum.GetValues<GraphicsLvo>();

        Assert.Equal(GraphicsLvoCatalog.PublicVectorCount, vectors.Length);
        Assert.Equal(GraphicsLvoCatalog.FirstPublicLvo, (int)vectors.Max());
        Assert.Equal(GraphicsLvoCatalog.LastPublicLvo, (int)vectors.Min());
        Assert.All(vectors, vector =>
        {
            Assert.True((int)vector < 0);
            Assert.True(GraphicsLvoCatalog.IsAligned(vector));
        });
        Assert.Equal(-198, (int)GraphicsLvo.InitRastPort);
        Assert.Equal(-204, (int)GraphicsLvo.InitVPort);
        Assert.Equal(-222, (int)GraphicsLvo.LoadView);
        Assert.Equal(-306, (int)GraphicsLvo.RectFill);
        Assert.Equal(-804, (int)GraphicsLvo.WeighTAMatch);
        Assert.Equal(-1056, (int)GraphicsLvo.WriteChunkyPixels);
    }

    [Fact]
    public void InitialGuestLayoutsMatchTheExistingAmigaBridgeOffsets()
    {
        Assert.Equal(0x12, GraphicsLayouts.ViewSize);
        Assert.Equal(0x28, GraphicsLayouts.ViewPortSize);
        Assert.Equal(0x0C, GraphicsLayouts.RasInfoSize);
        Assert.Equal(0x28, GraphicsLayouts.BitMapSize);
        Assert.Equal(0xB4, GraphicsLayouts.RastPortMinimumSize);
        Assert.Equal(0x34, GraphicsLayouts.TextFontMinimumSize);
        Assert.Equal(0x0C, GraphicsLayouts.TextExtentSize);
        Assert.Equal(0x10, GraphicsLayouts.ExtSpriteSize);
        Assert.Equal(0x0C, GraphicsLayouts.ExtSpriteWordWidth);
        Assert.Equal(0x0E, GraphicsLayouts.ExtSpriteFlags);
        Assert.Equal(0x8100_0000u, GraphicsLayouts.SpriteAWidth);
        Assert.Equal(0x8200_0020u, GraphicsLayouts.GsTagSpriteNum);
        Assert.Equal(0x3C, GraphicsLayouts.VSpriteSize);
        Assert.Equal(0x26, GraphicsLayouts.GelsInfoSize);
        Assert.Equal(0x12, GraphicsLayouts.GelsInfoCollisionHandler);
        Assert.Equal(0x40, GraphicsLayouts.CollisionTableSize);
        Assert.Equal(0x14, GraphicsLayouts.RastPortGelsInfo);
        Assert.Equal(0x20, GraphicsLayouts.BobSize);
        Assert.Equal(0x26, GraphicsLayouts.AnimCompSize);
        Assert.Equal(0x2A, GraphicsLayouts.AnimObSize);
        Assert.Equal(0x10, GraphicsLayouts.DBufPacketSize);
        Assert.Equal(0x14, GraphicsLayouts.TextFontYSize);
        Assert.Equal(0x22, GraphicsLayouts.TextFontCharData);
        Assert.Equal(0x28, GraphicsLayouts.TextFontCharLoc);
        Assert.Equal(0x0C, GraphicsLayouts.RegionSize);
        Assert.Equal(0x10, GraphicsLayouts.RegionRectangleSize);
        Assert.Equal(0x34, GraphicsLayouts.ColorMapSize);
        Assert.Equal(0x04, GraphicsLayouts.ViewPortColorMap);
        Assert.Equal(0x30, GraphicsLayouts.BitScaleArgsSize);
        Assert.Equal(0x18, GraphicsLayouts.BitScaleArgsSrcBitMap);
        Assert.Equal(0x1C, GraphicsLayouts.BitScaleArgsDestBitMap);
        Assert.Equal(0x10, GraphicsLayouts.BitScaleArgsDestWidth);
        Assert.Equal(0x08, GraphicsLayouts.TextAttrSize);
        Assert.Equal(0x0A, GraphicsLayouts.TextFontName);
        Assert.Equal(0x0E, GraphicsLayouts.TextFontExtension);
        Assert.Equal(0x18, GraphicsLayouts.TextFontExtensionSize);
        Assert.Equal(0x54, GraphicsLayouts.DBufInfoSize);
        Assert.Equal(0x11, GraphicsLayouts.BltNodeSize);
        Assert.Equal(0x04, GraphicsLayouts.BltNodeFunction);
        Assert.Equal(0x0B, GraphicsLayouts.BltNodeBeamSync);
        Assert.Equal(-966, (int)GraphicsLvo.AllocDBufInfo);
        Assert.Equal(-972, (int)GraphicsLvo.FreeDBufInfo);

        Assert.Equal(0x19, GraphicsLayouts.RastPortFgPen);
        Assert.Equal(0x1A, GraphicsLayouts.RastPortBgPen);
        Assert.Equal(0x1C, GraphicsLayouts.RastPortDrawMode);
        Assert.Equal(0x24, GraphicsLayouts.RastPortCurrentX);
        Assert.Equal(0x26, GraphicsLayouts.RastPortCurrentY);
        Assert.Equal(0x34, GraphicsLayouts.RastPortFont);
        Assert.Equal(0x24, GraphicsLayouts.ViewPortRasInfo);
        Assert.Equal(0x08, GraphicsLayouts.BitMapPlanes);
    }

    [Fact]
    public void MemoryContractUsesBigEndianWordsAndLongs()
    {
        var memory = new FakeMemory(16);

        Assert.True(memory.TryWriteWord(2, 0x1234));
        Assert.True(memory.TryWriteLong(4, 0x89ABCDEF));
        Assert.Equal((byte)0x12, memory.Bytes[2]);
        Assert.Equal((byte)0x34, memory.Bytes[3]);
        Assert.Equal((byte)0x89, memory.Bytes[4]);
        Assert.Equal((byte)0xAB, memory.Bytes[5]);
        Assert.Equal((byte)0xCD, memory.Bytes[6]);
        Assert.Equal((byte)0xEF, memory.Bytes[7]);

        Assert.True(memory.TryReadWord(2, out var word));
        Assert.True(memory.TryReadLong(4, out var value));
        Assert.Equal((ushort)0x1234, word);
        Assert.Equal(0x89ABCDEFu, value);
    }

    [Fact]
    public void MemoryContractRejectsOutOfRangeAccessWithoutChangingGuardBytes()
    {
        var memory = new FakeMemory(8);
        memory.Bytes[0] = 0xA5;
        memory.Bytes[7] = 0x5A;

        Assert.False(memory.TryReadLong(6, out _));
        Assert.False(memory.TryWriteLong(6, 0xDEADBEEFu));
        Assert.Equal((byte)0xA5, memory.Bytes[0]);
        Assert.Equal((byte)0x5A, memory.Bytes[7]);
    }

    [Fact]
    public void LayerRomLockVectorsProvideNestedLockAndImmediateTryLockSemantics()
    {
        var memory = new FakeMemory(0x2000);
        var core = new GraphicsLibraryCore(
            memory,
            new FakeAllocator(),
            new FakeBlitter(),
            new FakeDisplay());
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint layer = 0x120;

        var state = new M68kCpuState();
        state.A[0] = layer;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.LockLayerRom));
        Assert.Equal(0u, state.D[0]);

        state.D[0] = 0xFFFF_FFFF;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AttemptLockLayerRom));
        Assert.Equal(0u, state.D[0]);

        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.UnlockLayerRom));
        state.D[0] = 0;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AttemptLockLayerRom));
        Assert.Equal(1u, state.D[0]);

        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.UnlockLayerRom));
        state.A[0] = 0;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AttemptLockLayerRom));
        Assert.Equal(0u, state.D[0]);
        Assert.False(adapter.TryInvoke(state, (int)GraphicsLvo.SyncSBitMap));
        Assert.False(adapter.TryInvoke(state, (int)GraphicsLvo.CopySBitMap));
    }

    [Fact]
    public void LayerLockCallsCanBeForwardedToASeparateNativeBackend()
    {
        var backend = new RecordingLayerBackend();
        var core = new GraphicsLibraryCore(
            new FakeMemory(0x1000),
            new FakeAllocator(),
            new FakeBlitter(),
            new FakeDisplay(),
            backend);

        core.LockLayerRom(0x120);
        Assert.True(core.AttemptLockLayerRom(0x120));
        core.UnlockLayerRom(0x120);
        Assert.True(core.SyncSBitMap(0x120));
        Assert.True(core.CopySBitMap(0x120));

        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        var state = new M68kCpuState { A = { [0] = 0x120 } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.SyncSBitMap));
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.CopySBitMap));

        Assert.Equal(
            new[]
            {
                "lock:120", "try:120", "unlock:120", "sync:120", "copy:120",
                "sync:120", "copy:120"
            },
            backend.Calls);
    }

    [Fact]
    public void ColorMapStoresRgb4AndRgb32WithGuestCompatibleLayout()
    {
        var memory = new FakeMemory(0x10000);
        var allocator = new BitmapAllocator();
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), new FakeDisplay());

        var colorMap = core.GetColorMap(4);
        Assert.NotEqual(0u, colorMap);
        Assert.Equal((byte)0x02, ReadByte(memory, colorMap + (uint)GraphicsLayouts.ColorMapType));
        Assert.Equal((ushort)4, ReadWord(memory, colorMap + (uint)GraphicsLayouts.ColorMapCount));
        var colors = ReadLong(memory, colorMap + (uint)GraphicsLayouts.ColorMapColorTable);
        var lowColors = ReadLong(memory, colorMap + (uint)GraphicsLayouts.ColorMapLowColorBits);
        Assert.NotEqual(0u, colors);
        Assert.NotEqual(0u, lowColors);
        Assert.Equal((ushort)0, ReadWord(memory, colors));
        Assert.Equal((ushort)0, ReadWord(memory, lowColors));

        Assert.True(core.SetRGB4CM(colorMap, 1, 0x0A, 0x05, 0x0F));
        Assert.Equal(0x0A5Fu, core.GetRGB4(colorMap, 1));
        Assert.True(core.SetRGB32CM(colorMap, 2, 0xAB000000, 0x34120000, 0xFF800000));
        Assert.Equal(0x0A3Fu, core.GetRGB4(colorMap, 2));

        Assert.True(core.GetRGB32(colorMap, 1, 2, 0x900));
        Assert.Equal(0xA0A0_A0A0u, ReadLong(memory, 0x900));
        Assert.Equal(0x5050_5050u, ReadLong(memory, 0x904));
        Assert.Equal(0xF0F0_F0F0u, ReadLong(memory, 0x908));
        Assert.Equal(0xABAB_ABABu, ReadLong(memory, 0x90C));
        Assert.Equal(0x3434_3434u, ReadLong(memory, 0x910));
        Assert.Equal(0xFFFF_FFFFu, ReadLong(memory, 0x914));

        Assert.True(core.FreeColorMap(colorMap));
        Assert.Equal(3, allocator.Freed.Count);
        Assert.False(core.FreeColorMap(colorMap));
    }

    [Fact]
    public void AttachPalExtraAllocatesAndPublishesTheGuestPaletteSharingEnvelope()
    {
        var memory = new FakeMemory(0x10000);
        var allocator = new BitmapAllocator();
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), new FakeDisplay());
        var colorMap = core.GetColorMap(4);
        const uint viewPort = 0x400;

        Assert.Equal(0, core.AttachPalExtra(colorMap, viewPort));
        var extra = ReadLong(memory, colorMap + (uint)GraphicsLayouts.ColorMapPaletteExtra);
        Assert.NotEqual(0u, extra);
        Assert.Equal((ushort)0, ReadWord(memory, extra + (uint)GraphicsLayouts.PaletteExtraFirstFree));
        Assert.Equal((ushort)4, ReadWord(memory, extra + (uint)GraphicsLayouts.PaletteExtraNFree));
        Assert.Equal((ushort)0, ReadWord(memory, extra + (uint)GraphicsLayouts.PaletteExtraFirstShared));
        Assert.Equal((ushort)0, ReadWord(memory, extra + (uint)GraphicsLayouts.PaletteExtraNShared));
        Assert.NotEqual(0u, ReadLong(memory, extra + (uint)GraphicsLayouts.PaletteExtraRefCount));
        Assert.NotEqual(0u, ReadLong(memory, extra + (uint)GraphicsLayouts.PaletteExtraAllocList));
        Assert.Equal(viewPort, ReadLong(memory, extra + (uint)GraphicsLayouts.PaletteExtraViewPort));
        Assert.Equal((ushort)4, ReadWord(memory, extra + (uint)GraphicsLayouts.PaletteExtraSharableColors));

        var refCount = ReadLong(memory, extra + (uint)GraphicsLayouts.PaletteExtraRefCount);
        var allocList = ReadLong(memory, extra + (uint)GraphicsLayouts.PaletteExtraAllocList);
        var shared = core.ObtainPen(colorMap, uint.MaxValue, 0xFFFF_FFFF, 0, 0, 0);
        Assert.Equal(0, shared);
        Assert.Equal((ushort)1, ReadWord(memory, extra + (uint)GraphicsLayouts.PaletteExtraFirstFree));
        Assert.Equal((ushort)3, ReadWord(memory, extra + (uint)GraphicsLayouts.PaletteExtraNFree));
        Assert.Equal((ushort)0, ReadWord(memory, extra + (uint)GraphicsLayouts.PaletteExtraFirstShared));
        Assert.Equal((ushort)1, ReadWord(memory, extra + (uint)GraphicsLayouts.PaletteExtraNShared));
        Assert.Equal((byte)1, ReadByte(memory, refCount));
        Assert.Equal((byte)0, ReadByte(memory, allocList));

        var exclusive = core.ObtainPen(colorMap, 1, 0, 0xFFFF_FFFF, 0, 1);
        Assert.Equal(1, exclusive);
        Assert.Equal((ushort)2, ReadWord(memory, extra + (uint)GraphicsLayouts.PaletteExtraFirstFree));
        Assert.Equal((ushort)2, ReadWord(memory, extra + (uint)GraphicsLayouts.PaletteExtraNFree));
        Assert.Equal((ushort)0, ReadWord(memory, extra + (uint)GraphicsLayouts.PaletteExtraFirstShared));
        Assert.Equal((ushort)1, ReadWord(memory, extra + (uint)GraphicsLayouts.PaletteExtraNShared));
        Assert.Equal((byte)1, ReadByte(memory, refCount + 1));
        Assert.Equal((byte)1, ReadByte(memory, allocList + 1));
        Assert.True(core.ReleasePen(colorMap, (uint)shared));
        Assert.True(core.ReleasePen(colorMap, (uint)exclusive));
        Assert.Equal((ushort)0, ReadWord(memory, extra + (uint)GraphicsLayouts.PaletteExtraFirstFree));
        Assert.Equal((ushort)4, ReadWord(memory, extra + (uint)GraphicsLayouts.PaletteExtraNFree));
        Assert.Equal((ushort)0, ReadWord(memory, extra + (uint)GraphicsLayouts.PaletteExtraNShared));

        // Attaching the same viewport is idempotent; a different association
        // is rejected rather than leaking a second private palette envelope.
        Assert.Equal(0, core.AttachPalExtra(colorMap, viewPort));
        Assert.Equal(1, core.AttachPalExtra(colorMap, 0x500));

        var secondMap = core.GetColorMap(2);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        var state = new M68kCpuState { A = { [0] = secondMap, [1] = viewPort } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AttachPalExtra));
        Assert.Equal(0u, state.D[0]);
        Assert.NotEqual(0u, ReadLong(memory, secondMap + (uint)GraphicsLayouts.ColorMapPaletteExtra));
        Assert.True(core.FreeColorMap(secondMap));

        Assert.True(core.FreeColorMap(colorMap));
        Assert.Equal(12, allocator.Freed.Count);
    }

    [Fact]
    public void ColorMapViewportAndRgb32TableOperationsUsePortableGateway()
    {
        var memory = new FakeMemory(0x10000);
        var allocator = new BitmapAllocator();
        var display = new FakeDisplay();
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), display);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        var viewPort = 0x200u;
        var colorMap = InvokeGetColorMap(adapter, 4);
        Assert.True(memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortColorMap, colorMap));

        Assert.True(memory.TryWriteWord(0x800, 0x0123));
        Assert.True(memory.TryWriteWord(0x802, 0x0ABC));
        var load = new M68kCpuState { A = { [0] = viewPort, [1] = 0x800 }, D = { [0] = 2 } };
        load.Cycles = 123;
        Assert.True(adapter.TryInvoke(load, (int)GraphicsLvo.LoadRGB4));
        Assert.Equal(0x123u, core.GetRGB4(colorMap, 0));
        Assert.Equal(0xABCu, core.GetRGB4(colorMap, 1));
        Assert.Equal(viewPort, display.LoadedViewPort);
        Assert.Equal((short)2, display.LoadedCount);
        Assert.Equal(123, display.LoadedCycle);

        var set = new M68kCpuState { A = { [0] = viewPort }, D = { [0] = 2, [1] = 0x0F, [2] = 0x01, [3] = 0x08 } };
        set.Cycles = 234;
        Assert.True(adapter.TryInvoke(set, (int)GraphicsLvo.SetRGB4));
        Assert.Equal(0xF18u, core.GetRGB4(colorMap, 2));
        Assert.Equal((short)2, display.SetIndex);
        Assert.Equal(234, display.SetCycle);

        Assert.True(memory.TryWriteLong(0x900, (1u << 16) | 3u));
        Assert.True(memory.TryWriteLong(0x904, 0xFFFFFFFF));
        Assert.True(memory.TryWriteLong(0x908, 0x80000000));
        Assert.True(memory.TryWriteLong(0x90C, 0));
        Assert.True(memory.TryWriteLong(0x910, 0));
        var load32 = new M68kCpuState { A = { [0] = viewPort, [1] = 0x900 } };
        load32.Cycles = 345;
        Assert.True(adapter.TryInvoke(load32, (int)GraphicsLvo.LoadRGB32));
        Assert.Equal(0xF80u, core.GetRGB4(colorMap, 3));
        Assert.Equal(345, display.SetCycle);

        var set32 = new M68kCpuState { A = { [0] = colorMap }, D = { [0] = 1, [1] = 0x12000000, [2] = 0x34000000, [3] = 0x56000000 } };
        set32.Cycles = 456;
        Assert.True(adapter.TryInvoke(set32, (int)GraphicsLvo.SetRGB32CM));
        var get32 = new M68kCpuState { A = { [0] = colorMap, [1] = 0x980 }, D = { [0] = 1, [1] = 1 } };
        Assert.True(adapter.TryInvoke(get32, (int)GraphicsLvo.GetRGB32));
        Assert.Equal(0x1212_1212u, ReadLong(memory, 0x980));
        Assert.Equal(0x3434_3434u, ReadLong(memory, 0x984));
        Assert.Equal(0x5656_5656u, ReadLong(memory, 0x988));
    }

    [Fact]
    public void SetRgbRejectsAMalformedViewportColorMapBeforePublishingPalette()
    {
        var memory = new FakeMemory(0x10000);
        var display = new FakeDisplay();
        var core = new GraphicsLibraryCore(memory, new BitmapAllocator(), new FakeBlitter(), display);
        const uint viewPort = 0x200;

        Assert.True(memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortColorMap, 0xDEAD));
        Assert.False(core.SetRGB4(viewPort, 1, 0x0F, 0x02, 0x01));
        Assert.False(core.SetRGB32(viewPort, 1, 0xFF00_0000, 0x2200_0000, 0x1100_0000));
        Assert.Equal(0u, display.LoadedViewPort);
        Assert.Equal((short)0, display.SetIndex);
    }

    [Fact]
    public void ColorMapPensShareExactColorsAndReleaseOwnership()
    {
        var memory = new FakeMemory(0x10000);
        var core = new GraphicsLibraryCore(memory, new BitmapAllocator(), new FakeBlitter(), new FakeDisplay());
        var colorMap = core.GetColorMap(3);
        const uint red = 0xFF000000;
        const uint green = 0xFF000000;

        var shared = core.ObtainPen(colorMap, uint.MaxValue, red, 0, 0, 0);
        Assert.Equal(0, shared);
        Assert.Equal(0xF00u, core.GetRGB4(colorMap, shared));
        Assert.Equal(shared, core.ObtainPen(colorMap, uint.MaxValue, red, 0, 0, 0));
        Assert.Equal(-1, core.ObtainPen(colorMap, 0, green, 0, 0, 1));
        Assert.True(core.ReleasePen(colorMap, (uint)shared));
        Assert.True(core.ReleasePen(colorMap, (uint)shared));
        Assert.True(core.ReleasePen(colorMap, uint.MaxValue));
        Assert.False(core.ReleasePen(colorMap, (uint)shared));
        Assert.Equal(shared, core.ObtainBestPenA(colorMap, red, 0, 0, 0));
        Assert.True(core.ReleasePen(colorMap, (uint)shared));

        var exclusive = core.ObtainPen(colorMap, 1, 0, green, 0, 1);
        Assert.Equal(1, exclusive);
        Assert.Equal(0x0F0u, core.GetRGB4(colorMap, exclusive));
        Assert.Equal(-1, core.ObtainPen(colorMap, 1, 0, 0, red, 1));
        Assert.Equal(1, core.FindColor(colorMap, 0, 0xFF000000, 0, -1));
        Assert.True(core.ReleasePen(colorMap, (uint)exclusive));
    }

    [Fact]
    public void ObtainBestPenHonorsPrecisionToleranceAndFailIfBad()
    {
        var memory = new FakeMemory(0x10000);
        var core = new GraphicsLibraryCore(memory, new BitmapAllocator(), new FakeBlitter(), new FakeDisplay());
        var colorMap = core.GetColorMap(2);
        var black = core.ObtainPen(colorMap, uint.MaxValue, 0, 0, 0, 0);
        var white = core.ObtainPen(colorMap, uint.MaxValue, 0xFFFF_FFFF, 0xFFFF_FFFF, 0xFFFF_FFFF, 0);
        Assert.Equal(0, black);
        Assert.Equal(1, white);

        const uint tags = 0xA00;
        WriteTag(memory, tags, 0x8400_0000, unchecked((uint)GraphicsColorOperations.PrecisionExact));
        WriteTag(memory, tags + 8, 0x8400_0001, 1);
        WriteTag(memory, tags + 16, 0, 0);
        Assert.Equal(-1, core.ObtainBestPenA(colorMap, 0x2000_0000, 0, 0, tags));

        WriteTag(memory, tags, 0x8400_0000, GraphicsColorOperations.PrecisionIcon);
        Assert.Equal(black, core.ObtainBestPenA(colorMap, 0x2000_0000, 0, 0, tags));
        Assert.True(core.ReleasePen(colorMap, (uint)black));

        WriteTag(memory, tags, 0x8400_0000, GraphicsColorOperations.PrecisionGui);
        Assert.Equal(-1, core.ObtainBestPenA(colorMap, 0x2000_0000, 0, 0, tags));

        WriteTag(memory, tags + 8, 0x8400_0001, 0);
        Assert.Equal(black, core.ObtainBestPenA(colorMap, 0x2000_0000, 0, 0, tags));
        Assert.True(core.ReleasePen(colorMap, (uint)black));
        Assert.True(core.ReleasePen(colorMap, (uint)white));
    }

    [Fact]
    public void ObtainBestPenRegisterVectorReadsPrecisionTagsAndAllocatesFreePen()
    {
        var memory = new FakeMemory(0x10000);
        var core = new GraphicsLibraryCore(memory, new BitmapAllocator(), new FakeBlitter(), new FakeDisplay());
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        var colorMap = InvokeGetColorMap(adapter, 2);
        var black = core.ObtainPen(colorMap, uint.MaxValue, 0, 0, 0, 0);
        Assert.Equal(0, black);

        const uint tags = 0xA00;
        WriteTag(memory, tags, 0x8400_0000, GraphicsColorOperations.PrecisionGui);
        WriteTag(memory, tags + 8, 0x8400_0001, 1);
        WriteTag(memory, tags + 16, 0, 0);
        var state = new M68kCpuState
        {
            A = { [0] = colorMap, [1] = tags },
            D = { [1] = 0xFFFF_0000, [2] = 0, [3] = 0 }
        };

        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.ObtainBestPenA));
        Assert.Equal(1u, state.D[0]);
        Assert.Equal(0xF00u, core.GetRGB4(colorMap, 1));
        Assert.True(core.ReleasePen(colorMap, 0));
        Assert.True(core.ReleasePen(colorMap, 1));
    }

    [Fact]
    public void VideoControlAttachesViewportAndProcessesPaletteFlags()
    {
        var memory = new FakeMemory(0x10000);
        var core = new GraphicsLibraryCore(memory, new BitmapAllocator(), new FakeBlitter(), new FakeDisplay());
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        var colorMap = InvokeGetColorMap(adapter, 2);
        const uint viewPort = 0x300;
        const uint tags = 0xA00;
        Assert.True(memory.TryWriteLong(tags, 0x8000_000B)); // VTAG_ATTACH_CM_SET
        Assert.True(memory.TryWriteLong(tags + 4, viewPort));
        Assert.True(memory.TryWriteLong(tags + 8, 0x8000_0005)); // VTAG_BORDERBLANK_SET
        Assert.True(memory.TryWriteLong(tags + 12, 1));
        Assert.True(memory.TryWriteLong(tags + 16, 0));
        Assert.True(memory.TryWriteLong(tags + 20, 0));

        var set = new M68kCpuState { A = { [0] = colorMap, [1] = tags } };
        Assert.True(adapter.TryInvoke(set, (int)GraphicsLvo.VideoControl));
        Assert.Equal(0u, set.D[0]);
        Assert.Equal(viewPort, ReadLong(memory, colorMap + (uint)GraphicsLayouts.ColorMapViewPort));
        Assert.Equal(colorMap, ReadLong(memory, viewPort + (uint)GraphicsLayouts.ViewPortColorMap));
        Assert.NotEqual((byte)0, ReadByte(memory, colorMap + (uint)GraphicsLayouts.ColorMapFlags));

        Assert.True(memory.TryWriteLong(tags, 0x8000_0017)); // VTAG_BORDERBLANK_GET
        Assert.True(memory.TryWriteLong(tags + 4, 0xB00));
        Assert.True(memory.TryWriteLong(tags + 8, 0x8000_001B)); // VTAG_ATTACH_CM_GET
        Assert.True(memory.TryWriteLong(tags + 12, 0xB04));
        Assert.True(memory.TryWriteLong(tags + 16, 0));
        Assert.True(memory.TryWriteLong(tags + 20, 0));
        Assert.True(adapter.TryInvoke(set, (int)GraphicsLvo.VideoControl));
        Assert.Equal(1u, ReadLong(memory, 0xB00));
        Assert.Equal(viewPort, ReadLong(memory, 0xB04));
    }

    [Fact]
    public void VideoControlAssociatesViewportExtraAndNativeDisplayHandles()
    {
        var memory = new FakeMemory(0x10000);
        var core = new GraphicsLibraryCore(memory, new BitmapAllocator(), new FakeBlitter(), new FakeDisplay());
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        var colorMap = InvokeGetColorMap(adapter, 2);
        const uint viewPort = 0x300;
        const uint tags = 0xA00;

        var newExtra = new M68kCpuState { D = { [0] = GraphicsExtendedNodeOperations.ViewPortExtraType } };
        Assert.True(adapter.TryInvoke(newExtra, (int)GraphicsLvo.GfxNew));
        var viewPortExtra = newExtra.D[0];
        Assert.NotEqual(0u, viewPortExtra);

        WriteTag(memory, tags + 0, 0x8000_000B, viewPort); // VTAG_ATTACH_CM_SET
        WriteTag(memory, tags + 8, 0x8000_0014, viewPortExtra); // VTAG_VIEWPORTEXTRA_SET
        WriteTag(memory, tags + 16, 0x8000_0010, GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresMode); // VTAG_NORMAL_DISP_SET
        WriteTag(memory, tags + 24, 0x8000_0012, GraphicsModeIds.NtscMonitor); // VTAG_COERCE_DISP_SET
        WriteTag(memory, tags + 32, 0, 0);

        var set = new M68kCpuState { A = { [0] = colorMap, [1] = tags } };
        Assert.True(adapter.TryInvoke(set, (int)GraphicsLvo.VideoControl));
        Assert.Equal(0u, set.D[0]);
        Assert.Equal(viewPortExtra, ReadLong(memory, colorMap + (uint)GraphicsLayouts.ColorMapViewPortExtra));
        Assert.Equal(viewPort, ReadLong(memory, viewPortExtra + (uint)GraphicsLayouts.ViewPortExtraViewPort));
        Assert.Equal(
            GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresMode,
            ReadLong(memory, colorMap + (uint)GraphicsLayouts.ColorMapNormalDisplayInfo));
        Assert.Equal(
            GraphicsModeIds.NtscMonitor,
            ReadLong(memory, colorMap + (uint)GraphicsLayouts.ColorMapCoerceDisplayInfo));

        WriteTag(memory, tags + 0, 0x8000_0013, 0xB00); // VTAG_VIEWPORTEXTRA_GET
        WriteTag(memory, tags + 8, 0x8000_000F, 0xB04); // VTAG_NORMAL_DISP_GET
        WriteTag(memory, tags + 16, 0x8000_0011, 0xB08); // VTAG_COERCE_DISP_GET
        WriteTag(memory, tags + 24, 0, 0);
        Assert.True(adapter.TryInvoke(set, (int)GraphicsLvo.VideoControl));
        Assert.Equal(viewPortExtra, ReadLong(memory, 0xB00));
        Assert.Equal(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresMode, ReadLong(memory, 0xB04));
        Assert.Equal(GraphicsModeIds.NtscMonitor, ReadLong(memory, 0xB08));

        var newView = new M68kCpuState { D = { [0] = GraphicsExtendedNodeOperations.ViewExtraType } };
        Assert.True(adapter.TryInvoke(newView, (int)GraphicsLvo.GfxNew));
        WriteTag(memory, tags, 0x8000_0014, newView.D[0]); // ViewExtra is not a ViewPortExtra.
        WriteTag(memory, tags + 8, 0, 0);
        Assert.True(adapter.TryInvoke(set, (int)GraphicsLvo.VideoControl));
        Assert.Equal(1u, set.D[0]);

        WriteTag(memory, tags, 0x8000_0010, 0xDEAD_BEEFu); // foreign DisplayInfoHandle
        WriteTag(memory, tags + 8, 0, 0);
        Assert.True(adapter.TryInvoke(set, (int)GraphicsLvo.VideoControl));
        Assert.Equal(1u, set.D[0]);
        Assert.Equal(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresMode,
            ReadLong(memory, colorMap + (uint)GraphicsLayouts.ColorMapNormalDisplayInfo));
    }

    private static uint InvokeGetColorMap(CopperStartGraphicsRegisterAdapter adapter, uint entries)
    {
        var state = new M68kCpuState { D = { [0] = entries } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.GetColorMap));
        return state.D[0];
    }

    [Fact]
    public void CoreStoresOnlyExplicitPortableCapabilityBoundaries()
    {
        var memory = new FakeMemory(4);
        var allocator = new FakeAllocator();
        var blitter = new FakeBlitter();
        var display = new FakeDisplay();
        var core = new GraphicsLibraryCore(memory, allocator, blitter, display);

        Assert.Same(memory, core.Memory);
        Assert.Same(allocator, core.Allocator);
        Assert.Same(blitter, core.Blitter);
        Assert.Same(display, core.Display);
    }

    [Fact]
    public void BlitterOwnershipVectorsStayBehindTheExplicitBackendBoundary()
    {
        var memory = new FakeMemory(0x100);
        var blitter = new FakeBlitter();
        var core = new GraphicsLibraryCore(memory, new FakeAllocator(), blitter, new FakeDisplay());
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        var state = new M68kCpuState();

        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.OwnBlitter));
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.WaitBlit));
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.DisownBlitter));
        Assert.Equal(1, blitter.OwnCount);
        Assert.Equal(1, blitter.WaitCount);
        Assert.Equal(1, blitter.DisownCount);
    }

    [Fact]
    public void QBlitAndQBSBlitValidateClassicNodeAndPreserveQueueClass()
    {
        var memory = new FakeMemory(0x100);
        var blitter = new FakeBlitter();
        var core = new GraphicsLibraryCore(memory, new FakeAllocator(), blitter, new FakeDisplay());
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint node = 0x20;

        Assert.True(memory.TryWriteLong(node + (uint)GraphicsLayouts.BltNodeFunction, 0x4000));
        Assert.True(memory.TryWriteByte(node + (uint)GraphicsLayouts.BltNodeStatus, 0x40));
        Assert.True(memory.TryWriteWord(node + (uint)GraphicsLayouts.BltNodeBlitSize, 0x1234));
        Assert.True(memory.TryWriteWord(node + (uint)GraphicsLayouts.BltNodeBeamSync, 0xFF80));
        Assert.True(memory.TryWriteLong(node + (uint)GraphicsLayouts.BltNodeCleanup, 0x5000));

        var state = new M68kCpuState();
        state.A[1] = node;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.QBlit));
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.QBSBlit));
        Assert.Equal(2, blitter.Queued.Count);
        Assert.Equal((node, false, (short)-128), blitter.Queued[0]);
        Assert.Equal((node, true, (short)-128), blitter.Queued[1]);
    }

    [Fact]
    public void QBlitRejectsMalformedNodeWithoutSubmittingToBackend()
    {
        var memory = new FakeMemory(0x40);
        var blitter = new FakeBlitter();
        var core = new GraphicsLibraryCore(memory, new FakeAllocator(), blitter, new FakeDisplay());

        // The function field is present, but the cleanup pointer at 0x0D..0x10
        // falls outside guest memory.
        const uint truncatedNode = 0x30;
        Assert.True(memory.TryWriteLong(truncatedNode + (uint)GraphicsLayouts.BltNodeFunction, 0x4000));
        Assert.True(memory.TryWriteByte(truncatedNode + (uint)GraphicsLayouts.BltNodeStatus, 0));
        Assert.True(memory.TryWriteWord(truncatedNode + (uint)GraphicsLayouts.BltNodeBeamSync, 0));

        Assert.False(core.QBlit(truncatedNode));
        Assert.Empty(blitter.Queued);
    }

    [Fact]
    public void RastPortAndViewInitializationClearGuestStateAndSetDocumentedDefaults()
    {
        var memory = new FakeMemory(0x400);
        var core = CreateCore(memory);
        for (var offset = 0; offset < GraphicsLayouts.RastPortMinimumSize; offset++)
            memory.TryWriteByte(0x20u + (uint)offset, 0xCC);

        Assert.True(core.InitializeRastPort(0x20));
        Assert.Equal((byte)0xFF, ReadByte(memory, 0x20u + (uint)GraphicsLayouts.RastPortMask));
        Assert.Equal(1, core.GetAPen(0x20));
        Assert.Equal(0, core.GetBPen(0x20));
        Assert.Equal(1, core.GetDrawMode(0x20));
        Assert.Equal((ushort)0, ReadWord(memory, 0x20u + (uint)GraphicsLayouts.RastPortTextWidth));

        Assert.True(core.InitializeView(0x180));
        Assert.True(core.InitializeViewPort(0x1A0));
        Assert.Equal(0u, ReadLong(memory, 0x180u + (uint)GraphicsLayouts.ViewLoFCprList));
        Assert.Equal(0u, ReadLong(memory, 0x1A0u + (uint)GraphicsLayouts.ViewPortRasInfo));
        Assert.Equal((byte)0x24, ReadByte(memory, 0x1A0u + (uint)GraphicsLayouts.ViewPortSpritePriorities));
        Assert.Equal((byte)0, ReadByte(memory, 0x1A0u + (uint)GraphicsLayouts.ViewPortExtendedModes));

        // Structure initialization probes the full guest envelope before
        // clearing it; a truncated tail must not be partially overwritten.
        for (var offset = 0; offset < 4; offset++)
            memory.TryWriteByte(0x3FCu + (uint)offset, 0xA5);
        Assert.False(core.InitializeRastPort(0x3FC));
        Assert.Equal((byte)0xA5, ReadByte(memory, 0x3FC));
        Assert.False(core.InitializeView(0x3FC));
        Assert.Equal((byte)0xA5, ReadByte(memory, 0x3FC));
    }

    [Fact]
    public void HostRegisterAdapterClaimsInitVPortAndPreservesItsVoidAbi()
    {
        var memory = new FakeMemory(0x400);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint viewPort = 0x1A0;
        for (var offset = 0; offset < GraphicsLayouts.ViewPortSize; offset++)
            Assert.True(memory.TryWriteByte(viewPort + (uint)offset, 0xCC));

        var state = new M68kCpuState
        {
            A = { [0] = viewPort },
            D = { [0] = 0xDEAD_BEEFu }
        };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.InitVPort));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal((byte)0x24, ReadByte(memory, viewPort + (uint)GraphicsLayouts.ViewPortSpritePriorities));
        Assert.Equal(0u, ReadLong(memory, viewPort + (uint)GraphicsLayouts.ViewPortRasInfo));
        Assert.Equal((byte)0, ReadByte(memory, viewPort + (uint)GraphicsLayouts.ViewPortExtendedModes));
    }

    [Fact]
    public void RastPortPenAndModeSettersFailAtomicallyOnTruncatedPatternState()
    {
        // FgPen is in range, but linpatcnt at 0x1E is outside this guest
        // allocation.  The setter must not publish a new pen before the
        // associated line-pattern state can be updated.
        var memory = new FakeMemory(0x1E);
        Assert.True(memory.TryWriteByte((uint)GraphicsLayouts.RastPortFgPen, 3));
        var core = CreateCore(memory);

        Assert.Equal(-1, core.SetAPen(0, 7));
        Assert.Equal((byte)3, ReadByte(memory, (uint)GraphicsLayouts.RastPortFgPen));

        Assert.True(memory.TryWriteByte((uint)GraphicsLayouts.RastPortBgPen, 4));
        Assert.True(memory.TryWriteByte((uint)GraphicsLayouts.RastPortDrawMode, 1));
        Assert.Equal(-1, core.SetABPenDrMd(0, 7, 8, 2));
        Assert.Equal((byte)3, ReadByte(memory, (uint)GraphicsLayouts.RastPortFgPen));
        Assert.Equal((byte)4, ReadByte(memory, (uint)GraphicsLayouts.RastPortBgPen));
        Assert.Equal((byte)1, ReadByte(memory, (uint)GraphicsLayouts.RastPortDrawMode));
    }

    [Fact]
    public void RasInfoInitializationClearsTheGuestChainNodeAndRejectsPartialRanges()
    {
        var memory = new FakeMemory(0x200);
        var core = CreateCore(memory);
        const uint rasInfo = 0x100;

        for (var offset = 0; offset < GraphicsLayouts.RasInfoSize; offset++)
            Assert.True(memory.TryWriteByte(rasInfo + (uint)offset, 0xA5));

        Assert.True(core.InitializeRasInfo(rasInfo));
        for (var offset = 0; offset < GraphicsLayouts.RasInfoSize; offset++)
            Assert.Equal((byte)0, ReadByte(memory, rasInfo + (uint)offset));

        const uint malformed = 0x1F8;
        Assert.True(memory.TryWriteByte(malformed, 0x5A));
        Assert.False(core.InitializeRasInfo(malformed));
        Assert.Equal((byte)0x5A, ReadByte(memory, malformed));
    }

    [Fact]
    public void TmpRasInitializationPublishesGuestBufferAndSizeOnlyAfterValidation()
    {
        var memory = new FakeMemory(0x400);
        var core = CreateCore(memory);
        const uint tmpRas = 0x100;
        const uint buffer = 0x180;

        for (var offset = 0; offset < GraphicsLayouts.TmpRasSize; offset++)
            memory.TryWriteByte(tmpRas + (uint)offset, 0xA5);
        Assert.True(core.InitializeTmpRas(tmpRas, buffer, 0x20));
        Assert.Equal(buffer, ReadLong(memory, tmpRas + (uint)GraphicsLayouts.TmpRasRasPtr));
        Assert.Equal(0x20u, ReadLong(memory, tmpRas + (uint)GraphicsLayouts.TmpRasByteCount));

        Assert.False(core.InitializeTmpRas(tmpRas, 0x3F0, 0x20));
        Assert.Equal(buffer, ReadLong(memory, tmpRas + (uint)GraphicsLayouts.TmpRasRasPtr));
        Assert.Equal(0x20u, ReadLong(memory, tmpRas + (uint)GraphicsLayouts.TmpRasByteCount));
        Assert.False(core.InitializeTmpRas(tmpRas, buffer, 0));
        Assert.False(core.InitializeTmpRas(0x3FC, buffer, 0x20));
    }

    [Fact]
    public void InitBitMapSetsGeometryAndClearsReusedPlanes()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint bitMap = 0x1200;
        Assert.True(memory.TryWriteLong(bitMap + (uint)GraphicsLayouts.BitMapPlanes, 0x1800));
        Assert.True(core.InitializeBitMap(bitMap, 3, 17, 9));
        Assert.Equal((ushort)4, ReadWord(memory, bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow));
        Assert.Equal((ushort)9, ReadWord(memory, bitMap + (uint)GraphicsLayouts.BitMapRows));
        Assert.Equal((byte)3, ReadByte(memory, bitMap + (uint)GraphicsLayouts.BitMapDepth));
        for (var plane = 0; plane < 8; plane++)
        {
            Assert.Equal(
                0u,
                ReadLong(memory, bitMap + (uint)GraphicsLayouts.BitMapPlanes + (uint)(plane * 4)));
        }
        Assert.False(core.InitializeBitMap(0x1FF0, 1, 16, 1));
    }

    [Fact]
    public void AreaInfoInitializesFiveByteVectorStorageAndClosesPreviousPolygon()
    {
        var memory = new FakeMemory(0x1000);
        var core = CreateCore(memory);
        const uint rastPort = 0x20;
        const uint areaInfo = 0x100;
        const uint buffer = 0x200;

        Assert.True(core.InitializeRastPort(rastPort));
        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortAreaInfo, areaInfo));
        Assert.True(core.InitializeArea(areaInfo, buffer, 4));
        Assert.Equal(buffer, ReadLong(memory, areaInfo + (uint)GraphicsLayouts.AreaInfoVectorTable));
        Assert.Equal(buffer, ReadLong(memory, areaInfo + (uint)GraphicsLayouts.AreaInfoVectorPointer));
        Assert.Equal(buffer + 16, ReadLong(memory, areaInfo + (uint)GraphicsLayouts.AreaInfoFlagTable));
        Assert.Equal(buffer + 16, ReadLong(memory, areaInfo + (uint)GraphicsLayouts.AreaInfoFlagPointer));
        Assert.Equal((ushort)0, ReadWord(memory, areaInfo + (uint)GraphicsLayouts.AreaInfoCount));
        Assert.Equal((ushort)4, ReadWord(memory, areaInfo + (uint)GraphicsLayouts.AreaInfoMaxCount));

        Assert.Equal(0, core.AreaMove(rastPort, 1, 2));
        Assert.Equal((ushort)1, ReadWord(memory, areaInfo + (uint)GraphicsLayouts.AreaInfoCount));
        Assert.Equal((ushort)1, ReadWord(memory, buffer));
        Assert.Equal((ushort)2, ReadWord(memory, buffer + 2));
        Assert.Equal((byte)1, ReadByte(memory, buffer + 16));

        Assert.Equal(0, core.AreaDraw(rastPort, 4, 5));
        Assert.Equal((ushort)2, ReadWord(memory, areaInfo + (uint)GraphicsLayouts.AreaInfoCount));
        Assert.Equal((ushort)4, ReadWord(memory, buffer + 4));
        Assert.Equal((ushort)5, ReadWord(memory, buffer + 6));
        Assert.Equal((byte)0, ReadByte(memory, buffer + 17));

        // AreaMove closes (1,2), then starts the next polygon at (8,9).
        Assert.Equal(0, core.AreaMove(rastPort, 8, 9));
        Assert.Equal((ushort)4, ReadWord(memory, areaInfo + (uint)GraphicsLayouts.AreaInfoCount));
        Assert.Equal((ushort)1, ReadWord(memory, buffer + 8));
        Assert.Equal((ushort)2, ReadWord(memory, buffer + 10));
        Assert.Equal((byte)0, ReadByte(memory, buffer + 18));
        Assert.Equal((ushort)8, ReadWord(memory, buffer + 12));
        Assert.Equal((ushort)9, ReadWord(memory, buffer + 14));
        Assert.Equal((byte)1, ReadByte(memory, buffer + 19));
        Assert.Equal(-1, core.AreaDraw(rastPort, 10, 11));

        Assert.False(core.InitializeArea(areaInfo, buffer + 1, 4));
        Assert.False(core.InitializeArea(areaInfo, buffer, -1));
        Assert.False(core.InitializeArea(0xFF0, buffer, 1));
    }

    [Fact]
    public void InitAreaRejectsTruncatedCallerVectorStorageBeforePublishingPointers()
    {
        var memory = new FakeMemory(0x300);
        var core = CreateCore(memory);
        const uint areaInfo = 0x100;
        const uint buffer = 0x2FE;

        for (var offset = 0; offset < GraphicsLayouts.AreaInfoSize; offset++)
            Assert.True(memory.TryWriteByte(areaInfo + (uint)offset, 0xA5));

        // One vector needs four coordinate bytes plus one flag byte; the
        // caller-provided buffer crosses the end of the guest envelope.
        Assert.False(core.InitializeArea(areaInfo, buffer, 1));
        Assert.Equal(
            0xA5u << 24 | 0xA5u << 16 | 0xA5u << 8 | 0xA5u,
            ReadLong(memory, areaInfo + (uint)GraphicsLayouts.AreaInfoVectorTable));
    }

    [Fact]
    public void AreaEllipseConsumesTwoVectorsAndAllowsAFollowingPolygonStart()
    {
        var memory = new FakeMemory(0x1000);
        var core = CreateCore(memory);
        const uint rastPort = 0x20;
        const uint areaInfo = 0x100;
        const uint buffer = 0x200;

        Assert.True(core.InitializeRastPort(rastPort));
        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortAreaInfo, areaInfo));
        Assert.True(core.InitializeArea(areaInfo, buffer, 3));

        Assert.Equal(0, core.AreaEllipse(rastPort, 10, 11, 4, 5));
        Assert.Equal((ushort)2, ReadWord(memory, areaInfo + (uint)GraphicsLayouts.AreaInfoCount));
        Assert.Equal((ushort)10, ReadWord(memory, buffer));
        Assert.Equal((ushort)11, ReadWord(memory, buffer + 2));
        Assert.Equal((byte)2, ReadByte(memory, buffer + 12));
        Assert.Equal((ushort)4, ReadWord(memory, buffer + 4));
        Assert.Equal((ushort)5, ReadWord(memory, buffer + 6));
        Assert.Equal((byte)3, ReadByte(memory, buffer + 13));

        // An ellipse is already a closed area item, so AreaMove only consumes
        // one additional vector for the next polygon's first point.
        Assert.Equal(0, core.AreaMove(rastPort, 20, 21));
        Assert.Equal((ushort)3, ReadWord(memory, areaInfo + (uint)GraphicsLayouts.AreaInfoCount));
        Assert.Equal((ushort)20, ReadWord(memory, buffer + 8));
        Assert.Equal((ushort)21, ReadWord(memory, buffer + 10));
        Assert.Equal((byte)1, ReadByte(memory, buffer + 14));
        Assert.Equal(-1, core.AreaEllipse(rastPort, 0, 0, 1, 1));
        Assert.Equal(-1, core.AreaEllipse(rastPort, 0, 0, 0, 1));
    }

    [Fact]
    public void AreaEndFillsRecordedPolygonAndResetsTheAreaCollector()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint rastPort = 0x20;
        const uint areaInfo = 0x300;
        const uint buffer = 0x700;

        Assert.True(core.InitializeRastPort(rastPort));
        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortBitMap, 0x100));
        Assert.Equal(0, core.SetAPen(rastPort, 3));
        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortAreaInfo, areaInfo));
        Assert.True(core.InitializeArea(areaInfo, buffer, 8));
        Assert.Equal(0, core.AreaMove(rastPort, 1, 1));
        Assert.Equal(0, core.AreaDraw(rastPort, 6, 1));
        Assert.Equal(0, core.AreaDraw(rastPort, 6, 6));
        Assert.Equal(0, core.AreaDraw(rastPort, 1, 6));
        Assert.True(GraphicsRasterOperations.TryReadBitmap(memory, rastPort, out _));
        Assert.Equal(3, core.GetAPen(rastPort));
        Assert.Equal(1, core.GetDrawMode(rastPort));

        Assert.Equal(0, core.AreaEnd(rastPort));
        Assert.Equal(3, core.ReadPixel(rastPort, 3, 3));
        Assert.Equal(0, core.ReadPixel(rastPort, 0, 0));
        Assert.Equal((ushort)0, ReadWord(memory, areaInfo + (uint)GraphicsLayouts.AreaInfoCount));
        Assert.Equal(buffer, ReadLong(memory, areaInfo + (uint)GraphicsLayouts.AreaInfoVectorPointer));
        Assert.Equal(buffer + 32, ReadLong(memory, areaInfo + (uint)GraphicsLayouts.AreaInfoFlagPointer));
        Assert.Equal(-1, core.AreaEnd(rastPort));
    }

    [Fact]
    public void AreaEndFillsRecordedEllipseUsingTheSamePortablePlanarPath()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint rastPort = 0x20;
        const uint areaInfo = 0x300;
        const uint buffer = 0x700;

        Assert.True(core.InitializeRastPort(rastPort));
        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortBitMap, 0x100));
        Assert.Equal(0, core.SetAPen(rastPort, 3));
        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortAreaInfo, areaInfo));
        Assert.True(core.InitializeArea(areaInfo, buffer, 2));
        Assert.Equal(0, core.AreaEllipse(rastPort, 8, 4, 3, 2));

        Assert.Equal(0, core.AreaEnd(rastPort));
        Assert.Equal(3, core.ReadPixel(rastPort, 8, 4));
        Assert.Equal(0, core.ReadPixel(rastPort, 1, 4));
        Assert.Equal((ushort)0, ReadWord(memory, areaInfo + (uint)GraphicsLayouts.AreaInfoCount));
    }

    [Fact]
    public void AreaEndUsesTheRastPortAreaPatternAcrossPolygonScanlines()
    {
        var memory = new FakeMemory(0x4000);
        var core = CreateCore(memory);
        const uint rastPort = 0x20;
        const uint areaInfo = 0x300;
        const uint buffer = 0x700;
        const uint areaPattern = 0x800;
        const uint plane0 = 0x400;

        Assert.True(core.InitializeRastPort(rastPort));
        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortBitMap, 0x100));
        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortAreaInfo, areaInfo));
        Assert.True(core.InitializeArea(areaInfo, buffer, 8));
        Assert.Equal(0, core.SetAPen(rastPort, 1));
        Assert.Equal(0, core.SetBPen(rastPort, 0));
        Assert.Equal(0, core.SetDrawMode(rastPort, 1));
        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortAreaPtrn, areaPattern));
        Assert.True(memory.TryWriteByte(rastPort + (uint)GraphicsLayouts.RastPortAreaPtSz, 1));
        Assert.True(memory.TryWriteWord(areaPattern, 0xAAAA));
        Assert.True(memory.TryWriteWord(areaPattern + 2, 0x5555));

        Assert.Equal(0, core.AreaMove(rastPort, 0, 0));
        Assert.Equal(0, core.AreaDraw(rastPort, 7, 0));
        Assert.Equal(0, core.AreaDraw(rastPort, 7, 2));
        Assert.Equal(0, core.AreaDraw(rastPort, 0, 2));
        Assert.Equal(0, core.AreaEnd(rastPort));

        Assert.Equal((byte)0xAA, ReadByte(memory, plane0));
        Assert.Equal((byte)0x54, ReadByte(memory, plane0 + 2));
        Assert.Equal((byte)0, ReadByte(memory, plane0 + 4));
    }

    [Fact]
    public void AreaEndPreflightsShapeDestinationBeforePublishingPlanarWrites()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint rastPort = 0x20;
        const uint areaInfo = 0x300;
        const uint buffer = 0x700;
        const uint bitMap = 0x100;
        const uint plane = 0x1FFE;

        Assert.True(core.InitializeRastPort(rastPort));
        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortBitMap, bitMap));
        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortAreaInfo, areaInfo));
        Assert.True(memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 2));
        Assert.True(memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapRows, 2));
        Assert.True(memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, 1));
        Assert.True(memory.TryWriteLong(bitMap + (uint)GraphicsLayouts.BitMapPlanes, plane));
        Assert.True(memory.TryWriteByte(plane, 0x5A));
        Assert.True(core.InitializeArea(areaInfo, buffer, 8));

        Assert.Equal(0, core.AreaMove(rastPort, 0, 0));
        Assert.Equal(0, core.AreaDraw(rastPort, 1, 0));
        Assert.Equal(0, core.AreaDraw(rastPort, 1, 1));
        Assert.Equal(0, core.AreaDraw(rastPort, 0, 1));

        // The first scanline is addressable, while the second scanline starts
        // at 0x2000.  AreaEnd must reject before exposing the first row.
        Assert.Equal(-1, core.AreaEnd(rastPort));
        Assert.Equal((byte)0x5A, ReadByte(memory, plane));
        Assert.Equal((ushort)4, ReadWord(memory, areaInfo + (uint)GraphicsLayouts.AreaInfoCount));
    }

    [Fact]
    public void AllocRasterRoundsRowsToWordsAndFreeRasterMirrorsTheByteCount()
    {
        var memory = new FakeMemory(0x100);
        var allocator = new FakeAllocator();
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), new FakeDisplay());

        Assert.Equal(0x1000u, core.AllocRaster(17, 3));
        Assert.Equal(12u, allocator.LastAllocatedBytes);
        Assert.Equal(GraphicsMemoryClass.Chip, allocator.LastAllocatedClass);

        Assert.Equal(0, core.FreeRaster(0x1000, 17, 3));
        Assert.Equal(0x1000u, allocator.LastFreedAddress);
        Assert.Equal(12u, allocator.LastFreedBytes);
        Assert.Equal(GraphicsMemoryClass.Chip, allocator.LastFreedClass);

        Assert.Equal(0u, core.AllocRaster(uint.MaxValue, uint.MaxValue));
        Assert.Equal(-1, core.FreeRaster(0, 17, 3));
    }

    [Fact]
    public void GetBitMapAttrReportsAlignedGeometryFlagsAndUnknownAsZero()
    {
        var memory = new FakeMemory(0x400);
        var core = CreateCore(memory);
        const uint bitMap = 0x180;

        memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 4);
        memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapRows, 9);
        memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapFlags, 0x1A);
        memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, 3);

        Assert.Equal(9u, core.GetBitMapAttr(bitMap, 0));
        Assert.Equal(3u, core.GetBitMapAttr(bitMap, 4));
        Assert.Equal(32u, core.GetBitMapAttr(bitMap, 8));
        Assert.Equal(0x1Au, core.GetBitMapAttr(bitMap, 12));
        Assert.Equal(0u, core.GetBitMapAttr(bitMap, 99));
        Assert.Equal(0u, core.GetBitMapAttr(0, 8));
    }

    [Fact]
    public void BltBitMapCopiesPlanarPixelsAndPreservesOverlapWithMinterms()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint source = 0x100;
        const uint destination = 0x180;
        const uint sourcePlane0 = 0x400;
        const uint sourcePlane1 = 0x500;
        const uint destinationPlane0 = 0x600;
        const uint destinationPlane1 = 0x700;

        WritePlanarBitmap(memory, source, sourcePlane0, sourcePlane1, rows: 4);
        WritePlanarBitmap(memory, destination, destinationPlane0, destinationPlane1, rows: 4);
        memory.TryWriteByte(sourcePlane0, 0xA5);
        memory.TryWriteByte(sourcePlane1, 0x3C);
        memory.TryWriteByte(destinationPlane0, 0);
        memory.TryWriteByte(destinationPlane1, 0);

        Assert.Equal(2, core.BltBitMap(source, 0, 0, destination, 0, 0, 8, 1, 0xC0, 0xFF, 0));
        Assert.Equal((byte)0xA5, ReadByte(memory, destinationPlane0));
        Assert.Equal((byte)0x3C, ReadByte(memory, destinationPlane1));

        memory.TryWriteByte(destinationPlane0, 0xFF);
        memory.TryWriteByte(destinationPlane1, 0xFF);
        Assert.Equal(2, core.BltBitMap(source, 0, 0, destination, 0, 0, 8, 1, 0x30, 0xFF, 0));
        Assert.Equal(unchecked((byte)~0xA5), ReadByte(memory, destinationPlane0));
        Assert.Equal(unchecked((byte)~0x3C), ReadByte(memory, destinationPlane1));

        memory.TryWriteByte(sourcePlane0, 0x80);
        memory.TryWriteByte(sourcePlane1, 0);
        Assert.Equal(2, core.BltBitMap(source, 0, 0, source, 1, 0, 7, 1, 0xC0, 0xFF, 0));
        Assert.Equal((byte)0xC0, ReadByte(memory, sourcePlane0));
    }

    [Fact]
    public void BltBitMapSupportsDifferentDepthsAndClipsNegativeOrigins()
    {
        var memory = new FakeMemory(0x4000);
        var core = CreateCore(memory);
        const uint source = 0x100;
        const uint destination = 0x180;
        const uint sourcePlane = 0x400;
        const uint destinationPlane0 = 0x600;
        const uint destinationPlane1 = 0x700;

        WritePlanarBitmap(memory, source, sourcePlane, 0, rows: 2, depth: 1);
        WritePlanarBitmap(memory, destination, destinationPlane0, destinationPlane1, rows: 2, depth: 2);
        memory.TryWriteByte(sourcePlane, 0xF0);
        memory.TryWriteByte(destinationPlane0, 0);
        memory.TryWriteByte(destinationPlane1, 0xAA);

        // Only plane zero participates when the source and destination depths differ.
        Assert.Equal(1, core.BltBitMap(source, 0, 0, destination, 0, 0, 8, 1, 0xC0, 0xFF, 0));
        Assert.Equal((byte)0xF0, ReadByte(memory, destinationPlane0));
        Assert.Equal((byte)0xAA, ReadByte(memory, destinationPlane1));

        memory.TryWriteByte(destinationPlane0, 0);
        // Source X=-2 clips the six-pixel intersection into destination X=2.
        Assert.Equal(1, core.BltBitMap(source, -2, 0, destination, 0, 0, 8, 1, 0xC0, 0x01, 0));
        Assert.Equal((byte)0x3C, ReadByte(memory, destinationPlane0));

        memory.TryWriteByte(destinationPlane0, 0x5A);
        Assert.Equal(0, core.BltBitMap(source, 16, 0, destination, 0, 0, 1, 1, 0xC0, 0x01, 0));
        Assert.Equal((byte)0x5A, ReadByte(memory, destinationPlane0));
    }

    [Fact]
    public void BltClearSupportsByteCountRowsAndV36FillWords()
    {
        var memory = new FakeMemory(0x1000);
        var core = CreateCore(memory);
        for (var index = 0u; index < 12; index++)
            memory.TryWriteByte(0x200 + index, 0xCC);

        Assert.Equal(0, core.BltClear(0x200, 4, 0));
        Assert.Equal((byte)0, ReadByte(memory, 0x200));
        Assert.Equal((byte)0, ReadByte(memory, 0x203));
        Assert.Equal((byte)0xCC, ReadByte(memory, 0x204));

        // Rows mode encodes rows in the upper half and bytes-per-row below.
        Assert.Equal(0, core.BltClear(0x204, (2u << 16) | 2u, 0x2));
        Assert.Equal((byte)0, ReadByte(memory, 0x204));
        Assert.Equal((byte)0, ReadByte(memory, 0x205));
        Assert.Equal((byte)0, ReadByte(memory, 0x206));
        Assert.Equal((byte)0, ReadByte(memory, 0x207));

        for (var index = 0u; index < 4; index++)
            memory.TryWriteByte(0x220 + index, 0);
        Assert.Equal(0, core.BltClear(0x220, 4, (uint)(0xA55Au << 16) | 0x4));
        Assert.Equal((byte)0xA5, ReadByte(memory, 0x220));
        Assert.Equal((byte)0x5A, ReadByte(memory, 0x221));
        Assert.Equal((byte)0xA5, ReadByte(memory, 0x222));
        Assert.Equal((byte)0x5A, ReadByte(memory, 0x223));

        Assert.Equal(-1, core.BltClear(0x200, 3, 0));
        // V36 fill-word mode composes with rows mode: the low/high halves of
        // byteCount still select the row geometry, while Flags' high word is
        // repeated into each row.
        Assert.Equal(0, core.BltClear(0x200, (2u << 16) | 2u, (uint)(0x0002u << 16) | 0x6));
        Assert.Equal((byte)0x00, ReadByte(memory, 0x200));
        Assert.Equal((byte)0x02, ReadByte(memory, 0x201));
        Assert.Equal((byte)0x00, ReadByte(memory, 0x202));
        Assert.Equal((byte)0x02, ReadByte(memory, 0x203));
        Assert.Equal(-1, core.BltClear(0x201, 2, 0));
    }

    [Fact]
    public void ClipBlitUsesRastPortBitMapsAndRejectsLayeredPorts()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint sourceRastPort = 0x20;
        const uint destinationRastPort = 0xB0;
        const uint source = 0x300;
        const uint destination = 0x380;
        const uint sourcePlane = 0x500;
        const uint destinationPlane = 0x600;

        WritePlanarBitmap(memory, source, sourcePlane, 0, rows: 2, depth: 1);
        WritePlanarBitmap(memory, destination, destinationPlane, 0, rows: 2, depth: 1);
        memory.TryWriteByte(sourcePlane, 0xF0);
        memory.TryWriteLong(sourceRastPort + (uint)GraphicsLayouts.RastPortBitMap, source);
        memory.TryWriteLong(destinationRastPort + (uint)GraphicsLayouts.RastPortBitMap, destination);

        Assert.Equal(1, core.ClipBlit(sourceRastPort, 0, 0, destinationRastPort, 0, 0, 8, 1, 0xC0));
        Assert.Equal((byte)0xF0, ReadByte(memory, destinationPlane));

        memory.TryWriteLong(destinationRastPort + (uint)GraphicsLayouts.RastPortLayer, 0x1234);
        Assert.Equal(-1, core.ClipBlit(sourceRastPort, 0, 0, destinationRastPort, 0, 0, 8, 1, 0xC0));
    }

    [Fact]
    public void BltBitMapRastPortCopiesIntoTheGuestRastPortAndRejectsLayers()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint source = 0x300;
        const uint destination = 0x380;
        const uint rastPort = 0x20;
        const uint sourcePlane = 0x500;
        const uint destinationPlane = 0x600;

        WritePlanarBitmap(memory, source, sourcePlane, 0, rows: 2, depth: 1);
        WritePlanarBitmap(memory, destination, destinationPlane, 0, rows: 2, depth: 1);
        memory.TryWriteByte(sourcePlane, 0xF0);
        memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortBitMap, destination);

        Assert.Equal(0, core.BltBitMapRastPort(source, 0, 0, rastPort, 0, 0, 8, 1, 0xC0));
        Assert.Equal((byte)0xF0, ReadByte(memory, destinationPlane));

        memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortLayer, 0x1234);
        Assert.Equal(-1, core.BltBitMapRastPort(source, 0, 0, rastPort, 0, 0, 8, 1, 0xC0));
    }

    [Fact]
    public void BltMaskBitMapRastPortAppliesMaskAndMintermWithoutTouchingMaskedPixels()
    {
        var memory = new FakeMemory(0x4000);
        var core = CreateCore(memory);
        const uint source = 0x300;
        const uint destination = 0x380;
        const uint rastPort = 0x20;
        const uint sourcePlane = 0x500;
        const uint destinationPlane = 0x600;
        const uint blitMask = 0x800;

        WritePlanarBitmap(memory, source, sourcePlane, 0, rows: 2, depth: 1);
        WritePlanarBitmap(memory, destination, destinationPlane, 0, rows: 2, depth: 1);
        memory.TryWriteByte(sourcePlane, 0xF0);
        memory.TryWriteByte(destinationPlane, 0xAA);
        memory.TryWriteByte(blitMask, 0xCC);
        memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortBitMap, destination);

        Assert.Equal(
            0,
            core.BltMaskBitMapRastPort(
                source,
                0,
                0,
                rastPort,
                0,
                0,
                8,
                1,
                0xC0,
                blitMask));
        Assert.Equal((byte)0xE2, ReadByte(memory, destinationPlane));

        memory.TryWriteByte(destinationPlane, 0);
        Assert.Equal(
            0,
            core.BltMaskBitMapRastPort(
                source,
                0,
                0,
                rastPort,
                0,
                0,
                8,
                1,
                0x30,
                blitMask));
        Assert.Equal((byte)0x0C, ReadByte(memory, destinationPlane));

        memory.TryWriteByte(blitMask, 0xFF);
        memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortLayer, 0x1234);
        Assert.Equal(
            -1,
            core.BltMaskBitMapRastPort(
                source,
                0,
                0,
                rastPort,
                0,
                0,
                8,
                1,
                0xC0,
                blitMask));
    }

    [Fact]
    public void BltPatternUsesAreaPatternAndContiguousStencilWithClipping()
    {
        var memory = new FakeMemory(0x4000);
        var core = CreateCore(memory);
        const uint rastPort = 0x20;
        const uint bitMap = 0x300;
        const uint plane = 0x500;
        const uint areaPattern = 0x800;
        const uint stencil = 0x900;

        WritePlanarBitmap(memory, bitMap, plane, 0, rows: 2, depth: 1);
        memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortBitMap, bitMap);
        memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortAreaPtrn, areaPattern);
        memory.TryWriteByte(rastPort + (uint)GraphicsLayouts.RastPortAreaPtSz, 1);
        memory.TryWriteByte(rastPort + (uint)GraphicsLayouts.RastPortFgPen, 1);
        memory.TryWriteByte(rastPort + (uint)GraphicsLayouts.RastPortBgPen, 0);
        memory.TryWriteByte(rastPort + (uint)GraphicsLayouts.RastPortDrawMode, 1);
        memory.TryWriteWord(areaPattern, 0xAAAA);
        memory.TryWriteWord(areaPattern + 2, 0x5555);
        memory.TryWriteByte(stencil, 0xF0);
        memory.TryWriteByte(stencil + 2, 0x0F);

        Assert.Equal(0, core.BltPattern(rastPort, stencil, 0, 0, 7, 1, 2));
        Assert.Equal((byte)0xA0, ReadByte(memory, plane));
        Assert.Equal((byte)0x05, ReadByte(memory, plane + 2));

        memory.TryWriteByte(plane, 0);
        Assert.Equal(0, core.RectFill(rastPort, 0, 0, 7, 0));
        Assert.Equal((byte)0xAA, ReadByte(memory, plane));

        memory.TryWriteByte(plane, 0);
        memory.TryWriteByte(plane + 2, 0);
        Assert.Equal(0, core.BltPattern(rastPort, 0, -2, 0, 7, 0, 0));
        Assert.Equal((byte)0xAA, ReadByte(memory, plane));

        memory.TryWriteByte(rastPort + (uint)GraphicsLayouts.RastPortAreaPtSz, 0);
        memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortAreaPtrn, 0);
        memory.TryWriteByte(rastPort + (uint)GraphicsLayouts.RastPortDrawMode, 0);
        Assert.Equal(0, core.BltPattern(rastPort, 0, 0, 0, 7, 0, 0));
        Assert.Equal((byte)0xFF, ReadByte(memory, plane));

        memory.TryWriteByte(rastPort + (uint)GraphicsLayouts.RastPortDrawMode, 1);
        memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortAreaPtrn, areaPattern);
        memory.TryWriteByte(rastPort + (uint)GraphicsLayouts.RastPortAreaPtSz, unchecked((byte)-1));
        const uint multicolorBitmap = 0xA00;
        const uint multicolorPlane0 = 0xC00;
        const uint multicolorPlane1 = 0xD00;
        WritePlanarBitmap(memory, multicolorBitmap, multicolorPlane0, multicolorPlane1, rows: 2, depth: 2);
        memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortBitMap, multicolorBitmap);
        memory.TryWriteWord(areaPattern, 0xFFFF);
        memory.TryWriteWord(areaPattern + 2, 0x0000);
        memory.TryWriteWord(areaPattern + 4, 0x0000);
        memory.TryWriteWord(areaPattern + 6, 0xFFFF);
        Assert.Equal(0, core.BltPattern(rastPort, 0, 0, 0, 7, 1, 0));
        Assert.Equal((byte)0xFF, ReadByte(memory, multicolorPlane0));
        Assert.Equal((byte)0x00, ReadByte(memory, multicolorPlane0 + 2));
        Assert.Equal((byte)0x00, ReadByte(memory, multicolorPlane1));
        Assert.Equal((byte)0xFF, ReadByte(memory, multicolorPlane1 + 2));

        Assert.Equal(-1, core.BltPattern(rastPort, stencil, 0, 0, 7, 0, 3));
    }

    [Fact]
    public void HostRegisterAdapterRoutesMaskedAndPatternBlits()
    {
        var memory = new FakeMemory(0x4000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint source = 0x300;
        const uint destination = 0x380;
        const uint rastPort = 0x20;
        const uint sourcePlane = 0x500;
        const uint destinationPlane = 0x600;
        const uint blitMask = 0x800;

        WritePlanarBitmap(memory, source, sourcePlane, 0, rows: 1, depth: 1);
        WritePlanarBitmap(memory, destination, destinationPlane, 0, rows: 1, depth: 1);
        memory.TryWriteByte(sourcePlane, 0xC0);
        memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortBitMap, destination);
        memory.TryWriteByte(blitMask, 0xC0);

        var state = new M68kCpuState();
        state.A[0] = source;
        state.A[1] = rastPort;
        state.A[2] = blitMask;
        state.D[4] = 8;
        state.D[5] = 1;
        state.D[6] = 0xC0;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.BltMaskBitMapRastPort));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal((byte)0xC0, ReadByte(memory, destinationPlane));

        state.A[0] = 0;
        state.A[1] = rastPort;
        state.D[0] = 0;
        state.D[1] = 0;
        state.D[2] = 7;
        state.D[3] = 0;
        state.D[4] = 0;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.BltPattern));
        Assert.Equal(0u, state.D[0]);
    }

    [Fact]
    public void HostRegisterAdapterRoutesInitTmpRas()
    {
        var memory = new FakeMemory(0x1000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        var state = new M68kCpuState();
        state.A[0] = 0x100;
        state.A[1] = 0x200;
        state.D[0] = 0x20;

        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.InitTmpRas));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal(0x200u, ReadLong(memory, 0x100));
        Assert.Equal(0x20u, ReadLong(memory, 0x104));
    }

    [Fact]
    public void BltTemplateUsesBitOffsetDrawModeAndRastPortClipping()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint rastPort = 0x20;
        const uint bitMap = 0x300;
        const uint plane = 0x500;
        const uint template = 0x700;

        WritePlanarBitmap(memory, bitMap, plane, 0, rows: 2, depth: 1);
        memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortBitMap, bitMap);
        memory.TryWriteByte(template, 0xA0);
        memory.TryWriteByte(template + 1, 0x50);

        Assert.Equal(0, core.BltTemplate(template, 0, 1, rastPort, 0, 0, 8, 2));
        Assert.Equal((byte)0xA0, ReadByte(memory, plane));
        Assert.Equal((byte)0x50, ReadByte(memory, plane + 2));

        memory.TryWriteByte(plane, 0);
        memory.TryWriteByte(plane + 2, 0);
        Assert.Equal(0, core.BltTemplate(template, 0, 1, rastPort, -2, 0, 8, 1));
        Assert.Equal((byte)0x80, ReadByte(memory, plane));

        memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortLayer, 0x1234);
        Assert.Equal(-1, core.BltTemplate(template, 0, 1, rastPort, 0, 0, 8, 1));
    }

    [Fact]
    public void BltTemplatePreflightsEveryDestinationRowBeforePublishingWrites()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint rastPort = 0x20;
        const uint bitMap = 0x300;
        const uint plane = 0x1FFE;
        const uint template = 0x700;

        // The first row is addressable, but the second row starts beyond the
        // guest memory boundary.  Both template rows select a pixel so a
        // non-atomic implementation would publish the first row before
        // discovering the late destination failure.
        WritePlanarBitmap(memory, bitMap, plane, 0, rows: 2, depth: 1);
        memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortBitMap, bitMap);
        memory.TryWriteByte(plane, 0x5A);
        memory.TryWriteByte(template, 0x80);
        memory.TryWriteByte(template + 1, 0x80);

        Assert.Equal(-1, core.BltTemplate(template, 0, 1, rastPort, 0, 0, 1, 2));
        Assert.Equal((byte)0x5A, ReadByte(memory, plane));
    }

    [Fact]
    public void HostRegisterAdapterRoutesBltTemplate()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint rastPort = 0x20;
        const uint bitMap = 0x300;
        const uint plane = 0x500;
        const uint template = 0x700;

        WritePlanarBitmap(memory, bitMap, plane, 0, rows: 1, depth: 1);
        memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortBitMap, bitMap);
        memory.TryWriteByte(template, 0xC0);
        var state = new M68kCpuState();
        state.A[0] = template;
        state.A[1] = rastPort;
        state.D[0] = 0;
        state.D[1] = 1;
        state.D[2] = 0;
        state.D[3] = 0;
        state.D[4] = 8;
        state.D[5] = 1;

        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.BltTemplate));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal((byte)0xC0, ReadByte(memory, plane));
    }

    [Fact]
    public void HostRegisterAdapterRoutesBltBitMapAndReturnsPlaneCount()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint source = 0x100;
        const uint destination = 0x180;
        const uint sourcePlane = 0x400;
        const uint destinationPlane = 0x600;

        WritePlanarBitmap(memory, source, sourcePlane, 0, rows: 1, depth: 1);
        WritePlanarBitmap(memory, destination, destinationPlane, 0, rows: 1, depth: 1);
        memory.TryWriteByte(sourcePlane, 0xA5);
        memory.TryWriteByte(destinationPlane, 0);

        var state = new M68kCpuState();
        state.A[0] = source;
        state.A[1] = destination;
        state.D[0] = 0;
        state.D[1] = 0;
        state.D[2] = 0;
        state.D[3] = 0;
        state.D[4] = 8;
        state.D[5] = 1;
        state.D[6] = 0xC0;
        state.D[7] = 0x01;

        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.BltBitMap));
        Assert.Equal(1u, state.D[0]);
        Assert.Equal((byte)0xA5, ReadByte(memory, destinationPlane));
    }

    [Fact]
    public void HostRegisterAdapterRoutesBltClearWithClassicRegisters()
    {
        var memory = new FakeMemory(0x1000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        for (var index = 0u; index < 4; index++)
            memory.TryWriteByte(0x220 + index, 0xCC);

        var state = new M68kCpuState();
        state.A[1] = 0x220;
        state.D[0] = 4;
        state.D[1] = 0;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.BltClear));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal((byte)0, ReadByte(memory, 0x220));
        Assert.Equal((byte)0, ReadByte(memory, 0x223));
    }

    [Fact]
    public void AllocBitMapBuildsPlanarGeometryClearsInterleavedPlanesAndFreesAsOneBlock()
    {
        var memory = new FakeMemory(0x20_000);
        var allocator = new BitmapAllocator();
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), new FakeDisplay());
        const uint bmfClear = 1u << 0;
        const uint bmfInterleaved = 1u << 2;

        var bitMap = core.AllocBitMap(17, 3, 2, bmfClear | bmfInterleaved, 0);
        Assert.NotEqual(0u, bitMap);
        Assert.Equal((ushort)4, ReadWord(memory, bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow));
        Assert.Equal((ushort)3, ReadWord(memory, bitMap + (uint)GraphicsLayouts.BitMapRows));
        Assert.Equal((byte)2, ReadByte(memory, bitMap + (uint)GraphicsLayouts.BitMapDepth));
        var flags = ReadByte(memory, bitMap + (uint)GraphicsLayouts.BitMapFlags);
        Assert.Equal((byte)0x0D, flags); // CLEAR | INTERLEAVED | STANDARD

        var plane0 = ReadLong(memory, bitMap + (uint)GraphicsLayouts.BitMapPlanes);
        var plane1 = ReadLong(memory, bitMap + (uint)GraphicsLayouts.BitMapPlanes + 4);
        Assert.Equal(12u, plane1 - plane0);
        for (var offset = 0u; offset < 24; offset++)
            Assert.Equal((byte)0, ReadByte(memory, plane0 + offset));

        Assert.Equal(0, core.FreeBitMap(bitMap));
        Assert.Contains(allocator.Freed, allocation => allocation.Address == plane0 && allocation.Bytes == 24);
        Assert.Contains(allocator.Freed, allocation => allocation.Address == bitMap && allocation.Bytes == GraphicsLayouts.BitMapSize);
    }

    [Fact]
    public void AllocBitMapFallsBackToSeparatePlanesWhenInterleavedBlockIsUnavailable()
    {
        var memory = new FakeMemory(0x20_000);
        var allocator = new InterleavedFallbackAllocator(planeBytesToReject: 24);
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), new FakeDisplay());
        const uint bmfClear = 1u << 0;
        const uint bmfInterleaved = 1u << 2;

        var bitMap = core.AllocBitMap(17, 3, 2, bmfClear | bmfInterleaved, 0);
        Assert.NotEqual(0u, bitMap);
        Assert.Equal((byte)0x09, ReadByte(memory, bitMap + (uint)GraphicsLayouts.BitMapFlags));

        var plane0 = ReadLong(memory, bitMap + (uint)GraphicsLayouts.BitMapPlanes);
        var plane1 = ReadLong(memory, bitMap + (uint)GraphicsLayouts.BitMapPlanes + 4);
        Assert.NotEqual(plane0, plane1);
        for (var offset = 0u; offset < 12; offset++)
        {
            Assert.Equal((byte)0, ReadByte(memory, plane0 + offset));
            Assert.Equal((byte)0, ReadByte(memory, plane1 + offset));
        }

        Assert.Equal(0, core.FreeBitMap(bitMap));
        Assert.Contains(allocator.Freed, allocation => allocation.Address == plane0 && allocation.Bytes == 12);
        Assert.Contains(allocator.Freed, allocation => allocation.Address == plane1 && allocation.Bytes == 12);
    }

    [Fact]
    public void AllocBitMapClearsDisplayableWhenChipPlanesAreNotWordAligned()
    {
        var memory = new FakeMemory(0x20_000);
        var allocator = new SteppingAllocator(0x1001);
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), new FakeDisplay());
        const uint bmfDisplayable = 1u << 1;

        var bitMap = core.AllocBitMap(17, 3, 2, bmfDisplayable, 0);
        Assert.NotEqual(0u, bitMap);
        Assert.Equal((byte)0x08, ReadByte(memory, bitMap + (uint)GraphicsLayouts.BitMapFlags));

        var plane0 = ReadLong(memory, bitMap + (uint)GraphicsLayouts.BitMapPlanes);
        var plane1 = ReadLong(memory, bitMap + (uint)GraphicsLayouts.BitMapPlanes + 4);
        Assert.Equal(1u, plane0 & 1u);
        Assert.Equal(1u, plane1 & 1u);
        Assert.Equal(0u, core.GetBitMapAttr(bitMap, 12) & bmfDisplayable);
        Assert.Equal(0, core.FreeBitMap(bitMap));
    }

    [Fact]
    public void AllocBitMapUsesReadableFriendStrideAndRejectsMalformedFriend()
    {
        var memory = new FakeMemory(0x4000);
        var allocator = new BitmapAllocator();
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), new FakeDisplay());
        const uint friend = 0x200;

        // The requested 17-pixel raster needs only four bytes per row, but a
        // standard friend with an eight-byte stride should be blit-compatible
        // without narrowing the destination allocation.
        Assert.True(memory.TryWriteWord(friend + (uint)GraphicsLayouts.BitMapBytesPerRow, 8));
        Assert.True(memory.TryWriteWord(friend + (uint)GraphicsLayouts.BitMapRows, 2));
        Assert.True(memory.TryWriteByte(friend + (uint)GraphicsLayouts.BitMapDepth, 1));
        var bitMap = core.AllocBitMap(17, 2, 2, 0, friend);
        Assert.NotEqual(0u, bitMap);
        Assert.Equal((ushort)8, ReadWord(memory, bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow));

        var plane0 = ReadLong(memory, bitMap + (uint)GraphicsLayouts.BitMapPlanes);
        var plane1 = ReadLong(memory, bitMap + (uint)GraphicsLayouts.BitMapPlanes + 4);
        Assert.Equal(16u, plane1 - plane0);
        Assert.Equal(0, core.FreeBitMap(bitMap));
        Assert.Contains(allocator.Freed, allocation => allocation.Address == plane0 && allocation.Bytes == 16);

        var malformedMemory = new FakeMemory(0x4000);
        var malformedAllocator = new BitmapAllocator();
        var malformedCore = new GraphicsLibraryCore(
            malformedMemory,
            malformedAllocator,
            new FakeBlitter(),
            new FakeDisplay());
        Assert.Equal(0u, malformedCore.AllocBitMap(17, 2, 2, 0, 0x3FFF));
        Assert.Empty(malformedAllocator.Freed);
    }

    [Fact]
    public void BitmapLifecycleVectorsUseClassicDAndARegisterAbi()
    {
        var memory = new FakeMemory(0x20_000);
        var allocator = new BitmapAllocator();
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), new FakeDisplay());
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint bmfClear = 1u << 0;

        var state = new M68kCpuState
        {
            A = { [0] = 0 },
            D = { [0] = 17, [1] = 3, [2] = 2, [3] = bmfClear }
        };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AllocBitMap));
        var bitMap = state.D[0];
        Assert.NotEqual(0u, bitMap);

        state.A[0] = bitMap;
        state.D[0] = 8; // BMA_WIDTH
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.GetBitMapAttr));
        Assert.Equal(32u, state.D[0]);

        state.A[0] = bitMap;
        state.D[0] = 0xFFFF_FFFF;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.FreeBitMap));
        Assert.Equal(0u, state.D[0]);
        Assert.Contains(allocator.Freed, allocation => allocation.Address == bitMap);
    }

    [Fact]
    public void AllocBitMapRollsBackAlreadyAllocatedPlanesOnFailure()
    {
        var memory = new FakeMemory(0x20_000);
        var allocator = new BitmapAllocator { FailAfterAllocations = 2 };
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), new FakeDisplay());

        Assert.Equal(0u, core.AllocBitMap(32, 4, 2, 0, 0));
        Assert.Equal(2, allocator.Freed.Count); // one plane and the BitMap structure
    }

    [Fact]
    public void FreeBitMapRejectsAnInterleavedSpanThatOverflowsTheGuestAllocatorRange()
    {
        var memory = new FakeMemory(0x400);
        var allocator = new BitmapAllocator();
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), new FakeDisplay());
        const uint bitMap = 0x100;

        Assert.True(memory.TryWriteWord(
            bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow,
            ushort.MaxValue));
        Assert.True(memory.TryWriteWord(
            bitMap + (uint)GraphicsLayouts.BitMapRows,
            ushort.MaxValue));
        Assert.True(memory.TryWriteByte(
            bitMap + (uint)GraphicsLayouts.BitMapFlags,
            (byte)(1u << 2))); // BMF_INTERLEAVED
        Assert.True(memory.TryWriteByte(
            bitMap + (uint)GraphicsLayouts.BitMapDepth,
            8));

        Assert.Equal(-1, core.FreeBitMap(bitMap));
        Assert.Empty(allocator.Freed);
    }

    [Fact]
    public void ChangeViewPortBitMapUpdatesOnlyTheGuestRasInfoAssociation()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint viewPort = 0x180;
        const uint rasInfo = 0x1C0;
        const uint firstBitMap = 0x1200;
        const uint nextBitMap = 0x1400;

        memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, firstBitMap);
        memory.TryWriteWord(firstBitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 4);
        memory.TryWriteByte(firstBitMap + (uint)GraphicsLayouts.BitMapDepth, 2);
        memory.TryWriteWord(nextBitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 4);
        memory.TryWriteByte(nextBitMap + (uint)GraphicsLayouts.BitMapDepth, 2);

        Assert.Equal(0, core.ChangeViewPortBitMap(viewPort, nextBitMap));
        Assert.Equal(nextBitMap, ReadLong(memory, rasInfo + (uint)GraphicsLayouts.RasInfoBitMap));
        memory.TryWriteWord(nextBitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 2);
        Assert.Equal(-1, core.ChangeViewPortBitMap(viewPort, firstBitMap));
        Assert.Equal(nextBitMap, ReadLong(memory, rasInfo + (uint)GraphicsLayouts.RasInfoBitMap));
        Assert.Equal(-1, core.ChangeViewPortBitMap(viewPort, 0));
        Assert.Equal(-1, core.ChangeViewPortBitMap(0, nextBitMap));
    }

    [Fact]
    public void ChangeViewPortBitMapValidatesDbufInfoBeforeChangingTheGuestAssociation()
    {
        var memory = new FakeMemory(0x2400);
        var display = new FakeDisplay();
        var core = CreateCore(memory, display);
        const uint viewPort = 0x180;
        const uint rasInfo = 0x1C0;
        const uint firstBitMap = 0x1200;
        const uint nextBitMap = 0x1400;
        const uint dbufInfo = 0x1800;

        memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, firstBitMap);
        memory.TryWriteWord(firstBitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 4);
        memory.TryWriteByte(firstBitMap + (uint)GraphicsLayouts.BitMapDepth, 2);
        memory.TryWriteWord(nextBitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 4);
        memory.TryWriteByte(nextBitMap + (uint)GraphicsLayouts.BitMapDepth, 2);
        for (var offset = 0; offset < GraphicsLayouts.DBufInfoSize; offset++)
            memory.TryWriteByte(dbufInfo + (uint)offset, 0xA5);

        Assert.Equal(0, core.ChangeViewPortBitMap(viewPort, nextBitMap, dbufInfo));
        Assert.Equal(nextBitMap, ReadLong(memory, rasInfo + (uint)GraphicsLayouts.RasInfoBitMap));
        Assert.Equal(viewPort, display.ChangedViewPort);
        Assert.Equal(nextBitMap, display.ChangedBitMap);
        Assert.Equal(dbufInfo, display.ChangedDBufInfo);
        Assert.Equal(1, display.ChangeCount);

        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, firstBitMap);
        Assert.Equal(-1, core.ChangeViewPortBitMap(viewPort, nextBitMap, dbufInfo + 1));
        Assert.Equal(firstBitMap, ReadLong(memory, rasInfo + (uint)GraphicsLayouts.RasInfoBitMap));
        Assert.Equal(1, display.ChangeCount);
    }

    [Fact]
    public void ChangeViewPortBitMapPublishesPreviousBitmapToTheDoubleBufferBoundary()
    {
        var memory = new FakeMemory(0x2400);
        var display = new FakeDisplay();
        var core = CreateCore(memory, display);
        const uint viewPort = 0x180;
        const uint rasInfo = 0x1C0;
        const uint firstBitMap = 0x1200;
        const uint nextBitMap = 0x1400;
        const uint dbufInfo = 0x1800;

        memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, firstBitMap);
        memory.TryWriteWord(firstBitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 4);
        memory.TryWriteByte(firstBitMap + (uint)GraphicsLayouts.BitMapDepth, 2);
        memory.TryWriteWord(nextBitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 4);
        memory.TryWriteByte(nextBitMap + (uint)GraphicsLayouts.BitMapDepth, 2);

        Assert.Equal(1234, core.ChangeViewPortBitMap(viewPort, nextBitMap, dbufInfo, 1234));
        Assert.Equal(1, display.DoubleBufferScheduleCount);
        Assert.Equal(viewPort, display.ScheduledViewPort);
        Assert.Equal(firstBitMap, display.ScheduledPreviousBitMap);
        Assert.Equal(nextBitMap, display.ScheduledBitMap);
        Assert.Equal(dbufInfo, display.ScheduledDBufInfo);
        Assert.Equal(1234, display.ScheduledCycle);
    }

    [Fact]
    public void PlanarPixelsLinesAndRectanglesUseRastPortState()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);

        Assert.Equal(0, core.Move(0x20, 1, 1));
        Assert.Equal(0, core.Draw(0x20, 1, 4));
        Assert.Equal(3, core.ReadPixel(0x20, 1, 1));
        Assert.Equal(3, core.ReadPixel(0x20, 1, 4));
        Assert.Equal((short)1, unchecked((short)ReadWord(memory, 0x20u + (uint)GraphicsLayouts.RastPortCurrentX)));
        Assert.Equal((short)4, unchecked((short)ReadWord(memory, 0x20u + (uint)GraphicsLayouts.RastPortCurrentY)));

        Assert.Equal(0, core.SetAPen(0x20, 2));
        Assert.Equal(0, core.RectFill(0x20, 2, 2, 3, 3));
        Assert.Equal(2, core.ReadPixel(0x20, 2, 2));
        Assert.Equal(2, core.ReadPixel(0x20, 3, 3));
    }

    [Fact]
    public void SetRastSetsTheRequestedPenEvenWhenComplementModeIsActive()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);

        Assert.Equal(0, core.SetRast(0x20, 3));
        Assert.Equal(3, core.ReadPixel(0x20, 0, 0));

        // SetRast initializes the complete planar raster; it is not a
        // draw-mode operation.  COMPLEMENT must therefore not toggle the
        // existing value when the requested pen is written.
        Assert.Equal(0, core.SetDrawMode(0x20, 2));
        Assert.Equal(0, core.SetRast(0x20, 1));
        Assert.Equal(1, core.ReadPixel(0x20, 0, 0));
        Assert.Equal(1, core.ReadPixel(0x20, 15, 7));
    }

    [Fact]
    public void RectFillRejectsInvertedGuestBounds()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);

        Assert.Equal(-1, core.RectFill(0x20, 3, 2, 2, 3));
        Assert.Equal(-1, core.RectFill(0x20, 2, 3, 3, 2));
    }

    [Fact]
    public void ReadPixelRejectsADeclaredBitmapPlaneThatIsMissing()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        Assert.True(memory.TryWriteLong(
            0x100u + (uint)GraphicsLayouts.BitMapPlanes + 4,
            0));

        Assert.Equal(-1, core.ReadPixel(0x20, 0, 0));
    }

    [Fact]
    public void DrawEllipseUsesIntegerSymmetryAndRejectsNonPositiveRadii()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);

        Assert.Equal(0, core.DrawEllipse(0x20, 4, 4, 2, 2));
        Assert.Equal(3, core.ReadPixel(0x20, 4, 2));
        Assert.Equal(3, core.ReadPixel(0x20, 4, 6));
        Assert.Equal(3, core.ReadPixel(0x20, 2, 4));
        Assert.Equal(3, core.ReadPixel(0x20, 6, 4));
        Assert.Equal(-1, core.DrawEllipse(0x20, 4, 4, 0, 2));
    }

    [Fact]
    public void ScrollRasterSnapshotsOverlapAndFillsTheVacatedAreaWithBPen()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        Assert.Equal(0, core.SetAPen(0x20, 1));
        Assert.Equal(0, core.RectFill(0x20, 0, 0, 7, 0));
        Assert.Equal(0, core.SetBPen(0x20, 2));

        Assert.Equal(0, core.ScrollRaster(0x20, 1, 0, 0, 0, 7, 0));
        Assert.Equal(1, core.ReadPixel(0x20, 0, 0));
        Assert.Equal(2, core.ReadPixel(0x20, 7, 0));

        Assert.Equal(0, core.ScrollRaster(0x20, -1, 0, 0, 0, 7, 0));
        Assert.Equal(2, core.ReadPixel(0x20, 0, 0));
        Assert.Equal(1, core.ReadPixel(0x20, 6, 0));

        Assert.Equal(0, core.SetRast(0x20, 1));
        Assert.Equal(0, core.ScrollRasterBF(0x20, 1, 0, 0, 0, 7, 0));
        Assert.Equal(1, core.ReadPixel(0x20, 0, 0));
        Assert.Equal(0, core.ReadPixel(0x20, 7, 0));
        Assert.Equal(-1, core.ScrollRaster(0x20, 1, 0, 3, 0, 2, 0));
    }

    [Fact]
    public void ScrollRasterKeepsLogicalSourceCoordinatesWhenTheRectangleIsClipped()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);

        Assert.Equal(0, core.SetRast(0x20, 0));
        var sourcePens = new byte[] { 1, 2, 3, 0, 1, 3, 2, 3 };
        for (short x = 0; x < sourcePens.Length; x++)
        {
            Assert.Equal(0, core.SetAPen(0x20, sourcePens[x]));
            Assert.Equal(0, core.WritePixel(0x20, x, 0));
        }

        Assert.Equal(0, core.SetBPen(0x20, 0));
        // The logical rectangle starts two pixels outside the bitmap.  A
        // positive delta still samples in that logical coordinate space: the
        // rightmost destination pixel must be vacated, not read from x=6.
        Assert.Equal(0, core.ScrollRaster(0x20, 1, 0, -2, 0, 5, 0));
        Assert.Equal(2, core.ReadPixel(0x20, 0, 0));
        Assert.Equal(3, core.ReadPixel(0x20, 4, 0));
        Assert.Equal(0, core.ReadPixel(0x20, 5, 0));
        Assert.Equal(2, core.ReadPixel(0x20, 6, 0));
        Assert.Equal(3, core.ReadPixel(0x20, 7, 0));
    }

    [Fact]
    public void FloodFillsOutlineBoundedRegionAndSupportsSeedColorMode()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        Assert.Equal(0, core.SetAPen(0x20, 1));
        Assert.Equal(0, core.SetOutlinePen(0x20, 1));
        Assert.Equal(0, core.Move(0x20, 1, 1));
        Assert.Equal(0, core.Draw(0x20, 6, 1));
        Assert.Equal(0, core.Draw(0x20, 6, 6));
        Assert.Equal(0, core.Draw(0x20, 1, 6));
        Assert.Equal(0, core.Draw(0x20, 1, 1));

        Assert.Equal(0, core.SetAPen(0x20, 2));
        Assert.Equal(0, core.Flood(0x20, 0, 3, 3));
        Assert.Equal(2, core.ReadPixel(0x20, 3, 3));
        Assert.Equal(0, core.ReadPixel(0x20, 0, 0));
        Assert.Equal(1, core.ReadPixel(0x20, 1, 1));

        Assert.Equal(0, core.SetAPen(0x20, 3));
        Assert.Equal(0, core.Flood(0x20, 1, 0, 0));
        Assert.Equal(3, core.ReadPixel(0x20, 0, 0));
        Assert.Equal(2, core.ReadPixel(0x20, 3, 3));
    }

    [Fact]
    public void FloodUsesTheRastPortAreaPatternForSeedColorRegions()
    {
        var memory = new FakeMemory(0x4000);
        var core = CreateCore(memory);
        const uint rastPort = 0x20;
        const uint areaPattern = 0x800;
        const uint plane0 = 0x400;

        Assert.True(core.InitializeRastPort(rastPort));
        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortBitMap, 0x100));
        Assert.Equal(0, core.SetAPen(rastPort, 1));
        Assert.Equal(0, core.SetBPen(rastPort, 0));
        Assert.Equal(0, core.SetDrawMode(rastPort, 1));
        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortAreaPtrn, areaPattern));
        Assert.True(memory.TryWriteByte(rastPort + (uint)GraphicsLayouts.RastPortAreaPtSz, 1));
        Assert.True(memory.TryWriteWord(areaPattern, 0xAAAA));
        Assert.True(memory.TryWriteWord(areaPattern + 2, 0x5555));

        Assert.Equal(0, core.Flood(rastPort, 1, 0, 0));
        Assert.Equal((byte)0xAA, ReadByte(memory, plane0));
        Assert.Equal((byte)0x55, ReadByte(memory, plane0 + 2));
        Assert.Equal((byte)0xAA, ReadByte(memory, plane0 + 4));
    }

    [Fact]
    public void FloodPreflightsTheCompleteDestinationBeforePublishingSeedWrites()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint rastPort = 0x20;
        const uint bitMap = 0x100;
        const uint plane = 0x1FFE;

        Assert.True(core.InitializeRastPort(rastPort));
        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortBitMap, bitMap));
        Assert.True(memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 2));
        Assert.True(memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapRows, 2));
        Assert.True(memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, 1));
        Assert.True(memory.TryWriteLong(bitMap + (uint)GraphicsLayouts.BitMapPlanes, plane));
        Assert.True(memory.TryWriteByte(plane, 0x5A));

        // The seed row is readable and would be written first, but the second
        // row falls beyond guest memory.  Flood must fail before changing the
        // seed row or partially traversing the connected region.
        Assert.Equal(-1, core.Flood(rastPort, 1, 0, 0));
        Assert.Equal((byte)0x5A, ReadByte(memory, plane));
    }

    [Fact]
    public void FloodRejectsAnExplicitlyAttachedTemporaryRasterThatIsTooSmall()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint rastPort = 0x20;
        const uint tmpRas = 0x300;
        const uint tmpBuffer = 0x380;
        const uint plane0 = 0x400;

        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortTmpRas, tmpRas));
        Assert.True(memory.TryWriteLong(tmpRas + (uint)GraphicsLayouts.TmpRasRasPtr, tmpBuffer));
        Assert.True(memory.TryWriteLong(tmpRas + (uint)GraphicsLayouts.TmpRasByteCount, 1));
        Assert.Equal(-1, core.Flood(rastPort, 1, 0, 0));
        Assert.Equal((byte)0, ReadByte(memory, plane0));
    }

    [Fact]
    public void RegionsPreserveGuestLinkedLayoutAcrossRectangleAndRegionBooleanOperations()
    {
        var memory = new FakeMemory(0x10000);
        var allocator = new BitmapAllocator();
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), new FakeDisplay());
        const uint rectangle = 0x300;
        const uint rectangle2 = 0x320;

        var region = core.NewRegion();
        Assert.NotEqual(0u, region);
        Assert.Equal(0u, ReadLong(memory, region + (uint)GraphicsLayouts.RegionRectangle));
        WriteRectangle(memory, rectangle, 1, 1, 3, 3);
        Assert.True(core.OrRectRegion(region, rectangle));
        Assert.True(RegionContains(memory, region, 1, 1));
        Assert.True(RegionContains(memory, region, 3, 3));
        Assert.False(RegionContains(memory, region, 0, 0));
        Assert.Equal((short)1, ReadSignedWord(memory, region + (uint)GraphicsLayouts.RegionBounds));
        Assert.Equal((short)3, ReadSignedWord(memory, region + (uint)GraphicsLayouts.RegionBounds + 6));

        WriteRectangle(memory, rectangle2, 3, 2, 5, 4);
        Assert.True(core.OrRectRegion(region, rectangle2));
        Assert.True(RegionContains(memory, region, 5, 4));
        Assert.True(RegionContains(memory, region, 2, 2));

        WriteRectangle(memory, rectangle2, 2, 2, 4, 3);
        Assert.True(core.ClearRectRegion(region, rectangle2));
        Assert.True(RegionContains(memory, region, 1, 1));
        Assert.False(RegionContains(memory, region, 3, 2));
        Assert.True(RegionContains(memory, region, 5, 4));

        WriteRectangle(memory, rectangle2, 1, 1, 2, 1);
        Assert.True(core.XorRectRegion(region, rectangle2));
        Assert.False(RegionContains(memory, region, 1, 1));
        Assert.False(RegionContains(memory, region, 2, 1));

        WriteRectangle(memory, rectangle2, 0, 0, 4, 4);
        Assert.True(core.AndRectRegion(region, rectangle2));
        Assert.False(RegionContains(memory, region, 5, 4));

        var source = core.NewRegion();
        var destination = core.NewRegion();
        WriteRectangle(memory, rectangle, 0, 0, 2, 2);
        WriteRectangle(memory, rectangle2, 1, 1, 3, 3);
        Assert.True(core.OrRectRegion(source, rectangle));
        Assert.True(core.OrRectRegion(destination, rectangle2));
        Assert.True(core.AndRegionRegion(source, destination));
        Assert.True(RegionContains(memory, destination, 1, 1));
        Assert.False(RegionContains(memory, destination, 0, 0));
        Assert.False(RegionContains(memory, destination, 3, 3));

        Assert.True(core.ClearRegion(destination));
        Assert.Equal(0u, ReadLong(memory, destination + (uint)GraphicsLayouts.RegionRectangle));
        Assert.Equal(GraphicsRasterOperations.Success, core.DisposeRegion(region));
        Assert.Equal(GraphicsRasterOperations.Success, core.DisposeRegion(source));
        Assert.Equal(GraphicsRasterOperations.Success, core.DisposeRegion(destination));
        Assert.True(allocator.Freed.Count >= 6);
    }

    [Fact]
    public void HostRegisterAdapterRoutesRegionLifecycleAndBooleanVectors()
    {
        var memory = new FakeMemory(0x10000);
        var allocator = new BitmapAllocator();
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), new FakeDisplay());
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint rectangle = 0x300;
        WriteRectangle(memory, rectangle, 2, 3, 4, 5);

        var state = new M68kCpuState { A = { [1] = rectangle } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.NewRegion));
        var region = state.D[0];
        Assert.NotEqual(0u, region);

        state.A[0] = region;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.OrRectRegion));
        Assert.Equal(1u, state.D[0]);
        Assert.True(RegionContains(memory, region, 3, 4));

        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.ClearRegion));
        Assert.Equal(0u, ReadLong(memory, region + (uint)GraphicsLayouts.RegionRectangle));
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.DisposeRegion));
    }

    [Fact]
    public void PolyDrawConnectsGuestCoordinatePairsAndLeavesTheFinalPenPosition()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint points = 0x300;
        memory.TryWriteWord(points, 1);
        memory.TryWriteWord(points + 2, 1);
        memory.TryWriteWord(points + 4, 1);
        memory.TryWriteWord(points + 6, 4);
        memory.TryWriteWord(points + 8, 4);
        memory.TryWriteWord(points + 10, 4);

        Assert.Equal(0, core.PolyDraw(0x20, 3, points));
        Assert.Equal(3, core.ReadPixel(0x20, 1, 1));
        Assert.Equal(3, core.ReadPixel(0x20, 1, 4));
        Assert.Equal(3, core.ReadPixel(0x20, 4, 4));
        Assert.Equal((short)4, unchecked((short)ReadWord(memory, 0x20u + (uint)GraphicsLayouts.RastPortCurrentX)));
        Assert.Equal((short)4, unchecked((short)ReadWord(memory, 0x20u + (uint)GraphicsLayouts.RastPortCurrentY)));
        Assert.Equal(0, core.PolyDraw(0x20, 0, 0));
        Assert.Equal(-1, core.PolyDraw(0x20, 1, uint.MaxValue));
    }

    [Fact]
    public void DrawHonorsLinePatternForJam1AndJam2Modes()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        Assert.Equal(0, core.SetRast(0x20, 0));
        Assert.Equal(0, core.SetDrawMode(0x20, 0));
        Assert.True(memory.TryWriteWord(0x20u + (uint)GraphicsLayouts.RastPortLinePattern, 0xA000));
        Assert.Equal(0, core.Move(0x20, 0, 0));
        Assert.Equal(0, core.Draw(0x20, 3, 0));
        Assert.Equal(3, core.ReadPixel(0x20, 0, 0));
        Assert.Equal(0, core.ReadPixel(0x20, 1, 0));
        Assert.Equal(3, core.ReadPixel(0x20, 2, 0));
        Assert.Equal(0, core.ReadPixel(0x20, 3, 0));

        Assert.Equal(0, core.SetDrawMode(0x20, 1));
        Assert.Equal(0, core.SetBPen(0x20, 2));
        Assert.True(memory.TryWriteWord(0x20u + (uint)GraphicsLayouts.RastPortLinePattern, 0xA000));
        Assert.Equal(0, core.Move(0x20, 0, 1));
        Assert.Equal(0, core.Draw(0x20, 3, 1));
        Assert.Equal(3, core.ReadPixel(0x20, 0, 1));
        Assert.Equal(2, core.ReadPixel(0x20, 1, 1));
        Assert.Equal(3, core.ReadPixel(0x20, 2, 1));
        Assert.Equal(2, core.ReadPixel(0x20, 3, 1));

        Assert.Equal(0, core.SetRast(0x20, 0));
        Assert.Equal(0, core.SetDrawMode(0x20, 4));
        Assert.True(memory.TryWriteWord(0x20u + (uint)GraphicsLayouts.RastPortLinePattern, 0xA000));
        Assert.Equal(0, core.Move(0x20, 0, 2));
        Assert.Equal(0, core.Draw(0x20, 3, 2));
        Assert.Equal(0, core.ReadPixel(0x20, 0, 2));
        Assert.Equal(3, core.ReadPixel(0x20, 1, 2));
        Assert.Equal(0, core.ReadPixel(0x20, 2, 2));
        Assert.Equal(3, core.ReadPixel(0x20, 3, 2));
    }

    [Fact]
    public void ComplementAreaPatternsToggleOnlyTheSelectedSourceBits()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint rastPort = 0x20;
        const uint areaPattern = 0x800;

        Assert.Equal(0, core.SetRast(rastPort, 0));
        Assert.Equal(0, core.SetAPen(rastPort, 1));
        Assert.Equal(0, core.SetDrawMode(rastPort, 2));
        Assert.True(memory.TryWriteLong(
            rastPort + (uint)GraphicsLayouts.RastPortAreaPtrn,
            areaPattern));
        Assert.True(memory.TryWriteByte(
            rastPort + (uint)GraphicsLayouts.RastPortAreaPtSz,
            0));
        Assert.True(memory.TryWriteWord(areaPattern, 0x8000));

        Assert.Equal(0, core.RectFill(rastPort, 0, 0, 1, 0));
        Assert.Equal(3, core.ReadPixel(rastPort, 0, 0));
        Assert.Equal(0, core.ReadPixel(rastPort, 1, 0));

        Assert.Equal(0, core.SetRast(rastPort, 0));
        Assert.Equal(0, core.SetDrawMode(rastPort, 6));
        Assert.Equal(0, core.RectFill(rastPort, 0, 0, 1, 0));
        Assert.Equal(0, core.ReadPixel(rastPort, 0, 0));
        Assert.Equal(3, core.ReadPixel(rastPort, 1, 0));
    }

    [Fact]
    public void DrawConsumesFirstDotForConnectedComplementLines()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint rastPort = 0x20;

        Assert.Equal(0, core.SetRast(rastPort, 0));
        Assert.Equal(0, core.SetDrawMode(rastPort, 2));
        Assert.True(memory.TryWriteWord(
            rastPort + (uint)GraphicsLayouts.RastPortLinePattern,
            0xFFFF));
        Assert.True(memory.TryWriteWord(
            rastPort + (uint)GraphicsLayouts.RastPortFlags,
            (ushort)GraphicsLayouts.RastPortFirstDot));
        Assert.True(memory.TryWriteByte(
            rastPort + (uint)GraphicsLayouts.RastPortLinePatternCount,
            15));

        var initial0 = core.ReadPixel(rastPort, 0, 0);
        var initial1 = core.ReadPixel(rastPort, 1, 0);
        var initial2 = core.ReadPixel(rastPort, 2, 0);
        Assert.Equal(0, core.Move(rastPort, 0, 0));
        Assert.Equal(0, core.Draw(rastPort, 2, 0));
        Assert.NotEqual(initial0, core.ReadPixel(rastPort, 0, 0));
        Assert.NotEqual(initial1, core.ReadPixel(rastPort, 1, 0));
        var firstEnd = core.ReadPixel(rastPort, 2, 0);
        Assert.NotEqual(initial2, firstEnd);
        Assert.Equal((ushort)0, ReadWord(memory, rastPort + (uint)GraphicsLayouts.RastPortFlags));

        // The second connected line keeps the shared endpoint at its first
        // value rather than complementing it back.
        var beforeNew3 = core.ReadPixel(rastPort, 3, 0);
        Assert.Equal(0, core.Draw(rastPort, 4, 0));
        Assert.Equal(firstEnd, core.ReadPixel(rastPort, 2, 0));
        Assert.NotEqual(beforeNew3, core.ReadPixel(rastPort, 3, 0));
        Assert.NotEqual(beforeNew3, core.ReadPixel(rastPort, 4, 0));
    }

    [Fact]
    public void DrawOneDotModeKeepsOnePixelPerMajorRasterAndPreservesTheFlag()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint rastPort = 0x20;

        Assert.Equal(0, core.SetRast(rastPort, 0));
        Assert.Equal(0, core.SetDrawMode(rastPort, 0));
        Assert.True(memory.TryWriteWord(
            rastPort + (uint)GraphicsLayouts.RastPortFlags,
            (ushort)(GraphicsLayouts.RastPortOneDot | GraphicsLayouts.RastPortFirstDot)));
        Assert.True(memory.TryWriteWord(
            rastPort + (uint)GraphicsLayouts.RastPortLinePattern,
            0xFFFF));
        Assert.True(memory.TryWriteByte(
            rastPort + (uint)GraphicsLayouts.RastPortLinePatternCount,
            15));

        Assert.Equal(0, core.Move(rastPort, 2, 0));
        Assert.Equal(0, core.Draw(rastPort, 4, 5));

        // A steep line has Y as its major axis.  ONE_DOT therefore emits one
        // changed pixel on each raster row, while FRST_DOT is consumed by the
        // completed line and ONE_DOT remains a persistent RastPort mode.
        for (short y = 0; y <= 5; y++)
        {
            var changed = 0;
            for (short x = 0; x < 8; x++)
            {
                if (core.ReadPixel(rastPort, x, y) != 0)
                    changed++;
            }

            Assert.Equal(1, changed);
        }

        Assert.Equal(
            (ushort)GraphicsLayouts.RastPortOneDot,
            ReadWord(memory, rastPort + (uint)GraphicsLayouts.RastPortFlags));
    }

    [Fact]
    public void ClearEolAndClearScreenUseTextGeometryAndComplementBackgroundPen()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        var fonts = new FakeFontBackend();
        Assert.Equal(0, core.SetFont(0x20, 0x300, fonts));
        Assert.Equal(0, core.SetRast(0x20, 3));
        Assert.Equal(0, core.SetBPen(0x20, 2));
        Assert.Equal(0, core.SetDrawMode(0x20, 2));
        Assert.Equal(0, core.Move(0x20, 2, 5));

        Assert.Equal(0, core.ClearEOL(0x20));
        Assert.Equal(3, core.ReadPixel(0x20, 1, 2));
        Assert.Equal(2, core.ReadPixel(0x20, 2, 2));
        Assert.Equal(2, core.ReadPixel(0x20, 15, 4));
        Assert.Equal(3, core.ReadPixel(0x20, 1, 4));

        Assert.Equal(0, core.SetDrawMode(0x20, 1));
        Assert.Equal(0, core.SetRast(0x20, 3));
        Assert.Equal(0, core.SetDrawMode(0x20, 2));
        Assert.Equal(0, core.ClearScreen(0x20));
        Assert.Equal(3, core.ReadPixel(0x20, 1, 2));
        Assert.Equal(2, core.ReadPixel(0x20, 2, 2));
        Assert.Equal(2, core.ReadPixel(0x20, 0, 5));
        Assert.Equal(2, core.ReadPixel(0x20, 15, 7));
        Assert.Equal(3, core.ReadPixel(0x20, 0, 1));

        // JAM2 is not the clear-operation background mode: these vectors
        // clear to pen zero even when BPen is non-zero.
        Assert.Equal(0, core.SetDrawMode(0x20, 1));
        Assert.Equal(0, core.SetRast(0x20, 3));
        Assert.Equal(0, core.ClearScreen(0x20));
        Assert.Equal(0, core.ReadPixel(0x20, 2, 2));
        Assert.Equal(0, core.ReadPixel(0x20, 15, 7));
    }

    [Fact]
    public void EraseRectClearsNonLayeredRasterAndRejectsLayeredFallback()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        Assert.Equal(0, core.SetRast(0x20, 3));
        Assert.Equal(0, core.EraseRect(0x20, 1, 1, 2, 2));
        Assert.Equal(0, core.ReadPixel(0x20, 1, 1));
        Assert.Equal(3, core.ReadPixel(0x20, 0, 0));
        Assert.Equal(-1, core.EraseRect(0x20, 2, 1, 1, 2));

        Assert.True(memory.TryWriteLong(0x20u + (uint)GraphicsLayouts.RastPortLayer, 0x180));
        Assert.Equal(-1, core.EraseRect(0x20, 0, 0, 1, 1));
    }

    [Fact]
    public void WriteMaskRestrictsPlanarWritesAndTextAdvanceUsesCachedMetrics()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        Assert.True(memory.TryWriteByte(0x20u + (uint)GraphicsLayouts.RastPortMask, 0x01));
        Assert.True(memory.TryWriteWord(0x20u + (uint)GraphicsLayouts.RastPortTextWidth, 8));
        Assert.True(memory.TryWriteWord(0x20u + (uint)GraphicsLayouts.RastPortTextSpacing, 1));
        Assert.True(memory.TryWriteWord(0x20u + (uint)GraphicsLayouts.RastPortCurrentX, 10));
        Assert.True(memory.TryWriteWord(0x20u + (uint)GraphicsLayouts.RastPortCurrentY, 4));

        Assert.Equal(0, core.WritePixel(0x20, 0, 0));
        Assert.Equal(1, core.ReadPixel(0x20, 0, 0));
        Assert.Equal(-1, core.WritePixel(0x20, -1, 0));
        Assert.Equal(-1, core.WritePixel(0x20, 16, 0));
        Assert.Equal(26, core.TextLength(0x20, 3));
        Assert.Equal(26, core.AdvanceText(0x20, 3));
        Assert.Equal((short)36, unchecked((short)ReadWord(memory, 0x20u + (uint)GraphicsLayouts.RastPortCurrentX)));
    }

    [Fact]
    public void HostRegisterAdapterPreservesWritePixelOutsideRastPortError()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        var state = new M68kCpuState { A = { [1] = 0x20 }, D = { [0] = 16, [1] = 0 } };

        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.WritePixel));
        Assert.Equal(uint.MaxValue, state.D[0]);

        state.D[0] = 0;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.WritePixel));
        Assert.Equal(0u, state.D[0]);
    }

    [Fact]
    public void PixelArray8UsesPaddedGuestRowsAndRoundTripsPlanarPens()
    {
        var memory = new FakeMemory(0x4000);
        var core = CreateCore(memory);
        const uint array = 0x900;
        const uint readBack = 0xA00;

        Assert.True(memory.TryWriteByte(array + 0, 1));
        Assert.True(memory.TryWriteByte(array + 1, 2));
        Assert.True(memory.TryWriteByte(array + 2, 3));
        Assert.True(memory.TryWriteByte(array + 3, 0));
        Assert.Equal(4, core.WritePixelLine8(0x20, 1, 1, 4, array, 0));
        Assert.Equal(1, core.ReadPixel(0x20, 1, 1));
        Assert.Equal(2, core.ReadPixel(0x20, 2, 1));
        Assert.Equal(3, core.ReadPixel(0x20, 3, 1));
        Assert.Equal(0, core.ReadPixel(0x20, 4, 1));

        for (var index = 0; index < 16; index++)
            Assert.True(memory.TryWriteByte(readBack + (uint)index, 0xEE));

        Assert.Equal(4, core.ReadPixelLine8(0x20, 1, 1, 4, readBack, 0));
        Assert.Equal((byte)1, ReadByte(memory, readBack));
        Assert.Equal((byte)2, ReadByte(memory, readBack + 1));
        Assert.Equal((byte)3, ReadByte(memory, readBack + 2));
        Assert.Equal((byte)0, ReadByte(memory, readBack + 3));
        Assert.Equal((byte)0xEE, ReadByte(memory, readBack + 15));

        Assert.True(memory.TryWriteByte(array + 0, 3));
        Assert.True(memory.TryWriteByte(array + 1, 1));
        Assert.True(memory.TryWriteByte(array + 16, 2));
        Assert.True(memory.TryWriteByte(array + 17, 3));
        Assert.Equal(4, core.WritePixelArray8(0x20, 5, 2, 6, 3, array, 0));
        Assert.Equal(3, core.ReadPixel(0x20, 5, 2));
        Assert.Equal(1, core.ReadPixel(0x20, 6, 2));
        Assert.Equal(2, core.ReadPixel(0x20, 5, 3));
        Assert.Equal(3, core.ReadPixel(0x20, 6, 3));

        Assert.Equal(4, core.ReadPixelArray8(0x20, 5, 2, 6, 3, readBack, 0));
        Assert.Equal((byte)3, ReadByte(memory, readBack));
        Assert.Equal((byte)1, ReadByte(memory, readBack + 1));
        Assert.Equal((byte)2, ReadByte(memory, readBack + 16));
        Assert.Equal((byte)3, ReadByte(memory, readBack + 17));
    }

    [Fact]
    public void PixelArray8ClipsToPlanarBitmapAndRejectsMalformedRanges()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint array = 0x900;
        Assert.True(memory.TryWriteByte(array, 1));
        Assert.True(memory.TryWriteByte(array + 1, 2));
        Assert.True(memory.TryWriteByte(array + 2, 3));

        Assert.Equal(1, core.WritePixelLine8(0x20, 15, 0, 3, array, 0));
        Assert.Equal(1, core.ReadPixel(0x20, 15, 0));
        Assert.Equal(0, core.ReadPixel(0x20, 14, 0));
        Assert.Equal(1, core.ReadPixelLine8(0x20, 15, 0, 3, array, 0));
        Assert.Equal((byte)1, ReadByte(memory, array));
        Assert.Equal((byte)0, ReadByte(memory, array + 1));
        Assert.Equal((byte)0, ReadByte(memory, array + 2));

        Assert.Equal(-1, core.WritePixelArray8(0x20, 4, 3, 3, 4, array, 0));
        Assert.Equal(-1, core.ReadPixelLine8(0x20, 0, 0, 3, 0, 0));
    }

    [Fact]
    public void PixelArray8PreflightsGuestArraysAndAllDestinationPlanes()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint array = 0x1FF0;

        // The first row is mapped, but the second padded row starts exactly
        // at the end of guest memory.  A failed write must not expose row 0.
        Assert.True(memory.TryWriteByte(array, 3));
        Assert.Equal(0, core.ReadPixel(0x20, 0, 0));
        Assert.Equal(-1, core.WritePixelArray8(0x20, 0, 0, 0, 1, array, 0));
        Assert.Equal(0, core.ReadPixel(0x20, 0, 0));

        // A readable first plane is not enough: reject a malformed later
        // plane before changing the first one.
        Assert.True(memory.TryWriteLong(0x100u + (uint)GraphicsLayouts.BitMapPlanes + 4u, 0x2000));
        Assert.True(memory.TryWriteByte(0x900, 3));
        Assert.Equal(-1, core.WritePixelLine8(0x20, 0, 0, 1, 0x900, 0));
        Assert.Equal((byte)0, ReadByte(memory, 0x400));
    }

    [Fact]
    public void ReadPixelArray8PreflightsTruncatedOutputArray()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint array = 0x1FF0;

        Assert.True(memory.TryWriteByte(array, 0xEE));
        Assert.Equal(-1, core.ReadPixelArray8(0x20, 0, 0, 0, 1, array, 0));
        Assert.Equal((byte)0xEE, ReadByte(memory, array));
    }

    [Fact]
    public void WriteChunkyPixelsUsesExplicitStrideAndSignedCoordinates()
    {
        var memory = new FakeMemory(0x4000);
        var core = CreateCore(memory);
        const uint array = 0xB00;
        Assert.True(memory.TryWriteByte(array + 0, 1));
        Assert.True(memory.TryWriteByte(array + 1, 2));
        Assert.True(memory.TryWriteByte(array + 2, 3));
        Assert.True(memory.TryWriteByte(array + 3, 0));
        Assert.True(memory.TryWriteByte(array + 6, 3));
        Assert.True(memory.TryWriteByte(array + 7, 2));
        Assert.True(memory.TryWriteByte(array + 8, 1));
        Assert.True(memory.TryWriteByte(array + 9, 0));

        Assert.Equal(8, core.WriteChunkyPixels(0x20, 2, 6, 5, 7, array, 6));
        Assert.Equal(1, core.ReadPixel(0x20, 2, 6));
        Assert.Equal(2, core.ReadPixel(0x20, 3, 6));
        Assert.Equal(3, core.ReadPixel(0x20, 4, 6));
        Assert.Equal(0, core.ReadPixel(0x20, 5, 6));
        Assert.Equal(3, core.ReadPixel(0x20, 2, 7));
        Assert.Equal(2, core.ReadPixel(0x20, 3, 7));
        Assert.Equal(1, core.ReadPixel(0x20, 4, 7));
        Assert.Equal(0, core.ReadPixel(0x20, 5, 7));
        Assert.Equal(-1, core.WriteChunkyPixels(0x20, 0, 0, 3, 0, array, 3));
    }

    [Fact]
    public void PixelArray8VectorsUseA0RasterPortAndPreserveLayerAndRtgFallbacks()
    {
        var memory = new FakeMemory(0x3000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(
            core,
            isRtgRastPort: address => address == 0x20);
        const uint array = 0x900;
        Assert.True(memory.TryWriteByte(array, 2));
        Assert.True(memory.TryWriteByte(array + 1, 3));

        var write = new M68kCpuState
        {
            A = { [0] = 0x20, [1] = 0, [2] = array },
            D = { [0] = 0, [1] = 0, [2] = 2 }
        };
        Assert.False(adapter.TryInvoke(write, (int)GraphicsLvo.WritePixelLine8));

        var nonRtgAdapter = new CopperStartGraphicsRegisterAdapter(core);
        Assert.True(nonRtgAdapter.TryInvoke(write, (int)GraphicsLvo.WritePixelLine8));
        Assert.Equal(2u, write.D[0]);
        Assert.Equal(2, core.ReadPixel(0x20, 0, 0));
        Assert.Equal(3, core.ReadPixel(0x20, 1, 0));

        Assert.True(memory.TryWriteLong(0x20u + (uint)GraphicsLayouts.RastPortLayer, 0x180));
        Assert.False(nonRtgAdapter.TryInvoke(write, (int)GraphicsLvo.WritePixelLine8));
    }

    [Fact]
    public void CombinedPenModeWriteMaskAndOutlinePenUpdateRastPortState()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);

        Assert.True(memory.TryWriteByte(0x20u + (uint)GraphicsLayouts.RastPortOutlinePen, 3));
        Assert.Equal(0, core.SetABPenDrMd(0x20, 5, 6, 2));
        Assert.Equal(5, core.GetAPen(0x20));
        Assert.Equal(6, core.GetBPen(0x20));
        Assert.Equal(2, core.GetDrawMode(0x20));
        Assert.Equal(0, core.SetWriteMask(0x20, 0x01));
        Assert.Equal((byte)0x01, ReadByte(memory, 0x20u + (uint)GraphicsLayouts.RastPortMask));

        Assert.Equal(3, core.GetOutlinePen(0x20));
        Assert.Equal(3, core.SetOutlinePen(0x20, 7));
        Assert.Equal(7, core.GetOutlinePen(0x20));
        Assert.Equal((ushort)GraphicsLayouts.RastPortAreaOutline,
            ReadWord(memory, 0x20u + (uint)GraphicsLayouts.RastPortFlags));
    }

    [Fact]
    public void SoftStyleVectorsTrackAlgorithmicStyleAndResetWhenFontChanges()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        var fonts = new FakeFontBackend();

        Assert.Equal(0, core.SetFont(0x20, 0x300, fonts));
        Assert.Equal(-1, core.AskSoftStyle(0x20, fonts));
        Assert.Equal(4, core.SetSoftStyle(0x20, 4, 0xFF, fonts));
        Assert.Equal((byte)4, ReadByte(memory, 0x20u + (uint)GraphicsLayouts.RastPortAlgoStyle));
        Assert.Equal(0, core.SetSoftStyle(0x20, 0, 4, fonts));
        Assert.Equal((byte)0, ReadByte(memory, 0x20u + (uint)GraphicsLayouts.RastPortAlgoStyle));

        Assert.Equal(4, core.SetSoftStyle(0x20, 4, 0xFF, fonts));
        Assert.Equal(0, core.SetFont(0x20, 0x300, fonts));
        Assert.Equal((byte)0, ReadByte(memory, 0x20u + (uint)GraphicsLayouts.RastPortAlgoStyle));
    }

    [Fact]
    public void TextAppliesAlgorithmicBoldItalicAndUnderlineWithoutChangingAdvance()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        var fonts = new FakeFontBackend();
        const uint font = 0x300;
        const uint text = 0x320;
        memory.TryWriteByte(text, (byte)'A');

        Assert.Equal(0, core.SetFont(0x20, font, fonts));
        Assert.Equal(2, core.SetSoftStyle(0x20, 2, 2, fonts));
        Assert.Equal(0, core.Move(0x20, 0, 3));
        Assert.Equal(0, core.Text(0x20, text, 1, fonts));
        Assert.Equal(3, core.ReadPixel(0x20, 3, 0));
        Assert.Equal((short)4, unchecked((short)ReadWord(memory, 0x20u + (uint)GraphicsLayouts.RastPortCurrentX)));

        Assert.Equal(1, core.SetSoftStyle(0x20, 1, 3, fonts));
        Assert.Equal(0, core.Move(0x20, 0, 3));
        Assert.Equal(0, core.Text(0x20, text, 1, fonts));
        Assert.Equal(3, core.ReadPixel(0x20, 0, 4));

        Assert.Equal(4, core.SetSoftStyle(0x20, 4, 7, fonts));
        Assert.Equal(0, core.Move(0x20, 0, 3));
        Assert.Equal(0, core.Text(0x20, text, 1, fonts));
        Assert.Equal(3, core.ReadPixel(0x20, 1, 0));
        Assert.Equal(3, core.ReadPixel(0x20, 0, 2));
    }

    [Fact]
    public void TextUsesFontMetricsGlyphRowsAndAdvancesTheRastPortCursor()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        var fonts = new FakeFontBackend();
        const uint font = 0x300;
        const uint text = 0x320;
        Assert.True(memory.TryWriteByte(text, (byte)'A'));
        Assert.True(memory.TryWriteWord(0x20u + (uint)GraphicsLayouts.RastPortCurrentX, 0));
        Assert.True(memory.TryWriteWord(0x20u + (uint)GraphicsLayouts.RastPortCurrentY, 5));

        Assert.Equal(0, core.SetFont(0x20, font, fonts));
        Assert.Equal(0, core.Text(0x20, text, 1, fonts));
        Assert.Equal(3, core.ReadPixel(0x20, 0, 2));
        Assert.Equal(3, core.ReadPixel(0x20, 1, 2));
        Assert.Equal(3, core.ReadPixel(0x20, 2, 2));
        Assert.Equal(0, core.ReadPixel(0x20, 1, 3));
        Assert.Equal((short)4, unchecked((short)ReadWord(memory, 0x20u + (uint)GraphicsLayouts.RastPortCurrentX)));
    }

    [Fact]
    public void TextTruncatesTheCursorAtThePlanarRastPortBoundary()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        var fonts = new FakeFontBackend();
        const uint font = 0x300;
        const uint text = 0x320;
        memory.TryWriteByte(text, (byte)'A');

        Assert.Equal(0, core.SetFont(0x20, font, fonts));
        Assert.Equal(0, core.Move(0x20, 14, 5));
        Assert.Equal(0, core.Text(0x20, text, 1, fonts));

        // The test bitmap is 16 pixels wide.  The glyph advances from x=14
        // to x=18, but the public RastPort current position is clipped to
        // the drawing boundary instead of wrapping a signed word.
        Assert.Equal((short)16, unchecked((short)ReadWord(
            memory,
            0x20u + (uint)GraphicsLayouts.RastPortCurrentX)));
        Assert.Equal(3, core.ReadPixel(0x20, 14, 2));
        Assert.Equal(3, core.ReadPixel(0x20, 15, 2));
    }

    [Fact]
    public void TextFailsClosedWhenAnInRangePlaneCannotBeWritten()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        var fonts = new FakeFontBackend();
        const uint font = 0x300;
        const uint text = 0x320;
        memory.TryWriteByte(text, (byte)'A');
        memory.TryWriteLong(0x100u + (uint)GraphicsLayouts.BitMapPlanes, 0x2000);

        Assert.Equal(0, core.SetFont(0x20, font, fonts));
        Assert.Equal(0, core.Move(0x20, 0, 5));
        Assert.Equal(-1, core.Text(0x20, text, 1, fonts));
    }

    [Fact]
    public void TextPreflightsEveryGlyphBeforePublishingPlanarWrites()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint font = 0x300;
        const uint text = 0x320;
        Assert.True(memory.TryWriteByte(text, (byte)'A'));
        Assert.True(memory.TryWriteByte(text + 1, (byte)'B'));

        var fonts = new TableFontBackend(
            new GraphicsFontMetrics(3, 3, 3, 0),
            new Dictionary<byte, GraphicsGlyph>
            {
                [(byte)'A'] = new GraphicsGlyph(3, 3, 4, 0xE0A0E00000000000UL),
                // The first row is addressable, but the remaining rows fall
                // beyond the guest memory envelope.  Without the complete
                // Text preflight, A would be visible before B fails.
                [(byte)'B'] = new GraphicsGlyph(3, 3, 4, 0, 0x1FFF, 1, 0)
            });

        Assert.Equal(0, core.SetFont(0x20, font, fonts));
        Assert.Equal(0, core.Move(0x20, 0, 5));
        Assert.Equal(-1, core.Text(0x20, text, 2, fonts));
        Assert.Equal(0, core.ReadPixel(0x20, 0, 2));
        Assert.Equal(0, core.ReadPixel(0x20, 1, 2));
        Assert.Equal((short)0, unchecked((short)ReadWord(
            memory,
            0x20u + (uint)GraphicsLayouts.RastPortCurrentX)));
    }

    [Fact]
    public void TextPreflightsEveryDestinationRowBeforePublishingPlanarWrites()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint font = 0x300;
        const uint text = 0x320;
        const uint bitMap = 0x100;
        const uint plane0 = 0x400;

        Assert.True(memory.TryWriteByte(text, (byte)'A'));
        // Text starts at y=2.  The first two rows of plane 1 are mapped, but
        // the third row crosses the guest-memory boundary.  A per-pixel
        // guard would publish the first rows before discovering that fault.
        Assert.True(memory.TryWriteLong(
            bitMap + (uint)GraphicsLayouts.BitMapPlanes + 4u,
            0x1FF9));
        Assert.True(memory.TryWriteByte(plane0 + 4u, 0x5A));
        Assert.True(memory.TryWriteByte(0x1FFD, 0xA5));
        Assert.True(memory.TryWriteByte(0x1FFF, 0xA5));

        var fonts = new FakeFontBackend();
        Assert.Equal(0, core.SetFont(0x20, font, fonts));
        Assert.Equal(0, core.Move(0x20, 0, 5));
        Assert.Equal(-1, core.Text(0x20, text, 1, fonts));

        Assert.Equal((byte)0x5A, ReadByte(memory, plane0 + 4u));
        Assert.Equal((byte)0xA5, ReadByte(memory, 0x1FFD));
        Assert.Equal((byte)0xA5, ReadByte(memory, 0x1FFF));
        Assert.Equal((short)0, unchecked((short)ReadWord(
            memory,
            0x20u + (uint)GraphicsLayouts.RastPortCurrentX)));
    }

    [Fact]
    public void RectFillPreflightsEveryDestinationRowBeforePublishingPlanarWrites()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint bitMap = 0x100;
        const uint plane0 = 0x400;

        // Rows 2 and 3 are addressable in plane 1, while row 4 crosses the
        // guest-memory boundary.  A per-pixel guard would fill the first two
        // rows before discovering that final-row fault.
        Assert.True(memory.TryWriteLong(
            bitMap + (uint)GraphicsLayouts.BitMapPlanes + 4u,
            0x1FF9));
        Assert.True(memory.TryWriteByte(plane0 + 4u, 0x5A));
        Assert.True(memory.TryWriteByte(plane0 + 6u, 0x5A));
        Assert.True(memory.TryWriteByte(0x1FFD, 0xA5));
        Assert.True(memory.TryWriteByte(0x1FFF, 0xA5));

        Assert.Equal(-1, core.RectFill(0x20, 0, 2, 15, 4));
        Assert.Equal((byte)0x5A, ReadByte(memory, plane0 + 4u));
        Assert.Equal((byte)0x5A, ReadByte(memory, plane0 + 6u));
        Assert.Equal((byte)0xA5, ReadByte(memory, 0x1FFD));
        Assert.Equal((byte)0xA5, ReadByte(memory, 0x1FFF));
    }

    [Fact]
    public void DrawPreflightsEveryDestinationRowBeforePublishingPlanarWrites()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint bitMap = 0x100;
        const uint plane0 = 0x400;

        Assert.True(memory.TryWriteLong(
            bitMap + (uint)GraphicsLayouts.BitMapPlanes + 4u,
            0x1FF9));
        Assert.True(memory.TryWriteByte(plane0 + 4u, 0x5A));
        Assert.True(memory.TryWriteByte(plane0 + 6u, 0x5A));
        Assert.True(memory.TryWriteByte(0x1FFD, 0xA5));
        Assert.True(memory.TryWriteByte(0x1FFF, 0xA5));
        Assert.Equal(0, core.Move(0x20, 0, 2));

        Assert.Equal(-1, core.Draw(0x20, 0, 4));
        Assert.Equal((byte)0x5A, ReadByte(memory, plane0 + 4u));
        Assert.Equal((byte)0x5A, ReadByte(memory, plane0 + 6u));
        Assert.Equal((byte)0xA5, ReadByte(memory, 0x1FFD));
        Assert.Equal((byte)0xA5, ReadByte(memory, 0x1FFF));
        Assert.Equal((short)0, ReadSignedWord(
            memory,
            0x20u + (uint)GraphicsLayouts.RastPortCurrentX));
        Assert.Equal((short)2, ReadSignedWord(
            memory,
            0x20u + (uint)GraphicsLayouts.RastPortCurrentY));
    }

    [Fact]
    public void SetRastPreflightsTheCompleteBitmapBeforePublishingPlanarWrites()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint bitMap = 0x100;
        const uint plane0 = 0x400;

        Assert.True(memory.TryWriteLong(
            bitMap + (uint)GraphicsLayouts.BitMapPlanes + 4u,
            0x1FF9));
        for (var row = 0; row < 4; row++)
            Assert.True(memory.TryWriteByte(plane0 + (uint)(row * 2), 0x5A));
        Assert.True(memory.TryWriteByte(0x1FFD, 0xA5));
        Assert.True(memory.TryWriteByte(0x1FFF, 0xA5));

        Assert.Equal(-1, core.SetRast(0x20, 3));
        for (var row = 0; row < 4; row++)
            Assert.Equal((byte)0x5A, ReadByte(memory, plane0 + (uint)(row * 2)));
        Assert.Equal((byte)0xA5, ReadByte(memory, 0x1FFD));
        Assert.Equal((byte)0xA5, ReadByte(memory, 0x1FFF));
    }

    [Fact]
    public void ClearScreenPreflightsBothTextAndRemainingRowsBeforePublishing()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint bitMap = 0x100;
        const uint plane0 = 0x400;

        Assert.True(memory.TryWriteLong(
            bitMap + (uint)GraphicsLayouts.BitMapPlanes + 4u,
            0x1FF9));
        Assert.True(memory.TryWriteWord(
            0x20u + (uint)GraphicsLayouts.RastPortCurrentY,
            5));
        Assert.True(memory.TryWriteWord(
            0x20u + (uint)GraphicsLayouts.RastPortTextBaseline,
            3));
        Assert.True(memory.TryWriteWord(
            0x20u + (uint)GraphicsLayouts.RastPortTextHeight,
            3));
        Assert.True(memory.TryWriteByte(plane0 + 4u, 0x5A));
        Assert.True(memory.TryWriteByte(plane0 + 6u, 0x5A));
        Assert.True(memory.TryWriteByte(0x1FFD, 0xA5));
        Assert.True(memory.TryWriteByte(0x1FFF, 0xA5));

        Assert.Equal(-1, core.ClearScreen(0x20));
        Assert.Equal((byte)0x5A, ReadByte(memory, plane0 + 4u));
        Assert.Equal((byte)0x5A, ReadByte(memory, plane0 + 6u));
        Assert.Equal((byte)0xA5, ReadByte(memory, 0x1FFD));
        Assert.Equal((byte)0xA5, ReadByte(memory, 0x1FFF));
    }

    [Fact]
    public void ClearScreenDoesNotPublishTextPrefixWhenRemainingRowsFailLate()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint bitMap = 0x100;
        const uint plane0 = 0x400;
        const uint latePlane = 0x1FF8;

        // Rows 2 and 3 (the text-line prefix) are readable in the late
        // plane, while row 4 and onward run beyond the guest memory window.
        Assert.True(memory.TryWriteLong(
            bitMap + (uint)GraphicsLayouts.BitMapPlanes + 4u,
            latePlane));
        Assert.True(memory.TryWriteWord(
            0x20u + (uint)GraphicsLayouts.RastPortCurrentY,
            3));
        Assert.True(memory.TryWriteWord(
            0x20u + (uint)GraphicsLayouts.RastPortTextBaseline,
            1));
        Assert.True(memory.TryWriteWord(
            0x20u + (uint)GraphicsLayouts.RastPortTextHeight,
            2));

        for (var row = 2; row <= 3; row++)
            Assert.True(memory.TryWriteByte(plane0 + (uint)(row * 2), 0x5A));
        Assert.True(memory.TryWriteByte(latePlane + 4u, 0xA5));
        Assert.True(memory.TryWriteByte(latePlane + 6u, 0xA5));

        Assert.Equal(-1, core.ClearScreen(0x20));
        Assert.Equal((byte)0x5A, ReadByte(memory, plane0 + 4u));
        Assert.Equal((byte)0x5A, ReadByte(memory, plane0 + 6u));
        Assert.Equal((byte)0xA5, ReadByte(memory, latePlane + 4u));
        Assert.Equal((byte)0xA5, ReadByte(memory, latePlane + 6u));
    }

    [Fact]
    public void ScrollRasterPreflightsTheClippedDestinationBeforePublishing()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint bitMap = 0x100;
        const uint plane0 = 0x400;

        Assert.True(memory.TryWriteLong(
            bitMap + (uint)GraphicsLayouts.BitMapPlanes + 4u,
            0x1FF9));
        Assert.True(memory.TryWriteByte(plane0 + 4u, 0x5A));
        Assert.True(memory.TryWriteByte(plane0 + 6u, 0x5A));
        Assert.True(memory.TryWriteByte(0x1FFD, 0xA5));
        Assert.True(memory.TryWriteByte(0x1FFF, 0xA5));

        Assert.Equal(-1, core.ScrollRaster(0x20, 0, 0, 0, 2, 15, 4));
        Assert.Equal((byte)0x5A, ReadByte(memory, plane0 + 4u));
        Assert.Equal((byte)0x5A, ReadByte(memory, plane0 + 6u));
        Assert.Equal((byte)0xA5, ReadByte(memory, 0x1FFD));
        Assert.Equal((byte)0xA5, ReadByte(memory, 0x1FFF));
    }

    [Fact]
    public void DrawEllipsePreflightsEveryDestinationPointBeforePublishing()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint bitMap = 0x100;
        const uint plane0 = 0x400;

        Assert.True(memory.TryWriteLong(
            bitMap + (uint)GraphicsLayouts.BitMapPlanes + 4u,
            0x1FF9));
        Assert.True(memory.TryWriteByte(plane0 + 4u, 0x5A));
        Assert.True(memory.TryWriteByte(plane0 + 6u, 0x5A));
        Assert.True(memory.TryWriteByte(0x1FFD, 0xA5));
        Assert.True(memory.TryWriteByte(0x1FFF, 0xA5));

        Assert.Equal(-1, core.DrawEllipse(0x20, 0, 3, 2, 2));
        Assert.Equal((byte)0x5A, ReadByte(memory, plane0 + 4u));
        Assert.Equal((byte)0x5A, ReadByte(memory, plane0 + 6u));
        Assert.Equal((byte)0xA5, ReadByte(memory, 0x1FFD));
        Assert.Equal((byte)0xA5, ReadByte(memory, 0x1FFF));
    }

    [Fact]
    public void BltClearPreflightsTheCompleteDestinationSpanBeforePublishing()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        for (var offset = 0; offset < 4; offset++)
            Assert.True(memory.TryWriteByte(0x1FFCu + (uint)offset, 0xA5));

        Assert.Equal(-1, core.BltClear(0x1FFC, 8, 0));
        for (var offset = 0; offset < 4; offset++)
            Assert.Equal((byte)0xA5, ReadByte(memory, 0x1FFCu + (uint)offset));
    }

    [Fact]
    public void TextHonorsInverseVideoForJam1AndJam2()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        var fonts = new FakeFontBackend();
        const uint font = 0x300;
        const uint text = 0x320;
        memory.TryWriteByte(text, (byte)'A');

        Assert.Equal(0, core.SetFont(0x20, font, fonts));
        Assert.Equal(0, core.SetAPen(0x20, 1));
        Assert.Equal(0, core.SetBPen(0x20, 2));
        Assert.Equal(0, core.SetRast(0x20, 0));
        Assert.Equal(0, core.SetDrawMode(0x20, 4));
        Assert.Equal(0, core.Move(0x20, 0, 5));
        Assert.Equal(0, core.Text(0x20, text, 1, fonts));
        Assert.Equal(0, core.ReadPixel(0x20, 0, 2));
        Assert.Equal(1, core.ReadPixel(0x20, 3, 2));

        Assert.Equal(0, core.SetRast(0x20, 0));
        Assert.Equal(0, core.SetDrawMode(0x20, 5));
        Assert.Equal(0, core.Move(0x20, 0, 5));
        Assert.Equal(0, core.Text(0x20, text, 1, fonts));
        Assert.Equal(2, core.ReadPixel(0x20, 0, 2));
        Assert.Equal(1, core.ReadPixel(0x20, 3, 2));
    }

    [Fact]
    public void GuestTextFontDecoderReadsBitPackedProportionalGlyphs()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        var fonts = new GraphicsMemoryFontBackend(memory);
        memory.TryWriteWord(0x100u + (uint)GraphicsLayouts.BitMapBytesPerRow, 4);
        const uint font = 0x600;
        const uint charData = 0x700;
        const uint charLoc = 0x800;
        const uint charSpace = 0x900;
        const uint charKern = 0xA00;
        const uint text = 0xB00;

        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontYSize, 4);
        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontXSize, 8);
        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontBaseline, 3);
        memory.TryWriteByte(font + (uint)GraphicsLayouts.TextFontFlags, 0x04);
        memory.TryWriteByte(font + (uint)GraphicsLayouts.TextFontLoChar, (byte)'A');
        memory.TryWriteByte(font + (uint)GraphicsLayouts.TextFontHiChar, (byte)'B');
        memory.TryWriteLong(font + (uint)GraphicsLayouts.TextFontCharData, charData);
        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontModulo, 2);
        memory.TryWriteLong(font + (uint)GraphicsLayouts.TextFontCharLoc, charLoc);
        memory.TryWriteLong(font + (uint)GraphicsLayouts.TextFontCharSpace, charSpace);
        memory.TryWriteLong(font + (uint)GraphicsLayouts.TextFontCharKern, charKern);

        // A starts at bit one and is five pixels wide; B starts at bit nine.
        memory.TryWriteWord(charLoc, 1);
        memory.TryWriteWord(charLoc + 2, 5);
        memory.TryWriteWord(charLoc + 4, 9);
        memory.TryWriteWord(charLoc + 6, 3);
        memory.TryWriteWord(charSpace, 6);
        memory.TryWriteWord(charSpace + 2, 4);
        memory.TryWriteWord(charKern, unchecked((ushort)-1));
        memory.TryWriteWord(charKern + 2, 0);
        memory.TryWriteByte(charData, 0x54); // A: 10101
        memory.TryWriteByte(charData + 1, 0x70); // B: 111
        memory.TryWriteByte(charData + 2, 0x28); // A second row: 01010
        memory.TryWriteByte(charData + 3, 0x40); // B second row: 100
        memory.TryWriteByte(text, (byte)'A');
        memory.TryWriteByte(text + 1, (byte)'B');

        Assert.True(fonts.TryGetMetrics(font, out var metrics));
        Assert.Equal((ushort)4, metrics.Height);
        Assert.Equal((ushort)8, metrics.Width);
        Assert.Equal((ushort)3, metrics.Baseline);
        Assert.Equal((byte)0x04, metrics.Flags);
        Assert.True(fonts.TryGetGlyph(font, (byte)'A', out var glyphA));
        Assert.True(glyphA.TryIsSet(memory, 0, 0, out var a0) && a0);
        Assert.True(glyphA.TryIsSet(memory, 1, 0, out var a1) && !a1);
        Assert.Equal(6, glyphA.Advance);
        Assert.Equal((short)-1, glyphA.Kerning);
        Assert.True(fonts.TryGetGlyph(font, (byte)'B', out var glyphB));
        Assert.True(glyphB.TryIsSet(memory, 0, 0, out var b0) && b0);

        Assert.Equal(0, core.SetFont(0x20, font, fonts));
        Assert.True(memory.TryWriteWord(0x20u + (uint)GraphicsLayouts.RastPortTextSpacing, 1));
        Assert.Equal((ushort)1, ReadWord(memory, 0x20u + (uint)GraphicsLayouts.RastPortTextSpacing));
        Assert.Equal(11, core.TextLength(0x20, text, 2, fonts));
        const uint extent = 0xC00;
        Assert.Equal(0, core.TextExtent(0x20, text, 2, extent, fonts));
        Assert.Equal((ushort)11, ReadWord(memory, extent + (uint)GraphicsLayouts.TextExtentWidth));
        Assert.Equal((short)-1, unchecked((short)ReadWord(memory, extent + (uint)GraphicsLayouts.TextExtentMinX)));
        Assert.Equal((short)8, unchecked((short)ReadWord(memory, extent + (uint)GraphicsLayouts.TextExtentMaxX)));
        Assert.Equal(1, core.TextFit(0x20, text, 2, extent, 0, 1, 10, 4, fonts));
        Assert.Equal(0, core.Move(0x20, 10, 5));
        Assert.Equal(0, core.Text(0x20, text, 2, fonts));
        Assert.Equal(20, unchecked((short)ReadWord(memory, 0x20u + (uint)GraphicsLayouts.RastPortCurrentX)));
        Assert.Equal(3, core.ReadPixel(0x20, 9, 2));
        Assert.Equal(3, core.ReadPixel(0x20, 11, 2));
        Assert.Equal(3, core.ReadPixel(0x20, 16, 2));
        Assert.Equal(3, core.ReadPixel(0x20, 17, 2));
        Assert.Equal(3, core.ReadPixel(0x20, 18, 2));
    }

    [Fact]
    public void GuestTextFontDecoderUsesTheFinalGlyphAsTheDefaultForOutOfRangeCharacters()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        var fonts = new GraphicsMemoryFontBackend(memory);
        const uint font = 0x600;
        const uint charData = 0x700;
        const uint charLoc = 0x800;
        const uint text = 0x900;

        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontYSize, 4);
        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontXSize, 8);
        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontBaseline, 3);
        memory.TryWriteByte(font + (uint)GraphicsLayouts.TextFontLoChar, (byte)'A');
        memory.TryWriteByte(font + (uint)GraphicsLayouts.TextFontHiChar, (byte)'A');
        memory.TryWriteLong(font + (uint)GraphicsLayouts.TextFontCharData, charData);
        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontModulo, 1);
        memory.TryWriteLong(font + (uint)GraphicsLayouts.TextFontCharLoc, charLoc);
        memory.TryWriteLong(font + (uint)GraphicsLayouts.TextFontCharSpace, 0);
        memory.TryWriteLong(font + (uint)GraphicsLayouts.TextFontCharKern, 0);

        // The second CharLoc entry is the default glyph following the A entry.
        memory.TryWriteWord(charLoc, 0);
        memory.TryWriteWord(charLoc + 2, 1);
        memory.TryWriteWord(charLoc + 4, 0);
        memory.TryWriteWord(charLoc + 6, 3);
        memory.TryWriteByte(charData, 0xE0);
        memory.TryWriteByte(text, (byte)'B');

        Assert.Equal(0, core.SetFont(0x20, font, fonts));
        Assert.True(fonts.TryGetGlyph(font, (byte)'B', out var defaultGlyph));
        Assert.Equal((ushort)3, defaultGlyph.Width);
        Assert.Equal(8, defaultGlyph.Advance);
        Assert.Equal(8, core.TextLength(0x20, text, 1, fonts));
        Assert.Equal(0, core.Move(0x20, 0, 3));
        Assert.Equal(0, core.Text(0x20, text, 1, fonts));
        Assert.Equal(3, core.ReadPixel(0x20, 0, 0));
        Assert.Equal(3, core.ReadPixel(0x20, 2, 0));
        Assert.Equal((short)8, unchecked((short)ReadWord(
            memory,
            0x20u + (uint)GraphicsLayouts.RastPortCurrentX)));
    }

    [Fact]
    public void GuestTextFontDecoderSupportsFixedCellFontsWithoutCharLoc()
    {
        var memory = new FakeMemory(0x2000);
        var fonts = new GraphicsMemoryFontBackend(memory);
        const uint font = 0x600;
        const uint charData = 0x700;

        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontYSize, 4);
        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontXSize, 3);
        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontBaseline, 3);
        memory.TryWriteByte(font + (uint)GraphicsLayouts.TextFontLoChar, (byte)'A');
        memory.TryWriteByte(font + (uint)GraphicsLayouts.TextFontHiChar, (byte)'A');
        memory.TryWriteLong(font + (uint)GraphicsLayouts.TextFontCharData, charData);
        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontModulo, 1);
        memory.TryWriteLong(font + (uint)GraphicsLayouts.TextFontCharLoc, 0);
        memory.TryWriteLong(font + (uint)GraphicsLayouts.TextFontCharSpace, 0);
        memory.TryWriteLong(font + (uint)GraphicsLayouts.TextFontCharKern, 0);

        memory.TryWriteByte(charData, 0xE0);

        Assert.True(fonts.TryGetGlyph(font, (byte)'A', out var glyph));
        Assert.Equal((ushort)3, glyph.Width);
        Assert.True(glyph.TryIsSet(memory, 0, 0, out var firstPixel) && firstPixel);
        Assert.True(fonts.TryGetGlyph(font, (byte)'B', out var defaultGlyph));
        Assert.Equal((ushort)3, defaultGlyph.Width);
    }

    [Fact]
    public void AskFontCopiesTheCurrentGuestTextFontAttributesToTextAttr()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint rastPort = 0x20;
        const uint font = 0x600;
        const uint name = 0x500;
        const uint textAttr = 0x400;
        WriteFontHeader(memory, font, name, ySize: 9, style: 0x02, flags: 0x01);
        WriteAscii(memory, name, "topaz.font");
        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortFont, font));

        Assert.Equal(0, core.AskFont(rastPort, textAttr));
        Assert.Equal(name, ReadLong(memory, textAttr + (uint)GraphicsLayouts.TextAttrName));
        Assert.Equal((ushort)9, ReadWord(memory, textAttr + (uint)GraphicsLayouts.TextAttrYSize));
        Assert.Equal((byte)0x02, ReadByte(memory, textAttr + (uint)GraphicsLayouts.TextAttrStyle));
        Assert.Equal((byte)0x01, ReadByte(memory, textAttr + (uint)GraphicsLayouts.TextAttrFlags));
    }

    [Fact]
    public void AskFontPreflightsTheCompleteTextAttrEnvelopeBeforePublishing()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint rastPort = 0x20;
        const uint font = 0x600;
        const uint name = 0x500;
        const uint truncatedTextAttr = 0x1FFCu;
        WriteFontHeader(memory, font, name, ySize: 9, style: 0x02, flags: 0x01);
        WriteAscii(memory, name, "topaz.font");
        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortFont, font));
        Assert.True(memory.TryWriteLong(truncatedTextAttr, 0xDEAD_BEEFu));

        Assert.Equal(-1, core.AskFont(rastPort, truncatedTextAttr));
        Assert.Equal(0xDEAD_BEEFu, ReadLong(memory, truncatedTextAttr));
    }

    [Fact]
    public void FontLifecycleMatchesAddedFontsTracksAccessorsAndRemovesAvailability()
    {
        var memory = new FakeMemory(0x4000);
        var allocator = new FakeAllocator();
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), new FakeDisplay());
        var fonts = new GraphicsMemoryFontBackend(memory, allocator);
        const uint font = 0x600;
        const uint name = 0x500;
        const uint textAttr = 0x400;
        WriteFontHeader(memory, font, name, ySize: 9, style: 0, flags: 1);
        WriteAscii(memory, name, "topaz.font");
        memory.TryWriteLong(textAttr + (uint)GraphicsLayouts.TextAttrName, name);
        memory.TryWriteWord(textAttr + (uint)GraphicsLayouts.TextAttrYSize, 9);
        memory.TryWriteByte(textAttr + (uint)GraphicsLayouts.TextAttrStyle, 0);
        memory.TryWriteByte(textAttr + (uint)GraphicsLayouts.TextAttrFlags, 1);

        Assert.Equal(0, core.AddFont(font, fonts));
        Assert.Equal((ushort)0, ReadWord(memory, font + (uint)GraphicsLayouts.TextFontAccessors));
        Assert.Equal(font, core.OpenFont(textAttr, fonts));
        Assert.Equal((ushort)1, ReadWord(memory, font + (uint)GraphicsLayouts.TextFontAccessors));
        // RemFont removes future availability, but an already-open pointer
        // remains closeable until its accessor count reaches zero.
        Assert.Equal(0, core.RemFont(font, fonts));
        Assert.Equal(0u, core.OpenFont(textAttr, fonts));
        Assert.Equal(0, core.CloseFont(font, fonts));
        Assert.Equal((ushort)0, ReadWord(memory, font + (uint)GraphicsLayouts.TextFontAccessors));
        Assert.Equal(0, core.AddFont(font, fonts));
        Assert.Equal(font, core.OpenFont(textAttr, fonts));
        Assert.Equal(0, core.CloseFont(font, fonts));
        Assert.Equal(0, core.RemFont(font, fonts));
        Assert.Equal(0u, core.OpenFont(textAttr, fonts));
    }

    [Fact]
    public void OpenFontUsesKickstartWeightTamatchOrderingAndSourceRules()
    {
        var memory = new FakeMemory(0x12000);
        var fonts = new GraphicsMemoryFontBackend(memory);
        const uint name = 0x1000;
        const uint textAttr = 0x1100;
        const uint exact = 0x2000;
        const uint styleMismatch = 0x2100;
        const uint sizeMismatch = 0x2200;
        const uint disk = 0x2300;
        const uint diskRevPath = 0x2400;
        const byte bold = 1 << 1;
        const byte diskFlag = 1 << 1;
        const byte revPathFlag = 1 << 2;
        const byte designedFlag = 1 << 6;

        WriteAscii(memory, name, "match.font");
        WriteFontHeader(memory, styleMismatch, name, ySize: 10, style: 0, flags: 0);
        WriteFontHeader(memory, sizeMismatch, name, ySize: 9, style: bold, flags: 0);
        WriteFontHeader(memory, exact, name, ySize: 10, style: bold, flags: 0);
        WriteFontHeader(memory, disk, name, ySize: 10, style: bold, flags: diskFlag);
        WriteFontHeader(memory, diskRevPath, name, ySize: 10, style: bold,
            flags: (byte)(diskFlag | revPathFlag));
        Assert.True(fonts.TryAdd(styleMismatch));
        Assert.True(fonts.TryAdd(sizeMismatch));
        Assert.True(fonts.TryAdd(exact));
        Assert.True(fonts.TryAdd(disk));
        Assert.True(fonts.TryAdd(diskRevPath));

        WriteTextAttr(memory, textAttr, name, ySize: 10, style: bold, flags: 0);
        Assert.Equal(exact, fonts.TryOpen(textAttr, out var opened) ? opened : 0u);
        Assert.True(fonts.TryClose(opened));
        Assert.True(fonts.TryRemove(exact));

        // ROM/DISK are source bits, not ordinary Hamming-distance style
        // preferences.  With the exact ROM-like candidate removed, the disk
        // font remains an equally good match.
        Assert.Equal(disk, fonts.TryOpen(textAttr, out opened) ? opened : 0u);
        Assert.True(fonts.TryClose(opened));

        // A designed request accepts a disk-backed designed variant but not a
        // constructed font.  REVPATH remains a hard incompatibility.
        WriteTextAttr(memory, textAttr, name, ySize: 10, style: bold, flags: designedFlag);
        Assert.Equal(disk, fonts.TryOpen(textAttr, out opened) ? opened : 0u);
        Assert.True(fonts.TryClose(opened));
        Assert.True(fonts.TryRemove(disk));

        // Size is considered before style: an exact-size plain font beats a
        // one-pixel-nearer bold font when no exact style candidate exists.
        WriteTextAttr(memory, textAttr, name, ySize: 10, style: bold, flags: 0);
        Assert.Equal(styleMismatch, fonts.TryOpen(textAttr, out opened) ? opened : 0u);
        Assert.True(fonts.TryClose(opened));

        WriteTextAttr(memory, textAttr, name, ySize: 10, style: bold, flags: revPathFlag);
        Assert.Equal(diskRevPath, fonts.TryOpen(textAttr, out opened) ? opened : 0u);
        Assert.True(fonts.TryClose(opened));
    }

    [Fact]
    public void WeighTAMatchUsesGuestAttributesAndRegisterAbiAndFailsClosed()
    {
        var memory = new FakeMemory(0x2000);
        var fonts = new GraphicsMemoryFontBackend(memory);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint requested = 0x100;
        const uint target = 0x120;

        WriteTextAttr(memory, requested, 0, ySize: 10, style: 0x02, flags: 0);
        WriteTextAttr(memory, target, 0, ySize: 10, style: 0x02, flags: 0);
        Assert.True(fonts.TryWeighTAMatch(requested, target, 0x1FFC, out var exactWeight));
        Assert.Equal((short)32767, exactWeight);

        var state = new M68kCpuState
        {
            A = { [0] = requested, [1] = target, [2] = 0x1FFC }
        };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.WeighTAMatch));
        Assert.Equal(32767u, state.D[0]);

        memory.TryWriteByte(target + (uint)GraphicsLayouts.TextAttrStyle, 0);
        Assert.True(fonts.TryWeighTAMatch(requested, target, 0, out var styleWeight));
        Assert.InRange(styleWeight, 0, (short)32766);

        Assert.False(fonts.TryWeighTAMatch(0x1FFC, target, 0, out var malformedWeight));
        Assert.Equal((short)0, malformedWeight);
        state.A[0] = 0x1FFC;
        state.D[0] = 0xDEADBEEF;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.WeighTAMatch));
        Assert.Equal(0u, state.D[0]);
    }

    [Fact]
    public void WeighTAMatchUsesTaggedDeviceDpiAspectRatiosAndValidatesChains()
    {
        var memory = new FakeMemory(0x2000);
        var fonts = new GraphicsMemoryFontBackend(memory);
        const uint requested = 0x100;
        const uint target = 0x120;
        const uint requestedTags = 0x180;
        const uint equivalentTargetTags = 0x1A0;
        const uint mismatchedTargetTags = 0x1C0;

        WriteTextAttr(memory, requested, 0, ySize: 10, style: 0x82, flags: 0);
        WriteTextAttr(memory, target, 0, ySize: 10, style: 0x02, flags: 0);
        Assert.True(memory.TryWriteLong(requested + (uint)GraphicsLayouts.TextAttrSize, requestedTags));

        // TA_DeviceDPI is an aspect ratio: 75:50 and 150:100 are equivalent
        // even though their raw values differ.
        WriteTag(memory, requestedTags, 0x8000_0001, (75u << 16) | 50u);
        WriteTag(memory, requestedTags + 8, GraphicsRastPortAttributeOperations.TagDone, 0);
        WriteTag(memory, equivalentTargetTags, 0x8000_0001, (150u << 16) | 100u);
        WriteTag(memory, equivalentTargetTags + 8, GraphicsRastPortAttributeOperations.TagDone, 0);
        WriteTag(memory, mismatchedTargetTags, 0x8000_0001, (50u << 16) | 50u);
        WriteTag(memory, mismatchedTargetTags + 8, GraphicsRastPortAttributeOperations.TagDone, 0);

        Assert.True(fonts.TryWeighTAMatch(requested, target, equivalentTargetTags, out var equivalentWeight));
        Assert.Equal((short)32767, equivalentWeight);
        Assert.True(fonts.TryWeighTAMatch(requested, target, mismatchedTargetTags, out var mismatchedWeight));
        Assert.InRange(mismatchedWeight, (short)1, (short)(equivalentWeight - 1));

        // An ordinary TextAttr does not promise a tta_Tags suffix, so the
        // target tag pointer is intentionally not dereferenced in that mode.
        WriteTextAttr(memory, requested, 0, ySize: 10, style: 0x02, flags: 0);
        Assert.True(fonts.TryWeighTAMatch(requested, target, 0x1FFC, out var untaggedWeight));
        Assert.Equal((short)32767, untaggedWeight);

        // A tagged request does promise the suffix; a truncated list must
        // fail closed rather than return a score based on partial input.
        WriteTextAttr(memory, requested, 0, ySize: 10, style: 0x82, flags: 0);
        Assert.True(memory.TryWriteLong(requested + (uint)GraphicsLayouts.TextAttrSize, 0x1FFCu));
        Assert.True(memory.TryWriteLong(0x1FFCu, 0x8000_0001u));
        Assert.False(fonts.TryWeighTAMatch(requested, target, equivalentTargetTags, out var malformedWeight));
        Assert.Equal((short)0, malformedWeight);
    }

    [Fact]
    public void FontLifecycleFailsClosedForMalformedRequestsAccessorOverflowAndAllocationFailure()
    {
        var memory = new FakeMemory(0x4000);
        const uint font = 0x600;
        const uint name = 0x500;
        const uint textAttr = 0x400;
        WriteFontHeader(memory, font, name, ySize: 9, style: 0, flags: 1);
        WriteAscii(memory, name, "safe.font");

        var fonts = new GraphicsMemoryFontBackend(memory);
        Assert.True(fonts.TryAdd(font));
        WriteTextAttr(memory, textAttr, 0x4000, ySize: 9, style: 0, flags: 0);
        Assert.False(fonts.TryOpen(textAttr, out var malformedOpen));
        Assert.Equal(0u, malformedOpen);
        Assert.Equal((ushort)0, ReadWord(memory, font + (uint)GraphicsLayouts.TextFontAccessors));

        Assert.True(memory.TryWriteWord(
            font + (uint)GraphicsLayouts.TextFontAccessors,
            ushort.MaxValue));
        WriteTextAttr(memory, textAttr, name, ySize: 9, style: 0, flags: 0);
        Assert.False(fonts.TryOpen(textAttr, out var overflowOpen));
        Assert.Equal(0u, overflowOpen);
        Assert.Equal(ushort.MaxValue, ReadWord(memory, font + (uint)GraphicsLayouts.TextFontAccessors));

        var failingAllocator = new BitmapAllocator { FailAfterAllocations = 0 };
        var failingFonts = new GraphicsMemoryFontBackend(memory, failingAllocator);
        Assert.True(memory.TryWriteWord(
            font + (uint)GraphicsLayouts.TextFontAccessors,
            0));
        Assert.False(failingFonts.TryExtend(font, 0x900));
        Assert.Equal(0u, ReadLong(memory, font + (uint)GraphicsLayouts.TextFontExtension));
        Assert.Equal((byte)0, ReadByte(memory, font + (uint)GraphicsLayouts.TextFontStyle));
        Assert.Empty(failingAllocator.Freed);
    }

    [Fact]
    public void GuestGfxBaseFontListPublishesDefaultLinksAndRemovedState()
    {
        var memory = new FakeMemory(0x4000);
        const uint gfxBase = 0x1000;
        const uint font = 0x600;
        const uint name = 0x500;
        var list = new GraphicsGuestFontListBackend(memory, gfxBase);
        var fonts = new GraphicsMemoryFontBackend(memory, defaultFont: () => font, fontList: list);
        WriteFontHeader(memory, font, name, ySize: 8, style: 0, flags: 1);
        WriteAscii(memory, name, "topaz.font");

        Assert.True(list.Initialize(0));
        Assert.True(list.SetDefaultFont(font));
        Assert.Equal(font, ReadLong(memory, gfxBase + (uint)GraphicsLayouts.GfxBaseDefaultFont));
        Assert.Equal(gfxBase + (uint)GraphicsLayouts.GfxBaseTextFontsTail,
            ReadLong(memory, gfxBase + (uint)GraphicsLayouts.GfxBaseTextFontsHead));

        Assert.True(fonts.TryAdd(font));
        Assert.False(fonts.TryAdd(font));
        Assert.Equal(font, ReadLong(memory, gfxBase + (uint)GraphicsLayouts.GfxBaseTextFontsHead));
        Assert.Equal(gfxBase + (uint)GraphicsLayouts.GfxBaseTextFontsTail,
            ReadLong(memory, font));
        Assert.Equal(gfxBase + (uint)GraphicsLayouts.GfxBaseTextFonts,
            ReadLong(memory, font + 4));
        Assert.Equal((byte)1, (byte)(ReadByte(memory, font + (uint)GraphicsLayouts.TextFontFlags) & 0x7F));

        Assert.True(fonts.TryRemove(font));
        Assert.Equal(gfxBase + (uint)GraphicsLayouts.GfxBaseTextFontsTail,
            ReadLong(memory, gfxBase + (uint)GraphicsLayouts.GfxBaseTextFontsHead));
        Assert.Equal(gfxBase + (uint)GraphicsLayouts.GfxBaseTextFonts,
            ReadLong(memory, gfxBase + (uint)GraphicsLayouts.GfxBaseTextFontsTailPred));
        Assert.Equal(0u, ReadLong(memory, font));
        Assert.Equal(0u, ReadLong(memory, font + 4));
        Assert.Equal((byte)0x80, (byte)(ReadByte(memory, font + (uint)GraphicsLayouts.TextFontFlags) & 0x80));
        Assert.False(fonts.TryRemove(font));
    }

    [Fact]
    public void GuestGfxBaseFontListRejectsMalformedExistingLinks()
    {
        var memory = new FakeMemory(0x4000);
        const uint gfxBase = 0x1000;
        const uint font = 0x600;
        const uint name = 0x500;
        var list = new GraphicsGuestFontListBackend(memory, gfxBase);
        var fonts = new GraphicsMemoryFontBackend(memory, fontList: list);
        WriteFontHeader(memory, font, name, ySize: 8, style: 0, flags: 1);
        WriteAscii(memory, name, "topaz.font");
        Assert.True(list.Initialize(0));

        // A non-empty list must have a first node whose predecessor is the
        // list header and a last node whose successor is the tail sentinel.
        Assert.True(memory.TryWriteLong(gfxBase + (uint)GraphicsLayouts.GfxBaseTextFontsHead, 0x3000));
        Assert.False(fonts.TryAdd(font));
        Assert.Equal(0u, ReadLong(memory, font));
        Assert.Equal(0u, ReadLong(memory, font + 4));
    }

    [Fact]
    public void ExtendFontBuildsAnOwnedGuestExtensionAndStripReleasesIt()
    {
        var memory = new FakeMemory(0x4000);
        var allocator = new FakeAllocator();
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), new FakeDisplay());
        var fonts = new GraphicsMemoryFontBackend(memory, allocator);
        const uint font = 0x600;
        const uint name = 0x500;
        const uint tags = 0x900;
        WriteFontHeader(memory, font, name, ySize: 8, style: 0x04, flags: 1);

        Assert.True(core.ExtendFont(font, tags, fonts));
        var extension = ReadLong(memory, font + (uint)GraphicsLayouts.TextFontExtension);
        Assert.NotEqual(0u, extension);
        Assert.Equal((byte)0x84, ReadByte(memory, font + (uint)GraphicsLayouts.TextFontStyle));
        Assert.Equal((ushort)0xF00D, ReadWord(memory, extension + (uint)GraphicsLayouts.TextFontExtensionMatchWord));
        Assert.Equal(font, ReadLong(memory, extension + (uint)GraphicsLayouts.TextFontExtensionBackPtr));
        Assert.Equal(tags, ReadLong(memory, extension + (uint)GraphicsLayouts.TextFontExtensionTags));
        Assert.Equal(0u, ReadLong(memory, extension + (uint)GraphicsLayouts.TextFontExtensionOrigReplyPort));
        Assert.True(core.ExtendFont(font, tags + 4, fonts));
        Assert.Equal(0, core.StripFont(font, fonts));
        Assert.Equal(0u, ReadLong(memory, font + (uint)GraphicsLayouts.TextFontExtension));
        Assert.Equal((byte)0x04, ReadByte(memory, font + (uint)GraphicsLayouts.TextFontStyle));
        Assert.Equal(extension, allocator.LastFreedAddress);
    }

    [Fact]
    public void StripFontLeavesExternalExtensionAndStyleOwnershipUntouched()
    {
        var memory = new FakeMemory(0x4000);
        var allocator = new FakeAllocator();
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), new FakeDisplay());
        var fonts = new GraphicsMemoryFontBackend(memory, allocator);
        const uint font = 0x600;
        const uint name = 0x500;
        const uint externalExtension = 0x1200;
        const byte externalStyle = 0x84;
        WriteFontHeader(memory, font, name, ySize: 8, style: externalStyle, flags: 1);
        Assert.True(memory.TryWriteLong(font + (uint)GraphicsLayouts.TextFontExtension, externalExtension));

        // An extension already installed by another owner is accepted by
        // ExtendFont, but StripFont must not detach or free that memory.
        Assert.True(core.ExtendFont(font, 0x900, fonts));
        Assert.Equal(0, allocator.FreeCount);
        Assert.Equal(0, core.StripFont(font, fonts));
        Assert.Equal(externalExtension, ReadLong(memory, font + (uint)GraphicsLayouts.TextFontExtension));
        Assert.Equal(externalStyle, ReadByte(memory, font + (uint)GraphicsLayouts.TextFontStyle));
        Assert.Equal(0, allocator.FreeCount);
    }

    [Fact]
    public void TextExtentFontExtentAndTextFitWriteGuestTextExtentStructures()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        var fonts = new FakeFontBackend();
        const uint font = 0x300;
        const uint text = 0x320;
        const uint extent = 0x340;
        const uint emptyExtent = 0x380;
        memory.TryWriteByte(text, (byte)'A');
        memory.TryWriteByte(text + 1, (byte)'A');

        Assert.Equal(0, core.SetFont(0x20, font, fonts));
        // A non-empty string must have a valid guest address.  The cached
        // TextLength overload remains available for callers that do not pass
        // a decoded font, but decoded metric paths fail closed instead of
        // treating address zero as a readable character stream.
        Assert.Equal(-1, core.TextLength(0x20, 0, 1, fonts));
        Assert.Equal(-1, core.TextExtent(0x20, 0, 1, extent, fonts));
        Assert.Equal(-1, core.TextFit(0x20, 0, 1, extent, 0, 1, 8, 3, fonts));
        Assert.Equal(0, core.TextExtent(0x20, text, 1, extent, fonts));
        Assert.Equal((ushort)4, ReadWord(memory, extent + (uint)GraphicsLayouts.TextExtentWidth));
        Assert.Equal((ushort)3, ReadWord(memory, extent + (uint)GraphicsLayouts.TextExtentHeight));
        Assert.Equal((short)0, unchecked((short)ReadWord(memory, extent + (uint)GraphicsLayouts.TextExtentMinX)));
        Assert.Equal((short)-3, unchecked((short)ReadWord(memory, extent + (uint)GraphicsLayouts.TextExtentMinY)));
        Assert.Equal((short)2, unchecked((short)ReadWord(memory, extent + (uint)GraphicsLayouts.TextExtentMaxX)));
        Assert.Equal((short)-1, unchecked((short)ReadWord(memory, extent + (uint)GraphicsLayouts.TextExtentMaxY)));

        Assert.Equal(0, core.FontExtent(font, extent, fonts));
        // FontExtent follows the one-character TextLength advance, not only
        // the bitmap cell width.  The fake glyph's CharSpace is four pixels.
        Assert.Equal((ushort)4, ReadWord(memory, extent + (uint)GraphicsLayouts.TextExtentWidth));
        Assert.Equal((ushort)3, ReadWord(memory, extent + (uint)GraphicsLayouts.TextExtentHeight));

        const uint explicitConstraint = 0x3C0;
        memory.TryWriteWord(explicitConstraint + (uint)GraphicsLayouts.TextExtentWidth, 1);
        memory.TryWriteWord(explicitConstraint + (uint)GraphicsLayouts.TextExtentHeight, 1);
        memory.TryWriteWord(explicitConstraint + (uint)GraphicsLayouts.TextExtentMinX, 0);
        memory.TryWriteWord(explicitConstraint + (uint)GraphicsLayouts.TextExtentMinY, unchecked((ushort)-3));
        memory.TryWriteWord(explicitConstraint + (uint)GraphicsLayouts.TextExtentMaxX, 2);
        memory.TryWriteWord(explicitConstraint + (uint)GraphicsLayouts.TextExtentMaxY, unchecked((ushort)-1));
        Assert.Equal(1, core.TextFit(0x20, text, 1, extent, explicitConstraint, 1, 1, 1, fonts));

        Assert.Equal(1, core.TextFit(0x20, text, 1, extent, 0, 1, 4, 3, fonts));
        Assert.Equal((ushort)4, ReadWord(memory, extent + (uint)GraphicsLayouts.TextExtentWidth));
        Assert.Equal(0, core.TextFit(0x20, text, 1, emptyExtent, 0, 1, 3, 3, fonts));
        Assert.Equal((ushort)0, ReadWord(memory, emptyExtent + (uint)GraphicsLayouts.TextExtentWidth));
        Assert.Equal((ushort)0, ReadWord(memory, emptyExtent + (uint)GraphicsLayouts.TextExtentHeight));

        // TextFit's reverse form is anchored at the final character, not at
        // the first byte of the string.  Passing text+1 with direction -1
        // must therefore measure both A glyphs without touching text-1.
        Assert.Equal(2, core.TextFit(0x20, text + 1, 2, extent, 0, -1, 8, 3, fonts));
        Assert.Equal((ushort)8, ReadWord(memory, extent + (uint)GraphicsLayouts.TextExtentWidth));

        // A spacing value that cancels the glyph advance reverses/stalls the
        // path.  Kickstart reports no fit and clears the output extent.
        Assert.True(memory.TryWriteWord(
            0x20u + (uint)GraphicsLayouts.RastPortTextSpacing,
            unchecked((ushort)-4)));
        Assert.Equal(0, core.TextFit(0x20, text, 2, extent, 0, 1, 8, 3, fonts));
        Assert.Equal((ushort)0, ReadWord(memory, extent + (uint)GraphicsLayouts.TextExtentWidth));
        Assert.Equal((ushort)0, ReadWord(memory, extent + (uint)GraphicsLayouts.TextExtentHeight));
    }

    [Fact]
    public void FontExtentIncludesKerningBoundsAndUsesReversePathWidthDirection()
    {
        var memory = new FakeMemory(0x1000);
        var core = CreateCore(memory);
        const uint font = 0x200;
        const uint extent = 0x300;
        var glyphs = new Dictionary<byte, GraphicsGlyph>
        {
            // A left kerning offset is reflected in the bounding rectangle,
            // but is ignored when a normal font reports its width.
            [(byte)'A'] = new GraphicsGlyph(3, 8, 4, -2, 0, 0, 0),
            // A negative CharSpace is ignored for a normal font's width while
            // its right-side bitmap extent remains part of the bound.
            [(byte)'B'] = new GraphicsGlyph(6, 8, -5, 1, 0, 0, 0)
        };

        Assert.Equal(0, core.FontExtent(
            font,
            extent,
            new TableFontBackend(new GraphicsFontMetrics(8, 3, 6, 0), glyphs)));
        Assert.Equal((ushort)4, ReadWord(memory, extent + (uint)GraphicsLayouts.TextExtentWidth));
        Assert.Equal((short)-2, unchecked((short)ReadWord(memory, extent + (uint)GraphicsLayouts.TextExtentMinX)));
        Assert.Equal((short)6, unchecked((short)ReadWord(memory, extent + (uint)GraphicsLayouts.TextExtentMaxX)));

        Assert.Equal(0, core.FontExtent(
            font,
            extent,
            new TableFontBackend(new GraphicsFontMetrics(8, 10, 6, 0, flags: 0x24), glyphs)));
        Assert.Equal((short)-5, unchecked((short)ReadWord(memory, extent + (uint)GraphicsLayouts.TextExtentWidth)));
        Assert.Equal((short)-2, unchecked((short)ReadWord(memory, extent + (uint)GraphicsLayouts.TextExtentMinX)));
        Assert.Equal((short)6, unchecked((short)ReadWord(memory, extent + (uint)GraphicsLayouts.TextExtentMaxX)));
    }

    [Fact]
    public void ViewActivationAndSynchronizationStayInsideTheDisplayBackendBoundary()
    {
        var memory = new FakeMemory(0x2000);
        var display = new FakeDisplay { BeamPosition = 0x1234 };
        var core = new GraphicsLibraryCore(memory, new FakeAllocator(), new FakeBlitter(), display);

        Assert.Equal(0, core.LoadView(0x180));
        Assert.Equal(0x180u, display.PublishedView);
        Assert.Equal(0, core.WaitTOF());
        memory.TryWriteLong(0x1A0u + (uint)GraphicsLayouts.ViewPortRasInfo, 0x300);
        memory.TryWriteLong(0x300u + (uint)GraphicsLayouts.RasInfoNext, 0);
        memory.TryWriteLong(0x300u + (uint)GraphicsLayouts.RasInfoBitMap, 0);
        memory.TryWriteWord(0x300u + (uint)GraphicsLayouts.RasInfoRxOffset, 0);
        memory.TryWriteWord(0x300u + (uint)GraphicsLayouts.RasInfoRyOffset, 0);
        Assert.Equal(0, core.WaitBOVP(0x1A0));
        Assert.Equal((ushort)0x1234, core.VBeamPos());
        Assert.True(display.WaitedForTopOfFrame);
        Assert.Equal(0x1A0u, display.WaitedForViewPort);
        Assert.Equal(-1, core.WaitBOVP(0x1FF8));
        Assert.Equal(0x1A0u, display.WaitedForViewPort);

        var publishedBeforeMalformedView = display.PublishedView;
        Assert.Equal(-1, core.LoadView(0x1FF8));
        Assert.Equal(publishedBeforeMalformedView, display.PublishedView);
    }

    [Fact]
    public void LoadViewWalksTheEntireViewPortChainBeforePublishing()
    {
        var memory = new FakeMemory(0x1000);
        var display = new FakeDisplay();
        var core = CreateCore(memory, display);
        const uint view = 0x100;
        const uint firstViewPort = 0x180;
        const uint secondViewPort = 0x220;

        memory.TryWriteLong(view + (uint)GraphicsLayouts.ViewViewPort, firstViewPort);
        memory.TryWriteLong(firstViewPort + (uint)GraphicsLayouts.ViewPortNext, secondViewPort);
        memory.TryWriteLong(secondViewPort + (uint)GraphicsLayouts.ViewPortNext, 0);
        memory.TryWriteWord(firstViewPort + (uint)GraphicsLayouts.ViewPortDWidth, 320);
        memory.TryWriteWord(secondViewPort + (uint)GraphicsLayouts.ViewPortDWidth, 320);

        Assert.Equal(0, core.LoadView(view));
        Assert.Equal(view, display.PublishedView);

        memory.TryWriteLong(secondViewPort + (uint)GraphicsLayouts.ViewPortNext, 0x0FF8);
        Assert.Equal(-1, core.LoadView(view));
        Assert.Equal(view, display.PublishedView);

        memory.TryWriteLong(secondViewPort + (uint)GraphicsLayouts.ViewPortNext, firstViewPort);
        Assert.Equal(-1, core.LoadView(view));
        Assert.Equal(view, display.PublishedView);
    }

    [Fact]
    public void LoadViewNullClearsThePublishedViewWithoutTouchingGuestMemory()
    {
        var memory = new FakeMemory(0x1000);
        var display = new FakeDisplay();
        var core = CreateCore(memory, display);
        const uint view = 0x180;

        Assert.Equal(0, core.LoadView(view));
        Assert.Equal(view, display.PublishedView);

        // NULL disables the current display and does not require a guest
        // View envelope.  The host publication boundary still receives the
        // clear so the guest-compatible ActiView state cannot go stale.
        Assert.Equal(0, core.LoadView(0));
        Assert.Equal(0u, display.PublishedView);
        Assert.Equal(0u, ReadLong(memory, view + (uint)GraphicsLayouts.ViewViewPort));
    }

    [Fact]
    public void LoadViewRejectsAPresentMalformedRasInfoBitMapBeforePublishing()
    {
        var memory = new FakeMemory(0x2000);
        var display = new FakeDisplay();
        var core = CreateCore(memory, display);
        const uint view = 0x100;
        const uint viewPort = 0x180;
        const uint rasInfo = 0x220;
        const uint bitMap = 0x260;

        memory.TryWriteLong(view + (uint)GraphicsLayouts.ViewViewPort, viewPort);
        memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortNext, 0);
        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDWidth, 320);
        memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, bitMap);

        // A present bitmap link with zero geometry is malformed.
        Assert.Equal(-1, core.LoadView(view));
        Assert.Equal(0u, display.PublishedView);

        Assert.True(memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 2));
        Assert.True(memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, 1));
        Assert.Equal(-1, core.LoadView(view));
        Assert.Equal(0u, display.PublishedView);

        Assert.True(memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapRows, 1));
        Assert.True(memory.TryWriteLong(bitMap + (uint)GraphicsLayouts.BitMapPlanes, 0x400));
        Assert.Equal(0, core.LoadView(view));
        Assert.Equal(view, display.PublishedView);
    }

    [Fact]
    public void LoadViewRejectsDisplayPlaneStorageThatFallsOutsideGuestMemory()
    {
        var memory = new FakeMemory(0x2000);
        var display = new FakeDisplay();
        var core = CreateCore(memory, display);
        const uint view = 0x100;
        const uint viewPort = 0x180;
        const uint rasInfo = 0x220;
        const uint bitMap = 0x260;

        memory.TryWriteLong(view + (uint)GraphicsLayouts.ViewViewPort, viewPort);
        memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortNext, 0);
        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDWidth, 320);
        memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, bitMap);

        memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 2);
        memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapRows, 2);
        memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, 1);
        // The first row is readable, but the second row crosses the end of
        // the guest memory envelope.  LoadView must reject before publishing.
        memory.TryWriteLong(bitMap + (uint)GraphicsLayouts.BitMapPlanes, 0x1FFF);

        Assert.Equal(-1, core.LoadView(view));
        Assert.Equal(0u, display.PublishedView);
    }

    [Fact]
    public void LoadViewRejectsNonWordAlignedDisplayBitmapRowsAndPlanes()
    {
        var memory = new FakeMemory(0x2000);
        var display = new FakeDisplay();
        var core = CreateCore(memory, display);
        const uint view = 0x100;
        const uint viewPort = 0x180;
        const uint rasInfo = 0x220;
        const uint bitMap = 0x260;

        memory.TryWriteLong(view + (uint)GraphicsLayouts.ViewViewPort, viewPort);
        memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortNext, 0);
        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDWidth, 320);
        memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, bitMap);
        memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapRows, 1);
        memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, 1);
        memory.TryWriteLong(bitMap + (uint)GraphicsLayouts.BitMapPlanes, 0x400);

        memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 3);
        Assert.Equal(-1, core.LoadView(view));
        Assert.Equal(0u, display.PublishedView);

        memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 2);
        memory.TryWriteLong(bitMap + (uint)GraphicsLayouts.BitMapPlanes, 0x401);
        Assert.Equal(-1, core.LoadView(view));
        Assert.Equal(0u, display.PublishedView);

        memory.TryWriteLong(bitMap + (uint)GraphicsLayouts.BitMapPlanes, 0x400);
        Assert.Equal(0, core.LoadView(view));
        Assert.Equal(view, display.PublishedView);
    }

    [Fact]
    public void MakeVPortAndMrgCopValidateGuestStructuresBeforeCallingCopperBoundary()
    {
        var memory = new FakeMemory(0x1000);
        var display = new FakeDisplay();
        var core = CreateCore(memory, display);
        const uint view = 0x100;
        const uint viewPort = 0x180;
        const uint rasInfo = 0x1C0;
        const uint bitMap = 0x300;

        memory.TryWriteLong(view + (uint)GraphicsLayouts.ViewViewPort, viewPort);
        memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, bitMap);
        memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 4);
        memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, 2);

        Assert.Equal(0, core.MakeViewPort(view, viewPort));
        Assert.Equal(view, display.MadeView);
        Assert.Equal(viewPort, display.MadeViewPort);
        Assert.Equal(1, display.MakeCount);
        Assert.Equal(0, core.MergeCopperLists(view));
        Assert.Equal(view, display.MergedView);
        Assert.Equal(1, display.StatusMergeCount);

        Assert.Equal(GraphicsViewportStatuses.NoViewPortExtra, core.MakeViewPort(0x0FF8, viewPort));
        Assert.Equal(-1, core.MergeCopperLists(0x0FF8));
        Assert.Equal(1, display.MakeCount);
        Assert.Equal(1, display.StatusMergeCount);

        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        var state = new M68kCpuState { A = { [0] = 0x0FF8, [1] = viewPort } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.MakeVPort));
        Assert.Equal((uint)GraphicsViewportStatuses.NoViewPortExtra, state.D[0]);
    }

    [Fact]
    public void MakeVPortReportsNoDisplayWhenTheValidatedGuestDataHasNoCopperBoundary()
    {
        var memory = new FakeMemory(0x1000);
        var display = new FakeDisplayWithoutCopper();
        var core = CreateCore(memory, display);
        const uint view = 0x100;
        const uint viewPort = 0x180;
        const uint rasInfo = 0x1C0;
        const uint bitMap = 0x300;

        memory.TryWriteLong(view + (uint)GraphicsLayouts.ViewViewPort, viewPort);
        memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, bitMap);
        memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 4);
        memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, 1);

        Assert.Equal(GraphicsViewportStatuses.NoDisplay, core.MakeViewPort(view, viewPort));
    }

    [Fact]
    public void MakeVPortPropagatesStatusFromAStatusAwareCopperBoundary()
    {
        var memory = new FakeMemory(0x1000);
        var display = new FakeDisplayWithMakeStatus { MakeStatus = GraphicsViewportStatuses.NoDisplayInstructions };
        var core = CreateCore(memory, display);
        const uint view = 0x100;
        const uint viewPort = 0x180;
        const uint rasInfo = 0x1C0;
        const uint bitMap = 0x300;

        memory.TryWriteLong(view + (uint)GraphicsLayouts.ViewViewPort, viewPort);
        memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, bitMap);
        memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 4);
        memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, 1);

        Assert.Equal(GraphicsViewportStatuses.NoDisplayInstructions, core.MakeViewPort(view, viewPort));
        Assert.Equal(1, display.MakeStatusCount);

        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        var state = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.MakeVPort));
        Assert.Equal((uint)GraphicsViewportStatuses.NoDisplayInstructions, state.D[0]);
        Assert.Equal(2, display.MakeStatusCount);
    }

    [Fact]
    public void MrgCopReturnsNoOpForAValidViewWithoutViewPorts()
    {
        var memory = new FakeMemory(0x1000);
        var display = new FakeDisplay();
        var core = CreateCore(memory, display);
        const uint view = 0x100;

        Assert.True(memory.TryWriteLong(view + (uint)GraphicsLayouts.ViewViewPort, 0));
        Assert.Equal(GraphicsCopperOperations.MergeNoOp, core.MergeCopperLists(view));
        Assert.Equal(0, display.MergeCount);

        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        var state = new M68kCpuState { A = { [1] = view }, D = { [0] = 0xFFFF_FFFF } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.MrgCop));
        Assert.Equal((uint)GraphicsCopperOperations.MergeNoOp, state.D[0]);
        Assert.Equal(0, display.MergeCount);
    }

    [Fact]
    public void MrgCopPropagatesStatusFromAStatusAwareCopperBoundary()
    {
        var memory = new FakeMemory(0x1000);
        var display = new FakeDisplay { MergeStatus = GraphicsCopperOperations.MergeNoMemory };
        var core = CreateCore(memory, display);
        const uint view = 0x100;
        const uint viewPort = 0x180;
        const uint rasInfo = 0x1C0;
        const uint bitMap = 0x300;

        memory.TryWriteLong(view + (uint)GraphicsLayouts.ViewViewPort, viewPort);
        memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, bitMap);
        memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 4);
        memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, 1);

        Assert.Equal(GraphicsCopperOperations.MergeNoMemory, core.MergeCopperLists(view));
        Assert.Equal(1, display.StatusMergeCount);
        Assert.Equal(0, display.MergeCount);

        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        var state = new M68kCpuState { A = { [1] = view } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.MrgCop));
        Assert.Equal((uint)GraphicsCopperOperations.MergeNoMemory, state.D[0]);
        Assert.Equal(2, display.StatusMergeCount);
    }

    [Fact]
    public void FreeCprListValidatesThePublicEnvelopeBeforeCallingTheOptionalResourceBoundary()
    {
        var memory = new FakeMemory(0x1000);
        var display = new FakeDisplay();
        var core = CreateCore(memory, display);
        const uint cprList = 0x180;

        Assert.True(memory.TryWriteLong(cprList + (uint)GraphicsLayouts.CprListNext, 0));
        Assert.True(memory.TryWriteLong(cprList + (uint)GraphicsLayouts.CprListStart, 0x240));
        Assert.True(memory.TryWriteLong(cprList + (uint)GraphicsLayouts.CprListMaxCount, 64));

        Assert.True(core.FreeCprList(cprList));
        Assert.Equal(cprList, display.FreedCprList);
        Assert.Equal(1, display.FreeCprListCount);

        Assert.False(core.FreeCprList(0));
        Assert.False(core.FreeCprList(0x0FF5));
        Assert.False(core.FreeCprList(0x0FF8));
        Assert.Equal(1, display.FreeCprListCount);

        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        var state = new M68kCpuState { A = { [0] = cprList }, D = { [0] = 0xFFFF_FFFF } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.FreeCprList));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal(2, display.FreeCprListCount);
    }

    [Fact]
    public void UCopperListInitPublishesKickstartCopListAndIntermediateBufferLayout()
    {
        var memory = new FakeMemory(0x4000);
        var allocator = new BitmapAllocator();
        var core = new GraphicsLibraryCore(
            memory,
            allocator,
            new FakeBlitter(),
            new FakeDisplay());
        const uint userList = 0x200;
        const ushort instructionCount = 3;

        for (var offset = 0u; offset < (uint)GraphicsLayouts.UCopListSize; offset++)
            Assert.True(memory.TryWriteByte(userList + offset, 0xA5));

        var copList = core.UCopperListInit(userList, instructionCount);

        Assert.NotEqual(0u, copList);
        Assert.Equal(copList, ReadLong(memory, userList + (uint)GraphicsLayouts.UCopListFirstCopList));
        Assert.Equal(copList, ReadLong(memory, userList + (uint)GraphicsLayouts.UCopListCopList));
        Assert.Equal(0u, ReadLong(memory, userList + (uint)GraphicsLayouts.UCopListNext));
        var instructions = ReadLong(memory, copList + (uint)GraphicsLayouts.CopListCopIns);
        Assert.NotEqual(0u, instructions);
        Assert.Equal(instructions, ReadLong(memory, copList + (uint)GraphicsLayouts.CopListCopPtr));
        Assert.Equal((ushort)0, ReadWord(memory, copList + (uint)GraphicsLayouts.CopListCount));
        Assert.Equal(instructionCount, ReadWord(memory, copList + (uint)GraphicsLayouts.CopListMaxCount));
        Assert.Equal((ushort)0, ReadWord(memory, copList + (uint)GraphicsLayouts.CopListDyOffset));
        Assert.Equal((ushort)0, ReadWord(memory, instructions + (uint)GraphicsLayouts.CopInsOpCode));
        Assert.Equal((ushort)0, ReadWord(memory, instructions + (uint)GraphicsLayouts.CopInsArg0));
        Assert.Equal((ushort)0, ReadWord(memory, instructions + (uint)GraphicsLayouts.CopInsArg1));
        Assert.Empty(allocator.Freed);
    }

    [Fact]
    public void UCopperListInitRejectsMalformedOrDuplicateListsAndRollsBackInternalAllocations()
    {
        var memory = new FakeMemory(0x4000);
        var allocator = new BitmapAllocator();
        var core = new GraphicsLibraryCore(
            memory,
            allocator,
            new FakeBlitter(),
            new FakeDisplay());

        Assert.Equal(0u, core.UCopperListInit(0, 2));
        Assert.Equal(0u, core.UCopperListInit(0x3FF8, 2));
        Assert.Empty(allocator.Freed);

        const uint userList = 0x240;
        var first = core.UCopperListInit(userList, 2);
        Assert.NotEqual(0u, first);
        Assert.Equal(0u, core.UCopperListInit(userList, 2));
        Assert.Empty(allocator.Freed);

        var failingAllocator = new BitmapAllocator { FailAfterAllocations = 1 };
        var failingCore = new GraphicsLibraryCore(
            memory,
            failingAllocator,
            new FakeBlitter(),
            new FakeDisplay());
        Assert.Equal(0u, failingCore.UCopperListInit(0x280, 2));
        Assert.Single(failingAllocator.Freed);
        Assert.Equal((uint)GraphicsLayouts.CopListSize, failingAllocator.Freed[0].Bytes);
        Assert.Equal(GraphicsMemoryClass.Public, failingAllocator.Freed[0].MemoryClass);

        var adapterAllocator = new BitmapAllocator();
        var adapterCore = new GraphicsLibraryCore(
            memory,
            adapterAllocator,
            new FakeBlitter(),
            new FakeDisplay());
        var adapter = new CopperStartGraphicsRegisterAdapter(adapterCore);
        var state = new M68kCpuState
        {
            A = { [0] = 0x2C0 },
            D = { [0] = 2 }
        };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.UCopperListInit));
        Assert.NotEqual(0u, state.D[0]);
    }

    [Fact]
    public void UserCopperInstructionVectorsWritePseudoInstructionsAndBumpSafely()
    {
        var memory = new FakeMemory(0x4000);
        var core = new GraphicsLibraryCore(
            memory,
            new BitmapAllocator(),
            new FakeBlitter(),
            new FakeDisplay());
        const uint userList = 0x320;
        const ushort capacity = 3;
        var copList = core.UCopperListInit(userList, capacity);
        var instructions = ReadLong(memory, copList + (uint)GraphicsLayouts.CopListCopIns);

        Assert.True(core.CMove(userList, 0x00DFF180, 0x1234));
        Assert.Equal((ushort)0, ReadWord(memory, instructions + (uint)GraphicsLayouts.CopInsOpCode));
        Assert.Equal((ushort)0x0180, ReadWord(memory, instructions + (uint)GraphicsLayouts.CopInsArg0));
        Assert.Equal((ushort)0x1234, ReadWord(memory, instructions + (uint)GraphicsLayouts.CopInsArg1));
        Assert.Equal((ushort)0, ReadWord(memory, copList + (uint)GraphicsLayouts.CopListCount));
        Assert.Equal(instructions, ReadLong(memory, copList + (uint)GraphicsLayouts.CopListCopPtr));

        Assert.True(core.CBump(userList));
        Assert.Equal((ushort)1, ReadWord(memory, copList + (uint)GraphicsLayouts.CopListCount));
        Assert.Equal(instructions + (uint)GraphicsLayouts.CopInsSize, ReadLong(memory, copList + (uint)GraphicsLayouts.CopListCopPtr));

        var second = instructions + (uint)GraphicsLayouts.CopInsSize;
        Assert.True(core.CWait(userList, 42, 12));
        Assert.Equal((ushort)1, ReadWord(memory, second + (uint)GraphicsLayouts.CopInsOpCode));
        Assert.Equal((ushort)42, ReadWord(memory, second + (uint)GraphicsLayouts.CopInsArg0));
        Assert.Equal((ushort)12, ReadWord(memory, second + (uint)GraphicsLayouts.CopInsArg1));
        Assert.True(core.CBump(userList));

        Assert.True(core.CEnd(userList));
        var end = instructions + (2u * (uint)GraphicsLayouts.CopInsSize);
        Assert.Equal((ushort)1, ReadWord(memory, end + (uint)GraphicsLayouts.CopInsOpCode));
        Assert.Equal((ushort)10000, ReadWord(memory, end + (uint)GraphicsLayouts.CopInsArg0));
        Assert.Equal((ushort)255, ReadWord(memory, end + (uint)GraphicsLayouts.CopInsArg1));
        Assert.Equal(capacity, ReadWord(memory, copList + (uint)GraphicsLayouts.CopListCount));
        Assert.True(core.CBump(userList));
        var nextCopList = ReadLong(memory, userList + (uint)GraphicsLayouts.UCopListCopList);
        Assert.NotEqual(copList, nextCopList);
        Assert.True(core.CMove(userList, 0x180, 0x5678));
        var nextInstructions = ReadLong(memory, nextCopList + (uint)GraphicsLayouts.CopListCopIns);
        Assert.Equal((ushort)0x5678, ReadWord(memory, nextInstructions + (uint)GraphicsLayouts.CopInsArg1));

        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        Assert.True(memory.TryWriteLong(
            nextCopList + (uint)GraphicsLayouts.CopListCopPtr,
            0x3FF0));
        var forged = new M68kCpuState { A = { [1] = userList }, D = { [0] = 0x180, [1] = 0x5678 } };
        Assert.False(adapter.TryInvoke(forged, (int)GraphicsLvo.CMove));
        Assert.Equal((ushort)0x5678, ReadWord(memory, nextInstructions + (uint)GraphicsLayouts.CopInsArg1));
    }

    [Fact]
    public void UserCopperBlockRolloverRollsBackWhenTheNextBufferCannotBeAllocated()
    {
        var memory = new FakeMemory(0x4000);
        var allocator = new BitmapAllocator { FailAfterAllocations = 3 };
        var core = new GraphicsLibraryCore(
            memory,
            allocator,
            new FakeBlitter(),
            new FakeDisplay());
        const uint userList = 0x3A0;
        var firstCopList = core.UCopperListInit(userList, 1);
        Assert.NotEqual(0u, firstCopList);
        Assert.True(core.CMove(userList, 0x180, 1));
        Assert.True(core.CBump(userList));

        Assert.False(core.CBump(userList));
        Assert.Equal(firstCopList, ReadLong(memory, userList + (uint)GraphicsLayouts.UCopListCopList));
        Assert.Equal(0u, ReadLong(memory, firstCopList + (uint)GraphicsLayouts.CopListNext));
        Assert.Single(allocator.Freed);
        Assert.Equal((uint)GraphicsLayouts.CopListSize, allocator.Freed[0].Bytes);
    }

    [Fact]
    public void UserCopperInstructionAdapterUsesClassicA1D0D1Registers()
    {
        var memory = new FakeMemory(0x4000);
        var core = new GraphicsLibraryCore(
            memory,
            new BitmapAllocator(),
            new FakeBlitter(),
            new FakeDisplay());
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint userList = 0x380;
        var init = new M68kCpuState { A = { [0] = userList }, D = { [0] = 2 } };
        Assert.True(adapter.TryInvoke(init, (int)GraphicsLvo.UCopperListInit));
        var copList = init.D[0];
        var instructions = ReadLong(memory, copList + (uint)GraphicsLayouts.CopListCopIns);

        var move = new M68kCpuState
        {
            A = { [1] = userList },
            D = { [0] = 0x180, [1] = 0xCAFE }
        };
        Assert.True(adapter.TryInvoke(move, (int)GraphicsLvo.CMove));
        Assert.Equal(0u, move.D[0]);
        Assert.Equal((ushort)0xCAFE, ReadWord(memory, instructions + (uint)GraphicsLayouts.CopInsArg1));

        var bump = new M68kCpuState { A = { [1] = userList } };
        Assert.True(adapter.TryInvoke(bump, (int)GraphicsLvo.CBump));
        var wait = new M68kCpuState { A = { [1] = userList }, D = { [0] = 7, [1] = 8 } };
        Assert.True(adapter.TryInvoke(wait, (int)GraphicsLvo.CWait));
        Assert.Equal((ushort)7, ReadWord(memory, instructions + (uint)GraphicsLayouts.CopInsSize + (uint)GraphicsLayouts.CopInsArg0));
        Assert.Equal((ushort)8, ReadWord(memory, instructions + (uint)GraphicsLayouts.CopInsSize + (uint)GraphicsLayouts.CopInsArg1));
    }

    [Fact]
    public void FreeCopListReleasesOnlyRegistryOwnedIntermediateBuffersAndClearsOwnerLinks()
    {
        var memory = new FakeMemory(0x4000);
        var allocator = new BitmapAllocator();
        var core = new GraphicsLibraryCore(
            memory,
            allocator,
            new FakeBlitter(),
            new FakeDisplay());
        const uint userList = 0x3E0;
        var copList = core.UCopperListInit(userList, 1);
        Assert.NotEqual(0u, copList);

        Assert.True(core.FreeCopList(copList));
        Assert.Equal(0u, ReadLong(memory, userList + (uint)GraphicsLayouts.UCopListFirstCopList));
        Assert.Equal(0u, ReadLong(memory, userList + (uint)GraphicsLayouts.UCopListCopList));
        Assert.Equal(2, allocator.Freed.Count);
        Assert.All(allocator.Freed, freed => Assert.Equal(GraphicsMemoryClass.Public, freed.MemoryClass));
        Assert.False(core.FreeCopList(copList));
        Assert.False(core.CMove(userList, 0x180, 1));

        var adapterAllocator = new BitmapAllocator();
        var adapterCore = new GraphicsLibraryCore(
            memory,
            adapterAllocator,
            new FakeBlitter(),
            new FakeDisplay());
        var adapter = new CopperStartGraphicsRegisterAdapter(adapterCore);
        var init = new M68kCpuState { A = { [0] = 0x420 }, D = { [0] = 1 } };
        Assert.True(adapter.TryInvoke(init, (int)GraphicsLvo.UCopperListInit));
        var free = new M68kCpuState { A = { [0] = init.D[0] }, D = { [0] = 0xFFFFFFFF } };
        Assert.True(adapter.TryInvoke(free, (int)GraphicsLvo.FreeCopList));
        Assert.Equal(0u, free.D[0]);
    }

    [Fact]
    public void FreeVPortCopListsClearsAllGuestCopperLinksAndRejectsMalformedViewportRanges()
    {
        var memory = new FakeMemory(0x1000);
        var display = new FakeDisplay();
        var core = CreateCore(memory, display);
        const uint viewPort = 0x180;

        Assert.True(memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortDspIns, 0x400));
        Assert.True(memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortSprIns, 0x500));
        Assert.True(memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortClrIns, 0x600));
        Assert.True(memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortUCopIns, 0x700));

        Assert.Equal(0, core.FreeViewPortCopLists(viewPort));
        Assert.Equal(0u, ReadLong(memory, viewPort + (uint)GraphicsLayouts.ViewPortDspIns));
        Assert.Equal(0u, ReadLong(memory, viewPort + (uint)GraphicsLayouts.ViewPortSprIns));
        Assert.Equal(0u, ReadLong(memory, viewPort + (uint)GraphicsLayouts.ViewPortClrIns));
        Assert.Equal(0u, ReadLong(memory, viewPort + (uint)GraphicsLayouts.ViewPortUCopIns));
        Assert.Equal(viewPort, display.FreedViewPortCopLists);
        Assert.Equal(1, display.FreeViewPortCopListsCount);

        Assert.Equal(-1, core.FreeViewPortCopLists(0x0FF8));

        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        Assert.True(memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortDspIns, 0x400));
        var state = new M68kCpuState { A = { [0] = viewPort }, D = { [0] = 0xA5A5_A5A5 } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.FreeVPortCopLists));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal(0u, ReadLong(memory, viewPort + (uint)GraphicsLayouts.ViewPortDspIns));
        Assert.Equal(2, display.FreeViewPortCopListsCount);
    }

    [Fact]
    public void ScrollVPortValidatesEveryRasInfoNodeBeforeNotifyingDisplay()
    {
        var memory = new FakeMemory(0x1000);
        var display = new FakeDisplay();
        var core = CreateCore(memory, display);
        const uint viewPort = 0x180;
        const uint rasInfo = 0x1C0;

        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDWidth, 320);
        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDHeight, 256);
        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDxOffset, 0);
        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDyOffset, 0);
        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortModes, 0);
        memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, 0x300);
        memory.TryWriteWord(rasInfo + (uint)GraphicsLayouts.RasInfoRxOffset, 4);
        memory.TryWriteWord(rasInfo + (uint)GraphicsLayouts.RasInfoRyOffset, 8);

        Assert.True(core.ScrollViewPort(viewPort));
        Assert.Equal(viewPort, display.ScrolledViewPort);

        // A second node that crosses the guest-memory boundary is rejected
        // without repeating the display notification.
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0x0FF8);
        Assert.False(core.ScrollViewPort(viewPort));
        Assert.Equal(1, display.ScrollCount);
    }

    [Fact]
    public void HostScrollVPortUsesTheDisplayRebuildBoundaryWithoutAdvancingCycles()
    {
        var hostMemory = new HostGuestMemory(new AmigaBus());
        var rebuilds = 0;
        var services = CreateHostGraphicsServices(
            hostMemory,
            requestDisplayRebuild: () => rebuilds++);
        const uint viewPort = 0x500;
        const uint rasInfo = 0x580;

        hostMemory.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        hostMemory.WriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
        hostMemory.WriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, 0);
        hostMemory.WriteWord(rasInfo + (uint)GraphicsLayouts.RasInfoRxOffset, 0);
        hostMemory.WriteWord(rasInfo + (uint)GraphicsLayouts.RasInfoRyOffset, 0);

        var state = new M68kCpuState { A = { [0] = viewPort }, Cycles = 41 };
        services.Invoke(state, (int)GraphicsLvo.ScrollVPort);
        Assert.Equal(1, rebuilds);
        Assert.Equal(41, state.Cycles);

        state.A[0] = 0x00FF_0000;
        services.Invoke(state, (int)GraphicsLvo.ScrollVPort);
        Assert.Equal(1, rebuilds);
        Assert.Equal(41, state.Cycles);
    }

    [Fact]
    public void HostPaletteVectorsPreserveTheGuestCallCycle()
    {
        var hostMemory = new HostGuestMemory(new AmigaBus());
        var callbackCycle = -1L;
        var services = CreateHostGraphicsServices(
            hostMemory,
            setRgb4: state => callbackCycle = state.Cycles);

        var state = new M68kCpuState
        {
            A = { [0] = 0x500 },
            D = { [0] = 1, [1] = 0x0F, [2] = 0x02, [3] = 0x01 },
            Cycles = 99
        };

        services.Invoke(state, (int)GraphicsLvo.SetRGB4);

        Assert.Equal(99, callbackCycle);
        Assert.Equal(0u, state.D[0]);
    }

    [Fact]
    public void GetVPModeIDUsesTheExplicitModeBackendAndRejectsMalformedViewPorts()
    {
        var memory = new FakeMemory(0x1000);
        var display = new FakeDisplay { ModeId = 0x0002_9004 };
        var core = CreateCore(memory, display);
        const uint viewPort = 0x180;
        const uint rasInfo = 0x1C0;

        memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, 0);

        Assert.Equal(0x0002_9004u, core.GetViewPortModeId(viewPort));

        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0x0FF8);
        Assert.Equal(GraphicsModeIds.Invalid, core.GetViewPortModeId(viewPort));
    }

    [Fact]
    public void NativeModeIdMappingIsProfileAwareAndDoesNotClaimUnsupportedBits()
    {
        Assert.True(GraphicsModeIds.TryGetNativeModeId(GraphicsModeIds.HiresMode, ntsc: false, out var palHires));
        Assert.Equal(0x0002_9000u, palHires);
        Assert.True(GraphicsModeIds.TryGetNativeModeId(GraphicsModeIds.InterlaceMode, ntsc: true, out var ntscLace));
        Assert.Equal(0x0001_1004u, ntscLace);
        Assert.True(GraphicsModeIds.TryGetNativeModeId(
            (ushort)(GraphicsModeIds.HamMode | GraphicsModeIds.InterlaceMode),
            ntsc: false,
            out var palHamLace));
        Assert.Equal(0x0002_1804u, palHamLace);
        Assert.True(GraphicsModeIds.TryGetNativeModeId(
            (ushort)(GraphicsModeIds.DualPlayfieldMode | GraphicsModeIds.PlayfieldBitAssignment),
            ntsc: true,
            out var ntscDpf2));
        Assert.Equal(0x0001_1440u, ntscDpf2);
        Assert.True(GraphicsModeIds.TryGetNativeModeId(
            (ushort)(GraphicsModeIds.HiresMode | GraphicsModeIds.SpritesMode | GraphicsModeIds.GenlockVideoMode),
            ntsc: false,
            out var palHiresWithControls));
        Assert.Equal(palHires, palHiresWithControls);
        Assert.True(GraphicsModeIds.TryGetNativeModeId(GraphicsModeIds.SuperHiresMode, ntsc: false, out var palSuperHires));
        Assert.Equal(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresMode, palSuperHires);
        Assert.True(GraphicsModeIds.TryGetNativeModeId(
            (ushort)(GraphicsModeIds.SuperHiresMode | GraphicsModeIds.InterlaceMode),
            ntsc: true,
            out var ntscSuperHiresLace));
        Assert.Equal(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresMode | GraphicsModeIds.InterlaceMode, ntscSuperHiresLace);
    }

    [Fact]
    public void GetVPModeIDFallsBackToNativeViewportStateAndHonorsPrivateVideoControlOverride()
    {
        var memory = new FakeMemory(0x10000);
        var core = new GraphicsLibraryCore(memory, new BitmapAllocator(), new FakeBlitter(), new FakeDisplay());
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        var colorMap = core.GetColorMap(2);
        const uint viewPort = 0x180;
        const uint rasInfo = 0x1C0;
        const uint tags = 0xA00;

        Assert.True(memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortColorMap, colorMap));
        Assert.True(memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDWidth, 640));
        Assert.True(memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDHeight, 512));
        Assert.True(memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDxOffset, 0));
        Assert.True(memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDyOffset, 0));
        Assert.True(memory.TryWriteWord(
            viewPort + (uint)GraphicsLayouts.ViewPortModes,
            (ushort)(GraphicsModeIds.HiresMode | GraphicsModeIds.InterlaceMode)));
        Assert.True(memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo));
        Assert.True(memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0));
        Assert.True(memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, 0));
        Assert.True(memory.TryWriteWord(rasInfo + (uint)GraphicsLayouts.RasInfoRxOffset, 0));
        Assert.True(memory.TryWriteWord(rasInfo + (uint)GraphicsLayouts.RasInfoRyOffset, 0));

        Assert.Equal(
            GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresMode | GraphicsModeIds.InterlaceMode,
            core.GetViewPortModeId(viewPort));

        WriteTag(memory, tags, 0x8000_0021, GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresMode); // VTAG_VPMODEID_SET
        WriteTag(memory, tags + 8, 0, 0);
        var set = new M68kCpuState { A = { [0] = colorMap, [1] = tags } };
        Assert.True(adapter.TryInvoke(set, (int)GraphicsLvo.VideoControl));
        Assert.Equal(0u, set.D[0]);
        Assert.Equal(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresMode, core.GetViewPortModeId(viewPort));

        WriteTag(memory, tags, 0x8000_0020, 0xB00); // VTAG_VPMODEID_GET
        WriteTag(memory, tags + 8, 0, 0);
        Assert.True(adapter.TryInvoke(set, (int)GraphicsLvo.VideoControl));
        Assert.Equal(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresMode, ReadLong(memory, 0xB00));

        WriteTag(memory, tags, 0x8000_0022, 0); // VTAG_VPMODEID_CLR
        WriteTag(memory, tags + 8, 0, 0);
        Assert.True(adapter.TryInvoke(set, (int)GraphicsLvo.VideoControl));
        Assert.Equal(
            GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresMode | GraphicsModeIds.InterlaceMode,
            core.GetViewPortModeId(viewPort));

        WriteTag(memory, tags, 0x8000_0021, 0xDEAD_BEEFu);
        WriteTag(memory, tags + 8, 0, 0);
        Assert.True(adapter.TryInvoke(set, (int)GraphicsLvo.VideoControl));
        Assert.Equal(1u, set.D[0]);
    }

    [Fact]
    public void DisplayDatabaseEnumeratesSupportedPalNtscModesAndReportsAvailability()
    {
        var memory = new FakeMemory(0x1000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);

        var mode = GraphicsModeIds.Invalid;
        var seen = new List<uint>();
        while ((mode = core.NextDisplayInfo(mode)) != GraphicsModeIds.Invalid)
        {
            seen.Add(mode);
            Assert.Equal(mode, core.FindDisplayInfo(mode));
            Assert.Equal(0u, core.ModeNotAvailable(mode));
        }

        Assert.Equal(36, seen.Count);
        Assert.Equal(GraphicsModeIds.PalMonitor, seen[0]);
        Assert.Contains(GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresMode, seen);
        Assert.Contains(GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresMode | GraphicsModeIds.InterlaceMode, seen);
        Assert.Contains(GraphicsModeIds.PalMonitor | GraphicsModeIds.HamMode, seen);
        Assert.Contains(GraphicsModeIds.PalMonitor | GraphicsModeIds.ExtraHalfBriteMode, seen);
        Assert.Contains(GraphicsModeIds.PalMonitor | GraphicsModeIds.DualPlayfieldMode, seen);
        Assert.Contains(GraphicsModeIds.PalMonitor | GraphicsModeIds.DualPlayfieldMode | GraphicsModeIds.PlayfieldBitAssignment, seen);
        Assert.Equal(
            GraphicsModeIds.NtscMonitor |
                GraphicsModeIds.HiresMode |
                GraphicsModeIds.DualPlayfieldMode |
                GraphicsModeIds.PlayfieldBitAssignment |
                GraphicsModeIds.InterlaceMode,
            seen[^1]);
        Assert.Equal(0u, core.FindDisplayInfo(0xDEAD_BEEFu));
        Assert.Equal(GraphicsDisplayDatabase.DiAvailNoMonitor, core.ModeNotAvailable(0xDEAD_BEEFu));
        Assert.Equal(0u, core.ModeNotAvailable(GraphicsModeIds.PalMonitor | GraphicsModeIds.HamMode));

        var find = new M68kCpuState { D = { [0] = GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresMode } };
        Assert.True(adapter.TryInvoke(find, (int)GraphicsLvo.FindDisplayInfo));
        Assert.Equal(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresMode, find.D[0]);

        find.D[0] = GraphicsModeIds.Invalid;
        Assert.True(adapter.TryInvoke(find, (int)GraphicsLvo.NextDisplayInfo));
        Assert.Equal(GraphicsModeIds.PalMonitor, find.D[0]);

        find.D[0] = GraphicsModeIds.PalMonitor | GraphicsModeIds.DoubleScanMode;
        Assert.True(adapter.TryInvoke(find, (int)GraphicsLvo.ModeNotAvailable));
        Assert.Equal(GraphicsDisplayDatabase.DiAvailNoChips, find.D[0]);
    }

    [Fact]
    public void DisplayDatabaseReturnsNativeDisplayInfoDimensionMonitorAndNameChunks()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint mode = GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresMode | GraphicsModeIds.InterlaceMode;
        const uint buffer = 0x800;
        var handle = core.FindDisplayInfo(mode);

        Assert.Equal(0x30, core.GetDisplayInfoData(
            handle,
            buffer,
            0x30,
            GraphicsDisplayDatabase.DtagDisp,
            0));
        Assert.Equal(GraphicsDisplayDatabase.DtagDisp, ReadLong(memory, buffer));
        Assert.Equal(mode, ReadLong(memory, buffer + 4));
        Assert.Equal(4u, ReadLong(memory, buffer + 12));
        Assert.Equal((ushort)0, ReadWord(memory, buffer + 0x10));
        Assert.NotEqual(0u, ReadLong(memory, buffer + 0x12) & 1u);
        Assert.Equal((byte)4, ReadByte(memory, buffer + 0x28));
        Assert.Equal((byte)4, ReadByte(memory, buffer + 0x29));
        Assert.Equal((byte)4, ReadByte(memory, buffer + 0x2A));

        Assert.Equal(0x58, core.GetDisplayInfoData(
            handle,
            buffer,
            0x58,
            GraphicsDisplayDatabase.DtagDims,
            mode));
        Assert.Equal((ushort)6, ReadWord(memory, buffer + 0x10));
        Assert.Equal((ushort)640, ReadWord(memory, buffer + 0x12));
        Assert.Equal((ushort)512, ReadWord(memory, buffer + 0x14));
        Assert.Equal((ushort)639, ReadWord(memory, buffer + 0x1E));
        Assert.Equal((ushort)511, ReadWord(memory, buffer + 0x20));

        Assert.Equal(0x60, core.GetDisplayInfoData(
            handle,
            buffer,
            0x60,
            GraphicsDisplayDatabase.DtagMntr,
            0));
        Assert.Equal((ushort)512, ReadWord(memory, buffer + 0x24));
        Assert.Equal(mode, ReadLong(memory, buffer + 0x54));

        Assert.Equal(8, core.GetDisplayInfoData(
            handle,
            buffer,
            8,
            GraphicsDisplayDatabase.DtagName,
            0));
        Assert.Equal(GraphicsDisplayDatabase.DtagName, ReadLong(memory, buffer));
        Assert.Equal(0, core.GetDisplayInfoData(handle, buffer, 0x30, 0x8000_4000u, 0));
        Assert.Equal(-1, core.GetDisplayInfoData(0xDEAD_BEEFu, buffer, 0x30, GraphicsDisplayDatabase.DtagDisp, 0));

        var state = new M68kCpuState
        {
            A = { [0] = handle, [1] = buffer },
            D = { [0] = 0x58, [1] = GraphicsDisplayDatabase.DtagDims, [2] = mode }
        };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.GetDisplayInfoData));
        Assert.Equal(0x58u, state.D[0]);
    }

    [Fact]
    public void DisplayDatabaseExposesEcsSuperHiresGeometryThroughPureAndRegisterPaths()
    {
        var memory = new FakeMemory(0x3000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint mode = GraphicsModeIds.PalMonitor |
                          GraphicsModeIds.SuperHiresMode |
                          GraphicsModeIds.InterlaceMode;
        const uint buffer = 0xA00;
        var handle = core.FindDisplayInfo(mode);

        Assert.Equal(mode, handle);
        Assert.Equal(0x58, core.GetDisplayInfoData(
            handle,
            buffer,
            0x58,
            GraphicsDisplayDatabase.DtagDims,
            mode));
        Assert.Equal((ushort)2, ReadWord(memory, buffer + 0x10));
        Assert.Equal((ushort)1280, ReadWord(memory, buffer + 0x12));
        Assert.Equal((ushort)512, ReadWord(memory, buffer + 0x14));
        Assert.Equal((ushort)1279, ReadWord(memory, buffer + 0x1E));
        Assert.Equal((ushort)511, ReadWord(memory, buffer + 0x20));

        var state = new M68kCpuState
        {
            A = { [0] = handle, [1] = buffer },
            D = { [0] = 0x30, [1] = GraphicsDisplayDatabase.DtagDisp, [2] = mode }
        };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.GetDisplayInfoData));
        Assert.Equal(0x30u, state.D[0]);
        Assert.Equal(mode, ReadLong(memory, buffer + 4));
        Assert.Equal(0x0000_0010u, ReadLong(memory, buffer + 0x12) & 0x0000_0010u); // DIPF_IS_ECS
        Assert.Equal(0u, ReadLong(memory, buffer + 0x12) & 0x0000_0040u); // no attached sprites

        const uint bestTags = 0x700;
        WriteTag(memory, bestTags, GraphicsDisplayDatabase.BidTagNominalWidth, 1280);
        WriteTag(memory, bestTags + 8, GraphicsDisplayDatabase.BidTagNominalHeight, 256);
        WriteTag(memory, bestTags + 16, GraphicsDisplayDatabase.BidTagDesiredWidth, 1280);
        WriteTag(memory, bestTags + 24, GraphicsDisplayDatabase.BidTagDesiredHeight, 256);
        WriteTag(memory, bestTags + 32, GraphicsDisplayDatabase.BidTagDepth, 2);
        WriteTag(memory, bestTags + 40, GraphicsDisplayDatabase.BidTagMonitorId, GraphicsModeIds.PalMonitor);
        WriteTag(memory, bestTags + 48, 0, 0);
        Assert.Equal(
            GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresMode,
            core.BestModeIDA(bestTags));
    }

    [Fact]
    public void DisplayDatabasePublishesFeatureModePropertiesAndBestModeSelectsHam()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        const uint buffer = 0x900;
        var hamMode = GraphicsModeIds.PalMonitor | GraphicsModeIds.HamKey;
        var handle = core.FindDisplayInfo(hamMode);

        Assert.Equal(0x30, core.GetDisplayInfoData(
            handle,
            buffer,
            0x30,
            GraphicsDisplayDatabase.DtagDisp,
            hamMode));
        var properties = ReadLong(memory, buffer + 0x12);
        Assert.NotEqual(0u, properties & 0x0000_0008u); // DIPF_IS_HAM
        Assert.NotEqual(0u, properties & 0x0000_0020u); // DIPF_IS_PAL
        Assert.Equal((ushort)0, ReadWord(memory, buffer + 0x10));

        const uint tags = 0x500;
        WriteTag(memory, tags, GraphicsDisplayDatabase.BidTagNominalWidth, 320);
        WriteTag(memory, tags + 8, GraphicsDisplayDatabase.BidTagNominalHeight, 256);
        WriteTag(memory, tags + 16, GraphicsDisplayDatabase.BidTagDesiredWidth, 320);
        WriteTag(memory, tags + 24, GraphicsDisplayDatabase.BidTagDesiredHeight, 256);
        WriteTag(memory, tags + 32, GraphicsDisplayDatabase.BidTagDepth, 6);
        WriteTag(memory, tags + 40, GraphicsDisplayDatabase.BidTagMonitorId, GraphicsModeIds.PalMonitor);
        WriteTag(memory, tags + 48, GraphicsDisplayDatabase.BidTagDipfMustHave, 0x0000_0008u);
        WriteTag(memory, tags + 56, GraphicsDisplayDatabase.BidTagDipfMustNotHave, 0);
        Assert.True(memory.TryWriteLong(tags + 64, 0));

        Assert.Equal(hamMode, core.BestModeIDA(tags));
    }

    [Fact]
    public void BestModeIdSelectsNativePalNtscModesFromDocumentedTagsAndRejectsMalformedLists()
    {
        var memory = new FakeMemory(0x1000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint tags = 0x100;
        WriteTag(memory, tags + 0, GraphicsDisplayDatabase.BidTagNominalWidth, 640);
        WriteTag(memory, tags + 8, GraphicsDisplayDatabase.BidTagNominalHeight, 200);
        WriteTag(memory, tags + 16, GraphicsDisplayDatabase.BidTagDesiredWidth, 640);
        WriteTag(memory, tags + 24, GraphicsDisplayDatabase.BidTagDesiredHeight, 200);
        WriteTag(memory, tags + 32, GraphicsDisplayDatabase.BidTagDepth, 5);
        WriteTag(memory, tags + 40, GraphicsDisplayDatabase.BidTagMonitorId, GraphicsModeIds.NtscMonitor);
        Assert.True(memory.TryWriteLong(tags + 48, 0));

        Assert.Equal(
            GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresMode,
            core.BestModeIDA(tags));
        var state = new M68kCpuState { A = { [0] = tags } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.BestModeIDA));
        Assert.Equal(GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresMode, state.D[0]);

        WriteTag(memory, tags + 8, GraphicsDisplayDatabase.BidTagNominalHeight, 400);
        WriteTag(memory, tags + 24, GraphicsDisplayDatabase.BidTagDesiredHeight, 400);
        WriteTag(memory, tags + 48, GraphicsDisplayDatabase.BidTagDipfMustHave, 1);
        Assert.Equal(
            GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresMode | GraphicsModeIds.InterlaceMode,
            core.BestModeIDA(tags));

        const uint cycle = 0x200;
        WriteTag(memory, cycle, 2, cycle);
        Assert.Equal(GraphicsModeIds.Invalid, core.BestModeIDA(cycle));
        state.A[0] = cycle;
        Assert.False(adapter.TryInvoke(state, (int)GraphicsLvo.BestModeIDA));
    }

    [Fact]
    public void BestModeIdRejectsOutOfRangeFieldsAndIncompatibleSourceMonitors()
    {
        var memory = new FakeMemory(0x1000);
        var core = CreateCore(memory);
        const uint tags = 0x100;

        WriteTag(memory, tags, GraphicsDisplayDatabase.BidTagNominalWidth, 0x1_0000);
        Assert.True(memory.TryWriteLong(tags + 8, 0));
        Assert.Equal(GraphicsModeIds.Invalid, core.BestModeIDA(tags));

        WriteTag(memory, tags, GraphicsDisplayDatabase.BidTagNominalWidth, 640);
        WriteTag(memory, tags + 8, GraphicsDisplayDatabase.BidTagDepth, 0x106);
        Assert.True(memory.TryWriteLong(tags + 16, 0));
        Assert.Equal(GraphicsModeIds.Invalid, core.BestModeIDA(tags));

        WriteTag(
            memory,
            tags,
            GraphicsDisplayDatabase.BidTagSourceId,
            GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresMode);
        WriteTag(memory, tags + 8, GraphicsDisplayDatabase.BidTagMonitorId, GraphicsModeIds.NtscMonitor);
        Assert.True(memory.TryWriteLong(tags + 16, 0));
        Assert.Equal(GraphicsModeIds.Invalid, core.BestModeIDA(tags));
    }

    [Fact]
    public void BestModeIdViewportTagUsesViewportDefaultsAndExplicitModeProvider()
    {
        var memory = new FakeMemory(0x4000);
        var display = new FakeDisplay
        {
            ModeId = GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresMode
        };
        var core = CreateCore(memory, display);
        const uint viewPort = 0x180;
        const uint rasInfo = 0x1C0;
        const uint bitMap = 0x300;
        const uint tags = 0x500;

        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDWidth, 640);
        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDHeight, 200);
        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDxOffset, 0);
        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDyOffset, 0);
        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortModes, 0);
        memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, bitMap);
        memory.TryWriteWord(rasInfo + (uint)GraphicsLayouts.RasInfoRxOffset, 0);
        memory.TryWriteWord(rasInfo + (uint)GraphicsLayouts.RasInfoRyOffset, 0);
        memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 80);
        memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapRows, 200);
        memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, 5);

        WriteTag(memory, tags, GraphicsDisplayDatabase.BidTagViewPort, viewPort);
        Assert.True(memory.TryWriteLong(tags + 8, 0));
        Assert.Equal(
            GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresMode,
            core.BestModeIDA(tags));

        WriteTag(memory, tags + 8, GraphicsDisplayDatabase.BidTagDipfMustHave, 1);
        Assert.True(memory.TryWriteLong(tags + 16, 0));
        Assert.Equal(
            GraphicsModeIds.NtscMonitor |
                GraphicsModeIds.HiresMode |
                GraphicsModeIds.InterlaceMode,
            core.BestModeIDA(tags));

        display.ModeId = GraphicsModeIds.Invalid;
        WriteTag(memory, tags, GraphicsDisplayDatabase.BidTagViewPort, viewPort);
        Assert.True(memory.TryWriteLong(tags + 8, 0));
        Assert.Equal(GraphicsModeIds.Invalid, core.BestModeIDA(tags));

        // A SourceID explicitly replaces the viewport source, so a missing
        // viewport mode provider does not make this native request fail.
        WriteTag(memory, tags, GraphicsDisplayDatabase.BidTagViewPort, viewPort);
        WriteTag(
            memory,
            tags + 8,
            GraphicsDisplayDatabase.BidTagSourceId,
            GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresMode);
        Assert.True(memory.TryWriteLong(tags + 16, 0));
        Assert.Equal(
            GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresMode,
            core.BestModeIDA(tags));
    }

    [Fact]
    public void BestModeIdViewportTagUsesNativeColorMapModeWhenNoExplicitProviderExists()
    {
        var memory = new FakeMemory(0x4000);
        var core = new GraphicsLibraryCore(
            memory,
            new BitmapAllocator(),
            new FakeBlitter(),
            new FakeDisplayWithoutCopper());
        const uint viewPort = 0x180;
        const uint rasInfo = 0x1C0;
        const uint bitMap = 0x300;
        const uint tags = 0x500;

        var colorMap = core.GetColorMap(2);
        memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortColorMap, colorMap);
        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDWidth, 640);
        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDHeight, 200);
        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortModes, GraphicsModeIds.HiresMode);
        memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, bitMap);
        memory.TryWriteWord(rasInfo + (uint)GraphicsLayouts.RasInfoRxOffset, 0);
        memory.TryWriteWord(rasInfo + (uint)GraphicsLayouts.RasInfoRyOffset, 0);
        memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 80);
        memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapRows, 200);
        memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, 5);

        WriteTag(memory, tags, GraphicsDisplayDatabase.BidTagViewPort, viewPort);
        Assert.True(memory.TryWriteLong(tags + 8, 0));

        Assert.Equal(
            GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresMode,
            core.BestModeIDA(tags));
    }

    [Fact]
    public void OpenAndCloseMonitorBuildNativePalNtscSpecsAndGuardForeignRequests()
    {
        var memory = new FakeMemory(0x10000);
        var allocator = new FakeAllocator();
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), new FakeDisplay());
        const uint ntscName = 0x6000;
        WriteAscii(memory, ntscName, "ntsc.monitor");

        var ntsc = core.OpenMonitor(ntscName, GraphicsModeIds.PalMonitor);
        Assert.NotEqual(0u, ntsc);
        Assert.Equal((byte)18, ReadByte(memory, ntsc + (uint)GraphicsLayouts.MonitorSpecNodeType));
        Assert.Equal((byte)4, ReadByte(memory, ntsc + (uint)GraphicsLayouts.MonitorSpecNodeSubsystem));
        Assert.Equal((byte)2, ReadByte(memory, ntsc + (uint)GraphicsLayouts.MonitorSpecNodeSubtype));
        Assert.Equal(ntscName, ReadLong(memory, ntsc + (uint)GraphicsLayouts.MonitorSpecNodeName));
        Assert.Equal((ushort)1, ReadWord(memory, ntsc + (uint)GraphicsLayouts.MonitorSpecFlags));
        Assert.Equal((ushort)262, ReadWord(memory, ntsc + (uint)GraphicsLayouts.MonitorSpecTotalRows));
        Assert.Equal((ushort)21, ReadWord(memory, ntsc + (uint)GraphicsLayouts.MonitorSpecMinRow));
        Assert.Equal((ushort)1, ReadWord(memory, ntsc + (uint)GraphicsLayouts.MonitorSpecOpenCount));
        Assert.Equal(0, core.CloseMonitor(ntsc));
        Assert.Equal(ntsc, allocator.LastFreedAddress);
        Assert.Equal((uint)GraphicsLayouts.MonitorSpecSize, allocator.LastFreedBytes);
        Assert.Equal(GraphicsMemoryClass.Public, allocator.LastFreedClass);

        var pal = core.OpenMonitor(0, GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresMode);
        Assert.NotEqual(0u, pal);
        Assert.Equal((ushort)2, ReadWord(memory, pal + (uint)GraphicsLayouts.MonitorSpecFlags));
        Assert.Equal((ushort)312, ReadWord(memory, pal + (uint)GraphicsLayouts.MonitorSpecTotalRows));
        Assert.Equal((ushort)0x20, ReadWord(memory, pal + (uint)GraphicsLayouts.MonitorSpecBeamCon0));
        Assert.Equal(0, core.CloseMonitor(pal));

        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        var state = new M68kCpuState { A = { [1] = ntscName }, D = { [0] = GraphicsModeIds.NtscMonitor } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.OpenMonitor));
        var adapterMonitor = state.D[0];
        Assert.NotEqual(0u, adapterMonitor);
        state.A[0] = adapterMonitor;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.CloseMonitor));
        Assert.Equal(0u, state.D[0]);

        state.A[1] = 0x7000;
        WriteAscii(memory, state.A[1], "vga.monitor");
        Assert.False(adapter.TryInvoke(state, (int)GraphicsLvo.OpenMonitor));
        Assert.Equal(1, core.CloseMonitor(0x1FF0));
    }

    [Fact]
    public void CoerceModeSelectsNativeMonitorModesFromValidatedViewportGeometry()
    {
        var memory = new FakeMemory(0x4000);
        var display = new FakeDisplay();
        var core = CreateCore(memory, display);
        const uint viewPort = 0x180;
        const uint rasInfo = 0x1C0;
        const uint bitMap = 0x300;

        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDWidth, 320);
        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDHeight, 256);
        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDxOffset, 0);
        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDyOffset, 0);
        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortModes, 0);
        memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, bitMap);
        memory.TryWriteWord(rasInfo + (uint)GraphicsLayouts.RasInfoRxOffset, 0);
        memory.TryWriteWord(rasInfo + (uint)GraphicsLayouts.RasInfoRyOffset, 0);
        memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 40);
        memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapRows, 256);
        memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, 5);

        Assert.Equal(
            GraphicsModeIds.PalMonitor,
            core.CoerceMode(
                viewPort,
                GraphicsModeIds.PalMonitor,
                GraphicsDisplayDatabase.CoercePreserveColors));

        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDHeight, 512);
        Assert.Equal(
            GraphicsModeIds.PalMonitor | GraphicsModeIds.InterlaceMode,
            core.CoerceMode(viewPort, GraphicsModeIds.PalMonitor, 0));
        Assert.Equal(
            GraphicsModeIds.PalMonitor,
            core.CoerceMode(
                viewPort,
                GraphicsModeIds.PalMonitor,
                GraphicsDisplayDatabase.CoerceAvoidFlicker));

        memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, 6);
        Assert.Equal(
            GraphicsModeIds.PalMonitor | GraphicsModeIds.InterlaceMode,
            core.CoerceMode(
                viewPort,
                GraphicsModeIds.PalMonitor,
                GraphicsDisplayDatabase.CoercePreserveColors));
        Assert.Equal(
            GraphicsModeIds.PalMonitor | GraphicsModeIds.InterlaceMode,
            core.CoerceMode(viewPort, GraphicsModeIds.PalMonitor, 0));

        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        var state = new M68kCpuState
        {
            A = { [0] = viewPort },
            D =
            {
                [0] = GraphicsModeIds.PalMonitor,
                [1] = GraphicsDisplayDatabase.CoerceAvoidFlicker
            }
        };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.CoerceMode));
        Assert.Equal(GraphicsModeIds.PalMonitor, state.D[0]);

        state.D[0] = 0x0003_1000u;
        Assert.False(adapter.TryInvoke(state, (int)GraphicsLvo.CoerceMode));
        memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, 0x3FF8);
        Assert.Equal(
            GraphicsModeIds.Invalid,
            core.CoerceMode(viewPort, GraphicsModeIds.PalMonitor, 0));
    }

    [Fact]
    public void CoerceModeHonorsNativeViewportFeatureKeysAndRejectsUnsupportedModes()
    {
        var memory = new FakeMemory(0x4000);
        var core = CreateCore(memory);
        const uint viewPort = 0x180;
        const uint rasInfo = 0x1C0;
        const uint bitMap = 0x300;

        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDWidth, 320);
        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDHeight, 256);
        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDxOffset, 0);
        memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDyOffset, 0);
        memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, bitMap);
        memory.TryWriteWord(rasInfo + (uint)GraphicsLayouts.RasInfoRxOffset, 0);
        memory.TryWriteWord(rasInfo + (uint)GraphicsLayouts.RasInfoRyOffset, 0);
        memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 40);
        memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapRows, 256);
        memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, 6);

        memory.TryWriteWord(
            viewPort + (uint)GraphicsLayouts.ViewPortModes,
            GraphicsModeIds.HamMode);
        Assert.Equal(
            GraphicsModeIds.PalMonitor | GraphicsModeIds.HamMode,
            core.CoerceMode(
                viewPort,
                GraphicsModeIds.PalMonitor,
                GraphicsDisplayDatabase.CoercePreserveColors));

        memory.TryWriteWord(
            viewPort + (uint)GraphicsLayouts.ViewPortModes,
            GraphicsModeIds.SuperHiresMode);
        Assert.Equal(
            GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresMode,
            core.CoerceMode(viewPort, GraphicsModeIds.PalMonitor, 0));

        memory.TryWriteWord(
            viewPort + (uint)GraphicsLayouts.ViewPortModes,
            GraphicsModeIds.ExtendedMode);
        Assert.Equal(
            GraphicsModeIds.Invalid,
            core.CoerceMode(viewPort, GraphicsModeIds.PalMonitor, 0));
    }

    [Fact]
    public void GraphicsExtendedNodesAssociateViewAndViewportGuestState()
    {
        var memory = new FakeMemory(0x10000);
        var allocator = new BitmapAllocator();
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), new FakeDisplay());

        var viewExtra = core.GfxNew(GraphicsExtendedNodeOperations.ViewExtraType);
        Assert.NotEqual(0u, viewExtra);
        Assert.Equal((byte)18, ReadByte(memory, viewExtra + (uint)GraphicsLayouts.ExtendedNodeType));
        Assert.Equal((byte)1, ReadByte(memory, viewExtra + (uint)GraphicsLayouts.ExtendedNodeSubsystem));
        Assert.Equal((byte)2, ReadByte(memory, viewExtra + (uint)GraphicsLayouts.ExtendedNodeSubtype));
        Assert.True(core.GfxAssociate(0x300, viewExtra));
        Assert.Equal(viewExtra, core.GfxLookUp(0x300));
        Assert.Equal(0x300u, ReadLong(memory, viewExtra + (uint)GraphicsLayouts.ViewExtraView));

        var viewPortExtra = core.GfxNew(GraphicsExtendedNodeOperations.ViewPortExtraType);
        Assert.NotEqual(0u, viewPortExtra);
        Assert.Equal((byte)2, ReadByte(memory, viewPortExtra + (uint)GraphicsLayouts.ExtendedNodeSubsystem));
        Assert.Equal((byte)2, ReadByte(memory, viewPortExtra + (uint)GraphicsLayouts.ExtendedNodeSubtype));
        Assert.True(core.GfxAssociate(0x300, viewPortExtra));
        Assert.Equal(0u, ReadLong(memory, viewExtra + (uint)GraphicsLayouts.ViewExtraView));
        Assert.Equal(viewPortExtra, core.GfxLookUp(0x300));
        Assert.Equal(0x300u, ReadLong(memory, viewPortExtra + (uint)GraphicsLayouts.ViewPortExtraViewPort));

        Assert.False(core.GfxAssociate(0, viewPortExtra));
        Assert.Equal(0u, core.GfxLookUp(0x301));
        Assert.True(core.GfxFree(viewExtra));
        Assert.True(core.GfxFree(viewPortExtra));
        Assert.Equal(0u, core.GfxLookUp(0x300));
        Assert.False(core.GfxFree(viewPortExtra));

        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        var state = new M68kCpuState { D = { [0] = GraphicsExtendedNodeOperations.ViewExtraType } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.GfxNew));
        var adapterNode = state.D[0];
        state.A[0] = 0x400;
        state.A[1] = adapterNode;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.GfxAssociate));
        var lookupState = new M68kCpuState { A = { [0] = 0x400 } };
        Assert.True(adapter.TryInvoke(lookupState, (int)GraphicsLvo.GfxLookUp));
        Assert.Equal(adapterNode, lookupState.D[0]);
        state.A[0] = adapterNode;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.GfxFree));

        state.D[0] = 3;
        Assert.False(adapter.TryInvoke(state, (int)GraphicsLvo.GfxNew));
    }

    [Fact]
    public void SimpleSpriteOwnershipFollowsGuestNumFieldAndEightSpritePool()
    {
        var memory = new FakeMemory(0x1000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint sprite = 0x200;

        Assert.Equal(0, core.GetSprite(sprite, -1));
        Assert.Equal((ushort)0, ReadWord(memory, sprite + (uint)GraphicsLayouts.SimpleSpriteNum));
        Assert.Equal(1, core.GetSprite(sprite, -1));
        Assert.Equal((ushort)1, ReadWord(memory, sprite + (uint)GraphicsLayouts.SimpleSpriteNum));
        Assert.Equal(-1, core.GetSprite(sprite, 0));
        Assert.Equal(ushort.MaxValue, ReadWord(memory, sprite + (uint)GraphicsLayouts.SimpleSpriteNum));
        Assert.False(core.FreeSprite(-1));
        Assert.True(core.FreeSprite(0));
        Assert.Equal(0, core.GetSprite(sprite, 0));
        Assert.Equal(-1, core.GetSprite(sprite, 8));

        var state = new M68kCpuState { A = { [0] = sprite }, D = { [0] = unchecked((uint)-1) } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.GetSprite));
        Assert.Equal(2u, state.D[0]);
        Assert.Equal((ushort)2, ReadWord(memory, sprite + (uint)GraphicsLayouts.SimpleSpriteNum));

        state.D[0] = 2;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.FreeSprite));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal(2, core.GetSprite(sprite, 2));
    }

    [Fact]
    public void SimpleSpriteMoveAndChangeUpdateGuestStateWithViewportAndImageGuards()
    {
        var memory = new FakeMemory(0x1000);
        var spriteBackend = new RecordingSpriteBackend();
        var core = new GraphicsLibraryCore(
            memory,
            new FakeAllocator(),
            new FakeBlitter(),
            new FakeDisplay(),
            spriteBackend: spriteBackend);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint sprite = 0x200;
        const uint viewPort = 0x300;
        const uint image = 0x800;

        Assert.True(memory.TryWriteWord(sprite + (uint)GraphicsLayouts.SimpleSpriteHeight, 2));
        Assert.Equal(0, core.GetSprite(sprite, 0));
        Assert.True(core.MoveSprite(0, sprite, -12, 300));
        Assert.Equal((ushort)0xFFF4, ReadWord(memory, sprite + (uint)GraphicsLayouts.SimpleSpriteX));
        Assert.Equal((ushort)300, ReadWord(memory, sprite + (uint)GraphicsLayouts.SimpleSpriteY));
        Assert.Equal((0u, sprite, (short)-12, (short)300), Assert.Single(spriteBackend.Moves));

        var state = new M68kCpuState
        {
            A = { [0] = viewPort, [1] = sprite },
            D = { [0] = 123, [1] = 45 }
        };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.MoveSprite));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal((ushort)123, ReadWord(memory, sprite + (uint)GraphicsLayouts.SimpleSpriteX));
        Assert.Equal((ushort)45, ReadWord(memory, sprite + (uint)GraphicsLayouts.SimpleSpriteY));
        Assert.Equal((viewPort, sprite, (short)123, (short)45), spriteBackend.Moves[1]);

        for (var offset = 0; offset < 16; offset++)
            Assert.True(memory.TryWriteByte(image + (uint)offset, (byte)(offset + 1)));
        Assert.True(core.ChangeSprite(viewPort, sprite, image));
        Assert.Equal(image, ReadLong(memory, sprite + (uint)GraphicsLayouts.SimpleSpritePosCtlData));
        Assert.Equal((viewPort, sprite, image), Assert.Single(spriteBackend.Changes));

        state.A[2] = image;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.ChangeSprite));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal(2, spriteBackend.Changes.Count);

        Assert.False(core.ChangeSprite(viewPort, sprite, 0xFF8));
        Assert.Equal(image, ReadLong(memory, sprite + (uint)GraphicsLayouts.SimpleSpritePosCtlData));
        Assert.False(core.MoveSprite(0xFF0, sprite, 1, 2));
    }

    [Fact]
    public void SimpleSpriteMoveRegeneratesPositionControlWordsInViewportCoordinateSpace()
    {
        var memory = new FakeMemory(0x1000);
        var core = new GraphicsLibraryCore(
            memory,
            new FakeAllocator(),
            new FakeBlitter(),
            new FakeDisplay());
        const uint sprite = 0x200;
        const uint viewPort = 0x300;
        const uint posCtlData = 0x400;

        Assert.True(memory.TryWriteLong(
            sprite + (uint)GraphicsLayouts.SimpleSpritePosCtlData,
            posCtlData));
        Assert.True(memory.TryWriteWord(
            sprite + (uint)GraphicsLayouts.SimpleSpriteHeight,
            3));
        Assert.True(memory.TryWriteWord(posCtlData + 2u, GraphicsLayouts.SpriteAttachedFlag));
        Assert.True(memory.TryWriteWord(
            viewPort + (uint)GraphicsLayouts.ViewPortDxOffset,
            unchecked((ushort)4)));
        Assert.True(memory.TryWriteWord(
            viewPort + (uint)GraphicsLayouts.ViewPortDyOffset,
            unchecked((ushort)6)));
        Assert.True(memory.TryWriteWord(
            viewPort + (uint)GraphicsLayouts.ViewPortModes,
            (ushort)(GraphicsModeIds.HiresMode | GraphicsModeIds.InterlaceMode)));

        Assert.True(core.MoveSprite(viewPort, sprite, 9, 11));

        // (9 + 4) / 2 = 6 and (11 + 6) / 2 = 8 in the viewport's
        // hires/interlaced coordinate space.  The canonical View origin is
        // H=0x81,V=0x2C, and the attached bit must survive the rewrite.
        Assert.Equal((ushort)0x3443, ReadWord(memory, posCtlData));
        Assert.Equal((ushort)0x3781, ReadWord(memory, posCtlData + 2u));
        Assert.Equal((ushort)9, ReadWord(memory, sprite + (uint)GraphicsLayouts.SimpleSpriteX));
        Assert.Equal((ushort)11, ReadWord(memory, sprite + (uint)GraphicsLayouts.SimpleSpriteY));
    }

    [Fact]
    public void SimpleSpriteMoveRejectsMalformedPositionEnvelopeBeforeChangingGuestCoordinates()
    {
        var memory = new FakeMemory(0x1000);
        var core = new GraphicsLibraryCore(
            memory,
            new FakeAllocator(),
            new FakeBlitter(),
            new FakeDisplay());
        const uint sprite = 0x200;

        Assert.True(memory.TryWriteLong(
            sprite + (uint)GraphicsLayouts.SimpleSpritePosCtlData,
            0x0FFEu));
        Assert.True(memory.TryWriteWord(sprite + (uint)GraphicsLayouts.SimpleSpriteX, 7));
        Assert.True(memory.TryWriteWord(sprite + (uint)GraphicsLayouts.SimpleSpriteY, 8));

        Assert.False(core.MoveSprite(0, sprite, 20, 21));
        Assert.Equal((ushort)7, ReadWord(memory, sprite + (uint)GraphicsLayouts.SimpleSpriteX));
        Assert.Equal((ushort)8, ReadWord(memory, sprite + (uint)GraphicsLayouts.SimpleSpriteY));
    }

    [Fact]
    public void ExtendedSpriteAllocationConvertsPlanarBitmapAndPreservesOwnedStorage()
    {
        var memory = new FakeMemory(0x10000);
        var allocator = new BitmapAllocator();
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), new FakeDisplay());
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint bitMap = 0x200;
        const uint plane0 = 0x400;
        const uint plane1 = 0x404;

        Assert.True(memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 2));
        Assert.True(memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapRows, 2));
        Assert.True(memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, 2));
        Assert.True(memory.TryWriteLong(bitMap + (uint)GraphicsLayouts.BitMapPlanes, plane0));
        Assert.True(memory.TryWriteLong(bitMap + (uint)GraphicsLayouts.BitMapPlanes + 4, plane1));
        Assert.True(memory.TryWriteByte(plane0, 0x80));
        Assert.True(memory.TryWriteByte(plane1, 0x40));

        var state = new M68kCpuState { A = { [1] = 0, [2] = bitMap }, D = { [0] = 0xDEAD_BEEFu } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AllocSpriteDataA));
        var extSprite = state.D[0];
        Assert.NotEqual(0u, extSprite);
        Assert.Equal(2u, ReadWord(memory, extSprite + (uint)GraphicsLayouts.SimpleSpriteHeight));
        Assert.Equal(ushort.MaxValue, ReadWord(memory, extSprite + (uint)GraphicsLayouts.SimpleSpriteNum));
        Assert.Equal((ushort)1, ReadWord(memory, extSprite + (uint)GraphicsLayouts.ExtSpriteWordWidth));
        var image = ReadLong(memory, extSprite + (uint)GraphicsLayouts.SimpleSpritePosCtlData);
        Assert.Equal((ushort)0x4000, ReadWord(memory, image + 4));
        Assert.Equal((ushort)0x8000, ReadWord(memory, image + 6));
        Assert.Equal((ushort)0, ReadWord(memory, image + 8));
        Assert.Equal((ushort)0, ReadWord(memory, image + 10));

        state.A[2] = extSprite;
        state.D[0] = 0xFFFF_FFFF;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.FreeSpriteData));
        Assert.Equal(0u, state.D[0]);
        Assert.Collection(
            allocator.Freed,
            allocation =>
            {
                Assert.Equal(image, allocation.Address);
                Assert.Equal(16u, allocation.Bytes);
                Assert.Equal(GraphicsMemoryClass.Chip, allocation.MemoryClass);
            },
            allocation =>
            {
                Assert.Equal(extSprite, allocation.Address);
                Assert.Equal((uint)GraphicsLayouts.ExtSpriteSize, allocation.Bytes);
                Assert.Equal(GraphicsMemoryClass.Public, allocation.MemoryClass);
            });

        // FreeSpriteData is ownership-aware and must not free a foreign guest
        // envelope merely because its bytes happen to look valid.
        state.A[2] = 0x300;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.FreeSpriteData));
        Assert.Equal(2, allocator.Freed.Count);
    }

    [Fact]
    public void ExtendedSpriteGetAndChangeHonorTagsAndRejectUnsupportedForms()
    {
        var memory = new FakeMemory(0x10000);
        var allocator = new BitmapAllocator();
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), new FakeDisplay());
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint bitMap = 0x200;
        const uint plane0 = 0x400;
        const uint plane1 = 0x404;
        const uint tags = 0x600;

        WritePlanarSpriteSource(memory, bitMap, plane0, plane1);
        var first = AllocateExtendedSprite(adapter, bitMap);
        var second = AllocateExtendedSprite(adapter, bitMap);

        Assert.True(memory.TryWriteLong(tags, GraphicsLayouts.GsTagSpriteNum));
        Assert.True(memory.TryWriteLong(tags + 4, 3));
        Assert.True(memory.TryWriteLong(tags + 8, 0));
        Assert.True(memory.TryWriteLong(tags + 12, 0));

        var state = new M68kCpuState { A = { [0] = first, [1] = tags }, D = { [0] = 0xCAFE_BABEu } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.GetExtSpriteA));
        Assert.Equal(3u, state.D[0]);
        Assert.Equal((ushort)3, ReadWord(memory, first + (uint)GraphicsLayouts.SimpleSpriteNum));

        state.A[0] = 0;
        state.A[1] = first;
        state.A[2] = second;
        state.A[3] = 0;
        state.D[0] = 0x1234;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.ChangeExtSpriteA));
        Assert.Equal(1u, state.D[0]);
        Assert.Equal(
            ReadLong(memory, second + (uint)GraphicsLayouts.SimpleSpritePosCtlData),
            ReadLong(memory, first + (uint)GraphicsLayouts.SimpleSpritePosCtlData));
        Assert.Equal(
            ReadWord(memory, second + (uint)GraphicsLayouts.SimpleSpriteHeight),
            ReadWord(memory, first + (uint)GraphicsLayouts.SimpleSpriteHeight));

        // The bounded portable form intentionally rejects replication and
        // software/scan-doubled display requests instead of guessing.
        Assert.True(memory.TryWriteLong(tags, GraphicsLayouts.GsTagSoftSprite));
        Assert.True(memory.TryWriteLong(tags + 4, 1));
        Assert.True(memory.TryWriteLong(tags + 8, 0));
        state.A[0] = first;
        state.A[1] = tags;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.GetExtSpriteA));
        Assert.Equal(0xFFFF_FFFFu, state.D[0]);

        state.A[0] = first;
        state.A[1] = second;
        state.A[2] = 0;
        state.A[3] = tags;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.ChangeExtSpriteA));
        Assert.Equal(0u, state.D[0]);

        FreeExtendedSprite(adapter, first);
        FreeExtendedSprite(adapter, second);
        Assert.Equal(4, allocator.Freed.Count);
    }

    [Fact]
    public void ExtendedSpriteAllocationRejectsMalformedBitmapAndTagSpansWithoutLeaking()
    {
        var memory = new FakeMemory(0x10000);
        var allocator = new BitmapAllocator();
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), new FakeDisplay());
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint bitMap = 0x200;
        const uint plane0 = 0x400;
        const uint plane1 = 0x404;

        WritePlanarSpriteSource(memory, bitMap, plane0, plane1);
        Assert.True(memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, 1));
        var state = new M68kCpuState { A = { [1] = 0, [2] = bitMap } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AllocSpriteDataA));
        Assert.Equal(0u, state.D[0]);
        Assert.Empty(allocator.Freed);

        Assert.True(memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, 2));
        const uint truncatedTags = 0xFFFC;
        state.A[1] = truncatedTags;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AllocSpriteDataA));
        Assert.Equal(0u, state.D[0]);
        Assert.Empty(allocator.Freed);

        // Unsupported width is rejected before either allocator call.
        const uint widthTags = 0x700;
        Assert.True(memory.TryWriteLong(widthTags, GraphicsLayouts.SpriteAWidth));
        Assert.True(memory.TryWriteLong(widthTags + 4, 32));
        Assert.True(memory.TryWriteLong(widthTags + 8, 0));
        state.A[1] = widthTags;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AllocSpriteDataA));
        Assert.Equal(0u, state.D[0]);
        Assert.Empty(allocator.Freed);
    }

    private static uint AllocateExtendedSprite(
        CopperStartGraphicsRegisterAdapter adapter,
        uint bitMap)
    {
        var state = new M68kCpuState { A = { [1] = 0, [2] = bitMap } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AllocSpriteDataA));
        Assert.NotEqual(0u, state.D[0]);
        return state.D[0];
    }

    private static void FreeExtendedSprite(
        CopperStartGraphicsRegisterAdapter adapter,
        uint extSprite)
    {
        var state = new M68kCpuState { A = { [2] = extSprite } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.FreeSpriteData));
        Assert.Equal(0u, state.D[0]);
    }

    private static void WritePlanarSpriteSource(
        FakeMemory memory,
        uint bitMap,
        uint plane0,
        uint plane1)
    {
        Assert.True(memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 2));
        Assert.True(memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapRows, 2));
        Assert.True(memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, 2));
        Assert.True(memory.TryWriteLong(bitMap + (uint)GraphicsLayouts.BitMapPlanes, plane0));
        Assert.True(memory.TryWriteLong(bitMap + (uint)GraphicsLayouts.BitMapPlanes + 4, plane1));
        Assert.True(memory.TryWriteByte(plane0, 0x80));
        Assert.True(memory.TryWriteByte(plane1, 0x40));
    }

    [Fact]
    public void InitGelsLinksSentinelsPreservesBoundsAndClearsBoundaryCollisionVector()
    {
        var memory = new FakeMemory(0x1000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint head = 0x100;
        const uint tail = 0x180;
        const uint gelsInfo = 0x240;
        const uint collisionTable = 0x300;

        Assert.True(memory.TryWriteByte(gelsInfo + (uint)GraphicsLayouts.GelsInfoSpriteReserved, 0x03));
        Assert.True(memory.TryWriteWord(gelsInfo + (uint)GraphicsLayouts.GelsInfoLeftmost, 1));
        Assert.True(memory.TryWriteWord(gelsInfo + (uint)GraphicsLayouts.GelsInfoRightmost, 319));
        Assert.True(memory.TryWriteWord(gelsInfo + (uint)GraphicsLayouts.GelsInfoTopmost, 1));
        Assert.True(memory.TryWriteWord(gelsInfo + (uint)GraphicsLayouts.GelsInfoBottommost, 199));
        Assert.True(memory.TryWriteLong(
            gelsInfo + (uint)GraphicsLayouts.GelsInfoCollisionHandler,
            collisionTable));
        Assert.True(memory.TryWriteLong(collisionTable, 0x1234_5678));
        Assert.True(memory.TryWriteByte(head, 0xAA));
        Assert.True(memory.TryWriteByte(tail, 0xBB));

        var state = new M68kCpuState { A = { [0] = head, [1] = tail, [2] = gelsInfo }, D = { [0] = 0xFFFF } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.InitGels));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal(tail, ReadLong(memory, head + (uint)GraphicsLayouts.VSpriteNext));
        Assert.Equal(head, ReadLong(memory, tail + (uint)GraphicsLayouts.VSpritePrev));
        Assert.Equal(0u, ReadLong(memory, tail + (uint)GraphicsLayouts.VSpriteNext));
        Assert.Equal((ushort)0x8000, ReadWord(memory, head + (uint)GraphicsLayouts.VSpriteY));
        Assert.Equal((ushort)0x7FFF, ReadWord(memory, tail + (uint)GraphicsLayouts.VSpriteX));
        Assert.Equal(head, ReadLong(memory, gelsInfo + (uint)GraphicsLayouts.GelsInfoHead));
        Assert.Equal(tail, ReadLong(memory, gelsInfo + (uint)GraphicsLayouts.GelsInfoTail));
        Assert.Equal((byte)0x03, ReadByte(memory, gelsInfo + (uint)GraphicsLayouts.GelsInfoSpriteReserved));
        Assert.Equal((ushort)319, ReadWord(memory, gelsInfo + (uint)GraphicsLayouts.GelsInfoRightmost));
        Assert.Equal(0u, ReadLong(memory, collisionTable));

        state.D[0] = 4;
        state.A[0] = 0x1234_5678;
        state.A[1] = gelsInfo;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.SetCollision));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal(0x1234_5678u, ReadLong(memory, collisionTable + 16));

        state.D[0] = 16;
        Assert.False(adapter.TryInvoke(state, (int)GraphicsLvo.SetCollision));
        Assert.Equal(0x1234_5678u, ReadLong(memory, collisionTable + 16));
    }

    [Fact]
    public void InitMasksBuildsTrueVSpriteCollisionAndBorderMasks()
    {
        var memory = new FakeMemory(0x1000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint sprite = 0x100;
        const uint image = 0x400;
        const uint border = 0x500;
        const uint collision = 0x600;

        Assert.True(memory.TryWriteWord(sprite + (uint)GraphicsLayouts.VSpriteFlags, GraphicsLayouts.VSpriteFlag));
        Assert.True(memory.TryWriteWord(sprite + (uint)GraphicsLayouts.VSpriteHeight, 2));
        Assert.True(memory.TryWriteWord(sprite + (uint)GraphicsLayouts.VSpriteWidth, 1));
        Assert.True(memory.TryWriteWord(sprite + (uint)GraphicsLayouts.VSpriteDepth, 2));
        Assert.True(memory.TryWriteLong(sprite + (uint)GraphicsLayouts.VSpriteImageData, image));
        Assert.True(memory.TryWriteLong(sprite + (uint)GraphicsLayouts.VSpriteBorderLine, border));
        Assert.True(memory.TryWriteLong(sprite + (uint)GraphicsLayouts.VSpriteCollMask, collision));
        Assert.True(memory.TryWriteWord(image, 0x8000));
        Assert.True(memory.TryWriteWord(image + 2, 0));
        Assert.True(memory.TryWriteWord(image + 4, 0x4000));
        Assert.True(memory.TryWriteWord(image + 6, 0x2000));

        var state = new M68kCpuState { A = { [0] = sprite }, D = { [0] = 0xCAFE } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.InitMasks));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal((ushort)0xE000, ReadWord(memory, border));
        Assert.Equal((ushort)0xC000, ReadWord(memory, collision));
        Assert.Equal((ushort)0x2000, ReadWord(memory, collision + 2));

        // The portable foundation does not guess Bob image/shadow layout.
        Assert.True(memory.TryWriteWord(sprite + (uint)GraphicsLayouts.VSpriteFlags, 0));
        Assert.False(core.InitMasks(sprite));
        Assert.Equal((ushort)0xE000, ReadWord(memory, border));
        Assert.Equal((ushort)0xC000, ReadWord(memory, collision));
    }

    [Fact]
    public void AddAndRemoveVSpriteMaintainSortedReciprocalGuestLinks()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint rastPort = 0x80;
        const uint gelsInfo = 0x240;
        const uint head = 0x300;
        const uint tail = 0x340;
        const uint first = 0x400;
        const uint second = 0x440;

        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortGelsInfo, gelsInfo));
        Assert.True(core.InitGels(head, tail, gelsInfo));
        Assert.True(memory.TryWriteWord(first + (uint)GraphicsLayouts.VSpriteY, 10));
        Assert.True(memory.TryWriteWord(first + (uint)GraphicsLayouts.VSpriteX, 20));
        Assert.True(memory.TryWriteWord(second + (uint)GraphicsLayouts.VSpriteY, 10));
        Assert.True(memory.TryWriteWord(second + (uint)GraphicsLayouts.VSpriteX, 30));

        var state = new M68kCpuState { A = { [0] = second, [1] = rastPort }, D = { [0] = 0xFFFF } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AddVSprite));
        state.A[0] = first;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AddVSprite));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal(first, ReadLong(memory, head + (uint)GraphicsLayouts.VSpriteNext));
        Assert.Equal(second, ReadLong(memory, first + (uint)GraphicsLayouts.VSpriteNext));
        Assert.Equal(tail, ReadLong(memory, second + (uint)GraphicsLayouts.VSpriteNext));
        Assert.Equal(first, ReadLong(memory, second + (uint)GraphicsLayouts.VSpritePrev));

        // A linked VSprite cannot be added twice; removal requires reciprocal
        // neighbors and clears the removed guest links.
        state.A[0] = first;
        Assert.False(adapter.TryInvoke(state, (int)GraphicsLvo.AddVSprite));
        Assert.True(memory.TryWriteWord(first + (uint)GraphicsLayouts.VSpriteY, 40));
        Assert.True(memory.TryWriteWord(second + (uint)GraphicsLayouts.VSpriteY, 5));
        state.A[1] = rastPort;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.SortGList));
        Assert.Equal(second, ReadLong(memory, head + (uint)GraphicsLayouts.VSpriteNext));
        Assert.Equal(first, ReadLong(memory, second + (uint)GraphicsLayouts.VSpriteNext));

        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.RemVSprite));
        Assert.Equal(0u, ReadLong(memory, first + (uint)GraphicsLayouts.VSpriteNext));
        Assert.Equal(0u, ReadLong(memory, first + (uint)GraphicsLayouts.VSpritePrev));
        Assert.Equal(second, ReadLong(memory, head + (uint)GraphicsLayouts.VSpriteNext));
        Assert.Equal(head, ReadLong(memory, second + (uint)GraphicsLayouts.VSpritePrev));

        state.A[0] = head;
        Assert.False(adapter.TryInvoke(state, (int)GraphicsLvo.RemVSprite));
    }

    [Fact]
    public void AnimationVectorsLinkBobInitializeTimersAndAdvanceFixedPointPosition()
    {
        var memory = new FakeMemory(0x4000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint rastPort = 0x80;
        const uint gelsInfo = 0x240;
        const uint head = 0x300;
        const uint tail = 0x340;
        const uint animOb = 0x500;
        const uint component = 0x580;
        const uint bob = 0x600;
        const uint vSprite = 0x640;
        const uint animKey = 0x700;

        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortGelsInfo, gelsInfo));
        Assert.True(core.InitGels(head, tail, gelsInfo));
        Assert.True(memory.TryWriteLong(animOb + (uint)GraphicsLayouts.AnimObHeadComp, component));
        Assert.True(memory.TryWriteLong(component + (uint)GraphicsLayouts.AnimCompAnimBob, bob));
        Assert.True(memory.TryWriteWord(component + (uint)GraphicsLayouts.AnimCompTimeSet, 3));
        Assert.True(memory.TryWriteWord(component + (uint)GraphicsLayouts.AnimCompXTrans, 64));
        Assert.True(memory.TryWriteWord(component + (uint)GraphicsLayouts.AnimCompYTrans, 64));
        Assert.True(memory.TryWriteLong(bob + (uint)GraphicsLayouts.BobVSprite, vSprite));
        Assert.True(memory.TryWriteWord(vSprite + (uint)GraphicsLayouts.VSpriteWidth, 1));
        Assert.True(memory.TryWriteWord(vSprite + (uint)GraphicsLayouts.VSpriteHeight, 1));
        Assert.True(memory.TryWriteLong(animOb + (uint)GraphicsLayouts.AnimObX, 640));
        Assert.True(memory.TryWriteLong(animOb + (uint)GraphicsLayouts.AnimObY, 128));
        Assert.True(memory.TryWriteWord(animOb + (uint)GraphicsLayouts.AnimObX, 640));
        Assert.True(memory.TryWriteWord(animOb + (uint)GraphicsLayouts.AnimObY, 128));
        Assert.True(memory.TryWriteWord(animOb + (uint)GraphicsLayouts.AnimObXVel, 64));
        Assert.True(memory.TryWriteWord(animOb + (uint)GraphicsLayouts.AnimObYVel, 0));
        Assert.True(memory.TryWriteLong(animKey, 0));

        var state = new M68kCpuState
        {
            A = { [0] = animOb, [1] = animKey, [2] = rastPort },
            D = { [0] = 0xFFFF_FFFF }
        };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AddAnimOb));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal(animOb, ReadLong(memory, animKey));
        Assert.Equal((ushort)3, ReadWord(memory, component + (uint)GraphicsLayouts.AnimCompTimer));
        Assert.Equal(vSprite, ReadLong(memory, head + (uint)GraphicsLayouts.VSpriteNext));
        Assert.Equal(bob, ReadLong(memory, vSprite + (uint)GraphicsLayouts.VSpriteVSBob));

        state.A[0] = animKey;
        state.A[1] = rastPort;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.Animate));
        Assert.Equal((ushort)640, ReadWord(memory, animOb + (uint)GraphicsLayouts.AnimObOldX));
        Assert.Equal((ushort)704, ReadWord(memory, animOb + (uint)GraphicsLayouts.AnimObX));
        Assert.Equal((ushort)12, ReadWord(memory, vSprite + (uint)GraphicsLayouts.VSpriteX));
        Assert.Equal((ushort)3, ReadWord(memory, vSprite + (uint)GraphicsLayouts.VSpriteY));
        Assert.Equal((ushort)2, ReadWord(memory, component + (uint)GraphicsLayouts.AnimCompTimer));

        state.A[0] = bob;
        state.A[1] = rastPort;
        state.A[2] = 0;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.RemIBob));
        Assert.Equal(0u, ReadLong(memory, vSprite + (uint)GraphicsLayouts.VSpriteNext));
        Assert.Equal(0u, ReadLong(memory, vSprite + (uint)GraphicsLayouts.VSpriteVSBob));
    }

    [Fact]
    public void AnimationSequenceSwitchReplacesOnlyTheActiveComponentAndBob()
    {
        var memory = new FakeMemory(0x6000);
        var core = CreateCore(memory);
        const uint rastPort = 0x80;
        const uint gelsInfo = 0x240;
        const uint head = 0x300;
        const uint tail = 0x340;
        const uint animOb = 0x500;
        const uint first = 0x580;
        const uint second = 0x600;
        const uint firstBob = 0x680;
        const uint firstVSprite = 0x6C0;
        const uint secondBob = 0x740;
        const uint secondVSprite = 0x780;
        const uint animKey = 0x800;

        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortGelsInfo, gelsInfo));
        Assert.True(core.InitGels(head, tail, gelsInfo));
        Assert.True(memory.TryWriteLong(animOb + (uint)GraphicsLayouts.AnimObHeadComp, first));
        Assert.True(memory.TryWriteLong(first + (uint)GraphicsLayouts.AnimCompNextSeq, second));
        Assert.True(memory.TryWriteLong(first + (uint)GraphicsLayouts.AnimCompPrevSeq, second));
        Assert.True(memory.TryWriteLong(second + (uint)GraphicsLayouts.AnimCompNextSeq, first));
        Assert.True(memory.TryWriteLong(second + (uint)GraphicsLayouts.AnimCompPrevSeq, first));
        Assert.True(memory.TryWriteLong(first + (uint)GraphicsLayouts.AnimCompAnimBob, firstBob));
        Assert.True(memory.TryWriteLong(second + (uint)GraphicsLayouts.AnimCompAnimBob, secondBob));
        Assert.True(memory.TryWriteWord(first + (uint)GraphicsLayouts.AnimCompTimeSet, 1));
        Assert.True(memory.TryWriteWord(second + (uint)GraphicsLayouts.AnimCompTimeSet, 1));
        Assert.True(memory.TryWriteWord(second + (uint)GraphicsLayouts.AnimCompXTrans, 128));
        Assert.True(memory.TryWriteLong(firstBob + (uint)GraphicsLayouts.BobVSprite, firstVSprite));
        Assert.True(memory.TryWriteLong(secondBob + (uint)GraphicsLayouts.BobVSprite, secondVSprite));
        Assert.True(memory.TryWriteWord(firstVSprite + (uint)GraphicsLayouts.VSpriteWidth, 1));
        Assert.True(memory.TryWriteWord(firstVSprite + (uint)GraphicsLayouts.VSpriteHeight, 1));
        Assert.True(memory.TryWriteWord(secondVSprite + (uint)GraphicsLayouts.VSpriteWidth, 1));
        Assert.True(memory.TryWriteWord(secondVSprite + (uint)GraphicsLayouts.VSpriteHeight, 1));
        Assert.True(memory.TryWriteWord(animOb + (uint)GraphicsLayouts.AnimObX, 640));
        Assert.True(memory.TryWriteWord(animOb + (uint)GraphicsLayouts.AnimObY, 128));
        Assert.True(memory.TryWriteLong(animKey, 0));

        Assert.True(core.AddAnimOb(animOb, animKey, rastPort));
        Assert.Equal(first, ReadLong(memory, animOb + (uint)GraphicsLayouts.AnimObHeadComp));
        Assert.Equal(firstVSprite, ReadLong(memory, head + (uint)GraphicsLayouts.VSpriteNext));

        Assert.True(core.Animate(animKey, rastPort));
        Assert.Equal((ushort)0, ReadWord(memory, first + (uint)GraphicsLayouts.AnimCompTimer));
        Assert.Equal(firstVSprite, ReadLong(memory, head + (uint)GraphicsLayouts.VSpriteNext));

        Assert.True(core.Animate(animKey, rastPort));
        Assert.Equal(second, ReadLong(memory, animOb + (uint)GraphicsLayouts.AnimObHeadComp));
        Assert.Equal(0u, ReadLong(memory, first + (uint)GraphicsLayouts.AnimCompPrevComp));
        Assert.Equal(0u, ReadLong(memory, first + (uint)GraphicsLayouts.AnimCompNextComp));
        Assert.Equal(secondVSprite, ReadLong(memory, head + (uint)GraphicsLayouts.VSpriteNext));
        Assert.Equal(first, ReadLong(memory, second + (uint)GraphicsLayouts.AnimCompNextSeq));
        Assert.Equal((ushort)1, ReadWord(memory, second + (uint)GraphicsLayouts.AnimCompTimer));
        Assert.Equal(secondBob, ReadLong(memory, secondVSprite + (uint)GraphicsLayouts.VSpriteVSBob));
    }

    [Fact]
    public void AnimationBufferVectorsTrackGuestPointersAndFreeOnlyOwnedStorage()
    {
        var memory = new FakeMemory(0x8000);
        var allocator = new SteppingAllocator(0x5000);
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), new FakeDisplay());
        const uint rastPort = 0x80;
        const uint bitMap = 0x100;
        const uint animOb = 0x300;
        const uint component = 0x380;
        const uint nextComponent = 0x480;
        const uint bob = 0x400;
        const uint vSprite = 0x440;
        const uint nextBob = 0x4C0;
        const uint nextVSprite = 0x500;
        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortBitMap, bitMap));
        Assert.True(memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 2));
        Assert.True(memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapRows, 8));
        Assert.True(memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, 2));
        Assert.True(memory.TryWriteLong(animOb + (uint)GraphicsLayouts.AnimObHeadComp, component));
        Assert.True(memory.TryWriteLong(component + (uint)GraphicsLayouts.AnimCompNextSeq, nextComponent));
        Assert.True(memory.TryWriteLong(nextComponent + (uint)GraphicsLayouts.AnimCompNextSeq, component));
        Assert.True(memory.TryWriteLong(component + (uint)GraphicsLayouts.AnimCompAnimBob, bob));
        Assert.True(memory.TryWriteLong(nextComponent + (uint)GraphicsLayouts.AnimCompAnimBob, nextBob));
        Assert.True(memory.TryWriteLong(bob + (uint)GraphicsLayouts.BobVSprite, vSprite));
        Assert.True(memory.TryWriteLong(nextBob + (uint)GraphicsLayouts.BobVSprite, nextVSprite));
        Assert.True(memory.TryWriteWord(vSprite + (uint)GraphicsLayouts.VSpriteWidth, 1));
        Assert.True(memory.TryWriteWord(vSprite + (uint)GraphicsLayouts.VSpriteHeight, 2));
        Assert.True(memory.TryWriteWord(vSprite + (uint)GraphicsLayouts.VSpriteDepth, 1));
        Assert.True(memory.TryWriteLong(vSprite + (uint)GraphicsLayouts.VSpriteImageData, 0x1000));
        Assert.True(memory.TryWriteWord(0x1000, 0x8000));
        Assert.True(memory.TryWriteWord(0x1002, 0x4000));
        Assert.True(memory.TryWriteWord(nextVSprite + (uint)GraphicsLayouts.VSpriteWidth, 1));
        Assert.True(memory.TryWriteWord(nextVSprite + (uint)GraphicsLayouts.VSpriteHeight, 2));
        Assert.True(memory.TryWriteWord(nextVSprite + (uint)GraphicsLayouts.VSpriteDepth, 1));
        Assert.True(memory.TryWriteLong(nextVSprite + (uint)GraphicsLayouts.VSpriteImageData, 0x1100));
        Assert.True(memory.TryWriteWord(0x1100, 0x4000));
        Assert.True(memory.TryWriteWord(0x1102, 0x2000));

        Assert.True(core.GetGBuffers(animOb, rastPort, 0));
        var save = ReadLong(memory, bob + (uint)GraphicsLayouts.BobSaveBuffer);
        var shadow = ReadLong(memory, bob + (uint)GraphicsLayouts.BobImageShadow);
        var border = ReadLong(memory, vSprite + (uint)GraphicsLayouts.VSpriteBorderLine);
        var mask = ReadLong(memory, vSprite + (uint)GraphicsLayouts.VSpriteCollMask);
        var nextShadow = ReadLong(memory, nextBob + (uint)GraphicsLayouts.BobImageShadow);
        var nextBorder = ReadLong(memory, nextVSprite + (uint)GraphicsLayouts.VSpriteBorderLine);
        var nextMask = ReadLong(memory, nextVSprite + (uint)GraphicsLayouts.VSpriteCollMask);
        Assert.NotEqual(0u, save);
        Assert.NotEqual(0u, shadow);
        Assert.NotEqual(0u, border);
        Assert.NotEqual(0u, mask);
        Assert.NotEqual(0u, nextShadow);
        Assert.NotEqual(0u, nextBorder);
        Assert.NotEqual(0u, nextMask);
        Assert.True(core.InitGMasks(animOb));
        Assert.False(core.GetGBuffers(animOb, rastPort, 0));
        Assert.True(core.FreeGBuffers(animOb, rastPort, 0));
        Assert.Equal(0u, ReadLong(memory, bob + (uint)GraphicsLayouts.BobSaveBuffer));
        Assert.Equal(0u, ReadLong(memory, bob + (uint)GraphicsLayouts.BobImageShadow));
        Assert.Equal(0u, ReadLong(memory, vSprite + (uint)GraphicsLayouts.VSpriteBorderLine));
        Assert.Equal(0u, ReadLong(memory, vSprite + (uint)GraphicsLayouts.VSpriteCollMask));
        Assert.Equal(0u, ReadLong(memory, nextBob + (uint)GraphicsLayouts.BobImageShadow));
        Assert.Equal(0u, ReadLong(memory, nextVSprite + (uint)GraphicsLayouts.VSpriteBorderLine));
        Assert.Equal(0u, ReadLong(memory, nextVSprite + (uint)GraphicsLayouts.VSpriteCollMask));
        Assert.Equal(8, allocator.FreeCount);
    }

    [Fact]
    public void AnimationDisplayAndCollisionVectorsUseExplicitGelsBackendBoundary()
    {
        var memory = new FakeMemory(0x2000);
        var backend = new RecordingGelsBackend();
        var core = new GraphicsLibraryCore(
            memory,
            new FakeAllocator(),
            new FakeBlitter(),
            new FakeDisplay(),
            gels: backend);
        const uint rastPort = 0x80;
        const uint gelsInfo = 0x240;
        const uint head = 0x300;
        const uint tail = 0x340;
        const uint viewPort = 0x500;
        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortGelsInfo, gelsInfo));
        Assert.True(core.InitGels(head, tail, gelsInfo));

        Assert.True(core.DoCollision(rastPort));
        Assert.True(core.DrawGList(rastPort, viewPort));
        Assert.Equal(1, backend.CollisionCount);
        Assert.Equal((rastPort, viewPort), backend.Drawn.Last());
    }

    [Fact]
    public void HostGraphicsServicesForwardsGelsVectorsOnlyWhenAllCallbacksArePresent()
    {
        var hostMemory = new HostGuestMemory(new AmigaBus());
        var drawCount = 0;
        var collisionCount = 0;
        var services = CreateHostGraphicsServices(
            hostMemory,
            drawGList: (_, _) => drawCount++,
            doCollision: _ => collisionCount++,
            remIBob: (_, _, _) => { });
        const uint rastPort = 0x1000;
        const uint gelsInfo = 0x1100;
        const uint head = 0x1200;
        const uint tail = 0x1280;
        const uint viewPort = 0x1300;
        hostMemory.WriteLong(rastPort + (uint)GraphicsLayouts.RastPortGelsInfo, gelsInfo);

        var state = new M68kCpuState
        {
            A = { [0] = head, [1] = tail, [2] = gelsInfo },
            D = { [0] = 0xDEAD_BEEFu }
        };
        services.Invoke(state, (int)GraphicsLvo.InitGels);
        Assert.Equal(0u, state.D[0]);

        state.A[1] = rastPort;
        state.D[0] = 0xCAFE;
        services.Invoke(state, (int)GraphicsLvo.DoCollision);
        Assert.Equal(1, collisionCount);
        Assert.Equal(0u, state.D[0]);

        state.A[0] = viewPort;
        state.A[1] = rastPort;
        state.D[0] = 0xBABE;
        services.Invoke(state, (int)GraphicsLvo.DrawGList);
        Assert.Equal(1, drawCount);
        Assert.Equal(0u, state.D[0]);
    }

    [Fact]
    public void HostGraphicsServicesForwardsUntimedWaitBOVPToTheDisplayBoundary()
    {
        var hostMemory = new HostGuestMemory(new AmigaBus());
        uint waitedFor = 0;
        var services = CreateHostGraphicsServices(
            hostMemory,
            waitForBeginningOfVerticalBlank: viewPort => waitedFor = viewPort);
        const uint viewPort = 0x1000;
        const uint rasInfo = 0x1100;
        hostMemory.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        hostMemory.WriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
        hostMemory.WriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, 0);
        hostMemory.WriteWord(rasInfo + (uint)GraphicsLayouts.RasInfoRxOffset, 0);
        hostMemory.WriteWord(rasInfo + (uint)GraphicsLayouts.RasInfoRyOffset, 0);

        var state = new M68kCpuState
        {
            A = { [0] = viewPort },
            D = { [0] = 0xDEAD_BEEFu }
        };
        services.Invoke(state, (int)GraphicsLvo.WaitBOVP);

        Assert.Equal(viewPort, waitedFor);
        Assert.Equal(0u, state.D[0]);
    }

    [Fact]
    public void HostGraphicsServicesForwardsValidatedSpriteProjectionTransitions()
    {
        var hostMemory = new HostGuestMemory(new AmigaBus());
        (uint ViewPort, uint Sprite, short X, short Y)? moved = null;
        (uint ViewPort, uint Sprite, uint Image)? changed = null;
        var services = CreateHostGraphicsServices(
            hostMemory,
            moveSprite: (viewPort, sprite, x, y) => moved = (viewPort, sprite, x, y),
            changeSprite: (viewPort, sprite, image) => changed = (viewPort, sprite, image));
        const uint sprite = 0x2000;
        const uint image = 0x3000;
        hostMemory.WriteWord(sprite + (uint)GraphicsLayouts.SimpleSpriteHeight, 2);
        for (var offset = 0; offset < 16; offset++)
            hostMemory.WriteByte(image + (uint)offset, (byte)(offset + 1));

        var state = new M68kCpuState
        {
            A = { [0] = 0, [1] = sprite },
            D = { [0] = 17, [1] = 29 }
        };
        services.Invoke(state, (int)GraphicsLvo.MoveSprite);
        Assert.Equal((0u, sprite, (short)17, (short)29), moved);
        Assert.Equal(0u, state.D[0]);

        state.A[2] = image;
        services.Invoke(state, (int)GraphicsLvo.ChangeSprite);
        Assert.Equal((0u, sprite, image), changed);
        Assert.Equal(0u, state.D[0]);
    }

    [Fact]
    public void DoCollisionDispatchesMaskSelectedGelAndBoundaryRoutines()
    {
        var memory = new FakeMemory(0x2000);
        var backend = new RecordingGelsBackend();
        var core = new GraphicsLibraryCore(
            memory,
            new FakeAllocator(),
            new FakeBlitter(),
            new FakeDisplay(),
            gels: backend);
        const uint rastPort = 0x80;
        const uint gelsInfo = 0x240;
        const uint head = 0x300;
        const uint tail = 0x340;
        const uint first = 0x400;
        const uint second = 0x440;
        const uint collisionTable = 0x500;
        const uint firstMask = 0x600;
        const uint secondMask = 0x620;

        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortGelsInfo, gelsInfo));
        Assert.True(memory.TryWriteLong(gelsInfo + (uint)GraphicsLayouts.GelsInfoCollisionHandler, collisionTable));
        Assert.True(memory.TryWriteWord(gelsInfo + (uint)GraphicsLayouts.GelsInfoLeftmost, 0));
        Assert.True(memory.TryWriteWord(gelsInfo + (uint)GraphicsLayouts.GelsInfoRightmost, 100));
        Assert.True(memory.TryWriteWord(gelsInfo + (uint)GraphicsLayouts.GelsInfoTopmost, 0));
        Assert.True(memory.TryWriteWord(gelsInfo + (uint)GraphicsLayouts.GelsInfoBottommost, 100));
        Assert.True(memory.TryWriteLong(collisionTable + 4, 0x1234_5678));
        Assert.True(core.InitGels(head, tail, gelsInfo));
        Assert.True(memory.TryWriteLong(collisionTable, 0x1111_2222));

        foreach (var sprite in new[] { first, second })
        {
            Assert.True(memory.TryWriteWord(sprite + (uint)GraphicsLayouts.VSpriteWidth, 1));
            Assert.True(memory.TryWriteWord(sprite + (uint)GraphicsLayouts.VSpriteHeight, 1));
            Assert.True(memory.TryWriteWord(sprite + (uint)GraphicsLayouts.VSpriteFlags, GraphicsLayouts.VSpriteFlag));
        }
        Assert.True(memory.TryWriteWord(first + (uint)GraphicsLayouts.VSpriteX, 10));
        Assert.True(memory.TryWriteWord(first + (uint)GraphicsLayouts.VSpriteY, 10));
        Assert.True(memory.TryWriteWord(first + (uint)GraphicsLayouts.VSpriteMeMask, 0x0002));
        Assert.True(memory.TryWriteWord(first + (uint)GraphicsLayouts.VSpriteHitMask, 0));
        Assert.True(memory.TryWriteLong(first + (uint)GraphicsLayouts.VSpriteCollMask, firstMask));
        Assert.True(memory.TryWriteWord(firstMask, 0xC000));
        Assert.True(memory.TryWriteWord(second + (uint)GraphicsLayouts.VSpriteX, 11));
        Assert.True(memory.TryWriteWord(second + (uint)GraphicsLayouts.VSpriteY, 10));
        Assert.True(memory.TryWriteWord(second + (uint)GraphicsLayouts.VSpriteMeMask, 0));
        Assert.True(memory.TryWriteWord(second + (uint)GraphicsLayouts.VSpriteHitMask, 0x0002));
        Assert.True(memory.TryWriteLong(second + (uint)GraphicsLayouts.VSpriteCollMask, secondMask));
        Assert.True(memory.TryWriteWord(secondMask, 0x8000));
        Assert.True(core.AddVSprite(first, rastPort));
        Assert.True(core.AddVSprite(second, rastPort));

        Assert.True(core.DoCollision(rastPort));
        var pair = Assert.Single(backend.Collisions);
        Assert.Equal(first, pair.First);
        Assert.Equal(second, pair.Second);
        Assert.Equal(0x1234_5678u, pair.Routine);

        backend.Collisions.Clear();
        Assert.True(memory.TryWriteWord(first + (uint)GraphicsLayouts.VSpriteX, unchecked((ushort)-1)));
        Assert.True(memory.TryWriteWord(first + (uint)GraphicsLayouts.VSpriteHitMask, 1));
        Assert.Equal((short)-1, ReadSignedWord(memory, first + (uint)GraphicsLayouts.VSpriteX));
        Assert.Equal((ushort)1, ReadWord(memory, first + (uint)GraphicsLayouts.VSpriteHitMask));
        Assert.Equal(collisionTable, ReadLong(memory, gelsInfo + (uint)GraphicsLayouts.GelsInfoCollisionHandler));
        Assert.True(core.DoCollision(rastPort));
        var boundary = Assert.Single(backend.Boundaries);
        Assert.Equal(first, boundary.Sprite);
        Assert.Equal((ushort)4, boundary.Flags);
        Assert.Equal(0x1111_2222u, boundary.Routine);
    }

    [Fact]
    public void ScalerDivMatchesBitMapScaleExtentArithmeticAndRejectsInvalidFactors()
    {
        var memory = new FakeMemory(0x1000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);

        Assert.Equal((ushort)200, core.ScalerDiv(100, 2, 1));
        Assert.Equal((ushort)33, core.ScalerDiv(100, 1, 3));
        Assert.Equal(ushort.MaxValue, core.ScalerDiv(0x3FFF, 0x3FFF, 1));
        Assert.Equal((ushort)0, core.ScalerDiv(100, 1, 0));
        Assert.Equal((ushort)0, core.ScalerDiv(0x4000, 1, 1));

        var state = new M68kCpuState { D = { [0] = 640, [1] = 3, [2] = 2 } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.ScalerDiv));
        Assert.Equal(960u, state.D[0]);
    }

    [Fact]
    public void BitMapScaleWritesRatioDerivedExtentAndCopiesPlanarPixels()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint bitScaleArgs = 0x40;
        const uint source = 0x100;
        const uint destination = 0x180;
        const uint sourcePlane0 = 0x400;
        const uint sourcePlane1 = 0x500;
        const uint destinationPlane0 = 0x600;
        const uint destinationPlane1 = 0x700;

        WritePlanarBitmap(memory, source, sourcePlane0, sourcePlane1, rows: 2);
        WritePlanarBitmap(memory, destination, destinationPlane0, destinationPlane1, rows: 4);
        Assert.True(memory.TryWriteByte(sourcePlane0, 0xA0));
        Assert.True(memory.TryWriteByte(sourcePlane0 + 2, 0x50));
        Assert.True(memory.TryWriteByte(sourcePlane1, 0x60));
        Assert.True(memory.TryWriteByte(sourcePlane1 + 2, 0x30));
        Assert.True(memory.TryWriteByte(destinationPlane0, 0));
        Assert.True(memory.TryWriteByte(destinationPlane0 + 2, 0));
        Assert.True(memory.TryWriteByte(destinationPlane0 + 4, 0));
        Assert.True(memory.TryWriteByte(destinationPlane0 + 6, 0));
        Assert.True(memory.TryWriteByte(destinationPlane1, 0));
        Assert.True(memory.TryWriteByte(destinationPlane1 + 2, 0));
        Assert.True(memory.TryWriteByte(destinationPlane1 + 4, 0));
        Assert.True(memory.TryWriteByte(destinationPlane1 + 6, 0));
        WriteBitScaleArgs(
            memory,
            bitScaleArgs,
            source,
            destination,
            srcWidth: 4,
            srcHeight: 2,
            xSrcFactor: 4,
            xDestFactor: 8,
            ySrcFactor: 2,
            yDestFactor: 4);

        Assert.Equal(0, core.BitMapScale(bitScaleArgs));
        Assert.Equal((ushort)8, ReadWord(memory, bitScaleArgs + (uint)GraphicsLayouts.BitScaleArgsDestWidth));
        Assert.Equal((ushort)4, ReadWord(memory, bitScaleArgs + (uint)GraphicsLayouts.BitScaleArgsDestHeight));
        Assert.Equal((byte)0xCC, ReadByte(memory, destinationPlane0));
        Assert.Equal((byte)0xCC, ReadByte(memory, destinationPlane0 + 2));
        Assert.Equal((byte)0x33, ReadByte(memory, destinationPlane0 + 4));
        Assert.Equal((byte)0x33, ReadByte(memory, destinationPlane0 + 6));
        Assert.Equal((byte)0x3C, ReadByte(memory, destinationPlane1));
        Assert.Equal((byte)0x3C, ReadByte(memory, destinationPlane1 + 2));
        Assert.Equal((byte)0x0F, ReadByte(memory, destinationPlane1 + 4));
        Assert.Equal((byte)0x0F, ReadByte(memory, destinationPlane1 + 6));

        var state = new M68kCpuState { A = { [0] = bitScaleArgs }, D = { [0] = 0xA5A5_A5A5 } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.BitMapScale));
        Assert.Equal(0u, state.D[0]);
    }

    [Fact]
    public void BitMapScaleRejectsMalformedOrRtgEndpointsBeforeMutatingExtent()
    {
        var memory = new FakeMemory(0x1000);
        var core = CreateCore(memory);
        const uint bitScaleArgs = 0x40;
        const uint source = 0x100;
        const uint destination = 0x180;
        WritePlanarBitmap(memory, source, 0x400, 0x500, rows: 2);
        WritePlanarBitmap(memory, destination, 0x600, 0x700, rows: 2);
        WriteBitScaleArgs(
            memory,
            bitScaleArgs,
            source,
            destination,
            srcWidth: 4,
            srcHeight: 2,
            xSrcFactor: 1,
            xDestFactor: 2,
            ySrcFactor: 1,
            yDestFactor: 2);
        Assert.True(memory.TryWriteWord(
            bitScaleArgs + (uint)GraphicsLayouts.BitScaleArgsDestWidth,
            0x1234));
        Assert.Equal(-1, core.BitMapScale(bitScaleArgs));
        Assert.Equal((ushort)0x1234, ReadWord(memory, bitScaleArgs + (uint)GraphicsLayouts.BitScaleArgsDestWidth));

        Assert.True(memory.TryWriteLong(
            bitScaleArgs + (uint)GraphicsLayouts.BitScaleArgsFlags,
            1));
        Assert.Equal(-1, core.BitMapScale(bitScaleArgs));
        Assert.True(memory.TryWriteLong(
            bitScaleArgs + (uint)GraphicsLayouts.BitScaleArgsFlags,
            0));

        var adapter = new CopperStartGraphicsRegisterAdapter(
            core,
            isRtgBitMap: bitmap => bitmap == destination);
        var state = new M68kCpuState { A = { [0] = bitScaleArgs } };
        Assert.False(adapter.TryInvoke(state, (int)GraphicsLvo.BitMapScale));
        Assert.Equal((ushort)0x1234, ReadWord(memory, bitScaleArgs + (uint)GraphicsLayouts.BitScaleArgsDestWidth));
    }

    [Fact]
    public void HostGetVPModeIDUsesTheProfileModeProviderBoundary()
    {
        var hostMemory = new HostGuestMemory(new AmigaBus());
        var services = CreateHostGraphicsServices(
            hostMemory,
            getViewPortModeId: viewPort => viewPort == 0x500 ? 0x0002_9004u : GraphicsModeIds.Invalid);
        const uint viewPort = 0x500;
        const uint rasInfo = 0x580;

        hostMemory.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        hostMemory.WriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
        hostMemory.WriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, 0);

        var state = new M68kCpuState { A = { [0] = viewPort } };
        services.Invoke(state, (int)GraphicsLvo.GetVPModeID);
        Assert.Equal(0x0002_9004u, state.D[0]);

        state.A[0] = 0x00FF_0000;
        services.Invoke(state, (int)GraphicsLvo.GetVPModeID);
        Assert.Equal(GraphicsModeIds.Invalid, state.D[0]);
    }

    [Fact]
    public void AllocDBufInfoInitializesPublicMessageEnvelopesAndFreeMirrorsTheSize()
    {
        var memory = new FakeMemory(0x2000);
        var allocator = new FakeAllocator();
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), new FakeDisplay());
        const uint viewPort = 0x180;
        const uint rasInfo = 0x1C0;

        memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, 0);

        var dbuf = core.AllocDBufInfo(viewPort);
        Assert.Equal(0x1000u, dbuf);
        Assert.Equal((ushort)GraphicsLayouts.ExecMessageSize,
            ReadWord(memory, dbuf + (uint)GraphicsLayouts.DBufInfoSafeMessage + (uint)GraphicsLayouts.ExecMessageLength));
        Assert.Equal((ushort)GraphicsLayouts.ExecMessageSize,
            ReadWord(memory, dbuf + (uint)GraphicsLayouts.DBufInfoDispMessage + (uint)GraphicsLayouts.ExecMessageLength));
        Assert.Equal(0, core.FreeDBufInfo(dbuf));
        Assert.Equal((uint)GraphicsLayouts.DBufInfoSize, allocator.LastFreedBytes);
        Assert.Equal(GraphicsMemoryClass.Public, allocator.LastFreedClass);
        Assert.Equal(-1, core.FreeDBufInfo(dbuf));
        Assert.Equal(1, allocator.FreeCount);
        Assert.Equal(-1, core.FreeDBufInfo(0));
    }

    [Fact]
    public void AllocDBufInfoReturnsZeroWhenTheDisplayRejectsDoubleBuffering()
    {
        var memory = new FakeMemory(0x2000);
        var allocator = new FakeAllocator();
        var display = new FakeDisplay { DoubleBufferSupported = false };
        var core = new GraphicsLibraryCore(memory, allocator, new FakeBlitter(), display);
        const uint viewPort = 0x180;
        const uint rasInfo = 0x1C0;

        memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
        memory.TryWriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, 0);

        Assert.Equal(0u, core.AllocDBufInfo(viewPort));
        Assert.Equal(0u, allocator.LastAllocatedBytes);
    }

    [Fact]
    public void HostDBufInfoVectorsUsePublicAllocatorAndGuestStructureMemory()
    {
        var hostMemory = new HostGuestMemory(new AmigaBus());
        var allocatedBytes = 0;
        var allocatedFlags = 0u;
        var freedAddress = 0u;
        var freedBytes = 0;
        var services = CreateHostGraphicsServices(
            hostMemory,
            allocateMemory: (bytes, flags) =>
            {
                allocatedBytes = bytes;
                allocatedFlags = flags;
                return 0x1800u;
            },
            freeMemory: (address, bytes) =>
            {
                freedAddress = address;
                freedBytes = bytes;
            });
        const uint viewPort = 0x500;
        const uint rasInfo = 0x580;

        hostMemory.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        hostMemory.WriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
        hostMemory.WriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, 0);

        var state = new M68kCpuState { A = { [0] = viewPort } };
        services.Invoke(state, (int)GraphicsLvo.AllocDBufInfo);
        Assert.Equal(0x1800u, state.D[0]);
        Assert.Equal(GraphicsLayouts.DBufInfoSize, allocatedBytes);
        Assert.Equal(0x1u, allocatedFlags); // MEMF_PUBLIC
        Assert.Equal((ushort)GraphicsLayouts.ExecMessageSize,
            hostMemory.ReadWord(0x1800u + (uint)GraphicsLayouts.DBufInfoSafeMessage + (uint)GraphicsLayouts.ExecMessageLength));

        state.A[0] = state.D[0];
        services.Invoke(state, (int)GraphicsLvo.FreeDBufInfo);
        Assert.Equal(0u, state.D[0]);
        Assert.Equal(0x1800u, freedAddress);
        Assert.Equal(GraphicsLayouts.DBufInfoSize, freedBytes);
    }

    [Fact]
    public void HostAllocDBufInfoRejectsAnUnsupportedProfileModeBeforeAllocation()
    {
        var hostMemory = new HostGuestMemory(new AmigaBus());
        var allocations = 0;
        var services = CreateHostGraphicsServices(
            hostMemory,
            allocateMemory: (bytes, flags) =>
            {
                allocations++;
                return 0x1800u;
            },
            getViewPortModeId: _ => GraphicsModeIds.Invalid);
        const uint viewPort = 0x500;
        const uint rasInfo = 0x580;

        hostMemory.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        hostMemory.WriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
        hostMemory.WriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, 0);

        var state = new M68kCpuState { A = { [0] = viewPort } };
        services.Invoke(state, (int)GraphicsLvo.AllocDBufInfo);

        Assert.Equal(0u, state.D[0]);
        Assert.Equal(0, allocations);
    }

    [Fact]
    public void HostChangeVPBitMapUpdatesGuestRasInfoAndRequestsDisplayRebuild()
    {
        var hostMemory = new HostGuestMemory(new AmigaBus());
        var rebuilds = 0;
        var services = CreateHostGraphicsServices(
            hostMemory,
            requestDisplayRebuild: () => rebuilds++);
        const uint viewPort = 0x500;
        const uint rasInfo = 0x580;
        const uint firstBitMap = 0x700;
        const uint nextBitMap = 0x780;
        const uint dbufInfo = 0x1800;

        hostMemory.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        hostMemory.WriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
        hostMemory.WriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, firstBitMap);
        hostMemory.WriteWord(firstBitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 4);
        hostMemory.WriteByte(firstBitMap + (uint)GraphicsLayouts.BitMapDepth, 2);
        hostMemory.WriteWord(nextBitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 4);
        hostMemory.WriteByte(nextBitMap + (uint)GraphicsLayouts.BitMapDepth, 2);
        for (var offset = 0; offset < GraphicsLayouts.DBufInfoSize; offset++)
            hostMemory.WriteByte(dbufInfo + (uint)offset, 0xA5);

        var state = new M68kCpuState
        {
            A = { [0] = viewPort, [1] = nextBitMap, [2] = dbufInfo },
            Cycles = 27
        };
        services.Invoke(state, (int)GraphicsLvo.ChangeVPBitMap);

        Assert.Equal(0u, state.D[0]);
        Assert.Equal(27, state.Cycles);
        Assert.Equal(nextBitMap, hostMemory.ReadLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap));
        Assert.Equal(1, rebuilds);
    }

    [Fact]
    public void HostChangeVPBitMapForwardsPreviousBitmapToDoubleBufferScheduler()
    {
        var hostMemory = new HostGuestMemory(new AmigaBus());
        var scheduled = new List<(uint ViewPort, uint Previous, uint Next, uint DBufInfo, long Cycle)>();
        var services = CreateHostGraphicsServices(
            hostMemory,
            scheduleDoubleBufferMessages: (viewPort, previous, next, dbufInfo, cycle) =>
                scheduled.Add((viewPort, previous, next, dbufInfo, cycle)));
        const uint viewPort = 0x500;
        const uint rasInfo = 0x580;
        const uint firstBitMap = 0x700;
        const uint nextBitMap = 0x780;
        const uint dbufInfo = 0x1800;

        hostMemory.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        hostMemory.WriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, firstBitMap);
        hostMemory.WriteWord(firstBitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 4);
        hostMemory.WriteByte(firstBitMap + (uint)GraphicsLayouts.BitMapDepth, 2);
        hostMemory.WriteWord(nextBitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 4);
        hostMemory.WriteByte(nextBitMap + (uint)GraphicsLayouts.BitMapDepth, 2);
        for (var offset = 0; offset < GraphicsLayouts.DBufInfoSize; offset++)
            hostMemory.WriteByte(dbufInfo + (uint)offset, 0);

        var state = new M68kCpuState
        {
            A = { [0] = viewPort, [1] = nextBitMap, [2] = dbufInfo },
            Cycles = 77
        };
        services.Invoke(state, (int)GraphicsLvo.ChangeVPBitMap);

        var notification = Assert.Single(scheduled);
        Assert.Equal(viewPort, notification.ViewPort);
        Assert.Equal(firstBitMap, notification.Previous);
        Assert.Equal(nextBitMap, notification.Next);
        Assert.Equal(dbufInfo, notification.DBufInfo);
        Assert.Equal(77, notification.Cycle);
    }

    [Fact]
    public void HostRegisterAdapterMapsPureMemoryVectorsAndRoutesDisplayBoundary()
    {
        var memory = new FakeMemory(0x2000);
        var display = new FakeDisplay();
        var core = CreateCore(memory, display);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        var state = new M68kCpuState();
        state.A[1] = 0x20;

        state.D[0] = 5;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.SetAPen));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal(5, core.GetAPen(0x20));

        state.D[0] = 12;
        state.D[1] = 3;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.Move));
        Assert.Equal((ushort)12, ReadWord(memory, 0x20u + (uint)GraphicsLayouts.RastPortCurrentX));
        Assert.Equal((ushort)3, ReadWord(memory, 0x20u + (uint)GraphicsLayouts.RastPortCurrentY));

        state.D[0] = 2;
        state.D[1] = 2;
        state.D[2] = 3;
        state.D[3] = 3;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.RectFill));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal(1, core.ReadPixel(0x20, 2, 2));

        state.D[0] = 4;
        state.D[1] = 4;
        state.D[2] = 2;
        state.D[3] = 2;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.DrawEllipse));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal(1, core.ReadPixel(0x20, 4, 2));

        state.D[0] = 1;
        state.D[1] = 0;
        state.D[2] = 0;
        state.D[3] = 0;
        state.D[4] = 7;
        state.D[5] = 7;
        Assert.Equal(0, core.SetRast(0x20, 1));
        Assert.Equal(0, core.SetBPen(0x20, 2));
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.ScrollRasterBF));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal(1, core.ReadPixel(0x20, 0, 0));
        Assert.Equal(0, core.ReadPixel(0x20, 7, 0));

        memory.TryWriteLong(0x20u + (uint)GraphicsLayouts.RastPortLayer, 0x1234);
        Assert.False(adapter.TryInvoke(state, (int)GraphicsLvo.DrawEllipse));
        Assert.False(adapter.TryInvoke(state, (int)GraphicsLvo.ScrollRaster));
        memory.TryWriteLong(0x20u + (uint)GraphicsLayouts.RastPortLayer, 0);

        state.D[0] = 0;
        state.D[1] = 0;
        state.D[2] = 1;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.Flood));

        state.A[0] = 0x1200;
        state.D[0] = 2;
        state.D[1] = 17;
        state.D[2] = 9;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.InitBitMap));
        Assert.Equal((ushort)4, ReadWord(memory, 0x1200u + (uint)GraphicsLayouts.BitMapBytesPerRow));

        state.A[0] = 0x300;
        memory.TryWriteWord(0x300, 1);
        memory.TryWriteWord(0x302, 1);
        memory.TryWriteWord(0x304, 1);
        memory.TryWriteWord(0x306, 3);
        state.D[0] = 2;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.PolyDraw));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal(1, core.ReadPixel(0x20, 1, 1));
        Assert.Equal(1, core.ReadPixel(0x20, 1, 3));

        state.A[0] = 0x20;
        state.D[0] = 5;
        state.D[1] = 6;
        state.D[2] = 2;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.SetABPenDrMd));
        Assert.Equal(0u, state.D[0]);
        state.D[0] = 0x01;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.SetWriteMask));
        Assert.Equal(0u, state.D[0]);

        state.D[0] = 7;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.SetOutlinePen));
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.GetOutlinePen));
        Assert.Equal(7u, state.D[0]);
        
        state.A[0] = 0x20;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.GetAPen));
        Assert.Equal(5u, state.D[0]);

        state.A[1] = 0x180;
        state.Cycles = 37;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.LoadView));
        Assert.Equal(0x180u, display.PublishedView);
        Assert.Equal(0u, state.D[0]);

        state.A[1] = 0x00FF_0000;
        state.D[0] = 0xDEAD_BEEFu;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.LoadView));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal(0x180u, display.PublishedView);

        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.WaitTOF));
        Assert.True(display.WaitedForTopOfFrame);
        Assert.Equal(37, state.Cycles);
        state.A[0] = 0x1A0;
        memory.TryWriteLong(0x1A0u + (uint)GraphicsLayouts.ViewPortRasInfo, 0x500);
        memory.TryWriteLong(0x500u + (uint)GraphicsLayouts.RasInfoNext, 0);
        memory.TryWriteLong(0x500u + (uint)GraphicsLayouts.RasInfoBitMap, 0);
        memory.TryWriteWord(0x500u + (uint)GraphicsLayouts.RasInfoRxOffset, 0);
        memory.TryWriteWord(0x500u + (uint)GraphicsLayouts.RasInfoRyOffset, 0);
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.WaitBOVP));
        Assert.Equal(0x1A0u, display.WaitedForViewPort);
        Assert.Equal(37, state.Cycles);

        state.D[0] = 17;
        state.D[1] = 3;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AllocRaster));
        Assert.Equal(0x1000u, state.D[0]);
        state.A[0] = state.D[0];
        state.D[0] = 17;
        state.D[1] = 3;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.FreeRaster));
        Assert.Equal(0u, state.D[0]);
    }

    [Fact]
    public void HostRegisterAdapterLeavesLayeredRastPortDrawingToTheLayerProvider()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint rastPort = 0x20;

        Assert.True(memory.TryWriteLong(
            rastPort + (uint)GraphicsLayouts.RastPortLayer,
            0x180));

        var state = new M68kCpuState
        {
            A =
            {
                [0] = 0x300,
                [1] = rastPort,
                [2] = 0x400
            },
            D =
            {
                [0] = 1,
                [1] = 1,
                [2] = 4,
                [3] = 4,
                [4] = 7,
                [5] = 7
            }
        };

        // Layer clipping, damage tracking, and backing-store selection are
        // owned by layers.library (or a future host/native provider).  The
        // portable planar path must not claim these vectors and draw into the
        // backing bitmap as if the RastPort were a plain one.
        var layeredVectors = new[]
        {
            GraphicsLvo.ClipBlit,
            GraphicsLvo.BltBitMapRastPort,
            GraphicsLvo.BltMaskBitMapRastPort,
            GraphicsLvo.BltPattern,
            GraphicsLvo.BltTemplate,
            GraphicsLvo.Draw,
            GraphicsLvo.AreaMove,
            GraphicsLvo.AreaDraw,
            GraphicsLvo.AreaEllipse,
            GraphicsLvo.AreaEnd,
            GraphicsLvo.ReadPixel,
            GraphicsLvo.WritePixel,
            GraphicsLvo.RectFill,
            GraphicsLvo.DrawEllipse,
            GraphicsLvo.ScrollRaster,
            GraphicsLvo.ScrollRasterBF,
            GraphicsLvo.Flood,
            GraphicsLvo.SetRast,
            GraphicsLvo.PolyDraw,
            GraphicsLvo.ClearEOL,
            GraphicsLvo.ClearScreen,
            GraphicsLvo.EraseRect,
            GraphicsLvo.Text
        };

        foreach (var vector in layeredVectors)
        {
            state.D[0] = 1;
            Assert.False(
                adapter.TryInvoke(state, (int)vector),
                $"Layered RastPort vector {vector} must remain with the layer provider.");
        }
    }

    [Fact]
    public void InvalidInitVPortDoesNotInvokeTheHostDisplayProjection()
    {
        var hostMemory = new HostGuestMemory(new AmigaBus());
        var projectionCalls = 0;
        var services = CreateHostGraphicsServices(
            hostMemory,
            initializeCompatibilityViewPort: _ => projectionCalls++);

        var state = new M68kCpuState { A = { [0] = 0x00FF_0000 } };
        services.Invoke(state, (int)GraphicsLvo.InitVPort);
        Assert.Equal(0, projectionCalls);

        state.A[0] = 0x200;
        services.Invoke(state, (int)GraphicsLvo.InitVPort);
        Assert.Equal(1, projectionCalls);
    }

    [Fact]
    public void HostRegisterAdapterRoutesAreaVectorsOnlyForNonLayeredRastPorts()
    {
        var memory = new FakeMemory(0x1000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint rastPort = 0x20;
        const uint areaInfo = 0x100;
        const uint buffer = 0x200;

        Assert.True(core.InitializeRastPort(rastPort));
        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortAreaInfo, areaInfo));
        var state = new M68kCpuState();
        state.A[0] = areaInfo;
        state.A[1] = buffer;
        state.D[0] = 3;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.InitArea));

        state.A[1] = rastPort;
        state.D[0] = 1;
        state.D[1] = 2;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AreaMove));
        Assert.Equal(0u, state.D[0]);
        state.D[0] = 4;
        state.D[1] = 5;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AreaDraw));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal((ushort)2, ReadWord(memory, areaInfo + (uint)GraphicsLayouts.AreaInfoCount));

        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortLayer, 0x1234));
        Assert.False(adapter.TryInvoke(state, (int)GraphicsLvo.AreaDraw));
    }

    [Fact]
    public void HostRegisterAdapterRoutesAreaEllipseAndFallsBackForLayeredPorts()
    {
        var memory = new FakeMemory(0x1000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint rastPort = 0x20;
        const uint areaInfo = 0x100;
        const uint buffer = 0x200;

        Assert.True(core.InitializeRastPort(rastPort));
        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortAreaInfo, areaInfo));
        var state = new M68kCpuState();
        state.A[0] = areaInfo;
        state.A[1] = buffer;
        state.D[0] = 2;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.InitArea));

        state.A[1] = rastPort;
        state.D[0] = 8;
        state.D[1] = 9;
        state.D[2] = 3;
        state.D[3] = 4;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AreaEllipse));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal((ushort)2, ReadWord(memory, areaInfo + (uint)GraphicsLayouts.AreaInfoCount));

        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortLayer, 0x1234));
        Assert.False(adapter.TryInvoke(state, (int)GraphicsLvo.AreaEllipse));
    }

    [Fact]
    public void HostRegisterAdapterRoutesAreaEndAndFallsBackForLayeredPorts()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint rastPort = 0x20;
        const uint areaInfo = 0x300;
        const uint buffer = 0x700;

        Assert.True(core.InitializeRastPort(rastPort));
        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortBitMap, 0x100));
        Assert.Equal(0, core.SetAPen(rastPort, 3));
        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortAreaInfo, areaInfo));
        var state = new M68kCpuState();
        state.A[0] = areaInfo;
        state.A[1] = buffer;
        state.D[0] = 4;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.InitArea));

        state.A[1] = rastPort;
        state.D[0] = 1;
        state.D[1] = 1;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AreaMove));
        state.D[0] = 6;
        state.D[1] = 1;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AreaDraw));
        state.D[0] = 6;
        state.D[1] = 6;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AreaDraw));
        state.D[0] = 1;
        state.D[1] = 6;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AreaDraw));

        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AreaEnd));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal(3, core.ReadPixel(rastPort, 3, 3));

        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortLayer, 0x1234));
        Assert.False(adapter.TryInvoke(state, (int)GraphicsLvo.AreaEnd));
    }

    [Fact]
    public void HostRegisterAdapterRoutesFontLifecycleVectors()
    {
        var memory = new FakeMemory(0x4000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint rastPort = 0x20;
        const uint font = 0x600;
        const uint name = 0x500;
        const uint textAttr = 0x400;
        const uint askedAttr = 0x420;
        WriteFontHeader(memory, font, name, ySize: 9, style: 0, flags: 1);
        WriteAscii(memory, name, "topaz.font");
        memory.TryWriteLong(textAttr + (uint)GraphicsLayouts.TextAttrName, name);
        memory.TryWriteWord(textAttr + (uint)GraphicsLayouts.TextAttrYSize, 9);
        memory.TryWriteByte(textAttr + (uint)GraphicsLayouts.TextAttrStyle, 0);
        memory.TryWriteByte(textAttr + (uint)GraphicsLayouts.TextAttrFlags, 1);
        memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortFont, font);

        var state = new M68kCpuState();
        state.A[1] = font;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AddFont));
        state.A[0] = textAttr;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.OpenFont));
        Assert.Equal(font, state.D[0]);
        Assert.Equal((ushort)1, ReadWord(memory, font + (uint)GraphicsLayouts.TextFontAccessors));

        state.A[1] = font;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.CloseFont));
        Assert.Equal((ushort)0, ReadWord(memory, font + (uint)GraphicsLayouts.TextFontAccessors));
        state.A[1] = rastPort;
        state.A[0] = askedAttr;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AskFont));
        Assert.Equal(name, ReadLong(memory, askedAttr + (uint)GraphicsLayouts.TextAttrName));

        state.A[1] = font;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.RemFont));
        state.A[0] = textAttr;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.OpenFont));
        Assert.Equal(0u, state.D[0]);

        WriteAscii(memory, name, "topaz.font");
        memory.TryWriteByte(font + (uint)GraphicsLayouts.TextFontFlags, 0x81);
        state.A[0] = textAttr;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.OpenFont));
        Assert.Equal(0u, state.D[0]);
    }

    [Fact]
    public void HostRegisterAdapterUsesTopazCompatibilityFontOnlyForTopazRequests()
    {
        var memory = new FakeMemory(0x4000);
        var core = CreateCore(memory);
        const uint font = 0x600;
        const uint name = 0x500;
        const uint textAttr = 0x400;
        WriteFontHeader(memory, font, name, ySize: 8, style: 0, flags: 1);
        WriteAscii(memory, name, "topaz.font");
        memory.TryWriteLong(textAttr + (uint)GraphicsLayouts.TextAttrName, name);
        memory.TryWriteWord(textAttr + (uint)GraphicsLayouts.TextAttrYSize, 8);
        memory.TryWriteByte(textAttr + (uint)GraphicsLayouts.TextAttrStyle, 0);
        memory.TryWriteByte(textAttr + (uint)GraphicsLayouts.TextAttrFlags, 1);

        var adapter = new CopperStartGraphicsRegisterAdapter(core, () => font);
        var state = new M68kCpuState();
        state.A[0] = textAttr;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.OpenFont));
        Assert.Equal(font, state.D[0]);

        WriteAscii(memory, name, "other.font");
        state.A[0] = textAttr;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.OpenFont));
        Assert.Equal(0u, state.D[0]);
    }

    [Fact]
    public void HostRegisterAdapterClaimsTextVectorsForDecodedGuestFonts()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint font = 0x600;
        const uint charData = 0x700;
        const uint charLoc = 0x800;
        const uint text = 0x900;
        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontYSize, 1);
        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontXSize, 1);
        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontBaseline, 1);
        memory.TryWriteByte(font + (uint)GraphicsLayouts.TextFontLoChar, (byte)'A');
        memory.TryWriteByte(font + (uint)GraphicsLayouts.TextFontHiChar, (byte)'A');
        memory.TryWriteLong(font + (uint)GraphicsLayouts.TextFontCharData, charData);
        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontModulo, 1);
        memory.TryWriteLong(font + (uint)GraphicsLayouts.TextFontCharLoc, charLoc);
        memory.TryWriteWord(charLoc, 0);
        memory.TryWriteWord(charLoc + 2, 1);
        memory.TryWriteByte(charData, 0x80);
        memory.TryWriteByte(text, (byte)'A');
        memory.TryWriteByte(text + 1, (byte)'A');

        var state = new M68kCpuState { A = { [0] = font, [1] = 0x20 }, D = { [0] = 1 } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.SetFont));
        Assert.Equal(0u, state.D[0]);

        state.A[0] = 0;
        state.D[0] = 0;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.AskSoftStyle));
        Assert.Equal(uint.MaxValue, state.D[0]);
        state.D[0] = 4;
        state.D[1] = 0xFF;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.SetSoftStyle));
        Assert.Equal(4u, state.D[0]);

        state.A[0] = text;
        state.D[0] = 1;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.TextLength));
        Assert.Equal(1u, state.D[0]);

        state.D[0] = 1;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.Text));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal((ushort)1, ReadWord(memory, 0x20u + (uint)GraphicsLayouts.RastPortCurrentX));

        // Text's documented D0 input is a WORD; high bits must not turn a
        // one-character draw into an unbounded guest-memory walk.
        state.D[0] = 0x0001_0001;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.Text));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal((ushort)2, ReadWord(memory, 0x20u + (uint)GraphicsLayouts.RastPortCurrentX));

        state.A[0] = text;
        state.A[2] = 0xA00;
        state.D[0] = 1;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.TextExtent));
        Assert.Equal((ushort)1, ReadWord(memory, 0xA00u + (uint)GraphicsLayouts.TextExtentWidth));

        // A zero-length metric query has no string bytes to dereference;
        // retain the core's empty-string result at the 68k gateway while
        // non-empty calls still reject a null guest pointer.
        state.A[0] = 0;
        state.D[0] = 0;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.TextExtent));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal((ushort)0, ReadWord(memory, 0xA00u + (uint)GraphicsLayouts.TextExtentWidth));

        state.A[0] = text;
        state.D[1] = 1;
        state.D[2] = 1;
        state.D[3] = 1;
        state.D[0] = 1;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.TextFit));
        Assert.Equal(1u, state.D[0]);

        state.A[0] = 0;
        state.A[3] = 0;
        state.D[0] = 0;
        state.D[1] = 1;
        state.D[2] = 0;
        state.D[3] = 0;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.TextFit));
        Assert.Equal(0u, state.D[0]);
        Assert.Equal((ushort)0, ReadWord(memory, 0xA00u + (uint)GraphicsLayouts.TextExtentWidth));

        state.A[0] = text + 1;
        state.D[0] = 2;
        state.D[1] = unchecked((uint)-1);
        state.D[2] = 2;
        state.D[3] = 1;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.TextFit));
        Assert.Equal(2u, state.D[0]);
        Assert.Equal((ushort)2, ReadWord(memory, 0xA00u + (uint)GraphicsLayouts.TextExtentWidth));

        state.A[0] = font;
        state.A[1] = 0xA00;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.FontExtent));
        Assert.Equal((ushort)1, ReadWord(memory, 0xA00u + (uint)GraphicsLayouts.TextExtentWidth));
    }

    [Fact]
    public void HostRegisterAdapterUsesDecodedFontForProportionalTextLength()
    {
        var memory = new FakeMemory(0x2400);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint rastPort = 0x20;
        const uint font = 0x600;
        const uint charLoc = 0x800;
        const uint charSpace = 0x900;
        const uint charKern = 0xA00;
        const uint text = 0xB00;

        memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortFont, font);
        memory.TryWriteWord(rastPort + (uint)GraphicsLayouts.RastPortTextWidth, 8);
        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontYSize, 8);
        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontXSize, 8);
        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontBaseline, 7);
        memory.TryWriteByte(font + (uint)GraphicsLayouts.TextFontLoChar, (byte)'A');
        memory.TryWriteByte(font + (uint)GraphicsLayouts.TextFontHiChar, (byte)'A');
        memory.TryWriteLong(font + (uint)GraphicsLayouts.TextFontCharData, 0xC00);
        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontModulo, 1);
        memory.TryWriteLong(font + (uint)GraphicsLayouts.TextFontCharLoc, charLoc);
        memory.TryWriteLong(font + (uint)GraphicsLayouts.TextFontCharSpace, charSpace);
        memory.TryWriteLong(font + (uint)GraphicsLayouts.TextFontCharKern, charKern);
        memory.TryWriteWord(charLoc, 0);
        memory.TryWriteWord(charLoc + 2, 5);
        memory.TryWriteWord(charSpace, 3);
        memory.TryWriteWord(charKern, 0);
        memory.TryWriteByte(text, (byte)'A');

        var state = new M68kCpuState { A = { [0] = text, [1] = rastPort }, D = { [0] = 1 } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.TextLength));
        Assert.Equal(3u, state.D[0]);
    }

    [Fact]
    public void RastPortAttributeTagListsUpdateAndQueryGuestState()
    {
        var memory = new FakeMemory(0x3000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint rastPort = 0x20;
        const uint font = 0x400;
        const uint fontName = 0x300;
        const uint bitMap = 0x700;
        const uint tags = 0x900;
        const uint moreTags = 0xA00;
        const uint queryTags = 0xB00;
        const uint resultBase = 0xC00;
        const uint bounds = 0xD00;

        Assert.True(GraphicsRasterOperations.InitializeRastPort(memory, rastPort));
        WriteFontHeader(memory, font, fontName, ySize: 8, style: 0, flags: 1);
        memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortBitMap, bitMap);
        memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 2);
        memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapRows, 3);
        memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, 1);

        WriteTag(memory, tags, GraphicsRastPortAttributeOperations.RptagFont, font);
        WriteTag(memory, tags + 8, GraphicsRastPortAttributeOperations.RptagAPen, 5);
        WriteTag(memory, tags + 16, GraphicsRastPortAttributeOperations.RptagBPen, 6);
        WriteTag(memory, tags + 24, GraphicsRastPortAttributeOperations.RptagDrMd, 0);
        WriteTag(memory, tags + 32, GraphicsRastPortAttributeOperations.RptagWriteMask, 3);
        WriteTag(memory, tags + 40, GraphicsRastPortAttributeOperations.TagMore, moreTags);
        WriteTag(memory, moreTags, GraphicsRastPortAttributeOperations.RptagOutlinePen, 7);
        WriteTag(memory, moreTags + 8, GraphicsRastPortAttributeOperations.TagDone, 0);

        var state = new M68kCpuState { A = { [0] = tags, [1] = rastPort } };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.SetRPAttrsA));
        Assert.Equal((byte)5, ReadByte(memory, rastPort + (uint)GraphicsLayouts.RastPortFgPen));
        Assert.Equal((byte)6, ReadByte(memory, rastPort + (uint)GraphicsLayouts.RastPortBgPen));
        Assert.Equal((byte)3, ReadByte(memory, rastPort + (uint)GraphicsLayouts.RastPortMask));
        Assert.Equal((byte)7, ReadByte(memory, rastPort + (uint)GraphicsLayouts.RastPortOutlinePen));
        Assert.Equal(font, ReadLong(memory, rastPort + (uint)GraphicsLayouts.RastPortFont));

        WriteTag(memory, queryTags, GraphicsRastPortAttributeOperations.RptagFont, resultBase);
        WriteTag(memory, queryTags + 8, GraphicsRastPortAttributeOperations.RptagAPen, resultBase + 4);
        WriteTag(memory, queryTags + 16, GraphicsRastPortAttributeOperations.RptagWriteMask, resultBase + 8);
        WriteTag(memory, queryTags + 24, GraphicsRastPortAttributeOperations.RptagDrawBounds, bounds);
        WriteTag(memory, queryTags + 32, GraphicsRastPortAttributeOperations.TagDone, 0);
        state.A[0] = queryTags;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.GetRPAttrsA));
        Assert.Equal(font, ReadLong(memory, resultBase));
        Assert.Equal(5u, ReadLong(memory, resultBase + 4));
        Assert.Equal(3u, ReadLong(memory, resultBase + 8));
        Assert.Equal((short)0, ReadSignedWord(memory, bounds + (uint)GraphicsLayouts.RectangleMinX));
        Assert.Equal((short)15, ReadSignedWord(memory, bounds + (uint)GraphicsLayouts.RectangleMaxX));
        Assert.Equal((short)2, ReadSignedWord(memory, bounds + (uint)GraphicsLayouts.RectangleMaxY));

        memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortLayer, 0x180);
        Assert.False(GraphicsRastPortAttributeOperations.Get(memory, rastPort, queryTags, new FakeFontBackend()));
    }

    [Fact]
    public void SetMaxPenNarrowsPlanarWriteMaskWithoutWideningCallerState()
    {
        var memory = new FakeMemory(0x1000);
        var core = CreateCore(memory);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        const uint rastPort = 0x20;
        const uint result = 0x300;
        const uint tags = 0x340;

        var state = new M68kCpuState
        {
            A = { [0] = rastPort },
            D = { [0] = 3 }
        };
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.SetMaxPen));
        Assert.Equal((byte)3, ReadByte(memory, rastPort + (uint)GraphicsLayouts.RastPortMask));

        WriteTag(memory, tags, GraphicsRastPortAttributeOperations.RptagMaxPen, result);
        WriteTag(memory, tags + 8, GraphicsRastPortAttributeOperations.TagDone, 0);
        state.A[0] = tags;
        state.A[1] = rastPort;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.GetRPAttrsA));
        Assert.Equal(3u, ReadLong(memory, result));

        // A later max-pen request may narrow the mask, but it must not undo a
        // caller's narrower write-mask or broaden it back to all planes.
        state.A[0] = rastPort;
        state.D[0] = 0xFF;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.SetMaxPen));
        Assert.Equal((byte)3, ReadByte(memory, rastPort + (uint)GraphicsLayouts.RastPortMask));

        state.D[0] = 0;
        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.SetMaxPen));
        Assert.Equal((byte)1, ReadByte(memory, rastPort + (uint)GraphicsLayouts.RastPortMask));
    }

    [Fact]
    public void GraphicsServicesDispatchesMigratedVectorsThroughTheHostAdapter()
    {
        using var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot));
        var hostMemory = new HostGuestMemory(machine.Bus);
        var publishedView = 0u;
        var waitedCycle = -1L;
        var waitedViewPort = 0u;
        var waitedBovpCycle = -1L;
        var allocatedBytes = -1;
        var allocatedFlags = 0u;
        var freedAddress = 0u;
        var freedBytes = -1;
        var services = CreateHostGraphicsServices(
            hostMemory,
            waitTof: callbackState =>
            {
                waitedCycle = callbackState.Cycles;
                callbackState.Cycles += 5;
            },
            loadView: callbackState => publishedView = callbackState.A[1],
            beamPosition: _ => 277,
            waitForViewportBottom: (viewPort, cycle) =>
            {
                waitedViewPort = viewPort;
                waitedBovpCycle = cycle;
                return cycle + 7;
            },
            allocateMemory: (bytes, flags) =>
            {
                allocatedBytes = bytes;
                allocatedFlags = flags;
                return 0x1800;
            },
            freeMemory: (address, bytes) =>
            {
                freedAddress = address;
                freedBytes = bytes;
            });
        const uint rastPort = 0x400;

        var state = new M68kCpuState { A = { [1] = rastPort, }, D = { [0] = 7 } };
        services.Invoke(state, (int)GraphicsLvo.SetAPen);
        Assert.Equal(0u, state.D[0]);
        Assert.Equal((byte)7, machine.Bus.ReadByte(rastPort + (uint)GraphicsLayouts.RastPortFgPen));

        services.Invoke(state, (int)GraphicsLvo.InitRastPort);
        Assert.Equal((byte)0xFF, machine.Bus.ReadByte(rastPort + (uint)GraphicsLayouts.RastPortMask));
        Assert.Equal((byte)1, machine.Bus.ReadByte(rastPort + (uint)GraphicsLayouts.RastPortFgPen));

        machine.Bus.WriteWord(rastPort + (uint)GraphicsLayouts.RastPortTextWidth, 8);
        machine.Bus.WriteWord(rastPort + (uint)GraphicsLayouts.RastPortTextSpacing, 1);
        state.D[0] = 3;
        services.Invoke(state, (int)GraphicsLvo.TextLength);
        Assert.Equal(26u, state.D[0]);

        state.A[1] = 0x180;
        state.Cycles = 91;
        services.Invoke(state, (int)GraphicsLvo.LoadView);
        Assert.Equal(0x180u, publishedView);
        Assert.Equal(0u, state.D[0]);

        state.Cycles = 107;
        services.Invoke(state, (int)GraphicsLvo.WaitTOF);
        Assert.Equal(107, waitedCycle);
        Assert.Equal(112, state.Cycles);
        Assert.Equal(0u, state.D[0]);

        services.Invoke(state, (int)GraphicsLvo.VBeamPos);
        Assert.Equal(277u, state.D[0]);

        state.A[0] = 0x222;
        machine.Bus.WriteLong(0x222u + (uint)GraphicsLayouts.ViewPortRasInfo, 0x300);
        machine.Bus.WriteLong(0x300u + (uint)GraphicsLayouts.RasInfoNext, 0);
        machine.Bus.WriteLong(0x300u + (uint)GraphicsLayouts.RasInfoBitMap, 0);
        machine.Bus.WriteWord(0x300u + (uint)GraphicsLayouts.RasInfoRxOffset, 0);
        machine.Bus.WriteWord(0x300u + (uint)GraphicsLayouts.RasInfoRyOffset, 0);
        state.Cycles = 120;
        services.Invoke(state, (int)GraphicsLvo.WaitBOVP);
        Assert.Equal(0x222u, waitedViewPort);
        Assert.Equal(120, waitedBovpCycle);
        Assert.Equal(127, state.Cycles);
        Assert.Equal(0u, state.D[0]);

        state.D[0] = 17;
        state.D[1] = 3;
        services.Invoke(state, (int)GraphicsLvo.AllocRaster);
        Assert.Equal(0x1800u, state.D[0]);
        Assert.Equal(12, allocatedBytes);
        Assert.Equal(0x2u, allocatedFlags); // MEMF_CHIP

        state.A[0] = state.D[0];
        state.D[0] = 17;
        state.D[1] = 3;
        services.Invoke(state, (int)GraphicsLvo.FreeRaster);
        Assert.Equal(0u, state.D[0]);
        Assert.Equal(0x1800u, freedAddress);
        Assert.Equal(12, freedBytes);
    }

    [Fact]
    public void CalcIvgValidatesGuestViewEnvelopesBeforeCallingTheDisplayBackend()
    {
        var memory = new FakeMemory(0x2000);
        var display = new FakeDisplay
        {
            CalcIvgAvailable = true,
            CalcIvgScanLines = 37
        };
        var core = CreateCore(memory, display);
        const uint view = 0x700;
        const uint viewPort = 0x800;
        const uint rasInfo = 0x900;

        Assert.True(core.InitializeView(view));
        Assert.True(core.InitializeViewPort(viewPort));
        Assert.True(core.InitializeRasInfo(rasInfo));
        Assert.True(memory.TryWriteLong(view + (uint)GraphicsLayouts.ViewViewPort, viewPort));
        Assert.True(memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo));
        Assert.True(memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDWidth, 320));
        Assert.True(memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDHeight, 200));

        Assert.True(core.TryCalcIvg(view, viewPort, out var scanLines));
        Assert.Equal((ushort)37, scanLines);
        Assert.Equal(view, display.CalcIvgView);
        Assert.Equal(viewPort, display.CalcIvgViewPort);

        Assert.True(memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, 0x1FFF));
        Assert.False(core.TryCalcIvg(view, viewPort, out scanLines));
        Assert.Equal((ushort)0, scanLines);
        Assert.Equal(1, display.CalcIvgCallCount);
    }

    [Fact]
    public void CalcIvgRegisterVectorLeavesUnclaimedWhenNoNativeBackendIsPresent()
    {
        var memory = new FakeMemory(0x2000);
        var display = new FakeDisplay
        {
            CalcIvgAvailable = true,
            CalcIvgScanLines = 23
        };
        var core = CreateCore(memory, display);
        const uint view = 0x700;
        const uint viewPort = 0x800;
        const uint rasInfo = 0x900;
        InitializeValidViewPort(memory, core, view, viewPort, rasInfo);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        var state = new M68kCpuState
        {
            A = { [0] = view, [1] = viewPort },
            D = { [0] = 0xDEAD_BEEFu }
        };

        Assert.True(adapter.TryInvoke(state, (int)GraphicsLvo.CalcIVG));
        Assert.Equal(23u, state.D[0]);

        display.CalcIvgAvailable = false;
        state.D[0] = 0xCAFE_BABEu;
        Assert.False(adapter.TryInvoke(state, (int)GraphicsLvo.CalcIVG));
        Assert.Equal(0xCAFE_BABEu, state.D[0]);
    }

    [Fact]
    public void GraphicsServicesRoutesCalcIvgToTheExplicitHostCallback()
    {
        using var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot));
        var hostMemory = new HostGuestMemory(machine.Bus);
        const uint view = 0x700;
        const uint viewPort = 0x800;
        const uint rasInfo = 0x900;
        var callbackView = 0u;
        var callbackViewPort = 0u;
        var services = CreateHostGraphicsServices(
            hostMemory,
            calcIvg: (viewAddress, viewPortAddress) =>
            {
                callbackView = viewAddress;
                callbackViewPort = viewPortAddress;
                return 19;
            });

        var state = new M68kCpuState { A = { [1] = view } };
        services.Invoke(state, (int)GraphicsLvo.InitView);
        state.A[0] = viewPort;
        services.Invoke(state, (int)GraphicsLvo.InitVPort);
        machine.Bus.WriteLong(view + (uint)GraphicsLayouts.ViewViewPort, viewPort);
        machine.Bus.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo);
        machine.Bus.WriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
        machine.Bus.WriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, 0);
        machine.Bus.WriteWord(rasInfo + (uint)GraphicsLayouts.RasInfoRxOffset, 0);
        machine.Bus.WriteWord(rasInfo + (uint)GraphicsLayouts.RasInfoRyOffset, 0);
        machine.Bus.WriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDWidth, 320);
        machine.Bus.WriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDHeight, 200);

        state.A[0] = view;
        state.A[1] = viewPort;
        services.Invoke(state, (int)GraphicsLvo.CalcIVG);

        Assert.Equal(19u, state.D[0]);
        Assert.Equal(view, callbackView);
        Assert.Equal(viewPort, callbackViewPort);
    }

    [Fact]
    public void SetChipRevReturnsOnlyTheCapabilitiesEnabledByTheNativeProvider()
    {
        var memory = new FakeMemory(0x2000);
        var display = new FakeDisplay
        {
            ChipRevAvailable = true,
            ChipRevBits = GraphicsChipRevision.SetEcs
        };
        var core = CreateCore(memory, display);

        Assert.True(core.TrySetChipRev(GraphicsChipRevision.SetBest, out var actualBits));
        Assert.Equal(GraphicsChipRevision.SetEcs, actualBits);
        Assert.Equal(GraphicsChipRevision.SetBest, display.ChipRevRequested);
        Assert.Equal(1, display.ChipRevCallCount);

        display.ChipRevAvailable = false;
        Assert.False(core.TrySetChipRev(GraphicsChipRevision.SetAa, out actualBits));
        Assert.Equal(0u, actualBits);
        Assert.Equal(2, display.ChipRevCallCount);
    }

    [Fact]
    public void SetChipRevRegisterVectorPreservesD0WhenNoNativeCapabilityProviderExists()
    {
        var memory = new FakeMemory(0x2000);
        var display = new FakeDisplay { ChipRevAvailable = false };
        var core = CreateCore(memory, display);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        var state = new M68kCpuState { D = { [0] = GraphicsChipRevision.SetEcs } };

        Assert.False(adapter.TryInvoke(state, (int)GraphicsLvo.SetChipRev));
        Assert.Equal(GraphicsChipRevision.SetEcs, state.D[0]);
    }

    [Fact]
    public void GraphicsServicesRoutesSetChipRevToTheExplicitHostCallback()
    {
        using var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot));
        var hostMemory = new HostGuestMemory(machine.Bus);
        var requested = 0u;
        var services = CreateHostGraphicsServices(
            hostMemory,
            setChipRev: value =>
            {
                requested = value;
                return GraphicsChipRevision.SetA;
            });
        var state = new M68kCpuState { D = { [0] = GraphicsChipRevision.SetBest } };

        services.Invoke(state, (int)GraphicsLvo.SetChipRev);

        Assert.Equal(GraphicsChipRevision.SetBest, requested);
        Assert.Equal(GraphicsChipRevision.SetA, state.D[0]);
    }

    [Fact]
    public void GraphicsServicesClaimsSuperBitmapSynchronizationOnlyWhenHostProvidesIt()
    {
        using var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot));
        var hostMemory = new HostGuestMemory(machine.Bus);
        var calls = new List<string>();
        var services = CreateHostGraphicsServices(
            hostMemory,
            syncSBitMap: layer =>
            {
                calls.Add($"sync:{layer:X}");
                return true;
            },
            copySBitMap: layer =>
            {
                calls.Add($"copy:{layer:X}");
                return true;
            });
        var state = new M68kCpuState { A = { [0] = 0x420 } };

        services.Invoke(state, (int)GraphicsLvo.SyncSBitMap);
        Assert.Equal(0u, state.D[0]);
        services.Invoke(state, (int)GraphicsLvo.CopySBitMap);
        Assert.Equal(0u, state.D[0]);
        Assert.Equal(new[] { "sync:420", "copy:420" }, calls);
    }

    [Fact]
    public void PortableContractSignaturesDoNotLeakHostOrCyberGraphicsTypes()
    {
        var assembly = typeof(GraphicsLibraryCore).Assembly;
        var portableTypes = assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("CopperMod.Amiga.CopperStart.Graphics.Portable", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotEmpty(portableTypes);

        foreach (var type in portableTypes)
        {
            foreach (var memberType in GetSignatureTypes(type))
            {
                var name = memberType.FullName ?? memberType.Name;
                Assert.DoesNotContain("M68kCpuState", name, StringComparison.Ordinal);
                Assert.DoesNotContain("HostGuestMemory", name, StringComparison.Ordinal);
                Assert.DoesNotContain("CyberGraphics", name, StringComparison.Ordinal);
                Assert.DoesNotContain("Avalonia", name, StringComparison.Ordinal);
            }
        }
    }

    private static IEnumerable<Type> GetSignatureTypes(Type type)
    {
        yield return type;

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            yield return field.FieldType;

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            yield return property.PropertyType;

        foreach (var constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (var parameter in constructor.GetParameters())
                yield return parameter.ParameterType;
        }

        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
                yield return parameter.ParameterType;
        }
    }

    private static GraphicsLibraryCore CreateCore(FakeMemory memory, IGraphicsDisplayBackend? display = null)
    {
        const uint rastPort = 0x20;
        const uint bitMap = 0x100;
        const uint plane0 = 0x400;
        const uint plane1 = 0x500;
        memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortBitMap, bitMap);
        memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 2);
        memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapRows, 8);
        memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, 2);
        memory.TryWriteLong(bitMap + (uint)GraphicsLayouts.BitMapPlanes, plane0);
        memory.TryWriteLong(bitMap + (uint)GraphicsLayouts.BitMapPlanes + 4, plane1);
        memory.TryWriteByte(rastPort + (uint)GraphicsLayouts.RastPortMask, 0xFF);
        memory.TryWriteByte(rastPort + (uint)GraphicsLayouts.RastPortFgPen, 3);
        memory.TryWriteByte(rastPort + (uint)GraphicsLayouts.RastPortDrawMode, 1);
        memory.TryWriteWord(rastPort + (uint)GraphicsLayouts.RastPortLinePattern, 0xFFFF);
        return new GraphicsLibraryCore(memory, new FakeAllocator(), new FakeBlitter(), display ?? new FakeDisplay());
    }

    private static void InitializeValidViewPort(
        FakeMemory memory,
        GraphicsLibraryCore core,
        uint view,
        uint viewPort,
        uint rasInfo)
    {
        Assert.True(core.InitializeView(view));
        Assert.True(core.InitializeViewPort(viewPort));
        Assert.True(core.InitializeRasInfo(rasInfo));
        Assert.True(memory.TryWriteLong(view + (uint)GraphicsLayouts.ViewViewPort, viewPort));
        Assert.True(memory.TryWriteLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, rasInfo));
        Assert.True(memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDWidth, 320));
        Assert.True(memory.TryWriteWord(viewPort + (uint)GraphicsLayouts.ViewPortDHeight, 200));
    }

    private static GraphicsServices CreateHostGraphicsServices(
        HostGuestMemory memory,
        Action<M68kCpuState>? waitTof = null,
        Action<M68kCpuState>? loadView = null,
        Func<long, ushort>? beamPosition = null,
        Func<uint, long, long>? waitForViewportBottom = null,
        Func<int, uint, uint>? allocateMemory = null,
        Action<uint, int>? freeMemory = null,
        Action<uint>? initializeCompatibilityViewPort = null,
        Action? requestDisplayRebuild = null,
        Func<uint, uint>? getViewPortModeId = null,
        Func<uint, bool>? syncSBitMap = null,
        Func<uint, bool>? copySBitMap = null,
        Func<uint, uint, ushort>? calcIvg = null,
        Func<uint, uint>? setChipRev = null,
        Action<uint, uint, uint, uint, long>? scheduleDoubleBufferMessages = null,
        Action<M68kCpuState>? loadRgb4 = null,
        Action<M68kCpuState>? setRgb4 = null,
        Action<uint, uint>? drawGList = null,
        Action<uint>? doCollision = null,
        Action<uint, uint, uint>? remIBob = null,
        Action<uint>? waitForBeginningOfVerticalBlank = null,
        Action<uint, uint, short, short>? moveSprite = null,
        Action<uint, uint, uint>? changeSprite = null)
    {
        static uint Zero(M68kCpuState state) => 0;
        static void NoOp(M68kCpuState state) { }
        static uint ZeroPair(uint first, uint second) => 0;
        static uint EnsureHost() => 0;
        static void Free(uint address) { }
        static uint ZeroAttr(uint address, uint attribute) => 0;

        var context = new CopperStartGraphicsContext(
            memory,
            waitTof ?? NoOp,
            static (_, _, _) => { },
            static _ => { },
            requestDisplayRebuild ?? (static () => { }),
            initializeCompatibilityViewPort ?? (static _ => { }),
            address => memory.IsMapped(address, GraphicsLayouts.RastPortMinimumSize),
            static () => 0,
            static (_, _) => { },
            Zero,
            Zero,
            Zero,
            Zero,
            Free,
            ZeroAttr,
            ZeroPair,
            Zero,
            Zero,
            loadView ?? NoOp,
            loadRgb4 ?? NoOp,
            setRgb4 ?? NoOp,
            EnsureHost,
            NoOp,
            NoOp,
            NoOp,
            NoOp,
            beamPosition,
            waitForViewportBottom,
            allocateMemory,
            freeMemory,
            getViewPortModeId,
            null,
            null,
            null,
            null,
            null,
            syncSBitMap,
            copySBitMap,
            calcIvg,
            setChipRev,
            scheduleDoubleBufferMessages,
            drawGList,
            doCollision,
            remIBob,
            null,
            null,
            waitForBeginningOfVerticalBlank,
            moveSprite,
            changeSprite);
        return new GraphicsServices(context);
    }

    private static ushort ReadWord(FakeMemory memory, uint address)
    {
        Assert.True(memory.TryReadWord(address, out var value));
        return value;
    }

    private static void WritePlanarBitmap(
        FakeMemory memory,
        uint bitMap,
        uint plane0,
        uint plane1,
        ushort rows,
        byte depth = 2)
    {
        Assert.True(memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 2));
        Assert.True(memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapRows, rows));
        Assert.True(memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, depth));
        Assert.True(memory.TryWriteLong(bitMap + (uint)GraphicsLayouts.BitMapPlanes, plane0));
        if (depth > 1)
            Assert.True(memory.TryWriteLong(bitMap + (uint)GraphicsLayouts.BitMapPlanes + 4, plane1));
    }

    private static void WriteBitScaleArgs(
        FakeMemory memory,
        uint address,
        uint source,
        uint destination,
        ushort srcWidth,
        ushort srcHeight,
        ushort xSrcFactor,
        ushort xDestFactor,
        ushort ySrcFactor,
        ushort yDestFactor)
    {
        Assert.True(memory.TryWriteWord(address + (uint)GraphicsLayouts.BitScaleArgsSrcX, 0));
        Assert.True(memory.TryWriteWord(address + (uint)GraphicsLayouts.BitScaleArgsSrcY, 0));
        Assert.True(memory.TryWriteWord(address + (uint)GraphicsLayouts.BitScaleArgsSrcWidth, srcWidth));
        Assert.True(memory.TryWriteWord(address + (uint)GraphicsLayouts.BitScaleArgsSrcHeight, srcHeight));
        Assert.True(memory.TryWriteWord(address + (uint)GraphicsLayouts.BitScaleArgsXSrcFactor, xSrcFactor));
        Assert.True(memory.TryWriteWord(address + (uint)GraphicsLayouts.BitScaleArgsYSrcFactor, ySrcFactor));
        Assert.True(memory.TryWriteWord(address + (uint)GraphicsLayouts.BitScaleArgsDestX, 0));
        Assert.True(memory.TryWriteWord(address + (uint)GraphicsLayouts.BitScaleArgsDestY, 0));
        Assert.True(memory.TryWriteWord(address + (uint)GraphicsLayouts.BitScaleArgsDestWidth, 0));
        Assert.True(memory.TryWriteWord(address + (uint)GraphicsLayouts.BitScaleArgsDestHeight, 0));
        Assert.True(memory.TryWriteWord(address + (uint)GraphicsLayouts.BitScaleArgsXDestFactor, xDestFactor));
        Assert.True(memory.TryWriteWord(address + (uint)GraphicsLayouts.BitScaleArgsYDestFactor, yDestFactor));
        Assert.True(memory.TryWriteLong(address + (uint)GraphicsLayouts.BitScaleArgsSrcBitMap, source));
        Assert.True(memory.TryWriteLong(address + (uint)GraphicsLayouts.BitScaleArgsDestBitMap, destination));
        Assert.True(memory.TryWriteLong(address + (uint)GraphicsLayouts.BitScaleArgsFlags, 0));
    }

    private static void WriteTag(FakeMemory memory, uint address, uint tag, uint value)
    {
        Assert.True(memory.TryWriteLong(address, tag));
        Assert.True(memory.TryWriteLong(address + 4, value));
    }

    private static void WriteTextAttr(
        FakeMemory memory,
        uint textAttr,
        uint name,
        ushort ySize,
        byte style,
        byte flags)
    {
        Assert.True(memory.TryWriteLong(textAttr + (uint)GraphicsLayouts.TextAttrName, name));
        Assert.True(memory.TryWriteWord(textAttr + (uint)GraphicsLayouts.TextAttrYSize, ySize));
        Assert.True(memory.TryWriteByte(textAttr + (uint)GraphicsLayouts.TextAttrStyle, style));
        Assert.True(memory.TryWriteByte(textAttr + (uint)GraphicsLayouts.TextAttrFlags, flags));
    }

    private static void WriteFontHeader(
        FakeMemory memory,
        uint font,
        uint name,
        ushort ySize,
        byte style,
        byte flags)
    {
        memory.TryWriteLong(font + (uint)GraphicsLayouts.TextFontName, name);
        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontYSize, ySize);
        memory.TryWriteByte(font + (uint)GraphicsLayouts.TextFontStyle, style);
        memory.TryWriteByte(font + (uint)GraphicsLayouts.TextFontFlags, flags);
        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontXSize, 8);
        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontBaseline, 7);
        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontBoldSmear, 1);
        memory.TryWriteByte(font + (uint)GraphicsLayouts.TextFontLoChar, 32);
        memory.TryWriteByte(font + (uint)GraphicsLayouts.TextFontHiChar, 127);
        memory.TryWriteLong(font + (uint)GraphicsLayouts.TextFontCharData, 0x900);
        memory.TryWriteWord(font + (uint)GraphicsLayouts.TextFontModulo, 1);
        memory.TryWriteLong(font + (uint)GraphicsLayouts.TextFontCharLoc, 0xA00);
    }

    private static void WriteAscii(FakeMemory memory, uint address, string value)
    {
        for (var index = 0; index < value.Length; index++)
            Assert.True(memory.TryWriteByte(address + (uint)index, (byte)value[index]));

        Assert.True(memory.TryWriteByte(address + (uint)value.Length, 0));
    }

    private static byte ReadByte(FakeMemory memory, uint address)
    {
        Assert.True(memory.TryReadByte(address, out var value));
        return value;
    }

    private static short ReadSignedWord(FakeMemory memory, uint address)
        => unchecked((short)ReadWord(memory, address));

    private static void WriteRectangle(
        FakeMemory memory,
        uint address,
        short minX,
        short minY,
        short maxX,
        short maxY)
    {
        Assert.True(memory.TryWriteWord(
            address + (uint)GraphicsLayouts.RectangleMinX,
            unchecked((ushort)minX)));
        Assert.True(memory.TryWriteWord(
            address + (uint)GraphicsLayouts.RectangleMinY,
            unchecked((ushort)minY)));
        Assert.True(memory.TryWriteWord(
            address + (uint)GraphicsLayouts.RectangleMaxX,
            unchecked((ushort)maxX)));
        Assert.True(memory.TryWriteWord(
            address + (uint)GraphicsLayouts.RectangleMaxY,
            unchecked((ushort)maxY)));
    }

    private static bool RegionContains(
        FakeMemory memory,
        uint region,
        short x,
        short y)
    {
        var node = ReadLong(memory, region + (uint)GraphicsLayouts.RegionRectangle);
        for (var count = 0; node != 0 && count < 4096; count++)
        {
            var minX = ReadSignedWord(memory, node + (uint)GraphicsLayouts.RegionRectangleBounds);
            var minY = ReadSignedWord(memory, node + (uint)GraphicsLayouts.RegionRectangleBounds + 2);
            var maxX = ReadSignedWord(memory, node + (uint)GraphicsLayouts.RegionRectangleBounds + 4);
            var maxY = ReadSignedWord(memory, node + (uint)GraphicsLayouts.RegionRectangleBounds + 6);
            if (x >= minX && x <= maxX && y >= minY && y <= maxY)
                return true;

            node = ReadLong(memory, node + (uint)GraphicsLayouts.RegionRectangleNext);
        }

        return false;
    }

    private static uint ReadLong(FakeMemory memory, uint address)
    {
        Assert.True(memory.TryReadLong(address, out var value));
        return value;
    }

    private sealed class FakeMemory : IGraphicsMemory
    {
        internal FakeMemory(int size) => Bytes = new byte[size];

        internal byte[] Bytes { get; }

        public bool TryReadByte(uint address, out byte value)
        {
            if (address >= Bytes.Length)
            {
                value = 0;
                return false;
            }

            value = Bytes[(int)address];
            return true;
        }

        public bool TryReadWord(uint address, out ushort value)
        {
            if (address > (uint)(Bytes.Length - 2))
            {
                value = 0;
                return false;
            }

            var offset = (int)address;
            value = (ushort)((Bytes[offset] << 8) | Bytes[offset + 1]);
            return true;
        }

        public bool TryReadLong(uint address, out uint value)
        {
            if (address > (uint)(Bytes.Length - 4))
            {
                value = 0;
                return false;
            }

            var offset = (int)address;
            value = ((uint)Bytes[offset] << 24)
                | ((uint)Bytes[offset + 1] << 16)
                | ((uint)Bytes[offset + 2] << 8)
                | Bytes[offset + 3];
            return true;
        }

        public bool TryWriteByte(uint address, byte value)
        {
            if (address >= Bytes.Length)
                return false;

            Bytes[(int)address] = value;
            return true;
        }

        public bool TryWriteWord(uint address, ushort value)
        {
            if (address > (uint)(Bytes.Length - 2))
                return false;

            var offset = (int)address;
            Bytes[offset] = (byte)(value >> 8);
            Bytes[offset + 1] = (byte)value;
            return true;
        }

        public bool TryWriteLong(uint address, uint value)
        {
            if (address > (uint)(Bytes.Length - 4))
                return false;

            var offset = (int)address;
            Bytes[offset] = (byte)(value >> 24);
            Bytes[offset + 1] = (byte)(value >> 16);
            Bytes[offset + 2] = (byte)(value >> 8);
            Bytes[offset + 3] = (byte)value;
            return true;
        }
    }

    private sealed class FakeAllocator : IGraphicsAllocatorBackend
    {
        internal uint LastAllocatedBytes { get; private set; }
        internal GraphicsMemoryClass LastAllocatedClass { get; private set; }
        internal uint LastFreedAddress { get; private set; }
        internal uint LastFreedBytes { get; private set; }
        internal GraphicsMemoryClass LastFreedClass { get; private set; }
        internal int FreeCount { get; private set; }

        public bool TryAllocate(uint byteCount, GraphicsMemoryClass memoryClass, out uint address)
        {
            LastAllocatedBytes = byteCount;
            LastAllocatedClass = memoryClass;
            address = 0x1000;
            return byteCount != 0;
        }

        public void Free(uint address, uint byteCount, GraphicsMemoryClass memoryClass)
        {
            LastFreedAddress = address;
            LastFreedBytes = byteCount;
            LastFreedClass = memoryClass;
            FreeCount++;
        }
    }

    private sealed class SteppingAllocator : IGraphicsAllocatorBackend
    {
        private uint _next;

        internal SteppingAllocator(uint first) => _next = first;

        internal int FreeCount { get; private set; }

        public bool TryAllocate(uint byteCount, GraphicsMemoryClass memoryClass, out uint address)
        {
            address = _next;
            if (byteCount == 0 || _next > uint.MaxValue - byteCount - 0x10u)
            {
                address = 0;
                return false;
            }

            _next += byteCount + 0x10u;
            return true;
        }

        public void Free(uint address, uint byteCount, GraphicsMemoryClass memoryClass)
            => FreeCount++;
    }

    private sealed class BitmapAllocator : IGraphicsAllocatorBackend
    {
        internal sealed record Allocation(uint Address, uint Bytes, GraphicsMemoryClass MemoryClass);

        private uint _nextAddress = 0x1000;
        private int _allocationCount;

        internal int FailAfterAllocations { get; init; } = int.MaxValue;
        internal List<Allocation> Freed { get; } = new();

        public bool TryAllocate(uint byteCount, GraphicsMemoryClass memoryClass, out uint address)
        {
            address = 0;
            if (byteCount == 0 || _allocationCount >= FailAfterAllocations)
                return false;

            address = _nextAddress;
            _nextAddress += (byteCount + 3u) & ~3u;
            _allocationCount++;
            return true;
        }

        public void Free(uint address, uint byteCount, GraphicsMemoryClass memoryClass)
            => Freed.Add(new Allocation(address, byteCount, memoryClass));
    }

    private sealed class InterleavedFallbackAllocator : IGraphicsAllocatorBackend
    {
        internal sealed record Allocation(uint Address, uint Bytes, GraphicsMemoryClass MemoryClass);

        private readonly uint _rejectedBytes;
        private uint _nextAddress = 0x1000;

        internal InterleavedFallbackAllocator(uint planeBytesToReject)
            => _rejectedBytes = planeBytesToReject;

        internal List<Allocation> Freed { get; } = new();

        public bool TryAllocate(uint byteCount, GraphicsMemoryClass memoryClass, out uint address)
        {
            if (memoryClass == GraphicsMemoryClass.Chip && byteCount == _rejectedBytes)
            {
                address = 0;
                return false;
            }

            address = _nextAddress;
            _nextAddress += (byteCount + 3u) & ~3u;
            return byteCount != 0;
        }

        public void Free(uint address, uint byteCount, GraphicsMemoryClass memoryClass)
            => Freed.Add(new Allocation(address, byteCount, memoryClass));
    }

    [Fact]
    public void RastPortOutlineAndMoveSettersFailAtomicallyOnTruncatedGuestState()
    {
        // The outline pen byte is mapped, but the Flags word at 0x20 is not.
        var outlineMemory = new FakeMemory(0x20);
        var outlineCore = new GraphicsLibraryCore(
            outlineMemory,
            new FakeAllocator(),
            new FakeBlitter(),
            new FakeDisplay());
        Assert.True(outlineMemory.TryWriteByte((uint)GraphicsLayouts.RastPortOutlinePen, 3));
        Assert.Equal(-1, outlineCore.SetOutlinePen(0, 7));
        Assert.Equal((byte)3, ReadByte(outlineMemory, (uint)GraphicsLayouts.RastPortOutlinePen));

        // CurrentX is mapped, while CurrentY is truncated out of the guest
        // range.  A failed Move must not expose only its new X coordinate.
        var moveMemory = new FakeMemory(0x26);
        var moveCore = new GraphicsLibraryCore(
            moveMemory,
            new FakeAllocator(),
            new FakeBlitter(),
            new FakeDisplay());
        Assert.True(moveMemory.TryWriteWord((uint)GraphicsLayouts.RastPortCurrentX, 4));
        Assert.Equal(-1, moveCore.Move(0, 9, 10));
        Assert.Equal((short)4, ReadSignedWord(moveMemory, (uint)GraphicsLayouts.RastPortCurrentX));
    }

    [Fact]
    public void SetFontFailsAtomicallyWhenTheCachedMetricSpanIsTruncated()
    {
        var memory = new FakeMemory(0x40);
        var core = new GraphicsLibraryCore(memory, new FakeAllocator(), new FakeBlitter(), new FakeDisplay());
        var fonts = new FakeFontBackend();

        Assert.True(memory.TryWriteLong((uint)GraphicsLayouts.RastPortFont, 0x1111));
        Assert.True(memory.TryWriteByte((uint)GraphicsLayouts.RastPortAlgoStyle, 0x22));
        Assert.True(memory.TryWriteWord((uint)GraphicsLayouts.RastPortTextHeight, 0x3333));
        Assert.True(memory.TryWriteWord((uint)GraphicsLayouts.RastPortTextWidth, 0x4444));
        Assert.True(memory.TryWriteWord((uint)GraphicsLayouts.RastPortTextBaseline, 0x5555));

        // TxSpacing at 0x40 is outside this guest range.  None of the earlier
        // font/cache fields may change when the complete update is impossible.
        Assert.Equal(-1, core.SetFont(0, 0x300, fonts));
        Assert.Equal(0x1111u, ReadLong(memory, (uint)GraphicsLayouts.RastPortFont));
        Assert.Equal((byte)0x22, ReadByte(memory, (uint)GraphicsLayouts.RastPortAlgoStyle));
        Assert.Equal((ushort)0x3333, ReadWord(memory, (uint)GraphicsLayouts.RastPortTextHeight));
        Assert.Equal((ushort)0x4444, ReadWord(memory, (uint)GraphicsLayouts.RastPortTextWidth));
        Assert.Equal((ushort)0x5555, ReadWord(memory, (uint)GraphicsLayouts.RastPortTextBaseline));
    }

    [Fact]
    public void ZeroWriteMaskMakesPlanarPrimitivesSuccessfulNoOps()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);

        Assert.Equal(0, core.SetRast(0x20, 3));
        Assert.Equal(3, core.ReadPixel(0x20, 0, 0));
        Assert.Equal(0, core.SetWriteMask(0x20, 0));

        Assert.Equal(0, core.WritePixel(0x20, 0, 0));
        Assert.Equal(0, core.RectFill(0x20, 0, 0, 1, 1));
        Assert.Equal(0, core.Move(0x20, 0, 0));
        Assert.Equal(0, core.Draw(0x20, 1, 0));
        Assert.Equal(3, core.ReadPixel(0x20, 0, 0));
    }

    [Fact]
    public void MultiPlanePixelWritePreflightsEverySelectedPlane()
    {
        var memory = new FakeMemory(0x1000);
        var core = new GraphicsLibraryCore(memory, new FakeAllocator(), new FakeBlitter(), new FakeDisplay());
        const uint rastPort = 0x20;
        const uint bitMap = 0x100;
        const uint firstPlane = 0x200;

        Assert.True(memory.TryWriteLong(rastPort + (uint)GraphicsLayouts.RastPortBitMap, bitMap));
        Assert.True(memory.TryWriteByte(rastPort + (uint)GraphicsLayouts.RastPortFgPen, 1));
        Assert.True(memory.TryWriteByte(rastPort + (uint)GraphicsLayouts.RastPortDrawMode, 1));
        Assert.True(memory.TryWriteByte(rastPort + (uint)GraphicsLayouts.RastPortMask, 3));
        Assert.True(memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 2));
        Assert.True(memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapRows, 1));
        Assert.True(memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, 2));
        Assert.True(memory.TryWriteLong(bitMap + (uint)GraphicsLayouts.BitMapPlanes, firstPlane));
        // The second plane pointer is nonzero but outside the guest memory.
        Assert.True(memory.TryWriteLong(bitMap + (uint)GraphicsLayouts.BitMapPlanes + 4, 0x2000));

        Assert.Equal(-1, core.WritePixel(rastPort, 0, 0));
        Assert.Equal((byte)0, ReadByte(memory, firstPlane));
    }

    [Fact]
    public void TextExtentDoesNotPartiallyPublishIntoATruncatedGuestResult()
    {
        var memory = new FakeMemory(0x2000);
        var core = CreateCore(memory);
        var fonts = new FakeFontBackend();
        const uint text = 0x320;
        const uint extent = 0x1FFB;

        Assert.True(memory.TryWriteByte(text, (byte)'A'));
        Assert.Equal(0, core.SetFont(0x20, 0x300, fonts));
        Assert.True(memory.TryWriteWord(extent + (uint)GraphicsLayouts.TextExtentWidth, 0xAAAA));

        // The final TextExtent fields would lie beyond the guest range.
        Assert.Equal(-1, core.TextExtent(0x20, text, 1, extent, fonts));
        Assert.Equal((ushort)0xAAAA, ReadWord(memory, extent + (uint)GraphicsLayouts.TextExtentWidth));
    }

    private sealed class FakeFontBackend : IGraphicsFontBackend
    {
        public bool TryGetMetrics(uint fontAddress, out GraphicsFontMetrics metrics)
        {
            metrics = new GraphicsFontMetrics(3, 3, 3, 0);
            return fontAddress != 0;
        }

        public bool TryGetGlyph(uint fontAddress, byte character, out GraphicsGlyph glyph)
        {
            glyph = new GraphicsGlyph(3, 3, 4, 0xE0A0E00000000000UL);
            return fontAddress != 0 && character == (byte)'A';
        }
    }

    private sealed class TableFontBackend : IGraphicsFontBackend
    {
        private readonly GraphicsFontMetrics _metrics;
        private readonly IReadOnlyDictionary<byte, GraphicsGlyph> _glyphs;

        internal TableFontBackend(
            GraphicsFontMetrics metrics,
            IReadOnlyDictionary<byte, GraphicsGlyph> glyphs)
        {
            _metrics = metrics;
            _glyphs = glyphs;
        }

        public bool TryGetMetrics(uint fontAddress, out GraphicsFontMetrics metrics)
        {
            metrics = _metrics;
            return fontAddress != 0;
        }

        public bool TryGetGlyph(uint fontAddress, byte character, out GraphicsGlyph glyph)
        {
            glyph = default;
            return fontAddress != 0 && _glyphs.TryGetValue(character, out glyph);
        }
    }

    private sealed class FakeBlitter : IGraphicsBlitterBackend, IGraphicsQueuedBlitterBackend
    {
        internal int OwnCount { get; private set; }
        internal int DisownCount { get; private set; }
        internal int WaitCount { get; private set; }
        internal List<(uint Address, bool BeamSynchronized, short BeamSync)> Queued { get; } = new();

        public void Own() => OwnCount++;
        public void Disown() => DisownCount++;
        public void Wait() => WaitCount++;
        public void Submit(uint operationAddress) { }
        public void SubmitQueued(uint operationAddress, bool beamSynchronized, short beamSync)
            => Queued.Add((operationAddress, beamSynchronized, beamSync));
    }

    private sealed class RecordingGelsBackend : IGraphicsGelsBackend
    {
        internal int CollisionCount { get; private set; }
        internal List<(uint RastPort, uint ViewPort)> Drawn { get; } = new();
        internal List<(uint First, uint Second, uint Routine)> Collisions { get; } = new();
        internal List<(uint Sprite, ushort Flags, uint Routine)> Boundaries { get; } = new();

        public void DrawGList(uint rastPort, uint viewPort)
            => Drawn.Add((rastPort, viewPort));

        public void DoCollision(uint rastPort)
            => CollisionCount++;

        public void RemIBob(uint bob, uint rastPort, uint viewPort) { }

        public void DispatchCollision(uint firstVSprite, uint secondVSprite, uint routineAddress)
            => Collisions.Add((firstVSprite, secondVSprite, routineAddress));

        public void DispatchBoundary(uint vSprite, ushort boundaryFlags, uint routineAddress)
            => Boundaries.Add((vSprite, boundaryFlags, routineAddress));
    }

    private sealed class RecordingSpriteBackend : IGraphicsSpriteBackend
    {
        internal List<(uint ViewPort, uint Sprite, short X, short Y)> Moves { get; } = new();
        internal List<(uint ViewPort, uint Sprite, uint Image)> Changes { get; } = new();

        public void MoveSprite(uint viewPort, uint sprite, short x, short y)
            => Moves.Add((viewPort, sprite, x, y));

        public void ChangeSprite(uint viewPort, uint sprite, uint imageData)
            => Changes.Add((viewPort, sprite, imageData));
    }

    private sealed class RecordingLayerBackend : IGraphicsLayerBackend
    {
        internal List<string> Calls { get; } = new();

        public void Lock(uint layerAddress) => Calls.Add($"lock:{layerAddress:X}");
        public bool TryLock(uint layerAddress)
        {
            Calls.Add($"try:{layerAddress:X}");
            return true;
        }

        public void Unlock(uint layerAddress) => Calls.Add($"unlock:{layerAddress:X}");
        public bool SyncSuperBitMap(uint layerAddress)
        {
            Calls.Add($"sync:{layerAddress:X}");
            return true;
        }

        public bool CopySuperBitMap(uint layerAddress)
        {
            Calls.Add($"copy:{layerAddress:X}");
            return true;
        }
    }

    private sealed class FakeDisplayWithoutCopper : IGraphicsDisplayBackend
    {
        public void PublishView(uint viewAddress) { }
        public void WaitForTopOfFrame() { }
        public void WaitForBeginningOfVerticalBlank(uint viewPortAddress) { }
        public ushort GetBeamPosition() => 0;
    }

    private sealed class FakeDisplayWithMakeStatus :
        IGraphicsDisplayBackend,
        IGraphicsCopperBackend,
        IGraphicsCopperBuildStatusBackend
    {
        internal int MakeStatus { get; set; }
        internal int MakeStatusCount { get; private set; }

        public void PublishView(uint viewAddress) { }
        public void WaitForTopOfFrame() { }
        public void WaitForBeginningOfVerticalBlank(uint viewPortAddress) { }
        public ushort GetBeamPosition() => 0;
        public void MakeViewPort(uint viewAddress, uint viewPortAddress) { }
        public void MergeCopperLists(uint viewAddress) { }
        public int MakeViewPortStatus(uint viewAddress, uint viewPortAddress)
        {
            MakeStatusCount++;
            return MakeStatus;
        }
    }

    private sealed class FakeDisplay :
        IGraphicsDisplayBackend,
        IGraphicsPaletteBackend,
        IGraphicsTimedPaletteBackend,
        IGraphicsViewportDisplayBackend,
        IGraphicsViewportBitmapDisplayBackend,
        IGraphicsDoubleBufferCapabilityBackend,
        IGraphicsCopperBackend,
        IGraphicsCopperMergeStatusBackend,
        IGraphicsCopperResourceBackend,
        IGraphicsViewModeBackend,
        IGraphicsCalcIvgBackend,
        IGraphicsChipRevisionBackend,
        IGraphicsDoubleBufferMessageBackend
    {
        internal uint PublishedView { get; private set; }
        internal bool WaitedForTopOfFrame { get; private set; }
        internal uint WaitedForViewPort { get; private set; }
        internal uint ScrolledViewPort { get; private set; }
        internal int ScrollCount { get; private set; }
        internal uint ChangedViewPort { get; private set; }
        internal uint ChangedBitMap { get; private set; }
        internal uint ChangedDBufInfo { get; private set; }
        internal int ChangeCount { get; private set; }
        internal uint MadeView { get; private set; }
        internal uint MadeViewPort { get; private set; }
        internal int MakeCount { get; private set; }
        internal uint MergedView { get; private set; }
        internal int MergeCount { get; private set; }
        internal int MergeStatus { get; set; } = GraphicsCopperOperations.MergeOk;
        internal int StatusMergeCount { get; private set; }
        internal uint FreedCprList { get; private set; }
        internal int FreeCprListCount { get; private set; }
        internal uint FreedViewPortCopLists { get; private set; }
        internal int FreeViewPortCopListsCount { get; private set; }
        internal bool DoubleBufferSupported { get; set; } = true;
        internal uint ModeId { get; set; } = GraphicsModeIds.Invalid;
        internal ushort BeamPosition { get; set; }
        internal uint LoadedViewPort { get; private set; }
        internal short LoadedCount { get; private set; }
        internal long LoadedCycle { get; private set; }
        internal short SetIndex { get; private set; }
        internal long SetCycle { get; private set; }
        internal bool CalcIvgAvailable { get; set; }
        internal ushort CalcIvgScanLines { get; set; }
        internal uint CalcIvgView { get; private set; }
        internal uint CalcIvgViewPort { get; private set; }
        internal int CalcIvgCallCount { get; private set; }
        internal bool ChipRevAvailable { get; set; }
        internal uint ChipRevBits { get; set; }
        internal uint ChipRevRequested { get; private set; }
        internal int ChipRevCallCount { get; private set; }
        internal uint ScheduledViewPort { get; private set; }
        internal uint ScheduledPreviousBitMap { get; private set; }
        internal uint ScheduledBitMap { get; private set; }
        internal uint ScheduledDBufInfo { get; private set; }
        internal long ScheduledCycle { get; private set; }
        internal int DoubleBufferScheduleCount { get; private set; }

        public void PublishView(uint viewAddress) => PublishedView = viewAddress;
        public void WaitForTopOfFrame() => WaitedForTopOfFrame = true;
        public void WaitForBeginningOfVerticalBlank(uint viewPortAddress) => WaitedForViewPort = viewPortAddress;
        public void ScrollViewPort(uint viewPortAddress)
        {
            ScrolledViewPort = viewPortAddress;
            ScrollCount++;
        }
        public void ChangeViewPortBitMap(uint viewPortAddress, uint bitMapAddress, uint dbufInfoAddress)
        {
            ChangedViewPort = viewPortAddress;
            ChangedBitMap = bitMapAddress;
            ChangedDBufInfo = dbufInfoAddress;
            ChangeCount++;
        }
        public bool SupportsDoubleBuffer(uint viewPortAddress) => DoubleBufferSupported;
        public void MakeViewPort(uint viewAddress, uint viewPortAddress)
        {
            MadeView = viewAddress;
            MadeViewPort = viewPortAddress;
            MakeCount++;
        }
        public void MergeCopperLists(uint viewAddress)
        {
            MergedView = viewAddress;
            MergeCount++;
        }
        public int MergeCopperListsStatus(uint viewAddress)
        {
            MergedView = viewAddress;
            StatusMergeCount++;
            return MergeStatus;
        }
        public void FreeCprList(uint cprListAddress)
        {
            FreedCprList = cprListAddress;
            FreeCprListCount++;
        }
        public void FreeVPortCopLists(uint viewPortAddress)
        {
            FreedViewPortCopLists = viewPortAddress;
            FreeViewPortCopListsCount++;
        }
        public bool TryGetModeId(uint viewPortAddress, out uint modeId)
        {
            modeId = ModeId;
            return modeId != GraphicsModeIds.Invalid;
        }
        public bool TryCalcIvg(uint viewAddress, uint viewPortAddress, out ushort scanLines)
        {
            CalcIvgView = viewAddress;
            CalcIvgViewPort = viewPortAddress;
            CalcIvgCallCount++;
            scanLines = CalcIvgScanLines;
            return CalcIvgAvailable;
        }
        public bool TrySetChipRev(uint requestedBits, out uint actualBits)
        {
            ChipRevRequested = requestedBits;
            ChipRevCallCount++;
            actualBits = ChipRevBits;
            return ChipRevAvailable;
        }
        public void ScheduleDoubleBufferMessages(
            uint viewPortAddress,
            uint previousBitMapAddress,
            uint bitMapAddress,
            uint dbufInfoAddress,
            long cycle)
        {
            ScheduledViewPort = viewPortAddress;
            ScheduledPreviousBitMap = previousBitMapAddress;
            ScheduledBitMap = bitMapAddress;
            ScheduledDBufInfo = dbufInfoAddress;
            ScheduledCycle = cycle;
            DoubleBufferScheduleCount++;
        }
        public ushort GetBeamPosition() => BeamPosition;
        public void LoadRgb4(uint viewPortAddress, uint colorsAddress, short count)
        {
            LoadedViewPort = viewPortAddress;
            LoadedCount = count;
        }
        public void LoadRgb4(uint viewPortAddress, uint colorsAddress, short count, long cycle)
        {
            LoadRgb4(viewPortAddress, colorsAddress, count);
            LoadedCycle = cycle;
        }
        public void SetRgb4(uint viewPortAddress, short index, byte red, byte green, byte blue)
            => SetIndex = index;
        public void SetRgb4(uint viewPortAddress, short index, byte red, byte green, byte blue, long cycle)
        {
            SetRgb4(viewPortAddress, index, red, green, blue);
            SetCycle = cycle;
        }
    }
}
