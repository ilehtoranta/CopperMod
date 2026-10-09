/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */
using System;
using CopperFloat;

namespace Copper68k
{
    // Shares integer, arithmetic and table-walk helpers with the 040. The 060
    // owns its PCR, stack, exception and three-long FPU state-frame contracts.
    internal sealed class M68060Interpreter : M68040Interpreter
    {
        private bool _debugInstructionHalted;

        private int _pendingFloatingPointException;
        private ExtF80 _exceptionOperand;
        public M68060Interpreter(IM68kBus bus)
            : base(bus, M68020CpuProfile.Ocs68060Accelerator8x, new M68kCpuState(),
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
            _pendingFloatingPointException = 0; _exceptionOperand = ExtF80.PositiveZero;
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
            if ((opcode & 0xFFC0) is 0xF300 or 0xF340)
                return (State.M68060ProcessorConfiguration & 2) != 0
                    ? ExecuteDisabledFpu(opcode) : ExecuteFpuStateTransfer(opcode);
            var kind = M68020OpcodeDispatchTable.M68020Kinds[opcode];
            if (kind is M68020OpcodeKind.Movep or M68020OpcodeKind.Chk2Cmp2 or M68020OpcodeKind.Cas2)
            {
                BeginInstruction(opcode);
                var pc = State.ProgramCounter;
                _ = FetchWord();
                RaiseFormat0Exception(61, pc, M68kInstructionTimingKey.IllegalInstruction);
                return true;
            }
            // Keep unassigned operand fields ahead of the inherited FPU decoder.
            if ((opcode & 0xFF80) == 0xF200 && (opcode & 0x3F) >= 0x3D)
            {
                BeginInstruction(opcode);
                _ = FetchWord();
                RaiseFormat0Exception(11, State.LastInstructionProgramCounter, M68kInstructionTimingKey.LineFException);
                return true;
            }
            if ((opcode & 0xFE00) == 0xF200 && (State.M68060ProcessorConfiguration & 2) != 0)
                return ExecuteDisabledFpu(opcode);
            if ((opcode & 0xFE00) == 0xF200 && _pendingFloatingPointException != 0)
            {
                BeginInstruction(opcode);
                RaiseFormat0Exception(_pendingFloatingPointException, State.ProgramCounter, M68kInstructionTimingKey.LineFException);
                return true;
            }
            if ((opcode & 0xFFC0) == 0xF240)
            {
                // FScc/FDBcc/FTRAPcc are implemented by the 040, but require
                // the software package on the 060 (MC68060UM table 6-11).
                BeginInstruction(opcode); _ = FetchWord(); _ = FetchWord();
                var mode = (opcode >> 3) & 7; var register = opcode & 7;
                uint ea = 0;
                if (mode == 1 || mode == 7 && register == 2) _ = FetchWord();
                else if (mode == 7 && register == 3) _ = FetchLong();
                else if (mode >= 2 && !(mode == 7 && register == 4)) ea = DecodeFpuTrapAddress(mode, register, 1);
                State.M68040Fpu.Fpiar = State.LastInstructionProgramCounter;
                RaiseFpuFormatException(2, 11, State.ProgramCounter, ea);
                return true;
            }
            return base.TryExecuteModelSpecificInstruction(opcode);
        }

        protected override bool ExecuteFpuCommand(ushort opcode, ushort extension, bool useFastTiming)
        {
            var mode = (opcode >> 3) & 7; var register = opcode & 7;
            var immediate = mode == 7 && register == 4;
            var dynamicList = (extension & 0xC000) == 0xC000 && (extension & 0x0800) != 0;
            var unimplementedImmediate = immediate && (
                (extension & 0xE000) == 0x4000 && ((extension >> 10) & 7) is 2 or 3 ||
                (extension & 0xE000) == 0x8000 && System.Numerics.BitOperations.PopCount((uint)(extension & 0x1C00)) > 1);
            if (dynamicList || unimplementedImmediate)
            {
                uint ea = 0;
                if (immediate)
                {
                    var bytes = (extension & 0x8000) != 0
                        ? 4 * System.Numerics.BitOperations.PopCount((uint)(extension & 0x1C00))
                        : (int)FpuFormatByteSize((extension >> 10) & 7);
                    for (var offset = 0; offset < bytes; offset += 2) _ = FetchWord();
                }
                else if (mode >= 2) ea = DecodeFpuTrapAddress(mode, register, 12);
                State.M68040Fpu.Fpiar = State.LastInstructionProgramCounter;
                RaiseFpuFormatException(2, 11, State.ProgramCounter, ea); return true;
            }
            return base.ExecuteFpuCommand(opcode, extension, useFastTiming);
        }

        private uint DecodeFpuTrapAddress(int mode, int register, uint bytes)
        {
            var old = State.A[register];
            var ea = GetFpuMemoryAddress(mode, register, bytes);
            if (mode is 3 or 4) WriteGeneralRegister(true, register, old);
            return ea;
        }

