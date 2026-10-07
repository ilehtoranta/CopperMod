/* Copyright (C) 2026 Ilkka Lehtoranta. SPDX-License-Identifier: MIT */
// Isolated candidate only; copied into an audit snapshot by the test command.
namespace Copper68k;

internal partial class M68kAdvancedTimingInterpreter
{
    private const ushort FinalMoveWriteContext020 = 0xc023;
    private bool _enteringFinalMoveWrite020, _resumingFinalMoveWrite020;
    private ushort? _restoredWriteFunctionCode020;

    private sealed class FinalMoveWriteFault020(uint address, uint value, ushort opcode,
        uint nextPc, ushort functionCode, M68kAdvancedInstructionPipe pipe) :
        M68kEmulationException($"020/030 final MOVE write fault at {address:X8}.")
    {
        internal readonly uint Address = address, Value = value, NextPc = nextPc;
        internal readonly ushort Opcode = opcode, FunctionCode = functionCode;
        internal readonly M68kAdvancedInstructionPipe Pipe = pipe;
    }

    private static bool IsSimpleFinalMove020(ushort opcode) => opcode >> 12 is >= 1 and <= 3 &&
        ((opcode >> 6) & 7) == 2 && ((opcode >> 3) & 7) is 2 or 3;

    private static M68kOperandSize FinalMoveSize020(ushort opcode) => (opcode >> 12) switch
    { 1 => M68kOperandSize.Byte, 3 => M68kOperandSize.Word, _ => M68kOperandSize.Long };

    private static M68kInstructionTimingKey FinalMoveTiming020(ushort opcode) =>
        (((opcode >> 3) & 7) == 3, opcode >> 12) switch
        {
            (false, 1) => M68kInstructionTimingKey.MoveByteAddressIndirectToAddressIndirect,
            (false, 3) => M68kInstructionTimingKey.MoveWordAddressIndirectToAddressIndirect,
            (false, _) => M68kInstructionTimingKey.MoveLongAddressIndirectToAddressIndirect,
            (true, 1) => M68kInstructionTimingKey.MoveBytePostIncrementToAddressIndirect,
            (true, 3) => M68kInstructionTimingKey.MoveWordPostIncrementToAddressIndirect,
            _ => M68kInstructionTimingKey.MoveLongPostIncrementToAddressIndirect
        };

    private void WriteFinalMoveDestination020(uint address, uint value, M68kOperandSize size)
    {
        var width = size == M68kOperandSize.Byte ? 1 : size == M68kOperandSize.Word ? 2 : 4;
        if (_has020PhysicalMap && !_physicalAddressMap!.IsCpuPhysicalAddressMapped(address, width, M68kBusAccessKind.CpuDataWrite))
        {
            if (!IsSimpleFinalMove020(State.LastOpcode) || size != FinalMoveSize020(State.LastOpcode) ||
                State.ProgramCounter != unchecked(State.LastInstructionProgramCounter + 2))
                throw new UnsupportedM68kTimingException(State.LastOpcode, State.LastInstructionProgramCounter, _profile);
            // Normal mapped execution keeps its existing ordering. On this
            // final-write fault the completed MOVE flags belong in saved SR.
            // A repeated pending write must retain the returned SR verbatim.
            if (!_resumingFinalMoveWrite020) SetMoveFlags(value, size);
            var fc = _restoredWriteFunctionCode020 ?? (ushort)((State.StatusRegister & 0x2000) != 0 ? 5 : 1);
            throw new FinalMoveWriteFault020(address, value, State.LastOpcode, State.ProgramCounter, fc, _instructionPipe);
        }
        WriteSized(address, value, size);
    }

