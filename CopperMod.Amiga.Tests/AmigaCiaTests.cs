using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaCiaTests
{
	[Fact]
	public void TimerAUnderflowIsScheduledOnTheGlobalCpuTimebase()
	{
		var bus = new AmigaBus();

		bus.WriteByte(0x00BFD400, 0x03, 0);
		bus.WriteByte(0x00BFD500, 0x00, 0);
		bus.WriteByte(0x00BFDD00, 0x81, 0);
		bus.WriteByte(0x00BFDE00, 0x11, 0);

		Assert.Equal(30, bus.GetNextCiaInterruptCycle(100));

		bus.AdvanceCiasTo(29);
		Assert.Empty(bus.DrainCiaInterrupts());

		bus.AdvanceCiasTo(30);
		var interruptEvent = Assert.Single(bus.DrainCiaInterrupts());
		Assert.Equal(AmigaCiaId.B, interruptEvent.Cia);
		Assert.Equal(AmigaCia.TimerAInterruptMask, interruptEvent.IcrBits);
		Assert.Equal(30, interruptEvent.Cycle);
		Assert.Equal(60, bus.GetNextCiaInterruptCycle(100));
	}

	[Fact]
	public void TimerOneShotStopsAfterFirstUnderflow()
	{
		var bus = new AmigaBus();

		bus.WriteByte(0x00BFD400, 0x02, 0);
		bus.WriteByte(0x00BFD500, 0x00, 0);
		bus.WriteByte(0x00BFDD00, 0x81, 0);
		bus.WriteByte(0x00BFDE00, 0x19, 0);
		bus.AdvanceCiasTo(100);

		var interruptEvent = Assert.Single(bus.DrainCiaInterrupts());
		Assert.Equal(20, interruptEvent.Cycle);
		Assert.Null(bus.GetNextCiaInterruptCycle(1_000));
	}

	[Fact]
	public void InterruptControlRegisterReportsAndClearsPendingBits()
	{
		var bus = new AmigaBus();

		bus.WriteByte(0x00BFD400, 0x01, 0);
		bus.WriteByte(0x00BFD500, 0x00, 0);
		bus.WriteByte(0x00BFDD00, 0x81, 0);
		bus.WriteByte(0x00BFDE00, 0x11, 0);
		bus.AdvanceCiasTo(10);

		Assert.Equal(0x81, bus.ReadByte(0x00BFDD00));
		Assert.Equal(0x00, bus.ReadByte(0x00BFDD00));
	}

	[Fact]
	public void ResourceStyleAbleAndSetIcrUpdateMaskAndPendingState()
	{
		var bus = new AmigaBus();

		Assert.Equal(0x00, bus.AbleCiaInterrupts(AmigaCiaId.B, 0x81, 0));
		Assert.Equal(AmigaCia.TimerAInterruptMask, bus.CiaB.InterruptMask);

		Assert.Equal(0x00, bus.SetCiaInterrupts(AmigaCiaId.B, 0x81, 10));
		var interruptEvent = Assert.Single(bus.DrainCiaInterrupts());
		Assert.Equal(10, interruptEvent.Cycle);
		Assert.Equal(0x81, bus.ReadByte(0x00BFDD00));
	}

	[Fact]
	public void CiaAPortAControlsAudioFilterLedBit()
	{
		var bus = new AmigaBus();

		Assert.False(bus.AudioFilterEnabled);
		bus.WriteByte(0x00BFE001, 0x00, 0);
		Assert.True(bus.AudioFilterEnabled);
		bus.WriteByte(0x00BFE001, 0x02, 0);
		Assert.False(bus.AudioFilterEnabled);
	}
}
