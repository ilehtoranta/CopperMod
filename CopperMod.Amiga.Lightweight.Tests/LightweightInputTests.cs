using Copper68k;
using Xunit;

namespace CopperMod.Amiga.Lightweight.Tests;

public sealed class LightweightInputTests
{
    [Theory]
    [InlineData(0x35, 0x95)]
    [InlineData(0xB5, 0x94)]
    [InlineData(0, 0xFF)]
    public void KeyboardLatchesOnlyAtEighthRisingEdge(int raw, int wire)
    {
        using var m = new LightweightA500Machine();
        m.SubmitKey((byte)raw);
        m.AdvanceHardwareTo(3267);
        Assert.Equal(0, m.CiaAPendingInterrupts & 8);
        m.AdvanceHardwareTo(3268);
        Assert.Equal(8, m.CiaAPendingInterrupts & 8);
        Assert.Equal(wire, ReadCia(m, 12));
        Assert.True(m.KeyboardWaitingForHandshake);
    }

    [Fact]
    public void ReadingSdrOrIcrDoesNotAcknowledgeKeyboard()
    {
        using var m = new LightweightA500Machine();
        m.SubmitKey(0x35);
        m.SubmitKey(0xB5);
        m.AdvanceHardwareTo(4000);
        Assert.Equal(0x95, ReadCia(m, 12));
        ReadCia(m, 13);
        m.AdvanceHardwareTo(8000);
        Assert.True(m.KeyboardWaitingForHandshake);
        Assert.Equal(0x95, ReadCia(m, 12));
        Handshake(m);
        m.AdvanceHardwareTo(m.Cycle + 4000);
        Assert.Equal(0x94, ReadCia(m, 12));
    }

    [Fact]
    public void SerialInterruptUsesMaskAndExistingEightCycleIplDelay()
    {
        using var m = new LightweightA500Machine();
        WriteCia(m, 13, 0x88);
        m.WriteCustomRegisterFromCopper(0x09A, 0xC008, m.Cycle);
        var start = m.Cycle;
        m.SubmitKey(0x35);
        m.AdvanceHardwareTo(start + 3268);
        Assert.Equal(8, m.Intreq & 8);
        Assert.Equal(0, m.InterruptPinLevel);
        m.AdvanceHardwareTo(start + 3276);
        Assert.Equal(2, m.InterruptPinLevel);
        Assert.Equal(0x88, ReadCia(m, 13));
    }

    [Fact]
    public void KeyboardQueueOverflowIsExplicitAndDoesNotCorruptPendingByte()
    {
        using var m = new LightweightA500Machine();
        for (byte i = 0; i < 11; i++) m.SubmitKey(i);
        Assert.Throws<InvalidOperationException>(() => m.SubmitKey(11));
        m.AdvanceHardwareTo(4000);
        Assert.Equal(0xFF, ReadCia(m, 12));
    }

    [Fact]
    public void HandshakeTimeoutIsReportedInsteadOfSilentlyDroppingKeys()
    {
        using var m = new LightweightA500Machine();
        m.SubmitKey(0x35);
        m.AdvanceHardwareTo(3268 + 1_014_412);
        Assert.Contains("resynchronization", m.UnsupportedActiveFeature!);
    }

    [Fact]
    public void PendingKeyboardCrossesFieldAndColdResetClearsIt()
    {
        using var m = new LightweightA500Machine();
        m.AdvanceHardwareTo(141000);
        m.SubmitKey(0x35);
        m.AdvanceHardwareTo(144268);
        Assert.Equal(0x95, ReadCia(m, 12));
        m.Reset();
        Assert.Equal(long.MaxValue, m.KeyboardNextCycle);
        Assert.Equal(0, ReadCia(m, 12));
    }

    [Fact]
    public void MouseDeltasApplyOnceWithEightBitWrapAndButtonsReachPins()
    {
        using var m = new LightweightA500Machine();
        m.SubmitInput(new(0, 0, 3, -1, 257, 0));
        Assert.Equal(0x01FF, ReadCustom(m, 0x00A));
        Assert.Equal(0x01FF, ReadCustom(m, 0x00A));
        Assert.Equal(0, ReadCia(m, 0) & 0x40);
        Assert.Equal(0, ReadCustom(m, 0x016) & 0x0400);
        m.SubmitInput(default);
        Assert.Equal(0x40, ReadCia(m, 0) & 0x40);
        Assert.Equal(0x0400, ReadCustom(m, 0x016) & 0x0400);
        Assert.Equal(0x01FF, ReadCustom(m, 0x00A));
    }

    [Theory]
    [InlineData(1, 0x0100)]
    [InlineData(2, 0x0001)]
    [InlineData(4, 0x0300)]
    [InlineData(8, 0x0003)]
    [InlineData(1 | 8, 0x0103)]
    public void JoystickDirectionsUseHardwareXorEncoding(int directions, int expected)
    {
        using var m = new LightweightA500Machine();
        m.SubmitInput(new(0, (byte)directions, 0, 0, 0, 0));
        Assert.Equal(expected, ReadCustom(m, 0x00C));
    }

