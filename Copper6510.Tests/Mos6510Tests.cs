using Copper6510;

namespace Copper6510.Tests;

public sealed class Mos6510Tests
{
	[Fact]
	public void ExecutesOfficialLoadStoreAndCycleCounts()
	{
		var bus = new TestBus();
		bus.Memory[0x1000] = 0xA9; // LDA #$42
		bus.Memory[0x1001] = 0x42;
		bus.Memory[0x1002] = 0x8D; // STA $D400
		bus.Memory[0x1003] = 0x00;
		bus.Memory[0x1004] = 0xD4;
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);

		Assert.Equal(2, cpu.ExecuteInstruction());
		Assert.Equal(4, cpu.ExecuteInstruction());

		Assert.Equal(0x42, cpu.A);
		Assert.Equal(6, cpu.Cycles);
		Assert.Equal((ushort)0xD400, bus.LastWriteAddress);
		Assert.Equal(0x42, bus.LastWriteValue);
		Assert.Equal(5, bus.LastWriteCycle);
	}

	[Fact]
	public void BranchAddsPageCrossingPenalty()
	{
		var bus = new TestBus();
		bus.Memory[0x10FD] = 0xD0; // BNE +2
		bus.Memory[0x10FE] = 0x02;
		bus.Memory[0x1101] = 0xEA;
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x10FD);
		cpu.Status &= 0xFD;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(4, cycles);
		Assert.Equal(0x1101, cpu.ProgramCounter);
	}

	[Fact]
	public void ExecutesCommonIllegalSlo()
	{
		var bus = new TestBus();
		bus.Memory[0x1000] = 0xA9; // LDA #$01
		bus.Memory[0x1001] = 0x01;
		bus.Memory[0x1002] = 0x07; // SLO $20
		bus.Memory[0x1003] = 0x20;
		bus.Memory[0x0020] = 0x40;
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(0x80, bus.Memory[0x0020]);
		Assert.Equal(0x81, cpu.A);
	}

	[Fact]
	public void ReadModifyWriteInstructionsPerformDummyWriteBeforeFinalWrite()
	{
		var bus = new TestBus();
		bus.Memory[0x1000] = 0x0E; // ASL $D019
		bus.Memory[0x1001] = 0x19;
		bus.Memory[0x1002] = 0xD0;
		bus.Memory[0xD019] = 0x81;
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);

		cpu.ExecuteInstruction();

		Assert.Equal(2, bus.Writes.Count);
		Assert.Equal((0xD019, (byte)0x81, 4), bus.Writes[0]);
		Assert.Equal((0xD019, (byte)0x02, 5), bus.Writes[1]);
	}

	[Theory]
	[MemberData(nameof(StoreWriteCases))]
	public void StoreInstructionsWriteOnFinalBusCycle(StoreWriteCase testCase)
	{
		var bus = new TestBus();
		LoadProgram(bus, testCase.Program);
		if (testCase.PointerLocation.HasValue && testCase.PointerTarget.HasValue)
		{
			WriteZeroPagePointer(bus, testCase.PointerLocation.Value, testCase.PointerTarget.Value);
		}

		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);
		cpu.A = StoreA;
		cpu.X = StoreX;
		cpu.Y = StoreY;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(testCase.TotalCycles, cycles);
		var write = Assert.Single(bus.Writes);
		Assert.Equal(testCase.TargetAddress, write.Address);
		Assert.Equal(testCase.ExpectedValue, write.Value);
		Assert.Equal(testCase.TotalCycles - 1, write.CycleOffset);
	}

	[Theory]
	[MemberData(nameof(LoadReadCases))]
	public void LoadInstructionsReadDataOnFinalBusCycle(LoadReadCase testCase)
	{
		var bus = new TestBus();
		LoadProgram(bus, testCase.Program);
		bus.Memory[testCase.TargetAddress] = 0x5A;
		if (testCase.PointerLocation.HasValue && testCase.PointerTarget.HasValue)
		{
			WriteZeroPagePointer(bus, testCase.PointerLocation.Value, testCase.PointerTarget.Value);
		}

		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);
		cpu.X = testCase.X;
		cpu.Y = testCase.Y;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(testCase.TotalCycles, cycles);
		Assert.Equal(0x5A, cpu.A);
		var targetRead = Assert.Single(bus.Reads.Where(read => read.Address == testCase.TargetAddress));
		Assert.Equal(testCase.TotalCycles - 1, targetRead.CycleOffset);
	}

	[Theory]
	[MemberData(nameof(ReadModifyWriteCases))]
	public void ReadModifyWriteInstructionsPerformDummyAndFinalWritesOnLastTwoCycles(ReadModifyWriteCase testCase)
	{
		var bus = new TestBus();
		LoadProgram(bus, testCase.Program);
		bus.Memory[testCase.TargetAddress] = testCase.OriginalValue;
		if (testCase.PointerLocation.HasValue && testCase.PointerTarget.HasValue)
		{
			WriteZeroPagePointer(bus, testCase.PointerLocation.Value, testCase.PointerTarget.Value);
		}

		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);
		cpu.A = 0x22;
		cpu.X = 0x0C;
		cpu.Y = 0x03;
		if (testCase.CarryIn)
		{
			cpu.Status |= 0x01;
		}

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(testCase.TotalCycles, cycles);
		var targetRead = Assert.Single(bus.BusFrames.Where(frame =>
			frame.Address == testCase.TargetAddress &&
			frame.Kind == Mos6510BusAccessKind.Read));
		Assert.Equal(testCase.TotalCycles - 3, targetRead.CycleOffset);
		Assert.Equal(2, bus.Writes.Count);
		Assert.Equal((testCase.TargetAddress, testCase.OriginalValue, testCase.TotalCycles - 2), bus.Writes[0]);
		Assert.Equal((testCase.TargetAddress, testCase.FinalValue, testCase.TotalCycles - 1), bus.Writes[1]);
	}

	[Fact]
	public void BusTraceRecordsOpcodeAndDiscardedReadCycles()
	{
		var bus = new TestBus();
		LoadProgram(bus, [0x18]); // CLC
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);

		cpu.ExecuteInstruction();

		Assert.Collection(
			bus.BusFrames,
			frame =>
			{
				Assert.Equal(Mos6510BusAccessKind.OpcodeFetch, frame.Kind);
				Assert.Equal(0, frame.CycleOffset);
				Assert.Equal(0x1000, frame.Address);
				Assert.Equal((byte)0x18, frame.Value.GetValueOrDefault());
			},
			frame =>
			{
				Assert.Equal(Mos6510BusAccessKind.DummyRead, frame.Kind);
				Assert.Equal(1, frame.CycleOffset);
			});
	}

	[Fact]
	public void AbsoluteIndexedPageCrossLoadEmitsDummyReadBeforeFinalRead()
	{
		var bus = new TestBus();
		LoadProgram(bus, Absolute(0xBD, 0xD3FF)); // LDA $D3FF,X
		bus.Memory[0xD41C] = 0x5A;
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);
		cpu.X = 0x1D;

		cpu.ExecuteInstruction();

		Assert.Contains(bus.BusFrames, frame =>
			frame.Kind == Mos6510BusAccessKind.DummyRead &&
			frame.CycleOffset == 3 &&
			frame.Address == 0xD31C);
		Assert.Contains(bus.BusFrames, frame =>
			frame.Kind == Mos6510BusAccessKind.Read &&
			frame.CycleOffset == 4 &&
			frame.Address == 0xD41C &&
			frame.Value == 0x5A);
	}

	[Fact]
	public void AbsoluteIndexedStoreEmitsDummyReadBeforeFinalWrite()
	{
		var bus = new TestBus();
		LoadProgram(bus, AbsoluteX(0x9D, 0xD418, 0x0C)); // STA $D40C,X
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);
		cpu.A = 0x77;
		cpu.X = 0x0C;

		cpu.ExecuteInstruction();

		Assert.Contains(bus.BusFrames, frame =>
			frame.Kind == Mos6510BusAccessKind.DummyRead &&
			frame.CycleOffset == 3 &&
			frame.Address == 0xD418);
		Assert.Contains(bus.BusFrames, frame =>
			frame.Kind == Mos6510BusAccessKind.Write &&
			frame.CycleOffset == 4 &&
			frame.Address == 0xD418 &&
			frame.Value == 0x77);
	}

	[Fact]
	public void BranchPageCrossTraceReadsSequentialAndWrongPageAddresses()
	{
		var bus = new TestBus();
		bus.Memory[0x10FD] = 0xD0; // BNE +2
		bus.Memory[0x10FE] = 0x02;
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x10FD);
		cpu.Status &= 0xFD;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1101, cpu.ProgramCounter);
		Assert.Collection(
			bus.BusFrames,
			item => Assert.Equal((Mos6510BusAccessKind.OpcodeFetch, (ushort)0x10FD), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.OperandFetch, (ushort)0x10FE), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.DummyRead, (ushort)0x10FF), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.DummyRead, (ushort)0x1001), (item.Kind, item.Address)));
	}

	[Fact]
	public void JsrTraceReadsCurrentStackAddressBeforeWrites()
	{
		var bus = new TestBus();
		LoadProgram(bus, [0x20, 0x56, 0x34]); // JSR $3456
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);

		cpu.ExecuteInstruction();

		Assert.Equal(0x3456, cpu.ProgramCounter);
		Assert.Collection(
			bus.BusFrames,
			item => Assert.Equal((Mos6510BusAccessKind.OpcodeFetch, (ushort)0x1000), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.OperandFetch, (ushort)0x1001), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.StackRead, (ushort)0x01FD), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.StackWrite, (ushort)0x01FD), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.StackWrite, (ushort)0x01FC), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.OperandFetch, (ushort)0x1002), (item.Kind, item.Address)));
		Assert.Equal(0x10, bus.Memory[0x01FD]);
		Assert.Equal(0x02, bus.Memory[0x01FC]);
	}

	[Theory]
	[InlineData(0x08, 0x34)] // PHP
	[InlineData(0x48, 0x5A)] // PHA
	public void PushInstructionsDiscardReadTheNextPcBeforeWritingStack(byte opcode, byte expectedValue)
	{
		var bus = new TestBus();
		LoadProgram(bus, [opcode, 0xA5]);
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);
		cpu.A = 0x5A;

		Assert.Equal(3, cpu.ExecuteInstruction());

		Assert.Collection(
			bus.BusFrames,
			item => Assert.Equal((Mos6510BusAccessKind.OpcodeFetch, (ushort)0x1000), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.DummyRead, (ushort)0x1001, (byte)0xA5), (item.Kind, item.Address, item.Value)),
			item => Assert.Equal((Mos6510BusAccessKind.StackWrite, (ushort)0x01FD, expectedValue), (item.Kind, item.Address, item.Value)));
	}

	[Theory]
	[InlineData(0x28)] // PLP
	[InlineData(0x68)] // PLA
	public void PullInstructionsReadPcThenCurrentAndIncrementedStackAddresses(byte opcode)
	{
		var bus = new TestBus();
		LoadProgram(bus, [opcode, 0xA5]);
		bus.Memory[0x01FD] = 0x11;
		bus.Memory[0x01FE] = 0x42;
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);

		Assert.Equal(4, cpu.ExecuteInstruction());

		Assert.Collection(
			bus.BusFrames,
			item => Assert.Equal((Mos6510BusAccessKind.OpcodeFetch, (ushort)0x1000), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.DummyRead, (ushort)0x1001), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.StackRead, (ushort)0x01FD, (byte)0x11), (item.Kind, item.Address, item.Value)),
			item => Assert.Equal((Mos6510BusAccessKind.StackRead, (ushort)0x01FE, (byte)0x42), (item.Kind, item.Address, item.Value)));
		Assert.Equal(0xFE, cpu.StackPointer);
		if (opcode == 0x68)
		{
			Assert.Equal(0x42, cpu.A);
		}
		else
		{
			Assert.Equal(0x62, cpu.Status);
		}
	}

	[Fact]
	public void RtsReadsPulledReturnAddressBeforeIncrementingPc()
	{
		var bus = new TestBus();
		LoadProgram(bus, [0x60, 0xA5]);
		bus.Memory[0x01FD] = 0x11;
		bus.Memory[0x01FE] = 0x34;
		bus.Memory[0x01FF] = 0x12;
		bus.Memory[0x1234] = 0x7E;
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);

		Assert.Equal(6, cpu.ExecuteInstruction());

		Assert.Equal(0x1235, cpu.ProgramCounter);
		Assert.Collection(
			bus.BusFrames,
			item => Assert.Equal((Mos6510BusAccessKind.OpcodeFetch, (ushort)0x1000), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.DummyRead, (ushort)0x1001), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.StackRead, (ushort)0x01FD), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.StackRead, (ushort)0x01FE), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.StackRead, (ushort)0x01FF), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.DummyRead, (ushort)0x1234, (byte)0x7E), (item.Kind, item.Address, item.Value)));
	}

	[Fact]
	public void RtiReadsPcCurrentStackStatusAndReturnAddressInOrder()
	{
		var bus = new TestBus();
		LoadProgram(bus, [0x40, 0xA5]);
		bus.Memory[0x01FD] = 0x11;
		bus.Memory[0x01FE] = 0x10;
		bus.Memory[0x01FF] = 0x78;
		bus.Memory[0x0100] = 0x56;
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);

		Assert.Equal(6, cpu.ExecuteInstruction());

		Assert.Equal(0x5678, cpu.ProgramCounter);
		Assert.Equal(0x20, cpu.Status);
		Assert.Collection(
			bus.BusFrames,
			item => Assert.Equal((Mos6510BusAccessKind.OpcodeFetch, (ushort)0x1000), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.DummyRead, (ushort)0x1001), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.StackRead, (ushort)0x01FD), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.StackRead, (ushort)0x01FE), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.StackRead, (ushort)0x01FF), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.StackRead, (ushort)0x0100), (item.Kind, item.Address)));
	}

	[Fact]
	public void NotTakenBranchPerformsOnlyOpcodeAndOffsetReads()
	{
		var bus = new TestBus();
		LoadProgram(bus, [0xD0, 0x7F]); // BNE, not taken because Z is set
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);
		cpu.Status |= 0x02;

		Assert.Equal(2, cpu.ExecuteInstruction());

		Assert.Equal(0x1002, cpu.ProgramCounter);
		Assert.Collection(
			bus.BusFrames,
			item => Assert.Equal((Mos6510BusAccessKind.OpcodeFetch, (ushort)0x1000), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.OperandFetch, (ushort)0x1001), (item.Kind, item.Address)));
	}

	[Fact]
	public void TakenSamePageBranchDiscardReadsTheSequentialPc()
	{
		var bus = new TestBus();
		LoadProgram(bus, [0xD0, 0x02]);
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);

		Assert.Equal(3, cpu.ExecuteInstruction());

		Assert.Equal(0x1004, cpu.ProgramCounter);
		Assert.Collection(
			bus.BusFrames,
			item => Assert.Equal((Mos6510BusAccessKind.OpcodeFetch, (ushort)0x1000), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.OperandFetch, (ushort)0x1001), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.DummyRead, (ushort)0x1002), (item.Kind, item.Address)));
	}

	[Theory]
	[InlineData(0x1100, 0xFC, 0x10FE, 0x1102, 0x11FE)]
	[InlineData(0xFFFD, 0x02, 0x0001, 0xFFFF, 0xFF01)]
	public void PageCrossBranchUsesOldPageForItsCorrectionRead(
		ushort start,
		byte offset,
		ushort target,
		ushort sequentialRead,
		ushort wrongPageRead)
	{
		var bus = new TestBus();
		bus.Memory[start] = 0xD0;
		bus.Memory[(ushort)(start + 1)] = offset;
		var cpu = new Mos6510(bus);
		cpu.InitializeState(start);

		Assert.Equal(4, cpu.ExecuteInstruction());

		Assert.Equal(target, cpu.ProgramCounter);
		Assert.Collection(
			bus.BusFrames,
			item => Assert.Equal((Mos6510BusAccessKind.OpcodeFetch, start), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.OperandFetch, (ushort)(start + 1)), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.DummyRead, sequentialRead), (item.Kind, item.Address)),
			item => Assert.Equal((Mos6510BusAccessKind.DummyRead, wrongPageRead), (item.Kind, item.Address)));
	}

	[Fact]
	public void DiscardedReadInvokesReadToClearSideEffectsAndCapturesReturnedValue()
	{
		var bus = new TestBus { ReadToClearAddress = 0x1001 };
		LoadProgram(bus, [0x18, 0xC7]); // CLC
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);

		cpu.ExecuteInstruction();

		Assert.Equal(1, bus.ReadToClearCount);
		Assert.Equal(0, bus.Memory[0x1001]);
		Assert.Equal((ushort)0x1001, bus.BusFrames[1].Address);
		Assert.Equal((byte)0xC7, bus.BusFrames[1].Value);
		Assert.Equal(Mos6510BusAccessKind.DummyRead, bus.BusFrames[1].Kind);
	}

	[Fact]
	public void RdyRepeatsTheSameDiscardedReadAndItsSideEffects()
	{
		var bus = new TestBus { ReadToClearAddress = 0x1001 };
		LoadProgram(bus, [0x18, 0xC7]); // CLC
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);
		Assert.True(cpu.StepCycle().CpuAdvanced);

		cpu.SetReadyLine(false);
		Assert.False(cpu.StepCycle().CpuAdvanced);
		Assert.False(cpu.StepCycle().CpuAdvanced);
		cpu.SetReadyLine(true);
		var completed = cpu.StepCycle();

		Assert.Equal(Mos6510OperationKind.Instruction, completed.CompletedOperation);
		Assert.Equal(3, bus.ReadToClearCount);
		Assert.Equal(new byte?[] { 0xC7, 0x00, 0x00 }, bus.BusFrames.Skip(1).Select(frame => frame.Value));
		Assert.All(bus.BusFrames.Skip(1), frame =>
		{
			Assert.Equal(0x1001, frame.Address);
			Assert.Equal(Mos6510BusAccessKind.DummyRead, frame.Kind);
		});
	}

	[Fact]
	public void ImpliedOperationCommitsOnlyAfterItsDiscardedReadCompletes()
	{
		var bus = new TestBus();
		LoadProgram(bus, [0x18, 0xEA]); // CLC
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);
		cpu.Status |= 0x01;
		cpu.StepCycle();

		cpu.SetReadyLine(false);
		Assert.False(cpu.StepCycle().CpuAdvanced);
		Assert.Equal(0x01, cpu.Status & 0x01);

		cpu.SetReadyLine(true);
		var completed = cpu.StepCycle();
		Assert.Equal(Mos6510OperationKind.Instruction, completed.CompletedOperation);
		Assert.Equal(0, cpu.Status & 0x01);
	}

	[Fact]
	public void BrkTraceOrdersSignatureStackWritesAndVectorReads()
	{
		var bus = new TestBus();
		LoadProgram(bus, [0x00, 0xEA]);
		bus.Memory[0xFFFE] = 0x34;
		bus.Memory[0xFFFF] = 0x12;
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);
		cpu.Status &= 0xFB;

		for (var cycle = 0; cycle < 6; cycle++)
		{
			var result = cpu.StepCycle();
			Assert.Equal(Mos6510OperationKind.None, result.CompletedOperation);
			Assert.Equal(0, cpu.Status & 0x04);
		}

		var completed = cpu.StepCycle();

		Assert.Equal(Mos6510OperationKind.Instruction, completed.CompletedOperation);
		Assert.Equal(0x1234, cpu.ProgramCounter);
		Assert.Equal(0x04, cpu.Status & 0x04);
		Assert.Equal(0x10, bus.Memory[0x01FD]);
		Assert.Equal(0x02, bus.Memory[0x01FC]);
		Assert.Equal(0x30, bus.Memory[0x01FB] & 0x30);
		Assert.Collection(
			bus.BusFrames.Select(frame => (frame.Kind, frame.CycleOffset)).ToArray(),
			item => Assert.Equal((Mos6510BusAccessKind.OpcodeFetch, 0), item),
			item => Assert.Equal((Mos6510BusAccessKind.OperandFetch, 1), item),
			item => Assert.Equal((Mos6510BusAccessKind.StackWrite, 2), item),
			item => Assert.Equal((Mos6510BusAccessKind.StackWrite, 3), item),
			item => Assert.Equal((Mos6510BusAccessKind.StackWrite, 4), item),
			item => Assert.Equal((Mos6510BusAccessKind.VectorRead, 5), item),
			item => Assert.Equal((Mos6510BusAccessKind.VectorRead, 6), item));
	}

	[Fact]
	public void IrqTraceOrdersDiscardedReadsStackWritesAndVectorReads()
	{
		var bus = new TestBus();
		bus.Memory[0xFFFE] = 0x78;
		bus.Memory[0xFFFF] = 0x56;
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x2345);
		cpu.Status &= 0xFB;

		cpu.SetIrqLine(true);
		for (var cycle = 0; cycle < 7; cycle++)
		{
			cpu.StepCycle();
		}

		Assert.Equal(0x5678, cpu.ProgramCounter);
		Assert.Equal(7, cpu.Cycles);
		Assert.Collection(
			bus.BusFrames,
			item => Assert.Equal((Mos6510BusAccessKind.DiscardedOpcodeFetch, (ushort)0x2345, 0), (item.Kind, item.Address, item.CycleOffset)),
			item => Assert.Equal((Mos6510BusAccessKind.DummyRead, (ushort)0x2345, 1), (item.Kind, item.Address, item.CycleOffset)),
			item => Assert.Equal((Mos6510BusAccessKind.StackWrite, (ushort)0x01FD, (byte)0x23, 2), (item.Kind, item.Address, item.Value.GetValueOrDefault(), item.CycleOffset)),
			item => Assert.Equal((Mos6510BusAccessKind.StackWrite, (ushort)0x01FC, (byte)0x45, 3), (item.Kind, item.Address, item.Value.GetValueOrDefault(), item.CycleOffset)),
			item => Assert.Equal((Mos6510BusAccessKind.StackWrite, (ushort)0x01FB, 4), (item.Kind, item.Address, item.CycleOffset)),
			item => Assert.Equal((Mos6510BusAccessKind.VectorRead, (ushort)0xFFFE, 5), (item.Kind, item.Address, item.CycleOffset)),
			item => Assert.Equal((Mos6510BusAccessKind.VectorRead, (ushort)0xFFFF, 6), (item.Kind, item.Address, item.CycleOffset)));
		Assert.Equal(0, bus.Memory[0x01FB] & 0x10);
	}

	[Fact]
	public void NmiIsSevenCyclesAndHeldLineDoesNotRetrigger()
	{
		var bus = new TestBus();
		bus.Memory[0xFFFA] = 0x78;
		bus.Memory[0xFFFB] = 0x56;
		bus.Memory[0x5678] = 0xEA;
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x3456);
		cpu.SetNmiLine(true);

		Mos6510CycleResult result = default;
		for (var cycle = 0; cycle < 7; cycle++)
		{
			result = cpu.StepCycle();
		}

		Assert.Equal(Mos6510OperationKind.Nmi, result.CompletedOperation);
		Assert.Equal(0x5678, cpu.ProgramCounter);
		Assert.Equal(7, cpu.Cycles);
		Assert.Equal(7, bus.BusFrames.Count);

		bus.BusFrames.Clear();
		Assert.Equal(2, cpu.ExecuteInstruction());
		Assert.Equal(0x5679, cpu.ProgramCounter);
		Assert.DoesNotContain(bus.BusFrames, frame => frame.Kind == Mos6510BusAccessKind.VectorRead);

		cpu.SetNmiLine(false);
		cpu.SetNmiLine(true);
		Assert.Equal(Mos6510OperationKind.Nmi, cpu.StepCycle().StartedOperation);
	}

	[Fact]
	public void ResetReleasePerformsSevenReadCyclesAndPreservesNmosRegisters()
	{
		var bus = new TestBus();
		bus.Memory[0xFFFC] = 0x34;
		bus.Memory[0xFFFD] = 0x12;
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x4567);
		cpu.A = 0x11;
		cpu.X = 0x22;
		cpu.Y = 0x33;
		cpu.Status |= 0x08;

		cpu.SetResetLine(true);
		Assert.False(cpu.StepCycle().CpuAdvanced);
		Assert.False(cpu.StepCycle().CpuAdvanced);
		Assert.Empty(bus.BusFrames);
		cpu.SetResetLine(false);
		Mos6510CycleResult result = default;
		for (var cycle = 0; cycle < 7; cycle++)
		{
			result = cpu.StepCycle();
		}

		Assert.Equal(Mos6510OperationKind.Reset, result.CompletedOperation);
		Assert.Equal(0x1234, cpu.ProgramCounter);
		Assert.Equal(0xFA, cpu.StackPointer);
		Assert.Equal((0x11, 0x22, 0x33), (cpu.A, cpu.X, cpu.Y));
		Assert.Equal(0x0C, cpu.Status & 0x0C);
		Assert.Equal(9, cpu.Cycles);
		Assert.Collection(
			bus.BusFrames.Select(frame => (frame.Kind, frame.Address)).ToArray(),
			item => Assert.Equal((Mos6510BusAccessKind.DiscardedOpcodeFetch, (ushort)0x4567), item),
			item => Assert.Equal((Mos6510BusAccessKind.DummyRead, (ushort)0x4567), item),
			item => Assert.Equal((Mos6510BusAccessKind.StackRead, (ushort)0x01FD), item),
			item => Assert.Equal((Mos6510BusAccessKind.StackRead, (ushort)0x01FC), item),
			item => Assert.Equal((Mos6510BusAccessKind.StackRead, (ushort)0x01FB), item),
			item => Assert.Equal((Mos6510BusAccessKind.VectorRead, (ushort)0xFFFC), item),
			item => Assert.Equal((Mos6510BusAccessKind.VectorRead, (ushort)0xFFFD), item));
	}

	[Fact]
	public void RdyRepeatsReadButAecSuppressesBusAccess()
	{
		var bus = new TestBus();
		bus.Memory[0x1000] = 0xEA;
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);

		cpu.SetReadyLine(false);
		Assert.False(cpu.StepCycle().CpuAdvanced);
		Assert.False(cpu.StepCycle().CpuAdvanced);
		Assert.Equal(2, bus.BusFrames.Count);
		Assert.All(bus.BusFrames, frame => Assert.Equal(0x1000, frame.Address));

		cpu.SetBusAvailable(false);
		Assert.False(cpu.StepCycle().CpuAdvanced);
		Assert.Equal(2, bus.BusFrames.Count);

		cpu.SetReadyLine(true);
		cpu.SetBusAvailable(true);
		Assert.True(cpu.StepCycle().CpuAdvanced);
		Assert.Equal(3, bus.BusFrames.Count);
		Assert.Equal(0x1001, cpu.ProgramCounter);
	}

	[Fact]
	public void CliAndSeiUsePreviousInterruptMaskAtSamplingBoundary()
	{
		var cliBus = new TestBus();
		cliBus.Memory[0x1000] = 0x58;
		cliBus.Memory[0x1001] = 0xEA;
		var cli = new Mos6510(cliBus);
		cli.InitializeState(0x1000);
		cli.SetIrqLine(true);
		Assert.Equal(Mos6510OperationKind.Instruction, cli.StepCycle().StartedOperation);
		cli.StepCycle();
		Assert.Equal(Mos6510OperationKind.Instruction, cli.StepCycle().StartedOperation);
		cli.StepCycle();
		Assert.Equal(Mos6510OperationKind.Irq, cli.StepCycle().StartedOperation);

		var seiBus = new TestBus();
		seiBus.Memory[0x1000] = 0x78;
		var sei = new Mos6510(seiBus);
		sei.InitializeState(0x1000);
		sei.Status &= 0xFB;
		sei.StepCycle();
		sei.SetIrqLine(true);
		sei.StepCycle();
		Assert.Equal(Mos6510OperationKind.Irq, sei.StepCycle().StartedOperation);
	}

	[Fact]
	public void NmiCanHijackIrqBeforeVectorLowRead()
	{
		var bus = new TestBus();
		bus.Memory[0xFFFA] = 0x34;
		bus.Memory[0xFFFB] = 0x12;
		bus.Memory[0xFFFE] = 0x78;
		bus.Memory[0xFFFF] = 0x56;
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x2000);
		cpu.Status &= 0xFB;
		cpu.SetIrqLine(true);
		for (var cycle = 0; cycle < 5; cycle++)
		{
			cpu.StepCycle();
		}

		cpu.SetNmiLine(true);
		cpu.StepCycle();
		var result = cpu.StepCycle();
		Assert.Equal(Mos6510OperationKind.Nmi, result.CompletedOperation);
		Assert.Equal(0x1234, cpu.ProgramCounter);
		Assert.Equal(new ushort[] { 0xFFFA, 0xFFFB },
			bus.BusFrames.Where(frame => frame.Kind == Mos6510BusAccessKind.VectorRead).Select(frame => frame.Address));
	}

	[Fact]
	public void OneCycleResetPulseDoesNotReleaseJamButValidPulseDoes()
	{
		var bus = new TestBus();
		bus.Memory[0x1000] = 0x02; // JAM
		bus.Memory[0xFFFC] = 0x34;
		bus.Memory[0xFFFD] = 0x12;
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);
		Assert.Equal(Mos6510OperationKind.Halted, cpu.StepCycle().CompletedOperation);

		bus.BusFrames.Clear();
		cpu.SetResetLine(true);
		Assert.False(cpu.StepCycle().CpuAdvanced);
		cpu.SetResetLine(false);
		Assert.Equal(Mos6510OperationKind.Halted, cpu.StepCycle().CompletedOperation);
		Assert.True(cpu.Halted);
		Assert.Empty(bus.BusFrames);

		cpu.SetResetLine(true);
		cpu.StepCycle();
		cpu.StepCycle();
		cpu.SetResetLine(false);
		Mos6510CycleResult result = default;
		for (var cycle = 0; cycle < 7; cycle++)
		{
			result = cpu.StepCycle();
		}

		Assert.Equal(Mos6510OperationKind.Reset, result.CompletedOperation);
		Assert.False(cpu.Halted);
		Assert.Equal(0x1234, cpu.ProgramCounter);
	}

	[Fact]
	public void InterruptVectorReadsRepeatOnRdyAndDisappearWhenAecIsLow()
	{
		var bus = new TestBus();
		bus.Memory[0xFFFE] = 0x78;
		bus.Memory[0xFFFF] = 0x56;
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x2000);
		cpu.Status &= 0xFB;
		cpu.SetIrqLine(true);
		for (var cycle = 0; cycle < 5; cycle++)
		{
			Assert.True(cpu.StepCycle().CpuAdvanced);
		}

		cpu.SetReadyLine(false);
		Assert.False(cpu.StepCycle().CpuAdvanced);
		Assert.False(cpu.StepCycle().CpuAdvanced);
		Assert.Equal(2, bus.BusFrames.Count(frame => frame.Kind == Mos6510BusAccessKind.VectorRead));

		cpu.SetBusAvailable(false);
		Assert.False(cpu.StepCycle().CpuAdvanced);
		Assert.Equal(2, bus.BusFrames.Count(frame => frame.Kind == Mos6510BusAccessKind.VectorRead));

		cpu.SetReadyLine(true);
		cpu.SetBusAvailable(true);
		Assert.True(cpu.StepCycle().CpuAdvanced);
		var result = cpu.StepCycle();
		Assert.Equal(Mos6510OperationKind.Irq, result.CompletedOperation);
		Assert.Equal(0x5678, cpu.ProgramCounter);
		Assert.Equal(3, bus.BusFrames.Count(frame => frame.Address == 0xFFFE));
		Assert.Equal(1, bus.BusFrames.Count(frame => frame.Address == 0xFFFF));
	}

	[Fact]
	public void RdyDoesNotStopWritesButAecDoes()
	{
		var bus = new TestBus();
		LoadProgram(bus, [0x8D, 0x00, 0x40]); // STA $4000
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);
		cpu.A = 0x5A;
		for (var cycle = 0; cycle < 3; cycle++)
		{
			cpu.StepCycle();
		}

		cpu.SetReadyLine(false);
		cpu.SetBusAvailable(false);
		Assert.False(cpu.StepCycle().CpuAdvanced);
		Assert.Equal(0, bus.Memory[0x4000]);
		cpu.SetBusAvailable(true);
		var result = cpu.StepCycle();
		Assert.True(result.CpuAdvanced);
		Assert.Equal(Mos6510OperationKind.Instruction, result.CompletedOperation);
		Assert.Equal(0x5A, bus.Memory[0x4000]);
	}

	[Fact]
	public void IrqWithdrawalUsesTheInstructionBoundarySample()
	{
		var earlyBus = new TestBus();
		LoadProgram(earlyBus, [0xEA, 0xEA]);
		var early = new Mos6510(earlyBus);
		early.InitializeState(0x1000);
		early.Status &= 0xFB;
		early.StepCycle();
		early.SetIrqLine(true);
		early.SetIrqLine(false);
		early.StepCycle();
		Assert.Equal(Mos6510OperationKind.Instruction, early.StepCycle().StartedOperation);

		var sampledBus = new TestBus();
		LoadProgram(sampledBus, [0xEA]);
		var sampled = new Mos6510(sampledBus);
		sampled.InitializeState(0x1000);
		sampled.Status &= 0xFB;
		sampled.StepCycle();
		sampled.SetIrqLine(true);
		sampled.StepCycle();
		sampled.SetIrqLine(false);
		Assert.Equal(Mos6510OperationKind.Irq, sampled.StepCycle().StartedOperation);
	}

	[Fact]
	public void SimultaneousNmiAndIrqSelectsNmi()
	{
		var bus = new TestBus();
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x2000);
		cpu.Status &= 0xFB;
		cpu.SetIrqLine(true);
		cpu.SetNmiLine(true);

		Assert.Equal(Mos6510OperationKind.Nmi, cpu.StepCycle().StartedOperation);
	}

	[Fact]
	public void PlpDelaysNewlyUnmaskedIrqButRtiDoesNot()
	{
		var plpBus = new TestBus();
		LoadProgram(plpBus, [0x28, 0xEA]); // PLP; NOP
		plpBus.Memory[0x01FE] = 0x20;
		var plp = new Mos6510(plpBus);
		plp.InitializeState(0x1000);
		plp.SetIrqLine(true);
		plp.ExecuteInstruction();
		Assert.Equal(Mos6510OperationKind.Instruction, plp.StepCycle().StartedOperation);
		plp.StepCycle();
		Assert.Equal(Mos6510OperationKind.Irq, plp.StepCycle().StartedOperation);

		var rtiBus = new TestBus();
		rtiBus.Memory[0x1000] = 0x40; // RTI
		rtiBus.Memory[0x01FE] = 0x20;
		rtiBus.Memory[0x01FF] = 0x00;
		rtiBus.Memory[0x0100] = 0x20;
		var rti = new Mos6510(rtiBus);
		rti.InitializeState(0x1000);
		rti.SetIrqLine(true);
		rti.ExecuteInstruction();
		Assert.Equal(0x2000, rti.ProgramCounter);
		Assert.Equal(Mos6510OperationKind.Irq, rti.StepCycle().StartedOperation);
	}

	[Theory]
	[InlineData(0x1000, 0x02)]
	[InlineData(0x10FD, 0x01)]
	public void TakenBranchDelaysIrqByOneInstruction(ushort start, byte offset)
	{
		var bus = new TestBus();
		bus.Memory[start] = 0xD0; // BNE
		bus.Memory[(ushort)(start + 1)] = offset;
		var target = (ushort)(start + 2 + unchecked((sbyte)offset));
		bus.Memory[target] = 0xEA;
		var cpu = new Mos6510(bus);
		cpu.InitializeState(start);
		cpu.Status &= 0xF9; // IRQ enabled and Z clear.
		cpu.StepCycle();
		cpu.SetIrqLine(true);
		Mos6510CycleResult branchResult;
		do
		{
			branchResult = cpu.StepCycle();
		}
		while (branchResult.CompletedOperation == Mos6510OperationKind.None);

		Assert.Equal(target, cpu.ProgramCounter);
		Assert.Equal(Mos6510OperationKind.Instruction, cpu.StepCycle().StartedOperation);
		cpu.StepCycle();
		Assert.Equal(Mos6510OperationKind.Irq, cpu.StepCycle().StartedOperation);
	}

	[Fact]
	public void TakenBranchDelaysNmiByOneInstruction()
	{
		var bus = new TestBus();
		LoadProgram(bus, [0xD0, 0x02]); // BNE $1004
		bus.Memory[0x1004] = 0xEA;
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);
		cpu.Status &= 0xFD;
		cpu.StepCycle();
		cpu.SetNmiLine(true);
		Mos6510CycleResult branchResult;
		do
		{
			branchResult = cpu.StepCycle();
		}
		while (branchResult.CompletedOperation == Mos6510OperationKind.None);

		Assert.Equal(Mos6510OperationKind.Instruction, cpu.StepCycle().StartedOperation);
		cpu.StepCycle();
		Assert.Equal(Mos6510OperationKind.Nmi, cpu.StepCycle().StartedOperation);
	}

	[Fact]
	public void NmiAfterIrqVectorLowWaitsUntilAfterFirstHandlerInstruction()
	{
		var bus = new TestBus();
		bus.Memory[0xFFFE] = 0x00;
		bus.Memory[0xFFFF] = 0x30;
		bus.Memory[0x3000] = 0xEA;
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x2000);
		cpu.Status &= 0xFB;
		cpu.SetIrqLine(true);
		for (var cycle = 0; cycle < 6; cycle++)
		{
			cpu.StepCycle();
		}

		cpu.SetNmiLine(true);
		Assert.Equal(Mos6510OperationKind.Irq, cpu.StepCycle().CompletedOperation);
		Assert.Equal(Mos6510OperationKind.Instruction, cpu.StepCycle().StartedOperation);
		cpu.StepCycle();
		Assert.Equal(Mos6510OperationKind.Nmi, cpu.StepCycle().StartedOperation);
	}

	[Fact]
	public void NmiHijacksBrkOnlyBeforeItsVectorLowRead()
	{
		var earlyBus = new TestBus();
		LoadProgram(earlyBus, [0x00, 0xEA]);
		earlyBus.Memory[0xFFFA] = 0x00;
		earlyBus.Memory[0xFFFB] = 0x40;
		earlyBus.Memory[0xFFFE] = 0x00;
		earlyBus.Memory[0xFFFF] = 0x30;
		var early = new Mos6510(earlyBus);
		early.InitializeState(0x1000);
		for (var cycle = 0; cycle < 5; cycle++)
		{
			early.StepCycle();
		}

		early.SetNmiLine(true);
		early.StepCycle();
		early.StepCycle();
		Assert.Equal(0x4000, early.ProgramCounter);
		Assert.Equal(new ushort[] { 0xFFFA, 0xFFFB },
			earlyBus.BusFrames.Where(frame => frame.Kind == Mos6510BusAccessKind.VectorRead).Select(frame => frame.Address));

		var lateBus = new TestBus();
		LoadProgram(lateBus, [0x00, 0xEA]);
		lateBus.Memory[0xFFFE] = 0x00;
		lateBus.Memory[0xFFFF] = 0x30;
		lateBus.Memory[0x3000] = 0xEA;
		var late = new Mos6510(lateBus);
		late.InitializeState(0x1000);
		for (var cycle = 0; cycle < 6; cycle++)
		{
			late.StepCycle();
		}

		late.SetNmiLine(true);
		late.StepCycle();
		Assert.Equal(0x3000, late.ProgramCounter);
		Assert.Equal(Mos6510OperationKind.Instruction, late.StepCycle().StartedOperation);
		late.StepCycle();
		Assert.Equal(Mos6510OperationKind.Nmi, late.StepCycle().StartedOperation);
	}

	[Fact]
	public void RdyStallOnUnstableStoreDummyReadDropsTheDataMask()
	{
		var bus = new TestBus();
		LoadProgram(bus, [0x9F, 0x00, 0x40]); // AHX $4000,Y
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);
		cpu.A = 0xFF;
		cpu.X = 0xF7;
		cpu.Y = 0;
		for (var cycle = 0; cycle < 3; cycle++)
		{
			cpu.StepCycle();
		}

		cpu.SetReadyLine(false);
		Assert.False(cpu.StepCycle().CpuAdvanced);
		cpu.SetReadyLine(true);
		cpu.StepCycle();
		cpu.StepCycle();
		Assert.Equal(0xF7, bus.Memory[0x4000]);
	}

	[Fact]
	public void EveryOpcodeCompletesOrHaltsThroughCycleStepping()
	{
		for (var opcode = 0; opcode <= 0xFF; opcode++)
		{
			var bus = new TestBus();
			bus.Memory[0x1000] = (byte)opcode;
			var cpu = new Mos6510(bus);
			cpu.InitializeState(0x1000);
			Mos6510OperationKind completed = Mos6510OperationKind.None;
			for (var cycle = 0; cycle < 16 && completed == Mos6510OperationKind.None; cycle++)
			{
				completed = cpu.StepCycle().CompletedOperation;
			}

			Assert.True(
				completed == Mos6510OperationKind.Instruction || completed == Mos6510OperationKind.Halted,
				$"Opcode ${opcode:X2} did not complete through StepCycle().");
			Assert.Equal(cpu.Cycles, bus.BusFrames.Count);
		}
	}

	[Fact]
	public void ConvenienceExecutionMatchesCycleSteppingForEveryOpcode()
	{
		for (var opcode = 0; opcode <= 0xFF; opcode++)
		{
			var wrapperBus = new TestBus();
			for (var address = 0; address < wrapperBus.Memory.Length; address++)
			{
				wrapperBus.Memory[address] = (byte)((address * 37 + 11) & 0xFF);
			}

			wrapperBus.Memory[0x1000] = (byte)opcode;
			wrapperBus.Memory[0x1001] = 0x10;
			wrapperBus.Memory[0x1002] = 0x20;
			var steppedBus = new TestBus();
			Array.Copy(wrapperBus.Memory, steppedBus.Memory, wrapperBus.Memory.Length);
			var wrapper = CreateComparisonCpu(wrapperBus);
			var stepped = CreateComparisonCpu(steppedBus);

			var wrapperCycles = wrapper.ExecuteInstruction();
			Mos6510OperationKind completed = Mos6510OperationKind.None;
			while (completed == Mos6510OperationKind.None)
			{
				completed = stepped.StepCycle().CompletedOperation;
			}

			Assert.Equal(wrapperCycles, stepped.Cycles);
			Assert.Equal(
				(wrapper.A, wrapper.X, wrapper.Y, wrapper.StackPointer, wrapper.ProgramCounter, wrapper.Status, wrapper.Halted),
				(stepped.A, stepped.X, stepped.Y, stepped.StackPointer, stepped.ProgramCounter, stepped.Status, stepped.Halted));
			Assert.Equal(wrapperBus.Memory, steppedBus.Memory);
			Assert.Equal(wrapperBus.BusFrames, steppedBus.BusFrames);
		}
	}

	public static IEnumerable<object[]> StoreWriteCases()
	{
		yield return Store("STA zpg", [0x85, 0x18], 3, 0x0018, StoreA);
		yield return Store("STA zpg,X", [0x95, ZeroPageOperandForX(0x18)], 4, 0x0018, StoreA);
		yield return Store("STA abs", Absolute(0x8D, 0xD418), 4, 0xD418, StoreA);
		yield return Store("STA abs,X", AbsoluteX(0x9D, 0xD418, StoreX), 5, 0xD418, StoreA);
		yield return Store("STA abs,Y", AbsoluteY(0x99, 0xD418, StoreY), 5, 0xD418, StoreA);
		yield return Store("STA (zpg,X)", [0x81, 0x20], 6, 0xD418, StoreA, PointerForIndirectX(0x20, StoreX), 0xD418);
		yield return Store("STA (zpg),Y", [0x91, 0x30], 6, 0xD418, StoreA, 0x30, unchecked((ushort)(0xD418 - StoreY)));
		yield return Store("STX zpg", [0x86, 0x18], 3, 0x0018, StoreX);
		yield return Store("STX zpg,Y", [0x96, ZeroPageOperandForY(0x18)], 4, 0x0018, StoreX);
		yield return Store("STX abs", Absolute(0x8E, 0xD418), 4, 0xD418, StoreX);
		yield return Store("STY zpg", [0x84, 0x18], 3, 0x0018, StoreY);
		yield return Store("STY zpg,X", [0x94, ZeroPageOperandForX(0x18)], 4, 0x0018, StoreY);
		yield return Store("STY abs", Absolute(0x8C, 0xD418), 4, 0xD418, StoreY);
		yield return Store("SAX zpg", [0x87, 0x18], 3, 0x0018, StoreAAndX);
		yield return Store("SAX zpg,Y", [0x97, ZeroPageOperandForY(0x18)], 4, 0x0018, StoreAAndX);
		yield return Store("SAX abs", Absolute(0x8F, 0xD418), 4, 0xD418, StoreAAndX);
		yield return Store("SAX (zpg,X)", [0x83, 0x20], 6, 0xD418, StoreAAndX, PointerForIndirectX(0x20, StoreX), 0xD418);
		yield return Store("AHX (zpg),Y", [0x93, 0x30], 6, 0xD418, StoreHighMasked(StoreAAndX, 0xD418), 0x30, unchecked((ushort)(0xD418 - StoreY)));
		yield return Store("AHX abs,Y", AbsoluteY(0x9F, 0xD418, StoreY), 5, 0xD418, StoreHighMasked(StoreAAndX, 0xD418));
		yield return Store("SHX abs,Y", AbsoluteY(0x9E, 0xD418, StoreY), 5, 0xD418, StoreHighMasked(StoreX, 0xD418));
		yield return Store("SHY abs,X page-cross corruption", AbsoluteX(0x9C, 0xD418, StoreX), 5, 0x0018, 0x00);
		yield return Store("TAS abs,Y", AbsoluteY(0x9B, 0xD418, StoreY), 5, 0xD418, StoreHighMasked(StoreAAndX, 0xD418));
	}

	public static IEnumerable<object[]> LoadReadCases()
	{
		yield return Load("LDA zpg", [0xA5, 0x20], 3, 0x0020);
		yield return Load("LDA zpg,X", [0xB5, 0x20], 4, 0x0023, x: 0x03);
		yield return Load("LDA abs", Absolute(0xAD, 0xD41C), 4, 0xD41C);
		yield return Load("LDA abs,X", Absolute(0xBD, 0xD419), 4, 0xD41C, x: 0x03);
		yield return Load("LDA abs,X page cross", Absolute(0xBD, 0xD3FF), 5, 0xD41C, x: 0x1D);
		yield return Load("LDA (zpg),Y", [0xB1, 0x30], 5, 0xD41C, y: 0x03, pointerLocation: 0x30, pointerTarget: 0xD419);
		yield return Load("LDA (zpg),Y page cross", [0xB1, 0x30], 6, 0xD41C, y: 0x1D, pointerLocation: 0x30, pointerTarget: 0xD3FF);
		yield return Load("LAX abs", Absolute(0xAF, 0xD41C), 4, 0xD41C);
	}

	public static IEnumerable<object[]> ReadModifyWriteCases()
	{
		yield return Rmw("ASL abs", Absolute(0x0E, 0xD418), 6, 0xD418, 0x81, 0x02);
		yield return Rmw("LSR abs,X", AbsoluteX(0x5E, 0xD418, 0x0C), 7, 0xD418, 0x81, 0x40);
		yield return Rmw("ROL abs", Absolute(0x2E, 0xD418), 6, 0xD418, 0x41, 0x82);
		yield return Rmw("ROR abs,X", AbsoluteX(0x7E, 0xD418, 0x0C), 7, 0xD418, 0x02, 0x81, carryIn: true);
		yield return Rmw("INC abs", Absolute(0xEE, 0xD418), 6, 0xD418, 0x7F, 0x80);
		yield return Rmw("DEC abs,X", AbsoluteX(0xDE, 0xD418, 0x0C), 7, 0xD418, 0x80, 0x7F);
		yield return Rmw("SLO abs", Absolute(0x0F, 0xD418), 6, 0xD418, 0x81, 0x02);
		yield return Rmw("RLA abs,X", AbsoluteX(0x3F, 0xD418, 0x0C), 7, 0xD418, 0x41, 0x82);
		yield return Rmw("SRE abs,Y", AbsoluteY(0x5B, 0xD418, 0x03), 7, 0xD418, 0x81, 0x40);
		yield return Rmw("RRA (zpg,X)", [0x63, 0x20], 8, 0xD418, 0x02, 0x81, carryIn: true, PointerForIndirectX(0x20, 0x0C), 0xD418);
		yield return Rmw("DCP (zpg),Y", [0xD3, 0x30], 8, 0xD418, 0x80, 0x7F, carryIn: false, 0x30, unchecked((ushort)(0xD418 - 0x03)));
		yield return Rmw("ISC abs", Absolute(0xEF, 0xD418), 6, 0xD418, 0x7F, 0x80);
	}

	private readonly record struct CpuBusTraceFrame(
		long RequestedCycle,
		long Cycle,
		int CycleOffset,
		byte Opcode,
		ushort Address,
		byte? Value,
		Mos6510BusAccessKind Kind,
		bool DelayedByVic);

	private sealed class TestBus : IMos6510Bus
	{
		public byte[] Memory { get; } = new byte[65536];

		public List<(ushort Address, byte Value, int CycleOffset)> Writes { get; } = new();

		public List<(ushort Address, int CycleOffset)> Reads { get; } = new();

		public List<CpuBusTraceFrame> BusFrames { get; } = new();

		public ushort LastWriteAddress { get; private set; }

		public byte LastWriteValue { get; private set; }

		public long LastWriteCycle { get; private set; }

		public ushort? ReadToClearAddress { get; init; }

		public int ReadToClearCount { get; private set; }

		public byte Read(ushort address, Mos6510BusAccessKind kind = Mos6510BusAccessKind.Read)
		{
			var cycleOffset = BusFrames.Count;
			Reads.Add((address, cycleOffset));
			var value = Memory[address];
			if (address == ReadToClearAddress)
			{
				ReadToClearCount++;
				Memory[address] = 0;
			}

			BusFrames.Add(new CpuBusTraceFrame(cycleOffset, cycleOffset, cycleOffset, value, address, value, kind, DelayedByVic: false));
			return value;
		}

		public void Write(ushort address, byte value, Mos6510BusAccessKind kind = Mos6510BusAccessKind.Write)
		{
			var cycleOffset = BusFrames.Count;
			Memory[address] = value;
			Writes.Add((address, value, cycleOffset));
			BusFrames.Add(new CpuBusTraceFrame(cycleOffset, cycleOffset, cycleOffset, 0, address, value, kind, DelayedByVic: false));
			LastWriteAddress = address;
			LastWriteValue = value;
			LastWriteCycle = cycleOffset;
		}
	}

	private const byte StoreA = 0xF7;
	private const byte StoreX = 0xD5;
	private const byte StoreY = 0x03;
	private const byte StoreAAndX = StoreA & StoreX;

	private static object[] Store(
		string name,
		byte[] program,
		int totalCycles,
		ushort targetAddress,
		byte expectedValue,
		byte? pointerLocation = null,
		ushort? pointerTarget = null)
	{
		return [new StoreWriteCase(name, program, totalCycles, targetAddress, expectedValue, pointerLocation, pointerTarget)];
	}

	private static object[] Load(
		string name,
		byte[] program,
		int totalCycles,
		ushort targetAddress,
		byte x = 0,
		byte y = 0,
		byte? pointerLocation = null,
		ushort? pointerTarget = null)
	{
		return [new LoadReadCase(name, program, totalCycles, targetAddress, x, y, pointerLocation, pointerTarget)];
	}

	private static object[] Rmw(
		string name,
		byte[] program,
		int totalCycles,
		ushort targetAddress,
		byte originalValue,
		byte finalValue,
		bool carryIn = false,
		byte? pointerLocation = null,
		ushort? pointerTarget = null)
	{
		return [new ReadModifyWriteCase(name, program, totalCycles, targetAddress, originalValue, finalValue, carryIn, pointerLocation, pointerTarget)];
	}

	private static void LoadProgram(TestBus bus, IReadOnlyList<byte> program)
	{
		for (var i = 0; i < program.Count; i++)
		{
			bus.Memory[0x1000 + i] = program[i];
		}
	}

	private static Mos6510 CreateComparisonCpu(TestBus bus)
	{
		var cpu = new Mos6510(bus);
		cpu.InitializeState(0x1000);
		cpu.A = 0x55;
		cpu.X = 0x03;
		cpu.Y = 0x05;
		cpu.StackPointer = 0xF0;
		cpu.Status = 0x29;
		return cpu;
	}

	private static void WriteZeroPagePointer(TestBus bus, byte location, ushort target)
	{
		bus.Memory[location] = (byte)(target & 0xFF);
		bus.Memory[(byte)(location + 1)] = (byte)(target >> 8);
	}

	private static byte[] Absolute(byte opcode, ushort address)
	{
		return [opcode, (byte)(address & 0xFF), (byte)(address >> 8)];
	}

	private static byte[] AbsoluteX(byte opcode, ushort targetAddress, byte x)
	{
		return Absolute(opcode, unchecked((ushort)(targetAddress - x)));
	}

	private static byte[] AbsoluteY(byte opcode, ushort targetAddress, byte y)
	{
		return Absolute(opcode, unchecked((ushort)(targetAddress - y)));
	}

	private static byte ZeroPageOperandForX(byte target)
	{
		return unchecked((byte)(target - StoreX));
	}

	private static byte ZeroPageOperandForY(byte target)
	{
		return unchecked((byte)(target - StoreY));
	}

	private static byte PointerForIndirectX(byte operand, byte x)
	{
		return unchecked((byte)(operand + x));
	}

	private static byte StoreHighMasked(byte value, ushort targetAddress)
	{
		return (byte)(value & (((targetAddress >> 8) + 1) & 0xFF));
	}

	public sealed record StoreWriteCase(
		string Name,
		byte[] Program,
		int TotalCycles,
		ushort TargetAddress,
		byte ExpectedValue,
		byte? PointerLocation,
		ushort? PointerTarget)
	{
		public override string ToString() => Name;
	}

	public sealed record LoadReadCase(
		string Name,
		byte[] Program,
		int TotalCycles,
		ushort TargetAddress,
		byte X,
		byte Y,
		byte? PointerLocation,
		ushort? PointerTarget)
	{
		public override string ToString() => Name;
	}

	public sealed record ReadModifyWriteCase(
		string Name,
		byte[] Program,
		int TotalCycles,
		ushort TargetAddress,
		byte OriginalValue,
		byte FinalValue,
		bool CarryIn,
		byte? PointerLocation,
		ushort? PointerTarget)
	{
		public override string ToString() => Name;
	}
}
