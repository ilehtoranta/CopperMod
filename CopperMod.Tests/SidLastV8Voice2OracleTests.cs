using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using CopperMod.Abstractions;
using CopperMod.Rendering;
using CopperMod.Sid;

namespace CopperMod.Tests;

[Collection("SidPlayFP oracle")]
public sealed class SidLastV8Voice2OracleTests
{
	private const int SampleRate = 48000;
	private const ushort SidBase = 0xD400;
	private const ushort ProgramBase = 0x1000;
	private const byte FrameCounterAddress = 0x02;
	private const byte PointerAddress = 0xFB;

	private static readonly TimedWrite[] LastV8Voice2Writes =
	{
		new(0, 8394, 0x08, 0x3A), new(0, 8451, 0x07, 0x8C), new(0, 8506, 0x0B, 0x11), new(0, 8515, 0x09, 0x00),
		new(0, 8524, 0x0A, 0x08), new(0, 8533, 0x0C, 0x04), new(0, 8542, 0x0D, 0x0F), new(1, 8233, 0x07, 0x8C),
		new(1, 8242, 0x08, 0x3A), new(1, 8320, 0x08, 0x3A), new(1, 8327, 0x0B, 0x80), new(2, 8330, 0x07, 0x8C),
		new(2, 8339, 0x08, 0x3A), new(2, 8466, 0x08, 0x3A), new(2, 8480, 0x0B, 0x10), new(3, 8234, 0x07, 0x8C),
		new(3, 8243, 0x08, 0x3A), new(3, 8327, 0x08, 0x39), new(3, 8341, 0x0B, 0x10), new(4, 8331, 0x07, 0x8C),
		new(4, 8340, 0x08, 0x3A), new(4, 8467, 0x08, 0x38), new(4, 8481, 0x0B, 0x10), new(5, 8235, 0x07, 0x8C),
		new(5, 8244, 0x08, 0x3A), new(5, 8328, 0x08, 0x37), new(5, 8342, 0x0B, 0x10), new(6, 8152, 0x0B, 0x10),
		new(6, 8159, 0x0C, 0x00), new(6, 8164, 0x0D, 0x00), new(6, 8353, 0x07, 0x8C), new(6, 8362, 0x08, 0x3A),
		new(7, 8234, 0x07, 0x8C), new(7, 8243, 0x08, 0x3A), new(8, 8271, 0x08, 0x41), new(8, 8285, 0x07, 0xB8),
		new(8, 8340, 0x0B, 0x41), new(8, 8349, 0x09, 0xA0), new(8, 8358, 0x0A, 0x09), new(8, 8367, 0x0C, 0x04),
		new(8, 8376, 0x0D, 0x09), new(9, 8188, 0x08, 0x41), new(9, 8195, 0x0B, 0x80), new(9, 8256, 0x08, 0x83),
		new(9, 8265, 0x07, 0x70), new(10, 8292, 0x08, 0x41), new(10, 8306, 0x0B, 0x40), new(10, 8361, 0x08, 0x41),
		new(10, 8370, 0x07, 0xB8), new(11, 8194, 0x08, 0x40), new(11, 8208, 0x0B, 0x40), new(11, 8269, 0x08, 0x83),
		new(11, 8278, 0x07, 0x70), new(12, 8291, 0x08, 0x3F), new(12, 8305, 0x0B, 0x40), new(12, 8360, 0x08, 0x41),
		new(12, 8369, 0x07, 0xB8), new(13, 8195, 0x08, 0x3E), new(13, 8209, 0x0B, 0x40), new(13, 8270, 0x08, 0x83),
		new(13, 8279, 0x07, 0x70), new(14, 8152, 0x0B, 0x40), new(14, 8159, 0x0C, 0x00), new(14, 8164, 0x0D, 0x00),
		new(14, 8334, 0x08, 0x41), new(14, 8343, 0x07, 0xB8), new(15, 8220, 0x08, 0x83), new(15, 8229, 0x07, 0x70),
		new(16, 8250, 0x08, 0x57), new(16, 8264, 0x07, 0xAC), new(16, 8319, 0x0B, 0x41), new(16, 8328, 0x09, 0xA0),
		new(16, 8337, 0x0A, 0x09), new(16, 8346, 0x0C, 0x04), new(16, 8355, 0x0D, 0x09), new(17, 8189, 0x08, 0x57),
		new(17, 8196, 0x0B, 0x80), new(17, 8257, 0x08, 0xAF), new(17, 8266, 0x07, 0x58), new(18, 8292, 0x08, 0x57),
		new(18, 8306, 0x0B, 0x40), new(18, 8361, 0x08, 0x57), new(18, 8370, 0x07, 0xAC), new(19, 8194, 0x08, 0x56),
		new(19, 8208, 0x0B, 0x40), new(19, 8269, 0x08, 0xAF), new(19, 8278, 0x07, 0x58), new(20, 8291, 0x08, 0x55),
		new(20, 8305, 0x0B, 0x40), new(20, 8360, 0x08, 0x57), new(20, 8369, 0x07, 0xAC), new(21, 8195, 0x08, 0x54),
		new(21, 8209, 0x0B, 0x40), new(21, 8270, 0x08, 0xAF), new(21, 8279, 0x07, 0x58), new(22, 8153, 0x0B, 0x40),
		new(22, 8160, 0x0C, 0x00), new(22, 8165, 0x0D, 0x00), new(22, 8335, 0x08, 0x57), new(22, 8344, 0x07, 0xAC),
		new(23, 8220, 0x08, 0xAF), new(23, 8229, 0x07, 0x58), new(24, 8271, 0x08, 0x2B), new(24, 8285, 0x07, 0xD6),
		new(24, 8340, 0x0B, 0x11), new(24, 8349, 0x09, 0x00), new(24, 8358, 0x0A, 0x08), new(24, 8367, 0x0C, 0x04),
		new(24, 8376, 0x0D, 0x0F), new(25, 8233, 0x07, 0xD6), new(25, 8242, 0x08, 0x2B), new(25, 8320, 0x08, 0x2B),
		new(25, 8327, 0x0B, 0x80), new(26, 8330, 0x07, 0xD6), new(26, 8339, 0x08, 0x2B), new(26, 8466, 0x08, 0x2B),
		new(26, 8480, 0x0B, 0x10), new(27, 8233, 0x07, 0xD6), new(27, 8242, 0x08, 0x2B), new(27, 8326, 0x08, 0x2A),
		new(27, 8340, 0x0B, 0x10), new(28, 8331, 0x07, 0xD6), new(28, 8340, 0x08, 0x2B), new(28, 8467, 0x08, 0x29),
		new(28, 8481, 0x0B, 0x10), new(29, 8234, 0x07, 0xD6), new(29, 8243, 0x08, 0x2B), new(29, 8327, 0x08, 0x28),
		new(29, 8341, 0x0B, 0x10), new(30, 8151, 0x0B, 0x10), new(30, 8158, 0x0C, 0x00), new(30, 8163, 0x0D, 0x00),
		new(30, 8352, 0x07, 0xD6), new(30, 8361, 0x08, 0x2B), new(31, 8234, 0x07, 0xD6), new(31, 8243, 0x08, 0x2B),
		new(32, 8273, 0x08, 0x57), new(32, 8287, 0x07, 0xAC), new(32, 8342, 0x0B, 0x41), new(32, 8351, 0x09, 0xA0),
		new(32, 8360, 0x0A, 0x09), new(32, 8369, 0x0C, 0x04), new(32, 8378, 0x0D, 0x09), new(33, 8188, 0x08, 0x57),
		new(33, 8195, 0x0B, 0x80), new(33, 8256, 0x08, 0xAF), new(33, 8265, 0x07, 0x58), new(34, 8291, 0x08, 0x57),
		new(34, 8305, 0x0B, 0x40), new(34, 8360, 0x08, 0x57), new(34, 8369, 0x07, 0xAC), new(35, 8194, 0x08, 0x56),
		new(35, 8208, 0x0B, 0x40), new(35, 8269, 0x08, 0xAF), new(35, 8278, 0x07, 0x58), new(36, 8291, 0x08, 0x55),
		new(36, 8305, 0x0B, 0x40), new(36, 8360, 0x08, 0x57), new(36, 8369, 0x07, 0xAC), new(37, 8197, 0x0A, 0x0A),
		new(37, 8211, 0x09, 0x00), new(37, 8283, 0x08, 0x54), new(37, 8297, 0x0B, 0x40), new(37, 8358, 0x08, 0xAF),
		new(37, 8367, 0x07, 0x58), new(38, 8152, 0x0B, 0x40), new(38, 8159, 0x0C, 0x00), new(38, 8164, 0x0D, 0x00),
		new(38, 8334, 0x08, 0x57), new(38, 8343, 0x07, 0xAC), new(39, 8220, 0x08, 0xAF), new(39, 8229, 0x07, 0x58),
		new(40, 8250, 0x08, 0x41), new(40, 8264, 0x07, 0xB8), new(40, 8319, 0x0B, 0x41), new(40, 8328, 0x09, 0x00),
		new(40, 8337, 0x0A, 0x0A), new(40, 8346, 0x0C, 0x04), new(40, 8355, 0x0D, 0x09), new(41, 8189, 0x08, 0x41),
		new(41, 8196, 0x0B, 0x80), new(41, 8257, 0x08, 0x83), new(41, 8266, 0x07, 0x70), new(42, 8292, 0x08, 0x41),
		new(42, 8306, 0x0B, 0x40), new(42, 8361, 0x08, 0x41), new(42, 8370, 0x07, 0xB8), new(43, 8194, 0x08, 0x40),
		new(43, 8208, 0x0B, 0x40), new(43, 8269, 0x08, 0x83), new(43, 8278, 0x07, 0x70), new(44, 8291, 0x08, 0x3F),
		new(44, 8305, 0x0B, 0x40), new(44, 8360, 0x08, 0x41), new(44, 8369, 0x07, 0xB8), new(45, 8195, 0x08, 0x3E),
		new(45, 8209, 0x0B, 0x40), new(45, 8270, 0x08, 0x83), new(45, 8279, 0x07, 0x70), new(46, 8151, 0x0B, 0x40),
		new(46, 8158, 0x0C, 0x00), new(46, 8163, 0x0D, 0x00), new(46, 8333, 0x08, 0x41), new(46, 8342, 0x07, 0xB8),
		new(47, 8221, 0x08, 0x83), new(47, 8230, 0x07, 0x70), new(48, 8272, 0x08, 0x1D), new(48, 8286, 0x07, 0x46),
		new(48, 8341, 0x0B, 0x11), new(48, 8350, 0x09, 0x00), new(48, 8359, 0x0A, 0x08), new(48, 8368, 0x0C, 0x04),
		new(48, 8377, 0x0D, 0x0F), new(49, 8234, 0x07, 0x46), new(49, 8243, 0x08, 0x1D), new(49, 8321, 0x08, 0x1D),
		new(49, 8328, 0x0B, 0x80)
	};

