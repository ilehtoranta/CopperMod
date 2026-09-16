using Copper68k;
using Xunit;

namespace CopperMod.Amiga.Lightweight.Tests;

// Discriminating tests of the bounded H6b2 model, not a physical DMAL oracle.
public sealed class LightweightDiskDmaTests
{
    [Fact]
    public void FirstEnableOnlyArmsAndDisableCancelsWithoutInterrupt()
    {
        using var m = new LightweightA500Machine();
        Write(m, 0x024, 0x8001);
        Assert.False(m.DiskDmaActive);
        Write(m, 0x024, 0x8002); // The second value, not equality, starts DMA.
        Assert.True(m.DiskDmaActive);
        Assert.Equal(2, m.DiskDmaRemaining);
        Write(m, 0x024, 0x4000);
        Assert.False(m.DiskDmaActive);
        Assert.Equal(0, m.Intreq & 2);
        Assert.Equal(long.MaxValue, m.DiskDmaNextCycle);
    }

    [Theory]
    [InlineData(0x8000)]
    [InlineData(0x80FF)]
    public void ZeroLengthSecondStrobeCompletesWithoutMemoryTransfer(int first)
    {
        using var m = new LightweightA500Machine();
        m.WriteChipWordDma(0x1000, 0);
        Write(m, 0x022, 0x1000);
        Write(m, 0x024, (ushort)first);
        Assert.Equal(0, m.Intreq & 2);
        Write(m, 0x024, 0x8000);
        Assert.Equal(2, m.Intreq & 2);
        Assert.False(m.DiskDmaActive);
        Assert.Equal(0x1000u, m.GetDiskPointer());
        Assert.Equal(0, m.ReadChipWordDma(0x1000));
    }

    [Fact]
    public void ThreeWordFifoUsesSeparateAddressAndRamPhases()
    {
        using var m = Armed(3);
        FeedWord(m, 0x1234);
        FeedWord(m, 0x5678);
        FeedWord(m, 0x9ABC);
        for (var i = 0; i < 3; i++)
        {
            var input = 14 + 4 * i;
            m.AdvanceHardwareTo(input);
            Assert.Equal((uint)(0x1002 + 2 * i), m.GetDiskPointer());
            Assert.Equal(0, m.ReadChipWordDma((uint)(0x1000 + 2 * i)));
            Assert.False(m.CanCopperOwnOutputSlot(input + 2));
            Assert.False(m.CanBitplaneOwnOutputSlot(input + 2));
            Assert.False(m.CanSpriteOwnOutputSlot(input + 2));
            Assert.False(m.CanBlitterAdvanceControl(input));
            Assert.Equal(3 - i, m.DiskDmaRemaining);
            Assert.Equal(0, m.Intreq & 2);
            m.AdvanceHardwareTo(input + 2);
            Assert.True(m.IsHigherPriorityOutputOwned(input + 2));
        }
        Assert.Equal(0x1234, m.ReadChipWordDma(0x1000));
        Assert.Equal(0x5678, m.ReadChipWordDma(0x1002));
        Assert.Equal(0x9ABC, m.ReadChipWordDma(0x1004));
        Assert.Equal(0, m.DiskDmaRemaining);
        Assert.Equal(2, m.Intreq & 2);
        Assert.False(m.DiskDmaActive);
        Assert.Null(m.UnsupportedActiveFeature);
    }

    [Fact]
    public void CpuCannotUseCompletedDiskOutputCck()
    {
        using var m = Armed(1);
        FeedWord(m, 0xCAFE);
        long cycle = 16;
        Assert.Equal(0xCAFE, m.ReadWord(0x1000, ref cycle, M68kBusAccessKind.CpuDataRead));
        Assert.Equal(20, cycle); // Disk OUT16; CPU accepts18, completes20.
    }

    [Fact]
    public void AcceptedWordSurvivesCancelAndPointerWriteButDoesNotCompleteNewTransfer()
    {
        using var m = Armed(1);
        FeedWord(m, 0xCAFE);
        m.AdvanceHardwareTo(14);
        Write(m, 0x024, 0x4000);
        Write(m, 0x022, 0x2000);
        Write(m, 0x024, 0x8001);
        Write(m, 0x024, 0x8001);
        FeedWord(m, 0xBEEF);
        m.AdvanceHardwareTo(16);
        Assert.Equal(0xCAFE, m.ReadChipWordDma(0x1000));
        Assert.Equal(0, m.ReadChipWordDma(0x2000));
        Assert.Equal(1, m.DiskDmaRemaining);
        Assert.Equal(0, m.Intreq & 2);
        m.AdvanceHardwareTo(20);
        Assert.Equal(0xBEEF, m.ReadChipWordDma(0x2000));
        Assert.Equal(2, m.Intreq & 2);
    }

