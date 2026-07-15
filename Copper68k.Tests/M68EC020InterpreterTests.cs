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
		Assert.False(string.IsNullOrWhiteSpace(caseName));
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
}