	private static readonly Segment[] LastV8Segments =
	{
		new("tri/noise release", 0.02, 0.12, 0.90, 1.15),
		new("triangle retrigger", 0.16, 0.14, 0.90, 1.15),
		new("pulse block", 0.34, 0.12, 0.90, 1.15),
		new("second tri/noise release", 0.50, 0.12, 0.90, 1.15),
		new("second pulse block", 0.66, 0.16, 0.90, 1.15)
	};

	[Fact]
	public void OptionalLastV8Voice2TimedFixtureMatchesSidPlayFpLevelEnvelope()
	{
		if (Environment.GetEnvironmentVariable("SIDPLAYFP_ORACLE_TESTS") != "1")
		{
			return;
		}

		using var fixture = SidPlayFixture.Create(CreateLastV8Voice2TimedSid(), seconds: 1.0);
		var reference = fixture.RenderSidPlayFp();
		var player = RenderCopperMod(fixture.SidBytes, fixture.Seconds, ModuleRenderOutputMode.Player);
		var raw = RenderCopperMod(fixture.SidBytes, fixture.Seconds, ModuleRenderOutputMode.Raw);

		AssertSegmentsWithinRatio("Last V8 voice-2 timed", reference, player, LastV8Segments);
		Assert.All(raw, sample => Assert.True(float.IsFinite(sample)));
	}