    [Fact]
    public void CancelBeforeAddressAcceptanceDropsQueuedWord()
    {
        using var m = Armed(1);
        FeedWord(m, 0xFFFF);
        m.AdvanceHardwareTo(12);
        Write(m, 0x024, 0x4000);
        m.AdvanceHardwareTo(500);
        Assert.Equal(0, m.ReadChipWordDma(0x1000));
        Assert.Equal(0x1000u, m.GetDiskPointer());
        Assert.Equal(0, m.Intreq & 2);
    }

    [Theory]
    [InlineData(0x0010)]
    [InlineData(0x0200)]
    public void DmaDisablePausesQueuedWordButNotAcceptedWord(int mask)
    {
        using var m = Armed(1);
        FeedWord(m, 0xBEEF);
        Write(m, 0x096, (ushort)mask);
        m.AdvanceHardwareTo(16);
        Assert.Equal(0, m.ReadChipWordDma(0x1000));
        Write(m, 0x096, (ushort)(0x8000 | mask));
        m.AdvanceHardwareTo(18);
        Write(m, 0x096, (ushort)mask);
        m.AdvanceHardwareTo(20);
        Assert.Equal(0xBEEF, m.ReadChipWordDma(0x1000));
        Assert.Equal(2, m.Intreq & 2);
    }

    [Fact]
    public void WordsyncDiscardsFirstMatchAndStoresFollowingAlignedSync()
    {
        using var m = Armed(2, sync: true);
        FeedWord(m, 0x1111);
        Assert.Equal(long.MaxValue, m.DiskDmaNextCycle);
        m.ReceiveDiskBit(0x4489, true, m.Cycle);
        FeedWord(m, 0x4489, equal: true);
        FeedWord(m, 0xABCD);
        m.AdvanceHardwareTo(20);
        Assert.Equal(0x4489, m.ReadChipWordDma(0x1000));
        Assert.Equal(0xABCD, m.ReadChipWordDma(0x1002));
    }

    [Fact]
    public void UnalignedSubsequentMatchRestartsWordBoundary()
    {
        using var m = Armed(1, sync: true);
        m.ReceiveDiskBit(0x4489, true, m.Cycle);
        for (var i = 0; i < 7; i++) m.ReceiveDiskBit(0, false, m.Cycle);
        m.ReceiveDiskBit(0x4489, true, m.Cycle);
        for (var i = 0; i < 15; i++) m.ReceiveDiskBit(0x1234, false, m.Cycle);
        Assert.Equal(long.MaxValue, m.DiskDmaNextCycle);
        m.ReceiveDiskBit(0x1234, false, m.Cycle);
        Write(m, 0x096, 0x8210); // Refresh the canonical deadline after test injection.
        m.AdvanceHardwareTo(16);
        Assert.Equal(0x1234, m.ReadChipWordDma(0x1000));
    }

    [Theory]
    [InlineData(14, 18)]
    [InlineData(22, 468)]
    [InlineData(142100, 142116)] // Queued word crosses the PAL long-field boundary.
    public void NewlyReadyWordWaitsForAFutureAddressPhase(long ready, long input)
    {
        using var m = Armed(1);
        m.AdvanceHardwareTo(ready);
        FeedWord(m, 0xABCD);
        Assert.Equal(input, m.DiskDmaNextCycle);
        m.AdvanceHardwareTo(input);
        Assert.Equal(0, m.ReadChipWordDma(0x1000));
        m.AdvanceHardwareTo(input + 2);
        Assert.Equal(0xABCD, m.ReadChipWordDma(0x1000));
    }

    [Fact]
    public void PointerWrapsWithinChipRamAndResetRevokesPendingHardware()
    {
        using var m = Armed(2);
        Write(m, 0x020, 0xFFFF);
        Write(m, 0x022, 0xFFFF);
        FeedWord(m, 0xA55A);
        m.AdvanceHardwareTo(14);
        Assert.Equal(0u, m.GetDiskPointer());
        m.AdvanceHardwareTo(16);
        Assert.Equal(0xA55A, m.ReadChipWordDma(0x7FFFE));
        FeedWord(m, 0xFACE);
        m.AdvanceHardwareTo(18);
        m.ResetExternalDevices(18);
        m.AdvanceHardwareTo(24);
        Assert.Equal(0, m.ReadChipWordDma(0));
        Assert.False(m.DiskDmaActive);
        Assert.Equal(0, m.Intreq & 2);
    }

