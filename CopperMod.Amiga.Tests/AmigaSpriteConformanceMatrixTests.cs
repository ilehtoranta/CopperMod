using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaSpriteConformanceMatrixTests
{
	private const int StandardX = AmigaConstants.PalLowResOverscanBorderX;
	private const int StandardY = AmigaConstants.PalLowResOverscanBorderY;
	private const uint SpriteListBase = 0x3000;
	private const uint CopperListBase = 0x4000;

	public static IEnumerable<object[]> ManualRegisterRows =>
		Enumerable.Range(0, 8).Select(sprite => new object[] { new SpriteIndexRow(sprite) });

	public static IEnumerable<object[]> DmaPointerRows =>
		Enumerable.Range(0, 8).Select(sprite => new object[] { new SpriteIndexRow(sprite) });

	public static IEnumerable<object[]> SingleSpritePaletteRows
	{
		get
		{
			for (var sprite = 0; sprite < 8; sprite++)
			{
				for (var pixel = 1; pixel <= 3; pixel++)
				{
					yield return new object[] { new SingleSpritePaletteRow(sprite, pixel) };
				}
			}
		}
	}

	public static IEnumerable<object[]> SpritePixelRows
	{
		get
		{
			for (var bitOffset = 0; bitOffset < 16; bitOffset++)
			{
				for (var pixel = 0; pixel <= 3; pixel++)
				{
					yield return new object[] { new SpritePixelRow(bitOffset, pixel) };
				}
			}
		}
	}

	public static IEnumerable<object[]> AttachedSpritePaletteRows =>
		Enumerable.Range(1, 15).Select(pixel => new object[] { new AttachedSpritePaletteRow(pixel) });

	public static IEnumerable<object[]> PositionRows
	{
		get
		{
			yield return new object[] { new PositionRow("even horizontal start", StandardX, StandardY, 1) };
			yield return new object[] { new PositionRow("odd horizontal start from SPRxCTL bit 0", StandardX + 1, StandardY + 1, 1) };
			yield return new object[] { new PositionRow("VSTART high bit from SPRxCTL", StandardX, 230, 4) };
			yield return new object[] { new PositionRow("last visible standard PAL display row", StandardX, AmigaConstants.PalLowResOverscanBorderY + AmigaConstants.PalLowResStandardHeight - 1, 1) };
		}
	}

	public static IEnumerable<object[]> DmaEnableRows
	{
		get
		{
			yield return new object[] { new DmaEnableRow("DMA disabled", 0x0000, false) };
			yield return new object[] { new DmaEnableRow("master only", 0x8200, false) };
			yield return new object[] { new DmaEnableRow("sprite only", 0x8020, false) };
			yield return new object[] { new DmaEnableRow("master and sprite", 0x8220, true) };
		}
	}

	public static IEnumerable<object[]> DmaHeightRows =>
		Enumerable.Range(1, 4).Select(height => new object[] { new DmaHeightRow(height) });

	public static IEnumerable<object[]> SpritePriorityRows
	{
		get
		{
			for (var front = 0; front < 8; front++)
			{
				for (var back = front + 1; back < 8; back++)
				{
					yield return new object[] { new SpritePriorityRow(front, back) };
				}
			}
		}
	}

	private static IEnumerable<SpriteConformanceRow> MatrixRows
	{
		get
		{
			foreach (var row in ManualRegisterRows)
			{
				yield return SpriteConformanceRow.Executable("manual-registers", $"SPR{((SpriteIndexRow)row[0]).Sprite}POS/CTL/DATA/DATB");
			}

			foreach (var row in DmaPointerRows)
			{
				yield return SpriteConformanceRow.Executable("dma-pointers", $"SPR{((SpriteIndexRow)row[0]).Sprite}PTH/PTL");
			}
			yield return SpriteConformanceRow.Executable("dma-pointers", "SPRxPT address zero is valid");
			yield return SpriteConformanceRow.Executable("dma-pointers", "SPRxPT address zero terminator");

			foreach (var row in SingleSpritePaletteRows)
			{
				var paletteRow = (SingleSpritePaletteRow)row[0];
				yield return SpriteConformanceRow.Executable("single-sprite-colors", $"sprite {paletteRow.Sprite} pixel {paletteRow.Pixel}");
			}

			foreach (var row in SpritePixelRows)
			{
				var pixelRow = (SpritePixelRow)row[0];
				yield return SpriteConformanceRow.Executable("sprite-data-bits", $"bit offset {pixelRow.BitOffset} pixel {pixelRow.Pixel}");
			}

			foreach (var row in AttachedSpritePaletteRows)
			{
				yield return SpriteConformanceRow.Executable("attached-colors", $"attached pixel {((AttachedSpritePaletteRow)row[0]).Pixel}");
			}

			foreach (var row in PositionRows)
			{
				yield return SpriteConformanceRow.Executable("position-control", ((PositionRow)row[0]).Name);
			}

			foreach (var row in DmaEnableRows)
			{
				yield return SpriteConformanceRow.Executable("dma-enable", ((DmaEnableRow)row[0]).Name);
			}

			foreach (var row in DmaHeightRows)
			{
				yield return SpriteConformanceRow.Executable("dma-list", $"height {((DmaHeightRow)row[0]).Height}");
			}

			foreach (var row in SpritePriorityRows)
			{
				var priorityRow = (SpritePriorityRow)row[0];
				yield return SpriteConformanceRow.Executable("sprite-priority", $"sprite {priorityRow.FrontSprite} over {priorityRow.BackSprite}");
			}

			yield return SpriteConformanceRow.Executable("display-window", "left clip");
			yield return SpriteConformanceRow.Executable("display-window", "right clip");
			yield return SpriteConformanceRow.Executable("display-scaling", "low-resolution sprite expands in high-resolution framebuffer");
			yield return SpriteConformanceRow.Executable("manual-control", "DATB alone does not arm");
			yield return SpriteConformanceRow.Executable("manual-control", "DATB before DATAA arms both words");
			yield return SpriteConformanceRow.Executable("manual-control", "manual sprite repeats on following scan lines until disabled");
			yield return SpriteConformanceRow.Executable("manual-control", "zero-height control does not render stale manual data");
			yield return SpriteConformanceRow.Executable("manual-control", "SPRxCTL disarms");
			yield return SpriteConformanceRow.Executable("manual-control", "SPRxPOS can move armed sprite");
			yield return SpriteConformanceRow.Executable("dma-list", "zero control words terminate");
			yield return SpriteConformanceRow.Executable("dma-list", "live DMA SPRxCTL terminator disarms manual state");
			yield return SpriteConformanceRow.Executable("dma-list", "DMA terminator prevents stale manual state after DMA is disabled");
			yield return SpriteConformanceRow.Executable("dma-list", "zero height control block terminates");
			yield return SpriteConformanceRow.Executable("dma-list", "multiple control blocks reuse a channel");
			yield return SpriteConformanceRow.Executable("dma-pointers", "SPRxPTL bit 0 ignored");
			yield return SpriteConformanceRow.Executable("dma-timing", "extra-wide playfield fetches can consume late sprite DMA slots");
			yield return SpriteConformanceRow.Executable("dma-timing", "sprite DMA latch is consumed after granted data fetch");
			yield return SpriteConformanceRow.Executable("dma-timing", "live DMA sprite archive carries stationary command across missed capture frame");
			yield return SpriteConformanceRow.Executable("dma-timing", "live DMA sprite archive does not carry across captured terminator");
			yield return SpriteConformanceRow.Executable("dma-timing", "live DMA sprite archive does not carry stale command after control block rewrite");
			yield return SpriteConformanceRow.Executable("dma-timing", "live DMA pending sprite pointer rewrite replaces previous pending X");
			yield return SpriteConformanceRow.Executable("attached-colors", "odd attached sprite with transparent or missing even partner");
			yield return SpriteConformanceRow.Executable("dma-list", "hardware one-line gap requirement between reused sprite images");
			yield return SpriteConformanceRow.Executable("undocumented-ocs", "BPLxDAT latch enables sprites outside normal bitplane area");
			yield return SpriteConformanceRow.Executable("undocumented-ocs", "DDFSTRT sprite-slot stealing");
			yield return SpriteConformanceRow.Executable("undocumented-ocs", "DDFSTRT denied DATB latch reuse");
			yield return SpriteConformanceRow.Pending("undocumented-ocs", "sprite vertical stop and previous-line data edge cases", "Requires tighter sprite DMA slot and shift-register timing.");
			yield return SpriteConformanceRow.Pending("undocumented-ocs", "DDFSTRT refresh pointer conflicts", "Thread-derived refresh corruption needs exact cycle/pixel reproduction before implementation.");
		}
	}

	[Fact]
	public void SpriteConformanceMatrixCoversHrmFeatureGroups()
	{
		// Reference: CopperMod.Amiga/References/Commodore_Amiga_Hardware_Reference_Manual_2nd.pdf,
		// Chapter 4 "Sprite Hardware", especially Tables 4-1, 4-3, 4-6, 4-7 and the SPRxPOS/SPRxCTL bit layout.
		var rows = MatrixRows.ToArray();
		var groups = rows.Select(row => row.Group).Distinct().Order().ToArray();

		Assert.Equal(
			new[]
			{
				"attached-colors",
				"display-scaling",
				"display-window",
				"dma-enable",
				"dma-list",
				"dma-pointers",
				"dma-timing",
				"manual-control",
				"manual-registers",
				"position-control",
				"single-sprite-colors",
				"sprite-data-bits",
				"sprite-priority",
				"undocumented-ocs",
			},
			groups);
		Assert.Equal(8, ManualRegisterRows.Count());
		Assert.Equal(8, DmaPointerRows.Count());
		Assert.Equal(24, SingleSpritePaletteRows.Count());
		Assert.Equal(64, SpritePixelRows.Count());
		Assert.Equal(15, AttachedSpritePaletteRows.Count());
		Assert.Equal(4, DmaEnableRows.Count());
		Assert.Equal(4, DmaHeightRows.Count());
		Assert.Equal(28, SpritePriorityRows.Count());
		Assert.True(rows.Count(row => row.Status == SpriteRowStatus.Executable) >= 162);
	}

	[Fact]
	public void SpriteConformancePendingRowsAreDocumented()
	{
		var pendingRows = MatrixRows.Where(row => row.Status == SpriteRowStatus.Pending).ToArray();

		Assert.All(pendingRows, row => Assert.False(string.IsNullOrWhiteSpace(row.Reason)));
	}

	[Theory]
	[MemberData(nameof(ManualRegisterRows))]
	public void ManualSpriteRegisterSetSelectsSpriteChannel(object rowObject)
	{
		var row = Assert.IsType<SpriteIndexRow>(rowObject);
		var bus = new AmigaBus();
		var colorIndex = SingleSpriteColorIndex(row.Sprite, 1);
		SetColor(bus, colorIndex, 0x0F00);
		WriteManualSprite(bus, row.Sprite, StandardX + row.Sprite, StandardY, 1, 0x8000, 0x0000);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX + row.Sprite, StandardY));
	}

	[Theory]
	[MemberData(nameof(DmaPointerRows))]
	public void SpriteDmaPointerRegisterSetSelectsSpriteChannel(object rowObject)
	{
		var row = Assert.IsType<SpriteIndexRow>(rowObject);
		var bus = new AmigaBus();
		var address = SpriteListBase + (uint)(row.Sprite * 0x40);
		var colorIndex = SingleSpriteColorIndex(row.Sprite, 1);
		SetColor(bus, colorIndex, 0x0F00);
		EnableSpriteDma(bus, 0x8220);
		WriteSpriteDmaBlock(bus, address, StandardX + row.Sprite, StandardY, 1, 0x8000, 0x0000);
		SetSpritePointer(bus, row.Sprite, address);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX + row.Sprite, StandardY));
	}

	[Fact]
	public void SpriteDmaPointerZeroIsValidChipRamAddress()
	{
		var bus = new AmigaBus();
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		EnableSpriteDma(bus, 0x8220);
		WriteSpriteDmaBlock(bus, 0, StandardX, StandardY, 1, 0x8000, 0x0000);
		SetSpritePointer(bus, sprite: 0, 0);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX, StandardY));
	}

	[Fact]
	public void SpriteDmaPointerZeroReadsTerminatorWithoutOutput()
	{
		var bus = new AmigaBus();
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		EnableSpriteDma(bus, 0x8220);
		WriteChipWord(bus, 0, 0);
		WriteChipWord(bus, 2, 0);
		SetSpritePointer(bus, sprite: 0, 0);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0), Pixel(frame, StandardX, StandardY));
		Assert.Equal(0, bus.Display.CaptureSnapshot().LastSpriteNonZeroPixels);
	}

	[Theory]
	[MemberData(nameof(SingleSpritePaletteRows))]
	public void SingleSpritesUseHrmPaletteGroups(object rowObject)
	{
		var row = Assert.IsType<SingleSpritePaletteRow>(rowObject);
		var bus = new AmigaBus();
		var colorIndex = SingleSpriteColorIndex(row.Sprite, row.Pixel);
		var color = UniqueColor(colorIndex);
		var (dataA, dataB) = DataWordsForPixel(row.Pixel, bitOffset: 0);
		SetColor(bus, colorIndex, color);
		WriteManualSprite(bus, row.Sprite, StandardX, StandardY, 1, dataA, dataB);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(color), Pixel(frame, StandardX, StandardY));
	}

	[Theory]
	[MemberData(nameof(SpritePixelRows))]
	public void SpriteDataWordsShiftMostSignificantBitFirstAndZeroIsTransparent(object rowObject)
	{
		var row = Assert.IsType<SpritePixelRow>(rowObject);
		var bus = new AmigaBus();
		var colorIndex = row.Pixel == 0 ? 0 : SingleSpriteColorIndex(sprite: 0, row.Pixel);
		var color = row.Pixel == 0 ? (ushort)0x000F : UniqueColor(colorIndex);
		var (dataA, dataB) = DataWordsForPixel(row.Pixel, row.BitOffset);
		SetColor(bus, 0, 0x000F);
		if (row.Pixel != 0)
		{
			SetColor(bus, colorIndex, color);
		}

		WriteManualSprite(bus, sprite: 0, StandardX, StandardY, 1, dataA, dataB);
		var frame = RenderLowResFrame(bus);
		var expected = ToBgra(color);

		Assert.Equal(expected, Pixel(frame, StandardX + row.BitOffset, StandardY));
		var snapshot = bus.Display.CaptureSnapshot();
		if (row.Pixel == 0)
		{
			Assert.Equal(0, snapshot.LastSpriteNonZeroPixels);
		}
		else
		{
			Assert.True(snapshot.LastSpriteNonZeroPixels > 1);
		}
	}

	[Theory]
	[MemberData(nameof(AttachedSpritePaletteRows))]
	public void AttachedSpritePairsUseFourBitColorRegisterSelection(object rowObject)
	{
		var row = Assert.IsType<AttachedSpritePaletteRow>(rowObject);
		var bus = new AmigaBus();
		var colorIndex = 16 + row.Pixel;
		var color = UniqueColor(colorIndex);
		var (evenPixel, oddPixel) = (row.Pixel & 0x03, row.Pixel >> 2);
		var (evenDataA, evenDataB) = DataWordsForPixel(evenPixel, bitOffset: 0);
		var (oddDataA, oddDataB) = DataWordsForPixel(oddPixel, bitOffset: 0);
		SetColor(bus, colorIndex, color);
		WriteManualSprite(bus, sprite: 0, StandardX, StandardY, 1, evenDataA, evenDataB);
		WriteManualSprite(bus, sprite: 1, StandardX, StandardY, 1, oddDataA, oddDataB, attached: true);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(color), Pixel(frame, StandardX, StandardY));
	}

	[Fact]
	public void AttachedOddSpriteWithoutEvenPartnerDoesNotRender()
	{
		var bus = new AmigaBus();
		SetColor(bus, 20, 0x0F00);
		WriteManualSprite(bus, sprite: 1, StandardX, StandardY, 1, 0x8000, 0x0000, attached: true);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0), Pixel(frame, StandardX, StandardY));
	}

	[Fact]
	public void EvenSpriteAttachBitAloneIsIgnoredAndRendersStandalone()
	{
		var bus = new AmigaBus();
		SetColor(bus, 17, 0x0F00);
		WriteManualSprite(bus, sprite: 0, StandardX, StandardY, 1, 0x8000, 0x0000, attached: true);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX, StandardY));
	}

	[Fact]
	public void EvenSpriteAttachBitDoesNotAttachPairWithoutOddAttachBit()
	{
		var bus = new AmigaBus();
		SetColor(bus, 17, 0x00F0);
		SetColor(bus, 21, 0x0F00);
		WriteManualSprite(bus, sprite: 0, StandardX, StandardY, 1, 0x8000, 0x0000, attached: true);
		WriteManualSprite(bus, sprite: 1, StandardX, StandardY, 1, 0x8000, 0x0000);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0x00F0), Pixel(frame, StandardX, StandardY));
	}

	[Fact]
	public void AttachedOddSpriteWithTransparentEvenPartnerUsesHighColorBits()
	{
		var bus = new AmigaBus();
		SetColor(bus, 20, 0x0F00);
		WriteManualSprite(bus, sprite: 0, StandardX, StandardY, 1, 0x0000, 0x0000);
		WriteManualSprite(bus, sprite: 1, StandardX, StandardY, 1, 0x8000, 0x0000, attached: true);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX, StandardY));
	}

	[Fact]
	public void AttachedManualSpriteRepeatsFollowingScanLinesUntilCtlDisarmsIt()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		const ushort color = 0x0F00;
		SetColor(bus, 21, color);
		var (evenPos, evenCtl) = EncodeSpritePosition(StandardX, StandardY, 1);
		var (oddPos, oddCtl) = EncodeSpritePosition(StandardX, StandardY, 1, attached: true);
		var armCycle = RowCycle(StandardY);
		var disarmCycle = RowCycle(StandardY + 2);
		bus.Display.ScheduleWrite(armCycle, 0x140, evenPos);
		bus.Display.ScheduleWrite(armCycle, 0x142, evenCtl);
		bus.Display.ScheduleWrite(armCycle, 0x146, 0x0000);
		bus.Display.ScheduleWrite(armCycle, 0x144, 0x8000);
		bus.Display.ScheduleWrite(armCycle, 0x148, oddPos);
		bus.Display.ScheduleWrite(armCycle, 0x14A, oddCtl);
		bus.Display.ScheduleWrite(armCycle, 0x14E, 0x0000);
		bus.Display.ScheduleWrite(armCycle, 0x14C, 0x8000);
		bus.Display.ScheduleWrite(disarmCycle, 0x142, evenCtl);
		bus.Display.ScheduleWrite(disarmCycle, 0x14A, oddCtl);
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

		bus.AdvanceDmaTo(FrameCycles());
		bus.Display.RenderFrame(frame, 0, FrameCycles());

		Assert.Equal(ToBgra(color), Pixel(frame, StandardX, StandardY));
		Assert.Equal(ToBgra(color), Pixel(frame, StandardX, StandardY + 1));
		Assert.Equal(ToBgra(0), Pixel(frame, StandardX, StandardY + 2));
	}

	[Fact]
	public void HorizontallySeparatedAttachedSpritesUseTheirOwnPixelPositions()
	{
		var bus = new AmigaBus();
		SetColor(bus, 17, 0x0F00);
		SetColor(bus, 20, 0x00F0);
		SetColor(bus, 21, 0x000F);
		WriteManualSprite(bus, sprite: 0, StandardX, StandardY, 1, 0x8000, 0x0000);
		WriteManualSprite(bus, sprite: 1, StandardX + 8, StandardY, 1, 0x8000, 0x0000, attached: true);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX, StandardY));
		Assert.Equal(ToBgra(0x00F0), Pixel(frame, StandardX + 8, StandardY));
		Assert.NotEqual(ToBgra(0x000F), Pixel(frame, StandardX, StandardY));
	}

	[Theory]
	[MemberData(nameof(PositionRows))]
	public void SpritePositionAndControlBitsUseHrmCoordinateLayout(object rowObject)
	{
		var row = Assert.IsType<PositionRow>(rowObject);
		var bus = new AmigaBus();
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		WriteManualSprite(bus, sprite: 0, row.X, row.Y, row.Height, 0x8000, 0x0000);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0x0F00), Pixel(frame, row.X, row.Y));
	}

	[Theory]
	[MemberData(nameof(DmaEnableRows))]
	public void SpriteDmaRequiresMasterAndSpriteDmaBits(object rowObject)
	{
		var row = Assert.IsType<DmaEnableRow>(rowObject);
		var bus = new AmigaBus();
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		EnableSpriteDma(bus, row.Dmacon);
		WriteSpriteDmaBlock(bus, SpriteListBase, StandardX, StandardY, 1, 0x8000, 0x0000);
		SetSpritePointer(bus, sprite: 0, SpriteListBase);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(row.ExpectedVisible ? ToBgra(0x0F00) : ToBgra(0), Pixel(frame, StandardX, StandardY));
	}

	[Theory]
	[MemberData(nameof(DmaHeightRows))]
	public void SpriteDmaListHeightIsVstopMinusVstart(object rowObject)
	{
		var row = Assert.IsType<DmaHeightRow>(rowObject);
		var bus = new AmigaBus();
		EnableSpriteDma(bus, 0x8220);
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		SetColor(bus, SingleSpriteColorIndex(0, 2), 0x00F0);
		SetColor(bus, SingleSpriteColorIndex(0, 3), 0x000F);
		var (pos, ctl) = EncodeSpritePosition(StandardX, StandardY, row.Height);
		WriteChipWord(bus, SpriteListBase, pos);
		WriteChipWord(bus, SpriteListBase + 2, ctl);
		for (var y = 0; y < row.Height; y++)
		{
			var pixel = (y % 3) + 1;
			var (dataA, dataB) = DataWordsForPixel(pixel, bitOffset: 0);
			WriteChipWord(bus, SpriteListBase + 4 + (uint)(y * 4), dataA);
			WriteChipWord(bus, SpriteListBase + 6 + (uint)(y * 4), dataB);
		}

		var terminator = SpriteListBase + 4 + (uint)(row.Height * 4);
		WriteChipWord(bus, terminator, 0);
		WriteChipWord(bus, terminator + 2, 0);
		SetSpritePointer(bus, sprite: 0, SpriteListBase);
		var frame = RenderLowResFrame(bus);

		for (var y = 0; y < row.Height; y++)
		{
			var pixel = (y % 3) + 1;
			Assert.Equal(ToBgra(ColorAt(bus, SingleSpriteColorIndex(0, pixel))), Pixel(frame, StandardX, StandardY + y));
		}

		Assert.Equal(ToBgra(0), Pixel(frame, StandardX, StandardY + row.Height));
	}

	[Theory]
	[MemberData(nameof(SpritePriorityRows))]
	public void LowerNumberedSpritesHaveFixedPriorityOverHigherNumberedSprites(object rowObject)
	{
		var row = Assert.IsType<SpritePriorityRow>(rowObject);
		var bus = new AmigaBus();
		var frontPixel = 1;
		var backPixel = row.FrontSprite / 2 == row.BackSprite / 2 ? 2 : 1;
		SetColor(bus, SingleSpriteColorIndex(row.FrontSprite, frontPixel), 0x0F00);
		SetColor(bus, SingleSpriteColorIndex(row.BackSprite, backPixel), 0x00F0);
		var (frontDataA, frontDataB) = DataWordsForPixel(frontPixel, bitOffset: 0);
		var (backDataA, backDataB) = DataWordsForPixel(backPixel, bitOffset: 0);
		WriteManualSprite(bus, row.BackSprite, StandardX, StandardY, 1, backDataA, backDataB);
		WriteManualSprite(bus, row.FrontSprite, StandardX, StandardY, 1, frontDataA, frontDataB);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX, StandardY));
	}

	[Fact]
	public void SpriteIsClippedByDisplayWindowLeftEdge()
	{
		var bus = new AmigaBus();
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		bus.WriteWord(0x00DFF08E, 0x2C91);
		bus.WriteWord(0x00DFF090, 0x2CC1);
		WriteManualSprite(bus, sprite: 0, StandardX + 8, StandardY, 1, 0xFFFF, 0x0000);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0), Pixel(frame, StandardX + 8, StandardY));
		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX + 16, StandardY));
	}

	[Fact]
	public void SpriteIsClippedByDisplayWindowRightEdge()
	{
		var bus = new AmigaBus();
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		WriteManualSprite(bus, sprite: 0, StandardX + 312, StandardY, 1, 0xFFFF, 0x0000);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX + 319, StandardY));
		Assert.Equal(ToBgra(0), Pixel(frame, StandardX + 320, StandardY));
	}

	[Fact]
	public void SpriteLowResolutionPixelsExpandInHighResolutionFramebuffers()
	{
		var bus = new AmigaBus();
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		WriteManualSprite(bus, sprite: 0, StandardX, StandardY, 1, 0x8000, 0x0000);
		var frame = new uint[AmigaConstants.PalHighResWidth * AmigaConstants.PalHighResHeight];

		bus.Display.RenderFrame(frame);

		Assert.Equal(ToBgra(0x0F00), Pixel(frame, AmigaConstants.PalHighResWidth, StandardX * 2, StandardY * 2));
		Assert.Equal(ToBgra(0x0F00), Pixel(frame, AmigaConstants.PalHighResWidth, (StandardX * 2) + 1, StandardY * 2));
		Assert.Equal(ToBgra(0x0F00), Pixel(frame, AmigaConstants.PalHighResWidth, StandardX * 2, (StandardY * 2) + 1));
	}

	[Fact]
	public void ManualSpriteDatbWriteAloneDoesNotArmSpriteOutput()
	{
		var bus = new AmigaBus();
		SetColor(bus, SingleSpriteColorIndex(0, 2), 0x0F00);
		var (pos, ctl) = EncodeSpritePosition(StandardX, StandardY, 1);
		bus.WriteWord(0x00DFF140, pos);
		bus.WriteWord(0x00DFF142, ctl);
		bus.WriteWord(0x00DFF146, 0x8000);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0), Pixel(frame, StandardX, StandardY));
	}

	[Fact]
	public void ManualSpriteDatbBeforeDataaArmsBothDataWords()
	{
		var bus = new AmigaBus();
		SetColor(bus, SingleSpriteColorIndex(0, 3), 0x0F00);
		var (pos, ctl) = EncodeSpritePosition(StandardX, StandardY, 1);
		bus.WriteWord(0x00DFF140, pos);
		bus.WriteWord(0x00DFF142, ctl);
		bus.WriteWord(0x00DFF146, 0x8000);
		bus.WriteWord(0x00DFF144, 0x8000);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX, StandardY));
	}

	[Fact]
	public void ManualSpriteZeroHeightControlDoesNotRenderStaleData()
	{
		var bus = new AmigaBus();
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		var (pos, ctl) = EncodeSpritePosition(StandardX, StandardY, 0);
		bus.WriteWord(0x00DFF140, pos);
		bus.WriteWord(0x00DFF142, ctl);
		bus.WriteWord(0x00DFF146, 0x0000);
		bus.WriteWord(0x00DFF144, 0x8000);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0), Pixel(frame, StandardX, StandardY));
		Assert.Equal(0, bus.Display.CaptureSnapshot().LastSpriteNonZeroPixels);
	}

	[Fact]
	public void LiveTimelineManualSpriteZeroHeightControlDoesNotRenderStaleData()
	{
		var presentationBus = CreateDisplayComponentBus();
		var liveBus = new AmigaBus(enableLiveAgnusDma: true);
		foreach (var bus in new[] { presentationBus, liveBus })
		{
			SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
			var (pos, ctl) = EncodeSpritePosition(StandardX, StandardY, 0);
			var armCycle = RowCycle(StandardY);
			bus.Display.ScheduleWrite(armCycle, 0x140, pos);
			bus.Display.ScheduleWrite(armCycle, 0x142, ctl);
			bus.Display.ScheduleWrite(armCycle, 0x146, 0x0000);
			bus.Display.ScheduleWrite(armCycle, 0x144, 0x8000);
		}

		var expected = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
		var actual = new uint[expected.Length];
		presentationBus.Display.RenderFrame(expected, 0, FrameCycles());
		liveBus.AdvanceDmaTo(FrameCycles());
		liveBus.Display.RenderFrame(actual, 0, FrameCycles());

		Assert.Equal(expected, actual);
		Assert.Equal(ToBgra(0), Pixel(actual, StandardX, StandardY));
		Assert.Equal(0, liveBus.Display.CaptureSnapshot().LastSpriteNonZeroPixels);
	}

	[Fact]
	public void ManualSpriteRepeatsFollowingScanLinesUntilCtlDisarmsIt()
	{
		var bus = CreateDisplayComponentBus();
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		var (pos, ctl) = EncodeSpritePosition(StandardX, StandardY, 1);
		var armCycle = RowCycle(StandardY);
		var disarmCycle = RowCycle(StandardY + 2);
		bus.Display.ScheduleWrite(armCycle, 0x140, pos);
		bus.Display.ScheduleWrite(armCycle, 0x142, ctl);
		bus.Display.ScheduleWrite(armCycle, 0x146, 0x0000);
		bus.Display.ScheduleWrite(armCycle, 0x144, 0x8000);
		bus.Display.ScheduleWrite(disarmCycle, 0x142, ctl);
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

		bus.Display.RenderFrame(frame, 0, FrameCycles());

		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX, StandardY));
		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX, StandardY + 1));
		Assert.Equal(ToBgra(0), Pixel(frame, StandardX, StandardY + 2));
	}

	[Fact]
	public void ManualSpriteDataWriteAfterHorizontalMatchStartsOnNextScanLine()
	{
		var bus = CreateDisplayComponentBus();
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		var (pos, ctl) = EncodeSpritePosition(StandardX, StandardY, 1);
		var armCycle = RowCycle(StandardY);
		var lateDataCycle = AfterSpriteMatchCycle(StandardY, StandardX);
		var disarmCycle = RowCycle(StandardY + 2);
		bus.Display.ScheduleWrite(armCycle, 0x140, pos);
		bus.Display.ScheduleWrite(armCycle, 0x142, ctl);
		bus.Display.ScheduleWrite(lateDataCycle, 0x146, 0x0000);
		bus.Display.ScheduleWrite(lateDataCycle, 0x144, 0x8000);
		bus.Display.ScheduleWrite(disarmCycle, 0x142, ctl);
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

		bus.Display.RenderFrame(frame, 0, FrameCycles());

		Assert.Equal(ToBgra(0), Pixel(frame, StandardX, StandardY));
		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX, StandardY + 1));
		Assert.Equal(ToBgra(0), Pixel(frame, StandardX, StandardY + 2));
	}

	[Fact]
	public void ManualSpriteCtlWriteDisarmsSpriteOutput()
	{
		var bus = new AmigaBus();
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		var (pos, ctl) = EncodeSpritePosition(StandardX, StandardY, 1);
		bus.WriteWord(0x00DFF140, pos);
		bus.WriteWord(0x00DFF142, ctl);
		bus.WriteWord(0x00DFF144, 0x8000);
		bus.WriteWord(0x00DFF142, ctl);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0), Pixel(frame, StandardX, StandardY));
	}

	[Fact]
	public void ManualSpritePosWriteMovesArmedSpriteHorizontallyByEvenPixels()
	{
		var bus = new AmigaBus();
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		var (firstPos, ctl) = EncodeSpritePosition(StandardX, StandardY, 1);
		var (secondPos, _) = EncodeSpritePosition(StandardX + 4, StandardY, 1);
		bus.WriteWord(0x00DFF140, firstPos);
		bus.WriteWord(0x00DFF142, ctl);
		bus.WriteWord(0x00DFF144, 0x8000);
		bus.WriteWord(0x00DFF140, secondPos);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0), Pixel(frame, StandardX, StandardY));
		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX + 4, StandardY));
	}

	[Fact]
	public void SpriteDmaZeroControlWordsTerminateListWithoutOutput()
	{
		var bus = new AmigaBus();
		EnableSpriteDma(bus, 0x8220);
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		WriteChipWord(bus, SpriteListBase, 0);
		WriteChipWord(bus, SpriteListBase + 2, 0);
		SetSpritePointer(bus, sprite: 0, SpriteListBase);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0), Pixel(frame, StandardX, StandardY));
	}

	[Fact]
	public void LiveSpriteDmaTerminatorSuppressesStaleManualSpriteState()
	{
		var bus = new AmigaBus();
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		WriteManualSprite(bus, sprite: 0, StandardX, StandardY, 1, 0x8000, 0x0000);
		var manualFrame = RenderLowResFrame(bus);
		Assert.Equal(ToBgra(0x0F00), Pixel(manualFrame, StandardX, StandardY));

		WriteChipWord(bus, SpriteListBase, 0);
		WriteChipWord(bus, SpriteListBase + 2, 0);
		SetSpritePointer(bus, sprite: 0, SpriteListBase);
		EnableSpriteDma(bus, 0x8220);
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

		bus.Display.RenderFrame(frame, 0, FrameCycles());

		Assert.Equal(ToBgra(0), Pixel(frame, StandardX, StandardY));
		Assert.Equal(0, bus.Display.CaptureSnapshot().LastSpriteNonZeroPixels);
	}

	[Fact]
	public void LiveDmaTimelineSpriteTerminatorDisarmsInitialManualSpriteState()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		WriteManualSprite(bus, sprite: 0, StandardX, StandardY, 1, 0x8000, 0x0000);
		WriteChipWord(bus, SpriteListBase, 0);
		WriteChipWord(bus, SpriteListBase + 2, 0);
		SetSpritePointer(bus, sprite: 0, SpriteListBase);
		EnableSpriteDma(bus, 0x8220);
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

		bus.AdvanceDmaTo(FrameCycles());
		bus.Display.RenderFrame(frame, 0, FrameCycles());

		Assert.Equal(ToBgra(0), Pixel(frame, StandardX, StandardY));
		Assert.Equal(0, bus.Display.CaptureSnapshot().LastSpriteNonZeroPixels);
	}

	[Fact]
	public void SpriteDmaTerminatorPreventsStaleManualStateAfterDmaIsDisabled()
	{
		var bus = new AmigaBus();
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		WriteManualSprite(bus, sprite: 0, StandardX, StandardY, 1, 0x8000, 0x0000);
		var manualFrame = RenderLowResFrame(bus);
		Assert.Equal(ToBgra(0x0F00), Pixel(manualFrame, StandardX, StandardY));

		WriteChipWord(bus, SpriteListBase, 0);
		WriteChipWord(bus, SpriteListBase + 2, 0);
		SetSpritePointer(bus, sprite: 0, SpriteListBase);
		EnableSpriteDma(bus, 0x8220);
		var terminatedFrame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
		bus.Display.RenderFrame(terminatedFrame, 0, FrameCycles());
		Assert.Equal(ToBgra(0), Pixel(terminatedFrame, StandardX, StandardY));

		EnableSpriteDma(bus, 0x8200);
		var afterDmaDisabledFrame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0), Pixel(afterDmaDisabledFrame, StandardX, StandardY));
		Assert.Equal(0, bus.Display.CaptureSnapshot().LastSpriteNonZeroPixels);
	}

	[Fact]
	public void SpriteDmaZeroHeightControlBlockTerminatesListWithoutOutput()
	{
		var bus = new AmigaBus();
		EnableSpriteDma(bus, 0x8220);
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		var (pos, _) = EncodeSpritePosition(StandardX, StandardY, 1);
		var (_, ctl) = EncodeSpritePosition(StandardX, StandardY, 0);
		WriteChipWord(bus, SpriteListBase, pos);
		WriteChipWord(bus, SpriteListBase + 2, ctl);
		WriteChipWord(bus, SpriteListBase + 4, 0x8000);
		WriteChipWord(bus, SpriteListBase + 6, 0);
		SetSpritePointer(bus, sprite: 0, SpriteListBase);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0), Pixel(frame, StandardX, StandardY));
	}

	[Fact]
	public void SpriteDmaListCanReuseChannelWithMultipleControlBlocks()
	{
		var bus = new AmigaBus();
		EnableSpriteDma(bus, 0x8220);
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		SetColor(bus, SingleSpriteColorIndex(0, 2), 0x00F0);
		WriteSpriteDmaBlock(bus, SpriteListBase, StandardX, 30, 1, 0x8000, 0x0000, terminate: false);
		WriteSpriteDmaBlock(bus, SpriteListBase + 8, 40, 60, 1, 0x0000, 0x8000);
		SetSpritePointer(bus, sprite: 0, SpriteListBase);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX, 30));
		Assert.Equal(ToBgra(0x00F0), Pixel(frame, 40, 60));
	}

	[Fact]
	public void SpriteDmaPointerLowBitIsIgnored()
	{
		var bus = new AmigaBus();
		EnableSpriteDma(bus, 0x8220);
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		WriteSpriteDmaBlock(bus, SpriteListBase, StandardX, StandardY, 1, 0x8000, 0x0000);
		bus.WriteWord(0x00DFF120, (ushort)(SpriteListBase >> 16));
		bus.WriteWord(0x00DFF122, (ushort)(SpriteListBase | 1));
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX, StandardY));
	}

	[Fact]
	public void ExtraWidePlayfieldFetchStealsLateSpriteDmaSlots()
	{
		var bus = new AmigaBus();
		EnableSpriteDma(bus, 0x8220);
		bus.WriteWord(0x00DFF092, 0x0030);
		bus.WriteWord(0x00DFF094, 0x00D0);
		bus.WriteWord(0x00DFF100, 0x1000);
		SetColor(bus, SingleSpriteColorIndex(6, 1), 0x0F00);
		WriteSpriteDmaBlock(bus, SpriteListBase + 0x180, StandardX + 40, StandardY, 1, 0x8000, 0x0000);
		WriteSpriteDmaBlock(bus, SpriteListBase + 0x1C0, StandardX + 60, StandardY, 1, 0x8000, 0x0000);
		SetSpritePointer(bus, sprite: 6, SpriteListBase + 0x180);
		SetSpritePointer(bus, sprite: 7, SpriteListBase + 0x1C0);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX + 40, StandardY));
		Assert.Equal(ToBgra(0), Pixel(frame, StandardX + 60, StandardY));
	}

	[Fact]
	public void EarliestPlayfieldFetchStealsAllSpriteDmaSlots()
	{
		var bus = new AmigaBus();
		EnableSpriteDma(bus, 0x8220);
		bus.WriteWord(0x00DFF092, 0x0018);
		bus.WriteWord(0x00DFF094, 0x00D0);
		bus.WriteWord(0x00DFF100, 0x1000);
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		WriteSpriteDmaBlock(bus, SpriteListBase, StandardX, StandardY, 1, 0x8000, 0x0000);
		SetSpritePointer(bus, sprite: 0, SpriteListBase);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0), Pixel(frame, StandardX, StandardY));
	}

	[Fact]
	public void DdfStartAt18DeniesCompleteSpriteOutput()
	{
		var bus = CreateDisplayComponentBus();
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		WriteSpriteDmaBlock(bus, SpriteListBase, StandardX, StandardY, 1, 0x8000, 0x8000);
		bus.WriteWord(0x00DFF092, 0x0018);
		bus.WriteWord(0x00DFF094, 0x00D0);
		bus.WriteWord(0x00DFF100, 0x6000);
		EnableSpriteDma(bus, 0x8320);
		SetSpritePointer(bus, sprite: 0, SpriteListBase);
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

		bus.Display.RenderFrame(frame, 0, FrameCycles());

		Assert.Equal(ToBgra(0), Pixel(frame, StandardX, StandardY));
	}

	[Fact]
	public void DdfStartShrinkAfterDescriptorFetchReusesPreviousSpriteDatb()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		SetColor(bus, SingleSpriteColorIndex(0, 2), 0x00F0);
		WriteSpriteDmaRows(bus, SpriteListBase, StandardX, StandardY, 2, 0x0000, 0x0000);
		WriteChipWord(bus, SpriteListBase + 6, 0x8000);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x00D0);
		bus.WriteWord(0x00DFF100, 0x6000);
		EnableSpriteDma(bus, 0x8320);
		SetSpritePointer(bus, sprite: 0, SpriteListBase);
		bus.WriteWord(0x00DFF122, (ushort)SpriteListBase, RowCycle(StandardY - 1));
		bus.WriteWord(0x00DFF092, 0x0018, RowCycle(StandardY + 1) - AmigaConstants.A500PalCpuCyclesPerColorClock);
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

		bus.AdvanceDmaTo(FrameCycles());
		bus.Display.RenderFrame(frame, 0, FrameCycles());

		Assert.Equal(ToBgra(0x00F0), Pixel(frame, StandardX, StandardY));
		Assert.Equal(ToBgra(0x00F0), Pixel(frame, StandardX, StandardY + 1));
	}

	[Fact]
	public void Bpl1DatWriteEnablesSpriteVisibilityBeforeLateDdfStartAndRendersSpan()
	{
		var bus = CreateDisplayComponentBus();
		var spanX = OutputXForHorizontal(0x60);
		var spriteX = spanX + 20;
		SetColor(bus, 1, 0x0F00);
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x00F0);
		WriteManualSprite(bus, sprite: 0, spriteX, StandardY, 1, 0x8000, 0x0000);
		bus.WriteWord(0x00DFF092, 0x0080);
		bus.WriteWord(0x00DFF094, 0x00D0);
		bus.WriteWord(0x00DFF100, 0x1000);
		bus.WriteWord(0x00DFF110, 0x8000, OutputCycle(StandardY, 0x60));
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

		bus.Display.RenderFrame(frame, 0, FrameCycles());

		Assert.Equal(ToBgra(0x0F00), Pixel(frame, spanX, StandardY));
		Assert.Equal(ToBgra(0x00F0), Pixel(frame, spriteX, StandardY));
	}

	[Fact]
	public void SpriteBeforeLateDdfStartStaysHiddenWithoutBpl1DatLoad()
	{
		var bus = CreateDisplayComponentBus();
		var spriteX = OutputXForHorizontal(0x60) + 20;
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x00F0);
		WriteManualSprite(bus, sprite: 0, spriteX, StandardY, 1, 0x8000, 0x0000);
		bus.WriteWord(0x00DFF092, 0x0080);
		bus.WriteWord(0x00DFF094, 0x00D0);
		bus.WriteWord(0x00DFF100, 0x1000);
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

		bus.Display.RenderFrame(frame, 0, FrameCycles());

		Assert.Equal(ToBgra(0), Pixel(frame, spriteX, StandardY));
	}

	[Fact]
	public void EarlyPlayfieldFetchDoesNotHideManualSpriteRegisters()
	{
		var bus = new AmigaBus();
		bus.WriteWord(0x00DFF092, 0x0030);
		bus.WriteWord(0x00DFF094, 0x00D0);
		bus.WriteWord(0x00DFF100, 0x1000);
		SetColor(bus, SingleSpriteColorIndex(7, 1), 0x0F00);
		WriteManualSprite(bus, sprite: 7, StandardX, StandardY, 1, 0x8000, 0x0000);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX, StandardY));
	}

	[Fact]
	public void SpriteDmaRequiresOneLineGapBeforeReusedImage()
	{
		var bus = new AmigaBus();
		EnableSpriteDma(bus, 0x8220);
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		SetColor(bus, SingleSpriteColorIndex(0, 2), 0x00F0);
		var (firstPos, firstCtl) = EncodeSpritePosition(StandardX, StandardY, 1);
		var (secondPos, secondCtl) = EncodeSpritePosition(StandardX, StandardY + 1, 2);
		WriteChipWord(bus, SpriteListBase, firstPos);
		WriteChipWord(bus, SpriteListBase + 2, firstCtl);
		WriteChipWord(bus, SpriteListBase + 4, 0x8000);
		WriteChipWord(bus, SpriteListBase + 6, 0x0000);
		WriteChipWord(bus, SpriteListBase + 8, secondPos);
		WriteChipWord(bus, SpriteListBase + 10, secondCtl);
		WriteChipWord(bus, SpriteListBase + 12, 0x0000);
		WriteChipWord(bus, SpriteListBase + 14, 0x8000);
		WriteChipWord(bus, SpriteListBase + 16, 0x0000);
		WriteChipWord(bus, SpriteListBase + 18, 0x8000);
		WriteChipWord(bus, SpriteListBase + 20, 0);
		WriteChipWord(bus, SpriteListBase + 22, 0);
		SetSpritePointer(bus, sprite: 0, SpriteListBase);

		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX, StandardY));
		Assert.Equal(ToBgra(0), Pixel(frame, StandardX, StandardY + 1));
		Assert.Equal(ToBgra(0x00F0), Pixel(frame, StandardX, StandardY + 2));
	}

	[Fact]
	public void TimedSpriteDmaUsesBusSlotsAndRecordsMissedSlots()
	{
		var bus = CreateDisplayComponentBus();
		EnableSpriteDma(bus, 0x8220);
		bus.WriteWord(0x00DFF092, 0x0030);
		bus.WriteWord(0x00DFF094, 0x00D0);
		bus.WriteWord(0x00DFF100, 0x1000);
		SetColor(bus, SingleSpriteColorIndex(6, 1), 0x0F00);
		WriteSpriteDmaBlock(bus, SpriteListBase + 0x180, StandardX + 40, StandardY, 1, 0x8000, 0x0000);
		WriteSpriteDmaBlock(bus, SpriteListBase + 0x1C0, StandardX + 60, StandardY, 1, 0x8000, 0x0000);
		SetSpritePointer(bus, sprite: 6, SpriteListBase + 0x180);
		SetSpritePointer(bus, sprite: 7, SpriteListBase + 0x1C0);
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

		bus.Display.RenderFrame(frame, 0, FrameCycles());

		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX + 40, StandardY));
		Assert.Equal(ToBgra(0), Pixel(frame, StandardX + 60, StandardY));
		Assert.Contains(bus.BusAccesses, access => access.Request.Requester == AmigaBusRequester.Sprite);
		var snapshot = bus.Display.CaptureSnapshot();
		Assert.True(snapshot.LastSpriteDmaFetches > 0);
		Assert.True(snapshot.LastMissedSpriteDmaSlots > 0);
	}

	[Fact]
	public void SpriteDmaLatchIsConsumedAfterGrantedDataFetch()
	{
		var bus = CreateDisplayComponentBus();
		EnableSpriteDma(bus, 0x8220);
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		WriteSpriteDmaBlock(bus, SpriteListBase, StandardX, StandardY, 1, 0x8000, 0x0000);
		SetSpritePointer(bus, sprite: 0, SpriteListBase);
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

		bus.Display.RenderFrame(frame, 0, FrameCycles());

		var latch = GetPrivateField<object>(bus.Display, "_spriteDmaReadLatch");
		var hasValue = (bool)latch.GetType().GetProperty("HasValue")!.GetValue(latch)!;
		var snapshot = bus.Display.CaptureSnapshot();
		Assert.False(hasValue);
		Assert.True(snapshot.LastSpriteDmaFetches > 0);
		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX, StandardY));
	}

	[Fact]
	public void TimedRenderKeepsLiveSpriteDmaCommandsAfterFrameBoundaryOvershoot()
	{
		var bus = new AmigaBus();
		EnableSpriteDma(bus, 0x8220);
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		WriteSpriteDmaBlock(bus, SpriteListBase, StandardX, StandardY, 1, 0x8000, 0x0000);
		SetSpritePointer(bus, sprite: 0, SpriteListBase);
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

		bus.AdvanceDmaTo(FrameCycles() + 112);
		bus.Display.RenderFrame(frame, 0, FrameCycles());

		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX, StandardY));
	}

	[Fact]
	public void LiveDmaTimelineRendersSpriteDmaCommands()
	{
		var presentationBus = new AmigaBus(enableLiveAgnusDma: false);
		var liveBus = new AmigaBus(enableLiveAgnusDma: true);
		foreach (var bus in new[] { presentationBus, liveBus })
		{
			EnableSpriteDma(bus, 0x8220);
			SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
			WriteSpriteDmaBlock(bus, SpriteListBase, StandardX, StandardY, 1, 0x8000, 0x0000);
			SetSpritePointer(bus, sprite: 0, SpriteListBase);
		}

		var expected = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
		var actual = new uint[expected.Length];
		presentationBus.Display.RenderFrame(expected, 0, FrameCycles());
		liveBus.Display.RenderFrame(actual, 0, FrameCycles());

		var snapshot = liveBus.Display.CaptureSnapshot();
		Assert.Equal(Pixel(expected, StandardX, StandardY), Pixel(actual, StandardX, StandardY));
		Assert.Equal(ToBgra(0x0F00), Pixel(actual, StandardX, StandardY));
		Assert.True(snapshot.LastTimelineSpriteCommandCount > 0);
		Assert.Equal(0, snapshot.LastTimelineFallbackCount);
	}

	[Fact]
	public void LiveDmaTimelineRendersInitialManualSpriteRegisters()
	{
		var presentationBus = new AmigaBus(enableLiveAgnusDma: false);
		var liveBus = new AmigaBus(enableLiveAgnusDma: true);
		foreach (var bus in new[] { presentationBus, liveBus })
		{
			SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
			WriteManualSprite(bus, sprite: 0, StandardX, StandardY, 1, 0x8000, 0x0000);
		}

		var expected = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
		var actual = new uint[expected.Length];
		presentationBus.Display.RenderFrame(expected, 0, FrameCycles());
		liveBus.Display.RenderFrame(actual, 0, FrameCycles());

		var snapshot = liveBus.Display.CaptureSnapshot();
		Assert.Equal(Pixel(expected, StandardX, StandardY), Pixel(actual, StandardX, StandardY));
		Assert.Equal(ToBgra(0x0F00), Pixel(actual, StandardX, StandardY));
		Assert.True(snapshot.LastTimelineSpriteCommandCount > 0);
		Assert.Equal(0, snapshot.LastTimelineFallbackCount);
	}

	[Fact]
	public void LiveDmaArchivedTimelineRendersSpriteDmaCommands()
	{
		var presentationBus = new AmigaBus(enableLiveAgnusDma: false);
		var liveBus = new AmigaBus(enableLiveAgnusDma: true);
		foreach (var bus in new[] { presentationBus, liveBus })
		{
			EnableSpriteDma(bus, 0x8220);
			SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
			WriteSpriteDmaBlock(bus, SpriteListBase, StandardX, StandardY, 1, 0x8000, 0x0000);
			SetSpritePointer(bus, sprite: 0, SpriteListBase);
		}

		var expected = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
		var actual = new uint[expected.Length];
		presentationBus.Display.RenderFrame(expected, 0, FrameCycles());
		liveBus.AdvanceDmaTo(FrameCycles());
		liveBus.Display.RenderFrame(actual, 0, FrameCycles());

		var snapshot = liveBus.Display.CaptureSnapshot();
		Assert.Equal(Pixel(expected, StandardX, StandardY), Pixel(actual, StandardX, StandardY));
		Assert.Equal(ToBgra(0x0F00), Pixel(actual, StandardX, StandardY));
		Assert.True(snapshot.LastTimelineSpriteCommandCount > 0);
		Assert.Equal(0, snapshot.LastActiveTimelineFrameCount);
		Assert.Equal(1, snapshot.LastArchivedTimelineFrameCount);
		Assert.Equal(0, snapshot.LastTimelineFallbackCount);
	}

	[Fact]
	public void ArchivedSpriteFallbackUsesTimelineCompletedSpriteWords()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		EnableSpriteDma(bus, 0x8220);
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		WriteSpriteDmaRows(bus, SpriteListBase, StandardX, StandardY, 4, 0x8000, 0x0000);
		SetSpritePointer(bus, sprite: 0, SpriteListBase);
		bus.AdvanceDmaTo(FrameCycles() - 1);
		ClearPrivateArray(bus.Display, "_liveSpriteWordMasks");
		bus.AdvanceDmaTo(FrameCycles());
		SetPrivateField(bus.Display, "_archivedTimelineValid", false);
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

		bus.Display.RenderFrame(frame, 0, FrameCycles());

		for (var row = 0; row < 4; row++)
		{
			Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX, StandardY + row));
		}
	}

	[Fact]
	public void ArchivedSpriteFallbackCarriesStationaryCommandAcrossMissedCaptureFrame()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		EnableSpriteDma(bus, 0x8220);
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		WriteSpriteDmaRows(bus, SpriteListBase, StandardX, StandardY, 4, 0x8000, 0x0000);
		SetSpritePointer(bus, sprite: 0, SpriteListBase);
		bus.AdvanceDmaTo(FrameCycles());
		SetPrivateField(bus.Display, "_liveCapturedThroughCycle", (FrameCycles() * 2) - 1);
		InvokePrivateMethod(bus.Display, "ArchiveLiveSpriteFrameBeforeStarting", FrameCycles() * 2);
		Assert.Equal(1, GetPrivateCollectionCount(bus.Display, "_previousLiveSpriteFrameCommands"));
		SetPrivateField(bus.Display, "_archivedTimelineValid", false);
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

		bus.Display.RenderFrame(frame, FrameCycles(), FrameCycles() * 2);

		var snapshot = bus.Display.CaptureSnapshot();
		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX, StandardY));
		Assert.Equal(0, snapshot.LastSpriteDmaFetches);
		Assert.Equal(0, snapshot.LastMissedSpriteDmaSlots);
	}

	[Fact]
	public void ArchivedSpriteFallbackDoesNotCarryStationaryCommandAcrossCapturedTerminator()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		EnableSpriteDma(bus, 0x8220);
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		WriteSpriteDmaRows(bus, SpriteListBase, StandardX, StandardY, 4, 0x8000, 0x0000);
		SetSpritePointer(bus, sprite: 0, SpriteListBase);
		bus.AdvanceDmaTo(FrameCycles());
		WriteChipWord(bus, SpriteListBase, 0);
		WriteChipWord(bus, SpriteListBase + 2, 0);
		bus.AdvanceDmaTo(FrameCycles() * 2);
		Assert.Equal(0, GetPrivateCollectionCount(bus.Display, "_previousLiveSpriteFrameCommands"));
		SetPrivateField(bus.Display, "_archivedTimelineValid", false);
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

		bus.Display.RenderFrame(frame, FrameCycles(), FrameCycles() * 2);

		Assert.Equal(ToBgra(0), Pixel(frame, StandardX, StandardY));
		Assert.Equal(0, bus.Display.CaptureSnapshot().LastSpriteNonZeroPixels);
	}

	[Fact]
	public void ArchivedSpriteFallbackDoesNotCarryStaleCommandAfterControlBlockRewrite()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		EnableSpriteDma(bus, 0x8220);
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		WriteSpriteDmaRows(bus, SpriteListBase, StandardX, StandardY, 4, 0x8000, 0x0000);
		SetSpritePointer(bus, sprite: 0, SpriteListBase);
		bus.AdvanceDmaTo(FrameCycles());
		var (movedPos, movedCtl) = EncodeSpritePosition(StandardX + 12, StandardY, 4);
		WriteChipWord(bus, SpriteListBase, movedPos);
		WriteChipWord(bus, SpriteListBase + 2, movedCtl);
		SetPrivateField(bus.Display, "_liveCapturedThroughCycle", (FrameCycles() * 2) - 1);
		InvokePrivateMethod(bus.Display, "ArchiveLiveSpriteFrameBeforeStarting", FrameCycles() * 2);
		SetPrivateField(bus.Display, "_archivedTimelineValid", false);
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

		bus.Display.RenderFrame(frame, FrameCycles(), FrameCycles() * 2);

		Assert.Equal(ToBgra(0), Pixel(frame, StandardX, StandardY));
		Assert.Equal(ToBgra(0), Pixel(frame, StandardX + 12, StandardY));
		Assert.Equal(0, bus.Display.CaptureSnapshot().LastSpriteNonZeroPixels);
	}

	[Fact]
	public void LiveDmaArchivedTimelineRendersAttachedManualSpriteRepeats()
	{
		var presentationBus = new AmigaBus(enableLiveAgnusDma: false);
		var liveBus = new AmigaBus(enableLiveAgnusDma: true);
		foreach (var bus in new[] { presentationBus, liveBus })
		{
			const ushort color = 0x0F00;
			SetColor(bus, 21, color);
			var (evenPos, evenCtl) = EncodeSpritePosition(StandardX, StandardY, 1);
			var (oddPos, oddCtl) = EncodeSpritePosition(StandardX, StandardY, 1, attached: true);
			var armCycle = RowCycle(StandardY);
			var disarmCycle = RowCycle(StandardY + 2);
			bus.Display.ScheduleWrite(armCycle, 0x140, evenPos);
			bus.Display.ScheduleWrite(armCycle, 0x142, evenCtl);
			bus.Display.ScheduleWrite(armCycle, 0x146, 0x0000);
			bus.Display.ScheduleWrite(armCycle, 0x144, 0x8000);
			bus.Display.ScheduleWrite(armCycle, 0x148, oddPos);
			bus.Display.ScheduleWrite(armCycle, 0x14A, oddCtl);
			bus.Display.ScheduleWrite(armCycle, 0x14E, 0x0000);
			bus.Display.ScheduleWrite(armCycle, 0x14C, 0x8000);
			bus.Display.ScheduleWrite(disarmCycle, 0x142, evenCtl);
			bus.Display.ScheduleWrite(disarmCycle, 0x14A, oddCtl);
		}

		var expected = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
		var actual = new uint[expected.Length];
		presentationBus.Display.RenderFrame(expected, 0, FrameCycles());
		liveBus.AdvanceDmaTo(FrameCycles());
		liveBus.Display.RenderFrame(actual, 0, FrameCycles());

		var snapshot = liveBus.Display.CaptureSnapshot();
		Assert.Equal(Pixel(expected, StandardX, StandardY), Pixel(actual, StandardX, StandardY));
		Assert.Equal(Pixel(expected, StandardX, StandardY + 1), Pixel(actual, StandardX, StandardY + 1));
		Assert.Equal(ToBgra(0x0F00), Pixel(actual, StandardX, StandardY));
		Assert.Equal(ToBgra(0x0F00), Pixel(actual, StandardX, StandardY + 1));
		Assert.Equal(ToBgra(0), Pixel(actual, StandardX, StandardY + 2));
		Assert.True(snapshot.LastTimelineSpriteCommandCount > 0);
		Assert.Equal(0, snapshot.LastActiveTimelineFrameCount);
		Assert.Equal(1, snapshot.LastArchivedTimelineFrameCount);
		Assert.Equal(0, snapshot.LastTimelineFallbackCount);
	}

	[Fact]
	public void LiveDmaArchivedTimelineMatchesMultiLineAttachedManualSpritePair()
	{
		var presentationBus = new AmigaBus(enableLiveAgnusDma: false);
		var liveBus = new AmigaBus(enableLiveAgnusDma: true);
		foreach (var bus in new[] { presentationBus, liveBus })
		{
			SetColor(bus, 21, 0x0F00);
			SetColor(bus, 26, 0x00F0);
			var (evenPos, evenCtl) = EncodeSpritePosition(StandardX, StandardY, 1);
			var (oddPos, oddCtl) = EncodeSpritePosition(StandardX, StandardY, 1, attached: true);
			var row0Cycle = RowCycle(StandardY);
			var row1Cycle = RowCycle(StandardY + 1);
			var disarmCycle = RowCycle(StandardY + 2);
			var (row0EvenA, row0EvenB) = DataWordsForPixel(1, bitOffset: 0);
			var (row0OddA, row0OddB) = DataWordsForPixel(1, bitOffset: 0);
			var (row1EvenA, row1EvenB) = DataWordsForPixel(2, bitOffset: 0);
			var (row1OddA, row1OddB) = DataWordsForPixel(2, bitOffset: 0);
			bus.Display.ScheduleWrite(row0Cycle, 0x140, evenPos);
			bus.Display.ScheduleWrite(row0Cycle, 0x142, evenCtl);
			bus.Display.ScheduleWrite(row0Cycle, 0x148, oddPos);
			bus.Display.ScheduleWrite(row0Cycle, 0x14A, oddCtl);
			bus.Display.ScheduleWrite(row0Cycle, 0x146, row0EvenB);
			bus.Display.ScheduleWrite(row0Cycle, 0x144, row0EvenA);
			bus.Display.ScheduleWrite(row0Cycle, 0x14E, row0OddB);
			bus.Display.ScheduleWrite(row0Cycle, 0x14C, row0OddA);
			bus.Display.ScheduleWrite(row1Cycle, 0x146, row1EvenB);
			bus.Display.ScheduleWrite(row1Cycle, 0x144, row1EvenA);
			bus.Display.ScheduleWrite(row1Cycle, 0x14E, row1OddB);
			bus.Display.ScheduleWrite(row1Cycle, 0x14C, row1OddA);
			bus.Display.ScheduleWrite(disarmCycle, 0x142, evenCtl);
			bus.Display.ScheduleWrite(disarmCycle, 0x14A, oddCtl);
		}

		var expected = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
		var actual = new uint[expected.Length];
		presentationBus.Display.RenderFrame(expected, 0, FrameCycles());
		liveBus.AdvanceDmaTo(FrameCycles());
		liveBus.Display.RenderFrame(actual, 0, FrameCycles());

		var snapshot = liveBus.Display.CaptureSnapshot();
		Assert.Equal(expected, actual);
		Assert.Equal(ToBgra(0x0F00), Pixel(actual, StandardX, StandardY));
		Assert.Equal(ToBgra(0x00F0), Pixel(actual, StandardX, StandardY + 1));
		Assert.Equal(1, snapshot.LastArchivedTimelineFrameCount);
		Assert.Equal(0, snapshot.LastTimelineFallbackCount);
	}

	[Fact]
	public void TimedFallbackUsesArchivedLiveSpriteDataWithoutPresentationDmaReads()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		EnableSpriteDma(bus, 0x8220);
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		WriteSpriteDmaRows(bus, SpriteListBase, StandardX, StandardY, 16, 0x8000, 0x0000);
		SetSpritePointer(bus, sprite: 0, SpriteListBase);
		bus.WriteWord(0x00DFF088, 0x0000, RowCycle(StandardY + 1));
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

		bus.AdvanceDmaTo(FrameCycles());
		bus.Display.RenderFrame(frame, 0, FrameCycles());

		var snapshot = bus.Display.CaptureSnapshot();
		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX, StandardY));
		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX, StandardY + 15));
		Assert.Equal(0, snapshot.LastSpriteDmaFetches);
		Assert.Equal(0, snapshot.LastMissedSpriteDmaSlots);
		Assert.Equal(0, snapshot.LastArchivedTimelineFrameCount);
		Assert.Equal(0, snapshot.LastActiveTimelineFrameCount);
		Assert.Equal(0, snapshot.LastTimelineSegmentCount);
	}

	[Fact]
	public void ArchivedLiveSpriteCommandsCoalesceSameDescriptorWithLaterRows()
	{
		var expectedBus = CreateArchivedSpriteFallbackBus(rewritePointer: false);
		var actualBus = CreateArchivedSpriteFallbackBus(rewritePointer: true);
		var expected = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
		var actual = new uint[expected.Length];

		expectedBus.AdvanceDmaTo(FrameCycles());
		expectedBus.Display.RenderFrame(expected, 0, FrameCycles());
		actualBus.AdvanceDmaTo(FrameCycles());
		actualBus.Display.RenderFrame(actual, 0, FrameCycles());

		var snapshot = actualBus.Display.CaptureSnapshot();
		Assert.Equal(expected, actual);
		Assert.Equal(0, snapshot.LastSpriteDmaFetches);
		Assert.Equal(0, snapshot.LastMissedSpriteDmaSlots);
	}

	[Fact]
	public void ArchivedLiveSpritePendingPointerRewriteReplacesPreviousPendingHorizontalPosition()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		const int movedX = StandardX + 12;
		EnableSpriteDma(bus, 0x8220);
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		WriteSpriteDmaRows(bus, SpriteListBase, StandardX, StandardY, 16, 0x8000, 0x0000);
		SetSpritePointer(bus, sprite: 0, SpriteListBase);
		var (movedPos, movedCtl) = EncodeSpritePosition(movedX, StandardY, 16);
		bus.WriteWord(SpriteListBase, movedPos, RowCycle(StandardY - 10));
		bus.WriteWord(SpriteListBase + 2, movedCtl, RowCycle(StandardY - 10));
		bus.WriteWord(0x00DFF122, (ushort)SpriteListBase, RowCycle(StandardY - 10));
		bus.WriteWord(0x00DFF100, 0x0000, RowCycle(StandardY + 1));
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

		bus.AdvanceDmaTo(FrameCycles());
		bus.Display.RenderFrame(frame, 0, FrameCycles());

		var snapshot = bus.Display.CaptureSnapshot();
		Assert.Equal(ToBgra(0), Pixel(frame, StandardX, StandardY));
		Assert.Equal(ToBgra(0x0F00), Pixel(frame, movedX, StandardY));
		Assert.InRange(snapshot.LastSpriteNonZeroPixels, 1, 16);
		Assert.Equal(0, snapshot.LastSpriteDmaFetches);
		Assert.Equal(0, snapshot.LastMissedSpriteDmaSlots);
	}

	[Fact]
	public void LiveDmaArchivedTimelineAcceptsCopperSpritePointerWrites()
	{
		var presentationBus = new AmigaBus(enableLiveAgnusDma: false);
		var liveBus = new AmigaBus(enableLiveAgnusDma: true);
		foreach (var bus in new[] { presentationBus, liveBus })
		{
			EnableSpriteDma(bus, 0x82A0);
			SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
			WriteSpriteDmaBlock(bus, SpriteListBase, StandardX, StandardY, 1, 0x8000, 0x0000);
			WriteCopperList(
				bus,
				CopperListBase,
				(0x0120, (ushort)(SpriteListBase >> 16)),
				(0x0122, (ushort)SpriteListBase),
				(0xFFFF, 0xFFFE));
			bus.WriteWord(0x00DFF080, (ushort)(CopperListBase >> 16));
			bus.WriteWord(0x00DFF082, (ushort)CopperListBase);
		}

		var expected = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
		var actual = new uint[expected.Length];
		presentationBus.Display.RenderFrame(expected, 0, FrameCycles());
		liveBus.AdvanceDmaTo(FrameCycles());
		liveBus.Display.RenderFrame(actual, 0, FrameCycles());

		var snapshot = liveBus.Display.CaptureSnapshot();
		Assert.Equal(Pixel(expected, StandardX, StandardY), Pixel(actual, StandardX, StandardY));
		Assert.Equal(ToBgra(0x0F00), Pixel(actual, StandardX, StandardY));
		Assert.True(snapshot.LastTimelineSpriteCommandCount > 0);
		Assert.Equal(0, snapshot.LastActiveTimelineFrameCount);
		Assert.Equal(1, snapshot.LastArchivedTimelineFrameCount);
		Assert.Equal(0, snapshot.LastTimelineFallbackCount);
		Assert.Equal(0, snapshot.LastArchiveRejectUnsafeWrite);
	}

	[Fact]
	public void LiveDmaArchivedTimelineRecordsDeniedSpriteDataSlots()
	{
		var presentationBus = CreateDeniedSpriteSlotBus(enableLiveDma: false);
		var liveBus = CreateDeniedSpriteSlotBus(enableLiveDma: true);
		var expected = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
		var actual = new uint[expected.Length];

		presentationBus.Display.RenderFrame(expected, 0, FrameCycles());
		liveBus.AdvanceDmaTo(FrameCycles());
		liveBus.Display.RenderFrame(actual, 0, FrameCycles());

		var snapshot = liveBus.Display.CaptureSnapshot();
		Assert.Equal(Pixel(expected, StandardX, StandardY), Pixel(actual, StandardX, StandardY));
		Assert.Equal(0xFF000000u, Pixel(actual, StandardX, StandardY));
		Assert.Equal(0, snapshot.LastActiveTimelineFrameCount);
		Assert.Equal(1, snapshot.LastArchivedTimelineFrameCount);
		Assert.Equal(0, snapshot.LastTimelineFallbackCount);
		Assert.Equal(0, snapshot.LastArchiveRejectMissingSpriteFetch);
		Assert.True(snapshot.LastSpriteDeniedFetchCount > 0);
		Assert.Equal(0, snapshot.LastSpriteRecoveryAttemptCount);
	}

	private static void WriteManualSprite(
		AmigaBus bus,
		int sprite,
		int x,
		int y,
		int height,
		ushort dataA,
		ushort dataB,
		bool attached = false)
	{
		var (pos, ctl) = EncodeSpritePosition(x, y, height, attached);
		var register = 0x00DFF140u + (uint)(sprite * 8);
		bus.WriteWord(register, pos);
		bus.WriteWord(register + 2, ctl);
		bus.WriteWord(register + 6, dataB);
		bus.WriteWord(register + 4, dataA);
	}

	private static void ClearPrivateArray(object instance, string fieldName)
	{
		var field = instance.GetType().GetField(
			fieldName,
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		Assert.NotNull(field);
		var value = Assert.IsAssignableFrom<Array>(field.GetValue(instance));
		Array.Clear(value);
	}

	private static void SetPrivateField(object instance, string fieldName, object value)
	{
		var field = instance.GetType().GetField(
			fieldName,
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		Assert.NotNull(field);
		field.SetValue(instance, value);
	}

	private static int GetPrivateCollectionCount(object instance, string fieldName)
	{
		var field = instance.GetType().GetField(
			fieldName,
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		Assert.NotNull(field);
		var value = Assert.IsAssignableFrom<System.Collections.ICollection>(field.GetValue(instance));
		return value.Count;
	}

	private static T GetPrivateField<T>(object instance, string fieldName)
	{
		var field = instance.GetType().GetField(
			fieldName,
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		Assert.NotNull(field);
		return Assert.IsAssignableFrom<T>(field.GetValue(instance));
	}

	private static void InvokePrivateMethod(object instance, string methodName, params object[] arguments)
	{
		var method = instance.GetType().GetMethod(
			methodName,
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		Assert.NotNull(method);
		method.Invoke(instance, arguments);
	}

	private static void WriteSpriteDmaBlock(
		AmigaBus bus,
		uint address,
		int x,
		int y,
		int height,
		ushort dataA,
		ushort dataB,
		bool attached = false,
		bool terminate = true)
	{
		var (pos, ctl) = EncodeSpritePosition(x, y, height, attached);
		WriteChipWord(bus, address, pos);
		WriteChipWord(bus, address + 2, ctl);
		WriteChipWord(bus, address + 4, dataA);
		WriteChipWord(bus, address + 6, dataB);
		if (terminate)
		{
			WriteChipWord(bus, address + 8, 0);
			WriteChipWord(bus, address + 10, 0);
		}
	}

	private static void WriteSpriteDmaRows(
		AmigaBus bus,
		uint address,
		int x,
		int y,
		int height,
		ushort dataA,
		ushort dataB)
	{
		var (pos, ctl) = EncodeSpritePosition(x, y, height);
		WriteChipWord(bus, address, pos);
		WriteChipWord(bus, address + 2, ctl);
		for (var row = 0; row < height; row++)
		{
			WriteChipWord(bus, address + 4 + (uint)(row * 4), dataA);
			WriteChipWord(bus, address + 6 + (uint)(row * 4), dataB);
		}

		var terminator = address + 4 + (uint)(height * 4);
		WriteChipWord(bus, terminator, 0);
		WriteChipWord(bus, terminator + 2, 0);
	}

	private static void WriteChipWord(AmigaBus bus, uint address, ushort value)
	{
		BigEndian.WriteUInt16(bus.ChipRam, checked((int)address), value);
	}

	private static void WriteCopperList(AmigaBus bus, uint address, params (ushort First, ushort Second)[] instructions)
	{
		for (var i = 0; i < instructions.Length; i++)
		{
			WriteChipWord(bus, address + (uint)(i * 4), instructions[i].First);
			WriteChipWord(bus, address + (uint)(i * 4) + 2, instructions[i].Second);
		}
	}

	private static void SetSpritePointer(AmigaBus bus, int sprite, uint address)
	{
		var register = 0x00DFF120u + (uint)(sprite * 4);
		bus.WriteWord(register, (ushort)(address >> 16));
		bus.WriteWord(register + 2, (ushort)address);
	}

	private static AmigaBus CreateDeniedSpriteSlotBus(bool enableLiveDma)
	{
		var bus = new AmigaBus(enableLiveAgnusDma: enableLiveDma);
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		WriteSpriteDmaBlock(bus, SpriteListBase, StandardX, StandardY, 1, 0x8000, 0x0000);
		SetSpritePointer(bus, sprite: 0, SpriteListBase);
		WriteChipWord(bus, 0x5000, 0);
		bus.WriteWord(0x00DFF0E0, 0x0000);
		bus.WriteWord(0x00DFF0E2, 0x5000);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x00D0);
		bus.WriteWord(0x00DFF100, 0x1000);
		EnableSpriteDma(bus, 0x8320);
		bus.WriteWord(0x00DFF092, 0x0018, RowCycle(StandardY - 1));
		return bus;
	}

	private static AmigaBus CreateArchivedSpriteFallbackBus(bool rewritePointer)
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		EnableSpriteDma(bus, 0x8220);
		SetColor(bus, SingleSpriteColorIndex(0, 1), 0x0F00);
		WriteSpriteDmaRows(bus, SpriteListBase, StandardX, StandardY, 16, 0x8000, 0x0000);
		SetSpritePointer(bus, sprite: 0, SpriteListBase);
		if (rewritePointer)
		{
			bus.WriteWord(0x00DFF122, (ushort)SpriteListBase, RowCycle(StandardY - 1));
			bus.WriteWord(0x00DFF122, (ushort)SpriteListBase, RowCycle(StandardY + 5));
			bus.WriteWord(0x00DFF122, (ushort)SpriteListBase, RowCycle(StandardY + 12));
		}

		bus.WriteWord(0x00DFF100, 0x0000, RowCycle(StandardY + 1));
		return bus;
	}

	private static void EnableSpriteDma(AmigaBus bus, ushort dmacon)
	{
		bus.WriteWord(0x00DFF096, dmacon);
		bus.Paula.AdvanceTo(0);
	}

	private static void SetColor(AmigaBus bus, int index, ushort value)
	{
		bus.WriteWord(0x00DFF180u + (uint)(index * 2), value);
	}

	private static ushort ColorAt(AmigaBus bus, int index)
	{
		return bus.Display.CaptureSnapshot().Colors[index];
	}

	private static uint[] RenderLowResFrame(AmigaBus bus)
	{
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
		bus.Display.RenderFrame(frame);
		return frame;
	}

	private static AmigaBus CreateDisplayComponentBus()
	{
		return new AmigaBus(
			enableLiveAgnusDma: false);
	}

	private static uint Pixel(uint[] frame, int x, int y)
	{
		return Pixel(frame, AmigaConstants.PalLowResWidth, x, y);
	}

	private static uint Pixel(uint[] frame, int width, int x, int y)
	{
		return frame[(y * width) + x];
	}

	private static int SingleSpriteColorIndex(int sprite, int pixel)
	{
		return 16 + ((sprite / 2) * 4) + pixel;
	}

	private static (ushort DataA, ushort DataB) DataWordsForPixel(int pixel, int bitOffset)
	{
		var bit = 15 - bitOffset;
		var dataA = (pixel & 0x01) != 0 ? (ushort)(1 << bit) : (ushort)0;
		var dataB = (pixel & 0x02) != 0 ? (ushort)(1 << bit) : (ushort)0;
		return (dataA, dataB);
	}

	private static (ushort Pos, ushort Ctl) EncodeSpritePosition(int x, int y, int height, bool attached = false)
	{
		var hStart = x + 128 - AmigaConstants.PalLowResOverscanBorderX;
		var vStart = y + (0x2C - AmigaConstants.PalLowResOverscanBorderY);
		var vStop = vStart + height;
		var pos = (ushort)(((vStart & 0xFF) << 8) | ((hStart >> 1) & 0xFF));
		var ctl = (ushort)(((vStop & 0xFF) << 8) |
			(hStart & 0x0001) |
			((vStop & 0x100) != 0 ? 0x0002 : 0) |
			((vStart & 0x100) != 0 ? 0x0004 : 0) |
			(attached ? 0x0080 : 0));
		return (pos, ctl);
	}

	private static ushort UniqueColor(int colorIndex)
	{
		return (ushort)(((colorIndex & 0x0F) << 8) |
			(((colorIndex * 3) & 0x0F) << 4) |
			((colorIndex * 5) & 0x0F));
	}

	private static uint ToBgra(ushort amigaColor)
	{
		var r = (uint)(((amigaColor >> 8) & 0x0F) * 17);
		var g = (uint)(((amigaColor >> 4) & 0x0F) * 17);
		var b = (uint)((amigaColor & 0x0F) * 17);
		return 0xFF00_0000u | (r << 16) | (g << 8) | b;
	}

	private static long RowCycle(int row)
	{
		var lineCycles = AmigaConstants.A500PalCpuClockHz / AmigaConstants.A500PalVBlankHz / AmigaConstants.A500PalRasterLines;
		var displayRow = (0x2C - AmigaConstants.PalLowResOverscanBorderY) + row;
		return (long)Math.Round(lineCycles * displayRow);
	}

	private static long FrameCycles()
	{
		return (long)Math.Round(AmigaConstants.A500PalCpuClockHz / AmigaConstants.A500PalVBlankHz);
	}

	private static long OutputCycle(int row, int horizontal)
	{
		var lineCycles = AmigaConstants.A500PalCpuClockHz / AmigaConstants.A500PalVBlankHz / AmigaConstants.A500PalRasterLines;
		var displayRow = (0x2C - AmigaConstants.PalLowResOverscanBorderY) + row;
		return (long)Math.Round((lineCycles * displayRow) + (horizontal * AmigaConstants.A500PalCpuCyclesPerColorClock));
	}

	private static int OutputXForHorizontal(int horizontal)
	{
		return Math.Clamp((horizontal - 0x38) * 2, 0, AmigaConstants.PalLowResWidth);
	}

	private static long AfterSpriteMatchCycle(int row, int x)
	{
		const int defaultDdfStart = 0x38;
		var horizontal = defaultDdfStart + (x / 2) + 1;
		return RowCycle(row) + (horizontal * AmigaConstants.A500PalCpuCyclesPerColorClock);
	}

	private sealed record SpriteConformanceRow(string Group, string Name, SpriteRowStatus Status, string Reason)
	{
		public static SpriteConformanceRow Executable(string group, string name)
		{
			return new SpriteConformanceRow(group, name, SpriteRowStatus.Executable, string.Empty);
		}

		public static SpriteConformanceRow Pending(string group, string name, string reason)
		{
			return new SpriteConformanceRow(group, name, SpriteRowStatus.Pending, reason);
		}
	}

	private enum SpriteRowStatus
	{
		Executable,
		Pending
	}

	private sealed record SpriteIndexRow(int Sprite);

	private sealed record SingleSpritePaletteRow(int Sprite, int Pixel);

	private sealed record SpritePixelRow(int BitOffset, int Pixel);

	private sealed record AttachedSpritePaletteRow(int Pixel);

	private sealed record PositionRow(string Name, int X, int Y, int Height);

	private sealed record DmaEnableRow(string Name, ushort Dmacon, bool ExpectedVisible);

	private sealed record DmaHeightRow(int Height);

	private sealed record SpritePriorityRow(int FrontSprite, int BackSprite);
}
