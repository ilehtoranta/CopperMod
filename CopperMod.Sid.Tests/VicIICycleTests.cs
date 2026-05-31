namespace CopperMod.Sid.Tests;

public sealed class VicIICycleTests
{
	[Fact]
	public void PalAndNtscRasterCountersWrapAtExplicitGeometry()
	{
		var pal = new VicII(C64ClockProfile.FromSidClock(SidClock.Pal));
		var ntsc = new VicII(C64ClockProfile.FromSidClock(SidClock.Ntsc));
		pal.Reset();
		ntsc.Reset();

		Tick(pal, SidConstants.PalCyclesPerFrame);
		Tick(ntsc, SidConstants.NtscCyclesPerFrame);

		Assert.Equal(0, pal.DebugState.RasterLine);
		Assert.Equal(0, pal.DebugState.RasterCycle);
		Assert.Equal(0, ntsc.DebugState.RasterLine);
		Assert.Equal(0, ntsc.DebugState.RasterCycle);
	}

	[Fact]
	public void RasterCompareRegistersUseD011HighBitAndD012LowBits()
	{
		var vic = CreateVic();

		vic.Write(0x11, 0x80);
		vic.Write(0x12, 0x23);

		Assert.Equal(0x0123, vic.DebugState.RasterCompare);

		AdvanceTo(vic, rasterLine: 0x0123, publicCycle: 1);

		Assert.Equal(0x80, vic.Read(0x11) & 0x80);
		Assert.Equal(0x23, vic.Read(0x12));
	}

	[Fact]
	public void RasterIrqMaskOnlyControlsLineHighBit()
	{
		var vic = CreateVic();
		vic.Write(0x19, 0x01);
		vic.Write(0x12, 0x01);

		Tick(vic, 63);

		Assert.Equal(0x01, vic.Read(0x19));
		vic.Write(0x1A, 0x01);
		Assert.Equal(0x81, vic.Read(0x19));
		vic.Write(0x1A, 0x00);
		Assert.Equal(0x01, vic.Read(0x19));
	}

	[Fact]
	public void RasterCompareWriteCanAssertIrqImmediatelyButAckDoesNotRetriggerSameLine()
	{
		var vic = CreateVic();
		vic.Write(0x19, 0x01);
		vic.Write(0x1A, 0x01);
		AdvanceTo(vic, rasterLine: 0x20, publicCycle: 8);

		vic.Write(0x12, 0x20);

		Assert.Equal(0x81, vic.Read(0x19));
		vic.Write(0x19, 0x01);
		Assert.Equal(0x00, vic.Read(0x19));
		vic.Write(0x12, 0x20);
		Assert.Equal(0x00, vic.Read(0x19));
	}

	[Fact]
	public void YScrollWriteBeforePublicCycleTwelveCreatesCurrentBadline()
	{
		var vic = CreateVic();
		vic.Write(0x11, 0x11);
		AdvanceTo(vic, rasterLine: 0x30, publicCycle: 11);

		vic.Write(0x11, 0x10);
		Tick(vic, 1);

		Assert.True(vic.DebugState.BadlineCandidate);
		Assert.True(vic.DebugState.BaLow);
		Assert.False(vic.DebugState.BadlineArtificial);
	}

	[Fact]
	public void D011WriteAtPublicCycleFourteenCreatesArtificialBadlineOnNextCycle()
	{
		var vic = CreateVic();
		vic.Write(0x11, 0x11);
		AdvanceTo(vic, rasterLine: 0x30, publicCycle: 14);

		vic.Write(0x11, 0x10);
		Tick(vic, 1);

		Assert.True(vic.DebugState.BadlineActive);
		Assert.True(vic.DebugState.BadlineArtificial);
		Assert.True(vic.DebugState.AecLow);
		Assert.Equal(3, vic.DebugState.BadlineFliBugColumns);
		Assert.Equal(0, vic.DebugState.BadlineFetchIndex);
	}

	[Fact]
	public void BadlineBaAndAecWindowsFollowPublicVicCycles()
	{
		var vic = CreateVic();
		vic.Write(0x11, 0x10);

		AdvanceTo(vic, rasterLine: 0x30, publicCycle: 11);
		Assert.False(vic.DebugState.BaLow);
		Assert.False(vic.DebugState.AecLow);

		Tick(vic, 1);
		Assert.True(vic.DebugState.BaLow);
		Assert.False(vic.DebugState.AecLow);
		Assert.True(vic.DebugState.TransitionWriteAllowed);

		AdvanceTo(vic, rasterLine: 0x30, publicCycle: 15);
		Assert.True(vic.DebugState.BaLow);
		Assert.True(vic.DebugState.AecLow);
		Assert.False(vic.DebugState.TransitionWriteAllowed);

		AdvanceTo(vic, rasterLine: 0x30, publicCycle: 54);
		Assert.True(vic.DebugState.BaLow);
		Assert.True(vic.DebugState.AecLow);

		Tick(vic, 1);
		Assert.False(vic.DebugState.BaLow);
		Assert.False(vic.DebugState.AecLow);
	}

