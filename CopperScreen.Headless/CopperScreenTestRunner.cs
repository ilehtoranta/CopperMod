using Copper68k;
using CopperMod.Amiga;
using CopperMod.Amiga.Runtime;

namespace CopperScreen.Headless;

/// <summary>
/// Runs Amiga HUNK applications synchronously without Avalonia, host audio, or
/// a background emulation thread.
/// </summary>
public sealed class CopperScreenTestRunner : IDisposable
{
	private readonly CopperScreenTestOptions _options;
	private readonly Machine _machine;
	private readonly AmigaBootController _boot;
	private IReadOnlyList<CopperScreenTestDiagnostic> _diagnostics = Array.Empty<CopperScreenTestDiagnostic>();
	private long _targetCycle;
	private bool _programActive;
	private bool _programReturned;
	private bool _disposed;

	private CopperScreenTestRunner(CopperScreenTestOptions options)
	{
		options.Validate();
		_options = options;
		var machineOptions = MachineOptions.ForProfile(MapProfile(options.MachineProfile))
			.WithCpu(AmigaM68kCoreFactory.Default, MapBackend(options.CpuBackend))
			.WithBusAccessLogging(options.CaptureBusAccesses);
		_machine = new Machine(machineOptions);
		_boot = new AmigaBootController(_machine)
		{
			AutoRunStartupSequence = false,
			AutoStartWorkbenchDefaultTool = false
		};
		_boot.StartApplicationSession();
		_targetCycle = _machine.Cpu.State.Cycles;
	}

	public static CopperScreenTestRunner Create(CopperScreenTestOptions? options = null)
		=> new(options ?? new CopperScreenTestOptions());

	/// <summary>The active Copper68k core. Mutations are intended for advanced tests.</summary>
	public IM68kCore Cpu
	{
		get
		{
			ThrowIfDisposed();
			return _machine.Cpu;
		}
	}

	/// <summary>
	/// The CPU-visible bus. Device reads can have side effects and must not be
	/// used as a non-mutating diagnostic probe.
	/// </summary>
	public IM68kBus Bus
	{
		get
		{
			ThrowIfDisposed();
			return _machine.Bus;
		}
	}

	public long FramesExecuted { get; private set; }

	public long InstructionsExecuted { get; private set; }

	public CopperScreenProgramLaunchResult LaunchHunk(
		ReadOnlyMemory<byte> hunkImage,
		CopperScreenProgramLaunchOptions? options = null)
	{
		ThrowIfDisposed();
		if (hunkImage.IsEmpty)
		{
			throw new ArgumentException("The HUNK image cannot be empty.", nameof(hunkImage));
		}

		options ??= new CopperScreenProgramLaunchOptions();
		options.Validate();
		_boot.StartApplicationSession();
		var request = new AmigaProgramLaunchRequest(
			options.DisplayName,
			projectPath: null,
			currentDirectory: string.Empty,
			toolTypes: Array.Empty<string>(),
			options.StackSize,
			options.Arguments);
		if (!_boot.TryLaunchProgram(
			hunkImage.Span,
			request,
			out var launch,
			out var message,
			options.EnableInterrupts))
		{
			throw new InvalidDataException(message);
		}

		FramesExecuted = 0;
		InstructionsExecuted = 0;
		_targetCycle = _machine.Cpu.State.Cycles;
		_programActive = true;
		_programReturned = false;
		_diagnostics = Array.Empty<CopperScreenTestDiagnostic>();
		return new CopperScreenProgramLaunchResult(
			launch.EntryAddress,
			launch.ExecutablePath,
			launch.StartupArguments,
			launch.StackSize,
			CaptureSnapshot());
	}

	public CopperScreenTestSnapshot RunFrame()
	{
		ThrowIfDisposed();
		EnsureProgramActive();
		_ = ExecuteBoundary(_options.MaxInstructionsPerFrame, cycleDeadline: null, out _);
		return CaptureSnapshot();
	}

	public CopperScreenTestRunResult RunUntilProgramReturn(CopperScreenTestRunLimits limits)
	{
		ThrowIfDisposed();
		EnsureProgramActive();
		limits.Validate();
		var initialFrames = FramesExecuted;
		var initialCycles = _machine.Cpu.State.Cycles;
		var initialInstructions = InstructionsExecuted;
		var cycleDeadline = limits.MaxCycles.HasValue
			? checked(initialCycles + limits.MaxCycles.Value)
			: (long?)null;

		while (true)
		{
			if (FramesExecuted - initialFrames >= limits.MaxFrames)
			{
				return CreateRunResult(CopperScreenTestStopReason.FrameLimitReached);
			}

			var remainingInstructions = limits.MaxInstructions.HasValue
				? limits.MaxInstructions.Value - (InstructionsExecuted - initialInstructions)
				: _options.MaxInstructionsPerFrame;
			if (remainingInstructions <= 0)
			{
				return CreateRunResult(CopperScreenTestStopReason.InstructionLimitReached);
			}

			var instructionBudget = (int)Math.Min(_options.MaxInstructionsPerFrame, remainingInstructions);
			var completed = ExecuteBoundary(instructionBudget, cycleDeadline, out var reachedCycleDeadline);
			if (completed)
			{
				return CreateRunResult(CopperScreenTestStopReason.ProgramReturned);
			}

			if (limits.StopOnCpuHalt && _machine.Cpu.State.Halted)
			{
				return CreateRunResult(CopperScreenTestStopReason.CpuHalted);
			}

			if (reachedCycleDeadline)
			{
				return CreateRunResult(CopperScreenTestStopReason.CycleLimitReached);
			}

			if (limits.MaxInstructions.HasValue &&
				InstructionsExecuted - initialInstructions >= limits.MaxInstructions.Value)
			{
				return CreateRunResult(CopperScreenTestStopReason.InstructionLimitReached);
			}
		}
	}