	[Fact]
	public void OptionalLastV8DecompositionFixturesStayNearSidPlayFp()
	{
		if (Environment.GetEnvironmentVariable("SIDPLAYFP_ORACLE_TESTS") != "1")
		{
			return;
		}

		using var waveformFixture = SidPlayFixture.Create(CreateWaveformDecompositionSid(), seconds: 2.0);
		AssertSegmentsWithinRatio(
			"constant-envelope waveform decomposition",
			waveformFixture.RenderSidPlayFp(),
			RenderCopperMod(waveformFixture.SidBytes, waveformFixture.Seconds, ModuleRenderOutputMode.Player),
			new[]
			{
				new Segment("triangle", 0.18, 0.18, 0.75, 1.25),
				new Segment("pulse", 0.68, 0.18, 0.70, 1.25),
				new Segment("triangle+pulse", 1.18, 0.18, 0.75, 1.25),
				new Segment("noise", 1.68, 0.18, 0.75, 1.25)
			});

		using var envelopeFixture = SidPlayFixture.Create(CreateEnvelopeDecompositionSid(), seconds: 2.0);
		AssertSegmentsWithinRatio(
			"triangle envelope decomposition",
			envelopeFixture.RenderSidPlayFp(),
			RenderCopperMod(envelopeFixture.SidBytes, envelopeFixture.Seconds, ModuleRenderOutputMode.Player),
			new[]
			{
				new Segment("env-0", 0.08, 0.18, 0.85, 1.25),
				new Segment("env-1", 0.43, 0.18, 0.85, 1.25),
				new Segment("env-2", 0.78, 0.18, 0.85, 1.25),
				new Segment("env-3", 1.13, 0.18, 0.85, 1.25),
				new Segment("env-4", 1.48, 0.18, 0.85, 1.25)
			});
	}

