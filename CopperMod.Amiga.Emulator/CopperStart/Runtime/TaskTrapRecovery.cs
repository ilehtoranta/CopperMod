using System;
using Copper68k;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.CopperStart.Runtime;

/// <summary>Decodes task-trap frames and recovers only recognized host probes.</summary>
internal sealed class TaskTrapRecovery
{
    private const int BusError = 2, AddressError = 3, IllegalInstruction = 4, PrivilegeViolation = 8, LineA = 10, LineF = 11;
    private readonly AmigaBus _bus;
    private readonly Func<uint, bool> _isZeroFilledInstructionTarget;
    private readonly Action<string> _invalidFrame;

    public TaskTrapRecovery(AmigaBus bus, Func<uint, bool> isZeroFilledInstructionTarget, Action<string> invalidFrame)
    { _bus = bus ?? throw new ArgumentNullException(nameof(bus)); _isZeroFilledInstructionTarget = isZeroFilledInstructionTarget ?? throw new ArgumentNullException(nameof(isZeroFilledInstructionTarget)); _invalidFrame = invalidFrame ?? throw new ArgumentNullException(nameof(invalidFrame)); }

    public void HandleDefault(M68kCpuState state)
    {
        var hasVector = TryReadVector(state.A[7], out var vector); var frame = state.A[7] + 4;
        var valid = hasVector ? TryReadFrameForVector(frame, vector, out var sr, out var pc, out var sp) : TryReadFrame(frame, out sr, out pc, out sp);
        if (!valid) { frame = state.A[7]; valid = hasVector ? TryReadFrameForVector(frame, vector, out sr, out pc, out sp) : TryReadFrame(frame, out sr, out pc, out sp); }
        if (!valid)
        {
            frame = state.A[7] + 4; sr = _bus.ReadWord(frame); pc = _bus.ReadLong(frame + 2); sp = frame + 6;
            valid = IsPlausibleStatus(sr) && IsPlausibleReturn(pc);
        }
        if (!valid) { _invalidFrame($"Task trap frame was not recognized at SP=0x{state.A[7]:X8}, vector={(hasVector ? vector.ToString() : "none")}, frame=0x{frame:X8}."); state.Halted = true; return; }
        if (!hasVector && TryReadFrameVector(frame, out vector)) hasVector = true;
        if (hasVector && TrySkipProbe(state, vector, sr, pc, out var next)) pc = next;
        state.SetActiveStackPointer(sp); state.StatusRegister = sr; state.ProgramCounter = pc;
    }

    public bool TryRecoverFromZeroPc(M68kCpuState state, bool hasSyntheticExec)
    {
        if (!hasSyntheticExec || state.ProgramCounter != 0 || !TryReadFrame(state.A[7], out var sr, out var pc, out var sp) || !TryReadFrameVector(state.A[7], out var vector) || !TrySkipProbe(state, vector, sr, pc, out var next)) return false;
        state.SetActiveStackPointer(sp); state.StatusRegister = sr; state.ProgramCounter = next; return true;
    }

