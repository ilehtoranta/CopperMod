using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class M68020InterpreterTests
{
	private const uint CodeBase = 0x1000;

	[Fact]
	public void FactoryCreatesAccurateM68020Backend()
	{
		using var cpu = M68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68020, new AmigaBus());

		var interpreter = Assert.IsType<M68020Interpreter>(cpu);
		Assert.Same(M68020CpuProfile.OcsAccelerator14Mhz, interpreter.Profile);
		Assert.True(interpreter.State.M68020StackModeEnabled);
	}

	[Fact]
	public void FactoryCreatesAccurateM68030Backend()
	{
		using var cpu = M68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68030, new AmigaBus());

		var interpreter = Assert.IsType<M68030Interpreter>(cpu);
		Assert.Same(M68020CpuProfile.Ocs68030Accelerator14Mhz, interpreter.Profile);
		Assert.True(interpreter.State.M68020StackModeEnabled);
	}

	[Fact]
	public void MovecTransfersVectorBaseRegister()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(
			bus,
			CodeBase,
			0x4E7B, 0x0801, // MOVEC D0,VBR
			0x4E7A, 0x1801); // MOVEC VBR,D1
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0400;

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_0400u, cpu.State.VectorBaseRegister);
		Assert.Equal(0x0000_0400u, cpu.State.D[1]);
		Assert.Equal(24, cpu.State.NativeCycles);
		Assert.Equal(12, cpu.State.Cycles);
	}

	[Fact]
	public void LongBraUsesThirtyTwoBitDisplacement()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x60FF, 0x0000, 0x0004);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(CodeBase + 0x0Au, cpu.State.ProgramCounter);
		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void LineAExceptionUsesVectorBaseAndFormatZeroFrame()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xA000);
		bus.WriteLong(0x0400 + (10u * 4), 0x0000_2000);
		WriteWords(bus, 0x2000, 0x4E73); // RTE
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.VectorBaseRegister = 0x0400;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF8u, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.Supervisor, (ushort)(bus.ReadWord(0x2FF8) & M68kCpuState.Supervisor));
		Assert.Equal(CodeBase, bus.ReadLong(0x2FFA));
		Assert.Equal(10 * 4, bus.ReadWord(0x2FFE));

		cpu.ExecuteInstruction();

		Assert.Equal(CodeBase, cpu.State.ProgramCounter);
		Assert.Equal(0x3000u, cpu.State.A[7]);
	}

	[Fact]
	public void InterruptUsesVectorBaseRegister()
	{
		var bus = new ZeroWaitCodeBus();
		bus.WriteLong(0x0400 + (27u * 4), 0x0000_2400);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.VectorBaseRegister = 0x0400;

		cpu.RequestInterrupt(3, 27u * 4);

		Assert.Equal(0x2400u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF8u, cpu.State.A[7]);
		Assert.Equal(CodeBase, bus.ReadLong(0x2FFA));
		Assert.Equal(27 * 4, bus.ReadWord(0x2FFE));
		Assert.Equal(3, (cpu.State.StatusRegister >> 8) & 7);
	}

	[Fact]
	public void LinkLongUsesThirtyTwoBitFrameDisplacement()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x480E, 0xFFFF, 0xFFF0); // LINK.L A6,#-16
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x1234_5678;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2FFCu, cpu.State.A[6]);
		Assert.Equal(0x2FECu, cpu.State.A[7]);
		Assert.Equal(0x1234_5678u, bus.ReadLong(0x2FFC));
	}

	[Fact]
	public void ExtbSignExtendsByteToLong()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x49C0); // EXTB.L D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0080;

		cpu.ExecuteInstruction();

		Assert.Equal(0xFFFF_FF80u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
	}

	[Fact]
	public void RtdPullsProgramCounterAndDropsParameterBytes()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4E74, 0x0008); // RTD #8
		bus.WriteLong(0x3000, 0x0000_2000);
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x300Cu, cpu.State.A[7]);
	}

	[Fact]
	public void M68030HeadTailOverlapReducesAdjacentInstructionCycles()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x49C0, 0x49C1); // EXTB.L D0; EXTB.L D1
		var cpu = new M68030Interpreter(bus, M68020CpuProfile.Ocs68030Accelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(4, cpu.State.NativeCycles);
		Assert.Equal(0, cpu.Timing.LastInstructionTiming.OverlapCycles);

		cpu.ExecuteInstruction();

		Assert.Equal(6, cpu.State.NativeCycles);
		Assert.Equal(3, cpu.State.Cycles);
		Assert.Equal(2, cpu.Timing.LastInstructionTiming.OverlapCycles);
	}

	[Fact]
	public void M68030TakenBranchSuppressesNextHeadTailOverlap()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(
			bus,
			CodeBase,
			0x49C0, // EXTB.L D0
			0x60FF, 0x0000, 0x0000, // BRA.L to next instruction
			0x49C1); // EXTB.L D1
		var cpu = new M68030Interpreter(bus, M68020CpuProfile.Ocs68030Accelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(18, cpu.State.NativeCycles);
		Assert.Equal(0, cpu.Timing.LastInstructionTiming.OverlapCycles);
	}

	[Fact]
	public void NopSynchronizesPendingPostedWrite()
	{
		var bus = new ZeroWaitCodeBus { WriteMachineDelay = 20 };
		WriteWords(bus, CodeBase, 0x480E, 0x0000, 0x0000, 0x4E71); // LINK.L A6,#0; NOP
		var cpu = new M68030Interpreter(bus, M68020CpuProfile.Ocs68030Accelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[6] = 0x1234_5678;

		cpu.ExecuteInstruction();

		Assert.Equal(16, cpu.State.NativeCycles);
		Assert.Equal(40, cpu.Timing.BusControllerAvailableNativeCycle);

		cpu.ExecuteInstruction();

		Assert.Equal(44, cpu.State.NativeCycles);
		Assert.Equal(22, cpu.State.Cycles);
	}

	[Fact]
	public void EnabledInstructionCacheAvoidsSecondFetchWithinLine()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(
			bus,
			CodeBase,
			0x4E7B, 0x0002, // MOVEC D0,CACR
			0x4E71, // NOP
			0x4E71); // NOP, same 4-byte cache line as previous NOP
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0001;

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(3, bus.InstructionFetchWords);
		Assert.True(cpu.Timing.InstructionCache.Enabled);
	}

	[Fact]
	public void ProfileWaitStatesDelayBlockingInstructionFetch()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4E71); // NOP
		var profile = M68020CpuProfile.CreateForTesting(
			"test-wait",
			M68kAcceleratorModel.M68020,
			2,
			new M68020BusTimingRule(M68020MemoryTarget.ChipRam, M68020BusWidth.Word, 3));
		var cpu = new M68020Interpreter(bus, profile);
		cpu.Reset(CodeBase, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(10, cpu.State.NativeCycles);
		Assert.Equal(5, cpu.State.Cycles);
	}

	[Fact]
	public void UnsupportedExactOpcodeFailsInsteadOfFallingBackToM68000Timing()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x303C, 0x1234); // MOVE.W #$1234,D0
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);

		var exception = Assert.Throws<UnsupportedM68kTimingException>(() => cpu.ExecuteInstruction());

		Assert.Equal(0x303C, exception.Opcode);
		Assert.Equal(CodeBase, exception.ProgramCounter);
	}

	private static void WriteWords(ZeroWaitCodeBus bus, uint address, params ushort[] words)
	{
		for (var i = 0; i < words.Length; i++)
		{
			bus.WriteWord(address + (uint)(i * 2), words[i]);
		}
	}

	private sealed class ZeroWaitCodeBus : IM68kBus, IM68kCodeReader
	{
		private readonly byte[] _memory = new byte[0x0100_0000];

		public int InstructionFetchWords { get; private set; }

		public int WriteMachineDelay { get; init; }

		public byte ReadByte(uint address, ref long cycle, AmigaBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			return _memory[Normalize(address)];
		}

		public ushort ReadWord(uint address, ref long cycle, AmigaBusAccessKind accessKind)
		{
			if (accessKind == AmigaBusAccessKind.CpuInstructionFetch)
			{
				InstructionFetchWords++;
			}

			return ReadWord(address);
		}

		public uint ReadLong(uint address, ref long cycle, AmigaBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			return ReadLong(address);
		}

		public void WriteByte(uint address, byte value, ref long cycle, AmigaBusAccessKind accessKind)
		{
			_ = accessKind;
			_memory[Normalize(address)] = value;
			cycle += WriteMachineDelay;
		}

		public void WriteWord(uint address, ushort value, ref long cycle, AmigaBusAccessKind accessKind)
		{
			_ = accessKind;
			WriteWord(address, value);
			cycle += WriteMachineDelay;
		}

		public void WriteLong(uint address, uint value, ref long cycle, AmigaBusAccessKind accessKind)
		{
			_ = accessKind;
			WriteLong(address, value);
			cycle += WriteMachineDelay;
		}

		public bool HasHostTrapStub(uint address)
		{
			_ = address;
			return false;
		}

		public bool TryInvokeHostTrap(uint instructionProgramCounter, ushort trapId, M68kCpuState state)
		{
			_ = instructionProgramCounter;
			_ = trapId;
			_ = state;
			return false;
		}

		public void ResetExternalDevices(long cycle)
		{
			_ = cycle;
		}

		public ushort ReadHostWord(uint address)
			=> ReadWord(address);

		public ushort ReadWord(uint address)
		{
			var offset = Normalize(address);
			return (ushort)((_memory[offset] << 8) | _memory[Normalize(address + 1)]);
		}

		public uint ReadLong(uint address)
			=> ((uint)ReadWord(address) << 16) | ReadWord(address + 2);

		public void WriteWord(uint address, ushort value)
		{
			var offset = Normalize(address);
			_memory[offset] = (byte)(value >> 8);
			_memory[Normalize(address + 1)] = (byte)value;
		}

		public void WriteLong(uint address, uint value)
		{
			WriteWord(address, (ushort)(value >> 16));
			WriteWord(address + 2, (ushort)value);
		}

		private static int Normalize(uint address)
			=> (int)(address & 0x00FF_FFFF);
	}
}
