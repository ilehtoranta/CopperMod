namespace CopperMod.Amiga.Tests;

public sealed class CpuDeferredCustomAccessClassifierTests
{
	[Theory]
	[InlineData(0x0E0)]
	[InlineData(0x0F6)]
	[InlineData(0x120)]
	[InlineData(0x13E)]
	public void OcsBitplaneAndSpritePointerWritesAreTheOnlyInitialJournalCandidates(int offset)
	{
		Assert.Equal(
			CpuDeferredPeripheralAccess.JournalableWrite,
			CpuDeferredCustomAccessClassifier.ClassifyCustom(
				AmigaChipset.OcsPal,
				(ushort)offset,
				isWrite: true));
	}

	[Theory]
	[InlineData(0x004)] // VPOSR
	[InlineData(0x006)] // VHPOSR
	[InlineData(0x01E)] // INTREQR
	[InlineData(0x026)] // DSKBYTR
	[InlineData(0x00E)] // CLXDAT
	public void CpuVisibleCustomReadsRemainImmediateBarriers(int offset)
	{
		Assert.Equal(
			CpuDeferredPeripheralAccess.ImmediateBarrier,
			CpuDeferredCustomAccessClassifier.ClassifyCustom(
				AmigaChipset.OcsPal,
				(ushort)offset,
				isWrite: false));
	}

	[Theory]
	[InlineData(0x096)] // DMACON
	[InlineData(0x09A)] // INTENA
	[InlineData(0x09C)] // INTREQ
	[InlineData(0x09E)] // ADKCON
	[InlineData(0x08A)] // COPJMP2
	[InlineData(0x058)] // BLTSIZE
	[InlineData(0x024)] // DSKLEN
	[InlineData(0x100)] // BPLCON0
	[InlineData(0x180)] // COLOR00
	public void ScheduleAndCpuVisibleWritesRemainImmediateBarriers(int offset)
	{
		Assert.Equal(
			CpuDeferredPeripheralAccess.ImmediateBarrier,
			CpuDeferredCustomAccessClassifier.ClassifyCustom(
				AmigaChipset.OcsPal,
				(ushort)offset,
				isWrite: true));
	}

	[Fact]
	public void AbsentRegistersAreUnsupportedAndAllCiaAccessesRemainBarriers()
	{
		Assert.Equal(
			CpuDeferredPeripheralAccess.Unsupported,
			CpuDeferredCustomAccessClassifier.ClassifyCustom(
				AmigaChipset.OcsPal,
				0x1E4,
				isWrite: true));
		Assert.Equal(
			CpuDeferredPeripheralAccess.ImmediateBarrier,
			CpuDeferredCustomAccessClassifier.ClassifyCia(isWrite: false, register: 0x0D));
		Assert.Equal(
			CpuDeferredPeripheralAccess.ImmediateBarrier,
			CpuDeferredCustomAccessClassifier.ClassifyCia(isWrite: true, register: 0x0E));
	}
}
