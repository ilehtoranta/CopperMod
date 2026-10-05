/* Copyright (C) 2026 Ilkka Lehtoranta. SPDX-License-Identifier: MIT */
namespace Copper68k;

internal partial class M68kAdvancedTimingInterpreter
{
    private (uint Pc, uint Address)? _m68040MovemContinuation;
    internal bool HasPendingM68040MovemContinuation => _m68040MovemContinuation.HasValue;

    private bool TryRestoreM68040AccessFrame(uint frame, ushort sr, uint pc)
    {
        var priorSr = State.StatusRegister;
        var continuation = ReadWord(frame + 12) & 0xf000;
        // Multiple continuation bits are undefined (MC68040UM 8.4.6.7).
        if (continuation is not (0 or 0x1000 or 0x2000 or 0x4000 or 0x8000)) return false;
        var pending = continuation == 0x8000
            ? State.M68040PendingFpuExceptions.Find(3)
            : continuation == 0x4000 ? State.M68040PendingFpuExceptions.Find(2) : null;
        // A foreign/context-transferred CP frame without its selected vector is
        // still an explicit implementation gap. Do not invent a format error or
        // reselect a vector from handler-modified FPCR/FPSR registers.
        if (continuation == 0x8000 && pending == null)
            throw new UnsupportedM68kTimingException(0x4e73, State.LastInstructionProgramCounter, _profile);
        var address = continuation == 0 ? 0u : ReadLong(frame + 8);
        State.SetActiveStackPointer(unchecked(frame + 60));
        State.StatusRegister = sr;
        State.ProgramCounter = pc;
        if (continuation >= 0x2000)
        {
            // MC68040UM 8.4.6.2/7: convert the pending exception frame at
            // old SP+48. CT/CU use format 2; CP uses format 3. Trace is deferred
            // to the CU/CP handler, as indicated by the saved SR trace bits.
            var vector = continuation == 0x8000 ? pending!.Vector : continuation == 0x4000 ? 11 : 9;
            var format = continuation == 0x8000 ? 3 : 2;
            State.RecordException(vector, pc, sr);
            State.StatusRegister = (ushort)((sr | M68kCpuState.Supervisor) & ~0xc000);
            PushLong(address);
            PushWord((ushort)(format << 12 | vector * 4));
            PushLong(pc);
            PushWord(sr);
            State.ProgramCounter = ReadLong(State.VectorBaseRegister + (uint)vector * 4);
            if (pending != null && (continuation == 0x8000 || pending.Vector == 11))
                State.M68040PendingFpuExceptions.Complete(pending);
            CompleteTiming(continuation == 0x2000 ? M68kInstructionTimingKey.IllegalInstruction : M68kInstructionTimingKey.LineFException);
        }
        else
        {
            if (TryRaiseM68040RteAddressError(pc, priorSr, State.LastInstructionProgramCounter)) return true;
            if (continuation == 0x1000) _m68040MovemContinuation = (pc, address);
            CompleteTiming(M68kInstructionTimingKey.Rte);
        }
        return true;
    }

    private bool TryRaiseM68040RteAddressError(uint target, ushort priorSr, uint rtePc)
    {
        if (_profile.Model != M68kAcceleratorModel.M68040 || (target & 1) == 0) return false;
        // MC68040UM 8.4 requires S in the saved SR on a traced user return.
        // Documentary WinUAE retains the pre-restoration SR image, while the
        // restored SR selects the live exception bank and CCR. Chained USP SR
        // provenance remains separately unqualified rather than disappearing.
        if ((State.StatusRegister & 0x2000) == 0 && (State.StatusRegister & 0xc000) != 0)
            priorSr |= M68kCpuState.Supervisor;
        RaiseM68040AddressError(rtePc, target, priorSr);
        return true;
    }

    private void RaiseM68040AddressError(uint instructionPc, uint faultAddress, ushort savedSr)
    {
        // MC68040UM 8.2.2, 8.4.3/6.7: format 2, fault address with A0
        // cleared, and the instruction which caused the failed prefetch.
        State.RecordException(3, instructionPc, savedSr);
        State.StatusRegister = (ushort)((State.StatusRegister | M68kCpuState.Supervisor) & ~0xc000);
        PushLong(faultAddress & ~1u);
        PushWord(0x200c);
        PushLong(instructionPc);
        PushWord(savedSr);
        State.ProgramCounter = ReadLong(State.VectorBaseRegister + 12);
        CompleteTiming(M68kInstructionTimingKey.IllegalInstruction);
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
