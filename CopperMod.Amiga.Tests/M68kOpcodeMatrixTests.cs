using CopperMod.Amiga;
using Xunit.Abstractions;

namespace CopperMod.Amiga.Tests;

public sealed class M68kOpcodeMatrixTests
{
	private readonly ITestOutputHelper _output;

	public M68kOpcodeMatrixTests(ITestOutputHelper output)
	{
		_output = output;
	}

	public static IEnumerable<object[]> ExecutableRows =>
		M68kOpcodeMatrix.Rows
			.Where(row => row.Status == MatrixStatus.Executable)
			.Select(row => new object[] { row });

	[Fact]
	public void MatrixIncludesEveryDocumentedMc68000InstructionFamily()
	{
		var actual = M68kOpcodeMatrix.Rows
			.Select(row => row.Mnemonic)
			.Distinct()
			.OrderBy(mnemonic => mnemonic)
			.ToArray();

		var missing = M68kOpcodeMatrix.ExpectedMc68000Mnemonics
			.Except(actual)
			.OrderBy(mnemonic => mnemonic)
			.ToArray();

		Assert.Empty(missing);
	}

	[Fact]
	public void MatrixRowsHaveSampleOpcodesAndExplicitPendingReasons()
	{
		Assert.All(M68kOpcodeMatrix.Rows, row =>
		{
			Assert.NotEmpty(row.SampleWords);

			if (row.Status == MatrixStatus.Pending)
			{
				Assert.False(string.IsNullOrWhiteSpace(row.PendingReason));
			}
			else
			{
				Assert.Null(row.PendingReason);
				Assert.NotNull(row.ExecutionCase);
			}
		});
	}

	[Fact]
	public void MatrixRowsAreUniqueByDocumentedLegalCombination()
	{
		var duplicates = M68kOpcodeMatrix.Rows
			.GroupBy(row => row.CombinationKey)
			.Where(group => group.Count() > 1)
			.Select(group => group.Key)
			.OrderBy(key => key)
			.ToArray();

		Assert.Empty(duplicates);
	}

	[Fact]
	public void ExecutableRowsUseUniqueSampleInstructionStreams()
	{
		var duplicates = M68kOpcodeMatrix.Rows
			.Where(row => row.Status == MatrixStatus.Executable)
			.GroupBy(row => row.SampleStream)
			.Where(group => group.Count() > 1)
			.Select(group => string.Join(", ", group.Select(row => row.DisplayName).OrderBy(name => name)))
			.ToArray();

		Assert.Empty(duplicates);
	}

	[Fact]
	public void IllegalEffectiveAddressCombinationsAreExcludedWithReasons()
	{
		Assert.NotEmpty(M68kOpcodeMatrix.Exclusions);
		Assert.All(M68kOpcodeMatrix.Exclusions, exclusion =>
		{
			Assert.False(string.IsNullOrWhiteSpace(exclusion.Reason));
			Assert.DoesNotContain(
				M68kOpcodeMatrix.Rows,
				row =>
					row.Mnemonic == exclusion.Mnemonic &&
					row.Variant == exclusion.Variant &&
					row.Size == exclusion.Size &&
					row.Source == exclusion.Source &&
					row.Destination == exclusion.Destination);
		});
	}

	[Fact]
	public void PendingRowsAreReportedByMnemonic()
	{
		var pendingReport = M68kOpcodeMatrix.Rows
			.Where(row => row.Status == MatrixStatus.Pending)
			.GroupBy(row => row.Mnemonic)
			.OrderBy(group => group.Key)
			.Select(group =>
			{
				var reasons = group
					.GroupBy(row => row.PendingReason!)
					.OrderBy(reason => reason.Key)
					.Select(reason => $"{reason.Key}:{reason.Count()}");
				return $"{group.Key}={group.Count()} ({string.Join(", ", reasons)})";
			})
			.ToArray();

		_output.WriteLine(string.Join(Environment.NewLine, pendingReport));

		Assert.Contains(pendingReport, line => line.StartsWith("ABCD=", StringComparison.Ordinal));
		Assert.Contains(pendingReport, line => line.StartsWith("MOVE=", StringComparison.Ordinal));
		Assert.Contains(pendingReport, line => line.StartsWith("MOVEP=", StringComparison.Ordinal));
		Assert.DoesNotContain(M68kOpcodeMatrix.Rows, row => row.Status == MatrixStatus.Pending && row.PendingReason == "Skipped");
	}

	[Theory]
	[MemberData(nameof(ExecutableRows))]
	public void ExecutableMatrixRowsRunOneInstruction(object rowObject)
	{
		var row = Assert.IsType<M68kMatrixRow>(rowObject);
		var bus = new MatrixBus();
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(M68kOpcodeMatrix.ProgramAddress, M68kOpcodeMatrix.SupervisorStackAddress);
		M68kOpcodeMatrix.PrepareDefaultMachine(bus, cpu, row);
		row.ExecutionCase!.Prepare(bus, cpu, row);

		var cyclesBefore = cpu.State.Cycles;
		cpu.ExecuteInstruction();

		Assert.True(cpu.State.Cycles > cyclesBefore);
		row.ExecutionCase.Verify(bus, cpu, row);
	}

	private sealed class MatrixBus : IM68kBus
	{
		public byte[] Memory { get; } = new byte[0x0100_0000];

		public List<(uint Address, AmigaBusAccessKind Kind, AmigaBusAccessSize Size, bool IsWrite)> Accesses { get; } = new();

		public int ExternalResetCount { get; private set; }

