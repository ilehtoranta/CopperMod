namespace Copper68k.Tests.Synthetic;

internal sealed class ArchitecturalExpectation
{
    public uint[] D { get; init; } = new uint[8];
    public uint[] A { get; init; } = new uint[8];
    public Dictionary<uint, byte> Memory { get; init; } = [];
    public ushort Sr { get; set; }
    public ushort DefinedSrMask { get; init; } = 0xffff;
    public uint Pc { get; set; }
    public bool Stopped { get; set; }
    public bool Halted { get; set; }
    public int? ExceptionVector { get; set; }
    public uint? InactiveStackPointer { get; init; }
    public uint? MasterStackPointer { get; init; }

    public static ArchitecturalExpectation Capture(SyntheticMachine machine) => new()
    {
        D = (uint[])machine.Core.State.D.Clone(), A = (uint[])machine.Core.State.A.Clone(),
        Memory = new(machine.Bus.Memory), Sr = machine.Core.State.StatusRegister, Pc = machine.Core.State.ProgramCounter,
        InactiveStackPointer = (machine.Core.State.StatusRegister & 0x2000) != 0 ? machine.Core.State.UserStackPointer : machine.Core.State.SupervisorStackPointer,
        MasterStackPointer = machine.Core.State.MasterStackPointer
    };
    public void Write(uint address, uint value, int width, ModelSpec model)
    {
        for (var i = 0; i < width; i++) Memory[model.Physical(unchecked(address + (uint)i))] = (byte)(value >> (8 * (width - 1 - i)));
    }
    public string? Verify(SyntheticMachine machine)
    {
        var state = machine.Core.State;
        if (state.ProgramCounter != Pc) return $"PC expected {Pc:X8}, actual {state.ProgramCounter:X8}";
        if ((state.StatusRegister & DefinedSrMask) != (Sr & DefinedSrMask)) return $"SR expected {Sr:X4}, actual {state.StatusRegister:X4}, mask={DefinedSrMask:X4}";
        if (state.Halted != Halted || state.Stopped != Stopped) return "Unexpected halted/stopped state";
        if (ExceptionVector.HasValue && state.LastExceptionVector != ExceptionVector) return $"Expected exception {ExceptionVector}, actual {state.LastExceptionVector}";
        for (var i = 0; i < 8; i++)
        {
            if (state.D[i] != D[i]) return $"D{i} expected {D[i]:X8}, actual {state.D[i]:X8}";
            if (state.A[i] != A[i]) return $"A{i} expected {A[i]:X8}, actual {state.A[i]:X8}";
        }
        var supervisor = (Sr & 0x2000) != 0;
        if (supervisor && state.SupervisorStackPointer != A[7]) return $"Active SSP expected {A[7]:X8}, actual {state.SupervisorStackPointer:X8}";
        if (!supervisor && state.UserStackPointer != A[7]) return $"Active USP expected {A[7]:X8}, actual {state.UserStackPointer:X8}";
        if (InactiveStackPointer.HasValue && InactiveStackPointer != (supervisor ? state.UserStackPointer : state.SupervisorStackPointer)) return "Inactive stack pointer changed";
        if (MasterStackPointer.HasValue && MasterStackPointer != state.MasterStackPointer) return "Master stack pointer changed";
        foreach (var address in Memory.Keys.Concat(machine.Bus.Memory.Keys).Distinct())
            if (Memory.GetValueOrDefault(address) != machine.Bus.Peek(address))
                return $"Memory {address:X8}: expected {Memory.GetValueOrDefault(address):X2}, actual {machine.Bus.Peek(address):X2}";
        return null;
    }
}
