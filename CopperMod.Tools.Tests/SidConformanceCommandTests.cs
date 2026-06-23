using System.Buffers.Binary;
using CopperMod.Tools;

namespace CopperMod.Tools.Tests;

public sealed class SidConformanceCommandTests
{
	[Fact]
	public void CompareSidConformanceRendersCandidatesAndReusesReferenceWavs()
	{
		using var temp = TemporaryDirectory.Create();
		var fixtureRoot = FindWorkspaceFile("CopperMod.Sid.Tests", "ConformanceFixtures");
		var referenceDirectory = Path.Combine(temp.Path, "ref");
		var outputDirectory = Path.Combine(temp.Path, "out");
		Directory.CreateDirectory(referenceDirectory);
		var suite = SidConformance.LoadSuite(fixtureRoot);
		foreach (var fixture in suite.Fixtures)
		{
			WriteFloatWav(Path.Combine(referenceDirectory, fixture.Id + "-ref.wav"), 48000, CreateAlternatingSamples(120_000, 0.5f));
		}

		using var stdout = new StringWriter();
		using var stderr = new StringWriter();
		var exitCode = CopperModTools.Run(
			new[]
			{
				SidConformance.CommandName,
				fixtureRoot,
				"--reference-dir",
				referenceDirectory,
				"--out",
				outputDirectory,
				"--overwrite-candidate"
			},
			stdout,
			stderr);

		Assert.Equal(0, exitCode);
		Assert.Empty(stderr.ToString());
		Assert.Contains("id,category,name,output,ref_ac,cand_ac,ac_ratio,diff,corr", stdout.ToString());
		Assert.True(File.Exists(Path.Combine(outputDirectory, "conformance-comparison.csv")));
		Assert.True(File.Exists(Path.Combine(outputDirectory, "conformance-segments.csv")));
		Assert.True(File.Exists(Path.Combine(outputDirectory, "conformance-adsr-trace.csv")));
		Assert.True(File.Exists(Path.Combine(outputDirectory, "conformance-adsr-pulses.csv")));
		Assert.True(File.Exists(Path.Combine(outputDirectory, "conformance-filter-bands.csv")));
		Assert.True(File.Exists(Path.Combine(outputDirectory, "conformance-waveform-edges.csv")));
		Assert.True(File.Exists(Path.Combine(outputDirectory, "index.html")));
		Assert.True(Directory.GetFiles(Path.Combine(outputDirectory, "waveforms"), "*-candidate.png").Length >= suite.Fixtures.Count);
		Assert.True(Directory.GetFiles(Path.Combine(outputDirectory, "waveforms"), "*-raw.png").Length >= suite.Fixtures.Count);
		Assert.True(Directory.GetFiles(Path.Combine(outputDirectory, "coppermod"), "*-player.wav").Length >= suite.Fixtures.Count);
		Assert.True(Directory.GetFiles(Path.Combine(outputDirectory, "coppermod"), "*-raw.wav").Length >= suite.Fixtures.Count);
		Assert.Contains(
			"id,category,name,segment_index,segment,start_ms,end_ms,ref_mean,ref_ac,ref_peak,cand_player_mean,cand_player_ac,cand_player_peak,player_ratio,player_diff,player_corr,cand_raw_mean,cand_raw_ac,cand_raw_peak",
			File.ReadAllText(Path.Combine(outputDirectory, "conformance-segments.csv")));
		Assert.Contains(
			"id,category,name,time_ms,frame,gate,alignment_offset_samples,ref_ac,cand_player_ac",
			File.ReadAllText(Path.Combine(outputDirectory, "conformance-adsr-trace.csv")));
		Assert.Contains(
			"id,category,name,segment_index,segment,start_ms,end_ms,ref_low,ref_mid,ref_high",
			File.ReadAllText(Path.Combine(outputDirectory, "conformance-filter-bands.csv")));
		Assert.Contains(
			"id,category,name,segment_index,segment,start_ms,end_ms,ref_ac,cand_player_ac",
			File.ReadAllText(Path.Combine(outputDirectory, "conformance-waveform-edges.csv")));
		var index = File.ReadAllText(Path.Combine(outputDirectory, "index.html"));
		Assert.Contains("conformance-adsr-trace.csv", index);
		Assert.Contains("conformance-filter-bands.csv", index);
		Assert.Contains("conformance-waveform-edges.csv", index);
	}

