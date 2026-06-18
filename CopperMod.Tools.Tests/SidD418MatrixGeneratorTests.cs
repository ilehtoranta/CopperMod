using CopperMod.Sid;

namespace CopperMod.Tools.Tests;

public sealed class SidD418MatrixGeneratorTests
{
	private const string MeasurementRootName = "Musik_RunStop_8-bit_sample_measurements_by_Pex_Mahoney_Tufvesson";

	[Fact]
	public void ParsesSidD418MatrixGeneratorOptions()
	{
		var options = SidD418MatrixGeneratorOptions.Parse(new[]
		{
			"generate-sid-d418-matrices",
			"--input",
			"pex",
			"--out",
			"SidD418TransitionMatrices.g.cs"
		});

		Assert.Equal("pex", options.InputRoot);
		Assert.Equal("SidD418TransitionMatrices.g.cs", options.OutputPath);
	}

	[Fact]
	public void SidD418MatrixGeneratorRequiresInputAndOutput()
	{
		Assert.Throws<CommandLineException>(() =>
			SidD418MatrixGeneratorOptions.Parse(new[] { "generate-sid-d418-matrices", "--out", "generated.cs" }));
		Assert.Throws<CommandLineException>(() =>
			SidD418MatrixGeneratorOptions.Parse(new[] { "generate-sid-d418-matrices", "--input", "pex" }));
	}

	[Fact]
	public void GeneratorCommandWritesMatrixSourceWhenCapturesArePresent()
	{
		var root = FindMeasurementRoot();
		if (root == null)
		{
			return;
		}

		using var temp = TemporaryDirectory.Create();
		var output = Path.Combine(temp.Path, "SidD418TransitionMatrices.g.cs");
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exitCode = CopperModTools.Run(new[]
		{
			"generate-sid-d418-matrices",
			"--input",
			root,
			"--out",
			output
		}, stdout, stderr);

		Assert.Equal(0, exitCode);
		Assert.True(File.Exists(output));
		Assert.Contains("Generated SID $D418 transition matrices", stdout.ToString());
		Assert.Equal(string.Empty, stderr.ToString());

		var source = File.ReadAllText(output);
		Assert.Contains("private static readonly float[] Mos6581PostWriteData", source);
		Assert.Contains("private static readonly float[] Mos8580PostWriteData", source);
		Assert.Contains(SidD418MatrixGenerator.FormatFloat((float)SidAnalog.Mos6581D418TransitionPostWriteAmplitude(0x00, 0x0F)) + "f", source);
		Assert.Contains(SidD418MatrixGenerator.FormatFloat((float)SidAnalog.Mos8580D418TransitionPostWriteAmplitude(0x00, 0x0F)) + "f", source);
		Assert.Contains(SidD418MatrixGenerator.FormatDouble(SidD418TransitionMatrices.Mos6581TransientDecaySeconds), source);
		Assert.Contains(SidD418MatrixGenerator.FormatDouble(SidD418TransitionMatrices.Mos8580TransientDecaySeconds), source);
	}

	private static string? FindMeasurementRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null)
		{
			var candidate = Path.Combine(directory.FullName, "CopperMod.Sid", "Docs", MeasurementRootName);
			if (Directory.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		return null;
	}
}
