using CopperScreen;

namespace CopperScreen.Tests;

public sealed class CopperScreenDebugSnapshotTests
{
	[Fact]
	public void MiniDisassemblerFormatsCommonCrashContextInstructions()
	{
		var words = new Dictionary<uint, ushort>
		{
			[0x1000] = 0x70FF,
			[0x1002] = 0x4E75,
			[0x1004] = 0x6000,
			[0x1006] = 0x0006,
			[0x1008] = 0x4E7A,
			[0x100A] = 0x0801
		};

		var lines = M68kMiniDisassembler.Disassemble(0x1000, 4, TryReadWord);

		Assert.Contains("MOVEQ #-1,D0", lines[0]);
		Assert.Contains("RTS", lines[1]);
		Assert.Contains("BRA $00100C", lines[2]);
		Assert.Contains("MOVEC (68010+, illegal on 68000)", lines[3]);

		bool TryReadWord(uint address, out ushort value)
			=> words.TryGetValue(address, out value);
	}

	[Fact]
	public void DebugSnapshotReportIncludesRegistersDisassemblyAndDrives()
	{
		var data = Enumerable.Range(0, 8).Select(index => (uint)(0x1000 + index)).ToArray();
		var address = Enumerable.Range(0, 8).Select(index => (uint)(0x2000 + index)).ToArray();
		var cpu = new CopperScreenDebugCpuSnapshot(
			0x123456,
			0x123450,
			0x4E7A,
			0x2015,
			0x00FF00,
			0x00FE00,
			1234,
			Halted: true,
			Stopped: false,
			data,
			address);
		var snapshot = new CopperScreenDebugSnapshot(
			DateTimeOffset.UnixEpoch,
			"AMIGA_BOOT_UNSUPPORTED_OPCODE",
			"Unsupported MC68000 opcode.",
			"Expanded A500",
			"AccurateM68000",
			"Test.adf",
			@"C:\Test\Test.adf",
			42,
			cpu,
			[
				new CopperScreenDriveState(0, true, true, "Test.adf", @"C:\Test\Test.adf", 12, 1, true, true, true, true)
			],
			["AMIGA_BOOT_UNSUPPORTED_OPCODE: Unsupported MC68000 opcode."],
			["123456: 4E7A 0801       MOVEC (68010+, illegal on 68000)"],
			["00FE00: 1234"]);

		var report = snapshot.ToReport();

		Assert.Contains("AMIGA_BOOT_UNSUPPORTED_OPCODE", report);
		Assert.Contains("PC=$123456", report);
		Assert.Contains("D0=$00001000", report);
		Assert.Contains("DF0: cyl 12.1 DSP Test.adf", report);
		Assert.Contains("MOVEC", report);
		Assert.Contains("00FE00: 1234", report);
	}

	[Fact]
	public void EmulatorFatalExceptionFreezesDebuggerUntilReset()
	{
		using var emulator = CopperScreenEmulator.CreateWithoutDisk();

		emulator.CaptureFatalException(new InvalidOperationException("synthetic fault"));

		Assert.True(emulator.IsPaused);
		Assert.NotNull(emulator.DebugSnapshot);
		Assert.Equal("COPPERSCREEN_RUNTIME_FAULT", emulator.DebugSnapshot!.ReasonCode);
		Assert.Contains("synthetic fault", emulator.StatusText);

		Assert.True(emulator.TogglePaused());
		Assert.True(emulator.IsPaused);

		emulator.Reset();

		Assert.False(emulator.IsPaused);
		Assert.Null(emulator.DebugSnapshot);
	}
}
