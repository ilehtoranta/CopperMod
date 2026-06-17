using System.IO.Compression;
using CopperMod.Abstractions;

namespace CopperMod.Sid.Tests;

public sealed class C64CartridgeTests
{
	[Fact]
	public void ParserReadsEasyFlashTargetZipWhenPresent()
	{
		var path = FindWorkspaceFile("TestTunes", "SID", "Tough", "48Khz_hifi_Digi_Player_1_[EASYFLASH].zip");
		if (!File.Exists(path))
		{
			return;
		}

		var song = new SidFormat().Load(File.ReadAllBytes(path));

		Assert.Equal("C64 CRT", song.Metadata.FormatName);
		Assert.Equal("EasyFlash", song.Metadata.Tags["Cartridge"]);
		Assert.Equal("64", song.Metadata.Tags["CartridgeBanks"]);
	}

	[Fact]
	public void ParserReadsEasyFlashHeaderAndBankLayout()
	{
		var cartridge = C64CartridgeParser.Parse(CreateEasyFlashCrt());

		Assert.Equal(C64CartridgeType.EasyFlash, cartridge.Type);
		Assert.Equal(64, cartridge.BankCount);
		Assert.Equal(0x10, cartridge.RomlBanks[1][0]);
		Assert.Equal(0x20, cartridge.RomhBanks[1][0]);
	}

	[Fact]
	public void FormatLoadsZipWithSingleCrt()
	{
		var zip = CreateZip(CreateEasyFlashCrt());
		var song = new SidFormat().Load(zip);

		Assert.Equal("C64 CRT", song.Metadata.FormatName);
		Assert.Equal("EasyFlash", song.Metadata.Tags["Cartridge"]);
	}

	[Fact]
	public void EasyFlashMappingSwitchesBankAndMode()
	{
		var module = SidModule.CreateEasyFlashCartridge(C64CartridgeParser.Parse(CreateEasyFlashCrt()));
		var machine = new C64Machine(module);
		machine.Reset(0);

		Assert.Equal(0x8000, machine.Cpu.ProgramCounter);
		Assert.Equal(0x30, machine.Read(0xE000));
		machine.Write(0xDE02, 0x05, 0);
		Assert.Equal(0x30, machine.Read(0xE000));

		machine.Write(0xDE00, 0x01, 0);
		machine.Write(0xDE02, 0x07, 0);
		Assert.Equal(0x10, machine.Read(0x8000));
		Assert.Equal(0x20, machine.Read(0xA000));
		Assert.Equal(0x20, machine.Read(0xE000));

		machine.Write(0xDE02, 0x87, 0);
		Assert.Equal(0x60, machine.Read(0xE000));

		machine.Write(0x8000, 0x44, 0);
		machine.Write(0xDE02, 0x04, 0);
		Assert.Equal(0x44, machine.Read(0x8000));

		machine.Write(0xDE02, 0x05, 0);
		Assert.Equal(0x20, machine.Read(0xE000));
	}

	[Fact]
	public void DigimaxWritesProduceBoundedAudio()
	{
		var module = SidModule.CreateEasyFlashCartridge(C64CartridgeParser.Parse(CreateDigimaxLoopCrt()));
		var machine = new C64Machine(module);
		machine.Reset(0);
		var options = new AudioRenderOptionsAdapter(sampleRate: 48000, channelCount: 1);
		var samples = new float[64];
		var sampleTargets = Enumerable.Range(1, samples.Length).Select(i => (long)(i * 21)).ToArray();

		machine.RenderFrame(samples, options, sampleTargets, sampleTargets[^1]);

		Assert.NotEmpty(machine.DigimaxWrites);
		Assert.All(samples, sample => Assert.InRange(sample, -0.999f, 0.999f));
		Assert.True(samples.Max() - samples.Min() > 0.05f);
		Assert.Contains(
			machine.DigimaxWrites.Zip(machine.DigimaxWrites.Skip(1), (left, right) => right.Cycle - left.Cycle),
			delta => delta > 0 && delta <= 25);
	}

	[Fact]
	public void F3AutostartPullsKeyboardRowOnlyDuringHoldWindow()
	{
		var module = SidModule.CreateEasyFlashCartridge(C64CartridgeParser.Parse(CreateKeyboardProbeCrt(portAColumnSelect: 0xFE, expectedPortBMask: 0xDF)));
		var machine = new C64Machine(module);
		machine.ScheduleAutostartKey("f3", TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(4));
		machine.Reset(0);
		var options = new AudioRenderOptionsAdapter(sampleRate: 1000, channelCount: 1);
		var samples = new float[10];
		var sampleTargets = Enumerable.Range(1, samples.Length)
			.Select(i => SidIntegerMath.TimeSpanToCycles(TimeSpan.FromMilliseconds(i), SidConstants.PalCpuCyclesPerSecond))
			.ToArray();

		machine.RenderFrame(samples, options, sampleTargets, sampleTargets[^1]);

		Assert.Equal(0xFF, machine.Ram[0x2000]);
		Assert.Equal(0xDF, machine.Ram[0x2001]);
	}

