using System.Diagnostics;
using System.Runtime.InteropServices;
using Copper68k;
using CopperFloat;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Copper68k.Tests;

[CollectionDefinition("WinUAE CPU tester", DisableParallelization = true)]
public sealed class WinUaeCpuTesterCollection;

[Collection("WinUAE CPU tester")]
public sealed class M68kWinUaeCpuTesterConformanceTests
{
	private const string RunVariable = "COPPER68K_RUN_WINUAE_CPUTEST_M68000";
	private const string RunM68040FpuVariable = "COPPER68K_RUN_WINUAE_CPUTEST_M68040_FPU";
	private const string PathVariable = "COPPER68K_WINUAE_CPUTEST_M68000_PATH";
	private const string M68040FpuPathVariable = "COPPER68K_WINUAE_CPUTEST_M68040_FPU_PATH";
	private const string LibraryVariable = "COPPER68K_WINUAE_CPUTEST_LIBRARY";
	private const string OpcodeVariable = "COPPER68K_WINUAE_CPUTEST_OPCODE";
	private const string M68040FpuOpcodeVariable = "COPPER68K_WINUAE_CPUTEST_M68040_FPU_OPCODE";
	private const string CheckUndefinedSrVariable = "COPPER68K_WINUAE_CPUTEST_CHECK_UNDEFINED_SR";
	private const string ContinueOnErrorVariable = "COPPER68K_WINUAE_CPUTEST_CONTINUE_ON_ERROR";
	private const string AuditVariable = "COPPER68K_WINUAE_CPUTEST_AUDIT";
	private const string AuditOutputVariable = "COPPER68K_WINUAE_CPUTEST_AUDIT_OUTPUT";
	private const string DefaultCorpusRelativePath = "third_party/winuae-cputest";
	private readonly ITestOutputHelper _output;

	public M68kWinUaeCpuTesterConformanceTests(ITestOutputHelper output)
	{
		_output = output;
	}

	[Fact]
	public void WinUaeM68000CpuTesterPassesInterpreterWhenEnabled()
	{
		if (!IsTruthy(Environment.GetEnvironmentVariable(RunVariable)))
		{
			_output.WriteLine($"Set {RunVariable}=1 to run the WinUAE cputest/gencpu MC68000 corpus.");
			return;
		}

		var corpusPath = ResolveCorpusPath(PathVariable, "68000");
		if (corpusPath is null)
		{
			throw new XunitException(
				$"WinUAE cputest MC68000 corpus not found. Set {PathVariable} to a generated cputest output folder, " +
				$"or generate it at {DefaultCorpusRelativePath}.");
		}

		var libraryPath = Environment.GetEnvironmentVariable(LibraryVariable);
		if (string.IsNullOrWhiteSpace(libraryPath))
		{
			throw new XunitException(
				$"Set {LibraryVariable} to a native m68k_cpu_tester_api library built from Copperline's vendored cputest runner.");
		}

		var opcode = Environment.GetEnvironmentVariable(OpcodeVariable);
		if (string.IsNullOrWhiteSpace(opcode))
		{
			opcode = "all";
		}

		using var tester = NativeTester.Load(libraryPath);
		var checkUndefinedSr = IsTruthy(Environment.GetEnvironmentVariable(CheckUndefinedSrVariable));
		var continueOnError = IsTruthy(Environment.GetEnvironmentVariable(ContinueOnErrorVariable));
		if (IsTruthy(Environment.GetEnvironmentVariable(AuditVariable)))
		{
			RunOpcodeAudit(tester, corpusPath, "68000", 0, checkUndefinedSr, continueOnError);
			return;
		}

		var result = tester.Run(corpusPath, opcode, 0, checkUndefinedSr, continueOnError);
		if (!result.Passed)
		{
			throw new XunitException(result.Detail);
		}

		_output.WriteLine($"Executed WinUAE cputest/gencpu MC68000 '{opcode}' corpus with the interpreter backend.");
	}

