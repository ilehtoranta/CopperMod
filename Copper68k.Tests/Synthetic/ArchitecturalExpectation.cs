namespace Copper68k.Tests.Synthetic;

internal sealed class ArchitecturalExpectation
{
    public uint[] D { get; init; } = new uint[8];
    public uint[] A { get; init; } = new uint[8];
    public Dictionary<uint, byte> Memory { get; init; } = [];
    public Dictionary<uint, byte> MemoryMasks { get; } = [];
    public HashSet<uint> ForbiddenOperandReads { get; } = [];
    public int? ExpectedByteOperandAccesses { get; set; }
    public HashSet<uint> OperandAccessAddresses { get; } = [];
    public List<(bool Write, int Width)>? ExpectedOperandTransfers { get; set; }
    public ushort Sr { get; set; }
    public ushort DefinedSrMask { get; set; } = 0xffff;
    public uint Pc { get; set; }
    public bool Stopped { get; set; }
    public bool Halted { get; set; }
    public int? ExceptionVector { get; set; }
    public uint? InactiveStackPointer { get; set; }
    public uint? MasterStackPointer { get; set; }
    public Dictionary<string, (Func<Copper68k.M68kCpuState, uint> Read, uint Value)> ControlChecks { get; } = [];
    public int? ExpectedDeviceResets { get; set; }

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
        var master = machine.Model.FullIndex && machine.Model.Id != "68060" && (Sr & 0x3000) == 0x3000;
        if (supervisor && !master && state.SupervisorStackPointer != A[7]) return $"Active SSP expected {A[7]:X8}, actual {state.SupervisorStackPointer:X8}";
        if (master && state.MasterStackPointer != A[7]) return $"Active MSP expected {A[7]:X8}, actual {state.MasterStackPointer:X8}";
        if (!supervisor && state.UserStackPointer != A[7]) return $"Active USP expected {A[7]:X8}, actual {state.UserStackPointer:X8}";
        if (InactiveStackPointer.HasValue && InactiveStackPointer != (supervisor ? state.UserStackPointer : state.SupervisorStackPointer)) return "Inactive stack pointer changed";
        if (MasterStackPointer.HasValue && MasterStackPointer != state.MasterStackPointer) return "Master stack pointer changed";
        foreach (var (name, check) in ControlChecks)
            if (check.Read(state) != check.Value) return $"{name} expected {check.Value:X8}, actual {check.Read(state):X8}";
        if (ExpectedDeviceResets.HasValue && ExpectedDeviceResets != machine.Bus.DeviceResets) return "RESET device notification count differs";
        if (ExpectedOperandTransfers != null)
        {
            var transfers = machine.Bus.Accesses.Where(a => a.Kind is Copper68k.M68kBusAccessKind.CpuDataRead or Copper68k.M68kBusAccessKind.CpuDataWrite)
                .Where(a => OperandAccessAddresses.Contains(a.Address)).Select(a => (a.Write, a.Width));
            if (!transfers.SequenceEqual(ExpectedOperandTransfers)) return "Operand transfer order/width/count differs";
        }
        if (ForbiddenOperandReads.Count != 0 || ExpectedByteOperandAccesses.HasValue)
        {
            var operandAccesses = machine.Bus.Accesses.Where(a => a.Kind is Copper68k.M68kBusAccessKind.CpuDataRead or Copper68k.M68kBusAccessKind.CpuDataWrite).ToArray();
            if (operandAccesses.Any(a => !a.Write && Enumerable.Range(0, a.Width).Any(offset => ForbiddenOperandReads.Contains(machine.Model.Physical(unchecked(a.Address + (uint)offset))))))
                return "Unavailable instruction read its operand before trapping";
            if (ExpectedByteOperandAccesses.HasValue && (operandAccesses.Length != ExpectedByteOperandAccesses || operandAccesses.Any(a => a.Width != 1)))
                return "PACK/UNPK must transfer the specified bytes separately";
        }
        foreach (var address in Memory.Keys.Concat(machine.Bus.Memory.Keys).Distinct())
            if ((Memory.GetValueOrDefault(address) & MemoryMasks.GetValueOrDefault(address, (byte)255)) != (machine.Bus.Peek(address) & MemoryMasks.GetValueOrDefault(address, (byte)255)))
                return $"Memory {address:X8}: expected {Memory.GetValueOrDefault(address):X2}, actual {machine.Bus.Peek(address):X2}";
        return null;
    }
}
