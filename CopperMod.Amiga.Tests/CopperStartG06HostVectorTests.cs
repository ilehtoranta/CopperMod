using Amiga;
using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class CopperStartG06HostVectorTests
{
	[Fact]
	public void InterruptVectorsUseSdkIntVectorAndPriorityLinks()
	{
		var machine = StartCopperStart();
		var bus = machine.Bus;
		const uint low = 0x2800;
		const uint high = 0x2840;
		bus.WriteByte(low + (uint)ExecLayout.Node.Priority, unchecked((byte)-5), 0);
		bus.WriteByte(high + (uint)ExecLayout.Node.Priority, 12, 0);
		bus.WriteLong(low + (uint)ExecLayout.Interrupt.Data, 0x3000);
		bus.WriteLong(high + (uint)ExecLayout.Interrupt.Data, 0x3040);
		bus.WriteLong(low + (uint)ExecLayout.Interrupt.Code, 0x4000);
		bus.WriteLong(high + (uint)ExecLayout.Interrupt.Code, 0x4040);
		var state = new M68kCpuState();
		state.D[0] = 5; state.A[1] = low;
		Assert.True(InvokeExec(bus, ExecLvo.AddIntServer, state));
		state.D[0] = 5; state.A[1] = high;
		Assert.True(InvokeExec(bus, ExecLvo.AddIntServer, state));
		var vector = AmigaKickstartHost.ExecLibraryBase + (uint)ExecLayout.ExecBase.IntVector0 + 5 * IntVector.Size;
		Assert.Equal(high, bus.ReadLong(vector + (uint)ExecLayout.IntVector.Node));
		Assert.Equal(low, bus.ReadLong(high + (uint)ExecLayout.Node.Successor));

		state.D[0] = 5;
		Assert.True(InvokeExec(bus, ExecLvo.RemIntServer, state));
		Assert.Equal(low, bus.ReadLong(vector + (uint)ExecLayout.IntVector.Node));
	}

	[Fact]
	public void CreateDeletePortAndSemaphoreRegistryRouteThroughPortableCores()
	{
		var machine = StartCopperStart();
		var bus = machine.Bus;
		var state = new M68kCpuState();
		Assert.True(InvokeExec(bus, ExecLvo.CreateMsgPort, state));
		var port = state.D[0];
		Assert.NotEqual(0u, port);
		Assert.Equal((byte)NodeType.MessagePort, bus.ReadByte(port + (uint)ExecLayout.Node.Type));
		state.A[0] = port;
		Assert.True(InvokeExec(bus, ExecLvo.DeleteMsgPort, state));

		const uint semaphore = 0x3000;
		const uint name = 0x3100;
		WriteString(bus, name, "g06.semaphore");
		bus.WriteLong(semaphore + (uint)ExecLayout.Node.Name, name);
		state.A[1] = semaphore;
		Assert.True(InvokeExec(bus, ExecLvo.AddSemaphore, state));
		state.A[1] = name;
		Assert.True(InvokeExec(bus, ExecLvo.FindSemaphore, state));
		Assert.Equal(semaphore, state.D[0]);
		state.A[1] = semaphore;
		Assert.True(InvokeExec(bus, ExecLvo.RemSemaphore, state));
		state.A[1] = name;
		Assert.True(InvokeExec(bus, ExecLvo.FindSemaphore, state));
		Assert.Equal(0u, state.D[0]);
	}

	[Fact]
	public void CauseQueuesAndDispatchesGuestSoftInterruptAtRuntimeBoundary()
	{
		var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint interrupt = 0x3200;
		const uint data = 0x3300;
		const uint code = 0x3400;
		bus.WriteByte(interrupt + (uint)ExecLayout.Node.Priority, 20, 0);
		bus.WriteLong(interrupt + (uint)ExecLayout.Interrupt.Data, data);
		bus.WriteLong(interrupt + (uint)ExecLayout.Interrupt.Code, code);
		bus.WriteWord(code, 0x4E75, 0);
		var state = new M68kCpuState();
		state.A[1] = interrupt;
		Assert.True(InvokeExec(bus, ExecLvo.Cause, state));

		var dispatched = (bool)typeof(AmigaBootController).GetMethod("TryDispatchPendingExecInterruptServer",
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
			.Invoke(boot, null)!;

		Assert.True(dispatched);
		Assert.Equal(code, machine.Cpu.State.ProgramCounter);
		Assert.Equal(data, machine.Cpu.State.A[1]);
		Assert.Equal(AmigaKickstartHost.ExecLibraryBase, machine.Cpu.State.A[6]);
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

	private static void WriteString(AmigaBus bus, uint address, string value)
	{
		for (var index = 0; index < value.Length; index++) bus.WriteByte(address + (uint)index, (byte)value[index], 0);
		bus.WriteByte(address + (uint)value.Length, 0, 0);
	}

	private static bool InvokeExec(AmigaBus bus, short lvo, M68kCpuState state)
	{
		var address = unchecked((uint)((int)AmigaKickstartHost.ExecLibraryBase + lvo));
		return bus.ReadWord(address) == 0xFF00 &&
			bus.TryInvokeHostGateway(address, bus.ReadLong(address + 2), state);
	}
}