	[Fact]
	public void CompareSidConformanceRejectsSilentReferences()
	{
		using var temp = TemporaryDirectory.Create();
		var fixtureRoot = FindWorkspaceFile("CopperMod.Sid.Tests", "ConformanceFixtures");
		var referenceDirectory = Path.Combine(temp.Path, "ref");
		var outputDirectory = Path.Combine(temp.Path, "out");
		Directory.CreateDirectory(referenceDirectory);
		var suite = SidConformance.LoadSuite(fixtureRoot);
		foreach (var fixture in suite.Fixtures)
		{
			WriteFloatWav(Path.Combine(referenceDirectory, fixture.Id + "-ref.wav"), 48000, new float[120_000]);
		}

		using var stdout = new StringWriter();
		using var stderr = new StringWriter();
		var exitCode = CopperModTools.Run(
			new[]
			{
				SidConformance.CommandName,
				fixtureRoot,
				"--reference-dir",
				referenceDirectory,
				"--out",
				outputDirectory,
				"--overwrite-candidate"
			},
			stdout,
			stderr);

		Assert.Equal(1, exitCode);
		Assert.Contains("appears silent", stderr.ToString());
	}

	[Fact]
	public void CompareSidConformanceCanRunWithoutReferences()
	{
		using var temp = TemporaryDirectory.Create();
		var fixtureRoot = FindWorkspaceFile("CopperMod.Sid.Tests", "ConformanceFixtures");
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exitCode = CopperModTools.Run(
			new[]
			{
				SidConformance.CommandName,
				fixtureRoot,
				"--out",
				Path.Combine(temp.Path, "out"),
				"--overwrite-candidate"
			},
			stdout,
			stderr);

		Assert.Equal(0, exitCode);
		Assert.Empty(stderr.ToString());
		Assert.Contains("sidtest5-01-basic-waveforms,1,BASIC WAVEFORMS,player,", stdout.ToString());
	}

	private static void WriteFloatWav(string path, int sampleRate, IReadOnlyList<float> samples)
	{
		using var stream = File.Create(path);
		using var writer = new BinaryWriter(stream);
		var dataBytes = samples.Count * sizeof(float);
		writer.Write("RIFF"u8);
		writer.Write(36 + dataBytes);
		writer.Write("WAVE"u8);
		writer.Write("fmt "u8);
		writer.Write(16);
		writer.Write((short)3);
		writer.Write((short)1);
		writer.Write(sampleRate);
		writer.Write(sampleRate * sizeof(float));
		writer.Write((short)sizeof(float));
		writer.Write((short)32);
		writer.Write("data"u8);
		writer.Write(dataBytes);
		var sampleBytes = new byte[sizeof(float)];
		foreach (var sample in samples)
		{
			BinaryPrimitives.WriteSingleLittleEndian(sampleBytes, sample);
			writer.Write(sampleBytes);
		}
	}

	private static float[] CreateAlternatingSamples(int count, float amplitude)
	{
		var samples = new float[count];
		for (var i = 0; i < samples.Length; i++)
		{
			samples[i] = (i & 1) == 0 ? -amplitude : amplitude;
		}

		return samples;
	}

	private static string FindWorkspaceFile(params string[] parts)
	{
		var directory = AppContext.BaseDirectory;
		while (!string.IsNullOrWhiteSpace(directory))
		{
			var candidate = Path.Combine(new[] { directory }.Concat(parts).ToArray());
			if (File.Exists(candidate) || Directory.Exists(candidate))
			{
				return candidate;
			}

			directory = Directory.GetParent(directory)?.FullName;
		}

		throw new FileNotFoundException("Could not find workspace path: " + Path.Combine(parts));
	}
}
