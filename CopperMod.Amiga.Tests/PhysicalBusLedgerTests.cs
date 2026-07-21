namespace CopperMod.Amiga.Tests;

/// <summary>
/// Physical OCS chip-bus contracts. These tests describe committed slot ownership;
/// they deliberately do not infer timing from pixels or presentation output.
/// </summary>
public sealed class PhysicalBusLedgerTests
{
	private const int LowResPlane1FetchSlot = 7;

	[Fact]
	public void OcsRefreshLedgerOwnsFourMandatorySlotsPerRasterline()
	{
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		var lineEnd = AmigaConstants.A500PalCpuCyclesPerRasterLine - AgnusChipSlotScheduler.SlotCycles;
		bus.AdvanceDmaTo(lineEnd);

		var ledger = PhysicalBusLedger.Capture(bus, 0, lineEnd);
		var refresh = ledger.Slots.Where(slot => slot.Owner == AgnusChipSlotOwner.Refresh).ToArray();
		var diagnostic = ledger.Format();

		Assert.Equal(AgnusHrmOcsSlotTable.RefreshSlotsPerLine, refresh.Length);
		Assert.True(
			refresh.All(slot => AgnusHrmOcsSlotTable.IsMandatoryRefreshSlot(slot.Cycle)),
			diagnostic);
		Assert.True(
			ledger.Slots
				.Where(slot => AgnusHrmOcsSlotTable.IsMandatoryRefreshSlot(slot.Cycle))
				.All(slot => slot.Owner == AgnusChipSlotOwner.Refresh),
			diagnostic);
	}

	[Fact]
	public void CpuRequestAtMandatoryRefreshMovesToNextPhysicalSlot()
	{
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		var requestCycle = (long)AgnusHrmOcsSlotTable.FirstRefreshHorizontal *
			AgnusChipSlotScheduler.SlotCycles;
		var cycle = requestCycle;
		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var access = Assert.Single(bus.BusAccesses, item =>
			item.Request.Requester == AmigaBusRequester.Cpu &&
			item.Request.Address == 0x00001000);
		var ledger = PhysicalBusLedger.Capture(bus, requestCycle, access.CompletedCycle);
		var diagnostic = ledger.Format();

		Assert.Equal(AgnusChipSlotOwner.Refresh, ledger.At(requestCycle).Owner);
		Assert.Equal(requestCycle + AgnusChipSlotScheduler.SlotCycles, access.GrantedCycle);
		Assert.Equal(AgnusChipSlotOwner.Cpu, ledger.At(access.GrantedCycle).Owner);
		Assert.True(access.CompletedCycle == cycle, diagnostic);
	}

	[Fact]
	public void CpuRequestAtLowresBitplaneFetchPreservesBitplaneThenUsesNextSlot()
	{
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		var fetchCycle = lineStart +
			((0x38 + LowResPlane1FetchSlot) * AgnusChipSlotScheduler.SlotCycles);
		ConfigureOneBitplane(bus);
		bus.AdvanceDmaTo(lineStart);

		var cycle = fetchCycle;
		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);
		var cpu = Assert.Single(bus.BusAccesses, item =>
			item.Request.Requester == AmigaBusRequester.Cpu &&
			item.Request.Address == 0x00001000);
		var bitplane = Assert.Single(bus.BusAccesses, item =>
			item.Request.Requester == AmigaBusRequester.Bitplane &&
			item.GrantedCycle == fetchCycle);
		var ledger = PhysicalBusLedger.Capture(bus, fetchCycle, cpu.CompletedCycle);
		var diagnostic = ledger.Format();