	[Fact]
	public void WinUaeM68040FpuCpuTesterPassesInterpreterWhenEnabled()
	{
		if (!IsTruthy(Environment.GetEnvironmentVariable(RunM68040FpuVariable)))
		{
			_output.WriteLine($"Set {RunM68040FpuVariable}=1 to run the WinUAE MC68040 FPU corpus.");
			return;
		}

		var corpusPath = ResolveCorpusPath(M68040FpuPathVariable, "68040");
		if (corpusPath is null)
		{
			throw new XunitException(
				$"WinUAE cputest MC68040 FPU corpus not found. Set {M68040FpuPathVariable} to a generated output folder containing 68040 data.");
		}

		var libraryPath = Environment.GetEnvironmentVariable(LibraryVariable);
		if (string.IsNullOrWhiteSpace(libraryPath))
		{
			throw new XunitException($"Set {LibraryVariable} to the native m68k_cpu_tester_api library.");
		}

		var opcode = Environment.GetEnvironmentVariable(M68040FpuOpcodeVariable);
		if (string.IsNullOrWhiteSpace(opcode))
		{
			opcode = "all";
		}

		using var tester = NativeTester.Load(libraryPath);
		var checkUndefinedSr = IsTruthy(Environment.GetEnvironmentVariable(CheckUndefinedSrVariable));
		var continueOnError = IsTruthy(Environment.GetEnvironmentVariable(ContinueOnErrorVariable));
		if (IsTruthy(Environment.GetEnvironmentVariable(AuditVariable)))
		{
			RunOpcodeAudit(tester, corpusPath, "68040", 4, checkUndefinedSr, continueOnError);
			return;
		}

		var result = tester.Run(corpusPath, opcode, 4, checkUndefinedSr, continueOnError);
		if (!result.Passed)
		{
			throw new XunitException(result.Detail);
		}

		_output.WriteLine($"Executed WinUAE cputest/gencpu MC68040 FPU '{opcode}' corpus with the interpreter backend.");
	}

	private void RunOpcodeAudit(
		NativeTester tester,
		string corpusPath,
		string cpuDirectory,
		byte cpuLevel,
		bool checkUndefinedSr,
		bool continueOnError)
	{
		var opcodeRoot = Path.Combine(corpusPath, cpuDirectory);
		var opcodes = Directory.GetDirectories(opcodeRoot)
			.Select(Path.GetFileName)
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Cast<string>()
			.OrderBy(name => name, StringComparer.Ordinal)
			.ToArray();
		if (opcodes.Length == 0)
		{
			throw new XunitException($"WinUAE cputest opcode directory '{opcodeRoot}' is empty.");
		}

		var rows = new List<OpcodeAuditRow>(opcodes.Length);
		foreach (var opcode in opcodes)
		{
			var stopwatch = Stopwatch.StartNew();
			try
			{
				var result = tester.Run(corpusPath, opcode, cpuLevel, checkUndefinedSr, continueOnError);
				rows.Add(new OpcodeAuditRow(
					opcode,
					result.Passed ? "pass" : "fail",
					result.ExecutedCases,
					result.UnmappedReads,
					result.UnmappedWrites,
					stopwatch.Elapsed.TotalMilliseconds,
					result.Detail));
			}
			catch (Exception ex)
			{
				rows.Add(new OpcodeAuditRow(
					opcode,
					"error",
					0,
					0,
					0,
					stopwatch.Elapsed.TotalMilliseconds,
					ex.ToString()));
			}

			var row = rows[^1];
			_output.WriteLine(
				$"{row.Status}: {row.Opcode}, cases={row.ExecutedCases}, " +
				$"unmappedReads={row.UnmappedReads}, unmappedWrites={row.UnmappedWrites}, " +
				$"durationMs={row.DurationMs:F0}");
		}

		var configuredOutput = Environment.GetEnvironmentVariable(AuditOutputVariable);
		var outputPath = string.IsNullOrWhiteSpace(configuredOutput)
			? Path.Combine(corpusPath, $"winuae-m{cpuDirectory}-opcode-audit.tsv")
			: Path.GetFullPath(configuredOutput);
		var outputDirectory = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrWhiteSpace(outputDirectory))
		{
			Directory.CreateDirectory(outputDirectory);
		}

		var lines = new List<string>(rows.Count + 1)
		{
			"opcode\tstatus\texecuted_cases\tunmapped_reads\tunmapped_writes\tduration_ms\tdetail"
		};
		lines.AddRange(rows.Select(row => string.Join(
			"\t",
			row.Opcode,
			row.Status,
			row.ExecutedCases.ToString(),
			row.UnmappedReads.ToString(),
			row.UnmappedWrites.ToString(),
			row.DurationMs.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
			EscapeTsv(row.Detail))));
		File.WriteAllLines(outputPath, lines);

