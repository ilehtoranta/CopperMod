/* Copyright (C) 2026 Ilkka Lehtoranta. SPDX-License-Identifier: MIT */
namespace Copper68k;

internal partial class M68kAdvancedTimingInterpreter
{
    private const ushort RteValidationContext020 = 0xc021;

    // Transient during one RTE. A suspended context lives entirely in its
    // private format-B image; no frame-address lookup table is retained.
    private sealed class RteValidation020(uint frame, uint instructionPc)
    {
        internal uint Frame = frame, InstructionPc = instructionPc, Pc;
        internal ushort Sr, Format, Version;
        internal int Phase;
        internal int Width => Phase == 1 ? 4 : 2;
        internal uint Address => unchecked(Frame + (Phase switch
        { 0 => 0u, 1 => 2u, 2 => 6u, 3 => 0x36u, 4 => Format >> 12 == 10 ? 30u : 90u, _ => 0u }));
    }

    private sealed class RteReadFault020(RteValidation020? context, uint address, int width) :
        M68kEmulationException($"020/030 RTE {(context is null ? "state-load" : "validation")} read fault at {address:X8}.")
    {
        internal RteValidation020? Context = context;
        internal uint Address = address;
        internal int Width = width;
    }

    private bool IsRte020Mapped(uint address, int width, M68kBusAccessKind kind) =>
        _physicalAddressMap is null ||
        _physicalAddressMap is M68EC020AddressMaskedBus { HasPhysicalAddressMap: false } ||
        _physicalAddressMap.IsCpuPhysicalAddressMapped(address, width, kind);

    private uint ReadRteState020(uint address, int width)
    {
        if (!IsRte020Mapped(address, width, M68kBusAccessKind.CpuDataRead))
            throw new RteReadFault020(null, address, width);
        return width == 2 ? ReadWord(address) : ReadLong(address);
    }

    private uint ReadRteValidation020(RteValidation020 context, ref uint? supplied)
    {
        uint value;
        if (supplied is { } repaired) { value = repaired; supplied = null; }
        else
        {
            if (!IsRte020Mapped(context.Address, context.Width, M68kBusAccessKind.CpuDataRead))
                throw new RteReadFault020(context, context.Address, context.Width);
            value = context.Width == 2 ? ReadWord(context.Address) : ReadLong(context.Address);
        }
        return context.Width == 2 ? value & 0xffff : value;
    }

    private void ExecuteRte020(RteValidation020? resume = null, uint? supplied = null)
    {
        try
        {
            while (true)
            {
                var context = resume ?? new RteValidation020(State.A[7], State.LastInstructionProgramCounter);
                resume = null;
                if (context.Phase == 0) { context.Sr = (ushort)ReadRteValidation020(context, ref supplied); context.Phase = 1; }
                if (context.Phase == 1) { context.Pc = ReadRteValidation020(context, ref supplied); context.Phase = 2; }
                if (context.Phase == 2) { context.Format = (ushort)ReadRteValidation020(context, ref supplied); context.Phase = 3; }
                var format = context.Format >> 12;
                var size = format switch { 0 or 1 => 8u, 2 => 12u, 10 => 32u, 11 => 92u, _ => 0u };
                if (size == 0)
                { RaiseFormat0Exception(14, context.InstructionPc, M68kInstructionTimingKey.FormatError); return; }
                if (format is 10 or 11)
                {
                    if (context.Phase == 3)
                    {
                        if (format == 11) context.Version = (ushort)ReadRteValidation020(context, ref supplied);
                        context.Phase = 4;
                    }
                    if (format == 11 && (context.Version >> 12) != 0)
                    { RaiseFormat0Exception(14, context.InstructionPc, M68kInstructionTimingKey.FormatError); return; }
                    if (context.Phase == 4) { _ = ReadRteValidation020(context, ref supplied); context.Phase = 5; }
                    if (TryRestoreM68020AccessFrame(context.Frame, (ushort)format, context.Sr, context.Pc, validated: true)) return;
                }
                State.SetActiveStackPointer(unchecked(context.Frame + size));
                State.StatusRegister = context.Sr;
                if (format == 1) continue;
                State.ProgramCounter = context.Pc;
                CompleteTiming(M68kInstructionTimingKey.Rte);
                return;
            }
        }
        catch (RteReadFault020 fault)
        {
            if (fault.Context is null)
            {
                // MC68020UM 6.1.12 / MC68030UM 8.1.13: BERR during
                // internal-state loading halts, without another stack image.
                State.Halted = true;
                _instructionPipe.Reset();
            }
            else RaiseRteValidationFault020(fault.Context, fault.Address, fault.Width);
        }
    }