	private static void AssertSegmentsWithinRatio(string fixtureName, float[] reference, float[] candidate, IReadOnlyList<Segment> segments)
	{
		var failures = new List<string>();
		var ratios = new List<string>();
		foreach (var segment in segments)
		{
			var start = SecondsToSamples(segment.StartSeconds);
			var length = SecondsToSamples(segment.DurationSeconds);
			Assert.True(reference.Length >= start + length, fixtureName + " reference is too short for " + segment.Name + ".");
			Assert.True(candidate.Length >= start + length, fixtureName + " candidate is too short for " + segment.Name + ".");

			var referenceRms = Rms(reference, start, length);
			var candidateRms = Rms(candidate, start, length);
			var ratio = candidateRms / Math.Max(1.0e-9, referenceRms);
			ratios.Add(segment.Name + "=" + ratio.ToString("0.000", CultureInfo.InvariantCulture));
			if (ratio < segment.MinimumRatio || ratio > segment.MaximumRatio)
			{
				failures.Add($"{segment.Name} RMS ratio {ratio:0.000} is outside {segment.MinimumRatio:0.000}..{segment.MaximumRatio:0.000} (reference {referenceRms:0.000000}, candidate {candidateRms:0.000000})");
			}
		}

		Assert.True(failures.Count == 0, fixtureName + " failed: " + string.Join("; ", failures) + ". All ratios: " + string.Join(", ", ratios) + ".");
	}

	private static byte[] CreateLastV8Voice2TimedSid()
	{
		var frames = new TimedWrite[50][];
		for (var i = 0; i < frames.Length; i++)
		{
			frames[i] = LastV8Voice2Writes.Where(write => write.Frame == i).OrderBy(write => write.Cycle).ToArray();
		}

		var asm = CreateInitializedDispatcher("Last V8 Voice2 Timed", frames.Length);
		for (var frame = 0; frame < frames.Length; frame++)
		{
			asm.Label("frame" + frame.ToString(CultureInfo.InvariantCulture));
			var elapsed = 0;
			foreach (var write in frames[frame])
			{
				var targetCycle = Math.Max(0, write.Cycle - 32);
				elapsed += asm.BurnCycles(Math.Max(0, targetCycle - elapsed));
				elapsed += EmitSidWrite(asm, write.Register, write.Value);
			}

			asm.Rts();
		}

		return CreatePsid(asm, "Last V8 Voice2 Timed");
	}

	private static byte[] CreateWaveformDecompositionSid()
	{
		Action<Mos6510Emitter>[] frames =
		{
			asm => EmitVoice2(asm, frequency: 0x3000, pulseWidth: 0x0800, control: 0x11, attackDecay: 0x00, sustainRelease: 0xF0),
			asm => EmitVoice2(asm, frequency: 0x3000, pulseWidth: 0x0800, control: 0x41, attackDecay: 0x00, sustainRelease: 0xF0),
			asm => EmitVoice2(asm, frequency: 0x3000, pulseWidth: 0x0800, control: 0x51, attackDecay: 0x00, sustainRelease: 0xF0),
			asm => EmitVoice2(asm, frequency: 0x3000, pulseWidth: 0x0800, control: 0x81, attackDecay: 0x00, sustainRelease: 0xF0)
		};

		return CreateSegmentedFixture(frames, framesPerSegment: 25, "Last V8 Wave Decomp");
	}

