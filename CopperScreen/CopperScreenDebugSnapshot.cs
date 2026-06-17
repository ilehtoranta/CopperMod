using System.Text;
using CopperMod.Amiga;

namespace CopperScreen;

internal sealed class CopperScreenDebugSnapshot
{
	public CopperScreenDebugSnapshot(
		DateTimeOffset capturedAt,
		string reasonCode,
		string message,
		string profileName,
		string cpuBackendName,
		string diskName,
		string? diskPath,
		long frameNumber,
		CopperScreenDebugCpuSnapshot cpu,
		CopperScreenDriveState[] drives,
		string[] diagnostics,
		string[] disassembly,
		string[] stackWords)
	{
		CapturedAt = capturedAt;
		ReasonCode = reasonCode;
		Message = message;
		ProfileName = profileName;
		CpuBackendName = cpuBackendName;
		DiskName = diskName;
		DiskPath = diskPath;
		FrameNumber = frameNumber;
		Cpu = cpu;
		Drives = drives;
		Diagnostics = diagnostics;
		Disassembly = disassembly;
		StackWords = stackWords;
	}

	public DateTimeOffset CapturedAt { get; }

	public string ReasonCode { get; }

	public string Message { get; }

	public string ProfileName { get; }

	public string CpuBackendName { get; }

	public string DiskName { get; }

	public string? DiskPath { get; }

	public long FrameNumber { get; }

	public CopperScreenDebugCpuSnapshot Cpu { get; }

	public CopperScreenDriveState[] Drives { get; }

	public string[] Diagnostics { get; }

	public string[] Disassembly { get; }

	public string[] StackWords { get; }

	public string ToReport()
	{
		var builder = new StringBuilder();
		builder.AppendLine("CopperScreen debugger snapshot");
		builder.AppendLine($"Captured: {CapturedAt:O}");
		builder.AppendLine($"Reason: {ReasonCode}");
		builder.AppendLine($"Message: {Message}");
		builder.AppendLine($"Profile: {ProfileName}");
		builder.AppendLine($"CPU backend: {CpuBackendName}");
		builder.AppendLine($"Disk: {DiskName}");
		if (!string.IsNullOrWhiteSpace(DiskPath))
		{
			builder.AppendLine($"Disk path: {DiskPath}");
		}

		builder.AppendLine($"Frame: {FrameNumber}");
		builder.AppendLine();
		builder.Append(Cpu.FormatRegisters());
		builder.AppendLine();
		builder.AppendLine("Drives:");
		for (var i = 0; i < Drives.Length; i++)
		{
			builder.AppendLine("  " + FormatDrive(Drives[i]));
		}

		if (Diagnostics.Length > 0)
		{
			builder.AppendLine();
			builder.AppendLine("Diagnostics:");
			for (var i = 0; i < Diagnostics.Length; i++)
			{
				builder.AppendLine("  " + Diagnostics[i]);
			}
		}

		builder.AppendLine();
		builder.AppendLine("Disassembly:");
		for (var i = 0; i < Disassembly.Length; i++)
		{
			builder.AppendLine("  " + Disassembly[i]);
		}

		builder.AppendLine();
		builder.AppendLine("Stack:");
		for (var i = 0; i < StackWords.Length; i++)
		{
			builder.AppendLine("  " + StackWords[i]);
		}

		return builder.ToString();
	}

	internal static string FormatDrive(CopperScreenDriveState drive)
	{
		if (!drive.Connected)
		{
			return $"DF{drive.Index}: not connected";
		}

		if (!drive.HasDisk)
		{
			return $"DF{drive.Index}: empty";
		}

		var flags = string.Concat(drive.ActiveDma ? 'D' : drive.MotorOn ? 'M' : '-', drive.Selected ? 'S' : '-', drive.WriteProtected ? 'P' : 'W');
		return $"DF{drive.Index}: cyl {drive.Cylinder:00}.{drive.Head} {flags} {drive.DiskName}";
	}
}