	public CopperScreenTestSnapshot CaptureSnapshot()
	{
		ThrowIfDisposed();
		var state = _machine.Cpu.State;
		var beam = _machine.Bus.GetBeamPosition(state.Cycles);
		return new CopperScreenTestSnapshot(
			FramesExecuted,
			InstructionsExecuted,
			beam.FrameNumber,
			beam.BeamLine,
			beam.BeamHorizontal,
			_options.MachineProfile,
			_options.CpuBackend,
			_programActive,
			_programReturned,
			new CopperScreenTestCpuSnapshot(
				state.ProgramCounter,
				state.LastInstructionProgramCounter,
				state.LastOpcode,
				state.StatusRegister,
				state.UserStackPointer,
				state.SupervisorStackPointer,
				state.Cycles,
				state.NativeCycles,
				state.Halted,
				state.Stopped,
				state.D.ToArray(),
				state.A.ToArray()),
			_diagnostics);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_machine.Dispose();
	}

	private bool ExecuteBoundary(int instructionBudget, long? cycleDeadline, out bool reachedCycleDeadline)
	{
		var frameStartCycle = _targetCycle;
		var frameTargetCycle = _machine.Bus.GetFrameStopCycle(frameStartCycle);
		if (cycleDeadline.HasValue)
		{
			frameTargetCycle = Math.Min(frameTargetCycle, cycleDeadline.Value);
		}

		var result = _boot.ContinueExecutionUntilCycle(frameTargetCycle, instructionBudget);
		InstructionsExecuted += result.InstructionsExecuted;
		_diagnostics = result.Diagnostics
			.Select(static diagnostic => new CopperScreenTestDiagnostic(diagnostic.Code, diagnostic.Message))
			.ToArray();
		if (result.CompletedBootBlock)
		{
			_programActive = false;
			_programReturned = true;
			_targetCycle = _machine.Cpu.State.Cycles;
			FramesExecuted++;
			reachedCycleDeadline = cycleDeadline.HasValue && _targetCycle >= cycleDeadline.Value;
			return true;
		}

		if (_machine.Cpu.State.Cycles >= frameTargetCycle)
		{
			_machine.Bus.AdvanceHardwareTo(frameTargetCycle);
			_targetCycle = frameTargetCycle;
		}
		else
		{
			_targetCycle = _machine.Cpu.State.Cycles;
		}

		FramesExecuted++;
		reachedCycleDeadline = cycleDeadline.HasValue && _targetCycle >= cycleDeadline.Value;
		return false;
	}

	private CopperScreenTestRunResult CreateRunResult(CopperScreenTestStopReason reason)
	{
		var snapshot = CaptureSnapshot();
		return new CopperScreenTestRunResult(reason, snapshot.Cpu.DataRegisters[0], snapshot);
	}

	private void EnsureProgramActive()
	{
		if (!_programActive)
		{
			throw new InvalidOperationException(
				_programReturned ? "The HUNK program has already returned." : "Launch a HUNK program before running the machine.");
		}
	}

	private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

	private static MachineProfile MapProfile(CopperScreenTestMachineProfile profile) => profile switch
	{
		CopperScreenTestMachineProfile.A500Pal512K => MachineProfile.A500Pal512KChipOnlyBoot,
		CopperScreenTestMachineProfile.A500PalExpanded => MachineProfile.A500Pal512KBoot,
		CopperScreenTestMachineProfile.A500PlusEcsPal => MachineProfile.A500PlusEcsPal,
		CopperScreenTestMachineProfile.A500PlusEcsNtsc => MachineProfile.A500PlusEcsNtsc,
		_ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
	};

	private static M68kBackendKind MapBackend(CopperScreenTestCpuBackend backend) => backend switch
	{
		CopperScreenTestCpuBackend.AccurateM68000 => M68kBackendKind.AccurateM68000,
		CopperScreenTestCpuBackend.AccurateM68020 => M68kBackendKind.AccurateM68020,
		CopperScreenTestCpuBackend.AccurateM68EC020 => M68kBackendKind.AccurateM68EC020,
		CopperScreenTestCpuBackend.AccurateM68030 => M68kBackendKind.AccurateM68030,
		CopperScreenTestCpuBackend.AccurateM68040 => M68kBackendKind.AccurateM68040,
		CopperScreenTestCpuBackend.JitM68000 => M68kBackendKind.JitM68000,
		CopperScreenTestCpuBackend.JitM68040 => M68kBackendKind.JitM68040,
		_ => throw new ArgumentOutOfRangeException(nameof(backend), backend, null)
	};
}