    [Fact]
    public void JoytestPreservesLowBitsAndWritesBothCounterPairs()
    {
        using var m = new LightweightA500Machine();
        m.SubmitInput(new(0, 8, 0, 1, 2, 0));
        m.WriteCustomRegisterFromCopper(0x036, 0xAC54, m.Cycle);
        Assert.Equal(0xAE55, ReadCustom(m, 0x00A));
        Assert.Equal(0xAC57, ReadCustom(m, 0x00C));
    }

    [Fact]
    public void PotgoDigitalOutputAndJoystickFireAreVisible()
    {
        using var m = new LightweightA500Machine();
        m.SubmitInput(new(0, 0x30, 0, 0, 0, 0));
        Assert.Equal(0, ReadCia(m, 0) & 0x80);
        Assert.Equal(0, ReadCustom(m, 0x016) & 0x4000);
        m.WriteCustomRegisterFromCopper(0x034, 0x0200, m.Cycle);
        Assert.Equal(0, ReadCustom(m, 0x016) & 0x0100);
        m.WriteCustomRegisterFromCopper(0x034, 0x0300, m.Cycle);
        Assert.Equal(0x0100, ReadCustom(m, 0x016) & 0x0100);
    }

    internal static void Handshake(LightweightA500Machine m)
    {
        WriteCia(m, 14, 0x40);
        m.AdvanceHardwareTo(m.Cycle + 604);
        WriteCia(m, 14, 0);
    }

    [Fact]
    public void HandshakeRequiresLowHighTransitionAndMinimumPulseWidth()
    {
        var k = new LightweightKeyboard();
        k.Reset();
        using var m = new LightweightA500Machine();
        var cia = new LightweightCia();
        cia.Reset();
        k.Enqueue(0x35, 0);
        while (!k.WaitingForHandshake) k.Step(k.NextCycle, cia, m);
        k.HostDataChanged(true, 4000);
        Assert.True(k.WaitingForHandshake);
        k.HostDataChanged(false, 4100);
        k.HostDataChanged(true, 4107);
        Assert.True(k.WaitingForHandshake);
        k.HostDataChanged(false, 4200);
        k.HostDataChanged(true, 4208);
        Assert.False(k.WaitingForHandshake);
    }

    [Fact]
    public void KeyboardHasSeparateDataSetupLowClockAndRisingSamplePhases()
    {
        var k = new LightweightKeyboard();
        k.Reset();
        using var m = new LightweightA500Machine();
        var cia = new LightweightCia();
        cia.Reset();
        k.Enqueue(0x40, 0); // wire byte 0x7F: first pin level low.
        Assert.Equal(2, k.NextCycle);
        k.Step(2, cia, m);
        Assert.False(k.DataHigh);
        Assert.True(k.ClockHigh);
        k.Step(144, cia, m);
        Assert.False(k.ClockHigh);
        k.Step(286, cia, m);
        Assert.True(k.ClockHigh);
        Assert.False(k.DataHigh);
        k.Step(428, cia, m);
        Assert.True(k.DataHigh);
    }

    [Fact]
    public void SerialDirectionChangeDiscardsPartialInputNotCompletedSdr()
    {
        var c = new LightweightCia();
        c.Reset();
        for (var i = 0; i < 8; i++) c.ReceiveSerialBit(true, i * 10);
        c.ReadRegister(13, 255, 80, out _);
        c.ReceiveSerialBit(false, 90);
        c.WriteRegister(14, 0x40, 100, out _);
        c.WriteRegister(14, 0, 110, out _);
        for (var i = 0; i < 7; i++) c.ReceiveSerialBit(false, 120 + i * 10);
        Assert.Equal(0, c.PendingInterrupts);
        Assert.Equal(255, c.ReadRegister(12, 255, 190, out _));
        c.ReceiveSerialBit(false, 200);
        Assert.Equal(8, c.PendingInterrupts);
        Assert.Equal(0, c.ReadRegister(12, 255, 210, out _));
    }

    [Fact]
    public void OddCycleSubmissionNeverSchedulesPastAndStopWakesForSerialIrq()
    {
        using var m = new LightweightA500Machine();
        m.WriteChipWordDma(0x1000, 0x4E72);
        m.WriteChipWordDma(0x1002, 0x2000);
        m.WriteChipWordDma(0x68, 0);
        m.WriteChipWordDma(0x6A, 0x1100);
        m.WriteChipWordDma(0x1100, 0x60FE);
        m.Reset();
        WriteCia(m, 13, 0x88);
        m.WriteCustomRegisterFromCopper(0x09A, 0xC008, m.Cycle);
        m.AdvanceHardwareTo(1001);
        m.SubmitKey(0x35);
        Assert.Equal(1002, m.KeyboardNextCycle);
        m.Cpu.Cycles = m.Cycle;
        m.ExecuteFrame();
        Assert.Equal(2, (m.Cpu.StatusRegister >> 8) & 7);
        Assert.Equal(0x1100u, m.Cpu.ProgramCounter);
        Assert.Equal(4276, m.InterruptPinChangeCycle);
    }