	[Fact]
	public void SpaceAutostartPullsKeyboardRowDuringScheduledWindow()
	{
		var module = SidModule.CreateEasyFlashCartridge(C64CartridgeParser.Parse(CreateKeyboardProbeCrt(portAColumnSelect: 0x7F, expectedPortBMask: 0xEF)));
		var machine = new C64Machine(module);
		machine.ScheduleAutostartKey("space", TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(4));
		machine.Reset(0);
		var options = new AudioRenderOptionsAdapter(sampleRate: 1000, channelCount: 1);
		var samples = new float[10];
		var sampleTargets = Enumerable.Range(1, samples.Length)
			.Select(i => SidIntegerMath.TimeSpanToCycles(TimeSpan.FromMilliseconds(i), SidConstants.PalCpuCyclesPerSecond))
			.ToArray();

		machine.RenderFrame(samples, options, sampleTargets, sampleTargets[^1]);

		Assert.Equal(0xEF, machine.Ram[0x2001]);
	}

	[Fact]
	public void EasyFlashTargetZipBootsFromRomhResetVectorWhenPresent()
	{
		var path = FindWorkspaceFile("TestTunes", "SID", "Tough", "48Khz_hifi_Digi_Player_1_[EASYFLASH].zip");
		if (!File.Exists(path))
		{
			return;
		}

		var module = SidModule.CreateEasyFlashCartridge(C64CartridgeParser.Parse(ReadSingleCrtFromZip(path)));
		var machine = new C64Machine(module);

		machine.Reset(0);

		Assert.Equal(0xE000, machine.Cpu.ProgramCounter);
	}

	[Fact]
	public void EasyFlashTargetZipProducesVisibleDiagnosticVideoWhenPresent()
	{
		var path = FindWorkspaceFile("TestTunes", "SID", "Tough", "48Khz_hifi_Digi_Player_1_[EASYFLASH].zip");
		if (!File.Exists(path))
		{
			return;
		}

		var module = SidModule.CreateEasyFlashCartridge(C64CartridgeParser.Parse(ReadSingleCrtFromZip(path)));
		var machine = new C64Machine(module);
		machine.Reset(0);

		for (var i = 0; i < 20; i++)
		{
			machine.RunFrame();
		}

		var frame = machine.RenderVideoFrame();
		var distinctColors = frame.Pixels.Select(pixel => pixel.Value).Distinct().Take(4).Count();

		Assert.True(
			distinctColors >= 3,
			$"Expected the EasyFlash target to produce visible diagnostic graphics; found {distinctColors} distinct colors.");
	}

	[Fact]
	public void EasyFlashTargetZip2RendersPictureAndSpriteTextWhenPresent()
	{
		var path = FindWorkspaceFile("TestTunes", "SID", "Tough", "48Khz_hifi_Digi_Player_2_[EASYFLASH].zip");
		if (!File.Exists(path))
		{
			return;
		}

		var module = SidModule.CreateEasyFlashCartridge(C64CartridgeParser.Parse(ReadSingleCrtFromZip(path)));
		var machine = new C64Machine(module);
		machine.ScheduleAutostartKey("f3", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.25));
		machine.Reset(0);

		for (var i = 0; i < 130; i++)
		{
			machine.RunFrame();
		}

		var frame = machine.RenderVideoFrame();
		var distinctColors = frame.Pixels.Select(pixel => pixel.Value).Distinct().Take(8).Count();
		var white = new Argb32(255, 0xFF, 0xFF, 0xFF).Value;
		var rightPanelWhitePixels = CountPixels(frame, x0: 230, y0: 80, x1: 360, y1: 230, white);

