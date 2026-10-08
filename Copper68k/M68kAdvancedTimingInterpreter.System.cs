/* Copyright (C) 2026 Ilkka Lehtoranta. SPDX-License-Identifier: MIT */
using System;
namespace Copper68k;

internal partial class M68kAdvancedTimingInterpreter
{
    private bool TryExecuteExtendedSystem(ushort opcode)
    {
        if ((opcode & 0xffc0) != 0xf600 && opcode != 0xf800) return false;
        BeginInstruction(opcode); _ = FetchWord();
        var pc = State.LastInstructionProgramCounter;
        if (opcode == 0xf800)
        {
            if (_profile.Model != M68kAcceleratorModel.M68060)
            { RaiseFormat0Exception(11, pc, M68kInstructionTimingKey.LineFException); return true; }
            // MC68060UM D-19/20 fixes the second opcode word at 01C0.
            // Unrecognized F-line encodings take vector 11 (8.2.4), before
            // the privilege check for a recognized instruction (8.2.5).
            if (FetchWord() != 0x01c0)
            { RaiseFormat0Exception(11, pc, M68kInstructionTimingKey.LineFException); return true; }
            if (!State.GetFlag(M68kCpuState.Supervisor))
            { RaiseFormat0Exception(8, pc, M68kInstructionTimingKey.PrivilegeViolation); return true; }
            var immediate = FetchWord();
            if ((immediate & M68kCpuState.Supervisor) == 0)
            { RaiseFormat0Exception(8, pc, M68kInstructionTimingKey.PrivilegeViolation); return true; }
            State.StatusRegister = immediate; State.Stopped = true;
            CompleteTiming(M68kInstructionTimingKey.Stop);
            return true;
        }
        if (_profile.Model is not (M68kAcceleratorModel.M68040 or M68kAcceleratorModel.M68060))
        { RaiseFormat0Exception(11, pc, M68kInstructionTimingKey.LineFException); return true; }
        var register = opcode & 7; var form = (opcode >> 3) & 7;
        uint source, destination; var other = -1;
        if (form == 4)
        {
            var extension = FetchWord();
            if ((extension & 0x8fff) != 0x8000)
            { RaiseFormat0Exception(4, pc, M68kInstructionTimingKey.IllegalInstruction); return true; }
            other = (extension >> 12) & 7; source = State.A[register]; destination = State.A[other];
        }
        else if (form < 4)
        {
            var absolute = FetchLong();
            source = (form & 1) == 0 ? State.A[register] : absolute;
            destination = (form & 1) == 0 ? absolute : State.A[register];
        }
        // F628..F63F do not assign a MOVE16 first-word form. They are
        // unrecognized F-line words (MC68040UM 9.6.1 / MC68060UM 8.2.4),
        // unlike a recognized F620..F627 word with an invalid extension.
        else { RaiseFormat0Exception(11, pc, M68kInstructionTimingKey.LineFException); return true; }
        source &= 0xfffffff0; destination &= 0xfffffff0;
        Span<uint> line = stackalloc uint[4];
        for (var i = 0; i < 4; i++) line[i] = ReadLong(source + (uint)i * 4);
        for (var i = 0; i < 4; i++) WriteLong(destination + (uint)i * 4, line[i]);
        if (form == 4 || form < 2) WriteGeneralRegister(true, register, unchecked(State.A[register] + 16));
        if (other >= 0 && other != register) WriteGeneralRegister(true, other, unchecked(State.A[other] + 16));
        CompleteTiming(M68kInstructionTimingKey.Movec);
        return true;
    }

    private static bool IsStatusTransfer(ushort opcode) => (opcode & 0xffc0) is 0x40c0 or 0x42c0 or 0x44c0 or 0x46c0;

