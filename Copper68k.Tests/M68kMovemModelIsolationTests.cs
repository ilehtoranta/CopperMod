using Copper68k;
using Xunit;

namespace Copper68k.Tests;

public sealed class M68kMovemModelIsolationTests
{
	[Theory]
	[InlineData(M68kCpuModel.M68000, true)]
	[InlineData(M68kCpuModel.M68010, true)]
	[InlineData(M68kCpuModel.M68040, false)]
	public void MovemWordIgnoredReadIsExcludedFromM68040ApproximateFallback(
		M68kCpuModel model,
		bool expectsIgnoredRead)
	{
		const uint codeAddress = 0x1000;
		const uint sourceAddress = 0x2000;
		const uint stackAddress = 0x6000;
		const ushort opcode = 0x4C90; // MOVEM.W (A0),D0
		const ushort status = M68kCpuState.ResetStatusRegister |
			M68kCpuState.Extend | M68kCpuState.Zero |
			M68kCpuState.Overflow | M68kCpuState.Carry;
		var bus = new WordReadRecordingBus();
		bus.StoreWord(codeAddress, opcode);
		bus.StoreWord(codeAddress + 2, 0x0001);
		bus.StoreWord(codeAddress + 4, 0x4E71);
		bus.StoreWord(codeAddress + 6, 0x4E71);
		bus.StoreWord(sourceAddress, 0x8001);
		bus.StoreWord(sourceAddress + 2, 0x1234);

		// Unlike the native MOVEM.L (An)+ path, this word-indirect form has
		// no advanced/fast dispatch kind. Successful 040 execution therefore
		// exercises its shared integer fallback, where the 000/010 tail must
		// remain disabled. Fail explicitly if this ceases to test that route.
		Assert.Equal(M68020OpcodeKind.Unsupported,
			M68020OpcodeDispatchTable.M68040Kinds[opcode]);
		using IM68kCore cpu = model == M68kCpuModel.M68040
			? new M68040Interpreter(bus, M68020CpuProfile.Ocs68040JitMaxSpeed)
			: M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(codeAddress, stackAddress);
		cpu.State.StatusRegister = status;
		for (var register = 0; register < 8; register++)
		{
			cpu.State.D[register] = 0xA500_0000u + (uint)register;
			if (register < 7)
			{
				cpu.State.A[register] = 0x3000u + (uint)(register * 0x100);
			}
		}
		cpu.State.A[0] = sourceAddress;
		var expectedDataRegisters = cpu.State.D.ToArray();
		expectedDataRegisters[0] = 0xFFFF_8001;
		var expectedAddressRegisters = cpu.State.A.ToArray();
		bus.Reads.Clear();

		cpu.ExecuteInstruction();

		var dataReads = bus.Reads
			.Where(read => read.Kind == M68kBusAccessKind.CpuDataRead)
			.Select(read => read.Address)
			.ToArray();
		var expectedReads = expectsIgnoredRead
			? new[] { sourceAddress, sourceAddress + 2 }
			: new[] { sourceAddress };
		var diagnostic = $"model={model}, PC={cpu.State.ProgramCounter:X8}, " +
			$"SR={cpu.State.StatusRegister:X4}, SP={cpu.State.A[7]:X8}, " +
			$"wordReads=[{string.Join(",", bus.Reads.Select(read =>
				$"{read.Kind}@{read.Address:X8}"))}]";
		Assert.True(expectedReads.SequenceEqual(dataReads), diagnostic);
		Assert.Equal(expectsIgnoredRead, bus.Reads.Any(read => read.Address == sourceAddress + 2));
		Assert.Equal(expectedDataRegisters, cpu.State.D);
		Assert.Equal(expectedAddressRegisters, cpu.State.A);
		Assert.Equal(codeAddress + 4, cpu.State.ProgramCounter);
		Assert.Equal(opcode, cpu.State.LastOpcode);
		Assert.Equal(status, cpu.State.StatusRegister);
		Assert.False(cpu.State.Halted);
		Assert.False(cpu.State.Stopped);

		if (model == M68kCpuModel.M68040)
		{
			// This is the selected approximate profile's policy, not an
			// assertion about physical MC68040 instruction or bus timing.
			var timing = Assert.IsType<M68040Interpreter>(cpu).Timing.LastInstructionTiming;
			Assert.Equal(1, timing.Plan.NativeCycles);
			Assert.Equal(1, timing.ElapsedNativeCycles);
		}
	}

	private sealed class WordReadRecordingBus : IM68kBus, IM68kCodeReader
	{
		private readonly Dictionary<uint, ushort> _words = new();
		public List<(uint Address, M68kBusAccessKind Kind)> Reads { get; } = new();

		public void StoreWord(uint address, ushort value) => _words[address] = value;

		public ushort ReadHostWord(uint address) => _words.GetValueOrDefault(address);

		public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			Reads.Add((address, accessKind));
			return ReadHostWord(address);
		}

		// The fixture executes only a word load. Reject another transfer size
		// or any write instead of hiding it behind a word-only counter.
		public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
			=> throw new InvalidOperationException($"Unexpected byte read at {address:X8}.");

		public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
			=> throw new InvalidOperationException($"Unexpected long read at {address:X8}.");

		public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
			=> throw new InvalidOperationException($"Unexpected byte write at {address:X8}.");

		public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
			=> throw new InvalidOperationException($"Unexpected word write at {address:X8}.");

		public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
			=> throw new InvalidOperationException($"Unexpected long write at {address:X8}.");

		public void ResetExternalDevices(long cycle) { }
	}
}
