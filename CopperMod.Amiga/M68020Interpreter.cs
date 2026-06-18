using System;
using System.Runtime.CompilerServices;

namespace CopperMod.Amiga
{
    internal class M68020Interpreter : IM68kBatchCore, IM68kInstructionFrequencyProvider
    {
        private const uint SubroutineSentinel = 0xFFFF_FFFC;
        protected const ushort Format0ExceptionFrame = 0x0000;
        protected readonly IM68kBus _bus;
        protected readonly M68020CpuProfile _profile;
        protected readonly M68kInstructionFrequencyMatrix _instructionFrequency;
        protected readonly M68kTimingEngine _timing;
        protected readonly M68kAcceleratorBusBridge _busBridge;

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
            _timing = new M68kTimingEngine(_profile, State);
            _busBridge = new M68kAcceleratorBusBridge(_bus, _profile, State, _timing);
        }

        public M68kCpuState State { get; }

        public M68020CpuProfile Profile => _profile;

        public M68kTimingEngine Timing => _timing;

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

        public virtual int ExecuteInstruction()
        {
            var startCycles = State.Cycles;
            if (State.Halted || State.Stopped)
            {
                CompleteTiming(M68kInstructionTimingKey.Idle);
                return (int)(State.Cycles - startCycles);
            }

            if (!TryPeekOpcode(State.ProgramCounter, out var opcode))
            {
                throw new UnsupportedM68kTimingException(0, State.ProgramCounter, _profile);
            }

            if (TryExecuteM68020Instruction(opcode))
            {
                return (int)(State.Cycles - startCycles);
            }

            if (TryExecuteApproximateInstruction(opcode))
            {
                return (int)(State.Cycles - startCycles);
            }

            throw new UnsupportedM68kTimingException(opcode, State.ProgramCounter, _profile);
        }

