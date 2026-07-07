using System.Buffers.Binary;
using System.Text;
using Copper68k;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Copper68k.Tests;

public sealed class M68kSingleStepConformanceTests
{
	private const string RunVariable = "COPPER68K_RUN_M68000_SINGLESTEP";
	private const string PathVariable = "COPPER68K_M68000_SINGLESTEP_PATH";
	private const string FilterVariable = "COPPER68K_M68000_SINGLESTEP_FILTER";
	private const string LimitVariable = "COPPER68K_M68000_SINGLESTEP_LIMIT";
	private const string IncludeUnverifiedVariable = "COPPER68K_M68000_SINGLESTEP_INCLUDE_UNVERIFIED";
	private const string ValidateCyclesVariable = "COPPER68K_M68000_SINGLESTEP_VALIDATE_CYCLES";
	private const string BackendVariable = "COPPER68K_M68000_SINGLESTEP_BACKEND";
	private const string DefaultCorpusRelativePath = "third_party/SingleStepTests.m68000/v1";
	private readonly ITestOutputHelper _output;

	public M68kSingleStepConformanceTests(ITestOutputHelper output)
	{
		_output = output;
	}

	[Fact]
	public void OfficialSingleStepCorpusMatchesInterpreterWhenEnabled()
	{
		if (!IsEnabled())
		{
			_output.WriteLine($"Set {RunVariable}=1 to run the SingleStepTests/m68000 conformance corpus.");
			return;
		}

		var corpusPath = ResolveCorpusPath();
		if (corpusPath is null)
		{
			throw new XunitException(
				$"SingleStepTests/m68000 corpus not found. Set {PathVariable} to the repo root or v1 folder, " +
				$"or clone it to {DefaultCorpusRelativePath}.");
		}

		var filter = Environment.GetEnvironmentVariable(FilterVariable);
		var includeUnverified = IsTruthy(Environment.GetEnvironmentVariable(IncludeUnverifiedVariable));
		var validateCycles = IsTruthy(Environment.GetEnvironmentVariable(ValidateCyclesVariable));
		var limit = ParseLimit(Environment.GetEnvironmentVariable(LimitVariable)) ?? int.MaxValue;
		var backend = ParseBackend(Environment.GetEnvironmentVariable(BackendVariable));
		var files = Directory.EnumerateFiles(corpusPath, "*.json.bin", SearchOption.TopDirectoryOnly)
			.Where(path => includeUnverified || IsVerifiedCorpusFile(path))
			.Where(path => string.IsNullOrWhiteSpace(filter) ||
				Path.GetFileName(path).Contains(filter, StringComparison.OrdinalIgnoreCase))
			.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		if (files.Length == 0)
		{
			throw new XunitException($"No SingleStepTests/m68000 files matched path '{corpusPath}' and filter '{filter}'.");
		}

		var executed = 0;
		foreach (var file in files)
		{
			var reader = new SingleStepBinaryReader(File.ReadAllBytes(file));
			foreach (var test in reader.ReadTests())
			{
				RunCase(file, test, validateCycles, backend);
				executed++;
				if (executed >= limit)
				{
					_output.WriteLine($"Executed {executed} SingleStepTests/m68000 case(s) with {backend} backend.");
					return;
				}
			}
		}

		_output.WriteLine($"Executed {executed} SingleStepTests/m68000 case(s) with {backend} backend.");
	}

	private static bool IsEnabled()
		=> IsTruthy(Environment.GetEnvironmentVariable(RunVariable));

	private static bool IsTruthy(string? value)
		=> value is not null &&
			(value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
			 value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
			 value.Equals("yes", StringComparison.OrdinalIgnoreCase));

	private static int? ParseLimit(string? value)
	{
		return int.TryParse(value, out var limit) && limit > 0
			? limit
			: null;
	}

	private static SingleStepBackend ParseBackend(string? value)
	{
		if (string.IsNullOrWhiteSpace(value) ||
			value.Equals("interpreter", StringComparison.OrdinalIgnoreCase))
		{
			return SingleStepBackend.Interpreter;
		}

		if (value.Equals("jit", StringComparison.OrdinalIgnoreCase))
		{
			return SingleStepBackend.Jit;
		}

		throw new XunitException(
			$"Invalid {BackendVariable} value '{value}'. Expected 'interpreter' or 'jit'.");
	}

	private static bool IsVerifiedCorpusFile(string path)
	{
		var fileName = Path.GetFileName(path);
		return !fileName.Equals("TAS.json.bin", StringComparison.OrdinalIgnoreCase) &&
			!fileName.Equals("TRAPV.json.bin", StringComparison.OrdinalIgnoreCase);
	}

	private static string? ResolveCorpusPath()
	{
		var configuredPath = Environment.GetEnvironmentVariable(PathVariable);
		if (!string.IsNullOrWhiteSpace(configuredPath))
		{
			return ResolveV1Path(configuredPath);
		}

		var root = FindRepositoryRoot();
		return root is null
			? null
			: ResolveV1Path(Path.Combine(root, DefaultCorpusRelativePath.Replace('/', Path.DirectorySeparatorChar)));
	}

