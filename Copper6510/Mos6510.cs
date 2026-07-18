/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace Copper6510
{
    /// <summary>
    /// Bus interface used by the MOS 6510 CPU core to access memory and devices one cycle at a time.
    /// </summary>
    public interface IMos6510Bus
    {
        /// <summary>
        /// Reads one byte from the emulated bus.
        /// </summary>
        /// <param name="address">The 16-bit CPU address.</param>
        /// <param name="kind">The reason for the bus access.</param>
        /// <returns>The byte read from the bus.</returns>
        byte Read(ushort address, Mos6510BusAccessKind kind = Mos6510BusAccessKind.Read);

        /// <summary>
        /// Writes one byte to the emulated bus.
        /// </summary>
        /// <param name="address">The 16-bit CPU address.</param>
        /// <param name="value">The byte to write.</param>
        /// <param name="kind">The reason for the bus access.</param>
        void Write(ushort address, byte value, Mos6510BusAccessKind kind = Mos6510BusAccessKind.Write);
    }

    /// <summary>
    /// Cycle-aware MOS 6510 CPU core with official and common undocumented opcode support.
    /// </summary>
    [HotPath]
    public sealed partial class Mos6510
    {
        private const byte Carry = 0x01;
        private const byte Zero = 0x02;
        private const byte InterruptDisable = 0x04;
        private const byte Decimal = 0x08;
        private const byte Break = 0x10;
        private const byte Unused = 0x20;
        private const byte Overflow = 0x40;
        private const byte Negative = 0x80;

        private readonly IMos6510Bus _bus;
        private Mos6510OperationKind _activeOperation;
        private Mos6510OperationKind _interruptVectorKind;
        private int _operationCycle;
        private ushort _interruptVectorBase;
        private byte _interruptVectorLow;
        private bool _irqLine;
        private bool _nmiLine;
        private bool _nmiPending;
        private bool _irqAccepted;
        private bool _blockIrqOnce;
        private bool _blockNmiOnce;
        private bool _readyLine = true;
        private bool _busAvailable = true;
        private bool _resetLine;
        private int _resetAssertedCycles;
        private bool _resetArmed;
        private bool _unstableStoreRdyStall;

        /// <summary>
        /// Initializes a new instance of the <see cref="Mos6510"/> class.
        /// </summary>
        /// <param name="bus">The bus used by the CPU core.</param>
        public Mos6510(IMos6510Bus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            InitializeState();
        }

        /// <summary>
        /// Gets or sets the accumulator register.
        /// </summary>
        public byte A { get; set; }

        /// <summary>
        /// Gets or sets the X index register.
        /// </summary>
        public byte X { get; set; }

        /// <summary>
        /// Gets or sets the Y index register.
        /// </summary>
        public byte Y { get; set; }

        /// <summary>
        /// Gets or sets the stack pointer register.
        /// </summary>
        public byte StackPointer { get; set; }

        /// <summary>
        /// Gets or sets the program counter.
        /// </summary>
        public ushort ProgramCounter { get; set; }

        /// <summary>
        /// Gets or sets the processor status register.
        /// </summary>
        public byte Status { get; set; }

        /// <summary>
        /// Gets the total CPU cycles executed by this core.
        /// </summary>
        public long Cycles { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the CPU is halted by a JAM/KIL opcode.
        /// </summary>
        public bool Halted { get; private set; }

        /// <summary>
        /// Gets the last opcode fetched by the CPU core.
        /// </summary>
        public byte LastOpcode { get; private set; }

        /// <summary>
        /// Initializes deterministic CPU state without executing the hardware RESET sequence.
        /// </summary>
        /// <param name="programCounter">The program counter to install.</param>
        public void InitializeState(ushort programCounter = 0)
        {
            A = 0;
            X = 0;
            Y = 0;
            StackPointer = 0xFD;
            ProgramCounter = programCounter;
            Status = Unused | InterruptDisable;
            Cycles = 0;
            Halted = false;
            LastOpcode = 0;
            _activeOperation = Mos6510OperationKind.None;
            _operationCycle = 0;
            _interruptVectorBase = 0;
            _interruptVectorKind = Mos6510OperationKind.None;
            _nmiPending = false;
            _irqAccepted = false;
            _blockIrqOnce = false;
            _blockNmiOnce = false;
            _irqLine = false;
            _nmiLine = false;
            _resetLine = false;
            _resetAssertedCycles = 0;
            _resetArmed = false;
            _unstableStoreRdyStall = false;
            _readyLine = true;
            _busAvailable = true;
        }

        /// <summary>
        /// Sets the asserted state of the level-sensitive IRQ input.
        /// </summary>
        public void SetIrqLine(bool asserted)
        {
            _irqLine = asserted;
        }

        /// <summary>
        /// Sets the asserted state of the edge-sensitive NMI input.
        /// </summary>
        public void SetNmiLine(bool asserted)
        {
            if (asserted && !_nmiLine)
            {
                _nmiPending = true;
            }

            _nmiLine = asserted;
        }

        /// <summary>
        /// Sets the asserted state of the RESET input.
        /// </summary>
        public void SetResetLine(bool asserted)
        {
            _resetLine = asserted;
        }

        /// <summary>
        /// Sets whether read cycles may advance. Write cycles are not stopped by RDY.
        /// </summary>
        public void SetReadyLine(bool ready)
        {
            _readyLine = ready;
        }

        /// <summary>
        /// Sets whether the CPU owns the external bus (AEC high).
        /// </summary>
        public void SetBusAvailable(bool available)
        {
            _busAvailable = available;
        }

        /// <summary>
        /// Resets the CPU cycle counter without changing register state.
        /// </summary>
        public void ResetCycles()
        {
            Cycles = 0;
        }

        /// <summary>
        /// Advances the CPU cycle counter without executing instructions.
        /// </summary>
        /// <param name="cycles">The number of cycles to add.</param>
        public void AdvanceCycles(long cycles)
        {
            if (cycles <= 0)
            {
                return;
            }

            Cycles += cycles;
        }

        /// <summary>
        /// Sets the accumulator and updates the zero and negative flags from the value.
        /// </summary>
        /// <param name="value">The new accumulator value.</param>
        public void SetAccumulatorAndFlags(byte value)
        {
            A = value;
            SetZn(A);
        }

        /// <summary>
        /// Starts executing a host-provided subroutine and pushes a sentinel return address on the stack.
        /// </summary>
        /// <param name="address">The subroutine entry address.</param>
        /// <param name="accumulator">The accumulator value to use at entry.</param>
        /// <param name="x">The X register value to use at entry.</param>
        /// <param name="y">The Y register value to use at entry.</param>
        public void BeginSubroutine(ushort address, byte accumulator, byte x = 0, byte y = 0)
        {
            _activeOperation = Mos6510OperationKind.None;
            _operationCycle = 0;
            _irqAccepted = false;
            _blockIrqOnce = false;
            _blockNmiOnce = false;
            A = accumulator;
            X = x;
            Y = y;
            StackPointer = 0xFD;
            ProgramCounter = address;
            Halted = false;
            _bus.Write(0x01FD, 0xFF, Mos6510BusAccessKind.StackWrite);
            _bus.Write(0x01FC, 0xFE, Mos6510BusAccessKind.StackWrite);
            StackPointer = 0xFB;
        }

        /// <summary>
        /// Executes one CPU cycle.
        /// </summary>
        public Mos6510CycleResult StepCycle()
        {
            if (_resetLine)
            {
                _resetAssertedCycles++;
                if (_resetAssertedCycles == 2)
                {
                    _resetArmed = true;
                    AbortActiveOperation();
                    Halted = false;
                }

                Cycles++;
                return new Mos6510CycleResult(Mos6510OperationKind.None, Mos6510OperationKind.None, false);
            }

            if (!_resetArmed && _resetAssertedCycles != 0)
            {
                _resetAssertedCycles = 0;
            }

            Mos6510OperationKind started = Mos6510OperationKind.None;
            if (_activeOperation == Mos6510OperationKind.None)
            {
                if (_resetArmed)
                {
                    _resetArmed = false;
                    _resetAssertedCycles = 0;
                    StartDirectOperation(Mos6510OperationKind.Reset, 0xFFFC);
                }
                else if (Halted)
                {
                    Cycles++;
                    return new Mos6510CycleResult(
                        Mos6510OperationKind.Halted,
                        Mos6510OperationKind.Halted,
                        false);
                }
                else if (_nmiPending && !_blockNmiOnce)
                {
                    _nmiPending = false;
                    StartDirectOperation(Mos6510OperationKind.Nmi, 0xFFFA);
                }
                else if (!_blockIrqOnce && (_irqAccepted || (_irqLine && !GetFlag(InterruptDisable))))
                {
                    _irqAccepted = false;
                    StartDirectOperation(Mos6510OperationKind.Irq, 0xFFFE);
                }
                else
                {
                    StartInstructionOperation();
                }

                started = _activeOperation;
                _blockNmiOnce = false;
                _blockIrqOnce = false;
            }

            return _activeOperation == Mos6510OperationKind.Instruction
                ? StepInstructionCycle(started)
                : StepDirectOperation(started);
        }

        /// <summary>
        /// Executes through interrupt/reset entry, if any, and then one logical instruction.
        /// </summary>
        /// <returns>The number of elapsed CPU cycles.</returns>
        public int ExecuteInstruction()
        {
            var start = Cycles;
            while (true)
            {
                var result = StepCycle();
                if (result.CompletedOperation == Mos6510OperationKind.Instruction ||
                    result.CompletedOperation == Mos6510OperationKind.Halted)
                {
                    return checked((int)(Cycles - start));
                }
            }
        }

        private void StartInstructionOperation()
        {
            _activeOperation = Mos6510OperationKind.Instruction;
            _operationCycle = 0;
            _interruptVectorBase = 0;
            _interruptVectorKind = Mos6510OperationKind.None;
            _unstableStoreRdyStall = false;
        }

        private void StartDirectOperation(Mos6510OperationKind operation, ushort vectorBase)
        {
            _activeOperation = operation;
            _interruptVectorKind = operation;
            _interruptVectorBase = vectorBase;
            _operationCycle = 0;
            _interruptVectorLow = 0;
            if (operation == Mos6510OperationKind.Reset)
            {
                _nmiPending = false;
                _irqAccepted = false;
                Halted = false;
            }
        }

        private Mos6510CycleResult StepInstructionCycle(Mos6510OperationKind started)
        {
            return StepMicrocodedInstructionCycle(started);
        }

        private Mos6510CycleResult StepDirectOperation(Mos6510OperationKind started)
        {
            var operation = _activeOperation;
            var advanced = operation == Mos6510OperationKind.Reset
                ? StepResetBusCycle()
                : StepInterruptBusCycle();
            Cycles++;
            if (!advanced)
            {
                return new Mos6510CycleResult(started, Mos6510OperationKind.None, false);
            }

            _operationCycle++;
            if (_operationCycle < 7)
            {
                return new Mos6510CycleResult(started, Mos6510OperationKind.None, true);
            }

            var completed = operation == Mos6510OperationKind.Reset
                ? Mos6510OperationKind.Reset
                : _interruptVectorKind;
            if (operation != Mos6510OperationKind.Reset &&
                _interruptVectorBase != 0xFFFA &&
                _nmiPending)
            {
                _blockNmiOnce = true;
            }

            _activeOperation = Mos6510OperationKind.None;
            _operationCycle = 0;
            return new Mos6510CycleResult(started, completed, true);
        }

        private bool StepInterruptBusCycle()
        {
            switch (_operationCycle)
            {
                case 0:
                    return TryDirectRead(ProgramCounter, Mos6510BusAccessKind.DiscardedOpcodeFetch, out _);
                case 1:
                    return TryDirectRead(ProgramCounter, Mos6510BusAccessKind.DummyRead, out _);
                case 2:
                    if (!TryDirectWrite(
                        (ushort)(0x0100 | StackPointer),
                        (byte)(ProgramCounter >> 8),
                        Mos6510BusAccessKind.StackWrite))
                    {
                        return false;
                    }

                    StackPointer--;
                    return true;
                case 3:
                    if (!TryDirectWrite(
                        (ushort)(0x0100 | StackPointer),
                        (byte)ProgramCounter,
                        Mos6510BusAccessKind.StackWrite))
                    {
                        return false;
                    }

                    StackPointer--;
                    return true;
                case 4:
                    if (!TryDirectWrite(
                        (ushort)(0x0100 | StackPointer),
                        (byte)((Status & ~Break) | Unused),
                        Mos6510BusAccessKind.StackWrite))
                    {
                        return false;
                    }

                    StackPointer--;
                    return true;
                case 5:
                    if (_activeOperation == Mos6510OperationKind.Irq && _nmiPending)
                    {
                        _nmiPending = false;
                        _interruptVectorBase = 0xFFFA;
                        _interruptVectorKind = Mos6510OperationKind.Nmi;
                    }

                    return TryDirectRead(_interruptVectorBase, Mos6510BusAccessKind.VectorRead, out _interruptVectorLow);
                default:
                    if (!TryDirectRead(
                        (ushort)(_interruptVectorBase + 1),
                        Mos6510BusAccessKind.VectorRead,
                        out var high))
                    {
                        return false;
                    }

                    ProgramCounter = (ushort)(_interruptVectorLow | (high << 8));
                    SetFlag(InterruptDisable, true);
                    return true;
            }
        }

        private bool StepResetBusCycle()
        {
            switch (_operationCycle)
            {
                case 0:
                    return TryDirectRead(ProgramCounter, Mos6510BusAccessKind.DiscardedOpcodeFetch, out _);
                case 1:
                    return TryDirectRead(ProgramCounter, Mos6510BusAccessKind.DummyRead, out _);
                case 2:
                case 3:
                case 4:
                    if (!TryDirectRead(
                        (ushort)(0x0100 | StackPointer),
                        Mos6510BusAccessKind.StackRead,
                        out _))
                    {
                        return false;
                    }

                    StackPointer--;
                    return true;
                case 5:
                    return TryDirectRead(0xFFFC, Mos6510BusAccessKind.VectorRead, out _interruptVectorLow);
                default:
                    if (!TryDirectRead(0xFFFD, Mos6510BusAccessKind.VectorRead, out var high))
                    {
                        return false;
                    }

                    ProgramCounter = (ushort)(_interruptVectorLow | (high << 8));
                    SetFlag(InterruptDisable, true);
                    Halted = false;
                    _nmiPending = false;
                    _irqAccepted = false;
                    return true;
            }
        }

        private bool TryDirectRead(ushort address, Mos6510BusAccessKind kind, out byte value)
        {
            value = 0;
            if (!_busAvailable)
            {
                return false;
            }

            value = _bus.Read(address, kind);
            return _readyLine;
        }

        private bool TryDirectWrite(ushort address, byte value, Mos6510BusAccessKind kind)
        {
            if (!_busAvailable)
            {
                return false;
            }

            _bus.Write(address, value, kind);
            return true;
        }

        private void SampleInterruptsAfterInstruction(byte initialStatus, byte opcode, int cycles)
        {
            var branchTaken = (opcode & 0x1F) == 0x10 && cycles > 2;
            if (branchTaken)
            {
                _blockNmiOnce = _nmiPending;
                _blockIrqOnce = _irqLine;
                return;
            }

            var useInitialInterruptMask = opcode == 0x28 || opcode == 0x58 || opcode == 0x78;
            var sampledStatus = useInitialInterruptMask ? initialStatus : Status;
            if (_irqLine && (sampledStatus & InterruptDisable) == 0)
            {
                _irqAccepted = true;
            }
            else if (useInitialInterruptMask &&
                     (initialStatus & InterruptDisable) != 0 &&
                     (Status & InterruptDisable) == 0)
            {
                _blockIrqOnce = true;
            }

            if (opcode == 0x00 && _interruptVectorBase == 0xFFFE && _nmiPending)
            {
                _blockNmiOnce = true;
            }
        }

        private void AbortActiveOperation()
        {
            _activeOperation = Mos6510OperationKind.None;
            _operationCycle = 0;
            _interruptVectorBase = 0;
            _interruptVectorKind = Mos6510OperationKind.None;
            _irqAccepted = false;
            _blockIrqOnce = false;
            _nmiPending = false;
        }

        private bool GetFlag(byte flag)
        {
            return (Status & flag) != 0;
        }

        private void SetFlag(byte flag, bool value)
        {
            Status = value ? (byte)(Status | flag | Unused) : (byte)((Status & ~flag) | Unused);
        }

        private void SetZn(byte value)
        {
            SetFlag(Zero, value == 0);
            SetFlag(Negative, (value & 0x80) != 0);
        }

        private void BinaryAdc(byte value)
        {
            var carryIn = GetFlag(Carry) ? 1 : 0;
            var result = A + value + carryIn;
            var output = (byte)result;
            SetFlag(Carry, result > 0xFF);
            SetFlag(Overflow, (~(A ^ value) & (A ^ output) & 0x80) != 0);
            A = output;
            SetZn(A);
        }

        private void DecimalAdc(byte value)
        {
            var carryIn = GetFlag(Carry) ? 1 : 0;
            var binary = A + value + carryIn;
            var low = (A & 0x0F) + (value & 0x0F) + carryIn;
            var high = (A >> 4) + (value >> 4);
            if (low > 9)
            {
                low += 6;
                high++;
            }

            if (high > 9)
            {
                high += 6;
            }

            var output = (byte)((high << 4) | (low & 0x0F));
            SetFlag(Carry, high > 15);
            SetFlag(Overflow, (~(A ^ value) & (A ^ binary) & 0x80) != 0);
            A = output;
            SetZn(A);
        }

        private void DecimalSbc(byte value)
        {
            var carryIn = GetFlag(Carry) ? 0 : 1;
            var result = A - value - carryIn;
            var low = (A & 0x0F) - (value & 0x0F) - carryIn;
            var high = (A >> 4) - (value >> 4);
            if (low < 0)
            {
                low -= 6;
                high--;
            }

            if (high < 0)
            {
                high -= 6;
            }

            var output = (byte)(((high << 4) & 0xF0) | (low & 0x0F));
            SetFlag(Carry, result >= 0);
            SetFlag(Overflow, ((A ^ output) & (A ^ value) & 0x80) != 0);
            A = output;
            SetZn(A);
        }

        private byte Asl(byte value)
        {
            SetFlag(Carry, (value & 0x80) != 0);
            value = (byte)(value << 1);
            SetZn(value);
            return value;
        }

        private byte Lsr(byte value)
        {
            SetFlag(Carry, (value & 0x01) != 0);
            value = (byte)(value >> 1);
            SetZn(value);
            return value;
        }

        private byte Rol(byte value)
        {
            var carryIn = GetFlag(Carry) ? 1 : 0;
            SetFlag(Carry, (value & 0x80) != 0);
            value = (byte)((value << 1) | carryIn);
            SetZn(value);
            return value;
        }

        private byte Ror(byte value)
        {
            var carryIn = GetFlag(Carry) ? 0x80 : 0;
            SetFlag(Carry, (value & 0x01) != 0);
            value = (byte)((value >> 1) | carryIn);
            SetZn(value);
            return value;
        }

        private void Anc(byte value)
        {
            A &= value;
            SetZn(A);
            SetFlag(Carry, (A & 0x80) != 0);
        }

        private void Alr(byte value)
        {
            A &= value;
            A = Lsr(A);
        }

        private void Arr(byte value)
        {
            A &= value;
            A = Ror(A);
            SetFlag(Carry, (A & 0x40) != 0);
            SetFlag(Overflow, ((A >> 6) ^ (A >> 5) & 1) != 0);
        }

        private void Axs(byte value)
        {
            var source = A & X;
            var result = source - value;
            X = (byte)result;
            SetFlag(Carry, result >= 0);
            SetZn(X);
        }

        private void Las(byte value)
        {
            var result = (byte)(value & StackPointer);
            A = result;
            X = result;
            StackPointer = result;
            SetZn(result);
        }

        private static bool IsUnstableStore(byte opcode)
        {
            return opcode == 0x93 || opcode == 0x9B || opcode == 0x9C || opcode == 0x9E || opcode == 0x9F;
        }

        private static bool PageCrossed(int a, int b)
        {
            return (a & 0xFF00) != (b & 0xFF00);
        }
    }
}
