using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaBlitterConformanceMatrixTests
{
	private const uint SourceA = 0x3000;
	private const uint SourceB = 0x3200;
	private const uint SourceC = 0x3400;
	private const uint DestinationD = 0x4000;
	private const int LineRowStride = 0x20;

	public static IEnumerable<object[]> AreaMintermRows =>
		Enumerable.Range(0, 256).Select(minterm => new object[] { new AreaMintermRow((byte)minterm) });

	public static IEnumerable<object[]> ChannelRows =>
		Enumerable.Range(0, 16).Select(mask => new object[] { new ChannelRow(mask) });

	public static IEnumerable<object[]> MaskRows
	{
		get
		{
			yield return new object[] { new MaskRow("single word combines first and last masks", 1, 0x0FF0, 0x00FF, new ushort[] { 0x00F0 }) };
			yield return new object[] { new MaskRow("first middle last", 3, 0x0F0F, 0xF0F0, new ushort[] { 0x0F0F, 0xFFFF, 0xF0F0 }) };
			yield return new object[] { new MaskRow("empty first word still advances", 2, 0x0000, 0xFFFF, new ushort[] { 0x0000, 0xFFFF }) };
		}
	}

	public static IEnumerable<object[]> ShiftRows
	{
		get
		{
			foreach (var descending in new[] { false, true })
			{
				for (var shift = 0; shift < 16; shift++)
				{
					yield return new object[] { new ShiftRow(shift, descending) };
				}
			}
		}
	}

	public static IEnumerable<object[]> FillRows
	{
		get
		{
			yield return new object[] { new FillRow("disabled", 0x0000, false, false, 0x0008) };
			yield return new object[] { new FillRow("inclusive fci=0", 0x0008, false, false, 0x0008) };
			yield return new object[] { new FillRow("inclusive fci=1", 0x000C, false, true, 0x0008) };
			yield return new object[] { new FillRow("exclusive fci=0", 0x0010, true, false, 0x0008) };
			yield return new object[] { new FillRow("exclusive fci=1", 0x0014, true, true, 0x0008) };
		}
	}

	public static IEnumerable<object[]> LineOctantRows
	{
		get
		{
			for (var octant = 0; octant < 8; octant++)
			{
				yield return new object[] { new LineOctantRow((ushort)(octant << 2), Sign: false) };
				yield return new object[] { new LineOctantRow((ushort)(octant << 2), Sign: true) };
			}
		}
	}

	public static IEnumerable<object[]> LineTextureRows =>
		Enumerable.Range(0, 16).Select(shift => new object[] { new LineTextureRow(shift) });

	public static IEnumerable<object[]> AreaTimingRows
	{
		get
		{
			yield return new object[] { new AreaTimingRow("D-only clear", 0x0100, DestinationD, 4, 1) };
			yield return new object[] { new AreaTimingRow("A+D copy", 0x09F0, SourceA, 4, 2) };
			yield return new object[] { new AreaTimingRow("B+D copy", 0x05CC, SourceB, 6, 2) };
			yield return new object[] { new AreaTimingRow("B+C+D copy", 0x07CA, SourceB, 8, 3) };
		}
	}

	[Fact]
	public void BlitterConformanceMatrixCoversHrmFeatureGroups()
	{
		var groups = new[]
		{
			nameof(AreaMintermRows),
			nameof(ChannelRows),
			nameof(MaskRows),
			nameof(ShiftRows),
			nameof(FillRows),
			nameof(LineOctantRows),
			nameof(LineTextureRows),
			nameof(AreaTimingRows),
			"undocumented-ocs",
			"dma-control",
			"interrupt-status"
		};

		Assert.Equal(groups.Length, groups.Distinct().Count());
		Assert.Equal(256, AreaMintermRows.Count());
		Assert.Equal(16, ChannelRows.Count());
		Assert.Equal(32, ShiftRows.Count());
		Assert.Equal(16, LineOctantRows.Count());
		Assert.Equal(16, LineTextureRows.Count());
		Assert.Equal(4, AreaTimingRows.Count());
	}

	[Theory]
	[MemberData(nameof(AreaMintermRows))]
	public void BlitterAreaMintermTruthTableMatchesHrmReference(object rowObject)
	{
		var row = Assert.IsType<AreaMintermRow>(rowObject);
		var bus = new AmigaBus();
		const ushort a = 0xFF00;
		const ushort b = 0xF0F0;
		const ushort c = 0xCCCC;
		WriteWord(bus, SourceA, a);
		WriteWord(bus, SourceB, b);
		WriteWord(bus, SourceC, c);

		ConfigureAreaBlit(bus, (ushort)(0x0F00 | row.Minterm));
		StartBlitAndRun(bus, widthWords: 1, height: 1);

		var expected = HrmReference.ApplyMinterm(row.Minterm, a, b, c);
		Assert.Equal(expected, ReadWord(bus, DestinationD));
	}

	[Theory]
	[MemberData(nameof(ChannelRows))]
	public void BlitterChannelEnableBitsControlDmaReadsAndWrites(object rowObject)
	{
		var row = Assert.IsType<ChannelRow>(rowObject);
		var bus = new AmigaBus();
		const ushort memoryA = 0xFF00;
		const ushort memoryB = 0xF0F0;
		const ushort memoryC = 0xCCCC;
		const ushort registerA = 0x00FF;
		const ushort registerB = 0x0F0F;
		const ushort registerC = 0x3333;
		const ushort sentinel = 0x5A5A;
		WriteWord(bus, SourceA, memoryA);
		WriteWord(bus, SourceB, memoryB);
		WriteWord(bus, SourceC, memoryC);
		WriteWord(bus, DestinationD, sentinel);
		bus.WriteWord(0x00DFF074, registerA);
		bus.WriteWord(0x00DFF072, registerB);
		bus.WriteWord(0x00DFF070, registerC);

		ConfigureAreaBlit(bus, (ushort)((row.Mask << 8) | 0xCA));
		StartBlitAndRun(bus, widthWords: 1, height: 1);

		var expectedA = row.UseA ? memoryA : registerA;
		var expectedB = row.UseB ? memoryB : registerB;
		var expectedC = row.UseC ? memoryC : registerC;
		var expected = HrmReference.ApplyMinterm(0xCA, expectedA, expectedB, expectedC);
		Assert.Equal(row.UseD ? expected : sentinel, ReadWord(bus, DestinationD));

		var blitterAccesses = bus.BusAccesses
			.Where(access => access.Request.Requester == AmigaBusRequester.Blitter && access.Request.Kind == AmigaBusAccessKind.Blitter)
			.ToArray();
		Assert.Equal(row.EnabledSourceCount, blitterAccesses.Count(access => !access.Request.IsWrite));
		Assert.Equal(row.UseD ? 1 : 0, blitterAccesses.Count(access => access.Request.IsWrite));
	}

	[Theory]
	[MemberData(nameof(MaskRows))]
	public void BlitterFirstAndLastMasksFollowHrmWordRules(object rowObject)
	{
		var row = Assert.IsType<MaskRow>(rowObject);
		var bus = new AmigaBus();
		for (var i = 0; i < row.WidthWords; i++)
		{
			WriteWord(bus, SourceA + (uint)(i * 2), 0xFFFF);
			WriteWord(bus, DestinationD + (uint)(i * 2), 0x5555);
		}

		ConfigureAreaBlit(bus, 0x09F0);
		bus.WriteWord(0x00DFF044, row.FirstMask);
		bus.WriteWord(0x00DFF046, row.LastMask);
		StartBlitAndRun(bus, row.WidthWords, height: 1);

		for (var i = 0; i < row.Expected.Length; i++)
		{
			Assert.Equal(row.Expected[i], ReadWord(bus, DestinationD + (uint)(i * 2)));
		}
	}

	[Fact]
	public void BlitterSizeFieldsUseHrmZeroAliases()
	{
		var widthBus = new AmigaBus();
		for (var i = 0; i < 64; i++)
		{
			WriteWord(widthBus, SourceA + (uint)(i * 2), (ushort)(0x1000 + i));
		}

		ConfigureAreaBlit(widthBus, 0x09F0);
		StartBlitAndRun(widthBus, bltsize: 0x0040);
		Assert.Equal(0x1000, ReadWord(widthBus, DestinationD));
		Assert.Equal(0x103F, ReadWord(widthBus, DestinationD + 126));

		var heightBus = new AmigaBus();
		for (var i = 0; i < 1024; i++)
		{
			WriteWord(heightBus, SourceA + (uint)(i * 2), (ushort)(0x2000 + i));
		}

		ConfigureAreaBlit(heightBus, 0x09F0);
		StartBlitAndRun(heightBus, bltsize: 0x0001);
		Assert.Equal(0x2000, ReadWord(heightBus, DestinationD));
		Assert.Equal(0x23FF, ReadWord(heightBus, DestinationD + 2046));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void BlitterAreaClearWithDOnlyMintermZeroClearsRectangleAndPreservesModuloGuards(bool enableHardwareSpecialization)
	{
		var bus = new AmigaBus(enableHardwareSpecialization: enableHardwareSpecialization);
		const int widthWords = 3;
		const int height = 4;
		const int rowStride = 20;
		const uint destination = DestinationD + 0x1200;
		for (var y = -1; y <= height; y++)
		{
			for (var word = -1; word <= widthWords; word++)
			{
				var address = destination + (uint)(y * rowStride) + (uint)(word * 2);
				WriteWord(bus, address, (ushort)(0xA000 | ((y + 1) << 8) | (word + 1)));
			}
		}

		ConfigureAreaBlit(bus, 0x0100, destinationD: destination);
		bus.WriteWord(0x00DFF066, rowStride - (widthWords * 2));

		EnableBlitterDma(bus);
		bus.WriteWord(0x00DFF058, (ushort)((height << 6) | widthWords));
		var expectedCompletion = bus.Blitter.CaptureSnapshot().NextDmaCycle +
			(widthWords * height * 4);
		RunBlitterUntilIdle(bus);
		Assert.Equal(expectedCompletion, bus.Blitter.CaptureSnapshot().CurrentCycle);

		for (var y = 0; y < height; y++)
		{
			for (var word = 0; word < widthWords; word++)
			{
				Assert.Equal(0, ReadWord(bus, destination + (uint)(y * rowStride) + (uint)(word * 2)));
			}

			Assert.NotEqual(0, ReadWord(bus, destination + (uint)(y * rowStride) - 2));
			Assert.NotEqual(0, ReadWord(bus, destination + (uint)(y * rowStride) + (uint)(widthWords * 2)));
		}

		for (var word = -1; word <= widthWords; word++)
		{
			Assert.NotEqual(0, ReadWord(bus, destination - (uint)rowStride + (uint)(word * 2)));
			Assert.NotEqual(0, ReadWord(bus, destination + (uint)(height * rowStride) + (uint)(word * 2)));
		}
	}

	[Theory]
	[MemberData(nameof(ShiftRows))]
	public void BlitterBarrelShiftAndCarryMatchHrmReference(object rowObject)
	{
		var row = Assert.IsType<ShiftRow>(rowObject);
		var bus = new AmigaBus();
		var words = row.Descending
			? new[] { (Address: SourceA + 2, Value: (ushort)0x5678), (Address: SourceA, Value: (ushort)0x1234) }
			: new[] { (Address: SourceA, Value: (ushort)0x1234), (Address: SourceA + 2, Value: (ushort)0x5678) };
		foreach (var word in words)
		{
			WriteWord(bus, word.Address, word.Value);
		}

		var sourcePointer = row.Descending ? SourceA + 2 : SourceA;
		var destinationPointer = row.Descending ? DestinationD + 2 : DestinationD;
		ConfigureAreaBlit(
			bus,
			(ushort)(0x0900 | (row.Shift << 12) | 0x00F0),
			bltcon1: row.Descending ? (ushort)0x0002 : (ushort)0,
			sourceA: sourcePointer,
			destinationD: destinationPointer);
		StartBlitAndRun(bus, widthWords: 2, height: 1);

		ushort previous = 0;
		var first = HrmReference.ShiftSource(words[0].Value, ref previous, row.Shift, row.Descending);
		var second = HrmReference.ShiftSource(words[1].Value, ref previous, row.Shift, row.Descending);
		if (row.Descending)
		{
			Assert.Equal(first, ReadWord(bus, DestinationD + 2));
			Assert.Equal(second, ReadWord(bus, DestinationD));
		}
		else
		{
			Assert.Equal(first, ReadWord(bus, DestinationD));
			Assert.Equal(second, ReadWord(bus, DestinationD + 2));
		}
	}

	[Theory]
	[MemberData(nameof(FillRows))]
	public void BlitterAreaFillMatchesHrmInclusiveExclusiveRules(object rowObject)
	{
		var row = Assert.IsType<FillRow>(rowObject);
		var bus = new AmigaBus();
		WriteWord(bus, SourceA, row.Source);

		ConfigureAreaBlit(bus, 0x09F0, bltcon1: (ushort)(0x0002 | row.Bltcon1FillBits));
		StartBlitAndRun(bus, widthWords: 1, height: 1);

		var minterm = HrmReference.ApplyMinterm(0xF0, row.Source, 0, 0);
		var expected = row.Bltcon1FillBits == 0
			? minterm
			: HrmReference.ApplyFill(minterm, row.Exclusive, row.FillCarryIn);
		Assert.Equal(expected, ReadWord(bus, DestinationD));
	}

	[Theory]
	[MemberData(nameof(LineOctantRows))]
	public void BlitterLineOctantAndSignBitsMatchHrmStepRules(object rowObject)
	{
		var row = Assert.IsType<LineOctantRow>(rowObject);
		var bus = new AmigaBus();
		var baseAddress = DestinationD + 0x800;
		var bltcon1 = (ushort)(0x0001 | row.OctantBits | (row.Sign ? 0x0040 : 0));
		var expected = HrmReference.NextLineDelta(row.OctantBits, row.Sign);

		ConfigureLineBlit(bus, baseAddress, LineRowStride, bltcon1, initialAccumulator: row.Sign ? (short)-2 : (short)0);
		bus.WriteWord(0x00DFF058, 0x0082);
		RunBlitterUntilIdle(bus);

		Assert.True(IsLinePixelSet(bus, baseAddress, LineRowStride, 0, 0), row.ToString());
		Assert.True(IsLinePixelSet(bus, baseAddress, LineRowStride, expected.X, expected.Y), row.ToString());
	}

	[Theory]
	[MemberData(nameof(LineTextureRows))]
	public void BlitterLineTextureStartPhaseMatchesHrmReference(object rowObject)
	{
		var row = Assert.IsType<LineTextureRow>(rowObject);
		var bus = new AmigaBus();
		var baseAddress = DestinationD + 0x1000;
		var bltcon1 = (ushort)(0x0001 | (row.ShiftB << 12));

		ConfigureLineBlit(bus, baseAddress, LineRowStride, bltcon1, texture: 0x8000);
		bus.WriteWord(0x00DFF058, 0x0042);
		RunBlitterUntilIdle(bus);

		Assert.Equal(row.ShiftB == 0, IsLinePixelSet(bus, baseAddress, LineRowStride, 0, 0));
	}

	[Fact]
	public void BlitterDmaControlBusyZeroInterruptAndNastyModeMatchHrmControlExpectations()
	{
		var bus = new AmigaBus(
			expansionRamSize: 0x10000,
			realFastRamSize: 0x10000);
		for (var i = 0; i < 4; i++)
		{
			WriteWord(bus, SourceA + (uint)(i * 2), (ushort)(0x1234 + i));
		}

		ConfigureAreaBlit(bus, 0x09F0);
		bus.WriteWord(0x00DFF058, 0x0044);

		bus.AdvanceDmaTo(100);
		Assert.True(bus.Blitter.CaptureSnapshot().Busy);
		Assert.Equal(0, ReadWord(bus, DestinationD));

		EnableBlitterDma(bus, nasty: true, cycle: 100);
		var expansionCycle = 100L;
		_ = bus.ReadWord(AmigaConstants.A500BootPseudoFastRamBase, ref expansionCycle, AmigaBusAccessKind.CpuDataRead);
		Assert.True(expansionCycle > 100);

		var realFastCycle = 100L;
		_ = bus.ReadWord(AmigaConstants.A500RealFastRamBase, ref realFastCycle, AmigaBusAccessKind.CpuDataRead);
		Assert.Equal(100, realFastCycle);

		var chipCycle = 100L;
		_ = bus.ReadWord(0x00001000, ref chipCycle, AmigaBusAccessKind.CpuDataRead);
		Assert.True(chipCycle > 100);

		RunBlitterUntilIdle(bus);
		Assert.Equal(0, bus.ReadWord(0x00DFF002) & 0x4000);
		Assert.Equal(0, bus.ReadWord(0x00DFF002) & 0x2000);
		Assert.NotEqual(0, bus.ReadWord(0x00DFF01E) & AmigaConstants.IntreqBlitter);
	}

	[Theory]
	[MemberData(nameof(AreaTimingRows))]
	public void BlitterAreaModeUsesHrmNoContentionSlotTiming(object rowObject)
	{
		var row = Assert.IsType<AreaTimingRow>(rowObject);
		var bus = new AmigaBus();
		WriteWord(bus, row.SourceAddress, 0x1234);
		WriteWord(bus, SourceC, 0x00FF);
		ConfigureAreaBlit(bus, row.Bltcon0);
		EnableBlitterDma(bus);

		bus.WriteWord(0x00DFF058, 0x0041);
		var startCycle = bus.Blitter.CaptureSnapshot().NextDmaCycle;
		var expectedCompletion = startCycle + row.ExpectedCycles;

		bus.AdvanceDmaTo(expectedCompletion - 1);
		Assert.True(bus.Blitter.CaptureSnapshot().Busy, row.ToString());

		bus.AdvanceDmaTo(expectedCompletion);
		var snapshot = bus.Blitter.CaptureSnapshot();
		Assert.False(snapshot.Busy, row.ToString());
		Assert.Equal(expectedCompletion, snapshot.CurrentCycle);

		var blitterDma = bus.BusAccesses
			.Where(access => access.Request.Requester == AmigaBusRequester.Blitter && access.Request.Kind == AmigaBusAccessKind.Blitter)
			.ToArray();
		Assert.Equal(row.ExpectedMicroOps, blitterDma.Length);
		Assert.Equal(expectedCompletion - AgnusChipSlotScheduler.SlotCycles, blitterDma[^1].GrantedCycle);
	}

	[Fact]
	public void BlitterBusyRemainsSetWhenBitplaneDmaDelaysFinalWriteSlot()
	{
		var bus = new AmigaBus(
			captureBusAccesses: true,
			enableLiveAgnusDma: true);
		var fetchCycle = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY) +
			(0x38 * AgnusChipSlotScheduler.SlotCycles);
		var destination = 0x1000u;
		WriteWord(bus, SourceA, 0xCAFE);
		WriteWord(bus, destination, 0x1234);
		ConfigureAreaBlit(bus, 0x09F0, destinationD: destination);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x0038);
		WritePointer(bus, 0x00DFF0E0, 0x2000);
		bus.WriteWord(0x00DFF100, 0x1000);
		bus.WriteWord(0x00DFF096, 0x8340);
		bus.EnableLiveAgnusDma();

		bus.Blitter.WriteRegister(0x058, 0x0041, fetchCycle - (2 * AgnusChipSlotScheduler.SlotCycles));
		var startCycle = bus.Blitter.CaptureSnapshot().NextDmaCycle;
		Assert.Equal(fetchCycle - AgnusChipSlotScheduler.SlotCycles, startCycle);

		var nominalCompletion = startCycle + 4;
		bus.AdvanceDmaTo(nominalCompletion);

		Assert.True(bus.Blitter.CaptureSnapshot().Busy);
		Assert.Equal(0xCAFE, bus.ReadChipWordForPresentation(destination, nominalCompletion));
		Assert.Equal(0, bus.ReadWord(0x00DFF01E) & AmigaConstants.IntreqBlitter);

		bus.AdvanceDmaTo(nominalCompletion + AgnusChipSlotScheduler.SlotCycles);
		Assert.False(bus.Blitter.CaptureSnapshot().Busy);
		Assert.Equal(0xCAFE, bus.ReadChipWordForPresentation(destination, nominalCompletion + AgnusChipSlotScheduler.SlotCycles));
	}

	[Fact]
	public void BlitterLineModeUsesEightHrmTicksPerPixel()
	{
		var bus = new AmigaBus();
		var baseAddress = DestinationD + 0x1800;
		ConfigureLineBlit(bus, baseAddress, LineRowStride, bltcon1: 0x0001);

		bus.WriteWord(0x00DFF058, 0x0082);
		var startCycle = bus.Blitter.CaptureSnapshot().NextDmaCycle;
		var expectedCompletion = startCycle + (2 * 8);

		bus.AdvanceDmaTo(expectedCompletion - 1);
		Assert.True(bus.Blitter.CaptureSnapshot().Busy);

		bus.AdvanceDmaTo(expectedCompletion);
		Assert.False(bus.Blitter.CaptureSnapshot().Busy);
		Assert.Equal(expectedCompletion, bus.Blitter.CaptureSnapshot().CurrentCycle);
	}

	[Fact]
	public void UndocumentedOcsLineModeRequiresCButIgnoresDEnable()
	{
		var withoutC = new AmigaBus();
		var baseAddress = DestinationD + 0x1A00;
		ConfigureLineBlit(withoutC, baseAddress, LineRowStride, bltcon1: 0x0001, channelMask: 0x0900);
		withoutC.WriteWord(0x00DFF058, 0x0041);
		RunBlitterUntilIdle(withoutC);
		Assert.False(IsLinePixelSet(withoutC, baseAddress, LineRowStride, 0, 0));

		var withoutD = new AmigaBus();
		ConfigureLineBlit(withoutD, baseAddress, LineRowStride, bltcon1: 0x0001, channelMask: 0x0A00);
		withoutD.WriteWord(0x00DFF058, 0x0041);
		RunBlitterUntilIdle(withoutD);
		Assert.True(IsLinePixelSet(withoutD, baseAddress, LineRowStride, 0, 0));
	}

	[Fact]
	public void UndocumentedOcsLineModeUsesDPointerOnlyForFirstPixel()
	{
		var bus = new AmigaBus();
		var baseAddress = DestinationD + 0x1C00;
		var firstPixelAddress = DestinationD + 0x1E00;
		ConfigureLineBlit(bus, baseAddress, LineRowStride, bltcon1: 0x0001, destinationD: firstPixelAddress);

		bus.WriteWord(0x00DFF058, 0x0082);
		RunBlitterUntilIdle(bus);

		Assert.Equal(0x8000, ReadWord(bus, firstPixelAddress));
		Assert.Equal(0x4000, ReadWord(bus, baseAddress + LineRowStride));
		var snapshot = bus.Blitter.CaptureSnapshot();
		Assert.Equal(snapshot.SourceC, snapshot.DestinationD);
	}

	[Fact]
	public void UndocumentedOcsLineModeUsesBltcmodForDestinationStride()
	{
		var bus = new AmigaBus();
		var baseAddress = DestinationD + 0x2000;
		ConfigureLineBlit(
			bus,
			baseAddress,
			LineRowStride,
			0x0041,
			initialAccumulator: -2,
			aModulo: -4,
			dModulo: 0x40);

		bus.WriteWord(0x00DFF058, 0x0082);
		RunBlitterUntilIdle(bus);

		Assert.True(IsLinePixelSet(bus, baseAddress, LineRowStride, 0, 0));
		Assert.True(IsLinePixelSet(bus, baseAddress, LineRowStride, 0, 1));
		Assert.False(IsLinePixelSet(bus, baseAddress, 0x40, 0, 1));
	}

	[Fact]
	public void UndocumentedOcsLineModeDoesNotUpdateBltaptWhenAIsDisabled()
	{
		var bus = new AmigaBus();
		var baseAddress = DestinationD + 0x2200;
		ConfigureLineBlit(bus, baseAddress, LineRowStride, bltcon1: 0x0001, initialAccumulator: 0x1234, channelMask: 0x0300);

		bus.WriteWord(0x00DFF058, 0x0041);
		RunBlitterUntilIdle(bus);

		Assert.Equal((uint)0x1234, bus.Blitter.CaptureSnapshot().SourceA);
		Assert.True(IsLinePixelSet(bus, baseAddress, LineRowStride, 0, 0));
	}

	[Fact]
	public void UndocumentedOcsLineModeIgnoresBltalwm()
	{
		var bus = new AmigaBus();
		var baseAddress = DestinationD + 0x2400;
		ConfigureLineBlit(bus, baseAddress, LineRowStride, bltcon1: 0x0001);
		bus.WriteWord(0x00DFF046, 0x0000);

		bus.WriteWord(0x00DFF058, 0x0041);
		RunBlitterUntilIdle(bus);

		Assert.True(IsLinePixelSet(bus, baseAddress, LineRowStride, 0, 0));
	}

	[Fact]
	public void UndocumentedOcsHiddenOldBdatPersistsWhenBChannelIsDisabled()
	{
		var bus = new AmigaBus();
		WriteWord(bus, SourceB, 0x0001);
		ConfigureAreaBlit(bus, 0x05CC);
		StartBlitAndRun(bus, widthWords: 1, height: 1);

		bus.WriteWord(0x00DFF042, 0x1000);
		bus.WriteWord(0x00DFF072, 0x0000);
		ConfigureAreaBlit(bus, 0x01CC, bltcon1: 0x1000, destinationD: DestinationD + 0x2600);
		StartBlitAndRun(bus, widthWords: 1, height: 1);

		Assert.Equal(0x8000, ReadWord(bus, DestinationD + 0x2600));
	}

	[Fact]
	public void UndocumentedOcsLineModeBChannelReloadsPatternWords()
	{
		var bus = new AmigaBus();
		var baseAddress = DestinationD + 0x2800;
		WriteWord(bus, SourceB, 0x8000);
		WriteWord(bus, SourceB + 2, 0x4000);
		ConfigureLineBlit(bus, baseAddress, LineRowStride, bltcon1: 0x0001, bModulo: 2, channelMask: 0x0F00);

		bus.WriteWord(0x00DFF058, 0x0082);
		RunBlitterUntilIdle(bus);

		Assert.True(IsLinePixelSet(bus, baseAddress, LineRowStride, 0, 0));
		Assert.True(IsLinePixelSet(bus, baseAddress, LineRowStride, 1, 1));
		Assert.Equal(
			2,
			bus.BusAccesses.Count(access =>
				access.Request.Requester == AmigaBusRequester.Blitter &&
				!access.Request.IsWrite &&
				access.Request.Address == SourceB));
		Assert.Equal(
			2,
			bus.BusAccesses.Count(access =>
				access.Request.Requester == AmigaBusRequester.Blitter &&
				!access.Request.IsWrite &&
				access.Request.Address == SourceB + 2));
	}

	[Fact]
	public void BlitterDoesNotAdvanceWhileDmaIsDisabled()
	{
		var bus = new AmigaBus();
		WriteWord(bus, SourceA, 0x1234);
		ConfigureAreaBlit(bus, 0x09F0);

		bus.WriteWord(0x00DFF058, 0x0041);
		bus.AdvanceDmaTo(1_000);

		Assert.True(bus.Blitter.CaptureSnapshot().Busy);
		Assert.Equal(0x0000, ReadWord(bus, DestinationD));
		Assert.DoesNotContain(bus.BusAccesses, access => access.Request.Requester == AmigaBusRequester.Blitter);
	}

	private static void ConfigureAreaBlit(
		AmigaBus bus,
		ushort bltcon0,
		ushort bltcon1 = 0,
		uint sourceA = SourceA,
		uint sourceB = SourceB,
		uint sourceC = SourceC,
		uint destinationD = DestinationD)
	{
		bus.WriteWord(0x00DFF040, bltcon0);
		bus.WriteWord(0x00DFF042, bltcon1);
		WritePointer(bus, 0x00DFF050, sourceA);
		WritePointer(bus, 0x00DFF04C, sourceB);
		WritePointer(bus, 0x00DFF048, sourceC);
		WritePointer(bus, 0x00DFF054, destinationD);
	}

	private static void ConfigureLineBlit(
		AmigaBus bus,
		uint baseAddress,
		ushort rowStride,
		ushort bltcon1,
		ushort texture = 0xFFFF,
		byte minterm = 0xCA,
		short initialAccumulator = 0,
		short aModulo = 0,
		short bModulo = 0,
		short? dModulo = null,
		ushort channelMask = 0x0B00,
		uint sourceB = SourceB,
		uint? destinationD = null)
	{
		bus.WriteWord(0x00DFF040, (ushort)(channelMask | minterm));
		bus.WriteWord(0x00DFF042, bltcon1);
		WritePointer(bus, 0x00DFF048, baseAddress);
		WritePointer(bus, 0x00DFF04C, sourceB);
		WritePointer(bus, 0x00DFF050, unchecked((uint)(ushort)initialAccumulator));
		WritePointer(bus, 0x00DFF054, destinationD ?? baseAddress);
		bus.WriteWord(0x00DFF060, rowStride);
		bus.WriteWord(0x00DFF062, unchecked((ushort)bModulo));
		bus.WriteWord(0x00DFF064, unchecked((ushort)aModulo));
		bus.WriteWord(0x00DFF066, unchecked((ushort)(dModulo ?? (short)rowStride)));
		bus.WriteWord(0x00DFF072, texture);
		bus.WriteWord(0x00DFF074, 0x8000);
		EnableBlitterDma(bus);
	}

	private static void StartBlitAndRun(AmigaBus bus, int widthWords, int height)
	{
		var bltsize = (ushort)((height << 6) | (widthWords & 0x3F));
		StartBlitAndRun(bus, bltsize);
	}

	private static void StartBlitAndRun(AmigaBus bus, ushort bltsize)
	{
		EnableBlitterDma(bus);
		bus.WriteWord(0x00DFF058, bltsize);
		RunBlitterUntilIdle(bus);
	}

	private static void EnableBlitterDma(AmigaBus bus, bool nasty = false, long cycle = 0)
	{
		bus.WriteWord(0x00DFF096, (ushort)(0x8240 | (nasty ? 0x0400 : 0)), cycle);
		bus.AdvanceDmaTo(cycle);
	}

	private static void RunBlitterUntilIdle(AmigaBus bus, long cycle = 1_000_000)
	{
		bus.AdvanceDmaTo(cycle);
		Assert.False(bus.Blitter.CaptureSnapshot().Busy);
	}

	private static void WritePointer(AmigaBus bus, uint highRegisterAddress, uint pointer)
	{
		bus.WriteWord(highRegisterAddress, (ushort)(pointer >> 16));
		bus.WriteWord(highRegisterAddress + 2, (ushort)pointer);
	}

	private static void WriteWord(AmigaBus bus, uint address, ushort value)
	{
		BigEndian.WriteUInt16(bus.ChipRam, (int)address, value);
	}

	private static long OutputRowStartCycle(int row)
	{
		var line = (0x2C - AmigaConstants.PalLowResOverscanBorderY) + row;
		return (long)line * AmigaConstants.A500PalCpuCyclesPerRasterLine;
	}

	private static ushort ReadWord(AmigaBus bus, uint address)
	{
		return BigEndian.ReadUInt16(bus.ChipRam, (int)address, "blitter conformance word");
	}

	private static bool IsLinePixelSet(AmigaBus bus, uint baseAddress, int rowStride, int x, int y)
	{
		var wordOffset = Math.DivRem(x, 16, out var bit);
		if (bit < 0)
		{
			bit += 16;
			wordOffset--;
		}

		var address = (int)baseAddress + (y * rowStride) + (wordOffset * 2);
		var word = BigEndian.ReadUInt16(bus.ChipRam, address, "line pixel word");
		return (word & (0x8000 >> bit)) != 0;
	}

	private sealed record AreaMintermRow(byte Minterm)
	{
		public override string ToString() => $"minterm=${Minterm:X2}";
	}

	private sealed record ChannelRow(int Mask)
	{
		public bool UseA => (Mask & 0x8) != 0;

		public bool UseB => (Mask & 0x4) != 0;

		public bool UseC => (Mask & 0x2) != 0;

		public bool UseD => (Mask & 0x1) != 0;

		public int EnabledSourceCount => (UseA ? 1 : 0) + (UseB ? 1 : 0) + (UseC ? 1 : 0);

		public override string ToString() => $"channels={(UseA ? "A" : "")}{(UseB ? "B" : "")}{(UseC ? "C" : "")}{(UseD ? "D" : "")}";
	}

	private sealed record MaskRow(string Name, int WidthWords, ushort FirstMask, ushort LastMask, ushort[] Expected)
	{
		public override string ToString() => Name;
	}

	private sealed record ShiftRow(int Shift, bool Descending)
	{
		public override string ToString() => $"shift={Shift}, descending={Descending}";
	}

	private sealed record FillRow(string Name, ushort Bltcon1FillBits, bool Exclusive, bool FillCarryIn, ushort Source)
	{
		public override string ToString() => Name;
	}

	private sealed record LineOctantRow(ushort OctantBits, bool Sign)
	{
		public override string ToString() => $"octant=0x{OctantBits:X4}, sign={Sign}";
	}

	private sealed record LineTextureRow(int ShiftB)
	{
		public override string ToString() => $"texture shift={ShiftB}";
	}

	private sealed record AreaTimingRow(string Name, ushort Bltcon0, uint SourceAddress, int ExpectedCycles, int ExpectedMicroOps)
	{
		public override string ToString() => Name;
	}

	private static class HrmReference
	{
		// Source: CopperMod.Amiga/References/Commodore_Amiga_Hardware_Reference_Manual_2nd.pdf,
		// blitter chapter tables for minterms, DMA channel enables, fill, and line octants.
		public static ushort ApplyMinterm(byte minterm, ushort sourceA, ushort sourceB, ushort sourceC)
		{
			ushort value = 0;
			for (var bit = 0; bit < 16; bit++)
			{
				var mask = 1 << bit;
				var selector = 0;
				if ((sourceA & mask) != 0)
				{
					selector |= 4;
				}

				if ((sourceB & mask) != 0)
				{
					selector |= 2;
				}

				if ((sourceC & mask) != 0)
				{
					selector |= 1;
				}

				if (((minterm >> selector) & 1) != 0)
				{
					value |= (ushort)mask;
				}
			}

			return value;
		}

		public static ushort ShiftSource(ushort current, ref ushort previous, int shift, bool descending)
		{
			shift &= 0x0F;
			if (shift == 0)
			{
				previous = current;
				return current;
			}

			var combined = descending
				? ((uint)current << 16) | previous
				: ((uint)previous << 16) | current;
			var value = descending
				? (ushort)(combined >> (16 - shift))
				: (ushort)(combined >> shift);
			previous = current;
			return value;
		}

		public static ushort ApplyFill(ushort value, bool exclusive, bool fillCarryIn)
		{
			ushort output = 0;
			var fillCarry = fillCarryIn;
			for (var bit = 0; bit < 16; bit++)
			{
				var mask = (ushort)(1 << bit);
				var input = (value & mask) != 0;
				if (exclusive)
				{
					if (fillCarry)
					{
						output |= mask;
					}

					if (input)
					{
						fillCarry = !fillCarry;
					}

					continue;
				}

				if (fillCarry || input)
				{
					output |= mask;
				}

				if (input)
				{
					fillCarry = !fillCarry;
				}
			}

			return output;
		}

		public static (int X, int Y) NextLineDelta(ushort octantBits, bool sign)
		{
			var sud = (octantBits & 0x0010) != 0;
			var sul = (octantBits & 0x0008) != 0;
			var aul = (octantBits & 0x0004) != 0;
			var x = 0;
			var y = 0;

			if (!sign)
			{
				if (sud)
				{
					y += sul ? -1 : 1;
				}
				else
				{
					x += sul ? -1 : 1;
				}
			}

			if (sud)
			{
				x += aul ? -1 : 1;
			}
			else
			{
				y += aul ? -1 : 1;
			}

			return (x, y);
		}
	}
}
