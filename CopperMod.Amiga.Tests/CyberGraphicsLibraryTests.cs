using Copper68k;
using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class CyberGraphicsLibraryTests
{
	[Fact]
	public void RtgDeviceRegistersStableModesAndLinearGuestBackedSurfaces()
	{
		var bus = new AmigaBus(rtgVramSize: 256L * 1024 * 1024);
		bus.ConfigureAutoconfigRtgForHost();
		var library = new CyberGraphicsLibrary(bus);
		var surface = Assert.IsType<CyberGraphicsSurface>(
			library.AllocateRtgSurface(640, 480, CyberGraphicsPixelFormat.Lut8));
		const uint bitMap = 0x3000;
		library.RegisterBitMap(bitMap, surface);

		Assert.Equal(0x8000_0000u, surface.GuestBaseAddress);
		Assert.Equal(640, surface.BytesPerRow);
		var state = new M68kCpuState();
		state.A[0] = bitMap;
		state.D[0] = CyberGraphicsLibrary.CyberMapAttrIsLinearMemory;
		Assert.True(library.Invoke(-96, state));
		Assert.Equal(uint.MaxValue, state.D[0]);

		state.D[0] = 0x4350_0011u;
		Assert.True(library.Invoke(-54, state));
		Assert.Equal(1u, state.D[0]);
		state.D[0] = 0x4350_0113u;
		Assert.True(library.Invoke(-54, state));
		Assert.Equal(1u, state.D[0]);
		state.D[0] = 0x4350_0014u;
		Assert.True(library.Invoke(-54, state));
		Assert.Equal(1u, state.D[0]);
		Assert.True(library.TryGetMode(0x4350_0014u, out var rgb15Mode));
		Assert.Equal(15, rgb15Mode.Depth);
		Assert.Equal(CyberGraphicsPixelFormat.Rgb15, rgb15Mode.PixelFormat);
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
	[InlineData(14)]
	[InlineData(15)]
	public void PlanarToRtgLut8ImplementsEveryBcMinterm(int operation)
	{
		var bus = new AmigaBus(rtgVramSize: 16L * 1024 * 1024);
		bus.ConfigureAutoconfigRtgForHost();
		var library = new CyberGraphicsLibrary(bus);
		const uint sourceBitMap = 0x1000;
		const uint sourcePlane = 0x2000;
		const uint destinationBitMap = 0x3000;
		bus.WriteWord(sourceBitMap, 2);
		bus.WriteWord(sourceBitMap + 2, 1);
		bus.WriteByte(sourceBitMap + 5, 1, 0);
		bus.WriteLong(sourceBitMap + 8, sourcePlane);
		bus.WriteByte(sourcePlane, 0x80, 0);
		var surface = Assert.IsType<CyberGraphicsSurface>(
			library.AllocateRtgSurface(1, 1, CyberGraphicsPixelFormat.Lut8));
		library.RegisterBitMap(destinationBitMap, surface);
		bus.WriteByte(surface.GuestBaseAddress, 0x5A, 0);

		Assert.Equal(1, library.BlitPlanarToRtg(
			sourceBitMap, 0, 0, destinationBitMap, 0, 0, 1, 1, (byte)(operation << 4), 0xFF));

		Assert.Equal(ApplyMinterm(1, 0x5A, operation), bus.ReadByte(surface.GuestBaseAddress));
	}

	[Fact]
	public void PlanarToRtgMapsPensToBigEndianRgb16()
	{
		var bus = new AmigaBus(rtgVramSize: 16L * 1024 * 1024);
		bus.ConfigureAutoconfigRtgForHost();
		var library = new CyberGraphicsLibrary(bus);
		const uint sourceBitMap = 0x1000;
		const uint destinationBitMap = 0x3000;
		bus.WriteWord(sourceBitMap, 2);
		bus.WriteWord(sourceBitMap + 2, 1);
		bus.WriteByte(sourceBitMap + 5, 1, 0);
		bus.WriteLong(sourceBitMap + 8, uint.MaxValue);
		var surface = Assert.IsType<CyberGraphicsSurface>(
			library.AllocateRtgSurface(1, 1, CyberGraphicsPixelFormat.Rgb16));
		surface.Palette[1] = 0xFFFF_0000u;
		library.RegisterBitMap(destinationBitMap, surface);

		library.BlitPlanarToRtg(sourceBitMap, 0, 0, destinationBitMap, 0, 0, 1, 1, 0xC0, 0xFF);

		Assert.Equal(0xF800, bus.ReadWord(surface.GuestBaseAddress));
	}

	[Theory]
	[InlineData(1, 0x7C, 0x00)]
	[InlineData(2, 0xF8, 0x00)]
	[InlineData(3, 0x00, 0x7C)]
	[InlineData(4, 0x1F, 0x00)]
	public void PlanarToRtgMapsPensToEveryRgb15MemoryLayout(
		int formatValue,
		byte firstByte,
		byte secondByte)
	{
		var format = (CyberGraphicsPixelFormat)formatValue;
		var bus = new AmigaBus(rtgVramSize: 16L * 1024 * 1024);
		bus.ConfigureAutoconfigRtgForHost();
		var library = new CyberGraphicsLibrary(bus);
		const uint sourceBitMap = 0x1000;
		const uint destinationBitMap = 0x3000;
		bus.WriteWord(sourceBitMap, 2);
		bus.WriteWord(sourceBitMap + 2, 1);
		bus.WriteByte(sourceBitMap + 5, 1, 0);
		bus.WriteLong(sourceBitMap + 8, uint.MaxValue);
		var surface = Assert.IsType<CyberGraphicsSurface>(library.AllocateRtgSurface(1, 1, format));
		surface.Palette[1] = 0xFFFF_0000u;
		library.RegisterBitMap(destinationBitMap, surface);

		Assert.Equal(1, library.BlitPlanarToRtg(
			sourceBitMap, 0, 0, destinationBitMap, 0, 0, 1, 1, 0xC0, 0xFF));

		Assert.Equal(firstByte, bus.ReadByte(surface.GuestBaseAddress));
		Assert.Equal(secondByte, bus.ReadByte(surface.GuestBaseAddress + 1));
	}

	[Theory]
	[InlineData(1, 0x7C, 0x00)]
	[InlineData(2, 0xF8, 0x00)]
	[InlineData(3, 0x00, 0x7C)]
	[InlineData(4, 0x1F, 0x00)]
	public void HostPresentationDecodesEveryRgb15MemoryLayout(
		int formatValue,
		byte firstByte,
		byte secondByte)
	{
		var format = (CyberGraphicsPixelFormat)formatValue;
		var bus = new AmigaBus(rtgVramSize: 16L * 1024 * 1024);
		bus.ConfigureAutoconfigRtgForHost();
		var library = new CyberGraphicsLibrary(bus);
		var surface = Assert.IsType<CyberGraphicsSurface>(library.AllocateRtgSurface(1, 1, format));
		const uint bitMap = 0x3000;
		const uint viewPort = 0x4000;
		library.RegisterBitMap(bitMap, surface);
		Assert.True(library.ChangeViewPortBitMap(viewPort, bitMap));
		library.SelectFrontViewPort(viewPort);
		bus.WriteByte(surface.GuestBaseAddress, firstByte, 0);
		bus.WriteByte(surface.GuestBaseAddress + 1, secondByte, 0);

		Assert.True(library.TryRenderRtgFrame(out var frame));
		Assert.Equal(0xFFFF_0000u, frame.Bgra[0]);
	}

	[Fact]
	public void RtgLut8ToPlanarCopiesPenBitsWithoutPaletteConversion()
	{
		var bus = new AmigaBus(rtgVramSize: 16L * 1024 * 1024);
		bus.ConfigureAutoconfigRtgForHost();
		var library = new CyberGraphicsLibrary(bus);
		var source = Assert.IsType<CyberGraphicsSurface>(
			library.AllocateRtgSurface(1, 1, CyberGraphicsPixelFormat.Lut8));
		library.RegisterBitMap(0x1000, source);
		bus.WriteByte(source.GuestBaseAddress, 5, 0);
		source.Palette[5] = 0xFFFF_0000;
		source.Palette[2] = 0xFFFF_0000;
		WritePlanarBitMap(bus, 0x2000, 3, 0x3000);

		Assert.Equal(0, library.BlitRtgToPlanar(
			0x1000, 0, 0, 0x2000, 0, 0, 1, 1, 0xC0, 0xFF, 0x4000));
		bus.WriteByte(0x4000, 0x80, 0);
		Assert.Equal(1, library.BlitRtgToPlanar(
			0x1000, 0, 0, 0x2000, 0, 0, 1, 1, 0xC0, 0xFF, 0x4000));
		Assert.Equal(0x80, bus.ReadByte(0x3000));
		Assert.Equal(0x00, bus.ReadByte(0x3100));
		Assert.Equal(0x80, bus.ReadByte(0x3200));
	}

	[Fact]
	public void DeepRtgToPlanarUsesItsAssociatedScreenPalette()
	{
		var bus = new AmigaBus(rtgVramSize: 16L * 1024 * 1024);
		bus.ConfigureAutoconfigRtgForHost();
		var library = new CyberGraphicsLibrary(bus);
		var source = Assert.IsType<CyberGraphicsSurface>(
			library.AllocateRtgSurface(1, 1, CyberGraphicsPixelFormat.Argb32));
		library.RegisterBitMap(0x1000, source);
		bus.WriteLong(source.GuestBaseAddress, 0xFF12_3456);
		WritePlanarBitMap(bus, 0x2000, 3, 0x3000);

		Assert.Equal(0, library.BlitRtgToPlanar(
			0x1000, 0, 0, 0x2000, 0, 0, 1, 1, 0xC0, 0xFF));
		source.AssociateColorMap(0x5000);
		source.Palette[7] = 0xFF12_3456;
		Assert.Equal(1, library.BlitRtgToPlanar(
			0x1000, 0, 0, 0x2000, 0, 0, 1, 1, 0xC0, 0xFF));
		Assert.Equal(0x80, bus.ReadByte(0x3000));
		Assert.Equal(0x80, bus.ReadByte(0x3100));
		Assert.Equal(0x80, bus.ReadByte(0x3200));
	}

	[Fact]
	public void RtgLut8ToLut8PreservesIndicesAcrossDifferentPalettes()
	{
		var bus = new AmigaBus(rtgVramSize: 16L * 1024 * 1024);
		bus.ConfigureAutoconfigRtgForHost();
		var library = new CyberGraphicsLibrary(bus);
		var source = Assert.IsType<CyberGraphicsSurface>(
			library.AllocateRtgSurface(1, 1, CyberGraphicsPixelFormat.Lut8));
		var destination = Assert.IsType<CyberGraphicsSurface>(
			library.AllocateRtgSurface(1, 1, CyberGraphicsPixelFormat.Lut8));
		library.RegisterBitMap(0x1000, source);
		library.RegisterBitMap(0x2000, destination);
		source.Palette[7] = 0xFFFF_0000;
		destination.Palette[3] = 0xFFFF_0000;
		bus.WriteByte(source.GuestBaseAddress, 7, 0);

		Assert.Equal(1, library.BlitRtgToRtg(0x1000, 0, 0, 0x2000, 0, 0, 1, 1, 0xC0, 0xFF));
		Assert.Equal(7, bus.ReadByte(destination.GuestBaseAddress));
	}

	[Fact]
	public void ViewPortBufferSwapInheritsAssociatedColorMapAndPalette()
	{
		var bus = new AmigaBus(rtgVramSize: 16L * 1024 * 1024);
		bus.ConfigureAutoconfigRtgForHost();
		var library = new CyberGraphicsLibrary(bus);
		var screen = Assert.IsType<CyberGraphicsSurface>(
			library.AllocateRtgSurface(1, 1, CyberGraphicsPixelFormat.Lut8));
		var backBuffer = Assert.IsType<CyberGraphicsSurface>(
			library.AllocateRtgSurface(1, 1, CyberGraphicsPixelFormat.Lut8));
		screen.AssociateColorMap(0x5000);
		screen.Palette[9] = 0xFF12_3456;
		library.RegisterBitMap(0x1000, screen);
		library.RegisterBitMap(0x2000, backBuffer);
		library.RegisterViewPort(0x3000, screen);

		Assert.True(library.ChangeViewPortBitMap(0x3000, 0x2000));
		Assert.Equal(0x5000u, backBuffer.ColorMapAddress);
		Assert.Same(screen.Palette, backBuffer.Palette);
		Assert.Equal(0xFF12_3456u, backBuffer.Palette[9]);
	}

	[Fact]
	public void CyberGraphicsRastPortCallsFollowCurrentScreenBufferBitMap()
	{
		var bus = new AmigaBus(rtgVramSize: 16L * 1024 * 1024);
		bus.ConfigureAutoconfigRtgForHost();
		var library = new CyberGraphicsLibrary(bus);
		var front = Assert.IsType<CyberGraphicsSurface>(
			library.AllocateRtgSurface(1, 1, CyberGraphicsPixelFormat.Argb32));
		var back = Assert.IsType<CyberGraphicsSurface>(
			library.AllocateRtgSurface(1, 1, CyberGraphicsPixelFormat.Argb32));
		const uint frontBitMap = 0x1000;
		const uint backBitMap = 0x2000;
		const uint rastPort = 0x3000;
		library.RegisterBitMap(frontBitMap, front);
		library.RegisterBitMap(backBitMap, back);
		library.RegisterRastPort(rastPort, front);
		bus.WriteLong(rastPort + 4, frontBitMap);

		var write = new M68kCpuState();
		write.A[1] = rastPort;
		write.D[2] = 0xFF11_2233;
		Assert.True(library.Invoke(-114, write));
		Assert.Equal(0xFF11_2233u, bus.ReadLong(front.GuestBaseAddress));

		bus.WriteLong(rastPort + 4, backBitMap);
		write.D[2] = 0xFFAA_BBCC;
		Assert.True(library.Invoke(-114, write));
		Assert.Equal(0xFF11_2233u, bus.ReadLong(front.GuestBaseAddress));
		Assert.Equal(0xFFAA_BBCCu, bus.ReadLong(back.GuestBaseAddress));
	}

	[Fact]
	public void PlanarToRtgMaskPlaneSuppressesUnselectedPixels()
	{
		var bus = new AmigaBus(rtgVramSize: 16L * 1024 * 1024);
		bus.ConfigureAutoconfigRtgForHost();
		var library = new CyberGraphicsLibrary(bus);
		const uint sourceBitMap = 0x1000;
		const uint sourcePlane = 0x2000;
		const uint maskPlane = 0x2100;
		const uint destinationBitMap = 0x3000;
		bus.WriteWord(sourceBitMap, 2);
		bus.WriteWord(sourceBitMap + 2, 1);
		bus.WriteByte(sourceBitMap + 5, 1, 0);
		bus.WriteLong(sourceBitMap + 8, sourcePlane);
		bus.WriteWord(sourcePlane, 0xC000);
		bus.WriteWord(maskPlane, 0x8000);
		var surface = Assert.IsType<CyberGraphicsSurface>(
			library.AllocateRtgSurface(2, 1, CyberGraphicsPixelFormat.Lut8));
		library.RegisterBitMap(destinationBitMap, surface);
		bus.WriteByte(surface.GuestBaseAddress, 0x55, 0);
		bus.WriteByte(surface.GuestBaseAddress + 1, 0x55, 0);

		Assert.Equal(1, library.BlitPlanarToRtg(
			sourceBitMap, 0, 0, destinationBitMap, 0, 0, 2, 1, 0xC0, 0xFF, maskPlane));
		Assert.Equal(1, bus.ReadByte(surface.GuestBaseAddress));
		Assert.Equal(0x55, bus.ReadByte(surface.GuestBaseAddress + 1));
	}

	[Fact]
	public void RtgToRtgBlitIsOverlapSafeAndHonorsLutWriteMask()
	{
		var bus = new AmigaBus(rtgVramSize: 16L * 1024 * 1024);
		bus.ConfigureAutoconfigRtgForHost();
		var library = new CyberGraphicsLibrary(bus);
		var surface = Assert.IsType<CyberGraphicsSurface>(
			library.AllocateRtgSurface(4, 1, CyberGraphicsPixelFormat.Lut8));
		const uint bitMap = 0x3000;
		library.RegisterBitMap(bitMap, surface);
		bus.WriteByte(surface.GuestBaseAddress + 0, 1, 0);
		bus.WriteByte(surface.GuestBaseAddress + 1, 2, 0);
		bus.WriteByte(surface.GuestBaseAddress + 2, 3, 0);
		bus.WriteByte(surface.GuestBaseAddress + 3, 4, 0);

		Assert.Equal(3, library.BlitRtgToRtg(bitMap, 0, 0, bitMap, 1, 0, 3, 1, 0xC0, 0xFF));
		Assert.Equal(new byte[] { 1, 1, 2, 3 }, Enumerable.Range(0, 4)
			.Select(index => bus.ReadByte(surface.GuestBaseAddress + (uint)index)));

		bus.WriteByte(surface.GuestBaseAddress, 0xAB, 0);
		bus.WriteByte(surface.GuestBaseAddress + 1, 0xF0, 0);
		Assert.Equal(1, library.BlitRtgToRtg(bitMap, 0, 0, bitMap, 1, 0, 1, 1, 0xC0, 0x0F));
		Assert.Equal(0xFB, bus.ReadByte(surface.GuestBaseAddress + 1));
	}

	[Fact]
	public void FrontViewPortSwitchingAndDpmsControlHostPresentation()
	{
		var bus = new AmigaBus(rtgVramSize: 16L * 1024 * 1024);
		bus.ConfigureAutoconfigRtgForHost();
		var library = new CyberGraphicsLibrary(bus);
		var first = Assert.IsType<CyberGraphicsSurface>(
			library.AllocateRtgSurface(2, 1, CyberGraphicsPixelFormat.Argb32));
		var second = Assert.IsType<CyberGraphicsSurface>(
			library.AllocateRtgSurface(2, 1, CyberGraphicsPixelFormat.Argb32));
		const uint firstBitMap = 0x3000;
		const uint secondBitMap = 0x3040;
		const uint firstViewPort = 0x4000;
		const uint secondViewPort = 0x4040;
		library.RegisterBitMap(firstBitMap, first);
		library.RegisterBitMap(secondBitMap, second);
		Assert.True(library.ChangeViewPortBitMap(firstViewPort, firstBitMap));
		Assert.True(library.ChangeViewPortBitMap(secondViewPort, secondBitMap));
		bus.WriteLong(first.GuestBaseAddress, 0xFF11_2233u);
		bus.WriteLong(second.GuestBaseAddress, 0xFFAA_BBCCu);

		library.SelectFrontViewPort(firstViewPort);
		Assert.True(library.TryRenderRtgFrame(out var firstFrame));
		Assert.Equal(0xFF11_2233u, firstFrame.Bgra[0]);
		library.SelectFrontViewPort(secondViewPort);
		Assert.True(library.TryRenderRtgFrame(out var secondFrame));
		Assert.Equal(0xFFAA_BBCCu, secondFrame.Bgra[0]);

		const uint tags = 0x5000;
		bus.WriteLong(tags, CyberGraphicsLibrary.SetVideoDpmsLevel);
		bus.WriteLong(tags + 4, 1);
		bus.WriteLong(tags + 8, CyberGraphicsLibrary.TagDone);
		var state = new M68kCpuState();
		state.A[0] = secondViewPort;
		state.A[1] = tags;
		Assert.True(library.Invoke(-162, state));
		Assert.False(library.TryRenderRtgFrame(out _));

		Assert.True(library.UnregisterViewPort(secondViewPort));
		Assert.False(library.RtgScanoutSelected);
	}

	[Fact]
	public void ViewPortChainBuildsMixedCompositionUsingTopRtgModeAndNativeGeometry()
	{
		var bus = new AmigaBus(rtgVramSize: 16L * 1024 * 1024);
		bus.ConfigureAutoconfigRtgForHost();
		var library = new CyberGraphicsLibrary(bus);
		var mode = new CyberGraphicsMode(
			0x4350_FF01, 4, 4, 8, CyberGraphicsPixelFormat.Lut8, "Test 4x4");
		library.RegisterMode(mode);
		var surface = Assert.IsType<CyberGraphicsSurface>(
			library.AllocateRtgSurface(4, 4, CyberGraphicsPixelFormat.Lut8));
		var backBuffer = Assert.IsType<CyberGraphicsSurface>(
			library.AllocateRtgSurface(4, 4, CyberGraphicsPixelFormat.Lut8));
		const uint view = 0x1000;
		const uint rtgViewPort = 0x2000;
		const uint planarViewPort = 0x2100;
		const uint bitMap = 0x3000;
		const uint backBitMap = 0x3100;
		library.RegisterBitMap(bitMap, surface);
		library.RegisterBitMap(backBitMap, backBuffer);
		library.RegisterViewPort(rtgViewPort, surface, mode);
		bus.WriteLong(view, rtgViewPort);
		bus.WriteLong(rtgViewPort, planarViewPort);
		bus.WriteWord(rtgViewPort + 0x18, 4);
		bus.WriteWord(rtgViewPort + 0x1A, 2);
		bus.WriteWord(rtgViewPort + 0x1C, 0);
		bus.WriteWord(rtgViewPort + 0x1E, 2);
		bus.WriteWord(planarViewPort + 0x18, 4);
		bus.WriteWord(planarViewPort + 0x1A, 4);

		Assert.True(library.TryBuildDisplayComposition(view, 716, 570, out var composition));
		Assert.Equal(4, composition.Width);
		Assert.Equal(4, composition.Height);
		Assert.True(composition.TopIsRtg);
		Assert.Collection(
			composition.Layers,
			layer =>
			{
				Assert.True(layer.IsRtg);
				Assert.Equal(2, layer.Y);
				Assert.Equal(2, layer.Height);
			},
			layer =>
			{
				Assert.False(layer.IsRtg);
				Assert.Equal(4, layer.Height);
			});

		// Intuition dragging changes the native ViewPort geometry in place.  The
		// host must observe that state directly instead of requiring another hook.
		bus.WriteWord(rtgViewPort + 0x1E, 1);
		Assert.True(library.TryBuildDisplayComposition(view, 716, 570, out composition));
		Assert.Equal(1, composition.Layers[0].Y);

		// ChangeVPBitMap-style backbuffer flips retain the ViewPort's display mode.
		backBuffer.Palette[1] = 0xFF12_3456u;
		bus.WriteByte(backBuffer.GuestBaseAddress, 1, 0);
		Assert.True(library.ChangeViewPortBitMap(rtgViewPort, backBitMap));
		Assert.True(library.TryBuildDisplayComposition(view, 716, 570, out composition));
		Assert.Equal(0xFF12_3456u, composition.Layers[0].Bgra![0]);
	}

	private static byte ApplyMinterm(byte source, byte destination, int operation)
	{
		var result = 0;
		if ((operation & 8) != 0) result |= source & destination;
		if ((operation & 4) != 0) result |= source & ~destination;
		if ((operation & 2) != 0) result |= ~source & destination;
		if ((operation & 1) != 0) result |= ~source & ~destination;
		return (byte)result;
	}

	private static void WritePlanarBitMap(AmigaBus bus, uint bitMap, byte depth, uint firstPlane)
	{
		bus.WriteWord(bitMap, 2);
		bus.WriteWord(bitMap + 2, 1);
		bus.WriteByte(bitMap + 5, depth, 0);
		for (var plane = 0; plane < depth; plane++)
		{
			bus.WriteLong(bitMap + 8u + (uint)(plane * 4), firstPlane + (uint)(plane * 0x100));
		}
	}
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

    [Fact]
    public void SystemLibraryPatchModulesSaveVectorsHandleRtgAndTailChainNativeCalls()
    {
        const uint execBase = 0x1000;
        const uint graphicsBase = 0x3000;
        const uint intuitionBase = 0x4000;
        const uint layersBase = 0x5000;
        const uint graphicsOriginal = 0x7000;
        var bus = new AmigaBus();
        LinkLibraryList(bus, execBase, graphicsBase, intuitionBase, layersBase);
        InitializeLibrary(bus, graphicsBase, 0x6000, "graphics.library", 40);
        InitializeLibrary(bus, intuitionBase, 0x6040, "intuition.library", 40);
        InitializeLibrary(bus, layersBase, 0x6080, "layers.library", 40);
        WriteLibraryVector(bus, graphicsBase, -30, graphicsOriginal);
        WriteLibraryVector(bus, graphicsBase, -918, graphicsOriginal + 4);
        WriteLibraryVector(bus, graphicsBase, -942, graphicsOriginal + 8);
        WriteLibraryVector(bus, intuitionBase, -252, 0x7100);
        WriteLibraryVector(bus, intuitionBase, -768, 0x7110);
        WriteLibraryVector(bus, intuitionBase, -774, 0x7120);
        WriteLibraryVector(bus, intuitionBase, -780, 0x7130);
        WriteLibraryVector(bus, layersBase, -72, 0x7200);
        var services = new GuestServices(0x8000);
        var library = new CyberGraphicsLibrary(bus, services);

        Assert.Equal(8, library.InstallSystemPatches(execBase));
        Assert.Equal(
            new[] { "graphics.library", "intuition.library", "layers.library" },
            library.SystemPatches.Modules.Select(module => module.LibraryName).OrderBy(name => name));
        Assert.Equal(8, library.SystemPatches.InstalledVectors.Count);

        var nativeState = new M68kCpuState();
        nativeState.ProgramCounter = graphicsBase - 30 + 4;
        nativeState.D[0] = 0x1122_3344;
        nativeState.A[0] = 0x5566_7788;
        Assert.True(InvokeInstalledTrap(bus, graphicsBase - 30, nativeState));
        Assert.Equal(graphicsOriginal, nativeState.ProgramCounter);
        Assert.Equal(0x1122_3344u, nativeState.D[0]);
        Assert.Equal(0x5566_7788u, nativeState.A[0]);

        foreach (var (offset, original) in new[]
        {
            (-942, graphicsOriginal + 8),
            (-768, 0x7110u),
            (-774, 0x7120u),
            (-780, 0x7130u)
        })
        {
            var libraryBase = offset == -942 ? graphicsBase : intuitionBase;
            var screenBufferState = new M68kCpuState();
            screenBufferState.ProgramCounter = AddOffset(libraryBase, offset) + 4;
            Assert.True(InvokeInstalledTrap(bus, AddOffset(libraryBase, offset), screenBufferState));
            Assert.Equal(original, screenBufferState.ProgramCounter);
        }

        services.HandledGraphicsOffset = -918;
        var rtgState = new M68kCpuState();
        rtgState.ProgramCounter = graphicsBase - 918 + 4;
        Assert.True(InvokeInstalledTrap(bus, graphicsBase - 918, rtgState));
        Assert.Equal(graphicsBase - 918 + 4, rtgState.ProgramCounter);
        Assert.Equal(0xCAFE_BABEu, rtgState.D[0]);
        Assert.Equal(-918, services.LastGraphicsOffset);
    }

    [Fact]
    public void SystemLibraryPatchManagerRejectsOldLibrariesAndNonStandardVectors()
    {
        const uint execBase = 0x1000;
        const uint graphicsBase = 0x3000;
        var bus = new AmigaBus();
        LinkLibraryList(bus, execBase, graphicsBase);
        InitializeLibrary(bus, graphicsBase, 0x6000, "graphics.library", 38);
        WriteLibraryVector(bus, graphicsBase, -30, 0x7000);
        var library = new CyberGraphicsLibrary(bus, new GuestServices(0x8000));

        Assert.Equal(0, library.InstallSystemPatches(execBase));
        Assert.False(bus.HasHostTrapStub(graphicsBase - 30));

        bus.WriteWord(graphicsBase + 0x14, 40);
        bus.WriteWord(graphicsBase - 30, 0x4E75);
        Assert.Equal(0, library.InstallSystemPatches(execBase));
        Assert.False(bus.HasHostTrapStub(graphicsBase - 30));
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

    private static void LinkLibraryList(AmigaBus bus, uint execBase, params uint[] libraries)
    {
        var list = execBase + 0x17A;
        var tail = list + 4;
        bus.WriteLong(list, libraries.Length == 0 ? tail : libraries[0]);
        bus.WriteLong(list + 4, 0);
        bus.WriteLong(list + 8, libraries.Length == 0 ? list : libraries[^1]);
        for (var index = 0; index < libraries.Length; index++)
        {
            bus.WriteLong(libraries[index], index + 1 < libraries.Length ? libraries[index + 1] : tail);
            bus.WriteLong(libraries[index] + 4, index == 0 ? list : libraries[index - 1]);
        }
    }

    private static void InitializeLibrary(
        AmigaBus bus,
        uint libraryBase,
        uint nameAddress,
        string name,
        ushort version)
    {
        bus.WriteLong(libraryBase + 0x0A, nameAddress);
        bus.WriteWord(libraryBase + 0x14, version);
        WriteAscii(bus, nameAddress, name);
    }

    private static void WriteLibraryVector(AmigaBus bus, uint libraryBase, int offset, uint target)
    {
        var vector = AddOffset(libraryBase, offset);
        bus.WriteWord(vector, 0x4EF9);
        bus.WriteLong(vector + 2, target);
    }

    private static bool InvokeInstalledTrap(AmigaBus bus, uint address, M68kCpuState state)
        => bus.TryInvokeHostTrap(address, bus.ReadWord(address + 2), state);

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

        public int? HandledGraphicsOffset { get; set; }

        public int? LastGraphicsOffset { get; private set; }

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

        public bool TryInvokeGraphicsLibraryPatch(int vectorOffset, M68kCpuState state)
        {
            LastGraphicsOffset = vectorOffset;
            if (HandledGraphicsOffset != vectorOffset)
            {
                return false;
            }

            state.D[0] = 0xCAFE_BABE;
            return true;
        }
    }
}
