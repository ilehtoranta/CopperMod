using Copper68k;

namespace Copper68k.Tests;

public sealed class M68EC020InterpreterTests
{
	private const uint CodeBase = 0x0000_1000;
	private const uint AliasedCodeBase = 0x0100_1000;
	private const uint StackBase = 0x0000_3000;

	[Fact]
	public void FactoryCreatesAccurateM68EC020BackendWithM68020TimingProfile()
	{
		using var cpu = M68kCoreFactory.Default.Create(
			M68kBackendKind.AccurateM68EC020,
			new Copper68kTestBus());
		var interpreter = Assert.IsType<M68EC020Interpreter>(cpu);

		Assert.Same(M68020CpuProfile.OcsAccelerator14Mhz, interpreter.Profile);
		Assert.True(interpreter.State.M68020StackModeEnabled);
	}

	public static TheoryData<string, ushort[], uint> SharedInstructionCases => new()
	{
		{ "NOP", new ushort[] { 0x4E71 }, 0 },
		{ "EXTB.L D0", new ushort[] { 0x49C0 }, 0x0000_0080 },
		{ "MULU.W D0,D0", new ushort[] { 0xC0C0 }, 0xFFFF_0007 },
		{ "MOVEC D0,VBR", new ushort[] { 0x4E7B, 0x0801 }, 0x0000_0400 },
		{ "BRA.L", new ushort[] { 0x60FF, 0x0000, 0x0008, 0x4E71, 0x4E71, 0x4E71 }, 0 },
		{ "BSET #1,D0", new ushort[] { 0x08C0, 0x0001 }, 0x1234_0000 }
	};

	[Theory]
	[MemberData(nameof(SharedInstructionCases))]
	public void M68EC020MatchesM68020ArchitecturalStateAndTiming(
		string caseName,
		ushort[] words,
		uint initialD0)
	{
		_ = caseName;
		using var m68020 = CreateAndExecute(M68kCpuModel.M68020, CodeBase, words, initialD0);
		using var ec020 = CreateAndExecute(M68kCpuModel.M68EC020, CodeBase, words, initialD0);

		Assert.Equal(m68020.State.D, ec020.State.D);
		Assert.Equal(m68020.State.A, ec020.State.A);
		Assert.Equal(m68020.State.ProgramCounter, ec020.State.ProgramCounter);
		Assert.Equal(m68020.State.StatusRegister, ec020.State.StatusRegister);
		Assert.Equal(m68020.State.VectorBaseRegister, ec020.State.VectorBaseRegister);
		Assert.Equal(m68020.State.Cycles, ec020.State.Cycles);
		Assert.Equal(m68020.State.NativeCycles, ec020.State.NativeCycles);
	}

	[Fact]
	public void AliasedInstructionFetchMatchesLowAddressResultAndTiming()
	{
		var words = new ushort[] { 0x49C0 }; // EXTB.L D0
		using var low = CreateAndExecute(M68kCpuModel.M68EC020, CodeBase, words, 0x80);
		using var aliased = CreateAndExecute(M68kCpuModel.M68EC020, AliasedCodeBase, words, 0x80);

		Assert.Equal(low.State.D, aliased.State.D);
		Assert.Equal(low.State.StatusRegister, aliased.State.StatusRegister);
		Assert.Equal(low.State.ProgramCounter + 0x0100_0000u, aliased.State.ProgramCounter);
		Assert.Equal(low.State.Cycles, aliased.State.Cycles);
		Assert.Equal(low.State.NativeCycles, aliased.State.NativeCycles);
	}

	[Fact]
	public void AliasedDataAddressPreservesFullRegisterAndMatchesLowAddressTiming()
	{
		const uint lowAddress = 0x0000_2000;
		var lowWords = new ushort[] { 0x1039, 0x0000, 0x2000 }; // MOVE.B $00002000.L,D0
		var aliasedWords = new ushort[] { 0x1039, 0x0100, 0x2000 }; // MOVE.B $01002000.L,D0

		using var low = CreateAndExecute(M68kCpuModel.M68EC020, CodeBase, lowWords, 0xA5A5_A500, lowAddress, 0x5A);
		using var aliased = CreateAndExecute(M68kCpuModel.M68EC020, CodeBase, aliasedWords, 0xA5A5_A500, lowAddress, 0x5A);

		Assert.Equal(0xA5A5_A55Au, aliased.State.D[0]);
		Assert.Equal(low.State.D, aliased.State.D);
		Assert.Equal(low.State.StatusRegister, aliased.State.StatusRegister);
		Assert.Equal(low.State.Cycles, aliased.State.Cycles);
		Assert.Equal(low.State.NativeCycles, aliased.State.NativeCycles);
	}

