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
	public void RsidRasterIrqCanReturnThroughKernalEpilogue()
	{
		var module = SidParser.Parse(SidFixtureBuilder.CreateRsid(RasterIrqProgram()));
		var machine = new C64Machine(module);
		machine.Reset(0);

		machine.RunCycles(SidConstants.PalCyclesPerFrame * 3);

		Assert.False(machine.Cpu.Halted);
		Assert.InRange(machine.Cpu.ProgramCounter, 0xFF94, 0xFF98);
		Assert.Contains(machine.SidWrites, write => write.Register == 0x18 && write.Value == 0x0F);
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
