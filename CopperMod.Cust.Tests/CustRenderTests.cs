using CopperMod.Abstractions;
using CopperMod.Cust;

namespace CopperMod.Cust.Tests;

public sealed class CustRenderTests
{
	[Fact]
	public void AlteredBeastFixtureIsRecognizedAndLoadsMetadata()
	{
		var data = File.ReadAllBytes(HunkParserTests.FindWorkspaceFile("TestTunes", "Amiga.CUST", "AlteredBeast.CUST"));
		var format = new CustFormat();

		Assert.True(format.CanLoad(data));
		using var song = format.Load(data);

		Assert.Equal("Amiga CUST", song.Metadata.FormatName);
		Assert.Equal(4, song.Metadata.ChannelCount);
		Assert.Contains("AlteredBeast", song.Metadata.Title);
	}

	[Fact]
	public void AlteredBeastFixtureRendersFinitePcmAndTimedRegisterWrites()
	{
		using var song = (CustSong)new CustFormat().Load(File.ReadAllBytes(HunkParserTests.FindWorkspaceFile("TestTunes", "Amiga.CUST", "AlteredBeast.CUST")));
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		var frames = song.GetCurrentTickFrameCount(options);
		var buffer = new float[options.GetSampleCount(frames)];

		RenderResult result = default;
		for (var tick = 0; tick < 100; tick++)
		{
			result = song.RenderTick(buffer, options);
			if (buffer.Select(Math.Abs).DefaultIfEmpty().Max() > 0.0001f)
			{
				break;
			}
		}

		Assert.Equal(frames, result.FramesWritten);
		Assert.Equal(buffer.Length, result.SamplesWritten);
		Assert.All(buffer, sample => Assert.True(float.IsFinite(sample)));
		var peak = buffer.Select(Math.Abs).DefaultIfEmpty().Max();
		Assert.True(peak > 0.0001f, "CUST render stayed silent after 100 ticks. Writes: " + SummarizeWrites(song.CustomRegisterWrites) + ". Diagnostics: " + string.Join(", ", song.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
		Assert.Contains(song.CustomRegisterWrites, write => write.Address == 0x096);
		Assert.Equal(song.CustomRegisterWrites.OrderBy(write => write.Cycle).Select(write => write.Cycle), song.CustomRegisterWrites.Select(write => write.Cycle));
	}

	[Fact]
	public void ChannelWaveformCaptureExposesFourPaulaChannels()
	{
		using var song = (CustSong)new CustFormat().Load(File.ReadAllBytes(HunkParserTests.FindWorkspaceFile("TestTunes", "Amiga.CUST", "AlteredBeast.CUST")));
		var provider = (IModuleChannelWaveformProvider)song;
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		var buffer = new float[options.GetSampleCount(song.GetCurrentTickFrameCount(options))];

		provider.ChannelWaveformCaptureEnabled = true;
		song.RenderTick(buffer, options);

		Assert.NotNull(provider.LastChannelWaveform);
		Assert.Equal(4, provider.LastChannelWaveform!.Channels.Count);
	}

	[Fact]
	public void BatmanTitleLoadsWithoutInitFreezeAndRendersAudio()
	{
		using var song = (CustSong)new CustFormat().Load(File.ReadAllBytes(HunkParserTests.FindWorkspaceFile("TestTunes", "Amiga.CUST", "Batman_The_Movie-Title.CUST")));
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		var buffer = new float[options.GetSampleCount(song.GetCurrentTickFrameCount(options))];
		var peak = 0.0f;
		for (var i = 0; i < 200; i++)
		{
			song.RenderTick(buffer, options);
			peak = Math.Max(peak, buffer.Select(Math.Abs).DefaultIfEmpty().Max());
		}

		Assert.All(buffer, sample => Assert.True(float.IsFinite(sample)));
		Assert.True(peak > 0.0001f, "Batman title stayed silent. Diagnostics: " + string.Join(", ", song.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)) + " writes=" + SummarizeWrites(song.CustomRegisterWrites));
		Assert.DoesNotContain(song.Diagnostics, diagnostic => diagnostic.Code == "CUST_CPU_FAULT");
	}

	[Fact]
	public void EliteFixtureRendersAudio()
	{
		using var song = (CustSong)new CustFormat().Load(File.ReadAllBytes(HunkParserTests.FindWorkspaceFile("TestTunes", "Amiga.CUST", "Elite.CUST")));
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		var buffer = new float[options.GetSampleCount(song.GetCurrentTickFrameCount(options))];
		var peak = 0.0f;
		for (var i = 0; i < 200; i++)
		{
			song.RenderTick(buffer, options);
			peak = Math.Max(peak, buffer.Select(Math.Abs).DefaultIfEmpty().Max());
		}

		Assert.All(buffer, sample => Assert.True(float.IsFinite(sample)));
		Assert.True(peak > 0.0001f, "Elite stayed silent. Diagnostics: " + string.Join(", ", song.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)) + " writes=" + SummarizeWrites(song.CustomRegisterWrites));
		Assert.DoesNotContain(song.Diagnostics, diagnostic => diagnostic.Code == "CUST_UNSUPPORTED_OPCODE");
		Assert.DoesNotContain(song.Diagnostics, diagnostic => diagnostic.Code == "CUST_CPU_FAULT");
	}

