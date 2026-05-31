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
		Assert.Equal(0x80, machine.Read(0xD000));
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

		Assert.Equal(0x01, machine.Cpu.A);
		Assert.Equal(0x00020000u, machine.Sid.Chips[0].DebugState.Voices[2].Accumulator);
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

		machine.Cpu.Status &= 0xFB;
		machine.RunCycles(200);

		Assert.Contains(machine.SidWrites, write => write.Register == 0x18 && write.Value == 0x0F);
		Assert.Equal(C64InterruptSource.Cia1, machine.DebugState.LastInterruptSource);
	}

	[Fact]
	public void Cia2NmiRunsDespiteInterruptDisableFlag()
	{
		var machine = CreateRsidMachine(NmiTimerProgram());

		machine.Cpu.Status |= 0x04;
		machine.RunCycles(200);

		Assert.Contains(machine.SidWrites, write => write.Register == 0x18 && write.Value == 0x0E);
		Assert.Equal(C64InterruptSource.Cia2, machine.DebugState.LastInterruptSource);
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
			(machine.Cpu.ProgramCounter >= 0xFF80 && machine.Cpu.ProgramCounter <= 0xFF88) ||
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
			(machine.Cpu.ProgramCounter >= 0xFF80 && machine.Cpu.ProgramCounter <= 0xFF88) ||
			(machine.Cpu.ProgramCounter >= 0xEA31 && machine.Cpu.ProgramCounter <= 0xEA86) ||
			(machine.Cpu.ProgramCounter >= 0xFFEA && machine.Cpu.ProgramCounter <= 0xFFF7),
			$"Expected CPU to return through KERNAL IRQ service, got ${machine.Cpu.ProgramCounter:X4}.");
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
		machine.Cpu.Reset(0x2000);
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
