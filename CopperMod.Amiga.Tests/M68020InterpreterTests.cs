using CopperMod.Amiga;
namespace CopperMod.Amiga.Tests;
public sealed class M68020InterpreterTests
{
	[Fact]
	public void DiagRomWaitShortFallbackLoopStaysBoundedOnAmigaBus()
	{
		var bus = new AmigaBus(captureBusAccesses: true);
		WriteAmigaBusWords(
			bus,
			CodeBase,
			0x1239, 0x00BF, 0xE001, // MOVE.B $BFE001,D1
			0x1239, 0x00DF, 0xF006, // MOVE.B $DFF006,D1
			0x51C8, 0xFFF2); // DBF D0,loop
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 3;
		for (var i = 0; i < 12; i++)
		{
			cpu.ExecuteInstruction();
		}
		Assert.Equal(CodeBase + 0x10u, cpu.State.ProgramCounter);
		Assert.Equal(0xFFFFu, cpu.State.D[0] & 0xFFFFu);
		Assert.InRange(cpu.State.Cycles, 1, 1_000);
	}
	[Fact]
	public void DiagRomWaitShortFallbackLoopStaysBoundedFromRom()
	{
		const uint romLoopBase = 0x00F8_9F42;
		var bus = new AmigaBus(captureBusAccesses: true);
		MapReadOnlyWords(
			bus,
			romLoopBase,
			0x1239, 0x00BF, 0xE001, // MOVE.B $BFE001,D1
			0x1239, 0x00DF, 0xF006, // MOVE.B $DFF006,D1
			0x51C8, 0xFFF2); // DBF D0,loop
		var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
		cpu.Reset(romLoopBase, 0x3000);
		cpu.State.D[0] = 3;
		for (var i = 0; i < 12; i++)
		{
			cpu.ExecuteInstruction();
		}
		Assert.Equal(romLoopBase + 0x10u, cpu.State.ProgramCounter);
		Assert.Equal(0xFFFFu, cpu.State.D[0] & 0xFFFFu);
		Assert.InRange(cpu.State.Cycles, 1, 1_000);
	}
	private const uint CodeBase = 0x1000;
	private static void WriteAmigaBusWords(AmigaBus bus, uint address, params ushort[] words)
	{
		for (var i = 0; i < words.Length; i++)
		{
			bus.WriteWord(address + (uint)(i * 2), words[i]);
		}
	}
	private static void MapReadOnlyWords(AmigaBus bus, uint address, params ushort[] words)
	{
		var bytes = new byte[words.Length * 2];
		for (var i = 0; i < words.Length; i++)
		{
			bytes[i * 2] = (byte)(words[i] >> 8);
			bytes[(i * 2) + 1] = (byte)words[i];
		}
		bus.MapReadOnlyMemory(address, bytes);
	}
}