	[Fact]
	public void BadlineRequiresDenYScrollMatchAndVisibleRasterRange()
	{
		var noDen = CreateVic();
		noDen.Write(0x11, 0x00);
		AdvanceTo(noDen, rasterLine: 0x30, publicCycle: 12);
		Assert.False(noDen.DebugState.BadlineCandidate);

		var yScrollMismatch = CreateVic();
		yScrollMismatch.Write(0x11, 0x11);
		AdvanceTo(yScrollMismatch, rasterLine: 0x30, publicCycle: 12);
		Assert.False(yScrollMismatch.DebugState.BadlineCandidate);

		var outsideVisibleRange = CreateVic();
		outsideVisibleRange.Write(0x11, 0x10);
		AdvanceTo(outsideVisibleRange, rasterLine: 0x28, publicCycle: 12);
		Assert.False(outsideVisibleRange.DebugState.BadlineCandidate);
	}

	[Fact]
	public void DenIsLatchedAtVisibleAreaStartForBadlineEligibility()
	{
		var vic = CreateVic();
		vic.Write(0x11, 0x00);
		AdvanceTo(vic, rasterLine: 0x30, publicCycle: 11);

		vic.Write(0x11, 0x10);
		Tick(vic, 1);

		Assert.False(vic.DebugState.BadlineCandidate);
		Assert.False(vic.DebugState.BaLow);
	}

	[Fact]
	public void DenLatchKeepsBadlineEligibilityAfterDenIsClearedInsideVisibleArea()
	{
		var vic = CreateVic();
		vic.Write(0x11, 0x10);
		AdvanceTo(vic, rasterLine: 0x30, publicCycle: 11);

		vic.Write(0x11, 0x00);
		Tick(vic, 1);

		Assert.True(vic.DebugState.BadlineCandidate);
		Assert.True(vic.DebugState.BaLow);
	}

	[Fact]
	public void CancellingYScrollBeforeAecPreventsBadlineSteal()
	{
		var vic = CreateVic();
		vic.Write(0x11, 0x10);
		AdvanceTo(vic, rasterLine: 0x30, publicCycle: 12);

		Assert.True(vic.DebugState.BaLow);
		vic.Write(0x11, 0x11);
		AdvanceTo(vic, rasterLine: 0x30, publicCycle: 15);

		Assert.False(vic.DebugState.BadlineActive);
		Assert.False(vic.DebugState.AecLow);
	}

	[Fact]
	public void D011WriteAfterAecIsActiveDoesNotReleaseBadlineEarly()
	{
		var vic = CreateVic();
		vic.Write(0x11, 0x10);
		AdvanceTo(vic, rasterLine: 0x30, publicCycle: 15);

		vic.Write(0x11, 0x11);
		AdvanceTo(vic, rasterLine: 0x30, publicCycle: 20);

		Assert.True(vic.DebugState.BadlineActive);
		Assert.True(vic.DebugState.AecLow);
	}

	[Fact]
	public void LateArtificialBadlineDoesNotRetroactivelyStealPassedCycles()
	{
		var vic = CreateVic();
		vic.Write(0x11, 0x11);
		AdvanceTo(vic, rasterLine: 0x30, publicCycle: 16);

		Assert.False(vic.DebugState.AecLow);
		vic.Write(0x11, 0x10);
		Tick(vic, 1);

		Assert.True(vic.DebugState.BadlineActive);
		Assert.True(vic.DebugState.BadlineArtificial);
		Assert.Equal(2, vic.DebugState.BadlineFetchIndex);
		Assert.Equal(5, vic.DebugState.BadlineFliBugColumns);
	}

	[Fact]
	public void ArtificialBadlinesCanBeForcedOnConsecutiveLines()
	{
		var vic = CreateVic();
		vic.Write(0x11, 0x11);
		AdvanceTo(vic, rasterLine: 0x30, publicCycle: 14);
		vic.Write(0x11, 0x10);
		Tick(vic, 1);
		Assert.True(vic.DebugState.BadlineArtificial);

		AdvanceTo(vic, rasterLine: 0x31, publicCycle: 14);
		vic.Write(0x11, 0x11);
		Tick(vic, 1);

		Assert.True(vic.DebugState.BadlineActive);
		Assert.True(vic.DebugState.BadlineArtificial);
	}

	[Fact]
	public void BadlineCountersResetAndAdvanceAcrossDisplayLines()
	{
		var vic = CreateVic();
		vic.Write(0x11, 0x10);
		AdvanceTo(vic, rasterLine: 0x30, publicCycle: 15);

		Assert.Equal(0, vic.DebugState.BadlineRc);
		Assert.Equal(0, vic.DebugState.BadlineVcBase);
		Assert.Equal(1, vic.DebugState.BadlineVc);
		AdvanceTo(vic, rasterLine: 0x30, publicCycle: 54);
		Assert.Equal(40, vic.DebugState.BadlineVc);

		AdvanceTo(vic, rasterLine: 0x31, publicCycle: 1);
		Assert.Equal(1, vic.DebugState.BadlineRc);
		Assert.Equal(40, vic.DebugState.BadlineVc);

		AdvanceTo(vic, rasterLine: 0x38, publicCycle: 15);
		Assert.Equal(0, vic.DebugState.BadlineRc);
		Assert.Equal(40, vic.DebugState.BadlineVcBase);
		Assert.Equal(41, vic.DebugState.BadlineVc);
	}

