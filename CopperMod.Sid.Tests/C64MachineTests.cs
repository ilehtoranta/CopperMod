using Copper6510;

namespace CopperMod.Sid.Tests;

public sealed class C64MachineTests
{
	[Fact]
	public void CiaInterruptLineStaysAssertedUntilInterruptRegisterIsRead()
	{
		var cia = new Cia6526();
		cia.Reset(defaultTimerA60Hz: false);
		cia.Write(0x04, 0x02);
		cia.Write(0x05, 0x00);
		cia.Write(0x0D, 0x81);
		cia.Write(0x0E, 0x11);

		Assert.False(cia.Tick());
		Assert.False(cia.Tick());
		Assert.True(cia.Tick());
		Assert.True(cia.Tick());
		Assert.Equal(0x81, cia.Read(0x0D));
		Assert.False(cia.Tick());
	}

	[Fact]
	public void CiaOneShotTimerStopsAfterUnderflow()
	{
		var cia = new Cia6526();
		cia.Reset(defaultTimerA60Hz: false);
		cia.Write(0x04, 0x01);
		cia.Write(0x05, 0x00);
		cia.Write(0x0D, 0x81);
		cia.Write(0x0E, 0x19);

		Assert.False(cia.Tick());
		Assert.True(cia.Tick());

		Assert.Equal(0x08, cia.DebugState.ControlA);
	}

	[Fact]
	public void CiaTimerBCanCountTimerAUnderflows()
	{
		var cia = new Cia6526();
		cia.Reset(defaultTimerA60Hz: false);
		cia.Write(0x04, 0x01);
		cia.Write(0x05, 0x00);
		cia.Write(0x06, 0x01);
		cia.Write(0x07, 0x00);
		cia.Write(0x0E, 0x11);
		cia.Write(0x0F, 0x51);

		for (var i = 0; i < 4; i++)
		{
			cia.Tick();
		}

		Assert.Equal(0x02, cia.Read(0x0D) & 0x02);
	}

	[Fact]
	public void CiaPortsUseDataDirectionAndPullups()
	{
		var cia = new Cia6526();
		cia.Reset(defaultTimerA60Hz: false);
		cia.Write(0x02, 0x0F);
		cia.Write(0x00, 0x05);

		Assert.Equal(0xF5, cia.Read(0x00));
	}

	[Fact]
	public void CiaTodAlarmAndSerialInterruptsUseIcrSemantics()
	{
		var cia = new Cia6526();
		cia.Reset(defaultTimerA60Hz: false, SidConstants.PalCpuCyclesPerSecond);
		cia.Write(0x0F, 0x80);
		cia.Write(0x08, 0x01);
		cia.Write(0x09, 0x00);
		cia.Write(0x0A, 0x00);
		cia.Write(0x0B, 0x01);
		cia.Write(0x0F, 0x00);
		cia.Write(0x0E, 0x80);
		cia.Write(0x0D, 0x84);

		for (var i = 0; i < SidIntegerMath.DivRoundNearest(SidConstants.PalCpuCyclesPerSecond, 10); i++)
		{
			cia.Tick();
		}

		Assert.Equal(0x84, cia.Read(0x0D));

		cia.Write(0x0D, 0x88);
		cia.TriggerSerialInterrupt();

		Assert.Equal(0x88, cia.Read(0x0D));
	}

	[Fact]
	public void VicRasterInterruptHighBitFollowsPendingMaskedSources()
	{
		var vic = new VicII(C64ClockProfile.FromSidClock(SidClock.Pal));
		vic.Reset();
		vic.Write(0x12, 0x01);
		vic.Write(0x1A, 0x01);

		for (var i = 0; i < SidConstants.PalCyclesPerFrame / 312; i++)
		{
			vic.Tick();
		}

		Assert.Equal(0x81, vic.Read(0x19));
		vic.Write(0x19, 0x01);
		Assert.Equal(0x00, vic.Read(0x19));
	}

	[Fact]
	public void VicUsesExplicitPalAndNtscRasterGeometry()
	{
		var pal = new VicII(C64ClockProfile.FromSidClock(SidClock.Pal));
		var ntsc = new VicII(C64ClockProfile.FromSidClock(SidClock.Ntsc));
		pal.Reset();
		ntsc.Reset();

		for (var i = 0; i < 63; i++)
		{
			pal.Tick();
		}

		for (var i = 0; i < 65; i++)
		{
			ntsc.Tick();
		}

		Assert.Equal(1, pal.DebugState.RasterLine);
		Assert.Equal(0, pal.DebugState.RasterCycle);
		Assert.Equal(1, ntsc.DebugState.RasterLine);
		Assert.Equal(0, ntsc.DebugState.RasterCycle);
	}

	[Fact]
	public void VicRasterCompareUsesD011HighBitAndDoesNotRetriggerOnSameLineAfterAck()
	{
		var vic = new VicII(C64ClockProfile.FromSidClock(SidClock.Pal));
		vic.Reset();
		vic.Write(0x19, 0x01);
		vic.Write(0x1A, 0x01);
		vic.Write(0x11, 0x80);
		vic.Write(0x12, 0x00);

		Assert.Equal(0x0100, vic.DebugState.RasterCompare);

		vic.Write(0x11, 0x00);
		vic.Write(0x12, 0x00);

		Assert.Equal(0x81, vic.Read(0x19));
		vic.Write(0x19, 0x01);
		Assert.Equal(0x00, vic.Read(0x19));
		vic.Tick();
		Assert.Equal(0x00, vic.Read(0x19));
	}

	[Fact]
	public void MemoryBankingControlsRomIoAndWritesUnderRom()
	{
		var machine = CreateRsidMachine(new byte[] { 0x60 });

		machine.Write(0xA000, 0x42, 0);
		Assert.Equal(0x60, machine.Read(0xA000));

		machine.Write(0x0001, 0x34, 0);
		Assert.Equal(0x42, machine.Read(0xA000));
		Assert.False(machine.DebugState.MemoryBank.IoVisible);

		machine.Write(0xD000, 0x12, 0);
		machine.Write(0x0001, 0x33, 0);
		Assert.Equal(0x3C, machine.Read(0xD000));
		Assert.True(machine.DebugState.MemoryBank.CharacterVisible);
	}

	[Fact]
	public void SidWritesAreIgnoredWhenIoIsBankedOut()
	{
		var machine = CreateRsidMachine(new byte[] { 0x60 });

		machine.Write(0xD418, 0x0F, 0);
		machine.Write(0x0001, 0x34, 0);
		machine.Write(0xD418, 0x08, 0);

		Assert.Single(machine.SidWrites);
		Assert.Equal(0x0F, machine.SidWrites[0].Value);
	}

	[Fact]
	public void PsidSidWritesIgnoreProcessorPortIoBanking()
	{
		var module = SidParser.Parse(SidFixtureBuilder.CreatePsid(
			new byte[]
			{
				0xA9, 0x30,       // LDA #$30
				0x8D, 0x01, 0x00, // STA $0001; bank I/O out on a real C64
				0x60,             // RTS
				0xA9, 0x08,       // LDA #$08
				0x8D, 0x18, 0xD4, // STA $D418
				0x60              // RTS
			},
			loadAddress: 0x1000,
			initAddress: 0x1000,
			playAddress: 0x1006,
			songs: 1,
			startSong: 1,
			flags: (1 << 2) | (1 << 4)));
		var machine = new C64Machine(module);
		machine.Reset(0);

		machine.BeginFrame();

		var write = Assert.Single(machine.SidWrites);
		Assert.Equal(0x18, write.Register);
		Assert.Equal(0x08, write.Value);
	}

	[Fact]
	public void PsidResetInitializesDocumentedPalVbiEnvironment()
	{
		var module = SidParser.Parse(SidFixtureBuilder.CreatePsid(
			new byte[] { 0x60 },
			playAddress: 0,
			songs: 1,
			startSong: 1,
			speed: 0,
			flags: (1 << 2) | (1 << 4)));
		var machine = new C64Machine(module);

		machine.Reset(0);

		var debug = machine.DebugState;
		Assert.Equal(0x01, machine.Ram[0x02A6]);
		Assert.Equal(0x4025, debug.Cia1.TimerALatch);
		Assert.Equal(0x11, debug.Cia1.ControlA);
		Assert.Equal(0x00, debug.Cia1.InterruptMask);
		Assert.Equal(0x01, debug.Vic.IrqMask);
		Assert.Equal(0x1B, machine.Read(0xD011));
		Assert.Equal(0x0F, machine.Sid.GetRegisterChipDebugState(0).ForwardedRegisters[0x18]);
	}

