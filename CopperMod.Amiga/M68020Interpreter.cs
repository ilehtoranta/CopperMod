using System;
using System.Runtime.CompilerServices;

namespace CopperMod.Amiga
{
    internal sealed class M68020Interpreter : IM68kBatchCore, IM68kInstructionFrequencyProvider
    {
        private const uint SubroutineSentinel = 0xFFFF_FFFC;
        private const ushort Format0ExceptionFrame = 0x0000;
        private readonly IM68kBus _bus;
        private readonly M68020CpuProfile _profile;
        private readonly M68kInstructionFrequencyMatrix _instructionFrequency;
        private readonly M68kInterpreter _m68000Fallback;

        public M68020Interpreter(IM68kBus bus, M68020CpuProfile profile)
            : this(bus, profile, new M68kCpuState())
        {
        }

        public M68020Interpreter(
            IM68kBus bus,
            M68020CpuProfile profile,
            M68kCpuState state,
            M68kInstructionFrequencyMatrix? instructionFrequency = null)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            State = state ?? throw new ArgumentNullException(nameof(state));
            State.EnableM68020StackMode();
            _instructionFrequency = instructionFrequency ?? new M68kInstructionFrequencyMatrix();
            _m68000Fallback = new M68kInterpreter(bus, State, _instructionFrequency);
        }

        public M68kCpuState State { get; }

        public M68020CpuProfile Profile => _profile;

        public bool InstructionFrequencyEnabled
        {
            get => _instructionFrequency.Enabled;
            set => _instructionFrequency.Enabled = value;
        }

        public M68kInstructionFrequencySnapshot CaptureInstructionFrequency()
            => _instructionFrequency.CaptureSnapshot();

        public void ResetInstructionFrequency()
            => _instructionFrequency.Reset();

        public void Dispose()
        {
        }

        public int ExecuteInstructions(int maxInstructions, long? targetCycle, IM68kInstructionBoundary boundary)
        {
            ArgumentNullException.ThrowIfNull(boundary);
            var instructions = 0;
            while (!State.Halted &&
                instructions < maxInstructions &&
                (!targetCycle.HasValue || State.Cycles < targetCycle.Value))
            {
                if (State.Stopped &&
                    targetCycle.HasValue &&
                    boundary is IM68kStoppedCpuFastForwardBoundary stoppedBoundary)
                {
                    if (!stoppedBoundary.TryFastForwardStoppedInstruction(State, targetCycle.Value, out _))
                    {
                        break;
                    }

                    SynchronizeNativeToMachine();
                    instructions++;
                    continue;
                }

                if (!boundary.BeforeInstruction())
                {
                    break;
                }

                var previousCycle = State.Cycles;
                ExecuteInstruction();
                boundary.AfterInstruction(previousCycle, State.Cycles);
                instructions++;
            }

            return instructions;
        }

        public int ExecuteInstruction()
        {
            var startCycles = State.Cycles;
            if (State.Halted || State.Stopped)
            {
                AddNativeCycles(2);
                return (int)(State.Cycles - startCycles);
            }

            if (!TryPeekOpcode(State.ProgramCounter, out var opcode))
            {
                var fallbackCycles = _m68000Fallback.ExecuteInstruction();
                SynchronizeNativeToMachine();
                return fallbackCycles;
            }

            if (TryExecuteM68020Instruction(opcode))
            {
                return (int)(State.Cycles - startCycles);
            }

            var executed = _m68000Fallback.ExecuteInstruction();
            SynchronizeNativeToMachine();
            return executed;
        }

        public void Reset(uint programCounter, uint stackPointer)
        {
            Array.Clear(State.D);
            Array.Clear(State.A);
            State.ProgramCounter = programCounter;
            State.ResetStackPointers(stackPointer, 0, supervisorMode: true);
            State.EnableM68020StackMode();
            State.VectorBaseRegister = 0;
            State.SourceFunctionCode = 0;
            State.DestinationFunctionCode = 0;
            State.CacheControlRegister = 0;
            State.CacheAddressRegister = 0;
            State.Cycles = 0;
            State.NativeCycles = 0;
            State.Halted = false;
            State.Stopped = false;
            State.LastOpcode = 0;
            State.LastInstructionProgramCounter = 0;
        }