	[Fact]
	public void SpriteDmaStartsFromEnableAndYCompareAtDocumentedSlot()
	{
		var vic = CreateVic();
		EnableSprite(vic, sprite: 3, y: 1);

		AdvanceTo(vic, rasterLine: 1, publicCycle: 1);

		Assert.True(vic.DebugState.SpriteBaLow);
		Assert.True(vic.DebugState.SpriteAecLow);
		Assert.Equal(3, vic.DebugState.CurrentSpriteIndex);
		Assert.Equal(1 << 3, vic.DebugState.ActiveSpriteMask);
		Assert.Equal(VicMemoryAccessKind.SpritePointer, vic.DebugState.MemoryAccessKind);
	}

	[Fact]
	public void SpriteZeroToTwoFetchAtRightEdgeForNextRasterLine()
	{
		var vic = CreateVic();
		EnableSprite(vic, sprite: 0, y: 1);

		AdvanceTo(vic, rasterLine: 0, publicCycle: 55);

		Assert.True(vic.DebugState.SpriteAecLow);
		Assert.Equal(0, vic.DebugState.CurrentSpriteIndex);
		Assert.Equal(1, vic.DebugState.ActiveSpriteMask);
	}

	[Fact]
	public void DisabledSpriteDoesNotAssertBusLinesOrFetchMemory()
	{
		var vic = CreateVic();
		vic.Write(0x07, 0x01);

		AdvanceTo(vic, rasterLine: 1, publicCycle: 1);

		Assert.False(vic.DebugState.SpriteBaLow);
		Assert.False(vic.DebugState.SpriteAecLow);
		Assert.Equal(-1, vic.DebugState.CurrentSpriteIndex);
		Assert.Equal(VicMemoryAccessKind.None, vic.DebugState.MemoryAccessKind);
	}

	[Fact]
	public void StandardHeightSpriteStopsAfterSixtyThreeBytes()
	{
		var vic = CreateVic();
		EnableSprite(vic, sprite: 3, y: 1);

		AdvanceTo(vic, rasterLine: 21, publicCycle: 3);

		Assert.Equal(0, vic.DebugState.ActiveSpriteMask & (1 << 3));
		Assert.False(vic.DebugState.SpriteAecLow);
	}

	[Fact]
	public void VerticalExpansionKeepsSpriteDmaActiveForDoubleHeight()
	{
		var vic = CreateVic();
		EnableSprite(vic, sprite: 3, y: 1);
		vic.Write(0x17, 1 << 3);

		AdvanceTo(vic, rasterLine: 21, publicCycle: 3);
		Assert.NotEqual(0, vic.DebugState.ActiveSpriteMask & (1 << 3));

		AdvanceTo(vic, rasterLine: 42, publicCycle: 3);
		Assert.Equal(0, vic.DebugState.ActiveSpriteMask & (1 << 3));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void SpritePointerSlotsAreStableAcrossPalAndNtscGeometry(bool ntsc)
	{
		var clock = C64ClockProfile.FromSidClock(ntsc ? SidClock.Ntsc : SidClock.Pal);
		var vic = new VicII(clock);
		vic.Reset();
		for (var sprite = 0; sprite < 8; sprite++)
		{
			EnableSprite(vic, sprite, y: 1);
		}

		var actual = new List<(int Line, int PublicCycle, int Sprite)>();
		foreach (var item in new[] { (0, 55), (0, 58), (0, 61), (1, 1), (1, 4), (1, 7), (1, 10), (1, 13) })
		{
			AdvanceTo(vic, item.Item1, item.Item2);
			actual.Add((vic.DebugState.RasterLine, vic.DebugState.RasterCycle + 1, vic.DebugState.CurrentSpriteIndex));
		}

		Assert.Equal(
		[
			(0, 55, 0),
			(0, 58, 1),
			(0, 61, 2),
			(1, 1, 3),
			(1, 4, 4),
			(1, 7, 5),
			(1, 10, 6),
			(1, 13, 7)
		], actual);
	}

	private static VicII CreateVic()
	{
		var vic = new VicII(C64ClockProfile.FromSidClock(SidClock.Pal));
		vic.Reset();
		return vic;
	}

	private static void EnableSprite(VicII vic, int sprite, int y)
	{
		vic.Write((byte)((sprite * 2) + 1), (byte)y);
		vic.Write(0x15, (byte)(vic.Read(0x15) | (1 << sprite)));
	}

	private static void AdvanceTo(VicII vic, int rasterLine, int publicCycle)
	{
		while (vic.DebugState.RasterLine < rasterLine ||
			(vic.DebugState.RasterLine == rasterLine && vic.DebugState.RasterCycle + 1 < publicCycle))
		{
			vic.Tick();
		}
	}

	private static void Tick(VicII vic, int cycles)
	{
		for (var i = 0; i < cycles; i++)
		{
			vic.Tick();
		}
	}
}