    // This path handles forms absent from the existing timing dispatch before
    // the 040's 000 fallback can apply the earlier model's privilege rules.
    private bool TryExecuteGeneralSystem(ushort opcode)
    {
        if ((opcode & 0xfff8) == 0x4848)
        {
            BeginInstruction(opcode); _ = FetchWord();
            // No breakpoint replacement device is exposed by IM68kBus.
            RaiseFormat0Exception(4, State.LastInstructionProgramCounter, M68kInstructionTimingKey.IllegalInstruction);
            return true;
        }
        if ((opcode & 0xff00) == 0x0e00)
        {
            var m = (opcode >> 3) & 7; var r = opcode & 7; var field = (opcode >> 6) & 3;
            if (field == 3 || m < 2 || m == 7 && r > 1) return false;
            BeginInstruction(opcode); _ = FetchWord();
            if (!State.GetFlag(M68kCpuState.Supervisor))
            { RaiseFormat0Exception(8, State.LastInstructionProgramCounter, M68kInstructionTimingKey.PrivilegeViolation); return true; }
            var ext = FetchWord(); var general = (ext >> 12) & 7;
            var ar = (ext & 0x8000) != 0; var store = (ext & 0x800) != 0;
            var size = (M68kOperandSize)(1 << field);
            var address = ResolveMoveAddress(m, r, size, opcode);
            if (m == 3) WriteGeneralRegister(true, r, unchecked(address + M68kIntegerSemantics.AddressIncrement(r, size)));
            if (store) WriteSized(address, ReadGeneralRegister(ar, general), size);
            else
            {
                var value = ReadSized(address, size);
                if (ar) WriteGeneralRegister(true, general, M68kCpuState.SignExtend(value, size));
                else WriteDataRegisterSized(general, value, size);
            }
            CompleteGeneralLogicalTiming(m, r, false);
            return true;
        }
        if (opcode is 0x4e76 or 0x4e77)
        {
            BeginInstruction(opcode); _ = FetchWord();
            if (opcode == 0x4e76)
            {
                if (State.GetFlag(M68kCpuState.Overflow))
                {
                    RaiseFormat0Exception(7, State.ProgramCounter, M68kInstructionTimingKey.Nop);
                    return true;
                }
            }
            else
            {
                var ccr = ReadWord(State.A[7]); State.SetActiveStackPointer(State.A[7] + 2); var pc = PullLong();
                State.StatusRegister = (ushort)((State.StatusRegister & 0xffe0) | (ccr & 31));
                State.ProgramCounter = pc;
            }
            CompleteTiming(M68kInstructionTimingKey.Nop);
            return true;
        }
        if ((opcode & 0xf140) == 0x4100)
        {
            var checkMode = (opcode >> 3) & 7; var checkReg = opcode & 7;
            if (checkMode == 1 || checkMode == 7 && checkReg > 4) return false;
            BeginInstruction(opcode); _ = FetchWord();
            var size = (opcode & 0x80) != 0 ? M68kOperandSize.Word : M68kOperandSize.Long;
            uint bound;
            if (checkMode == 0) bound = State.D[checkReg];
            else if (checkMode == 7 && checkReg == 4) bound = size == M68kOperandSize.Long ? FetchLong() : FetchWord();
            else
            {
                var address = ResolveMoveAddress(checkMode, checkReg, size, opcode); bound = ReadSized(address, size);
                if (checkMode == 3) WriteGeneralRegister(true, checkReg, address + (uint)size);
            }
            var value = SignExtendForSize(State.D[(opcode >> 9) & 7], size);
            if (value < 0 || value > SignExtendForSize(bound, size))
            {
                State.SetFlag(M68kCpuState.Negative, value < 0);
                RaiseFormat0Exception(6, State.ProgramCounter, M68kInstructionTimingKey.IllegalInstruction);
            }
            else CompleteGeneralLogicalTiming(checkMode, checkReg, false);
            return true;
        }
        if (!IsStatusTransfer(opcode)) return false;
        var mode = (opcode >> 3) & 7; var reg = opcode & 7;
        var family = opcode & 0xffc0;
        var from = family is 0x40c0 or 0x42c0;
        if (mode == 1 || mode == 7 && reg > (from ? 1 : 4)) return false;
        BeginInstruction(opcode); _ = FetchWord();
        if (family is 0x40c0 or 0x46c0 && !State.GetFlag(M68kCpuState.Supervisor))
        {
            RaiseFormat0Exception(8, State.LastInstructionProgramCounter, M68kInstructionTimingKey.PrivilegeViolation);
            return true;
        }
        if (from)
            WriteWordDestination(mode, reg, (ushort)(family == 0x40c0 ? State.StatusRegister : State.StatusRegister & 31), opcode);
        else
        {
            ushort value;
            if (mode == 0) value = (ushort)State.D[reg];
            else if (mode == 7 && reg == 4) value = FetchWord();
            else
            {
                var address = ResolveMoveAddress(mode, reg, M68kOperandSize.Word, opcode);
                value = ReadWord(address);
                if (mode == 3) WriteGeneralRegister(true, reg, unchecked(address + 2));
            }
            State.StatusRegister = family == 0x46c0 ? value : (ushort)((State.StatusRegister & 0xffe0) | (value & 31));
        }
        CompleteGeneralLogicalTiming(mode, reg, false);
        return true;
    }
}
