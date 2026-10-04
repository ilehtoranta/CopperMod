/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */
using System;

namespace Copper68k
{
    // Initial integer-only profile. Unsupported FPU/MMU use stops explicitly;
    // it must never silently acquire the MC68040's different architecture.
    internal sealed class M68060Interpreter : M68kAdvancedTimingInterpreter
    {
        private bool _debugInstructionHalted;

        public M68060Interpreter(IM68kBus bus)
            : base(bus, M68020CpuProfile.Ocs68060Accelerator8x, new M68kCpuState(),
                enableM68020StackMode: false, hasModelSpecificInstructions: true,
                enableAdvancedFastPath: false)
        {
            State.EnableM68060StackMode();
        }

        public override void Reset(uint programCounter, uint stackPointer)
        {
            base.Reset(programCounter, stackPointer);
            _debugInstructionHalted = false;
            State.EnableM68060StackMode();
            State.M68060ProcessorConfiguration = 0x0430_0000; // First revision, ESS/DFP clear.
            State.M68060BusControl = 0;
        }

        protected override bool TryExecuteModelSpecificInstruction(ushort opcode)
        {
            if (opcode is 0x4AC8 or 0x4ACC)
            {
                // MC68060UM 9.2.2: these reuse TAS mode-1 words on earlier
                // processors. PULSE changes no integer state; physical PST and
                // debug-port pipeline commands are outside this bus boundary.
                BeginInstruction(opcode);
                var pc = State.ProgramCounter;
                _ = FetchWord();
                if (opcode == 0x4AC8)
                {
                    if (!State.GetFlag(M68kCpuState.Supervisor))
                    {
                        RaiseFormat0Exception(8, pc, M68kInstructionTimingKey.PrivilegeViolation);
                        return true;
                    }
                    _debugInstructionHalted = true;
                    State.Halted = true;
                }
                CompleteTiming(M68kInstructionTimingKey.Nop);
                return true;
            }
            if (TryExecuteCacheMaintenance(opcode)) return true;
            if (opcode == 0x4E73) { Execute060Rte(); return true; }
            if ((opcode & 0xFFC0) is 0xF300 or 0xF340) return ExecuteFpuStateTransfer(opcode);
            var kind = M68020OpcodeDispatchTable.M68020Kinds[opcode];
            if (kind is M68020OpcodeKind.Movep or M68020OpcodeKind.Chk2Cmp2 or M68020OpcodeKind.Cas2)
            {
                BeginInstruction(opcode);
                var pc = State.ProgramCounter;
                _ = FetchWord();
                RaiseFormat0Exception(61, pc, M68kInstructionTimingKey.IllegalInstruction);
                return true;
            }
            if ((opcode & 0xFFF0) == 0xF200) return ExecuteFpuControlTransfer(opcode);
            if ((opcode & 0xFE00) == 0xF200)
                throw Unavailable("floating-point execution", opcode);
            if ((opcode & 0xFFE0) == 0xF500 || (opcode & 0xFFF8) is 0xF588 or 0xF5C8)
                throw Unavailable("MMU instructions", opcode);
            return false;
        }

        private bool ExecuteFpuControlTransfer(ushort opcode)
        {
            BeginInstruction(opcode);
            var pc = State.ProgramCounter;
            _ = FetchWord();
            var extension = FetchWord();
            var mask = extension & 0x1C00;
            var addressRegister = (opcode & 8) != 0;
            if ((extension & 0xC3FF) != 0x8000 || mask is not (0x1000 or 0x0800 or 0x0400) ||
                (addressRegister && mask != 0x0400))
                throw Unavailable("this floating-point operand form", opcode);
            if ((State.M68060ProcessorConfiguration & 2) != 0)
            {
                // MC68060UM 8.2.4: disabled FPU uses format 4, next PC and fault PC.
                var sr = State.StatusRegister;
                State.RecordException(11, State.ProgramCounter, sr);
                State.StatusRegister = (ushort)((sr | M68kCpuState.Supervisor) & ~M68kCpuState.Trace);
                PushLong(pc); PushLong(0); PushWord(0x402C);
                PushLong(State.ProgramCounter); PushWord(sr);
                State.ProgramCounter = ReadLong(State.VectorBaseRegister + 44);
                DiscardInstructionPrefetch();
                CompleteTiming(M68kInstructionTimingKey.LineFException);
                return true;
            }
            var fpu = State.M68040Fpu;
            if ((extension & 0x2000) == 0)
            {
                var value = ReadGeneralRegister(addressRegister, opcode & 7);
                if (mask == 0x1000) fpu.Fpcr = value;
                if (mask == 0x0800) fpu.Fpsr = value;
                if (mask == 0x0400) fpu.Fpiar = value;
                fpu.MarkInstructionExecuted();
            }
            else
                WriteGeneralRegister(addressRegister, opcode & 7,
                    mask == 0x1000 ? fpu.Fpcr : mask == 0x0800 ? fpu.Fpsr : fpu.Fpiar);
            CompleteTiming(M68kInstructionTimingKey.FpuControlMove);
            return true;
        }