        private bool ExecuteDisabledFpu(ushort opcode)
        {
            BeginInstruction(opcode); var pc = State.ProgramCounter; _ = FetchWord();
            uint ea = 0;
            var stateTransfer = (opcode & 0xFFC0) is 0xF300 or 0xF340;
            if (stateTransfer && (State.StatusRegister & M68kCpuState.Supervisor) == 0)
            { RaiseFormat0Exception(8, pc, M68kInstructionTimingKey.PrivilegeViolation); return true; }
            if ((opcode & 0xFFC0) is 0xF280 or 0xF2C0)
            {
                if ((opcode & 0x40) != 0) _ = FetchLong(); else _ = FetchWord();
            }
            else if ((opcode & 0xFFC0) == 0xF240)
            {
                _ = FetchWord(); var mode = (opcode >> 3) & 7; var register = opcode & 7;
                if (mode == 1 || mode == 7 && register == 2) _ = FetchWord();
                else if (mode == 7 && register == 3) _ = FetchLong();
                else if (mode >= 2 && !(mode == 7 && register == 4)) ea = DecodeFpuTrapAddress(mode, register, 1);
            }
            else
            {
                var extension = stateTransfer ? (ushort)0 : FetchWord();
                var mode = (opcode >> 3) & 7; var register = opcode & 7;
                var bytes = stateTransfer ? 12u : (extension & 0x8000) != 0 ? 4u : FpuFormatByteSize((extension >> 10) & 7);
                if (mode == 7 && register == 4)
                    for (var i = 0u; i < Math.Max(2u, bytes); i += 2) _ = FetchWord();
                else if (mode >= 2)
                {
                    var oldAddress = State.A[register];
                    ea = GetFpuMemoryAddress(mode, register, bytes);
                    if (mode is 3 or 4) WriteGeneralRegister(true, register, oldAddress);
                }
            }
            var sr = State.StatusRegister;
            State.RecordException(11, State.ProgramCounter, sr);
            State.StatusRegister = (ushort)((sr | M68kCpuState.Supervisor) & ~M68kCpuState.Trace);
            PushLong(pc); PushLong(ea); PushWord(0x402C); PushLong(State.ProgramCounter); PushWord(sr);
            State.ProgramCounter = ReadLong(State.VectorBaseRegister + 44); DiscardInstructionPrefetch();
            CompleteTiming(M68kInstructionTimingKey.LineFException); return true;
        }

