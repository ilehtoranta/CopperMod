using CopperMod.Sid;
using CopperMod.Tools;

namespace CopperMod.Sid.Tests;

public sealed class SidConformanceTests
{
	private const string UpdateEnvironmentVariable = "SID_CONFORMANCE_UPDATE";
	private const string OracleEnvironmentVariable = "SID_CONFORMANCE_ORACLE_TESTS";

	[Fact]
	public void SidConformanceSpecsRegenerateCheckedInBinaries()
	{
		var root = FixtureRoot();
		var suite = SidConformance.LoadSuite(root);
		var update = Environment.GetEnvironmentVariable(UpdateEnvironmentVariable) == "1";
		foreach (var fixture in suite.Fixtures)
		{
			var generated = SidConformance.BuildFixtureBinary(fixture.Spec);
			var generatedHash = SidConformance.Sha256Hex(generated);
			if (update)
			{
				Directory.CreateDirectory(Path.GetDirectoryName(fixture.BinaryPath) ?? ".");
				File.WriteAllBytes(fixture.BinaryPath, generated);
				fixture.Manifest.Sha256 = generatedHash;
				continue;
			}

			Assert.True(File.Exists(fixture.BinaryPath), "Missing SID conformance binary: " + fixture.BinaryPath);
			var actual = File.ReadAllBytes(fixture.BinaryPath);
			Assert.Equal(generatedHash, SidConformance.Sha256Hex(actual));
			Assert.Equal(fixture.Manifest.Sha256, SidConformance.Sha256Hex(actual));
			Assert.Equal(generated, actual);
		}

		if (update)
		{
			SidConformance.WriteManifest(Path.Combine(root, "manifest.json"), suite.Manifest);
		}
	}

	[Fact]
	public void SidConformanceCopperModBaselineMatchesCheckedInMetrics()
	{
		var root = FixtureRoot();
		var suite = SidConformance.LoadSuite(root);
		var baselinePath = Path.Combine(root, "baselines", "coppermod-balanced-pal-6581.json");
		var update = Environment.GetEnvironmentVariable(UpdateEnvironmentVariable) == "1";
		if (update)
		{
			EnsureGeneratedBinaries(suite);
			var generated = new SidConformanceBaseline
			{
				Schema = 1,
				Profile = "balanced",
				SampleRate = suite.Manifest.SampleRate ?? SidConformance.DefaultSampleRate,
				Tolerance = 0.0001
			};
			foreach (var fixture in suite.Fixtures)
			{
				generated.Fixtures.Add(SidConformance.MeasureFixture(fixture, generated.SampleRate, SidEmulationProfile.Balanced));
			}

			SidConformance.WriteBaseline(baselinePath, generated);
			return;
		}

		var baseline = SidConformance.LoadBaseline(baselinePath);
		var expectedById = baseline.Fixtures.ToDictionary(fixture => fixture.Id, StringComparer.Ordinal);
		Assert.Equal(suite.Fixtures.Count, baseline.Fixtures.Count);
		foreach (var fixture in suite.Fixtures)
		{
			Assert.True(expectedById.TryGetValue(fixture.Id, out var expected), "Missing baseline row for " + fixture.Id);
			var actual = SidConformance.MeasureFixture(fixture, baseline.SampleRate, SidEmulationProfile.Balanced);
			AssertOutputs(expected.Outputs, actual.Outputs, baseline.Tolerance, fixture.Id);
		}
	}

	[Fact]
	public void SidConformanceAccuracyEvidenceIsVersionedAndComplete()
	{
		var suite = SidConformance.LoadSuite(FixtureRoot());
		Assert.True(suite.Manifest.Schema >= 2);
		var evidence = Assert.IsType<SidConformanceEvidence>(suite.Manifest.Evidence);
		Assert.Equal("sidplayfp", evidence.Authority);
		Assert.Equal("reference", evidence.Profile);
		Assert.Equal(48000, evidence.SampleRate);
		Assert.False(string.IsNullOrWhiteSpace(evidence.ReferenceVersion));
		Assert.Matches("^[0-9a-f]{64}$", evidence.ReferenceSha256);
		Assert.Contains("segmentMetrics", evidence.Metrics);

		foreach (var fixture in suite.Fixtures)
		{
			Assert.Equal(fixture.Spec.ChipModel, fixture.Manifest.ChipModel, ignoreCase: true);
			Assert.Equal(fixture.Spec.Clock, fixture.Manifest.Clock, ignoreCase: true);
			var accuracy = Assert.IsType<SidConformanceAccuracyThreshold>(fixture.Manifest.Accuracy);
			Assert.True(accuracy.MinimumAcRatio > 0.0, fixture.Id + " minimum AC ratio must be positive.");
			Assert.True(accuracy.MaximumAcRatio >= accuracy.MinimumAcRatio, fixture.Id + " AC ratio range is invalid.");
			Assert.False(string.IsNullOrWhiteSpace(accuracy.Authority));
		}
	}

