using System.Reflection;
using System.Security.Cryptography;
using CopperMod.Amiga;
using CopperScreen;

namespace CopperScreen.Tests;

public sealed class CopperScreenDiagRomTests
{
	private const int ExpectedDiagRomLength = 524_288;
	private const uint ExpectedDiagRomA500ResetPc = 0x00F8_00D2;
	private const string ExpectedDiagRomA500Sha256 = "1803BFFF5D866DE7605DBCF8445B590F36ED7CB7EE92166B240907E488CF0FF7";
	private const uint ExpectedDiagRomV20ResetPc = 0x00F8_00D6;
	private const string ExpectedDiagRomV20Sha256 = "8DA1CF37B74B2BF1BDE3DC725D00A27EB6BD402E859BAFAF9CA2E74BBF0273F6";
	private const int MainMenuMinimumFrames = 1_000;
	private const int MainMenuMaxFrames = 1_500;
	private const int MenuTransitionMaxFrames = 300;
	private const int StableSamples = 6;
	private const int CrashScreenRedPixelThreshold = 2_000;

	[Fact]
	public void DiagRomProfileBootsBundledRomWithoutDiskWhenAvailable()
	{
		if (TryFindSupportedDiagRom(requireProfileDefault: true) == null)
		{
			return;
		}

		using var emulator = CopperScreenEmulator.Create(["--profile", "diagrom"], AppContext.BaseDirectory);

		emulator.RenderNextFrame();

		Assert.DoesNotContain("insert disk image", emulator.StatusText, StringComparison.OrdinalIgnoreCase);
		Assert.False(ContainsFatalBootStatus(emulator.StatusText), emulator.StatusText);
		Assert.NotEqual(0u, emulator.CpuState.ProgramCounter);
	}

	[Fact]
	public void DiagRomMainMenuNavigatesToMemoryMenuAndBackWhenEnabledAndAvailable()
	{
		if (!IsDiagRomMenuTestsEnabled())
		{
			return;
		}

		var harness = TryCreateHarness();
		if (harness == null)
		{
			return;
		}

		using (harness)
		{
			var main = WaitForStableScreen(harness, MainMenuMaxFrames, MainMenuMinimumFrames, "DiagROM main menu");

			TapKey(harness, AmigaRawKey.Digit2);
			var memory = WaitForStableScreenDifferentFrom(harness, main.Hash, MenuTransitionMaxFrames, "DiagROM memory menu");
			Assert.NotEqual(main.Hash, memory.Hash);

			TapKey(harness, AmigaRawKey.Digit0);
			WaitForStableScreenMatching(harness, main.Hash, MenuTransitionMaxFrames, "DiagROM main menu after leaving memory menu");
		}
	}

	[Fact]
	public void DiagRomMainMenuNavigatesToIrqCiaMenuAndBackWhenEnabledAndAvailable()
	{
		if (!IsDiagRomMenuTestsEnabled())
		{
			return;
		}

		var harness = TryCreateHarness();
		if (harness == null)
		{
			return;
		}

		using (harness)
		{
			var main = WaitForStableScreen(harness, MainMenuMaxFrames, MainMenuMinimumFrames, "DiagROM main menu");

			TapKey(harness, AmigaRawKey.Digit3);
			var irqCia = WaitForStableScreenDifferentFrom(harness, main.Hash, MenuTransitionMaxFrames, "DiagROM IRQ/CIA menu");
			Assert.NotEqual(main.Hash, irqCia.Hash);

			TapKey(harness, AmigaRawKey.Digit9);
			WaitForStableScreenMatching(harness, main.Hash, MenuTransitionMaxFrames, "DiagROM main menu after leaving IRQ/CIA menu");
		}
	}

