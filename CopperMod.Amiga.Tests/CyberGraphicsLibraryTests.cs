using Copper68k;
using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class CyberGraphicsLibraryTests
{
    private const uint MemoryBase = 0x0001_0000;
    private const uint BitMap = 0x0000_4000;
    private const uint RastPort = 0x0000_5000;

    [Fact]
    public void VectorTableMatchesTheCompleteMorphOsV52Abi()
    {
        Assert.Equal(
            new[]
            {
                -54, -60, -66, -72, -78, -90, -96, -102, -108, -114,
                -120, -126, -132, -144, -150, -156, -162, -168, -174,
                -180, -186, -198, -216, -222, -228, -234, -240, -252, -258
            },
            CyberGraphicsLibrary.ApiVectors.Select(vector => vector.Offset));
        Assert.Equal(52, CyberGraphicsLibrary.ApiVectors[^1].IntroducedVersion);
        Assert.Equal(CyberGraphicsFunction.ScaleMapRastPortAlpha, CyberGraphicsLibrary.ApiVectors[^1].Function);

        var offsets = CyberGraphicsLibrary.AllVectors.Select(vector => vector.Offset).ToHashSet();
        Assert.Contains(CyberGraphicsLibrary.OpenOffset, offsets);
        Assert.Contains(CyberGraphicsLibrary.ReservedOffset, offsets);
        Assert.DoesNotContain(-84, offsets);
        Assert.DoesNotContain(-138, offsets);
        Assert.DoesNotContain(-192, offsets);
        Assert.DoesNotContain(-204, offsets);
        Assert.DoesNotContain(-210, offsets);
        Assert.DoesNotContain(-246, offsets);
    }

    [Fact]
    public void LibraryIsDormantUntilTrapInstallationIsExplicitlyRequested()
    {
        const uint libraryBase = 0x00F1_0200;
        var bus = new AmigaBus();
        var library = new CyberGraphicsLibrary(bus);

        foreach (var vector in CyberGraphicsLibrary.AllVectors)
        {
            Assert.False(bus.HasHostTrapStub(AddOffset(libraryBase, vector.Offset)));
        }

        library.InstallTrapVectors(libraryBase);

        foreach (var vector in CyberGraphicsLibrary.AllVectors)
        {
            Assert.True(bus.HasHostTrapStub(AddOffset(libraryBase, vector.Offset)));
        }

        Assert.False(bus.HasHostTrapStub(AddOffset(libraryBase, -84)));
        Assert.Equal(libraryBase, library.LibraryBase);
    }

    [Fact]
    public void EveryPublishedVectorDispatchesThroughTheRegisterAbi()
    {
        var bus = CreateBus();
        var library = new CyberGraphicsLibrary(bus);
        var observed = new List<CyberGraphicsFunction>();
        library.CallObserved = observed.Add;

        foreach (var vector in CyberGraphicsLibrary.AllVectors)
        {
            Assert.True(library.Invoke(vector.Offset, new M68kCpuState()));
        }

        Assert.Equal(CyberGraphicsLibrary.AllVectors.Select(vector => vector.Function), observed);
        Assert.False(library.Invoke(-84, new M68kCpuState()));
    }

    [Fact]
    public void ModeSelectionHonorsBestModeRequesterAndColorModelTags()
    {
        var bus = CreateBus();
        var library = new CyberGraphicsLibrary(bus);
        library.RegisterMode(new CyberGraphicsMode(0x5001, 640, 480, 8, CyberGraphicsPixelFormat.Lut8, "640x480", 1, "Board A"));
        library.RegisterMode(new CyberGraphicsMode(0x5002, 800, 600, 16, CyberGraphicsPixelFormat.Rgb16, "800x600", 1, "Board A"));
        library.RegisterMode(new CyberGraphicsMode(0x5003, 1024, 768, 16, CyberGraphicsPixelFormat.Rgb16Pc, "1024x768", 2, "Board B"));

        var best = new M68kCpuState();
        Assert.True(library.Invoke(-60, best));
        Assert.Equal(0x5001u, best.D[0]);

        const uint boardName = MemoryBase + 0x100;
        WriteAscii(bus, boardName, "Board B");
        const uint bestTags = MemoryBase + 0x200;
        WriteTags(
            bus,
            bestTags,
            (CyberGraphicsLibrary.BestModeDepth, 16),
            (CyberGraphicsLibrary.BestModeNominalWidth, 1024),
            (CyberGraphicsLibrary.BestModeNominalHeight, 768),
            (CyberGraphicsLibrary.BestModeBoardName, boardName));
        best.A[0] = bestTags;
        Assert.True(library.Invoke(-60, best));
        Assert.Equal(0x5003u, best.D[0]);

        const uint colorModels = MemoryBase + 0x300;
        bus.WriteWord(colorModels, (ushort)CyberGraphicsPixelFormat.Rgb16Pc);
        bus.WriteWord(colorModels + 2, ushort.MaxValue);
        const uint requestTags = MemoryBase + 0x320;
        WriteTags(
            bus,
            requestTags,
            (CyberGraphicsLibrary.ModeRequestMinWidth, 900),
            (CyberGraphicsLibrary.ModeRequestColorModelArray, colorModels));
        var request = new M68kCpuState();
        request.A[1] = requestTags;
        Assert.True(library.Invoke(-66, request));
        Assert.Equal(0x5003u, request.D[0]);

        WriteTags(bus, requestTags, (CyberGraphicsLibrary.ModeRequestMinWidth, 2000));
        Assert.True(library.Invoke(-66, request));
        Assert.Equal(0u, request.D[0]);
    }

    [Fact]
    public void AllocatedModeListUsesPackedExecAndCyberModeNodeLayout()
    {
        var bus = CreateBus();
        var services = new GuestServices(MemoryBase + 0x2000);
        var library = new CyberGraphicsLibrary(bus, services);
        library.RegisterMode(new CyberGraphicsMode(0x6001, 640, 480, 8, CyberGraphicsPixelFormat.Lut8, "Copper LUT8"));
        library.RegisterMode(new CyberGraphicsMode(0x6002, 1280, 720, 32, CyberGraphicsPixelFormat.Argb32, "Copper ARGB"));
        library.RegisterMode(new CyberGraphicsMode(0x6003, 2560, 1440, 32, CyberGraphicsPixelFormat.Argb32, "Filtered by V52 defaults"));

        var state = new M68kCpuState();
        Assert.True(library.Invoke(-72, state));
        var list = state.D[0];
        Assert.NotEqual(0u, list);

        var first = bus.ReadLong(list);
        var second = bus.ReadLong(first);
        Assert.Equal(list + 14, first);
        Assert.Equal(0x6001u, bus.ReadLong(first + 46));
        Assert.Equal((ushort)640, bus.ReadWord(first + 50));
        Assert.Equal(0x6002u, bus.ReadLong(second + 46));
        Assert.Equal(list + 4, bus.ReadLong(second));

        state.A[0] = list;
        Assert.True(library.Invoke(-78, state));
        Assert.Contains(services.Freed, allocation => allocation.Address == list && allocation.ByteCount == 134);
    }

    [Fact]
    public void MapAttributesAndBitmapLockExposeGuestBackedSurfaceLayout()
    {
        var bus = CreateBus();
        var library = new CyberGraphicsLibrary(bus);
        var surface = new CyberGraphicsSurface(4, 3, CyberGraphicsPixelFormat.Rgb24, 16, MemoryBase + 0x4000);
        library.RegisterBitMap(BitMap, surface);

        var state = new M68kCpuState();
        state.A[0] = BitMap;
        state.D[0] = CyberGraphicsLibrary.CyberMapAttrIsCyberGfx;
        Assert.True(library.Invoke(-96, state));
        Assert.Equal(uint.MaxValue, state.D[0]);
        state.D[0] = CyberGraphicsLibrary.CyberMapAttrXMod;
        Assert.True(library.Invoke(-96, state));
        Assert.Equal(16u, state.D[0]);

        const uint widthResult = MemoryBase + 0x5000;
        const uint formatResult = widthResult + 4;
        const uint baseResult = widthResult + 8;
        const uint lockTags = MemoryBase + 0x5100;
        WriteTags(
            bus,
            lockTags,
            (CyberGraphicsLibrary.LockWidth, widthResult),
            (CyberGraphicsLibrary.LockPixelFormat, formatResult),
            (CyberGraphicsLibrary.LockBaseAddress, baseResult));
        state.A[1] = lockTags;
        Assert.True(library.Invoke(-168, state));
        Assert.NotEqual(0u, state.D[0]);
        Assert.Equal(4u, bus.ReadLong(widthResult));
        Assert.Equal((uint)CyberGraphicsPixelFormat.Rgb24, bus.ReadLong(formatResult));
        Assert.Equal(MemoryBase + 0x4000, bus.ReadLong(baseResult));

        state.A[0] = state.D[0];
        Assert.True(library.Invoke(-174, state));
    }

    [Fact]
    public void PixelArrayConversionAndAlphaBlendingUseCgXRegisterAssignments()
    {
        var bus = CreateBus();
        var library = new CyberGraphicsLibrary(bus);
        var surface = new CyberGraphicsSurface(2, 1, CyberGraphicsPixelFormat.Rgb24);
        library.RegisterRastPort(RastPort, surface);
        const uint pixels = MemoryBase + 0x6000;
        bus.WriteByte(pixels, 0x10, 0);
        bus.WriteByte(pixels + 1, 0x20, 0);
        bus.WriteByte(pixels + 2, 0x30, 0);
        bus.WriteByte(pixels + 3, 0x40, 0);
        bus.WriteByte(pixels + 4, 0x50, 0);
        bus.WriteByte(pixels + 5, 0x60, 0);

        var state = PixelArrayState(pixels, RastPort, 6, 2, 1);
        state.D[7] = (uint)CyberGraphicsRectangleFormat.Rgb;
        Assert.True(library.Invoke(-126, state));
        Assert.Equal(2u, state.D[0]);
        Assert.Equal(0x0010_2030u, ReadRgb(library, RastPort, 0, 0));
        Assert.Equal(0x0040_5060u, ReadRgb(library, RastPort, 1, 0));

        const uint alphaPixel = MemoryBase + 0x6100;
        bus.WriteByte(alphaPixel, 0x80, 0);
        bus.WriteByte(alphaPixel + 1, 0xFF, 0);
        bus.WriteByte(alphaPixel + 2, 0x00, 0);
        bus.WriteByte(alphaPixel + 3, 0x00, 0);
        state = PixelArrayState(alphaPixel, RastPort, 4, 1, 1);
        state.D[7] = uint.MaxValue;
        Assert.True(library.Invoke(-216, state));
        Assert.Equal(1u, state.D[0]);
        Assert.Equal(0x0088_1018u, ReadRgb(library, RastPort, 0, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void EveryCgXPixelFormatRoundTripsThroughRectFmtRaw(int pixelFormatValue)
    {
        var pixelFormat = (CyberGraphicsPixelFormat)pixelFormatValue;
        var bus = CreateBus();
        var library = new CyberGraphicsLibrary(bus);
        var surface = new CyberGraphicsSurface(1, 1, pixelFormat);
        surface.Palette[42] = 0xFF12_56A3;
        library.RegisterRastPort(RastPort, surface);

        var state = new M68kCpuState();
        state.A[1] = RastPort;
        state.D[2] = 0x7F12_56A3;
        Assert.True(library.Invoke(-114, state));
        var encodedColor = ReadRgb(library, RastPort, 0, 0);

        const uint rawPixel = MemoryBase + 0x6200;
        state = PixelArrayState(rawPixel, RastPort, (ushort)surface.BytesPerPixel, 1, 1);
        state.D[7] = (uint)CyberGraphicsRectangleFormat.Raw;
        Assert.True(library.Invoke(-120, state));
        Assert.Equal(1u, state.D[0]);

        state = new M68kCpuState();
        state.A[1] = RastPort;
        state.D[2] = 0;
        Assert.True(library.Invoke(-114, state));
        state = PixelArrayState(rawPixel, RastPort, (ushort)surface.BytesPerPixel, 1, 1);
        state.D[7] = (uint)CyberGraphicsRectangleFormat.Raw;
        Assert.True(library.Invoke(-126, state));
        Assert.Equal(encodedColor, ReadRgb(library, RastPort, 0, 0));
    }

    [Fact]
    public void FillMoveInvertAndProcessOperateOnTheRegisteredRastPort()
    {
        var bus = CreateBus();
        var library = new CyberGraphicsLibrary(bus);
        var surface = new CyberGraphicsSurface(3, 1, CyberGraphicsPixelFormat.Rgb24);
        library.RegisterRastPort(RastPort, surface);

        var state = new M68kCpuState();
        state.A[1] = RastPort;
        state.D[2] = 2;
        state.D[3] = 1;
        state.D[4] = 0x0010_2030;
        Assert.True(library.Invoke(-150, state));
        Assert.Equal(2u, state.D[0]);

        state = new M68kCpuState();
        state.A[1] = RastPort;
        state.D[2] = 1;
        state.D[4] = 2;
        state.D[5] = 1;
        Assert.True(library.Invoke(-132, state));
        Assert.Equal(0x0010_2030u, ReadRgb(library, RastPort, 2, 0));

        state = new M68kCpuState();
        state.A[1] = RastPort;
        state.D[2] = 1;
        state.D[3] = 1;
        Assert.True(library.Invoke(-144, state));
        Assert.Equal(0x00EF_DFCFu, ReadRgb(library, RastPort, 0, 0));

        state = new M68kCpuState();
        state.A[1] = RastPort;
        state.D[2] = 1;
        state.D[3] = 1;
        state.D[4] = 1;
        state.D[5] = 0x10;
        Assert.True(library.Invoke(-228, state));
        Assert.Equal(0x00DF_CFBFu, ReadRgb(library, RastPort, 0, 0));
    }

    [Fact]
    public void DoCDrawBuildsThePackedMessageAndCallsTheGuestHookEntry()
    {
        var bus = CreateBus();
        var services = new GuestServices(MemoryBase + 0x7000);
        var library = new CyberGraphicsLibrary(bus, services);
        var surface = new CyberGraphicsSurface(8, 6, CyberGraphicsPixelFormat.Argb32, 40, MemoryBase + 0x8000);
        library.RegisterRastPort(RastPort, surface);
        const uint hook = MemoryBase + 0x6800;
        bus.WriteLong(hook + 8, 0x00AB_CDEF);

        var state = new M68kCpuState();
        state.A[0] = hook;
        state.A[1] = RastPort;
        Assert.True(library.Invoke(-156, state));

        Assert.Equal(0x00AB_CDEFu, services.LastHookEntry);
        Assert.Equal(RastPort, services.LastHookObject);
        Assert.Equal(MemoryBase + 0x8000, bus.ReadLong(services.LastHookMessage));
        Assert.Equal(8u, bus.ReadLong(services.LastHookMessage + 12));
        Assert.Equal((ushort)40, bus.ReadWord(services.LastHookMessage + 20));
        Assert.Equal((ushort)CyberGraphicsPixelFormat.Argb32, bus.ReadWord(services.LastHookMessage + 24));
    }

    private static AmigaBus CreateBus()
    {
        var bus = new AmigaBus();
        bus.MapWritableMemory(MemoryBase, new byte[0x0002_0000]);
        return bus;
    }

    private static M68kCpuState PixelArrayState(uint pixels, uint rastPort, ushort stride, ushort width, ushort height)
    {
        var state = new M68kCpuState();
        state.A[0] = pixels;
        state.A[1] = rastPort;
        state.D[2] = stride;
        state.D[5] = width;
        state.D[6] = height;
        return state;
    }

    private static uint ReadRgb(CyberGraphicsLibrary library, uint rastPort, ushort x, ushort y)
    {
        var state = new M68kCpuState();
        state.A[1] = rastPort;
        state.D[0] = x;
        state.D[1] = y;
        Assert.True(library.Invoke(-108, state));
        return state.D[0];
    }

    private static void WriteTags(AmigaBus bus, uint address, params (uint Tag, uint Data)[] tags)
    {
        foreach (var (tag, data) in tags)
        {
            bus.WriteLong(address, tag);
            bus.WriteLong(address + 4, data);
            address += 8;
        }

        bus.WriteLong(address, CyberGraphicsLibrary.TagDone);
        bus.WriteLong(address + 4, 0);
    }

    private static void WriteAscii(AmigaBus bus, uint address, string value)
    {
        foreach (var character in value)
        {
            bus.WriteByte(address++, (byte)character, 0);
        }

        bus.WriteByte(address, 0, 0);
    }

    private static uint AddOffset(uint address, int offset)
        => unchecked((uint)((int)address + offset));

    private sealed class GuestServices : ICyberGraphicsGuestServices
    {
        private uint _nextAddress;

        public GuestServices(uint firstAddress)
        {
            _nextAddress = firstAddress;
        }

        public List<(uint Address, int ByteCount)> Freed { get; } = new();

        public uint LastHookEntry { get; private set; }

        public uint LastHookObject { get; private set; }

        public uint LastHookMessage { get; private set; }

        public uint Allocate(int byteCount)
        {
            var address = _nextAddress;
            _nextAddress += (uint)((byteCount + 3) & ~3);
            return address;
        }

        public void Free(uint address, int byteCount)
            => Freed.Add((address, byteCount));

        public bool InvokeHook(uint entryAddress, uint objectAddress, uint messageAddress)
        {
            LastHookEntry = entryAddress;
            LastHookObject = objectAddress;
            LastHookMessage = messageAddress;
            return true;
        }
    }
}
