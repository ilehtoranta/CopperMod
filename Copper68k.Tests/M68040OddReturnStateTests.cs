using Copper68k.Tests.Synthetic;

namespace Copper68k.Tests;

public sealed class M68040OddReturnStateTests
{
    [Theory]
    [InlineData("accurate")]
    [InlineData("v1")]
    [InlineData("v2")]
    public void WarmDispatchFallsBackForTheOddReturnBeforeTargetAccess(string engine)
    {
        var bus = new SparseRecordingBus();
        var executionBus = new ImmutableCodeBus(bus);
        bus.Initialize(0x2000, 0x60fe, 2); // warm a compiled self branch
        bus.Initialize(0x1000, 0x4e73, 2); bus.Initialize(0x1002, 0x4e71, 2);
        bus.Initialize(0x9000, 0x4e73, 2); bus.Initialize(0x9002, 0x4e71, 2);
        bus.Initialize(0x9190, 0x4e73, 2); bus.Initialize(0x9192, 0x4e71, 2);
        bus.Initialize(12, 0x9190, 4); bus.Initialize(55 * 4, 0x9000, 4);
        IM68kCore core = engine == "accurate" ? M68kCoreFactory.Default.Create(M68kCpuModel.M68040, executionBus)
            : M68kJitCore.CreateM68040ForTesting(executionBus, engine == "v2");
        using var disposable = core as IDisposable;
        core.Reset(0x2000, 0x4700); core.State.CacheControlRegister = 0x8000;
        for (var n = 0; n < (engine == "accurate" ? 1 : 300); n++) Execute(core);
        if (core is M68kJitCore warmed)
            Assert.True(warmed.Counters.TraceHits + warmed.Counters.V2TraceHits > 0, "Require actual compiled warm dispatch before testing RTE fallback");
        foreach (var form in new[] { "format0", "format2", "format3", "CM", "CP55" })
        foreach (var master in new[] { false, true })
        foreach (var result in new[] { "user", "ISP", "MSP" })
        foreach (var trace in new ushort[] { 0, 0x8000, 0x4000 })
        foreach (var ccr in new[] { 0, 31 })
        {
            var frame = master ? 0x7400u : 0x4700u;
            var saved = (ushort)((result == "ISP" ? 0x2000 : result == "MSP" ? 0x3000 : 0) | trace | (ccr ^ 31));
            var initial = (ushort)((master ? 0x3000 : 0x2000) | ccr);
            var format = form == "format0" ? 0 : form == "format2" ? 2 : form == "format3" ? 3 : 7;
            var size = format == 0 ? 8u : format == 7 ? 60u : 12u;
            core.State.SetUserStackPointer(0x7800); core.State.SetInterruptStackPointer(0x4700); core.State.SetMasterStackPointer(0x7400);
            core.State.StatusRegister = initial; core.State.ProgramCounter = 0x1000;
            bus.Initialize(frame, saved, 2); bus.Initialize(frame + 2, 0x6001, 4);
            bus.Initialize(frame + 6, (uint)(format << 12) | 8, 2);
            bus.Initialize(frame + 8, 0x4200, 4); bus.Initialize(frame + 12, form == "CP55" ? 0x8005u : form == "CM" ? 0x1005u : 0x0105u, 2);
            if (form == "CP55") core.State.M68040PendingFpuExceptions.Begin(3, 55, 0x6001);
            var d = (uint[])core.State.D.Clone(); var a = core.State.A.Take(7).ToArray();
            bus.Accesses.Clear();
            var fallbacks = core is M68kJitCore jit ? jit.Counters.FallbackInstructions : 0;
            Execute(core);
            if (form == "CP55")
            {
                Assert.Equal(0x9000u, core.State.ProgramCounter); Assert.Equal(55, core.State.LastExceptionVector);
                Assert.Null(core.State.M68040PendingFpuExceptions.Find(3));
                Execute(core); // converted handler return now raises address error
            }
            if (core is M68kJitCore fallback)
                Assert.True(fallback.Counters.FallbackInstructions > fallbacks, "RTE must use the interpreter fallback under warmed dispatch");
            var expectedSr = (ushort)((saved | 0x2000) & ~0xc000);
            var bank = result == "MSP" ? "MSP" : "ISP";
            var sp = (bank == "MSP" ? 0x7400u : 0x4700u) + (bank == (master ? "MSP" : "ISP") ? size : 0) - 12;
            var stacked = form == "CP55" ? expectedSr : initial;
            Assert.Equal(0x9190u, core.State.ProgramCounter); Assert.Equal(3, core.State.LastExceptionVector);
            Assert.Equal(expectedSr, core.State.StatusRegister); Assert.Equal(sp, core.State.A[7]);
            Assert.Equal((uint)stacked, bus.Peek(sp, 2));
            Assert.Equal(form == "CP55" ? 0x9000u : 0x1000u, bus.Peek(sp + 2, 4));
            Assert.Equal(0x200cu, bus.Peek(sp + 6, 2)); Assert.Equal(0x6000u, bus.Peek(sp + 8, 4));
            Assert.Equal(d, core.State.D); Assert.Equal(a, core.State.A.Take(7).ToArray());
            Assert.DoesNotContain(bus.Accesses, access => access.Address is 0x6001 or 0x4200);
            Assert.Equal(0x7800u, core.State.UserStackPointer);
        }
    }

    private static void Execute(IM68kCore core)
    {
        if (core is M68kJitCore jit) Assert.Equal(1, jit.ExecuteInstructions(1, core.State.Cycles + 1000, new Boundary()));
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
    private sealed class ImmutableCodeBus(SparseRecordingBus memory) : IM68kBus, IM68kJitBus
    {
        public event Action<uint, int>? JitCodeRangeWritten { add { } remove { } }
        public bool IsJitCodeAddress(uint address, int count, M68kBusAccessKind kind) =>
            address >= 0x1000 && address + count <= 0x1004 || address >= 0x2000 && address + count <= 0x2004 ||
            address >= 0x9000 && address + count <= 0x9004 || address >= 0x9190 && address + count <= 0x9194;
        public bool IsJitReadOnlyCodeAddress(uint address, int count, M68kBusAccessKind kind) => IsJitCodeAddress(address, count, kind);
        public ushort ReadJitCodeWord(uint address) => (ushort)memory.Peek(address, 2);
        public uint GetJitCodePageGeneration(uint address) => 0;
        public bool JitCodeRangeGenerationMatches(uint address, int count, uint first, uint last) => first == 0 && last == 0;
        public bool TryCaptureJitCodeSnapshot(uint address, int count, out M68kJitCodeSnapshot snapshot) { snapshot = default; return false; }
        public void ResetExternalDevices(long cycle) => memory.ResetExternalDevices(cycle);
        public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind kind) => memory.ReadByte(address, ref cycle, kind);
        public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind kind) => memory.ReadWord(address, ref cycle, kind);
        public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind kind) => memory.ReadLong(address, ref cycle, kind);
        public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind kind) => memory.WriteByte(address, value, ref cycle, kind);
        public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind kind) => memory.WriteWord(address, value, ref cycle, kind);
        public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind kind) => memory.WriteLong(address, value, ref cycle, kind);
    }
}
