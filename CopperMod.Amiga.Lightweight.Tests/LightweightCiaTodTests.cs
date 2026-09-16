using Copper68k;
using Xunit;

namespace CopperMod.Amiga.Lightweight.Tests;

// HRM 8520 register semantics; board-level boundary tests exercise the declared
// raster-TOD model, not a hardware oracle for sync edges or CIA debounce.
public sealed class LightweightCiaTodTests
{
    [Fact]
    public void ResetClearsCounterAlarmLatchAndStopsUntilLowByteWrite()
    {
        var c = new LightweightCia();
        c.Reset();
        Assert.Equal(long.MaxValue, c.PulseTod(100));
        Assert.Equal(0u, c.TodCounter);
        W(c, 8, 0xFE);
        c.PulseTod(200);
        Assert.Equal(255u, c.TodCounter);
        R(c, 10);
        c.Reset();
        Assert.False(c.TodRunning);
        Assert.Equal(0, R(c, 8));
        Assert.Equal(0, c.PendingInterrupts);
    }

    [Theory]
    [InlineData(0x0000FF, 0x000100)]
    [InlineData(0x00FFFF, 0x010000)]
    [InlineData(0xFFFFFF, 0x000000)]
    public void CounterUses24BitBinaryCarryAndWrap(int initial, int expected)
    {
        var c = NewCounter((uint)initial);
        c.PulseTod(100);
        Assert.Equal((uint)expected, c.TodCounter);
    }

    [Fact]
    public void HighWriteStopsMiddlePreservesStateAndLowWriteRestarts()
    {
        var c = NewCounter(1);
        W(c, 10, 2);
        c.PulseTod(100);
        Assert.Equal(0x020001u, c.TodCounter);
        W(c, 9, 3);
        c.PulseTod(200);
        Assert.Equal(0x020301u, c.TodCounter);
        W(c, 8, 4);
        c.PulseTod(300);
        Assert.Equal(0x020305u, c.TodCounter);
        W(c, 9, 7);
        c.PulseTod(400);
        Assert.Equal(0x020706u, c.TodCounter);
    }

    [Fact]
    public void HighReadLatchesUntilLowReadWhileCounterKeepsRunning()
    {
        var c = NewCounter(0x00FFFF);
        Assert.Equal(0, R(c, 10));
        c.PulseTod(100);
        Assert.Equal(0, R(c, 10)); // A repeated high read must not relatch.
        Assert.Equal(0xFF, R(c, 9));
        Assert.Equal(0xFF, R(c, 8));
        Assert.Equal(1, R(c, 10));
        Assert.Equal(0, R(c, 9));
        Assert.Equal(0, R(c, 8));
    }

    [Fact]
    public void MiddleReadAloneDoesNotFreezeLowByte()
    {
        var c = NewCounter(0x1234);
        Assert.Equal(0x12, R(c, 9));
        c.PulseTod(100);
        Assert.Equal(0x35, R(c, 8));
    }

    [Fact]
    public void AlarmIsWriteOnlyAndDoesNotStopOrOverwriteCounter()
    {
        var c = NewCounter(0x010203);
        W(c, 15, 0x80);
        W(c, 10, 0xAA);
        W(c, 9, 0xBB);
        W(c, 8, 0xCC);
        Assert.Equal(1, R(c, 10));
        Assert.Equal(2, R(c, 9));
        Assert.Equal(3, R(c, 8));
        c.PulseTod(100);
        Assert.Equal(0x010204u, c.TodCounter);
    }

    [Fact]
    public void MaskedAlarmStaysPendingAndEnablingMaskMakesItVisible()
    {
        var c = NewCounter(0);
        Alarm(c, 1);
        Assert.Equal(long.MaxValue, c.PulseTod(100));
        Assert.Equal(4, c.PendingInterrupts);
        c.WriteRegister(13, 0x84, 110, out var irq);
        Assert.Equal(110, irq);
        Assert.Equal(0x84, R(c, 13));
        Assert.Equal(0, R(c, 13));
    }

    [Fact]
    public void EnabledAlarmLatchesAtPulseAndDoesNotRepeatWhilePending()
    {
        var c = NewCounter(0xFFFFFF);
        W(c, 13, 0x84);
        Assert.Equal(100, c.PulseTod(100));
        Assert.Equal(4, c.PendingInterrupts);
        Assert.Equal(long.MaxValue, c.PulseTod(200));
        Assert.Equal(0x84, R(c, 13));
    }

