namespace CopperScreen.Headless;

public enum CopperScreenTestStopReason
{
	ProgramReturned,
	FrameLimitReached,
	CycleLimitReached,
	InstructionLimitReached,
	CpuHalted
}

public sealed record CopperScreenTestCpuSnapshot(
	uint ProgramCounter,
	uint LastInstructionProgramCounter,
	ushort LastOpcode,
	ushort StatusRegister,
	uint UserStackPointer,
	uint SupervisorStackPointer,
	long Cycles,
	long NativeCycles,
	bool Halted,
	bool Stopped,
	uint[] DataRegisters,
	uint[] AddressRegisters);

public sealed record CopperScreenTestSnapshot(
	long FramesExecuted,
	long InstructionsExecuted,
	int MachineFrameNumber,
	int BeamLine,
	int BeamHorizontal,
	CopperScreenTestMachineProfile MachineProfile,
	CopperScreenTestCpuBackend CpuBackend,
	bool ProgramActive,
	bool ProgramReturned,
	CopperScreenTestCpuSnapshot Cpu,
	IReadOnlyList<CopperScreenTestDiagnostic> Diagnostics);

public readonly record struct CopperScreenTestDiagnostic(string Code, string Message);

public sealed record CopperScreenProgramLaunchResult(
	uint EntryAddress,
	string DisplayName,
	string Arguments,
	int StackSize,
	CopperScreenTestSnapshot Snapshot);

public sealed record CopperScreenTestRunResult(
	CopperScreenTestStopReason StopReason,
	uint ReturnValue,
	CopperScreenTestSnapshot Snapshot);