	private static byte[] CreateEnvelopeDecompositionSid()
	{
		Action<Mos6510Emitter>[] frames =
		{
			asm => EmitVoice2(asm, frequency: 0x3A8C, pulseWidth: 0x0800, control: 0x11, attackDecay: 0x04, sustainRelease: 0x0F),
			asm => EmitSidWrite(asm, 0x0B, 0x80),
			asm => EmitSidWrite(asm, 0x0B, 0x10),
			asm => EmitSidWrite(asm, 0x0B, 0x10),
			asm => EmitSidWrite(asm, 0x0B, 0x10),
			asm => EmitSidWrite(asm, 0x0B, 0x10)
		};

		var expanded = new Action<Mos6510Emitter>[50];
		for (var i = 0; i < expanded.Length; i++)
		{
			expanded[i] = frames[i % frames.Length];
		}

		return CreateFrameFixture(expanded, "Last V8 Env Decomp");
	}

	private static byte[] CreateSegmentedFixture(IReadOnlyList<Action<Mos6510Emitter>> segments, int framesPerSegment, string title)
	{
		var frames = new Action<Mos6510Emitter>[segments.Count * framesPerSegment];
		for (var segment = 0; segment < segments.Count; segment++)
		{
			frames[segment * framesPerSegment] = segments[segment];
			for (var i = 1; i < framesPerSegment; i++)
			{
				frames[(segment * framesPerSegment) + i] = _ => { };
			}
		}

		return CreateFrameFixture(frames, title);
	}

	private static byte[] CreateFrameFixture(IReadOnlyList<Action<Mos6510Emitter>> frames, string title)
	{
		var asm = CreateInitializedDispatcher(title, frames.Count);
		for (var frame = 0; frame < frames.Count; frame++)
		{
			asm.Label("frame" + frame.ToString(CultureInfo.InvariantCulture));
			frames[frame](asm);
			asm.Rts();
		}

		return CreatePsid(asm, title);
	}

	private static Mos6510Emitter CreateInitializedDispatcher(string title, int frameCount)
	{
		var asm = new Mos6510Emitter(ProgramBase);
		asm.Label("init");
		asm.LdxImm(0x18);
		asm.LdaImm(0);
		asm.Label("clear");
		asm.StaAbsX(SidBase);
		asm.Dex();
		asm.Bpl("clear");
		EmitSidWrite(asm, 0x0B, 0x08);
		EmitSidWrite(asm, 0x0B, 0x00);
		asm.LdaImm(0);
		asm.StaZp(FrameCounterAddress);
		EmitSidWrite(asm, 0x18, 0x0F);
		asm.Rts();

		asm.Label("play");
		asm.LdaZp(FrameCounterAddress);
		asm.CmpImm((byte)frameCount);
		asm.Bcc("frame-in-range");
		asm.LdaImm(0);
		asm.StaZp(FrameCounterAddress);
		asm.Label("frame-in-range");
		asm.AslA();
		asm.Tax();
		asm.LdaAbsX("frame-table");
		asm.StaZp(PointerAddress);
		asm.LdaAbsX("frame-table", 1);
		asm.StaZp(PointerAddress + 1);
		asm.IncZp(FrameCounterAddress);
		asm.JmpIndirectZp(PointerAddress);

		asm.Label("frame-table");
		for (var i = 0; i < frameCount; i++)
		{
			asm.WordLabel("frame" + i.ToString(CultureInfo.InvariantCulture));
		}

		_ = title;
		return asm;
	}

	private static void EmitVoice2(Mos6510Emitter asm, ushort frequency, ushort pulseWidth, byte control, byte attackDecay, byte sustainRelease)
	{
		EmitSidWrite(asm, 0x07, (byte)frequency);
		EmitSidWrite(asm, 0x08, (byte)(frequency >> 8));
		EmitSidWrite(asm, 0x09, (byte)pulseWidth);
		EmitSidWrite(asm, 0x0A, (byte)(pulseWidth >> 8));
		EmitSidWrite(asm, 0x0C, attackDecay);
		EmitSidWrite(asm, 0x0D, sustainRelease);
		EmitSidWrite(asm, 0x0B, control);
	}

	private static int EmitSidWrite(Mos6510Emitter asm, byte register, byte value)
	{
		asm.LdaImm(value);
		asm.StaAbs(SidBase + register);
		return 6;
	}

