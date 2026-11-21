using System.Buffers.Binary;
using Copper68k.Tests.Synthetic;

namespace Copper68k.Tests;

public sealed class M68040AcceleratedMovemTests
{
    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void MovemUsesContiguousRangeOrTransfersEachLong(bool postincrement, int rangeMode)
    {
        var bus = new MovemBus(rangeMode);
        bus.Initialize(MovemBus.Code, 0x2045, 2);     // MOVEA.L D5,A0
        bus.Initialize(MovemBus.Code + 2, postincrement ? 0x4CD8u : 0x4CD0u, 2);
        bus.Initialize(MovemBus.Code + 4, 0x8101, 2); // MOVEM.L memory,D0/A0/A7
        bus.Initialize(MovemBus.Code + 6, 0x60F8, 2); // BRA.S Code
        using var cpu = M68kJitCore.CreateM68040ForTesting(bus, enableV2: true,
            enableUncachedFastRamTraces: true);
        cpu.Reset(MovemBus.Code, 0x7800);
        cpu.State.D[5] = MovemBus.Source;

        Assert.Equal(300, cpu.ExecuteInstructions(300, 10000, new Boundary()));
        Assert.True(cpu.GetJitStatistics().CompiledInstructions > 0);
        Assert.True(bus.WholeRangeProbes > 0);
        Assert.Equal(MovemBus.Code, cpu.State.ProgramCounter);
        Assert.Equal(0x12345678u, cpu.State.D[0]);
        Assert.Equal(postincrement ? MovemBus.Source + 12 : 0x9ABCDEF0u, cpu.State.A[0]);
        Assert.Equal(0x6800u, cpu.State.A[7]);
        Assert.Equal(300L, cpu.State.Cycles);
        if (rangeMode == 0)
            Assert.True(bus.ScalarReads < bus.WholeRangeProbes * 3);
        else
            Assert.Equal(bus.WholeRangeProbes * 3, bus.ScalarReads);
    }

    private sealed class Boundary : IM68kBusAccessTraceBatchBoundary
    {
        public bool BeforeInstruction() => true;
        public void AfterInstruction(long previous, long current) { }

        public bool TryBeginBusAccessTraceBatch(M68kCpuState state, long target, out long batchTarget)
        {
            batchTarget = target;
            return true;
        }

        public void AfterBusAccessTraceBatch(long previous, long current, int count) { }
    }

    private sealed class MovemBus : SparseRecordingBus, IM68kCodeReader, IM68kJitBus, IM68kJitFastMemoryBus
    {
        internal const uint Code = 0x00500000;
        internal const uint Source = 0x00600000;
        private readonly int _rangeMode;
        private readonly byte[] _data = new byte[12];

        internal MovemBus(int rangeMode)
        {
            _rangeMode = rangeMode;
            BinaryPrimitives.WriteUInt32BigEndian(_data.AsSpan(0, 4), 0x12345678);
            BinaryPrimitives.WriteUInt32BigEndian(_data.AsSpan(4, 4), 0x9ABCDEF0);
            BinaryPrimitives.WriteUInt32BigEndian(_data.AsSpan(8, 4), 0x6800);
            for (int i = 0; i < _data.Length; i++)
                Initialize(Source + (uint)i, _data[i], 1);
        }

        public int WholeRangeProbes { get; private set; }
        public int ScalarReads { get; private set; }
        public event Action<uint, int>? JitCodeRangeWritten { add { } remove { } }
        public bool IsJitCodeAddress(uint address, int count, M68kBusAccessKind kind)
            => address >= Code && (ulong)address + (uint)count <= Code + 0x100;
        public bool IsJitReadOnlyCodeAddress(uint address, int count, M68kBusAccessKind kind) => false;
        public ushort ReadHostWord(uint address) => (ushort)Peek(address, 2);
        public ushort ReadJitCodeWord(uint address) => ReadHostWord(address);
        public uint GetJitCodePageGeneration(uint address) => 0;
        public bool JitCodeRangeGenerationMatches(uint address, int count, uint first, uint last)
            => first == 0 && last == 0;
        public bool TryCaptureJitCodeSnapshot(uint address, int count, out M68kJitCodeSnapshot snapshot)
        {
            snapshot = default;
            return false;
        }

        public bool TryReadJitZeroWaitMemory(uint address, M68kOperandSize size, out uint value)
        {
            value = 0;
            if (size != M68kOperandSize.Long || address < Source || address > Source + 8)
                return false;
            ScalarReads++;
            value = BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan((int)(address - Source), 4));
            return true;
        }

        public bool TryWriteJitZeroWaitMemory(uint address, uint value, M68kOperandSize size) => false;

        public bool TryGetJitZeroWaitReadMemory(uint address, int count, out byte[] memory, out int offset,
            out M68kJitMemoryKind kind)
        {
            memory = _data;
            offset = _rangeMode == 2 ? 1 : 0;
            kind = M68kJitMemoryKind.FastRam;
            if (address != Source || count != _data.Length)
                return false;
            WholeRangeProbes++;
            return _rangeMode != 1;
        }

        public bool TryGetJitZeroWaitWriteMemory(uint address, int count, out byte[] memory, out int offset,
            out M68kJitMemoryKind kind)
        {
            memory = [];
            offset = 0;
            kind = M68kJitMemoryKind.FastRam;
            return false;
        }

        public void CompleteJitZeroWaitWrite(uint address, int count) { }
    }
}