	[Fact]
	public void PsidResetInitializesDocumentedNtscCiaEnvironment()
	{
		var module = SidParser.Parse(SidFixtureBuilder.CreatePsid(
			new byte[] { 0x60 },
			playAddress: 0,
			songs: 1,
			startSong: 1,
			speed: 1,
			flags: (2 << 2) | (1 << 4)));
		var machine = new C64Machine(module);

		machine.Reset(0);

		var debug = machine.DebugState;
		Assert.Equal(0x00, machine.Ram[0x02A6]);
		Assert.Equal(0x4295, debug.Cia1.TimerALatch);
		Assert.Equal(0x01, debug.Cia1.ControlA);
		Assert.Equal(0x01, debug.Cia1.InterruptMask);
		Assert.Equal(0x00, debug.Vic.IrqMask);
		Assert.Equal(0x1B, machine.Read(0xD011));
		Assert.Equal(0x0F, machine.Sid.GetRegisterChipDebugState(0).ForwardedRegisters[0x18]);
	}

	[Fact]
	public void PsidWritesDocumentedBankRegisterBeforeInitAndPlayCalls()
	{
		var module = SidParser.Parse(SidFixtureBuilder.CreatePsid(
			new byte[]
			{
				0x60, // init at $9FFC
				0xEA,
				0xEA,
				0xEA,
				0x60  // play at $A000
			},
			loadAddress: 0x9FFC,
			initAddress: 0x9FFC,
			playAddress: 0xA000,
			songs: 1,
			startSong: 1,
			flags: (1 << 2) | (1 << 4)));
		var machine = new C64Machine(module);

		machine.Reset(0);
		Assert.Equal(0x37, machine.DebugState.MemoryBank.Value);

		machine.BeginFrame();
		Assert.Equal(0x36, machine.DebugState.MemoryBank.Value);
	}

	[Theory]
	[MemberData(nameof(CpuSidWriteCases))]
	public void CpuWritesReachSidOnExpectedBusCycleAndForwardOnNextSidCycle(CpuSidWriteCase testCase)
	{
		var machine = CreateInstructionMachine(testCase.Program, testCase.A, testCase.X, testCase.Y);
		if (testCase.PointerLocation.HasValue && testCase.PointerTarget.HasValue)
		{
			WriteZeroPagePointer(machine.Ram, testCase.PointerLocation.Value, testCase.PointerTarget.Value);
		}

		var trace = new SidCycleTrace();
		machine.Sid.Trace = trace;

		machine.RunCycles(testCase.TotalCycles);

		var write = Assert.Single(machine.SidWrites);
		Assert.Equal(testCase.ExpectedWriteCycle, write.Cycle);
		Assert.Equal(0, write.ChipIndex);
		Assert.Equal(testCase.ExpectedRegister, write.Register);
		Assert.Equal(testCase.ExpectedValue, write.Value);

		Assert.DoesNotContain(trace.Frames, frame =>
			frame.Cycle == testCase.ExpectedWriteCycle &&
			frame.VoiceIndex == testCase.ExpectedRegister / 7 &&
			frame.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite));

