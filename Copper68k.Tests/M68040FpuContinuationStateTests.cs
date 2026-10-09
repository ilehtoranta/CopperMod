using Copper68k.Tests.Synthetic;
using CopperFloat;

namespace Copper68k.Tests;

public sealed class M68040FpuContinuationStateTests
{
    [Theory]
    [InlineData("accurate", false, "resume")]
    [InlineData("accurate", true, "resume")]
    [InlineData("accurate", false, "nested")]
    [InlineData("accurate", true, "nested")]
    [InlineData("accurate", false, "reset")]
    [InlineData("accurate", true, "reset")]
    [InlineData("accurate", false, "redirect")]
    [InlineData("accurate", true, "redirect")]
    [InlineData("v1", false, "resume")]
    [InlineData("v1", true, "resume")]
    [InlineData("v2", false, "resume")]
    [InlineData("v2", true, "resume")]
    public void DeliveryRetainsItsVectorUntilCompletion(string engine, bool post, string scenario)
    {
        var bus = new DeliveryBus();
        var memory = bus.Memory;
        memory.Initialize(0x1000, post ? 0xf210u : 0xf200u, 2);
        memory.Initialize(0x1002, post ? 0x6880u : 0x040eu, 2); // FMOVE.X FP1,(A0) / FSIN.X FP1,FP0
        memory.Initialize(0x1004, 0x4e71, 2); memory.Initialize(0x1006, 0x60f8, 2);
        memory.Initialize(0x9000, 0x4e73, 2); memory.Initialize(0x9002, 0x4e71, 2);
        memory.Initialize(11 * 4, 0x6800, 4); memory.Initialize(55 * 4, 0x6800, 4);
        memory.Initialize(2 * 4, 0x6900, 4); memory.Initialize(0x6800, 0x4e73, 2);
        IM68kCore core = engine == "accurate" ? M68kCoreFactory.Default.Create(M68kCpuModel.M68040, bus)
            : M68kJitCore.CreateM68040ForTesting(bus, engine == "v2");
        using var disposable = core as IDisposable;
        core.Reset(0x1000, 0x8000); core.State.A[0] = 0x4200; core.State.CacheControlRegister = 0x8000;
        core.State.M68040Fpu.FP[1] = ExtF80.FromBits(0x3fff, 0x8000000000000000);
        var format = post ? 3 : 2; var vector = post ? 55 : 11;
        for (var n = 0; n < (engine == "accurate" ? 1 : 300); n++)
        { core.State.ProgramCounter = 0x1000; core.State.SetActiveStackPointer(0x8000); ExecuteOne(core); }
        Assert.Null(core.State.M68040PendingFpuExceptions.Find(format, 0x1004));
        core.State.ProgramCounter = 0x1000; core.State.SetActiveStackPointer(0x8000);
        var hits = core is M68kJitCore jit ? jit.Counters.TraceHits + jit.Counters.V2TraceHits : 0;
        ExecuteOne(core);
        if (core is M68kJitCore warmed)
            Assert.True(warmed.Counters.TraceHits + warmed.Counters.V2TraceHits > hits, $"Require an actual warmed compiled entry: compiled={warmed.Counters.CompiledTraces}, rejected={warmed.Counters.V2RejectedCandidates}, operation={warmed.Counters.V2RejectedUnsupportedOperation}, ea={warmed.Counters.V2RejectedUnsupportedEa}, hits={warmed.Counters.TraceHits}/{warmed.Counters.V2TraceHits}, fallback={warmed.Counters.FallbackInstructions}, exits={warmed.Counters.V2SideExits}");
        core.State.ProgramCounter = 0x1000; core.State.SetActiveStackPointer(0x8000);
        // Unsupported extended data creates the documented post-instruction vector 55.
        if (post) core.State.M68040Fpu.FP[1] = ExtF80.FromBits(0, 1);
        bus.FaultVectorAddress = (uint)vector * 4;
        ExecuteOne(core);
        var pending = core.State.M68040PendingFpuExceptions.Find(format, 0x1004);
        Assert.NotNull(pending); Assert.Equal(vector, pending.Vector);
        Assert.Equal(2, core.State.LastExceptionVector);
        // This injected internal fault uses the existing approximate entry. It
        // does not qualify hardware format-7 generation or enabled MMU behavior.
        if (scenario == "reset")
        {
            core.Reset(0x1004, 0x8000);
            Assert.Null(core.State.M68040PendingFpuExceptions.Find(format, 0x1004));
            return;
        }
        if (scenario == "nested")
        {
            core.State.ProgramCounter = 0x1000; core.State.SetActiveStackPointer(0x7a00);
            ExecuteOne(core); // Equal-valued nested delivery completes independently.
            Assert.Same(pending, core.State.M68040PendingFpuExceptions.Find(format, 0x1004));
        }
        // FPU context/register changes cannot change integer delivery ownership.
        core.State.M68040Fpu.Reset(); core.State.M68040Fpu.Fpsr = 0x8000;
        Assert.Same(pending, core.State.M68040PendingFpuExceptions.Find(format, 0x1004));
        var returnPc = scenario == "redirect" ? 0x6000u : 0x1004u;
        memory.Initialize(0x8000, 0x2000, 2); memory.Initialize(0x8002, returnPc, 4);
        memory.Initialize(0x8006, 0x7008, 2); memory.Initialize(0x8008, 0x4200, 4);
        memory.Initialize(0x800c, post ? 0x8005u : 0x4005u, 2);
        core.State.ProgramCounter = 0x9000; core.State.SetActiveStackPointer(0x8000);
        ExecuteOne(core);
        Assert.Equal(vector, core.State.LastExceptionVector); Assert.Equal(0x6800u, core.State.ProgramCounter);
        Assert.Equal(0x8030u, core.State.A[7]);
        Assert.Equal((uint)(format << 12 | vector * 4), memory.Peek(0x8036, 2));
        Assert.Equal(returnPc, memory.Peek(0x8032, 4));
        Assert.Equal(0x4200u, memory.Peek(0x8038, 4));
        Assert.Null(core.State.M68040PendingFpuExceptions.Find(format, 0x1004));
    }

