using System.Security.Cryptography;
using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaDiagRomTests
{
	private const int ExpectedDiagRomLength = 524_288;
	private const uint ExpectedDiagRomBase = 0x00F8_0000;
	private const uint ExpectedDiagRomResetPc = 0x00F8_00D6;
	private const string ExpectedDiagRomSha256 = "8DA1CF37B74B2BF1BDE3DC725D00A27EB6BD402E859BAFAF9CA2E74BBF0273F6";

	[Fact]
	public void DiagRomMapsAtF80000AndOverlayMirrorsVectorsWhenAvailable()
	{
		if (!TryLoadSupportedDiagRom(out var rom, out _))
		{
			return;
		}

		var bus = new AmigaBus();
		var host = new AmigaKickstartHost(AmigaKickstartConfiguration.FromRomImage(AmigaKickstartVersion.Kickstart13, rom));
		var resetVector = BigEndian.ReadUInt32(rom, 4, "DiagROM reset program counter");

		host.Install(bus, CreateTrapTable());

		Assert.Equal(ExpectedDiagRomBase, 0x0100_0000u - (uint)rom.Length);
		Assert.Equal(ExpectedDiagRomResetPc, resetVector);
		Assert.Equal(ExpectedDiagRomResetPc, bus.ReadLong(ExpectedDiagRomBase + 4));
		Assert.Equal(ExpectedDiagRomResetPc, bus.ReadLong(0x0000_0004));

		var originalRomByte = bus.ReadByte(ExpectedDiagRomBase);
		bus.WriteByte(ExpectedDiagRomBase, (byte)(originalRomByte ^ 0xFF), 0);
		Assert.Equal(originalRomByte, bus.ReadByte(ExpectedDiagRomBase));

		bus.WriteByte(0x0000_0000, 0x42, 0);
		Assert.Equal(BigEndian.ReadUInt32(rom, 0, "DiagROM initial stack vector"), bus.ReadLong(0x0000_0000));

		bus.WriteByte(0x00BF_E001, 0x00, 0);
		Assert.Equal(0x4200_0000u, bus.ReadLong(0x0000_0000));
	}

	[Fact]
	public void DiagRomColdBootStartsAtResetVectorWhenAvailable()
	{
		if (!TryLoadSupportedDiagRom(out var rom, out _))
		{
			return;
		}

		var options = AmigaMachineOptions
			.ForProfile(AmigaMachineProfile.A500Pal512KBoot)
			.WithKickstart(AmigaKickstartConfiguration.FromRomImage(AmigaKickstartVersion.Kickstart13, rom));
		var machine = new AmigaMachine(options);
		var boot = new AmigaBootController(machine);
		var blankDisk = AmigaDiskImage.FromAdfBytes(new byte[AmigaDiskImage.StandardAdfSize], "diagrom-blank.adf");

		boot.StartKickstartRomBoot(blankDisk);

		Assert.Equal(ExpectedDiagRomResetPc, machine.Cpu.State.ProgramCounter);
		Assert.Equal(ExpectedDiagRomResetPc, machine.Bus.ReadLong(0x0000_0004));
		Assert.Equal(ExpectedDiagRomResetPc, machine.Bus.ReadLong(ExpectedDiagRomBase + 4));
		Assert.False(machine.Cpu.State.Halted);
	}

	private static bool TryLoadSupportedDiagRom(out byte[] rom, out string path)
	{
		path = TryFindWorkspaceFile("CopperScreen", "ROM", "DiagROM", "diagrom.rom") ?? string.Empty;
		if (path.Length == 0)
		{
			rom = Array.Empty<byte>();
			return false;
		}

		rom = File.ReadAllBytes(path);
		if (rom.Length != ExpectedDiagRomLength)
		{
			rom = Array.Empty<byte>();
			return false;
		}

		var hash = Convert.ToHexString(SHA256.HashData(rom));
		if (!string.Equals(hash, ExpectedDiagRomSha256, StringComparison.OrdinalIgnoreCase))
		{
			rom = Array.Empty<byte>();
			return false;
		}

		return true;
	}

	private static string? TryFindWorkspaceFile(params string[] parts)
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null)
		{
			var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		return null;
	}

	private static AmigaKickstartTrapTable CreateTrapTable()
	{
		static void Ok(M68kCpuState state)
		{
			state.D[0] = 0;
		}

		return new AmigaKickstartTrapTable(
			0x00F0_0010,
			Ok,
			Ok,
			Ok,
			Ok,
			Ok,
			Ok,
			Ok,
			Ok,
			Ok,
			Ok,
			Ok,
			Ok,
			Ok,
			Ok,
			Ok);
	}
}
