/* Copyright (C) 2026 Ilkka Lehtoranta. SPDX-License-Identifier: MIT */
namespace Copper68k;

// CPU-space module access-control registers are distinct from normal memory.
// A flat IM68kBus has no external access-control responder: type-1 calls then
// take the documented format error. This optional internal hook qualifies the
// integer module protocol without extending the public package API.
internal interface IM68kModuleAccessBus
{
    bool TryReadModuleByte(uint address, out byte value);
    bool TryWriteModuleByte(uint address, byte value);
    bool TryWriteModuleLong(uint address, uint value);
}

internal partial class M68kAdvancedTimingInterpreter
{
    private bool TryExecuteModule(ushort opcode)
    {
        if ((opcode & 0xffc0) != 0x06c0) return false;
        BeginInstruction(opcode); _ = FetchWord();
        var pc = State.LastInstructionProgramCounter;
        if (_profile.Model != M68kAcceleratorModel.M68020)
        { RaiseFormat0Exception(4, pc, M68kInstructionTimingKey.IllegalInstruction); return true; }
        var mode = (opcode >> 3) & 7; var reg = opcode & 7;
        if (mode < 2)
        {
            var sp = State.A[7]; var header = ReadWord(sp); var opt = header >> 13; var type = (header >> 8) & 31;
            if (opt is not (0 or 4) || type > 1) { ModuleFormatError(pc); return true; }
            var ccr = ReadWord(sp + 2); var count = ReadWord(sp + 4) & 255;
            var nextPc = ReadLong(sp + 12); var pointer = ReadLong(sp + 16);
            var nextSp = type == 1 || opt == 4 ? ReadLong(sp + 20) : sp + 24;
            if (type == 1)
            {
                if (_bus is not IM68kModuleAccessBus access || !access.TryWriteModuleByte(0x10008, (byte)header) ||
                    !access.TryReadModuleByte(0x1000c, out var status) || status is 0 or > 7)
                { ModuleFormatError(pc); return true; }
            }
            WriteGeneralRegister(mode == 1, reg, pointer);
            State.SetActiveStackPointer(unchecked(nextSp + (uint)count));
            State.StatusRegister = (ushort)((State.StatusRegister & 0xffe0) | (ccr & 31));
            State.ProgramCounter = nextPc;
            CompleteTiming(M68kInstructionTimingKey.Rtd); return true;
        }
        if (mode is not (2 or 5 or 6) && !(mode == 7 && reg <= 3))
        { RaiseFormat0Exception(4, pc, M68kInstructionTimingKey.IllegalInstruction); return true; }
        var argumentCount = FetchWord() & 255;
        var descriptor = ResolveMoveAddress(mode, reg, M68kOperandSize.Long, opcode);
        var control = ReadLong(descriptor); var option = control >> 29; var descriptorType = (control >> 24) & 31;
        if (option is not (0 or 4) || descriptorType > 1 || (control & 0xffff) != 0)
        { ModuleFormatError(pc); return true; }
        var entry = ReadLong(descriptor + 4); var data = ReadLong(descriptor + 8);
        var entryWord = ReadWord(entry); var general = (entryWord >> 12) & 7; var addressRegister = (entryWord & 0x8000) != 0;
        var oldSp = State.A[7]; var frameTop = oldSp; byte savedAccess = 0;
        if (descriptorType == 1)
        {
            if (_bus is not IM68kModuleAccessBus access || !access.TryReadModuleByte(0x10000, out savedAccess))
            { ModuleFormatError(pc); return true; }
            _ = ReadByte(oldSp);
            var functionCode = State.GetFlag(M68kCpuState.Supervisor) ? 5u : 1u;
            if (!access.TryWriteModuleLong(0x10040 + functionCode * 4, descriptor) ||
                !access.TryWriteModuleByte(0x10004, (byte)(control >> 16)) ||
                !access.TryReadModuleByte(0x1000c, out var status) || status is 0 or > 7)
            { ModuleFormatError(pc); return true; }
            if (status >= 4)
            {
                frameTop = ReadLong(descriptor + 12);
                if (option == 0)
                {
                    frameTop = unchecked(frameTop - (uint)argumentCount);
                    for (var i = 0; i < argumentCount; i++) WriteByte(frameTop + (uint)i, ReadByte(oldSp + (uint)i));
                }
            }
        }
        var savedData = ReadGeneralRegister(addressRegister, general);
        var frame = unchecked(frameTop - 24);
        WriteWord(frame, (ushort)((control >> 16 & 0xff00) | savedAccess));
        WriteWord(frame + 2, (ushort)(State.StatusRegister & 31)); WriteWord(frame + 4, (ushort)argumentCount);
        WriteWord(frame + 6, 0); WriteLong(frame + 8, descriptor); WriteLong(frame + 12, State.ProgramCounter);
        WriteLong(frame + 16, savedData); WriteLong(frame + 20, descriptorType == 1 || option == 4 ? oldSp : 0);
        WriteGeneralRegister(addressRegister, general, data);
        State.SetActiveStackPointer(frame); State.ProgramCounter = entry + 2;
        CompleteGeneralLogicalTiming(mode, reg, false); return true;
    }
    private void ModuleFormatError(uint pc) => RaiseFormat0Exception(14, pc, M68kInstructionTimingKey.FormatError);
}
