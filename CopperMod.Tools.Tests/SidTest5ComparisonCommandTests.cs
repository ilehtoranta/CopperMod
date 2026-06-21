using System.Buffers.Binary;

namespace CopperMod.Tools.Tests;

public sealed class SidTest5ComparisonCommandTests
{
	[Fact]
	public void CompareSidTest5ReusesExistingReferenceAndCandidateWavs()
	{
		using var temp = TemporaryDirectory.Create();
		var prgDirectory = Path.Combine(temp.Path, "prg");
		var referenceDirectory = Path.Combine(temp.Path, "ref");
		var candidateDirectory = Path.Combine(temp.Path, "candidate");
		var outputDirectory = Path.Combine(temp.Path, "out");
		Directory.CreateDirectory(prgDirectory);
		Directory.CreateDirectory(referenceDirectory);
		Directory.CreateDirectory(candidateDirectory);
		for (var test = 1; test <= 14; test++)
		{
			File.WriteAllBytes(Path.Combine(prgDirectory, "sidtest5-test" + test.ToString("00") + ".prg"), new byte[] { 0x01, 0x08, 0x00, 0x00 });
			WriteFloatWav(Path.Combine(referenceDirectory, "test" + test.ToString("00") + "-ref.wav"), new[] { -0.5f, 0.5f, -0.5f, 0.5f });
			WriteFloatWav(Path.Combine(candidateDirectory, "test" + test.ToString("00") + "-player.wav"), new[] { -0.25f, 0.25f, -0.25f, 0.25f });
		}

		using var stdout = new StringWriter();
		using var stderr = new StringWriter();
		var exitCode = CopperModTools.Run(
			new[]
			{
				"compare-sidtest5",
				prgDirectory,
				"--reference-dir",
				referenceDirectory,
				"--candidate-dir",
				candidateDirectory,
				"--out",
				outputDirectory
			},
			stdout,
			stderr);

		Assert.Equal(0, exitCode);
		Assert.Empty(stderr.ToString());
		Assert.Contains("test,name,ref_ac,cand_ac,ac_ratio,diff,corr", stdout.ToString());
		Assert.Contains("summary,median_ratio=0.5", stdout.ToString());
		var csv = File.ReadAllText(Path.Combine(outputDirectory, "sidtest5-comparison.csv"));
		Assert.Contains("1,BASIC WAVEFORMS,0.5,0.25,0.5,0.25,1", csv);
	}

	[Fact]
	public void CompareSidTest5RequiresReferenceSourceForMissingReferences()
	{
		using var temp = TemporaryDirectory.Create();
		var prgDirectory = Path.Combine(temp.Path, "prg");
		Directory.CreateDirectory(prgDirectory);
		File.WriteAllBytes(Path.Combine(prgDirectory, "sidtest5-test01.prg"), new byte[] { 0x01, 0x08, 0x00, 0x00 });
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exitCode = CopperModTools.Run(
			new[] { "compare-sidtest5", prgDirectory, "--out", Path.Combine(temp.Path, "out") },
			stdout,
			stderr);

		Assert.Equal(1, exitCode);
		Assert.Contains("Provide --sidplayfp", stderr.ToString());
	}

	private static void WriteFloatWav(string path, IReadOnlyList<float> samples)
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
		writer.Write(48000);
		writer.Write(48000 * sizeof(float));
		writer.Write((short)sizeof(float));
		writer.Write((short)32);
		writer.Write("data"u8);
		writer.Write(dataBytes);
		Span<byte> sampleBytes = stackalloc byte[sizeof(float)];
		foreach (var sample in samples)
		{
			BinaryPrimitives.WriteSingleLittleEndian(sampleBytes, sample);
			writer.Write(sampleBytes);
		}
	}
}
