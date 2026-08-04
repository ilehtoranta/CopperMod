namespace CopperScreen.Headless;

/// <summary>Machine profiles supported by the deterministic headless runner.</summary>
public enum CopperScreenTestMachineProfile
{
	A500Pal512K,
	A500PalExpanded,
	A500PlusEcsPal,
	A500PlusEcsNtsc
}

/// <summary>CPU backends supported by the deterministic headless runner.</summary>
public enum CopperScreenTestCpuBackend
{
	AccurateM68000,
	AccurateM68020,
	AccurateM68EC020,
	AccurateM68030,
	AccurateM68040,
	JitM68000,
	JitM68040
}

/// <summary>Configuration for one isolated headless machine.</summary>
public sealed record CopperScreenTestOptions
{
	public CopperScreenTestMachineProfile MachineProfile { get; init; } =
		CopperScreenTestMachineProfile.A500PalExpanded;

	public CopperScreenTestCpuBackend CpuBackend { get; init; } =
		CopperScreenTestCpuBackend.AccurateM68000;

	public bool CaptureBusAccesses { get; init; }

	public int MaxInstructionsPerFrame { get; init; } = 1_000_000;

	internal void Validate()
	{
		if (MaxInstructionsPerFrame <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(MaxInstructionsPerFrame),
				"The per-frame instruction limit must be positive.");
		}
	}
}

/// <summary>Application register-frame options used when launching HUNK bytes.</summary>
public sealed record CopperScreenProgramLaunchOptions
{
	public string DisplayName { get; init; } = "headless-test";

	public string Arguments { get; init; } = string.Empty;

	public int StackSize { get; init; } = 8 * 1024;

	public bool EnableInterrupts { get; init; }

	internal void Validate()
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
		if (StackSize <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(StackSize), "The program stack size must be positive.");
		}
	}
}

/// <summary>Deterministic bounds for one program run.</summary>
public readonly record struct CopperScreenTestRunLimits(
	long MaxFrames,
	long? MaxCycles = null,
	long? MaxInstructions = null,
	bool StopOnCpuHalt = true)
{
	internal void Validate()
	{
		if (MaxFrames <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(MaxFrames), "The frame limit must be positive.");
		}

		if (MaxCycles is <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(MaxCycles), "The cycle limit must be positive when specified.");
		}

		if (MaxInstructions is <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(MaxInstructions), "The instruction limit must be positive when specified.");
		}
	}
}