	[Fact]
	public void DiagRomKeyboardTestReceivesKeyInputWhenEnabledAndAvailable()
	{
		if (!IsDiagRomMenuTestsEnabled())
		{
			return;
		}

		var harness = TryCreateHarness();
		if (harness == null)
		{
			return;
		}

		using (harness)
		{
			var main = WaitForStableScreen(harness, MainMenuMaxFrames, MainMenuMinimumFrames, "DiagROM main menu");

			TapKey(harness, AmigaRawKey.Digit7);
			var keyboard = WaitForStableScreenDifferentFrom(harness, main.Hash, MenuTransitionMaxFrames, "DiagROM keyboard test");

			var changed = TapKeyAndDetectScreenChange(harness, AmigaRawKey.A, keyboard.Hash, framesPerEdge: 10);
			Assert.True(changed, BuildFailure("Expected DiagROM keyboard test screen to change after A key input.", harness, frame: 0, []));

			TapKey(harness, AmigaRawKey.Escape);
			WaitForStableScreenMatching(harness, main.Hash, MenuTransitionMaxFrames, "DiagROM main menu after leaving keyboard test");
		}
	}

	[Fact]
	public void DiagRomAudioSimpleWaveformDoesNotEnterCrashScreenWhenEnabledAndAvailable()
	{
		if (!IsDiagRomMenuTestsEnabled())
		{
			return;
		}

		var harness = TryCreateHarness(requireAudioSimpleWaveform: true);
		if (harness == null)
		{
			return;
		}

		using (harness)
		{
			var main = WaitForStableScreen(harness, MainMenuMaxFrames, MainMenuMinimumFrames, "DiagROM main menu");

			TapKey(harness, AmigaRawKey.Digit1);
			WaitForStableScreenDifferentFrom(harness, main.Hash, MenuTransitionMaxFrames, "DiagROM audio menu");

			TapKey(harness, AmigaRawKey.Digit1);
			for (var frame = 1; frame <= 240; frame++)
			{
				RenderChecked(harness, frame, null);
				if (HasDiagRomCrashScreen(harness.Emulator))
				{
					throw new Xunit.Sdk.XunitException(BuildFailure("DiagROM simple waveform entered the crash screen.", harness, frame, []));
				}
			}
		}
	}

	// These tests boot and drive the full DiagROM UI, so they stay gated like the other corpus-style diagnostics.
	private static bool IsDiagRomMenuTestsEnabled()
		=> string.Equals(
			Environment.GetEnvironmentVariable("COPPERSCREEN_DIAGROM_MENU_TESTS"),
			"1",
			StringComparison.Ordinal);

	private static StableScreen WaitForStableScreen(
		DiagRomHarness harness,
		int maxFrames,
		int minimumFrames,
		string description)
	{
		var recentHashes = new List<uint>(maxFrames);
		uint lastHash = 0;
		var stableCount = 0;
		for (var frame = 1; frame <= maxFrames; frame++)
		{
			RenderChecked(harness, frame, recentHashes);
			var hash = recentHashes[^1];
			if (frame < minimumFrames || !HasMeaningfulVideo(harness.Emulator))
			{
				lastHash = hash;
				stableCount = 0;
				continue;
			}

			if (hash == lastHash)
			{
				stableCount++;
			}
			else
			{
				lastHash = hash;
				stableCount = 1;
			}

			if (stableCount >= StableSamples)
			{
				return new StableScreen(hash, frame);
			}
		}

		throw new Xunit.Sdk.XunitException(BuildFailure($"Timed out waiting for {description}.", harness, maxFrames, recentHashes));
	}

	private static StableScreen WaitForStableScreenDifferentFrom(
		DiagRomHarness harness,
		uint previousHash,
		int maxFrames,
		string description)
	{
		var recentHashes = new List<uint>(maxFrames);
		uint lastHash = 0;
		var stableCount = 0;
		for (var frame = 1; frame <= maxFrames; frame++)
		{
			RenderChecked(harness, frame, recentHashes);
			var hash = recentHashes[^1];
			if (hash == previousHash || !HasMeaningfulVideo(harness.Emulator))
			{
				lastHash = hash;
				stableCount = 0;
				continue;
			}

			if (hash == lastHash)
			{
				stableCount++;
			}
			else
			{
				lastHash = hash;
				stableCount = 1;
			}

			if (stableCount >= StableSamples)
			{
				return new StableScreen(hash, frame);
			}
		}

		throw new Xunit.Sdk.XunitException(BuildFailure($"Timed out waiting for {description}.", harness, maxFrames, recentHashes));
	}

