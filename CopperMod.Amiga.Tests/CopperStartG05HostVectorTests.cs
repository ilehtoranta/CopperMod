using Amiga;
using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class CopperStartG05HostVectorTests
{
	[Fact]
	public void SetSrGetCcAndStackSwapRouteThroughPortableStateCore()
	{
		var machine = StartCopperStart();
		var bus = machine.Bus;
		var state = new M68kCpuState { StatusRegister = 0x2700 };
		state.D[0] = 5;
		state.D[1] = 0x1F;
		Assert.True(InvokeExec(bus, ExecLvo.SetSR, state));
		Assert.Equal(0x2700u, state.D[0]);
		Assert.Equal(0x2705, state.StatusRegister);
		Assert.True(InvokeExec(bus, ExecLvo.GetCC, state));
		Assert.Equal(5u, state.D[0]);

		var task = bus.ReadLong(AmigaKickstartHost.ExecLibraryBase + (uint)ExecLayout.ExecBase.ThisTask);
		const uint swap = 0x2800;
		bus.WriteLong(task + (uint)ExecLayout.Task.StackLower, 0x3000);
		bus.WriteLong(task + (uint)ExecLayout.Task.StackUpper, 0x4000);
		bus.WriteLong(swap + (uint)ExecLayout.StackSwapStruct.Lower, 0x5000);
		bus.WriteLong(swap + (uint)ExecLayout.StackSwapStruct.Upper, 0x6000);
		bus.WriteLong(swap + (uint)ExecLayout.StackSwapStruct.Pointer, 0x5FFC);
		state.ResetStackPointers(supervisorStackPointer: 0x7000, userStackPointer: 0x3FFC, supervisorMode: false);
		state.A[0] = swap;
		Assert.True(InvokeExec(bus, ExecLvo.StackSwap, state));
		Assert.Equal(0x5FFCu, state.A[7]);
		Assert.Equal(0x3000u, bus.ReadLong(swap + (uint)ExecLayout.StackSwapStruct.Lower));
		Assert.Equal(0x4000u, bus.ReadLong(swap + (uint)ExecLayout.StackSwapStruct.Upper));
		Assert.Equal(0x3FFCu, bus.ReadLong(swap + (uint)ExecLayout.StackSwapStruct.Pointer));
	}

	private static Machine StartCopperStart()
	{
		var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		new AmigaBootController(machine).StartBootFromDisk(CreateBootableDisk());
		return machine;
	}

	private static AmigaDiskImage CreateBootableDisk()
	{
		var data = new byte[AmigaDiskImage.StandardAdfSize];
		data[0] = (byte)'D'; data[1] = (byte)'O'; data[2] = (byte)'S';
		BigEndian.WriteUInt32(data, 4, CalculateBootChecksum(data.AsSpan(0, 1024)));
		return AmigaDiskImage.FromAdfBytes(data);
	}

	private static uint CalculateBootChecksum(ReadOnlySpan<byte> block)
	{
		var sum = 0u;
		for (var offset = 0; offset < block.Length; offset += 4)
		{
			var value = BigEndian.ReadUInt32(block, offset, "boot checksum word");
			var next = sum + value; if (next < sum) next++; sum = next;
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