	private static string? ResolveV1Path(string path)
	{
		var fullPath = Path.GetFullPath(path);
		if (Directory.Exists(fullPath) &&
			Directory.EnumerateFiles(fullPath, "*.json.bin", SearchOption.TopDirectoryOnly).Any())
		{
			return fullPath;
		}

		var v1Path = Path.Combine(fullPath, "v1");
		return Directory.Exists(v1Path) &&
			Directory.EnumerateFiles(v1Path, "*.json.bin", SearchOption.TopDirectoryOnly).Any()
				? v1Path
				: null;
	}

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

	private static void RunCase(string file, SingleStepCase test, bool validateCycles, SingleStepBackend backend)
	{
		var bus = new CorpusBus();
		foreach (var word in test.Initial.Ram)
		{
			bus.WriteWord(word.Address, word.Value);
		}

		var cpu = CreateCore(bus, backend);
		cpu.Reset(ToArchitecturalPc(test.Initial.Pc), test.Initial.Ssp);
		ApplyState(cpu.State, test.Initial);

		int cycles;
		try
		{
			cycles = cpu.ExecuteInstruction();
		}
		catch (Exception ex)
		{
			throw new XunitException($"{CasePrefix(file, test, backend)} threw {ex.GetType().Name}: {ex.Message}");
		}

		if (validateCycles)
		{
			AssertEqual(file, test, backend, "cycles", test.Cycles, (uint)cycles);
		}

		AssertState(file, test, backend, cpu.State);
		AssertRam(file, test, backend, bus);
	}

	private static IM68kCore CreateCore(IM68kBus bus, SingleStepBackend backend)
		=> backend switch
		{
			SingleStepBackend.Interpreter => new M68kInterpreter(bus),
			SingleStepBackend.Jit => M68kJitCore.CreateM68000(bus),
			_ => throw new InvalidOperationException($"Invalid SingleStepTests/m68000 backend: {backend}.")
		};

	private static void ApplyState(M68kCpuState state, SingleStepState expected)
	{
		Array.Copy(expected.D, state.D, state.D.Length);
		for (var i = 0; i < 7; i++)
		{
			state.A[i] = expected.A[i];
		}

		state.ProgramCounter = ToArchitecturalPc(expected.Pc);
		state.ResetStackPointers(expected.Ssp, expected.Usp, (expected.Sr & M68kCpuState.Supervisor) != 0);
		state.StatusRegister = expected.Sr;
	}

	private static uint ToArchitecturalPc(uint corpusPc)
		=> corpusPc - 4;

	private static uint ToFinalArchitecturalPc(string file, SingleStepCase test)
	{
		if (Path.GetFileName(file).Equals("STOP.json.bin", StringComparison.OrdinalIgnoreCase) &&
			test.Final.Pc == test.Initial.Pc)
		{
			return test.Final.Pc;
		}

		return ToArchitecturalPc(test.Final.Pc);
	}

	private static void AssertState(string file, SingleStepCase test, SingleStepBackend backend, M68kCpuState actual)
	{
		for (var i = 0; i < 8; i++)
		{
			AssertEqual(file, test, backend, $"D{i}", test.Final.D[i], actual.D[i]);
		}

		for (var i = 0; i < 7; i++)
		{
			AssertEqual(file, test, backend, $"A{i}", test.Final.A[i], actual.A[i]);
		}

		AssertEqual(file, test, backend, "USP", test.Final.Usp, actual.UserStackPointer);
		AssertEqual(file, test, backend, "SSP", test.Final.Ssp, actual.SupervisorStackPointer);
		AssertEqual(file, test, backend, "SR", test.Final.Sr, actual.StatusRegister);
		AssertEqual(file, test, backend, "PC", ToFinalArchitecturalPc(file, test), actual.ProgramCounter);
	}

	private static void AssertRam(string file, SingleStepCase test, SingleStepBackend backend, CorpusBus bus)
	{
		foreach (var expected in test.Final.Ram)
		{
			var actual = bus.ReadWord(expected.Address);
			AssertEqual(file, test, backend, $"RAM[{expected.Address:X6}]", expected.Value, actual);
		}
	}

	private static void AssertEqual(
		string file,
		SingleStepCase test,
		SingleStepBackend backend,
		string field,
		uint expected,
		uint actual)
	{
		if (expected != actual)
		{
			throw new XunitException(
				$"{CasePrefix(file, test, backend)} mismatch in {field}: expected 0x{expected:X8}, actual 0x{actual:X8}.");
		}
	}

	private static void AssertEqual(
		string file,
		SingleStepCase test,
		SingleStepBackend backend,
		string field,
		ushort expected,
		ushort actual)
	{
		if (expected != actual)
		{
			throw new XunitException(
				$"{CasePrefix(file, test, backend)} mismatch in {field}: expected 0x{expected:X4}, actual 0x{actual:X4}.");
		}
	}

	private static string CasePrefix(string file, SingleStepCase test, SingleStepBackend backend)
		=> $"{backend}:{Path.GetFileName(file)}::{test.Name}";

