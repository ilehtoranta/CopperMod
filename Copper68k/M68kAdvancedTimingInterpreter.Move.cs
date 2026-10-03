/* Copyright (C) 2026 Ilkka Lehtoranta. SPDX-License-Identifier: MIT */
namespace Copper68k;

internal partial class M68kAdvancedTimingInterpreter
{
    private int _indexedTransferExtraCycles;

    private uint CalculateIndexedOperandAddress(int register, ushort extension, ushort opcode)
        => CalculateIndexedOperandAddress(State.A[register], extension, opcode);

    // Enter only when the existing dispatch declined before beginning execution.
    // Never catch a partially executed operand route and retry it here.
    private bool TryExecuteGeneralMove(ushort opcode)
    {
        var top = opcode >> 12;
        if (top is < 1 or > 3) return false;
        var sourceMode = (opcode >> 3) & 7;
        var sourceRegister = opcode & 7;
        var destinationMode = (opcode >> 6) & 7;
        var destinationRegister = (opcode >> 9) & 7;
        var valid = !(top == 1 && (sourceMode == 1 || destinationMode == 1)) &&
            (sourceMode != 7 || sourceRegister <= 4) &&
            (destinationMode != 7 || destinationRegister <= 1);
        BeginInstruction(opcode);
        _ = FetchWord();
        if (!valid)
        {
            RaiseFormat0Exception(4, State.LastInstructionProgramCounter, M68kInstructionTimingKey.IllegalInstruction);
            return true;
        }
        var size = top == 1 ? M68kOperandSize.Byte : top == 3 ? M68kOperandSize.Word : M68kOperandSize.Long;
        uint value;
        if (sourceMode < 2)
            value = sourceMode == 0 ? State.D[sourceRegister] : State.A[sourceRegister];
        else if (sourceMode == 7 && sourceRegister == 4)
            value = size == M68kOperandSize.Long ? FetchLong() : FetchWord();
        else
        {
            var source = ResolveMoveAddress(sourceMode, sourceRegister, size, opcode);
            value = ReadSized(source, size);
            if (sourceMode == 3) WriteGeneralRegister(true, sourceRegister,
                unchecked(source + M68kIntegerSemantics.AddressIncrement(sourceRegister, size)));
        }
        if (destinationMode == 0) WriteDataRegisterSized(destinationRegister, value, size);
        else if (destinationMode == 1)
            WriteGeneralRegister(true, destinationRegister, size == M68kOperandSize.Word ? unchecked((uint)(int)(short)value) : value);
        else
        {
            var destination = ResolveMoveAddress(destinationMode, destinationRegister, size, opcode);
            WriteSized(destination, value, size);
            if (destinationMode == 3) WriteGeneralRegister(true, destinationRegister,
                unchecked(destination + M68kIntegerSemantics.AddressIncrement(destinationRegister, size)));
        }
        if (destinationMode != 1) SetMoveFlags(value, size);
        // New shapes follow the bounded operand-shape policy. Existing admitted
        // routes keep their original plans; this is not physical timing certification.
        var cycles = _profile.FixedInstructionNativeCycles ?? 4 +
            MoveEaPolicyCycles(sourceMode, sourceRegister) +
            MoveEaPolicyCycles(destinationMode, destinationRegister) + _indexedTransferExtraCycles;
        var plan = _profile.Model == M68kAcceleratorModel.M68030
            ? M68kInstructionPlan.CreateHeadTail(M68kInstructionTimingKey.GeneralMove, "MOVE/MOVEA general EA", cycles, 2, 0)
            : M68kInstructionPlan.CreateFlat(M68kInstructionTimingKey.GeneralMove, "MOVE/MOVEA general EA", cycles);
        CompleteTimingPlan(plan);
        return true;
    }

    private static int MoveEaPolicyCycles(int mode, int register) => mode switch
    {
        0 or 1 => 0, 2 or 3 => 2, 4 => 3, 5 => 4, 6 => 5,
        7 => register switch { 0 or 2 => 4, 1 => 6, 3 => 5, _ => 2 }, _ => 0
    };

    private uint ResolveMoveAddress(int mode, int register, M68kOperandSize size, ushort opcode)
    {
        switch (mode)
        {
            case 2: case 3: return State.A[register];
            case 4:
                var decremented = unchecked(State.A[register] - M68kIntegerSemantics.AddressIncrement(register, size));
                WriteGeneralRegister(true, register, decremented);
                return decremented;
            case 5: return unchecked(State.A[register] + (uint)(int)(short)FetchWord());
            case 6: return CalculateIndexedOperandAddress(State.A[register], FetchWord(), opcode);
            case 7:
                switch (register)
                {
                    case 0: return unchecked((uint)(int)(short)FetchWord());
                    case 1: return FetchLong();
                    case 2:
                        var displacementPc = State.ProgramCounter;
                        return unchecked(displacementPc + (uint)(int)(short)FetchWord());
                    case 3:
                        var indexPc = State.ProgramCounter;
                        return CalculateIndexedOperandAddress(indexPc, FetchWord(), opcode);
                }
                break;
        }
        throw new System.InvalidOperationException("Illegal MOVE memory EA reached execution.");
    }
}
