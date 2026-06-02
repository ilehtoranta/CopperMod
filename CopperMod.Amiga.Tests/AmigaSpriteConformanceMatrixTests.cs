using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaSpriteConformanceMatrixTests
{
	private const int StandardX = AmigaConstants.PalLowResOverscanBorderX;
	private const int StandardY = AmigaConstants.PalLowResOverscanBorderY;
	private const uint SpriteListBase = 0x3000;

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
			yield return new object[] { new PositionRow("even horizontal start", 24, 32, 1) };
			yield return new object[] { new PositionRow("odd horizontal start from SPRxCTL bit 0", 25, 33, 1) };
			yield return new object[] { new PositionRow("VSTART high bit from SPRxCTL", 24, 230, 4) };
			yield return new object[] { new PositionRow("last visible standard PAL display row", 24, AmigaConstants.PalLowResOverscanBorderY + AmigaConstants.PalLowResStandardHeight - 1, 1) };
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
			yield return SpriteConformanceRow.Executable("manual-control", "SPRxCTL disarms");
			yield return SpriteConformanceRow.Executable("manual-control", "SPRxPOS can move armed sprite");
			yield return SpriteConformanceRow.Executable("dma-list", "zero control words terminate");
			yield return SpriteConformanceRow.Executable("dma-list", "zero height control block terminates");
			yield return SpriteConformanceRow.Executable("dma-list", "multiple control blocks reuse a channel");
			yield return SpriteConformanceRow.Executable("dma-pointers", "SPRxPTL bit 0 ignored");
			yield return SpriteConformanceRow.Executable("dma-timing", "extra-wide playfield fetches can consume late sprite DMA slots");
			yield return SpriteConformanceRow.Executable("attached-colors", "odd attached sprite with transparent or missing even partner");
			yield return SpriteConformanceRow.Executable("dma-list", "hardware one-line gap requirement between reused sprite images");
			yield return SpriteConformanceRow.Pending("undocumented-ocs", "BPLxDAT latch enables sprites outside normal bitplane area", "Requires a latch-level Denise/BPLxDAT model.");
			yield return SpriteConformanceRow.Pending("undocumented-ocs", "sprite vertical stop and previous-line data edge cases", "Requires tighter sprite DMA slot and shift-register timing.");
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
	public void AttachedOddSpriteWithoutEvenPartnerUsesHighColorBits()
	{
		var bus = new AmigaBus();
		SetColor(bus, 20, 0x0F00);
		WriteManualSprite(bus, sprite: 1, StandardX, StandardY, 1, 0x8000, 0x0000, attached: true);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0x0F00), Pixel(frame, StandardX, StandardY));
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
	public void ManualSpriteRepeatsFollowingScanLinesUntilCtlDisarmsIt()
	{
		var bus = CreateLegacyDisplayBus();
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
		WriteSpriteDmaBlock(bus, SpriteListBase, 24, 30, 1, 0x8000, 0x0000, terminate: false);
		WriteSpriteDmaBlock(bus, SpriteListBase + 8, 40, 60, 1, 0x0000, 0x8000);
		SetSpritePointer(bus, sprite: 0, SpriteListBase);
		var frame = RenderLowResFrame(bus);

		Assert.Equal(ToBgra(0x0F00), Pixel(frame, 24, 30));
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
		var bus = CreateLegacyDisplayBus();
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

	private static void WriteChipWord(AmigaBus bus, uint address, ushort value)
	{
		BigEndian.WriteUInt16(bus.ChipRam, checked((int)address), value);
	}

	private static void SetSpritePointer(AmigaBus bus, int sprite, uint address)
	{
		var register = 0x00DFF120u + (uint)(sprite * 4);
		bus.WriteWord(register, (ushort)(address >> 16));
		bus.WriteWord(register + 2, (ushort)address);
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

	private static AmigaBus CreateLegacyDisplayBus()
	{
		return new AmigaBus(
			enableLiveAgnusDma: false,
			agnusTimingMode: AgnusTimingMode.LegacyReservation);
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
		var hStart = x + 64 - AmigaConstants.PalLowResOverscanBorderX;
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