	private static void WaitForStableScreenMatching(
		DiagRomHarness harness,
		uint expectedHash,
		int maxFrames,
		string description)
	{
		var recentHashes = new List<uint>(maxFrames);
		var stableCount = 0;
		for (var frame = 1; frame <= maxFrames; frame++)
		{
			RenderChecked(harness, frame, recentHashes);
			var hash = recentHashes[^1];
			stableCount = hash == expectedHash ? stableCount + 1 : 0;
			if (stableCount >= StableSamples)
			{
				return;
			}
		}

		throw new Xunit.Sdk.XunitException(BuildFailure($"Timed out waiting for {description}.", harness, maxFrames, recentHashes));
	}

	private static void TapKey(DiagRomHarness harness, AmigaRawKey key)
	{
		harness.Emulator.KeyDown(key);
		RenderChecked(harness, 0, null);
		RenderChecked(harness, 0, null);
		harness.Emulator.KeyUp(key);
		RenderChecked(harness, 0, null);
		RenderChecked(harness, 0, null);
	}

	private static bool TapKeyAndDetectScreenChange(DiagRomHarness harness, AmigaRawKey key, uint previousHash, int framesPerEdge)
	{
		var changed = false;
		harness.Emulator.KeyDown(key);
		for (var frame = 0; frame < framesPerEdge; frame++)
		{
			RenderChecked(harness, frame, null);
			changed |= CoarseFramebufferHash(harness.Emulator) != previousHash;
		}

		harness.Emulator.KeyUp(key);
		for (var frame = 0; frame < framesPerEdge; frame++)
		{
			RenderChecked(harness, frame, null);
			changed |= CoarseFramebufferHash(harness.Emulator) != previousHash;
		}

		return changed;
	}

	private static void RenderChecked(DiagRomHarness harness, int frame, List<uint>? recentHashes)
	{
		harness.Emulator.RenderNextFrame();
		var fatal = BuildFatalDiagnosticStatus(GetDiagnostics(harness.Emulator));
		if (fatal != null || ContainsFatalBootStatus(harness.Emulator.StatusText))
		{
			throw new Xunit.Sdk.XunitException(BuildFailure($"DiagROM boot entered fatal status: {fatal ?? harness.Emulator.StatusText}.", harness, frame, recentHashes ?? []));
		}

		recentHashes?.Add(CoarseFramebufferHash(harness.Emulator));
	}

	private static bool HasMeaningfulVideo(CopperScreenEmulator emulator)
	{
		var stats = SampleFrameStats(emulator);
		return stats.NonBlackSamples > 10 && stats.DifferentSamples > 10;
	}

	private static uint CoarseFramebufferHash(CopperScreenEmulator emulator)
	{
		var hash = 2166136261u;
		var pixels = emulator.Framebuffer;
		for (var y = 0; y < emulator.Height; y += 2)
		{
			var row = y * emulator.Width;
			for (var x = 0; x < emulator.Width; x += 2)
			{
				hash ^= unchecked((uint)pixels[row + x]);
				hash *= 16777619u;
			}
		}

		return hash;
	}