    [Fact]
    public void AlarmPredictionHonorsStopMaskWrapPendingAndVariableFields()
    {
        var c = NewCounter(0xFFFFFE);
        W(c, 13, 0x84);
        Assert.Equal(908, c.GetNextTodInterruptCycle(454, 454));
        Assert.Equal(long.MaxValue, c.GetNextTodInterruptCycle(142102, 0));
        c.PulseTod(454);
        Assert.Equal(142102, c.GetNextTodInterruptCycle(142102, 0));
        c.PulseTod(908);
        Assert.Equal(long.MaxValue, c.GetNextTodInterruptCycle(1362, 454));
        R(c, 13);
        Assert.Equal(1362 + 0xFFFFFFL * 454, c.GetNextTodInterruptCycle(1362, 454));
        W(c, 13, 4);
        Assert.Equal(long.MaxValue, c.GetNextTodInterruptCycle(1362, 454));
        W(c, 13, 0x84);
        W(c, 10, 0);
        Assert.Equal(long.MaxValue, c.GetNextTodInterruptCycle(1362, 454));
    }

    [Fact]
    public void BothCiasFollowTheSameRasterClockAndOddCpuPhases()
    {
        using var m = new LightweightA500Machine();
        Write(m, true, 8, 0);
        Write(m, false, 8, 0);
        m.AdvanceHardwareTo(453);
        Assert.Equal(0u, m.CiaBTod);
        m.AdvanceHardwareTo(454);
        Assert.Equal(1u, m.CiaBTod);
        Assert.Equal(0u, m.CiaATod);
        m.AdvanceHardwareTo(142101);
        Assert.Equal(312u, m.CiaBTod);
        m.AdvanceHardwareTo(142102);
        Assert.Equal(313u, m.CiaBTod);
        Assert.Equal(1u, m.CiaATod);
    }

    [Fact]
    public void LineAlarmPreservesEightCycleCpuVisibilityDelay()
    {
        using var m = LineAlarm();
        Assert.Equal(454, m.NextCiaInterruptCycle);
        m.AdvanceHardwareTo(453);
        Assert.Equal(0, m.Intreq & 0x2000);
        m.AdvanceHardwareTo(454);
        Assert.Equal(0x2000, m.Intreq & 0x2000);
        Assert.Equal(4, m.CiaBPendingInterrupts);
        Assert.Equal(0, m.InterruptPinLevel);
        m.AdvanceHardwareTo(461);
        Assert.Equal(0, m.InterruptPinLevel);
        m.AdvanceHardwareTo(462);
        Assert.Equal(6, m.InterruptPinLevel);
        Assert.Equal(462, m.InterruptPinChangeCycle);
    }

    [Fact]
    public void AlarmWriteAfterAcceptedPulseAffectsOnlyFuturePulses()
    {
        using var m = new LightweightA500Machine();
        Write(m, false, 8, 0);
        m.AdvanceHardwareTo(454);
        Write(m, false, 15, 0x80);
        Write(m, false, 8, 2);
        Write(m, false, 13, 0x84);
        Assert.Equal(1u, m.CiaBTod);
        Assert.Equal(908, m.NextCiaInterruptCycle);
        m.AdvanceHardwareTo(907);
        Assert.Equal(0, m.CiaBPendingInterrupts);
        m.AdvanceHardwareTo(908);
        Assert.Equal(4, m.CiaBPendingInterrupts);
    }

    [Fact]
    public void FieldAlarmPredictionTracksVposwAndLateFieldSelection()
    {
        using var m = new LightweightA500Machine();
        Write(m, true, 8, 0);
        Write(m, true, 15, 0x80);
        Write(m, true, 8, 1);
        Write(m, true, 13, 0x84);
        Assert.Equal(142102, m.NextCiaInterruptCycle);
        m.WriteCustomRegisterFromCopper(0x02A, 0, m.Cycle);
        Assert.Equal(141648, m.NextCiaInterruptCycle);
        m.WriteCustomRegisterFromCopper(0x02A, 0x8000, m.Cycle);
        m.AdvanceHardwareTo(141700);
        m.WriteCustomRegisterFromCopper(0x02A, 0, m.Cycle);
        Assert.Equal(142102, m.NextCiaInterruptCycle);
        m.AdvanceHardwareTo(142102);
        Assert.Equal(1u, m.CiaATod);
        Assert.Equal(4, m.CiaAPendingInterrupts);
    }