		Assert.Equal(AgnusChipSlotOwner.Bitplane, ledger.At(fetchCycle).Owner);
		Assert.Equal(fetchCycle, bitplane.GrantedCycle);
		Assert.Equal(fetchCycle + AgnusChipSlotScheduler.SlotCycles, cpu.GrantedCycle);
		Assert.Equal(AgnusChipSlotOwner.Cpu, ledger.At(cpu.GrantedCycle).Owner);
		Assert.True(cpu.CompletedCycle == cycle, diagnostic);
	}

	[Fact]
	public void CpuRequestAfterCommittedCopperTransferUsesNextPairedOpportunity()
	{
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		const long copperCycle = 20;
		var copper = bus.CausalBusExecutor.ReserveCopperDmaSlot(0x2400, copperCycle);
		var cycle = copperCycle;
		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);
		var cpu = Assert.Single(bus.BusAccesses, item =>
			item.Request.Requester == AmigaBusRequester.Cpu &&
			item.Request.Address == 0x00001000);
		var ledger = PhysicalBusLedger.Capture(bus, copperCycle, cpu.CompletedCycle);
		var diagnostic = ledger.Format();

		Assert.Equal(copperCycle, copper.GrantedCycle);
		Assert.Equal(AgnusChipSlotOwner.Copper, ledger.At(copperCycle).Owner);
		Assert.Equal(
			copper.GrantedCycle + (2 * AgnusChipSlotScheduler.SlotCycles),
			cpu.GrantedCycle);
		Assert.Equal(AgnusChipSlotOwner.Cpu, ledger.At(cpu.GrantedCycle).Owner);
		Assert.True(cpu.CompletedCycle == cycle, diagnostic);
	}

	[Fact]
	public void RepeatedCpuRequestsNeverConsumeMandatoryRefreshSlots()
	{
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		var cycle = 0L;
		for (var index = 0; index < 16; index++)
		{
			_ = bus.ReadWord(
				(uint)(0x00001000 + (index * 2)),
				ref cycle,
				AmigaBusAccessKind.CpuDataRead);
		}

		var cpu = bus.BusAccesses
			.Where(item => item.Request.Requester == AmigaBusRequester.Cpu)
			.ToArray();
		var ledger = PhysicalBusLedger.Capture(bus, 0, cpu[^1].CompletedCycle);
		var diagnostic = ledger.Format();

		Assert.Equal(16, cpu.Length);
		Assert.True(
			cpu.All(access => !AgnusHrmOcsSlotTable.IsMandatoryRefreshSlot(access.GrantedCycle)),
			diagnostic);
		Assert.True(
			ledger.Slots
				.Where(slot => AgnusHrmOcsSlotTable.IsMandatoryRefreshSlot(slot.Cycle))
				.All(slot => slot.Owner == AgnusChipSlotOwner.Refresh),
			diagnostic);
		Assert.True(
			cpu.Zip(cpu.Skip(1), (left, right) => right.GrantedCycle > left.GrantedCycle).All(value => value),
			diagnostic);
	}

	private static long OutputRowStartCycle(int row)
	{
		var line = (0x2C - AmigaConstants.PalLowResOverscanBorderY) + row;
		return (long)line * AmigaConstants.A500PalCpuCyclesPerRasterLine;
	}

	private static void ConfigureOneBitplane(AmigaBus bus)
	{
		bus.WriteWord(0x00DFF096, 0x8300);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x0038);
		bus.WriteWord(0x00DFF0E0, 0x0000);
		bus.WriteWord(0x00DFF0E2, 0x1000);
		bus.WriteWord(0x00DFF100, 0x1000);
		bus.EnableLiveAgnusDma();
	}

	private sealed class PhysicalBusLedger
	{
		private PhysicalBusLedger(IReadOnlyList<PhysicalBusLedgerSlot> slots)
		{
			Slots = slots;
		}

		public IReadOnlyList<PhysicalBusLedgerSlot> Slots { get; }

		public PhysicalBusLedgerSlot At(long cycle)
			=> Assert.Single(Slots, slot => slot.Cycle == cycle);

		public static PhysicalBusLedger Capture(AmigaBus bus, long firstCycle, long lastCycle)
		{
			firstCycle = AgnusChipSlotScheduler.AlignToSlot(Math.Max(0, firstCycle));
			lastCycle = AgnusChipSlotScheduler.AlignToSlot(Math.Max(firstCycle, lastCycle));
			var slots = new List<PhysicalBusLedgerSlot>();
			for (var cycle = firstCycle; cycle <= lastCycle; cycle += AgnusChipSlotScheduler.SlotCycles)
			{
				var owner = bus.TryGetCommittedAgnusSlotOwner(cycle, out var committedOwner)
					? committedOwner
					: AgnusChipSlotOwner.Free;
				var accesses = bus.BusAccesses.Where(access => access.GrantedCycle == cycle).ToArray();
				slots.Add(new PhysicalBusLedgerSlot(cycle, owner, accesses));
			}

			return new PhysicalBusLedger(slots);
		}

		public string Format()
			=> string.Join(" | ", Slots.Select(slot =>
				$"{slot.Cycle}/h{AgnusHrmOcsSlotTable.GetHorizontal(slot.Cycle):X2}:{slot.Owner}" +
				(slot.Accesses.Count == 0
					? string.Empty
					: $"[{string.Join(',', slot.Accesses.Select(access => $"{access.Request.Requester}:{access.Request.Kind}:r{access.RequestedCycle}:c{access.CompletedCycle}"))}]")));
	}

	private readonly record struct PhysicalBusLedgerSlot(
		long Cycle,
		AgnusChipSlotOwner Owner,
		IReadOnlyList<AmigaBusAccessResult> Accesses);
}