        private bool ExecuteFpuStateTransfer(ushort opcode)
        {
            BeginInstruction(opcode);
            var pc = State.ProgramCounter;
            _ = FetchWord();
            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0)
            {
                RaiseFormat0Exception(8, pc, M68kInstructionTimingKey.PrivilegeViolation);
                return true;
            }
            if ((State.M68060ProcessorConfiguration & 2) != 0)
                throw Unavailable("disabled-FPU state transfer", opcode);
            var restore = (opcode & 0x40) != 0;
            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            uint address;
            if (mode == 2 || (restore && mode == 3)) address = State.A[register];
            else if (!restore && mode == 4) address = State.A[register] - 12;
            else if (mode == 5) address = unchecked(State.A[register] + (uint)(int)(short)FetchWord());
            else if (mode == 7 && register == 0) address = unchecked((uint)(int)(short)FetchWord());
            else if (mode == 7 && register == 1) address = FetchLong();
            else throw Unavailable("this FPU state-transfer address mode", opcode);
            var fpu = State.M68040Fpu;
            if (restore)
            {
                // MC68060UM D-13..18: every state frame occupies THREE longs,
                // including NULL. The format is byte 2, unlike the MC68040.
                var format = (ReadLong(address) >> 8) & 0xFF;
                if (format == 0) fpu.Reset();
                else if (format == 0x60) fpu.MarkInstructionExecuted();
                else if (format == 0xE0) throw Unavailable("exceptional FPU state restoration", opcode);
                else
                {
                    RaiseFormat0Exception(14, pc, M68kInstructionTimingKey.FormatError);
                    return true;
                }
                _ = ReadLong(address + 4); _ = ReadLong(address + 8);
                if (mode == 3) WriteGeneralRegister(true, register, address + 12);
            }
            else
            {
                WriteLong(address, fpu.StateFrameKind == M68040FpuFrameKind.Null ? 0u : 0x6000u);
                WriteLong(address + 4, 0); WriteLong(address + 8, 0);
                if (mode == 4) WriteGeneralRegister(true, register, address);
            }
            CompleteTiming(M68kInstructionTimingKey.FpuControlMove);
            return true;
        }