    private void RaiseRteValidationFault020(RteValidation020 context, uint address, int width)
    {
        var sr = State.StatusRegister;
        State.RecordException(2, context.InstructionPc, sr);
        State.StatusRegister = (ushort)((sr | M68kCpuState.Supervisor) & ~0xc000);
        var words = new ushort[46];
        void Long(int offset, uint value) { words[offset / 2] = (ushort)(value >> 16); words[offset / 2 + 1] = (ushort)value; }
        words[0] = sr; Long(2, context.InstructionPc); words[3] = 0xb008;
        words[4] = RteValidationContext020;
        words[5] = (ushort)(0x140 | (width == 2 ? 0x20 : 0) | ((sr & 0x2000) != 0 ? 5 : 1));
        Long(0x10, address); Long(0x14, context.Frame);
        Long(0x1c, ((uint)context.Sr << 16) | context.Format); Long(0x20, context.Pc);
        words[0x28 / 2] = (ushort)context.Phase; words[0x30 / 2] = context.Version;
        // Right-justified input buffer at +2C is initially empty. Version 0
        // describes this interpreter's opaque continuation, not silicon state.
        try
        {
            for (var n = words.Length - 1; n >= 0; n--)
            {
                State.SetActiveStackPointer(unchecked(State.A[7] - 2));
                if (!IsRte020Mapped(State.A[7], 2, M68kBusAccessKind.CpuDataWrite))
                    throw new RteReadFault020(null, State.A[7], 2);
                WriteWord(State.A[7], words[n]);
            }
            _instructionPipe.Reset();
            State.ProgramCounter = ReadRteState020(unchecked(State.VectorBaseRegister + 8), 4);
            CompleteTiming(M68kInstructionTimingKey.IllegalInstruction);
        }
        catch (RteReadFault020) { State.Halted = true; _instructionPipe.Reset(); }
    }

    private bool RestoreRteValidation020(uint frame, ushort sr, uint pc, ushort ssw)
    {
        var context = new RteValidation020(ReadRteFrameLong(frame + 0x14), pc)
        {
            Sr = ReadRteFrameWord(frame + 0x1c), Format = ReadRteFrameWord(frame + 0x1e),
            Pc = ReadRteFrameLong(frame + 0x20), Phase = ReadRteFrameWord(frame + 0x28), Version = ReadRteFrameWord(frame + 0x30)
        };
        var faultAddress = ReadRteFrameLong(frame + 0x10);
        var input = ReadRteFrameLong(frame + 0x2c);
        var format = context.Format >> 12;
        var expectedSsw = 0x40 | (context.Width == 2 ? 0x20 : 0) | ((sr & 0x2000) != 0 ? 5 : 1);
        if (context.Phase is < 0 or > 4 || context.Phase == 3 && format != 11 || context.Phase == 4 && format is not (10 or 11) ||
            faultAddress != context.Address || (ssw & ~0x100) != expectedSsw)
            throw new UnsupportedM68kTimingException(0x4e73, State.LastInstructionProgramCounter, _profile);
        var returnedStack = (sr & 0x2000) == 0 ? State.UserStackPointer :
            (sr & 0x1000) == (State.StatusRegister & 0x1000) ? unchecked(frame + 92) :
            (sr & 0x1000) != 0 ? State.MasterStackPointer : State.InterruptStackPointer;
        if (returnedStack != context.Frame)
            throw new UnsupportedM68kTimingException(0x4e73, State.LastInstructionProgramCounter, _profile);
        for (var offset = 0x10u; offset < 92; offset += 2) _ = ReadRteFrameWord(frame + offset);
        State.SetActiveStackPointer(unchecked(frame + 92)); State.StatusRegister = sr;
        State.ProgramCounter = unchecked(pc + 2); _instructionPipe.Reset();
        // Explicit handler RTE resumes at the rejected validation read. Earlier
        // reads use their serialized values, never replaying completed phases.
        ExecuteRte020(context, (ssw & 0x100) == 0 ? input : null);
        return true;
    }
}
