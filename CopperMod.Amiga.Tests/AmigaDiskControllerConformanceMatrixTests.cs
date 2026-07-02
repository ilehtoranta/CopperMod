using CopperMod.Amiga;
using CopperDisk;
using System.IO.Compression;
using System.Reflection;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaDiskControllerConformanceMatrixTests
{
	private const uint DmaBase = 0x4000;
	private const ushort SyncWord = 0x4489;

	public static IEnumerable<object[]> PointerRows
	{
		get
		{
			yield return new object[] { new PointerRow(0x000000, 0x000000) };
			yield return new object[] { new PointerRow(0x004000, 0x004000) };
			yield return new object[] { new PointerRow(0x004001, 0x004000) };
			yield return new object[] { new PointerRow(0x01FFFE, 0x01FFFE) };
		}
	}

	public static IEnumerable<object[]> DmaGateRows
	{
		get
		{
			yield return new object[] { new DmaGateRow("no DMACON bits", 0x0000, false) };
			yield return new object[] { new DmaGateRow("master only", 0x8200, false) };
			yield return new object[] { new DmaGateRow("disk only", 0x8010, false) };
			yield return new object[] { new DmaGateRow("master and disk", 0x8210, true) };
		}
	}

	public static IEnumerable<object[]> DsklenRows
	{
		get
		{
			yield return new object[] { new DsklenRow("one word", 0x0001) };
			yield return new object[] { new DsklenRow("two words", 0x0002) };
			yield return new object[] { new DsklenRow("track-sized request", 0x1900) };
			yield return new object[] { new DsklenRow("maximum count field", 0x3FFF) };
		}
	}

	public static IEnumerable<object[]> DriveSelectRows =>
		Enumerable.Range(0, 4).Select(drive => new object[] { new DriveRow(drive) });

	public static IEnumerable<object[]> StepRows
	{
		get
		{
			yield return new object[] { new StepRow("inward", DirectionBitSet: false, ExpectedDelta: 1) };
			yield return new object[] { new StepRow("outward", DirectionBitSet: true, ExpectedDelta: -1) };
		}
	}

	public static IEnumerable<object[]> WordSyncRows
	{
		get
		{
			yield return new object[] { new WordSyncRow("default sync", 0x4489, 0xABCD) };
			yield return new object[] { new WordSyncRow("custom sync", 0xA1A1, 0x1357) };
		}
	}

	public static IEnumerable<object[]> RawBitOffsetRows =>
		new[] { 0, 1, 3, 7, 15 }.Select(shift => new object[] { new RawBitOffsetRow(shift) });

	private static IEnumerable<DiskConformanceRow> MatrixRows
	{
		get
		{
			foreach (var row in PointerRows)
			{
				yield return DiskConformanceRow.Executable("registers", $"DSKPTH/PTL pointer 0x{((PointerRow)row[0]).Address:X6}");
			}

			foreach (var row in DmaGateRows)
			{
				yield return DiskConformanceRow.Executable("dma-control", ((DmaGateRow)row[0]).Name);
			}

			foreach (var row in DsklenRows)
			{
				yield return DiskConformanceRow.Executable("dsklen", ((DsklenRow)row[0]).Name);
			}

			foreach (var row in DriveSelectRows)
			{
				yield return DiskConformanceRow.Executable("cia-drive-lines", $"drive {((DriveRow)row[0]).Drive}");
			}

			foreach (var row in StepRows)
			{
				yield return DiskConformanceRow.Executable("cia-step-lines", ((StepRow)row[0]).Name);
			}

			foreach (var row in WordSyncRows)
			{
				yield return DiskConformanceRow.Executable("wordsync", ((WordSyncRow)row[0]).Name);
			}

			foreach (var row in RawBitOffsetRows)
			{
				yield return DiskConformanceRow.Executable("raw-bitstream", $"bit offset {((RawBitOffsetRow)row[0]).ShiftBits}");
			}

			yield return DiskConformanceRow.Executable("registers", "DSKSYNC default and writable value");
			yield return DiskConformanceRow.Executable("dsklen", "two matching DMAEN writes required");
			yield return DiskConformanceRow.Executable("dsklen", "mismatched second DMAEN write rearms but does not start");
			yield return DiskConformanceRow.Executable("write-dma", "WRITE bit starts write DMA and reports DISKWRITE");
			yield return DiskConformanceRow.Executable("dsklen", "zero length cancels pending DMA");
			yield return DiskConformanceRow.Executable("dma-control", "DMA stop cancels active transfer");
			yield return DiskConformanceRow.Executable("dma-control", "DSKPT advances and DSKLEN counts down at completion");
			yield return DiskConformanceRow.Executable("dskbytr", "byte ready and clear on read");
			yield return DiskConformanceRow.Executable("dskbytr", "DMAON status during active DMA");
			yield return DiskConformanceRow.Executable("dskbytr", "WORDEQUAL status and DSKSYNC interrupt");
			yield return DiskConformanceRow.Executable("interrupts", "DSKBLK interrupt on DMA completion");
			yield return DiskConformanceRow.Executable("interrupts", "index pulse reaches CIA-B flag");
			yield return DiskConformanceRow.Executable("cia-drive-lines", "CIA-A reports change, track zero and ready");
			yield return DiskConformanceRow.Executable("cia-drive-lines", "unconnected selected drive is ignored");
			yield return DiskConformanceRow.Executable("media-routing", "selected external drive supplies DMA data");
			yield return DiskConformanceRow.Executable("media-routing", "selected head/cylinder supplies DMA data");
			yield return DiskConformanceRow.Executable("raw-bitstream", "DMA without WORDSYNC starts at current stream position");
			yield return DiskConformanceRow.Executable("registers", "DSKDATR/DSKDAT data register latch");
			yield return DiskConformanceRow.Executable("dma-control", "disk DMA latch is consumed after granted read word");
			yield return DiskConformanceRow.Executable("wordsync", "resynchronize active read DMA on every DSKSYNC match");
			yield return DiskConformanceRow.Executable("cia-drive-lines", "write-protect sensor");
			yield return DiskConformanceRow.Executable("cia-drive-lines", "500ms motor spin-up delay before DSKRDY");
			yield return DiskConformanceRow.Executable("dsklen", "pending read DMA starts when motor becomes ready");
			yield return DiskConformanceRow.Executable("diagnostics", "bounded disk DMA trace records start/completion/cancel/sync-miss state");
			yield return DiskConformanceRow.Executable("write-dma", "ADF write DMA updates writable in-memory media");
			yield return DiskConformanceRow.Executable("write-dma", "write-protect and read-only media block media mutation");
			yield return DiskConformanceRow.Executable("write-dma", "disk DMA latch is consumed after granted write word");
			yield return DiskConformanceRow.Executable("write-dma", "WORDSYNC gates write DMA start");
			yield return DiskConformanceRow.Executable("wordsync", "MSBSYNC/GCR byte sync mode");
			yield return DiskConformanceRow.Executable("timing", "ADKCON FAST two-microsecond bit-cell mode");
			yield return DiskConformanceRow.Executable("write-dma", "MFM/GCR precompensation and write splice edge cases");
			yield return DiskConformanceRow.Pending("hardware-bugs", "last three write bits and possible missing final read word", "Requires a Paula internal disk-buffer pipeline model; byte-backed tracks preserve digital bits but do not yet model the documented lost-final-word quirk.");
		}
	}

	[Fact]
	public void DiskControllerConformanceMatrixCoversHrmFeatureGroups()
	{
		// Reference: CopperMod.Amiga/References/Commodore_Amiga_Hardware_Reference_Manual_2nd.pdf,
		// Chapter 8 "Interface Hardware", "Floppy Disk Controller", Tables 8-5 through 8-8.
		var rows = MatrixRows.ToArray();
		var groups = rows.Select(row => row.Group).Distinct().OrderBy(group => group).ToArray();

		Assert.Equal(
			new[]
			{
				"cia-drive-lines",
				"cia-step-lines",
				"diagnostics",
				"dma-control",
				"dskbytr",
				"dsklen",
				"hardware-bugs",
				"interrupts",
				"media-routing",
				"raw-bitstream",
				"registers",
				"timing",
				"wordsync",
				"write-dma",
			},
			groups);
		Assert.Equal(4, PointerRows.Count());
		Assert.Equal(4, DmaGateRows.Count());
		Assert.Equal(4, DsklenRows.Count());
		Assert.Equal(4, DriveSelectRows.Count());
		Assert.Equal(2, StepRows.Count());
		Assert.Equal(2, WordSyncRows.Count());
		Assert.Equal(5, RawBitOffsetRows.Count());
		Assert.True(rows.Count(row => row.Status == DiskRowStatus.Executable) >= 35);
	}

	[Fact]
	public void DiskControllerPendingRowsAreDocumented()
	{
		var pendingRows = MatrixRows.Where(row => row.Status == DiskRowStatus.Pending).ToArray();

		var pending = Assert.Single(pendingRows);
		Assert.All(pendingRows, row => Assert.False(string.IsNullOrWhiteSpace(row.Reason)));
		Assert.Equal("hardware-bugs", pending.Group);
	}

	[Fact]
	public void DiskRegistersResetToHrmDefaults()
	{
		var bus = CreateDiskComponentBus();
		var snapshot = bus.Disk.CaptureSnapshot();

		Assert.Equal(0u, snapshot.DiskPointer);
		Assert.Equal(0, snapshot.Dsklen);
		Assert.Equal(SyncWord, snapshot.Dsksync);
		Assert.Equal(0, snapshot.TransferCount);
	}

	[Theory]
	[MemberData(nameof(PointerRows))]
	public void DiskPointerRegistersMaskWordAddressAndStayInChipDmaSpace(object rowObject)
	{
		var row = Assert.IsType<PointerRow>(rowObject);
		var bus = CreateDiskComponentBus();

		SetDiskPointer(bus, row.Address);

		Assert.Equal(row.Expected, bus.Disk.CaptureSnapshot().DiskPointer);
		Assert.Equal((ushort)(row.Expected >> 16), bus.ReadWord(0x00DFF020));
		Assert.Equal((ushort)(row.Expected & 0xFFFE), bus.ReadWord(0x00DFF022));
	}

	[Fact]
	public void DskSyncDefaultsToMagicMfmSyncAndCanBeChanged()
	{
		var bus = CreateDiskComponentBus();

		Assert.Equal(SyncWord, bus.ReadWord(0x00DFF07E));

		bus.WriteWord(0x00DFF07E, 0xA1A1);

		Assert.Equal(0xA1A1, bus.Disk.CaptureSnapshot().Dsksync);
		Assert.Equal(0xA1A1, bus.ReadWord(0x00DFF07E));
	}

	[Fact]
	public void DiskDmaRequiresTwoMatchingDsklenDmaenWrites()
	{
		var bus = CreateBusWithTrack(0x1234, 0x5678);
		var cycle = PrepareDiskDma(bus);
		SetDiskPointer(bus, DmaBase, cycle);

		bus.WriteWord(0x00DFF024, 0x8002, cycle);
		Assert.Equal(0, bus.Disk.CaptureSnapshot().TransferCount);

		bus.WriteWord(0x00DFF024, 0x8002, cycle);

		var snapshot = bus.Disk.CaptureSnapshot();
		Assert.Equal(1, snapshot.TransferCount);
		Assert.Equal(2, snapshot.LastTransferWords);
		Assert.Equal(DmaBase, snapshot.LastTransferAddress);
	}

	[Fact]
	public void MismatchedSecondDsklenWriteRearmsWithoutStartingDma()
	{
		var bus = CreateBusWithTrack(0x1234, 0x5678);
		var cycle = PrepareDiskDma(bus);
		SetDiskPointer(bus, DmaBase, cycle);

		bus.WriteWord(0x00DFF024, 0x8001, cycle);
		bus.WriteWord(0x00DFF024, 0x8002, cycle);
		Assert.Equal(0, bus.Disk.CaptureSnapshot().TransferCount);

		bus.WriteWord(0x00DFF024, 0x8002, cycle);

		Assert.Equal(1, bus.Disk.CaptureSnapshot().TransferCount);
	}

	[Theory]
	[MemberData(nameof(DsklenRows))]
	public void DsklenLengthFieldSelectsRequestedWordCount(object rowObject)
	{
		var row = Assert.IsType<DsklenRow>(rowObject);
		var bus = CreateBusWithTrack(0x1234, 0x5678);
		var cycle = PrepareDiskDma(bus);
		SetDiskPointer(bus, DmaBase, cycle);

		WriteDsklenStartSequence(bus, row.Words, cycle);

		Assert.Equal(row.Words, bus.Disk.CaptureSnapshot().LastTransferWords);
		Assert.Equal((ushort)(0x8000 | row.Words), bus.Disk.CaptureSnapshot().Dsklen);
	}

	[Fact]
	public void DsklenWriteBitStartsWriteDmaAndReportsDiskWriteStatus()
	{
		var bus = CreateBusWithTrack(0x1234);
		var cycle = PrepareDiskDma(bus);
		SetDiskPointer(bus, DmaBase, cycle);
		WriteChipWord(bus, DmaBase, 0xBEEF);

		bus.WriteWord(0x00DFF024, 0xC001, cycle);
		bus.WriteWord(0x00DFF024, 0xC001, cycle);

		Assert.Equal(1, bus.Disk.CaptureSnapshot().TransferCount);
		CompleteDiskDma(bus);
		Assert.Equal(0xBEEF, bus.ReadWord(0x00DFF008));
		Assert.NotEqual(0, bus.ReadWord(0x00DFF01A) & 0x2000);
	}

	[Fact]
	public void DsklenZeroLengthCancelsPendingDma()
	{
		var bus = CreateBusWithTrack(0x1234);
		var cycle = PrepareDiskDma(bus);
		SetDiskPointer(bus, DmaBase, cycle);

		bus.WriteWord(0x00DFF024, 0x8001, cycle);
		bus.WriteWord(0x00DFF024, 0x8000, cycle);
		bus.WriteWord(0x00DFF024, 0x8001, cycle);

		Assert.Equal(0, bus.Disk.CaptureSnapshot().TransferCount);
	}

	[Theory]
	[MemberData(nameof(DmaGateRows))]
	public void DiskDmaRequiresDmaconMasterAndDiskEnableBits(object rowObject)
	{
		var row = Assert.IsType<DmaGateRow>(rowObject);
		var bus = CreateBusWithTrack(0x1234);
		SelectDriveAndStartMotor(bus, drive: 0);
		var cycle = AdvanceToMotorReady(bus);
		bus.WriteWord(0x00DFF096, row.Dmacon, cycle);
		bus.Paula.AdvanceTo(cycle);
		SetDiskPointer(bus, DmaBase, cycle);

		WriteDsklenStartSequence(bus, words: 1, cycle);
		CompleteDiskDma(bus);

		Assert.Equal(row.ExpectedStarts ? 1 : 0, bus.Disk.CaptureSnapshot().TransferCount);
		Assert.Equal(row.ExpectedStarts ? 0x1234 : 0, ReadChipWord(bus, DmaBase));
	}

	[Fact]
	public void StoppingDsklenCancelsActiveTransferBeforeCompletion()
	{
		var bus = CreateBusWithTrack(0x1111, 0x2222, 0x3333, 0x4444);
		var cycle = PrepareDiskDma(bus);
		SetDiskPointer(bus, DmaBase, cycle);

		WriteDsklenStartSequence(bus, words: 4, cycle);
		bus.AdvanceDmaTo(bus.Disk.CaptureSnapshot().ActiveDmaCompletionCycle - 1);
		bus.WriteWord(0x00DFF024, 0x0000, bus.Disk.CaptureSnapshot().ActiveDmaCompletionCycle - 1);

		Assert.False(bus.Disk.CaptureSnapshot().ActiveDma);
		Assert.Equal(0, bus.ReadWord(0x00DFF01E) & 0x0002);
	}

	[Fact]
	public void DiskPointerAdvancesAndDsklenCountsDownWhenDmaCompletes()
	{
		var bus = CreateBusWithTrack(0x1111, 0x2222, 0x3333);

		StartDiskDma(bus, DmaBase, words: 3);
		CompleteDiskDma(bus);

		var snapshot = bus.Disk.CaptureSnapshot();
		Assert.Equal(DmaBase + 6, snapshot.DiskPointer);
		Assert.Equal(0x8000, snapshot.Dsklen);
		Assert.Equal(0x1111, ReadChipWord(bus, DmaBase));
		Assert.Equal(0x3333, ReadChipWord(bus, DmaBase + 4));
	}

	[Fact]
	public void DiskReadDmaCompletionWaitsForGrantedDiskSlot()
	{
		var bus = CreateBusWithTrack(0x1111);

		StartDiskDma(bus, DmaBase, words: 1);

		var completionCycle = bus.Disk.CaptureSnapshot().ActiveDmaCompletionCycle;
		var grantCycle = completionCycle - AgnusChipSlotScheduler.SlotCycles;
		Assert.Equal(AgnusChipSlotOwner.Disk, AgnusHrmOcsSlotTable.GetFixedOwner(AgnusHrmOcsSlotTable.GetHorizontal(grantCycle)));

		bus.AdvanceDmaTo(Math.Max(0, grantCycle - 1));
		Assert.True(bus.Disk.CaptureSnapshot().ActiveDma);
		Assert.Equal(0, ReadChipWord(bus, DmaBase));

		bus.AdvanceDmaTo(grantCycle);
		Assert.True(bus.Disk.CaptureSnapshot().ActiveDma);
		Assert.Equal(0x1111, ReadChipWord(bus, DmaBase));

		bus.AdvanceDmaTo(completionCycle);
		bus.Paula.AdvanceTo(completionCycle);

		Assert.False(bus.Disk.CaptureSnapshot().ActiveDma);
		Assert.NotEqual(0, bus.ReadWord(0x00DFF01E) & 0x0002);
	}

	[Fact]
	public void DiskReadDmaRequestIsServedAtExactFixedDiskSlot()
	{
		var bus = CreateBusWithTrack(0x1111);

		StartDiskDma(bus, DmaBase, words: 1);
		CompleteDiskDma(bus);

		var dma = Assert.Single(CaptureDiskDmaAccesses(bus));
		Assert.Equal(dma.GrantedCycle, dma.RequestedCycle);
		Assert.Equal(AgnusChipSlotOwner.Disk, AgnusHrmOcsSlotTable.GetFixedOwner(AgnusHrmOcsSlotTable.GetHorizontal(dma.GrantedCycle)));
		Assert.Equal(0x1111, ReadChipWord(bus, DmaBase));
	}

	[Fact]
	public void DiskReadDmaRequestCreatedAfterLastDiskSlotWaitsUntilNextLine()
	{
		var trackBytes = Enumerable.Repeat((byte)0xFF, 32768).ToArray();
		var bus = CreateBusWithTrackBytes(trackBytes);
		var readyCycle = PrepareDiskDma(bus);
		var lineStart = ((readyCycle / AmigaConstants.A500PalCpuCyclesPerRasterLine) + 2) *
			AmigaConstants.A500PalCpuCyclesPerRasterLine;
		var startCycle = lineStart + (0x0E * AmigaConstants.A500PalCpuCyclesPerColorClock);
		var expectedSlot = AgnusHrmOcsSlotTable.FindNextFixedDmaSlot(startCycle, AgnusChipSlotOwner.Disk);

		StartDiskDmaWithoutSelecting(bus, DmaBase, words: 1, startCycle);
		bus.AdvanceDmaTo(expectedSlot - 1);

		Assert.Empty(CaptureDiskDmaAccesses(bus));
		Assert.Equal(0, ReadChipWord(bus, DmaBase));

		bus.AdvanceDmaTo(expectedSlot);

		var dma = Assert.Single(CaptureDiskDmaAccesses(bus));
		Assert.Equal(expectedSlot, dma.RequestedCycle);
		Assert.Equal(expectedSlot, dma.GrantedCycle);
		Assert.Equal(0xFFFF, ReadChipWord(bus, DmaBase));
	}

	[Fact]
	public void BlockedDiskSlotLeavesReadDmaRequestPending()
	{
		var bus = CreateBusWithTrack(0x1111);

		StartDiskDma(bus, DmaBase, words: 1);
		var completionCycle = bus.Disk.CaptureSnapshot().ActiveDmaCompletionCycle;
		var firstSlot = completionCycle - AgnusChipSlotScheduler.SlotCycles;
		_ = bus.TryReserveDiskDmaWordThrough(0x2000, isWrite: false, firstSlot, firstSlot, out _);

		bus.AdvanceDmaTo(firstSlot);
		var blockedSnapshot = bus.Disk.CaptureSnapshot();

		Assert.True(blockedSnapshot.ActiveDma);
		Assert.Equal(DmaBase, blockedSnapshot.DiskPointer);
		Assert.Equal(0x8001, blockedSnapshot.Dsklen);
		Assert.Equal(0, ReadChipWord(bus, DmaBase));

		var nextSlot = AgnusHrmOcsSlotTable.FindNextFixedDmaSlot(firstSlot + AgnusChipSlotScheduler.SlotCycles, AgnusChipSlotOwner.Disk);
		bus.AdvanceDmaTo(nextSlot);

		var diskDma = CaptureDiskDmaAccesses(bus)
			.Where(access => access.Request.Address == DmaBase)
			.Single();
		Assert.Equal(nextSlot, diskDma.RequestedCycle);
		Assert.Equal(nextSlot, diskDma.GrantedCycle);
		Assert.Equal(0x1111, ReadChipWord(bus, DmaBase));
	}

	[Fact]
	public void DiskWriteDmaReadsChipWordAtExactFixedDiskSlot()
	{
		var disk = new WritableTrackDisk(AmigaEncodedTrack.FromBytes(WordsToBytes(0xAAAA)));
		var bus = CreateDiskComponentBus();
		bus.Disk.Drive0.Insert(disk, writeProtected: false);
		WriteChipWord(bus, DmaBase, 0x5AA5);

		var cycle = PrepareDiskDma(bus);
		SetDiskPointer(bus, DmaBase, cycle);
		WriteDsklenStartSequence(bus, words: 1, cycle, writeMode: true);
		CompleteDiskDma(bus);

		var dma = Assert.Single(CaptureDiskDmaAccesses(bus));
		Assert.True(dma.Request.IsWrite);
		Assert.Equal(dma.GrantedCycle, dma.RequestedCycle);
		Assert.Equal(AgnusChipSlotOwner.Disk, AgnusHrmOcsSlotTable.GetFixedOwner(AgnusHrmOcsSlotTable.GetHorizontal(dma.GrantedCycle)));
		Assert.Equal(0x5AA5, disk.Track.ReadUInt16AtBit(0));
	}

	[Fact]
	public void DiskReadDmaReplaySerializesBusRequestsAcrossSplitAdvances()
	{
		var bus = CreateBusWithTrack(0x1111, 0x2222, 0x3333, 0x4444);

		StartDiskDma(bus, DmaBase, words: 4);
		var completionCycle = bus.Disk.CaptureSnapshot().ActiveDmaCompletionCycle;
		for (var cycle = 0L; cycle < completionCycle; cycle += AgnusChipSlotScheduler.SlotCycles)
		{
			bus.AdvanceDmaTo(cycle);
		}

		bus.AdvanceDmaTo(completionCycle);
		bus.Paula.AdvanceTo(completionCycle);

		var diskAccesses = bus.BusAccesses
			.Where(access => access.Request.Kind == AmigaBusAccessKind.DiskDma)
			.ToArray();
		Assert.Equal(4, diskAccesses.Length);
		for (var i = 1; i < diskAccesses.Length; i++)
		{
			Assert.True(
				diskAccesses[i].RequestedCycle >= diskAccesses[i - 1].CompletedCycle,
				$"Disk DMA request {i} at {diskAccesses[i].RequestedCycle} overlapped prior completion {diskAccesses[i - 1].CompletedCycle}.");
		}

		var agnus = bus.Agnus.CaptureSnapshot();
		Assert.Equal(0, agnus.DiskDeniedFixedSlotCount);
		Assert.Equal(0, agnus.DiskDeniedFixedSlotBlockerCount);
		Assert.Equal(0x1111, ReadChipWord(bus, DmaBase));
		Assert.Equal(0x4444, ReadChipWord(bus, DmaBase + 6));
	}

	[Fact]
	public void DskbytrReportsByteReadyDataAndClearsReadyOnRead()
	{
		var trackBytes = new byte[] { 0x12, 0x34, 0x56 };
		var bus = CreateBusWithTrackBytes(trackBytes);
		SelectDriveAndStartMotor(bus, drive: 0);
		var readyCycle = AdvanceToMotorReady(bus);

		bus.AdvanceDmaTo(readyCycle + DiskByteCycleCount(trackBytes.Length, 1));

		var first = bus.ReadWord(0x00DFF01A);
		var second = bus.ReadWord(0x00DFF01A);
		Assert.NotEqual(0, first & 0x8000);
		Assert.Equal(0x12, first & 0x00FF);
		Assert.Equal(0, second & 0x8000);
		Assert.Equal(0x12, second & 0x00FF);
	}

	[Fact]
	public void DskbytrReportsDmaOnWhileReadDmaIsActive()
	{
		var bus = CreateBusWithTrack(0x1111, 0x2222, 0x3333, 0x4444);

		StartDiskDma(bus, DmaBase, words: 4);

		Assert.NotEqual(0, bus.ReadWord(0x00DFF01A) & 0x4000);
		CompleteDiskDma(bus);
		Assert.Equal(0, bus.ReadWord(0x00DFF01A) & 0x4000);
	}

	[Theory]
	[MemberData(nameof(WordSyncRows))]
	public void WordSyncDmaStartsWithWordAfterMatchingDskSync(object rowObject)
	{
		var row = Assert.IsType<WordSyncRow>(rowObject);
		var bus = CreateBusWithTrack(0x9999, row.Sync, row.Payload, 0x2468);
		bus.WriteWord(0x00DFF07E, row.Sync);
		bus.WriteWord(0x00DFF09E, 0x8400);
		bus.Paula.AdvanceTo(0);

		StartDiskDma(bus, DmaBase, words: 2);
		CompleteDiskDma(bus);

		Assert.Equal(row.Payload, ReadChipWord(bus, DmaBase));
		Assert.Equal(0x2468, ReadChipWord(bus, DmaBase + 2));
	}

	[Fact]
	public void MsbSyncDmaStartsAfterHighByteOfDskSync()
	{
		var bus = CreateBusWithTrackBytes(0x99, 0x44, 0xAB, 0xCD, 0x24, 0x68);
		bus.WriteWord(0x00DFF07E, SyncWord);
		bus.WriteWord(0x00DFF09E, 0x8200);
		bus.Paula.AdvanceTo(0);

		StartDiskDma(bus, DmaBase, words: 2);
		CompleteDiskDma(bus);

		Assert.Equal(0xABCD, ReadChipWord(bus, DmaBase));
		Assert.Equal(0x2468, ReadChipWord(bus, DmaBase + 2));
		var started = Assert.Single(bus.Disk.CaptureDmaTrace().Where(entry => entry.Kind == AmigaDiskDmaTraceKind.Started));
		Assert.Equal(AmigaDiskSyncMode.Byte, started.SyncMode);
		Assert.False(started.WordSyncEnabled);
		Assert.Equal(16, started.SourceBit);
		Assert.Equal(16, started.SyncWaitBits);
	}

	[Fact]
	public void MsbSyncWordEqualUsesHighByteOfDskSync()
	{
		var trackBytes = new byte[] { 0x12, 0x44, 0x00, 0x99 };
		var bus = CreateBusWithTrackBytes(trackBytes);
		SelectDriveAndStartMotor(bus, drive: 0);
		var readyCycle = AdvanceToMotorReady(bus);
		bus.WriteWord(0x00DFF07E, SyncWord, readyCycle);
		bus.WriteWord(0x00DFF09E, 0x8200, readyCycle);
		bus.Paula.AdvanceTo(readyCycle);

		bus.AdvanceDmaTo(readyCycle + DiskByteCycleCount(trackBytes.Length, 2));

		Assert.NotEqual(0, bus.ReadWord(0x00DFF01E) & 0x1000);
		Assert.NotEqual(0, bus.ReadWord(0x00DFF01A) & 0x1000);
	}

	[Fact]
	public void DmaWithoutWordSyncStartsAtCurrentRawStreamPosition()
	{
		var bus = CreateBusWithTrack(0x1111, 0x2222, 0x3333);

		StartDiskDma(bus, DmaBase, words: 2);
		CompleteDiskDma(bus);

		Assert.Equal(0x1111, ReadChipWord(bus, DmaBase));
		Assert.Equal(0x2222, ReadChipWord(bus, DmaBase + 2));
	}

	[Fact]
	public void NonWordSyncDmaArmedMidWordDrainsRecoveredDiskWordPhase()
	{
		var bus = CreateBusWithTrackBytes(0x11, 0x22, 0x33, 0x44);
		var cycle = PrepareDiskDma(bus);
		var midWordCycle = cycle + DiskByteCycleCount(trackByteLength: 4, byteCount: 1);
		bus.AdvanceDmaTo(midWordCycle);
		bus.Paula.AdvanceTo(midWordCycle);

		StartDiskDmaWithoutSelecting(bus, DmaBase, words: 1, midWordCycle);
		CompleteDiskDma(bus);

		Assert.Equal(0x1122, ReadChipWord(bus, DmaBase));
		var started = Assert.Single(bus.Disk.CaptureDmaTrace().Where(entry => entry.Kind == AmigaDiskDmaTraceKind.Started));
		Assert.Equal(0, started.SourceBit);
	}

	[Theory]
	[MemberData(nameof(RawBitOffsetRows))]
	public void WordSyncedDmaReadsNonByteAlignedRawBitstreams(object rowObject)
	{
		var row = Assert.IsType<RawBitOffsetRow>(rowObject);
		var track = WordsToBytes(SyncWord, 0xABCD, 0x1357);
		var tracks = CreateEncodedTrackSet();
		tracks[0] = ShiftTrackBits(track, row.ShiftBits);
		var bus = CreateDiskComponentBus();
		bus.Disk.Drive0.Insert(AmigaDiskImage.FromEncodedTracks(tracks));
		bus.WriteWord(0x00DFF09E, 0x8400);
		bus.Paula.AdvanceTo(0);

		StartDiskDma(bus, DmaBase, words: 2);
		CompleteDiskDma(bus);

		Assert.Equal(0xABCD, ReadChipWord(bus, DmaBase));
		Assert.Equal(0x1357, ReadChipWord(bus, DmaBase + 2));
	}

	[Fact]
	public void DskSyncInterruptAndWordEqualStatusAreIndependentOfWordSyncEnable()
	{
		var trackBytes = WordsToBytes(SyncWord, 0xABCD);
		var bus = CreateBusWithTrackBytes(trackBytes);
		SelectDriveAndStartMotor(bus, drive: 0);
		var readyCycle = AdvanceToMotorReady(bus);
		bus.WriteWord(0x00DFF07E, SyncWord, readyCycle);

		bus.AdvanceDmaTo(readyCycle + DiskByteCycleCount(trackBytes.Length, 2));

		Assert.NotEqual(0, bus.ReadWord(0x00DFF01E) & 0x1000);
		Assert.NotEqual(0, bus.ReadWord(0x00DFF01A) & 0x1000);
	}

	[Fact]
	public void DiskBlockInterruptIsRaisedWhenReadDmaCompletes()
	{
		var bus = CreateBusWithTrack(0x1111, 0x2222);

		StartDiskDma(bus, DmaBase, words: 2);
		var completionCycle = bus.Disk.CaptureSnapshot().ActiveDmaCompletionCycle;
		bus.Paula.AdvanceTo(completionCycle - 1);
		Assert.Equal(0, bus.ReadWord(0x00DFF01E) & 0x0002);

		CompleteDiskDma(bus);

		Assert.NotEqual(0, bus.ReadWord(0x00DFF01E) & 0x0002);
	}

	[Theory]
	[MemberData(nameof(DriveSelectRows))]
	public void CiaBSelectBitsChooseOneOfFourConnectedDrives(object rowObject)
	{
		var row = Assert.IsType<DriveRow>(rowObject);
		var bus = CreateDiskComponentBus(floppyDriveCount: 4);
		for (var drive = 0; drive < 4; drive++)
		{
			GetDrive(bus, drive).Insert(AmigaDiskImage.FromEncodedTracks(CreateTrackSetWithWords((ushort)(0x1000 + drive))));
		}

		SelectDriveAndStartMotor(bus, row.Drive);

		var snapshot = bus.Disk.CaptureSnapshot();
		Assert.Equal(row.Drive, snapshot.SelectedDrive);
		for (var drive = 0; drive < 4; drive++)
		{
			Assert.Equal(drive == row.Drive, GetDrive(bus, drive).Selected);
		}
	}

	[Theory]
	[MemberData(nameof(StepRows))]
	public void CiaBStepPulseMovesSelectedDriveInRequestedDirection(object rowObject)
	{
		var row = Assert.IsType<StepRow>(rowObject);
		var bus = CreateDiskComponentBus();
		bus.Disk.Drive0.Insert(AmigaDiskImage.FromEncodedTracks(CreateTrackSetWithWords(0x1234)));
		bus.Disk.Drive0.Step(2);
		SelectDriveAndStartMotor(bus, drive: 0);
		var baseValue = (byte)(0x77 | (row.DirectionBitSet ? 0x02 : 0x00));
		if (!row.DirectionBitSet)
		{
			baseValue = (byte)(baseValue & ~0x02);
		}

		bus.WriteByte(0x00BFD100, baseValue, 0);
		bus.WriteByte(0x00BFD100, (byte)(baseValue & ~0x01), 0);

		Assert.Equal(2 + row.ExpectedDelta, bus.Disk.CaptureSnapshot().Cylinder);
	}

	[Fact]
	public void CiaBHeadSelectChoosesEncodedTrackHead()
	{
		var tracks = CreateEncodedTrackSet();
		tracks[0] = AmigaEncodedTrack.FromBytes(WordsToBytes(0x1111));
		tracks[1] = AmigaEncodedTrack.FromBytes(WordsToBytes(0x2222));
		var bus = CreateDiskComponentBus();
		bus.Disk.Drive0.Insert(AmigaDiskImage.FromEncodedTracks(tracks));

		SelectDriveAndStartMotor(bus, drive: 0, head: 1);
		var readyCycle = AdvanceToMotorReady(bus);
		bus.WriteWord(0x00DFF096, 0x8210, readyCycle);
		bus.Paula.AdvanceTo(readyCycle);
		StartDiskDmaWithoutSelecting(bus, DmaBase, words: 1, readyCycle);
		CompleteDiskDma(bus);

		Assert.Equal(1, bus.Disk.CaptureSnapshot().LastTransferHead);
		Assert.Equal(0x2222, ReadChipWord(bus, DmaBase));
	}

	[Fact]
	public void SelectedCylinderChoosesEncodedTrackCylinder()
	{
		var tracks = CreateEncodedTrackSet();
		tracks[0] = AmigaEncodedTrack.FromBytes(WordsToBytes(0x1111));
		tracks[2] = AmigaEncodedTrack.FromBytes(WordsToBytes(0x3333));
		var bus = CreateDiskComponentBus();
		bus.Disk.Drive0.Insert(AmigaDiskImage.FromEncodedTracks(tracks));
		bus.Disk.Drive0.Step(1);

		StartDiskDma(bus, DmaBase, words: 1);
		CompleteDiskDma(bus);

		Assert.Equal(1, bus.Disk.CaptureSnapshot().LastTransferCylinder);
		Assert.Equal(0x3333, ReadChipWord(bus, DmaBase));
	}

	[Theory]
	[MemberData(nameof(DriveSelectRows))]
	public void DiskDmaReadsFromSelectedExternalDrive(object rowObject)
	{
		var row = Assert.IsType<DriveRow>(rowObject);
		var bus = CreateDiskComponentBus(floppyDriveCount: 4);
		for (var drive = 0; drive < 4; drive++)
		{
			GetDrive(bus, drive).Insert(AmigaDiskImage.FromEncodedTracks(CreateTrackSetWithWords((ushort)(0xA000 + drive))));
		}

		StartDiskDma(bus, DmaBase, words: 1, drive: row.Drive);
		CompleteDiskDma(bus);

		Assert.Equal(row.Drive, bus.Disk.CaptureSnapshot().LastTransferDrive);
		Assert.Equal((ushort)(0xA000 + row.Drive), ReadChipWord(bus, DmaBase));
	}

	[Fact]
	public void CiaAReportsDiskChangeTrackZeroAndReadyLinesAsActiveLow()
	{
		var bus = CreateDiskComponentBus();
		bus.Disk.Drive0.Insert(AmigaDiskImage.FromEncodedTracks(CreateTrackSetWithWords(0x1234)), markChanged: true);

		var changedAtTrackZero = bus.ReadByte(0x00BFE001);
		Assert.Equal(0, changedAtTrackZero & 0x04);
		Assert.Equal(0, changedAtTrackZero & 0x10);
		Assert.NotEqual(0, changedAtTrackZero & 0x20);

		SelectDriveAndStartMotor(bus, drive: 0);
		var notReady = bus.ReadByte(0x00BFE001);
		Assert.NotEqual(0, notReady & 0x20);

		var readyCycle = AdvanceToMotorReady(bus);
		var readCycle = readyCycle;
		var ready = bus.ReadByte(0x00BFE001, ref readCycle, AmigaBusAccessKind.CpuDataRead);
		Assert.Equal(0, ready & 0x20);

		bus.Disk.Drive0.Step(1);
		var afterStep = bus.ReadByte(0x00BFE001);
		Assert.NotEqual(0, afterStep & 0x04);
		Assert.NotEqual(0, afterStep & 0x10);
	}

	[Fact]
	public void CiaAReadyWaitsForMotorSpinUpDelay()
	{
		var bus = CreateBusWithTrack(0x1234);
		SelectDriveAndStartMotor(bus, drive: 0);

		var beforeReadyCycle = MotorReadyCycle() - AmigaConstants.A500PalCpuCyclesPerCiaTick - 1;
		var cycle = beforeReadyCycle;
		var beforeReady = bus.ReadByte(0x00BFE001, ref cycle, AmigaBusAccessKind.CpuDataRead);
		Assert.NotEqual(0, beforeReady & 0x20);

		cycle = MotorReadyCycle();
		var ready = bus.ReadByte(0x00BFE001, ref cycle, AmigaBusAccessKind.CpuDataRead);
		Assert.Equal(0, ready & 0x20);
	}

	[Fact]
	public void PendingDsklenStartsAutomaticallyWhenMotorBecomesReady()
	{
		var bus = CreateBusWithTrack(0x1234, 0x5678);
		SelectDriveAndStartMotor(bus, drive: 0);
		bus.WriteWord(0x00DFF096, 0x8210);
		SetDiskPointer(bus, DmaBase);

		WriteDsklenStartSequence(bus, words: 2);
		Assert.Equal(0, bus.Disk.CaptureSnapshot().TransferCount);

		bus.AdvanceDmaTo(MotorReadyCycle());
		Assert.Equal(1, bus.Disk.CaptureSnapshot().TransferCount);

		CompleteDiskDma(bus);
		Assert.Equal(0x1234, ReadChipWord(bus, DmaBase));
		Assert.Equal(0x5678, ReadChipWord(bus, DmaBase + 2));
	}

	[Fact]
	public void CiaAReportsWriteProtectAsActiveLowForSelectedInsertedMedia()
	{
		var bus = CreateBusWithTrack(0x1234);
		bus.Disk.Drive0.SetWriteProtected(true);
		SelectDriveAndStartMotor(bus, drive: 0);

		Assert.Equal(0, bus.ReadByte(0x00BFE001) & 0x08);

		bus.Disk.Drive0.SetWriteProtected(false);
		Assert.NotEqual(0, bus.ReadByte(0x00BFE001) & 0x08);
	}

	[Fact]
	public void InsertCanMarkMediaWriteProtected()
	{
		var bus = CreateDiskComponentBus();
		bus.Disk.Drive0.Insert(AmigaDiskImage.FromEncodedTracks(CreateTrackSetWithWords(0x1234)), writeProtected: true);
		SelectDriveAndStartMotor(bus, drive: 0);

		Assert.Equal(0, bus.ReadByte(0x00BFE001) & 0x08);
	}

	[Fact]
	public void InsertUsesMediaDefaultWriteProtectionPolicy()
	{
		var adfBus = CreateDiskComponentBus();
		adfBus.Disk.Drive0.Insert(AmigaDiskImage.FromAdfBytes(new byte[AmigaDiskImage.StandardAdfSize]));
		SelectDriveAndStartMotor(adfBus, drive: 0);
		Assert.Equal(0, adfBus.ReadByte(0x00BFE001) & 0x08);

		var preservedBus = CreateDiskComponentBus();
		preservedBus.Disk.Drive0.Insert(AmigaDiskImage.FromEncodedTracks(CreateTrackSetWithWords(0x1234)));
		SelectDriveAndStartMotor(preservedBus, drive: 0);
		Assert.Equal(0, preservedBus.ReadByte(0x00BFE001) & 0x08);

		using var zip = new TempDiskFile(".zip");
		WriteZip(zip.Path, "disk.adf", new byte[AmigaDiskImage.StandardAdfSize]);
		var zipBus = CreateDiskComponentBus();
		zipBus.Disk.Drive0.Insert(AmigaDiskImage.Load(zip.Path));
		SelectDriveAndStartMotor(zipBus, drive: 0);
		Assert.Equal(0, zipBus.ReadByte(0x00BFE001) & 0x08);
	}

	[Fact]
	public void EmulatorIpfLoadPreservesExactNonWordTrackBitLength()
	{
		using var temp = new TempDiskFile(".ipf");
		File.WriteAllBytes(temp.Path, CreateSingleRawIpfTrack(startBit: 5, trackBits: 42));

		var disk = AmigaDiskImage.Load(temp.Path);

		var track = disk.ReadEncodedTrack(0, 0);
		Assert.Equal(42, track.BitLength);
		Assert.Equal(5, track.StartBit);
		Assert.Equal(0x4489, track.ReadUInt16AtBit(5));
	}

	[Fact]
	public void EmulatorExtendedAdfLoadPreservesExactNonWordTrackBitLength()
	{
		using var temp = new TempDiskFile(".adf");
		File.WriteAllBytes(temp.Path, CreateSingleRawExtendedAdfTrack(trackBits: 20));

		var disk = AmigaDiskImage.Load(temp.Path);

		var track = disk.ReadEncodedTrack(0, 0);
		Assert.True(disk.HasPreservedTrackData);
		Assert.False(disk.CanWriteTracks);
		Assert.Equal(20, track.BitLength);
		Assert.Equal(0, track.StartBit);
		Assert.Equal(0x4489, track.ReadUInt16AtBit(0));
	}

	[Fact]
	public void DskdatWriteLatchesDataRegisterWithoutStartingWriteDma()
	{
		var bus = CreateDiskComponentBus();

		bus.WriteWord(0x00DFF026, 0xBEEF);

		Assert.Equal(0xBEEF, bus.ReadWord(0x00DFF008));
		Assert.Equal(0, bus.Disk.CaptureSnapshot().TransferCount);
	}

	[Fact]
	public void AdfWriteDmaUpdatesWritableInMemoryMedia()
	{
		var target = AmigaDiskImage.FromAdfBytes(new byte[AmigaDiskImage.StandardAdfSize]);
		var sourceData = new byte[AmigaDiskImage.StandardAdfSize];
		sourceData[5 * AmigaDiskImage.SectorSize] = 0x42;
		sourceData[(5 * AmigaDiskImage.SectorSize) + 511] = 0x99;
		var sourceTrack = AmigaDiskImage.FromAdfBytes(sourceData).ReadEncodedTrack(0, 0);
		var bus = CreateDiskComponentBus();
		bus.Disk.Drive0.Insert(target, writeProtected: false);
		WriteTrackToChip(bus, DmaBase, sourceTrack);
		var cycle = PrepareDiskDma(bus);
		SetDiskPointer(bus, DmaBase, cycle);

		WriteDsklenStartSequence(bus, (ushort)(sourceTrack.ByteLength / 2), cycle, writeMode: true);
		var afterWriteCycle = CompleteDiskDma(bus);

		var sector = target.ReadSector(0, 0, 5);
		Assert.Equal(0x42, sector[0]);
		Assert.Equal(0x99, sector[^1]);
		Assert.True(target.IsDirty);
	}

	[Fact]
	public void AdfWriteDmaCanBeReadBackThroughDiskDmaAsCanonicalTrack()
	{
		var target = AmigaDiskImage.FromAdfBytes(new byte[AmigaDiskImage.StandardAdfSize]);
		var sourceData = new byte[AmigaDiskImage.StandardAdfSize];
		FillTrackPattern(sourceData, cylinder: 0, head: 0);
		var sourceDisk = AmigaDiskImage.FromAdfBytes(sourceData);
		var sourceTrack = sourceDisk.ReadEncodedTrack(0, 0);
		var bus = CreateDiskComponentBus();
		bus.Disk.Drive0.Insert(target, writeProtected: false);
		WriteTrackToChip(bus, DmaBase, sourceTrack);
		var cycle = PrepareDiskDma(bus);
		SetDiskPointer(bus, DmaBase, cycle);

		WriteDsklenStartSequence(bus, (ushort)(sourceTrack.ByteLength / 2), cycle, writeMode: true);
		var afterWriteCycle = CompleteDiskDma(bus);

		var canonicalTrack = target.ReadEncodedTrack(0, 0);
		Assert.Equal(sourceTrack.BitLength, canonicalTrack.BitLength);
		Assert.Equal(sourceTrack.EncodedData.ToArray(), canonicalTrack.EncodedData.ToArray());
		SetDiskPointer(bus, DmaBase + 0x2000, afterWriteCycle);
		WriteDsklenStartSequence(bus, words: 4, afterWriteCycle);
		CompleteDiskDma(bus);
		var readStartOffset = bus.Disk.CaptureDmaTrace()
			.Last(entry => entry.Kind == AmigaDiskDmaTraceKind.Started)
			.SourceBit;

		Assert.Equal(canonicalTrack.ReadUInt16AtBit(readStartOffset), ReadChipWord(bus, DmaBase + 0x2000));
		Assert.Equal(canonicalTrack.ReadUInt16AtBit(readStartOffset + 16), ReadChipWord(bus, DmaBase + 0x2002));
		Assert.Equal(canonicalTrack.ReadUInt16AtBit(readStartOffset + 32), ReadChipWord(bus, DmaBase + 0x2004));
		Assert.Equal(canonicalTrack.ReadUInt16AtBit(readStartOffset + 48), ReadChipWord(bus, DmaBase + 0x2006));
	}

	[Fact]
	public void WriteProtectedAdfRunsWriteDmaButLeavesMediaUnchanged()
	{
		var targetData = new byte[AmigaDiskImage.StandardAdfSize];
		targetData[5 * AmigaDiskImage.SectorSize] = 0x11;
		var target = AmigaDiskImage.FromAdfBytes(targetData);
		var sourceData = new byte[AmigaDiskImage.StandardAdfSize];
		sourceData[5 * AmigaDiskImage.SectorSize] = 0x42;
		var sourceTrack = AmigaDiskImage.FromAdfBytes(sourceData).ReadEncodedTrack(0, 0);
		var bus = CreateDiskComponentBus();
		bus.Disk.Drive0.Insert(target, writeProtected: true);
		WriteTrackToChip(bus, DmaBase, sourceTrack);
		var cycle = PrepareDiskDma(bus);
		SetDiskPointer(bus, DmaBase, cycle);

		WriteDsklenStartSequence(bus, (ushort)(sourceTrack.ByteLength / 2), cycle, writeMode: true);
		CompleteDiskDma(bus);

		Assert.Equal(1, bus.Disk.CaptureSnapshot().TransferCount);
		Assert.Equal(0x11, target.ReadSector(0, 0, 5)[0]);
		Assert.False(target.IsDirty);
	}

	[Fact]
	public void ReadOnlyMediaRunsWriteDmaButLeavesTrackUnchanged()
	{
		var tracks = CreateTrackSetWithWords(0xAAAA);
		var disk = AmigaDiskImage.FromEncodedTracks(tracks);
		var bus = CreateDiskComponentBus();
		bus.Disk.Drive0.Insert(disk, writeProtected: false);
		WriteChipWord(bus, DmaBase, 0x5555);
		var cycle = PrepareDiskDma(bus);
		SetDiskPointer(bus, DmaBase, cycle);

		WriteDsklenStartSequence(bus, words: 1, cycle, writeMode: true);
		CompleteDiskDma(bus);

		Assert.Equal(1, bus.Disk.CaptureSnapshot().TransferCount);
		Assert.Equal(0xAAAA, disk.ReadEncodedTrack(0, 0).ReadUInt16AtBit(0));
	}

	[Fact]
	public void DiskDmaLatchIsConsumedAfterGrantedWriteWord()
	{
		var disk = new WritableTrackDisk(AmigaEncodedTrack.FromBytes(WordsToBytes(0xAAAA)));
		var bus = CreateDiskComponentBus();
		bus.Disk.Drive0.Insert(disk, writeProtected: false);
		WriteChipWord(bus, DmaBase, 0x5AA5);
		var cycle = PrepareDiskDma(bus);
		SetDiskPointer(bus, DmaBase, cycle);

		WriteDsklenStartSequence(bus, words: 1, cycle, writeMode: true);
		CompleteDiskDma(bus);

		var latch = GetPrivateField<object>(bus.Disk, "_diskDmaWordLatch");
		var hasValue = (bool)latch.GetType().GetProperty("HasValue")!.GetValue(latch)!;
		Assert.False(hasValue);
		Assert.Equal(0x5AA5, bus.ReadWord(0x00DFF008));
		Assert.Equal(0x5AA5, disk.Track.ReadUInt16AtBit(0));
	}

	[Fact]
	public void WordSyncWriteDmaWaitsForDskSyncBeforeConsumingMemoryWords()
	{
		var bus = CreateBusWithTrack(0xAAAA, SyncWord, 0xBBBB);
		bus.Disk.Drive0.SetWriteProtected(false);
		var cycle = PrepareDiskDma(bus);
		bus.WriteWord(0x00DFF09E, 0x8400, cycle);
		SetDiskPointer(bus, DmaBase, cycle);
		WriteChipWord(bus, DmaBase, 0x1234);

		WriteDsklenStartSequence(bus, words: 1, cycle, writeMode: true);

		var started = Assert.Single(bus.Disk.CaptureDmaTrace().Where(entry => entry.Kind == AmigaDiskDmaTraceKind.Started));
		Assert.True(started.WordSyncEnabled);
		Assert.Equal(32, started.SourceBit);
		Assert.Equal(32, started.SyncWaitBits);
		CompleteDiskDma(bus);
		Assert.Equal(0x1234, bus.ReadWord(0x00DFF008));
	}

	[Fact]
	public void WriteDmaSplicesAtBitAccurateSyncOffsetAndRecordsPrecompSelection()
	{
		const int SyncShiftBits = 3;
		var sourceTrack = ShiftTrackBits(WordsToBytes(SyncWord, 0xAAAA, 0xBBBB), SyncShiftBits);
		var disk = new WritableTrackDisk(sourceTrack);
		var bus = CreateDiskComponentBus();
		bus.Disk.Drive0.Insert(disk, writeProtected: false);
		var cycle = PrepareDiskDma(bus);
		bus.WriteWord(0x00DFF09E, 0xE400, cycle);
		bus.Paula.AdvanceTo(cycle);
		SetDiskPointer(bus, DmaBase, cycle);
		WriteChipWord(bus, DmaBase, 0x5AA5);

		WriteDsklenStartSequence(bus, words: 1, cycle, writeMode: true);
		CompleteDiskDma(bus);

		var started = Assert.Single(bus.Disk.CaptureDmaTrace().Where(entry => entry.Kind == AmigaDiskDmaTraceKind.Started));
		Assert.Equal(AmigaDiskSyncMode.Word, started.SyncMode);
		Assert.Equal(0x6000, started.Adkcon & 0x6000);
		Assert.Equal(SyncShiftBits + 16, started.SourceBit);
		Assert.Equal(SyncWord, disk.Track.ReadUInt16AtBit(SyncShiftBits));
		Assert.Equal(0x5AA5, disk.Track.ReadUInt16AtBit(started.SourceBit));
	}

	[Fact]
	public void DskdatrReflectsRawInputWordAndDoesNotClearDskbytr()
	{
		var trackBytes = new byte[] { 0x12, 0x34, 0x56 };
		var bus = CreateBusWithTrackBytes(trackBytes);
		SelectDriveAndStartMotor(bus, drive: 0);
		var readyCycle = AdvanceToMotorReady(bus);

		bus.AdvanceDmaTo(readyCycle + DiskByteCycleCount(trackBytes.Length, 2));

		Assert.Equal(0x1234, bus.ReadWord(0x00DFF008));
		var dskbytr = bus.ReadWord(0x00DFF01A);
		Assert.NotEqual(0, dskbytr & 0x8000);
		Assert.Equal(0x34, dskbytr & 0x00FF);
		Assert.Equal(0, bus.ReadWord(0x00DFF01A) & 0x8000);
	}

	[Fact]
	public void DskdatrReflectsLastDmaWord()
	{
		var bus = CreateBusWithTrack(0x1111, 0x2222);

		StartDiskDma(bus, DmaBase, words: 2);
		CompleteDiskDma(bus);

		Assert.Equal(0x2222, bus.ReadWord(0x00DFF008));
	}

	[Fact]
	public void DiskDmaLatchIsConsumedAfterGrantedReadWord()
	{
		var bus = CreateBusWithTrack(0x1234);

		StartDiskDma(bus, DmaBase, words: 1);
		CompleteDiskDma(bus);

		var latch = GetPrivateField<object>(bus.Disk, "_diskDmaWordLatch");
		var hasValue = (bool)latch.GetType().GetProperty("HasValue")!.GetValue(latch)!;
		Assert.False(hasValue);
		Assert.Equal(0x1234, bus.ReadWord(0x00DFF008));
		Assert.Equal(0x1234, ReadChipWord(bus, DmaBase));
	}

	[Fact]
	public void ActiveWordSyncDmaTransfersCurrentWordBeforeBitSlippedSyncRealignsFollowingWord()
	{
		var tracks = CreateEncodedTrackSet();
		tracks[0] = CreateBitSlippedSyncTrack();
		var bus = CreateDiskComponentBus();
		bus.Disk.Drive0.Insert(AmigaDiskImage.FromEncodedTracks(tracks));
		bus.WriteWord(0x00DFF09E, 0x8400);
		bus.Paula.AdvanceTo(0);

		StartDiskDma(bus, DmaBase, words: 4);
		CompleteDiskDma(bus);

		Assert.Equal(0x1111, ReadChipWord(bus, DmaBase));
		Assert.Equal(0x2222, ReadChipWord(bus, DmaBase + 2));
		Assert.Equal(0xAA24, ReadChipWord(bus, DmaBase + 4));
		Assert.Equal(0x3333, ReadChipWord(bus, DmaBase + 6));
	}

	[Fact]
	public void DmaTraceRecordsReadDmaStartAndCompletionState()
	{
		var bus = CreateBusWithTrack(0x1111, 0x2222);

		StartDiskDma(bus, DmaBase, words: 2);
		var started = Assert.Single(bus.Disk.CaptureDmaTrace());
		Assert.Equal(AmigaDiskDmaTraceKind.Started, started.Kind);
		Assert.Equal(1, started.TransferCount);
		Assert.Equal(0, started.Drive);
		Assert.Equal(0, started.Cylinder);
		Assert.Equal(0, started.Head);
		Assert.Equal(DmaBase, started.TargetAddress);
		Assert.Equal(2, started.RequestedWords);
		Assert.Equal(0, started.TransferredWords);
		Assert.Equal(0, started.SourceBit);
		Assert.Equal(0, started.SyncWaitBits);
		Assert.Equal(32, started.TrackBitLength);
		Assert.False(started.WordSyncEnabled);
		Assert.Equal(0x8002, started.Dsklen);
		Assert.Equal(SyncWord, started.Dsksync);

		CompleteDiskDma(bus);

		var trace = bus.Disk.CaptureDmaTrace();
		var completed = Assert.Single(trace.Where(entry => entry.Kind == AmigaDiskDmaTraceKind.Completed));
		Assert.Equal(1, completed.TransferCount);
		Assert.Equal(2, completed.RequestedWords);
		Assert.Equal(2, completed.TransferredWords);
		Assert.Equal(0x2222, completed.Dskdatr);
		Assert.Equal(0x8000, completed.Dsklen);
		Assert.True(completed.Cycle >= started.Cycle);
		Assert.Equal(started.CompletionCycle, completed.Cycle);
	}

	[Fact]
	public void DmaTraceRecordsBlockedStartReason()
	{
		var bus = CreateBusWithTrack(0x1234);
		SelectDriveAndStartMotor(bus, drive: 0);
		var readyCycle = AdvanceToMotorReady(bus);
		SetDiskPointer(bus, DmaBase, readyCycle);

		WriteDsklenStartSequence(bus, words: 1, readyCycle);

		Assert.Equal(0, bus.Disk.CaptureSnapshot().TransferCount);
		var blocked = Assert.Single(bus.Disk.CaptureDmaTrace().Where(entry => entry.Kind == AmigaDiskDmaTraceKind.StartBlocked));
		Assert.Equal(AmigaDiskDmaBlockedReason.DmaDisabled, blocked.BlockedReason);
		Assert.Equal(1, blocked.RequestedWords);
		Assert.Equal(DmaBase, blocked.TargetAddress);
		Assert.Equal(AmigaDiskSyncMode.None, blocked.SyncMode);
	}

	[Fact]
	public void DivergenceTraceRecordsDsklenArmingDmaWordsAndDiskInterrupt()
	{
		using var trace = new EnvironmentVariableScope("COPPER_DISK_DIVERGENCE_TRACE", "1");
		var bus = CreateBusWithTrack(0x1111, 0x2222);
		bus.ConfigureDiskDivergenceTrace(
			"test",
			() => new AmigaDiskTraceCpuContext(
				programCounter: 0x0204,
				lastInstructionProgramCounter: 0x0200,
				lastOpcode: 0x33FC,
				cycles: 1234));

		StartDiskDma(bus, DmaBase, words: 2);
		CompleteDiskDma(bus);

		var events = bus.Disk.CaptureDivergenceTrace();
		var dsklenWrites = events
			.Where(entry => entry.Kind == AmigaDiskTraceEventKind.RegisterWrite && entry.Register == 0x024)
			.ToArray();
		Assert.Equal(2, dsklenWrites.Length);
		Assert.All(dsklenWrites, entry =>
		{
			Assert.Equal(0x0200u, entry.LastInstructionProgramCounter);
			Assert.Equal(0x33FC, entry.LastOpcode);
			Assert.Equal((ushort)0x8002, entry.Value);
		});
		Assert.Contains(events, entry => entry.Kind == AmigaDiskTraceEventKind.DmaStarted);
		Assert.Contains(events, entry => entry.Kind == AmigaDiskTraceEventKind.DmaWord && entry.TransferredWords == 1);
		Assert.Contains(events, entry => entry.Kind == AmigaDiskTraceEventKind.DmaWord && entry.TransferredWords == 2);
		Assert.Contains(events, entry => entry.Kind == AmigaDiskTraceEventKind.DmaCompleted);
		Assert.Contains(events, entry =>
			entry.Kind == AmigaDiskTraceEventKind.DiskInterruptWrite &&
			entry.Register == 0x09C &&
			entry.Value == 0x8002);
	}

	[Fact]
	public void DiskAdvancementIsChunkInvariantForInputAndActiveDma()
	{
		var inputEnd = DiskByteCycleCount(6);
		foreach (var split in new[]
		{
			1L,
			DiskByteCycleCount(2),
			Math.Max(1, DiskByteCycleCount(4) - 1)
		})
		{
			var direct = RunSelectedInputAdvance(inputEnd);
			var splitRun = RunSelectedInputAdvance(split, inputEnd);
			Assert.Equal(direct, splitRun);
		}

		var activeDirect = RunActiveDiskDmaAdvance(splitBeforeCompletion: false);
		var activeSplit = RunActiveDiskDmaAdvance(splitBeforeCompletion: true);
		Assert.Equal(activeDirect, activeSplit);
	}

	[Fact]
	public void CpuBatchWakeCandidateIncludesNextDskbytrInputAdvance()
	{
		var trackBytes = new byte[] { 0x12, 0x34, 0x56 };
		var bus = CreateBusWithTrackBytes(trackBytes);
		SelectDriveAndStartMotor(bus, drive: 0);
		var readyCycle = AdvanceToMotorReady(bus);
		var seedCycle = readyCycle + 1;
		bus.AdvanceDmaTo(seedCycle);
		var byteCycles = DiskByteCycleCount(trackBytes.Length, 1);

		var candidate = bus.Disk.GetNextWakeCandidateCycle(
			seedCycle,
			seedCycle + DiskByteCycleCount(trackBytes.Length, 3));

		Assert.NotNull(candidate);
		Assert.InRange(candidate.Value, seedCycle + 1, seedCycle + byteCycles);
	}

	[Fact]
	public void AdkconFastUsesTwoMicrosecondBitCellsForActiveDiskDma()
	{
		var bus = CreateBusWithTrack(0x1111, 0x2222);
		var cycle = PrepareDiskDma(bus);
		bus.WriteWord(0x00DFF09E, 0x8100, cycle);
		bus.Paula.AdvanceTo(cycle);
		SetDiskPointer(bus, DmaBase, cycle);

		WriteDsklenStartSequence(bus, words: 2, cycle);

		var started = Assert.Single(bus.Disk.CaptureDmaTrace().Where(entry => entry.Kind == AmigaDiskDmaTraceKind.Started));
		var expectedFastCycles = FastDiskBitCycleCount(16) * 2;
		var normalCycles = DiskByteCycleCount(trackByteLength: 4, byteCount: 4);
		Assert.True(started.CompletionCycle - cycle >= expectedFastCycles);
		Assert.True(started.CompletionCycle - cycle < normalCycles);
	}

	[Fact]
	public void DmaTraceRecordsCancellationReasonAndPartialCountdown()
	{
		var bus = CreateBusWithTrack(0x1111, 0x2222, 0x3333, 0x4444);

		StartDiskDma(bus, DmaBase, words: 4);
		var completionCycle = bus.Disk.CaptureSnapshot().ActiveDmaCompletionCycle;
		var cancelCycle = completionCycle - AgnusChipSlotScheduler.SlotCycles - 1;
		bus.AdvanceDmaTo(cancelCycle);
		bus.WriteWord(0x00DFF024, 0x0000, cancelCycle);

		var cancelled = Assert.Single(bus.Disk.CaptureDmaTrace().Where(entry => entry.Kind == AmigaDiskDmaTraceKind.Cancelled));
		Assert.Equal(4, cancelled.RequestedWords);
		Assert.InRange(cancelled.TransferredWords, 0, 3);
		Assert.Equal(cancelCycle, cancelled.Cycle);
		Assert.Equal(0, cancelled.Dsklen & 0xC000);
		Assert.Equal(4 - cancelled.TransferredWords, cancelled.Dsklen & 0x3FFF);
		Assert.Equal(0, bus.ReadWord(0x00DFF01E) & 0x0002);
	}

	[Fact]
	public void DmaTraceRecordsWordSyncStartBitAndSyncWait()
	{
		const int ShiftBits = 5;
		var tracks = CreateEncodedTrackSet();
		tracks[0] = ShiftTrackBits(WordsToBytes(SyncWord, 0xABCD), ShiftBits);
		var bus = CreateDiskComponentBus();
		bus.Disk.Drive0.Insert(AmigaDiskImage.FromEncodedTracks(tracks));
		bus.WriteWord(0x00DFF09E, 0x8400);
		bus.Paula.AdvanceTo(0);

		StartDiskDma(bus, DmaBase, words: 1);

		var started = Assert.Single(bus.Disk.CaptureDmaTrace());
		Assert.Equal(AmigaDiskDmaTraceKind.Started, started.Kind);
		Assert.True(started.WordSyncEnabled);
		Assert.Equal(ShiftBits + 16, started.SourceBit);
		Assert.Equal(ShiftBits + 16, started.SyncWaitBits);
		Assert.Equal(32, started.TrackBitLength);
	}

	[Fact]
	public void DmaTraceRecordsWordSyncMissWithoutStartingTransfer()
	{
		var bus = CreateBusWithTrack(0x1111, 0x2222);
		var cycle = PrepareDiskDma(bus);
		bus.WriteWord(0x00DFF09E, 0x8400, cycle);
		bus.Paula.AdvanceTo(cycle);

		StartDiskDmaWithoutSelecting(bus, DmaBase, words: 1, cycle);

		Assert.False(bus.Disk.CaptureSnapshot().ActiveDma);
		Assert.Equal(0, bus.Disk.CaptureSnapshot().TransferCount);
		var missed = Assert.Single(bus.Disk.CaptureDmaTrace());
		Assert.Equal(AmigaDiskDmaTraceKind.SyncMissing, missed.Kind);
		Assert.True(missed.WordSyncEnabled);
		Assert.Equal(0, missed.SourceBit);
		Assert.Equal(-1, missed.SyncWaitBits);
		Assert.Equal(1, missed.RequestedWords);
	}

	[Fact]
	public void SelectingUnconnectedDriveDoesNotAliasDf0()
	{
		var bus = CreateDiskComponentBus(floppyDriveCount: 1);
		bus.Disk.Drive0.Insert(AmigaDiskImage.FromEncodedTracks(CreateTrackSetWithWords(0x1234)));

		SelectDriveAndStartMotor(bus, drive: 1);
		bus.WriteWord(0x00DFF096, 0x8210);
		bus.Paula.AdvanceTo(0);
		StartDiskDmaWithoutSelecting(bus, DmaBase, words: 1);
		CompleteDiskDma(bus);

		Assert.Equal(-1, bus.Disk.CaptureSnapshot().SelectedDrive);
		Assert.Equal(0, bus.Disk.CaptureSnapshot().TransferCount);
		Assert.Equal(0, ReadChipWord(bus, DmaBase));
	}

	[Fact]
	public void FloppyIndexPulseSetsCiaBFlagWhenMotorIsOn()
	{
		var bus = CreateDiskComponentBus();
		bus.Disk.Drive0.Insert(AmigaDiskImage.FromEncodedTracks(CreateTrackSetWithWords(0x1234)));
		bus.AbleCiaInterrupts(AmigaCiaId.B, 0x80 | AmigaCia.FlagInterruptMask, 0);
		SelectDriveAndStartMotor(bus, drive: 0);
		var indexCycle = ExpectedCiaAccessCycle(0) + (long)Math.Round(AmigaConstants.A500PalCpuClockHz / 5);

		bus.AdvanceCiasTo(indexCycle - 1);
		Assert.Empty(bus.DrainCiaInterrupts());

		bus.AdvanceCiasTo(indexCycle);

		var interruptEvent = Assert.Single(bus.DrainCiaInterrupts());
		Assert.Equal(AmigaCiaId.B, interruptEvent.Cia);
		Assert.Equal(AmigaCia.FlagInterruptMask, interruptEvent.IcrBits);
	}

	[Fact]
	public void FloppyIndexPulseUsesPerDriveMotorPhase()
	{
		var bus = CreateDiskComponentBus(floppyDriveCount: 2);
		bus.Disk.Drive0.Insert(AmigaDiskImage.FromEncodedTracks(CreateTrackSetWithWords(0x1111)));
		bus.Disk.Drive1.Insert(AmigaDiskImage.FromEncodedTracks(CreateTrackSetWithWords(0x2222)));
		bus.AbleCiaInterrupts(AmigaCiaId.B, 0x80 | AmigaCia.FlagInterruptMask, 0);
		SelectDriveAndStartMotor(bus, drive: 0);
		const long SecondDriveStartCycle = 1000;
		SelectDriveWithMotor(bus, drive: 0, motorOn: false, SecondDriveStartCycle);
		SelectDriveAndStartMotor(bus, drive: 1, SecondDriveStartCycle + 1);
		var indexPeriod = (long)Math.Round(AmigaConstants.A500PalCpuClockHz / 5);

		bus.AdvanceCiasTo(indexPeriod);
		Assert.Empty(bus.DrainCiaInterrupts());

		bus.AdvanceCiasTo(ExpectedCiaAccessCycle(SecondDriveStartCycle + 1) + indexPeriod);

		var interruptEvent = Assert.Single(bus.DrainCiaInterrupts());
		Assert.Equal(AmigaCiaId.B, interruptEvent.Cia);
		Assert.Equal(AmigaCia.FlagInterruptMask, interruptEvent.IcrBits);
	}

	private static AmigaBus CreateBusWithTrack(params ushort[] words)
	{
		var bus = CreateDiskComponentBus();
		bus.Disk.Drive0.Insert(AmigaDiskImage.FromEncodedTracks(CreateTrackSetWithWords(words)));
		return bus;
	}

	private static AmigaBus CreateBusWithTrackBytes(params byte[] bytes)
	{
		var bus = CreateDiskComponentBus();
		bus.Disk.Drive0.Insert(AmigaDiskImage.FromEncodedTracks(CreateTrackSetWithBytes(bytes)));
		return bus;
	}

	private static void StartDiskDma(AmigaBus bus, uint targetAddress, ushort words, int drive = 0, long cycle = 0)
	{
		var readyCycle = PrepareDiskDma(bus, cycle, drive);
		StartDiskDmaWithoutSelecting(bus, targetAddress, words, readyCycle);
	}

	private static void StartDiskDmaWithoutSelecting(AmigaBus bus, uint targetAddress, ushort words, long cycle = 0)
	{
		SetDiskPointer(bus, targetAddress, cycle);
		WriteDsklenStartSequence(bus, words, cycle);
	}

	private static long PrepareDiskDma(AmigaBus bus, long cycle = 0, int drive = 0)
	{
		SelectDriveAndStartMotor(bus, drive, cycle);
		var readyCycle = AdvanceToMotorReady(bus, cycle);
		bus.WriteWord(0x00DFF096, 0x8210, readyCycle);
		bus.Paula.AdvanceTo(readyCycle);
		return readyCycle;
	}

	private static void SelectDriveAndStartMotor(AmigaBus bus, int drive, long cycle = 0, int head = 0)
	{
		bus.WriteByte(0x00BFD100, 0xFF, cycle);
		bus.WriteByte(0x00BFD300, 0xFF, cycle);
		var value = (byte)(0x7F & ~(1 << (drive + 3)));
		if (head != 0)
		{
			value = (byte)(value & ~0x04);
		}

		bus.WriteByte(0x00BFD100, value, cycle);
	}

	private static void SelectDriveWithMotor(AmigaBus bus, int drive, bool motorOn, long cycle, int head = 0)
	{
		bus.WriteByte(0x00BFD100, 0xFF, cycle);
		bus.WriteByte(0x00BFD300, 0xFF, cycle);
		var value = (byte)((motorOn ? 0x7F : 0xFF) & ~(1 << (drive + 3)));
		if (head != 0)
		{
			value = (byte)(value & ~0x04);
		}

		bus.WriteByte(0x00BFD100, value, cycle);
	}

	private static long AdvanceToMotorReady(AmigaBus bus, long cycle = 0)
	{
		var readyCycle = MotorReadyCycle(cycle);
		bus.AdvanceDmaTo(readyCycle);
		return readyCycle;
	}

	private static long MotorReadyCycle(long cycle = 0)
	{
		return ExpectedCiaAccessCycle(cycle) + MotorReadyDelayCycles();
	}

	private static long MotorReadyDelayCycles()
	{
		return Math.Max(1, (long)Math.Round(AmigaConstants.A500PalCpuClockHz * 0.5));
	}

	private static long ExpectedCiaAccessCycle(long requestedCycle)
	{
		var cycle = Math.Max(0, requestedCycle + 1);
		var remainder = cycle % AmigaConstants.A500PalCpuCyclesPerCiaTick;
		return remainder == 0
			? cycle
			: cycle + AmigaConstants.A500PalCpuCyclesPerCiaTick - remainder;
	}

	private static void SetDiskPointer(AmigaBus bus, uint targetAddress, long cycle = 0)
	{
		bus.WriteWord(0x00DFF020, (ushort)(targetAddress >> 16), cycle);
		bus.WriteWord(0x00DFF022, (ushort)targetAddress, cycle);
	}

	private static void WriteDsklenStartSequence(AmigaBus bus, ushort words, long cycle = 0, bool writeMode = false)
	{
		var dsklen = (ushort)(0x8000 | (writeMode ? 0x4000 : 0) | words);
		bus.WriteWord(0x00DFF024, dsklen, cycle);
		bus.WriteWord(0x00DFF024, dsklen, cycle);
	}

	private static long CompleteDiskDma(AmigaBus bus)
	{
		var snapshot = bus.Disk.CaptureSnapshot();
		if (!snapshot.ActiveDma)
		{
			return 0;
		}

		var completionCycle = snapshot.ActiveDmaCompletionCycle;
		bus.AdvanceDmaTo(completionCycle);
		bus.AdvanceCiasTo(completionCycle);
		bus.Paula.AdvanceTo(completionCycle);
		return completionCycle;
	}

	private static DiskAdvanceOutcome RunSelectedInputAdvance(params long[] relativeAdvanceCycles)
	{
		using var trace = new EnvironmentVariableScope("COPPER_DISK_DIVERGENCE_TRACE", "1");
		var bytes = WordsToBytes(SyncWord, 0xABCD, 0x1357, 0x2468);
		var bus = CreateBusWithTrackBytes(bytes);
		SelectDriveAndStartMotor(bus, drive: 0);
		var readyCycle = AdvanceToMotorReady(bus);
		bus.WriteWord(0x00DFF09A, 0x9000, readyCycle);
		bus.Paula.AdvanceTo(readyCycle);
		bus.Disk.ClearDmaTrace();
		foreach (var relativeCycle in relativeAdvanceCycles)
		{
			bus.AdvanceDmaTo(readyCycle + relativeCycle);
			bus.Paula.AdvanceTo(readyCycle + relativeCycle);
		}

		return CaptureDiskAdvanceOutcome(bus);
	}

	private static DiskAdvanceOutcome RunActiveDiskDmaAdvance(bool splitBeforeCompletion)
	{
		using var trace = new EnvironmentVariableScope("COPPER_DISK_DIVERGENCE_TRACE", "1");
		var bus = CreateBusWithTrack(0x1111, 0x2222, 0x3333, 0x4444);
		StartDiskDma(bus, DmaBase, words: 4);
		var completionCycle = bus.Disk.CaptureSnapshot().ActiveDmaCompletionCycle;
		if (splitBeforeCompletion)
		{
			bus.AdvanceDmaTo(Math.Max(0, completionCycle - 1));
		}

		bus.AdvanceDmaTo(completionCycle);
		bus.Paula.AdvanceTo(completionCycle);
		return CaptureDiskAdvanceOutcome(bus);
	}

	private static DiskAdvanceOutcome CaptureDiskAdvanceOutcome(AmigaBus bus)
	{
		var snapshot = bus.Disk.CaptureSnapshot();
		var inputEnd = bus.Disk.CaptureDivergenceTrace()
			.LastOrDefault(entry => entry.Kind == AmigaDiskTraceEventKind.DiskInputAdvance && entry.Detail == "end");
		return new DiskAdvanceOutcome(
			snapshot.DiskPointer,
			snapshot.Dsklen,
			snapshot.Dskbytr,
			snapshot.Dskdatr,
			snapshot.ActiveDma,
			snapshot.ActiveDmaCompletionCycle,
			snapshot.TransferCount,
			snapshot.LastTransferWords,
			snapshot.LastTransferAddress,
			bus.Paula.Intreq,
			inputEnd.StreamCycle,
			inputEnd.StreamOffset,
			inputEnd.StreamPosition,
			ReadChipWord(bus, DmaBase),
			ReadChipWord(bus, DmaBase + 2),
			ReadChipWord(bus, DmaBase + 4),
			ReadChipWord(bus, DmaBase + 6));
	}

	private static ushort ReadChipWord(AmigaBus bus, uint address)
	{
		return BigEndian.ReadUInt16(bus.ChipRam, checked((int)address), "disk DMA word");
	}

	private static AmigaBusAccessResult[] CaptureDiskDmaAccesses(AmigaBus bus)
	{
		return bus.BusAccesses
			.Where(access => access.Request.Kind == AmigaBusAccessKind.DiskDma)
			.ToArray();
	}

	private static void WriteChipWord(AmigaBus bus, uint address, ushort value)
	{
		BigEndian.WriteUInt16(bus.ChipRam, checked((int)address), value);
	}

	private static void WriteTrackToChip(AmigaBus bus, uint address, AmigaEncodedTrack track)
	{
		var data = track.EncodedData.Span;
		for (var offset = 0; offset < track.ByteLength; offset += 2)
		{
			var value = BigEndian.ReadUInt16(data, offset, "encoded track DMA word");
			WriteChipWord(bus, address + (uint)offset, value);
		}
	}

	private static long DiskByteCycleCount(int byteCount)
	{
		return DiskByteCycleCount(AmigaDosTrackEncoder.EncodedTrackByteCount, byteCount);
	}

	private static long DiskByteCycleCount(int trackByteLength, int byteCount)
	{
		return (long)Math.Ceiling(
			AmigaConstants.A500PalCpuClockHz / (trackByteLength * 5.0) * byteCount);
	}

	private static long FastDiskBitCycleCount(int bitCount)
	{
		return (long)Math.Ceiling(AmigaConstants.A500PalCpuClockHz / 500_000.0 * bitCount);
	}

	private static AmigaFloppyDrive GetDrive(AmigaBus bus, int drive)
	{
		return drive switch
		{
			0 => bus.Disk.Drive0,
			1 => bus.Disk.Drive1,
			2 => bus.Disk.Drive2,
			3 => bus.Disk.Drive3,
			_ => throw new ArgumentOutOfRangeException(nameof(drive))
		};
	}

	private static AmigaEncodedTrack[] CreateTrackSetWithWords(params ushort[] words)
	{
		return CreateTrackSetWithBytes(WordsToBytes(words));
	}

	private static AmigaEncodedTrack[] CreateTrackSetWithBytes(params byte[] bytes)
	{
		var tracks = CreateEncodedTrackSet();
		if (bytes.Length == 0)
		{
			bytes = WordsToBytes(0xAAAA);
		}

		tracks[0] = AmigaEncodedTrack.FromBytes(bytes);
		return tracks;
	}

	private static AmigaEncodedTrack[] CreateEncodedTrackSet()
	{
		var tracks = new AmigaEncodedTrack[AmigaDiskImage.TrackCount];
		var blankTrack = AmigaEncodedTrack.FromBytes(WordsToBytes(0xAAAA));
		for (var trackIndex = 0; trackIndex < tracks.Length; trackIndex++)
		{
			tracks[trackIndex] = blankTrack;
		}

		return tracks;
	}

	private static byte[] WordsToBytes(params ushort[] words)
	{
		var data = new byte[Math.Max(1, words.Length) * 2];
		for (var i = 0; i < words.Length; i++)
		{
			BigEndian.WriteUInt16(data, i * 2, words[i]);
		}

		return data;
	}

	private static void FillTrackPattern(byte[] data, int cylinder, int head)
	{
		for (var sector = 0; sector < AmigaDiskImage.SectorsPerTrack; sector++)
		{
			var logicalSector = ((cylinder * AmigaDiskImage.HeadCount) + head) * AmigaDiskImage.SectorsPerTrack + sector;
			var offset = logicalSector * AmigaDiskImage.SectorSize;
			for (var index = 0; index < AmigaDiskImage.SectorSize; index++)
			{
				data[offset + index] = (byte)((sector * 19 + index * 5 + cylinder + head) & 0xFF);
			}
		}
	}

	private static byte[] CreateSingleRawIpfTrack(int startBit, int trackBits)
	{
		const int DataBits = 32;
		const int GapBits = 2;
		using var stream = new MemoryStream();
		WriteIpfChunk(stream, "CAPS", Array.Empty<byte>());
		WriteIpfChunk(stream, "INFO", BuildIpfInfo());
		WriteIpfChunk(stream, "IMGE", BuildIpfImageDescriptor(startBit, DataBits, GapBits, trackBits));
		var payload = BuildIpfRawDataPayload(GapBits);
		WriteIpfChunk(stream, "DATA", BuildIpfDataHeader(payload.Length), payload);
		return stream.ToArray();
	}

	private static byte[] CreateSingleRawExtendedAdfTrack(int trackBits)
	{
		const int HeaderLength = 12;
		const int TrackHeaderLength = 12;
		const int TrackBytes = AmigaDiskImage.SectorsPerTrack * AmigaDiskImage.SectorSize;
		var rawTrack = new byte[] { 0x44, 0x89, 0xA0, 0x00 };
		var tableLength = HeaderLength + (AmigaDiskImage.TrackCount * TrackHeaderLength);
		var image = new byte[tableLength + rawTrack.Length + ((AmigaDiskImage.TrackCount - 1) * TrackBytes)];
		"UAE-1ADF"u8.CopyTo(image);
		WriteUInt16(image, 10, AmigaDiskImage.TrackCount);
		var dataOffset = tableLength;
		for (var track = 0; track < AmigaDiskImage.TrackCount; track++)
		{
			var headerOffset = HeaderLength + (track * TrackHeaderLength);
			if (track == 0)
			{
				WriteUInt16(image, headerOffset + 2, 1);
				WriteUInt32(image, headerOffset + 4, rawTrack.Length);
				WriteUInt32(image, headerOffset + 8, trackBits);
				rawTrack.CopyTo(image.AsSpan(dataOffset));
				dataOffset += rawTrack.Length;
			}
			else
			{
				WriteUInt16(image, headerOffset + 2, 0);
				WriteUInt32(image, headerOffset + 4, TrackBytes);
				WriteUInt32(image, headerOffset + 8, 0);
				dataOffset += TrackBytes;
			}
		}

		return image;
	}

	private static byte[] BuildIpfInfo()
	{
		using var stream = new MemoryStream();
		WriteUInt32(stream, 1);
		WriteUInt32(stream, 1);
		WriteUInt32(stream, 1);
		for (var i = 0; i < 9; i++)
		{
			WriteUInt32(stream, 0);
		}

		WriteUInt32(stream, 1);
		for (var i = 0; i < 8; i++)
		{
			WriteUInt32(stream, 0);
		}

		return stream.ToArray();
	}

	private static byte[] BuildIpfImageDescriptor(int startBit, int dataBits, int gapBits, int trackBits)
	{
		using var stream = new MemoryStream();
		WriteUInt32(stream, 0);
		WriteUInt32(stream, 0);
		WriteUInt32(stream, 2);
		WriteUInt32(stream, 1);
		WriteUInt32(stream, (uint)((trackBits + 7) / 8));
		WriteUInt32(stream, 0);
		WriteUInt32(stream, (uint)startBit);
		WriteUInt32(stream, (uint)dataBits);
		WriteUInt32(stream, (uint)gapBits);
		WriteUInt32(stream, (uint)trackBits);
		WriteUInt32(stream, 1);
		WriteUInt32(stream, 0);
		WriteUInt32(stream, 0);
		WriteUInt32(stream, 1);
		WriteUInt32(stream, 0);
		WriteUInt32(stream, 0);
		WriteUInt32(stream, 0);
		return stream.ToArray();
	}

	private static byte[] BuildIpfDataHeader(int dataSize)
	{
		using var stream = new MemoryStream();
		WriteUInt32(stream, (uint)dataSize);
		WriteUInt32(stream, (uint)(dataSize * 8));
		WriteUInt32(stream, 0);
		WriteUInt32(stream, 1);
		return stream.ToArray();
	}

	private static byte[] BuildIpfRawDataPayload(int gapBits)
	{
		using var stream = new MemoryStream();
		WriteUInt32(stream, 32);
		WriteUInt32(stream, (uint)gapBits);
		WriteUInt32(stream, 4);
		WriteUInt32(stream, (uint)((gapBits + 7) / 8));
		WriteUInt32(stream, 2);
		WriteUInt32(stream, 0);
		WriteUInt32(stream, 0xA5);
		WriteUInt32(stream, 32);
		stream.WriteByte(0x24);
		stream.WriteByte(4);
		stream.Write(WordsToBytes(SyncWord, 0x1234));
		stream.WriteByte(0);
		return stream.ToArray();
	}

	private static void WriteIpfChunk(Stream stream, string id, byte[] payload, byte[]? trailingData = null)
	{
		stream.Write(System.Text.Encoding.ASCII.GetBytes(id));
		WriteUInt32(stream, (uint)(12 + payload.Length));
		WriteUInt32(stream, 0);
		stream.Write(payload);
		if (trailingData != null)
		{
			stream.Write(trailingData);
		}
	}

	private static void WriteUInt32(Stream stream, uint value)
	{
		stream.WriteByte((byte)(value >> 24));
		stream.WriteByte((byte)(value >> 16));
		stream.WriteByte((byte)(value >> 8));
		stream.WriteByte((byte)value);
	}

	private static void WriteUInt16(Span<byte> data, int offset, int value)
	{
		data[offset] = (byte)(value >> 8);
		data[offset + 1] = (byte)value;
	}

	private static void WriteUInt32(Span<byte> data, int offset, int value)
	{
		data[offset] = (byte)(value >> 24);
		data[offset + 1] = (byte)(value >> 16);
		data[offset + 2] = (byte)(value >> 8);
		data[offset + 3] = (byte)value;
	}

	private static void WriteZip(string path, string entryName, byte[] data)
	{
		using var file = File.Open(path, FileMode.Create, FileAccess.ReadWrite);
		using var archive = new ZipArchive(file, ZipArchiveMode.Create);
		var entry = archive.CreateEntry(entryName);
		using var stream = entry.Open();
		stream.Write(data);
	}

	private static AmigaEncodedTrack CreateBitSlippedSyncTrack()
	{
		const int noiseBits = 5;
		var bitLength = (6 * 16) + noiseBits;
		var data = new byte[(bitLength + 7) / 8];
		var bitOffset = 0;
		WriteWordBits(data, ref bitOffset, SyncWord);
		WriteWordBits(data, ref bitOffset, 0x1111);
		WriteWordBits(data, ref bitOffset, 0x2222);
		WriteBits(data, ref bitOffset, 0b10101, noiseBits);
		WriteWordBits(data, ref bitOffset, SyncWord);
		WriteWordBits(data, ref bitOffset, 0x3333);
		WriteWordBits(data, ref bitOffset, 0x4444);
		return new AmigaEncodedTrack(data, bitLength);
	}

	private static void WriteWordBits(Span<byte> data, ref int bitOffset, ushort value)
	{
		WriteBits(data, ref bitOffset, value, 16);
	}

	private static void WriteBits(Span<byte> data, ref int bitOffset, uint value, int bitCount)
	{
		for (var bit = bitCount - 1; bit >= 0; bit--)
		{
			if (((value >> bit) & 1) != 0)
			{
				WriteBit(data, bitOffset);
			}

			bitOffset++;
		}
	}

	private static AmigaEncodedTrack ShiftTrackBits(byte[] source, int shiftBits)
	{
		var bitLength = source.Length * 8;
		var shifted = new byte[source.Length];
		shiftBits = AmigaEncodedTrack.WrapBitOffset(shiftBits, bitLength);
		for (var bit = 0; bit < bitLength; bit++)
		{
			if (!ReadBit(source, bit))
			{
				continue;
			}

			WriteBit(shifted, (bit + shiftBits) % bitLength);
		}

		return new AmigaEncodedTrack(shifted, bitLength);
	}

	private static T GetPrivateField<T>(object instance, string fieldName)
	{
		var field = instance.GetType().GetField(
			fieldName,
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(field);
		return Assert.IsAssignableFrom<T>(field.GetValue(instance));
	}

	private static bool ReadBit(ReadOnlySpan<byte> data, int bitOffset)
	{
		return ((data[bitOffset >> 3] >> (7 - (bitOffset & 7))) & 1) != 0;
	}

	private static void WriteBit(Span<byte> data, int bitOffset)
	{
		data[bitOffset >> 3] = (byte)(data[bitOffset >> 3] | (1 << (7 - (bitOffset & 7))));
	}

	private static AmigaBus CreateDiskComponentBus(int floppyDriveCount = 1)
	{
		return new AmigaBus(
			floppyDriveCount: floppyDriveCount,
			enableLiveAgnusDma: false);
	}

	private sealed record DiskConformanceRow(string Group, string Name, DiskRowStatus Status, string Reason)
	{
		public static DiskConformanceRow Executable(string group, string name)
		{
			return new DiskConformanceRow(group, name, DiskRowStatus.Executable, string.Empty);
		}

		public static DiskConformanceRow Pending(string group, string name, string reason)
		{
			return new DiskConformanceRow(group, name, DiskRowStatus.Pending, reason);
		}
	}

	private enum DiskRowStatus
	{
		Executable,
		Pending
	}

	private sealed record PointerRow(uint Address, uint Expected);

	private sealed record DmaGateRow(string Name, ushort Dmacon, bool ExpectedStarts);

	private sealed record DsklenRow(string Name, ushort Words);

	private sealed record DriveRow(int Drive);

	private sealed record StepRow(string Name, bool DirectionBitSet, int ExpectedDelta);

	private sealed record WordSyncRow(string Name, ushort Sync, ushort Payload);

	private sealed record RawBitOffsetRow(int ShiftBits);

	private sealed record DiskAdvanceOutcome(
		uint DiskPointer,
		ushort Dsklen,
		ushort Dskbytr,
		ushort Dskdatr,
		bool ActiveDma,
		long ActiveDmaCompletionCycle,
		int TransferCount,
		int LastTransferWords,
		uint LastTransferAddress,
		ushort Intreq,
		long StreamCycle,
		int StreamOffset,
		double StreamPosition,
		ushort Word0,
		ushort Word1,
		ushort Word2,
		ushort Word3);

	private sealed class WritableTrackDisk : IAmigaDiskImage
	{
		private readonly byte[] _data = new byte[AmigaDiskImage.StandardAdfSize];

		public WritableTrackDisk(AmigaEncodedTrack track)
		{
			Track = track;
		}

		public AmigaEncodedTrack Track { get; private set; }

		public byte[] Data => _data;

		public string Name => "writable-track.ipf";

		public bool HasCompleteSectorData => false;

		public bool HasPreservedTrackData => true;

		public bool DefaultWriteProtected => false;

		public bool IsDirty { get; private set; }

		public bool CanWriteTracks => true;

		public ReadOnlySpan<byte> BootBlock => _data.AsSpan(0, 1024);

		public ReadOnlySpan<byte> ReadSector(int cylinder, int head, int sector)
		{
			return ReadSector(AmigaDiskImage.GetLogicalSector(cylinder, head, sector));
		}

		public ReadOnlySpan<byte> ReadSector(int logicalSector)
		{
			var offset = logicalSector * AmigaDiskImage.SectorSize;
			return _data.AsSpan(offset, AmigaDiskImage.SectorSize);
		}

		public ReadOnlySpan<byte> ReadBytes(int byteOffset, int byteCount)
		{
			return _data.AsSpan(byteOffset, byteCount);
		}

		public AmigaEncodedTrack ReadEncodedTrack(int cylinder, int head)
		{
			return Track;
		}

		public bool TryWriteEncodedTrack(int cylinder, int head, AmigaEncodedTrack track)
		{
			Track = track;
			IsDirty = true;
			return true;
		}
	}

	private sealed class TempDiskFile : IDisposable
	{
		public TempDiskFile(string extension)
		{
			Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
		}

		public string Path { get; }

		public void Dispose()
		{
			try
			{
				File.Delete(Path);
			}
			catch (IOException)
			{
			}
		}
	}

	private sealed class EnvironmentVariableScope : IDisposable
	{
		private readonly string _name;
		private readonly string? _previous;

		public EnvironmentVariableScope(string name, string? value)
		{
			_name = name;
			_previous = Environment.GetEnvironmentVariable(name);
			Environment.SetEnvironmentVariable(name, value);
		}

		public void Dispose()
		{
			Environment.SetEnvironmentVariable(_name, _previous);
		}
	}
}