	[Fact]
	public void AddressMaskedBusMasksBaseBusOperationsToTwentyFourBits()
	{
		const uint aliasedAddress = 0xAB12_3456;
		const uint physicalAddress = 0x0012_3456;
		var inner = new RecordingOptionalBus();
		var bus = new M68EC020AddressMaskedBus(inner);
		long cycle = 0;

		Assert.Equal(0x5A, bus.ReadByte(aliasedAddress, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.Equal(physicalAddress, inner.LastAddress);
		Assert.Equal(0x1234, bus.ReadWord(aliasedAddress, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.Equal(physicalAddress, inner.LastAddress);
		Assert.Equal(0x89AB_CDEFu, bus.ReadLong(aliasedAddress, ref cycle, M68kBusAccessKind.CpuDataRead));
		Assert.Equal(physicalAddress, inner.LastAddress);

		bus.WriteByte(aliasedAddress, 0xA5, ref cycle, M68kBusAccessKind.CpuDataWrite);
		Assert.Equal(physicalAddress, inner.LastAddress);
		Assert.Equal(0xA5u, inner.LastValue);
		bus.WriteWord(aliasedAddress, 0x5678, ref cycle, M68kBusAccessKind.CpuDataWrite);
		Assert.Equal(physicalAddress, inner.LastAddress);
		Assert.Equal(0x5678u, inner.LastValue);
		bus.WriteLong(aliasedAddress, 0x0123_4567, ref cycle, M68kBusAccessKind.CpuDataWrite);
		Assert.Equal(physicalAddress, inner.LastAddress);
		Assert.Equal(0x0123_4567u, inner.LastValue);
	}

	[Fact]
	public void AddressMaskedBusMasksCodeReaderAndHostGatewayOperations()
	{
		const uint aliasedAddress = 0xFF12_3456;
		const uint physicalAddress = 0x0012_3456;
		var inner = new RecordingOptionalBus();
		var bus = new M68EC020AddressMaskedBus(inner);
		var state = new M68kCpuState();

		Assert.Equal(0xBEEF, bus.ReadHostWord(aliasedAddress));
		Assert.Equal(physicalAddress, inner.LastAddress);
		Assert.True(bus.HasHostGateway(aliasedAddress));
		Assert.Equal(physicalAddress, inner.LastAddress);
		Assert.True(bus.TryInvokeHostGateway(aliasedAddress, 7, state));
		Assert.Equal(physicalAddress, inner.LastAddress);
		Assert.Equal(7u, inner.LastValue);
		Assert.Equal(
			new M68kHostGatewayInvocation(true, M68kHostGatewayResult.Reschedule),
			bus.InvokeHostGateway(aliasedAddress, 9, state));
		Assert.Equal(physicalAddress, inner.LastAddress);
		Assert.Equal(9u, inner.LastValue);
	}

	[Fact]
	public void AddressMaskedBusMasksFastMemoryAndPhysicalMapOperations()
	{
		const uint aliasedAddress = 0xCD12_3456;
		const uint physicalAddress = 0x0012_3456;
		var inner = new RecordingOptionalBus();
		var bus = new M68EC020AddressMaskedBus(inner);

		Assert.True(bus.TryReadFastByte(aliasedAddress, M68kBusAccessKind.CpuDataRead, out var byteValue));
		Assert.Equal(0x5A, byteValue);
		Assert.Equal(physicalAddress, inner.LastAddress);
		Assert.True(bus.TryReadFastWord(aliasedAddress, M68kBusAccessKind.CpuDataRead, out var wordValue));
		Assert.Equal(0x1234, wordValue);
		Assert.Equal(physicalAddress, inner.LastAddress);
		Assert.True(bus.TryReadFastLong(aliasedAddress, M68kBusAccessKind.CpuDataRead, out var longValue));
		Assert.Equal(0x89AB_CDEFu, longValue);
		Assert.Equal(physicalAddress, inner.LastAddress);

		Assert.True(bus.TryWriteFastByte(aliasedAddress, 0xA5, M68kBusAccessKind.CpuDataWrite));
		Assert.Equal(physicalAddress, inner.LastAddress);
		Assert.Equal(0xA5u, inner.LastValue);
		Assert.True(bus.TryWriteFastWord(aliasedAddress, 0x5678, M68kBusAccessKind.CpuDataWrite));
		Assert.Equal(physicalAddress, inner.LastAddress);
		Assert.Equal(0x5678u, inner.LastValue);
		Assert.True(bus.TryWriteFastLong(aliasedAddress, 0x0123_4567, M68kBusAccessKind.CpuDataWrite));
		Assert.Equal(physicalAddress, inner.LastAddress);
		Assert.Equal(0x0123_4567u, inner.LastValue);

		Assert.True(bus.IsCpuPhysicalAddressMapped(aliasedAddress, 4, M68kBusAccessKind.CpuDataRead));
		Assert.Equal(physicalAddress, inner.LastAddress);
		Assert.Equal(4u, inner.LastValue);
	}

	[Fact]
	public void AddressMaskedBusUsesDocumentedFallbacksWhenOptionalInterfacesAreMissing()
	{
		var bus = new M68EC020AddressMaskedBus(new BusWithoutOptionalInterfaces());

		Assert.Throws<InvalidOperationException>(() => bus.ReadHostWord(AliasedCodeBase));
		Assert.False(bus.TryReadFastByte(AliasedCodeBase, M68kBusAccessKind.CpuDataRead, out var byteValue));
		Assert.Equal(0, byteValue);
		Assert.False(bus.TryReadFastWord(AliasedCodeBase, M68kBusAccessKind.CpuDataRead, out var wordValue));
		Assert.Equal(0, wordValue);
		Assert.False(bus.TryReadFastLong(AliasedCodeBase, M68kBusAccessKind.CpuDataRead, out var longValue));
		Assert.Equal(0u, longValue);
		Assert.False(bus.TryWriteFastByte(AliasedCodeBase, 1, M68kBusAccessKind.CpuDataWrite));
		Assert.False(bus.TryWriteFastWord(AliasedCodeBase, 1, M68kBusAccessKind.CpuDataWrite));
		Assert.False(bus.TryWriteFastLong(AliasedCodeBase, 1, M68kBusAccessKind.CpuDataWrite));
		Assert.False(bus.IsCpuPhysicalAddressMapped(AliasedCodeBase, 1, M68kBusAccessKind.CpuDataRead));
	}

	private static IM68kCore CreateAndExecute(
		M68kCpuModel model,
		uint programCounter,
		ushort[] words,
		uint initialD0,
		uint? dataAddress = null,
		byte dataValue = 0)
	{
		var bus = new Copper68kTestBus();
		var physicalCodeAddress = programCounter & 0x00FF_FFFFu;
		bus.WriteWords(physicalCodeAddress, words);
		if (dataAddress.HasValue)
		{
			bus.Memory[dataAddress.Value & 0x00FF_FFFFu] = dataValue;
		}

		var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(programCounter, StackBase);
		cpu.State.D[0] = initialD0;
		cpu.ExecuteInstruction();
		return cpu;
	}

	private sealed class RecordingOptionalBus :
		IM68kBus,
		IM68kCodeReader,
		IM68kFastMemoryBus,
		IM68kPhysicalAddressMap
	{
		public uint LastAddress { get; private set; }

		public uint LastValue { get; private set; }

		public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			Record(address);
			return 0x5A;
		}

		public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			Record(address);
			return 0x1234;
		}

		public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			Record(address);
			return 0x89AB_CDEF;
		}

		public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
			=> Record(address, value);

		public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
			=> Record(address, value);

		public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
			=> Record(address, value);

		public bool HasHostGateway(uint address)
		{
			Record(address);
			return true;
		}

		public bool TryInvokeHostGateway(uint instructionProgramCounter, uint token, M68kCpuState state)
		{
			Record(instructionProgramCounter, token);
			return true;
		}

		public M68kHostGatewayInvocation InvokeHostGateway(uint instructionProgramCounter, uint token, M68kCpuState state)
		{
			Record(instructionProgramCounter, token);
			return new(true, M68kHostGatewayResult.Reschedule);
		}

		public void ResetExternalDevices(long cycle)
		{
		}

		public ushort ReadHostWord(uint address)
		{
			Record(address);
			return 0xBEEF;
		}

		public bool TryReadFastByte(uint address, M68kBusAccessKind accessKind, out byte value)
		{
			Record(address);
			value = 0x5A;
			return true;
		}

		public bool TryReadFastWord(uint address, M68kBusAccessKind accessKind, out ushort value)
		{
			Record(address);
			value = 0x1234;
			return true;
		}

		public bool TryReadFastLong(uint address, M68kBusAccessKind accessKind, out uint value)
		{
			Record(address);
			value = 0x89AB_CDEF;
			return true;
		}

		public bool TryWriteFastByte(uint address, byte value, M68kBusAccessKind accessKind)
		{
			Record(address, value);
			return true;
		}

		public bool TryWriteFastWord(uint address, ushort value, M68kBusAccessKind accessKind)
		{
			Record(address, value);
			return true;
		}

		public bool TryWriteFastLong(uint address, uint value, M68kBusAccessKind accessKind)
		{
			Record(address, value);
			return true;
		}

		public bool IsCpuPhysicalAddressMapped(uint address, int byteCount, M68kBusAccessKind accessKind)
		{
			Record(address, (uint)byteCount);
			return true;
		}

		private void Record(uint address, uint value = 0)
		{
			LastAddress = address;
			LastValue = value;
		}
	}

	private sealed class BusWithoutOptionalInterfaces : IM68kBus
	{
		public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind) => 0;

		public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind) => 0;

		public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind) => 0;

		public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
		{
		}

		public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
		{
		}

		public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
		{
		}

		public void ResetExternalDevices(long cycle)
		{
		}
	}
}
