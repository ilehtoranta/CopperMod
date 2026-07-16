using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaCiaTests
{
	[Fact]
	public void CiaClockUsesIntegerCpuCycleGrid()
	{
		Assert.Equal(10, AmigaCia.CpuCyclesPerCiaTick);
		Assert.Equal(AmigaConstants.A500PalCpuCyclesPerCiaTick, AmigaCia.CpuCyclesPerCiaTick);
	}

	[Fact]
	public void TimerAUnderflowIsScheduledOnTheGlobalCpuTimebase()
	{
		var bus = new AmigaBus();

		bus.WriteByte(0x00BFD400, 0x03, 0);
		bus.WriteByte(0x00BFD500, 0x00, 0);
		bus.WriteByte(0x00BFDD00, 0x81, 0);
		bus.WriteByte(0x00BFDE00, 0x11, 0);

		Assert.Equal(40, bus.GetNextCiaInterruptCycle(100));

		bus.AdvanceCiasTo(39);
		Assert.Empty(bus.DrainCiaInterrupts());

		bus.AdvanceCiasTo(40);
		var interruptEvent = Assert.Single(bus.DrainCiaInterrupts());
		Assert.Equal(AmigaCiaId.B, interruptEvent.Cia);
		Assert.Equal(AmigaCia.TimerAInterruptMask, interruptEvent.IcrBits);
		Assert.Equal(40, interruptEvent.Cycle);
		Assert.Equal(70, bus.GetNextCiaInterruptCycle(100));
	}

	[Fact]
	public void TimerAZeroLatchUnderflowsAfterFullSixteenBitInterval()
	{
		var bus = new AmigaBus();
		var expectedCycle = 10 + (65_536L * AmigaCia.CpuCyclesPerCiaTick);

		bus.WriteByte(0x00BFD400, 0x00, 0);
		bus.WriteByte(0x00BFD500, 0x00, 0);
		bus.WriteByte(0x00BFDD00, 0x81, 0);
		bus.WriteByte(0x00BFDE00, 0x11, 0);

		Assert.Null(bus.GetNextCiaInterruptCycle(expectedCycle - 1));
		Assert.Equal(expectedCycle, bus.GetNextCiaInterruptCycle(expectedCycle));

		bus.AdvanceCiasTo(expectedCycle - 1);
		Assert.Empty(bus.DrainCiaInterrupts());

		bus.AdvanceCiasTo(expectedCycle);
		var interruptEvent = Assert.Single(bus.DrainCiaInterrupts());
		Assert.Equal(expectedCycle, interruptEvent.Cycle);
	}

	[Fact]
	public void CpuReadObservesPublishedCiaTimerState()
	{
		var bus = new AmigaBus();
		bus.WriteByte(0x00BFD400, 0x03, 0);
		bus.WriteByte(0x00BFD500, 0x00, 0);
		bus.WriteByte(0x00BFDE00, 0x11, 0);

		var cycle = 19L;
		Assert.Equal(0x03, bus.ReadByte(0x00BFD400, ref cycle, AmigaBusAccessKind.CpuDataRead));
		Assert.Equal(20, cycle);

		cycle = 20L;
		Assert.Equal(0x03, bus.ReadByte(0x00BFD400, ref cycle, AmigaBusAccessKind.CpuDataRead));
		Assert.Equal(30, cycle);

		bus.AdvanceCiaTimersTo(20);
		cycle = 20L;
		Assert.Equal(0x02, bus.ReadByte(0x00BFD400, ref cycle, AmigaBusAccessKind.CpuDataRead));
	}

	[Fact]
	public void CpuIcrReadObservesPublishedTimerUnderflowOnly()
	{
		var bus = new AmigaBus();
		bus.WriteByte(0x00BFD400, 0x01, 0);
		bus.WriteByte(0x00BFD500, 0x00, 0);
		bus.WriteByte(0x00BFDD00, 0x81, 0);
		bus.WriteByte(0x00BFDE00, 0x11, 0);

		var cycle = 10L;
		Assert.Equal(0x00, bus.ReadByte(0x00BFDD00, ref cycle, AmigaBusAccessKind.CpuDataRead));
		Assert.Equal(20, cycle);

		bus.AdvanceCiaTimersTo(20);
		cycle = 10L;
		Assert.Equal(0x81, bus.ReadByte(0x00BFDD00, ref cycle, AmigaBusAccessKind.CpuDataRead));
		Assert.Equal(0x00, bus.ReadByte(0x00BFDD00));
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
		Assert.Equal(30, interruptEvent.Cycle);
		Assert.Null(bus.GetNextCiaInterruptCycle(1_000));
	}

	[Fact]
	public void OneShotTimerHighByteWriteStartsTimerEvenWhenStartBitIsClear()
	{
		var bus = new AmigaBus();

		bus.WriteByte(0x00BFEF01, 0x08, 0);
		bus.WriteByte(0x00BFED01, 0x82, 0);
		bus.WriteByte(0x00BFE601, 0x03, 0);
		bus.WriteByte(0x00BFE701, 0x00, 0);

		Assert.Equal(40, bus.GetNextCiaInterruptCycle(100));

		bus.AdvanceCiasTo(39);
		Assert.Empty(bus.DrainCiaInterrupts());

		bus.AdvanceCiasTo(40);
		var interruptEvent = Assert.Single(bus.DrainCiaInterrupts());
		Assert.Equal(AmigaCiaId.A, interruptEvent.Cia);
		Assert.Equal(AmigaCia.TimerBInterruptMask, interruptEvent.IcrBits);
		Assert.Equal(40, interruptEvent.Cycle);
		Assert.Equal(0x08, bus.CiaA.ReadRegister(0x0F));
	}

	[Fact]
	public void InterruptControlRegisterReportsAndClearsPendingBits()
	{
		var bus = new AmigaBus();

		bus.WriteByte(0x00BFD400, 0x01, 0);
		bus.WriteByte(0x00BFD500, 0x00, 0);
		bus.WriteByte(0x00BFDD00, 0x81, 0);
		bus.WriteByte(0x00BFDE00, 0x11, 0);
		bus.AdvanceCiasTo(20);

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

		Assert.True(bus.AudioFilterEnabled);
		bus.WriteByte(0x00BFE001, 0x00, 0);
		Assert.True(bus.AudioFilterEnabled);
		bus.WriteByte(0x00BFE001, 0x02, 0);
		Assert.False(bus.AudioFilterEnabled);
	}

	[Fact]
	public void CiaPortsReadInputPinsWhenDataDirectionBitsAreInputs()
	{
		var cia = new AmigaCia(AmigaCiaId.B);
		cia.Reset();

		Assert.Equal(0xFF, cia.ReadRegister(1));

		cia.WriteRegister(1, 0x00, 0, new List<AmigaCiaInterruptEvent>());
		Assert.Equal(0xFF, cia.ReadRegister(1));

		cia.WriteRegister(3, 0x0F, 0, new List<AmigaCiaInterruptEvent>());
		Assert.Equal(0xF0, cia.ReadRegister(1));
	}

	[Fact]
	public void CiaBDataDirectionUpdateRefreshesDiskControlPins()
	{
		var bus = new AmigaBus();

		bus.WriteByte(0x00BFD100, 0x77, 0);
		Assert.False(bus.Disk.CaptureSnapshot().Selected);

		bus.WriteByte(0x00BFD300, 0xFF, 0);
		Assert.True(bus.Disk.CaptureSnapshot().Selected);
	}

	[Fact]
	public void CiaATodLowTicksOnPalVerticalBlank()
	{
		var bus = new AmigaBus();
		var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;

		Assert.Equal(0x00, bus.ReadByte(0x00BFE801));

		bus.AdvanceRasterTo(frameCycles);

		Assert.Equal(0x01, bus.ReadByte(0x00BFE801));
	}

	[Fact]
	public void CiaBTodLowTicksOnPalHorizontalSync()
	{
		var bus = new AmigaBus();
		var lineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;

		Assert.Equal(0x00, bus.ReadByte(0x00BFD800));

		bus.AdvanceRasterTo(lineCycles - 1);
		Assert.Equal(0x00, bus.ReadByte(0x00BFD800));

		bus.AdvanceRasterTo(lineCycles);
		Assert.Equal(0x01, bus.ReadByte(0x00BFD800));
	}

	[Fact]
	public void CiaTodCounterCarriesAcrossBinaryBytes()
	{
		var cia = new AmigaCia(AmigaCiaId.A);
		cia.Reset();
		var events = new List<AmigaCiaInterruptEvent>();

		cia.WriteRegister(0x08, 0xFF, 0, events);
		cia.WriteRegister(0x09, 0x00, 0, events);
		cia.WriteRegister(0x0A, 0x00, 0, events);
		cia.IncrementTod(100, events);

		Assert.Equal(0x00, cia.ReadRegister(0x08));
		Assert.Equal(0x01, cia.ReadRegister(0x09));
		Assert.Equal(0x00, cia.ReadRegister(0x0A));
	}

	[Fact]
	public void CiaTodHighMidLowReadReturnsLatchedCounter()
	{
		var cia = new AmigaCia(AmigaCiaId.A);
		cia.Reset();
		var events = new List<AmigaCiaInterruptEvent>();

		cia.WriteRegister(0x08, 0xFE, 0, events);
		cia.WriteRegister(0x09, 0x00, 0, events);
		cia.WriteRegister(0x0A, 0x00, 0, events);

		Assert.Equal(0x00, cia.ReadRegister(0x0A));
		cia.IncrementTod(100, events);
		cia.IncrementTod(200, events);

		Assert.Equal(0x00, cia.ReadRegister(0x09));
		Assert.Equal(0xFE, cia.ReadRegister(0x08));
		Assert.Equal(0x01, cia.ReadRegister(0x09));
	}

	[Fact]
	public void KeyboardKeyDownQueuesCiaASerialDataInterrupt()
	{
		var bus = new AmigaBus();
		bus.AbleCiaInterrupts(AmigaCiaId.A, 0x80 | AmigaCia.SerialInterruptMask, 0);

		bus.Keyboard.KeyDown(AmigaRawKey.Return, 100);

		var interruptEvent = Assert.Single(bus.DrainCiaInterrupts());
		Assert.Equal(AmigaCiaId.A, interruptEvent.Cia);
		Assert.Equal(AmigaCia.SerialInterruptMask, interruptEvent.IcrBits);
		Assert.Equal(100, interruptEvent.Cycle);
		var serialData = bus.ReadByte(0x00BFEC01);
		Assert.Equal(AmigaKeyboard.EncodeSerialData((byte)AmigaRawKey.Return), serialData);
		Assert.Equal((byte)AmigaRawKey.Return, AmigaKeyboard.DecodeSerialData(serialData));
	}

	[Fact]
	public void KeyboardSerialInterruptDispatchesPortsIntreqAfterRecognitionDelay()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		machine.Bus.WriteLong(0x68, 0x0000_2000);
		machine.Cpu.Reset(0x1000, 0x3000);
		machine.Cpu.State.StatusRegister = (ushort)(machine.Cpu.State.StatusRegister & 0xF8FF);
		machine.Bus.WriteWord(0x00DFF09A, (ushort)(0xC000 | AmigaConstants.IntreqPorts));
		machine.Bus.Paula.AdvanceTo(0);
		machine.Bus.AbleCiaInterrupts(AmigaCiaId.A, 0x80 | AmigaCia.SerialInterruptMask, 0);

		machine.Bus.Keyboard.KeyDown(AmigaRawKey.Digit1, 100);

		Assert.False(machine.DispatchPendingHardwareInterrupt());
		var releaseCycle = 100 + AmigaConstants.A500IntreqToIplDelayCpuCycles;
		machine.Cpu.State.Cycles = releaseCycle;
		machine.Bus.Paula.AdvanceTo(releaseCycle);
		Assert.True(machine.DispatchPendingHardwareInterrupt());
		Assert.NotEqual(0, machine.Bus.ReadWord(0x00DFF01E) & AmigaConstants.IntreqPorts);
		Assert.Equal(0x0000_2000u, machine.Cpu.State.ProgramCounter);
		Assert.Equal(2, (machine.Cpu.State.StatusRegister >> 8) & 7);
	}

	[Fact]
	public void CiaFlagPulseQueuesInterruptWhenEnabled()
	{
		var cia = new AmigaCia(AmigaCiaId.B);
		cia.Reset();
		var events = new List<AmigaCiaInterruptEvent>();
		cia.AbleInterrupts(0x80 | AmigaCia.FlagInterruptMask, 0, events);

		cia.PulseFlag(200, events);

		var interruptEvent = Assert.Single(events);
		Assert.Equal(AmigaCiaId.B, interruptEvent.Cia);
		Assert.Equal(AmigaCia.FlagInterruptMask, interruptEvent.IcrBits);
		Assert.Equal(200, interruptEvent.Cycle);
	}

	[Fact]
	public void KeyboardReleaseUsesHighBitAndWaitsForSerialReadAcknowledge()
	{
		var bus = new AmigaBus();
		bus.AbleCiaInterrupts(AmigaCiaId.A, 0x80 | AmigaCia.SerialInterruptMask, 0);

		bus.Keyboard.KeyDown(AmigaRawKey.Space, 10);
		bus.Keyboard.KeyUp(AmigaRawKey.Space, 20);
		Assert.Single(bus.DrainCiaInterrupts());

		Assert.Equal((byte)AmigaRawKey.Space, AmigaKeyboard.DecodeSerialData(bus.ReadByte(0x00BFEC01)));
		var releaseEvent = Assert.Single(bus.DrainCiaInterrupts());
		Assert.Equal(AmigaCia.SerialInterruptMask, releaseEvent.IcrBits);
		Assert.Equal((byte)((byte)AmigaRawKey.Space | 0x80), AmigaKeyboard.DecodeSerialData(bus.ReadByte(0x00BFEC01)));
	}

	[Fact]
	public void KeyboardDoesNotRepeatHeldKeyDown()
	{
		var bus = new AmigaBus();
		bus.AbleCiaInterrupts(AmigaCiaId.A, 0x80 | AmigaCia.SerialInterruptMask, 0);

		bus.Keyboard.KeyDown(AmigaRawKey.A, 1);
		bus.Keyboard.KeyDown(AmigaRawKey.A, 2);

		Assert.Equal((byte)AmigaRawKey.A, AmigaKeyboard.DecodeSerialData(bus.ReadByte(0x00BFEC01)));
		Assert.Equal(0, bus.Keyboard.QueuedRawKeys);
		Assert.Single(bus.DrainCiaInterrupts());
	}
}
