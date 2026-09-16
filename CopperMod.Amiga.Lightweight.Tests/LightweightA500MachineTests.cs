using CopperMod.Amiga.Lightweight;
using Copper68k;
using Xunit;

namespace CopperMod.Amiga.Lightweight.Tests;

public sealed class LightweightA500MachineTests
{
    [Fact]
    public void ResetStartsAtDeterministic68000Loop()
    {
        using var machine = new LightweightA500Machine();

        Assert.Equal(0x1000u, machine.Cpu.ProgramCounter);
        Assert.Equal(MachineConstants.StackPointer, machine.Cpu.A[7]);
        Assert.Equal(0x2700, machine.Cpu.StatusRegister);
    }

    [Fact]
    public void FrameAdvancesOnePalFrameAndKeepsOutputBuffersStable()
    {
        using var machine = new LightweightA500Machine();
        var framebuffer = machine.Framebuffer;

        machine.ExecuteFrame();

        Assert.True(machine.Cycle >= 454L * 313);
        Assert.Equal(1, machine.CompletedFrames);
        Assert.Equal(0, machine.BeamLine);
        Assert.Equal(framebuffer.Length, machine.Framebuffer.Length);
        Assert.Equal(2 * (454L * 313 * 48_000 / 7_093_790), machine.AudioSamples.Length);
    }

    [Fact]
    public void StoppedCpuFastForwardsHardwareToFrameBoundary()
    {
        using var machine = new LightweightA500Machine();
        long cycle = 0;
        machine.WriteWord(0x1000, 0x4E72, ref cycle, Copper68k.M68kBusAccessKind.CpuDataWrite);
        machine.WriteWord(0x1002, 0x2700, ref cycle, Copper68k.M68kBusAccessKind.CpuDataWrite);
        machine.Reset();

        machine.ExecuteFrame();

        Assert.True(machine.Cpu.Stopped);
        Assert.Equal(454L * 313, machine.Cycle);
        Assert.Equal(machine.Cycle, machine.Cpu.Cycles);
        Assert.Equal(1, machine.CompletedFrames);
    }

    [Fact]
    public void Copper68kCanBatchAVisibleBusLoopWithoutAWindowAdapter()
    {
        var bus = new BareLoopBus();
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
        var boundary = new CountingBatchBoundary();
        cpu.Reset(0x1000, 0x7FFFC);

        var executed = ((IM68kBatchCore)cpu).ExecuteInstructions(
            1_000,
            20_000,
            boundary);

        Assert.Equal(1_000, executed);
        Assert.True(boundary.BatchCallbacks > 0);
        Assert.True(boundary.LargestBatch > 1);
        Assert.Equal(0x1000u, cpu.State.ProgramCounter);
    }