        public void BeginSubroutine(uint address, uint stackPointer, uint returnAddress)
        {
            State.SetActiveStackPointer(stackPointer);
            PushLong(returnAddress);
            State.ProgramCounter = address;
            State.Halted = false;
            State.Stopped = false;
        }

        public void RequestInterrupt(int level, uint vectorAddress)
        {
            if (level <= 0)
            {
                return;
            }

            var mask = (State.StatusRegister >> 8) & 0x07;
            if (level <= mask)
            {
                return;
            }

            State.Stopped = false;
            var savedStatusRegister = State.StatusRegister;
            var vectorTarget = ReadLong(State.VectorBaseRegister + vectorAddress);
            State.StatusRegister = (ushort)((State.StatusRegister & 0xE8FF) | ((level & 7) << 8) | M68kCpuState.Supervisor);
            PushWord((ushort)(Format0ExceptionFrame | ((int)vectorAddress & 0x0FFF)));
            PushLong(State.ProgramCounter);
            PushWord(savedStatusRegister);
            State.ProgramCounter = vectorTarget;
            AddNativeCycles(44);
        }

        private bool TryExecuteM68020Instruction(ushort opcode)
        {
            if ((opcode & 0xF000) == 0xA000)
            {
                BeginInstruction(opcode);
                _ = FetchWord();
                RaiseFormat0Exception(10, State.LastInstructionProgramCounter, 34);
                return true;
            }

            if ((opcode & 0xF000) == 0xF000)
            {
                BeginInstruction(opcode);
                _ = FetchWord();
                RaiseFormat0Exception(11, State.LastInstructionProgramCounter, 34);
                return true;
            }

            if (opcode == 0x4AFC)
            {
                BeginInstruction(opcode);
                _ = FetchWord();
                RaiseFormat0Exception(4, State.LastInstructionProgramCounter, 20);
                return true;
            }

            if (opcode is 0x4E7A or 0x4E7B)
            {
                ExecuteMovec(opcode);
                return true;
            }

            if (opcode == 0x4E73)
            {
                ExecuteRte();
                return true;
            }

            if (opcode == 0x4E74)
            {
                ExecuteRtd();
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4808)
            {
                ExecuteLinkLong(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x49C0)
            {
                ExecuteExtbLong(opcode);
                return true;
            }

            if ((opcode & 0xF000) == 0x6000 && (opcode & 0x00FF) == 0x00FF)
            {
                ExecuteLongBranch(opcode);
                return true;
            }

            return false;
        }

        private void ExecuteMovec(ushort opcode)
        {
            BeginInstruction(opcode);
            var instructionPc = State.ProgramCounter;
            _ = FetchWord();
            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0)
            {
                _ = FetchWord();
                RaiseFormat0Exception(8, instructionPc, 20);
                return;
            }

            var extension = FetchWord();
            var generalRegister = (extension >> 12) & 7;
            var useAddressRegister = (extension & 0x8000) != 0;
            var controlRegister = extension & 0x0FFF;

            if (opcode == 0x4E7A)
            {
                var value = ReadControlRegister(controlRegister, instructionPc);
                WriteGeneralRegister(useAddressRegister, generalRegister, value);
            }
            else
            {
                var value = ReadGeneralRegister(useAddressRegister, generalRegister);
                WriteControlRegister(controlRegister, value, instructionPc);
            }

            AddNativeCycles(12);
        }

        private void ExecuteRte()
        {
            BeginInstruction(0x4E73);
            var instructionPc = State.ProgramCounter;
            _ = FetchWord();
            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0)
            {
                RaiseFormat0Exception(8, instructionPc, 20);
                return;
            }

            var framePointer = State.A[7];
            var restoredStatus = ReadWord(framePointer);
            var restoredPc = ReadLong(framePointer + 2);
            var format = ReadWord(framePointer + 6);
            if ((format & 0xF000) != Format0ExceptionFrame)
            {
                RaiseFormat0Exception(14, instructionPc, 20);
                return;
            }