        public virtual void Reset(uint programCounter, uint stackPointer)
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
            State.M68040Fpu.Reset();
            State.M68040Mmu.Reset();
            State.Cycles = 0;
            State.NativeCycles = 0;
            State.Halted = false;
            State.Stopped = false;
            State.LastOpcode = 0;
            State.LastInstructionProgramCounter = 0;
            _timing.Reset();
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
            CompleteTiming(M68kInstructionTimingKey.InterruptAcknowledge);
        }

        protected virtual bool TryExecuteM68020Instruction(ushort opcode)
        {
            if (TryExecuteModelSpecificInstruction(opcode))
            {
                return true;
            }

            if ((opcode & 0xF000) == 0xA000)
            {
                BeginInstruction(opcode);
                _ = FetchWord();
                RaiseFormat0Exception(10, State.LastInstructionProgramCounter, M68kInstructionTimingKey.LineAException);
                return true;
            }

            if ((opcode & 0xF000) == 0xF000)
            {
                BeginInstruction(opcode);
                _ = FetchWord();
                if (opcode == 0xFF00 && _bus.HasHostTrapStub(State.LastInstructionProgramCounter))
                {
                    var trapId = FetchWord();
                    var returnProgramCounter = State.ProgramCounter;
                    if (_bus.TryInvokeHostTrap(State.LastInstructionProgramCounter, trapId, State))
                    {
                        if (!State.Halted && State.ProgramCounter == returnProgramCounter)
                        {
                            State.ProgramCounter = PullLong();
                        }

                        CompleteTiming(M68kInstructionTimingKey.Nop);
                        return true;
                    }

                    State.ProgramCounter = returnProgramCounter;
                }

                RaiseFormat0Exception(11, State.LastInstructionProgramCounter, M68kInstructionTimingKey.LineFException);
                return true;
            }

            if (opcode == 0x4AFC)
            {
                BeginInstruction(opcode);
                _ = FetchWord();
                RaiseFormat0Exception(4, State.LastInstructionProgramCounter, M68kInstructionTimingKey.IllegalInstruction);
                return true;
            }

            if (TryExecuteImmediateLogicalToStatusRegister(opcode))
            {
                return true;
            }

            if ((opcode & 0xF100) == 0x7000)
            {
                ExecuteMoveq(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4280)
            {
                ExecuteClrDataLong(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4290)
            {
                ExecuteClrLongAddressIndirect(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x42A8)
            {
                ExecuteClrLongAddressDisplacement(opcode);
                return true;
            }

            if (opcode == 0x42B9)
            {
                ExecuteClrLongAbsoluteLong();
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4480)
            {
                ExecuteNegLongData(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4600)
            {
                ExecuteNotByteData(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4298)
            {
                ExecuteClrLongPostIncrement(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4240)
            {
                ExecuteClrDataWord(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4268)
            {
                ExecuteClrWordAddressDisplacement(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4210)
            {
                ExecuteClrByteAddressIndirect(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4228)
            {
                ExecuteClrByteAddressDisplacement(opcode);
                return true;
            }

            if ((opcode & 0xF1FF) == 0x41F9)
            {
                ExecuteLeaAbsoluteLong(opcode);
                return true;
            }

            if ((opcode & 0xF1FF) == 0x41F8)
            {
                ExecuteLeaAbsoluteWord(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x41E8)
            {
                ExecuteLeaAddressDisplacement(opcode);
                return true;
            }

            if (opcode == 0x13FC)
            {
                ExecuteMoveByteImmediateToAbsoluteLong();
                return true;
            }

            if ((opcode & 0xF1FF) == 0x10BC)
            {
                ExecuteMoveByteImmediateToAddressIndirect(opcode);
                return true;
            }

            if ((opcode & 0xF1FF) == 0x117C)
            {
                ExecuteMoveByteImmediateToAddressDisplacement(opcode);
                return true;
            }

            if ((opcode & 0xF1FF) == 0x11BC)
            {
                ExecuteMoveByteImmediateToBriefIndexed(opcode);
                return true;
            }

            if ((opcode & 0xF1FF) == 0x317C)
            {
                ExecuteMoveWordImmediateToAddressDisplacement(opcode);
                return true;
            }

            if (opcode == 0x33FC)
            {
                ExecuteMoveWordImmediateToAbsoluteLong();
                return true;
            }

            if (opcode == 0x23FC)
            {
                ExecuteMoveLongImmediateToAbsoluteLong();
                return true;
            }

            if ((opcode & 0xF1F8) == 0x20B8)
            {
                ExecuteMoveLongImmediateToAddressIndirect(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x2178)
            {
                ExecuteMoveLongImmediateToAddressDisplacement(opcode);
                return true;
            }

            if ((opcode & 0xF1FF) == 0x20FC)
            {
                ExecuteMoveLongImmediateToPostIncrement(opcode);
                return true;
            }

            if ((opcode & 0xF1FF) == 0x203C)
            {
                ExecuteMoveLongImmediateToData(opcode);
                return true;
            }

            if ((opcode & 0xF1FF) == 0x207C)
            {
                ExecuteMoveLongImmediateToAddress(opcode);
                return true;
            }

            if ((opcode & 0xF1FF) == 0x303C)
            {
                ExecuteMoveWordImmediateToData(opcode);
                return true;
            }

            if ((opcode & 0xF1FF) == 0x103C)
            {
                ExecuteMoveByteImmediateToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x2000)
            {
                ExecuteMoveLongDataToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x2040)
            {
                ExecuteMoveLongDataToAddress(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x2080)
            {
                ExecuteMoveLongDataToAddressIndirect(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x2140)
            {
                ExecuteMoveLongDataToAddressDisplacement(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x2048)
            {
                ExecuteMoveLongAddressToAddress(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x2088)
            {
                ExecuteMoveLongAddressToAddressIndirect(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x2148)
            {
                ExecuteMoveLongAddressToAddressDisplacement(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x20C8)
            {
                ExecuteMoveLongAddressToPostIncrement(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x2008)
            {
                ExecuteMoveLongAddressToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x2010)
            {
                ExecuteMoveLongAddressIndirectToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x2050)
            {
                ExecuteMoveLongAddressIndirectToAddress(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x2018)
            {
                ExecuteMoveLongPostIncrementToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x2058)
            {
                ExecuteMoveLongPostIncrementToAddress(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x2028)
            {
                ExecuteMoveLongAddressDisplacementToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x2068)
            {
                ExecuteMoveLongAddressDisplacementToAddress(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x20E8)
            {
                ExecuteMoveLongAddressDisplacementToPostIncrement(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x2030)
            {
                ExecuteMoveLongBriefIndexedToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x2070)
            {
                ExecuteMoveLongBriefIndexedToAddress(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x2090)
            {
                ExecuteMoveLongAddressIndirectToAddressIndirect(opcode);
                return true;
            }

            if ((opcode & 0xF1FF) == 0x2039)
            {
                ExecuteMoveLongAbsoluteLongToData(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x21C0)
            {
                ExecuteMoveLongDataToAbsoluteWord(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x23C0)
            {
                ExecuteMoveLongDataToAbsoluteLong(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x21C8)
            {
                ExecuteMoveLongAddressToAbsoluteWord(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x23C8)
            {
                ExecuteMoveLongAddressToAbsoluteLong(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x1000)
            {
                ExecuteMoveByteDataToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x1010)
            {
                ExecuteMoveByteAddressIndirectToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x1018)
            {
                ExecuteMoveBytePostIncrementToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x1028)
            {
                ExecuteMoveByteAddressDisplacementToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x1030)
            {
                ExecuteMoveByteBriefIndexedToData(opcode);
                return true;
            }

            if ((opcode & 0xF1FF) == 0x1039)
            {
                ExecuteMoveByteAbsoluteLongToData(opcode);
                return true;
            }

            if ((opcode & 0xF1FF) == 0x3039)
            {
                ExecuteMoveWordAbsoluteLongToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x3028)
            {
                ExecuteMoveWordAddressDisplacementToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x31C0)
            {
                ExecuteMoveWordDataToAbsoluteLong(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x3140)
            {
                ExecuteMoveWordDataToAddressDisplacement(opcode);
                return true;
            }

            if (opcode == 0x33F9)
            {
                ExecuteMoveWordAbsoluteLongToAbsoluteLong();
                return true;
            }

            if ((opcode & 0xF1FF) == 0x3179)
            {
                ExecuteMoveWordAbsoluteLongToAddressDisplacement(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x11C0)
            {
                ExecuteMoveByteDataToAbsoluteLong(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x1080)
            {
                ExecuteMoveByteDataToAddressIndirect(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x1140)
            {
                ExecuteMoveByteDataToAddressDisplacement(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x1180)
            {
                ExecuteMoveByteDataToBriefIndexed(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x1130)
            {
                ExecuteMoveByteBriefIndexedToPredecrement(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x10C0)
            {
                ExecuteMoveByteDataToPostIncrement(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x1100)
            {
                ExecuteMoveByteDataToPredecrement(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x10D8)
            {
                ExecuteMoveBytePostIncrementToPostIncrement(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x11D0)
            {
                ExecuteMoveByteAddressIndirectToAbsoluteLong(opcode);
                return true;
            }

            if (opcode == 0x13F9)
            {
                ExecuteMoveByteAbsoluteLongToAbsoluteLong();
                return true;
            }

            if (opcode is 0x0039 or 0x0239)
            {
                ExecuteImmediateLogicalByteToAbsoluteLong(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x0600)
            {
                ExecuteAddiByteImmediateToData(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x0610)
            {
                ExecuteAddiByteImmediateToAddressIndirect(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x0628)
            {
                ExecuteAddiByteImmediateToAddressDisplacement(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x0640)
            {
                ExecuteAddiWordImmediateToData(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x0680)
            {
                ExecuteAddiLongImmediateToData(opcode);
                return true;
            }

            if (opcode == 0x06B9)
            {
                ExecuteAddiLongImmediateToAbsoluteLong();
                return true;
            }

            if ((opcode & 0xFFF8) == 0x0400)
            {
                ExecuteSubiByteImmediateToData(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x0428)
            {
                ExecuteSubiByteImmediateToAddressDisplacement(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x0480)
            {
                ExecuteSubiLongImmediateToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x9000)
            {
                ExecuteSubByteDataToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x9080)
            {
                ExecuteSubLongDataToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x9088)
            {
                ExecuteSubLongAddressToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x90A8)
            {
                ExecuteSubLongAddressDisplacementToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xD040)
            {
                ExecuteAddWordDataToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xD080)
            {
                ExecuteAddLongDataToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xD180)
            {
                ExecuteAddxLongDataToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xD098)
            {
                ExecuteAddLongPostIncrementToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xD168)
            {
                ExecuteAddWordDataToAddressDisplacement(opcode);
                return true;
            }

            if ((opcode & 0xF1FF) == 0xD1FC)
            {
                ExecuteAddaLongImmediateToAddress(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xD1C0)
            {
                ExecuteAddaLongDataToAddress(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xD1A8)
            {
                ExecuteAddaLongAddressDisplacementToAddress(opcode);
                return true;
            }

            if ((opcode & 0xF1FF) == 0x91FC)
            {
                ExecuteSubaLongImmediateToAddress(opcode);
                return true;
            }

            if ((opcode & 0xFFC0) is 0x4C00 or 0x4C40)
            {
                ExecuteLongMultiplyDivide(opcode);
                return true;
            }

            if ((opcode & 0xF1FF) == 0x80FC)
            {
                ExecuteDivuWordImmediateToData(opcode);
                return true;
            }

            if ((opcode & 0xF1FF) == 0x81FC)
            {
                ExecuteDivsWordImmediateToData(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x0240)
            {
                ExecuteAndiWordImmediateToData(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x0280)
            {
                ExecuteAndLongImmediateToData(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x0A40)
            {
                ExecuteEoriWordImmediateToData(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x0A80)
            {
                ExecuteEoriLongImmediateToData(opcode);
                return true;
            }

            if ((opcode & 0xF1FF) == 0xC0FC)
            {
                ExecuteMuluWordImmediateToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xC000)
            {
                ExecuteAndByteDataToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F0) == 0xC100)
            {
                ExecuteBcdByte(opcode, subtract: false);
                return true;
            }

            if ((opcode & 0xF1F0) == 0x8100)
            {
                ExecuteBcdByte(opcode, subtract: true);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x0000)
            {
                ExecuteOriByteImmediateToData(opcode);
                return true;
            }

            if (opcode == 0x0839)
            {
                ExecuteBtstByteImmediateAbsoluteLong();
                return true;
            }

            if (opcode == 0x0879)
            {
                ExecuteBchgByteImmediateAbsoluteLong();
                return true;
            }

            if (opcode == 0x08B9)
            {
                ExecuteBclrByteImmediateAbsoluteLong();
                return true;
            }

            if (opcode == 0x08F9)
            {
                ExecuteBsetByteImmediateAbsoluteLong();
                return true;
            }

            if ((opcode & 0xFFF8) == 0x08E8)
            {
                ExecuteBsetByteImmediateAddressDisplacement(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x0800)
            {
                ExecuteBtstImmediateData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x0100)
            {
                ExecuteBtstDynamicData(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x08C0)
            {
                ExecuteBsetImmediateData(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x0880)
            {
                ExecuteBclrImmediateData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0x0180)
            {
                ExecuteBclrDynamicData(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4840)
            {
                ExecuteSwapData(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x48C0)
            {
                ExecuteExtWordData(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4A40)
            {
                ExecuteTstWordData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xE080)
            {
                ExecuteAsrLongImmediateData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xE040)
            {
                ExecuteAsrWordImmediateData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xE088)
            {
                ExecuteLsrLongImmediateData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xE180)
            {
                ExecuteAslLongImmediateData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xE140)
            {
                ExecuteAslWordImmediateData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xE018)
            {
                ExecuteRorByteImmediateData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xE058)
            {
                ExecuteRorWordImmediateData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xE158)
            {
                ExecuteRolWordImmediateData(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x0C00)
            {
                ExecuteCmpiByteImmediateToData(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x0C10)
            {
                ExecuteCmpiByteImmediateToAddressIndirect(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x0C28)
            {
                ExecuteCmpiByteImmediateToAddressDisplacement(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x0C40)
            {
                ExecuteCmpiWordImmediateToData(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x0C68)
            {
                ExecuteCmpiWordImmediateToAddressDisplacement(opcode);
                return true;
            }

            if (opcode == 0x0C79)
            {
                ExecuteCmpiWordImmediateToAbsoluteLong();
                return true;
            }

            if ((opcode & 0xFFF8) == 0x0C80)
            {
                ExecuteCmpiLongImmediateToData(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x0C98)
            {
                ExecuteCmpiLongImmediateToPostIncrement(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x0CA8)
            {
                ExecuteCmpiLongImmediateToAddressDisplacement(opcode);
                return true;
            }

            if ((opcode & 0xF1FF) == 0xB1FC)
            {
                ExecuteCmpaLongImmediateToAddress(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xB1C0)
            {
                ExecuteCmpaLongDataToAddress(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xB1C8)
            {
                ExecuteCmpaLongAddressToAddress(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xB1D0)
            {
                ExecuteCmpaLongAddressIndirectToAddress(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xB080)
            {
                ExecuteCmpLongDataToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xB088)
            {
                ExecuteCmpLongAddressToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xB090)
            {
                ExecuteCmpLongAddressIndirectToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xB098)
            {
                ExecuteCmpLongPostIncrementToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xB000)
            {
                ExecuteCmpByteDataToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xB010)
            {
                ExecuteCmpByteAddressIndirectToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xB028)
            {
                ExecuteCmpByteAddressDisplacementToData(opcode);
                return true;
            }

            if ((opcode & 0xF1FF) == 0xB039)
            {
                ExecuteCmpByteAbsoluteLongToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xB040)
            {
                ExecuteCmpWordDataToData(opcode);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xB068)
            {
                ExecuteCmpWordAddressDisplacementToData(opcode);
                return true;
            }

            if (opcode == 0x0CB9)
            {
                ExecuteCmpiLongImmediateToAbsoluteLong();
                return true;
            }

            if (opcode == 0x4E71)
            {
                ExecuteNop();
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

            if (opcode == 0x4E75)
            {
                ExecuteRts();
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4ED0)
            {
                ExecuteJmpAddressIndirect(opcode);
                return true;
            }

            if (opcode == 0x4EB9)
            {
                ExecuteJsrAbsoluteLong();
                return true;
            }

            if (opcode == 0x4EF9)
            {
                ExecuteJmpAbsoluteLong();
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4808)
            {
                ExecuteLinkLong(opcode);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x4800)
            {
                ExecuteNbcdByte(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x49C0)
            {
                ExecuteExtbLong(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x48E0)
            {
                ExecuteMovemLongRegistersToPredecrement(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4CD8)
            {
                ExecuteMovemLongPostIncrementToRegisters(opcode);
                return true;
            }

            if ((opcode & 0xF000) == 0x6000 && (opcode & 0x00FF) == 0x00FF)
            {
                ExecuteLongBranch(opcode);
                return true;
            }

            if ((opcode & 0xF000) == 0x6000 && (opcode & 0x00FF) != 0x0000)
            {
                ExecuteByteBranch(opcode);
                return true;
            }

            if ((opcode & 0xF000) == 0x6000 && (opcode & 0x00FF) == 0x0000)
            {
                ExecuteWordBranch(opcode);
                return true;
            }

            if ((opcode & 0xF0FF) == 0x50F9)
            {
                ExecuteSccAbsoluteLong(opcode);
                return true;
            }

            if ((opcode & 0xF0F8) == 0x50C8)
            {
                ExecuteDbcc(opcode);
                return true;
            }

            return false;
        }

        protected virtual bool TryExecuteApproximateInstruction(ushort opcode)
        {
            _ = opcode;
            return false;
        }

        protected virtual bool TryExecuteModelSpecificInstruction(ushort opcode)
        {
            _ = opcode;
            return false;
        }

        private bool TryExecuteImmediateLogicalToStatusRegister(ushort opcode)
        {
            if (opcode is not (0x003C or 0x007C or 0x023C or 0x027C or 0x0A3C or 0x0A7C))
            {
                return false;
            }

            BeginInstruction(opcode);
            var instructionPc = State.ProgramCounter;
            _ = FetchWord();
            var immediate = FetchWord();
            var operation = opcode & 0x0F00;
            var status = State.StatusRegister;
            var result = operation switch
            {
                0x0000 => status | immediate,
                0x0200 => status & immediate,
                0x0A00 => status ^ immediate,
                _ => status
            };

            if ((opcode & 0x0040) == 0)
            {
                State.StatusRegister = (ushort)((State.StatusRegister & 0xFFE0) | (result & 0x001F));
                CompleteTiming(M68kInstructionTimingKey.ImmediateWordToConditionCodeRegister);
                return true;
            }

            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0)
            {
                RaiseFormat0Exception(8, instructionPc, M68kInstructionTimingKey.PrivilegeViolation);
                return true;
            }

            State.StatusRegister = (ushort)result;
            CompleteTiming(M68kInstructionTimingKey.ImmediateWordToStatusRegister);
            return true;
        }

        private void ExecuteNop()
        {
            BeginInstruction(0x4E71);
            _ = FetchWord();
            CompleteTiming(M68kInstructionTimingKey.Nop);
        }

        protected virtual void ExecuteMovec(ushort opcode)
        {
            BeginInstruction(opcode);
            var instructionPc = State.ProgramCounter;
            _ = FetchWord();
            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0)
            {
                _ = FetchWord();
                RaiseFormat0Exception(8, instructionPc, M68kInstructionTimingKey.PrivilegeViolation);
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

            CompleteTiming(M68kInstructionTimingKey.Movec);
        }

        private void ExecuteRte()
        {
            BeginInstruction(0x4E73);
            var instructionPc = State.ProgramCounter;
            _ = FetchWord();
            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0)
            {
                RaiseFormat0Exception(8, instructionPc, M68kInstructionTimingKey.PrivilegeViolation);
                return;
            }

            var framePointer = State.A[7];
            var restoredStatus = ReadWord(framePointer);
            var restoredPc = ReadLong(framePointer + 2);
            var format = ReadWord(framePointer + 6);
            if ((format & 0xF000) != Format0ExceptionFrame)
            {
                RaiseFormat0Exception(14, instructionPc, M68kInstructionTimingKey.FormatError);
                return;
            }

            State.SetActiveStackPointer(framePointer + 8);
            State.ProgramCounter = restoredPc;
            State.StatusRegister = restoredStatus;
            CompleteTiming(M68kInstructionTimingKey.Rte);
        }

        private void ExecuteRtd()
        {
            BeginInstruction(0x4E74);
            _ = FetchWord();
            var displacement = unchecked((short)FetchWord());
            var target = PullLong();
            State.SetActiveStackPointer(State.A[7] + unchecked((uint)displacement));
            State.ProgramCounter = target;
            CompleteTiming(M68kInstructionTimingKey.Rtd);
        }

        private void ExecuteRts()
        {
            BeginInstruction(0x4E75);
            _ = FetchWord();
            State.ProgramCounter = PullLong();
            CompleteTiming(M68kInstructionTimingKey.Rts);
        }

        private void ExecuteJmpAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            State.ProgramCounter = State.A[opcode & 7];
            CompleteTiming(M68kInstructionTimingKey.JmpAddressIndirect);
        }

        private void ExecuteJmpAbsoluteLong()
        {
            BeginInstruction(0x4EF9);
            _ = FetchWord();
            State.ProgramCounter = FetchLong();
            CompleteTiming(M68kInstructionTimingKey.JmpAbsoluteLong);
        }

        private void ExecuteJsrAbsoluteLong()
        {
            BeginInstruction(0x4EB9);
            _ = FetchWord();
            var target = FetchLong();
            PushLong(State.ProgramCounter);
            State.ProgramCounter = target;
            CompleteTiming(M68kInstructionTimingKey.JsrAbsoluteLong);
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
            CompleteTiming(M68kInstructionTimingKey.LinkLong);
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
            CompleteTiming(M68kInstructionTimingKey.ExtbLong);
        }

        private void ExecuteMoveq(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var value = unchecked((uint)(int)(sbyte)(opcode & 0xFF));
            State.D[register] = value;
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.Moveq);
        }

        private void ExecuteNegLongData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var destination = State.D[register];
            var result = unchecked(0u - destination);
            State.D[register] = result;
            State.SetNegativeZero(result, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, destination == 0x8000_0000u);
            State.SetFlag(M68kCpuState.Carry, destination != 0);
            State.SetFlag(M68kCpuState.Extend, destination != 0);
            CompleteTiming(M68kInstructionTimingKey.NegLongData);
        }

        private void ExecuteNotByteData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var result = (byte)~State.D[register];
            WriteDataRegisterByte(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.NotByteData);
        }

        private void ExecuteMovemLongRegistersToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mask = FetchWord();
            var addressRegister = opcode & 7;
            var address = State.A[addressRegister];
            var dataSnapshot = new uint[8];
            var addressSnapshot = new uint[8];
            Array.Copy(State.D, dataSnapshot, dataSnapshot.Length);
            Array.Copy(State.A, addressSnapshot, addressSnapshot.Length);

            for (var bit = 0; bit < 16; bit++)
            {
                if ((mask & (1 << bit)) == 0)
                {
                    continue;
                }

                var value = bit < 8
                    ? addressSnapshot[7 - bit]
                    : dataSnapshot[15 - bit];
                address -= 4;
                WriteLong(address, value);
            }

            WriteGeneralRegister(true, addressRegister, address);
            CompleteMovemLongTiming(
                M68kInstructionTimingKey.MovemLongRegistersToPredecrement,
                "MOVEM.L <list>,-(An)",
                CountSetBits(mask),
                registerToMemory: true);
        }

        private void ExecuteMovemLongPostIncrementToRegisters(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mask = FetchWord();
            var addressRegister = opcode & 7;
            if ((mask & (1 << (8 + addressRegister))) != 0)
            {
                throw new UnsupportedM68kTimingException(opcode, State.LastInstructionProgramCounter, _profile);
            }

            var address = State.A[addressRegister];
            for (var register = 0; register < 8; register++)
            {
                if ((mask & (1 << register)) == 0)
                {
                    continue;
                }

                State.D[register] = ReadLong(address);
                address += 4;
            }

            for (var register = 0; register < 8; register++)
            {
                if ((mask & (1 << (8 + register))) == 0)
                {
                    continue;
                }

                WriteGeneralRegister(true, register, ReadLong(address));
                address += 4;
            }

            WriteGeneralRegister(true, addressRegister, address);
            CompleteMovemLongTiming(
                M68kInstructionTimingKey.MovemLongPostIncrementToRegisters,
                "MOVEM.L (An)+,<list>",
                CountSetBits(mask),
                registerToMemory: false);
        }

        private void ExecuteClrDataLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            State.D[register] = 0;
            State.SetNegativeZero(0, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrDataLong);
        }

        private void ExecuteClrDataWord(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            WriteDataRegisterWord(opcode & 7, 0);
            State.SetNegativeZero(0, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrDataWord);
        }

        private void ExecuteClrLongAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            WriteLong(State.A[register], 0);
            State.SetNegativeZero(0, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrLongAddressIndirect);
        }

        private void ExecuteClrLongAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            WriteLong(unchecked((uint)(State.A[register] + displacement)), 0);
            State.SetNegativeZero(0, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrLongAddressDisplacement);
        }

        private void ExecuteClrLongAbsoluteLong()
        {
            BeginInstruction(0x42B9);
            _ = FetchWord();
            var address = FetchLong();
            WriteLong(address, 0);
            State.SetNegativeZero(0, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrLongAbsoluteLong);
        }

        private void ExecuteClrByteAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            WriteByte(State.A[register], 0);
            State.SetNegativeZero(0, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrByteAddressIndirect);
        }

        private void ExecuteClrByteAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            WriteByte(unchecked((uint)(State.A[register] + displacement)), 0);
            State.SetNegativeZero(0, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrByteAddressDisplacement);
        }

        private void ExecuteClrWordAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            WriteWord(unchecked((uint)(State.A[register] + displacement)), 0);
            State.SetNegativeZero(0, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrWordAddressDisplacement);
        }

        private void ExecuteClrLongPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var address = State.A[register];
            WriteLong(address, 0);
            State.A[register] = address + 4;
            State.SetNegativeZero(0, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ClrLongPostIncrement);
        }

        private void ExecuteLeaAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var address = FetchLong();
            WriteGeneralRegister(true, register, address);
            CompleteTiming(M68kInstructionTimingKey.LeaAbsoluteLong);
        }

        private void ExecuteLeaAbsoluteWord(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var address = unchecked((uint)(int)(short)FetchWord());
            WriteGeneralRegister(true, register, address);
            CompleteTiming(M68kInstructionTimingKey.LeaAbsoluteWord);
        }

        private void ExecuteLeaAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            WriteGeneralRegister(true, destination, unchecked((uint)(State.A[source] + displacement)));
            CompleteTiming(M68kInstructionTimingKey.LeaAddressDisplacement);
        }

        private void ExecuteMoveByteImmediateToAbsoluteLong()
        {
            BeginInstruction(0x13FC);
            _ = FetchWord();
            var value = (byte)FetchWord();
            var address = FetchLong();
            WriteByte(address, value);
            State.SetNegativeZero(value, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.MoveByteImmediateToAbsoluteLong);
        }

        private void ExecuteMoveByteImmediateToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var value = (byte)FetchWord();
            WriteByte(State.A[destination], value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteImmediateToAddressIndirect);
        }

        private void ExecuteMoveWordImmediateToAbsoluteLong()
        {
            BeginInstruction(0x33FC);
            _ = FetchWord();
            var value = FetchWord();
            var address = FetchLong();
            WriteWord(address, value);
            State.SetNegativeZero(value, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.MoveWordImmediateToAbsoluteLong);
        }

        private void ExecuteMoveLongImmediateToAbsoluteLong()
        {
            BeginInstruction(0x23FC);
            _ = FetchWord();
            var value = FetchLong();
            var address = FetchLong();
            WriteLong(address, value);
            State.SetNegativeZero(value, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.MoveLongImmediateToAbsoluteLong);
        }

        private void ExecuteMoveLongImmediateToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var value = FetchLong();
            WriteLong(State.A[destination], value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongImmediateToAddressIndirect);
        }

        private void ExecuteMoveLongImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var value = FetchLong();
            var displacement = unchecked((int)(short)FetchWord());
            WriteLong(unchecked((uint)(State.A[destination] + displacement)), value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongImmediateToAddressDisplacement);
        }

        private void ExecuteMoveLongImmediateToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var value = FetchLong();
            var address = State.A[destination];
            WriteLong(address, value);
            WriteGeneralRegister(true, destination, address + 4);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongImmediateToPostIncrement);
        }

        private void ExecuteMoveLongImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var value = FetchLong();
            State.D[register] = value;
            State.SetNegativeZero(value, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.MoveLongImmediateToData);
        }

        private void ExecuteMoveLongImmediateToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var value = FetchLong();
            WriteGeneralRegister(true, register, value);
            CompleteTiming(M68kInstructionTimingKey.MoveLongImmediateToAddress);
        }

        private void ExecuteMoveWordImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var value = FetchWord();
            WriteDataRegisterWord(register, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordImmediateToData);
        }

        private void ExecuteMoveByteImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var value = (byte)FetchWord();
            WriteDataRegisterByte(register, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteImmediateToData);
        }

        private void ExecuteMoveByteImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = (opcode >> 9) & 7;
            var value = (byte)FetchWord();
            var displacement = unchecked((int)(short)FetchWord());
            WriteByte(unchecked((uint)(State.A[addressRegister] + displacement)), value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteImmediateToAddressDisplacement);
        }

        private void ExecuteMoveByteImmediateToBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var baseRegister = (opcode >> 9) & 7;
            var value = (byte)FetchWord();
            var extension = FetchWord();
            WriteByte(CalculateBriefIndexedAddress(baseRegister, extension, opcode), value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteImmediateToBriefIndexed);
        }

        private void ExecuteMoveWordImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = (opcode >> 9) & 7;
            var value = FetchWord();
            var displacement = unchecked((int)(short)FetchWord());
            WriteWord(unchecked((uint)(State.A[addressRegister] + displacement)), value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordImmediateToAddressDisplacement);
        }

        private void ExecuteMoveLongDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = State.D[source];
            State.D[destination] = value;
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongDataToData);
        }

        private void ExecuteMoveLongDataToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            WriteGeneralRegister(true, destination, State.D[source]);
            CompleteTiming(M68kInstructionTimingKey.MoveLongDataToAddress);
        }

        private void ExecuteMoveLongDataToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = State.D[source];
            WriteLong(State.A[destination], value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongDataToAddressIndirect);
        }

        private void ExecuteMoveLongDataToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = State.D[source];
            WriteLong(unchecked((uint)(State.A[destination] + displacement)), value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongDataToAddressDisplacement);
        }

        private void ExecuteMoveLongAddressToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            WriteGeneralRegister(true, destination, State.A[source]);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressToAddress);
        }

        private void ExecuteMoveLongAddressToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = State.A[source];
            WriteLong(State.A[destination], value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressToAddressIndirect);
        }

        private void ExecuteMoveLongAddressToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = State.A[source];
            WriteLong(unchecked((uint)(State.A[destination] + displacement)), value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressToAddressDisplacement);
        }

        private void ExecuteMoveLongAddressToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var address = State.A[destination];
            var value = State.A[source];
            WriteLong(address, value);
            State.A[destination] = address + 4;
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressToPostIncrement);
        }

        private void ExecuteMoveLongAddressToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = State.A[source];
            State.D[destination] = value;
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressToData);
        }

        private void ExecuteMoveLongAddressIndirectToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = ReadLong(State.A[source]);
            State.D[destination] = value;
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressIndirectToData);
        }

        private void ExecuteMoveLongAddressIndirectToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = ReadLong(State.A[source]);
            WriteGeneralRegister(true, destination, value);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressIndirectToAddress);
        }

        private void ExecuteMoveLongPostIncrementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var address = State.A[source];
            var value = ReadLong(address);
            State.D[destination] = value;
            WriteGeneralRegister(true, source, address + 4);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongPostIncrementToData);
        }

        private void ExecuteMoveLongPostIncrementToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var address = State.A[source];
            var value = ReadLong(address);
            WriteGeneralRegister(true, source, address + 4);
            WriteGeneralRegister(true, destination, value);
            CompleteTiming(M68kInstructionTimingKey.MoveLongPostIncrementToAddress);
        }

        private void ExecuteMoveLongAddressDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadLong(unchecked((uint)(State.A[source] + displacement)));
            State.D[destination] = value;
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressDisplacementToData);
        }

        private void ExecuteMoveLongAddressDisplacementToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadLong(unchecked((uint)(State.A[source] + displacement)));
            WriteGeneralRegister(true, destination, value);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressDisplacementToAddress);
        }

        private void ExecuteMoveLongAddressDisplacementToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadLong(unchecked((uint)(State.A[source] + displacement)));
            var destinationAddress = State.A[destination];
            WriteLong(destinationAddress, value);
            WriteGeneralRegister(true, destination, destinationAddress + 4);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressDisplacementToPostIncrement);
        }

        private void ExecuteMoveLongBriefIndexedToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var baseRegister = opcode & 7;
            var extension = FetchWord();
            var value = ReadLong(CalculateBriefIndexedAddress(baseRegister, extension, opcode));
            State.D[destination] = value;
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongBriefIndexedToData);
        }

        private void ExecuteMoveLongBriefIndexedToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var baseRegister = opcode & 7;
            var extension = FetchWord();
            var value = ReadLong(CalculateBriefIndexedAddress(baseRegister, extension, opcode));
            WriteGeneralRegister(true, destination, value);
            CompleteTiming(M68kInstructionTimingKey.MoveLongBriefIndexedToAddress);
        }

        private void ExecuteMoveLongAddressIndirectToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = ReadLong(State.A[source]);
            WriteLong(State.A[destination], value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressIndirectToAddressIndirect);
        }

        private void ExecuteMoveLongAbsoluteLongToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var address = FetchLong();
            var value = ReadLong(address);
            State.D[destination] = value;
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAbsoluteLongToData);
        }

        private void ExecuteMoveLongDataToAbsoluteWord(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var address = unchecked((uint)(short)FetchWord());
            var value = State.D[source];
            WriteLong(address, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongDataToAbsoluteLong);
        }

        private void ExecuteMoveLongDataToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var address = FetchLong();
            var value = State.D[source];
            WriteLong(address, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongDataToAbsoluteLong);
        }

        private void ExecuteMoveLongAddressToAbsoluteWord(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var address = unchecked((uint)(short)FetchWord());
            var value = State.A[source];
            WriteLong(address, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressToAbsoluteLong);
        }

        private void ExecuteMoveLongAddressToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var address = FetchLong();
            var value = State.A[source];
            WriteLong(address, value);
            SetMoveFlags(value, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.MoveLongAddressToAbsoluteLong);
        }

        private void ExecuteMoveByteDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = (byte)State.D[source];
            WriteDataRegisterByte(destination, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteDataToData);
        }

        private void ExecuteMoveByteAddressIndirectToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = ReadByte(State.A[source]);
            WriteDataRegisterByte(destination, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteAddressIndirectToData);
        }

        private void ExecuteMoveBytePostIncrementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = ReadByte(State.A[source]);
            State.A[source] += source == 7 ? 2u : 1u;
            WriteDataRegisterByte(destination, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveBytePostIncrementToData);
        }

        private void ExecuteMoveByteAddressDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadByte(unchecked((uint)(State.A[source] + displacement)));
            WriteDataRegisterByte(destination, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteAddressDisplacementToData);
        }

        private void ExecuteMoveByteBriefIndexedToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var baseRegister = opcode & 7;
            var extension = FetchWord();
            var value = ReadByte(CalculateBriefIndexedAddress(baseRegister, extension, opcode));
            WriteDataRegisterByte(destination, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteBriefIndexedToData);
        }

        private void ExecuteMoveByteAbsoluteLongToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var address = FetchLong();
            var value = ReadByte(address);
            WriteDataRegisterByte(destination, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteAbsoluteLongToData);
        }

        private void ExecuteMoveWordAbsoluteLongToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var address = FetchLong();
            var value = ReadWord(address);
            WriteDataRegisterWord(destination, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordAbsoluteLongToData);
        }

        private void ExecuteMoveWordAddressDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadWord(unchecked((uint)(State.A[source] + displacement)));
            WriteDataRegisterWord(destination, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordAddressDisplacementToData);
        }

        private void ExecuteMoveWordDataToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var address = FetchLong();
            var value = (ushort)State.D[source];
            WriteWord(address, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordDataToAbsoluteLong);
        }

        private void ExecuteMoveWordDataToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = (ushort)State.D[source];
            WriteWord(unchecked((uint)(State.A[destination] + displacement)), value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordDataToAddressDisplacement);
        }

        private void ExecuteMoveWordAbsoluteLongToAbsoluteLong()
        {
            BeginInstruction(0x33F9);
            _ = FetchWord();
            var sourceAddress = FetchLong();
            var destinationAddress = FetchLong();
            var value = ReadWord(sourceAddress);
            WriteWord(destinationAddress, value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordAbsoluteLongToAbsoluteLong);
        }

        private void ExecuteMoveWordAbsoluteLongToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var sourceAddress = FetchLong();
            var displacement = unchecked((int)(short)FetchWord());
            var value = ReadWord(sourceAddress);
            WriteWord(unchecked((uint)(State.A[destination] + displacement)), value);
            SetMoveFlags(value, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.MoveWordAbsoluteLongToAddressDisplacement);
        }

        private void ExecuteMoveByteDataToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var address = FetchLong();
            var value = (byte)State.D[source];
            WriteByte(address, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteDataToAbsoluteLong);
        }

        private void ExecuteMoveByteDataToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var value = (byte)State.D[source];
            WriteByte(State.A[destination], value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteDataToAddressIndirect);
        }

        private void ExecuteMoveByteDataToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var value = (byte)State.D[source];
            WriteByte(unchecked((uint)(State.A[destination] + displacement)), value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteDataToAddressDisplacement);
        }

        private void ExecuteMoveByteDataToBriefIndexed(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var baseRegister = (opcode >> 9) & 7;
            var source = opcode & 7;
            var extension = FetchWord();
            var value = (byte)State.D[source];
            WriteByte(CalculateBriefIndexedAddress(baseRegister, extension, opcode), value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteDataToBriefIndexed);
        }

        private void ExecuteMoveByteBriefIndexedToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceBase = opcode & 7;
            var destination = (opcode >> 9) & 7;
            var extension = FetchWord();
            var value = ReadByte(CalculateBriefIndexedAddress(sourceBase, extension, opcode));
            var address = State.A[destination] - (destination == 7 ? 2u : 1u);
            WriteGeneralRegister(true, destination, address);
            WriteByte(address, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteBriefIndexedToPredecrement);
        }

        private void ExecuteMoveByteDataToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var address = State.A[destination];
            var value = (byte)State.D[source];
            WriteByte(address, value);
            WriteGeneralRegister(true, destination, address + (destination == 7 ? 2u : 1u));
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteDataToPostIncrement);
        }

        private void ExecuteMoveByteDataToPredecrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = (opcode >> 9) & 7;
            var source = opcode & 7;
            var address = State.A[destination] - (destination == 7 ? 2u : 1u);
            var value = (byte)State.D[source];
            WriteGeneralRegister(true, destination, address);
            WriteByte(address, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteDataToPredecrement);
        }

        private void ExecuteMoveBytePostIncrementToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var destination = (opcode >> 9) & 7;
            var sourceAddress = State.A[source];
            var value = ReadByte(sourceAddress);
            WriteGeneralRegister(true, source, sourceAddress + (source == 7 ? 2u : 1u));
            var destinationAddress = State.A[destination];
            WriteByte(destinationAddress, value);
            WriteGeneralRegister(true, destination, destinationAddress + (destination == 7 ? 2u : 1u));
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveBytePostIncrementToPostIncrement);
        }

        private void ExecuteMoveByteAddressIndirectToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = opcode & 7;
            var address = FetchLong();
            var value = ReadByte(State.A[source]);
            WriteByte(address, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteAddressIndirectToAbsoluteLong);
        }

        private void ExecuteMoveByteAbsoluteLongToAbsoluteLong()
        {
            BeginInstruction(0x13F9);
            _ = FetchWord();
            var sourceAddress = FetchLong();
            var destinationAddress = FetchLong();
            var value = ReadByte(sourceAddress);
            WriteByte(destinationAddress, value);
            SetMoveFlags(value, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.MoveByteAbsoluteLongToAbsoluteLong);
        }

        private void ExecuteImmediateLogicalByteToAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var immediate = (byte)FetchWord();
            var address = FetchLong();
            var destination = ReadByte(address);
            var result = opcode == 0x0039
                ? (byte)(destination | immediate)
                : (byte)(destination & immediate);
            WriteByte(address, result);
            State.SetNegativeZero(result, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.ImmediateLogicalByteToAbsoluteLong);
        }

        private void ExecuteAddiWordImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var source = FetchWord();
            var destination = State.D[register] & 0xFFFF;
            var result = (destination + source) & 0xFFFF;
            WriteDataRegisterWord(register, (ushort)result);
            SetAddFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.AddiWordImmediateToData);
        }

        private void ExecuteAddiByteImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var source = (byte)FetchWord();
            var destination = State.D[register] & 0xFF;
            var result = (byte)(destination + source);
            WriteDataRegisterByte(register, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.AddiByteImmediateToData);
        }

        private void ExecuteSubiByteImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var source = (byte)FetchWord();
            var destination = State.D[register] & 0xFF;
            var result = (byte)(destination - source);
            WriteDataRegisterByte(register, result);
            SetSubtractFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.SubiByteImmediateToData);
        }

        private void ExecuteSubiByteImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var source = (byte)FetchWord();
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[register] + displacement));
            var destination = ReadByte(address);
            var result = (byte)(destination - source);
            WriteByte(address, result);
            SetSubtractFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.SubiByteImmediateToAddressDisplacement);
        }

        private void ExecuteAddiByteImmediateToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = opcode & 7;
            var source = (byte)FetchWord();
            var address = State.A[addressRegister];
            var destination = ReadByte(address);
            var result = (byte)(destination + source);
            WriteByte(address, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.AddiByteImmediateToAddressIndirect);
        }

        private void ExecuteAddiByteImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = opcode & 7;
            var source = (byte)FetchWord();
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[addressRegister] + displacement));
            var destination = ReadByte(address);
            var result = (byte)(destination + source);
            WriteByte(address, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.AddiByteImmediateToAddressDisplacement);
        }

        private void ExecuteAddiLongImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var source = FetchLong();
            var destination = State.D[register];
            var result = destination + source;
            State.D[register] = result;
            SetAddFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddiLongImmediateToData);
        }

        private void ExecuteAddiLongImmediateToAbsoluteLong()
        {
            BeginInstruction(0x06B9);
            _ = FetchWord();
            var source = FetchLong();
            var address = FetchLong();
            var destination = ReadLong(address);
            var result = destination + source;
            WriteLong(address, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddiLongImmediateToAbsoluteLong);
        }

        private void ExecuteSubiLongImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var source = FetchLong();
            var destination = State.D[register];
            var result = destination - source;
            State.D[register] = result;
            SetSubtractFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.SubiLongImmediateToData);
        }

        private void ExecuteSubLongDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var destination = State.D[destinationRegister];
            var source = State.D[sourceRegister];
            var result = destination - source;
            State.D[destinationRegister] = result;
            SetSubtractFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.SubLongDataToData);
        }

        private void ExecuteSubByteDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var destination = State.D[destinationRegister] & 0xFF;
            var source = State.D[sourceRegister] & 0xFF;
            var result = (byte)(destination - source);
            WriteDataRegisterByte(destinationRegister, result);
            SetSubtractFlags(destination, source, result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.SubByteDataToData);
        }

        private void ExecuteSubLongAddressToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var destination = State.D[destinationRegister];
            var source = State.A[sourceRegister];
            var result = destination - source;
            State.D[destinationRegister] = result;
            SetSubtractFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.SubLongAddressToData);
        }

        private void ExecuteSubLongAddressDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var destination = State.D[destinationRegister];
            var source = ReadLong(unchecked((uint)(State.A[sourceRegister] + displacement)));
            var result = destination - source;
            State.D[destinationRegister] = result;
            SetSubtractFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.SubLongAddressDisplacementToData);
        }

        private void ExecuteAddLongDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var destination = State.D[destinationRegister];
            var source = State.D[sourceRegister];
            var result = destination + source;
            State.D[destinationRegister] = result;
            SetAddFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddLongDataToData);
        }

        private void ExecuteAddWordDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var destination = State.D[destinationRegister] & 0xFFFF;
            var source = State.D[sourceRegister] & 0xFFFF;
            var result = (ushort)(destination + source);
            WriteDataRegisterWord(destinationRegister, result);
            SetAddFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.AddWordDataToData);
        }

        private void ExecuteAddxLongDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var destination = State.D[destinationRegister];
            var source = State.D[sourceRegister];
            var extend = State.GetFlag(M68kCpuState.Extend) ? 1u : 0u;
            var result = unchecked(destination + source + extend);
            State.D[destinationRegister] = result;
            SetAddxFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddxLongDataToData);
        }

        private void ExecuteAddLongPostIncrementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var sourceAddress = State.A[sourceRegister];
            var destination = State.D[destinationRegister];
            var source = ReadLong(sourceAddress);
            WriteGeneralRegister(true, sourceRegister, sourceAddress + 4);
            var result = destination + source;
            State.D[destinationRegister] = result;
            SetAddFlags(destination, source, result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.AddLongPostIncrementToData);
        }

        private void ExecuteAddWordDataToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = (opcode >> 9) & 7;
            var destinationRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[destinationRegister] + displacement));
            var destination = ReadWord(address);
            var source = State.D[sourceRegister] & 0xFFFF;
            var result = destination + source;
            WriteWord(address, (ushort)result);
            SetAddFlags(destination, source, result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.AddWordDataToAddressDisplacement);
        }

        private void ExecuteAddaLongImmediateToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var source = FetchLong();
            WriteGeneralRegister(true, register, State.A[register] + source);
            CompleteTiming(M68kInstructionTimingKey.AddaLongImmediateToAddress);
        }

        private void ExecuteAddaLongDataToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = (opcode >> 9) & 7;
            var dataRegister = opcode & 7;
            WriteGeneralRegister(true, addressRegister, State.A[addressRegister] + State.D[dataRegister]);
            CompleteTiming(M68kInstructionTimingKey.AddaLongDataToAddress);
        }

        private void ExecuteAddaLongAddressDisplacementToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destinationRegister = (opcode >> 9) & 7;
            var addressRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var source = ReadLong(unchecked((uint)(State.A[addressRegister] + displacement)));
            WriteGeneralRegister(true, destinationRegister, State.A[destinationRegister] + source);
            CompleteTiming(M68kInstructionTimingKey.AddaLongAddressDisplacementToAddress);
        }

        private void ExecuteSubaLongImmediateToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var source = FetchLong();
            WriteGeneralRegister(true, register, State.A[register] - source);
            CompleteTiming(M68kInstructionTimingKey.SubaLongImmediateToAddress);
        }

        private void ExecuteLongMultiplyDivide(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var extension = FetchWord();
            if ((extension & 0x83F8) != 0)
            {
                throw new UnsupportedM68kTimingException(opcode, State.LastInstructionProgramCounter, _profile);
            }

            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            var source = ReadLongDataSource(mode, register, opcode);
            var primaryDestination = (extension >> 12) & 7;
            var secondaryDestination = extension & 7;
            var signed = (extension & 0x0800) != 0;
            var extendedResult = (extension & 0x0400) != 0;

            if ((opcode & 0xFFC0) == 0x4C00)
            {
                ExecuteMultiplyLong(source, primaryDestination, secondaryDestination, signed, extendedResult);
                CompleteTiming(signed ? M68kInstructionTimingKey.MulsLong : M68kInstructionTimingKey.MuluLong);
                return;
            }

            ExecuteDivideLong(source, primaryDestination, secondaryDestination, signed, extendedResult);
            CompleteTiming(signed ? M68kInstructionTimingKey.DivsLong : M68kInstructionTimingKey.DivuLong);
        }

        private void ExecuteMultiplyLong(
            uint source,
            int lowRegister,
            int highRegister,
            bool signed,
            bool extendedResult)
        {
            if (signed)
            {
                var product = (long)unchecked((int)State.D[lowRegister]) * unchecked((int)source);
                var rawProduct = unchecked((ulong)product);
                if (extendedResult)
                {
                    State.D[highRegister] = (uint)(rawProduct >> 32);
                    State.D[lowRegister] = (uint)rawProduct;
                    State.SetFlag(M68kCpuState.Negative, product < 0);
                    State.SetFlag(M68kCpuState.Zero, product == 0);
                    State.SetFlag(M68kCpuState.Overflow, false);
                }
                else
                {
                    State.D[lowRegister] = (uint)rawProduct;
                    State.SetNegativeZero((uint)rawProduct, M68kOperandSize.Long);
                    State.SetFlag(M68kCpuState.Overflow, product < int.MinValue || product > int.MaxValue);
                }
            }
            else
            {
                var product = (ulong)State.D[lowRegister] * source;
                if (extendedResult)
                {
                    State.D[highRegister] = (uint)(product >> 32);
                    State.D[lowRegister] = (uint)product;
                    State.SetFlag(M68kCpuState.Negative, (product & 0x8000_0000_0000_0000ul) != 0);
                    State.SetFlag(M68kCpuState.Zero, product == 0);
                    State.SetFlag(M68kCpuState.Overflow, false);
                }
                else
                {
                    State.D[lowRegister] = (uint)product;
                    State.SetNegativeZero((uint)product, M68kOperandSize.Long);
                    State.SetFlag(M68kCpuState.Overflow, (product >> 32) != 0);
                }
            }

            State.SetFlag(M68kCpuState.Carry, false);
        }

        private void ExecuteDivideLong(
            uint source,
            int quotientRegister,
            int remainderRegister,
            bool signed,
            bool extendedDividend)
        {
            if (source == 0)
            {
                RaiseFormat0Exception(5, State.ProgramCounter, signed ? M68kInstructionTimingKey.DivsLong : M68kInstructionTimingKey.DivuLong);
                return;
            }

            if (signed)
            {
                var divisor = unchecked((int)source);
                var dividend = extendedDividend
                    ? unchecked((long)(((ulong)State.D[remainderRegister] << 32) | State.D[quotientRegister]))
                    : unchecked((int)State.D[quotientRegister]);
                if (dividend == long.MinValue && divisor == -1)
                {
                    State.SetFlag(M68kCpuState.Overflow, true);
                    State.SetFlag(M68kCpuState.Carry, false);
                    return;
                }

                var quotient = dividend / divisor;
                if (quotient < int.MinValue || quotient > int.MaxValue)
                {
                    State.SetFlag(M68kCpuState.Overflow, true);
                    State.SetFlag(M68kCpuState.Carry, false);
                    return;
                }

                var remainder = dividend % divisor;
                if (remainderRegister != quotientRegister)
                {
                    State.D[remainderRegister] = unchecked((uint)(int)remainder);
                }

                State.D[quotientRegister] = unchecked((uint)(int)quotient);
                State.SetNegativeZero((uint)quotient, M68kOperandSize.Long);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
                return;
            }

            var unsignedDivisor = source;
            var unsignedDividend = extendedDividend
                ? ((ulong)State.D[remainderRegister] << 32) | State.D[quotientRegister]
                : State.D[quotientRegister];
            var unsignedQuotient = unsignedDividend / unsignedDivisor;
            if (unsignedQuotient > uint.MaxValue)
            {
                State.SetFlag(M68kCpuState.Overflow, true);
                State.SetFlag(M68kCpuState.Carry, false);
                return;
            }

            var unsignedRemainder = unsignedDividend % unsignedDivisor;
            if (remainderRegister != quotientRegister)
            {
                State.D[remainderRegister] = (uint)unsignedRemainder;
            }

            State.D[quotientRegister] = (uint)unsignedQuotient;
            State.SetNegativeZero((uint)unsignedQuotient, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
        }

        private void ExecuteDivuWordImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var divisor = FetchWord();
            if (divisor == 0)
            {
                RaiseFormat0Exception(5, State.ProgramCounter, M68kInstructionTimingKey.DivuWordImmediateToData);
                return;
            }

            var dividend = State.D[register];
            var quotient = dividend / divisor;
            var remainder = dividend % divisor;
            if (quotient > 0xFFFF)
            {
                State.SetFlag(M68kCpuState.Overflow, true);
            }
            else
            {
                State.D[register] = ((remainder & 0xFFFF) << 16) | (quotient & 0xFFFF);
                State.SetNegativeZero(quotient, M68kOperandSize.Word);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
            }

            CompleteTiming(M68kInstructionTimingKey.DivuWordImmediateToData);
        }

        private void ExecuteDivsWordImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var divisor = FetchWord();
            if (divisor == 0)
            {
                RaiseFormat0Exception(5, State.ProgramCounter, M68kInstructionTimingKey.DivsWordImmediateToData);
                return;
            }

            var signedDivisor = unchecked((short)divisor);
            var signedDividend = unchecked((int)State.D[register]);
            var signedQuotient = signedDividend / signedDivisor;
            var signedRemainder = signedDividend % signedDivisor;
            if (signedQuotient < short.MinValue || signedQuotient > short.MaxValue)
            {
                State.SetFlag(M68kCpuState.Overflow, true);
            }
            else
            {
                var quotient = unchecked((uint)signedQuotient);
                var remainder = unchecked((uint)signedRemainder);
                State.D[register] = ((remainder & 0xFFFF) << 16) | (quotient & 0xFFFF);
                State.SetNegativeZero(quotient, M68kOperandSize.Word);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
            }

            CompleteTiming(M68kInstructionTimingKey.DivsWordImmediateToData);
        }

        private void ExecuteAndLongImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var immediate = FetchLong();
            var result = State.D[register] & immediate;
            State.D[register] = result;
            State.SetNegativeZero(result, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.AndLongImmediateToData);
        }

        private void ExecuteAndiWordImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var immediate = FetchWord();
            var result = (ushort)(State.D[register] & immediate);
            WriteDataRegisterWord(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.AndWordImmediateToData);
        }

        private void ExecuteEoriLongImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var immediate = FetchLong();
            var result = State.D[register] ^ immediate;
            State.D[register] = result;
            State.SetNegativeZero(result, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.EoriLongImmediateToData);
        }

        private void ExecuteEoriWordImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var immediate = FetchWord();
            var result = (ushort)(State.D[register] ^ immediate);
            WriteDataRegisterWord(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.EoriWordImmediateToData);
        }

        private void ExecuteMuluWordImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = (opcode >> 9) & 7;
            var source = FetchWord();
            var result = (uint)((ushort)State.D[register] * source);
            State.D[register] = result;
            State.SetNegativeZero(result, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.MuluWordImmediateToData);
        }

        private void ExecuteAndByteDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var result = (byte)(State.D[destinationRegister] & State.D[sourceRegister]);
            WriteDataRegisterByte(destinationRegister, result);
            SetMoveFlags(result, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.AndByteDataToData);
        }

        private void ExecuteBcdByte(ushort opcode, bool subtract)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var memoryMode = (opcode & 0x0008) != 0;
            byte source;
            byte destination;
            uint destinationAddress = 0;

            if (memoryMode)
            {
                var sourceAddress = State.A[sourceRegister] - (sourceRegister == 7 ? 2u : 1u);
                WriteGeneralRegister(true, sourceRegister, sourceAddress);
                var address = State.A[destinationRegister] - (destinationRegister == 7 ? 2u : 1u);
                WriteGeneralRegister(true, destinationRegister, address);
                source = ReadByte(State.A[sourceRegister]);
                destinationAddress = State.A[destinationRegister];
                destination = ReadByte(destinationAddress);
            }
            else
            {
                source = (byte)State.D[sourceRegister];
                destination = (byte)State.D[destinationRegister];
            }

            var extend = State.GetFlag(M68kCpuState.Extend) ? 1 : 0;
            var result = subtract
                ? SubtractBcdByte(destination, source, extend, out var carry)
                : AddBcdByte(destination, source, extend, out carry);

            if (memoryMode)
            {
                WriteByte(destinationAddress, result);
            }
            else
            {
                WriteDataRegisterByte(destinationRegister, result);
            }

            SetBcdFlags(result, carry);
            CompleteTiming(memoryMode
                ? subtract ? M68kInstructionTimingKey.SbcdBytePredecrementMemory : M68kInstructionTimingKey.AbcdBytePredecrementMemory
                : subtract ? M68kInstructionTimingKey.SbcdByteDataToData : M68kInstructionTimingKey.AbcdByteDataToData);
        }

        private void ExecuteNbcdByte(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            byte destination;
            uint address = 0;
            M68kInstructionTimingKey key;

            switch (mode)
            {
                case 0:
                    destination = (byte)State.D[register];
                    key = M68kInstructionTimingKey.NbcdByteData;
                    break;
                case 2:
                    address = State.A[register];
                    destination = ReadByte(address);
                    key = M68kInstructionTimingKey.NbcdByteAddressIndirect;
                    break;
                case 3:
                    address = State.A[register];
                    destination = ReadByte(address);
                    WriteGeneralRegister(true, register, address + (register == 7 ? 2u : 1u));
                    key = M68kInstructionTimingKey.NbcdBytePostIncrement;
                    break;
                case 4:
                    address = State.A[register] - (register == 7 ? 2u : 1u);
                    WriteGeneralRegister(true, register, address);
                    destination = ReadByte(address);
                    key = M68kInstructionTimingKey.NbcdBytePredecrement;
                    break;
                case 5:
                {
                    var displacement = unchecked((int)(short)FetchWord());
                    address = unchecked((uint)(State.A[register] + displacement));
                    destination = ReadByte(address);
                    key = M68kInstructionTimingKey.NbcdByteAddressDisplacement;
                    break;
                }
                case 6:
                {
                    var extension = FetchWord();
                    address = CalculateBriefIndexedAddress(register, extension, opcode);
                    destination = ReadByte(address);
                    key = M68kInstructionTimingKey.NbcdByteBriefIndexed;
                    break;
                }
                case 7 when register == 0:
                    address = unchecked((uint)(int)(short)FetchWord());
                    destination = ReadByte(address);
                    key = M68kInstructionTimingKey.NbcdByteAbsoluteWord;
                    break;
                case 7 when register == 1:
                    address = FetchLong();
                    destination = ReadByte(address);
                    key = M68kInstructionTimingKey.NbcdByteAbsoluteLong;
                    break;
                default:
                    RaiseFormat0Exception(4, State.LastInstructionProgramCounter, M68kInstructionTimingKey.IllegalInstruction);
                    return;
            }

            var extend = State.GetFlag(M68kCpuState.Extend) ? 1 : 0;
            var result = SubtractBcdByte(0, destination, extend, out var carry);
            if (mode == 0)
            {
                WriteDataRegisterByte(register, result);
            }
            else
            {
                WriteByte(address, result);
            }

            SetBcdFlags(result, carry);
            CompleteTiming(key);
        }

        private void ExecuteOriByteImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var immediate = (byte)FetchWord();
            var result = (byte)((State.D[register] & 0xFF) | immediate);
            State.D[register] = (State.D[register] & 0xFFFF_FF00u) | result;
            State.SetNegativeZero(result, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.OriByteImmediateToData);
        }

        private void ExecuteBtstByteImmediateAbsoluteLong()
        {
            BeginInstruction(0x0839);
            _ = FetchWord();
            var bit = FetchWord() & 7;
            var address = FetchLong();
            var value = ReadByte(address);
            State.SetFlag(M68kCpuState.Zero, (value & (1 << bit)) == 0);
            CompleteTiming(M68kInstructionTimingKey.BtstByteImmediateAbsoluteLong);
        }

        private void ExecuteBchgByteImmediateAbsoluteLong()
        {
            BeginInstruction(0x0879);
            _ = FetchWord();
            var bit = FetchWord() & 7;
            var address = FetchLong();
            var value = ReadByte(address);
            var mask = (byte)(1 << bit);
            State.SetFlag(M68kCpuState.Zero, (value & mask) == 0);
            WriteByte(address, (byte)(value ^ mask));
            CompleteTiming(M68kInstructionTimingKey.BchgByteImmediateAbsoluteLong);
        }

        private void ExecuteBclrByteImmediateAbsoluteLong()
        {
            BeginInstruction(0x08B9);
            _ = FetchWord();
            var bit = FetchWord() & 7;
            var address = FetchLong();
            var value = ReadByte(address);
            var mask = (byte)(1 << bit);
            State.SetFlag(M68kCpuState.Zero, (value & mask) == 0);
            WriteByte(address, (byte)(value & ~mask));
            CompleteTiming(M68kInstructionTimingKey.BclrByteImmediateAbsoluteLong);
        }

        private void ExecuteBsetByteImmediateAbsoluteLong()
        {
            BeginInstruction(0x08F9);
            _ = FetchWord();
            var bit = FetchWord() & 7;
            var address = FetchLong();
            var value = ReadByte(address);
            var mask = (byte)(1 << bit);
            State.SetFlag(M68kCpuState.Zero, (value & mask) == 0);
            WriteByte(address, (byte)(value | mask));
            CompleteTiming(M68kInstructionTimingKey.BsetByteImmediateAbsoluteLong);
        }

        private void ExecuteBsetByteImmediateAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = opcode & 7;
            var bit = FetchWord() & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var address = unchecked((uint)(State.A[addressRegister] + displacement));
            var value = ReadByte(address);
            var mask = (byte)(1 << bit);
            State.SetFlag(M68kCpuState.Zero, (value & mask) == 0);
            WriteByte(address, (byte)(value | mask));
            CompleteTiming(M68kInstructionTimingKey.BsetByteImmediateAddressDisplacement);
        }

        private void ExecuteBtstImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var bit = FetchWord() & 31;
            State.SetFlag(M68kCpuState.Zero, (State.D[register] & (1u << bit)) == 0);
            CompleteTiming(M68kInstructionTimingKey.BtstImmediateData);
        }

        private void ExecuteBtstDynamicData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var bitRegister = (opcode >> 9) & 7;
            var register = opcode & 7;
            var bit = (int)(State.D[bitRegister] & 31);
            State.SetFlag(M68kCpuState.Zero, (State.D[register] & (1u << bit)) == 0);
            CompleteTiming(M68kInstructionTimingKey.BtstDynamicData);
        }

        private void ExecuteBsetImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var bit = FetchWord() & 31;
            var mask = 1u << bit;
            State.SetFlag(M68kCpuState.Zero, (State.D[register] & mask) == 0);
            State.D[register] |= mask;
            CompleteTiming(M68kInstructionTimingKey.BsetImmediateData);
        }

        private void ExecuteBclrImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var bit = FetchWord() & 31;
            var mask = 1u << bit;
            State.SetFlag(M68kCpuState.Zero, (State.D[register] & mask) == 0);
            State.D[register] &= ~mask;
            CompleteTiming(M68kInstructionTimingKey.BclrImmediateData);
        }

        private void ExecuteBclrDynamicData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var bitRegister = (opcode >> 9) & 7;
            var register = opcode & 7;
            var bit = (int)(State.D[bitRegister] & 31);
            var mask = 1u << bit;
            State.SetFlag(M68kCpuState.Zero, (State.D[register] & mask) == 0);
            State.D[register] &= ~mask;
            CompleteTiming(M68kInstructionTimingKey.BclrDynamicData);
        }

        private void ExecuteSwapData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var value = State.D[register];
            var result = (value << 16) | (value >> 16);
            State.D[register] = result;
            SetMoveFlags(result, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.SwapData);
        }

        private void ExecuteExtWordData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var result = unchecked((ushort)(short)(sbyte)(State.D[register] & 0xFF));
            WriteDataRegisterWord(register, result);
            SetMoveFlags(result, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.ExtWordData);
        }

        private void ExecuteTstWordData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var value = State.D[opcode & 7] & 0xFFFF;
            State.SetNegativeZero(value, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            CompleteTiming(M68kInstructionTimingKey.TstWordData);
        }

        private void ExecuteAsrLongImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var value = State.D[register];
            var result = unchecked((uint)((int)value >> count));
            var carry = ((value >> (count - 1)) & 1) != 0;
            State.D[register] = result;
            State.SetNegativeZero(result, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, carry);
            State.SetFlag(M68kCpuState.Extend, carry);
            CompleteTiming(M68kInstructionTimingKey.AsrLongImmediateData);
        }

        private void ExecuteAsrWordImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var value = (ushort)(State.D[register] & 0xFFFF);
            var result = (ushort)((short)value >> count);
            var carry = ((value >> (count - 1)) & 1) != 0;
            WriteDataRegisterWord(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, carry);
            State.SetFlag(M68kCpuState.Extend, carry);
            CompleteTiming(M68kInstructionTimingKey.AsrWordImmediateData);
        }

        private void ExecuteLsrLongImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var value = State.D[register];
            var result = value >> count;
            var carry = ((value >> (count - 1)) & 1) != 0;
            State.D[register] = result;
            State.SetNegativeZero(result, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, carry);
            State.SetFlag(M68kCpuState.Extend, carry);
            CompleteTiming(M68kInstructionTimingKey.LsrLongImmediateData);
        }

        private void ExecuteAslLongImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var result = State.D[register];
            var carry = false;
            var overflow = false;
            for (var i = 0; i < count; i++)
            {
                var oldSign = (result & 0x8000_0000) != 0;
                carry = oldSign;
                result <<= 1;
                var newSign = (result & 0x8000_0000) != 0;
                overflow |= oldSign != newSign;
            }

            State.D[register] = result;
            State.SetNegativeZero(result, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, overflow);
            State.SetFlag(M68kCpuState.Carry, carry);
            State.SetFlag(M68kCpuState.Extend, carry);
            CompleteTiming(M68kInstructionTimingKey.AslLongImmediateData);
        }

        private void ExecuteAslWordImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var value = (ushort)(State.D[register] & 0xFFFF);
            var result = value;
            var carry = false;
            var overflow = false;
            for (var i = 0; i < count; i++)
            {
                var oldSign = (result & 0x8000) != 0;
                carry = oldSign;
                result = (ushort)(result << 1);
                var newSign = (result & 0x8000) != 0;
                overflow |= oldSign != newSign;
            }

            WriteDataRegisterWord(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, overflow);
            State.SetFlag(M68kCpuState.Carry, carry);
            State.SetFlag(M68kCpuState.Extend, carry);
            CompleteTiming(M68kInstructionTimingKey.AslWordImmediateData);
        }

        private void ExecuteRorByteImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var value = (byte)State.D[register];
            var result = (byte)((value >> count) | (value << (8 - count)));
            WriteDataRegisterByte(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Byte);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, (result & 0x80) != 0);
            CompleteTiming(M68kInstructionTimingKey.RorByteImmediateData);
        }

        private void ExecuteRorWordImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var value = (ushort)State.D[register];
            var result = (ushort)((value >> count) | (value << (16 - count)));
            WriteDataRegisterWord(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, (result & 0x8000) != 0);
            CompleteTiming(M68kInstructionTimingKey.RorWordImmediateData);
        }

        private void ExecuteRolWordImmediateData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var register = opcode & 7;
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var value = (ushort)State.D[register];
            var result = (ushort)((value << count) | (value >> (16 - count)));
            WriteDataRegisterWord(register, result);
            State.SetNegativeZero(result, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, (result & 0x0001) != 0);
            CompleteTiming(M68kInstructionTimingKey.RolWordImmediateData);
        }

        private void ExecuteByteBranch(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var condition = (opcode >> 8) & 0x0F;
            var displacement = unchecked((int)(sbyte)(opcode & 0xFF));
            var branchBase = State.ProgramCounter;

            if (condition == 0x1)
            {
                PushLong(branchBase);
                State.ProgramCounter = unchecked((uint)(branchBase + displacement));
                CompleteTiming(M68kInstructionTimingKey.BsrByte);
                return;
            }

            if (CheckCondition(condition))
            {
                State.ProgramCounter = unchecked((uint)(branchBase + displacement));
                CompleteTiming(M68kInstructionTimingKey.BranchByteTaken);
                return;
            }

            CompleteTiming(M68kInstructionTimingKey.BranchByteNotTaken);
        }

        private void ExecuteCmpiByteImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = (byte)FetchWord();
            var destination = State.D[opcode & 7] & 0xFF;
            SetCompareFlags(destination, source, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.CmpiByteImmediateToData);
        }

        private void ExecuteCmpiByteImmediateToAddressIndirect(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = (byte)FetchWord();
            var destination = ReadByte(State.A[opcode & 7]);
            SetCompareFlags(destination, source, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.CmpiByteImmediateToAddressIndirect);
        }

        private void ExecuteCmpiByteImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = opcode & 7;
            var source = (byte)FetchWord();
            var displacement = unchecked((int)(short)FetchWord());
            var destination = ReadByte(unchecked((uint)(State.A[addressRegister] + displacement)));
            SetCompareFlags(destination, source, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.CmpiByteImmediateToAddressDisplacement);
        }

        private void ExecuteCmpiWordImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = FetchWord();
            var destination = State.D[opcode & 7] & 0xFFFF;
            SetCompareFlags(destination, source, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.CmpiWordImmediateToData);
        }

        private void ExecuteCmpiWordImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var addressRegister = opcode & 7;
            var source = FetchWord();
            var displacement = unchecked((int)(short)FetchWord());
            var destination = ReadWord(unchecked((uint)(State.A[addressRegister] + displacement)));
            SetCompareFlags(destination, source, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.CmpiWordImmediateToAddressDisplacement);
        }

        private void ExecuteCmpiWordImmediateToAbsoluteLong()
        {
            BeginInstruction(0x0C79);
            _ = FetchWord();
            var source = FetchWord();
            var address = FetchLong();
            var destination = ReadWord(address);
            SetCompareFlags(destination, source, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.CmpiWordImmediateToAbsoluteLong);
        }

        private void ExecuteCmpiLongImmediateToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = FetchLong();
            var destination = State.D[opcode & 7];
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpiLongImmediateToData);
        }

        private void ExecuteCmpiLongImmediateToPostIncrement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = FetchLong();
            var register = opcode & 7;
            var destination = ReadLong(State.A[register]);
            State.A[register] += 4;
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpiLongImmediateToPostIncrement);
        }

        private void ExecuteCmpiLongImmediateToAddressDisplacement(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = FetchLong();
            var addressRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var destination = ReadLong(unchecked((uint)(State.A[addressRegister] + displacement)));
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpiLongImmediateToAddressDisplacement);
        }

        private void ExecuteCmpiLongImmediateToAbsoluteLong()
        {
            BeginInstruction(0x0CB9);
            _ = FetchWord();
            var source = FetchLong();
            var address = FetchLong();
            var destination = ReadLong(address);
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpiLongImmediateToAbsoluteLong);
        }

        private void ExecuteCmpaLongImmediateToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var source = FetchLong();
            var destination = State.A[(opcode >> 9) & 7];
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpaLongImmediateToAddress);
        }

        private void ExecuteCmpaLongDataToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.A[(opcode >> 9) & 7];
            var source = State.D[opcode & 7];
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpaLongDataToAddress);
        }

        private void ExecuteCmpaLongAddressToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.A[(opcode >> 9) & 7];
            var source = State.A[opcode & 7];
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpaLongAddressToAddress);
        }

        private void ExecuteCmpaLongAddressIndirectToAddress(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.A[(opcode >> 9) & 7];
            var source = ReadLong(State.A[opcode & 7]);
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpaLongAddressIndirectToAddress);
        }

        private void ExecuteCmpLongDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7];
            var source = State.D[opcode & 7];
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpLongDataToData);
        }

        private void ExecuteCmpLongAddressToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7];
            var source = State.A[opcode & 7];
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpLongAddressToData);
        }

        private void ExecuteCmpLongAddressIndirectToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7];
            var source = ReadLong(State.A[opcode & 7]);
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpLongAddressIndirectToData);
        }

        private void ExecuteCmpLongPostIncrementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7];
            var sourceRegister = opcode & 7;
            var address = State.A[sourceRegister];
            var source = ReadLong(address);
            WriteGeneralRegister(true, sourceRegister, address + 4);
            SetCompareFlags(destination, source, M68kOperandSize.Long);
            CompleteTiming(M68kInstructionTimingKey.CmpLongPostIncrementToData);
        }

        private void ExecuteCmpByteDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7] & 0xFF;
            var source = State.D[opcode & 7] & 0xFF;
            SetCompareFlags(destination, source, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.CmpByteDataToData);
        }

        private void ExecuteCmpByteAddressIndirectToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7] & 0xFF;
            var source = ReadByte(State.A[opcode & 7]);
            SetCompareFlags(destination, source, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.CmpByteAddressIndirectToData);
        }

        private void ExecuteCmpByteAddressDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7] & 0xFF;
            var sourceRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var source = ReadByte(unchecked((uint)(State.A[sourceRegister] + displacement)));
            SetCompareFlags(destination, source, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.CmpByteAddressDisplacementToData);
        }

        private void ExecuteCmpByteAbsoluteLongToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7] & 0xFF;
            var address = FetchLong();
            var source = ReadByte(address);
            SetCompareFlags(destination, source, M68kOperandSize.Byte);
            CompleteTiming(M68kInstructionTimingKey.CmpByteAbsoluteLongToData);
        }

        private void ExecuteCmpWordDataToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7] & 0xFFFF;
            var source = State.D[opcode & 7] & 0xFFFF;
            SetCompareFlags(destination, source, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.CmpWordDataToData);
        }

        private void ExecuteCmpWordAddressDisplacementToData(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var destination = State.D[(opcode >> 9) & 7] & 0xFFFF;
            var sourceRegister = opcode & 7;
            var displacement = unchecked((int)(short)FetchWord());
            var source = ReadWord(unchecked((uint)(State.A[sourceRegister] + displacement)));
            SetCompareFlags(destination, source, M68kOperandSize.Word);
            CompleteTiming(M68kInstructionTimingKey.CmpWordAddressDisplacementToData);
        }

        private void ExecuteWordBranch(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var branchBase = State.ProgramCounter;
            var condition = (opcode >> 8) & 0x0F;
            var displacement = unchecked((int)(short)FetchWord());
            var returnAddress = State.ProgramCounter;

            if (condition == 0x1)
            {
                PushLong(returnAddress);
                State.ProgramCounter = unchecked((uint)(branchBase + displacement));
                CompleteTiming(M68kInstructionTimingKey.BsrWord);
                return;
            }

            if (CheckCondition(condition))
            {
                State.ProgramCounter = unchecked((uint)(branchBase + displacement));
                CompleteTiming(M68kInstructionTimingKey.BranchWordTaken);
                return;
            }

            CompleteTiming(M68kInstructionTimingKey.BranchWordNotTaken);
        }

        private void ExecuteLongBranch(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var branchBase = State.ProgramCounter;
            var condition = (opcode >> 8) & 0x0F;
            var displacement = unchecked((int)FetchLong());
            var returnAddress = State.ProgramCounter;

            if (condition == 0x1)
            {
                PushLong(returnAddress);
                State.ProgramCounter = unchecked((uint)(branchBase + displacement));
                CompleteTiming(M68kInstructionTimingKey.BsrLong);
                return;
            }

            if (CheckCondition(condition))
            {
                State.ProgramCounter = unchecked((uint)(branchBase + displacement));
                CompleteTiming(M68kInstructionTimingKey.BranchLongTaken);
                return;
            }

            CompleteTiming(M68kInstructionTimingKey.BranchLongNotTaken);
        }

        private void ExecuteSccAbsoluteLong(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var condition = (opcode >> 8) & 0x0F;
            var address = FetchLong();
            WriteByte(address, CheckCondition(condition) ? (byte)0xFF : (byte)0x00);
            CompleteTiming(M68kInstructionTimingKey.SccAbsoluteLong);
        }

        private void ExecuteDbcc(ushort opcode)
        {
            BeginInstruction(opcode);
            _ = FetchWord();
            var branchBase = State.ProgramCounter;
            var condition = (opcode >> 8) & 0x0F;
            var displacement = unchecked((int)(short)FetchWord());
            if (CheckCondition(condition))
            {
                CompleteTiming(M68kInstructionTimingKey.DbccConditionTrue);
                return;
            }

            var register = opcode & 7;
            var counter = (ushort)(State.D[register] - 1);
            State.D[register] = (State.D[register] & 0xFFFF_0000u) | counter;
            if (counter != 0xFFFF)
            {
                State.ProgramCounter = unchecked((uint)(branchBase + displacement));
                CompleteTiming(M68kInstructionTimingKey.DbccBranchTaken);
                return;
            }

            CompleteTiming(M68kInstructionTimingKey.DbccExpired);
        }

        protected void BeginInstruction(ushort opcode)
        {
            State.LastInstructionProgramCounter = State.ProgramCounter;
            State.LastOpcode = opcode;
            _instructionFrequency.Record(opcode);
        }

        protected virtual void RaiseFormat0Exception(int vector, uint stackedProgramCounter, M68kInstructionTimingKey timingKey)
        {
            var savedStatusRegister = State.StatusRegister;
            State.StatusRegister = (ushort)((State.StatusRegister | M68kCpuState.Supervisor) & ~M68kCpuState.Master);
            PushWord((ushort)(Format0ExceptionFrame | ((vector * 4) & 0x0FFF)));
            PushLong(stackedProgramCounter);
            PushWord(savedStatusRegister);
            State.ProgramCounter = ReadLong(State.VectorBaseRegister + ((uint)vector * 4));
            CompleteTiming(timingKey);
        }

        protected virtual uint ReadControlRegister(int register, uint instructionPc)
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

        protected virtual void WriteControlRegister(int register, uint value, uint instructionPc)
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
                    _timing.ApplyCacheControl(State.CacheControlRegister, State.CacheAddressRegister);
                    break;
                case 0x801:
                    State.VectorBaseRegister = value;
                    break;
                case 0x802:
                    State.CacheAddressRegister = value;
                    _timing.ApplyCacheControl(State.CacheControlRegister, State.CacheAddressRegister);
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

        protected uint RaiseIllegalControlRegister(uint instructionPc)
        {
            RaiseFormat0Exception(4, instructionPc, M68kInstructionTimingKey.IllegalInstruction);
            return 0;
        }

        protected uint ReadGeneralRegister(bool addressRegister, int register)
            => addressRegister ? State.A[register] : State.D[register];

        protected void WriteGeneralRegister(bool addressRegister, int register, uint value)
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

        protected ushort FetchWord()
        {
            var address = State.ProgramCounter;
            var value = _bus is IM68kCodeReader codeReader && _timing.ProbeInstructionCache(address)
                ? codeReader.ReadHostWord(address)
                : ReadWord(address, AmigaBusAccessKind.CpuInstructionFetch);
            State.ProgramCounter += 2;
            return value;
        }

        protected uint FetchLong()
        {
            var high = FetchWord();
            var low = FetchWord();
            return ((uint)high << 16) | low;
        }

        protected byte ReadByte(uint address, AmigaBusAccessKind accessKind = AmigaBusAccessKind.CpuDataRead)
        {
            return _busBridge.ReadByte(address, accessKind);
        }

        protected ushort ReadWord(uint address, AmigaBusAccessKind accessKind = AmigaBusAccessKind.CpuDataRead)
        {
            return _busBridge.ReadWord(address, accessKind);
        }

        protected uint ReadLong(uint address)
        {
            return _busBridge.ReadLong(address, AmigaBusAccessKind.CpuDataRead);
        }

        protected void WriteByte(uint address, byte value)
        {
            _busBridge.WriteByte(address, value, AmigaBusAccessKind.CpuDataWrite);
        }

        protected void WriteWord(uint address, ushort value)
        {
            _busBridge.WriteWord(address, value, AmigaBusAccessKind.CpuDataWrite);
        }

        protected void WriteLong(uint address, uint value)
        {
            _busBridge.WriteLong(address, value, AmigaBusAccessKind.CpuDataWrite);
        }

        protected void PushWord(ushort value)
        {
            State.SetActiveStackPointer(State.A[7] - 2);
            WriteWord(State.A[7], value);
        }

        protected void PushLong(uint value)
        {
            State.SetActiveStackPointer(State.A[7] - 4);
            WriteLong(State.A[7], value);
        }

        protected uint PullLong()
        {
            var value = ReadLong(State.A[7]);
            State.SetActiveStackPointer(State.A[7] + 4);
            return value;
        }

        protected void CompleteTiming(M68kInstructionTimingKey key)
        {
            _timing.CompleteInstruction(_timing.GetPlan(key));
        }

        private void CompleteMovemLongTiming(
            M68kInstructionTimingKey key,
            string name,
            int registerCount,
            bool registerToMemory)
        {
            const int registerListImmediateAddressCycles = 4;
            var nativeCycles = _profile.Model == M68kAcceleratorModel.M68030
                ? registerToMemory
                    ? 4 + (2 * registerCount) + registerListImmediateAddressCycles
                    : 8 + (4 * registerCount) + registerListImmediateAddressCycles
                : registerToMemory
                    ? 4 + (3 * registerCount) + registerListImmediateAddressCycles
                    : 8 + (4 * registerCount) + registerListImmediateAddressCycles;
            var plan = _profile.Model == M68kAcceleratorModel.M68030
                ? M68kInstructionPlan.CreateHeadTail(key, name, nativeCycles, 2, 0)
                : M68kInstructionPlan.CreateFlat(key, name, nativeCycles);
            _timing.CompleteInstruction(plan);
        }

        private static int CountSetBits(ushort value)
        {
            var count = 0;
            while (value != 0)
            {
                value &= (ushort)(value - 1);
                count++;
            }

            return count;
        }

        private void SetCompareFlags(uint destination, uint source, M68kOperandSize size)
        {
            var mask = size switch
            {
                M68kOperandSize.Byte => 0xFFu,
                M68kOperandSize.Word => 0xFFFFu,
                _ => 0xFFFF_FFFFu
            };
            var sign = size switch
            {
                M68kOperandSize.Byte => 0x80u,
                M68kOperandSize.Word => 0x8000u,
                _ => 0x8000_0000u
            };
            destination &= mask;
            source &= mask;
            var result = (destination - source) & mask;
            State.SetNegativeZero(result, size);
            State.SetFlag(M68kCpuState.Overflow, ((destination ^ source) & (destination ^ result) & sign) != 0);
            State.SetFlag(M68kCpuState.Carry, source > destination);
        }

        private void SetAddFlags(uint destination, uint source, uint result, M68kOperandSize size)
        {
            var mask = size switch
            {
                M68kOperandSize.Byte => 0xFFu,
                M68kOperandSize.Word => 0xFFFFu,
                _ => 0xFFFF_FFFFu
            };
            var sign = size switch
            {
                M68kOperandSize.Byte => 0x80u,
                M68kOperandSize.Word => 0x8000u,
                _ => 0x8000_0000u
            };
            destination &= mask;
            source &= mask;
            result &= mask;
            var fullResult = (ulong)destination + source;
            var carry = fullResult > mask;
            State.SetNegativeZero(result, size);
            State.SetFlag(M68kCpuState.Overflow, ((~(destination ^ source) & (destination ^ result) & sign) != 0));
            State.SetFlag(M68kCpuState.Carry, carry);
            State.SetFlag(M68kCpuState.Extend, carry);
        }

        private void SetAddxFlags(uint destination, uint source, uint result, M68kOperandSize size)
        {
            var mask = size switch
            {
                M68kOperandSize.Byte => 0xFFu,
                M68kOperandSize.Word => 0xFFFFu,
                _ => 0xFFFF_FFFFu
            };
            var sign = size switch
            {
                M68kOperandSize.Byte => 0x80u,
                M68kOperandSize.Word => 0x8000u,
                _ => 0x8000_0000u
            };
            destination &= mask;
            source &= mask;
            result &= mask;
            var extend = State.GetFlag(M68kCpuState.Extend) ? 1u : 0u;
            var fullResult = (ulong)destination + source + extend;
            var carry = fullResult > mask;
            if (result != 0)
            {
                State.SetFlag(M68kCpuState.Zero, false);
            }

            State.SetFlag(M68kCpuState.Negative, (result & sign) != 0);
            State.SetFlag(M68kCpuState.Overflow, ((~(destination ^ source) & (destination ^ result) & sign) != 0));
            State.SetFlag(M68kCpuState.Carry, carry);
            State.SetFlag(M68kCpuState.Extend, carry);
        }

        private void SetSubtractFlags(uint destination, uint source, uint result, M68kOperandSize size)
        {
            SetCompareFlags(destination, source, size);
            State.SetFlag(M68kCpuState.Extend, State.GetFlag(M68kCpuState.Carry));
        }

        private void SetMoveFlags(uint value, M68kOperandSize size)
        {
            State.SetNegativeZero(value, size);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
        }

        private static byte AddBcdByte(byte destination, byte source, int extend, out bool carry)
        {
            var low = (destination & 0x0F) + (source & 0x0F) + extend;
            var high = (destination >> 4) + (source >> 4);
            if (low > 9)
            {
                low -= 10;
                high++;
            }

            carry = high > 9;
            if (carry)
            {
                high -= 10;
            }

            return (byte)((high << 4) | low);
        }

        private static byte SubtractBcdByte(byte destination, byte source, int extend, out bool carry)
        {
            var low = (destination & 0x0F) - (source & 0x0F) - extend;
            var high = (destination >> 4) - (source >> 4);
            if (low < 0)
            {
                low += 10;
                high--;
            }

            carry = high < 0;
            if (carry)
            {
                high += 10;
            }

            return (byte)((high << 4) | low);
        }

        private void SetBcdFlags(byte result, bool carry)
        {
            if (result != 0)
            {
                State.SetFlag(M68kCpuState.Zero, false);
            }

            State.SetFlag(M68kCpuState.Negative, (result & 0x80) != 0);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, carry);
            State.SetFlag(M68kCpuState.Extend, carry);
        }

        private void WriteDataRegisterByte(int register, byte value)
        {
            State.D[register] = (State.D[register] & 0xFFFF_FF00u) | value;
        }

        private void WriteDataRegisterWord(int register, ushort value)
        {
            State.D[register] = (State.D[register] & 0xFFFF_0000u) | value;
        }

        private uint ReadLongDataSource(int mode, int register, ushort opcode)
        {
            return mode switch
            {
                0 => State.D[register],
                2 => ReadLong(State.A[register]),
                3 => ReadLongPostIncrement(register),
                4 => ReadLongPredecrement(register),
                5 => ReadLong(unchecked((uint)(State.A[register] + unchecked((int)(short)FetchWord())))),
                6 => ReadLong(CalculateBriefIndexedAddress(register, FetchWord(), opcode)),
                7 => ReadLongExtendedSource(register, opcode),
                _ => throw new UnsupportedM68kTimingException(opcode, State.LastInstructionProgramCounter, _profile)
            };
        }

        private uint ReadLongPostIncrement(int register)
        {
            var address = State.A[register];
            var value = ReadLong(address);
            WriteGeneralRegister(true, register, address + 4);
            return value;
        }

        private uint ReadLongPredecrement(int register)
        {
            WriteGeneralRegister(true, register, State.A[register] - 4);
            return ReadLong(State.A[register]);
        }

        private uint ReadLongExtendedSource(int register, ushort opcode)
        {
            switch (register)
            {
                case 0:
                    return ReadLong(unchecked((uint)(short)FetchWord()));
                case 1:
                    return ReadLong(FetchLong());
                case 2:
                {
                    var extensionAddress = State.ProgramCounter;
                    var displacement = unchecked((int)(short)FetchWord());
                    return ReadLong(unchecked((uint)(extensionAddress + displacement)));
                }
                case 3:
                {
                    var extensionAddress = State.ProgramCounter;
                    var extension = FetchWord();
                    return ReadLong(CalculateBriefIndexedAddress(extensionAddress, extension, opcode));
                }
                case 4:
                    return FetchLong();
                default:
                    throw new UnsupportedM68kTimingException(opcode, State.LastInstructionProgramCounter, _profile);
            }
        }

        private uint CalculateBriefIndexedAddress(int baseRegister, ushort extension, ushort opcode)
            => CalculateBriefIndexedAddress(State.A[baseRegister], extension, opcode);

        private uint CalculateBriefIndexedAddress(uint baseAddress, ushort extension, ushort opcode)
        {
            if ((extension & 0x0100) != 0)
            {
                throw new UnsupportedM68kTimingException(opcode, State.LastInstructionProgramCounter, _profile);
            }

            var indexRegister = (extension >> 12) & 7;
            var usesAddressRegister = (extension & 0x8000) != 0;
            var usesLongIndex = (extension & 0x0800) != 0;
            var scale = 1 << ((extension >> 9) & 0x3);
            var displacement = unchecked((int)(sbyte)(extension & 0xFF));
            var rawIndex = usesAddressRegister ? State.A[indexRegister] : State.D[indexRegister];
            var index = usesLongIndex
                ? unchecked((int)rawIndex)
                : unchecked((int)(short)(rawIndex & 0xFFFF));
            return unchecked((uint)(baseAddress + displacement + (index * scale)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SynchronizeNativeToMachine()
        {
            _timing.SynchronizeNativeToMachine();
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