    [Fact]
    public void ForeignCpFrameWithoutDeliveryStateIsAnExplicitGap()
    {
        var bus = new SparseRecordingBus();
        bus.Initialize(0x1000, 0x4e73, 2); bus.Initialize(0x8000, 0x2000, 2);
        bus.Initialize(0x8002, 0x6000, 4); bus.Initialize(0x8006, 0x7008, 2);
        bus.Initialize(0x8008, 0x4200, 4); bus.Initialize(0x800c, 0x8005, 2);
        var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68040, bus);
        cpu.Reset(0x1000, 0x8000); var sequence = cpu.State.ExceptionSequence;
        Assert.Throws<UnsupportedM68040InstructionException>(() => cpu.ExecuteInstruction());
        Assert.Equal(sequence, cpu.State.ExceptionSequence); Assert.Equal(0x8000u, cpu.State.A[7]);
        Assert.DoesNotContain(bus.Accesses, a => a.Write);
    }

    [Theory]
    [InlineData("accurate")]
    [InlineData("v1")]
    [InlineData("v2")]
    public void PostInstructionReturnDoesNotRepeatACompletedOperandStore(string engine)
    {
        var bus = new DeliveryBus(); var memory = bus.Memory;
        memory.Initialize(0x1000, 0xf210, 2); memory.Initialize(0x1002, 0x6480, 2); // FMOVE.S FP1,(A0)
        memory.Initialize(0x1004, 0x4e71, 2); memory.Initialize(0x1006, 0x60f8, 2);
        memory.Initialize(0x9000, 0x4e73, 2); memory.Initialize(0x9002, 0x4e71, 2);
        memory.Initialize(53 * 4, 0x6800, 4); memory.Initialize(2 * 4, 0x6900, 4);
        IM68kCore core = engine == "accurate" ? M68kCoreFactory.Default.Create(M68kCpuModel.M68040, bus)
            : M68kJitCore.CreateM68040ForTesting(bus, engine == "v2");
        using var disposable = core as IDisposable;
        core.Reset(0x1000, 0x8000); core.State.A[0] = 0x4200; core.State.CacheControlRegister = 0x8000;
        core.State.M68040Fpu.FP[1] = ExtF80.FromBits(0x3fff, 0x8000000000000000);
        for (var n = 0; n < (engine == "accurate" ? 1 : 300); n++)
        { core.State.ProgramCounter = 0x1000; ExecuteOne(core); }
        core.State.ProgramCounter = 0x1000;
        var hits = core is M68kJitCore jit ? jit.Counters.TraceHits + jit.Counters.V2TraceHits : 0;
        ExecuteOne(core);
        if (core is M68kJitCore warmed)
            Assert.True(warmed.Counters.TraceHits + warmed.Counters.V2TraceHits > hits);
        core.State.ProgramCounter = 0x1000;
        core.State.M68040Fpu.FP[1] = ExtF80.FromBits(0x7ffe, ulong.MaxValue);
        bus.FaultVectorAddress = 53 * 4;
        memory.Accesses.Clear(); ExecuteOne(core);
        // Fixed IEEE format boundary: largest finite extended value overflows
        // single precision to positive infinity before the post-exception fetch.
        Assert.Equal(0x7f800000u, memory.Peek(0x4200, 4));
        Assert.Single(memory.Accesses.Where(a => a.Write && a.Address == 0x4200));
        Assert.Equal(53, core.State.M68040PendingFpuExceptions.Find(3, 0x1004)!.Vector);
        memory.Initialize(0x8000, 0x2000, 2); memory.Initialize(0x8002, 0x1004, 4);
        memory.Initialize(0x8006, 0x7008, 2); memory.Initialize(0x8008, 0x4200, 4);
        memory.Initialize(0x800c, 0x8005, 2);
        core.State.ProgramCounter = 0x9000; core.State.SetActiveStackPointer(0x8000);
        memory.Accesses.Clear(); ExecuteOne(core);
        Assert.Equal(53, core.State.LastExceptionVector); Assert.Equal(0x6800u, core.State.ProgramCounter);
        Assert.Equal(0x7f800000u, memory.Peek(0x4200, 4));
        Assert.DoesNotContain(memory.Accesses, a => a.Write && a.Address == 0x4200);
        Assert.DoesNotContain(memory.Accesses, a => a.Address == 0x1000 && a.Kind == M68kBusAccessKind.CpuInstructionFetch);
        Assert.Null(core.State.M68040PendingFpuExceptions.Find(3, 0x1004));
    }

    private static void ExecuteOne(IM68kCore core)
    {
        if (core is M68kJitCore jit) Assert.Equal(1, jit.ExecuteInstructions(1, jit.State.Cycles + 1000, new Boundary()));
        else core.ExecuteInstruction();
    }
    private sealed class Boundary : IM68kBusAccessTraceBatchBoundary, IM68kPureCpuTraceBatchBoundary
    {
        public bool BeforeInstruction() => true;
        public void AfterInstruction(long previous, long current) { }
        public bool TryBeginBusAccessTraceBatch(M68kCpuState state, long target, out long batchTarget)
        { batchTarget = target; return true; }
        public void AfterBusAccessTraceBatch(long previous, long current, int count) { }
        public bool TryBeginPureCpuTraceBatch(M68kCpuState state, long target, out long batchTarget)
        { batchTarget = target; return true; }
        public void AfterPureCpuTraceBatch(long previous, long current, int count) { }
    }
    private sealed class DeliveryBus : IM68kBus, IM68kJitBus
    {
        public SparseRecordingBus Memory { get; } = new();
        public uint? FaultVectorAddress { get; set; }
        public event Action<uint, int>? JitCodeRangeWritten;
        public bool IsJitCodeAddress(uint address, int count, M68kBusAccessKind kind) => address >= 0x1000 && address + count <= 0x1008;
        public bool IsJitReadOnlyCodeAddress(uint address, int count, M68kBusAccessKind kind) => false;
        public ushort ReadJitCodeWord(uint address) => (ushort)Memory.Peek(address, 2);
        public uint GetJitCodePageGeneration(uint address) => 0;
        public bool JitCodeRangeGenerationMatches(uint address, int count, uint first, uint last) => first == 0 && last == 0;
        public bool TryCaptureJitCodeSnapshot(uint address, int count, out M68kJitCodeSnapshot snapshot) { snapshot = default; return false; }
        public void ResetExternalDevices(long cycle) => Memory.ResetExternalDevices(cycle);
        public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind kind) => Memory.ReadByte(address, ref cycle, kind);
        public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind kind) => Memory.ReadWord(address, ref cycle, kind);
        public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind kind)
        {
            if (address == FaultVectorAddress)
            { FaultVectorAddress = null; throw new M68040MmuFaultException(new(address, kind, false, 0x400, 0x1004)); }
            return Memory.ReadLong(address, ref cycle, kind);
        }
        public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind kind)
        { Memory.WriteByte(address, value, ref cycle, kind); JitCodeRangeWritten?.Invoke(address, 1); }
        public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind kind)
        { Memory.WriteWord(address, value, ref cycle, kind); JitCodeRangeWritten?.Invoke(address, 2); }
        public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind kind)
        { Memory.WriteLong(address, value, ref cycle, kind); JitCodeRangeWritten?.Invoke(address, 4); }
    }
}