	private enum SingleStepBackend
	{
		Interpreter,
		Jit
	}

	private sealed class CorpusBus : IM68kBus, IM68kCodeReader
	{
		private readonly byte[] _memory = new byte[0x0100_0000];

		public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			return _memory[Offset(address)];
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
			return ((uint)ReadWord(address) << 16) | ReadWord(address + 2);
		}

		public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			_memory[Offset(address)] = value;
		}

		public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			WriteWord(address, value);
		}

		public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
		{
			WriteWord(address, (ushort)(value >> 16), ref cycle, accessKind);
			WriteWord(address + 2, (ushort)value, ref cycle, accessKind);
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

		public ushort ReadWord(uint address)
		{
			return (ushort)((_memory[Offset(address)] << 8) | _memory[Offset(address + 1)]);
		}

		public void WriteWord(uint address, ushort value)
		{
			_memory[Offset(address)] = (byte)(value >> 8);
			_memory[Offset(address + 1)] = (byte)value;
		}

		private static int Offset(uint address)
			=> (int)(address & 0x00FF_FFFF);
	}

	private sealed class SingleStepBinaryReader
	{
		private static readonly string[] RegisterOrder =
		{
			"d0", "d1", "d2", "d3", "d4", "d5", "d6", "d7",
			"a0", "a1", "a2", "a3", "a4", "a5", "a6", "usp",
			"ssp", "sr", "pc"
		};

		private readonly byte[] _content;
		private int _offset;

		public SingleStepBinaryReader(byte[] content)
		{
			_content = content;
		}

		public IEnumerable<SingleStepCase> ReadTests()
		{
			Expect(ReadUInt32(), 0x1A3F5D71, "file magic");
			var count = ReadUInt32();
			for (var i = 0; i < count; i++)
			{
				yield return ReadTest();
			}
		}

		private SingleStepCase ReadTest()
		{
			ReadRecordHeader(0xABC12367, "test");
			var name = ReadName();
			var initial = ReadState();
			var final = ReadState();
			var cycles = ReadTransactions();
			return new SingleStepCase(name, initial, final, cycles);
		}

		private string ReadName()
		{
			ReadRecordHeader(0x89ABCDEF, "name");
			var length = checked((int)ReadUInt32());
			var name = Encoding.UTF8.GetString(_content, _offset, length);
			_offset += length;
			return name;
		}

		private SingleStepState ReadState()
		{
			ReadRecordHeader(0x01234567, "state");
			var values = new Dictionary<string, uint>(StringComparer.Ordinal);
			foreach (var register in RegisterOrder)
			{
				values.Add(register, ReadUInt32());
			}

			_ = ReadUInt32();
			_ = ReadUInt32();
			var ramWordCount = ReadUInt32();
			var ram = new RamWord[ramWordCount];
			for (var i = 0; i < ram.Length; i++)
			{
				ram[i] = new RamWord(ReadUInt32(), ReadUInt16());
			}

			return new SingleStepState(
				new[]
				{
					values["d0"], values["d1"], values["d2"], values["d3"],
					values["d4"], values["d5"], values["d6"], values["d7"]
				},
				new[]
				{
					values["a0"], values["a1"], values["a2"], values["a3"],
					values["a4"], values["a5"], values["a6"]
				},
				values["usp"],
				values["ssp"],
				(ushort)values["sr"],
				values["pc"],
				ram);
		}

		private uint ReadTransactions()
		{
			ReadRecordHeader(0x456789AB, "transactions");
			var cycles = ReadUInt32();
			var count = ReadUInt32();
			for (var i = 0; i < count; i++)
			{
				var kind = ReadByte();
				_ = ReadUInt32();
				if (kind == 0)
				{
					continue;
				}

				_offset += 20;
			}

			return cycles;
		}

		private void ReadRecordHeader(uint expectedMagic, string label)
		{
			_ = ReadUInt32();
			Expect(ReadUInt32(), expectedMagic, label);
		}

		private byte ReadByte()
		{
			return _content[_offset++];
		}

		private ushort ReadUInt16()
		{
			var value = BinaryPrimitives.ReadUInt16LittleEndian(_content.AsSpan(_offset, 2));
			_offset += 2;
			return value;
		}

		private uint ReadUInt32()
		{
			var value = BinaryPrimitives.ReadUInt32LittleEndian(_content.AsSpan(_offset, 4));
			_offset += 4;
			return value;
		}

		private static void Expect(uint actual, uint expected, string label)
		{
			if (actual != expected)
			{
				throw new InvalidDataException($"Invalid SingleStepTests/m68000 {label}: expected 0x{expected:X8}, got 0x{actual:X8}.");
			}
		}
	}

	private sealed record SingleStepCase(string Name, SingleStepState Initial, SingleStepState Final, uint Cycles);

	private sealed record SingleStepState(
		uint[] D,
		uint[] A,
		uint Usp,
		uint Ssp,
		ushort Sr,
		uint Pc,
		IReadOnlyList<RamWord> Ram);

	private readonly record struct RamWord(uint Address, ushort Value);
}