            State.SetActiveStackPointer(framePointer + 8);
            State.ProgramCounter = restoredPc;
            State.StatusRegister = restoredStatus;
            AddNativeCycles(20);
        }

        private void ExecuteRtd()
        {
            BeginInstruction(0x4E74);
            _ = FetchWord();
            var displacement = unchecked((short)FetchWord());
            var target = PullLong();
            State.SetActiveStackPointer(State.A[7] + unchecked((uint)displacement));
            State.ProgramCounter = target;
            AddNativeCycles(16);
        }

        private void ExecuteLinkLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var displacement = unchecked((int)FetchLong());
            PushLong(State.A[register]);
            State.A[register] = State.A[7];
            State.SetActiveStackPointer(State.A[7] + unchecked((uint)displacement));
            AddNativeCycles(16);
        }

        private void ExecuteExtbLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var value = unchecked((uint)(int)(sbyte)(State.D[register] & 0xFF));
            State.D[register] = value;
            State.SetNegativeZero(value, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            AddNativeCycles(4);
        }

        private void ExecuteLongBranch(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var condition = (opcode >> 8) & 0x0F;
            var displacement = unchecked((int)FetchLong());
            var branchBase = State.ProgramCounter;

            if (condition == 0x1)
            {
                PushLong(branchBase);
                State.ProgramCounter = unchecked((uint)(branchBase + displacement));
                AddNativeCycles(18);
                return;
            }

            if (CheckCondition(condition))
            {
                State.ProgramCounter = unchecked((uint)(branchBase + displacement));
                AddNativeCycles(10);
                return;
            }

            AddNativeCycles(8);
        }

        private void BeginInstruction(ushort opcode)
        {
            State.LastInstructionProgramCounter = State.ProgramCounter;
            State.LastOpcode = opcode;
            _instructionFrequency.Record(opcode);
        }

        private void RaiseFormat0Exception(int vector, uint stackedProgramCounter, int nativeCycles)
        {
            var savedStatusRegister = State.StatusRegister;
            State.StatusRegister = (ushort)((State.StatusRegister | M68kCpuState.Supervisor) & ~M68kCpuState.Master);
            PushWord((ushort)(Format0ExceptionFrame | ((vector * 4) & 0x0FFF)));
            PushLong(stackedProgramCounter);
            PushWord(savedStatusRegister);
            State.ProgramCounter = ReadLong(State.VectorBaseRegister + ((uint)vector * 4));
            AddNativeCycles(nativeCycles);
        }

        private uint ReadControlRegister(int register, uint instructionPc)
        {
            return register switch
            {
                0x000 => State.SourceFunctionCode,
                0x001 => State.DestinationFunctionCode,
                0x002 => State.CacheControlRegister,
                0x801 => State.VectorBaseRegister,
                0x802 => State.CacheAddressRegister,
                0x803 => State.MasterStackPointer,
                0x804 => State.InterruptStackPointer,
                _ => RaiseIllegalControlRegister(instructionPc)
            };
        }

        private void WriteControlRegister(int register, uint value, uint instructionPc)
        {
            switch (register)
            {
                case 0x000:
                    State.SourceFunctionCode = value & 0x7;
                    break;
                case 0x001:
                    State.DestinationFunctionCode = value & 0x7;
                    break;
                case 0x002:
                    State.CacheControlRegister = value;
                    break;
                case 0x801:
                    State.VectorBaseRegister = value;
                    break;
                case 0x802:
                    State.CacheAddressRegister = value;
                    break;
                case 0x803:
                    State.SetMasterStackPointer(value);
                    break;
                case 0x804:
                    State.SetInterruptStackPointer(value);
                    break;
                default:
                    _ = RaiseIllegalControlRegister(instructionPc);
                    break;
            }
        }

        private uint RaiseIllegalControlRegister(uint instructionPc)
        {
            RaiseFormat0Exception(4, instructionPc, 20);
            return 0;
        }

        private uint ReadGeneralRegister(bool addressRegister, int register)
            => addressRegister ? State.A[register] : State.D[register];

        private void WriteGeneralRegister(bool addressRegister, int register, uint value)
        {
            if (addressRegister)
            {
                if (register == 7)
                {
                    State.SetActiveStackPointer(value);
                }
                else
                {
                    State.A[register] = value;
                }
            }
            else
            {
                State.D[register] = value;
            }
        }

        private ushort FetchWord()
        {
            var value = ReadWord(State.ProgramCounter, AmigaBusAccessKind.CpuInstructionFetch);
            State.ProgramCounter += 2;
            return value;
        }

        private uint FetchLong()
        {
            var high = FetchWord();
            var low = FetchWord();
            return ((uint)high << 16) | low;
        }

        private ushort ReadWord(uint address, AmigaBusAccessKind accessKind = AmigaBusAccessKind.CpuDataRead)
        {
            var cycle = Math.Max(State.Cycles, _profile.NativeToMachineCycles(State.NativeCycles));
            var value = _bus.ReadWord(address, ref cycle, accessKind);
            State.Cycles = cycle;
            SynchronizeNativeToMachine();
            return value;
        }

        private uint ReadLong(uint address)
        {
            var high = ReadWord(address);
            var low = ReadWord(address + 2);
            return ((uint)high << 16) | low;
        }

        private void WriteWord(uint address, ushort value)
        {
            var cycle = Math.Max(State.Cycles, _profile.NativeToMachineCycles(State.NativeCycles));
            _bus.WriteWord(address, value, ref cycle, AmigaBusAccessKind.CpuDataWrite);
            State.Cycles = cycle;
            SynchronizeNativeToMachine();
        }

        private void WriteLong(uint address, uint value)
        {
            WriteWord(address, (ushort)(value >> 16));
            WriteWord(address + 2, (ushort)value);
        }

        private void PushWord(ushort value)
        {
            State.SetActiveStackPointer(State.A[7] - 2);
            WriteWord(State.A[7], value);
        }

        private void PushLong(uint value)
        {
            State.SetActiveStackPointer(State.A[7] - 4);
            WriteLong(State.A[7], value);
        }

        private uint PullLong()
        {
            var value = ReadLong(State.A[7]);
            State.SetActiveStackPointer(State.A[7] + 4);
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddNativeCycles(int nativeCycles)
        {
            State.NativeCycles += nativeCycles;
            var machineCycles = _profile.NativeToMachineCycles(State.NativeCycles);
            if (State.Cycles < machineCycles)
            {
                State.Cycles = machineCycles;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SynchronizeNativeToMachine()
        {
            var nativeCycles = _profile.MachineToNativeCycles(State.Cycles);
            if (State.NativeCycles < nativeCycles)
            {
                State.NativeCycles = nativeCycles;
            }
        }

        private bool TryPeekOpcode(uint address, out ushort opcode)
        {
            opcode = 0;
            if ((address & 1) != 0 || _bus is not IM68kCodeReader codeReader)
            {
                return false;
            }

            opcode = codeReader.ReadHostWord(address);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CheckCondition(int condition)
        {
            var status = State.StatusRegister;
            return condition switch
            {
                0x0 => true,
                0x1 => false,
                0x2 => (status & (M68kCpuState.Carry | M68kCpuState.Zero)) == 0,
                0x3 => (status & (M68kCpuState.Carry | M68kCpuState.Zero)) != 0,
                0x4 => (status & M68kCpuState.Carry) == 0,
                0x5 => (status & M68kCpuState.Carry) != 0,
                0x6 => (status & M68kCpuState.Zero) == 0,
                0x7 => (status & M68kCpuState.Zero) != 0,
                0x8 => (status & M68kCpuState.Overflow) == 0,
                0x9 => (status & M68kCpuState.Overflow) != 0,
                0xA => (status & M68kCpuState.Negative) == 0,
                0xB => (status & M68kCpuState.Negative) != 0,
                0xC => ((status ^ (status << 2)) & M68kCpuState.Negative) == 0,
                0xD => ((status ^ (status << 2)) & M68kCpuState.Negative) != 0,
                0xE => (status & M68kCpuState.Zero) == 0 &&
                    ((status ^ (status << 2)) & M68kCpuState.Negative) == 0,
                0xF => (status & M68kCpuState.Zero) != 0 ||
                    ((status ^ (status << 2)) & M68kCpuState.Negative) != 0,
                _ => false
            };
        }
    }
}