	[Fact]
	public void OptionalSidConformanceFixturesCompareAgainstSidPlayFp()
	{
		if (Environment.GetEnvironmentVariable(OracleEnvironmentVariable) != "1" &&
			Environment.GetEnvironmentVariable("SIDPLAYFP_ORACLE_TESTS") != "1")
		{
			return;
		}

		var sidPlayFp = Environment.GetEnvironmentVariable("SIDPLAYFP_EXE");
		if (string.IsNullOrWhiteSpace(sidPlayFp))
		{
			sidPlayFp = @"D:\Models\sidplayfp-3.0.2-ucrt64\sidplayfp.exe";
		}

		Assert.True(File.Exists(sidPlayFp), "SidPlayFP executable was not found: " + sidPlayFp);
		var suite = SidConformance.LoadSuite(FixtureRoot());
		var evidence = Assert.IsType<SidConformanceEvidence>(suite.Manifest.Evidence);
		Assert.Equal(evidence.ReferenceSha256, SidConformance.Sha256Hex(File.ReadAllBytes(sidPlayFp)));
		var reportRoot = Environment.GetEnvironmentVariable("SID_CONFORMANCE_REPORT_DIR");
		if (string.IsNullOrWhiteSpace(reportRoot))
		{
			reportRoot = Path.Combine(Path.GetTempPath(), "coppermod-sid-conformance-" + Guid.NewGuid().ToString("N"));
		}

		using var stdout = new StringWriter();
		using var stderr = new StringWriter();
		var exitCode = CopperModTools.Run(
			new[]
			{
				SidConformance.CommandName,
				FixtureRoot(),
				"--out",
				reportRoot,
				"--sidplayfp",
				sidPlayFp,
				"--sid-profile",
				"reference",
				"--overwrite"
			},
			stdout,
			stderr);

		Assert.Equal(0, exitCode);
		Assert.Empty(stderr.ToString());
		Assert.True(File.Exists(Path.Combine(reportRoot, "conformance-comparison.csv")));
		Assert.True(File.Exists(Path.Combine(reportRoot, "conformance-segments.csv")));
		Assert.True(File.Exists(Path.Combine(reportRoot, "index.html")));
		var accuracyErrors = SidConformance.ValidateAccuracyReport(
			suite,
			Path.Combine(reportRoot, "conformance-comparison.csv"));
		Assert.True(accuracyErrors.Count == 0, string.Join(Environment.NewLine, accuracyErrors));
	}

	private static void EnsureGeneratedBinaries(SidConformanceSuite suite)
	{
		foreach (var fixture in suite.Fixtures)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(fixture.BinaryPath) ?? ".");
			File.WriteAllBytes(fixture.BinaryPath, SidConformance.BuildFixtureBinary(fixture.Spec));
		}
	}

	private static void AssertOutputs(
		IReadOnlyDictionary<string, SidConformanceOutputMetrics> expected,
		IReadOnlyDictionary<string, SidConformanceOutputMetrics> actual,
		double tolerance,
		string fixtureId)
	{
		Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
		foreach (var output in expected.Keys)
		{
			var expectedOutput = expected[output];
			var actualOutput = actual[output];
			AssertClose(expectedOutput.Mean, actualOutput.Mean, tolerance, fixtureId, output, "mean");
			AssertClose(expectedOutput.AcRms, actualOutput.AcRms, tolerance, fixtureId, output, "ac_rms");
			AssertClose(expectedOutput.Peak, actualOutput.Peak, tolerance, fixtureId, output, "peak");
			Assert.Equal(expectedOutput.Segments.Count, actualOutput.Segments.Count);
			for (var i = 0; i < expectedOutput.Segments.Count; i++)
			{
				var expectedSegment = expectedOutput.Segments[i];
				var actualSegment = actualOutput.Segments[i];
				Assert.Equal(expectedSegment.Name, actualSegment.Name);
				AssertClose(expectedSegment.Mean, actualSegment.Mean, tolerance, fixtureId, output, expectedSegment.Name + ".mean");
				AssertClose(expectedSegment.AcRms, actualSegment.AcRms, tolerance, fixtureId, output, expectedSegment.Name + ".ac_rms");
				AssertClose(expectedSegment.Peak, actualSegment.Peak, tolerance, fixtureId, output, expectedSegment.Name + ".peak");
			}
		}
	}

	private static void AssertClose(
		double expected,
		double actual,
		double tolerance,
		string fixtureId,
		string output,
		string metric)
	{
		Assert.True(
			Math.Abs(expected - actual) <= tolerance,
			$"{fixtureId} {output} {metric}: expected {expected:0.000000}, actual {actual:0.000000}, tolerance {tolerance:0.000000}");
	}

	private static string FixtureRoot()
	{
		var directory = AppContext.BaseDirectory;
		while (!string.IsNullOrWhiteSpace(directory))
		{
			var candidate = Path.Combine(directory, "CopperMod.Sid.Tests", "ConformanceFixtures");
			if (Directory.Exists(candidate))
			{
				return candidate;
			}

			directory = Directory.GetParent(directory)?.FullName;
		}

		throw new DirectoryNotFoundException("Could not find CopperMod.Sid.Tests\\ConformanceFixtures from " + AppContext.BaseDirectory);
	}
}