	private static DiagRomHarness? TryCreateHarness(bool requireAudioSimpleWaveform = false)
	{
		var rom = TryFindSupportedDiagRom(requireAudioSimpleWaveform: requireAudioSimpleWaveform);
		if (rom == null)
		{
			return null;
		}

		var tempDirectory = Path.Combine(Path.GetTempPath(), "copperscreen-diagrom-test-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempDirectory);
		var diskPath = Path.Combine(tempDirectory, "diagrom-blank.adf");
		var diskBytes = new byte[AmigaDiskImage.StandardAdfSize];
		File.WriteAllBytes(diskPath, diskBytes);

		var emulator = CopperScreenEmulator.CreateWithLoadedDisk(
			["--profile", "expanded-kickstart13", "--kickstart-rom", rom.Path, diskPath],
			AppContext.BaseDirectory,
			AmigaDiskImage.FromAdfBytes(diskBytes, Path.GetFileName(diskPath)));
		return new DiagRomHarness(emulator, GetMachine(emulator), tempDirectory);
	}

	private static DiagRomImage? TryFindSupportedDiagRom(
		bool requireAudioSimpleWaveform = false,
		bool requireProfileDefault = false)
	{
		foreach (var candidate in GetSupportedDiagRomCandidates())
		{
			if (requireAudioSimpleWaveform && !candidate.SupportsAudioSimpleWaveform)
			{
				continue;
			}

			if (requireProfileDefault && !candidate.IsProfileDefault)
			{
				continue;
			}

			var path = TryFindWorkspaceFile("CopperScreen", "ROM", "DiagROM", candidate.FileName);
			if (path == null)
			{
				continue;
			}

			var rom = File.ReadAllBytes(path);
			if (rom.Length != ExpectedDiagRomLength ||
				BigEndian.ReadUInt32(rom, 4, "DiagROM reset program counter") != candidate.ResetProgramCounter)
			{
				continue;
			}

			var hash = Convert.ToHexString(SHA256.HashData(rom));
			if (string.Equals(hash, candidate.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				return new DiagRomImage(path, candidate.SupportsAudioSimpleWaveform);
			}
		}

		return null;
	}

	private static IEnumerable<DiagRomCandidate> GetSupportedDiagRomCandidates()
	{
		yield return new DiagRomCandidate(
			"diagrom-a500.rom",
			ExpectedDiagRomA500ResetPc,
			ExpectedDiagRomA500Sha256,
			SupportsAudioSimpleWaveform: true,
			IsProfileDefault: true);
		yield return new DiagRomCandidate(
			"diagrom.rom",
			ExpectedDiagRomV20ResetPc,
			ExpectedDiagRomV20Sha256,
			SupportsAudioSimpleWaveform: false,
			IsProfileDefault: false);
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

	private static AmigaMachine GetMachine(CopperScreenEmulator emulator)
	{
		return (AmigaMachine)typeof(CopperScreenEmulator)
			.GetField("_machine", BindingFlags.NonPublic | BindingFlags.Instance)!
			.GetValue(emulator)!;
	}

	private static IReadOnlyList<AmigaBootDiagnostic> GetDiagnostics(CopperScreenEmulator emulator)
	{
		var boot = (AmigaBootController)typeof(CopperScreenEmulator)
			.GetField("_boot", BindingFlags.NonPublic | BindingFlags.Instance)!
			.GetValue(emulator)!;
		return boot.Diagnostics;
	}

	private static string? BuildFatalDiagnosticStatus(IReadOnlyList<AmigaBootDiagnostic> diagnostics)
	{
		var fatalDiagnostics = diagnostics
			.Where(diagnostic => diagnostic.Code is "AMIGA_BOOT_UNSUPPORTED_OPCODE" or "AMIGA_BOOT_FAULT" or "AMIGA_BOOT_PROTECTED_DISK_UNSUPPORTED" or "AMIGA_BOOT_NULL_PC")
			.Select(diagnostic => diagnostic.Code)
			.ToArray();
		return fatalDiagnostics.Length == 0
			? null
			: string.Join(", ", fatalDiagnostics);
	}

	private static bool ContainsFatalBootStatus(string statusText)
		=> statusText.Contains("AMIGA_BOOT_UNSUPPORTED_OPCODE", StringComparison.Ordinal) ||
			statusText.Contains("AMIGA_BOOT_FAULT", StringComparison.Ordinal) ||
			statusText.Contains("AMIGA_BOOT_PROTECTED_DISK_UNSUPPORTED", StringComparison.Ordinal) ||
			statusText.Contains("AMIGA_BOOT_NULL_PC", StringComparison.Ordinal);

	private static bool HasDiagRomCrashScreen(CopperScreenEmulator emulator)
	{
		return CountCrashScreenRedPixels(emulator) >= CrashScreenRedPixelThreshold;
	}

	private static int CountCrashScreenRedPixels(CopperScreenEmulator emulator)
	{
		var redPixels = 0;
		foreach (var pixel in emulator.Framebuffer)
		{
			var red = (pixel >> 16) & 0xFF;
			var green = (pixel >> 8) & 0xFF;
			var blue = pixel & 0xFF;
			if (red >= 160 && green <= 80 && blue <= 80)
			{
				redPixels++;
			}
		}

		return redPixels;
	}

	private static string BuildFailure(string message, DiagRomHarness harness, int frame, IReadOnlyList<uint> recentHashes)
	{
		var machine = harness.Machine;
		var disk = machine.Bus.Disk.CaptureSnapshot();
		var pc = machine.Cpu.State.ProgramCounter & 0x00FF_FFFF;
		var opcode = machine.Bus.ReadWord(pc);
		var intreq = machine.Bus.ReadWord(0x00DFF01E);
		var intena = machine.Bus.ReadWord(0x00DFF01C);
		var dmaconr = machine.Bus.ReadWord(0x00DFF002);
		var vector2 = machine.Bus.ReadLong(0x0000_0008);
		var vector3 = machine.Bus.ReadLong(0x0000_000C);
		var vector4 = machine.Bus.ReadLong(0x0000_0010);
		var stack = CaptureStackWords(machine, machine.Cpu.State.A[7], 8);
		var hashTail = string.Join(", ", recentHashes.TakeLast(12).Select(hash => $"0x{hash:X8}"));
		var stats = SampleFrameStats(harness.Emulator);
		return $"{message} frame={frame}, status={harness.Emulator.StatusText}, pc=0x{pc:X6}, opcode=0x{opcode:X4}, " +
			$"sr=0x{machine.Cpu.State.StatusRegister:X4}, cycles={machine.Cpu.State.Cycles}, halted={machine.Cpu.State.Halted}, stopped={machine.Cpu.State.Stopped}, " +
			$"frameStats={stats}, crashRedPixels={CountCrashScreenRedPixels(harness.Emulator)}, " +
			$"vectors=[2:0x{vector2:X8},3:0x{vector3:X8},4:0x{vector4:X8}], stack=[{stack}], " +
			$"dmacon=0x{machine.Bus.Paula.Dmacon:X4}, dmaconr=0x{dmaconr:X4}, intena=0x{intena:X4}, intreq=0x{intreq:X4}, " +
			$"disk={disk.LastTransferDrive}:{disk.LastTransferCylinder}.{disk.LastTransferHead}@0x{disk.LastTransferAddress:X6}, transfers={disk.TransferCount}, " +
			$"selected={disk.SelectedDrive}, active={disk.ActiveDmaDrive}/{disk.ActiveDma}, dsklen=0x{disk.Dsklen:X4}, dskbytr=0x{disk.Dskbytr:X4}, " +
			$"ciab=0x{disk.CiabPortB:X2}, hashes=[{hashTail}]";
	}

	private static string CaptureStackWords(AmigaMachine machine, uint stackPointer, int wordCount)
	{
		var words = new string[wordCount];
		for (var i = 0; i < words.Length; i++)
		{
			words[i] = $"0x{machine.Bus.ReadWord(stackPointer + (uint)(i * 2)):X4}";
		}

		return string.Join(", ", words);
	}

	private static FrameStats SampleFrameStats(CopperScreenEmulator emulator)
	{
		var pixels = emulator.Framebuffer;
		var nonBlack = 0;
		var first = pixels.Length == 0 ? 0 : pixels[0];
		var different = 0;
		for (var i = 0; i < pixels.Length; i += 4)
		{
			if (pixels[i] != unchecked((int)0xFF000000))
			{
				nonBlack++;
			}

			if (pixels[i] != first)
			{
				different++;
			}
		}

		return new FrameStats(nonBlack, different, pixels.Length / 4);
	}

	private sealed class DiagRomHarness : IDisposable
	{
		public DiagRomHarness(CopperScreenEmulator emulator, AmigaMachine machine, string tempDirectory)
		{
			Emulator = emulator;
			Machine = machine;
			TempDirectory = tempDirectory;
		}

		public CopperScreenEmulator Emulator { get; }

		public AmigaMachine Machine { get; }

		private string TempDirectory { get; }

		public void Dispose()
		{
			Emulator.Dispose();
			if (Directory.Exists(TempDirectory))
			{
				Directory.Delete(TempDirectory, recursive: true);
			}
		}
	}

	private readonly record struct StableScreen(uint Hash, int Frame);

	private readonly record struct FrameStats(int NonBlackSamples, int DifferentSamples, int TotalSamples);

	private readonly record struct DiagRomCandidate(
		string FileName,
		uint ResetProgramCounter,
		string Sha256,
		bool SupportsAudioSimpleWaveform,
		bool IsProfileDefault);

	private sealed record DiagRomImage(string Path, bool SupportsAudioSimpleWaveform);
}
