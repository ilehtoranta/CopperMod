using Copper68k;
using CopperMod.Amiga.Lightweight;
using Xunit;

namespace CopperMod.Amiga.Lightweight.Tests;

public sealed class LightweightInterruptProgressTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(312, true)]
    [InlineData(313, false)]
    public void InterruptInternalTailAdvancesHardwareAcrossRasterBoundary(int lines, bool shortField)
    {
        using var machine = new LightweightA500Machine();
        long setup = 0;
        machine.WriteLong(0x78, 0xC01000, ref setup, M68kBusAccessKind.CpuDataWrite);
        machine.WriteWord(0xC01000, 0x4E71, ref setup, M68kBusAccessKind.CpuDataWrite);
        machine.WriteWord(0xC01002, 0x60FC, ref setup, M68kBusAccessKind.CpuDataWrite);
        machine.Reset();
        setup = machine.Cpu.Cycles;
        if (shortField)
            machine.WriteWord(LightweightA500Machine.CustomBase + 0x02A, 0, ref setup, M68kBusAccessKind.CpuDataWrite);
        machine.WriteWord(LightweightA500Machine.CustomBase + 0x09A, 0xE000, ref setup, M68kBusAccessKind.CpuDataWrite);
        machine.WriteWord(LightweightA500Machine.CustomBase + 0x09C, 0xA000, ref setup, M68kBusAccessKind.CpuDataWrite);
        long boundary = lines * 454L;
        long entryCycle = boundary - 40;
        machine.AdvanceHardwareTo(entryCycle);
        machine.Cpu.Cycles = entryCycle;
        machine.Cpu.StatusRegister = 0x2000;
        machine.Cpu.SetActiveStackPointer(0xC7FFF0);
        machine.Cpu.Stopped = true; // STOP accepts the visible level without a prior instruction poll.

        Assert.True(machine.DispatchPendingCpuInterrupt());

        // Slow-RAM stack and handler leave the core's final internal tail after
        // its last physical bus transfer. A host frame loop must not spin here.
        Assert.Equal(entryCycle + 44, machine.Cpu.Cycles);
        Assert.Equal(machine.Cpu.Cycles, machine.Cycle);
        Assert.False(machine.Cpu.Stopped);
        Assert.Equal(6, (machine.Cpu.StatusRegister >> 8) & 7);
        Assert.Equal(0xC01000u, machine.Cpu.ProgramCounter);
        Assert.Equal(lines == 1 ? 0 : 1, machine.CompletedFrames);
        Assert.Equal(lines == 1 ? 1 : 0, machine.BeamLine);
        Assert.Equal(2, machine.BeamColorClock);
        // Only reached after the progress invariant above passes: safe on an
        // unfixed engine too, without a timeout or orphaned spinning test task.
        var completed = machine.CompletedFrames;
        machine.ExecuteFrame();
        Assert.Equal(completed + 1, machine.CompletedFrames);
    }
}