		public byte ReadByte(uint address, ref long cycle, AmigaBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, AmigaBusAccessSize.Byte, false));
			return Memory[address];
		}

		public ushort ReadWord(uint address, ref long cycle, AmigaBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, AmigaBusAccessSize.Word, false));
			return (ushort)((Memory[address] << 8) | Memory[address + 1]);
		}

		public uint ReadLong(uint address, ref long cycle, AmigaBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, AmigaBusAccessSize.Long, false));
			return ((uint)Memory[address] << 24) |
				((uint)Memory[address + 1] << 16) |
				((uint)Memory[address + 2] << 8) |
				Memory[address + 3];
		}

		public void WriteByte(uint address, byte value, ref long cycle, AmigaBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, AmigaBusAccessSize.Byte, true));
			Memory[address] = value;
		}

		public void WriteWord(uint address, ushort value, ref long cycle, AmigaBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, AmigaBusAccessSize.Word, true));
			WriteWordRaw(address, value);
		}

		public void WriteLong(uint address, uint value, ref long cycle, AmigaBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, AmigaBusAccessSize.Long, true));
			WriteLongRaw(address, value);
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
			ExternalResetCount++;
		}

		public void WriteWordRaw(uint address, ushort value)
		{
			Memory[address] = (byte)(value >> 8);
			Memory[address + 1] = (byte)value;
		}

		public void WriteLongRaw(uint address, uint value)
		{
			Memory[address] = (byte)(value >> 24);
			Memory[address + 1] = (byte)(value >> 16);
			Memory[address + 2] = (byte)(value >> 8);
			Memory[address + 3] = (byte)value;
		}
	}

	private enum MatrixStatus
	{
		Executable,
		Pending
	}

	private enum MatrixSize
	{
		None,
		Byte,
		Word,
		Long
	}

	private enum EaKind
	{
		DataRegister,
		AddressRegister,
		AddressIndirect,
		AddressPostIncrement,
		AddressPreDecrement,
		AddressDisplacement,
		AddressIndex,
		AbsoluteWord,
		AbsoluteLong,
		PcDisplacement,
		PcIndex,
		Immediate,
		Implied,
		Quick,
		RegisterList,
		BranchDisplacement,
		TrapVector,
		Ccr,
		Sr,
		Usp
	}

	private sealed record EaForm(string Name, EaKind Kind, int Mode, int Register)
	{
		public int Code => Mode < 0 ? 0 : (Mode << 3) | Register;

		public bool IsEncoded => Mode >= 0;

		public ushort[] Extensions(MatrixSize size)
		{
			return Kind switch
			{
				EaKind.AddressDisplacement => new ushort[] { 0x0010 },
				EaKind.AddressIndex => new ushort[] { 0x0004 },
				EaKind.AbsoluteWord => new ushort[] { 0x2200 },
				EaKind.AbsoluteLong => new ushort[] { 0x0000, 0x2200 },
				EaKind.PcDisplacement => new ushort[] { 0x0010 },
				EaKind.PcIndex => new ushort[] { 0x0004 },
				EaKind.Immediate => ImmediateWords(size),
				_ => Array.Empty<ushort>()
			};
		}

		private static ushort[] ImmediateWords(MatrixSize size)
		{
			return size == MatrixSize.Long
				? new ushort[] { 0x1234, 0x5678 }
				: new ushort[] { 0x0012 };
		}
	}

	private sealed record M68kExecutionCase(
		Action<MatrixBus, M68kInterpreter, M68kMatrixRow> Prepare,
		Action<MatrixBus, M68kInterpreter, M68kMatrixRow> Verify);

	private sealed record M68kMatrixRow(
		string Family,
		string Mnemonic,
		string Variant,
		MatrixSize Size,
		string Source,
		string Destination,
		bool Privileged,
		int? ExpectedVector,
		ushort[] SampleWords,
		MatrixStatus Status,
		string? PendingReason,
		M68kExecutionCase? ExecutionCase)
	{
		public string DisplayName => $"{Mnemonic} {Variant} {Size} {Source}->{Destination}";

		public string CombinationKey => $"{Mnemonic}|{Variant}|{Size}|{Source}|{Destination}";

		public string SampleStream => string.Join(" ", SampleWords.Select(word => word.ToString("X4")));

		public override string ToString() => DisplayName;
	}

	private sealed record M68kMatrixExclusion(
		string Mnemonic,
		string Variant,
		MatrixSize Size,
		string Source,
		string Destination,
		string Reason);

	private static class M68kOpcodeMatrix
	{
		public const uint ProgramAddress = 0x1000;
		public const uint SupervisorStackAddress = 0x8000;

		private static readonly EaForm Dn = new("Dn", EaKind.DataRegister, 0, 0);
		private static readonly EaForm An = new("An", EaKind.AddressRegister, 1, 0);
		private static readonly EaForm Indirect = new("(An)", EaKind.AddressIndirect, 2, 0);
		private static readonly EaForm PostIncrement = new("(An)+", EaKind.AddressPostIncrement, 3, 0);
		private static readonly EaForm PreDecrement = new("-(An)", EaKind.AddressPreDecrement, 4, 0);
		private static readonly EaForm Displacement = new("d16(An)", EaKind.AddressDisplacement, 5, 0);
		private static readonly EaForm Index = new("d8(An,Xn)", EaKind.AddressIndex, 6, 0);
		private static readonly EaForm AbsoluteWord = new("abs.W", EaKind.AbsoluteWord, 7, 0);
		private static readonly EaForm AbsoluteLong = new("abs.L", EaKind.AbsoluteLong, 7, 1);
		private static readonly EaForm PcDisplacement = new("d16(PC)", EaKind.PcDisplacement, 7, 2);
		private static readonly EaForm PcIndex = new("d8(PC,Xn)", EaKind.PcIndex, 7, 3);
		private static readonly EaForm Immediate = new("#imm", EaKind.Immediate, 7, 4);
		private static readonly EaForm Implied = new("implied", EaKind.Implied, -1, 0);
		private static readonly EaForm Quick = new("#quick", EaKind.Quick, -1, 0);
		private static readonly EaForm RegisterList = new("register-list", EaKind.RegisterList, -1, 0);
		private static readonly EaForm BranchDisplacement = new("displacement", EaKind.BranchDisplacement, -1, 0);
		private static readonly EaForm TrapVector = new("#vector", EaKind.TrapVector, -1, 0);
		private static readonly EaForm Ccr = new("CCR", EaKind.Ccr, -1, 0);
		private static readonly EaForm Sr = new("SR", EaKind.Sr, -1, 0);
		private static readonly EaForm Usp = new("USP", EaKind.Usp, -1, 0);
		private static readonly EaForm PostIncrementA7 = new("(A7)+", EaKind.AddressPostIncrement, 3, 7);
		private static readonly EaForm PreDecrementA7 = new("-(A7)", EaKind.AddressPreDecrement, 4, 7);

		private static readonly MatrixSize[] ByteWordLong = { MatrixSize.Byte, MatrixSize.Word, MatrixSize.Long };
		private static readonly MatrixSize[] WordLong = { MatrixSize.Word, MatrixSize.Long };

		public static IReadOnlyList<M68kMatrixRow> Rows { get; } = BuildRows();

		public static IReadOnlyList<M68kMatrixExclusion> Exclusions { get; } = BuildExclusions();

		public static IReadOnlySet<string> ExpectedMc68000Mnemonics { get; } = new HashSet<string>
		{
			"ABCD",
			"ADD",
			"ADDA",
			"ADDI",
			"ADDQ",
			"ADDX",
			"AND",
			"ANDI",
			"ANDI to CCR",
			"ANDI to SR",
			"ASL",
			"ASR",
			"Bcc",
			"BCHG",
			"BCLR",
			"BRA",
			"BSR",
			"BSET",
			"BTST",
			"CHK",
			"CLR",
			"CMP",
			"CMPA",
			"CMPI",
			"CMPM",
			"DBcc",
			"DIVS",
			"DIVU",
			"EOR",
			"EORI",
			"EORI to CCR",
			"EORI to SR",
			"EXG",
			"EXT",
			"ILLEGAL",
			"JMP",
			"JSR",
			"LEA",
			"Line-A",
			"Line-F",
			"LINK",
			"LSL",
			"LSR",
			"MOVE",
			"MOVE from SR",
			"MOVE to CCR",
			"MOVE to SR",
			"MOVE USP",
			"MOVEA",
			"MOVEC",
			"MOVEM",
			"MOVEP",
			"MOVEQ",
			"MULS",
			"MULU",
			"NBCD",
			"NEG",
			"NEGX",
			"NOP",
			"NOT",
			"OR",
			"ORI",
			"ORI to CCR",
			"ORI to SR",
			"PEA",
			"RESET",
			"ROL",
			"ROR",
			"ROXL",
			"ROXR",
			"RTE",
			"RTR",
			"RTS",
			"SBCD",
			"Scc",
			"STOP",
			"SUB",
			"SUBA",
			"SUBI",
			"SUBQ",
			"SUBX",
			"SWAP",
			"TAS",
			"TRAP",
			"TRAPV",
			"TST",
			"UNLK"
		};

		private static IReadOnlyList<M68kMatrixRow> BuildRows()
		{
			var rows = new List<M68kMatrixRow>();

			AddMoveRows(rows);
			AddImmediateRows(rows);
			AddBitRows(rows);
			AddUnaryRows(rows);
			AddLine4Rows(rows);
			AddLine5Rows(rows);
			AddBranchRows(rows);
			AddArithmeticRows(rows);
			AddShiftRotateRows(rows);
			AddBcdAndMiscPendingRows(rows);
			AddCpuGenerationRows(rows);

			return rows
				.OrderBy(row => row.Mnemonic)
				.ThenBy(row => row.Variant)
				.ThenBy(row => row.Size)
				.ThenBy(row => row.Source)
				.ThenBy(row => row.Destination)
				.ToArray();
		}

		private static IReadOnlyList<M68kMatrixExclusion> BuildExclusions()
		{
			var exclusions = new List<M68kMatrixExclusion>();
			var all = AllCanonicalEa().ToArray();

			foreach (var size in ByteWordLong)
			{
				AddDestinationExclusions(exclusions, "NEG", "data-alterable", size, Implied, DataAlterable(size), all, "Destination must be data alterable.");
				AddDestinationExclusions(exclusions, "TST", "data-alterable", size, Implied, DataAlterable(size), all, "Destination must be data alterable on MC68000.");
				AddDestinationExclusions(exclusions, "ADDQ", "quick", size, Quick, QuickAlterable(size), all, "Quick destination must be alterable; byte size cannot target An.");
				AddDestinationExclusions(exclusions, "Scc", "condition", MatrixSize.Byte, Implied, DataAlterable(MatrixSize.Byte), all, "Scc writes a byte to a data-alterable destination.");
			}

			foreach (var size in WordLong)
			{
				AddSourceExclusions(exclusions, "LEA", "address", size, Control(), An, all, "LEA source must be a control addressing mode.");
				AddSourceExclusions(exclusions, "JMP", "control", MatrixSize.None, Control(), Implied, all, "JMP target must be a control addressing mode.");
				AddSourceExclusions(exclusions, "JSR", "control", MatrixSize.None, Control(), Implied, all, "JSR target must be a control addressing mode.");
			}

			AddDestinationExclusions(exclusions, "MOVE to SR", "privileged", MatrixSize.Word, Sr, Data(MatrixSize.Word), all, "MOVE to SR source must be a data addressing mode.");
			AddDestinationExclusions(exclusions, "MOVEM", "registers-to-memory", MatrixSize.Long, RegisterList, MovemRegisterToMemory(), all, "MOVEM register-to-memory cannot use Dn, An, postincrement, PC-relative, or immediate destinations.");
			AddSourceExclusions(exclusions, "MOVEM", "memory-to-registers", MatrixSize.Long, MovemMemoryToRegister(), RegisterList, all, "MOVEM memory-to-register cannot use Dn, An, predecrement, or immediate sources.");

			return exclusions
				.GroupBy(exclusion => $"{exclusion.Mnemonic}|{exclusion.Variant}|{exclusion.Size}|{exclusion.Source}|{exclusion.Destination}")
				.Select(group => group.First())
				.OrderBy(exclusion => exclusion.Mnemonic)
				.ThenBy(exclusion => exclusion.Variant)
				.ThenBy(exclusion => exclusion.Size)
				.ThenBy(exclusion => exclusion.Source)
				.ThenBy(exclusion => exclusion.Destination)
				.ToArray();
		}

		public static void PrepareDefaultMachine(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			WriteWords(bus, ProgramAddress, row.SampleWords);

			for (var vector = 0; vector < 64; vector++)
			{
				bus.WriteLongRaw((uint)(vector * 4), 0x9000u + (uint)(vector * 0x10));
			}

			cpu.State.D[0] = 0x0000_0011;
			cpu.State.D[1] = 0x0000_0002;
			cpu.State.D[2] = 0x1234_5678;
			cpu.State.D[3] = 0x8765_4321;
			cpu.State.A[0] = 0x3000;
			cpu.State.A[1] = 0x3100;
			cpu.State.A[2] = 0x3200;
			cpu.State.A[3] = 0x3300;
			cpu.State.A[4] = 0x3400;
			cpu.State.A[5] = 0x3500;
			cpu.State.A[6] = 0x3600;
			cpu.State.StatusRegister = M68kCpuState.Supervisor;

			for (var address = 0x2000; address < 0x3800; address += 2)
			{
				bus.WriteWordRaw((uint)address, (ushort)(0x1000 + address));
			}

			bus.WriteLongRaw(0x7FFC, 0x0000_A000);
		}

		private static void AddMoveRows(List<M68kMatrixRow> rows)
		{
			foreach (var size in ByteWordLong)
			{
				foreach (var source in MoveSources(size))
				{
					foreach (var destination in DataAlterable(size))
					{
						Add(
							rows,
							"Move",
							"MOVE",
							"data-move",
							size,
							source,
							destination,
							privileged: false,
							expectedVector: null,
							EncodeMove(size, source, destination));
					}
				}
			}

			Add(
				rows,
				"Move",
				"MOVE",
				"a7-byte-postincrement-source",
				MatrixSize.Byte,
				PostIncrementA7,
				Dn,
				privileged: false,
				expectedVector: null,
				EncodeMove(MatrixSize.Byte, PostIncrementA7, Dn));
			Add(
				rows,
				"Move",
				"MOVE",
				"a7-byte-predecrement-destination",
				MatrixSize.Byte,
				Immediate,
				PreDecrementA7,
				privileged: false,
				expectedVector: null,
				EncodeMove(MatrixSize.Byte, Immediate, PreDecrementA7));

			foreach (var size in WordLong)
			{
				foreach (var source in MoveSources(size))
				{
					Add(
						rows,
						"Move",
						"MOVEA",
						"address-move",
						size,
						source,
						An,
						privileged: false,
						expectedVector: null,
						EncodeMoveA(size, source));
				}
			}

			Add(
				rows,
				"Move",
				"MOVEQ",
				"quick-immediate",
				MatrixSize.Long,
				Quick,
				Dn,
				privileged: false,
				expectedVector: null,
				new ushort[] { 0x7012 });
		}

		private static void AddImmediateRows(List<M68kMatrixRow> rows)
		{
			var immediateOps = new[]
			{
				("ORI", 0x0000),
				("ANDI", 0x0200),
				("SUBI", 0x0400),
				("ADDI", 0x0600),
				("EORI", 0x0A00),
				("CMPI", 0x0C00)
			};

			foreach (var (mnemonic, baseOpcode) in immediateOps)
			{
				foreach (var size in ByteWordLong)
				{
					foreach (var destination in DataAlterable(size))
					{
						Add(
							rows,
							"Immediate",
							mnemonic,
							"immediate",
							size,
							Immediate,
							destination,
							privileged: false,
							expectedVector: null,
							EncodeImmediate(baseOpcode, size, destination));
					}
				}
			}

			AddStatusImmediate(rows, "ORI to CCR", 0x003C, Ccr, privileged: false);
			AddStatusImmediate(rows, "ORI to SR", 0x007C, Sr, privileged: true);
			AddStatusImmediate(rows, "ANDI to CCR", 0x023C, Ccr, privileged: false);
			AddStatusImmediate(rows, "ANDI to SR", 0x027C, Sr, privileged: true);
			AddStatusImmediate(rows, "EORI to CCR", 0x0A3C, Ccr, privileged: false);
			AddStatusImmediate(rows, "EORI to SR", 0x0A7C, Sr, privileged: true);
		}

		private static void AddBitRows(List<M68kMatrixRow> rows)
		{
			var bitOps = new[]
			{
				("BTST", 0),
				("BCHG", 1),
				("BCLR", 2),
				("BSET", 3)
			};

			foreach (var (mnemonic, operation) in bitOps)
			{
				foreach (var destination in BitDestinations())
				{
					Add(
						rows,
						"Bit",
						mnemonic,
						"static",
						destination.Kind == EaKind.DataRegister ? MatrixSize.Long : MatrixSize.Byte,
						Immediate,
						destination,
						privileged: false,
						expectedVector: null,
						Append(EncodeStaticBitOperation(operation, destination), new ushort[] { 0x0003 }));
					Add(
						rows,
						"Bit",
						mnemonic,
						"dynamic",
						destination.Kind == EaKind.DataRegister ? MatrixSize.Long : MatrixSize.Byte,
						Dn,
						destination,
						privileged: false,
						expectedVector: null,
						EncodeDynamicBitOperation(operation, destination));
				}
			}
		}

		private static void AddUnaryRows(List<M68kMatrixRow> rows)
		{
			var unaryOps = new[]
			{
				("NEGX", 0x4000),
				("CLR", 0x4200),
				("NEG", 0x4400),
				("NOT", 0x4600),
				("TST", 0x4A00)
			};

			foreach (var (mnemonic, baseOpcode) in unaryOps)
			{
				foreach (var size in ByteWordLong)
				{
					foreach (var destination in DataAlterable(size))
					{
						Add(
							rows,
							"Unary",
							mnemonic,
							"data-alterable",
							size,
							Implied,
							destination,
							privileged: false,
							expectedVector: null,
							EncodeUnary(baseOpcode, size, destination));
					}
				}
			}
		}

		private static void AddLine4Rows(List<M68kMatrixRow> rows)
		{
			foreach (var destination in DataAlterable(MatrixSize.Word))
			{
				Add(rows, "Status", "MOVE from SR", "status-source", MatrixSize.Word, Sr, destination, false, null, EncodeEaBase(0x40C0, destination));
				Add(rows, "Status", "MOVE to CCR", "status-destination", MatrixSize.Word, destination, Ccr, false, null, EncodeEaBase(0x44C0, destination));
				Add(rows, "Status", "MOVE to SR", "privileged", MatrixSize.Word, destination, Sr, true, null, EncodeEaBase(0x46C0, destination));
			}

			Add(rows, "System", "SWAP", "data-register", MatrixSize.Word, Dn, Dn, false, null, new ushort[] { 0x4840 });
			Add(rows, "System", "EXT", "byte-to-word", MatrixSize.Word, Dn, Dn, false, null, new ushort[] { 0x4880 });
			Add(rows, "System", "EXT", "word-to-long", MatrixSize.Long, Dn, Dn, false, null, new ushort[] { 0x48C0 });

			foreach (var source in Control())
			{
				Add(rows, "Address", "PEA", "push-effective-address", MatrixSize.Long, source, PreDecrement, false, null, EncodeEaBase(0x4840, source));
				Add(rows, "Address", "LEA", "address", MatrixSize.Long, source, An, false, null, EncodeLea(source));
				Add(rows, "Control", "JSR", "control", MatrixSize.None, source, Implied, false, null, EncodeEaBase(0x4E80, source));
				Add(rows, "Control", "JMP", "control", MatrixSize.None, source, Implied, false, null, EncodeEaBase(0x4EC0, source));
			}

			foreach (var size in WordLong)
			{
				foreach (var destination in MovemRegisterToMemory())
				{
					Add(rows, "Move", "MOVEM", "registers-to-memory", size, RegisterList, destination, false, null, EncodeMovem(size, memoryToRegisters: false, destination));
				}

				foreach (var source in MovemMemoryToRegister())
				{
					Add(rows, "Move", "MOVEM", "memory-to-registers", size, source, RegisterList, false, null, EncodeMovem(size, memoryToRegisters: true, source));
				}
			}

			foreach (var register in AddressRegisterSpecials())
			{
				Add(rows, "Stack", "LINK", "frame", MatrixSize.Word, register, PreDecrement, false, null, new ushort[] { (ushort)(0x4E50 | register.Register), 0xFFF0 });
				Add(rows, "Stack", "UNLK", "frame", MatrixSize.Long, register, PostIncrement, false, null, new ushort[] { (ushort)(0x4E58 | register.Register) });
				Add(rows, "Status", "MOVE USP", "to-usp", MatrixSize.Long, register, Usp, true, null, new ushort[] { (ushort)(0x4E60 | register.Register) });
				Add(rows, "Status", "MOVE USP", "from-usp", MatrixSize.Long, Usp, register, true, null, new ushort[] { (ushort)(0x4E68 | register.Register) });
			}

			AddFixed(rows, "System", "TRAP", "vector", MatrixSize.None, TrapVector, Implied, false, 32, 0x4E40);
			AddFixed(rows, "System", "RESET", "privileged", MatrixSize.None, Implied, Implied, true, null, 0x4E70);
			AddFixed(rows, "System", "NOP", "implied", MatrixSize.None, Implied, Implied, false, null, 0x4E71);
			Add(rows, "System", "STOP", "privileged", MatrixSize.Word, Immediate, Implied, true, null, new ushort[] { 0x4E72, M68kCpuState.Supervisor });
			AddFixed(rows, "System", "RTE", "exception-return", MatrixSize.None, PostIncrement, Implied, true, null, 0x4E73);
			AddFixed(rows, "System", "RTS", "subroutine-return", MatrixSize.None, PostIncrement, Implied, false, null, 0x4E75);
			AddFixed(rows, "System", "TRAPV", "overflow-trap", MatrixSize.None, Implied, Implied, false, 7, 0x4E76);
			AddFixed(rows, "System", "RTR", "status-return", MatrixSize.None, PostIncrement, Implied, false, null, 0x4E77);
			AddFixed(rows, "System", "ILLEGAL", "exception", MatrixSize.None, Implied, Implied, false, 4, 0x4AFC);
			AddFixed(rows, "System", "Line-A", "emulator-exception", MatrixSize.None, Implied, Implied, false, 10, 0xA000);
			AddFixed(rows, "System", "Line-F", "emulator-exception", MatrixSize.None, Implied, Implied, false, 11, 0xF000);
		}

		private static void AddLine5Rows(List<M68kMatrixRow> rows)
		{
			foreach (var mnemonic in new[] { "ADDQ", "SUBQ" })
			{
				foreach (var size in ByteWordLong)
				{
					foreach (var destination in QuickAlterable(size))
					{
						Add(
							rows,
							"Quick",
							mnemonic,
							"quick",
							size,
							Quick,
							destination,
							privileged: false,
							expectedVector: null,
							EncodeAddSubQ(mnemonic == "SUBQ", size, destination));
					}
				}
			}

			foreach (var condition in Conditions())
			{
				Add(rows, "Condition", "Scc", condition, MatrixSize.Byte, Implied, Dn, false, null, new ushort[] { (ushort)(0x50C0 | (ConditionCode(condition) << 8)) });
				Add(rows, "Condition", "DBcc", condition, MatrixSize.Word, Dn, BranchDisplacement, false, null, new ushort[] { (ushort)(0x50C8 | (ConditionCode(condition) << 8)), 0xFFFE });
			}
		}

		private static void AddBranchRows(List<M68kMatrixRow> rows)
		{
			Add(rows, "Branch", "BRA", "8-bit-displacement", MatrixSize.Byte, BranchDisplacement, Implied, false, null, new ushort[] { 0x6002 });
			Add(rows, "Branch", "BRA", "16-bit-displacement", MatrixSize.Word, BranchDisplacement, Implied, false, null, new ushort[] { 0x6000, 0x0002 });
			Add(rows, "Branch", "BSR", "8-bit-displacement", MatrixSize.Byte, BranchDisplacement, Implied, false, null, new ushort[] { 0x6102 });
			Add(rows, "Branch", "BSR", "16-bit-displacement", MatrixSize.Word, BranchDisplacement, Implied, false, null, new ushort[] { 0x6100, 0x0002 });

			foreach (var condition in Conditions().Where(condition => condition is not ("T" or "F")))
			{
				var code = ConditionCode(condition);
				Add(rows, "Branch", "Bcc", $"{condition}-8-bit-displacement", MatrixSize.Byte, BranchDisplacement, Implied, false, null, new ushort[] { (ushort)(0x6002 | (code << 8)) });
				Add(rows, "Branch", "Bcc", $"{condition}-16-bit-displacement", MatrixSize.Word, BranchDisplacement, Implied, false, null, new ushort[] { (ushort)(0x6000 | (code << 8)), 0x0002 });
			}
		}

		private static void AddArithmeticRows(List<M68kMatrixRow> rows)
		{
			AddBinaryArithmetic(rows, "OR", 0x8);
			AddBinaryArithmetic(rows, "SUB", 0x9);
			AddBinaryArithmetic(rows, "AND", 0xC);
			AddBinaryArithmetic(rows, "ADD", 0xD);
			AddCompareAndEor(rows);
			AddAddressArithmetic(rows);
			AddMultiplyDivide(rows);
			AddAddSubX(rows);
			AddCmpm(rows);
			AddExchange(rows);
		}

		private static void AddShiftRotateRows(List<M68kMatrixRow> rows)
		{
			var shifts = new[]
			{
				("ASR", 0),
				("ASL", 0),
				("LSR", 1),
				("LSL", 1),
				("ROXR", 2),
				("ROXL", 2),
				("ROR", 3),
				("ROL", 3)
			};

			foreach (var (mnemonic, type) in shifts)
			{
				var left = mnemonic.EndsWith('L');
				foreach (var size in ByteWordLong)
				{
					Add(rows, "Shift", mnemonic, "register-count-immediate", size, Quick, Dn, false, null, EncodeRegisterShift(size, type, left, countFromRegister: false));
					Add(rows, "Shift", mnemonic, "register-count-register", size, Dn, Dn, false, null, EncodeRegisterShift(size, type, left, countFromRegister: true));
				}

				foreach (var destination in MemoryAlterable())
				{
					Add(rows, "Shift", mnemonic, "memory", MatrixSize.Word, Implied, destination, false, null, EncodeMemoryShift(type, left, destination));
				}
			}
		}

		private static void AddBcdAndMiscPendingRows(List<M68kMatrixRow> rows)
		{
			foreach (var variant in new[] { "register", "predecrement" })
			{
				var mode = variant == "register" ? 0 : 1;
				Add(rows, "BCD", "ABCD", variant, MatrixSize.Byte, variant == "register" ? Dn : PreDecrement, variant == "register" ? Dn : PreDecrement, false, null, new ushort[] { (ushort)(0xC100 | (mode << 3)) });
				Add(rows, "BCD", "SBCD", variant, MatrixSize.Byte, variant == "register" ? Dn : PreDecrement, variant == "register" ? Dn : PreDecrement, false, null, new ushort[] { (ushort)(0x8100 | (mode << 3)) });
			}

			foreach (var destination in DataAlterable(MatrixSize.Byte))
			{
				Add(rows, "BCD", "NBCD", "data-alterable", MatrixSize.Byte, Implied, destination, false, null, EncodeEaBase(0x4800, destination));
				Add(rows, "System", "TAS", "data-alterable", MatrixSize.Byte, Implied, destination, false, null, EncodeEaBase(0x4AC0, destination));
			}

			foreach (var source in Data(MatrixSize.Word))
			{
				Add(rows, "Arithmetic", "CHK", "bounds-check", MatrixSize.Word, source, Dn, false, null, EncodeArithmeticEaToRegister(0x4, 0, 6, source));
			}

			foreach (var size in WordLong)
			{
				Add(rows, "Move", "MOVEP", "memory-to-register", size, Displacement, Dn, false, null, EncodeMovep(size, memoryToRegister: true));
				Add(rows, "Move", "MOVEP", "register-to-memory", size, Dn, Displacement, false, null, EncodeMovep(size, memoryToRegister: false));
			}
		}

		private static void AddCpuGenerationRows(List<M68kMatrixRow> rows)
		{
			AddFixed(rows, "CpuGeneration", "MOVEC", "68010-illegal-on-68000", MatrixSize.Long, Implied, Implied, true, 4, 0x4E7B);
		}

		private static void AddBinaryArithmetic(List<M68kMatrixRow> rows, string mnemonic, int line)
		{
			foreach (var size in ByteWordLong)
			{
				foreach (var source in Data(size))
				{
					Add(rows, "Arithmetic", mnemonic, "ea-to-data-register", size, source, Dn, false, null, EncodeArithmeticEaToRegister(line, 0, SizeOpmode(size), source));
				}

				foreach (var destination in MemoryAlterable())
				{
					Add(rows, "Arithmetic", mnemonic, "data-register-to-memory", size, Dn, destination, false, null, EncodeArithmeticRegisterToEa(line, SizeOpmode(size) + 4, destination));
				}
			}
		}

		private static void AddCompareAndEor(List<M68kMatrixRow> rows)
		{
			foreach (var size in ByteWordLong)
			{
				foreach (var source in Data(size))
				{
					Add(rows, "Compare", "CMP", "ea-to-data-register", size, source, Dn, false, null, EncodeArithmeticEaToRegister(0xB, 0, SizeOpmode(size), source));
				}

				foreach (var destination in DataAlterable(size))
				{
					Add(rows, "Logical", "EOR", "data-register-to-ea", size, Dn, destination, false, null, EncodeArithmeticRegisterToEa(0xB, SizeOpmode(size) + 4, destination));
				}
			}
		}

		private static void AddAddressArithmetic(List<M68kMatrixRow> rows)
		{
			foreach (var size in WordLong)
			{
				foreach (var source in MoveSources(size))
				{
					Add(rows, "AddressArithmetic", "ADDA", "address-register", size, source, An, false, null, EncodeArithmeticEaToRegister(0xD, 0, size == MatrixSize.Word ? 3 : 7, source));
					Add(rows, "AddressArithmetic", "SUBA", "address-register", size, source, An, false, null, EncodeArithmeticEaToRegister(0x9, 0, size == MatrixSize.Word ? 3 : 7, source));
					Add(rows, "Compare", "CMPA", "address-register", size, source, An, false, null, EncodeArithmeticEaToRegister(0xB, 0, size == MatrixSize.Word ? 3 : 7, source));
				}
			}
		}

		private static void AddMultiplyDivide(List<M68kMatrixRow> rows)
		{
			foreach (var source in Data(MatrixSize.Word))
			{
				Add(rows, "Arithmetic", "DIVU", "unsigned", MatrixSize.Word, source, Dn, false, null, EncodeArithmeticEaToRegister(0x8, 0, 3, source));
				Add(rows, "Arithmetic", "DIVS", "signed", MatrixSize.Word, source, Dn, false, null, EncodeArithmeticEaToRegister(0x8, 0, 7, source));
				Add(rows, "Arithmetic", "MULU", "unsigned", MatrixSize.Word, source, Dn, false, null, EncodeArithmeticEaToRegister(0xC, 0, 3, source));
				Add(rows, "Arithmetic", "MULS", "signed", MatrixSize.Word, source, Dn, false, null, EncodeArithmeticEaToRegister(0xC, 0, 7, source));
			}
		}

		private static void AddAddSubX(List<M68kMatrixRow> rows)
		{
			foreach (var size in ByteWordLong)
			{
				Add(rows, "Arithmetic", "ADDX", "data-register", size, Dn, Dn, false, null, EncodeAddSubX(add: true, size, memoryMode: false));
				Add(rows, "Arithmetic", "ADDX", "predecrement-memory", size, PreDecrement, PreDecrement, false, null, EncodeAddSubX(add: true, size, memoryMode: true));
				Add(rows, "Arithmetic", "SUBX", "data-register", size, Dn, Dn, false, null, EncodeAddSubX(add: false, size, memoryMode: false));
				Add(rows, "Arithmetic", "SUBX", "predecrement-memory", size, PreDecrement, PreDecrement, false, null, EncodeAddSubX(add: false, size, memoryMode: true));
			}
		}

		private static void AddCmpm(List<M68kMatrixRow> rows)
		{
			foreach (var size in ByteWordLong)
			{
				Add(rows, "Compare", "CMPM", "postincrement-memory", size, PostIncrement, PostIncrement, false, null, EncodeCmpm(size));
			}
		}

		private static void AddExchange(List<M68kMatrixRow> rows)
		{
			Add(rows, "Register", "EXG", "data-data", MatrixSize.Long, Dn, Dn, false, null, new ushort[] { 0xC141 });
			Add(rows, "Register", "EXG", "address-address", MatrixSize.Long, An, An, false, null, new ushort[] { 0xC149 });
			Add(rows, "Register", "EXG", "data-address", MatrixSize.Long, Dn, An, false, null, new ushort[] { 0xC189 });
		}

		private static void AddStatusImmediate(List<M68kMatrixRow> rows, string mnemonic, ushort opcode, EaForm destination, bool privileged)
		{
			Add(
				rows,
				"Status",
				mnemonic,
				"immediate",
				MatrixSize.Word,
				Immediate,
				destination,
				privileged,
				expectedVector: null,
				new ushort[] { opcode, 0x001F });
		}

		private static void AddFixed(
			List<M68kMatrixRow> rows,
			string family,
			string mnemonic,
			string variant,
			MatrixSize size,
			EaForm source,
			EaForm destination,
			bool privileged,
			int? expectedVector,
			ushort opcode)
		{
			Add(rows, family, mnemonic, variant, size, source, destination, privileged, expectedVector, new[] { opcode });
		}

		private static void Add(
			List<M68kMatrixRow> rows,
			string family,
			string mnemonic,
			string variant,
			MatrixSize size,
			EaForm source,
			EaForm destination,
			bool privileged,
			int? expectedVector,
			ushort[] sampleWords)
		{
			var seed = new MatrixSeed(mnemonic, variant, size, source.Name, destination.Name, sampleWords);
			var executionCase = CreateExecutionCase(seed, expectedVector);
			var status = executionCase is null ? MatrixStatus.Pending : MatrixStatus.Executable;
			var pendingReason = executionCase is null
				? PendingReason(mnemonic)
				: null;

			rows.Add(new M68kMatrixRow(
				family,
				mnemonic,
				variant,
				size,
				source.Name,
				destination.Name,
				privileged,
				expectedVector,
				sampleWords,
				status,
				pendingReason,
				executionCase));
		}

		private static string PendingReason(string mnemonic)
		{
			return ImplementedMnemonicHolder.Value.Contains(mnemonic)
				? "ConformanceTemplatePending"
				: "NotImplemented";
		}

		private static class ImplementedMnemonicHolder
		{
			public static readonly HashSet<string> Value = new(StringComparer.Ordinal)
		{
			"ADD",
			"ADDA",
			"ADDI",
			"ADDQ",
			"ADDX",
			"AND",
			"ANDI",
			"ANDI to CCR",
			"ANDI to SR",
			"ASL",
			"ASR",
			"Bcc",
			"BCHG",
			"BCLR",
			"BRA",
			"BSR",
			"BSET",
			"BTST",
			"CLR",
			"CMP",
			"CMPA",
			"CMPI",
			"CMPM",
			"DBcc",
			"DIVS",
			"DIVU",
			"EOR",
			"EORI",
			"EORI to CCR",
			"EORI to SR",
			"EXG",
			"EXT",
			"ILLEGAL",
			"JMP",
			"JSR",
			"LEA",
			"Line-A",
			"Line-F",
			"LINK",
			"LSL",
			"LSR",
			"MOVE",
			"MOVE from SR",
			"MOVE to CCR",
			"MOVE to SR",
			"MOVE USP",
			"MOVEA",
			"MOVEC",
			"MOVEM",
			"MOVEQ",
			"MULS",
			"MULU",
			"NEG",
			"NEGX",
			"NOP",
			"NOT",
			"OR",
			"ORI",
			"ORI to CCR",
			"ORI to SR",
			"PEA",
			"RESET",
			"ROL",
			"ROR",
			"ROXL",
			"ROXR",
			"RTE",
			"RTR",
			"RTS",
			"Scc",
			"STOP",
			"SUB",
			"SUBA",
			"SUBI",
			"SUBQ",
			"SUBX",
			"SWAP",
			"TRAP",
			"TRAPV",
			"TST",
			"UNLK"
		};
		}

		private sealed record MatrixSeed(string Mnemonic, string Variant, MatrixSize Size, string Source, string Destination, ushort[] SampleWords)
		{
			public string Key => $"{Mnemonic}|{Variant}|{Size}|{Source}|{Destination}";
		}

		private static M68kExecutionCase? CreateExecutionCase(MatrixSeed seed, int? expectedVector)
		{
			if (expectedVector is not null && seed.Mnemonic is "ILLEGAL" or "Line-A" or "Line-F" or "MOVEC")
			{
				return ExceptionCase(expectedVector.Value);
			}

			return seed.Key switch
			{
				"MOVEQ|quick-immediate|Long|#quick|Dn" => new M68kExecutionCase(NoPrepare, VerifyMoveq),
				"MOVE|data-move|Byte|#imm|Dn" => new M68kExecutionCase(NoPrepare, VerifyMoveImmediateByteToD0),
				"MOVEA|address-move|Long|#imm|An" => new M68kExecutionCase(NoPrepare, VerifyMoveALongImmediateToA0),
				"MOVEM|registers-to-memory|Long|register-list|-(An)" => new M68kExecutionCase(PrepareMovemRegisterToMemory, VerifyMovemRegisterToMemory),
				"MOVE USP|to-usp|Long|An|USP" => new M68kExecutionCase(NoPrepare, VerifyMoveToUsp),
				"MOVE USP|from-usp|Long|USP|An" => new M68kExecutionCase(PrepareMoveFromUsp, VerifyMoveFromUsp),
				"EXG|address-address|Long|An|An" => new M68kExecutionCase(PrepareExgAddress, VerifyExgAddress),
				"RESET|privileged|None|implied|implied" => new M68kExecutionCase(NoPrepare, VerifyReset),
				"NOP|implied|None|implied|implied" => new M68kExecutionCase(NoPrepare, VerifyNextPc),
				"STOP|privileged|Word|#imm|implied" => new M68kExecutionCase(NoPrepare, VerifyStop),
				"RTS|subroutine-return|None|(An)+|implied" => new M68kExecutionCase(PrepareRts, VerifyRts),
				"TRAP|vector|None|#vector|implied" => ExceptionCase(32),
				"ADDQ|quick|Long|#quick|Dn" => new M68kExecutionCase(NoPrepare, VerifyAddqLongD0),
				"DBcc|F|Word|Dn|displacement" => new M68kExecutionCase(PrepareDbra, VerifyDbraTaken),
				"BRA|8-bit-displacement|Byte|displacement|implied" => new M68kExecutionCase(NoPrepare, VerifyBranchByte),
				"JSR|control|None|d16(PC)|implied" => new M68kExecutionCase(NoPrepare, VerifyJsrPcRelative),
				"EXT|byte-to-word|Word|Dn|Dn" => new M68kExecutionCase(PrepareExtWord, VerifyExtWord),
				"BCLR|dynamic|Byte|Dn|(An)" => new M68kExecutionCase(PrepareDynamicBclr, VerifyDynamicBclr),
				"CMPM|postincrement-memory|Byte|(An)+|(An)+" => new M68kExecutionCase(PrepareCmpmByte, VerifyCmpmByte),
				"ROXR|register-count-immediate|Byte|#quick|Dn" => new M68kExecutionCase(PrepareRoxr, VerifyRoxr),
				"ADDX|data-register|Byte|Dn|Dn" => new M68kExecutionCase(PrepareAddx, VerifyAddx),
				"SUBX|predecrement-memory|Word|-(An)|-(An)" => new M68kExecutionCase(PrepareSubx, VerifySubx),
				"DIVS|signed|Word|#imm|Dn" => new M68kExecutionCase(PrepareDivs, VerifyDivs),
				"CMPA|address-register|Word|#imm|An" => new M68kExecutionCase(NoPrepare, VerifyNextPc),
				"MOVE to CCR|status-destination|Word|Dn|CCR" => new M68kExecutionCase(NoPrepare, VerifyMoveToCcr),
				_ => null
			};
		}

		private static M68kExecutionCase ExceptionCase(int vector)
		{
			return new M68kExecutionCase(NoPrepare, (bus, cpu, row) =>
			{
				var expectedPc = 0x9000u + (uint)(vector * 0x10);
				var expectedStackedPc = row.Mnemonic == "TRAP" ? NextPc(row) : ProgramAddress;
				Assert.Equal(expectedPc, cpu.State.ProgramCounter);
				Assert.True(cpu.State.GetFlag(M68kCpuState.Supervisor));
				Assert.Equal(expectedStackedPc, ReadLong(bus, cpu.State.A[7] + 2));
			});
		}

		private static void NoPrepare(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			_ = cpu;
			_ = row;
		}

		private static void VerifyNextPc(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			Assert.Equal(NextPc(row), cpu.State.ProgramCounter);
			Assert.False(cpu.State.Halted);
		}

		private static void VerifyMoveq(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			VerifyNextPc(bus, cpu, row);
			Assert.Equal(0x12u, cpu.State.D[0]);
			Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
			Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		}

		private static void VerifyMoveImmediateByteToD0(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			VerifyNextPc(bus, cpu, row);
			Assert.Equal(0x0000_0012u, cpu.State.D[0] & 0xFF);
			Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
			Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		}

		private static void VerifyMoveALongImmediateToA0(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			VerifyNextPc(bus, cpu, row);
			Assert.Equal(0x1234_5678u, cpu.State.A[0]);
		}

		private static void PrepareMovemRegisterToMemory(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			_ = row;
			cpu.State.D[0] = 0x1111_2222;
			cpu.State.A[0] = 0x3000;
		}

		private static void VerifyMovemRegisterToMemory(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			VerifyNextPc(bus, cpu, row);
			Assert.Equal(0x2FFCu, cpu.State.A[0]);
			Assert.Equal(0x1111_2222u, ReadLong(bus, 0x2FFC));
		}

		private static void VerifyMoveToUsp(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			VerifyNextPc(bus, cpu, row);
			Assert.Equal(0x3000u, cpu.State.UserStackPointer);
		}

		private static void PrepareMoveFromUsp(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			_ = row;
			cpu.State.SetUserStackPointer(0x4567_89AB);
		}

		private static void VerifyMoveFromUsp(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			VerifyNextPc(bus, cpu, row);
			Assert.Equal(0x4567_89ABu, cpu.State.A[0]);
		}

		private static void PrepareExgAddress(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			_ = row;
			cpu.State.A[0] = 0x1234_5678;
			cpu.State.A[1] = 0x8765_4321;
		}

		private static void VerifyExgAddress(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			VerifyNextPc(bus, cpu, row);
			Assert.Equal(0x8765_4321u, cpu.State.A[0]);
			Assert.Equal(0x1234_5678u, cpu.State.A[1]);
		}

		private static void VerifyReset(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			VerifyNextPc(bus, cpu, row);
			Assert.Equal(1, bus.ExternalResetCount);
		}

		private static void VerifyStop(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			Assert.Equal(NextPc(row), cpu.State.ProgramCounter);
			Assert.True(cpu.State.Stopped);
		}

		private static void PrepareRts(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = row;
			cpu.State.SetActiveStackPointer(0x7FFC);
			bus.WriteLongRaw(0x7FFC, 0x00AA_5500);
		}

		private static void VerifyRts(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			_ = row;
			Assert.Equal(0x00AA_5500u, cpu.State.ProgramCounter);
			Assert.Equal(0x8000u, cpu.State.A[7]);
		}

		private static void PrepareRtr(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = row;
			cpu.State.SetActiveStackPointer(0x7FFA);
			bus.WriteWordRaw(0x7FFA, M68kCpuState.Zero);
			bus.WriteLongRaw(0x7FFC, 0x00AA_6600);
		}

		private static void VerifyRtr(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			_ = row;
			Assert.Equal(0x00AA_6600u, cpu.State.ProgramCounter);
			Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
			Assert.Equal(0x8000u, cpu.State.A[7]);
		}

		private static void VerifyAddqLongD0(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			VerifyNextPc(bus, cpu, row);
			Assert.Equal(0x0000_0012u, cpu.State.D[0]);
		}

		private static void PrepareDbra(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			_ = row;
			cpu.State.D[0] = 2;
		}

		private static void VerifyDbraTaken(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			_ = row;
			Assert.Equal(0x1000u, cpu.State.ProgramCounter);
			Assert.Equal(1u, cpu.State.D[0] & 0xFFFF);
		}

		private static void VerifyBranchByte(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			_ = row;
			Assert.Equal(0x1004u, cpu.State.ProgramCounter);
		}

		private static void VerifyJsrPcRelative(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = row;
			Assert.Equal(0x1012u, cpu.State.ProgramCounter);
			Assert.Equal(NextPc(row), ReadLong(bus, cpu.State.A[7]));
		}

		private static void PrepareExtWord(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			_ = row;
			cpu.State.D[0] = 0x1234_00F0;
		}

		private static void VerifyExtWord(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			VerifyNextPc(bus, cpu, row);
			Assert.Equal(0x1234_FFF0u, cpu.State.D[0]);
			Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		}

		private static void PrepareDynamicBclr(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = row;
			cpu.State.D[0] = 3;
			cpu.State.A[0] = 0x3000;
			bus.Memory[0x3000] = 0x08;
		}

		private static void VerifyDynamicBclr(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			VerifyNextPc(bus, cpu, row);
			Assert.Equal(0, bus.Memory[0x3000]);
			Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		}

		private static void PrepareCmpmByte(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = row;
			cpu.State.A[0] = 0x3000;
			cpu.State.A[1] = 0x3100;
			bus.Memory[0x3000] = 0x20;
			bus.Memory[0x3100] = 0x20;
			cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;
		}

		private static void VerifyCmpmByte(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			VerifyNextPc(bus, cpu, row);
			Assert.Equal(0x3001u, cpu.State.A[0]);
			Assert.Equal(0x3101u, cpu.State.A[1]);
			Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
			Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		}

		private static void PrepareRoxr(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			_ = row;
			cpu.State.D[0] = 0x01;
			cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;
		}

		private static void VerifyRoxr(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			VerifyNextPc(bus, cpu, row);
			Assert.Equal(0x80u, cpu.State.D[0] & 0xFF);
			Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
			Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		}

		private static void PrepareAddx(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			_ = row;
			cpu.State.D[0] = 1;
			cpu.State.D[1] = 2;
			cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero;
		}

		private static void VerifyAddx(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			VerifyNextPc(bus, cpu, row);
			Assert.Equal(4u, cpu.State.D[0] & 0xFF);
			Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		}

		private static void PrepareSubx(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = row;
			cpu.State.A[0] = 0x3002;
			cpu.State.A[1] = 0x3102;
			bus.WriteWordRaw(0x3000, 0x0000);
			bus.WriteWordRaw(0x3100, 0x0001);
			cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero;
		}

		private static void VerifySubx(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			VerifyNextPc(bus, cpu, row);
			Assert.Equal(0xFFFE, ReadWord(bus, 0x3000));
			Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		}

		private static void PrepareDivs(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			_ = row;
			cpu.State.D[0] = unchecked((uint)-18);
		}

		private static void VerifyDivs(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			VerifyNextPc(bus, cpu, row);
			Assert.Equal(0x0000_FFFFu, cpu.State.D[0]);
			Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		}

		private static void VerifyMoveToCcr(MatrixBus bus, M68kInterpreter cpu, M68kMatrixRow row)
		{
			_ = bus;
			VerifyNextPc(bus, cpu, row);
			Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
			Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		}

		private static uint NextPc(M68kMatrixRow row) => ProgramAddress + (uint)(row.SampleWords.Length * 2);

		private static void WriteWords(MatrixBus bus, uint address, IReadOnlyList<ushort> words)
		{
			for (var i = 0; i < words.Count; i++)
			{
				bus.WriteWordRaw(address + (uint)(i * 2), words[i]);
			}
		}

		private static ushort ReadWord(MatrixBus bus, uint address)
		{
			return (ushort)((bus.Memory[address] << 8) | bus.Memory[address + 1]);
		}

		private static uint ReadLong(MatrixBus bus, uint address)
		{
			return ((uint)bus.Memory[address] << 24) |
				((uint)bus.Memory[address + 1] << 16) |
				((uint)bus.Memory[address + 2] << 8) |
				bus.Memory[address + 3];
		}

		private static IEnumerable<EaForm> AllCanonicalEa()
		{
			yield return Dn;
			yield return An;
			yield return Indirect;
			yield return PostIncrement;
			yield return PreDecrement;
			yield return Displacement;
			yield return Index;
			yield return AbsoluteWord;
			yield return AbsoluteLong;
			yield return PcDisplacement;
			yield return PcIndex;
			yield return Immediate;
		}

		private static IEnumerable<EaForm> MemoryAlterable()
		{
			yield return Indirect;
			yield return PostIncrement;
			yield return PreDecrement;
			yield return Displacement;
			yield return Index;
			yield return AbsoluteWord;
			yield return AbsoluteLong;
		}

		private static IEnumerable<EaForm> Data(MatrixSize size)
		{
			yield return Dn;
			if (size != MatrixSize.Byte)
			{
				yield return An;
			}

			foreach (var ea in MemoryAlterable())
			{
				yield return ea;
			}

			yield return PcDisplacement;
			yield return PcIndex;
			yield return Immediate;
		}

		private static IEnumerable<EaForm> DataAlterable(MatrixSize size)
		{
			yield return Dn;
			foreach (var ea in MemoryAlterable())
			{
				yield return ea;
			}
		}

		private static IEnumerable<EaForm> MoveSources(MatrixSize size)
		{
			return Data(size);
		}

		private static IEnumerable<EaForm> Control()
		{
			yield return Indirect;
			yield return Displacement;
			yield return Index;
			yield return AbsoluteWord;
			yield return AbsoluteLong;
			yield return PcDisplacement;
			yield return PcIndex;
		}

		private static IEnumerable<EaForm> QuickAlterable(MatrixSize size)
		{
			yield return Dn;
			if (size != MatrixSize.Byte)
			{
				yield return An;
			}

			foreach (var ea in MemoryAlterable())
			{
				yield return ea;
			}
		}

		private static IEnumerable<EaForm> BitDestinations()
		{
			yield return Dn;
			foreach (var ea in MemoryAlterable())
			{
				yield return ea;
			}
		}

		private static IEnumerable<EaForm> MovemRegisterToMemory()
		{
			yield return Indirect;
			yield return PreDecrement;
			yield return Displacement;
			yield return Index;
			yield return AbsoluteWord;
			yield return AbsoluteLong;
		}

		private static IEnumerable<EaForm> MovemMemoryToRegister()
		{
			yield return Indirect;
			yield return PostIncrement;
			yield return Displacement;
			yield return Index;
			yield return AbsoluteWord;
			yield return AbsoluteLong;
			yield return PcDisplacement;
			yield return PcIndex;
		}

		private static IEnumerable<EaForm> AddressRegisterSpecials()
		{
			for (var register = 0; register < 8; register++)
			{
				yield return new EaForm(register == 0 ? "An" : $"A{register}", EaKind.AddressRegister, 1, register);
			}
		}

		private static IEnumerable<string> Conditions()
		{
			return new[] { "T", "F", "HI", "LS", "CC", "CS", "NE", "EQ", "VC", "VS", "PL", "MI", "GE", "LT", "GT", "LE" };
		}

		private static int ConditionCode(string condition)
		{
			return condition switch
			{
				"T" => 0,
				"F" => 1,
				"HI" => 2,
				"LS" => 3,
				"CC" => 4,
				"CS" => 5,
				"NE" => 6,
				"EQ" => 7,
				"VC" => 8,
				"VS" => 9,
				"PL" => 10,
				"MI" => 11,
				"GE" => 12,
				"LT" => 13,
				"GT" => 14,
				"LE" => 15,
				_ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown MC68000 condition.")
			};
		}

		private static void AddDestinationExclusions(
			List<M68kMatrixExclusion> exclusions,
			string mnemonic,
			string variant,
			MatrixSize size,
			EaForm source,
			IEnumerable<EaForm> legalDestinations,
			IEnumerable<EaForm> allDestinations,
			string reason)
		{
			var legal = legalDestinations.Select(ea => ea.Name).ToHashSet(StringComparer.Ordinal);
			foreach (var illegal in allDestinations.Where(ea => !legal.Contains(ea.Name)))
			{
				exclusions.Add(new M68kMatrixExclusion(mnemonic, variant, size, source.Name, illegal.Name, reason));
			}
		}

		private static void AddSourceExclusions(
			List<M68kMatrixExclusion> exclusions,
			string mnemonic,
			string variant,
			MatrixSize size,
			IEnumerable<EaForm> legalSources,
			EaForm destination,
			IEnumerable<EaForm> allSources,
			string reason)
		{
			var legal = legalSources.Select(ea => ea.Name).ToHashSet(StringComparer.Ordinal);
			foreach (var illegal in allSources.Where(ea => !legal.Contains(ea.Name)))
			{
				exclusions.Add(new M68kMatrixExclusion(mnemonic, variant, size, illegal.Name, destination.Name, reason));
			}
		}

		private static ushort[] EncodeMove(MatrixSize size, EaForm source, EaForm destination)
		{
			var opcode = (ushort)(MoveSizeCode(size) << 12 | destination.Register << 9 | destination.Mode << 6 | source.Code);
			return Append(new[] { opcode }, source.Extensions(size), destination.Extensions(size));
		}

		private static ushort[] EncodeMoveA(MatrixSize size, EaForm source)
		{
			var opcode = (ushort)(MoveSizeCode(size) << 12 | 1 << 6 | source.Code);
			return Append(new[] { opcode }, source.Extensions(size));
		}

		private static ushort[] EncodeImmediate(int baseOpcode, MatrixSize size, EaForm destination)
		{
			var opcode = (ushort)(baseOpcode | SizeBits(size) | destination.Code);
			return Append(new[] { opcode }, Immediate.Extensions(size), destination.Extensions(size));
		}

		private static ushort[] EncodeStaticBitOperation(int operation, EaForm destination)
		{
			var opcode = (ushort)(0x0800 | (operation << 6) | destination.Code);
			return Append(new[] { opcode }, destination.Extensions(destination.Kind == EaKind.DataRegister ? MatrixSize.Long : MatrixSize.Byte));
		}

		private static ushort[] EncodeDynamicBitOperation(int operation, EaForm destination)
		{
			var opcode = (ushort)(0x0100 | (operation << 6) | destination.Code);
			return Append(new[] { opcode }, destination.Extensions(destination.Kind == EaKind.DataRegister ? MatrixSize.Long : MatrixSize.Byte));
		}

		private static ushort[] EncodeUnary(int baseOpcode, MatrixSize size, EaForm destination)
		{
			return EncodeEaBase(baseOpcode | SizeBits(size), destination, size);
		}

		private static ushort[] EncodeEaBase(int baseOpcode, EaForm ea, MatrixSize size = MatrixSize.Word)
		{
			return Append(new[] { (ushort)(baseOpcode | ea.Code) }, ea.Extensions(size));
		}

		private static ushort[] EncodeLea(EaForm source)
		{
			return Append(new[] { (ushort)(0x41C0 | source.Code) }, source.Extensions(MatrixSize.Long));
		}

		private static ushort[] EncodeMovem(MatrixSize size, bool memoryToRegisters, EaForm ea)
		{
			var opcode = (ushort)(0x4880 | (memoryToRegisters ? 0x0400 : 0) | (size == MatrixSize.Long ? 0x0040 : 0) | ea.Code);
			var registerMask = !memoryToRegisters && ea.Kind == EaKind.AddressPreDecrement
				? (ushort)0x8000
				: (ushort)0x0001;
			return Append(new[] { opcode, registerMask }, ea.Extensions(size));
		}

		private static ushort[] EncodeAddSubQ(bool subtract, MatrixSize size, EaForm destination)
		{
			var opcode = (ushort)(0x5000 | 0x0200 | (subtract ? 0x0100 : 0) | SizeBits(size) | destination.Code);
			return Append(new[] { opcode }, destination.Extensions(size));
		}

		private static ushort[] EncodeArithmeticEaToRegister(int line, int register, int opmode, EaForm source)
		{
			var size = OpmodeSize(opmode);
			var opcode = (ushort)(line << 12 | register << 9 | opmode << 6 | source.Code);
			return Append(new[] { opcode }, source.Extensions(size));
		}

		private static ushort[] EncodeArithmeticRegisterToEa(int line, int opmode, EaForm destination)
		{
			var size = OpmodeSize(opmode);
			var opcode = (ushort)(line << 12 | opmode << 6 | destination.Code);
			return Append(new[] { opcode }, destination.Extensions(size));
		}

		private static ushort[] EncodeAddSubX(bool add, MatrixSize size, bool memoryMode)
		{
			var line = add ? 0xD : 0x9;
			var opmode = SizeOpmode(size) + 4;
			return new[] { (ushort)(line << 12 | 0 << 9 | opmode << 6 | (memoryMode ? 1 << 3 : 0) | 1) };
		}

		private static ushort[] EncodeCmpm(MatrixSize size)
		{
			return new[] { (ushort)(0xB108 | 1 << 9 | SizeOpmode(size) << 6) };
		}

		private static ushort[] EncodeRegisterShift(MatrixSize size, int type, bool left, bool countFromRegister)
		{
			return new[]
			{
				(ushort)(0xE000 |
					(countFromRegister ? 1 << 5 : 1 << 9) |
					(left ? 1 << 8 : 0) |
					SizeBits(size) |
					type << 3)
			};
		}

		private static ushort[] EncodeMemoryShift(int type, bool left, EaForm destination)
		{
			return Append(new[] { (ushort)(0xE0C0 | (left ? 1 << 8 : 0) | type << 9 | destination.Code) }, destination.Extensions(MatrixSize.Word));
		}

		private static ushort[] EncodeMovep(MatrixSize size, bool memoryToRegister)
		{
			var mode = (size, memoryToRegister) switch
			{
				(MatrixSize.Word, true) => 0x0108,
				(MatrixSize.Long, true) => 0x0148,
				(MatrixSize.Word, false) => 0x0188,
				_ => 0x01C8
			};
			return new ushort[] { (ushort)mode, 0x0010 };
		}

		private static int MoveSizeCode(MatrixSize size)
		{
			return size switch
			{
				MatrixSize.Byte => 1,
				MatrixSize.Long => 2,
				MatrixSize.Word => 3,
				_ => throw new ArgumentOutOfRangeException(nameof(size), size, "MOVE requires byte, word, or long.")
			};
		}

		private static int SizeBits(MatrixSize size)
		{
			return size switch
			{
				MatrixSize.Byte => 0x0000,
				MatrixSize.Word => 0x0040,
				MatrixSize.Long => 0x0080,
				_ => 0
			};
		}

		private static int SizeOpmode(MatrixSize size)
		{
			return size switch
			{
				MatrixSize.Byte => 0,
				MatrixSize.Word => 1,
				MatrixSize.Long => 2,
				_ => throw new ArgumentOutOfRangeException(nameof(size), size, "Arithmetic opmode requires byte, word, or long.")
			};
		}

		private static MatrixSize OpmodeSize(int opmode)
		{
			return (opmode & 3) switch
			{
				0 => MatrixSize.Byte,
				1 => MatrixSize.Word,
				2 => MatrixSize.Long,
				3 => MatrixSize.Word,
				_ => MatrixSize.Word
			};
		}

		private static ushort[] Append(params ushort[][] chunks)
		{
			var length = chunks.Sum(chunk => chunk.Length);
			var result = new ushort[length];
			var offset = 0;
			foreach (var chunk in chunks)
			{
				Array.Copy(chunk, 0, result, offset, chunk.Length);
				offset += chunk.Length;
			}

			return result;
		}
	}
}
