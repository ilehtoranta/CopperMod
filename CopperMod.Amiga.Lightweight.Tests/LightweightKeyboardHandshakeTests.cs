using Xunit;

namespace CopperMod.Amiga.Lightweight.Tests;

public sealed class LightweightKeyboardHandshakeTests
{
    [Theory]
    [InlineData(199, false)]
    [InlineData(200, true)]
    public void DirectionWriteCannotEraseAnOutputClockAlreadyReached(long writeCycle, bool reached)
    {
        var cia = new LightweightCia();
        cia.Reset();
        cia.WriteRegister(4, 20, 0, out _);
        cia.WriteRegister(5, 0, 0, out _);
        cia.WriteRegister(14, 0x51, 0, out _);
        cia.WriteRegister(12, 1, 10, out _);
        cia.WriteRegister(14, 1, writeCycle, out _);
        cia.AdvanceTo(500);
        Assert.Equal(reached, cia.SerialOutputClockReached);
    }

    [Fact]
    public void BufferWriteOnUnderflowWaitsForTheFollowingOutputClock()
    {
        var cia = new LightweightCia();
        cia.Reset();
        cia.WriteRegister(4, 20, 0, out _);
        cia.WriteRegister(5, 0, 0, out _);
        cia.WriteRegister(14, 0x51, 0, out _);
        cia.WriteRegister(12, 1, 200, out _);
        Assert.Equal(400, cia.GetNextActiveInterruptCycle());
        cia.AdvanceTo(399);
        Assert.False(cia.SerialOutputClockReached);
        cia.AdvanceTo(400);
        Assert.True(cia.SerialOutputClockReached);
    }

    [Fact]
    public void SdrWriteBuffersDataWithoutDrivingSpOrCompletingHandshake()
    {
        var cia = new LightweightCia();
        cia.Reset();
        cia.WriteRegister(14, 0x40, 0, out _);
        Assert.False(cia.SerialSpHigh);
        cia.WriteRegister(12, 0xFF, 10, out _);
        Assert.False(cia.SerialSpHigh); // SDR is a buffer, not the output pin latch.
        Assert.Equal(255, cia.ReadRegister(12, 255, 20, out _));
        Assert.Equal(0, cia.PendingInterrupts & 8);
        cia.WriteRegister(14, 0, 30, out _);
        Assert.True(cia.SerialSpHigh);
    }

    [Fact]
    public void BufferedAcknowledgementBeforeTimerUnderflowAllowsNextKey()
    {
        using var m = new LightweightA500Machine();
        LightweightInputTests.WriteCia(m, 4, 0xFF);
        LightweightInputTests.WriteCia(m, 5, 0xFF);
        LightweightInputTests.WriteCia(m, 14, 1);
        m.SubmitKey(0x44);
        m.SubmitKey(0xC4);
        m.AdvanceHardwareTo(m.Cycle + 4000);
        Assert.Equal(0x77, LightweightInputTests.ReadCia(m, 12));
        LightweightInputTests.ReadCia(m, 13);
        LightweightInputTests.WriteCia(m, 12, 0);
        LightweightInputTests.WriteCia(m, 14, 0x41);
        LightweightInputTests.WriteCia(m, 12, 1);
        m.AdvanceHardwareTo(m.Cycle + 1130);
        LightweightInputTests.WriteCia(m, 12, 0);
        Assert.True(m.KeyboardWaitingForHandshake);
        LightweightInputTests.WriteCia(m, 14, 1);
        Assert.False(m.KeyboardWaitingForHandshake);
        m.AdvanceHardwareTo(m.Cycle + 4000);
        Assert.Equal(0x76, LightweightInputTests.ReadCia(m, 12));
        Assert.Null(m.UnsupportedActiveFeature);
    }

    [Fact]
    public void PendingSerialOutputIsRejectedAtUnderflowEvenWithInterruptsMasked()
    {
        using var m = new LightweightA500Machine();
        LightweightInputTests.WriteCia(m, 4, 100);
        LightweightInputTests.WriteCia(m, 5, 0);
        LightweightInputTests.WriteCia(m, 14, 0x51);
        var first = (m.Cycle / 10 + 100) * 10;
        LightweightInputTests.WriteCia(m, 12, 0xFF);
        Assert.Null(m.UnsupportedActiveFeature);
        m.AdvanceHardwareTo(first - 1);
        Assert.Null(m.UnsupportedActiveFeature);
        m.AdvanceHardwareTo(first);
        Assert.Contains("serial output", m.UnsupportedActiveFeature!);
    }

    [Fact]
    public void ReturningToInputCancelsBufferedTransmissionAcrossFieldBoundary()
    {
        using var m = new LightweightA500Machine();
        m.AdvanceHardwareTo(141900);
        LightweightInputTests.WriteCia(m, 4, 200);
        LightweightInputTests.WriteCia(m, 5, 0);
        LightweightInputTests.WriteCia(m, 14, 0x51);
        LightweightInputTests.WriteCia(m, 12, 0xFF);
        m.AdvanceHardwareTo(142200);
        LightweightInputTests.WriteCia(m, 14, 1);
        m.AdvanceHardwareTo(145000);
        Assert.Null(m.UnsupportedActiveFeature);
        Assert.Equal(0, m.CiaAPendingInterrupts & 8);
    }

    [Fact]
    public void StoppedTimerDoesNotTransmitAndStartingItCannotHidePendingOutput()
    {
        using var m = new LightweightA500Machine();
        LightweightInputTests.WriteCia(m, 4, 100);
        LightweightInputTests.WriteCia(m, 5, 0);
        LightweightInputTests.WriteCia(m, 14, 0x40);
        LightweightInputTests.WriteCia(m, 12, 0xA5);
        m.AdvanceHardwareTo(2000);
        Assert.Null(m.UnsupportedActiveFeature);
        LightweightInputTests.WriteCia(m, 14, 0x41);
        m.AdvanceHardwareTo(m.Cycle + 1100);
        Assert.Contains("serial output", m.UnsupportedActiveFeature!);
        m.Reset();
        m.AdvanceHardwareTo(5000);
        Assert.Null(m.UnsupportedActiveFeature);
    }
}
