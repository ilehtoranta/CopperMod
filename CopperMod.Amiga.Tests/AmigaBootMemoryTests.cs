using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaBootMemoryTests
{
	private const int ExecMemListOffset = 0x142;
	private const int MemNodeNameOffset = 0x0A;
	private const int MemHeaderAttributesOffset = 0x0E;
	private const int MemHeaderFirstChunkOffset = 0x10;
	private const int MemHeaderLowerOffset = 0x14;
	private const int MemHeaderUpperOffset = 0x18;
	private const int MemHeaderFreeOffset = 0x1C;
	private const int MemChunkNextOffset = 0x00;
	private const int MemChunkBytesOffset = 0x04;
	private const uint MemfPublic = 0x0000_0001;
	private const uint MemfChip = 0x0000_0002;
	private const uint MemfFast = 0x0000_0004;

	[Fact]
	public void BootShimBuildsKickstartStyleMemListWithPseudoFastFirst()
	{
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var listAddress = AmigaKickstartHost.ExecLibraryBase + ExecMemListOffset;
		var fastHeader = bus.ExpansionRamBase;
		var chipHeader = fastHeader + 0x40;
		var fastLower = bus.ExpansionRamBase + 0x100;
		var fastUpper = bus.ExpansionRamBase + (uint)bus.ExpansionRam.Length - 0x1000;
		var chipLower = 0x100u;
		var chipUpper = (uint)bus.ChipRam.Length;

		Assert.Equal(fastHeader, bus.ReadLong(listAddress));
		Assert.Equal(0u, bus.ReadLong(listAddress + 4));
		Assert.Equal(chipHeader, bus.ReadLong(listAddress + 8));
		AssertMemoryHeader(bus, fastHeader, chipHeader, listAddress, MemfPublic | MemfFast, fastLower, fastUpper, "pseudo-fast");
		AssertMemoryHeader(bus, chipHeader, listAddress + 4, fastHeader, MemfPublic | MemfChip, chipLower, chipUpper, "chip");
	}

	[Fact]
	public void ChipOnlyBootProfileKeepsMemListMetadataOutOfPublicLowMemory()
	{
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KChipOnlyBoot);
		var bus = machine.Bus;
		var listAddress = AmigaKickstartHost.ExecLibraryBase + ExecMemListOffset;
		var chipHeader = 0x2400u;
		var chipLower = 0x2800u;
		var chipUpper = (uint)bus.ChipRam.Length - 0x1000;

		Assert.Empty(bus.ExpansionRam);
		Assert.Equal(chipHeader, bus.ReadLong(listAddress));
		Assert.Equal(0u, bus.ReadLong(listAddress + 4));
		Assert.Equal(chipHeader, bus.ReadLong(listAddress + 8));
		AssertMemoryHeader(bus, chipHeader, listAddress + 4, listAddress, MemfPublic | MemfChip, chipLower, chipUpper, "chip");
	}

	[Fact]
	public void AllocMemAvailMemAndFreeMemUseKickstartMemListChunks()
	{
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var fastHeader = bus.ExpansionRamBase;
		var chipHeader = fastHeader + 0x40;
		var initialFastFree = bus.ReadLong(fastHeader + MemHeaderFreeOffset);
		var initialChipFree = bus.ReadLong(chipHeader + MemHeaderFreeOffset);

		var publicAllocation = InvokeAllocMem(bus, 0x1000, MemfPublic);
		var chipAllocation = InvokeAllocMem(bus, 0x2000, MemfPublic | MemfChip);

		Assert.Equal(bus.ExpansionRamBase + (uint)bus.ExpansionRam.Length - 0x2000, publicAllocation);
		Assert.Equal((uint)bus.ChipRam.Length - 0x2000, chipAllocation);
		Assert.Equal(initialFastFree - 0x1000, bus.ReadLong(fastHeader + MemHeaderFreeOffset));
		Assert.Equal(initialChipFree - 0x2000, bus.ReadLong(chipHeader + MemHeaderFreeOffset));
		Assert.Equal(initialFastFree - 0x1000, InvokeAvailMem(bus, MemfFast));
		Assert.Equal(initialChipFree - 0x2000, InvokeAvailMem(bus, MemfChip));

		InvokeFreeMem(bus, publicAllocation, 0x1000);
		InvokeFreeMem(bus, chipAllocation, 0x2000);

		Assert.Equal(initialFastFree, bus.ReadLong(fastHeader + MemHeaderFreeOffset));
		Assert.Equal(initialChipFree, bus.ReadLong(chipHeader + MemHeaderFreeOffset));
		Assert.Equal(initialFastFree, InvokeAvailMem(bus, MemfFast));
		Assert.Equal(initialChipFree, InvokeAvailMem(bus, MemfChip));
		Assert.Equal(bus.ExpansionRamBase + 0x100, bus.ReadLong(fastHeader + MemHeaderFirstChunkOffset));
		Assert.Equal(0x100u, bus.ReadLong(chipHeader + MemHeaderFirstChunkOffset));
	}

	private static AmigaMachine StartBootShim(AmigaMachineProfile profile)
	{
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(profile));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		return machine;
	}

	private static uint InvokeAllocMem(AmigaBus bus, uint byteCount, uint flags)
	{
		var state = new M68kCpuState();
		state.D[0] = byteCount;
		state.D[1] = flags;
		Assert.True(bus.TryInvokeHost(Lvo(AmigaKickstartHost.ExecLibraryBase, -198), state));
		return state.D[0];
	}

	private static uint InvokeAvailMem(AmigaBus bus, uint flags)
	{
		var state = new M68kCpuState();
		state.D[1] = flags;
		Assert.True(bus.TryInvokeHost(Lvo(AmigaKickstartHost.ExecLibraryBase, -216), state));
		return state.D[0];
	}

	private static void InvokeFreeMem(AmigaBus bus, uint address, uint byteCount)
	{
		var state = new M68kCpuState();
		state.A[1] = address;
		state.D[0] = byteCount;
		Assert.True(bus.TryInvokeHost(Lvo(AmigaKickstartHost.ExecLibraryBase, -210), state));
		Assert.Equal(0u, state.D[0]);
	}

	private static void AssertMemoryHeader(
		AmigaBus bus,
		uint header,
		uint successor,
		uint predecessor,
		uint attributes,
		uint lower,
		uint upper,
		string name)
	{
		var freeBytes = upper - lower;
		Assert.Equal(successor, bus.ReadLong(header));
		Assert.Equal(predecessor, bus.ReadLong(header + 4));
		Assert.Equal((ushort)attributes, bus.ReadWord(header + MemHeaderAttributesOffset));
		Assert.Equal(lower, bus.ReadLong(header + MemHeaderLowerOffset));
		Assert.Equal(upper, bus.ReadLong(header + MemHeaderUpperOffset));
		Assert.Equal(freeBytes, bus.ReadLong(header + MemHeaderFreeOffset));
		Assert.Equal(lower, bus.ReadLong(header + MemHeaderFirstChunkOffset));
		Assert.Equal(0u, bus.ReadLong(lower + MemChunkNextOffset));
		Assert.Equal(freeBytes, bus.ReadLong(lower + MemChunkBytesOffset));
		Assert.Equal(name, ReadCString(bus, bus.ReadLong(header + MemNodeNameOffset), 16));
	}

	private static string ReadCString(AmigaBus bus, uint address, int maxLength)
	{
		var chars = new char[maxLength];
		var count = 0;
		for (; count < chars.Length; count++)
		{
			var value = bus.ReadByte(address + (uint)count);
			if (value == 0)
			{
				break;
			}

			chars[count] = (char)value;
		}

		return new string(chars, 0, count);
	}

	private static AmigaDiskImage CreateBootableDisk()
	{
		var data = new byte[AmigaDiskImage.StandardAdfSize];
		data[0] = (byte)'D';
		data[1] = (byte)'O';
		data[2] = (byte)'S';
		BigEndian.WriteUInt32(data, 4, CalculateBootChecksum(data.AsSpan(0, 1024)));
		return AmigaDiskImage.FromAdfBytes(data);
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

	private static uint Lvo(uint libraryBase, int displacement)
	{
		return unchecked((uint)((int)libraryBase + displacement));
	}
}