	[Fact]
	public void LongInitPlayerIsAbortedByWatchdog()
	{
		var data = CreateLoopingInitPlayerHunk();
		var hunk = HunkParser.Parse(data);
		Assert.True(DeliTagParser.TryFindTags(hunk, out var tags));

		var started = DateTime.UtcNow;
		var machine = new CustMachine(hunk, tags);

		Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
		Assert.Contains(machine.Diagnostics, diagnostic => diagnostic.Code == "CUST_CPU_OVERRUN");
	}

	private static string SummarizeWrites(IReadOnlyList<CustomRegisterWrite> writes)
	{
		return "count=" + writes.Count + " " + string.Join(", ", writes.TakeLast(Math.Min(24, writes.Count)).Select(write => $"@{write.Cycle}:{write.Address:X3}={write.Value:X4}"));
	}

	private static byte[] CreateLoopingInitPlayerHunk()
	{
		var bytes = new byte[0x30];
		WriteLong(bytes, 0x00, CustConstants.DtpPlayerVersion);
		WriteLong(bytes, 0x04, 1);
		WriteLong(bytes, 0x08, CustConstants.DtpInitPlayer);
		WriteLong(bytes, 0x0C, CustConstants.DefaultModuleBaseAddress + 0x28);
		WriteLong(bytes, 0x10, CustConstants.DtpInitSound);
		WriteLong(bytes, 0x14, CustConstants.DefaultModuleBaseAddress + 0x2C);
		WriteLong(bytes, 0x18, CustConstants.DtpInterrupt);
		WriteLong(bytes, 0x1C, CustConstants.DefaultModuleBaseAddress + 0x2C);
		WriteLong(bytes, 0x20, CustConstants.TagDone);
		bytes[0x28] = 0x60;
		bytes[0x29] = 0xFE;
		bytes[0x2C] = 0x4E;
		bytes[0x2D] = 0x75;

		var words = new List<uint>
		{
			HunkParser.HunkHeader,
			0,
			1,
			0,
			0,
			(uint)(bytes.Length / 4),
			HunkParser.HunkCode,
			(uint)(bytes.Length / 4)
		};
		var result = new byte[(words.Count * 4) + bytes.Length + 4];
		for (var i = 0; i < words.Count; i++)
		{
			WriteLong(result, i * 4, words[i]);
		}

		Array.Copy(bytes, 0, result, words.Count * 4, bytes.Length);
		WriteLong(result, (words.Count * 4) + bytes.Length, HunkParser.HunkEnd);
		return result;
	}

	private static void WriteLong(byte[] bytes, int offset, uint value)
	{
		bytes[offset] = (byte)(value >> 24);
		bytes[offset + 1] = (byte)(value >> 16);
		bytes[offset + 2] = (byte)(value >> 8);
		bytes[offset + 3] = (byte)value;
	}
}
