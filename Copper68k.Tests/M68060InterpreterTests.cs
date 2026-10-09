using Copper68k;
using CopperFloat;

namespace Copper68k.Tests;

public sealed class M68060InterpreterTests
{
    [Fact]
    public void ArithmeticExceptionIsDeferredUntilNextFpuInstructionAndFsaveClearsIt()
    {
        var bus = new Copper68kTestBus(0x10000);
        bus.WriteWords(0x1000, 0xF200, 0x0420, 0x7207, 0xF200, 0x0000); // FDIV FP1,FP0; MOVEQ; FMOVE
        bus.WriteWords(0x4000, 0xF310, 0x4E73); bus.WriteLong(50 * 4, 0x4000);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68060, bus);
        cpu.Reset(0x1000, 0x7000); cpu.State.A[0] = 0x5000;
        cpu.State.M68040Fpu.FP[0] = ExtF80Math.FromInt32(1);
        cpu.State.M68040Fpu.FP[1] = ExtF80.PositiveZero;
        cpu.State.M68040Fpu.Fpcr = 0x400;
        cpu.ExecuteInstruction(); Assert.Equal(0x1004u, cpu.State.ProgramCounter); Assert.Equal(0x7000u, cpu.State.A[7]);
        cpu.ExecuteInstruction(); Assert.Equal(7u, cpu.State.D[1]);
        cpu.ExecuteInstruction(); Assert.Equal(0x4000u, cpu.State.ProgramCounter);
        Assert.Equal(0x6FF8u, cpu.State.A[7]); Assert.Equal(50 * 4, bus.ReadWord(0x6FFE));
        Assert.Equal(0x1006u, bus.ReadLong(0x6FFA));
        cpu.ExecuteInstruction(); Assert.Equal(0xE002u, bus.ReadLong(0x5000));
        cpu.ExecuteInstruction(); cpu.ExecuteInstruction();
        Assert.Equal(0x100Au, cpu.State.ProgramCounter); Assert.Equal(0x7000u, cpu.State.A[7]);
        Assert.Equal(ExtF80Math.FromInt32(1), cpu.State.M68040Fpu.FP[0]);
    }

    [Fact]
    public void ExceptionalFpuFrameRoundTripsTwelveBytesAndSaveReturnsToIdle()
    {
        var bus = new Copper68kTestBus(0x10000);
        bus.WriteWords(0x1000, 0xF350, 0xF311, 0xF312); // restore (A0); save (A1); save (A2)
        bus.WriteLong(0x5000, 0x3FFFE006); bus.WriteLong(0x5004, 0x81234567); bus.WriteLong(0x5008, 0x89ABCDEF);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68060, bus);
        cpu.Reset(0x1000, 0x7000); cpu.State.A[0] = 0x5000; cpu.State.A[1] = 0x5100; cpu.State.A[2] = 0x5200;
        cpu.ExecuteInstruction(); cpu.ExecuteInstruction();
        Assert.Equal(bus.Memory.AsSpan(0x5000, 12).ToArray(), bus.Memory.AsSpan(0x5100, 12).ToArray());
        cpu.ExecuteInstruction(); Assert.Equal(0x6000u, bus.ReadLong(0x5200));
        Assert.Equal(0x1006u, cpu.State.ProgramCounter); Assert.Equal(0x7000u, cpu.State.A[7]);
    }

    [Theory]
    [InlineData(0xF241, 0, 0)] // FSF D1
    [InlineData(0xF248, 0, 2)] // FDBF D0, displacement
    [InlineData(0xF27A, 0, 2)] // FTRAPF.W
    [InlineData(0xF27B, 0, 4)] // FTRAPF.L
    public void RemovedFpuConditionalsUseSoftwarePackageTrap(int opcode, int extension, int extraBytes)
    {
        var bus = new Copper68kTestBus(0x10000); bus.WriteWords(0x1000, (ushort)opcode, (ushort)extension, 0, 0);
        bus.WriteLong(44, 0x4000);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68060, bus);
        cpu.Reset(0x1000, 0x7000); cpu.State.D[0] = 9; cpu.State.D[1] = 7;
        cpu.ExecuteInstruction(); Assert.Equal(0x4000u, cpu.State.ProgramCounter);
        Assert.Equal(0x202C, bus.ReadWord(0x6FFA)); Assert.Equal(0x1004u + (uint)extraBytes, bus.ReadLong(0x6FF6));
        Assert.Equal(9u, cpu.State.D[0]); Assert.Equal(7u, cpu.State.D[1]);
    }

    // Independent expectations: MC68060UM 3.2.2.2, 3.2.2.5, 11.1.2, C.2, D-22.
    [Theory]
    [InlineData(0x0108, 0x0010)] // MOVEP
    [InlineData(0x01C8, 0x0010)] // MOVEP.L to memory
    [InlineData(0x00D0, 0x0000)] // CMP2.B
    [InlineData(0x02D0, 0x0800)] // CHK2.W
    [InlineData(0x04D0, 0x0800)] // CHK2.L
    [InlineData(0x0CFC, 0x0000)] // CAS2.W
    [InlineData(0x0EFC, 0x0000)] // CAS2.L
    [InlineData(0x4C18, 0x1402)] // MULU.L (A0)+,D2:D1 (64-bit result)
    [InlineData(0x4C18, 0x1C02)] // MULS.L
    [InlineData(0x4C58, 0x1402)] // DIVU.L (A0)+,D2:D1 (64-bit dividend)
    [InlineData(0x4C58, 0x1C02)] // DIVS.L
    public void RemovedIntegersTrapBeforeOperandEffects(int opcode, int extension)
    {
        foreach (var sr in new ushort[] { 0x001F, 0x301F })
        {
            var bus = new Copper68kTestBus(0x10000);
            bus.WriteWords(0x1000, (ushort)opcode, (ushort)extension, 0);
            bus.WriteLong(0x2000 + 61 * 4, 0x4000);
            bus.WriteLong(0x5010, 0x12345678);
            using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68060, bus);
            cpu.Reset(0x1000, 0x7000);
            cpu.State.SetUserStackPointer(0x6000);
            cpu.State.StatusRegister = sr;
            cpu.State.VectorBaseRegister = 0x2000;
            cpu.State.A[0] = 0x5000;
            cpu.State.D[0] = 3; cpu.State.D[1] = 5; cpu.State.D[2] = 7;
            cpu.ExecuteInstruction();
            Assert.Equal(0x4000u, cpu.State.ProgramCounter);
            Assert.Equal(0x6FF8u, cpu.State.A[7]);
            Assert.Equal(sr, bus.ReadWord(0x6FF8));
            Assert.Equal(0x1000u, bus.ReadLong(0x6FFA));
            Assert.Equal(61 * 4, bus.ReadWord(0x6FFE));
            Assert.Equal((ushort)(sr | 0x2000), cpu.State.StatusRegister);
            Assert.Equal(0x5000u, cpu.State.A[0]);
            Assert.Equal(new uint[] { 3, 5, 7 }, cpu.State.D.Take(3));
            Assert.Equal(0x12345678u, bus.ReadLong(0x5010));
        }
    }

    // PCR identification/reset regression consolidated into SyntheticPcrTests.
    // Replacement cases and three before/after mutation proofs are recorded in
    // docs/COPPER68K_REFERENCE_QUALIFICATION.md (68060 PCR qualification).

    [Theory]
    [InlineData(0x802)] // No CAAR, MSP, ISP or MMUSR.
    [InlineData(0x803)]
    [InlineData(0x804)]
    [InlineData(0x805)]
    public void RemovedControlRegistersRaiseIllegalInstruction(int control)
    {
        foreach (ushort opcode in new ushort[] { 0x4E7A, 0x4E7B })
        {
            var bus = new Copper68kTestBus(0x10000);
            bus.WriteWords(0x1000, opcode, (ushort)control);
            bus.WriteLong(4 * 4, 0x4000);
            using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68060, bus);
            cpu.Reset(0x1000, 0x7000);
            cpu.ExecuteInstruction();
            Assert.Equal(0x4000u, cpu.State.ProgramCounter);
            Assert.Equal(0x1000u, bus.ReadLong(0x6FFA));
            Assert.Equal(16, bus.ReadWord(0x6FFE));
        }
    }

    [Fact]
    public void MasterBitNeverChangesStackAndInterruptClearsIt()
    {
        var bus = new Copper68kTestBus(0x10000);
        bus.WriteLong(0x2000 + 0x70, 0x4000);
        bus.WriteWord(0x4000, 0x4E73);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68060, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.SetMasterStackPointer(0x5000);
        cpu.State.StatusRegister = 0xB01F;
        cpu.State.VectorBaseRegister = 0x2000;
        Assert.Equal(0x7000u, cpu.State.A[7]);
        cpu.RequestInterrupt(4, 0x70);
        Assert.Equal(0x241F, cpu.State.StatusRegister);
        Assert.Equal(0x6FF8u, cpu.State.A[7]);
        Assert.Equal(0xB01F, bus.ReadWord(0x6FF8));
        Assert.Equal(0x70, bus.ReadWord(0x6FFE));
        cpu.ExecuteInstruction(); // RTE restores M but retains the same SSP.
        Assert.Equal(0x7000u, cpu.State.A[7]);
        Assert.Equal(0xB01F, cpu.State.StatusRegister);
        Assert.Equal(0x1000u, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x0CD0, 0x5001u, true)] // Word CAS must align to word.
    [InlineData(0x0CD0, 0x5002u, false)]
    [InlineData(0x0ED0, 0x5002u, true)] // Long CAS must align to long.
    [InlineData(0x0ED0, 0x5004u, false)]
    public void CasAlignmentUsesOperandWidth(int opcode, uint address, bool trap)
    {
        var bus = new Copper68kTestBus(0x10000);
        bus.WriteWords(0x1000, (ushort)opcode, 0x0040);
        bus.WriteLong(61 * 4, 0x4000);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68060, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[0] = address;
        cpu.State.D[1] = 0x1234;
        cpu.ExecuteInstruction();
        Assert.Equal(trap ? 0x4000u : 0x1004u, cpu.State.ProgramCounter);
        if (trap) Assert.Equal(0u, bus.ReadLong(address));
        else Assert.Equal(0x1234u, (opcode & 0x200) == 0 ? bus.ReadWord(address) : bus.ReadLong(address));
    }

    [Theory]
    [InlineData(0x003, 0x8000u)]
    [InlineData(0x004, 0x8000u)]
    public void EnabledTranslationControlTransfersWithoutAnEmulatorException(int control, uint value)
    {
        var bus = new Copper68kTestBus(0x10000);
        bus.WriteWords(0x1000, 0x4E7B, (ushort)control);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68060, bus);
        cpu.Reset(0x1000, 0x7000); cpu.State.D[0] = value;
        cpu.ExecuteInstruction();
        Assert.Equal(value, control == 3 ? cpu.State.M68040Mmu.TranslationControl
            : cpu.State.M68040Mmu.InstructionTransparentTranslation0);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
    }

    [Fact]
    public void BusControlEnableTransfersWithoutInventingExternalLockActivity()
    {
        var bus = new Copper68kTestBus(0x10000);
        bus.WriteWords(0x1000, 0x4E7B, 0x0008, 0x4E7A, 0x1008);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68060, bus);
        cpu.Reset(0x1000, 0x7000); cpu.State.D[0] = 0x20000000;
        cpu.ExecuteInstruction(); cpu.ExecuteInstruction();
        Assert.Equal(0x20000000u, cpu.State.D[1]);
        Assert.Equal(0x1008u, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x1000, 0x1230u)]
    [InlineData(0x0800, 0x04000208u)]
    [InlineData(0x0400, 0x12345678u)]
    public void FpuControlRegistersTransferInBothDirectionsAndPreserveCcr(int mask, uint value)
    {
        for (int register = 0; register < 8; register++)
        {
            var bus = new Copper68kTestBus(0x10000);
            bus.WriteWords(0x1000, (ushort)(0xF200 | register), (ushort)(0x8000 | mask),
                (ushort)(0xF200 | register), (ushort)(0xA000 | mask));
            using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68060, bus);
            cpu.Reset(0x1000, 0x7000); cpu.State.StatusRegister = 0x301F;
            cpu.State.D[register] = value; cpu.ExecuteInstruction();
            cpu.State.D[register] = 0; cpu.ExecuteInstruction();
            Assert.Equal(value, cpu.State.D[register]);
            Assert.Equal(0x301F, cpu.State.StatusRegister);
            cpu.Reset(0x1004, 0x7000); cpu.ExecuteInstruction();
            Assert.Equal(0u, cpu.State.D[register]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FpuStateFramesAreAlwaysTwelveBytes(bool idle)
    {
        var bus = new Copper68kTestBus(0x10000);
        bus.WriteWords(0x1000, 0xF327, 0xF35F); // FSAVE -(A7); FRESTORE (A7)+.
        bus.WriteLong(0x6FF0, 0x12345678); bus.WriteLong(0x7000, 0xABCDEF12);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68060, bus);
        cpu.Reset(0x1000, 0x7000);
        if (idle) cpu.State.M68040Fpu.MarkInstructionExecuted();
        cpu.ExecuteInstruction();
        Assert.Equal(0x6FF4u, cpu.State.A[7]);
        Assert.Equal(idle ? 0x6000u : 0u, bus.ReadLong(0x6FF4));
        Assert.Equal(0u, bus.ReadLong(0x6FF8)); Assert.Equal(0u, bus.ReadLong(0x6FFC));
        Assert.Equal(0x12345678u, bus.ReadLong(0x6FF0)); Assert.Equal(0xABCDEF12u, bus.ReadLong(0x7000));
        cpu.ExecuteInstruction();
        Assert.Equal(0x7000u, cpu.State.A[7]); Assert.Equal(0x1004u, cpu.State.ProgramCounter);
    }

    [Fact]
    public void DisabledFpuControlTransferUsesFormatFourAndRteConsumesIt()
    {
        var bus = new Copper68kTestBus(0x10000);
        bus.WriteWords(0x1000, 0x4E7B, 0x0808, 0xF201, 0xB000);
        bus.WriteLong(44, 0x4000); bus.WriteWord(0x4000, 0x4E73);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68060, bus);
        cpu.Reset(0x1000, 0x7000); cpu.State.D[0] = 2; cpu.ExecuteInstruction();
        cpu.State.D[1] = 99; cpu.ExecuteInstruction();
        Assert.Equal(0x6FF0u, cpu.State.A[7]); Assert.Equal(0x402C, bus.ReadWord(0x6FF6));
        Assert.Equal(0x1008u, bus.ReadLong(0x6FF2)); Assert.Equal(0x1004u, bus.ReadLong(0x6FFC));
        Assert.Equal(99u, cpu.State.D[1]); cpu.ExecuteInstruction();
        Assert.Equal(0x7000u, cpu.State.A[7]); Assert.Equal(0x1008u, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x0022, 6)] // FADD FP0,FP0
    [InlineData(0x0000, 3)] // FMOVE FP0,FP0
    [InlineData(0x0023, 9)] // FMUL FP0,FP0
    [InlineData(0x0020, 1)] // FDIV FP0,FP0
    [InlineData(0x0028, 0)] // FSUB FP0,FP0
    public void FpuArithmeticProducesArchitecturalResults(int extension, int expected)
    {
        var bus = new Copper68kTestBus(0x10000); bus.WriteWords(0x1000, 0xF200, (ushort)extension);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68060, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.M68040Fpu.FP[0] = ExtF80Math.FromInt32(3);
        cpu.ExecuteInstruction();
        Assert.Equal(ExtF80Math.FromInt32(expected), cpu.State.M68040Fpu.FP[0]);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
        Assert.Equal(0x1000u, cpu.State.M68040Fpu.Fpiar);
        Assert.Equal(0x7000u, cpu.State.A[7]);
    }
}
