using CopperMod.Amiga;
namespace CopperMod.Amiga.Tests;
public sealed class M68kInstructionFrequencyTests
{
	[Fact]
	public void JitFrequencyIncludesCompiledTraceInstructions()
	{
		var bus = new AmigaBus(expansionRamSize: FastCodeSize);
		WriteWords(
			bus,
			FastCodeBase,
			0x7001, // MOVEQ #1,D0
			0x5280, // ADDQ.L #1,D0
			0x60FA); // BRA.S loop
		var cpu = new M68kJitCore(bus);
		cpu.Reset(FastCodeBase, 0x4000);
		cpu.InstructionFrequencyEnabled = true;
		var executed = cpu.ExecuteInstructions(240, null, new CountingBoundary());
		var snapshot = cpu.CaptureInstructionFrequency();
		Assert.Equal((long)executed, snapshot.TotalInstructions);
		Assert.True(cpu.Counters.CompiledTraces > 0);
		Assert.Contains(snapshot.Families, family => family.Family == M68kInstructionFamily.Move);
		Assert.Contains(snapshot.Families, family => family.Family == M68kInstructionFamily.QuickArithmetic);
		Assert.Contains(snapshot.Families, family => family.Family == M68kInstructionFamily.ControlFlow);
	}
	private const uint FastCodeBase = AmigaConstants.A500BootPseudoFastRamBase;
	private const int FastCodeSize = 64 * 1024;
	private static void WriteWords(AmigaBus bus, uint address, params ushort[] words)
	{
		for (var i = 0; i < words.Length; i++)
		{
			bus.WriteWord(address + (uint)(i * 2), words[i]);
		}
	}
	private sealed class CountingBoundary : IM68kInstructionBoundary
	{
		public bool BeforeInstruction()
			=> true;
		public void AfterInstruction(long previousCycle, long currentCycle)
		{
			_ = previousCycle;
			_ = currentCycle;
		}
	}
}
