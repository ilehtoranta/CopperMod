using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaBusTimingTests
{
	[Fact]
	public void ZeroWaitArbiterGrantsImmediatelyAndUsesConfiguredAccessCycles()
	{
		var request = new AmigaBusAccessRequest(
			AmigaBusRequester.Cpu,
			AmigaBusAccessKind.CpuDataRead,
			AmigaBusAccessTarget.ChipRam,
			0x1000,
			AmigaBusAccessSize.Word,
			123,
			isWrite: false);

		var result = new ZeroWaitBusArbiter(baseAccessCycles: 4).Arbitrate(request);

		Assert.Equal(123, result.RequestedCycle);
		Assert.Equal(123, result.GrantedCycle);
		Assert.Equal(127, result.CompletedCycle);
		Assert.Equal(0, result.WaitCycles);
		Assert.Equal(4, result.AccessCycles);
	}

	[Fact]
	public void CpuCyclesIncludeDelayedInstructionFetchCompletion()
	{
		var arbiter = new FixedDelayArbiter(waitCycles: 5, accessCycles: 3);
		var bus = new AmigaBus(arbiter: arbiter);
		Write(bus.ChipRam, 0x1000, 0x4E, 0x71); // NOP
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);

		cpu.ExecuteInstruction();

		var fetch = Assert.Single(bus.BusAccesses, access => access.Request.Kind == AmigaBusAccessKind.CpuInstructionFetch);
		Assert.Equal(0, fetch.RequestedCycle);
		Assert.Equal(5, fetch.GrantedCycle);
		Assert.Equal(8, fetch.CompletedCycle);
		Assert.Equal(12, cpu.State.Cycles);
	}

	[Fact]
	public void DelayedCustomRegisterWritesAreStampedAtGrantedCycle()
	{
		var arbiter = new FixedDelayArbiter(waitCycles: 7, accessCycles: 2);
		var bus = new AmigaBus(arbiter: arbiter);
		var cycle = 100L;

		bus.WriteWord(0x00DFF096, 0x800F, ref cycle, AmigaBusAccessKind.CpuDataWrite);
		bus.WriteWord(0x00DFF0AA, 0x7F81, ref cycle, AmigaBusAccessKind.CpuDataWrite);
		bus.WriteByte(0x00DFF0A8, 0x20, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		Assert.Equal(127, cycle);
		Assert.Equal(3, bus.CustomRegisterWrites.Count);
		Assert.Equal(107, bus.CustomRegisterWrites[0].Cycle);
		Assert.Equal(116, bus.CustomRegisterWrites[1].Cycle);
		Assert.Equal(125, bus.CustomRegisterWrites[2].Cycle);
		var cpuCustomWrites = bus.BusAccesses
			.Where(access => access.Request.Requester == AmigaBusRequester.Cpu && access.Request.Target == AmigaBusAccessTarget.CustomRegisters)
			.ToArray();
		Assert.All(
			bus.CustomRegisterWrites.Zip(cpuCustomWrites),
			pair => Assert.Equal(pair.Second.GrantedCycle, pair.First.Cycle));
	}

	[Fact]
	public void AmigaBusClassifiesCpuAccessTargetsForArbitration()
	{
		var bus = new AmigaBus();
		bus.MapReadOnlyMemory(0x00FC0000, new byte[] { 0x12, 0x34 });
		bus.RegisterHostCallback(0x00F00000, _ => { });

		var cycle = 10L;
		_ = bus.ReadByte(0x00001000, ref cycle, AmigaBusAccessKind.CpuInstructionFetch);
		cycle = 20;
		bus.WriteWord(0x00DFF096, 0x800F, ref cycle, AmigaBusAccessKind.CpuDataWrite);
		cycle = 30;
		bus.WriteByte(0x00BFE001, 0x00, ref cycle, AmigaBusAccessKind.CpuDataWrite);
		cycle = 40;
		_ = bus.ReadByte(0x00FC0000, ref cycle, AmigaBusAccessKind.CpuDataRead);
		var state = new M68kCpuState { Cycles = 50 };
		Assert.True(bus.TryInvokeHost(0x00F00000, state));

		Assert.Contains(bus.BusAccesses, access =>
			access.Request.Kind == AmigaBusAccessKind.CpuInstructionFetch &&
			access.Request.Target == AmigaBusAccessTarget.ChipRam);
		Assert.Contains(bus.BusAccesses, access =>
			access.Request.Kind == AmigaBusAccessKind.CpuDataWrite &&
			access.Request.Target == AmigaBusAccessTarget.CustomRegisters &&
			access.Request.Size == AmigaBusAccessSize.Word);
		Assert.Contains(bus.BusAccesses, access =>
			access.Request.Kind == AmigaBusAccessKind.CpuDataWrite &&
			access.Request.Target == AmigaBusAccessTarget.Cia &&
			access.Request.Size == AmigaBusAccessSize.Byte);
		Assert.Contains(bus.BusAccesses, access =>
			access.Request.Kind == AmigaBusAccessKind.CpuDataRead &&
			access.Request.Target == AmigaBusAccessTarget.Rom);
		Assert.Contains(bus.BusAccesses, access =>
			access.Request.Kind == AmigaBusAccessKind.HostTrap &&
			access.Request.Target == AmigaBusAccessTarget.HostTrap);
	}

	[Fact]
	public void PaulaDmaFetchesUseNamedBusRequestPath()
	{
		var bus = new AmigaBus();
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x81;
		bus.WriteWord(0x00DFF0A2, 0x1000, 0);
		bus.WriteWord(0x00DFF0A4, 0x0001, 0);
		bus.WriteWord(0x00DFF0A6, 0x0002, 0);
		bus.WriteWord(0x00DFF096, 0x8201, 0);

		bus.Paula.AdvanceTo(0);

		var dma = Assert.Single(bus.BusAccesses, access => access.Request.Kind == AmigaBusAccessKind.PaulaDma);
		Assert.Equal(AmigaBusRequester.Paula, dma.Request.Requester);
		Assert.Equal(AmigaBusAccessTarget.ChipRam, dma.Request.Target);
		Assert.Equal(AmigaBusAccessSize.Word, dma.Request.Size);
		Assert.Equal(0x1000u, dma.Request.Address);
		Assert.False(dma.Request.IsWrite);
	}

	private static void Write(byte[] memory, int address, params byte[] data)
	{
		Array.Copy(data, 0, memory, address, data.Length);
	}

	private sealed class FixedDelayArbiter : IAmigaBusArbiter
	{
		private readonly long _waitCycles;
		private readonly long _accessCycles;

		public FixedDelayArbiter(long waitCycles, long accessCycles)
		{
			_waitCycles = waitCycles;
			_accessCycles = accessCycles;
		}

		public AmigaBusAccessResult Arbitrate(AmigaBusAccessRequest request)
		{
			var granted = request.RequestedCycle + _waitCycles;
			return new AmigaBusAccessResult(request, granted, granted + _accessCycles);
		}
	}
}