    [Theory]
    [InlineData(32)]
    [InlineData(6334)]
    public void ActualAdfSerialInputTransfersFollowingWordAndCompletesWithoutAllocation(int words)
    {
        using var m = Armed(words, sync: true);
        var image = new byte[LightweightFloppyDrive.StandardAdfBytes];
        for (var i = 0; i < image.Length; i++) image[i] = (byte)(i * 17 + i / 512);
        var expectedDrive = new LightweightFloppyDrive();
        expectedDrive.Mount(image);
        m.MountAdf(image);
        Write(m, 0x09E, 0x8100);
        var cycle = m.Cycle;
        m.WriteByte(0xBFD100, 0x77, ref cycle, M68kBusAccessKind.CpuDataWrite);
        m.WriteByte(0xBFD300, 0xFF, ref cycle, M68kBusAccessKind.CpuDataWrite);
        var first = m.DiskSerialNextCycle;
        m.AdvanceHardwareTo(first - 2);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        m.AdvanceHardwareTo(first + (48 + 16L * words) * 14 + 454);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.Equal(0, allocated);
        Assert.Equal(0x4489, m.ReadChipWordDma(0x1000)); // Second sync in sector header.
        for (var word = 0; word < words; word++)
        {
            var offset = (6 + word * 2) % expectedDrive.Track.Length;
            var expected = (expectedDrive.Track[offset] << 8) | expectedDrive.Track[offset + 1];
            Assert.Equal(expected, m.ReadChipWordDma((uint)(0x1000 + word * 2)));
        }
        Assert.Equal((uint)(0x1000 + 2 * words), m.GetDiskPointer());
        Assert.Equal(0, m.DiskDmaRemaining);
        Assert.Equal(2, m.Intreq & 2);
        Assert.Null(m.UnsupportedActiveFeature);
    }

    [Fact]
    public void DiskCompletionWakesStoppedCpuThroughLevelOneAutovector()
    {
        using var m = new LightweightA500Machine();
        m.WriteChipWordDma(0x1000, 0x4E72);
        m.WriteChipWordDma(0x1002, 0x2000);
        m.WriteChipWordDma(0x0064, 0);
        m.WriteChipWordDma(0x0066, 0x1100);
        m.WriteChipWordDma(0x1100, 0x4E71);
        m.WriteChipWordDma(0x1102, 0x60FC);
        m.Reset();
        m.ExecuteFrame();
        Assert.True(m.Cpu.Stopped);
        Write(m, 0x022, 0x2000);
        Write(m, 0x096, 0x8210);
        Write(m, 0x09A, 0xC002);
        Write(m, 0x024, 0x8001);
        Write(m, 0x024, 0x8001);
        FeedWord(m, 0xA55A);
        m.ExecuteFrame();
        Assert.False(m.Cpu.Stopped);
        Assert.Equal(1, (m.Cpu.StatusRegister >> 8) & 7);
        Assert.InRange(m.Cpu.ProgramCounter, 0x1100u, 0x1104u);
        Assert.Equal(0xA55A, m.ReadChipWordDma(0x2000));
    }

    [Fact]
    public void CpuAndCopperShareSecondStrobeState()
    {
        using var m = new LightweightA500Machine();
        var cycle = m.Cycle;
        m.WriteWord(0xDFF024, 0x80FF, ref cycle, M68kBusAccessKind.CpuDataWrite);
        Assert.Equal(0, m.Intreq & 2);
        Write(m, 0x024, 0x8000);
        Assert.Equal(2, m.Intreq & 2);
    }

    [Fact]
    public void FifoOverflowAndActiveReprogrammingAreNotSilentlyAccepted()
    {
        using var m = Armed(8);
        for (var i = 0; i < 4; i++) FeedWord(m, (ushort)i);
        Assert.Contains("FIFO overrun", m.UnsupportedActiveFeature!);
        using var reprogrammed = Armed(8);
        Write(reprogrammed, 0x024, 0x8002);
        Assert.Contains("active DSKLEN", reprogrammed.UnsupportedActiveFeature!);
    }

    private static LightweightA500Machine Armed(int count, bool sync = false)
    {
        var m = new LightweightA500Machine();
        m.WriteChipWordDma(0, 0);
        for (uint address = 0x1000; address < 0x1040; address += 2)
            m.WriteChipWordDma(address, 0);
        Write(m, 0x022, 0x1000);
        Write(m, 0x096, 0x8210);
        if (sync) Write(m, 0x09E, 0x8400);
        Write(m, 0x024, (ushort)(0x8000 | count));
        Write(m, 0x024, (ushort)(0x8000 | count));
        return m;
    }

    // Inject receiver output to isolate the FIFO/clock contract. Real bit-rate
    // and ADF execution are checked separately above and in DiskSerialTests.
    private static void FeedWord(LightweightA500Machine m, ushort word, bool equal = false)
    {
        for (var i = 0; i < 16; i++) m.ReceiveDiskBit(word, equal && i == 15, m.Cycle);
        Write(m, 0x096, 0x8210);
    }

    private static void Write(LightweightA500Machine m, ushort offset, ushort value)
        => m.WriteCustomRegisterFromCopper(offset, value, m.Cycle);
}