        private void Execute060Rte()
        {
            BeginInstruction(0x4E73);
            var pc = State.ProgramCounter;
            _ = FetchWord();
            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0)
            {
                RaiseFormat0Exception(8, pc, M68kInstructionTimingKey.PrivilegeViolation);
                return;
            }
            var sp = State.A[7];
            var sr = ReadWord(sp);
            var restoredPc = ReadLong(sp + 2);
            var format = ReadWord(sp + 6) >> 12;
            var size = format switch { 0 => 8u, 2 or 3 => 12u, 4 => 16u, _ => 0u };
            if (size == 0)
            {
                RaiseFormat0Exception(14, pc, M68kInstructionTimingKey.FormatError);
                return;
            }
            State.SetActiveStackPointer(sp + size);
            State.StatusRegister = sr;
            State.ProgramCounter = restoredPc;
            DiscardInstructionPrefetch();
            CompleteTiming(M68kInstructionTimingKey.Rte);
        }

        protected override bool TryReadControlRegister(int register, uint pc, out uint value)
        {
            switch (register)
            {
                case 0x008: value = State.M68060BusControl; return true;
                case 0x800: value = State.UserStackPointer; return true;
                case 0x808: value = State.M68060ProcessorConfiguration; return true;
                // MC68060UM 4.1.2: bits 31-16 and bit 0 always read zero.
                case 0x003: value = State.M68040Mmu.TranslationControl & 0xFFFE; return true;
                case 0x004: value = State.M68040Mmu.InstructionTransparentTranslation0; return true;
                case 0x005: value = State.M68040Mmu.InstructionTransparentTranslation1; return true;
                case 0x006: value = State.M68040Mmu.DataTransparentTranslation0; return true;
                case 0x007: value = State.M68040Mmu.DataTransparentTranslation1; return true;
                case 0x806: value = State.M68040Mmu.UserRootPointer; return true;
                case 0x807: value = State.M68040Mmu.SupervisorRootPointer; return true;
                case 0x802: case 0x803: case 0x804: case 0x805:
                    value = RaiseIllegalControlRegister(pc); return false;
                default: return base.TryReadControlRegister(register, pc, out value);
            }
        }

        protected override bool TryWriteControlRegister(int register, uint value, uint pc)
        {
            switch (register)
            {
                case 0x002:
                    // Branch-cache commands self-clear; branch prediction and
                    // cache capacity are outside the current execution policy.
                    return base.TryWriteControlRegister(register, value & 0xF880_E000u, pc);
                case 0x008:
                    State.M68060BusControl = value & 0xF000_0000; return true;
                case 0x800: State.SetUserStackPointer(value); return true;
                case 0x808:
                    State.M68060ProcessorConfiguration = 0x0430_0000 | (value & 0x83); return true;
                case 0x003:
                    if ((value & 0x8000) != 0) throw Unavailable("enabled MMU translation", State.LastOpcode);
                    State.M68040Mmu.TranslationControl = value & 0xFFFE; return true;
                case 0x004: case 0x005: case 0x006: case 0x007:
                    if ((value & 0x8000) != 0) throw Unavailable("enabled transparent translation", State.LastOpcode);
                    if (register == 4) State.M68040Mmu.InstructionTransparentTranslation0 = value;
                    if (register == 5) State.M68040Mmu.InstructionTransparentTranslation1 = value;
                    if (register == 6) State.M68040Mmu.DataTransparentTranslation0 = value;
                    if (register == 7) State.M68040Mmu.DataTransparentTranslation1 = value;
                    return true;
                case 0x806: State.M68040Mmu.UserRootPointer = value & 0xFFFF_FE00; return true;
                case 0x807: State.M68040Mmu.SupervisorRootPointer = value & 0xFFFF_FE00; return true;
                case 0x802: case 0x803: case 0x804: case 0x805:
                    _ = RaiseIllegalControlRegister(pc); return false;
                default: return base.TryWriteControlRegister(register, value, pc);
            }
        }

        internal override void RaiseFormat0Exception(int vector, uint pc, M68kInstructionTimingKey timingKey)
        {
            // MC68060UM C.2: unimplemented integers use format 0 and the faulting
            // opcode PC. Only interrupts clear the software M bit (11.1.2).
            var sr = State.StatusRegister;
            State.RecordException(vector, pc, sr);
            State.StatusRegister = (ushort)((sr | M68kCpuState.Supervisor) & ~M68kCpuState.Trace);
            if (vector is 5 or 6 or 7 or 9) PushLong(State.LastInstructionProgramCounter);
            PushWord((ushort)((vector is 5 or 6 or 7 or 9 ? 0x2000 : 0) | (vector * 4)));
            PushLong(pc);
            PushWord(sr);
            State.ProgramCounter = ReadLong(State.VectorBaseRegister + (uint)vector * 4);
            DiscardInstructionPrefetch();
            CompleteTiming(timingKey);
        }

        public override void RequestInterrupt(int level, uint vectorAddress)
        {
            if (_debugInstructionHalted) return;
            if (level <= 0 || level != 7 && level <= ((State.StatusRegister >> 8) & 7)) return;
            var sr = State.StatusRegister;
            var pc = State.ProgramCounter;
            State.Stopped = false;
            State.RecordException((int)(vectorAddress / 4), pc, sr);
            State.StatusRegister = (ushort)((sr & 0x28FF) | M68kCpuState.Supervisor | ((level & 7) << 8));
            PushWord((ushort)(vectorAddress & 0xFFF));
            PushLong(pc);
            PushWord(sr);
            State.ProgramCounter = ReadLong(State.VectorBaseRegister + vectorAddress);
            DiscardInstructionPrefetch();
            CompleteTiming(M68kInstructionTimingKey.InterruptAcknowledge);
        }

        public override void BeginSubroutine(uint address, uint stackPointer, uint returnAddress)
        {
            if (_debugInstructionHalted) return;
            base.BeginSubroutine(address, stackPointer, returnAddress);
        }

        private M68kEmulationException Unavailable(string feature, ushort opcode)
            => new($"MC68060 experimental integer profile does not yet support {feature} (opcode ${opcode:X4} at ${State.ProgramCounter:X8}).");
    }
}