    [Fact]
    public void CoarseAndSingleCycleAdvanceProduceIdenticalTodAndInterrupts()
    {
        using var coarse = LineAlarm();
        using var fine = LineAlarm();
        Write(coarse, true, 8, 0);
        Write(fine, true, 8, 0);
        coarse.AdvanceHardwareTo(284205);
        for (var cycle = fine.Cycle + 1; cycle <= 284205; cycle++) fine.AdvanceHardwareTo(cycle);
        Assert.Equal(coarse.CiaATod, fine.CiaATod);
        Assert.Equal(coarse.CiaBTod, fine.CiaBTod);
        Assert.Equal(coarse.Intreq, fine.Intreq);
        Assert.Equal(coarse.InterruptPinChangeCycle, fine.InterruptPinChangeCycle);
    }

    [Fact]
    public void ExternalResetStopsTodWithoutResettingBeamOrLeakingAlarm()
    {
        using var m = LineAlarm();
        m.AdvanceHardwareTo(400);
        m.ResetExternalDevices(400);
        Assert.Equal(400, m.Cycle);
        m.AdvanceHardwareTo(1000);
        Assert.Equal(0u, m.CiaBTod);
        Assert.Equal(0, m.CiaBPendingInterrupts);
        Assert.Equal(0, m.Intreq & 0x2000);
        Assert.Equal(long.MaxValue, m.NextCiaInterruptCycle);
    }

    [Fact]
    public void TodAlarmWakesStopBeforeFrameBoundary()
    {
        using var m = new LightweightA500Machine();
        m.WriteChipWordDma(0x1000, 0x4E72);
        m.WriteChipWordDma(0x1002, 0x2000);
        m.WriteChipWordDma(0x78, 0);
        m.WriteChipWordDma(0x7A, 0x1100); // level 6 vector
        m.WriteChipWordDma(0x1100, 0x60FE);
        m.Reset();
        ConfigureLineAlarm(m);
        m.Cpu.Cycles = m.Cycle;
        m.ExecuteFrame();
        Assert.False(m.Cpu.Stopped);
        Assert.Equal(6, (m.Cpu.StatusRegister >> 8) & 7);
        Assert.Equal(0x1100u, m.Cpu.ProgramCounter);
        Assert.Equal(462, m.InterruptPinChangeCycle);
    }

    [Fact]
    public void RunningTodDoesNotAllocateDuringFrameExecution()
    {
        using var m = new LightweightA500Machine();
        m.WriteChipWordDma(0x1000, 0x4E72);
        m.WriteChipWordDma(0x1002, 0x2700);
        m.Reset();
        Write(m, true, 8, 0);
        Write(m, false, 8, 0);
        m.Cpu.Cycles = m.Cycle;
        // Use the same warmup length as the engineering runner; exclude
        // test-framework assertion setup from the allocation snapshot.
        for (var i = 0; i < 300; i++) m.ExecuteFrame();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 20; i++) m.ExecuteFrame();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(320u, m.CiaATod);
        Assert.Equal(320u * 313, m.CiaBTod);
    }

    private static LightweightCia NewCounter(uint value)
    {
        var c = new LightweightCia();
        c.Reset();
        W(c, 10, (byte)(value >> 16));
        W(c, 9, (byte)(value >> 8));
        W(c, 8, (byte)value);
        return c;
    }

    private static void Alarm(LightweightCia c, uint value)
    {
        W(c, 15, 0x80);
        W(c, 10, (byte)(value >> 16));
        W(c, 9, (byte)(value >> 8));
        W(c, 8, (byte)value);
        W(c, 15, 0);
    }

    private static void W(LightweightCia c, int reg, byte value)
        => c.WriteRegister(reg, value, 0, out _);
    private static byte R(LightweightCia c, int reg)
        => c.ReadRegister(reg, 0xFF, 0, out _);
    private static void Write(LightweightA500Machine m, bool a, int reg, byte value)
    {
        var cycle = m.Cycle;
        m.WriteByte((a ? 0xBFE001u : 0xBFD000u) + (uint)(reg * 256), value,
            ref cycle, M68kBusAccessKind.CpuDataWrite);
    }

    private static LightweightA500Machine LineAlarm()
    {
        var m = new LightweightA500Machine();
        ConfigureLineAlarm(m);
        return m;
    }

    private static void ConfigureLineAlarm(LightweightA500Machine m)
    {
        Write(m, false, 8, 0);
        Write(m, false, 15, 0x80);
        Write(m, false, 8, 1);
        Write(m, false, 13, 0x84);
        m.WriteCustomRegisterFromCopper(0x09A, 0xE000, m.Cycle);
    }
}
