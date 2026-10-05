/* Copyright (C) 2026 Ilkka Lehtoranta. SPDX-License-Identifier: MIT */
namespace Copper68k;

internal partial class M68kAdvancedTimingInterpreter
{
    private (uint Pc, uint Address)? _m68040MovemContinuation;
    internal bool HasPendingM68040MovemContinuation => _m68040MovemContinuation.HasValue;

    private bool TryRestoreM68040AccessFrame(uint frame, ushort sr, uint pc)
    {
        var continuation = ReadWord(frame + 12) & 0xf000;
        // CU/CP need qualified pending-FPU state; multiple bits are undefined.
        // Keep those explicit gaps on the existing rejected-frame path for now.
        if (continuation is not (0 or 0x1000 or 0x2000)) return false;
        var address = continuation == 0 ? 0u : ReadLong(frame + 8);
        State.SetActiveStackPointer(unchecked(frame + 60));
        State.StatusRegister = sr;
        State.ProgramCounter = pc;
        if (continuation == 0x1000)
            _m68040MovemContinuation = (pc, address);
        if (continuation == 0x2000)
        {
            // MC68040UM 8.4.6.2: old SP+48 is the new format-2 frame.
            // The instruction address is the saved EA, not the RTE opcode PC.
            State.RecordException(9, pc, sr);
            State.StatusRegister = (ushort)((sr | M68kCpuState.Supervisor) & ~0xc000);
            PushLong(address);
            PushWord(0x2024);
            PushLong(pc);
            PushWord(sr);
            State.ProgramCounter = ReadLong(State.VectorBaseRegister + 36);
            CompleteTiming(M68kInstructionTimingKey.IllegalInstruction);
        }
        else CompleteTiming(M68kInstructionTimingKey.Rte);
        return true;
    }

    private bool TryExecuteM68040MovemContinuation(ushort opcode)
    {
        if (_m68040MovemContinuation is not { } saved || saved.Pc != State.ProgramCounter) return false;
        // Consume once at the restored instruction. An interrupt handler at a
        // different PC must not consume the suspended instruction's saved EA.
        _m68040MovemContinuation = null;
        return TryExecuteGeneralMovem(opcode, saved.Address);
    }

    private void ConsumeMovemAddressExtensions(int mode, int register, ushort opcode)
    {
        if (mode == 5 || mode == 7 && register is 0 or 2) { _ = FetchWord(); return; }
        if (mode == 7 && register == 1) { _ = FetchLong(); return; }
        if (mode != 6 && !(mode == 7 && register == 3)) return;
        var extension = FetchWord();
        if ((extension & 0x100) == 0) return;
        var baseSize = (extension >> 4) & 3;
        var indirect = extension & 7;
        if (baseSize == 0 || (extension & 8) != 0 || indirect == 4 || ((extension & 0x40) != 0 && indirect >= 4))
            throw new UnsupportedM68kTimingException(opcode, State.LastInstructionProgramCounter, _profile);
        if (baseSize == 2) _ = FetchWord();
        if (baseSize == 3) _ = FetchLong();
        if ((indirect & 3) == 2) _ = FetchWord();
        if ((indirect & 3) == 3) _ = FetchLong();
        // Deliberately no index/base arithmetic or pointer-chain data access.
    }
}