internal readonly record struct CopperScreenDebugCpuSnapshot(
	uint ProgramCounter,
	uint LastInstructionProgramCounter,
	ushort LastOpcode,
	ushort StatusRegister,
	uint UserStackPointer,
	uint SupervisorStackPointer,
	long Cycles,
	bool Halted,
	bool Stopped,
	uint[] DataRegisters,
	uint[] AddressRegisters,
	bool M68020StateEnabled = false,
	long NativeCycles = 0,
	uint VectorBaseRegister = 0,
	uint SourceFunctionCode = 0,
	uint DestinationFunctionCode = 0,
	uint CacheControlRegister = 0,
	uint CacheAddressRegister = 0,
	uint MasterStackPointer = 0)
{
	public string Flags
	{
		get
		{
			Span<char> flags = stackalloc char[5];
			flags[0] = (StatusRegister & M68kCpuState.Extend) != 0 ? 'X' : '-';
			flags[1] = (StatusRegister & M68kCpuState.Negative) != 0 ? 'N' : '-';
			flags[2] = (StatusRegister & M68kCpuState.Zero) != 0 ? 'Z' : '-';
			flags[3] = (StatusRegister & M68kCpuState.Overflow) != 0 ? 'V' : '-';
			flags[4] = (StatusRegister & M68kCpuState.Carry) != 0 ? 'C' : '-';
			return new string(flags);
		}
	}

	public string FormatRegisters()
	{
		var builder = new StringBuilder();
		builder.AppendLine($"PC={FormatAddress(ProgramCounter)}  LastPC={FormatAddress(LastInstructionProgramCounter)}  Opcode=${LastOpcode:X4}");
		builder.AppendLine($"SR=${StatusRegister:X4} {Flags}  USP={FormatAddress(UserStackPointer)}  SSP={FormatAddress(SupervisorStackPointer)}");
		builder.AppendLine($"Cycles={Cycles}  Halted={Halted}  Stopped={Stopped}");
		if (M68020StateEnabled)
		{
			builder.AppendLine($"68020/030 NativeCycles={NativeCycles}  VBR={FormatAddress(VectorBaseRegister)}  SFC={SourceFunctionCode}  DFC={DestinationFunctionCode}  CACR=${CacheControlRegister:X8}  CAAR=${CacheAddressRegister:X8}  MSP={FormatAddress(MasterStackPointer)}");
		}

		for (var i = 0; i < 8; i += 4)
		{
			builder.Append("D");
			builder.Append(i);
			builder.Append("-D");
			builder.Append(i + 3);
			builder.Append(": ");
			for (var register = i; register < i + 4; register++)
			{
				builder.Append($"D{register}=${DataRegisters[register]:X8} ");
			}

			builder.AppendLine();
		}

		for (var i = 0; i < 8; i += 4)
		{
			builder.Append("A");
			builder.Append(i);
			builder.Append("-A");
			builder.Append(i + 3);
			builder.Append(": ");
			for (var register = i; register < i + 4; register++)
			{
				builder.Append($"A{register}=${AddressRegisters[register]:X8} ");
			}

			builder.AppendLine();
		}

		return builder.ToString();
	}

	private static string FormatAddress(uint address)
		=> $"${address & 0x00FF_FFFF:X6}";
}

internal delegate bool TryReadM68kWord(uint address, out ushort value);

internal static class M68kMiniDisassembler
{
	private static readonly string[] BranchNames =
	[
		"BRA", "BSR", "BHI", "BLS", "BCC", "BCS", "BNE", "BEQ",
		"BVC", "BVS", "BPL", "BMI", "BGE", "BLT", "BGT", "BLE"
	];

	internal static string[] Disassemble(uint startAddress, int instructionCount, TryReadM68kWord readWord)
	{
		if (instructionCount <= 0)
		{
			return [];
		}

		var lines = new string[instructionCount];
		var pc = startAddress & 0x00FF_FFFF;
		for (var i = 0; i < lines.Length; i++)
		{
			lines[i] = DisassembleLine(pc, readWord, out var byteLength);
			pc = (pc + (uint)Math.Max(2, byteLength)) & 0x00FF_FFFF;
		}

		return lines;
	}

	internal static string DisassembleLine(uint address, TryReadM68kWord readWord, out int byteLength)
	{
		address &= 0x00FF_FFFF;
		byteLength = 2;
		if (!readWord(address, out var opcode))
		{
			return $"{address:X6}: ????  <unreadable>";
		}

		var mnemonic = Decode(address, opcode, readWord, out byteLength);
		return $"{address:X6}: {FormatWords(address, byteLength, readWord),-17} {mnemonic}";
	}