    private bool TryReadVector(uint address, out uint vector)
    { vector = _bus.ReadLong(address); return IsSupportedVector(vector); }
    private static bool IsSupportedVector(uint vector) => vector < 256 && (vector is BusError or AddressError or IllegalInstruction or PrivilegeViolation or LineA or LineF || vector is >= 32 and < 48);
    private bool TrySkipProbe(M68kCpuState state, uint vector, ushort sr, uint pc, out uint next)
    {
        next = pc;
        if (vector is BusError or AddressError) return TryPopUserProbeReturn(state, sr, out next) || TrySkipDecodedProbe(state, pc, out next);
        if (vector == IllegalInstruction) return TryMatchWord(pc, 0x4AFC, out next);
        if (vector == PrivilegeViolation) return TryGetPrivilegeLength(pc, out next);
        if (vector == LineA) return TryMatchMaskedWord(pc, 0xF000, 0xA000, out next);
        if (vector != LineF || !IsReadable(pc, 2) || !M68kDecoder.TryDecode(new CodeReader(_bus), pc, out var instruction, out _, M68kJitCpuModel.M68040) || instruction.Operation != M68kJitOperation.M68040Fpu || instruction.Variant != (int)M68040FpuJitKind.LineFTrap) return false;
        next = unchecked(pc + (uint)instruction.Length); return true;
    }
    private bool TryMatchWord(uint pc, ushort expected, out uint next) { next = pc; if (!IsReadable(pc, 2) || _bus.ReadWord(pc) != expected) return false; next = pc + 2; return true; }
    private bool TryMatchMaskedWord(uint pc, ushort mask, ushort expected, out uint next) { next = pc; if (!IsReadable(pc, 2) || (_bus.ReadWord(pc) & mask) != expected) return false; next = pc + 2; return true; }
    private bool TrySkipDecodedProbe(M68kCpuState state, uint pc, out uint next)
    {
        next = pc; if (!IsReadable(pc, 2) || !M68kDecoder.TryDecode(new CodeReader(_bus), pc, out var instruction, out _, M68kJitCpuModel.M68040)) return false;
        switch (instruction.Operation)
        {
            case M68kJitOperation.Move: case M68kJitOperation.Cmp: case M68kJitOperation.Tst: ApplyReadSideEffects(state, instruction.Source, instruction.Size); break;
            case M68kJitOperation.Cmpm: ApplyReadSideEffects(state, instruction.Source, instruction.Size); ApplyReadSideEffects(state, instruction.Destination, instruction.Size); break;
        }
        next = unchecked(pc + (uint)instruction.Length); return true;
    }
    private static void ApplyReadSideEffects(M68kCpuState state, M68kDecodedEa ea, M68kOperandSize size)
    {
        if (ea.Kind != M68kJitEaKind.AddressPostincrement) return;
        var increment = size == M68kOperandSize.Byte && ea.Register == 7 ? 2u : (uint)size; var address = unchecked(state.A[ea.Register] + increment);
        if (ea.Register == 7) state.SetActiveStackPointer(address); else state.A[ea.Register] = address;
    }
    private bool TryGetPrivilegeLength(uint pc, out uint next)
    {
        next = pc; if (!IsReadable(pc, 2)) return false;
        var length = _bus.ReadWord(pc) switch { 0x4E70 or 0x4E73 or 0x4E76 or 0x4E77 => 2, 0x4E72 or 0x4E7A or 0x4E7B => 4, _ => 0 };
        if (length == 0) return false; next = pc + (uint)length; return true;
    }
    private bool TryPopUserProbeReturn(M68kCpuState state, ushort sr, out uint next)
    {
        next = 0; if ((sr & M68kCpuState.Supervisor) != 0 || !_bus.IsMappedMemoryRange(state.UserStackPointer, 4)) return false;
        var address = _bus.ReadLong(state.UserStackPointer); if (!IsPlausibleReturn(address) || (!_bus.HasHostGateway(address) && _isZeroFilledInstructionTarget(address))) return false;
        next = address; state.SetUserStackPointer(state.UserStackPointer + 4); return true;
    }
    private bool TryReadFrame(uint frame, out ushort sr, out uint pc, out uint sp)
    {
        sr = _bus.ReadWord(frame); pc = _bus.ReadLong(frame + 2); sp = frame + 6; if (!IsPlausibleStatus(sr) || !IsPlausibleReturn(pc)) return false;
        if (IsFormat0(_bus.ReadWord(frame + 6))) sp = frame + 8; return true;
    }
    private bool TryReadFrameForVector(uint frame, uint vector, out ushort sr, out uint pc, out uint sp)
    {
        if (vector is BusError or AddressError && TryReadFormat0Frame(frame, vector, true, out sr, out pc, out sp)) return true;
        if (TryReadFrame(frame, out sr, out pc, out sp) && TryReadFrameVector(frame, out _)) return true;
        if (vector is BusError or AddressError)
        {
            if (TryReadLegacyBusFrame(frame, out sr, out pc, out sp)) return true;
            if (TryReadFrame(frame, out sr, out pc, out _)) { sp = frame + 14; return true; }
        }
        if (TryReadFrame(frame, out sr, out pc, out sp)) return true; sp = frame; return false;
    }
    private bool TryReadFormat0Frame(uint frame, uint vector, bool allowUnmappedPc, out ushort sr, out uint pc, out uint sp)
    { sr = _bus.ReadWord(frame); pc = _bus.ReadLong(frame + 2); sp = frame + 8; return IsPlausibleStatus(sr) && TryReadFrameVector(frame, out var actual) && actual == vector && (allowUnmappedPc || IsPlausibleReturn(pc)); }
    private bool TryReadLegacyBusFrame(uint frame, out ushort sr, out uint pc, out uint sp)
    { sr = _bus.ReadWord(frame + 8); pc = _bus.ReadLong(frame + 2); sp = frame + 14; return IsPlausibleStatus(sr) && IsPlausibleReturn(pc); }
    private bool TryReadFrameVector(uint frame, out uint vector) => TryGetFormat0Vector(_bus.ReadWord(frame + 6), out vector);
    private bool IsPlausibleReturn(uint pc) => pc is not 0 and <= 0x00FF_FFFF && (pc & 1) == 0 && (IsReadable(pc, 2) || _bus.HasHostGateway(pc));
    private static bool IsPlausibleStatus(ushort sr) => (sr & unchecked((ushort)~0xF71F)) == 0;
    private bool IsReadable(uint pc, int bytes) => pc <= 0x00FF_FFFF && _bus.IsCpuPhysicalAddressMapped(pc, bytes, AmigaBusAccessKind.CpuInstructionFetch);
    private static bool IsFormat0(ushort word) => TryGetFormat0Vector(word, out _);
    private static bool TryGetFormat0Vector(ushort word, out uint vector)
    { var offset = word & 0x0FFF; var valid = (word & 0xF000) == 0 && (offset & 3) == 0 && (offset is BusError * 4 or AddressError * 4 or IllegalInstruction * 4 or PrivilegeViolation * 4 or LineA * 4 or LineF * 4 || offset is >= 0x0080 and <= 0x00BC); vector = valid ? (uint)(offset / 4) : 0; return valid; }
    private sealed class CodeReader(AmigaBus bus) : IM68kCodeReader { public ushort ReadHostWord(uint address) => bus.ReadHostWord(address); public bool HasHostGateway(uint address) => bus.HasHostGateway(address); }
}