        private bool ExecuteFpuStateTransfer(ushort opcode)
        {
            BeginInstruction(opcode);
            var pc = State.ProgramCounter;
            _ = FetchWord();
            if (!IsValidCoprocessorStateFrameEa(opcode))
            {
                RaiseFormat0Exception(11, pc, M68kInstructionTimingKey.LineFException);
                return true;
            }
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
            var valid = mode is 2 or 5 or 6 || restore && mode == 3 || !restore && mode == 4 ||
                mode == 7 && register <= (restore ? 3 : 1);
            if (!valid) { RaiseFormat0Exception(11, pc, M68kInstructionTimingKey.LineFException); return true; }
            address = DecodeFpuTrapAddress(mode, register, 12);
            var fpu = State.M68040Fpu;
            if (restore)
            {
                // MC68060UM D-13..18: every state frame occupies THREE longs,
                // including NULL. The format is byte 2, unlike the MC68040.
                var header = ReadLong(address);
                var format = (header >> 8) & 0xFF;
                if (format is not (0 or 0x60 or 0xE0))
                {
                    RaiseFormat0Exception(14, pc, M68kInstructionTimingKey.FormatError);
                    return true;
                }
                var high = ReadLong(address + 4); var low = ReadLong(address + 8);
                _pendingFloatingPointException = 0;
                if (format == 0) fpu.Reset();
                else if (format == 0x60) fpu.RestoreStateFrame(M68040FpuFrameKind.Idle);
                else
                {
                    fpu.RestoreStateFrame(M68040FpuFrameKind.Busy);
                    _pendingFloatingPointException = 48 + (int)(header & 7);
                    _exceptionOperand = ExtF80.FromBits((ushort)(header >> 16), ((ulong)high << 32) | low);
                }
                if (mode == 3) WriteGeneralRegister(true, register, address + 12);
            }
            else
            {
                var exceptional = _pendingFloatingPointException != 0;
                WriteLong(address, exceptional ? ((uint)_exceptionOperand.SignExponent << 16) | 0xE000u | (uint)(_pendingFloatingPointException - 48)
                    : fpu.StateFrameKind == M68040FpuFrameKind.Null ? 0u : 0x6000u);
                WriteLong(address + 4, exceptional ? (uint)(_exceptionOperand.Significand >> 32) : 0);
                WriteLong(address + 8, exceptional ? (uint)_exceptionOperand.Significand : 0);
                _pendingFloatingPointException = 0; fpu.ConsumeSavedStateFrame();
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

        // MC68060UM 8.2.4: an undefined MOVEC control field is illegal.
        protected override bool IsMovecControlEncodingValid(int register) =>
            register is >= 0 and <= 8 or 0x800 or 0x801 or 0x806 or 0x807 or 0x808;

        protected override bool TryReadControlRegister(int register, uint pc, out uint value)
        {
            switch (register)
            {
                case 0x008: value = State.M68060BusControl; return true;
                case 0x800: value = State.UserStackPointer; return true;
                case 0x808: value = State.M68060ProcessorConfiguration; return true;
                // MC68060UM 4.1.2: bits 31-16 and bit 0 always read zero.
                case 0x003: value = State.M68040Mmu.TranslationControl & 0xFFFE; return true;
                // MC68060UM 4.1.3 has the same TTR zero-read mask as 040.
                case 0x004: value = State.M68040Mmu.InstructionTransparentTranslation0 & 0xFFFF_E364; return true;
                case 0x005: value = State.M68040Mmu.InstructionTransparentTranslation1 & 0xFFFF_E364; return true;
                case 0x006: value = State.M68040Mmu.DataTransparentTranslation0 & 0xFFFF_E364; return true;
                case 0x007: value = State.M68040Mmu.DataTransparentTranslation1 & 0xFFFF_E364; return true;
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
                    // L/LE are software commands; SL/SLE retain exception state.
                    State.M68060BusControl = (State.M68060BusControl & 0x5000_0000) | (value & 0xA000_0000);
                    return true;
                case 0x800: State.SetUserStackPointer(value); return true;
                case 0x808:
                    State.M68060ProcessorConfiguration = 0x0430_0000 | (value & 0x83); return true;
                case 0x003:
                    State.M68040Mmu.TranslationControl = value & 0xFFFE; return true;
                case 0x004: case 0x005: case 0x006: case 0x007:
                    value &= 0xFFFF_E364;
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
            if (vector == 55)
            { _pendingFloatingPointException = vector; _exceptionOperand = State.M68040Fpu.StateFrameSource; }
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
            => new($"MC68060 execution does not yet support {feature} (opcode ${opcode:X4} at ${State.ProgramCounter:X8}).");

        protected override void RaiseFpuFormatException(int format, int vector, uint pc, uint ea)
        {
            if (vector is >= 48 and <= 55)
            {
                _pendingFloatingPointException = vector;
                _exceptionOperand = State.M68040Fpu.StateFrameSource;
                // Arithmetic to FP registers reports at the next FP instruction.
                // FMOVE OUT reports immediately using a format-3 frame.
                if (vector != 55 && (State.M68040Fpu.StateFrameCommand & 0xE000) != 0x6000)
                { CompleteTiming(M68kInstructionTimingKey.FpuControlMove); return; }
            }
            var sr = State.StatusRegister; State.RecordException(vector, pc, sr);
            State.StatusRegister = (ushort)((sr | M68kCpuState.Supervisor) & ~M68kCpuState.Trace);
            PushLong(ea); PushWord((ushort)((format << 12) | vector * 4)); PushLong(pc); PushWord(sr);
            State.ProgramCounter = ReadLong(State.VectorBaseRegister + (uint)vector * 4);
            DiscardInstructionPrefetch(); CompleteTiming(M68kInstructionTimingKey.LineFException);
        }

        protected override void RaiseMmuFault(M68040MmuFault fault)
        {
            var sr = State.StatusRegister;
            var pc = fault.StackedProgramCounter ?? State.LastInstructionProgramCounter;
            State.RecordException(2, pc, sr);
            State.StatusRegister = (ushort)((sr | M68kCpuState.Supervisor) & ~M68kCpuState.Trace);
            var instruction = fault.AccessKind == M68kBusAccessKind.CpuInstructionFetch;
            var fc = ((sr & M68kCpuState.Supervisor) != 0 ? 4u : 0u) | (instruction ? 2u : 1u);
            var size = fault.ByteCount == 1 ? 0u : fault.ByteCount == 2 ? 1u : 2u;
            var fslw = fault.FaultReason | (instruction ? 0x8000u : 0u) |
                ((fault.Write ? 1u : 2u) << 23) | (size << 21) | (fc << 16);
            try
            {
                PushLong(fslw); PushLong(fault.LogicalAddress); PushWord(0x4008); PushLong(pc); PushWord(sr);
                State.ProgramCounter = ReadLong(State.VectorBaseRegister + 8);
                DiscardInstructionPrefetch(); CompleteTiming(M68kInstructionTimingKey.IllegalInstruction);
            }
            catch (M68040MmuFaultException) { State.Halted = true; }
        }
    }
}