    [Fact]
    public void VisibleBusBatchMatchesScalarCpuStateAndFetchPhases()
    {
        var scalarBus = new BareLoopBus();
        var batchBus = new BareLoopBus();
        using var scalar = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, scalarBus);
        using var batched = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, batchBus);
        scalar.Reset(0x1000, 0x7FFFC);
        batched.Reset(0x1000, 0x7FFFC);

        for (var instruction = 0; instruction < 1_000; instruction++)
        {
            scalar.ExecuteInstruction();
        }

        var boundary = new CountingBatchBoundary();
        var executed = ((IM68kBatchCore)batched).ExecuteInstructions(
            1_000,
            20_000,
            boundary);

        Assert.Equal(1_000, executed);
        Assert.True(boundary.LargestBatch > 1);
        Assert.Equal(scalar.State.ProgramCounter, batched.State.ProgramCounter);
        Assert.Equal(scalar.State.StatusRegister, batched.State.StatusRegister);
        Assert.Equal(scalar.State.Cycles, batched.State.Cycles);
        Assert.Equal(scalar.State.LastOpcode, batched.State.LastOpcode);
        Assert.Equal(scalar.State.LastInstructionProgramCounter, batched.State.LastInstructionProgramCounter);
        Assert.Equal(scalar.State.D, batched.State.D);
        Assert.Equal(scalar.State.A, batched.State.A);
        Assert.Equal(
            ((IM68000PrefetchDiagnostics)scalar).CapturePrefetchDiagnosticState(),
            ((IM68000PrefetchDiagnostics)batched).CapturePrefetchDiagnosticState());
        Assert.Equal(scalarBus.Reads, batchBus.Reads);
    }

    [Fact]
    public void ConservativeLoopBatchMatchesScalarAcrossFrameBoundaries()
    {
        using var scalar = new LightweightA500Machine(
            configuration: null,
            enableConservativeCpuLoopBatch: false);
        using var batched = new LightweightA500Machine(
            configuration: null,
            enableConservativeCpuLoopBatch: true);

        for (var frame = 0; frame < 32; frame++)
        {
            scalar.ExecuteFrame();
            batched.ExecuteFrame();
        }

        Assert.Equal(scalar.Cycle, batched.Cycle);
        Assert.Equal(scalar.CompletedFrames, batched.CompletedFrames);
        Assert.Equal(scalar.BeamLine, batched.BeamLine);
        Assert.Equal(scalar.BeamColorClock, batched.BeamColorClock);
        Assert.Equal(scalar.Cpu.ProgramCounter, batched.Cpu.ProgramCounter);
        Assert.Equal(scalar.Cpu.StatusRegister, batched.Cpu.StatusRegister);
        Assert.Equal(scalar.Cpu.Cycles, batched.Cpu.Cycles);
        Assert.Equal(scalar.Cpu.LastOpcode, batched.Cpu.LastOpcode);
        Assert.Equal(scalar.Cpu.LastInstructionProgramCounter, batched.Cpu.LastInstructionProgramCounter);
        Assert.Equal(scalar.Cpu.D, batched.Cpu.D);
        Assert.Equal(scalar.Cpu.A, batched.Cpu.A);
        Assert.True(scalar.ChipRam.Span.SequenceEqual(batched.ChipRam.Span));
    }

    [Fact]
    public void ChipRamWordAccessUsesCanonicalSlotClock()
    {
        using var machine = new LightweightA500Machine();
        long cycle = 1;

        machine.WriteWord(0x20, 0x1234, ref cycle, Copper68k.M68kBusAccessKind.CpuDataWrite);
        var value = machine.ReadWord(0x20, ref cycle, Copper68k.M68kBusAccessKind.CpuDataRead);

        Assert.Equal((ushort)0x1234, value);
        Assert.Equal(8, cycle);
    }

    [Fact]
    public void MandatoryRefreshSlotsRepeatAtEveryRasterlineStart()
    {
        Assert.True(LightweightBusArbiter.IsMandatoryRefreshSlot(0));
        Assert.False(LightweightBusArbiter.IsMandatoryRefreshSlot(2));
        Assert.True(LightweightBusArbiter.IsMandatoryRefreshSlot(4));
        Assert.True(LightweightBusArbiter.IsMandatoryRefreshSlot(8));
        Assert.True(LightweightBusArbiter.IsMandatoryRefreshSlot(12));
        Assert.False(LightweightBusArbiter.IsMandatoryRefreshSlot(14));
        Assert.True(LightweightBusArbiter.IsMandatoryRefreshSlot(
            LightweightClock.CpuCyclesPerLine));
    }

    [Fact]
    public void CpuWordAtLineStartWaitsForRefreshAndCompletesOneSlotLater()
    {
        using var machine = new LightweightA500Machine();
        long cycle = LightweightClock.CpuCyclesPerLine;

        machine.WriteWord(0x20, 0xA55A, ref cycle, M68kBusAccessKind.CpuDataWrite);

        Assert.Equal(LightweightClock.CpuCyclesPerLine + 4, cycle);
    }

    [Fact]
    public void A500SlowRamUsesTheSameRefreshContendedAgnusBus()
    {
        using var machine = new LightweightA500Machine();
        long cycle = LightweightClock.CpuCyclesPerLine;

        machine.WriteWord(0x00C00000, 0x5AA5, ref cycle, M68kBusAccessKind.CpuDataWrite);

        Assert.Equal(LightweightClock.CpuCyclesPerLine + 4, cycle);
    }

    [Fact]
    public void CpuLongwordRetainsSeparatedPhysicalWordPhases()
    {
        using var machine = new LightweightA500Machine();
        long cycle = 0;

        machine.WriteLong(0x20, 0x12345678, ref cycle, M68kBusAccessKind.CpuDataWrite);

        Assert.Equal(8, cycle);
        cycle = 14;
        var value = machine.ReadLong(0x20, ref cycle, M68kBusAccessKind.CpuDataRead);
        Assert.Equal(0x12345678u, value);
        Assert.Equal(20, cycle);
    }

    [Fact]
    public void CpuLongwordKeepsItsSecondPhaseAcrossLineAndFieldBoundaries()
    {
        using var machine = new LightweightA500Machine();
        long lineCycle = LightweightClock.CpuCyclesPerLine - 4;
        machine.WriteLong(0x20, 0xAABBCCDD, ref lineCycle, M68kBusAccessKind.CpuDataWrite);
        Assert.Equal(LightweightClock.CpuCyclesPerLine + 4, lineCycle);
        Assert.Equal(2, machine.BeamColorClock);

        long fieldCycle = LightweightClock.PalLongFieldCycles - 4;
        machine.WriteLong(0x24, 0x11223344, ref fieldCycle, M68kBusAccessKind.CpuDataWrite);

        Assert.Equal(LightweightClock.PalLongFieldCycles + 4, fieldCycle);
        Assert.Equal(1, machine.CompletedFrames);
        Assert.Equal(0x0020, machine.Intreq & 0x0020);
        Assert.Equal(0x11223344u,
            ((uint)machine.ChipRam.Span[0x24] << 24) |
            ((uint)machine.ChipRam.Span[0x25] << 16) |
            ((uint)machine.ChipRam.Span[0x26] << 8) |
            machine.ChipRam.Span[0x27]);
    }

    [Fact]
    public void RomWordDoesNotAcquireAnAgnusSlotWait()
    {
        using var machine = new LightweightA500Machine();
        var rom = new byte[0x40000];
        WriteLong(rom, 0, 0x0007FFF0);
        WriteLong(rom, 4, 0x00FC0100);
        rom[0x100] = 0x12;
        rom[0x101] = 0x34;
        machine.LoadKickstart(rom);
        long cycle = 17;

        var value = machine.ReadWord(0x00FC0100, ref cycle, M68kBusAccessKind.CpuInstructionFetch);

        Assert.Equal((ushort)0x1234, value);
        Assert.Equal(17, cycle);
    }

    [Fact]
    public void VposwSelectsA312LineShortFieldWithoutInventedAlternation()
    {
        using var machine = new LightweightA500Machine();
        Assert.True(machine.IsLongField);
        long cycle = 16;

        machine.WriteWord(
            LightweightA500Machine.CustomBase + LightweightRegisters.Vposw,
            0,
            ref cycle,
            M68kBusAccessKind.CpuDataWrite);
        machine.AdvanceHardwareTo(LightweightClock.PalShortFieldCycles);

        Assert.False(machine.IsLongField);
        Assert.Equal(LightweightClock.PalShortFieldLines, machine.LinesThisField);
        Assert.Equal(1, machine.CompletedFrames);
        Assert.Equal(0, machine.BeamLine);
        Assert.Equal(LightweightClock.PalShortFieldCycles, machine.Cycle);
    }

    [Fact]
    public void LateVposwShortFieldSelectionCannotMoveFrameBoundaryBackwards()
    {
        using var machine = new LightweightA500Machine();
        var lateCycle = (454L * 312) + 20;
        machine.AdvanceHardwareTo(lateCycle);
        machine.Cpu.Cycles = lateCycle;

        machine.WriteWord(
            LightweightA500Machine.CustomBase + LightweightRegisters.Vposw,
            0,
            ref lateCycle,
            M68kBusAccessKind.CpuDataWrite);

        Assert.False(machine.IsLongField);
        Assert.Equal(454L * 313, machine.NextFrameCycle);

        machine.Cpu.Cycles = lateCycle;
        machine.Cpu.Stopped = true;
        machine.ExecuteFrame();

        Assert.Equal(454L * 313, machine.Cycle);
        Assert.Equal(1, machine.CompletedFrames);
        Assert.Equal(454L * (313 + 312), machine.NextFrameCycle);
    }

    [Fact]
    public void BeamRegistersExposeLofVerticalBitAndGrantedHorizontalPhase()
    {
        using var machine = new LightweightA500Machine();
        var beamCycle = 300L * LightweightClock.CpuCyclesPerLine + 40;
        machine.AdvanceHardwareTo(beamCycle);
        long cycle = beamCycle;

        var vposr = machine.ReadWord(
            LightweightA500Machine.CustomBase + LightweightRegisters.Vposr,
            ref cycle,
            M68kBusAccessKind.CpuDataRead);
        var vhposr = machine.ReadWord(
            LightweightA500Machine.CustomBase + LightweightRegisters.Vhposr,
            ref cycle,
            M68kBusAccessKind.CpuDataRead);

        Assert.Equal(0x8001, vposr);
        Assert.Equal(300 & 0xFF, vhposr >> 8);
        // VHPOSR exposes the external counter four CCKs ahead of the internal
        // granted slot coordinate. The second read grants at h21.
        Assert.Equal(25, vhposr & 0xFF);
    }

    [Fact]
    public void VerticalBlankLatchesOneColorClockAfterFrameStart()
    {
        using var machine = new LightweightA500Machine();
        long cycle = 16;
        machine.WriteWord(
            LightweightA500Machine.CustomBase + LightweightRegisters.IntenaWrite,
            0xC020,
            ref cycle,
            M68kBusAccessKind.CpuDataWrite);

        machine.AdvanceHardwareTo(LightweightClock.PalLongFieldCycles + 1);
        Assert.Equal(0, machine.Intreq & 0x0020);
        Assert.Equal(0, machine.InterruptPinLevel);

        machine.AdvanceHardwareTo(LightweightClock.PalLongFieldCycles + 2);
        Assert.Equal(0x0020, machine.Intreq & 0x0020);
        Assert.Equal(3, machine.InterruptPinLevel);
        Assert.Equal(
            LightweightClock.PalLongFieldCycles + 2,
            machine.InterruptPinChangeCycle);
    }

    [Fact]
    public void SoftwareInterruptSetAndClearUseOneColorClockVisibilityDelay()
    {
        using var machine = new LightweightA500Machine();
        long cycle = 16;
        machine.WriteWord(
            LightweightA500Machine.CustomBase + LightweightRegisters.IntenaWrite,
            0xC020,
            ref cycle,
            M68kBusAccessKind.CpuDataWrite);
        machine.WriteWord(
            LightweightA500Machine.CustomBase + LightweightRegisters.IntreqWrite,
            0x8020,
            ref cycle,
            M68kBusAccessKind.CpuDataWrite);

        Assert.Equal(3, machine.InterruptPinLevel);
        var assertedCycle = machine.InterruptPinChangeCycle;
        machine.WriteWord(
            LightweightA500Machine.CustomBase + LightweightRegisters.IntreqWrite,
            0x0020,
            ref cycle,
            M68kBusAccessKind.CpuDataWrite);

        Assert.Equal(0, machine.InterruptPinLevel);
        Assert.Equal(assertedCycle + 2, machine.InterruptPinChangeCycle);
    }

    [Fact]
    public void VerticalBlankWakesStopped68000ThroughLevel3Autovector()
    {
        using var machine = new LightweightA500Machine();
        long setupCycle = 0;
        machine.WriteWord(0x1000, 0x4E72, ref setupCycle, M68kBusAccessKind.CpuDataWrite);
        machine.WriteWord(0x1002, 0x2000, ref setupCycle, M68kBusAccessKind.CpuDataWrite);
        machine.WriteLong(0x006C, 0x00001100, ref setupCycle, M68kBusAccessKind.CpuDataWrite);
        machine.WriteWord(0x1100, 0x4E71, ref setupCycle, M68kBusAccessKind.CpuDataWrite);
        machine.WriteWord(0x1102, 0x60FC, ref setupCycle, M68kBusAccessKind.CpuDataWrite);
        machine.Reset();

        long cycle = machine.Cpu.Cycles;
        machine.WriteWord(
            LightweightA500Machine.CustomBase + LightweightRegisters.IntenaWrite,
            0xC020,
            ref cycle,
            M68kBusAccessKind.CpuDataWrite);
        machine.Cpu.Cycles = cycle;

        machine.ExecuteFrame();
        Assert.True(machine.Cpu.Stopped);

        machine.ExecuteFrame();

        Assert.False(machine.Cpu.Stopped);
        Assert.Equal(3, (machine.Cpu.StatusRegister >> 8) & 7);
        Assert.True(machine.Cpu.A[7] < MachineConstants.StackPointer);
        Assert.InRange(machine.Cpu.ProgramCounter, 0x1100u, 0x1104u);
    }

    [Fact]
    public void Running68000AcceptsVisibleInterruptAtAnInstructionBoundary()
    {
        using var machine = new LightweightA500Machine();
        long setupCycle = 0;
        machine.WriteLong(0x006C, 0x00001100, ref setupCycle, M68kBusAccessKind.CpuDataWrite);
        machine.WriteWord(0x1100, 0x4E71, ref setupCycle, M68kBusAccessKind.CpuDataWrite);
        machine.WriteWord(0x1102, 0x60FC, ref setupCycle, M68kBusAccessKind.CpuDataWrite);
        machine.Reset();
        machine.Cpu.StatusRegister = 0x2000;
        long cycle = machine.Cpu.Cycles;
        machine.WriteWord(
            LightweightA500Machine.CustomBase + LightweightRegisters.IntenaWrite,
            0xC020,
            ref cycle,
            M68kBusAccessKind.CpuDataWrite);
        machine.WriteWord(
            LightweightA500Machine.CustomBase + LightweightRegisters.IntreqWrite,
            0x8020,
            ref cycle,
            M68kBusAccessKind.CpuDataWrite);
        machine.Cpu.Cycles = cycle;

        machine.ExecuteFrame();

        Assert.Equal(3, (machine.Cpu.StatusRegister >> 8) & 7);
        Assert.True(machine.Cpu.A[7] < MachineConstants.StackPointer);
        Assert.InRange(machine.Cpu.ProgramCounter, 0x1100u, 0x1104u);
    }

    [Fact]
    public void UnsupportedFeaturesAreReportedWithoutLegacyDelegation()
    {
        using var machine = new LightweightA500Machine();

        Assert.Contains("ECS/AGA chipset profiles", LightweightA500Machine.UnsupportedFeatures);
        Assert.False(machine.IsAdfMounted);
    }

    [Fact]
    public void Native256KiBKickstartMapsAtUpperRomWindowAndOverlayVectors()
    {
        using var machine = new LightweightA500Machine();
        var rom = new byte[0x40000];
        WriteLong(rom, 0, 0x0007FFF0);
        WriteLong(rom, 4, 0x00FC0100);

        machine.LoadKickstart(rom);

        Assert.Equal(0x00FC0100u, machine.Cpu.ProgramCounter);
        Assert.Equal(0x0007FFF0u, machine.Cpu.A[7]);
    }

    [Fact]
    public void DmaconUsesHardwareSetClearSemantics()
    {
        using var machine = new LightweightA500Machine();
        long cycle = 0;

        machine.WriteWord(LightweightA500Machine.CustomBase + 0x096, 0x8201, ref cycle, Copper68k.M68kBusAccessKind.CpuDataWrite);
        Assert.Equal((ushort)0x2201, machine.ReadWord(LightweightA500Machine.CustomBase + 0x002, ref cycle, Copper68k.M68kBusAccessKind.CpuDataRead));

        machine.WriteWord(LightweightA500Machine.CustomBase + 0x096, 0x0001, ref cycle, Copper68k.M68kBusAccessKind.CpuDataWrite);
        Assert.Equal((ushort)0x2200, machine.ReadWord(LightweightA500Machine.CustomBase + 0x002, ref cycle, Copper68k.M68kBusAccessKind.CpuDataRead));
    }

    [Fact]
    public void SetClearRegistersDoNotStoreWriteOnlyOrReservedBits()
    {
        var registers = new LightweightRegisters();

        registers.Write(LightweightRegisters.DmaconWrite, 0xFFFF);
        registers.Write(LightweightRegisters.IntenaWrite, 0xFFFF);
        registers.Write(LightweightRegisters.IntreqWrite, 0xFFFF);

        Assert.Equal(0x07FF, registers.Dmacon);
        Assert.Equal(0x7FFF, registers.Intena);
        Assert.Equal(0x3FFF, registers.Intreq);
    }

    [Fact]
    public void CiaAOverlayControlHandsLowMemoryBackToChipRam()
    {
        using var machine = new LightweightA500Machine();
        var rom = new byte[0x40000];
        WriteLong(rom, 0, 0x0007FFF0);
        WriteLong(rom, 4, 0x00FC0100);
        machine.LoadKickstart(rom);
        Assert.True(machine.RomOverlayEnabled);

        long cycle = 0;
        machine.WriteByte(LightweightA500Machine.CiaABase + 0x201, 0x01, ref cycle, Copper68k.M68kBusAccessKind.CpuDataWrite);
        machine.WriteByte(LightweightA500Machine.CiaABase + 1, 0, ref cycle, Copper68k.M68kBusAccessKind.CpuDataWrite);

        Assert.False(machine.RomOverlayEnabled);
    }

    [Fact]
    public void CiaAResetStateKeepsBootOverlayUntilFirstPortWrite()
    {
        using var machine = new LightweightA500Machine();
        var rom = new byte[0x40000];
        WriteLong(rom, 0, 0x0007FFF0);
        WriteLong(rom, 4, 0x00FC0100);
        machine.LoadKickstart(rom);
        long cycle = 0;

        Assert.Equal(
            0xFC,
            machine.ReadByte(
                LightweightA500Machine.CiaABase + 1,
                ref cycle,
                M68kBusAccessKind.CpuDataRead));
        Assert.Equal(
            0x03,
            machine.ReadByte(
                LightweightA500Machine.CiaABase + 0x201,
                ref cycle,
                M68kBusAccessKind.CpuDataRead));
        Assert.True(machine.RomOverlayEnabled);

        machine.WriteByte(
            LightweightA500Machine.CiaABase + 1,
            0,
            ref cycle,
            M68kBusAccessKind.CpuDataWrite);
        Assert.False(machine.RomOverlayEnabled);
    }

    [Fact]
    public void CiaCpuAccessesAlignToTheTenCyclePeripheralGrid()
    {
        using var machine = new LightweightA500Machine();
        long cycle = 0;

        machine.WriteByte(
            LightweightA500Machine.CiaBBase,
            0x5A,
            ref cycle,
            M68kBusAccessKind.CpuDataWrite);
        Assert.Equal(10, cycle);
        var value = machine.ReadByte(
            LightweightA500Machine.CiaBBase,
            ref cycle,
            M68kBusAccessKind.CpuDataRead);

        // DDRB is still input, so external pull-ups are observed rather than
        // the written latch.
        Assert.Equal(0xFF, value);
        Assert.Equal(20, cycle);
    }

    [Fact]
    public void WritesToAnUnconnectedCiaByteLaneDoNotReachTheChip()
    {
        using var machine = new LightweightA500Machine();
        long cycle = 0;
        machine.WriteByte(
            LightweightA500Machine.CiaABase,
            1,
            ref cycle,
            M68kBusAccessKind.CpuDataWrite);
        Assert.Equal(0, cycle);

        machine.WriteByte(
            LightweightA500Machine.CiaBBase + 1,
            1,
            ref cycle,
            M68kBusAccessKind.CpuDataWrite);
        Assert.Equal(0, cycle);

        var pra = machine.ReadByte(
            LightweightA500Machine.CiaABase + 1,
            ref cycle,
            M68kBusAccessKind.CpuDataRead);
        Assert.Equal(0xFC, pra);
        Assert.Equal(10, cycle);
    }

    [Fact]
    public void CiaBTimerAUnderflowLatchesExternalInterruptAtItsExactCycle()
    {
        using var machine = new LightweightA500Machine();
        long cycle = 0;
        machine.WriteWord(
            LightweightA500Machine.CustomBase + LightweightRegisters.IntenaWrite,
            0xE000,
            ref cycle,
            M68kBusAccessKind.CpuDataWrite);
        machine.WriteByte(0x00BFD400, 0x03, ref cycle, M68kBusAccessKind.CpuDataWrite);
        machine.WriteByte(0x00BFD500, 0x00, ref cycle, M68kBusAccessKind.CpuDataWrite);
        machine.WriteByte(0x00BFDD00, 0x81, ref cycle, M68kBusAccessKind.CpuDataWrite);
        machine.WriteByte(0x00BFDE00, 0x11, ref cycle, M68kBusAccessKind.CpuDataWrite);

        Assert.Equal(40, cycle);
        Assert.Equal(70, machine.NextCiaInterruptCycle);
        machine.AdvanceHardwareTo(69);
        Assert.Equal(0, machine.Intreq & 0x2000);

        machine.AdvanceHardwareTo(70);
        Assert.Equal(0x2000, machine.Intreq & 0x2000);
        Assert.Equal(LightweightCia.TimerAInterrupt, machine.CiaBPendingInterrupts);
        Assert.Equal(0, machine.InterruptPinLevel);

        machine.AdvanceHardwareTo(78);
        Assert.Equal(6, machine.InterruptPinLevel);
        Assert.Equal(78, machine.InterruptPinChangeCycle);
    }

    [Fact]
    public void CiaTimerBCanCountTimerAUnderflowsWithoutAnEventQueue()
    {
        var cia = new LightweightCia();
        cia.Reset();
        cia.WriteRegister(0x04, 1, 0, out _);
        cia.WriteRegister(0x05, 0, 0, out _);
        cia.WriteRegister(0x06, 2, 0, out _);
        cia.WriteRegister(0x07, 0, 0, out _);
        cia.WriteRegister(0x0D, 0x82, 0, out _);
        cia.WriteRegister(0x0E, 0x11, 0, out _);
        cia.WriteRegister(0x0F, 0x51, 0, out _);

        Assert.Equal(20, cia.GetNextActiveInterruptCycle());
        Assert.Equal(long.MaxValue, cia.AdvanceTo(19));
        Assert.Equal(20, cia.AdvanceTo(20));
        Assert.Equal(
            LightweightCia.TimerBInterrupt,
            cia.PendingInterrupts & LightweightCia.TimerBInterrupt);

        var icr = cia.ReadRegister(0x0D, 0xFF, 20, out _);
        Assert.Equal(0x83, icr);
        Assert.Equal(0, cia.PendingInterrupts);
    }

    [Fact]
    public void CiaTimerBRetainsItsPhaseWhenSwitchingFromTimerAToCpuTicks()
    {
        var cia = new LightweightCia();
        cia.Reset();
        cia.WriteRegister(0x04, 1, 0, out _);
        cia.WriteRegister(0x05, 0, 0, out _);
        cia.WriteRegister(0x06, 5, 0, out _);
        cia.WriteRegister(0x07, 0, 0, out _);
        cia.WriteRegister(0x0D, 0x82, 0, out _);
        cia.WriteRegister(0x0E, 0x11, 0, out _);
        cia.WriteRegister(0x0F, 0x51, 0, out _);

        cia.WriteRegister(0x0F, 0x01, 20, out _);

        Assert.Equal(3, cia.TimerBCounter);
        Assert.Equal(50, cia.GetNextActiveInterruptCycle());
    }

    [Fact]
    public void CiaOneShotTimerStopsAfterItsFirstUnderflow()
    {
        var cia = new LightweightCia();
        cia.Reset();
        cia.WriteRegister(0x04, 2, 0, out _);
        cia.WriteRegister(0x05, 0, 0, out _);
        cia.WriteRegister(0x0D, 0x81, 0, out _);
        cia.WriteRegister(0x0E, 0x19, 0, out _);

        Assert.Equal(20, cia.GetNextActiveInterruptCycle());
        Assert.Equal(20, cia.AdvanceTo(100));
        Assert.Equal(0, cia.ReadRegister(0x0E, 0xFF, 100, out _) & 1);
        Assert.Equal(long.MaxValue, cia.GetNextActiveInterruptCycle());
    }

    private static void WriteLong(Span<byte> bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }

    private static class MachineConstants
    {
        internal const uint StackPointer = 512u * 1024u - 4;
    }

    private sealed class BareLoopBus : IM68kBus
    {
        internal List<(uint Address, long Cycle, M68kBusAccessKind Kind)> Reads { get; } = [];

        public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
        {
            var word = ReadWord(address & ~1u, ref cycle, accessKind);
            return (address & 1) == 0 ? (byte)(word >> 8) : (byte)word;
        }

        public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
        {
            Reads.Add((address, cycle, accessKind));
            return address switch
            {
                0x1000 => 0x4E71,
                0x1002 => 0x60FC,
                _ => 0
            };
        }

        public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
            => ((uint)ReadWord(address, ref cycle, accessKind) << 16) |
                ReadWord(address + 2, ref cycle, accessKind);

        public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
        {
        }

        public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
        {
        }

        public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
        {
        }

        public void ResetExternalDevices(long cycle)
        {
        }
    }

    private sealed class CountingBatchBoundary :
        IM68kInstructionBoundary,
        IM68kConservativeBusAccessBatchBoundary
    {
        internal int BatchCallbacks { get; private set; }
        internal int LargestBatch { get; private set; }

        public bool BeforeInstruction() => true;

        public void AfterInstruction(long previousCycle, long currentCycle)
        {
        }

        public bool TryBeginBusAccessTraceBatch(
            M68kCpuState state,
            long targetCycle,
            out long batchTargetCycle)
        {
            batchTargetCycle = targetCycle;
            return true;
        }

        public void AfterBusAccessTraceBatch(
            long previousCycle,
            long currentCycle,
            int instructionCount)
        {
            BatchCallbacks++;
            LargestBatch = Math.Max(LargestBatch, instructionCount);
        }
    }
}