    [Fact]
    public void ExternalResetKeepsPhysicalTransferButClearsCiaReceiveState()
    {
        using var m = new LightweightA500Machine();
        m.SubmitKey(0x35);
        m.AdvanceHardwareTo(1000);
        var next = m.KeyboardNextCycle;
        m.ResetExternalDevices(1000);
        Assert.Equal(next, m.KeyboardNextCycle);
        m.AdvanceHardwareTo(4000);
        Assert.True(m.KeyboardWaitingForHandshake);
        Assert.Equal(0, m.CiaAPendingInterrupts & 8); // Fewer than eight bits after CIA reset.
    }

    [Fact]
    public void OutputCollisionAndExternalCntModeAreNotSuccessfulInput()
    {
        using var m = new LightweightA500Machine();
        m.SubmitKey(1);
        WriteCia(m, 14, 0x40);
        m.AdvanceHardwareTo(4000);
        Assert.Contains("output mode", m.UnsupportedActiveFeature!);
        using var cnt = new LightweightA500Machine();
        WriteCia(cnt, 14, 0x21);
        cnt.SubmitKey(1);
        cnt.AdvanceHardwareTo(4000);
        Assert.Contains("external timer mode", cnt.UnsupportedActiveFeature!);
    }

    [Fact]
    public void InputRecordDistinguishesNoEventFromRawKeyZero()
    {
        using var m = new LightweightA500Machine();
        m.SubmitInput(default);
        Assert.Equal(long.MaxValue, m.KeyboardNextCycle);
        Assert.Throws<ArgumentOutOfRangeException>(() => m.SubmitInput(new(0, 0, 0, 0, 0, 0x200)));
        m.SubmitInput(new(0, 0, 0, 0, 0, 0x100));
        m.AdvanceHardwareTo(4000);
        Assert.Equal(0xFF, ReadCia(m, 12));
    }

    [Fact]
    public void AnalogCounterUseAndInvalidControllerFlagsAreExplicit()
    {
        using var m = new LightweightA500Machine();
        Assert.Throws<ArgumentOutOfRangeException>(() => m.SubmitInput(new(0, 0x80, 0, 0, 0, 0)));
        ReadCustom(m, 0x012);
        Assert.Contains("analog POT", m.UnsupportedActiveFeature!);
    }

    [Fact]
    public void PortZeroJoystickSelectionAndJoytestRemainConsistent()
    {
        using var m = new LightweightA500Machine();
        m.SubmitInput(new(0x94, 0, 0, 0, 0, 0)); // left + fire, port 0 joystick.
        m.WriteCustomRegisterFromCopper(0x036, 0xAC54, m.Cycle);
        Assert.Equal(0xAF54, ReadCustom(m, 0x00A));
        Assert.Equal(0, ReadCia(m, 0) & 0x40);
    }

    [Fact]
    public void TimestampedReplayIsIdenticalAcrossClockPartitioningAndAllocatesNothing()
    {
        using var coarse = new LightweightA500Machine();
        using var fine = new LightweightA500Machine();
        var a = Replay(coarse, singleCycle: false);
        var b = Replay(fine, singleCycle: true);
        Assert.Equal(a, b);
        Assert.Equal(coarse.Cycle, fine.Cycle);
        Assert.Equal(coarse.CiaAPendingInterrupts, fine.CiaAPendingInterrupts);
        for (var i = 0; i < 300; i++) Replay(coarse, false);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 20; i++) Replay(coarse, false);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    private static ulong Replay(LightweightA500Machine m, bool singleCycle)
    {
        ulong hash = 1469598103934665603;
        for (var i = 0; i < 8; i++)
        {
            m.SubmitInput(new(0, (byte)(1 << (i & 3)), (byte)(i & 3), (short)(i - 3), 1, (ushort)(0x135 | ((i & 1) << 7))));
            var end = m.Cycle + 4000;
            if (singleCycle) while (m.Cycle < end) m.AdvanceHardwareTo(m.Cycle + 1);
            else m.AdvanceHardwareTo(end);
            hash = (hash ^ ReadCia(m, 12)) * 1099511628211;
            hash = (hash ^ ReadCustom(m, 0x00A)) * 1099511628211;
            hash = (hash ^ ReadCustom(m, 0x00C)) * 1099511628211;
            hash = (hash ^ ReadCustom(m, 0x016)) * 1099511628211;
            ReadCia(m, 13);
            Handshake(m);
        }
        return hash;
    }

    internal static void WriteCia(LightweightA500Machine m, int reg, byte value)
    {
        var cycle = m.Cycle;
        m.WriteByte(0xBFE001u + (uint)(256 * reg), value, ref cycle, M68kBusAccessKind.CpuDataWrite);
    }
    internal static byte ReadCia(LightweightA500Machine m, int reg)
    {
        var cycle = m.Cycle;
        return m.ReadByte(0xBFE001u + (uint)(256 * reg), ref cycle, M68kBusAccessKind.CpuDataRead);
    }
    internal static ushort ReadCustom(LightweightA500Machine m, ushort offset)
    {
        var cycle = m.Cycle;
        return m.ReadWord(0xDFF000u + offset, ref cycle, M68kBusAccessKind.CpuDataRead);
    }
}
