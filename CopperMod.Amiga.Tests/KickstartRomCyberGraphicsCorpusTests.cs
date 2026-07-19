using CopperMod.Amiga;
using Xunit.Abstractions;

namespace CopperMod.Amiga.Tests;

/// <summary>
/// Real-ROM corpus probes. Supply a legally obtained ROM with
/// COPPER_AMIGA_KICKSTART_ROM and identify it with COPPER_AMIGA_KICKSTART_VERSION
/// (currently 3.0 or 3.1). ROM hashes are deliberately not part of this corpus.
/// </summary>
public sealed class KickstartRomCyberGraphicsCorpusTests
{
	private const string RomPathVariable = "COPPER_AMIGA_KICKSTART_ROM";
	private const string RomVersionVariable = "COPPER_AMIGA_KICKSTART_VERSION";
	private const uint ResultAddress = 0x0000_0400;
	private readonly ITestOutputHelper _output;

	public KickstartRomCyberGraphicsCorpusTests(ITestOutputHelper output)
		=> _output = output;

	[Fact]
	public void CyberGraphicsOpenLibraryProbeIsAvailableToRealKickstart()
	{
		if (!TryLoadConfiguredRom(out var rom, out var version, out var description))
		{
			return;
		}
		_output.WriteLine($"CGX OpenLibrary corpus probe: {description}");

		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithCpu(AmigaM68kCoreFactory.Default, M68kBackendKind.AccurateM68040)
			.WithRtgVram(16L * 1024 * 1024)
			.WithKickstart(KickstartConfiguration.FromRomImage(version, rom))
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		machine.Bus.WriteLong(ResultAddress, 0);

		_ = boot.BootFromKickstartRom(CreateCyberGraphicsOpenLibraryProbeAdf(), maxInstructions: 1_000_000);

		Assert.True(
			machine.Bus.ReadLong(ResultAddress) != 0,
			$"CyberGraphX OpenLibrary probe failed with {description}.");
	}

	internal static AmigaDiskImage CreateCyberGraphicsOpenLibraryProbeAdf()
	{
		var data = new byte[AmigaDiskImage.StandardAdfSize];
		data[0] = (byte)'D';
		data[1] = (byte)'O';
		data[2] = (byte)'S';

		const int entry = 0x0C;
		const int nameOffset = 0x1E;
		var offset = entry;
		WriteWord(data, ref offset, 0x43FA); // LEA cybergraphics.library(PC),A1
		WriteWord(data, ref offset, checked((ushort)(nameOffset - (entry + 4))));
		WriteWord(data, ref offset, 0x7000); // MOVEQ #0,D0
		WriteWord(data, ref offset, 0x4EAE); // JSR OpenLibrary(A6)
		WriteWord(data, ref offset, unchecked((ushort)-408));
		WriteWord(data, ref offset, 0x23C0); // MOVE.L D0,$400
		WriteLong(data, ref offset, ResultAddress);
		WriteWord(data, ref offset, 0x60FE); // BRA.S *
		foreach (var value in "cybergraphics.library"u8)
		{
			data[offset++] = value;
		}

		data[offset] = 0;
		BigEndian.WriteUInt32(data, 4, CalculateBootChecksum(data.AsSpan(0, 1024)));
		return AmigaDiskImage.FromAdfBytes(data, "cgx-openlibrary-probe.adf");
	}

	private static bool TryLoadConfiguredRom(out byte[] rom, out KickstartVersion version, out string description)
	{
		rom = Array.Empty<byte>();
		version = KickstartVersion.Kickstart13;
		description = string.Empty;
		var path = Environment.GetEnvironmentVariable(RomPathVariable);
		var versionValue = Environment.GetEnvironmentVariable(RomVersionVariable);
		if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(versionValue))
		{
			return false;
		}

		version = versionValue.Trim() switch
		{
			"3.0" or "30" => KickstartVersion.Kickstart30,
			"3.1" or "31" => KickstartVersion.Kickstart31,
			_ => throw new InvalidOperationException(
				$"{RomVersionVariable} must be 3.0 or 3.1 because CyberGraphX requires Kickstart 3.0 or newer; received '{versionValue}'.")
		};

		rom = File.ReadAllBytes(path);
		description = $"Kickstart {versionValue.Trim()} ({path})";
		return true;
	}

	private static void WriteWord(byte[] target, ref int offset, ushort value)
	{
		BigEndian.WriteUInt16(target, offset, value);
		offset += 2;
	}

	private static void WriteLong(byte[] target, ref int offset, uint value)
	{
		BigEndian.WriteUInt32(target, offset, value);
		offset += 4;
	}

	private static uint CalculateBootChecksum(ReadOnlySpan<byte> bootBlock)
	{
		var sum = 0u;
		for (var offset = 0; offset < 1024; offset += 4)
		{
			var value = BigEndian.ReadUInt32(bootBlock, offset, "boot checksum word");
			var previous = sum;
			sum += value;
			if (sum < previous)
			{
				sum++;
			}
		}

		return ~sum;
	}
}
