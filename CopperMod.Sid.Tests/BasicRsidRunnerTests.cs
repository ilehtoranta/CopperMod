using CopperMod.Abstractions;

namespace CopperMod.Sid.Tests;

public sealed class BasicRsidRunnerTests
{
	private const byte End = 0x80;
	private const byte For = 0x81;
	private const byte Next = 0x82;
	private const byte Data = 0x83;
	private const byte Dim = 0x86;
	private const byte Read = 0x87;
	private const byte Goto = 0x89;
	private const byte If = 0x8B;
	private const byte Restore = 0x8C;
	private const byte Poke = 0x97;
	private const byte Sys = 0x9E;
	private const byte Get = 0xA1;
	private const byte To = 0xA4;
	private const byte Then = 0xA7;
	private const byte Plus = 0xAA;
	private const byte Multiply = 0xAC;
	private const byte Divide = 0xAD;
	private const byte Greater = 0xB1;
	private const byte Equal = 0xB2;
	private const byte Less = 0xB3;
	private const byte Chr = 0xC7;

	[Fact]
	public void BasicRsidPokesSidRegistersWithCycleTimestamps()
	{
		var machine = CreateMachine(
			(10, T("S", Equal, "54272")),
			(20, T(Poke, "S", Plus, "24,15:", Poke, "S,0:", Poke, "S", Plus, "1,64")),
			(30, T(Poke, "S", Plus, "5,0:", Poke, "S", Plus, "6,240:", Poke, "S", Plus, "4,33")),
			(40, T(Goto, "40")));

		machine.RunCycles(120_000);

		Assert.True(machine.DebugState.Basic.Enabled);
		Assert.False(machine.DebugState.Basic.Halted);
		Assert.Contains(machine.SidWrites, write => write.Register == 0x18 && write.Value == 0x0F);
		Assert.Contains(machine.SidWrites, write => write.Register == 0x04 && write.Value == 0x21);
		Assert.Equal(machine.SidWrites.OrderBy(write => write.Cycle).Select(write => write.Cycle), machine.SidWrites.Select(write => write.Cycle));
	}

	[Fact]
	public void BasicRsidExecutesForReadDataAndNext()
	{
		var machine = CreateMachine(
			(10, T("S", Equal, "54272")),
			(20, T(For, "I", Equal, "0", To, "2:", Read, "A:", Poke, "S", Plus, "I,A:", Next)),
			(30, T(Data, "1,2,15")),
			(40, T(Goto, "40")));

		machine.RunCycles(200_000);

		Assert.False(machine.DebugState.Basic.Halted);
		Assert.Contains(machine.SidWrites, write => write.Register == 0x00 && write.Value == 1);
		Assert.Contains(machine.SidWrites, write => write.Register == 0x01 && write.Value == 2);
		Assert.Contains(machine.SidWrites, write => write.Register == 0x02 && write.Value == 15);
	}

	[Fact]
	public void BasicRsidExecutesDimArraysIfThenRestoreGetAndStringCompare()
	{
		var machine = CreateMachine(
			(10, T(Dim, "A(1)")),
			(20, T("A(0)", Equal, "15:A(1)", Equal, "33")),
			(30, T(Get, "K$:", If, "K$", Less, Greater, Chr, "(13)", Then, Restore, ":", Goto, "40")),
			(40, T(Poke, "54296,A(0):", Poke, "54276,A(1):", Goto, "30")));

		machine.RunCycles(240_000);

		Assert.False(machine.DebugState.Basic.Halted);
		Assert.Contains(machine.SidWrites, write => write.Register == 0x18 && write.Value == 15);
		Assert.Contains(machine.SidWrites, write => write.Register == 0x04 && write.Value == 33);
	}

	[Fact]
	public void BasicRsidSysRunsMachineCodeSubroutine()
	{
		var subroutine = new byte[]
		{
			0xA9, 0x0F,       // LDA #$0F
			0x8D, 0x18, 0xD4, // STA $D418
			0x60              // RTS
		};
		var module = SidParser.Parse(SidFixtureBuilder.CreateBasicRsid(
			new[]
			{
				((ushort)10, T(Sys, "4096")),
				((ushort)20, T(Goto, "20"))
			},
			subroutine,
			0x1000));
		var machine = new C64Machine(module);

		machine.Reset(0);
		machine.RunCycles(80_000);

		Assert.False(machine.DebugState.Basic.Halted);
		Assert.Contains(machine.SidWrites, write => write.Register == 0x18 && write.Value == 0x0F);
	}

	[Fact]
	public void BasicRsidUnsupportedTokenHaltsWithDiagnostic()
	{
		var machine = CreateMachine((10, T((byte)0x93))); // LOAD

		machine.RunCycles(10_000);

		Assert.True(machine.DebugState.Basic.Halted);
		Assert.Equal(0x93, machine.DebugState.Basic.LastUnsupportedToken);
		Assert.Contains("Unsupported BASIC token", machine.DebugState.Basic.LastDiagnostic);
	}

	[Fact]
	public void BasicCorpusFixturesProduceAudibleOutputOrSpecificRunnerDiagnosticWhenPresent()
	{
		var corpusRoot = Environment.GetEnvironmentVariable("COPPERMOD_RSID_BASIC_CORPUS");
		if (string.IsNullOrWhiteSpace(corpusRoot))
		{
			return;
		}

		var files = new[]
		{
			Path.Combine(corpusRoot, "GAMES", "A-F", "Boccia_BASIC.sid"),
			Path.Combine(corpusRoot, "GAMES", "G-L", "Gwendolyn_BASIC.sid"),
			Path.Combine(corpusRoot, "GAMES", "M-R", "Prospector_BASIC.sid"),
			Path.Combine(corpusRoot, "GAMES", "M-R", "Roulette_BASIC.sid")
		};
		if (!files.All(File.Exists))
		{
			return;
		}

		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		foreach (var file in files)
		{
			var song = new SidFormat().Load(File.ReadAllBytes(file));
			Assert.Contains(song.Diagnostics, diagnostic => diagnostic.Code == "SID_RSID_BASIC_NATIVE_RUNNER");
			var peakRange = 0.0f;
			for (var tick = 0; tick < 300; tick++)
			{
				var frames = song.GetCurrentTickFrameCount(options);
				var buffer = new float[options.GetSampleCount(frames)];
				song.RenderTick(buffer, options);
				Assert.All(buffer, sample => Assert.True(float.IsFinite(sample)));
				peakRange = Math.Max(peakRange, buffer.Max() - buffer.Min());
			}

			Assert.True(peakRange > 0.02f, $"{Path.GetFileName(file)} did not produce audible AC output before the 5 second probe window.");
		}
	}

	private static C64Machine CreateMachine(params (ushort LineNumber, byte[] Tokens)[] lines)
	{
		var module = SidParser.Parse(SidFixtureBuilder.CreateBasicRsid(lines));
		var machine = new C64Machine(module);
		machine.Reset(0);
		return machine;
	}

	private static byte[] T(params object[] parts)
	{
		var bytes = new List<byte>();
		foreach (var part in parts)
		{
			switch (part)
			{
				case byte value:
					bytes.Add(value);
					break;
				case string text:
					bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(text));
					break;
				default:
					throw new ArgumentException("Unsupported BASIC token part.", nameof(parts));
			}
		}

		return bytes.ToArray();
	}
}