		var failures = rows.Where(row => row.Status != "pass").ToArray();
		_output.WriteLine($"WinUAE opcode audit wrote {rows.Count} rows to '{outputPath}'.");
		if (failures.Length != 0)
		{
			throw new XunitException(
				$"WinUAE opcode audit found {failures.Length} failing directory(s). " +
				$"See '{outputPath}'. Failed opcodes: {string.Join(", ", failures.Select(row => row.Opcode))}.");
		}
	}

	private static string EscapeTsv(string value)
		=> value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

	private readonly record struct OpcodeAuditRow(
		string Opcode,
		string Status,
		int ExecutedCases,
		int UnmappedReads,
		int UnmappedWrites,
		double DurationMs,
		string Detail);

	private static string? ResolveCorpusPath(string pathVariable, string cpuDirectory)
	{
		var configuredPath = Environment.GetEnvironmentVariable(pathVariable);
		if (!string.IsNullOrWhiteSpace(configuredPath))
		{
			return ResolveGeneratedDataPath(configuredPath, cpuDirectory);
		}

		var root = FindRepositoryRoot();
		return root is null
			? null
			: ResolveGeneratedDataPath(
				Path.Combine(root, DefaultCorpusRelativePath.Replace('/', Path.DirectorySeparatorChar)),
				cpuDirectory);
	}

	private static string? ResolveGeneratedDataPath(string path, string cpuDirectory)
	{
		var fullPath = Path.GetFullPath(path);
		if (ContainsWinUaeCpuTesterData(Path.Combine(fullPath, cpuDirectory)))
		{
			return fullPath;
		}

		if (Path.GetFileName(fullPath).Equals(cpuDirectory, StringComparison.OrdinalIgnoreCase) &&
			ContainsWinUaeCpuTesterData(fullPath))
		{
			return Directory.GetParent(fullPath)?.FullName;
		}

		var copperlinePath = Path.Combine(fullPath, "data");
		if (ContainsWinUaeCpuTesterData(Path.Combine(copperlinePath, cpuDirectory)))
		{
			return copperlinePath;
		}

		return null;
	}

	private static bool ContainsWinUaeCpuTesterData(string path)
		=> Directory.Exists(path) &&
			File.Exists(Path.Combine(path, "lmem.dat")) &&
			File.Exists(Path.Combine(path, "tmem.dat")) &&
			Directory.EnumerateFiles(path, "*.dat", SearchOption.AllDirectories).Any();

	private static string? FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, "CopperMod.sln")))
			{
				return directory.FullName;
			}

			directory = directory.Parent;
		}

		return null;
	}

	private static bool IsTruthy(string? value)
		=> value is not null &&
			(value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
			 value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
			 value.Equals("yes", StringComparison.OrdinalIgnoreCase));

	private sealed class NativeTester : IDisposable
	{
		private const int MaxStepsPerCase = 64;
		private readonly IntPtr _library;
		private readonly NativeInit _init;
		private readonly NativeRunTests _runTests;
		private readonly NativeLastOutput? _lastOutput;
		private readonly NativeAddressingMask? _addressingMask;
		private readonly NativeCallback _callback;
		private Exception? _callbackException;
		private int _executedCases;
		private int _unmappedReads;
		private int _unmappedWrites;
		private string _lastCaseSummary = "";
		private byte _cpuLevel;

		private NativeTester(IntPtr library)
		{
			_library = library;
			_init = GetDelegate<NativeInit>(library, "M68KTester_init");
			_runTests = GetDelegate<NativeRunTests>(library, "M68KTester_run_tests");
			_lastOutput = TryGetDelegate<NativeLastOutput>(library, "M68KTester_last_output");
			_addressingMask = TryGetDelegate<NativeAddressingMask>(library, "m68k_tester_addressing_mask");
			_callback = RunCopper68k;
		}

		public static NativeTester Load(string libraryPath)
		{
			var fullPath = Path.GetFullPath(libraryPath);
			if (!File.Exists(fullPath))
			{
				throw new XunitException($"WinUAE cputest native library was not found at '{fullPath}'.");
			}

			try
			{
				return new NativeTester(NativeLibrary.Load(fullPath));
			}
			catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
			{
				throw new XunitException($"Failed to load WinUAE cputest native library '{fullPath}': {ex.Message}");
			}
		}

		public RunResult Run(
			string corpusPath,
			string opcode,
			byte cpuLevel,
			bool checkUndefinedSr,
			bool continueOnError)
		{
			_callbackException = null;
			_executedCases = 0;
			_unmappedReads = 0;
			_unmappedWrites = 0;
			_lastCaseSummary = "";
			_cpuLevel = cpuLevel;

			var corpusPathPtr = Marshal.StringToHGlobalAnsi(corpusPath);
			var opcodePtr = Marshal.StringToHGlobalAnsi(opcode);
			try
			{
				var settings = new NativeRunSettings
				{
					Opcode = opcodePtr,
					CpuLevel = cpuLevel,
					CheckUndefinedSr = checkUndefinedSr ? (byte)1 : (byte)0,
					ContinueOnError = continueOnError ? (byte)1 : (byte)0
				};

				var result = _init(corpusPathPtr, ref settings);
				if (result.Error != IntPtr.Zero)
				{
					throw new XunitException(Marshal.PtrToStringAnsi(result.Error) ?? "WinUAE cputest initialization failed.");
				}

				if (result.Context == IntPtr.Zero)
				{
					throw new XunitException("WinUAE cputest initialization returned a null context.");
				}

				var nativeResult = _runTests(result.Context, IntPtr.Zero, _callback);
				var nativeOutput = GetNativeOutput();
				if (_callbackException is not null)
				{
					return new RunResult(false, _executedCases, _unmappedReads, _unmappedWrites,
						$"WinUAE cputest callback failed after {_executedCases} callback case(s): " +
						$"{_callbackException.GetType().Name}: {_callbackException.Message}. " +
						$"Unmapped reads: {_unmappedReads}, unmapped writes: {_unmappedWrites}. " +
						$"Native diagnostic: {nativeOutput}");
				}

				if (nativeResult == 0)
				{
					return new RunResult(false, _executedCases, _unmappedReads, _unmappedWrites,
						$"WinUAE cputest reported failure after {_executedCases} callback case(s). " +
						$"Unmapped reads: {_unmappedReads}, unmapped writes: {_unmappedWrites}. " +
						$"Last case: {_lastCaseSummary}. " +
						$"Native diagnostic: {nativeOutput}. " +
						"Build the native wrapper so M68KTester_run_tests returns 1 only when the corpus passed.");
				}

				return new RunResult(true, _executedCases, _unmappedReads, _unmappedWrites,
					$"WinUAE cputest '{opcode}' passed.");
			}
			finally
			{
				Marshal.FreeHGlobal(opcodePtr);
				Marshal.FreeHGlobal(corpusPathPtr);
			}
		}

		private string GetNativeOutput()
		{
			if (_lastOutput is null)
			{
				return "unavailable (rebuild the native wrapper with M68KTester_last_output)";
			}

			return Marshal.PtrToStringAnsi(_lastOutput()) ?? "";
		}

		public readonly record struct RunResult(
			bool Passed,
			int ExecutedCases,
			int UnmappedReads,
			int UnmappedWrites,
			string Detail);

		public void Dispose()
		{
			if (_library != IntPtr.Zero)
			{
				NativeLibrary.Free(_library);
			}
		}

		private void RunCopper68k(IntPtr userData, IntPtr contextPtr, IntPtr registersPtr)
		{
			if (_callbackException is not null)
			{
				return;
			}

			try
			{
				_ = userData;
				var context = Marshal.PtrToStructure<NativeContext>(contextPtr);
				var registers = Marshal.PtrToStructure<NativeRegisters>(registersPtr);
				var bus = new NativeRangeBus(context, _addressingMask?.Invoke() ?? 0x00FF_FFFFu);
				IM68kCore cpu = _cpuLevel == 4
					? new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz)
					: new M68kInterpreter(bus);
				_lastCaseSummary = FormatCaseSummary(context, registers, bus);
				bus.CopyStackImage(registers.Regs[15], registers.Ssp, 0x20);

				ApplyRegisters(cpu, registers, _cpuLevel);
				registers.Cycles = 0;
				DeferredFpuException? deferredFpuException = null;
				var deferredFpuExceptionObserved = false;
				var executionTrace = "";
				for (var step = 0; step < MaxStepsPerCase; step++)
				{
					var instructionPc = cpu.State.ProgramCounter;
					var userStackPointer = cpu.State.UserStackPointer;
					var supervisorStackPointer = cpu.State.SupervisorStackPointer;
					var masterStackPointer = cpu.State.MasterStackPointer;
					var activeStackPointer = cpu.State.A[7];
					var tracePending = (cpu.State.StatusRegister & M68kCpuState.Trace) != 0;
					var cycles = cpu.ExecuteInstruction();
					executionTrace +=
						$" [{step}:pc=0x{instructionPc:X8},op=0x{cpu.State.LastOpcode:X4}," +
						$"next=0x{cpu.State.ProgramCounter:X8},exc={cpu.State.LastExceptionVector},d4=0x{cpu.State.D[4]:X8}]";
					if (cpu.State.LastExceptionVector >= 0)
					{
						if (deferredFpuException is null &&
							IsDeferredM68040FpuArithmeticException(cpu.State, bus, _cpuLevel))
						{
							deferredFpuException = DeferredFpuException.Capture(cpu.State);
							deferredFpuExceptionObserved = true;
							ResumeAfterDeferredFpuException(
								cpu.State,
								userStackPointer,
								supervisorStackPointer,
								masterStackPointer,
								activeStackPointer);
							continue;
						}

						deferredFpuException = null;
						break;
					}

					registers.Cycles += (uint)cycles;
					var traceSetAfterInstruction = (cpu.State.StatusRegister & M68kCpuState.Trace) != 0;
					if (tracePending && !cpu.State.Stopped && !cpu.State.Halted)
					{
						RaiseHarnessTraceException(cpu.State, bus);
						break;
					}

					if (!traceSetAfterInstruction &&
						(cpu.State.ProgramCounter == registers.EndPc ||
						 (registers.BranchTarget != 0xFFFF_FFFFu && cpu.State.ProgramCounter == registers.BranchTarget)))
					{
						break;
					}
				}

				deferredFpuException?.Restore(cpu.State);
				_lastCaseSummary += $", trace={executionTrace}";

				if (_cpuLevel == 4 && bus.UnmappedReads != 0 && !deferredFpuExceptionObserved)
				{
					RestoreUnobservableFpuRead(cpu.State, bus, registers);
				}

				CopyRegisters(cpu.State, bus, _cpuLevel, ref registers);
				Marshal.StructureToPtr(registers, registersPtr, fDeleteOld: false);
				_unmappedReads += bus.UnmappedReads;
				_unmappedWrites += bus.UnmappedWrites;
				_executedCases++;
			}
			catch (Exception ex)
			{
				_callbackException = new InvalidOperationException(
					$"{_lastCaseSummary}. Inner: {ex}",
					ex);
			}
		}

		private static void RaiseHarnessTraceException(M68kCpuState state, NativeRangeBus bus)
		{
			var savedStatusRegister = state.StatusRegister;
			var stackedProgramCounter = state.ProgramCounter;
			state.RecordException(9, stackedProgramCounter, savedStatusRegister);
			state.StatusRegister = (ushort)((savedStatusRegister | M68kCpuState.Supervisor) & ~M68kCpuState.Trace);
			state.SetActiveStackPointer(state.A[7] - 4);
			bus.WriteHostLong(state.A[7], stackedProgramCounter);
			state.SetActiveStackPointer(state.A[7] - 2);
			bus.WriteHostWord(state.A[7], savedStatusRegister);
			state.ProgramCounter = bus.ReadHostLong(9 * 4);
		}

		private static string FormatCaseSummary(NativeContext context, NativeRegisters registers, NativeRangeBus bus)
		{
			var opcode = bus.ReadHostWord(registers.Pc);
			var extension0 = bus.ReadHostWord(registers.Pc + 2);
			var extension1 = bus.ReadHostWord(registers.Pc + 4);
			var extension2 = bus.ReadHostWord(registers.Pc + 6);
			var extension3 = bus.ReadHostWord(registers.Pc + 8);
			var extension4 = bus.ReadHostWord(registers.Pc + 10);
			return $"opcode='{context.Name}', words=0x{opcode:X4},0x{extension0:X4},0x{extension1:X4}," +
				$"0x{extension2:X4},0x{extension3:X4},0x{extension4:X4}, " +
				$"PC=0x{registers.Pc:X8}, SR=0x{registers.Sr:X4}, " +
				$"EndPC=0x{registers.EndPc:X8}, BranchTarget=0x{registers.BranchTarget:X8}, " +
				$"D0=0x{registers.Regs[0]:X8}, A0=0x{registers.Regs[8]:X8}, A7=0x{registers.Regs[15]:X8}, " +
				bus.FormatRanges();
		}

		private static void ApplyRegisters(IM68kCore cpu, NativeRegisters registers, byte cpuLevel)
		{
			var state = cpu.State;
			var supervisorMode = (registers.Sr & M68kCpuState.Supervisor) != 0;
			var activeStackPointer = supervisorMode
				? registers.Ssp
				: registers.Regs[15];

			cpu.Reset(registers.Pc, registers.Ssp);

			for (var i = 0; i < 8; i++)
			{
				state.D[i] = registers.Regs[i];
			}

			for (var i = 0; i < 7; i++)
			{
				state.A[i] = registers.Regs[8 + i];
			}

			state.ResetStackPointers(registers.Ssp, registers.Regs[15], supervisorMode);
			state.SetMasterStackPointer(registers.Msp);
			state.ProgramCounter = registers.Pc;
			state.StatusRegister = (ushort)registers.Sr;
			state.A[7] = activeStackPointer;
			state.RecordException(-1, 0, 0);
			if (cpuLevel == 4)
			{
				for (var i = 0; i < state.M68040Fpu.FP.Length; i++)
				{
					var source = registers.FpuRegisters[i];
					state.M68040Fpu.FP[i] = ExtF80.FromBits(
						source.Exp,
						((ulong)source.Mantissa0 << 32) | source.Mantissa1);
				}

				state.M68040Fpu.Fpiar = registers.Fpiar;
				state.M68040Fpu.Fpcr = registers.Fpcr;
				state.M68040Fpu.Fpsr = registers.Fpsr;
			}
		}

		private static void RestoreUnobservableFpuRead(
			M68kCpuState state,
			NativeRangeBus bus,
			NativeRegisters registers)
		{
			var opcode = bus.ReadHostWord(registers.Pc);
			if ((opcode & 0xFFC0) != 0xF200)
			{
				return;
			}

			var extension = bus.ReadHostWord(registers.Pc + 2);
			var opclass = (extension >> 13) & 7;
			if (opclass is not (0 or 2 or 4 or 6))
			{
				return;
			}

			for (var i = 0; i < state.M68040Fpu.FP.Length; i++)
			{
				var source = registers.FpuRegisters[i];
				state.M68040Fpu.FP[i] = ExtF80.FromBits(
					source.Exp,
					((ulong)source.Mantissa0 << 32) | source.Mantissa1);
			}

			state.M68040Fpu.Fpcr = registers.Fpcr;
			state.M68040Fpu.Fpsr = registers.Fpsr;
			state.M68040Fpu.Fpiar = registers.Fpiar;
		}

		private static void CopyRegisters(
			M68kCpuState state,
			NativeRangeBus bus,
			byte cpuLevel,
			ref NativeRegisters registers)
		{
			var initialSupervisorStackPointer = registers.Ssp;
			for (var i = 0; i < 8; i++)
			{
				registers.Regs[i] = state.D[i];
			}

			for (var i = 0; i < 8; i++)
			{
				registers.Regs[8 + i] = state.A[i];
			}

			registers.Regs[15] = state.UserStackPointer;
			var deferredFpuArithmeticException = IsDeferredM68040FpuArithmeticException(state, bus, cpuLevel);
			if (state.LastExceptionVector >= 0 && !deferredFpuArithmeticException)
			{
				registers.Exc = (uint)state.LastExceptionVector;
				registers.Exc010 = (uint)state.LastExceptionVector;
				registers.ExcFrame = state.A[7];
				registers.Sr = bus.ReadHostWord(state.A[7]);
				registers.Pc = bus.ReadHostLong(state.A[7] + 2);
				registers.Ssp = state.A[7];
			}
			else
			{
				registers.Sr = deferredFpuArithmeticException
					? state.LastExceptionStatusRegister
					: state.StatusRegister;
				registers.Pc = deferredFpuArithmeticException
					? state.LastExceptionStackedProgramCounter
					: state.ProgramCounter;
				registers.Ssp = deferredFpuArithmeticException
					? initialSupervisorStackPointer
					: (state.StatusRegister & M68kCpuState.Supervisor) != 0
						? state.A[7]
						: state.SupervisorStackPointer;
			}

			registers.Msp = state.MasterStackPointer;
			if (cpuLevel == 4)
			{
				for (var i = 0; i < state.M68040Fpu.FP.Length; i++)
				{
					var value = state.M68040Fpu.FP[i];
					registers.FpuRegisters[i] = new NativeFpuRegister
					{
						Exp = value.SignExponent,
						Mantissa0 = (uint)(value.Significand >> 32),
						Mantissa1 = (uint)value.Significand
					};
				}

				registers.Fpiar = state.M68040Fpu.Fpiar;
				registers.Fpcr = state.M68040Fpu.Fpcr;
				registers.Fpsr = state.M68040Fpu.Fpsr;
			}
		}

		private static bool IsDeferredM68040FpuArithmeticException(
			M68kCpuState state,
			NativeRangeBus bus,
			byte cpuLevel)
		{
			if (cpuLevel != 4 || state.LastExceptionVector is < 49 or > 54 ||
				(state.LastExceptionOpcode & 0xFFC0) != 0xF200)
			{
				return false;
			}

			// WinUAE's single-instruction corpus retains register arithmetic exceptions
			// as an internal pending FPU exception until the next FPU boundary.
			var extension = bus.ReadHostWord(state.LastExceptionInstructionProgramCounter + 2);
			var opclass = (extension >> 13) & 7;
			return opclass is 0 or 2;
		}

		private static void ResumeAfterDeferredFpuException(
			M68kCpuState state,
			uint userStackPointer,
			uint supervisorStackPointer,
			uint masterStackPointer,
			uint activeStackPointer)
		{
			var statusRegister = state.LastExceptionStatusRegister;
			var programCounter = state.LastExceptionStackedProgramCounter;
			state.ResetStackPointers(
				supervisorStackPointer,
				userStackPointer,
				(statusRegister & M68kCpuState.Supervisor) != 0);
			state.EnableM68020StackMode();
			state.SetInterruptStackPointer(supervisorStackPointer);
			state.SetMasterStackPointer(masterStackPointer);
			state.StatusRegister = statusRegister;
			state.SetActiveStackPointer(activeStackPointer);
			state.ProgramCounter = programCounter;
			state.RecordException(-1, 0, 0);
		}

		private readonly record struct DeferredFpuException(
			int Vector,
			uint StackedProgramCounter,
			ushort StatusRegister,
			ushort Opcode,
			uint InstructionProgramCounter,
			uint D0,
			uint D1,
			uint A0,
			uint A6,
			uint A7)
		{
			public static DeferredFpuException Capture(M68kCpuState state)
				=> new(
					state.LastExceptionVector,
					state.LastExceptionStackedProgramCounter,
					state.LastExceptionStatusRegister,
					state.LastExceptionOpcode,
					state.LastExceptionInstructionProgramCounter,
					state.LastExceptionD0,
					state.LastExceptionD1,
					state.LastExceptionA0,
					state.LastExceptionA6,
					state.LastExceptionA7);

			public void Restore(M68kCpuState state)
			{
				state.LastExceptionVector = Vector;
				state.LastExceptionStackedProgramCounter = StackedProgramCounter;
				state.LastExceptionStatusRegister = StatusRegister;
				state.LastExceptionOpcode = Opcode;
				state.LastExceptionInstructionProgramCounter = InstructionProgramCounter;
				state.LastExceptionD0 = D0;
				state.LastExceptionD1 = D1;
				state.LastExceptionA0 = A0;
				state.LastExceptionA6 = A6;
				state.LastExceptionA7 = A7;
			}
		}

		private static T GetDelegate<T>(IntPtr library, string exportName)
			where T : Delegate
			=> Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, exportName));

		private static T? TryGetDelegate<T>(IntPtr library, string exportName)
			where T : Delegate
			=> NativeLibrary.TryGetExport(library, exportName, out var export)
				? Marshal.GetDelegateForFunctionPointer<T>(export)
				: null;

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate NativeInitResult NativeInit(IntPtr path, ref NativeRunSettings settings);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate int NativeRunTests(IntPtr context, IntPtr userData, NativeCallback callback);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate IntPtr NativeLastOutput();

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate uint NativeAddressingMask();

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void NativeCallback(IntPtr userData, IntPtr context, IntPtr registers);
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct NativeRunSettings
	{
		public IntPtr Opcode;
		public byte CpuLevel;
		public byte CheckUndefinedSr;
		public byte ContinueOnError;
	}

	[StructLayout(LayoutKind.Sequential)]
	private readonly struct NativeInitResult
	{
		public readonly IntPtr Context;
		public readonly IntPtr Error;
	}

	[StructLayout(LayoutKind.Sequential)]
	private readonly struct NativeMemoryRange
	{
		public NativeMemoryRange(IntPtr buffer, uint start, uint end, uint size)
		{
			Buffer = buffer;
			Start = start;
			End = end;
			Size = size;
		}

		public readonly IntPtr Buffer;
		public readonly uint Start;
		public readonly uint End;
		public readonly uint Size;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
	private struct NativeContext
	{
		public IntPtr Opcode;
		public uint StopOnError;
		public NativeMemoryRange LowMemory;
		public NativeMemoryRange HighMemory;
		public NativeMemoryRange TestMemory;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 17)]
		public string Name;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2048)]
		public string CpuPath;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct NativeFpuRegister
	{
		public ushort Exp;
		public ushort Dummy;
		public uint Mantissa0;
		public uint Mantissa1;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct NativeRegisters
	{
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
		public uint[] Regs;
		public uint Ssp;
		public uint Msp;
		public uint Pc;
		public uint Sr;
		public uint Exc;
		public uint Exc010;
		public uint ExcFrame;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
		public NativeFpuRegister[] FpuRegisters;
		public uint Fpiar;
		public uint Fpcr;
		public uint Fpsr;
		public uint SourceAddress;
		public uint DestinationAddress;
		public uint EndPc;
		public uint BranchTarget;
		public uint Cycles;
		public byte BranchTargetMode;
	}

	private sealed class NativeRangeBus : IM68kBus, IM68kCodeReader
	{
		private readonly NativeMemoryRange _lowMemory;
		private readonly NativeMemoryRange _highMemory;
		private readonly NativeMemoryRange _testMemory;
		private readonly uint _addressMask;

		public int UnmappedReads { get; private set; }
		public int UnmappedWrites { get; private set; }
		public uint? FirstUnmappedRead { get; private set; }
		public uint? FirstUnmappedWrite { get; private set; }

		public NativeRangeBus(NativeContext context, uint addressMask)
		{
			_lowMemory = context.LowMemory;
			_highMemory = NormalizeHighMemoryRange(context.HighMemory, addressMask);
			_testMemory = context.TestMemory;
			_addressMask = addressMask;
		}

		private static NativeMemoryRange NormalizeHighMemoryRange(
			NativeMemoryRange range,
			uint addressMask)
		{
			const uint defaultHighMemorySize = 0x8000;
			if (range.Buffer == IntPtr.Zero || range.Size != 0 ||
				range.Start != uint.MaxValue || range.End != uint.MaxValue)
			{
				return range;
			}

			var start = unchecked(addressMask - defaultHighMemorySize + 1);
			return new NativeMemoryRange(
				range.Buffer,
				start,
				unchecked(start + defaultHighMemorySize),
				defaultHighMemorySize);
		}

		public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			return ReadByte(address);
		}

		public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			return ReadWord(address);
		}

		public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			return ReadLong(address);
		}

		public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			WriteByte(address, value);
		}

		public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			WriteWord(address, value);
		}

		public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			WriteWord(address, (ushort)(value >> 16));
			WriteWord(address + 2, (ushort)value);
		}

		public bool HasHostTrapStub(uint address)
		{
			_ = address;
			return false;
		}

		public bool TryInvokeHostTrap(uint instructionProgramCounter, ushort trapId, M68kCpuState state)
		{
			_ = instructionProgramCounter;
			_ = trapId;
			_ = state;
			return false;
		}

		public void ResetExternalDevices(long cycle)
		{
			_ = cycle;
		}

		public ushort ReadHostWord(uint address)
			=> ReadWord(address);

		public uint ReadHostLong(uint address)
			=> ReadLong(address);

		public void WriteHostWord(uint address, ushort value)
			=> WriteWord(address, value);

		public void WriteHostLong(uint address, uint value)
		{
			WriteWord(address, (ushort)(value >> 16));
			WriteWord(address + 2, (ushort)value);
		}

		public void CopyStackImage(uint source, uint destination, int count)
		{
			for (var i = 0; i < count; i++)
			{
				WriteByte(destination + (uint)i, ReadByte(source + (uint)i));
			}
		}

		private byte ReadByte(uint address)
		{
			if (TryTranslate(address, out var nativeAddress))
			{
				return Marshal.ReadByte(nativeAddress);
			}

			UnmappedReads++;
			FirstUnmappedRead ??= address & _addressMask;
			return 0;
		}

		private ushort ReadWord(uint address)
		{
			var high = ReadByte(address);
			var low = ReadByte(address + 1);
			return (ushort)((high << 8) | low);
		}

		private uint ReadLong(uint address)
			=> ((uint)ReadWord(address) << 16) | ReadWord(address + 2);

		private void WriteByte(uint address, byte value)
		{
			if (TryTranslate(address, out var nativeAddress))
			{
				Marshal.WriteByte(nativeAddress, value);
				return;
			}

			UnmappedWrites++;
			FirstUnmappedWrite ??= address & _addressMask;
		}

		private void WriteWord(uint address, ushort value)
		{
			WriteByte(address, (byte)(value >> 8));
			WriteByte(address + 1, (byte)value);
		}

		public string FormatRanges()
			=> $"mask=0x{_addressMask:X8}, " +
				$"low=[0x{_lowMemory.Start:X8},0x{_lowMemory.End:X8}), " +
				$"high=[0x{_highMemory.Start:X8},0x{_highMemory.End:X8}), " +
				$"test=[0x{_testMemory.Start:X8},0x{_testMemory.End:X8}), " +
				$"firstUnmappedRead={FormatOptionalAddress(FirstUnmappedRead)}, " +
				$"firstUnmappedWrite={FormatOptionalAddress(FirstUnmappedWrite)}";

		private static string FormatOptionalAddress(uint? address)
			=> address.HasValue ? $"0x{address.Value:X8}" : "none";

		private bool TryTranslate(uint address, out IntPtr nativeAddress)
		{
			var maskedAddress = address & _addressMask;
			if (TryTranslate(_lowMemory, maskedAddress, out nativeAddress) ||
				TryTranslate(_highMemory, maskedAddress, out nativeAddress) ||
				TryTranslate(_testMemory, maskedAddress, out nativeAddress))
			{
				return true;
			}

			nativeAddress = IntPtr.Zero;
			return false;
		}

		private static bool TryTranslate(NativeMemoryRange range, uint address, out IntPtr nativeAddress)
		{
			var offset = unchecked(address - range.Start);
			if (range.Buffer != IntPtr.Zero && offset < range.Size)
			{
				nativeAddress = IntPtr.Add(range.Buffer, checked((int)offset));
				return true;
			}

			nativeAddress = IntPtr.Zero;
			return false;
		}
	}
}