	private static byte[] CreatePsid(Mos6510Emitter asm, string title)
	{
		var program = asm.ToArray();
		var data = new byte[0x7C + program.Length];
		WriteAscii(data, 0, "PSID");
		WriteBigEndian(data, 4, (ushort)2);
		WriteBigEndian(data, 6, (ushort)0x7C);
		WriteBigEndian(data, 8, ProgramBase);
		WriteBigEndian(data, 0x0A, ProgramBase);
		WriteBigEndian(data, 0x0C, asm.AddressOf("play"));
		WriteBigEndian(data, 0x0E, (ushort)1);
		WriteBigEndian(data, 0x10, (ushort)1);
		WriteBigEndian(data, 0x12, 0U);
		WriteFixed(data, 0x16, title);
		WriteFixed(data, 0x36, "CopperMod");
		WriteFixed(data, 0x56, "2026");
		WriteBigEndian(data, 0x76, (ushort)((1 << 2) | (1 << 4)));
		program.CopyTo(data, 0x7C);
		return data;
	}

	private static float[] RenderCopperMod(byte[] sidBytes, double seconds, ModuleRenderOutputMode outputMode)
	{
		using var song = new SidFormat().Load(sidBytes);
		((ISidEmulationProfileController)song).SidEmulationProfile = SidEmulationProfile.Balanced;
		var settings = new ModuleRenderSettings(
			sampleRate: SampleRate,
			channelCount: 1,
			outputMode: outputMode,
			c64OutputProfile: C64OutputProfile.C64);
		using var renderer = new ModulePcmRenderer(song, settings);
		var samples = new float[SecondsToSamples(seconds)];
		var written = renderer.Read(samples);
		if (written < samples.Length)
		{
			Array.Clear(samples, written, samples.Length - written);
		}

		return samples;
	}

	private static double Rms(float[] samples, int start, int length)
	{
		var sum = 0.0;
		for (var i = 0; i < length; i++)
		{
			var sample = samples[start + i];
			sum += sample * sample;
		}

		return Math.Sqrt(sum / length);
	}

	private static int SecondsToSamples(double seconds)
		=> (int)Math.Round(seconds * SampleRate);

	private static void WriteAscii(byte[] data, int offset, string value)
	{
		for (var i = 0; i < value.Length; i++)
		{
			data[offset + i] = (byte)value[i];
		}
	}

	private static void WriteFixed(byte[] data, int offset, string value)
	{
		var count = Math.Min(31, value.Length);
		for (var i = 0; i < count; i++)
		{
			data[offset + i] = (byte)value[i];
		}
	}