	private static string Decode(uint address, ushort opcode, TryReadM68kWord readWord, out int byteLength)
	{
		byteLength = 2;
		if ((opcode & 0xF100) == 0x7000)
		{
			var register = (opcode >> 9) & 7;
			var value = unchecked((sbyte)(opcode & 0xFF));
			return $"MOVEQ #{value},D{register}";
		}

		if ((opcode & 0xF000) == 0x6000)
		{
			var condition = (opcode >> 8) & 0xF;
			var displacement8 = opcode & 0xFF;
			if (displacement8 == 0 && readWord(address + 2, out var extension))
			{
				byteLength = 4;
				var target = (address + 2u + unchecked((uint)(short)extension)) & 0x00FF_FFFF;
				return $"{BranchNames[condition]} ${target:X6}";
			}

			var target8 = (address + 2u + unchecked((uint)(sbyte)displacement8)) & 0x00FF_FFFF;
			return $"{BranchNames[condition]} ${target8:X6}";
		}

		if (opcode is 0x4E70)
		{
			return "RESET";
		}

		if (opcode is 0x4E71)
		{
			return "NOP";
		}

		if (opcode is 0x4E72)
		{
			byteLength = 4;
			return readWord(address + 2, out var immediate)
				? $"STOP #${immediate:X4}"
				: "STOP #????";
		}

		if (opcode is 0x4E73)
		{
			return "RTE";
		}

		if (opcode is 0x4E75)
		{
			return "RTS";
		}

		if (opcode is 0x4E76)
		{
			return "TRAPV";
		}

		if (opcode is 0x4E77)
		{
			return "RTR";
		}

		if ((opcode & 0xFFF0) == 0x4E40)
		{
			return $"TRAP #{opcode & 0xF}";
		}

		if ((opcode & 0xFFC0) == 0x4E80)
		{
			var ea = FormatEffectiveAddress(address + 2, opcode, M68kDisplayOperandSize.Word, readWord, out var extensionWords);
			byteLength = 2 + (extensionWords * 2);
			return "JSR " + ea;
		}

		if ((opcode & 0xFFC0) == 0x4EC0)
		{
			var ea = FormatEffectiveAddress(address + 2, opcode, M68kDisplayOperandSize.Word, readWord, out var extensionWords);
			byteLength = 2 + (extensionWords * 2);
			return "JMP " + ea;
		}

		if ((opcode & 0xF1C0) == 0x41C0)
		{
			var register = (opcode >> 9) & 7;
			var ea = FormatEffectiveAddress(address + 2, opcode, M68kDisplayOperandSize.Long, readWord, out var extensionWords);
			byteLength = 2 + (extensionWords * 2);
			return $"LEA {ea},A{register}";
		}

		if ((opcode & 0xF000) is 0x1000 or 0x2000 or 0x3000)
		{
			var size = (opcode & 0xF000) switch
			{
				0x1000 => M68kDisplayOperandSize.Byte,
				0x2000 => M68kDisplayOperandSize.Long,
				_ => M68kDisplayOperandSize.Word
			};
			var destinationRegister = (opcode >> 9) & 7;
			var destinationMode = (opcode >> 6) & 7;
			var sourceMode = (opcode >> 3) & 7;
			var sourceRegister = opcode & 7;
			var source = FormatEffectiveAddress(address + 2, sourceMode, sourceRegister, size, readWord, out var sourceExtensionWords);
			var destination = FormatEffectiveAddress(
				address + 2 + (uint)(sourceExtensionWords * 2),
				destinationMode,
				destinationRegister,
				size,
				readWord,
				out var destinationExtensionWords);
			byteLength = 2 + ((sourceExtensionWords + destinationExtensionWords) * 2);
			var mnemonic = destinationMode == 1 ? "MOVEA" : "MOVE";
			return $"{mnemonic}.{SizeSuffix(size)} {source},{destination}";
		}

		if ((opcode & 0xFFC0) is 0x4200 or 0x4240 or 0x4280)
		{
			var size = DecodeUnarySize(opcode);
			var ea = FormatEffectiveAddress(address + 2, opcode, size, readWord, out var extensionWords);
			byteLength = 2 + (extensionWords * 2);
			return $"CLR.{SizeSuffix(size)} {ea}";
		}

		if ((opcode & 0xFFC0) is 0x4A00 or 0x4A40 or 0x4A80)
		{
			var size = DecodeUnarySize(opcode);
			var ea = FormatEffectiveAddress(address + 2, opcode, size, readWord, out var extensionWords);
			byteLength = 2 + (extensionWords * 2);
			return $"TST.{SizeSuffix(size)} {ea}";
		}

		if (opcode is 0x4E7A or 0x4E7B)
		{
			byteLength = 4;
			return "MOVEC (68010+, illegal on 68000)";
		}

		return $"DC.W ${opcode:X4}";
	}

	private static M68kDisplayOperandSize DecodeUnarySize(ushort opcode)
	{
		return ((opcode >> 6) & 3) switch
		{
			0 => M68kDisplayOperandSize.Byte,
			1 => M68kDisplayOperandSize.Word,
			_ => M68kDisplayOperandSize.Long
		};
	}

	private static string FormatEffectiveAddress(
		uint extensionAddress,
		ushort opcode,
		M68kDisplayOperandSize size,
		TryReadM68kWord readWord,
		out int extensionWords)
	{
		var mode = (opcode >> 3) & 7;
		var register = opcode & 7;
		return FormatEffectiveAddress(extensionAddress, mode, register, size, readWord, out extensionWords);
	}

