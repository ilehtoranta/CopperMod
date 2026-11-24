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
