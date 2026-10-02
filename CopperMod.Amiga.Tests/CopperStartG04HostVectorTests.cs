using Amiga;
using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class CopperStartG04HostVectorTests
{
	[Fact]
	public void SyntheticLibraryRegistryUsesGuestExecLists()
	{
		var machine = StartCopperStart();
		var bus = machine.Bus;
		const uint library = 0x2800;
		const uint name = 0x2900;
		var nameBytes = System.Text.Encoding.ASCII.GetBytes("portable.library\0");
		for (var index = 0; index < nameBytes.Length; index++)
			bus.WriteByte(name + (uint)index, nameBytes[index], 0);
		bus.WriteLong(library + (uint)ExecLayout.Node.Name, name);
		bus.WriteWord(library + (uint)ExecLayout.Library.Version, 1);

		var add = new M68kCpuState();
		add.A[1] = library;
		Assert.True(InvokeExec(bus, ExecLvo.AddLibrary, add));

		var find = new M68kCpuState();
		find.A[0] = AmigaKickstartHost.ExecLibraryBase + (uint)ExecLayout.ExecBase.LibraryList;
		find.A[1] = name;
		Assert.True(InvokeExec(bus, ExecLvo.FindName, find));
		Assert.Equal(library, find.D[0]);

		var remove = new M68kCpuState();
		remove.A[1] = library;
		Assert.True(InvokeExec(bus, ExecLvo.RemLibrary, remove));
		Assert.True(InvokeExec(bus, ExecLvo.FindName, find));
		Assert.Equal(0u, find.D[0]);
	}

	[Fact]
	public void SetFunctionAndSumLibraryRouteThroughPortableCore()
	{
		var machine = StartCopperStart();
		var bus = machine.Bus;
		const uint vectors = 0x2200;
		const uint oldFunction = 0x2300;
		const uint newFunction = 0x2400;
		bus.WriteLong(vectors, oldFunction);
		bus.WriteLong(vectors + 4, uint.MaxValue);
		var make = new M68kCpuState();
		make.A[0] = vectors;
		make.D[0] = Library.Size;
		Assert.True(InvokeExec(bus, ExecLvo.MakeLibrary, make));
		var library = make.D[0];
		Assert.NotEqual(0u, library);
		Assert.Equal(0u, library & 3);
		Assert.Equal((ushort)8, bus.ReadWord(library + (uint)ExecLayout.Library.NegativeSize));
		bus.WriteByte(library + (uint)ExecLayout.Library.Flags, (byte)LibraryFlags.SumUsed, 0);
		var set = new M68kCpuState();
		set.A[1] = library;
		set.A[0] = unchecked((uint)-6);
		set.D[0] = newFunction;

		Assert.True(InvokeExec(bus, ExecLvo.SetFunction, set));

		Assert.Equal(oldFunction, set.D[0]);
		Assert.Equal((ushort)0x4EF9, bus.ReadWord(library - 6));
		Assert.Equal(newFunction, bus.ReadLong(library - 4));
		Assert.Equal(unchecked(0x4EF9u + newFunction),
			bus.ReadLong(library + (uint)ExecLayout.Library.Checksum));
		Assert.Equal((byte)LibraryFlags.SumUsed,
			bus.ReadByte(library + (uint)ExecLayout.Library.Flags));

		var sum = new M68kCpuState();
		sum.A[1] = library;
		Assert.True(InvokeExec(bus, ExecLvo.SumLibrary, sum));
		Assert.Equal(unchecked(0x4EF9u + newFunction),
			bus.ReadLong(library + (uint)ExecLayout.Library.Checksum));
	}

	[Fact]
	public void SumKickDataUsesSdkExecBaseOffsetsThroughHostGateway()
	{
		var machine = StartCopperStart();
		var bus = machine.Bus;
		const uint table = 0x2600;
		const uint tag = 0x1234_5678;
		bus.WriteLong(table, tag);
		bus.WriteLong(table + 4, 0);
		bus.WriteLong(AmigaKickstartHost.ExecLibraryBase + (uint)ExecLayout.ExecBase.KickTagPtr, table);
		bus.WriteLong(AmigaKickstartHost.ExecLibraryBase + (uint)ExecLayout.ExecBase.KickMemPtr, 0);
		var state = new M68kCpuState();

		Assert.True(InvokeExec(bus, ExecLvo.SumKickData, state));

		Assert.Equal(tag - 1, state.D[0]);
	}

	private static Machine StartCopperStart()
	{
		var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		return machine;
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

	private static uint CalculateBootChecksum(ReadOnlySpan<byte> block)
	{
		var sum = 0u;
		for (var offset = 0; offset < block.Length; offset += 4)
		{
			var value = BigEndian.ReadUInt32(block, offset, "boot checksum word");
			var next = sum + value;
			if (next < sum) next++;
			sum = next;
		}
		return ~sum;
	}

	private static bool InvokeExec(AmigaBus bus, short lvo, M68kCpuState state)
	{
		var address = unchecked((uint)((int)AmigaKickstartHost.ExecLibraryBase + lvo));
		return bus.ReadWord(address) == 0xFF00 &&
			bus.TryInvokeHostGateway(address, bus.ReadLong(address + 2), state);
	}
}
