using Copper68k.Tests.Synthetic;

namespace Copper68k.Tests;

public sealed class M68040MovemContinuationStateTests
{
    [Theory]
    [InlineData("reset")]
    [InlineData("interrupt")]
    [InlineData("once")]
    public void SavedAddressHasOneOwnerAndDefinedLifetime(string scenario)
    {
        var bus = Fixture();
        var core = M68kCoreFactory.Default.Create(M68kCpuModel.M68040, bus);
        Initialize(core);
        core.ExecuteInstruction();
        Assert.Equal(0x1000u, core.State.ProgramCounter);
        Assert.True(((M68kAdvancedTimingInterpreter)core).HasPendingM68040MovemContinuation);
        if (scenario == "reset")
        {
            core.Reset(0x1000, 0x473c);
            core.State.A[0] = 0x4200;
            core.ExecuteInstruction();
            Assert.Equal(0xdeadbeefu, core.State.D[1]);
        }
        else
        {
            if (scenario == "interrupt")
            {
                core.RequestInterrupt(3, 28 * 4);
                core.ExecuteInstruction(); // handler NOP must not consume continuation
                Assert.True(((M68kAdvancedTimingInterpreter)core).HasPendingM68040MovemContinuation);
                core.ExecuteInstruction(); // handler RTE
                Assert.Equal(0x1000u, core.State.ProgramCounter);
            }
            core.ExecuteInstruction();
            Assert.Equal(0xcafebabeu, core.State.D[1]);
            Assert.Equal(0x1006u, core.State.ProgramCounter);
            Assert.False(((M68kAdvancedTimingInterpreter)core).HasPendingM68040MovemContinuation);
            core.State.ProgramCounter = 0x1000;
            core.ExecuteInstruction(); // ordinary address calculation resumes
            Assert.Equal(0xdeadbeefu, core.State.D[1]);
        }
        Assert.False(((M68kAdvancedTimingInterpreter)core).HasPendingM68040MovemContinuation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmCompiledMovemCannotBypassTheSavedAddress(bool v2)
    {
        // Exercise a warmed trace in both compiler versions.
        var bus = Fixture();
        using var core = M68kJitCore.CreateM68040ForTesting(new ContinuationJitBus(bus), v2);
        core.Reset(0x1000, 0x4700); core.State.A[0] = 0x4200;
        core.State.CacheControlRegister = 0x8000;
        for (var n = 0; n < 300; n++) ExecuteOne(core);
        core.State.ProgramCounter = 0x1000;
        var warmedHits = Hits(core);
        ExecuteOne(core);
        Assert.True(Hits(core) > warmedHits, $"The MOVEM entry must execute through a warmed compiled trace: rejected={core.Counters.V2RejectedCandidates}, decode={core.Counters.V2RejectedDecode}, ea={core.Counters.V2RejectedUnsupportedEa}, operation={core.Counters.V2RejectedUnsupportedOperation}, chip={core.Counters.V2RejectedChipRam}, empty={core.Counters.V2RejectedEmpty}, fallback={core.Counters.FallbackInstructions}");
        Assert.Equal(0xdeadbeefu, core.State.D[1]);
        core.State.ProgramCounter = 0x9000;
        core.State.SetActiveStackPointer(0x4700);
        ExecuteOne(core); // RTE into the already warmed MOVEM entry
        Assert.Equal(0x1000u, core.State.ProgramCounter);
        var before = core.Counters;
        bus.Accesses.Clear();
        ExecuteOne(core);
        Assert.Equal(0xcafebabeu, core.State.D[1]);
        Assert.Equal(0x1006u, core.State.ProgramCounter);
        Assert.Equal(before.TraceHits + before.V2TraceHits, Hits(core));
        Assert.Equal(before.FallbackInstructions + 1, core.Counters.FallbackInstructions);
        Assert.DoesNotContain(bus.Accesses, x => !x.Write && x.Kind == M68kBusAccessKind.CpuDataRead && x.Address == (0x4210u));
        core.State.ProgramCounter = 0x1000;
        ExecuteOne(core);
        Assert.Equal(0xdeadbeefu, core.State.D[1]);
        Assert.True(Hits(core) > before.TraceHits + before.V2TraceHits);
    }

    private static void ExecuteOne(M68kJitCore core)
        => Assert.Equal(1, core.ExecuteInstructions(1, core.State.Cycles + 1000, new ContinuationBoundary()));

    private sealed class ContinuationBoundary : IM68kBusAccessTraceBatchBoundary
    {
        public bool BeforeInstruction() => true;
        public void AfterInstruction(long previous, long current) { }
        public bool TryBeginBusAccessTraceBatch(M68kCpuState state, long target, out long batchTarget)
        { batchTarget = target; return true; }
        public void AfterBusAccessTraceBatch(long previous, long current, int count) { }
    }

    private static long Hits(M68kJitCore core) => core.Counters.TraceHits + core.Counters.V2TraceHits;
    private static void Initialize(IM68kCore core)
    {
        core.Reset(0x9000, 0x4700); core.State.A[0] = 0x4200;
    }
    private static SparseRecordingBus Fixture()
    {
        var bus = new SparseRecordingBus();
        bus.Initialize(0x1000, 0x4cf0, 2); // MOVEM.L (d8,A0,D0.W),D1
        bus.Initialize(0x1002, 2, 2); bus.Initialize(0x1004, 0x0010, 2);
        bus.Initialize(0x1006, 0x4e71, 2); bus.Initialize(0x1008, 0x60f6, 2);
        bus.Initialize(0x100a, 0x4e71, 2);
        bus.Initialize(0x4000, 0xcafebabe, 4); bus.Initialize(0x4210, 0xdeadbeef, 4);
        bus.Initialize(0x9000, 0x4e73, 2); bus.Initialize(0x9002, 0x4e71, 2);
        bus.Initialize(28 * 4, 0x91c0, 4);
        bus.Initialize(0x91c0, 0x4e71, 2); bus.Initialize(0x91c2, 0x4e73, 2);
        bus.Initialize(0x4700, 0x2000, 2); bus.Initialize(0x4702, 0x1000, 4);
        bus.Initialize(0x4706, 0x7008, 2); bus.Initialize(0x4708, 0x4000, 4); bus.Initialize(0x470c, 0x1005, 2);
        return bus;
    }

    private sealed class ContinuationJitBus(SparseRecordingBus bus) : IM68kBus, IM68kJitBus
    {
        public void ResetExternalDevices(long cycle) => bus.ResetExternalDevices(cycle);
        public event Action<uint, int>? JitCodeRangeWritten;
        public bool IsJitCodeAddress(uint address, int count, M68kBusAccessKind kind) => address >= 0x1000 && address + count <= 0x100c;
        public bool IsJitReadOnlyCodeAddress(uint address, int count, M68kBusAccessKind kind) => false;
        public ushort ReadJitCodeWord(uint address) => (ushort)bus.Peek(address, 2);
        public uint GetJitCodePageGeneration(uint address) => 0;
        public bool JitCodeRangeGenerationMatches(uint address, int count, uint first, uint last) => first == 0 && last == 0;
        public bool TryCaptureJitCodeSnapshot(uint address, int count, out M68kJitCodeSnapshot snapshot) { snapshot = default; return false; }
        public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind kind) => bus.ReadByte(address, ref cycle, kind);
        public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind kind) => bus.ReadWord(address, ref cycle, kind);
        public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind kind) => bus.ReadLong(address, ref cycle, kind);
        public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind kind)
        { bus.WriteByte(address, value, ref cycle, kind); JitCodeRangeWritten?.Invoke(address, 1); }
        public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind kind)
        { bus.WriteWord(address, value, ref cycle, kind); JitCodeRangeWritten?.Invoke(address, 2); }
        public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind kind)
        { bus.WriteLong(address, value, ref cycle, kind); JitCodeRangeWritten?.Invoke(address, 4); }
    }
}
