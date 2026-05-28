using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaKickstartTests
{
	[Fact]
	public void HostShim13InstallsExecBaseAndLibraryTraps()
	{
		var bus = new AmigaBus();
		var allocCalled = false;
		var host = new AmigaKickstartHost(AmigaKickstartConfiguration.HostShim13);

		host.Install(bus, CreateTrapTable(allocMem: state =>
		{
			allocCalled = true;
			state.D[0] = 0x0004_2000;
		}));

		Assert.Equal(AmigaKickstartHost.ExecStructAddress, bus.ReadLong(0));
		Assert.Equal(AmigaKickstartHost.ExecLibraryBase, bus.ReadLong(4));
		Assert.True(bus.TryInvokeHost(Lvo(AmigaKickstartHost.ExecLibraryBase, -198), new M68kCpuState()));

		var state = new M68kCpuState();
		Assert.True(bus.TryInvokeHost(Lvo(AmigaKickstartHost.ExecLibraryBase, -198), state));
		Assert.True(allocCalled);
		Assert.Equal(0x0004_2000u, state.D[0]);
	}

	[Fact]
	public void HostShim13MapsMinimalRomFontForBootPrograms()
	{
		var bus = new AmigaBus();
		var host = new AmigaKickstartHost(AmigaKickstartConfiguration.HostShim13);

		host.Install(bus, CreateTrapTable());

		Assert.Equal(0x0018_6C6Cu, bus.ReadLong(AmigaKickstartRomFont.FontMarkerAddress));
		Assert.Equal(0xFCu, bus.ReadByte(AmigaKickstartRomFont.FontBaseAddress + (byte)'F'));
		Assert.Equal(0xC0u, bus.ReadByte(AmigaKickstartRomFont.FontBaseAddress + 0x100 + (byte)'F'));
		Assert.Equal(0xF8u, bus.ReadByte(AmigaKickstartRomFont.FontBaseAddress + 0x200 + (byte)'F'));
		Assert.Equal(0xC0u, bus.ReadByte(AmigaKickstartRomFont.FontBaseAddress + 0x180 + (byte)'F'));
		Assert.Equal(0xF8u, bus.ReadByte(AmigaKickstartRomFont.FontBaseAddress + 0x240 + (byte)'F'));
		Assert.Equal(bus.ReadByte(AmigaKickstartRomFont.FontBaseAddress + 0x100 + (byte)'F'), bus.ReadByte(AmigaKickstartRomFont.FontBaseAddress + 0x100 + (byte)'f'));

		var original = bus.ReadByte(AmigaKickstartRomFont.FontBaseAddress);
		bus.WriteByte(AmigaKickstartRomFont.FontBaseAddress, (byte)(original ^ 0xFF), 0);
		Assert.Equal(original, bus.ReadByte(AmigaKickstartRomFont.FontBaseAddress));
	}

	[Fact]
	public void HostShim13OpenLibraryReturnsKnownFakeLibraryBases()
	{
		var bus = new AmigaBus();
		var host = new AmigaKickstartHost(AmigaKickstartConfiguration.HostShim13);
		host.Install(bus, CreateTrapTable(openLibrary: state =>
		{
			var name = ReadString(bus, state.A[1]);
			state.D[0] = name.Contains("dos", StringComparison.OrdinalIgnoreCase)
				? AmigaKickstartHost.DosLibraryBase
				: AmigaKickstartHost.DummyLibraryBase;
		}));
		WriteString(bus, 0x1000, "dos.library");
		var state = new M68kCpuState();
		state.A[1] = 0x1000;

		Assert.True(bus.TryInvokeHost(Lvo(AmigaKickstartHost.ExecLibraryBase, -408), state));

		Assert.Equal(AmigaKickstartHost.DosLibraryBase, state.D[0]);
	}

	[Fact]
	public void RomImageConfigurationMapsKickstartBytesWithoutInstallingHostTraps()
	{
		var bus = new AmigaBus();
		var rom = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
		var host = new AmigaKickstartHost(AmigaKickstartConfiguration.FromRomImage(AmigaKickstartVersion.Kickstart13, rom));
		var baseAddress = 0x0100_0000u - (uint)rom.Length;

		host.Install(bus, CreateTrapTable());

		Assert.Equal(rom[0], bus.ReadByte(baseAddress));
		Assert.Equal(rom[^1], bus.ReadByte(baseAddress + (uint)rom.Length - 1));
		Assert.False(bus.TryInvokeHost(Lvo(AmigaKickstartHost.ExecLibraryBase, -198), new M68kCpuState()));
		Assert.False(bus.TryInvokeHost(Lvo(AmigaKickstartHost.CiaBResourceBase, -18), new M68kCpuState()));
	}

	[Fact]
	public void HostShim13InstallsCiaAAndCiaBResourceTraps()
	{
		var bus = new AmigaBus();
		var ableCalls = new List<uint>();
		var setCalls = new List<uint>();
		var host = new AmigaKickstartHost(AmigaKickstartConfiguration.HostShim13);
		host.Install(bus, CreateTrapTable(
			ableIcr: state =>
			{
				ableCalls.Add(state.A[6]);
				state.D[0] = 0x11;
			},
			setIcr: state =>
			{
				setCalls.Add(state.A[6]);
				state.D[0] = 0x22;
			}));

		var ciaAState = new M68kCpuState();
		ciaAState.A[6] = AmigaKickstartHost.CiaAResourceBase;
		ciaAState.D[0] = 0x81;
		Assert.True(bus.TryInvokeHost(Lvo(AmigaKickstartHost.CiaAResourceBase, -18), ciaAState));

		var ciaBState = new M68kCpuState();
		ciaBState.A[6] = AmigaKickstartHost.CiaBResourceBase;
		ciaBState.D[0] = 0x81;
		Assert.True(bus.TryInvokeHost(Lvo(AmigaKickstartHost.CiaBResourceBase, -24), ciaBState));

		Assert.Equal(new[] { AmigaKickstartHost.CiaAResourceBase }, ableCalls);
		Assert.Equal(new[] { AmigaKickstartHost.CiaBResourceBase }, setCalls);
		Assert.Equal(0x11u, ciaAState.D[0]);
		Assert.Equal(0x22u, ciaBState.D[0]);
	}

	private static AmigaKickstartTrapTable CreateTrapTable(
		Action<M68kCpuState>? nullCallback = null,
		Action<M68kCpuState>? ok = null,
		Action<M68kCpuState>? openLibrary = null,
		Action<M68kCpuState>? allocMem = null,
		Action<M68kCpuState>? allocMemAndStore = null,
		Action<M68kCpuState>? freeMem = null,
		Action<M68kCpuState>? causeInterrupt = null,
		Action<M68kCpuState>? addInterrupt = null,
		Action<M68kCpuState>? removeInterrupt = null,
		Action<M68kCpuState>? ableIcr = null,
		Action<M68kCpuState>? setIcr = null,
		Action<M68kCpuState>? dosOpen = null,
		Action<M68kCpuState>? dosClose = null,
		Action<M68kCpuState>? dosRead = null,
		Action<M68kCpuState>? dosSeek = null)
	{
		static void Ok(M68kCpuState state)
		{
			state.D[0] = 0;
		}

		return new AmigaKickstartTrapTable(
			0x00F0_0010,
			nullCallback ?? Ok,
			ok ?? Ok,
			openLibrary ?? Ok,
			allocMem ?? Ok,
			allocMemAndStore ?? Ok,
			freeMem ?? Ok,
			causeInterrupt ?? Ok,
			addInterrupt ?? Ok,
			removeInterrupt ?? Ok,
			ableIcr ?? Ok,
			setIcr ?? Ok,
			dosOpen ?? Ok,
			dosClose ?? Ok,
			dosRead ?? Ok,
			dosSeek ?? Ok);
	}

	private static uint Lvo(uint libraryBase, int displacement)
	{
		return unchecked((uint)((int)libraryBase + displacement));
	}

	private static void WriteString(AmigaBus bus, uint address, string value)
	{
		for (var i = 0; i < value.Length; i++)
		{
			bus.WriteByte(address + (uint)i, (byte)value[i], 0);
		}

		bus.WriteByte(address + (uint)value.Length, 0, 0);
	}

	private static string ReadString(AmigaBus bus, uint address)
	{
		var chars = new List<char>();
		for (var i = 0; i < 64; i++)
		{
			var value = bus.ReadByte(address + (uint)i);
			if (value == 0)
			{
				break;
			}

			chars.Add((char)value);
		}

		return new string(chars.ToArray());
	}
}