	private static void WriteBigEndian(byte[] data, int offset, ushort value)
		=> BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset, 2), value);

	private static void WriteBigEndian(byte[] data, int offset, uint value)
		=> BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, 4), value);

	private readonly record struct TimedWrite(int Frame, int Cycle, byte Register, byte Value);

	private readonly record struct Segment(string Name, double StartSeconds, double DurationSeconds, double MinimumRatio, double MaximumRatio);

	private sealed class SidPlayFixture : IDisposable
	{
		private readonly string _root;

		private SidPlayFixture(byte[] sidBytes, double seconds)
		{
			SidBytes = sidBytes;
			Seconds = seconds;
			_root = Path.Combine(Path.GetTempPath(), "coppermod-lastv8-sidplayfp-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_root);
			SidPath = Path.Combine(_root, "input.sid");
			File.WriteAllBytes(SidPath, sidBytes);
		}

		public byte[] SidBytes { get; }

		public double Seconds { get; }

		public string SidPath { get; }

		public static SidPlayFixture Create(byte[] sidBytes, double seconds)
			=> new SidPlayFixture(sidBytes, seconds);

		public float[] RenderSidPlayFp()
		{
			var sidPlayFp = Environment.GetEnvironmentVariable("SIDPLAYFP_EXE");
			if (string.IsNullOrWhiteSpace(sidPlayFp))
			{
				sidPlayFp = @"D:\Models\sidplayfp-3.0.2-ucrt64\sidplayfp.exe";
			}

			Assert.True(File.Exists(sidPlayFp), "SidPlayFP executable was not found: " + sidPlayFp);

			var startInfo = new ProcessStartInfo
			{
				FileName = sidPlayFp,
				WorkingDirectory = _root,
				UseShellExecute = false,
				RedirectStandardError = true,
				RedirectStandardOutput = true
			};
			startInfo.ArgumentList.Add("--residfp");
			startInfo.ArgumentList.Add("-cwa");
			startInfo.ArgumentList.Add("-vpf");
			startInfo.ArgumentList.Add("-mof");
			startInfo.ArgumentList.Add("-f" + SampleRate.ToString(CultureInfo.InvariantCulture));
			startInfo.ArgumentList.Add("-p32");
			startInfo.ArgumentList.Add("-t" + FormatSidPlayFpDuration(Seconds));
			startInfo.ArgumentList.Add("-winput");
			startInfo.ArgumentList.Add(SidPath);

			using var process = Process.Start(startInfo);
			Assert.NotNull(process);
			var stdout = process.StandardOutput.ReadToEnd();
			var stderr = process.StandardError.ReadToEnd();
			Assert.True(process.WaitForExit(30_000), "SidPlayFP did not exit within 30 seconds.");
			Assert.True(process.ExitCode == 0, "SidPlayFP failed.\nSTDOUT:\n" + stdout + "\nSTDERR:\n" + stderr);

			return MeasurementWav.Read(Path.Combine(_root, "input.wav"));
		}

		public void Dispose()
		{
			if (Directory.Exists(_root))
			{
				Directory.Delete(_root, recursive: true);
			}
		}

		private static string FormatSidPlayFpDuration(double seconds)
		{
			var duration = TimeSpan.FromSeconds(seconds);
			return ((int)duration.TotalMinutes).ToString(CultureInfo.InvariantCulture) +
				":" +
				duration.Seconds.ToString("00", CultureInfo.InvariantCulture) +
				"." +
				duration.Milliseconds.ToString("000", CultureInfo.InvariantCulture);
		}
	}

	private sealed class Mos6510Emitter
	{
		private readonly ushort _baseAddress;
		private readonly List<byte> _bytes = new();
		private readonly Dictionary<string, ushort> _labels = new(StringComparer.Ordinal);
		private readonly List<Patch> _patches = new();

		public Mos6510Emitter(ushort baseAddress)
		{
			_baseAddress = baseAddress;
		}

		public void Label(string name)
			=> _labels[name] = CurrentAddress;

		public ushort AddressOf(string name)
			=> _labels[name];

		public void LdaImm(byte value) => Emit(0xA9, value);

		public void LdxImm(byte value) => Emit(0xA2, value);

		public void LdaZp(byte address) => Emit(0xA5, address);

		public void LdaAbsX(string label, int addend = 0)
		{
			Emit(0xBD);
			_patches.Add(new Patch(_bytes.Count, label, addend, PatchKind.Absolute, CurrentAddress));
			EmitWord(0);
		}

		public void StaZp(int address) => Emit(0x85, (byte)address);

		public void IncZp(byte address) => Emit(0xE6, address);

		public void CmpImm(byte value) => Emit(0xC9, value);

		public void AslA() => Emit(0x0A);

		public void Tax() => Emit(0xAA);

		public void Dex() => Emit(0xCA);

		public void Nop() => Emit(0xEA);

		public void BitZp(byte address) => Emit(0x24, address);

		public void Rts() => Emit(0x60);

		public void StaAbs(int address)
		{
			Emit(0x8D);
			EmitWord((ushort)address);
		}

		public void StaAbsX(ushort address)
		{
			Emit(0x9D);
			EmitWord(address);
		}

		public void Bcc(string label) => Branch(0x90, label);

		public void Bne(string label) => Branch(0xD0, label);

		public void Bpl(string label) => Branch(0x10, label);

		public void JmpIndirectZp(byte address) => Emit(0x6C, address, 0x00);

		public void WordLabel(string label)
		{
			_patches.Add(new Patch(_bytes.Count, label, 0, PatchKind.Absolute, CurrentAddress));
			EmitWord(0);
		}

		public int BurnCycles(int cycles)
		{
			var burned = 0;
			while (cycles > 20)
			{
				var maxIterations = Math.Min(255, (cycles - 2) / 5);
				var iterations = 0;
				for (var candidate = maxIterations; candidate > 1; candidate--)
				{
					var remainder = cycles - ((5 * candidate) + 1);
					if (remainder >= 0 && remainder != 1)
					{
						iterations = candidate;
						break;
					}
				}

				if (iterations == 0)
				{
					break;
				}

				var label = "delay" + _bytes.Count.ToString(CultureInfo.InvariantCulture);
				LdxImm((byte)iterations);
				Label(label);
				Dex();
				Bne(label);
				var loopCycles = (5 * iterations) + 1;
				cycles -= loopCycles;
				burned += loopCycles;
			}

			if (cycles == 1)
			{
				Nop();
				return burned + 2;
			}

			while (cycles >= 3 && (cycles & 1) != 0)
			{
				BitZp(FrameCounterAddress);
				cycles -= 3;
				burned += 3;
			}

			while (cycles >= 2)
			{
				Nop();
				cycles -= 2;
				burned += 2;
			}

			return burned;
		}

		public byte[] ToArray()
		{
			foreach (var patch in _patches)
			{
				var target = (ushort)(AddressOf(patch.Label) + patch.Addend);
				if (patch.Kind == PatchKind.Relative)
				{
					var delta = target - patch.NextInstructionAddress;
					if (delta < sbyte.MinValue || delta > sbyte.MaxValue)
					{
						throw new InvalidOperationException("Branch to " + patch.Label + " is outside 6502 relative range.");
					}

					_bytes[patch.Offset] = unchecked((byte)(sbyte)delta);
				}
				else
				{
					_bytes[patch.Offset] = (byte)target;
					_bytes[patch.Offset + 1] = (byte)(target >> 8);
				}
			}

			return _bytes.ToArray();
		}

		private ushort CurrentAddress => (ushort)(_baseAddress + _bytes.Count);

		private void Branch(byte opcode, string label)
		{
			Emit(opcode);
			_patches.Add(new Patch(_bytes.Count, label, 0, PatchKind.Relative, (ushort)(CurrentAddress + 1)));
			Emit(0);
		}

		private void Emit(params byte[] values)
			=> _bytes.AddRange(values);

		private void EmitWord(ushort value)
		{
			_bytes.Add((byte)value);
			_bytes.Add((byte)(value >> 8));
		}

		private readonly record struct Patch(int Offset, string Label, int Addend, PatchKind Kind, ushort NextInstructionAddress);

		private enum PatchKind
		{
			Absolute,
			Relative
		}
	}

	private static class MeasurementWav
	{
		public static float[] Read(string path)
		{
			using var stream = File.OpenRead(path);
			using var reader = new BinaryReader(stream);
			if (new string(reader.ReadChars(4)) != "RIFF")
			{
				throw new InvalidDataException("Only RIFF WAV files are supported.");
			}

			_ = reader.ReadUInt32();
			if (new string(reader.ReadChars(4)) != "WAVE")
			{
				throw new InvalidDataException("Only WAVE files are supported.");
			}

			WavFormat? format = null;
			byte[]? data = null;
			while (stream.Position + 8 <= stream.Length)
			{
				var id = new string(reader.ReadChars(4));
				var size = reader.ReadUInt32();
				var chunkStart = stream.Position;
				if (id == "fmt ")
				{
					format = WavFormat.Parse(reader.ReadBytes(checked((int)size)));
				}
				else if (id == "data")
				{
					data = reader.ReadBytes(checked((int)size));
				}

				stream.Position = chunkStart + size + (size & 1);
			}

			if (format == null || data == null)
			{
				throw new InvalidDataException("WAV file is missing fmt or data chunk.");
			}

			return DecodeMono(format, data);
		}

		private static float[] DecodeMono(WavFormat format, byte[] data)
		{
			var frameCount = data.Length / format.BlockAlign;
			var samples = new float[frameCount];
			for (var frame = 0; frame < frameCount; frame++)
			{
				var frameOffset = frame * format.BlockAlign;
				var sum = 0.0;
				for (var channel = 0; channel < format.Channels; channel++)
				{
					var sampleOffset = frameOffset + (channel * format.BytesPerSample);
					sum += DecodeSample(format, data.AsSpan(sampleOffset, format.BytesPerSample));
				}

				samples[frame] = (float)(sum / format.Channels);
			}

			return samples;
		}

		private static double DecodeSample(WavFormat format, ReadOnlySpan<byte> bytes)
		{
			if (format.AudioFormat == 3 && format.BitsPerSample == 32)
			{
				return BinaryPrimitives.ReadSingleLittleEndian(bytes);
			}

			if (format.AudioFormat == 1 && format.BitsPerSample == 16)
			{
				return BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768.0;
			}

			throw new InvalidDataException("Unsupported WAV format.");
		}
	}

	private sealed record WavFormat(int AudioFormat, int Channels, int SampleRate, int BlockAlign, int BitsPerSample)
	{
		public int BytesPerSample => BitsPerSample / 8;

		public static WavFormat Parse(byte[] bytes)
		{
			if (bytes.Length < 16)
			{
				throw new InvalidDataException("WAV fmt chunk is too short.");
			}

			return new WavFormat(
				BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0, 2)),
				BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2, 2)),
				BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4)),
				BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(12, 2)),
				BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(14, 2)));
		}
	}
}

[CollectionDefinition("SidPlayFP oracle", DisableParallelization = true)]
public sealed class SidPlayFpOracleCollection
{
}