    private void RaiseFinalMoveWriteFault020(FinalMoveWriteFault020 fault)
    {
        if (_enteringFinalMoveWrite020) { State.Halted = true; _instructionPipe.Reset(); return; }
        var sr = State.StatusRegister;
        var size = FinalMoveSize020(fault.Opcode);
        var words = new ushort[16];
        void Long(int offset, uint value) { words[offset / 2] = (ushort)(value >> 16); words[offset / 2 + 1] = (ushort)value; }
        words[0] = sr; Long(2, fault.NextPc); words[3] = 0xa008;
        words[4] = FinalMoveWriteContext020;
        words[5] = (ushort)(0x100 | (size == M68kOperandSize.Byte ? 0x10 : size == M68kOperandSize.Word ? 0x20 : 0) | fault.FunctionCode);
        Long(16, fault.Address); words[10] = fault.Opcode;
        words[11] = (ushort)(1 | (fault.Pipe.Count << 8)); Long(24, fault.Value);
        var pipe = fault.Pipe;
        for (var n = 0; n < fault.Pipe.Count; n++)
        {
            var address = pipe.HeadAddress;
            _ = pipe.TryConsumeKnownHead(out var word, out _);
            if (address != unchecked(fault.NextPc + (uint)n * 2))
                throw new UnsupportedM68kTimingException(fault.Opcode, unchecked(fault.NextPc - 2), _profile);
            words[n == 0 ? 6 : n == 1 ? 7 : 14] = word;
        }
        if (fault.Pipe.Count != 0 && fault.Pipe.NextAddress != unchecked(fault.NextPc + (uint)fault.Pipe.Count * 2))
            throw new UnsupportedM68kTimingException(fault.Opcode, unchecked(fault.NextPc - 2), _profile);
        State.RecordException(2, fault.NextPc, sr);
        State.StatusRegister = (ushort)((sr | M68kCpuState.Supervisor) & ~0xc000);
        _enteringFinalMoveWrite020 = true;
        try
        {
            for (var n = words.Length - 1; n >= 0; n--)
            {
                State.SetActiveStackPointer(unchecked(State.A[7] - 2));
                if (!_physicalAddressMap!.IsCpuPhysicalAddressMapped(State.A[7], 2, M68kBusAccessKind.CpuDataWrite))
                { State.Halted = true; _instructionPipe.Reset(); return; }
                WriteWord(State.A[7], words[n]);
            }
            _instructionPipe.Reset();
            State.ProgramCounter = ReadLong(unchecked(State.VectorBaseRegister + 8));
            CompleteTiming(M68kInstructionTimingKey.IllegalInstruction);
        }
        catch (OperandReadFault020) { State.Halted = true; _instructionPipe.Reset(); }
        finally { _enteringFinalMoveWrite020 = false; }
    }

    private bool RestoreFinalMoveWrite020(uint frame, ushort sr, uint pc, ushort ssw)
    {
        var address = ReadRteFrameLong(unchecked(frame + 16));
        var opcode = ReadRteFrameWord(unchecked(frame + 20));
        var metadata = ReadRteFrameWord(unchecked(frame + 22));
        var value = ReadRteFrameLong(unchecked(frame + 24));
        var reserved = ReadRteFrameWord(unchecked(frame + 30));
        var count = metadata >> 8;
        var size = FinalMoveSize020(opcode);
        var expectedSsw = (size == M68kOperandSize.Byte ? 0x10 : size == M68kOperandSize.Word ? 0x20 : 0) | (ssw & 4) | 1;
        if (!IsSimpleFinalMove020(opcode) || (metadata & 0xff) != 1 || count > M68kAdvancedInstructionPipe.Capacity || reserved != 0 ||
            (ssw & ~0x100) != expectedSsw || (sr & 0xc000) != 0 || (pc & 1) != 0)
            throw new UnsupportedM68kTimingException(0x4e73, State.LastInstructionProgramCounter, _profile);
        M68kAdvancedInstructionPipe pipe = default; pipe.Reset(pc);
        for (var n = 0; n < count; n++)
        {
            var offset = n == 0 ? 12u : n == 1 ? 14u : 28u;
            pipe.Append(unchecked(pc + (uint)n * 2), ReadRteFrameWord(unchecked(frame + offset)), default);
        }
        // Read the full private state before committing stack, SR or the write.
        for (var offset = 12u; offset < 32; offset += 2) _ = ReadRteFrameWord(unchecked(frame + offset));
        State.SetActiveStackPointer(unchecked(frame + 32)); State.StatusRegister = sr; State.ProgramCounter = pc;
        CompleteTiming(M68kInstructionTimingKey.Rte); _instructionPipe = pipe;
        State.LastOpcode = opcode; State.LastInstructionProgramCounter = unchecked(pc - 2);
        var previous = _resumingFinalMoveWrite020; var previousFc = _restoredWriteFunctionCode020;
        _resumingFinalMoveWrite020 = true; _restoredWriteFunctionCode020 = (ushort)(ssw & 7);
        try
        {
            // DF clear means software completed the cycle. No opcode fetch,
            // source read, EA calculation, source update or flag recomputation.
            if ((ssw & 0x100) != 0) WriteFinalMoveDestination020(address, value, size);
        }
        finally { _resumingFinalMoveWrite020 = previous; _restoredWriteFunctionCode020 = previousFc; }
        CompleteTiming(FinalMoveTiming020(opcode));
        return true;
    }
}
