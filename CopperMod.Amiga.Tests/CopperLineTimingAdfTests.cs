using CopperDisk;
using Xunit.Abstractions;

namespace CopperMod.Amiga.Tests;

public sealed class CopperLineTimingAdfTests
{
	private const int ResultRowCount = 28;
	private const int ResultsAddress = 0x0004_8000;
	private readonly ITestOutputHelper _output;

	public CopperLineTimingAdfTests(ITestOutputHelper output)
	{
		_output = output;
	}

	[Fact]
	public void CopperLineTimingTestAdfProducesResultRowsWhenAvailable()
	{
		var path = TryFindWorkspaceFile("CopperScreen", "TestImages", "timing-test.adf");
		if (path == null)
		{
			return;
		}

		var machine = new AmigaMachine(
			AmigaMachineOptions
				.ForProfile(AmigaMachineProfile.A500Pal512KBoot)
				.WithFloppyDriveCount(1)
				.WithLiveAgnusDma(true));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(AmigaDiskImage.Load(path));

		var rows = new uint[ResultRowCount];
		var result = default(AmigaBootResult);
		const int maxFrames = 120;
		for (var frame = 1; frame <= maxFrames; frame++)
		{
			result = boot.ContinueExecutionUntilCycle(
				frame * (long)AmigaConstants.A500PalCpuCyclesPerFrame,
				maxInstructions: 2_000_000);
			ReadResultRows(machine.Bus.ChipRam, rows);
			if (RowsComplete(rows))
			{
				WriteResultReport(path, rows, frame, machine.Cpu.State.Cycles);
				AssertExpectedSanity(rows);
				return;
			}
		}

		WriteResultReport(path, rows, maxFrames, machine.Cpu.State.Cycles);
		Assert.Fail(
			$"CopperLine timing-test did not complete {ResultRowCount} rows in {maxFrames} frames. " +
			$"PC=0x{machine.Cpu.State.ProgramCounter & 0x00FF_FFFF:X6}, " +
			$"cycles={machine.Cpu.State.Cycles}, completedBootBlock={result.CompletedBootBlock}.");
	}

	private static void ReadResultRows(ReadOnlySpan<byte> chipRam, Span<uint> rows)
	{
		for (var row = 0; row < rows.Length; row++)
		{
			rows[row] = BigEndian.ReadUInt32(chipRam, ResultsAddress + (row * 4), $"timing row {row}");
		}
	}

	private static bool RowsComplete(ReadOnlySpan<uint> rows)
		=> rows[^1] != 0;

	private void WriteResultReport(string adfPath, ReadOnlySpan<uint> rows, int frames, long cycles)
	{
		var lines = new List<string>
		{
			"# CopperLine timing-test results from CopperMod.Amiga",
			"adf=" + adfPath,
			"frames=" + frames.ToString(System.Globalization.CultureInfo.InvariantCulture),
			"cycles=" + cycles.ToString(System.Globalization.CultureInfo.InvariantCulture)
		};

		for (var i = 0; i < rows.Length; i++)
		{
			var line = $"{i:D2} 0x{rows[i]:X8} {TimingRowNames[i]}";
			lines.Add(line);
			_output.WriteLine(line);
		}

		File.WriteAllLines(Path.ChangeExtension(adfPath, ".results.txt"), lines);
	}

	private static void AssertExpectedSanity(ReadOnlySpan<uint> rows)
	{
		Assert.InRange(rows[8], 14_000u, 14_300u);
		Assert.True(rows[19] != rows[22], "Handler entry row should include CPU interrupt-recognition latency beyond raw VERTB raise.");
		Assert.NotEqual(0xFFFF_FFFFu, rows[27]);
		Assert.Equal(0x64u, (rows[27] >> 8) & 0xFFu);
	}

	private static string? TryFindWorkspaceFile(params string[] parts)
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null)
		{
			var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		return null;
	}

	private static readonly string[] TimingRowNames =
	[
		"slow-RAM read x8192",
		"slow-RAM write x8192",
		"chip-RAM read x8192",
		"chip-RAM write x8192",
		"register move x8192",
		"lsl.l #8 x8192",
		"mulu #$5555 x8192",
		"dbra baseline x8192",
		"one PAL frame in CIA E-clock ticks",
		"slow reads/frame until vpos 280",
		"chip write x1024, no display DMA",
		"chip write x1024 during 6-bitplane DMA",
		"chip write x1024 during 8-sprite DMA",
		"dbra x8192 from slow RAM",
		"dbra x8192 from chip RAM",
		"chip write x1024 during 6-bitplane + 8-sprite DMA",
		"chip writes/frame, no interrupts",
		"chip writes/frame with VERTB interrupt",
		"chip write x1024 during 3-bitplane DMA",
		"VHPOSR at VERTB handler entry",
		"VHPOSR at chained SOFTINT task-switch end",
		"chip writes/frame with VERTB+SOFTINT task switch",
		"VHPOSR when INTREQR VERTB first reads set",
		"beam cck during D-only clear blit",
		"beam cck during A->D fill blit",
		"beam cck during line blit",
		"beam cck during A->D fill with 3-bitplane display + BLTPRI",
		"VHPOSR when INTREQR COPER first reads set"
	];
}
