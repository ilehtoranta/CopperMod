namespace CopperMod.Sid.Tests;

public sealed class Cia6526CycleTests
{
	[Fact]
	public void TimerAUnderflowsAfterLatchPlusOnePhi2Ticks()
	{
		var cia = CreateCia();
		cia.Write(0x04, 0x02);
		cia.Write(0x05, 0x00);
		cia.Write(0x0D, 0x81);
		cia.Write(0x0E, 0x11);

		Assert.False(cia.Tick());
		Assert.Equal(0x0001, cia.DebugState.TimerA);
		Assert.False(cia.Tick());
		Assert.Equal(0x0000, cia.DebugState.TimerA);
		Assert.True(cia.Tick());
		Assert.Equal(0x0002, cia.DebugState.TimerA);
		Assert.Equal(0x81, cia.Read(0x0D));
	}

	[Fact]
	public void ForceLoadCopiesLatchAndSelfClearsControlBit()
	{
		var cia = CreateCia();
		cia.Write(0x04, 0x34);
		cia.Write(0x05, 0x12);

		cia.Write(0x0E, 0x10);

		Assert.Equal(0x1234, cia.DebugState.TimerA);
		Assert.Equal(0x00, cia.DebugState.ControlA);
	}

	[Fact]
	public void TimerADoesNotCountPhi2WhenCntSourceIsSelected()
	{
		var cia = CreateCia();
		cia.Write(0x04, 0x01);
		cia.Write(0x05, 0x00);
		cia.Write(0x0D, 0x81);
		cia.Write(0x0E, 0x31);

		for (var i = 0; i < 8; i++)
		{
			Assert.False(cia.Tick());
		}

		Assert.Equal(0x0001, cia.DebugState.TimerA);
		Assert.Equal(0x00, cia.Read(0x0D));
	}

	[Fact]
	public void TimerBDoesNotCountPhi2WhenCntSourceIsSelected()
	{
		var cia = CreateCia();
		cia.Write(0x06, 0x01);
		cia.Write(0x07, 0x00);
		cia.Write(0x0D, 0x82);
		cia.Write(0x0F, 0x31);

		for (var i = 0; i < 8; i++)
		{
			Assert.False(cia.Tick());
		}

		Assert.Equal(0x0001, cia.DebugState.TimerB);
		Assert.Equal(0x00, cia.Read(0x0D));
	}

	[Fact]
	public void TimerBCountsTimerAUnderflowsOnlyInTimerASourceMode()
	{
		var cia = CreateCia();
		cia.Write(0x04, 0x00);
		cia.Write(0x05, 0x00);
		cia.Write(0x06, 0x01);
		cia.Write(0x07, 0x00);
		cia.Write(0x0D, 0x82);
		cia.Write(0x0E, 0x11);
		cia.Write(0x0F, 0x51);

		Assert.False(cia.Tick());
		Assert.Equal(0x0000, cia.DebugState.TimerB);
		Assert.True(cia.Tick());
		Assert.Equal(0x0001, cia.DebugState.TimerB);
		Assert.Equal(0x83, cia.Read(0x0D));
	}

	[Fact]
	public void TimerBTimerAAndCntSourceDoesNotCountWithoutExternalCnt()
	{
		var cia = CreateCia();
		cia.Write(0x04, 0x00);
		cia.Write(0x05, 0x00);
		cia.Write(0x06, 0x01);
		cia.Write(0x07, 0x00);
		cia.Write(0x0D, 0x82);
		cia.Write(0x0E, 0x11);
		cia.Write(0x0F, 0x71);

		for (var i = 0; i < 4; i++)
		{
			Assert.False(cia.Tick());
		}

		Assert.Equal(0x0001, cia.DebugState.TimerB);
		Assert.Equal(0x00, cia.Read(0x0D) & 0x02);
	}

	[Fact]
	public void TimerPortToggleModeTogglesPb6OnEachTimerAUnderflow()
	{
		var cia = CreateCia();
		cia.Write(0x01, 0x00);
		cia.Write(0x03, 0x40);
		cia.Write(0x04, 0x00);
		cia.Write(0x05, 0x00);
		cia.Write(0x0E, 0x17);

		Assert.Equal(0x00, cia.Read(0x01) & 0x40);
		cia.Tick();
		Assert.Equal(0x40, cia.Read(0x01) & 0x40);
		cia.Tick();
		Assert.Equal(0x00, cia.Read(0x01) & 0x40);
	}

	[Fact]
	public void TimerPortPulseModeIsHighOnlyForUnderflowCycle()
	{
		var cia = CreateCia();
		cia.Write(0x01, 0x00);
		cia.Write(0x03, 0x40);
		cia.Write(0x04, 0x01);
		cia.Write(0x05, 0x00);
		cia.Write(0x0E, 0x13);

		Assert.Equal(0x00, cia.Read(0x01) & 0x40);
		cia.Tick();
		Assert.Equal(0x00, cia.Read(0x01) & 0x40);
		cia.Tick();
		Assert.Equal(0x40, cia.Read(0x01) & 0x40);
		cia.Tick();
		Assert.Equal(0x00, cia.Read(0x01) & 0x40);
	}

	[Fact]
	public void InterruptMaskCanAssertPendingMaskedEvent()
	{
		var cia = CreateCia();

		cia.TriggerSerialInterrupt();
		Assert.False(cia.DebugState.InterruptLine);
		Assert.Equal(0x08, cia.DebugState.InterruptData);

		cia.Write(0x0D, 0x88);

		Assert.True(cia.DebugState.InterruptLine);
		Assert.Equal(0x88, cia.Read(0x0D));
		Assert.False(cia.DebugState.InterruptLine);
	}

	[Fact]
	public void InterruptMaskClearDeassertsLineWithoutClearingPendingData()
	{
		var cia = CreateCia();
		cia.Write(0x0D, 0x88);
		cia.TriggerSerialInterrupt();

		Assert.True(cia.DebugState.InterruptLine);

		cia.Write(0x0D, 0x08);

		Assert.False(cia.DebugState.InterruptLine);
		Assert.Equal(0x08, cia.DebugState.InterruptData);
		Assert.Equal(0x08, cia.Read(0x0D));
	}

	[Fact]
	public void TodHoursReadLatchesTenthsUntilTenthsReadReleasesLatch()
	{
		var cia = CreateCia(cpuCyclesPerSecond: 10);
		cia.Write(0x08, 0x01);
		cia.Write(0x0B, 0x01);

		Assert.Equal(0x01, cia.Read(0x0B));
		cia.Tick();

		Assert.Equal(0x01, cia.Read(0x08));
		Assert.Equal(0x02, cia.Read(0x08));
	}

	private static Cia6526 CreateCia(int cpuCyclesPerSecond = SidConstants.PalCpuCyclesPerSecond)
	{
		var cia = new Cia6526();
		cia.Reset(defaultTimerA60Hz: false, cpuCyclesPerSecond);
		return cia;
	}
}