	private static string FormatEffectiveAddress(
		uint extensionAddress,
		int mode,
		int register,
		M68kDisplayOperandSize size,
		TryReadM68kWord readWord,
		out int extensionWords)
	{
		extensionWords = 0;
		switch (mode)
		{
			case 0:
				return $"D{register}";
			case 1:
				return $"A{register}";
			case 2:
				return $"(A{register})";
			case 3:
				return $"(A{register})+";
			case 4:
				return $"-(A{register})";
			case 5:
				extensionWords = 1;
				return ReadSignedWord(extensionAddress, readWord, out var displacement)
					? $"${displacement:X4}(A{register})"
					: $"????(A{register})";
			case 6:
				extensionWords = 1;
				return readWord(extensionAddress, out var indexWord)
					? FormatIndexed(indexWord, $"A{register}")
					: $"????(A{register},Xn)";
			case 7:
				return FormatMode7(extensionAddress, register, size, readWord, out extensionWords);
			default:
				return "???";
		}
	}

	private static string FormatMode7(
		uint extensionAddress,
		int register,
		M68kDisplayOperandSize size,
		TryReadM68kWord readWord,
		out int extensionWords)
	{
		extensionWords = 0;
		switch (register)
		{
			case 0:
				extensionWords = 1;
				return readWord(extensionAddress, out var absoluteWord)
					? $"${absoluteWord:X4}.W"
					: "$????.W";
			case 1:
				extensionWords = 2;
				return ReadLong(extensionAddress, readWord, out var absoluteLong)
					? $"${absoluteLong & 0x00FF_FFFF:X6}.L"
					: "$????????.L";
			case 2:
				extensionWords = 1;
				return ReadSignedWord(extensionAddress, readWord, out var displacement)
					? $"${displacement:X4}(PC)"
					: "????(PC)";
			case 3:
				extensionWords = 1;
				return readWord(extensionAddress, out var indexWord)
					? FormatIndexed(indexWord, "PC")
					: "????(PC,Xn)";
			case 4:
				extensionWords = size == M68kDisplayOperandSize.Long ? 2 : 1;
				return size == M68kDisplayOperandSize.Long
					? ReadLong(extensionAddress, readWord, out var immediateLong) ? $"#${immediateLong:X8}" : "#????????"
					: readWord(extensionAddress, out var immediateWord) ? $"#${immediateWord:X4}" : "#????";
			default:
				return "???";
		}
	}

	private static string FormatIndexed(ushort extensionWord, string baseRegister)
	{
		var displacement = unchecked((sbyte)(extensionWord & 0xFF));
		var indexRegisterKind = (extensionWord & 0x8000) != 0 ? 'A' : 'D';
		var indexRegister = (extensionWord >> 12) & 7;
		var indexSize = (extensionWord & 0x0800) != 0 ? 'L' : 'W';
		return $"{displacement:+#;-#;0}({baseRegister},{indexRegisterKind}{indexRegister}.{indexSize})";
	}

	private static bool ReadSignedWord(uint address, TryReadM68kWord readWord, out short value)
	{
		if (readWord(address, out var word))
		{
			value = unchecked((short)word);
			return true;
		}

		value = 0;
		return false;
	}

	private static bool ReadLong(uint address, TryReadM68kWord readWord, out uint value)
	{
		if (readWord(address, out var high) && readWord(address + 2, out var low))
		{
			value = ((uint)high << 16) | low;
			return true;
		}

		value = 0;
		return false;
	}

	private static string FormatWords(uint address, int byteLength, TryReadM68kWord readWord)
	{
		var wordCount = Math.Clamp((byteLength + 1) / 2, 1, 4);
		Span<char> chars = stackalloc char[(wordCount * 5) - 1];
		var offset = 0;
		for (var i = 0; i < wordCount; i++)
		{
			if (i > 0)
			{
				chars[offset++] = ' ';
			}

			var text = readWord(address + (uint)(i * 2), out var word) ? word.ToString("X4") : "????";
			for (var charIndex = 0; charIndex < 4; charIndex++)
			{
				chars[offset++] = text[charIndex];
			}
		}

		return new string(chars);
	}

	private static char SizeSuffix(M68kDisplayOperandSize size)
		=> size switch
		{
			M68kDisplayOperandSize.Byte => 'B',
			M68kDisplayOperandSize.Word => 'W',
			_ => 'L'
		};

	private enum M68kDisplayOperandSize
	{
		Byte,
		Word,
		Long
	}
}
