/* Copyright (C) 2026 Ilkka Lehtoranta. SPDX-License-Identifier: MIT */
namespace Copper68k;

internal partial class M68kAdvancedTimingInterpreter
{
    // Internal register images are processor-specific. This identifies the
    // synchronous interpreter's pending instruction-prefetch context, not
    // an external silicon/MMU/pipeline image. No out-of-frame state is needed.
    private const ushort InstructionPrefetchContext020 = 0xc020;
    private bool Has020AccessFrames => _profile.Model is M68kAcceleratorModel.M68020 or M68kAcceleratorModel.M68030;

    private void RaiseM68020InstructionAddressError(uint address)
    {
        var sr = State.StatusRegister;
        State.RecordException(3, address, sr);
        State.StatusRegister = (ushort)((sr | M68kCpuState.Supervisor) & ~0xc000);
        // MC68020UM 6.1.3/6.4, MC68030UM 8.1.3/8.4: format B retains
        // instruction continuation. Stage C is the failed opcode prefetch;
        // stage B's explicit address is C+2. No bus read is made at either
        // odd address. FB/FC/DF stay clear; RC/RB request software repair.
        for (var n = 0; n < 18; n++) PushWord(0); // Internal state.
        PushWord(0); // Version 0 / internal state.
        for (var n = 0; n < 3; n++) PushWord(0);
        PushLong(0); // No data input buffer.
        PushLong(0); // Internal state.
        PushLong(unchecked(address + 2)); // Stage B address.
        PushLong(0); PushLong(0); // Internal state.
        PushLong(0); // No data output buffer.
        PushLong(address); // Internal pending instruction address.
        PushLong(0); // No data-cycle fault.
        PushWord(0); PushWord(0); // Stage B/C images, to be repaired.
        PushWord(0x3000); // RC/RB, address error has no bus fault bits.
        PushWord(InstructionPrefetchContext020);
        PushWord(0xb00c); PushLong(address); PushWord(sr);
        _instructionPipe.Reset();
        State.ProgramCounter = ReadLong(State.VectorBaseRegister + 12);
        CompleteTiming(M68kInstructionTimingKey.IllegalInstruction);
    }

    private bool TryRestoreM68020AccessFrame(uint frame, ushort format, ushort sr, uint pc)
    {
        if (!Has020AccessFrames || format is not (10 or 11)) return false;
        var size = format == 10 ? 32u : 92u;
        // Check the far end and long-frame version before loading state.
        if (format == 11 && (ReadRteFrameWord(frame + 0x36) >> 12) != 0)
        {
            RaiseFormat0Exception(14, State.LastInstructionProgramCounter, M68kInstructionTimingKey.FormatError);
            return true;
        }
        _ = ReadRteFrameWord(unchecked(frame + size - 2));
        var context = ReadRteFrameWord(frame + 8);
        var ssw = ReadRteFrameWord(frame + 10);
        // Data faults and foreign opaque images remain implementation gaps.
        // Never silently consume them as ordinary frame-size pops.
        if (format != 11 || context != InstructionPrefetchContext020 || (ssw & 0xcf00) != 0)
            throw new UnsupportedM68kTimingException(0x4e73, State.LastInstructionProgramCounter, _profile);
        var instructionAddress = ReadRteFrameLong(frame + 0x14);
        var stageBAddress = ReadRteFrameLong(frame + 0x24);
        if (pc != instructionAddress || stageBAddress != unchecked(instructionAddress + 2))
            throw new UnsupportedM68kTimingException(0x4e73, State.LastInstructionProgramCounter, _profile);
        var stageC = ReadRteFrameWord(frame + 12);
        var stageB = ReadRteFrameWord(frame + 14);
        // Read the complete opaque state before committing the stack/status.
        for (var offset = 0x10u; offset < size; offset += 2) _ = ReadRteFrameWord(frame + offset);
        State.SetActiveStackPointer(unchecked(frame + size));
        State.StatusRegister = sr;
        State.ProgramCounter = pc;
        _instructionPipe.Reset(pc);
        if ((ssw & 0x3000) != 0)
        {
            // An odd prefetch cannot be rerun on the bus. Software must repair
            // the pipeline words and clear RC/RB. This is explicit RTE work,
            // never an automatic retry during exception delivery.
            RaiseM68020InstructionAddressError(instructionAddress);
            return true;
        }
        CompleteTiming(M68kInstructionTimingKey.Rte);
        // RTE's existing timing barrier flushes the handler's pipe first.
        // Restore the architectural images after that barrier, not before it.
        _instructionPipe.Append(instructionAddress, stageC, default);
        _instructionPipe.Append(stageBAddress, stageB, default);
        return true;
    }
}
