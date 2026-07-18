/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace Copper6510
{
    public sealed partial class Mos6510
    {
        private static readonly OpcodeDescriptor[] OpcodeTable =
        [
            D(Op.Brk, Mode.Brk, 7), D(Op.Ora, Mode.IndirectX, 6), D(Op.Jam, Mode.Jam, 1), D(Op.Slo, Mode.IndirectX, 8),
            D(Op.Nop, Mode.ZeroPage, 3), D(Op.Ora, Mode.ZeroPage, 3), D(Op.Asl, Mode.ZeroPage, 5), D(Op.Slo, Mode.ZeroPage, 5),
            D(Op.Php, Mode.Push, 3), D(Op.Ora, Mode.Immediate, 2), D(Op.Asl, Mode.Accumulator, 2), D(Op.Anc, Mode.Immediate, 2),
            D(Op.Nop, Mode.Absolute, 4), D(Op.Ora, Mode.Absolute, 4), D(Op.Asl, Mode.Absolute, 6), D(Op.Slo, Mode.Absolute, 6),

            D(Op.Bpl, Mode.Relative, 2), D(Op.Ora, Mode.IndirectY, 5), D(Op.Jam, Mode.Jam, 1), D(Op.Slo, Mode.IndirectY, 8),
            D(Op.Nop, Mode.ZeroPageX, 4), D(Op.Ora, Mode.ZeroPageX, 4), D(Op.Asl, Mode.ZeroPageX, 6), D(Op.Slo, Mode.ZeroPageX, 6),
            D(Op.Clc, Mode.Implied, 2), D(Op.Ora, Mode.AbsoluteY, 4), D(Op.Nop, Mode.Implied, 2), D(Op.Slo, Mode.AbsoluteY, 7),
            D(Op.Nop, Mode.AbsoluteX, 4), D(Op.Ora, Mode.AbsoluteX, 4), D(Op.Asl, Mode.AbsoluteX, 7), D(Op.Slo, Mode.AbsoluteX, 7),

            D(Op.Jsr, Mode.Jsr, 6), D(Op.And, Mode.IndirectX, 6), D(Op.Jam, Mode.Jam, 1), D(Op.Rla, Mode.IndirectX, 8),
            D(Op.Bit, Mode.ZeroPage, 3), D(Op.And, Mode.ZeroPage, 3), D(Op.Rol, Mode.ZeroPage, 5), D(Op.Rla, Mode.ZeroPage, 5),
            D(Op.Plp, Mode.Pull, 4), D(Op.And, Mode.Immediate, 2), D(Op.Rol, Mode.Accumulator, 2), D(Op.Anc, Mode.Immediate, 2),
            D(Op.Bit, Mode.Absolute, 4), D(Op.And, Mode.Absolute, 4), D(Op.Rol, Mode.Absolute, 6), D(Op.Rla, Mode.Absolute, 6),

            D(Op.Bmi, Mode.Relative, 2), D(Op.And, Mode.IndirectY, 5), D(Op.Jam, Mode.Jam, 1), D(Op.Rla, Mode.IndirectY, 8),
            D(Op.Nop, Mode.ZeroPageX, 4), D(Op.And, Mode.ZeroPageX, 4), D(Op.Rol, Mode.ZeroPageX, 6), D(Op.Rla, Mode.ZeroPageX, 6),
            D(Op.Sec, Mode.Implied, 2), D(Op.And, Mode.AbsoluteY, 4), D(Op.Nop, Mode.Implied, 2), D(Op.Rla, Mode.AbsoluteY, 7),
            D(Op.Nop, Mode.AbsoluteX, 4), D(Op.And, Mode.AbsoluteX, 4), D(Op.Rol, Mode.AbsoluteX, 7), D(Op.Rla, Mode.AbsoluteX, 7),

            D(Op.Rti, Mode.Rti, 6), D(Op.Eor, Mode.IndirectX, 6), D(Op.Jam, Mode.Jam, 1), D(Op.Sre, Mode.IndirectX, 8),
            D(Op.Nop, Mode.ZeroPage, 3), D(Op.Eor, Mode.ZeroPage, 3), D(Op.Lsr, Mode.ZeroPage, 5), D(Op.Sre, Mode.ZeroPage, 5),
            D(Op.Pha, Mode.Push, 3), D(Op.Eor, Mode.Immediate, 2), D(Op.Lsr, Mode.Accumulator, 2), D(Op.Alr, Mode.Immediate, 2),
            D(Op.Jmp, Mode.AbsoluteJump, 3), D(Op.Eor, Mode.Absolute, 4), D(Op.Lsr, Mode.Absolute, 6), D(Op.Sre, Mode.Absolute, 6),

            D(Op.Bvc, Mode.Relative, 2), D(Op.Eor, Mode.IndirectY, 5), D(Op.Jam, Mode.Jam, 1), D(Op.Sre, Mode.IndirectY, 8),
            D(Op.Nop, Mode.ZeroPageX, 4), D(Op.Eor, Mode.ZeroPageX, 4), D(Op.Lsr, Mode.ZeroPageX, 6), D(Op.Sre, Mode.ZeroPageX, 6),
            D(Op.Cli, Mode.Implied, 2), D(Op.Eor, Mode.AbsoluteY, 4), D(Op.Nop, Mode.Implied, 2), D(Op.Sre, Mode.AbsoluteY, 7),
            D(Op.Nop, Mode.AbsoluteX, 4), D(Op.Eor, Mode.AbsoluteX, 4), D(Op.Lsr, Mode.AbsoluteX, 7), D(Op.Sre, Mode.AbsoluteX, 7),

            D(Op.Rts, Mode.Rts, 6), D(Op.Adc, Mode.IndirectX, 6), D(Op.Jam, Mode.Jam, 1), D(Op.Rra, Mode.IndirectX, 8),
            D(Op.Nop, Mode.ZeroPage, 3), D(Op.Adc, Mode.ZeroPage, 3), D(Op.Ror, Mode.ZeroPage, 5), D(Op.Rra, Mode.ZeroPage, 5),
            D(Op.Pla, Mode.Pull, 4), D(Op.Adc, Mode.Immediate, 2), D(Op.Ror, Mode.Accumulator, 2), D(Op.Arr, Mode.Immediate, 2),
            D(Op.Jmp, Mode.IndirectJump, 5), D(Op.Adc, Mode.Absolute, 4), D(Op.Ror, Mode.Absolute, 6), D(Op.Rra, Mode.Absolute, 6),

            D(Op.Bvs, Mode.Relative, 2), D(Op.Adc, Mode.IndirectY, 5), D(Op.Jam, Mode.Jam, 1), D(Op.Rra, Mode.IndirectY, 8),
            D(Op.Nop, Mode.ZeroPageX, 4), D(Op.Adc, Mode.ZeroPageX, 4), D(Op.Ror, Mode.ZeroPageX, 6), D(Op.Rra, Mode.ZeroPageX, 6),
            D(Op.Sei, Mode.Implied, 2), D(Op.Adc, Mode.AbsoluteY, 4), D(Op.Nop, Mode.Implied, 2), D(Op.Rra, Mode.AbsoluteY, 7),
            D(Op.Nop, Mode.AbsoluteX, 4), D(Op.Adc, Mode.AbsoluteX, 4), D(Op.Ror, Mode.AbsoluteX, 7), D(Op.Rra, Mode.AbsoluteX, 7),

            D(Op.Nop, Mode.Immediate, 2), D(Op.Sta, Mode.IndirectX, 6), D(Op.Nop, Mode.Immediate, 2), D(Op.Sax, Mode.IndirectX, 6),
            D(Op.Sty, Mode.ZeroPage, 3), D(Op.Sta, Mode.ZeroPage, 3), D(Op.Stx, Mode.ZeroPage, 3), D(Op.Sax, Mode.ZeroPage, 3),
            D(Op.Dey, Mode.Implied, 2), D(Op.Nop, Mode.Immediate, 2), D(Op.Txa, Mode.Implied, 2), D(Op.Xaa, Mode.Immediate, 2),
            D(Op.Sty, Mode.Absolute, 4), D(Op.Sta, Mode.Absolute, 4), D(Op.Stx, Mode.Absolute, 4), D(Op.Sax, Mode.Absolute, 4),

            D(Op.Bcc, Mode.Relative, 2), D(Op.Sta, Mode.IndirectY, 6), D(Op.Jam, Mode.Jam, 1), D(Op.Ahx, Mode.IndirectY, 6),
            D(Op.Sty, Mode.ZeroPageX, 4), D(Op.Sta, Mode.ZeroPageX, 4), D(Op.Stx, Mode.ZeroPageY, 4), D(Op.Sax, Mode.ZeroPageY, 4),
            D(Op.Tya, Mode.Implied, 2), D(Op.Sta, Mode.AbsoluteY, 5), D(Op.Txs, Mode.Implied, 2), D(Op.Tas, Mode.AbsoluteY, 5),
            D(Op.Shy, Mode.AbsoluteX, 5), D(Op.Sta, Mode.AbsoluteX, 5), D(Op.Shx, Mode.AbsoluteY, 5), D(Op.Ahx, Mode.AbsoluteY, 5),

            D(Op.Ldy, Mode.Immediate, 2), D(Op.Lda, Mode.IndirectX, 6), D(Op.Ldx, Mode.Immediate, 2), D(Op.Lax, Mode.IndirectX, 6),
            D(Op.Ldy, Mode.ZeroPage, 3), D(Op.Lda, Mode.ZeroPage, 3), D(Op.Ldx, Mode.ZeroPage, 3), D(Op.Lax, Mode.ZeroPage, 3),
            D(Op.Tay, Mode.Implied, 2), D(Op.Lda, Mode.Immediate, 2), D(Op.Tax, Mode.Implied, 2), D(Op.Lxa, Mode.Immediate, 2),
            D(Op.Ldy, Mode.Absolute, 4), D(Op.Lda, Mode.Absolute, 4), D(Op.Ldx, Mode.Absolute, 4), D(Op.Lax, Mode.Absolute, 4),

            D(Op.Bcs, Mode.Relative, 2), D(Op.Lda, Mode.IndirectY, 5), D(Op.Jam, Mode.Jam, 1), D(Op.Lax, Mode.IndirectY, 5),
            D(Op.Ldy, Mode.ZeroPageX, 4), D(Op.Lda, Mode.ZeroPageX, 4), D(Op.Ldx, Mode.ZeroPageY, 4), D(Op.Lax, Mode.ZeroPageY, 4),
            D(Op.Clv, Mode.Implied, 2), D(Op.Lda, Mode.AbsoluteY, 4), D(Op.Tsx, Mode.Implied, 2), D(Op.Las, Mode.AbsoluteY, 4),
            D(Op.Ldy, Mode.AbsoluteX, 4), D(Op.Lda, Mode.AbsoluteX, 4), D(Op.Ldx, Mode.AbsoluteY, 4), D(Op.Lax, Mode.AbsoluteY, 4),

            D(Op.Cpy, Mode.Immediate, 2), D(Op.Cmp, Mode.IndirectX, 6), D(Op.Nop, Mode.Immediate, 2), D(Op.Dcp, Mode.IndirectX, 8),
            D(Op.Cpy, Mode.ZeroPage, 3), D(Op.Cmp, Mode.ZeroPage, 3), D(Op.Dec, Mode.ZeroPage, 5), D(Op.Dcp, Mode.ZeroPage, 5),
            D(Op.Iny, Mode.Implied, 2), D(Op.Cmp, Mode.Immediate, 2), D(Op.Dex, Mode.Implied, 2), D(Op.Axs, Mode.Immediate, 2),
            D(Op.Cpy, Mode.Absolute, 4), D(Op.Cmp, Mode.Absolute, 4), D(Op.Dec, Mode.Absolute, 6), D(Op.Dcp, Mode.Absolute, 6),

            D(Op.Bne, Mode.Relative, 2), D(Op.Cmp, Mode.IndirectY, 5), D(Op.Jam, Mode.Jam, 1), D(Op.Dcp, Mode.IndirectY, 8),
            D(Op.Nop, Mode.ZeroPageX, 4), D(Op.Cmp, Mode.ZeroPageX, 4), D(Op.Dec, Mode.ZeroPageX, 6), D(Op.Dcp, Mode.ZeroPageX, 6),
            D(Op.Cld, Mode.Implied, 2), D(Op.Cmp, Mode.AbsoluteY, 4), D(Op.Nop, Mode.Implied, 2), D(Op.Dcp, Mode.AbsoluteY, 7),
            D(Op.Nop, Mode.AbsoluteX, 4), D(Op.Cmp, Mode.AbsoluteX, 4), D(Op.Dec, Mode.AbsoluteX, 7), D(Op.Dcp, Mode.AbsoluteX, 7),

            D(Op.Cpx, Mode.Immediate, 2), D(Op.Sbc, Mode.IndirectX, 6), D(Op.Nop, Mode.Immediate, 2), D(Op.Isc, Mode.IndirectX, 8),
            D(Op.Cpx, Mode.ZeroPage, 3), D(Op.Sbc, Mode.ZeroPage, 3), D(Op.Inc, Mode.ZeroPage, 5), D(Op.Isc, Mode.ZeroPage, 5),
            D(Op.Inx, Mode.Implied, 2), D(Op.Sbc, Mode.Immediate, 2), D(Op.Nop, Mode.Implied, 2), D(Op.Sbc, Mode.Immediate, 2),
            D(Op.Cpx, Mode.Absolute, 4), D(Op.Sbc, Mode.Absolute, 4), D(Op.Inc, Mode.Absolute, 6), D(Op.Isc, Mode.Absolute, 6),

            D(Op.Beq, Mode.Relative, 2), D(Op.Sbc, Mode.IndirectY, 5), D(Op.Jam, Mode.Jam, 1), D(Op.Isc, Mode.IndirectY, 8),
            D(Op.Nop, Mode.ZeroPageX, 4), D(Op.Sbc, Mode.ZeroPageX, 4), D(Op.Inc, Mode.ZeroPageX, 6), D(Op.Isc, Mode.ZeroPageX, 6),
            D(Op.Sed, Mode.Implied, 2), D(Op.Sbc, Mode.AbsoluteY, 4), D(Op.Nop, Mode.Implied, 2), D(Op.Isc, Mode.AbsoluteY, 7),
            D(Op.Nop, Mode.AbsoluteX, 4), D(Op.Sbc, Mode.AbsoluteX, 4), D(Op.Inc, Mode.AbsoluteX, 7), D(Op.Isc, Mode.AbsoluteX, 7)
        ];

        private OpcodeDescriptor _instructionDescriptor;
        private byte _instructionOperand;
        private byte _instructionData;
        private byte _instructionResult;
        private byte _instructionInitialStatus;
        private ushort _instructionBaseAddress;
        private ushort _instructionAddress;
        private bool _instructionPageCrossed;

        private static OpcodeDescriptor D(Op operation, Mode mode, byte cycles)
            => new OpcodeDescriptor(operation, mode, cycles);

        private Mos6510CycleResult StepMicrocodedInstructionCycle(Mos6510OperationKind started)
        {
            var advanced = StepInstructionMicroOperation(out var completed);
            Cycles++;
            if (!advanced)
            {
                return new Mos6510CycleResult(started, Mos6510OperationKind.None, false);
            }

            _operationCycle++;
            if (!completed)
            {
                return new Mos6510CycleResult(started, Mos6510OperationKind.None, true);
            }

            var expectedCycles = _instructionDescriptor.Cycles;
            if (HasIndexedReadPagePenalty(_instructionDescriptor) && _instructionPageCrossed)
            {
                expectedCycles++;
            }
            else if (_instructionDescriptor.Mode == Mode.Relative && BranchCondition(_instructionDescriptor.Operation))
            {
                expectedCycles = (byte)(_instructionPageCrossed ? 4 : 3);
            }

            if (_operationCycle != expectedCycles)
            {
                throw new InvalidOperationException("The active opcode completed on an unexpected bus cycle.");
            }

            var operation = Halted ? Mos6510OperationKind.Halted : Mos6510OperationKind.Instruction;
            if (!Halted)
            {
                SampleInterruptsAfterInstruction(
                    _instructionInitialStatus,
                    LastOpcode,
                    _operationCycle);
            }

            _activeOperation = Mos6510OperationKind.None;
            return new Mos6510CycleResult(started, operation, true);
        }

        private bool StepInstructionMicroOperation(out bool completed)
        {
            completed = false;
            if (_operationCycle == 0)
            {
                if (!TryDirectRead(ProgramCounter, Mos6510BusAccessKind.OpcodeFetch, out var opcode))
                {
                    return false;
                }

                LastOpcode = opcode;
                ProgramCounter++;
                _instructionDescriptor = OpcodeTable[opcode];
                _instructionInitialStatus = Status;
                _instructionOperand = 0;
                _instructionData = 0;
                _instructionResult = 0;
                _instructionBaseAddress = 0;
                _instructionAddress = 0;
                _instructionPageCrossed = false;
                _unstableStoreRdyStall = false;
                if (_instructionDescriptor.Mode == Mode.Jam)
                {
                    Halted = true;
                    completed = true;
                }

                return true;
            }

            return _instructionDescriptor.Mode switch
            {
                Mode.Implied => StepImplied(out completed),
                Mode.Accumulator => StepAccumulator(out completed),
                Mode.Immediate => StepImmediate(out completed),
                Mode.ZeroPage => StepZeroPage(0, out completed),
                Mode.ZeroPageX => StepZeroPage(X, out completed),
                Mode.ZeroPageY => StepZeroPage(Y, out completed),
                Mode.Absolute => StepAbsolute(0, indexed: false, out completed),
                Mode.AbsoluteX => StepAbsolute(X, indexed: true, out completed),
                Mode.AbsoluteY => StepAbsolute(Y, indexed: true, out completed),
                Mode.IndirectX => StepIndirectX(out completed),
                Mode.IndirectY => StepIndirectY(out completed),
                Mode.Relative => StepBranch(out completed),
                Mode.Push => StepPush(out completed),
                Mode.Pull => StepPull(out completed),
                Mode.Jsr => StepJsr(out completed),
                Mode.Rts => StepRts(out completed),
                Mode.Rti => StepRti(out completed),
                Mode.Brk => StepBrk(out completed),
                Mode.AbsoluteJump => StepAbsoluteJump(out completed),
                Mode.IndirectJump => StepIndirectJump(out completed),
                _ => throw new InvalidOperationException("The active opcode has no microprogram.")
            };
        }

        private bool StepImplied(out bool completed)
        {
            completed = false;
            if (!TryDirectRead(ProgramCounter, Mos6510BusAccessKind.DummyRead, out _))
            {
                return false;
            }

            ApplyImpliedOperation(_instructionDescriptor.Operation);
            completed = true;
            return true;
        }

        private bool StepAccumulator(out bool completed)
        {
            completed = false;
            if (!TryDirectRead(ProgramCounter, Mos6510BusAccessKind.DummyRead, out _))
            {
                return false;
            }

            A = _instructionDescriptor.Operation switch
            {
                Op.Asl => Asl(A),
                Op.Lsr => Lsr(A),
                Op.Rol => Rol(A),
                Op.Ror => Ror(A),
                _ => throw new InvalidOperationException("Invalid accumulator microprogram.")
            };
            completed = true;
            return true;
        }

        private bool StepImmediate(out bool completed)
        {
            completed = false;
            if (!TryDirectRead(ProgramCounter, Mos6510BusAccessKind.OperandFetch, out var value))
            {
                return false;
            }

            ProgramCounter++;
            ApplyReadOperation(_instructionDescriptor.Operation, value);
            completed = true;
            return true;
        }

        private bool StepZeroPage(byte index, out bool completed)
        {
            completed = false;
            if (_operationCycle == 1)
            {
                if (!TryDirectRead(ProgramCounter, Mos6510BusAccessKind.OperandFetch, out _instructionOperand))
                {
                    return false;
                }

                ProgramCounter++;
                _instructionAddress = _instructionOperand;
                return true;
            }

            var indexed = _instructionDescriptor.Mode != Mode.ZeroPage;
            if (indexed && _operationCycle == 2)
            {
                if (!TryDirectRead(_instructionOperand, Mos6510BusAccessKind.DummyRead, out _))
                {
                    return false;
                }

                _instructionAddress = (byte)(_instructionOperand + index);
                return true;
            }

            var dataCycle = indexed ? 3 : 2;
            return StepEffectiveAddressAccess(dataCycle, out completed);
        }

        private bool StepAbsolute(byte index, bool indexed, out bool completed)
        {
            completed = false;
            if (_operationCycle == 1)
            {
                if (!TryDirectRead(ProgramCounter, Mos6510BusAccessKind.OperandFetch, out _instructionOperand))
                {
                    return false;
                }

                ProgramCounter++;
                return true;
            }

            if (_operationCycle == 2)
            {
                if (!TryDirectRead(ProgramCounter, Mos6510BusAccessKind.OperandFetch, out var high))
                {
                    return false;
                }

                ProgramCounter++;
                _instructionBaseAddress = (ushort)(_instructionOperand | (high << 8));
                _instructionAddress = (ushort)(_instructionBaseAddress + index);
                _instructionPageCrossed = PageCrossed(_instructionBaseAddress, _instructionAddress);
                return true;
            }

            if (indexed && _operationCycle == 3 &&
                (IsWriteOperation(_instructionDescriptor.Operation) ||
                 IsRmwOperation(_instructionDescriptor.Operation) ||
                 _instructionPageCrossed))
            {
                return TryInstructionDummyRead(
                    (ushort)((_instructionBaseAddress & 0xFF00) | (_instructionAddress & 0x00FF)));
            }

            var dataCycle = indexed &&
                (IsWriteOperation(_instructionDescriptor.Operation) ||
                 IsRmwOperation(_instructionDescriptor.Operation) ||
                 _instructionPageCrossed)
                ? 4
                : 3;
            return StepEffectiveAddressAccess(dataCycle, out completed);
        }

        private bool StepIndirectX(out bool completed)
        {
            completed = false;
            switch (_operationCycle)
            {
                case 1:
                    if (!TryDirectRead(ProgramCounter, Mos6510BusAccessKind.OperandFetch, out _instructionOperand))
                    {
                        return false;
                    }

                    ProgramCounter++;
                    return true;
                case 2:
                    if (!TryDirectRead(_instructionOperand, Mos6510BusAccessKind.DummyRead, out _))
                    {
                        return false;
                    }

                    _instructionOperand += X;
                    return true;
                case 3:
                    return TryDirectRead(_instructionOperand, Mos6510BusAccessKind.Read, out _instructionData);
                case 4:
                    if (!TryDirectRead((byte)(_instructionOperand + 1), Mos6510BusAccessKind.Read, out var high))
                    {
                        return false;
                    }

                    _instructionAddress = (ushort)(_instructionData | (high << 8));
                    return true;
                default:
                    return StepEffectiveAddressAccess(5, out completed);
            }
        }

        private bool StepIndirectY(out bool completed)
        {
            completed = false;
            switch (_operationCycle)
            {
                case 1:
                    if (!TryDirectRead(ProgramCounter, Mos6510BusAccessKind.OperandFetch, out _instructionOperand))
                    {
                        return false;
                    }

                    ProgramCounter++;
                    return true;
                case 2:
                    return TryDirectRead(_instructionOperand, Mos6510BusAccessKind.Read, out _instructionData);
                case 3:
                    if (!TryDirectRead((byte)(_instructionOperand + 1), Mos6510BusAccessKind.Read, out var high))
                    {
                        return false;
                    }

                    _instructionBaseAddress = (ushort)(_instructionData | (high << 8));
                    _instructionAddress = (ushort)(_instructionBaseAddress + Y);
                    _instructionPageCrossed = PageCrossed(_instructionBaseAddress, _instructionAddress);
                    return true;
            }

            if (_operationCycle == 4 &&
                (IsWriteOperation(_instructionDescriptor.Operation) ||
                 IsRmwOperation(_instructionDescriptor.Operation) ||
                 _instructionPageCrossed))
            {
                return TryInstructionDummyRead(
                    (ushort)((_instructionBaseAddress & 0xFF00) | (_instructionAddress & 0x00FF)));
            }

            var dataCycle = IsWriteOperation(_instructionDescriptor.Operation) ||
                IsRmwOperation(_instructionDescriptor.Operation) ||
                _instructionPageCrossed
                ? 5
                : 4;
            return StepEffectiveAddressAccess(dataCycle, out completed);
        }

        private bool StepEffectiveAddressAccess(int dataCycle, out bool completed)
        {
            completed = false;
            var operation = _instructionDescriptor.Operation;
            if (IsWriteOperation(operation))
            {
                if (_operationCycle != dataCycle)
                {
                    throw new InvalidOperationException("Store microprogram reached an unexpected cycle.");
                }

                var address = _instructionAddress;
                var value = StoreValue(operation, ref address);
                if (!TryDirectWrite(address, value, Mos6510BusAccessKind.Write))
                {
                    return false;
                }

                if (operation == Op.Tas)
                {
                    StackPointer = (byte)(A & X);
                }

                completed = true;
                return true;
            }

            if (!IsRmwOperation(operation))
            {
                if (_operationCycle != dataCycle)
                {
                    throw new InvalidOperationException("Read microprogram reached an unexpected cycle.");
                }

                if (!TryDirectRead(_instructionAddress, Mos6510BusAccessKind.Read, out var value))
                {
                    return false;
                }

                ApplyReadOperation(operation, value);
                completed = true;
                return true;
            }

            if (_operationCycle == dataCycle)
            {
                if (!TryDirectRead(_instructionAddress, Mos6510BusAccessKind.Read, out _instructionData))
                {
                    return false;
                }

                _instructionResult = PrepareRmwResult(operation, _instructionData);
                return true;
            }

            if (_operationCycle == dataCycle + 1)
            {
                return TryDirectWrite(
                    _instructionAddress,
                    _instructionData,
                    Mos6510BusAccessKind.DummyWrite);
            }

            if (_operationCycle != dataCycle + 2)
            {
                throw new InvalidOperationException("RMW microprogram reached an unexpected cycle.");
            }

            if (!TryDirectWrite(
                _instructionAddress,
                _instructionResult,
                Mos6510BusAccessKind.Write))
            {
                return false;
            }

            FinishRmwOperation(operation, _instructionResult);
            completed = true;
            return true;
        }

        private bool TryInstructionDummyRead(ushort address)
        {
            var busWasAvailable = _busAvailable;
            var advanced = TryDirectRead(address, Mos6510BusAccessKind.DummyRead, out _);
            if (!advanced && busWasAvailable && IsUnstableStore(LastOpcode))
            {
                _unstableStoreRdyStall = true;
            }

            return advanced;
        }

        private bool StepBranch(out bool completed)
        {
            completed = false;
            if (_operationCycle == 1)
            {
                if (!TryDirectRead(ProgramCounter, Mos6510BusAccessKind.OperandFetch, out _instructionOperand))
                {
                    return false;
                }

                ProgramCounter++;
                if (!BranchCondition(_instructionDescriptor.Operation))
                {
                    completed = true;
                }

                return true;
            }

            var sequentialAddress = ProgramCounter;
            var targetAddress = (ushort)(sequentialAddress + unchecked((sbyte)_instructionOperand));
            if (_operationCycle == 2)
            {
                if (!TryDirectRead(sequentialAddress, Mos6510BusAccessKind.DummyRead, out _))
                {
                    return false;
                }

                _instructionAddress = targetAddress;
                _instructionPageCrossed = PageCrossed(sequentialAddress, targetAddress);
                if (!_instructionPageCrossed)
                {
                    ProgramCounter = targetAddress;
                    completed = true;
                }

                return true;
            }

            if (_operationCycle != 3 || !_instructionPageCrossed)
            {
                throw new InvalidOperationException("Branch microprogram reached an unexpected cycle.");
            }

            if (!TryDirectRead(
                (ushort)((sequentialAddress & 0xFF00) | (targetAddress & 0x00FF)),
                Mos6510BusAccessKind.DummyRead,
                out _))
            {
                return false;
            }

            ProgramCounter = targetAddress;
            completed = true;
            return true;
        }

        private bool StepPush(out bool completed)
        {
            completed = false;
            if (_operationCycle == 1)
            {
                return TryDirectRead(ProgramCounter, Mos6510BusAccessKind.DummyRead, out _);
            }

            var value = _instructionDescriptor.Operation == Op.Pha
                ? A
                : (byte)(Status | Break | Unused);
            if (!TryDirectWrite(
                (ushort)(0x0100 | StackPointer),
                value,
                Mos6510BusAccessKind.StackWrite))
            {
                return false;
            }

            StackPointer--;
            completed = true;
            return true;
        }

        private bool StepPull(out bool completed)
        {
            completed = false;
            if (_operationCycle == 1)
            {
                return TryDirectRead(ProgramCounter, Mos6510BusAccessKind.DummyRead, out _);
            }

            if (_operationCycle == 2)
            {
                return TryDirectRead(
                    (ushort)(0x0100 | StackPointer),
                    Mos6510BusAccessKind.StackRead,
                    out _);
            }

            if (!TryDirectRead(
                (ushort)(0x0100 | (byte)(StackPointer + 1)),
                Mos6510BusAccessKind.StackRead,
                out var value))
            {
                return false;
            }

            StackPointer++;
            if (_instructionDescriptor.Operation == Op.Pla)
            {
                A = value;
                SetZn(A);
            }
            else
            {
                Status = (byte)((value & ~Break) | Unused);
            }

            completed = true;
            return true;
        }

        private bool StepJsr(out bool completed)
        {
            completed = false;
            switch (_operationCycle)
            {
                case 1:
                    if (!TryDirectRead(ProgramCounter, Mos6510BusAccessKind.OperandFetch, out _instructionOperand))
                    {
                        return false;
                    }

                    ProgramCounter++;
                    return true;
                case 2:
                    return TryDirectRead(
                        (ushort)(0x0100 | StackPointer),
                        Mos6510BusAccessKind.StackRead,
                        out _);
                case 3:
                    if (!TryDirectWrite(
                        (ushort)(0x0100 | StackPointer),
                        (byte)(ProgramCounter >> 8),
                        Mos6510BusAccessKind.StackWrite))
                    {
                        return false;
                    }

                    StackPointer--;
                    return true;
                case 4:
                    if (!TryDirectWrite(
                        (ushort)(0x0100 | StackPointer),
                        (byte)ProgramCounter,
                        Mos6510BusAccessKind.StackWrite))
                    {
                        return false;
                    }

                    StackPointer--;
                    return true;
                default:
                    if (!TryDirectRead(ProgramCounter, Mos6510BusAccessKind.OperandFetch, out var high))
                    {
                        return false;
                    }

                    ProgramCounter = (ushort)(_instructionOperand | (high << 8));
                    completed = true;
                    return true;
            }
        }

        private bool StepRts(out bool completed)
        {
            completed = false;
            switch (_operationCycle)
            {
                case 1:
                    return TryDirectRead(ProgramCounter, Mos6510BusAccessKind.DummyRead, out _);
                case 2:
                    return TryDirectRead((ushort)(0x0100 | StackPointer), Mos6510BusAccessKind.StackRead, out _);
                case 3:
                    if (!TryDirectRead((ushort)(0x0100 | (byte)(StackPointer + 1)), Mos6510BusAccessKind.StackRead, out _instructionOperand))
                    {
                        return false;
                    }

                    StackPointer++;
                    return true;
                case 4:
                    if (!TryDirectRead((ushort)(0x0100 | (byte)(StackPointer + 1)), Mos6510BusAccessKind.StackRead, out var high))
                    {
                        return false;
                    }

                    StackPointer++;
                    _instructionAddress = (ushort)(_instructionOperand | (high << 8));
                    return true;
                default:
                    if (!TryDirectRead(_instructionAddress, Mos6510BusAccessKind.DummyRead, out _))
                    {
                        return false;
                    }

                    ProgramCounter = (ushort)(_instructionAddress + 1);
                    completed = true;
                    return true;
            }
        }

        private bool StepRti(out bool completed)
        {
            completed = false;
            switch (_operationCycle)
            {
                case 1:
                    return TryDirectRead(ProgramCounter, Mos6510BusAccessKind.DummyRead, out _);
                case 2:
                    return TryDirectRead((ushort)(0x0100 | StackPointer), Mos6510BusAccessKind.StackRead, out _);
                case 3:
                    if (!TryDirectRead((ushort)(0x0100 | (byte)(StackPointer + 1)), Mos6510BusAccessKind.StackRead, out var status))
                    {
                        return false;
                    }

                    StackPointer++;
                    Status = (byte)((status & ~Break) | Unused);
                    return true;
                case 4:
                    if (!TryDirectRead((ushort)(0x0100 | (byte)(StackPointer + 1)), Mos6510BusAccessKind.StackRead, out _instructionOperand))
                    {
                        return false;
                    }

                    StackPointer++;
                    return true;
                default:
                    if (!TryDirectRead((ushort)(0x0100 | (byte)(StackPointer + 1)), Mos6510BusAccessKind.StackRead, out var high))
                    {
                        return false;
                    }

                    StackPointer++;
                    ProgramCounter = (ushort)(_instructionOperand | (high << 8));
                    completed = true;
                    return true;
            }
        }

        private bool StepBrk(out bool completed)
        {
            completed = false;
            switch (_operationCycle)
            {
                case 1:
                    if (!TryDirectRead(ProgramCounter, Mos6510BusAccessKind.OperandFetch, out _))
                    {
                        return false;
                    }

                    ProgramCounter++;
                    return true;
                case 2:
                    if (!TryDirectWrite((ushort)(0x0100 | StackPointer), (byte)(ProgramCounter >> 8), Mos6510BusAccessKind.StackWrite))
                    {
                        return false;
                    }

                    StackPointer--;
                    return true;
                case 3:
                    if (!TryDirectWrite((ushort)(0x0100 | StackPointer), (byte)ProgramCounter, Mos6510BusAccessKind.StackWrite))
                    {
                        return false;
                    }

                    StackPointer--;
                    return true;
                case 4:
                    if (!TryDirectWrite((ushort)(0x0100 | StackPointer), (byte)(Status | Break | Unused), Mos6510BusAccessKind.StackWrite))
                    {
                        return false;
                    }

                    StackPointer--;
                    return true;
                case 5:
                    if (_nmiPending)
                    {
                        _nmiPending = false;
                        _interruptVectorBase = 0xFFFA;
                    }
                    else
                    {
                        _interruptVectorBase = 0xFFFE;
                    }

                    return TryDirectRead(_interruptVectorBase, Mos6510BusAccessKind.VectorRead, out _interruptVectorLow);
                default:
                    if (!TryDirectRead((ushort)(_interruptVectorBase + 1), Mos6510BusAccessKind.VectorRead, out var high))
                    {
                        return false;
                    }

                    ProgramCounter = (ushort)(_interruptVectorLow | (high << 8));
                    SetFlag(InterruptDisable, true);
                    completed = true;
                    return true;
            }
        }

        private bool StepAbsoluteJump(out bool completed)
        {
            completed = false;
            if (_operationCycle == 1)
            {
                if (!TryDirectRead(ProgramCounter, Mos6510BusAccessKind.OperandFetch, out _instructionOperand))
                {
                    return false;
                }

                ProgramCounter++;
                return true;
            }

            if (!TryDirectRead(ProgramCounter, Mos6510BusAccessKind.OperandFetch, out var high))
            {
                return false;
            }

            ProgramCounter = (ushort)(_instructionOperand | (high << 8));
            completed = true;
            return true;
        }

        private bool StepIndirectJump(out bool completed)
        {
            completed = false;
            switch (_operationCycle)
            {
                case 1:
                    if (!TryDirectRead(ProgramCounter, Mos6510BusAccessKind.OperandFetch, out _instructionOperand))
                    {
                        return false;
                    }

                    ProgramCounter++;
                    return true;
                case 2:
                    if (!TryDirectRead(ProgramCounter, Mos6510BusAccessKind.OperandFetch, out var high))
                    {
                        return false;
                    }

                    ProgramCounter++;
                    _instructionBaseAddress = (ushort)(_instructionOperand | (high << 8));
                    return true;
                case 3:
                    return TryDirectRead(_instructionBaseAddress, Mos6510BusAccessKind.Read, out _instructionData);
                default:
                    var highAddress = (ushort)((_instructionBaseAddress & 0xFF00) | ((_instructionBaseAddress + 1) & 0x00FF));
                    if (!TryDirectRead(highAddress, Mos6510BusAccessKind.Read, out var targetHigh))
                    {
                        return false;
                    }

                    ProgramCounter = (ushort)(_instructionData | (targetHigh << 8));
                    completed = true;
                    return true;
            }
        }

        private void ApplyImpliedOperation(Op operation)
        {
            switch (operation)
            {
                case Op.Nop: break;
                case Op.Clc: SetFlag(Carry, false); break;
                case Op.Sec: SetFlag(Carry, true); break;
                case Op.Cli: SetFlag(InterruptDisable, false); break;
                case Op.Sei: SetFlag(InterruptDisable, true); break;
                case Op.Clv: SetFlag(Overflow, false); break;
                case Op.Cld: SetFlag(Decimal, false); break;
                case Op.Sed: SetFlag(Decimal, true); break;
                case Op.Dey: Y--; SetZn(Y); break;
                case Op.Txa: A = X; SetZn(A); break;
                case Op.Tya: A = Y; SetZn(A); break;
                case Op.Txs: StackPointer = X; break;
                case Op.Tay: Y = A; SetZn(Y); break;
                case Op.Tax: X = A; SetZn(X); break;
                case Op.Tsx: X = StackPointer; SetZn(X); break;
                case Op.Iny: Y++; SetZn(Y); break;
                case Op.Dex: X--; SetZn(X); break;
                case Op.Inx: X++; SetZn(X); break;
                default: throw new InvalidOperationException("Invalid implied microprogram.");
            }
        }

        private void ApplyReadOperation(Op operation, byte value)
        {
            switch (operation)
            {
                case Op.Nop: break;
                case Op.Ora: A |= value; SetZn(A); break;
                case Op.And: A &= value; SetZn(A); break;
                case Op.Eor: A ^= value; SetZn(A); break;
                case Op.Adc: if (GetFlag(Decimal)) DecimalAdc(value); else BinaryAdc(value); break;
                case Op.Sbc: if (GetFlag(Decimal)) DecimalSbc(value); else BinaryAdc((byte)~value); break;
                case Op.Bit:
                    SetFlag(Zero, (A & value) == 0);
                    SetFlag(Overflow, (value & Overflow) != 0);
                    SetFlag(Negative, (value & Negative) != 0);
                    break;
                case Op.Lda: A = value; SetZn(A); break;
                case Op.Ldx: X = value; SetZn(X); break;
                case Op.Ldy: Y = value; SetZn(Y); break;
                case Op.Lax: A = value; X = value; SetZn(value); break;
                case Op.Lxa: A = (byte)(A & value); X = A; SetZn(A); break;
                case Op.Las: Las(value); break;
                case Op.Cmp: CompareValue(A, value); break;
                case Op.Cpx: CompareValue(X, value); break;
                case Op.Cpy: CompareValue(Y, value); break;
                case Op.Anc: Anc(value); break;
                case Op.Alr: Alr(value); break;
                case Op.Arr: Arr(value); break;
                case Op.Axs: Axs(value); break;
                case Op.Xaa: A = (byte)(X & value); SetZn(A); break;
                default: throw new InvalidOperationException("Invalid read microprogram.");
            }
        }

        private byte PrepareRmwResult(Op operation, byte value)
        {
            return operation switch
            {
                Op.Asl or Op.Slo => Asl(value),
                Op.Lsr or Op.Sre => Lsr(value),
                Op.Rol or Op.Rla => Rol(value),
                Op.Ror or Op.Rra => Ror(value),
                Op.Inc => IncrementMemoryValue(value),
                Op.Dec => DecrementMemoryValue(value),
                Op.Dcp => (byte)(value - 1),
                Op.Isc => (byte)(value + 1),
                _ => throw new InvalidOperationException("Invalid RMW microprogram.")
            };
        }

        private byte IncrementMemoryValue(byte value)
        {
            value++;
            SetZn(value);
            return value;
        }

        private byte DecrementMemoryValue(byte value)
        {
            value--;
            SetZn(value);
            return value;
        }

        private void FinishRmwOperation(Op operation, byte value)
        {
            switch (operation)
            {
                case Op.Asl:
                case Op.Lsr:
                case Op.Rol:
                case Op.Ror:
                case Op.Inc:
                case Op.Dec:
                    return;
                case Op.Slo: A |= value; SetZn(A); return;
                case Op.Rla: A &= value; SetZn(A); return;
                case Op.Sre: A ^= value; SetZn(A); return;
                case Op.Rra: if (GetFlag(Decimal)) DecimalAdc(value); else BinaryAdc(value); return;
                case Op.Dcp: CompareValue(A, value); return;
                case Op.Isc: if (GetFlag(Decimal)) DecimalSbc(value); else BinaryAdc((byte)~value); return;
                default: throw new InvalidOperationException("Invalid RMW completion.");
            }
        }

        private byte StoreValue(Op operation, ref ushort address)
        {
            switch (operation)
            {
                case Op.Sta: return A;
                case Op.Stx: return X;
                case Op.Sty: return Y;
                case Op.Sax: return (byte)(A & X);
            }

            var raw = operation switch
            {
                Op.Ahx => (byte)(A & X),
                Op.Shx => X,
                Op.Shy => Y,
                Op.Tas => (byte)(A & X),
                _ => throw new InvalidOperationException("Invalid store microprogram.")
            };
            var highMask = (byte)(((_instructionBaseAddress >> 8) + 1) & 0xFF);
            var masked = (byte)(raw & highMask);
            if (_instructionPageCrossed)
            {
                address = (ushort)((masked << 8) | (address & 0x00FF));
            }

            return _unstableStoreRdyStall ? raw : masked;
        }

        private void CompareValue(byte register, byte value)
        {
            var result = register - value;
            SetFlag(Carry, register >= value);
            SetZn((byte)result);
        }

        private bool BranchCondition(Op operation)
        {
            return operation switch
            {
                Op.Bpl => !GetFlag(Negative),
                Op.Bmi => GetFlag(Negative),
                Op.Bvc => !GetFlag(Overflow),
                Op.Bvs => GetFlag(Overflow),
                Op.Bcc => !GetFlag(Carry),
                Op.Bcs => GetFlag(Carry),
                Op.Bne => !GetFlag(Zero),
                Op.Beq => GetFlag(Zero),
                _ => false
            };
        }

        private static bool IsWriteOperation(Op operation)
            => operation is Op.Sta or Op.Stx or Op.Sty or Op.Sax or Op.Ahx or Op.Shx or Op.Shy or Op.Tas;

        private static bool IsRmwOperation(Op operation)
            => operation is Op.Asl or Op.Lsr or Op.Rol or Op.Ror or Op.Inc or Op.Dec or
                Op.Slo or Op.Rla or Op.Sre or Op.Rra or Op.Dcp or Op.Isc;

        private static bool HasIndexedReadPagePenalty(OpcodeDescriptor descriptor)
            => (descriptor.Mode is Mode.AbsoluteX or Mode.AbsoluteY or Mode.IndirectY) &&
                !IsWriteOperation(descriptor.Operation) &&
                !IsRmwOperation(descriptor.Operation);

        private readonly struct OpcodeDescriptor
        {
            public OpcodeDescriptor(Op operation, Mode mode, byte cycles)
            {
                Operation = operation;
                Mode = mode;
                Cycles = cycles;
            }

            public Op Operation { get; }
            public Mode Mode { get; }
            public byte Cycles { get; }
        }

        private enum Mode : byte
        {
            Implied,
            Accumulator,
            Immediate,
            ZeroPage,
            ZeroPageX,
            ZeroPageY,
            Absolute,
            AbsoluteX,
            AbsoluteY,
            IndirectX,
            IndirectY,
            Relative,
            Push,
            Pull,
            Jsr,
            Rts,
            Rti,
            Brk,
            AbsoluteJump,
            IndirectJump,
            Jam
        }

        private enum Op : byte
        {
            Nop, Brk, Jam, Ora, And, Eor, Adc, Sbc, Bit,
            Asl, Lsr, Rol, Ror, Inc, Dec, Slo, Rla, Sre, Rra, Dcp, Isc,
            Lda, Ldx, Ldy, Lax, Lxa, Las,
            Sta, Stx, Sty, Sax, Ahx, Shx, Shy, Tas,
            Cmp, Cpx, Cpy, Anc, Alr, Arr, Axs, Xaa,
            Clc, Sec, Cli, Sei, Clv, Cld, Sed,
            Dey, Txa, Tya, Txs, Tay, Tax, Tsx, Iny, Dex, Inx,
            Pha, Php, Pla, Plp, Jsr, Rts, Rti, Jmp,
            Bpl, Bmi, Bvc, Bvs, Bcc, Bcs, Bne, Beq
        }
    }
}
