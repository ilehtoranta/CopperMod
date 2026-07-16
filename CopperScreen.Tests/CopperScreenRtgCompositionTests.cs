using CopperMod.Amiga;

namespace CopperScreen.Tests;

public sealed class CopperScreenRtgCompositionTests
{
	[Fact]
	public void MixedPlanarAndRtgRowsFollowFrontToBackViewPortOwnership()
	{
		var planar = Enumerable.Repeat(unchecked((int)0xFF00_AA00u), 16).ToArray();
		var rtgPixels = Enumerable.Repeat(0xFFAA_0000u, 16).ToArray();
		var composition = new CyberGraphicsDisplayComposition(
			4,
			4,
			TopIsRtg: true,
			TopDpmsOff: false,
			[
				new CyberGraphicsDisplayLayer(
					0x2000, true, 0, 2, 4, 2, 0, 0, 4, 4,
					0xFF00_0000u, false, rtgPixels),
				new CyberGraphicsDisplayLayer(
					0x2100, false, 0, 0, 4, 4, 0, 0, 4, 4,
					0xFF00_0000u, false, null)
			]);
		var logical = new uint[16];

		CopperScreenEmulator.ComposeRtgLogicalFrame(planar, 4, 4, composition, logical);

		Assert.All(logical[..8], pixel => Assert.Equal(0xFF00_AA00u, pixel));
		Assert.All(logical[8..], pixel => Assert.Equal(0xFFAA_0000u, pixel));
	}

	[Fact]
	public void PlanarFrontScreenRevealsRtgScreenBelowItsDraggedEdge()
	{
		var planar = Enumerable.Repeat(unchecked((int)0xFF00_AA00u), 16).ToArray();
		var rtgPixels = Enumerable.Repeat(0xFFAA_0000u, 16).ToArray();
		var composition = new CyberGraphicsDisplayComposition(
			4,
			4,
			TopIsRtg: false,
			TopDpmsOff: false,
			[
				new CyberGraphicsDisplayLayer(
					0x2000, false, 0, 2, 4, 2, 0, 0, 4, 4,
					0xFF00_0000u, false, null),
				new CyberGraphicsDisplayLayer(
					0x2100, true, 0, 0, 4, 4, 0, 0, 4, 4,
					0xFF00_0000u, false, rtgPixels)
			]);
		var logical = new uint[16];

		CopperScreenEmulator.ComposeRtgLogicalFrame(planar, 4, 4, composition, logical);

		Assert.All(logical[..8], pixel => Assert.Equal(0xFFAA_0000u, pixel));
		Assert.All(logical[8..], pixel => Assert.Equal(0xFF00_AA00u, pixel));
	}

	[Fact]
	public void DifferentResolutionBandsClipAndPadInsteadOfFitScaling()
	{
		var widePixels = Enumerable.Range(0, 8)
			.Select(index => 0xFF00_0000u | (uint)(index + 1))
			.ToArray();
		var narrowPixels = new[] { 0xFF10_0000u, 0xFF20_0000u };
		var composition = new CyberGraphicsDisplayComposition(
			4,
			2,
			TopIsRtg: true,
			TopDpmsOff: false,
			[
				new CyberGraphicsDisplayLayer(
					0x2000, true, 0, 1, 2, 1, 0, 0, 2, 1,
					0xFF00_0000u, false, narrowPixels),
				new CyberGraphicsDisplayLayer(
					0x2100, true, 0, 0, 8, 1, 0, 0, 8, 1,
					0xFF00_0000u, false, widePixels)
			]);
		var logical = new uint[8];

		CopperScreenEmulator.ComposeRtgLogicalFrame(
			ReadOnlySpan<int>.Empty, 0, 0, composition, logical);

		Assert.Equal(new uint[] { 0xFF00_0001, 0xFF00_0002, 0xFF00_0003, 0xFF00_0004 }, logical[..4]);
		Assert.Equal(new uint[] { 0xFF10_0000, 0xFF20_0000, 0xFF00_0000, 0xFF00_0000 }, logical[4..]);
	}

	[Fact]
	public void TopRtgDpmsBlanksTheWholePhysicalCanvas()
	{
		var composition = new CyberGraphicsDisplayComposition(
			2,
			2,
			TopIsRtg: true,
			TopDpmsOff: true,
			[
				new CyberGraphicsDisplayLayer(
					0x2000, true, 0, 0, 2, 2, 0, 0, 2, 2,
					0xFFFF_FFFFu, true, Enumerable.Repeat(0xFFFF_FFFFu, 4).ToArray())
			]);
		var logical = new uint[4];

		CopperScreenEmulator.ComposeRtgLogicalFrame(
			ReadOnlySpan<int>.Empty, 0, 0, composition, logical);

		Assert.All(logical, pixel => Assert.Equal(0xFF00_0000u, pixel));
	}
}