		Assert.True(distinctColors >= 8, $"Expected the EasyFlash picture to use a broad palette; found {distinctColors} colors.");
		Assert.True(rightPanelWhitePixels > 200, $"Expected readable sprite text on the right panel; found {rightPanelWhitePixels} white pixels.");
	}

	private static byte[] CreateDigimaxLoopCrt()
	{
		return CreateEasyFlashCrt(romlBank0: new byte[]
		{
			0xA2, 0x00,       // LDX #$00
			0x8E, 0x00, 0xDF, // loop: STX $DF00
			0xE8,             // INX
			0xE8,             // INX
			0xE8,             // INX
			0xEA,             // NOP
			0x4C, 0x02, 0x80  // JMP loop; regular high-rate DAC loop
		});
	}

	private static byte[] CreateKeyboardProbeCrt(byte portAColumnSelect, byte expectedPortBMask)
	{
		return CreateEasyFlashCrt(romlBank0: new byte[]
		{
			0xA9, 0xFF,       // LDA #$FF
			0x8D, 0x02, 0xDC, // STA $DC02; drive CIA1 port A as keyboard column output
			0xA9, portAColumnSelect, // loop: LDA #column; select one keyboard column
			0x8D, 0x00, 0xDC, // STA $DC00
			0xAD, 0x01, 0xDC, // LDA $DC01
			0xC9, expectedPortBMask, // CMP #expected
			0xF0, 0x06,       // BEQ pressed
			0x8D, 0x00, 0x20, // STA $2000
			0x4C, 0x05, 0x80, // JMP loop
			0x8D, 0x01, 0x20, // pressed: STA $2001
			0x4C, 0x05, 0x80  // JMP loop
		});
	}

	private static byte[] CreateEasyFlashCrt(byte[]? romlBank0 = null, ushort resetVector = 0x8000)
	{
		using var stream = new MemoryStream();
		WriteAscii(stream, "C64 CARTRIDGE   ");
		WriteBigEndian(stream, 0x40U);
		WriteBigEndian(stream, (ushort)0x0100);
		WriteBigEndian(stream, (ushort)32);
		stream.WriteByte(0);
		stream.WriteByte(0);
		stream.Write(new byte[6]);
		var name = new byte[32];
		"TEST EASYFLASH"u8.CopyTo(name);
		stream.Write(name);

		for (var bank = 0; bank < 64; bank++)
		{
			var roml = new byte[0x2000];
			var romh = new byte[0x2000];
			roml[0] = (byte)(bank * 0x10);
			romh[0] = (byte)(bank * 0x20);
			romh[0x1FFC] = (byte)resetVector;
			romh[0x1FFD] = (byte)(resetVector >> 8);
			if (bank == 0)
			{
				romh[0] = 0x30;
				if (romlBank0 != null)
				{
					romlBank0.CopyTo(roml, 0);
				}
			}

			WriteChip(stream, bank, 0x8000, roml);
			WriteChip(stream, bank, 0xA000, romh);
		}

		return stream.ToArray();
	}

	private static byte[] CreateZip(byte[] crt)
	{
		using var stream = new MemoryStream();
		using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
		{
			var entry = archive.CreateEntry("test.crt");
			using var entryStream = entry.Open();
			entryStream.Write(crt);
		}

		return stream.ToArray();
	}

	private static byte[] ReadSingleCrtFromZip(string path)
	{
		using var archive = ZipFile.OpenRead(path);
		var entries = archive.Entries.Where(entry => entry.Name.EndsWith(".crt", StringComparison.OrdinalIgnoreCase)).ToArray();
		Assert.Single(entries);
		using var stream = entries[0].Open();
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}

	private static int CountPixels(C64VideoFrame frame, int x0, int y0, int x1, int y1, uint value)
	{
		var count = 0;
		for (var y = y0; y < y1; y++)
		{
			for (var x = x0; x < x1; x++)
			{
				if (frame.Pixels[(y * frame.Width) + x].Value == value)
				{
					count++;
				}
			}
		}

		return count;
	}

	private static void WriteChip(Stream stream, int bank, ushort address, byte[] data)
	{
		WriteAscii(stream, "CHIP");
		WriteBigEndian(stream, (uint)(16 + data.Length));
		WriteBigEndian(stream, (ushort)2);
		WriteBigEndian(stream, (ushort)bank);
		WriteBigEndian(stream, address);
		WriteBigEndian(stream, (ushort)data.Length);
		stream.Write(data);
	}

	private static void WriteAscii(Stream stream, string text)
	{
		stream.Write(System.Text.Encoding.ASCII.GetBytes(text));
	}

	private static void WriteBigEndian(Stream stream, ushort value)
	{
		stream.WriteByte((byte)(value >> 8));
		stream.WriteByte((byte)value);
	}

	private static void WriteBigEndian(Stream stream, uint value)
	{
		stream.WriteByte((byte)(value >> 24));
		stream.WriteByte((byte)(value >> 16));
		stream.WriteByte((byte)(value >> 8));
		stream.WriteByte((byte)value);
	}

	private static string FindWorkspaceFile(params string[] parts)
	{
		var current = new DirectoryInfo(AppContext.BaseDirectory);
		while (current != null)
		{
			var candidate = Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
			if (File.Exists(candidate))
			{
				return candidate;
			}

			current = current.Parent;
		}

		return Path.Combine(parts);
	}
}