		var forwarded = Assert.Single(trace.Frames, frame =>
			frame.Cycle == testCase.ExpectedWriteCycle + 1 &&
			frame.VoiceIndex == testCase.ExpectedRegister / 7 &&
			frame.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite));
		Assert.Equal(testCase.ExpectedWriteCycle + 1, forwarded.Cycle);
	}

	[Fact]
	public void CpuReadModifyWriteSidRegisterForwardsDummyAndFinalWritesOnFollowingCycles()
	{
		var machine = CreateInstructionMachine(new byte[] { 0x0E, 0x00, 0xD4 }, a: 0, x: 0, y: 0);
		var trace = new SidCycleTrace();
		machine.Sid.Trace = trace;

		machine.RunCycles(6);

		Assert.Equal(2, machine.SidWrites.Count);
		Assert.Equal(4, machine.SidWrites[0].Cycle);
		Assert.Equal(0x00, machine.SidWrites[0].Register);
		Assert.Equal(0x00, machine.SidWrites[0].Value);
		Assert.Equal(5, machine.SidWrites[1].Cycle);
		Assert.Equal(0x00, machine.SidWrites[1].Register);
		Assert.Equal(0x00, machine.SidWrites[1].Value);

		var forwardedCycles = trace.Frames
			.Where(frame => frame.VoiceIndex == 0 && frame.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite))
			.Select(frame => frame.Cycle)
			.ToArray();
		Assert.Equal([5L, 6L], forwardedCycles);
	}

	[Fact]
	public void RenderFrameSampleTargetsPreserveSidForwardingBoundary()
	{
		var machine = CreateRsidMachine(new byte[] { 0x60 });
		var trace = new SidCycleTrace();
		machine.Sid.Trace = trace;
		Assert.True(machine.Sid.TryWrite(0xD404, 0x21, 100));

		var buffer = new float[2];
		machine.RenderFrame(
			buffer,
			new AudioRenderOptionsAdapter(sampleRate: 44100, channelCount: 1),
			[100L, 101L],
			cycleCount: 101);

		var forwarded = trace.Frames
			.Where(frame => frame.VoiceIndex == 0 && frame.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite))
			.Select(frame => frame.Cycle)
			.ToArray();
		Assert.Equal([101L], forwarded);

		var sameCycle = Assert.Single(trace.Frames, frame => frame.Cycle == 100 && frame.VoiceIndex == 0);
		Assert.Equal(0x00, sameCycle.Control);
		Assert.False(sameCycle.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite));

		var nextCycle = Assert.Single(trace.Frames, frame => frame.Cycle == 101 && frame.VoiceIndex == 0);
		Assert.Equal(0x21, nextCycle.Control);
		Assert.True(nextCycle.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite));
	}

	[Fact]
	public void CpuReadOfSidEnvelopeRegisterSamplesAtExpectedBusCycle()
	{
		var machine = CreateInstructionMachine(
			new byte[] { 0xEA, 0xEA, 0xEA, 0xEA, 0xAD, 0x1C, 0xD4 },
			a: 0,
			x: 0,
			y: 0);
		var trace = new SidCycleTrace();
		machine.Sid.Trace = trace;
		Assert.True(machine.Sid.TryWrite(0xD413, 0x00, 0));
		Assert.True(machine.Sid.TryWrite(0xD414, 0xF0, 0));
		Assert.True(machine.Sid.TryWrite(0xD412, 0x11, 0));

		machine.RunCycles(12);

		Assert.Equal(0x01, machine.Cpu.A);
		Assert.Contains(trace.Frames, frame => frame.Cycle == 11 && frame.VoiceIndex == 2 && frame.EnvelopeCounter == 1);
		Assert.Equal(3, machine.Sid.Chips[0].DebugState.Voices[2].RateCounter);
	}

	[Fact]
	public void CpuReadOfSidOscillatorRegisterSamplesAtExpectedBusCycle()
	{
		var machine = CreateInstructionMachine(new byte[] { 0xAD, 0x1B, 0xD4 }, a: 0, x: 0, y: 0);
		Assert.True(machine.Sid.TryWrite(0xD40E, 0x00, 0));
		Assert.True(machine.Sid.TryWrite(0xD40F, 0x80, 0));
		Assert.True(machine.Sid.TryWrite(0xD412, 0x20, 0));

		machine.RunCycles(4);

		Assert.Equal(0x56, machine.Cpu.A);
		Assert.Equal(0x575555u, machine.Sid.Chips[0].DebugState.Voices[2].Accumulator);
	}

	[Fact]
	public void CpuSidPotPollingDoesNotAdvanceMainSidAudioTimeline()
	{
		var machine = CreateInstructionMachine(new byte[] { 0xEA }, a: 0, x: 0, y: 0);
		Assert.True(machine.Sid.TryWrite(0xD418, 0x0F, 4));
		var before = machine.Sid.CaptureTimingSnapshot();

		Assert.Equal(0x0F, machine.Read(0xD418, cycleOffset: 4));
		Assert.Equal(0xFF, machine.Read(0xD419, cycleOffset: 4));

		var after = machine.Sid.CaptureTimingSnapshot();
		Assert.Equal(before.AudioCycle, after.AudioCycle);
		Assert.Equal(before.SampleCycles, after.SampleCycles);
		Assert.Equal(before.SampleAccumulator, after.SampleAccumulator);
		Assert.Equal(0, after.RegisterCycle);
	}

	[Fact]
	public void CpuSidOscillatorPollingDoesNotAdvanceMainSidAudioTimeline()
	{
		var machine = CreateInstructionMachine(new byte[] { 0xEA }, a: 0, x: 0, y: 0);
		Assert.True(machine.Sid.TryWrite(0xD40E, 0x00, 0));
		Assert.True(machine.Sid.TryWrite(0xD40F, 0x80, 0));
		Assert.True(machine.Sid.TryWrite(0xD412, 0x20, 0));
		var before = machine.Sid.CaptureTimingSnapshot();

		Assert.Equal(0x56, machine.Read(0xD41B, cycleOffset: 4));
		Assert.Equal(0x56, machine.Read(0xD41B, cycleOffset: 4));

		var after = machine.Sid.CaptureTimingSnapshot();
		Assert.Equal(before.AudioCycle, after.AudioCycle);
		Assert.Equal(before.SampleCycles, after.SampleCycles);
		Assert.Equal(before.SampleAccumulator, after.SampleAccumulator);
		Assert.Equal(4, after.RegisterCycle);
	}

	public static IEnumerable<object[]> CpuSidWriteCases()
	{
		yield return CpuSidWrite(
			"STA abs",
			[0x8D, 0x04, 0xD4],
			totalCycles: 4,
			expectedWriteCycle: 3,
			expectedRegister: 0x04,
			expectedValue: 0x21,
			a: 0x21);
		yield return CpuSidWrite(
			"STA abs,X",
			[0x9D, 0x04, 0xD4],
			totalCycles: 5,
			expectedWriteCycle: 4,
			expectedRegister: 0x04,
			expectedValue: 0x22,
			a: 0x22);
		yield return CpuSidWrite(
			"STA (zpg),Y",
			[0x91, 0x20],
			totalCycles: 6,
			expectedWriteCycle: 5,
			expectedRegister: 0x04,
			expectedValue: 0x23,
			a: 0x23,
			pointerLocation: 0x20,
			pointerTarget: 0xD404);
		yield return CpuSidWrite(
			"SAX abs",
			[0x8F, 0x00, 0xD4],
			totalCycles: 4,
			expectedWriteCycle: 3,
			expectedRegister: 0x00,
			expectedValue: 0xD5,
			a: 0xF7,
			x: 0xD5);
	}

	[Fact]
	public void IrqIsIgnoredUntilInterruptDisableFlagIsCleared()
	{
		var machine = CreateRsidMachine(IrqTimerProgram());

		machine.RunCycles(200);
		Assert.DoesNotContain(machine.SidWrites, write => write.Register == 0x18 && write.Value == 0x0F);

		var trace = new CpuBusTrace();
		machine.CpuBusTrace = trace;
		machine.Cpu.Status &= 0xFB;
		machine.RunCycles(200);

		var sidWrite = machine.SidWrites.First(write => write.Register == 0x18 && write.Value == 0x0F);
		Assert.Equal(C64InterruptSource.Cia1, machine.DebugState.LastInterruptSource);
		var entryStart = trace.Frames
			.Select((frame, index) => (frame, index))
			.First(item => item.frame.Kind == Mos6510BusAccessKind.DiscardedOpcodeFetch)
			.index;
		var entry = trace.Frames.Skip(entryStart).Take(7).ToArray();
		Assert.Equal(
			new[]
			{
				Mos6510BusAccessKind.DiscardedOpcodeFetch,
				Mos6510BusAccessKind.DummyRead,
				Mos6510BusAccessKind.StackWrite,
				Mos6510BusAccessKind.StackWrite,
				Mos6510BusAccessKind.StackWrite,
				Mos6510BusAccessKind.VectorRead,
				Mos6510BusAccessKind.VectorRead
			},
			entry.Select(frame => frame.Kind.GetValueOrDefault()));
		Assert.Equal(new ushort[] { 0xFFFE, 0xFFFF }, entry.Skip(5).Select(frame => frame.Address.GetValueOrDefault()));
		Assert.Equal(entry[0].Cycle + 6, entry[6].Cycle);
		Assert.Equal(entry[6].Cycle + 38, sidWrite.Cycle);
	}

	[Fact]
	public void Cia2NmiRunsDespiteInterruptDisableFlag()
	{
		var machine = CreateRsidMachine(NmiTimerProgram());

		machine.Cpu.Status |= 0x04;
		var trace = new CpuBusTrace();
		machine.CpuBusTrace = trace;
		machine.RunCycles(200);

		var sidWrite = machine.SidWrites.First(write => write.Register == 0x18 && write.Value == 0x0E);
		Assert.Equal(C64InterruptSource.Cia2, machine.DebugState.LastInterruptSource);
		var entryStart = trace.Frames
			.Select((frame, index) => (frame, index))
			.First(item => item.frame.Kind == Mos6510BusAccessKind.DiscardedOpcodeFetch)
			.index;
		var entry = trace.Frames.Skip(entryStart).Take(7).ToArray();
		var vectorReads = trace.Frames
			.Where(frame => frame.Kind == Mos6510BusAccessKind.VectorRead)
			.Take(2)
			.Select(frame => frame.Address.GetValueOrDefault())
			.ToArray();
		Assert.Equal(new ushort[] { 0xFFFA, 0xFFFB }, vectorReads);
		Assert.Equal(entry[6].Cycle + 15, sidWrite.Cycle);
	}

	[Fact]
	public void RtiDummyReadCanAcknowledgeCia2NmiRegister()
	{
		var machine = CreateInstructionMachine(new byte[] { 0xEA, 0x4C, 0x00, 0x20 }, a: 0, x: 0, y: 0);
		machine.Write(0x0001, 0x35, 0);
		machine.Write(0xFFFA, 0x0C, 0);
		machine.Write(0xFFFB, 0xDD, 0);
		machine.Write(0xDD0C, 0x40, 0);
		machine.Write(0xDD04, 0x02, 0);
		machine.Write(0xDD05, 0x00, 0);
		machine.Write(0xDD0D, 0x81, 0);
		machine.Write(0xDD0E, 0x19, 0);
		var trace = new CpuBusTrace();
		machine.CpuBusTrace = trace;

		machine.RunCycles(80);

		Assert.Equal(C64InterruptSource.Cia2, machine.DebugState.LastInterruptSource);
		Assert.False(machine.DebugState.Cia2.InterruptLine);
		Assert.False(machine.DebugState.Cia2NmiLine);
		var acknowledge = Assert.Single(trace.Frames.Where(frame =>
			frame.Address == 0xDD0D &&
			frame.Kind == Mos6510BusAccessKind.DummyRead));
		Assert.True(acknowledge.CpuAdvanced);
		Assert.True(acknowledge.Ready);
		Assert.True(acknowledge.BusAvailable);
		Assert.False(acknowledge.VicOwned);
	}

	[Fact]
	public void CiaInterruptRegisterReadRefreshesNmiLineAtReadCycle()
	{
		var machine = CreateInstructionMachine(new byte[] { 0xEA }, a: 0, x: 0, y: 0);
		machine.Write(0xDD04, 0x01, 0);
		machine.Write(0xDD05, 0x00, 0);
		machine.Write(0xDD0D, 0x81, 0);
		machine.Write(0xDD0E, 0x11, 0);
		machine.AdvanceNativeCycles(2);

		Assert.True(machine.DebugState.Cia2NmiLine);

		var value = machine.Read(0xDD0D, 0);

		Assert.Equal(0x81, value);
		Assert.False(machine.DebugState.Cia2NmiLine);
	}

	[Fact]
	public void SyntheticCallSuppressesInterruptsWithoutRearmingHeldNmi()
	{
		var machine = CreateInstructionMachine(new byte[] { 0xEA }, a: 0, x: 0, y: 0);
		machine.Write(0xDD04, 0x01, 0);
		machine.Write(0xDD05, 0x00, 0);
		machine.Write(0xDD0D, 0x81, 0);
		machine.Write(0xDD0E, 0x11, 0);
		machine.AdvanceNativeCycles(2);
		Assert.True(machine.DebugState.Cia2NmiLine);

		// Establish the CPU's delivered NMI level as high and consume that edge.
		machine.Cpu.SetNmiLine(true);
		for (var cycle = 0; cycle < 7; cycle++)
		{
			machine.Cpu.StepCycle();
		}

		machine.Ram[0x3000] = 0x60; // RTS
		machine.Cpu.BeginSubroutine(0x3000, 0);
		var trace = new CpuBusTrace();
		machine.CpuBusTrace = trace;

		machine.RunUntilSubroutineReturn(16, serviceInterrupts: false);

		Assert.Equal(0xFFFF, machine.Cpu.ProgramCounter);
		Assert.DoesNotContain(trace.Frames, frame => frame.Kind == Mos6510BusAccessKind.DiscardedOpcodeFetch);
		trace.Clear();
		machine.Cpu.ProgramCounter = 0x3100;
		machine.Ram[0x3100] = 0xEA;
		machine.RunCycles(2);
		Assert.DoesNotContain(trace.Frames, frame => frame.Kind == Mos6510BusAccessKind.DiscardedOpcodeFetch);
		Assert.DoesNotContain(trace.Frames, frame => frame.Kind == Mos6510BusAccessKind.VectorRead);
	}

	[Fact]
	public void CpuReadOfCiaTimerSamplesAtExpectedBusCycle()
	{
		var machine = CreateInstructionMachine(new byte[] { 0xAD, 0x04, 0xDC }, a: 0, x: 0, y: 0);
		machine.Write(0xDC04, 0x05, 0);
		machine.Write(0xDC05, 0x00, 0);
		machine.Write(0xDC0E, 0x11, 0);

		machine.RunCycles(4);

		Assert.Equal(0x02, machine.Cpu.A);
		Assert.Equal(0x0001, machine.DebugState.Cia1.TimerA);
	}

	[Fact]
	public void CpuSidWriteWhoseFetchHitsBadlineIsDelayedToFirstFreeBusCycle()
	{
		var machine = CreateInstructionMachine(new byte[] { 0x8D, 0x04, 0xD4 }, a: 0x21, x: 0, y: 0);
		machine.Write(0xD011, 0x10, 0);
		var targetWriteCycle = PositionCpuForDataBusCycle(machine, rasterLine: 0x30, publicCycle: 15, dataCycleOffset: 3);
		var expectedWriteCycle = targetWriteCycle + 43;
		var trace = new SidCycleTrace();
		var cpuTrace = new CpuBusTrace();
		machine.Sid.Trace = trace;
		machine.CpuBusTrace = cpuTrace;

		machine.RunCycles(4);

		var write = Assert.Single(machine.SidWrites);
		Assert.Equal(expectedWriteCycle, write.Cycle);
		Assert.Equal(0x04, write.Register);
		Assert.Equal(0x21, write.Value);
		Assert.Equal(expectedWriteCycle + 1, machine.Cpu.Cycles);
		Assert.Equal(43, write.Cycle - targetWriteCycle);
		var targetFetchCycle = targetWriteCycle - 3;
		AssertStalledCpuRead(
			cpuTrace,
			targetFetchCycle,
			targetFetchCycle + 43,
			Mos6510BusAccessKind.OpcodeFetch);
		Assert.Contains(trace.Frames, frame =>
			frame.Cycle == expectedWriteCycle + 1 &&
			frame.VoiceIndex == 0 &&
			frame.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite));
	}

	[Fact]
	public void CpuSidWriteOnBadlineTransitionCycleCompletesWithoutStall()
	{
		var machine = CreateInstructionMachine(new byte[] { 0x8D, 0x04, 0xD4 }, a: 0x22, x: 0, y: 0);
		machine.Write(0xD011, 0x10, 0);
		var targetWriteCycle = PositionCpuForDataBusCycle(machine, rasterLine: 0x30, publicCycle: 12, dataCycleOffset: 3);
		var expectedEndCycle = machine.Cpu.Cycles + 4;

		machine.RunCycles(4);

		var write = Assert.Single(machine.SidWrites);
		Assert.Equal(targetWriteCycle, write.Cycle);
		Assert.Equal(expectedEndCycle, machine.Cpu.Cycles);
	}

	[Fact]
	public void CpuOperandFetchOnBadlineTransitionCycleStallsToFirstFreeBusCycle()
	{
		var machine = CreateInstructionMachine(new byte[] { 0xA9, 0x42 }, a: 0, x: 0, y: 0);
		machine.Write(0xD011, 0x10, 0);
		var targetOperandCycle = PositionCpuForDataBusCycle(machine, rasterLine: 0x30, publicCycle: 12, dataCycleOffset: 1);
		var trace = new CpuBusTrace();
		machine.CpuBusTrace = trace;

		machine.RunCycles(2);

		AssertStalledCpuRead(
			trace,
			targetOperandCycle,
			targetOperandCycle + 43,
			Mos6510BusAccessKind.OperandFetch);
		Assert.Equal(targetOperandCycle + 44, machine.Cpu.Cycles);
	}

	[Fact]
	public void CpuSidReadOnBadlineTransitionCycleStallsToFirstFreeBusCycle()
	{
		var machine = CreateInstructionMachine(new byte[] { 0xAD, 0x1B, 0xD4 }, a: 0, x: 0, y: 0);
		machine.Write(0xD011, 0x10, 0);
		var targetReadCycle = PositionCpuForDataBusCycle(machine, rasterLine: 0x30, publicCycle: 12, dataCycleOffset: 3);
		var expectedReadCycle = targetReadCycle + 43;
		var expectedEndCycle = machine.Cpu.Cycles + 4 + (expectedReadCycle - targetReadCycle);
		var trace = new CpuBusTrace();
		machine.CpuBusTrace = trace;

		machine.RunCycles(4);

		Assert.Equal(expectedEndCycle, machine.Cpu.Cycles);
		Assert.Equal(43, expectedReadCycle - targetReadCycle);
		AssertStalledCpuRead(
			trace,
			targetReadCycle,
			expectedReadCycle,
			Mos6510BusAccessKind.Read,
			0xD41B);
	}

	[Fact]
	public void CpuStackReadOnBadlineTransitionCycleStallsToFirstFreeBusCycle()
	{
		var machine = CreateInstructionMachine(new byte[] { 0x68 }, a: 0, x: 0, y: 0); // PLA
		machine.Write(0xD011, 0x10, 0);
		var targetStackReadCycle = PositionCpuForDataBusCycle(machine, rasterLine: 0x30, publicCycle: 12, dataCycleOffset: 3);
		machine.Ram[0x01FE] = 0x7A;
		var trace = new CpuBusTrace();
		machine.CpuBusTrace = trace;

		machine.RunCycles(4);

		AssertStalledCpuRead(
			trace,
			targetStackReadCycle,
			targetStackReadCycle + 43,
			Mos6510BusAccessKind.StackRead,
			0x01FE);
		Assert.Equal(0x7A, machine.Cpu.A);
	}

	[Fact]
	public void CpuSidWriteOutsideBadlineKeepsOriginalCycleTiming()
	{
		var machine = CreateInstructionMachine(new byte[] { 0x8D, 0x04, 0xD4 }, a: 0x23, x: 0, y: 0);
		machine.Write(0xD011, 0x10, 0);
		var targetWriteCycle = PositionCpuForDataBusCycle(machine, rasterLine: 0x31, publicCycle: 15, dataCycleOffset: 3);
		var expectedEndCycle = machine.Cpu.Cycles + 4;

		machine.RunCycles(4);

		var write = Assert.Single(machine.SidWrites);
		Assert.Equal(targetWriteCycle, write.Cycle);
		Assert.Equal(expectedEndCycle, machine.Cpu.Cycles);
	}

	[Fact]
	public void RenderFrameSampleTargetsPreserveBadlineDelayedSidForwardingBoundary()
	{
		var machine = CreateInstructionMachine(new byte[] { 0x8D, 0x04, 0xD4 }, a: 0x24, x: 0, y: 0);
		machine.Write(0xD011, 0x10, 0);
		var targetWriteCycle = PositionCpuForDataBusCycle(machine, rasterLine: 0x30, publicCycle: 15, dataCycleOffset: 3);
		var expectedWriteCycle = targetWriteCycle + 43;
		var sidTrace = new SidCycleTrace();
		machine.Sid.Trace = sidTrace;

		var buffer = new float[2];
		machine.RenderFrame(
			buffer,
			new AudioRenderOptionsAdapter(sampleRate: 44100, channelCount: 1),
			[expectedWriteCycle, expectedWriteCycle + 1],
			cycleCount: expectedWriteCycle + 1 - machine.Cpu.Cycles);

		var write = Assert.Single(machine.SidWrites);
		Assert.Equal(expectedWriteCycle, write.Cycle);
		var forwardedCycles = sidTrace.Frames
			.Where(frame => frame.VoiceIndex == 0 && frame.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite))
			.Select(frame => frame.Cycle)
			.ToArray();
		Assert.Equal([expectedWriteCycle + 1], forwardedCycles);
	}

	[Fact]
	public void CpuD011WriteAtPublicCycleFourteenCreatesArtificialBadline()
	{
		var machine = CreateInstructionMachine(new byte[] { 0x8D, 0x11, 0xD0 }, a: 0x10, x: 0, y: 0);
		machine.Write(0xD011, 0x11, 0);
		var targetWriteCycle = PositionCpuForDataBusCycle(machine, rasterLine: 0x30, publicCycle: 14, dataCycleOffset: 3);
		var trace = new CpuBusTrace();
		machine.CpuBusTrace = trace;

		machine.RunCycles(4);

		var write = Assert.Single(trace.Frames, frame => frame.Kind == Mos6510BusAccessKind.Write && frame.Address == 0xD011);
		Assert.Equal(targetWriteCycle, write.Cycle);
		Assert.True(write.CpuAdvanced);
		Assert.True(write.BusAvailable);
		Assert.False(write.VicOwned);
		Assert.True(machine.DebugState.Vic.BadlineActive);
		Assert.True(machine.DebugState.Vic.BadlineArtificial);
		Assert.True(machine.DebugState.Vic.AecLow);
		Assert.Equal(0, machine.DebugState.Vic.BadlineFetchIndex);
	}

	[Fact]
	public void CpuD011WriteAtPublicCycleTwelveCancelsPendingBadlineBeforeAec()
	{
		var machine = CreateInstructionMachine(new byte[] { 0x8D, 0x11, 0xD0 }, a: 0x11, x: 0, y: 0);
		machine.Write(0xD011, 0x10, 0);
		var targetWriteCycle = PositionCpuForDataBusCycle(machine, rasterLine: 0x30, publicCycle: 12, dataCycleOffset: 3);
		var trace = new CpuBusTrace();
		machine.CpuBusTrace = trace;

		machine.RunCycles(4);
		machine.AdvanceNativeCycles(2);

		var write = Assert.Single(trace.Frames, frame => frame.Kind == Mos6510BusAccessKind.Write && frame.Address == 0xD011);
		Assert.Equal(targetWriteCycle, write.Cycle);
		Assert.True(write.CpuAdvanced);
		Assert.True(write.BusAvailable);
		Assert.False(write.VicOwned);
		Assert.False(machine.DebugState.Vic.BadlineActive);
		Assert.False(machine.DebugState.Vic.AecLow);
	}

	[Fact]
	public void DynamicArtificialBadlineStallsOpcodeFetchToFirstFreeBusCycle()
	{
		var machine = CreateInstructionMachine(new byte[] { 0xA9, 0x42 }, a: 0, x: 0, y: 0);
		CreateArtificialBadlineAtPublicCycleFifteen(machine);
		var targetFetchCycle = machine.Cpu.Cycles;
		var trace = new CpuBusTrace();
		machine.CpuBusTrace = trace;

		machine.RunCycles(2);

		AssertStalledCpuRead(
			trace,
			targetFetchCycle,
			targetFetchCycle + 40,
			Mos6510BusAccessKind.OpcodeFetch);
		Assert.Equal(0x42, machine.Cpu.A);
	}

	[Fact]
	public void DynamicArtificialBadlineDelayedSidWriteStillForwardsOnFollowingSidCycle()
	{
		var machine = CreateInstructionMachine(new byte[] { 0x8D, 0x04, 0xD4 }, a: 0x27, x: 0, y: 0);
		CreateArtificialBadlineAtPublicCycleFifteen(machine);
		var targetFetchCycle = machine.Cpu.Cycles;
		var sidTrace = new SidCycleTrace();
		machine.Sid.Trace = sidTrace;

		machine.RunCycles(4);

		var write = Assert.Single(machine.SidWrites);
		Assert.Equal(targetFetchCycle + 43, write.Cycle);
		Assert.Contains(sidTrace.Frames, frame =>
			frame.Cycle == write.Cycle + 1 &&
			frame.VoiceIndex == 0 &&
			frame.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite));
	}

	[Fact]
	public void SpriteDmaStallsOpcodeFetchUntilFirstFreeBusCycle()
	{
		var machine = CreateInstructionMachine(new byte[] { 0xA9, 0x42 }, a: 0, x: 0, y: 0);
		EnableSprite(machine, sprite: 3, y: 0x30);
		var targetFetchCycle = PositionCpuForDataBusCycle(machine, rasterLine: 0x30, publicCycle: 1, dataCycleOffset: 0);
		var trace = new CpuBusTrace();
		machine.CpuBusTrace = trace;

		machine.RunCycles(2);

		AssertStalledCpuRead(
			trace,
			targetFetchCycle,
			targetFetchCycle + 3,
			Mos6510BusAccessKind.OpcodeFetch);
		Assert.Equal(0x42, machine.Cpu.A);
	}

	[Fact]
	public void SpriteDmaStallsOperandFetchDuringBaTransition()
	{
		var machine = CreateInstructionMachine(new byte[] { 0xA9, 0x42 }, a: 0, x: 0, y: 0);
		EnableSprite(machine, sprite: 5, y: 0x30);
		var targetOperandCycle = PositionCpuForDataBusCycle(machine, rasterLine: 0x30, publicCycle: 4, dataCycleOffset: 1);
		var trace = new CpuBusTrace();
		machine.CpuBusTrace = trace;

		machine.RunCycles(2);

		AssertStalledCpuRead(
			trace,
			targetOperandCycle,
			targetOperandCycle + 6,
			Mos6510BusAccessKind.OperandFetch);
		Assert.Equal(0x42, machine.Cpu.A);
	}

	[Fact]
	public void SpriteDmaStallsDataAndStackReads()
	{
		var dataRead = CreateInstructionMachine(new byte[] { 0xAD, 0x1B, 0xD4 }, a: 0, x: 0, y: 0);
		EnableSprite(dataRead, sprite: 5, y: 0x30);
		var dataTargetCycle = PositionCpuForDataBusCycle(dataRead, rasterLine: 0x30, publicCycle: 4, dataCycleOffset: 3);
		var dataTrace = new CpuBusTrace();
		dataRead.CpuBusTrace = dataTrace;

		dataRead.RunCycles(4);

		var completedDataRead = dataTrace.Frames.Last(frame =>
			frame.Kind == Mos6510BusAccessKind.Read &&
			frame.Address == 0xD41B &&
			frame.CpuAdvanced);
		Assert.True(completedDataRead.Cycle > dataTargetCycle);
		Assert.Contains(dataTrace.Frames, frame =>
			frame.Cycle >= dataTargetCycle &&
			frame.Cycle < completedDataRead.Cycle &&
			!frame.CpuAdvanced);

		var stackRead = CreateInstructionMachine(new byte[] { 0x68 }, a: 0, x: 0, y: 0);
		stackRead.Ram[0x01FE] = 0x6A;
		EnableSprite(stackRead, sprite: 5, y: 0x30);
		var stackTargetCycle = PositionCpuForDataBusCycle(stackRead, rasterLine: 0x30, publicCycle: 4, dataCycleOffset: 3);
		var stackTrace = new CpuBusTrace();
		stackRead.CpuBusTrace = stackTrace;

		stackRead.RunCycles(4);

		var completedStackRead = stackTrace.Frames.Last(frame =>
			frame.Kind == Mos6510BusAccessKind.StackRead &&
			frame.CpuAdvanced);
		Assert.True(completedStackRead.Cycle > stackTargetCycle);
		Assert.Contains(stackTrace.Frames, frame =>
			frame.Cycle >= stackTargetCycle &&
			frame.Cycle < completedStackRead.Cycle &&
			!frame.CpuAdvanced);
		Assert.Equal(0x6A, stackRead.Cpu.A);
	}

	[Fact]
	public void SpriteTransitionAllowsWriteAlreadyInProgressButStallsReads()
	{
		var write = CreateInstructionMachine(new byte[] { 0x8D, 0x04, 0xD4 }, a: 0x25, x: 0, y: 0);
		EnableSprite(write, sprite: 5, y: 0x30);
		var writeCycle = PositionCpuForDataBusCycle(write, rasterLine: 0x30, publicCycle: 4, dataCycleOffset: 3);

		write.RunCycles(4);

		Assert.Equal(writeCycle, Assert.Single(write.SidWrites).Cycle);

		var read = CreateInstructionMachine(new byte[] { 0xA9, 0x42 }, a: 0, x: 0, y: 0);
		EnableSprite(read, sprite: 5, y: 0x30);
		var readCycle = PositionCpuForDataBusCycle(read, rasterLine: 0x30, publicCycle: 4, dataCycleOffset: 1);
		var trace = new CpuBusTrace();
		read.CpuBusTrace = trace;

		read.RunCycles(2);

		AssertStalledCpuRead(
			trace,
			readCycle,
			readCycle + 6,
			Mos6510BusAccessKind.OperandFetch);
	}

	[Fact]
	public void OverlappingBadlineAndSpriteDmaDoNotReleaseCpuBeforeBadlineEnds()
	{
		var machine = CreateInstructionMachine(new byte[] { 0xA9, 0x42 }, a: 0, x: 0, y: 0);
		machine.Write(0xD011, 0x10, 0);
		EnableSprite(machine, sprite: 7, y: 0x30);
		var targetFetchCycle = PositionCpuForDataBusCycle(machine, rasterLine: 0x30, publicCycle: 13, dataCycleOffset: 0);
		var trace = new CpuBusTrace();
		machine.CpuBusTrace = trace;

		machine.RunCycles(2);

		AssertStalledCpuRead(
			trace,
			targetFetchCycle,
			targetFetchCycle + 42,
			Mos6510BusAccessKind.OpcodeFetch);
	}

	[Fact]
	public void SpriteDmaDelayedSidWriteStillForwardsOnFollowingSidCycle()
	{
		var machine = CreateInstructionMachine(new byte[] { 0x8D, 0x04, 0xD4 }, a: 0x26, x: 0, y: 0);
		EnableSprite(machine, sprite: 3, y: 0x30);
		var targetWriteCycle = PositionCpuForDataBusCycle(machine, rasterLine: 0x30, publicCycle: 1, dataCycleOffset: 3);
		var sidTrace = new SidCycleTrace();
		machine.Sid.Trace = sidTrace;

		machine.RunCycles(4);

		var write = Assert.Single(machine.SidWrites);
		Assert.Equal(targetWriteCycle + 6, write.Cycle);
		Assert.Contains(sidTrace.Frames, frame =>
			frame.Cycle == write.Cycle + 1 &&
			frame.VoiceIndex == 0 &&
			frame.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite));
	}

	[Fact]
	public void VicSpriteMemoryUsesCia2BankD018ScreenBaseAndCharacterRomHoles()
	{
		var machine = CreateInstructionMachine(new byte[] { 0xEA }, a: 0, x: 0, y: 0);
		machine.Write(0xDD02, 0x03, 0);
		machine.Write(0xDD00, 0x03, 0); // CIA2 bank 0, VIC absolute base $0000.
		machine.Write(0xD018, 0x10, 0); // Screen matrix at $0400 within the VIC bank.
		EnableSprite(machine, sprite: 3, y: 0x30);
		machine.Ram[0x0400 + 0x03F8 + 3] = 0x40;
		machine.Ram[0x1001] = 0x12;

		AdvanceToVicPublicCycle(machine, rasterLine: 0x30, publicCycle: 1);

		Assert.Equal(VicMemoryAccessKind.SpritePointer, machine.DebugState.Vic.MemoryAccessKind);
		Assert.Equal(0x07FB, machine.DebugState.Vic.MemoryAddress);
		Assert.Equal(0x40, machine.DebugState.Vic.MemoryValue);

		machine.AdvanceNativeCycles(1);

		Assert.Equal(VicMemoryAccessKind.SpriteData, machine.DebugState.Vic.MemoryAccessKind);
		Assert.Equal(0x1001, machine.DebugState.Vic.MemoryAddress);
		Assert.Equal(0x42, machine.DebugState.Vic.MemoryValue);
	}

	[Fact]
	public void VicSpritePointerAddressFollowsCia2BankSelection()
	{
		var machine = CreateInstructionMachine(new byte[] { 0xEA }, a: 0, x: 0, y: 0);
		machine.Write(0xDD02, 0x03, 0);
		machine.Write(0xDD00, 0x02, 0); // CIA2 bank 1, VIC absolute base $4000.
		EnableSprite(machine, sprite: 3, y: 0x30);
		machine.Ram[0x4000 + 0x03F8 + 3] = 0x22;

		AdvanceToVicPublicCycle(machine, rasterLine: 0x30, publicCycle: 1);

		Assert.Equal(VicMemoryAccessKind.SpritePointer, machine.DebugState.Vic.MemoryAccessKind);
		Assert.Equal(0x43FB, machine.DebugState.Vic.MemoryAddress);
		Assert.Equal(0x22, machine.DebugState.Vic.MemoryValue);
	}

	[Fact]
	public void VicBadlineMemoryUsesCia2BankD018AndCharacterRomHoles()
	{
		var machine = CreateInstructionMachine(new byte[] { 0xEA }, a: 0, x: 0, y: 0);
		machine.Write(0xDD02, 0x03, 0);
		machine.Write(0xDD00, 0x03, 0); // CIA2 bank 0, VIC absolute base $0000.
		machine.Write(0xD018, 0x14, 0); // Screen matrix at $0400, character data at $1000.
		machine.Write(0xD011, 0x10, 0);
		machine.Ram[0x0400] = 0x00;
		machine.Ram[0x1000] = 0x44;

		AdvanceToVicPublicCycle(machine, rasterLine: 0x30, publicCycle: 15);

		Assert.Equal(VicMemoryAccessKind.BadlineScreen, machine.DebugState.Vic.BadlineMemoryAccessKind);
		Assert.Equal(0, machine.DebugState.Vic.BadlineFetchIndex);
		Assert.Equal(0x0400, machine.DebugState.Vic.BadlineMatrixAddress);
		Assert.Equal(0x1000, machine.DebugState.Vic.BadlineGraphicsAddress);
		Assert.Equal(0x00, machine.DebugState.Vic.BadlineMatrixValue);
		Assert.Equal(0x3C, machine.DebugState.Vic.BadlineGraphicsValue);
	}

	[Fact]
	public void ColorRamReadWriteKeepsOnlyLowNibbleForIoReads()
	{
		var machine = CreateInstructionMachine(new byte[] { 0xEA }, a: 0, x: 0, y: 0);

		machine.Write(0xD800, 0xA5, 0);

		Assert.Equal(0xF5, machine.Read(0xD800));
		machine.Write(0xD800, 0x0C, 0);
		Assert.Equal(0xFC, machine.Read(0xD800));
	}

	[Fact]
	public void OverlappingSpriteDmaAndDynamicBadlineDoNotReleaseCpuBeforeBadlineEnds()
	{
		var machine = CreateInstructionMachine(new byte[] { 0xA9, 0x42 }, a: 0, x: 0, y: 0);
		EnableSprite(machine, sprite: 7, y: 0x30);
		CreateArtificialBadlineAtPublicCycleFifteen(machine);
		var targetFetchCycle = machine.Cpu.Cycles;
		var trace = new CpuBusTrace();
		machine.CpuBusTrace = trace;

		machine.RunCycles(2);

		AssertStalledCpuRead(
			trace,
			targetFetchCycle,
			targetFetchCycle + 40,
			Mos6510BusAccessKind.OpcodeFetch);
		Assert.Equal(0x42, machine.Cpu.A);
	}

	[Fact]
	public void BankingRsidFixtureOnlyWritesSidWhenIoIsVisible()
	{
		var module = SidParser.Parse(SidFixtureBuilder.CreateRsid(BankingProgram()));
		var machine = new C64Machine(module);

		machine.Reset(0);

		Assert.DoesNotContain(machine.SidWrites, write => write.Value == 0x0F);
		Assert.Contains(machine.SidWrites, write => write.Register == 0x18 && write.Value == 0x0E);
	}

	[Fact]
	public void RsidRasterIrqCanReturnThroughKernalEpilogue()
	{
		var module = SidParser.Parse(SidFixtureBuilder.CreateRsid(RasterIrqProgram()));
		var machine = new C64Machine(module);
		machine.Reset(0);

		machine.RunCycles(SidConstants.PalCyclesPerFrame * 3);

		Assert.False(machine.Cpu.Halted);
		Assert.True(
			(machine.Cpu.ProgramCounter >= 0xFF94 && machine.Cpu.ProgramCounter <= 0xFF98) ||
			(machine.Cpu.ProgramCounter >= 0xEA81 && machine.Cpu.ProgramCounter <= 0xEA86),
			$"Expected CPU to be in the idle loop or KERNAL IRQ epilogue, got ${machine.Cpu.ProgramCounter:X4}.");
		Assert.Contains(machine.SidWrites, write => write.Register == 0x18 && write.Value == 0x0F);
	}

	[Fact]
	public void RsidIrqCanReturnThroughKernalCiaAcknowledgeExit()
	{
		var machine = CreateRsidMachine(IrqTimerProgramReturningThroughEa7e());

		machine.Cpu.Status &= 0xFB;
		machine.RunCycles(500);

		Assert.False(machine.Cpu.Halted);
		Assert.Equal(C64InterruptSource.Cia1, machine.DebugState.LastInterruptSource);
		Assert.Contains(machine.SidWrites, write => write.Register == 0x18 && write.Value == 0x0D);
		Assert.True(
			(machine.Cpu.ProgramCounter >= 0xFF94 && machine.Cpu.ProgramCounter <= 0xFF98) ||
			(machine.Cpu.ProgramCounter >= 0xFF48 && machine.Cpu.ProgramCounter <= 0xFF50) ||
			(machine.Cpu.ProgramCounter >= 0xEA7E && machine.Cpu.ProgramCounter <= 0xEA86),
			$"Expected CPU to return through KERNAL IRQ exit, got ${machine.Cpu.ProgramCounter:X4}.");
	}

	[Fact]
	public void RsidIrqCanRunKernalClockServiceWhenReturningThroughEa31()
	{
		var machine = CreateRsidMachine(IrqTimerProgramReturningThroughEa31());

		machine.Cpu.Status &= 0xFB;
		machine.RunCycles(500);

		Assert.False(machine.Cpu.Halted);
		Assert.Equal(C64InterruptSource.Cia1, machine.DebugState.LastInterruptSource);
		Assert.Contains(machine.SidWrites, write => write.Register == 0x18 && write.Value == 0x0C);
		Assert.True(machine.Read(0x00A2) > 0, "Expected the minimal KERNAL IRQ service at $EA31 to update the jiffy clock.");
		Assert.True(
			(machine.Cpu.ProgramCounter >= 0xFF94 && machine.Cpu.ProgramCounter <= 0xFF98) ||
			(machine.Cpu.ProgramCounter >= 0xFF48 && machine.Cpu.ProgramCounter <= 0xFF50) ||
			(machine.Cpu.ProgramCounter >= 0xEA31 && machine.Cpu.ProgramCounter <= 0xEA86) ||
			(machine.Cpu.ProgramCounter >= 0xFFEA && machine.Cpu.ProgramCounter <= 0xFFF7),
			$"Expected CPU to return through KERNAL IRQ service, got ${machine.Cpu.ProgramCounter:X4}.");
	}

	[Fact]
	public void InstalledIrqServiceSurvivesOverwrittenRamUnderKernal()
	{
		var machine = CreateRsidMachine(IrqTimerProgramWithOverwrittenKernalRam());

		machine.Cpu.Status &= 0xFB;
		machine.RunCycles(500);

		Assert.Equal(0x02, machine.Ram[0xEA31]);
		Assert.False(machine.Cpu.Halted);
		Assert.Equal(C64InterruptSource.Cia1, machine.DebugState.LastInterruptSource);
		Assert.True(machine.Read(0x00A2) > 0, "Expected the resident KERNAL IRQ service to update the jiffy clock.");
	}

	[Fact]
	public void InstalledIrqServiceParksBreakInstruction()
	{
		var machine = CreateInstructionMachine(new byte[] { 0x00, 0xEA }, a: 0x12, x: 0x34, y: 0x56);
		var stackPointer = machine.Cpu.StackPointer;

		machine.RunCycles(80);

		Assert.False(machine.Cpu.Halted);
		Assert.InRange(machine.Cpu.ProgramCounter, 0xFF94, 0xFF98);
		Assert.Equal(stackPointer, machine.Cpu.StackPointer);
	}

	private static C64Machine CreateRsidMachine(byte[] program)
	{
		var module = SidParser.Parse(SidFixtureBuilder.CreateRsid(program));
		var machine = new C64Machine(module);
		machine.Reset(0);
		return machine;
	}

	private static C64Machine CreateInstructionMachine(byte[] program, byte a, byte x, byte y)
	{
		var machine = CreateRsidMachine(new byte[] { 0x60 });
		machine.Sid.Reset();
		machine.Cpu.InitializeState(0x2000);
		machine.Cpu.A = a;
		machine.Cpu.X = x;
		machine.Cpu.Y = y;
		for (var i = 0; i < program.Length; i++)
		{
			machine.Ram[0x2000 + i] = program[i];
		}

		return machine;
	}

	private static object[] CpuSidWrite(
		string name,
		byte[] program,
		int totalCycles,
		long expectedWriteCycle,
		byte expectedRegister,
		byte expectedValue,
		byte a,
		byte x = 0,
		byte y = 0,
		byte? pointerLocation = null,
		ushort? pointerTarget = null)
	{
		return [new CpuSidWriteCase(name, program, totalCycles, expectedWriteCycle, expectedRegister, expectedValue, a, x, y, pointerLocation, pointerTarget)];
	}

	private static void WriteZeroPagePointer(byte[] memory, byte location, ushort target)
	{
		memory[location] = (byte)(target & 0xFF);
		memory[(byte)(location + 1)] = (byte)(target >> 8);
	}

	private static CpuBusTraceFrame AssertStalledCpuRead(
		CpuBusTrace trace,
		long requestedCycle,
		long completedCycle,
		Mos6510BusAccessKind kind,
		ushort? address = null)
	{
		var frames = trace.Frames
			.Where(frame => frame.Cycle >= requestedCycle && frame.Cycle <= completedCycle)
			.ToArray();
		Assert.Equal(
			Enumerable.Range(0, checked((int)(completedCycle - requestedCycle + 1)))
				.Select(offset => requestedCycle + offset)
				.ToArray(),
			frames.Select(frame => frame.Cycle).ToArray());

		foreach (var frame in frames[..^1])
		{
			Assert.False(frame.CpuAdvanced);
			if (frame.VicOwned)
			{
				Assert.Null(frame.Kind);
				Assert.Null(frame.Address);
				Assert.False(frame.BusAvailable);
			}
			else
			{
				Assert.Equal(kind, frame.Kind);
				Assert.False(frame.Ready);
				Assert.True(frame.BusAvailable);
				if (address.HasValue)
				{
					Assert.Equal(address, frame.Address);
				}
			}
		}

		var completed = frames[^1];
		Assert.Equal(kind, completed.Kind);
		Assert.True(completed.CpuAdvanced);
		Assert.True(completed.Ready);
		Assert.True(completed.BusAvailable);
		Assert.False(completed.VicOwned);
		if (address.HasValue)
		{
			Assert.Equal(address, completed.Address);
		}

		return completed;
	}

	private static long PositionCpuForDataBusCycle(C64Machine machine, int rasterLine, int publicCycle, int dataCycleOffset)
	{
		var busCycle = machine.Cpu.Cycles + GetCyclesUntilVicPublicCycle(machine, rasterLine, publicCycle);
		var startCycle = busCycle - dataCycleOffset;
		var delta = startCycle - machine.Cpu.Cycles;
		Assert.True(delta >= 0, "Cannot position CPU backwards in the current test helper.");
		machine.AdvanceNativeCycles(delta);
		return busCycle;
	}

	private static void AdvanceToVicPublicCycle(C64Machine machine, int rasterLine, int publicCycle)
	{
		machine.AdvanceNativeCycles(GetCyclesUntilVicPublicCycle(machine, rasterLine, publicCycle));
	}

	private static void CreateArtificialBadlineAtPublicCycleFifteen(C64Machine machine)
	{
		machine.Write(0xD011, 0x11, 0);
		AdvanceToVicPublicCycle(machine, rasterLine: 0x30, publicCycle: 14);
		machine.Write(0xD011, 0x10, 0);
		machine.AdvanceNativeCycles(1);
	}

	private static long GetCyclesUntilVicPublicCycle(C64Machine machine, int rasterLine, int publicCycle)
	{
		var current = machine.DebugState.Vic;
		var currentFrameCycle = current.RasterLine * machine.Clock.CyclesPerRasterLine + current.RasterCycle;
		var targetFrameCycle = rasterLine * machine.Clock.CyclesPerRasterLine + publicCycle - 1;
		var delta = targetFrameCycle - currentFrameCycle;
		return delta >= 0 ? delta : delta + machine.Clock.CyclesPerFrame;
	}

	private static void EnableSprite(C64Machine machine, int sprite, int y)
	{
		machine.Write((ushort)(0xD001 + (sprite * 2)), (byte)y, 0);
		machine.Write(0xD015, (byte)(machine.Read(0xD015) | (1 << sprite)), 0);
	}

	public sealed record CpuSidWriteCase(
		string Name,
		byte[] Program,
		int TotalCycles,
		long ExpectedWriteCycle,
		byte ExpectedRegister,
		byte ExpectedValue,
		byte A,
		byte X,
		byte Y,
		byte? PointerLocation,
		ushort? PointerTarget)
	{
		public override string ToString() => Name;
	}

	private static byte[] IrqTimerProgram()
	{
		return new byte[]
		{
			0x78,             // SEI
			0xA9, 0x20,       // LDA #<irq
			0x8D, 0x14, 0x03, // STA $0314
			0xA9, 0x10,       // LDA #>irq
			0x8D, 0x15, 0x03, // STA $0315
			0xA9, 0x02,       // LDA #$02
			0x8D, 0x04, 0xDC, // STA $DC04
			0xA9, 0x00,       // LDA #$00
			0x8D, 0x05, 0xDC, // STA $DC05
			0xA9, 0x81,       // LDA #$81
			0x8D, 0x0D, 0xDC, // STA $DC0D
			0xA9, 0x11,       // LDA #$11
			0x8D, 0x0E, 0xDC, // STA $DC0E
			0x60,             // RTS
			0xAD, 0x0D, 0xDC, // irq: LDA $DC0D
			0xA9, 0x0F,       // LDA #$0F
			0x8D, 0x18, 0xD4, // STA $D418
			0x4C, 0x81, 0xEA  // JMP $EA81
		};
	}

	private static byte[] IrqTimerProgramReturningThroughEa7e()
	{
		return new byte[]
		{
			0x78,             // SEI
			0xA9, 0x20,       // LDA #<irq
			0x8D, 0x14, 0x03, // STA $0314
			0xA9, 0x10,       // LDA #>irq
			0x8D, 0x15, 0x03, // STA $0315
			0xA9, 0x02,       // LDA #$02
			0x8D, 0x04, 0xDC, // STA $DC04
			0xA9, 0x00,       // LDA #$00
			0x8D, 0x05, 0xDC, // STA $DC05
			0xA9, 0x81,       // LDA #$81
			0x8D, 0x0D, 0xDC, // STA $DC0D
			0xA9, 0x11,       // LDA #$11
			0x8D, 0x0E, 0xDC, // STA $DC0E
			0x60,             // RTS
			0xA9, 0x0D,       // irq: LDA #$0D
			0x8D, 0x18, 0xD4, // STA $D418
			0x4C, 0x7E, 0xEA  // JMP $EA7E; KERNAL reads $DC0D and exits interrupt
		};
	}

	private static byte[] IrqTimerProgramReturningThroughEa31()
	{
		return new byte[]
		{
			0x78,             // SEI
			0xA9, 0x20,       // LDA #<irq
			0x8D, 0x14, 0x03, // STA $0314
			0xA9, 0x10,       // LDA #>irq
			0x8D, 0x15, 0x03, // STA $0315
			0xA9, 0x02,       // LDA #$02
			0x8D, 0x04, 0xDC, // STA $DC04
			0xA9, 0x00,       // LDA #$00
			0x8D, 0x05, 0xDC, // STA $DC05
			0xA9, 0x81,       // LDA #$81
			0x8D, 0x0D, 0xDC, // STA $DC0D
			0xA9, 0x11,       // LDA #$11
			0x8D, 0x0E, 0xDC, // STA $DC0E
			0x60,             // RTS
			0xA9, 0x0C,       // irq: LDA #$0C
			0x8D, 0x18, 0xD4, // STA $D418
			0x4C, 0x31, 0xEA  // JMP $EA31; KERNAL updates time, acknowledges CIA/VIC, and exits
		};
	}

	private static byte[] IrqTimerProgramWithOverwrittenKernalRam()
	{
		return new byte[]
		{
			0x78,             // SEI
			0xA9, 0x35,       // LDA #$35
			0x85, 0x01,       // STA $01; I/O visible, KERNAL RAM visible
			0xA9, 0x02,       // LDA #$02
			0x8D, 0x31, 0xEA, // STA $EA31; overwrite RAM under the default IRQ handler with KIL
			0xA9, 0x02,       // LDA #$02
			0x8D, 0x04, 0xDC, // STA $DC04
			0xA9, 0x00,       // LDA #$00
			0x8D, 0x05, 0xDC, // STA $DC05
			0xA9, 0x11,       // LDA #$11
			0x8D, 0x0E, 0xDC, // STA $DC0E
			0x58,             // CLI
			0x60              // RTS
		};
	}

	private static byte[] NmiTimerProgram()
	{
		return new byte[]
		{
			0x78,             // SEI
			0xA9, 0x20,       // LDA #<nmi
			0x8D, 0x18, 0x03, // STA $0318
			0xA9, 0x10,       // LDA #>nmi
			0x8D, 0x19, 0x03, // STA $0319
			0xA9, 0x02,       // LDA #$02
			0x8D, 0x04, 0xDD, // STA $DD04
			0xA9, 0x00,       // LDA #$00
			0x8D, 0x05, 0xDD, // STA $DD05
			0xA9, 0x81,       // LDA #$81
			0x8D, 0x0D, 0xDD, // STA $DD0D
			0xA9, 0x19,       // LDA #$19
			0x8D, 0x0E, 0xDD, // STA $DD0E
			0x60,             // RTS
			0xAD, 0x0D, 0xDD, // nmi: LDA $DD0D
			0xA9, 0x0E,       // LDA #$0E
			0x8D, 0x18, 0xD4, // STA $D418
			0x40              // RTI
		};
	}

	private static byte[] BankingProgram()
	{
		return new byte[]
		{
			0xA9, 0x34,       // LDA #$34
			0x8D, 0x01, 0x00, // STA $0001; I/O out
			0xA9, 0x0F,       // LDA #$0F
			0x8D, 0x18, 0xD4, // STA $D418; RAM-only write
			0xA9, 0x37,       // LDA #$37
			0x8D, 0x01, 0x00, // STA $0001; I/O in
			0xA9, 0x0E,       // LDA #$0E
			0x8D, 0x18, 0xD4, // STA $D418
			0x60
		};
	}

	private static byte[] RasterIrqProgram()
	{
		return new byte[]
		{
			0x78,             // SEI
			0xA2, 0x7F,       // LDX #$7F
			0x8E, 0x0D, 0xDC, // STX $DC0D
			0xAE, 0x0D, 0xDC, // LDX $DC0D
			0x8E, 0x0D, 0xDD, // STX $DD0D
			0xAE, 0x0D, 0xDD, // LDX $DD0D
			0xA9, 0x23,       // LDA #<irq
			0x8D, 0x14, 0x03, // STA $0314
			0xA9, 0x10,       // LDA #>irq
			0x8D, 0x15, 0x03, // STA $0315
			0xA9, 0x01,       // LDA #$01
			0x8D, 0x12, 0xD0, // STA $D012
			0x8D, 0x1A, 0xD0, // STA $D01A
			0x58,             // CLI
			0x60,             // RTS
			0xA9, 0x01,       // irq: LDA #$01
			0x8D, 0x19, 0xD0, // STA $D019
			0xA9, 0x0F,       // LDA #$0F
			0x8D, 0x18, 0xD4, // STA $D418
			0x4C, 0x81, 0xEA  // JMP $EA81
		};
	}
}
