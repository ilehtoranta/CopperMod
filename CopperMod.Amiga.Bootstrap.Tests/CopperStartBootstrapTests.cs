using System.Buffers.Binary;

namespace CopperMod.Amiga.Tests;

public sealed class CopperStartBootstrapTests
{
    private static AmigaBus Request(out M68kCpuState state)
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x780, 0xFF00); bus.WriteLong(0x782, 0x43534731);
        bus.WriteWord(0x786, 0x4E75); bus.WriteLong(0x7FFF0, 0xF80080);
        bus.WriteLong(0x760, 0x43534231); bus.WriteLong(0x764, 1);
        bus.WriteLong(0x768, 0x12345678); bus.WriteLong(0x76C, 1);
        state = new M68kCpuState { ProgramCounter = 0x786, LastInstructionProgramCounter = 0x780,
            StatusRegister = 0x2700 };
        state.A[0] = 0x760; state.A[7] = 0x7FFF0; state.D[0] = 0x43534231; state.D[1] = 1;
        return bus;
    }

    [Fact]
    public void HandlerOnlyWritesReplyAndResultThenResetsWithHardware()
    {
        var bus = Request(out var state); var before = bus.ChipRam.ToArray();
        Assert.True(bus.HasHostGateway(0x780));
        Assert.True(bus.TryInvokeHostGateway(0x780, 0x43534731, state));
        Assert.Equal(0x43504D31u, state.D[0]);
        Assert.Equal(1u, state.D[1]); Assert.Equal(0x760u, state.A[0]); Assert.Equal(0x7FFF0u, state.A[7]);
        Assert.Equal(0x786u, state.ProgramCounter); Assert.Equal((ushort)0x2700, state.StatusRegister);
        Assert.Equal(0x12345678u ^ 0x43504D31u ^ 1u, bus.ReadLong(0x778));
        Assert.True(before.AsSpan(0,0x770).SequenceEqual(bus.ChipRam.AsSpan(0,0x770)));
        Assert.True(before.AsSpan(0x780).SequenceEqual(bus.ChipRam.AsSpan(0x780)));
        Assert.True(bus.CopperStartBootstrapReady); Assert.Equal(1,bus.CopperStartBootstrapCalls);
        bus.ResetExternalDevices(0);
        Assert.False(bus.CopperStartBootstrapReady); Assert.Equal(0,bus.CopperStartBootstrapCalls);
        Assert.True(bus.HasHostGateway(0x780));
        bus.CopperStartBootstrapEnabled = false;
        Assert.False(bus.HasHostGateway(0x780));
        Assert.False(bus.TryInvokeHostGateway(0x780,0x43534731,state));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void MalformedContextsCannotInitializeSession(int kind)
    {
        var bus = Request(out var state);
        switch(kind)
        {
            case 0: state.StatusRegister = 0; break;
            case 1: state.D[1] = 2; break;
            case 2: state.A[0] = 0xFFFFFFFF; break;
            case 3: state.A[7] = 0x7FFFF; break;
            case 4: bus.WriteLong(0x7FFF0,0x1000); break;
            case 5: bus.WriteLong(0x764,2); break;
            case 6: state.LastInstructionProgramCounter = 0x782; break;
        }
        var before = bus.ChipRam.ToArray();
        Assert.True(bus.TryInvokeHostGateway(0x780,0x43534731,state));
        Assert.Equal(0u,state.D[0]); Assert.False(bus.CopperStartBootstrapReady);
        Assert.Equal(before,bus.ChipRam);
    }

    [Theory]
    [InlineData((int)M68kBackendKind.AccurateM68000, true)]
    [InlineData((int)M68kBackendKind.AccurateM68000, false)]
    [InlineData((int)M68kBackendKind.JitM68000, true)]
    [InlineData((int)M68kBackendKind.JitM68000, false)]
    [InlineData((int)M68kBackendKind.AccurateM68020, true)]
    [InlineData((int)M68kBackendKind.AccurateM68020, false)]
    [InlineData((int)M68kBackendKind.AccurateM68040, true)]
    [InlineData((int)M68kBackendKind.AccurateM68040, false)]
    public void ActualCpuTrapReturnsToRomAndPreservesTheCallStack(int backend, bool enabled)
    {
        var bus = Request(out _);
        bus.CopperStartBootstrapEnabled = enabled;
        bus.WriteLong(0x2C,0x790);
        byte[] fallback = [0x70,0,0x2F,0x7C,0,0,7,0x86,0,2,0x4E,0x73];
        fallback.CopyTo(bus.ChipRam,0x790);
        byte[] code = [0x4E,0xB9,0,0,7,0x80, // JSR $780
            0x24,0x39,0,0,7,0x70, // MOVE.L $770,D2
            0x26,0x39,0,0,7,0x78, // MOVE.L $778,D3
            0x4E,0x72,0x27,0]; // STOP #$2700
        var rom = new byte[256]; code.CopyTo(rom,0);
        bus.MapReadOnlyMemory(0xF80000,rom);
        using var cpu = AmigaM68kCoreFactory.Default.Create((M68kBackendKind)backend,bus);
        cpu.Reset(0xF80000,0x7FFF0);
        cpu.State.D[0] = 0x43534231; cpu.State.D[1] = 1; cpu.State.A[0] = 0x760;
        for(var i=0;i<100 && !cpu.State.Stopped && !cpu.State.Halted;i++) cpu.ExecuteInstruction();
        Assert.True(cpu.State.Stopped, $"PC={cpu.State.ProgramCounter:X8} halted={cpu.State.Halted} vector={cpu.State.LastExceptionVector} calls={bus.CopperStartBootstrapCalls} D0={cpu.State.D[0]:X8}"); Assert.False(cpu.State.Halted);
        Assert.Equal(enabled ? 0x43504D31u : 0u,cpu.State.D[0]);
        Assert.Equal(enabled ? 0x43504D31u : 0u,cpu.State.D[2]);
        Assert.Equal(enabled ? 0x12345678u ^ 0x43504D31u ^ 1u : 0u,cpu.State.D[3]); Assert.Equal(0x7FFF0u,cpu.State.A[7]);
        Assert.Equal(enabled,bus.CopperStartBootstrapReady); Assert.Equal(enabled ? 1 : 0,bus.CopperStartBootstrapCalls);
        if (!enabled) Assert.Equal(11,cpu.State.LastExceptionVector);
    }
}
