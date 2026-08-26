using CopperScreen.Headless;
using Xunit;

namespace CopperScreen.Headless.Tests;

public sealed class CopperScreenTestRunnerTests
{
	[Fact]
	public void HunkProgramReturnsD0()
	{
		using var runner = CopperScreenTestRunner.Create();
		var launch = runner.LaunchHunk(CreateHunk(0x70, 0x2A, 0x4E, 0x75));
		var result = runner.RunUntilProgramReturn(new CopperScreenTestRunLimits(10, MaxInstructions: 100));

		Assert.NotEqual(0u, launch.EntryAddress);
		Assert.Equal(CopperScreenTestStopReason.ProgramReturned, result.StopReason);
		Assert.Equal(42u, result.ReturnValue);
		Assert.True(result.Snapshot.ProgramReturned);
	}

	[Fact]
	public void InstructionLimitStopsNonReturningProgram()
	{
		using var runner = CopperScreenTestRunner.Create();
		runner.LaunchHunk(CreateHunk(0x60, 0xFE, 0x4E, 0x71));
		var result = runner.RunUntilProgramReturn(new CopperScreenTestRunLimits(10, MaxInstructions: 20));

		Assert.Equal(CopperScreenTestStopReason.InstructionLimitReached, result.StopReason);
		Assert.Equal(20, result.Snapshot.InstructionsExecuted);
	}

	[Fact]
	public void InvalidHunkIsRejected()
	{
		using var runner = CopperScreenTestRunner.Create();
		Assert.Throws<InvalidDataException>(() => runner.LaunchHunk(new byte[] { 1, 2, 3, 4 }));
	}

	[Fact]
	public void SnapshotsOwnTheirRegisterArrays()
	{
		using var runner = CopperScreenTestRunner.Create();
		runner.LaunchHunk(CreateHunk(0x70, 0x2A, 0x4E, 0x75));
		var snapshot = runner.CaptureSnapshot();
		snapshot.Cpu.DataRegisters[0] = uint.MaxValue;

		Assert.NotEqual(uint.MaxValue, runner.CaptureSnapshot().Cpu.DataRegisters[0]);
	}

	[Fact]
	public void PublicAssemblyHasNoUiOrAudioDependency()
	{
		var dependencies = typeof(CopperScreenTestRunner).Assembly.GetReferencedAssemblies().Select(static item => item.Name).ToArray();
		Assert.DoesNotContain(dependencies, static name => name is not null &&
			(name.StartsWith("Avalonia", StringComparison.Ordinal) ||
			 name.StartsWith("Miniaudio", StringComparison.Ordinal) ||
			 name.Equals("CopperScreen", StringComparison.Ordinal)));
	}

	private static byte[] CreateHunk(params byte[] code)
	{
		if (code.Length != 4)
			throw new ArgumentException("This test helper creates one-longword code hunks.", nameof(code));

		return
		[
			0x00, 0x00, 0x03, 0xF3, // HUNK_HEADER
			0x00, 0x00, 0x00, 0x00, // no resident-library names
			0x00, 0x00, 0x00, 0x01, // table size
			0x00, 0x00, 0x00, 0x00, // first hunk
			0x00, 0x00, 0x00, 0x00, // last hunk
			0x00, 0x00, 0x00, 0x01, // allocation size
			0x00, 0x00, 0x03, 0xE9, // HUNK_CODE
			0x00, 0x00, 0x00, 0x01, // data size
			.. code,
			0x00, 0x00, 0x03, 0xF2  // HUNK_END
		];
	}
}
