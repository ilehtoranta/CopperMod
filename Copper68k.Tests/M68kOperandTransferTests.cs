using Copper68k;
using Copper68k.Tests.Synthetic;

namespace Copper68k.Tests;

public sealed class M68kOperandTransferTests
{
    [Theory]
    [InlineData(M68kCpuModel.M68EC020, 2, 6)]
    [InlineData(M68kCpuModel.M68020, 2, 6)]
    [InlineData(M68kCpuModel.M68030, 2, 6)]
    [InlineData(M68kCpuModel.M68040, 1, 5)]
    [InlineData(M68kCpuModel.M68060, 1, 5)]
    public void MovesSelectsFunctionCodeOnlyForTheOperand(M68kCpuModel model, byte readFc, byte writeFc)
    {
        var bus = new FunctionCodeBus();
        bus.Initialize(0x1000, 0x0e900000, 4); // MOVES.L (A0),D0
        bus.Initialize(0x1004, 0x0e910800, 4); // MOVES.L D0,(A1)
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.SourceFunctionCode = 2;
        cpu.State.DestinationFunctionCode = 6;
        cpu.State.A[0] = 0x3000;
        cpu.State.A[1] = 0x4000;
        cpu.ExecuteInstruction();
        cpu.ExecuteInstruction();
        Assert.Equal(0x11223300u + readFc, cpu.State.D[0]);
        Assert.Equal(0x11223300u + readFc, bus.Peek(0x4000, 4));
        Assert.Equal([(0x3000u, (byte?)readFc), (0x4000u, (byte?)writeFc)], bus.Operands);
        Assert.Null(bus.AlternateFunctionCode);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68040)]
    [InlineData(M68kCpuModel.M68060)]
    public void MovesLongUsesSelectedRootAcrossPagesAndBypassesItForCpuSpace(M68kCpuModel model)
    {
        var bus = new SparseRecordingBus();
        bus.Initialize(0x1000, 0x0e900000, 4); // MOVES.L (A0),D0
        bus.Initialize(0x1004, 0x0e900800, 4); // MOVES.L D0,(A0)
        bus.Initialize(0x1008, 0x0e902000, 4); // MOVES.L (A0),D2
        bus.Initialize(0x4100, 0x4202, 4);
        bus.Initialize(0x4200, 0x4402, 4);
        bus.Initialize(0x4424, 0x5001, 4);
        bus.Initialize(0x4428, 0x8001, 4);
        bus.Initialize(0x6100, 0x6202, 4);
        bus.Initialize(0x6200, 0x6402, 4);
        bus.Initialize(0x6424, 0xa001, 4);
        bus.Initialize(0x6428, 0xb001, 4);
        const uint logical = 0x80009ffe;
        bus.Initialize(0x5ffe, 0x1234, 2);
        bus.Initialize(0x8000, 0x5678, 2);
        bus.Initialize(logical, 0xcafebabe, 4);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        var state = cpu.State;
        state.A[0] = logical;
        state.M68040Mmu.UserRootPointer = 0x4000;
        state.M68040Mmu.SupervisorRootPointer = 0x6000;
        state.M68040Mmu.InstructionTransparentTranslation0 = 0xc000;
        state.M68040Mmu.TranslationControl = 0x8000;
        state.SourceFunctionCode = 2;
        state.DestinationFunctionCode = 6;
        cpu.ExecuteInstruction();
        cpu.ExecuteInstruction();
        state.SourceFunctionCode = 7;
        cpu.ExecuteInstruction();
        Assert.Equal(0x12345678u, state.D[0]);
        Assert.Equal(0x1234u, bus.Peek(0xaffe, 2));
        Assert.Equal(0x5678u, bus.Peek(0xb000, 2));
        Assert.Equal(0xcafebabeu, state.D[2]);
    }

    private sealed class FunctionCodeBus : SparseRecordingBus, IM68kBus, IM68kFunctionCodeBus
    {
        public byte? AlternateFunctionCode { get; set; }
        public List<(uint, byte?)> Operands { get; } = [];

        uint IM68kBus.ReadLong(uint address, ref long cycle, M68kBusAccessKind kind)
        {
            if (address != 0x3000) return ReadLong(address, ref cycle, kind);
            Operands.Add((address, AlternateFunctionCode));
            return 0x11223300u + (AlternateFunctionCode ?? 0);
        }

        void IM68kBus.WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind kind)
        {
            Operands.Add((address, AlternateFunctionCode));
            WriteLong(address, value, ref cycle, kind);
        }
    }

    [Theory]
    [InlineData(0, 8, 1, 0)]
    [InlineData(0, 16, 2, 0)]
    [InlineData(0, 24, 2, 1)]
    [InlineData(0, 32, 4, 0)]
    [InlineData(1, 32, 4, 1)]
    public void BitFieldChangeUsesSizedAscendingOperandTransfers(int offset, int width, int first, int last)
    {
        var bus = new SparseRecordingBus();
        bus.Initialize(0x1000, 0xead0, 2); // BFCHG (A0){offset:width}
        bus.Initialize(0x1002, (uint)((offset << 6) | (width & 31)), 2);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68EC020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[0] = 0x3000;
        cpu.ExecuteInstruction();
        var transfers = bus.Accesses.Where(a => a.Address >= 0x3000 && a.Address < 0x3005)
            .Select(a => (a.Address, a.Width, a.Write));
        var expected = new List<(uint, int, bool)>();
        foreach (var write in new[] { false, true })
        {
            expected.Add((0x3000, first, write));
            if (last != 0) expected.Add((0x3000u + (uint)first, last, write));
        }
        Assert.Equal(expected, transfers);
        if (offset == 1)
        {
            Assert.Equal(0x7fffffffu, bus.Peek(0x3000, 4));
            Assert.Equal(0x80, bus.Peek(0x3004));
        }
    }
}
