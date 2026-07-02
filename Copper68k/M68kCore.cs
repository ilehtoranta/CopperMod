/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Copper68k
{
    /// <summary>
    /// Operand sizes used by 68k integer instructions and bus accesses.
    /// </summary>
    public enum M68kOperandSize
    {
        /// <summary>
        /// An 8-bit operand.
        /// </summary>
        Byte = 1,

        /// <summary>
        /// A 16-bit operand.
        /// </summary>
        Word = 2,

        /// <summary>
        /// A 32-bit operand.
        /// </summary>
        Long = 4
    }

    /// <summary>
    /// CPU models exposed by the Copper68k factory.
    /// </summary>
    public enum M68kCpuModel
    {
        /// <summary>
        /// Motorola MC68000-compatible execution.
        /// </summary>
        M68000 = 0,

        /// <summary>
        /// Motorola MC68020-compatible execution.
        /// </summary>
        M68020 = 1,

        /// <summary>
        /// Motorola MC68030-compatible execution.
        /// </summary>
        M68030 = 2,

        /// <summary>
        /// Motorola MC68040-compatible execution.
        /// </summary>
        M68040 = 3
    }

    /// <summary>
    /// Bus interface used by Copper68k cores to access memory, devices, and host traps.
    /// </summary>
    /// <remarks>
    /// Bus implementations receive the current CPU cycle by reference and may advance it to model
    /// memory wait states or device latency. Multi-byte values are transferred in 68k big-endian order.
    /// </remarks>
    public interface IM68kBus
    {
        /// <summary>
        /// Reads one byte from the emulated bus.
        /// </summary>
        /// <param name="address">The 32-bit CPU address.</param>
        /// <param name="cycle">The current CPU cycle, which the bus may advance.</param>
        /// <param name="accessKind">The reason for the bus access.</param>
        /// <returns>The byte read from the bus.</returns>
        byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind);

        /// <summary>
        /// Reads one 16-bit big-endian word from the emulated bus.
        /// </summary>
        /// <param name="address">The 32-bit CPU address.</param>
        /// <param name="cycle">The current CPU cycle, which the bus may advance.</param>
        /// <param name="accessKind">The reason for the bus access.</param>
        /// <returns>The word read from the bus.</returns>
        ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind);

        /// <summary>
        /// Reads one 32-bit big-endian long word from the emulated bus.
        /// </summary>
        /// <param name="address">The 32-bit CPU address.</param>
        /// <param name="cycle">The current CPU cycle, which the bus may advance.</param>
        /// <param name="accessKind">The reason for the bus access.</param>
        /// <returns>The long word read from the bus.</returns>
        uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind);

        /// <summary>
        /// Writes one byte to the emulated bus.
        /// </summary>
        /// <param name="address">The 32-bit CPU address.</param>
        /// <param name="value">The byte to write.</param>
        /// <param name="cycle">The current CPU cycle, which the bus may advance.</param>
        /// <param name="accessKind">The reason for the bus access.</param>
        void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind);

        /// <summary>
        /// Writes one 16-bit big-endian word to the emulated bus.
        /// </summary>
        /// <param name="address">The 32-bit CPU address.</param>
        /// <param name="value">The word to write.</param>
        /// <param name="cycle">The current CPU cycle, which the bus may advance.</param>
        /// <param name="accessKind">The reason for the bus access.</param>
        void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind);

        /// <summary>
        /// Writes one 32-bit big-endian long word to the emulated bus.
        /// </summary>
        /// <param name="address">The 32-bit CPU address.</param>
        /// <param name="value">The long word to write.</param>
        /// <param name="cycle">The current CPU cycle, which the bus may advance.</param>
        /// <param name="accessKind">The reason for the bus access.</param>
        void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind);

        /// <summary>
        /// Determines whether an instruction address contains a host trap stub.
        /// </summary>
        /// <param name="address">The instruction address to probe.</param>
        /// <returns><see langword="true"/> if the host wants to intercept the instruction.</returns>
        bool HasHostTrapStub(uint address);

        /// <summary>
        /// Invokes a host trap previously identified by <see cref="HasHostTrapStub"/>.
        /// </summary>
        /// <param name="instructionProgramCounter">The address of the host trap instruction.</param>
        /// <param name="trapId">The trap identifier word following the trap opcode.</param>
        /// <param name="state">The mutable CPU state at the trap point.</param>
        /// <returns><see langword="true"/> if the host handled the trap.</returns>
        bool TryInvokeHostTrap(uint instructionProgramCounter, ushort trapId, M68kCpuState state);

        /// <summary>
        /// Notifies external devices that the 68k RESET instruction was executed.
        /// </summary>
        /// <param name="cycle">The CPU cycle at which the reset notification occurs.</param>
        void ResetExternalDevices(long cycle);
    }

    internal interface IM68kPhysicalAddressMap
    {
        bool IsCpuPhysicalAddressMapped(uint address, int byteCount, M68kBusAccessKind accessKind);
    }

    internal interface IM68kFastMemoryBus
    {
        bool TryReadFastByte(uint address, M68kBusAccessKind accessKind, out byte value);

        bool TryReadFastWord(uint address, M68kBusAccessKind accessKind, out ushort value);

        bool TryReadFastLong(uint address, M68kBusAccessKind accessKind, out uint value);

        bool TryWriteFastByte(uint address, byte value, M68kBusAccessKind accessKind);

        bool TryWriteFastWord(uint address, ushort value, M68kBusAccessKind accessKind);

        bool TryWriteFastLong(uint address, uint value, M68kBusAccessKind accessKind);
    }

    internal interface IM68kCpuDataAccess<TBus, TSelf>
        where TBus : IM68kBus
        where TSelf : struct, IM68kCpuDataAccess<TBus, TSelf>
    {
        static abstract byte ReadByte(TBus bus, uint address, ref long cycle);

        static abstract ushort ReadWord(TBus bus, uint address, ref long cycle);

        static abstract uint ReadLong(TBus bus, uint address, ref long cycle);

        static abstract void WriteByte(TBus bus, uint address, byte value, ref long cycle);

        static abstract void WriteWord(TBus bus, uint address, ushort value, ref long cycle);

        static abstract void WriteLong(TBus bus, uint address, uint value, ref long cycle);
    }

    internal readonly struct M68kNoExactCpuDataAccess<TBus> : IM68kCpuDataAccess<TBus, M68kNoExactCpuDataAccess<TBus>>
        where TBus : IM68kBus
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ReadByte(TBus bus, uint address, ref long cycle)
            => bus.ReadByte(address, ref cycle, M68kBusAccessKind.CpuDataRead);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort ReadWord(TBus bus, uint address, ref long cycle)
            => bus.ReadWord(address, ref cycle, M68kBusAccessKind.CpuDataRead);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ReadLong(TBus bus, uint address, ref long cycle)
            => bus.ReadLong(address, ref cycle, M68kBusAccessKind.CpuDataRead);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteByte(TBus bus, uint address, byte value, ref long cycle)
            => bus.WriteByte(address, value, ref cycle, M68kBusAccessKind.CpuDataWrite);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteWord(TBus bus, uint address, ushort value, ref long cycle)
            => bus.WriteWord(address, value, ref cycle, M68kBusAccessKind.CpuDataWrite);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteLong(TBus bus, uint address, uint value, ref long cycle)
            => bus.WriteLong(address, value, ref cycle, M68kBusAccessKind.CpuDataWrite);
    }

    internal readonly struct M68kInstructionFetchWindow
    {
        public static M68kInstructionFetchWindow Empty { get; } = new(
            Array.Empty<byte>(),
            0,
            0,
            0,
            0,
            0,
            Array.Empty<uint>(),
            0);

        public M68kInstructionFetchWindow(
            byte[] memory,
            int memoryOffset,
            uint startAddress,
            uint endAddressExclusive,
            uint addressMask,
            int busTag,
            uint[] generationSource,
            uint generation)
        {
            Memory = memory ?? throw new ArgumentNullException(nameof(memory));
            MemoryOffset = memoryOffset;
            StartAddress = startAddress;
            EndAddressExclusive = endAddressExclusive;
            AddressMask = addressMask;
            BusTag = busTag;
            GenerationSource = generationSource ?? throw new ArgumentNullException(nameof(generationSource));
            Generation = generation;
        }

        public byte[] Memory { get; }

        public int MemoryOffset { get; }

        public uint StartAddress { get; }

        public uint EndAddressExclusive { get; }

        public uint AddressMask { get; }

        public int BusTag { get; }

        public uint[] GenerationSource { get; }

        public uint Generation { get; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsWord(uint address)
        {
            var generationSource = GenerationSource;
            if (generationSource.Length == 0 ||
                generationSource[0] != Generation)
            {
                return false;
            }

            var normalized = address & AddressMask;
            return normalized >= StartAddress &&
                normalized < EndAddressExclusive &&
                (ulong)normalized + 1u < EndAddressExclusive;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadWord(uint address)
        {
            var offset = MemoryOffset + checked((int)((address & AddressMask) - StartAddress));
            return (ushort)((Memory[offset] << 8) | Memory[offset + 1]);
        }
    }

    internal interface IM68kInstructionFetchWindowBus
    {
        bool TryGetInstructionFetchWindow(uint address, out M68kInstructionFetchWindow window);

        void CommitInstructionFetchWindowWord(in M68kInstructionFetchWindow window, uint address, ref long cycle);
    }

    internal enum M68kTraceBatchWakeSource
    {
        Unknown = 0,
        TargetCycle,
        PendingInterrupt,
        VerticalBlank,
        HorizontalSyncTod,
        CiaTimer,
        Disk,
        Paula,
        Copper,
        Blitter
    }

    /// <summary>
    /// Common interface for Copper68k CPU cores.
    /// </summary>
    public interface IM68kCore : IDisposable
    {
        /// <summary>
        /// Gets the mutable CPU register and execution state.
        /// </summary>
        M68kCpuState State { get; }

        /// <summary>
        /// Executes one logical CPU instruction or one idle cycle while halted or stopped.
        /// </summary>
        /// <returns>The number of machine cycles advanced by the instruction.</returns>
        int ExecuteInstruction();

        /// <summary>
        /// Resets the CPU core to a supplied entry point and supervisor stack pointer.
        /// </summary>
        /// <param name="programCounter">The reset program counter.</param>
        /// <param name="stackPointer">The reset supervisor stack pointer.</param>
        void Reset(uint programCounter, uint stackPointer);

        /// <summary>
        /// Starts executing a host-provided subroutine and pushes a return address on the active stack.
        /// </summary>
        /// <param name="address">The subroutine entry address.</param>
        /// <param name="stackPointer">The stack pointer to use before pushing the return address.</param>
        /// <param name="returnAddress">The return address to push.</param>
        void BeginSubroutine(uint address, uint stackPointer, uint returnAddress);

        /// <summary>
        /// Requests an interrupt at the specified level.
        /// </summary>
        /// <param name="level">The interrupt priority level, from 1 through 7.</param>
        /// <param name="vectorAddress">The byte offset of the vector entry in the active vector table.</param>
        void RequestInterrupt(int level, uint vectorAddress);
    }

    internal interface IM68kInstructionBoundary
    {
        bool BeforeInstruction();

        void AfterInstruction(long previousCycle, long currentCycle);
    }

    internal interface IM68kTraceBatchDiagnosticsBoundary
    {
        M68kTraceBatchWakeSource LastTraceBatchWakeSource { get; }
    }

    internal interface IM68kStoppedCpuFastForwardBoundary : IM68kInstructionBoundary
    {
        bool TryFastForwardStoppedInstruction(M68kCpuState state, long targetCycle, out long advancedCycles);
    }

    internal interface IM68kPureCpuTraceBatchBoundary : IM68kInstructionBoundary
    {
        bool TryBeginPureCpuTraceBatch(M68kCpuState state, long targetCycle, out long batchTargetCycle);

        void AfterPureCpuTraceBatch(long previousCycle, long currentCycle, int instructionCount);
    }

    internal interface IM68kBusAccessTraceBatchBoundary : IM68kInstructionBoundary
    {
        bool TryBeginBusAccessTraceBatch(M68kCpuState state, long targetCycle, out long batchTargetCycle);

        void AfterBusAccessTraceBatch(long previousCycle, long currentCycle, int instructionCount);
    }

    internal interface IM68kBatchCore : IM68kCore
    {
        int ExecuteInstructions(int maxInstructions, long? targetCycle, IM68kInstructionBoundary boundary);
    }

    internal enum M68kBackendKind
    {
        AccurateM68000 = 0,
        AccurateM68020 = 1,
        FastM68000 = 2,
        JitM68000 = 3,
        Cpu32 = 4,
        AccurateM68030 = 5,
        AccurateM68040 = 6,
        JitM68040 = 7
    }

    /// <summary>
    /// Creates Copper68k CPU cores for public CPU models.
    /// </summary>
    public interface IM68kCoreFactory
    {
        /// <summary>
        /// Creates a CPU core for the requested model.
        /// </summary>
        /// <param name="model">The CPU model to emulate.</param>
        /// <param name="bus">The bus implementation used by the CPU core.</param>
        /// <returns>A new CPU core instance.</returns>
        IM68kCore Create(M68kCpuModel model, IM68kBus bus);
    }

    internal interface IM68kBackendCoreFactory : IM68kCoreFactory
    {
        IM68kCore Create(M68kBackendKind backend, IM68kBus bus);
    }

    /// <summary>
    /// Default factory for Copper68k CPU cores.
    /// </summary>
    public sealed class M68kCoreFactory : IM68kBackendCoreFactory
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="M68kCoreFactory"/> class.
        /// </summary>
        public M68kCoreFactory()
        {
        }

        /// <summary>
        /// Gets the shared default factory instance.
        /// </summary>
        public static M68kCoreFactory Default { get; } = new M68kCoreFactory();

        internal static M68kOpcodePlanDispatch M68000OpcodePlanDispatch { get; set; } = M68kOpcodePlanDispatch.KindTable;

        internal static M68kInterpreterCore<TBus, TCpuDataAccess> CreateM68000Core<TBus, TCpuDataAccess>(
            TBus bus,
            TCpuDataAccess cpuDataAccess,
            M68kCpuState? state = null,
            M68kInstructionFrequencyMatrix? instructionFrequency = null,
            bool enableInstructionFetchWindow = true,
            bool enableOpcodePlan = true,
            M68kOpcodePlanDispatch? opcodePlanDispatch = null)
            where TBus : IM68kBus
            where TCpuDataAccess : struct, IM68kCpuDataAccess<TBus, TCpuDataAccess>
            => new M68kInterpreterCore<TBus, TCpuDataAccess>(
                bus,
                cpuDataAccess,
                state ?? new M68kCpuState(),
                instructionFrequency,
                enableInstructionFetchWindow,
                enableOpcodePlan,
                opcodePlanDispatch ?? M68000OpcodePlanDispatch);

        /// <inheritdoc />
        public IM68kCore Create(M68kCpuModel model, IM68kBus bus)
        {
            return model switch
            {
                M68kCpuModel.M68000 => new M68kInterpreter(bus, opcodePlanDispatch: M68000OpcodePlanDispatch),
                M68kCpuModel.M68020 => new M68020Interpreter(bus),
                M68kCpuModel.M68030 => new M68030Interpreter(bus),
                M68kCpuModel.M68040 => new M68040Interpreter(bus),
                _ => throw new M68kEmulationException($"The requested M68k CPU model is not implemented: {model}.")
            };
        }

        /// <summary>
        /// Creates a CPU core for the requested model and execution options.
        /// </summary>
        /// <param name="model">The CPU model to emulate.</param>
        /// <param name="bus">The bus implementation used by the CPU core.</param>
        /// <param name="options">The core creation options.</param>
        /// <returns>A new CPU core instance.</returns>
        public IM68kCore Create(M68kCpuModel model, IM68kBus bus, M68kCoreOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (options.ExecutionMode == M68kExecutionMode.Interpreter)
            {
                return Create(model, bus);
            }

            if (options.ExecutionMode != M68kExecutionMode.Jit)
            {
                throw new M68kEmulationException($"The requested M68k execution mode is not implemented: {options.ExecutionMode}.");
            }

            if (model != M68kCpuModel.M68040)
            {
                throw new M68kEmulationException("Copper68k JIT execution is supported only for MC68040 cores.");
            }

            if (bus is not IM68kJitBus)
            {
                throw new M68kEmulationException("Copper68k MC68040 JIT execution requires a bus that implements IM68kJitBus.");
            }

            return M68kJitCore.CreateM68040(bus);
        }

        internal IM68kCore Create(M68kBackendKind backend, IM68kBus bus)
        {
            if (backend == M68kBackendKind.AccurateM68000)
            {
                return new M68kInterpreter(bus, opcodePlanDispatch: M68000OpcodePlanDispatch);
            }

            if (backend == M68kBackendKind.AccurateM68020)
            {
                return new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
            }

            if (backend == M68kBackendKind.AccurateM68030)
            {
                return new M68030Interpreter(bus, M68020CpuProfile.Ocs68030Accelerator14Mhz);
            }

            if (backend == M68kBackendKind.AccurateM68040)
            {
                return new M68040Interpreter(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz);
            }

            if (backend == M68kBackendKind.JitM68000)
            {
                return new M68kJitCore(bus);
            }

            if (backend == M68kBackendKind.JitM68040)
            {
                return M68kJitCore.CreateM68040(bus);
            }

            throw new M68kEmulationException($"The requested M68k backend is not implemented: {backend}.");
        }

        IM68kCore IM68kBackendCoreFactory.Create(M68kBackendKind backend, IM68kBus bus)
            => Create(backend, bus);
    }

    /// <summary>
    /// Mutable 68k register, control, and timing state shared by Copper68k cores.
    /// </summary>
    public sealed class M68kCpuState
    {
        /// <summary>
        /// Condition-code bit for carry.
        /// </summary>
        public const ushort Carry = 0x0001;

        /// <summary>
        /// Condition-code bit for overflow.
        /// </summary>
        public const ushort Overflow = 0x0002;

        /// <summary>
        /// Condition-code bit for zero.
        /// </summary>
        public const ushort Zero = 0x0004;

        /// <summary>
        /// Condition-code bit for negative.
        /// </summary>
        public const ushort Negative = 0x0008;

        /// <summary>
        /// Condition-code bit for extend.
        /// </summary>
        public const ushort Extend = 0x0010;

        /// <summary>
        /// Status-register bit for 68020+ master stack mode.
        /// </summary>
        public const ushort Master = 0x1000;

        /// <summary>
        /// Status-register bit for supervisor mode.
        /// </summary>
        public const ushort Supervisor = 0x2000;

        /// <summary>
        /// Hardware reset status register value: supervisor mode with interrupt mask 7.
        /// </summary>
        public const ushort ResetStatusRegister = 0x2700;
        private const ushort ConditionCodeMask = Carry | Overflow | Zero | Negative | Extend;

        /// <summary>
        /// Gets the eight data registers D0 through D7.
        /// </summary>
        public uint[] D { get; } = new uint[8];

        /// <summary>
        /// Gets the eight address registers A0 through A7. A7 is the currently active stack pointer.
        /// </summary>
        public uint[] A { get; } = new uint[8];

        private ushort _statusRegister = Supervisor;

        /// <summary>
        /// Initializes a new instance of the <see cref="M68kCpuState"/> class.
        /// </summary>
        public M68kCpuState()
        {
        }

        /// <summary>
        /// Gets or sets the program counter.
        /// </summary>
        public uint ProgramCounter { get; set; }

        /// <summary>
        /// Gets or sets the 16-bit status register.
        /// </summary>
        /// <remarks>
        /// Setting this property updates the active A7 stack pointer when supervisor/user or
        /// 68020+ master/interrupt stack mode changes.
        /// </remarks>
        public ushort StatusRegister
        {
            get => _statusRegister;
            set => SetStatusRegister(value);
        }

        /// <summary>
        /// Gets the saved user stack pointer.
        /// </summary>
        public uint UserStackPointer { get; private set; }

        /// <summary>
        /// Gets the saved supervisor stack pointer.
        /// </summary>
        public uint SupervisorStackPointer { get; private set; }

        /// <summary>
        /// Gets the 68020+ interrupt stack pointer.
        /// </summary>
        public uint InterruptStackPointer => SupervisorStackPointer;

        /// <summary>
        /// Gets the 68020+ master stack pointer.
        /// </summary>
        public uint MasterStackPointer { get; private set; }

        /// <summary>
        /// Gets or sets the 68010+ vector base register.
        /// </summary>
        public uint VectorBaseRegister { get; set; }

        /// <summary>
        /// Gets or sets the 68020+ source function-code register value.
        /// </summary>
        public uint SourceFunctionCode { get; set; }

        /// <summary>
        /// Gets or sets the 68020+ destination function-code register value.
        /// </summary>
        public uint DestinationFunctionCode { get; set; }

        /// <summary>
        /// Gets or sets the 68020+/68040 cache control register value.
        /// </summary>
        public uint CacheControlRegister { get; set; }

        /// <summary>
        /// Gets or sets the 68040 cache address register value.
        /// </summary>
        public uint CacheAddressRegister { get; set; }

        internal M68040FpuState M68040Fpu { get; } = new M68040FpuState();

        internal M68040MmuState M68040Mmu { get; } = new M68040MmuState();

        /// <summary>
        /// Gets or sets the elapsed 68k machine-cycle count.
        /// </summary>
        public long Cycles { get; set; }

        /// <summary>
        /// Gets or sets the elapsed native-cycle count used by 68020+ timing profiles.
        /// </summary>
        public long NativeCycles { get; set; }

        /// <summary>
        /// Gets or sets whether the core is halted.
        /// </summary>
        public bool Halted { get; set; }

        /// <summary>
        /// Gets or sets whether the core is stopped by the STOP instruction.
        /// </summary>
        public bool Stopped { get; set; }

        /// <summary>
        /// Gets or sets the last fetched opcode.
        /// </summary>
        public ushort LastOpcode { get; set; }

        /// <summary>
        /// Gets or sets the program counter of the last instruction start.
        /// </summary>
        public uint LastInstructionProgramCounter { get; set; }

        internal int LastExceptionVector { get; set; } = -1;

        internal int FirstExceptionVector { get; set; } = -1;

        internal uint FirstExceptionStackedProgramCounter { get; set; }

        internal ushort FirstExceptionStatusRegister { get; set; }

        internal ushort FirstExceptionOpcode { get; set; }

        internal uint FirstExceptionInstructionProgramCounter { get; set; }

        internal uint FirstExceptionD0 { get; set; }

        internal uint FirstExceptionD1 { get; set; }

        internal uint FirstExceptionA0 { get; set; }

        internal uint FirstExceptionA6 { get; set; }

        internal uint FirstExceptionA7 { get; set; }

        internal uint LastExceptionStackedProgramCounter { get; set; }

        internal ushort LastExceptionStatusRegister { get; set; }

        internal ushort LastExceptionOpcode { get; set; }

        internal uint LastExceptionInstructionProgramCounter { get; set; }

        internal uint LastExceptionD0 { get; set; }

        internal uint LastExceptionD1 { get; set; }

        internal uint LastExceptionA0 { get; set; }

        internal uint LastExceptionA6 { get; set; }

        internal uint LastExceptionA7 { get; set; }

        internal void RecordException(int vector, uint stackedProgramCounter, ushort savedStatusRegister)
        {
            if (vector < 0)
            {
                FirstExceptionVector = -1;
                FirstExceptionStackedProgramCounter = 0;
                FirstExceptionStatusRegister = 0;
                FirstExceptionOpcode = 0;
                FirstExceptionInstructionProgramCounter = 0;
                FirstExceptionD0 = 0;
                FirstExceptionD1 = 0;
                FirstExceptionA0 = 0;
                FirstExceptionA6 = 0;
                FirstExceptionA7 = 0;
            }
            else if (FirstExceptionVector < 0)
            {
                FirstExceptionVector = vector;
                FirstExceptionStackedProgramCounter = stackedProgramCounter;
                FirstExceptionStatusRegister = savedStatusRegister;
                FirstExceptionOpcode = LastOpcode;
                FirstExceptionInstructionProgramCounter = LastInstructionProgramCounter;
                FirstExceptionD0 = D[0];
                FirstExceptionD1 = D[1];
                FirstExceptionA0 = A[0];
                FirstExceptionA6 = A[6];
                FirstExceptionA7 = A[7];
            }

            LastExceptionVector = vector;
            LastExceptionStackedProgramCounter = stackedProgramCounter;
            LastExceptionStatusRegister = savedStatusRegister;
            LastExceptionOpcode = LastOpcode;
            LastExceptionInstructionProgramCounter = LastInstructionProgramCounter;
            LastExceptionD0 = D[0];
            LastExceptionD1 = D[1];
            LastExceptionA0 = A[0];
            LastExceptionA6 = A[6];
            LastExceptionA7 = A[7];
        }

        internal bool M68020StackModeEnabled { get; private set; }

        internal void EnableM68020StackMode()
        {
            M68020StackModeEnabled = true;
            SetStatusRegister(_statusRegister);
        }

        /// <summary>
        /// Tests whether a status-register flag or mask is set.
        /// </summary>
        /// <param name="flag">The flag or mask to test.</param>
        /// <returns><see langword="true"/> if all bits in <paramref name="flag"/> are set.</returns>
        public bool GetFlag(ushort flag)
        {
            return (_statusRegister & flag) != 0;
        }

        /// <summary>
        /// Sets or clears a status-register flag or mask.
        /// </summary>
        /// <param name="flag">The flag or mask to update.</param>
        /// <param name="value"><see langword="true"/> to set the bits; <see langword="false"/> to clear them.</param>
        public void SetFlag(ushort flag, bool value)
        {
            if ((flag & ~ConditionCodeMask) == 0)
            {
                _statusRegister = value
                    ? (ushort)(_statusRegister | flag)
                    : (ushort)(_statusRegister & ~flag);
                return;
            }

            StatusRegister = value
                ? (ushort)(_statusRegister | flag)
                : (ushort)(_statusRegister & ~flag);
        }

        /// <summary>
        /// Resets the saved user/supervisor stack pointers and selects the active stack pointer.
        /// </summary>
        /// <param name="supervisorStackPointer">The supervisor stack pointer value.</param>
        /// <param name="userStackPointer">The user stack pointer value.</param>
        /// <param name="supervisorMode">Whether A7 should select the supervisor stack.</param>
        public void ResetStackPointers(uint supervisorStackPointer, uint userStackPointer, bool supervisorMode)
        {
            SupervisorStackPointer = supervisorStackPointer;
            UserStackPointer = userStackPointer;
            MasterStackPointer = supervisorStackPointer;
            A[7] = supervisorMode ? supervisorStackPointer : userStackPointer;
            _statusRegister = supervisorMode ? Supervisor : (ushort)0;
        }

        /// <summary>
        /// Sets A7 and the currently active saved stack pointer.
        /// </summary>
        /// <param name="stackPointer">The new active stack pointer value.</param>
        public void SetActiveStackPointer(uint stackPointer)
        {
            A[7] = stackPointer;
            if (M68020StackModeEnabled)
            {
                SetActiveM68020StackPointer(stackPointer);
                return;
            }

            if (GetFlag(Supervisor))
            {
                SupervisorStackPointer = stackPointer;
            }
            else
            {
                UserStackPointer = stackPointer;
            }
        }

        /// <summary>
        /// Sets the saved user stack pointer.
        /// </summary>
        /// <param name="stackPointer">The user stack pointer value.</param>
        public void SetUserStackPointer(uint stackPointer)
        {
            UserStackPointer = stackPointer;
            if (!GetFlag(Supervisor))
            {
                A[7] = stackPointer;
            }
        }

        /// <summary>
        /// Sets the saved interrupt stack pointer.
        /// </summary>
        /// <param name="stackPointer">The interrupt stack pointer value.</param>
        public void SetInterruptStackPointer(uint stackPointer)
        {
            SupervisorStackPointer = stackPointer;
            if (M68020StackModeEnabled && UsesInterruptStack(_statusRegister))
            {
                A[7] = stackPointer;
            }
        }

        /// <summary>
        /// Sets the saved master stack pointer.
        /// </summary>
        /// <param name="stackPointer">The master stack pointer value.</param>
        public void SetMasterStackPointer(uint stackPointer)
        {
            MasterStackPointer = stackPointer;
            if (M68020StackModeEnabled && UsesMasterStack(_statusRegister))
            {
                A[7] = stackPointer;
            }
        }

        /// <summary>
        /// Enters supervisor mode while preserving the current user stack as the active stack.
        /// </summary>
        /// <returns>The previous supervisor stack pointer, or zero if already in supervisor mode.</returns>
        public uint EnterSupervisorModeWithUserStack()
        {
            if (GetFlag(Supervisor))
            {
                return 0;
            }

            var oldSupervisorStackPointer = SupervisorStackPointer;
            UserStackPointer = A[7];
            SupervisorStackPointer = A[7];
            _statusRegister |= Supervisor;
            return oldSupervisorStackPointer;
        }

        /// <summary>
        /// Returns to user mode after <see cref="EnterSupervisorModeWithUserStack"/>.
        /// </summary>
        /// <param name="supervisorStackPointer">The supervisor stack pointer to restore.</param>
        public void ReturnToUserModeWithUserStack(uint supervisorStackPointer)
        {
            if (!GetFlag(Supervisor))
            {
                return;
            }

            UserStackPointer = A[7];
            SupervisorStackPointer = supervisorStackPointer;
            _statusRegister &= unchecked((ushort)~Supervisor);
            A[7] = UserStackPointer;
        }

        private void SetStatusRegister(ushort value)
        {
            if (M68020StackModeEnabled)
            {
                SetM68020StatusRegister(value);
                return;
            }

            var wasSupervisor = (_statusRegister & Supervisor) != 0;
            var isSupervisor = (value & Supervisor) != 0;
            if (wasSupervisor != isSupervisor)
            {
                if (wasSupervisor)
                {
                    SupervisorStackPointer = A[7];
                    A[7] = UserStackPointer;
                }
                else
                {
                    UserStackPointer = A[7];
                    A[7] = SupervisorStackPointer;
                }
            }

            _statusRegister = value;
        }

        private void SetM68020StatusRegister(ushort value)
        {
            SaveActiveM68020StackPointer(_statusRegister);
            _statusRegister = value;
            A[7] = GetActiveM68020StackPointer(value);
        }

        private void SaveActiveM68020StackPointer(ushort statusRegister)
        {
            if (!UsesSupervisorStack(statusRegister))
            {
                UserStackPointer = A[7];
            }
            else if (UsesMasterStack(statusRegister))
            {
                MasterStackPointer = A[7];
            }
            else
            {
                SupervisorStackPointer = A[7];
            }
        }

        private uint GetActiveM68020StackPointer(ushort statusRegister)
        {
            if (!UsesSupervisorStack(statusRegister))
            {
                return UserStackPointer;
            }

            return UsesMasterStack(statusRegister)
                ? MasterStackPointer
                : SupervisorStackPointer;
        }

        private void SetActiveM68020StackPointer(uint stackPointer)
        {
            if (!UsesSupervisorStack(_statusRegister))
            {
                UserStackPointer = stackPointer;
            }
            else if (UsesMasterStack(_statusRegister))
            {
                MasterStackPointer = stackPointer;
            }
            else
            {
                SupervisorStackPointer = stackPointer;
            }
        }

        private static bool UsesSupervisorStack(ushort statusRegister)
            => (statusRegister & Supervisor) != 0;

        private static bool UsesMasterStack(ushort statusRegister)
            => (statusRegister & (Supervisor | Master)) == (Supervisor | Master);

        private static bool UsesInterruptStack(ushort statusRegister)
            => (statusRegister & Supervisor) != 0 && (statusRegister & Master) == 0;

        /// <summary>
        /// Updates the negative and zero condition codes from a sized value.
        /// </summary>
        /// <param name="value">The value to test.</param>
        /// <param name="size">The operand size that defines the mask and sign bit.</param>
        public void SetNegativeZero(uint value, M68kOperandSize size)
        {
            var mask = Mask(size);
            var sign = SignBit(size);
            value &= mask;
            var status = _statusRegister & unchecked((ushort)~(Zero | Negative));
            if (value == 0)
            {
                status |= Zero;
            }

            if ((value & sign) != 0)
            {
                status |= Negative;
            }

            _statusRegister = (ushort)status;
        }

        /// <summary>
        /// Gets the value mask for an operand size.
        /// </summary>
        /// <param name="size">The operand size.</param>
        /// <returns>The unsigned mask for that operand size.</returns>
        public static uint Mask(M68kOperandSize size)
        {
            return size switch
            {
                M68kOperandSize.Byte => 0xFF,
                M68kOperandSize.Word => 0xFFFF,
                _ => 0xFFFF_FFFF
            };
        }

        /// <summary>
        /// Gets the sign bit for an operand size.
        /// </summary>
        /// <param name="size">The operand size.</param>
        /// <returns>The sign bit for that operand size.</returns>
        public static uint SignBit(M68kOperandSize size)
        {
            return size switch
            {
                M68kOperandSize.Byte => 0x80,
                M68kOperandSize.Word => 0x8000,
                _ => 0x8000_0000
            };
        }

        /// <summary>
        /// Sign-extends a byte, word, or long value to 32 bits.
        /// </summary>
        /// <param name="value">The value to sign-extend.</param>
        /// <param name="size">The original operand size.</param>
        /// <returns>The 32-bit sign-extended value.</returns>
        public static uint SignExtend(uint value, M68kOperandSize size)
        {
            value &= Mask(size);
            return size switch
            {
                M68kOperandSize.Byte => (value & 0x80) != 0 ? value | 0xFFFF_FF00 : value,
                M68kOperandSize.Word => (value & 0x8000) != 0 ? value | 0xFFFF_0000 : value,
                _ => value
            };
        }
    }

    /// <summary>
    /// Exception thrown when the 68000 interpreter encounters an unsupported opcode.
    /// </summary>
    public sealed class UnsupportedM68kOpcodeException : M68kEmulationException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UnsupportedM68kOpcodeException"/> class.
        /// </summary>
        /// <param name="opcode">The unsupported opcode.</param>
        /// <param name="programCounter">The instruction address where the opcode was fetched.</param>
        public UnsupportedM68kOpcodeException(ushort opcode, uint programCounter)
            : base($"Unsupported MC68000 opcode 0x{opcode:X4} at 0x{programCounter:X8}.")
        {
            Opcode = opcode;
            ProgramCounter = programCounter;
        }

        /// <summary>
        /// Gets the unsupported opcode.
        /// </summary>
        public ushort Opcode { get; }

        /// <summary>
        /// Gets the instruction address where the opcode was fetched.
        /// </summary>
        public uint ProgramCounter { get; }
    }

    internal sealed class M68kAddressErrorException : Exception
    {
        public static M68kAddressErrorException Instance { get; } = new M68kAddressErrorException();

        private M68kAddressErrorException()
        {
        }
    }

    internal sealed class M68kIllegalInstructionException : Exception
    {
        public static M68kIllegalInstructionException Instance { get; } = new M68kIllegalInstructionException();

        private M68kIllegalInstructionException()
        {
        }
    }

    internal class M68kInterpreterCore<TBus, TCpuDataAccess> : IM68kBatchCore, IM68kInstructionFrequencyProvider
        where TBus : IM68kBus
        where TCpuDataAccess : struct, IM68kCpuDataAccess<TBus, TCpuDataAccess>
    {
        private const int AddressErrorExceptionCycles = 50;
        private const uint SubroutineSentinel = 0xFFFF_FFFC;
        private static readonly M68kPlannedInstructionHandler?[] s_plannedInstructionHandlers = CreatePlannedInstructionHandlers();
        private readonly IM68kBus _bus;
        private readonly TBus _typedBus;
        private readonly IM68kInstructionFetchWindowBus? _instructionFetchWindowBus;
        private readonly M68kInstructionFrequencyMatrix _instructionFrequency;
        private readonly M68kOpcodePlanDispatch _opcodePlanDispatch;
        private M68kInstructionFetchWindow _instructionFetchWindow;
        private uint _prefetchAddress;
        private ushort _prefetchWord;
        private long _prefetchCompletedCycle;
        private bool _prefetchValid;
        private long _cpuBusCycle;
        private long _cpuRetireBusCycle;
        private bool _instructionCycleFloorActive;
        private long _instructionCycleStart;
        private long _instructionCycleFloor;
        private bool _plannedInterpreterCountersEnabled;
        private long _plannedFastInstructions;
        private long _plannedScalarFallbackInstructions;
        private long _plannedNopInstructions;
        private long _plannedMoveqInstructions;
        private long _plannedBranchInstructions;
        private long _plannedDbccInstructions;
        private long _plannedQuickRegisterInstructions;
        private long _plannedMoveInstructions;
        private long _plannedImmediateInstructions;
        private long _plannedImmediateBtstInstructions;
        private long _plannedRegisterArithmeticInstructions;

        private delegate bool M68kPlannedInstructionHandler(M68kInterpreterCore<TBus, TCpuDataAccess> interpreter, ushort opcode, uint instructionPc);

        public M68kInterpreterCore(
            TBus bus,
            TCpuDataAccess cpuDataAccess,
            M68kOpcodePlanDispatch opcodePlanDispatch = M68kOpcodePlanDispatch.KindTable)
            : this(bus, cpuDataAccess, new M68kCpuState(), opcodePlanDispatch: opcodePlanDispatch)
        {
        }

        internal M68kInterpreterCore(
            TBus bus,
            TCpuDataAccess cpuDataAccess,
            M68kCpuState state,
            M68kInstructionFrequencyMatrix? instructionFrequency = null,
            bool enableInstructionFetchWindow = true,
            bool enableOpcodePlan = true,
            M68kOpcodePlanDispatch opcodePlanDispatch = M68kOpcodePlanDispatch.KindTable)
        {
            _typedBus = bus ?? throw new ArgumentNullException(nameof(bus));
            _bus = bus;
            _ = cpuDataAccess;
            _instructionFetchWindowBus = enableInstructionFetchWindow
                ? bus as IM68kInstructionFetchWindowBus
                : null;
            _instructionFetchWindow = M68kInstructionFetchWindow.Empty;
            State = state ?? throw new ArgumentNullException(nameof(state));
            _instructionFrequency = instructionFrequency ?? new M68kInstructionFrequencyMatrix();
            _opcodePlanDispatch = enableOpcodePlan ? opcodePlanDispatch : M68kOpcodePlanDispatch.Scalar;
        }

        public M68kCpuState State { get; }

        internal bool InstructionFrequencyEnabled
        {
            get => _instructionFrequency.Enabled;
            set => _instructionFrequency.Enabled = value;
        }

        internal M68kInstructionFrequencySnapshot CaptureInstructionFrequency()
            => _instructionFrequency.CaptureSnapshot();

        internal void ResetInstructionFrequency()
            => _instructionFrequency.Reset();

        internal bool PlannedInterpreterCountersEnabled
        {
            get => _plannedInterpreterCountersEnabled;
            set => _plannedInterpreterCountersEnabled = value;
        }

        internal M68kPlannedInterpreterCounters CapturePlannedInterpreterCounters()
            => _plannedInterpreterCountersEnabled
                ? new M68kPlannedInterpreterCounters(
                    _plannedFastInstructions,
                    _plannedScalarFallbackInstructions,
                    _plannedNopInstructions,
                    _plannedMoveqInstructions,
                    _plannedBranchInstructions,
                    _plannedDbccInstructions,
                    _plannedQuickRegisterInstructions,
                    _plannedMoveInstructions,
                    _plannedImmediateInstructions,
                    _plannedImmediateBtstInstructions,
                    _plannedRegisterArithmeticInstructions)
                : M68kPlannedInterpreterCounters.Empty;

        internal void ResetPlannedInterpreterCounters()
        {
            _plannedFastInstructions = 0;
            _plannedScalarFallbackInstructions = 0;
            _plannedNopInstructions = 0;
            _plannedMoveqInstructions = 0;
            _plannedBranchInstructions = 0;
            _plannedDbccInstructions = 0;
            _plannedQuickRegisterInstructions = 0;
            _plannedMoveInstructions = 0;
            _plannedImmediateInstructions = 0;
            _plannedImmediateBtstInstructions = 0;
            _plannedRegisterArithmeticInstructions = 0;
        }

        internal static bool HasDelegatePlanForOpcode(ushort opcode)
            => s_plannedInstructionHandlers[opcode] != null;

        bool IM68kInstructionFrequencyProvider.InstructionFrequencyEnabled
        {
            get => InstructionFrequencyEnabled;
            set => InstructionFrequencyEnabled = value;
        }

        M68kInstructionFrequencySnapshot IM68kInstructionFrequencyProvider.CaptureInstructionFrequency()
            => CaptureInstructionFrequency();

        void IM68kInstructionFrequencyProvider.ResetInstructionFrequency()
            => ResetInstructionFrequency();

        public void Dispose()
        {
        }

        internal int ExecuteInstructions(int maxInstructions, long? targetCycle, IM68kInstructionBoundary boundary)
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

        int IM68kBatchCore.ExecuteInstructions(int maxInstructions, long? targetCycle, IM68kInstructionBoundary boundary)
            => ExecuteInstructions(maxInstructions, targetCycle, boundary);

        public int ExecuteInstruction()
        {
            if (State.Halted || State.Stopped)
            {
                State.Cycles++;
                return 1;
            }

            var startCycles = State.Cycles;
            BeginInstructionCycleFloor(startCycles);
            try
            {
                return ExecuteInstructionBody(startCycles);
            }
            catch (M68kAddressErrorException)
            {
                return CompleteInstruction(startCycles);
            }
            catch (M68kIllegalInstructionException)
            {
                return CompleteInstruction(startCycles);
            }
            finally
            {
                _instructionCycleFloorActive = false;
            }
        }

        private int ExecuteInstructionBody(long startCycles)
        {
            var instructionPc = State.ProgramCounter;
            var opcode = FetchWord();
            State.LastOpcode = opcode;
            State.LastInstructionProgramCounter = instructionPc;
            if (_instructionFrequency.Enabled)
            {
                _instructionFrequency.Record(instructionPc, opcode);
            }

            if (_opcodePlanDispatch != M68kOpcodePlanDispatch.Scalar &&
                TryExecutePlannedInstruction(opcode, instructionPc))
            {
                return CompleteInstruction(startCycles);
            }

            RecordPlannedScalarFallback();
            var decoded = DecodeByOpcodeLine(opcode, instructionPc);
            if (decoded)
            {
                return CompleteInstruction(startCycles);
            }

            if ((opcode & 0xF000) == 0xA000)
            {
                RaiseException(10, instructionPc, 34);
                return CompleteInstruction(startCycles);
            }

            if ((opcode & 0xF000) == 0xF000)
            {
                if (opcode == 0xFF00 && _bus.HasHostTrapStub(instructionPc))
                {
                    var trapId = FetchWord();
                    var returnProgramCounter = State.ProgramCounter;
                    if (_bus.TryInvokeHostTrap(instructionPc, trapId, State))
                    {
                        AddCycles(16);
                        if (!State.Halted && State.ProgramCounter == returnProgramCounter)
                        {
                            SetProgramCounterAndFlushPrefetch(PullLong());
                        }
                        else
                        {
                            FlushPrefetch();
                        }

                        return CompleteInstruction(startCycles);
                    }

                    SetProgramCounterAndFlushPrefetch(returnProgramCounter);
                }

                RaiseException(11, instructionPc, 34);
                return CompleteInstruction(startCycles);
            }

            throw new UnsupportedM68kOpcodeException(opcode, instructionPc);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryExecutePlannedInstruction(ushort opcode, uint instructionPc)
        {
            return _opcodePlanDispatch switch
            {
                M68kOpcodePlanDispatch.KindTable => TryExecutePlannedKind(
                    opcode,
                    instructionPc,
                    M68kOpcodePlanTable.Kinds[opcode]),
                M68kOpcodePlanDispatch.ComputedKind => TryExecutePlannedKind(
                    opcode,
                    instructionPc,
                    M68kOpcodePlanTable.ComputeKind(opcode)),
                M68kOpcodePlanDispatch.PackedPlan => TryExecutePackedPlan(
                    opcode,
                    instructionPc,
                    in M68kOpcodePlanTable.PackedPlans[opcode]),
                M68kOpcodePlanDispatch.DelegateTable => TryExecuteDelegatePlan(opcode, instructionPc),
                _ => false
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryExecutePlannedKind(ushort opcode, uint instructionPc, M68kOpcodePlanKind kind)
        {
            switch (kind)
            {
                case M68kOpcodePlanKind.Nop:
                    AddCycles(4);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.Moveq:
                    ExecutePlannedMoveq(opcode);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.Branch:
                    ExecutePlannedBranch(opcode, instructionPc);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.Dbcc:
                    ExecutePlannedDbcc(opcode);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.QuickRegister:
                    ExecutePlannedQuickRegister(opcode);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.Move:
                    ExecutePlannedMove(opcode);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.MoveLongPostincrementToData:
                    ExecutePlannedMoveLongPostincrementToData(opcode);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.MoveLongDataToPostincrement:
                    ExecutePlannedMoveLongDataToPostincrement(opcode);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.Immediate:
                    ExecutePlannedImmediate(opcode);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.ImmediateBtst:
                    ExecutePlannedImmediateBtst(opcode);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.RegisterArithmetic:
                    ExecutePlannedRegisterArithmetic(opcode);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.DataRegisterLongOrToRegister:
                    ExecutePlannedOrLongToDataRegister(opcode);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.DataRegisterLongEorToDestination:
                    ExecutePlannedEorLongToDataRegister(opcode);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.DataRegisterLongAndToRegister:
                    ExecutePlannedAndLongToDataRegister(opcode);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.DataRegisterLongAddToRegister:
                    ExecutePlannedAddLongToDataRegister(opcode);
                    RecordPlannedFast(kind);
                    return true;
                default:
                    return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryExecutePackedPlan(ushort opcode, uint instructionPc, in M68kPackedOpcodePlan plan)
        {
            _ = opcode;
            var kind = plan.Kind;
            switch (kind)
            {
                case M68kOpcodePlanKind.Nop:
                    AddCycles(4);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.Moveq:
                    ExecutePackedMoveq(in plan);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.Branch:
                    ExecutePackedBranch(in plan, instructionPc);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.Dbcc:
                    ExecutePackedDbcc(opcode, in plan);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.QuickRegister:
                    ExecutePackedQuickRegister(opcode, in plan);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.Move:
                    ExecutePackedMove(opcode, in plan);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.MoveLongPostincrementToData:
                    ExecutePackedMoveLongPostincrementToData(in plan);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.MoveLongDataToPostincrement:
                    ExecutePackedMoveLongDataToPostincrement(in plan);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.Immediate:
                    ExecutePackedImmediate(opcode, in plan);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.ImmediateBtst:
                    ExecutePackedImmediateBtst(opcode, in plan);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.RegisterArithmetic:
                    ExecutePackedRegisterArithmetic(in plan);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.DataRegisterLongOrToRegister:
                    ExecutePackedOrLongToDataRegister(in plan);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.DataRegisterLongEorToDestination:
                    ExecutePackedEorLongToDataRegister(in plan);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.DataRegisterLongAndToRegister:
                    ExecutePackedAndLongToDataRegister(in plan);
                    RecordPlannedFast(kind);
                    return true;
                case M68kOpcodePlanKind.DataRegisterLongAddToRegister:
                    ExecutePackedAddLongToDataRegister(in plan);
                    RecordPlannedFast(kind);
                    return true;
                default:
                    return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryExecuteDelegatePlan(ushort opcode, uint instructionPc)
        {
            var handler = s_plannedInstructionHandlers[opcode];
            return handler != null && handler(this, opcode, instructionPc);
        }

        private static M68kPlannedInstructionHandler?[] CreatePlannedInstructionHandlers()
        {
            var handlers = new M68kPlannedInstructionHandler?[0x1_0000];
            for (var opcode = 0; opcode <= 0xFFFF; opcode++)
            {
                handlers[opcode] = GetPlannedInstructionHandler(M68kOpcodePlanTable.Kinds[opcode]);
            }

            return handlers;
        }

        private static M68kPlannedInstructionHandler? GetPlannedInstructionHandler(M68kOpcodePlanKind kind)
            => kind switch
            {
                M68kOpcodePlanKind.Nop => ExecuteNopDelegatePlan,
                M68kOpcodePlanKind.Moveq => ExecuteMoveqDelegatePlan,
                M68kOpcodePlanKind.Branch => ExecuteBranchDelegatePlan,
                M68kOpcodePlanKind.Dbcc => ExecuteDbccDelegatePlan,
                M68kOpcodePlanKind.QuickRegister => ExecuteQuickRegisterDelegatePlan,
                M68kOpcodePlanKind.Move => ExecuteMoveDelegatePlan,
                M68kOpcodePlanKind.MoveLongPostincrementToData => ExecuteMoveLongPostincrementToDataDelegatePlan,
                M68kOpcodePlanKind.MoveLongDataToPostincrement => ExecuteMoveLongDataToPostincrementDelegatePlan,
                M68kOpcodePlanKind.Immediate => ExecuteImmediateDelegatePlan,
                M68kOpcodePlanKind.ImmediateBtst => ExecuteImmediateBtstDelegatePlan,
                M68kOpcodePlanKind.RegisterArithmetic => ExecuteRegisterArithmeticDelegatePlan,
                M68kOpcodePlanKind.DataRegisterLongOrToRegister => ExecuteOrLongToDataRegisterDelegatePlan,
                M68kOpcodePlanKind.DataRegisterLongEorToDestination => ExecuteEorLongToDataRegisterDelegatePlan,
                M68kOpcodePlanKind.DataRegisterLongAndToRegister => ExecuteAndLongToDataRegisterDelegatePlan,
                M68kOpcodePlanKind.DataRegisterLongAddToRegister => ExecuteAddLongToDataRegisterDelegatePlan,
                _ => null
            };

        private static bool ExecuteNopDelegatePlan(M68kInterpreterCore<TBus, TCpuDataAccess> interpreter, ushort opcode, uint instructionPc)
        {
            _ = opcode;
            _ = instructionPc;
            interpreter.AddCycles(4);
            interpreter.RecordPlannedFast(M68kOpcodePlanKind.Nop);
            return true;
        }

        private static bool ExecuteMoveqDelegatePlan(M68kInterpreterCore<TBus, TCpuDataAccess> interpreter, ushort opcode, uint instructionPc)
        {
            _ = instructionPc;
            interpreter.ExecutePlannedMoveq(opcode);
            interpreter.RecordPlannedFast(M68kOpcodePlanKind.Moveq);
            return true;
        }

        private static bool ExecuteBranchDelegatePlan(M68kInterpreterCore<TBus, TCpuDataAccess> interpreter, ushort opcode, uint instructionPc)
        {
            interpreter.ExecutePlannedBranch(opcode, instructionPc);
            interpreter.RecordPlannedFast(M68kOpcodePlanKind.Branch);
            return true;
        }

        private static bool ExecuteDbccDelegatePlan(M68kInterpreterCore<TBus, TCpuDataAccess> interpreter, ushort opcode, uint instructionPc)
        {
            _ = instructionPc;
            interpreter.ExecutePlannedDbcc(opcode);
            interpreter.RecordPlannedFast(M68kOpcodePlanKind.Dbcc);
            return true;
        }

        private static bool ExecuteQuickRegisterDelegatePlan(M68kInterpreterCore<TBus, TCpuDataAccess> interpreter, ushort opcode, uint instructionPc)
        {
            _ = instructionPc;
            interpreter.ExecutePlannedQuickRegister(opcode);
            interpreter.RecordPlannedFast(M68kOpcodePlanKind.QuickRegister);
            return true;
        }

        private static bool ExecuteMoveDelegatePlan(M68kInterpreterCore<TBus, TCpuDataAccess> interpreter, ushort opcode, uint instructionPc)
        {
            _ = instructionPc;
            interpreter.ExecutePlannedMove(opcode);
            interpreter.RecordPlannedFast(M68kOpcodePlanKind.Move);
            return true;
        }

        private static bool ExecuteMoveLongPostincrementToDataDelegatePlan(M68kInterpreterCore<TBus, TCpuDataAccess> interpreter, ushort opcode, uint instructionPc)
        {
            _ = instructionPc;
            interpreter.ExecutePlannedMoveLongPostincrementToData(opcode);
            interpreter.RecordPlannedFast(M68kOpcodePlanKind.MoveLongPostincrementToData);
            return true;
        }

        private static bool ExecuteMoveLongDataToPostincrementDelegatePlan(M68kInterpreterCore<TBus, TCpuDataAccess> interpreter, ushort opcode, uint instructionPc)
        {
            _ = instructionPc;
            interpreter.ExecutePlannedMoveLongDataToPostincrement(opcode);
            interpreter.RecordPlannedFast(M68kOpcodePlanKind.MoveLongDataToPostincrement);
            return true;
        }

        private static bool ExecuteImmediateDelegatePlan(M68kInterpreterCore<TBus, TCpuDataAccess> interpreter, ushort opcode, uint instructionPc)
        {
            _ = instructionPc;
            interpreter.ExecutePlannedImmediate(opcode);
            interpreter.RecordPlannedFast(M68kOpcodePlanKind.Immediate);
            return true;
        }

        private static bool ExecuteImmediateBtstDelegatePlan(M68kInterpreterCore<TBus, TCpuDataAccess> interpreter, ushort opcode, uint instructionPc)
        {
            _ = instructionPc;
            interpreter.ExecutePlannedImmediateBtst(opcode);
            interpreter.RecordPlannedFast(M68kOpcodePlanKind.ImmediateBtst);
            return true;
        }

        private static bool ExecuteRegisterArithmeticDelegatePlan(M68kInterpreterCore<TBus, TCpuDataAccess> interpreter, ushort opcode, uint instructionPc)
        {
            _ = instructionPc;
            interpreter.ExecutePlannedRegisterArithmetic(opcode);
            interpreter.RecordPlannedFast(M68kOpcodePlanKind.RegisterArithmetic);
            return true;
        }

        private static bool ExecuteOrLongToDataRegisterDelegatePlan(M68kInterpreterCore<TBus, TCpuDataAccess> interpreter, ushort opcode, uint instructionPc)
        {
            _ = instructionPc;
            interpreter.ExecutePlannedOrLongToDataRegister(opcode);
            interpreter.RecordPlannedFast(M68kOpcodePlanKind.DataRegisterLongOrToRegister);
            return true;
        }

        private static bool ExecuteEorLongToDataRegisterDelegatePlan(M68kInterpreterCore<TBus, TCpuDataAccess> interpreter, ushort opcode, uint instructionPc)
        {
            _ = instructionPc;
            interpreter.ExecutePlannedEorLongToDataRegister(opcode);
            interpreter.RecordPlannedFast(M68kOpcodePlanKind.DataRegisterLongEorToDestination);
            return true;
        }

        private static bool ExecuteAndLongToDataRegisterDelegatePlan(M68kInterpreterCore<TBus, TCpuDataAccess> interpreter, ushort opcode, uint instructionPc)
        {
            _ = instructionPc;
            interpreter.ExecutePlannedAndLongToDataRegister(opcode);
            interpreter.RecordPlannedFast(M68kOpcodePlanKind.DataRegisterLongAndToRegister);
            return true;
        }

        private static bool ExecuteAddLongToDataRegisterDelegatePlan(M68kInterpreterCore<TBus, TCpuDataAccess> interpreter, ushort opcode, uint instructionPc)
        {
            _ = instructionPc;
            interpreter.ExecutePlannedAddLongToDataRegister(opcode);
            interpreter.RecordPlannedFast(M68kOpcodePlanKind.DataRegisterLongAddToRegister);
            return true;
        }

        private void ExecutePlannedMoveq(ushort opcode)
        {
            var register = (opcode >> 9) & 7;
            State.D[register] = unchecked((uint)(int)(sbyte)(opcode & 0xFF));
            State.SetNegativeZero(State.D[register], M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            AddCycles(4);
        }

        private void ExecutePlannedBranch(ushort opcode, uint instructionPc)
        {
            var branchBase = State.ProgramCounter;
            var displacement = opcode & 0xFF;
            var extensionDisplacement = displacement == 0;
            var offset = extensionDisplacement
                ? unchecked((short)FetchWord())
                : unchecked((sbyte)displacement);
            var condition = (opcode >> 8) & 0x0F;
            if (condition == 0 || CheckCondition(condition))
            {
                var target = unchecked((uint)(branchBase + offset));
                if (_instructionFrequency.Enabled)
                {
                    _instructionFrequency.RecordTakenBranch(
                        instructionPc,
                        opcode,
                        target,
                        extensionDisplacement ? 4 : 2);
                }

                SetProgramCounterAndFlushPrefetch(target);
                AddCycles(10);
                return;
            }

            AddCycles(8);
        }

        private void ExecutePlannedDbcc(ushort opcode)
        {
            if ((opcode & 0xFFF8) == 0x51C8)
            {
                ExecutePlannedDbra(opcode);
                return;
            }

            var branchBase = State.ProgramCounter;
            var displacement = unchecked((short)FetchWord());
            var condition = (opcode >> 8) & 0x0F;
            if (!CheckCondition(condition))
            {
                var register = opcode & 7;
                var counter = (ushort)((State.D[register] & 0xFFFF) - 1);
                State.D[register] = (State.D[register] & 0xFFFF_0000) | counter;
                if (counter != 0xFFFF)
                {
                    var target = unchecked((uint)(branchBase + displacement));
                    if (_instructionFrequency.Enabled)
                    {
                        _instructionFrequency.RecordTakenBranch(State.LastInstructionProgramCounter, opcode, target, 4);
                    }

                    SetProgramCounterAndFlushPrefetch(target);
                    AddCycles(10);
                    return;
                }

                AddCycles(14);
                return;
            }

            AddCycles(12);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePlannedDbra(ushort opcode)
        {
            var branchBase = State.ProgramCounter;
            var displacement = unchecked((short)FetchWord());
            var register = opcode & 7;
            var counter = (ushort)((State.D[register] & 0xFFFF) - 1);
            State.D[register] = (State.D[register] & 0xFFFF_0000) | counter;
            if (counter != 0xFFFF)
            {
                var target = unchecked((uint)(branchBase + displacement));
                if (_instructionFrequency.Enabled)
                {
                    _instructionFrequency.RecordTakenBranch(State.LastInstructionProgramCounter, opcode, target, 4);
                }

                SetProgramCounterAndFlushPrefetch(target);
                AddCycles(10);
                return;
            }

            AddCycles(14);
        }

        private void ExecutePlannedQuickRegister(ushort opcode)
        {
            if ((opcode & 0xFFF8) == 0x5380)
            {
                ExecutePlannedSubqLongOneDataRegister(opcode);
                return;
            }

            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var size = DecodeQuickSize(opcode);
            var register = opcode & 7;
            var subtract = (opcode & 0x0100) != 0;
            if (((opcode >> 3) & 7) == 1)
            {
                SetAddressRegister(
                    register,
                    subtract
                        ? unchecked(State.A[register] - (uint)count)
                        : unchecked(State.A[register] + (uint)count));
                AddCycles(8);
                return;
            }

            var old = State.D[register] & M68kCpuState.Mask(size);
            var result = subtract
                ? Subtract(old, (uint)count, size, setExtend: true)
                : Add(old, (uint)count, size, setExtend: true);
            WriteDataRegister(register, result, size);
            AddCycles(size == M68kOperandSize.Long ? 8 : 4);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePlannedSubqLongOneDataRegister(ushort opcode)
        {
            var register = opcode & 7;
            State.D[register] = Subtract(State.D[register], 1, M68kOperandSize.Long, setExtend: true);
            AddCycles(8);
        }

        private void ExecutePlannedMove(ushort opcode)
        {
            if ((opcode & 0xF1FF) == 0x302E)
            {
                ExecutePlannedMoveWordDisplacementA6ToDataRegister(opcode);
                return;
            }

            var size = DecodePlannedMoveSize(opcode);
            var sourceMode = (opcode >> 3) & 7;
            var sourceRegister = opcode & 7;
            var destinationMode = (opcode >> 6) & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var source = ResolvePlannedEa(sourceMode, sourceRegister, size);
            var value = ReadPlannedEaValue(in source);
            var destination = ResolvePlannedEa(destinationMode, destinationRegister, size);
            WritePlannedEaValue(in destination, value);
            if (destinationMode != 1)
            {
                State.SetNegativeZero(value, size);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
            }

            AddCycles(EstimatePlannedMoveCycles(source.EaCycles, destination.IsRegister, size));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePlannedMoveWordDisplacementA6ToDataRegister(ushort opcode)
        {
            var displacement = unchecked((short)FetchWord());
            var address = unchecked(State.A[6] + (uint)displacement);
            var value = ReadWord(address);
            WriteDataRegister((opcode >> 9) & 7, value, M68kOperandSize.Word);
            State.SetNegativeZero(value, M68kOperandSize.Word);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            AddCycles(12);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePlannedMoveLongPostincrementToData(ushort opcode)
        {
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var sourceAddress = State.A[sourceRegister];
            SetAddressRegister(sourceRegister, unchecked(sourceAddress + 4));
            var value = ReadLong(sourceAddress);
            State.D[destinationRegister] = value;
            State.SetNegativeZero(value, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            AddCycles(12);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePlannedMoveLongDataToPostincrement(ushort opcode)
        {
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var destinationAddress = State.A[destinationRegister];
            SetAddressRegister(destinationRegister, unchecked(destinationAddress + 4));
            var value = State.D[sourceRegister];
            WriteLong(destinationAddress, value);
            State.SetNegativeZero(value, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            AddCycles(12);
        }

        private void ExecutePlannedImmediate(ushort opcode)
        {
            if ((opcode & 0xFFF8) == 0x0200)
            {
                ExecutePlannedAndiByteDataRegister(opcode);
                return;
            }

            var size = DecodeImmediateSize(opcode);
            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            var immediate = FetchImmediate(size);
            var destinationEa = ResolvePlannedEa(mode, register, size);
            var destination = ReadPlannedEaValue(in destinationEa);
            switch (opcode & 0xFF00)
            {
                case 0x0000:
                    destination |= immediate;
                    WritePlannedEaValue(in destinationEa, destination);
                    SetLogicFlags(destination, size);
                    break;
                case 0x0200:
                    destination &= immediate;
                    WritePlannedEaValue(in destinationEa, destination);
                    SetLogicFlags(destination, size);
                    break;
                case 0x0400:
                    destination = Subtract(destination, immediate, size, setExtend: true);
                    WritePlannedEaValue(in destinationEa, destination);
                    break;
                case 0x0600:
                    destination = Add(destination, immediate, size, setExtend: true);
                    WritePlannedEaValue(in destinationEa, destination);
                    break;
                case 0x0A00:
                    destination ^= immediate;
                    WritePlannedEaValue(in destinationEa, destination);
                    SetLogicFlags(destination, size);
                    break;
                default:
                    _ = Subtract(destination, immediate, size, setExtend: false, storeResult: false);
                    break;
            }

            AddCycles((opcode & 0xFF00) == 0x0C00
                ? GetCmpiCycles(size, mode, register)
                : size == M68kOperandSize.Long ? 16 : 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePlannedAndiByteDataRegister(ushort opcode)
        {
            var register = opcode & 7;
            var result = (State.D[register] & 0xFF) & FetchImmediate(M68kOperandSize.Byte);
            WriteDataRegister(register, result, M68kOperandSize.Byte);
            SetLogicFlags(result, M68kOperandSize.Byte);
            AddCycles(8);
        }

        private void ExecutePlannedImmediateBtst(ushort opcode)
        {
            if ((opcode & 0xFFF8) == 0x0810)
            {
                ExecutePlannedBtstImmediateAddressIndirect(opcode);
                return;
            }

            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            var size = mode == 0 ? M68kOperandSize.Long : M68kOperandSize.Byte;
            var bit = FetchWord() & 31;
            var bitEa = ResolvePlannedEa(mode, register, size);
            var value = ReadPlannedEaValue(in bitEa);
            var maskedBit = mode == 0 ? bit : bit & 7;
            State.SetFlag(M68kCpuState.Zero, (value & (1u << (int)maskedBit)) == 0);
            AddCycles(GetImmediateBtstCycles(mode, register));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePlannedBtstImmediateAddressIndirect(ushort opcode)
        {
            var register = opcode & 7;
            var bit = FetchWord() & 7;
            var value = ReadByte(State.A[register]);
            State.SetFlag(M68kCpuState.Zero, (value & (1u << (int)bit)) == 0);
            AddCycles(GetImmediateBtstCycles(2, register));
        }

        private void ExecutePlannedRegisterArithmetic(ushort opcode)
        {
            var line = opcode >> 12;
            var opmode = (opcode >> 6) & 7;
            var registerToEa = opmode >= 4;
            if (line == 0xB)
            {
                registerToEa = true;
            }

            var register = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var size = DecodePlannedRegisterArithmeticSize(opmode);
            var eaValue = State.D[sourceRegister] & M68kCpuState.Mask(size);
            var regValue = State.D[register] & M68kCpuState.Mask(size);
            uint result;
            switch (line)
            {
                case 0x8:
                    result = eaValue | regValue;
                    if (registerToEa)
                    {
                        WriteDataRegister(sourceRegister, result, size);
                    }
                    else
                    {
                        WriteDataRegister(register, result, size);
                    }

                    SetLogicFlags(result, size);
                    break;
                case 0x9:
                    result = Subtract(regValue, eaValue, size, setExtend: true);
                    WriteDataRegister(register, result, size);
                    break;
                case 0xB:
                    if (opmode >= 4)
                    {
                        result = eaValue ^ regValue;
                        WriteDataRegister(sourceRegister, result, size);
                        SetLogicFlags(result, size);
                    }
                    else
                    {
                        _ = Subtract(regValue, eaValue, size, setExtend: false, storeResult: false);
                    }

                    break;
                case 0xC:
                    result = eaValue & regValue;
                    if (registerToEa)
                    {
                        WriteDataRegister(sourceRegister, result, size);
                    }
                    else
                    {
                        WriteDataRegister(register, result, size);
                    }

                    SetLogicFlags(result, size);
                    break;
                default:
                    result = Add(regValue, eaValue, size, setExtend: true);
                    WriteDataRegister(register, result, size);
                    break;
            }

            AddCycles(size == M68kOperandSize.Long ? 12 : 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePlannedOrLongToDataRegister(ushort opcode)
        {
            var register = (opcode >> 9) & 7;
            var result = State.D[opcode & 7] | State.D[register];
            State.D[register] = result;
            SetLogicFlags(result, M68kOperandSize.Long);
            AddCycles(12);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePlannedEorLongToDataRegister(ushort opcode)
        {
            var sourceRegister = opcode & 7;
            var result = State.D[sourceRegister] ^ State.D[(opcode >> 9) & 7];
            State.D[sourceRegister] = result;
            SetLogicFlags(result, M68kOperandSize.Long);
            AddCycles(12);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePlannedAndLongToDataRegister(ushort opcode)
        {
            var register = (opcode >> 9) & 7;
            var result = State.D[opcode & 7] & State.D[register];
            State.D[register] = result;
            SetLogicFlags(result, M68kOperandSize.Long);
            AddCycles(12);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePlannedAddLongToDataRegister(ushort opcode)
        {
            var register = (opcode >> 9) & 7;
            State.D[register] = Add(
                State.D[register],
                State.D[opcode & 7],
                M68kOperandSize.Long,
                setExtend: true);
            AddCycles(12);
        }

        private void ExecutePackedMoveq(in M68kPackedOpcodePlan plan)
        {
            State.D[plan.Register] = unchecked((uint)(int)plan.Displacement);
            State.SetNegativeZero(State.D[plan.Register], M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            AddCycles(4);
        }

        private void ExecutePackedBranch(in M68kPackedOpcodePlan plan, uint instructionPc)
        {
            var branchBase = State.ProgramCounter;
            var offset = plan.ExtensionDisplacement
                ? unchecked((short)FetchWord())
                : plan.Displacement;
            if (plan.Condition == 0 || CheckCondition(plan.Condition))
            {
                var target = unchecked((uint)(branchBase + offset));
                if (_instructionFrequency.Enabled)
                {
                    _instructionFrequency.RecordTakenBranch(
                        instructionPc,
                        State.LastOpcode,
                        target,
                        plan.ExtensionDisplacement ? 4 : 2);
                }

                SetProgramCounterAndFlushPrefetch(target);
                AddCycles(10);
                return;
            }

            AddCycles(8);
        }

        private void ExecutePackedDbcc(ushort opcode, in M68kPackedOpcodePlan plan)
        {
            if ((opcode & 0xFFF8) == 0x51C8)
            {
                ExecutePlannedDbra(opcode);
                return;
            }

            var branchBase = State.ProgramCounter;
            var displacement = unchecked((short)FetchWord());
            if (!CheckCondition(plan.Condition))
            {
                var register = plan.Register;
                var counter = (ushort)((State.D[register] & 0xFFFF) - 1);
                State.D[register] = (State.D[register] & 0xFFFF_0000) | counter;
                if (counter != 0xFFFF)
                {
                    var target = unchecked((uint)(branchBase + displacement));
                    if (_instructionFrequency.Enabled)
                    {
                        _instructionFrequency.RecordTakenBranch(State.LastInstructionProgramCounter, State.LastOpcode, target, 4);
                    }

                    SetProgramCounterAndFlushPrefetch(target);
                    AddCycles(10);
                    return;
                }

                AddCycles(14);
                return;
            }

            AddCycles(12);
        }

        private void ExecutePackedQuickRegister(ushort opcode, in M68kPackedOpcodePlan plan)
        {
            if ((opcode & 0xFFF8) == 0x5380)
            {
                ExecutePlannedSubqLongOneDataRegister(opcode);
                return;
            }

            var count = plan.QuickValue;
            var register = plan.DestinationRegister;
            if (plan.DestinationMode == 1)
            {
                SetAddressRegister(
                    register,
                    plan.Variant != 0
                        ? unchecked(State.A[register] - (uint)count)
                        : unchecked(State.A[register] + (uint)count));
                AddCycles(8);
                return;
            }

            var old = State.D[register] & M68kCpuState.Mask(plan.Size);
            var result = plan.Variant != 0
                ? Subtract(old, count, plan.Size, setExtend: true)
                : Add(old, count, plan.Size, setExtend: true);
            WriteDataRegister(register, result, plan.Size);
            AddCycles(plan.Size == M68kOperandSize.Long ? 8 : 4);
        }

        private void ExecutePackedMove(ushort opcode, in M68kPackedOpcodePlan plan)
        {
            if ((opcode & 0xF1FF) == 0x302E)
            {
                ExecutePlannedMoveWordDisplacementA6ToDataRegister(opcode);
                return;
            }

            var source = ResolvePlannedEa(plan.SourceMode, plan.SourceRegister, plan.Size);
            var value = ReadPlannedEaValue(in source);
            var destination = ResolvePlannedEa(plan.DestinationMode, plan.DestinationRegister, plan.Size);
            WritePlannedEaValue(in destination, value);
            if (plan.DestinationMode != 1)
            {
                State.SetNegativeZero(value, plan.Size);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
            }

            AddCycles(EstimatePlannedMoveCycles(source.EaCycles, destination.IsRegister, plan.Size));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePackedMoveLongPostincrementToData(in M68kPackedOpcodePlan plan)
        {
            var sourceRegister = plan.SourceRegister;
            var sourceAddress = State.A[sourceRegister];
            SetAddressRegister(sourceRegister, unchecked(sourceAddress + 4));
            var value = ReadLong(sourceAddress);
            State.D[plan.DestinationRegister] = value;
            State.SetNegativeZero(value, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            AddCycles(12);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePackedMoveLongDataToPostincrement(in M68kPackedOpcodePlan plan)
        {
            var destinationRegister = plan.DestinationRegister;
            var destinationAddress = State.A[destinationRegister];
            SetAddressRegister(destinationRegister, unchecked(destinationAddress + 4));
            var value = State.D[plan.SourceRegister];
            WriteLong(destinationAddress, value);
            State.SetNegativeZero(value, M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            AddCycles(12);
        }

        private void ExecutePackedImmediate(ushort opcode, in M68kPackedOpcodePlan plan)
        {
            if ((opcode & 0xFFF8) == 0x0200)
            {
                ExecutePlannedAndiByteDataRegister(opcode);
                return;
            }

            var immediate = FetchImmediate(plan.Size);
            var destinationEa = ResolvePlannedEa(plan.DestinationMode, plan.DestinationRegister, plan.Size);
            var destination = ReadPlannedEaValue(in destinationEa);
            switch (plan.Variant)
            {
                case 0:
                    destination |= immediate;
                    WritePlannedEaValue(in destinationEa, destination);
                    SetLogicFlags(destination, plan.Size);
                    break;
                case 1:
                    destination &= immediate;
                    WritePlannedEaValue(in destinationEa, destination);
                    SetLogicFlags(destination, plan.Size);
                    break;
                case 2:
                    destination = Subtract(destination, immediate, plan.Size, setExtend: true);
                    WritePlannedEaValue(in destinationEa, destination);
                    break;
                case 3:
                    destination = Add(destination, immediate, plan.Size, setExtend: true);
                    WritePlannedEaValue(in destinationEa, destination);
                    break;
                case 4:
                    destination ^= immediate;
                    WritePlannedEaValue(in destinationEa, destination);
                    SetLogicFlags(destination, plan.Size);
                    break;
                default:
                    _ = Subtract(destination, immediate, plan.Size, setExtend: false, storeResult: false);
                    break;
            }

            AddCycles(plan.Variant == 5
                ? GetCmpiCycles(plan.Size, plan.DestinationMode, plan.DestinationRegister)
                : plan.Size == M68kOperandSize.Long ? 16 : 8);
        }

        private void ExecutePackedImmediateBtst(ushort opcode, in M68kPackedOpcodePlan plan)
        {
            if ((opcode & 0xFFF8) == 0x0810)
            {
                ExecutePlannedBtstImmediateAddressIndirect(opcode);
                return;
            }

            var bit = FetchWord() & 31;
            var bitEa = ResolvePlannedEa(plan.DestinationMode, plan.DestinationRegister, plan.Size);
            var value = ReadPlannedEaValue(in bitEa);
            var maskedBit = plan.DestinationMode == 0 ? bit : bit & 7;
            State.SetFlag(M68kCpuState.Zero, (value & (1u << (int)maskedBit)) == 0);
            AddCycles(GetImmediateBtstCycles(plan.DestinationMode, plan.DestinationRegister));
        }

        private void ExecutePackedRegisterArithmetic(in M68kPackedOpcodePlan plan)
        {
            var line = plan.Variant >> 4;
            var opmode = plan.Variant & 7;
            var registerToEa = opmode >= 4;
            if (line == 0xB)
            {
                registerToEa = true;
            }

            var eaValue = State.D[plan.SourceRegister] & M68kCpuState.Mask(plan.Size);
            var regValue = State.D[plan.Register] & M68kCpuState.Mask(plan.Size);
            uint result;
            switch (line)
            {
                case 0x8:
                    result = eaValue | regValue;
                    if (registerToEa)
                    {
                        WriteDataRegister(plan.SourceRegister, result, plan.Size);
                    }
                    else
                    {
                        WriteDataRegister(plan.Register, result, plan.Size);
                    }

                    SetLogicFlags(result, plan.Size);
                    break;
                case 0x9:
                    result = Subtract(regValue, eaValue, plan.Size, setExtend: true);
                    WriteDataRegister(plan.Register, result, plan.Size);
                    break;
                case 0xB:
                    if (opmode >= 4)
                    {
                        result = eaValue ^ regValue;
                        WriteDataRegister(plan.SourceRegister, result, plan.Size);
                        SetLogicFlags(result, plan.Size);
                    }
                    else
                    {
                        _ = Subtract(regValue, eaValue, plan.Size, setExtend: false, storeResult: false);
                    }

                    break;
                case 0xC:
                    result = eaValue & regValue;
                    if (registerToEa)
                    {
                        WriteDataRegister(plan.SourceRegister, result, plan.Size);
                    }
                    else
                    {
                        WriteDataRegister(plan.Register, result, plan.Size);
                    }

                    SetLogicFlags(result, plan.Size);
                    break;
                default:
                    result = Add(regValue, eaValue, plan.Size, setExtend: true);
                    WriteDataRegister(plan.Register, result, plan.Size);
                    break;
            }

            AddCycles(plan.Size == M68kOperandSize.Long ? 12 : 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePackedOrLongToDataRegister(in M68kPackedOpcodePlan plan)
        {
            var result = State.D[plan.SourceRegister] | State.D[plan.Register];
            State.D[plan.Register] = result;
            SetLogicFlags(result, M68kOperandSize.Long);
            AddCycles(12);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePackedEorLongToDataRegister(in M68kPackedOpcodePlan plan)
        {
            var result = State.D[plan.SourceRegister] ^ State.D[plan.Register];
            State.D[plan.SourceRegister] = result;
            SetLogicFlags(result, M68kOperandSize.Long);
            AddCycles(12);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePackedAndLongToDataRegister(in M68kPackedOpcodePlan plan)
        {
            var result = State.D[plan.SourceRegister] & State.D[plan.Register];
            State.D[plan.Register] = result;
            SetLogicFlags(result, M68kOperandSize.Long);
            AddCycles(12);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePackedAddLongToDataRegister(in M68kPackedOpcodePlan plan)
        {
            State.D[plan.Register] = Add(
                State.D[plan.Register],
                State.D[plan.SourceRegister],
                M68kOperandSize.Long,
                setExtend: true);
            AddCycles(12);
        }

        private static M68kOperandSize DecodeQuickSize(ushort opcode)
            => ((opcode >> 6) & 3) switch
            {
                0 => M68kOperandSize.Byte,
                1 => M68kOperandSize.Word,
                _ => M68kOperandSize.Long
            };

        private static M68kOperandSize DecodePlannedMoveSize(ushort opcode)
            => (opcode >> 12) switch
            {
                1 => M68kOperandSize.Byte,
                2 => M68kOperandSize.Long,
                _ => M68kOperandSize.Word
            };

        private static M68kOperandSize DecodePlannedRegisterArithmeticSize(int opmode)
            => opmode switch
            {
                0 or 4 => M68kOperandSize.Byte,
                1 or 5 => M68kOperandSize.Word,
                _ => M68kOperandSize.Long
            };

        private PlannedEaOperand ResolvePlannedEa(int mode, int register, M68kOperandSize size)
        {
            switch (mode)
            {
                case 0:
                    return PlannedEaOperand.DataRegister(register, size);
                case 1:
                    return PlannedEaOperand.AddressRegister(register, size);
                case 2:
                    return PlannedEaOperand.Memory(State.A[register], size, GetEaOperandCycles(mode, register, size));
                case 3:
                {
                    var address = State.A[register];
                    SetAddressRegister(register, State.A[register] + AddressIncrement(register, size));
                    return PlannedEaOperand.Memory(address, size, GetEaOperandCycles(mode, register, size));
                }
                case 4:
                    SetAddressRegister(register, State.A[register] - AddressIncrement(register, size));
                    return PlannedEaOperand.Memory(State.A[register], size, GetEaOperandCycles(mode, register, size));
                case 5:
                {
                    var displacement = unchecked((short)FetchWord());
                    return PlannedEaOperand.Memory(
                        unchecked((uint)(State.A[register] + displacement)),
                        size,
                        GetEaOperandCycles(mode, register, size));
                }
                case 6:
                {
                    var extension = FetchWord();
                    return PlannedEaOperand.Memory(
                        M68kIntegerSemantics.CalculateM68000BriefIndexedAddress(State.A[register], extension, State.D, State.A),
                        size,
                        GetEaOperandCycles(mode, register, size));
                }
                case 7:
                    return ResolvePlannedMode7(register, size);
                default:
                    throw new InvalidOperationException("Invalid planned effective address mode.");
            }
        }

        private PlannedEaOperand ResolvePlannedMode7(int register, M68kOperandSize size)
        {
            switch (register)
            {
                case 0:
                    return PlannedEaOperand.Memory(unchecked((uint)(short)FetchWord()), size, GetEaOperandCycles(7, register, size));
                case 1:
                    return PlannedEaOperand.Memory(FetchLong(), size, GetEaOperandCycles(7, register, size));
                case 2:
                {
                    var extensionAddress = State.ProgramCounter;
                    var displacement = unchecked((short)FetchWord());
                    return PlannedEaOperand.Memory(
                        unchecked((uint)(extensionAddress + displacement)),
                        size,
                        GetEaOperandCycles(7, register, size));
                }
                case 3:
                {
                    var extensionAddress = State.ProgramCounter;
                    var extension = FetchWord();
                    return PlannedEaOperand.Memory(
                        M68kIntegerSemantics.CalculateM68000BriefIndexedAddress(extensionAddress, extension, State.D, State.A),
                        size,
                        GetEaOperandCycles(7, register, size));
                }
                case 4:
                    return PlannedEaOperand.ImmediateValue(FetchImmediate(size), size);
                default:
                    return RaisePlannedIllegalInstruction();
            }
        }

        private PlannedEaOperand RaisePlannedIllegalInstruction()
        {
            RaiseException(4, State.LastInstructionProgramCounter, 34);
            throw M68kIllegalInstructionException.Instance;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint ReadPlannedEaValue(in PlannedEaOperand operand)
        {
            return operand.Kind switch
            {
                0 => State.D[operand.Register] & M68kCpuState.Mask(operand.Size),
                1 => operand.Size == M68kOperandSize.Word ? State.A[operand.Register] & 0xFFFF : State.A[operand.Register],
                2 => operand.Size switch
                {
                    M68kOperandSize.Byte => ReadByte(operand.Address),
                    M68kOperandSize.Word => ReadWord(operand.Address),
                    _ => ReadLong(operand.Address)
                },
                3 => operand.Immediate & M68kCpuState.Mask(operand.Size),
                _ => 0
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WritePlannedEaValue(in PlannedEaOperand operand, uint value)
        {
            value &= M68kCpuState.Mask(operand.Size);
            switch (operand.Kind)
            {
                case 0:
                    WriteDataRegister(operand.Register, value, operand.Size);
                    return;
                case 1:
                    SetAddressRegister(
                        operand.Register,
                        operand.Size == M68kOperandSize.Word
                            ? M68kCpuState.SignExtend(value, M68kOperandSize.Word)
                            : value);
                    return;
                case 2:
                    if (operand.Size == M68kOperandSize.Byte)
                    {
                        WriteByte(operand.Address, (byte)value);
                    }
                    else if (operand.Size == M68kOperandSize.Word)
                    {
                        WriteWord(operand.Address, (ushort)value);
                    }
                    else
                    {
                        WriteLong(operand.Address, value);
                    }

                    return;
                default:
                    throw new M68kEmulationException("Cannot write to an immediate MC68000 operand.");
            }
        }

        private static int EstimatePlannedMoveCycles(int sourceEaCycles, bool destinationIsRegister, M68kOperandSize size)
        {
            var baseCycles = size == M68kOperandSize.Long ? 12 : 8;
            if (destinationIsRegister)
            {
                baseCycles = Math.Max(baseCycles, 4 + sourceEaCycles);
            }

            return baseCycles;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RecordPlannedScalarFallback()
        {
            if (_plannedInterpreterCountersEnabled)
            {
                _plannedScalarFallbackInstructions++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RecordPlannedFast(M68kOpcodePlanKind kind)
        {
            if (!_plannedInterpreterCountersEnabled)
            {
                return;
            }

            _plannedFastInstructions++;
            switch (kind)
            {
                case M68kOpcodePlanKind.Nop:
                    _plannedNopInstructions++;
                    break;
                case M68kOpcodePlanKind.Moveq:
                    _plannedMoveqInstructions++;
                    break;
                case M68kOpcodePlanKind.Branch:
                    _plannedBranchInstructions++;
                    break;
                case M68kOpcodePlanKind.Dbcc:
                    _plannedDbccInstructions++;
                    break;
                case M68kOpcodePlanKind.QuickRegister:
                    _plannedQuickRegisterInstructions++;
                    break;
                case M68kOpcodePlanKind.Move:
                case M68kOpcodePlanKind.MoveLongPostincrementToData:
                case M68kOpcodePlanKind.MoveLongDataToPostincrement:
                    _plannedMoveInstructions++;
                    break;
                case M68kOpcodePlanKind.Immediate:
                    _plannedImmediateInstructions++;
                    break;
                case M68kOpcodePlanKind.ImmediateBtst:
                    _plannedImmediateBtstInstructions++;
                    break;
                case M68kOpcodePlanKind.RegisterArithmetic:
                case M68kOpcodePlanKind.DataRegisterLongOrToRegister:
                case M68kOpcodePlanKind.DataRegisterLongEorToDestination:
                case M68kOpcodePlanKind.DataRegisterLongAndToRegister:
                case M68kOpcodePlanKind.DataRegisterLongAddToRegister:
                    _plannedRegisterArithmeticInstructions++;
                    break;
            }
        }

        private bool DecodeByOpcodeLine(ushort opcode, uint instructionPc)
        {
            return (opcode >> 12) switch
            {
                0x0 => DecodeLine0(opcode, instructionPc),
                0x1 or 0x2 or 0x3 => DecodeMove(opcode),
                0x4 => DecodeLine4(opcode, instructionPc),
                0x5 => DecodeLine5(opcode),
                0x6 => DecodeBranch(opcode, instructionPc),
                0x7 => DecodeMoveq(opcode),
                0x8 or 0x9 or 0xB or 0xC or 0xD => DecodeArithmetic(opcode),
                0xE => DecodeShiftRotate(opcode),
                _ => false
            };
        }

        public void Reset(uint programCounter, uint stackPointer)
        {
            Array.Clear(State.D);
            Array.Clear(State.A);
            SetProgramCounterAndFlushPrefetch(programCounter);
            State.ResetStackPointers(stackPointer, 0, supervisorMode: true);
            State.StatusRegister = M68kCpuState.ResetStatusRegister;
            State.Cycles = 0;
            ResetPrefetchPipeline();
            State.Halted = false;
            State.Stopped = false;
            State.LastOpcode = 0;
            State.LastInstructionProgramCounter = 0;
            State.RecordException(-1, 0, 0);
        }

        public void BeginSubroutine(uint address, uint stackPointer, uint returnAddress)
        {
            State.SetActiveStackPointer(stackPointer);
            PushLong(returnAddress);
            SetProgramCounterAndFlushPrefetch(address);
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
            State.StatusRegister = (ushort)((State.StatusRegister & 0xF8FF) | ((level & 7) << 8) | M68kCpuState.Supervisor);
            PushLong(State.ProgramCounter);
            PushWord(savedStatusRegister);
            SetProgramCounterAndFlushPrefetch(ReadLong(vectorAddress));
            AddCycles(44);
        }

        private bool DecodeMove(ushort opcode)
        {
            var size = ((opcode >> 12) & 0x03) switch
            {
                1 => M68kOperandSize.Byte,
                2 => M68kOperandSize.Long,
                3 => M68kOperandSize.Word,
                _ => (M68kOperandSize)0
            };
            if (size == 0)
            {
                return false;
            }

            var src = ResolveEa((opcode >> 3) & 7, opcode & 7, size);
            var value = src.Read();
            var destMode = (opcode >> 6) & 7;
            var destReg = (opcode >> 9) & 7;
            var dest = ResolveEa(destMode, destReg, size, write: true);
            dest.Write(value);
            // MOVEA does not alter the condition codes.
            if (destMode != 1)
            {
                State.SetNegativeZero(value, size);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
            }

            AddCycles(EstimateEaCycles(src, dest, size, write: true));
            return true;
        }

        private bool DecodeMoveq(ushort opcode)
        {
            if ((opcode & 0xF100) != 0x7000)
            {
                return false;
            }

            var reg = (opcode >> 9) & 7;
            State.D[reg] = (uint)unchecked((int)(sbyte)(opcode & 0xFF));
            State.SetNegativeZero(State.D[reg], M68kOperandSize.Long);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            AddCycles(4);
            return true;
        }

        private bool DecodeBranch(ushort opcode, uint instructionPc)
        {
            if ((opcode & 0xF000) != 0x6000)
            {
                return false;
            }

            var condition = (opcode >> 8) & 0x0F;
            var displacement = opcode & 0xFF;
            var branchBase = State.ProgramCounter;
            int offset;
            if (displacement == 0)
            {
                offset = unchecked((short)FetchWord());
            }
            else
            {
                offset = unchecked((sbyte)displacement);
            }

            if (condition == 1)
            {
                var target = (uint)(branchBase + offset);
                PushLong(State.ProgramCounter);
                SetProgramCounterAndFlushPrefetch(target);
                AddCycles(displacement == 0 ? 18 : 18);
                return true;
            }

            if (condition == 2 || condition == 3)
            {
                var carryOrZero = (State.StatusRegister & (M68kCpuState.Carry | M68kCpuState.Zero)) != 0;
                if (condition == 2 ? !carryOrZero : carryOrZero)
                {
                    var target = (uint)(branchBase + offset);
                    if (_instructionFrequency.Enabled)
                    {
                        _instructionFrequency.RecordTakenBranch(instructionPc, opcode, target, displacement == 0 ? 4 : 2);
                    }

                    SetProgramCounterAndFlushPrefetch(target);
                    AddCycles(displacement == 0 ? 10 : 10);
                }
                else
                {
                    _ = instructionPc;
                    AddCycles(8);
                }

                return true;
            }

            if (condition == 6)
            {
                if ((State.StatusRegister & M68kCpuState.Zero) == 0)
                {
                    var target = (uint)(branchBase + offset);
                    if (_instructionFrequency.Enabled)
                    {
                        _instructionFrequency.RecordTakenBranch(instructionPc, opcode, target, displacement == 0 ? 4 : 2);
                    }

                    SetProgramCounterAndFlushPrefetch(target);
                    AddCycles(displacement == 0 ? 10 : 10);
                }
                else
                {
                    _ = instructionPc;
                    AddCycles(8);
                }

                return true;
            }

            if (condition == 7)
            {
                if ((State.StatusRegister & M68kCpuState.Zero) != 0)
                {
                    var target = (uint)(branchBase + offset);
                    if (_instructionFrequency.Enabled)
                    {
                        _instructionFrequency.RecordTakenBranch(instructionPc, opcode, target, displacement == 0 ? 4 : 2);
                    }

                    SetProgramCounterAndFlushPrefetch(target);
                    AddCycles(displacement == 0 ? 10 : 10);
                }
                else
                {
                    _ = instructionPc;
                    AddCycles(8);
                }

                return true;
            }

            if (CheckCondition(condition))
            {
                var target = (uint)(branchBase + offset);
                if (_instructionFrequency.Enabled)
                {
                    _instructionFrequency.RecordTakenBranch(instructionPc, opcode, target, displacement == 0 ? 4 : 2);
                }

                SetProgramCounterAndFlushPrefetch(target);
                AddCycles(displacement == 0 ? 10 : 10);
            }
            else
            {
                _ = instructionPc;
                AddCycles(8);
            }

            return true;
        }

        private bool DecodeLine0(ushort opcode, uint instructionPc)
        {
            if (DecodeImmediateToStatusRegister(opcode, instructionPc))
            {
                return true;
            }

            if ((opcode & 0xF138) == 0x0108)
            {
                ExecuteMovep(opcode);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x0800 && TryDecodeImmediateBtst(opcode))
            {
                return true;
            }

            if ((opcode & 0xFF00) is 0x0800 or 0x0840 or 0x0880 or 0x08C0)
            {
                var bit = FetchWord() & 31;
                var operation = (opcode >> 6) & 3;
                var bitMode = (opcode >> 3) & 7;
                var bitReg = opcode & 7;
                if (bitMode == 1 || (bitMode == 7 && bitReg == 4))
                {
                    return false;
                }

                var bitSize = bitMode == 0 ? M68kOperandSize.Long : M68kOperandSize.Byte;
                if (bitMode == 7 && bitReg == 1)
                {
                    var address = FetchLong();
                    var absoluteValue = (uint)ReadByte(address);
                    var absoluteMask = 1u << (int)(bit & 7);
                    State.SetFlag(M68kCpuState.Zero, (absoluteValue & absoluteMask) == 0);
                    if (operation != 0)
                    {
                        absoluteValue = operation switch
                        {
                            1 => absoluteValue ^ absoluteMask,
                            2 => absoluteValue & ~absoluteMask,
                            _ => absoluteValue | absoluteMask
                        };
                        WriteByte(address, (byte)absoluteValue);
                    }

                    AddCycles(14);
                    return true;
                }

                var bitEa = ResolveEa(bitMode, bitReg, bitSize, write: operation != 0);
                var value = bitEa.Read();
                var mask = 1u << (int)(bitMode == 0 ? bit : bit & 7);
                State.SetFlag(M68kCpuState.Zero, (value & mask) == 0);
                if (operation != 0)
                {
                    value = operation switch
                    {
                        1 => value ^ mask,
                        2 => value & ~mask,
                        _ => value | mask
                    };
                    bitEa.Write(value);
                }

                AddCycles(bitMode == 0 ? 10 : 14);
                return true;
            }

            if ((opcode & 0xF100) == 0x0100)
            {
                var bitRegister = (opcode >> 9) & 7;
                var operation = (opcode >> 6) & 3;
                var bitMode = (opcode >> 3) & 7;
                var bitReg = opcode & 7;
                if (bitMode == 1 || (bitMode == 7 && bitReg == 4))
                {
                    return false;
                }

                var bitSize = bitMode == 0 ? M68kOperandSize.Long : M68kOperandSize.Byte;
                var bitEa = ResolveEa(bitMode, bitReg, bitSize, write: operation != 0);
                var value = bitEa.Read();
                var bit = State.D[bitRegister] & (bitMode == 0 ? 31u : 7u);
                var mask = 1u << (int)bit;
                State.SetFlag(M68kCpuState.Zero, (value & mask) == 0);
                if (operation != 0)
                {
                    value = operation switch
                    {
                        1 => value ^ mask,
                        2 => value & ~mask,
                        _ => value | mask
                    };
                    bitEa.Write(value);
                }

                AddCycles(bitMode == 0 ? 8 : 14);
                return true;
            }

            if (opcode == 0x0C39)
            {
                var compareImmediate = FetchWord() & 0xFFu;
                var compareAddress = FetchLong();
                var compareDestination = ReadByte(compareAddress);
                SetCompareByteFlags(compareDestination, compareImmediate);
                AddCycles(GetCmpiCycles(M68kOperandSize.Byte, 7, 1));
                return true;
            }

            var high = opcode & 0xFF00;
            if (high != 0x0000 && high != 0x0200 && high != 0x0400 && high != 0x0600 && high != 0x0A00 && high != 0x0C00)
            {
                return false;
            }

            var size = DecodeImmediateSize(opcode);
            if (size == 0)
            {
                RaiseException(4, instructionPc, 34);
                return true;
            }

            var immediate = FetchImmediate(size);
            var mode = (opcode >> 3) & 7;
            var reg = opcode & 7;
            var ea = ResolveEa(mode, reg, size, write: high != 0x0C00);
            var destination = ea.Read();
            switch (high)
            {
                case 0x0000:
                    destination |= immediate;
                    ea.Write(destination);
                    SetLogicFlags(destination, size);
                    break;
                case 0x0200:
                    destination &= immediate;
                    ea.Write(destination);
                    SetLogicFlags(destination, size);
                    break;
                case 0x0400:
                    destination = Subtract(destination, immediate, size, setExtend: true);
                    ea.Write(destination);
                    break;
                case 0x0600:
                    destination = Add(destination, immediate, size, setExtend: true);
                    ea.Write(destination);
                    break;
                case 0x0A00:
                    destination ^= immediate;
                    ea.Write(destination);
                    SetLogicFlags(destination, size);
                    break;
                case 0x0C00:
                    _ = Subtract(destination, immediate, size, setExtend: false, storeResult: false);
                    break;
            }

            AddCycles(high == 0x0C00 ? GetCmpiCycles(size, mode, reg) : size == M68kOperandSize.Long ? 16 : 8);
            return true;
        }

        private void ExecuteMovep(ushort opcode)
        {
            var dataRegister = (opcode >> 9) & 7;
            var addressRegister = opcode & 7;
            var address = unchecked((uint)(State.A[addressRegister] + unchecked((int)(short)FetchWord())));
            var isLong = (opcode & 0x0040) != 0;
            var registerToMemory = (opcode & 0x0080) != 0;

            if (registerToMemory)
            {
                var value = State.D[dataRegister];
                if (isLong)
                {
                    WriteByte(address, (byte)(value >> 24));
                    WriteByte(unchecked(address + 2), (byte)(value >> 16));
                    WriteByte(unchecked(address + 4), (byte)(value >> 8));
                    WriteByte(unchecked(address + 6), (byte)value);
                }
                else
                {
                    WriteByte(address, (byte)(value >> 8));
                    WriteByte(unchecked(address + 2), (byte)value);
                }
            }
            else if (isLong)
            {
                State.D[dataRegister] =
                    ((uint)ReadByte(address) << 24) |
                    ((uint)ReadByte(unchecked(address + 2)) << 16) |
                    ((uint)ReadByte(unchecked(address + 4)) << 8) |
                    ReadByte(unchecked(address + 6));
            }
            else
            {
                var value = (ushort)((ReadByte(address) << 8) | ReadByte(unchecked(address + 2)));
                WriteDataRegister(dataRegister, value, M68kOperandSize.Word);
            }

            AddCycles(isLong ? 24 : 16);
        }

        private bool DecodeImmediateToStatusRegister(ushort opcode, uint instructionPc)
        {
            if (opcode is not (0x003C or 0x007C or 0x023C or 0x027C or 0x0A3C or 0x0A7C))
            {
                return false;
            }

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
                SetCcr((ushort)result);
                AddCycles(8);
                return true;
            }

            if (!State.GetFlag(M68kCpuState.Supervisor))
            {
                RaiseException(8, instructionPc, 34);
                return true;
            }

            State.StatusRegister = (ushort)result;
            AddCycles(20);
            return true;
        }

        private bool DecodeLine4(ushort opcode, uint instructionPc)
        {
            switch (opcode)
            {
                case 0x44FC:
                    SetCcr((ushort)(FetchWord() & 0x001F));
                    AddCycles(12);
                    return true;
                case 0x46FC:
                    State.StatusRegister = FetchWord();
                    AddCycles(12);
                    return true;
                case 0x4E70:
                    _bus.ResetExternalDevices(State.Cycles);
                    AddCycles(132);
                    return true;
                case 0x4E71:
                    AddCycles(4);
                    return true;
                case 0x4E72:
                    if (!State.GetFlag(M68kCpuState.Supervisor))
                    {
                        RaiseException(8, instructionPc, 34);
                        return true;
                    }

                    State.StatusRegister = FetchWord();
                    State.Stopped = true;
                    AddCycles(4);
                    return true;
                case 0x4E73:
                {
                    var statusRegister = PullWord();
                    var programCounter = PullLong();
                    State.StatusRegister = statusRegister;
                    SetProgramCounterAndFlushPrefetch(programCounter);
                    AddCycles(20);
                    return true;
                }
                case 0x4E75:
                {
                    var programCounter = PullLong();
                    SetProgramCounterAndFlushPrefetch(programCounter);
                    AddCycles(16);
                    return true;
                }
                case 0x4E76:
                    State.Halted = true;
                    AddCycles(4);
                    return true;
                case 0x4E77:
                    State.StatusRegister = PullWord();
                    AddCycles(12);
                    return true;
                case 0x4AFC:
                    RaiseException(4, instructionPc, 34);
                    return true;
            }

            if (opcode is 0x4E7A or 0x4E7B)
            {
                RaiseException(4, instructionPc, 34);
                return true;
            }

            if ((opcode & 0xFFF0) == 0x4E60)
            {
                if (!State.GetFlag(M68kCpuState.Supervisor))
                {
                    RaiseException(8, instructionPc, 34);
                    return true;
                }

                var reg = opcode & 7;
                if ((opcode & 0x0008) == 0)
                {
                    State.SetUserStackPointer(State.A[reg]);
                }
                else
                {
                    SetAddressRegister(reg, State.UserStackPointer);
                }

                AddCycles(4);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x40C0)
            {
                var ea = ResolveEa((opcode >> 3) & 7, opcode & 7, M68kOperandSize.Word, write: true);
                ea.Write(State.StatusRegister);
                AddCycles(12);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x44C0)
            {
                var ea = ResolveEa((opcode >> 3) & 7, opcode & 7, M68kOperandSize.Word);
                SetCcr((ushort)ea.Read());
                AddCycles(12);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x46C0)
            {
                if (!State.GetFlag(M68kCpuState.Supervisor))
                {
                    RaiseException(8, instructionPc, 34);
                    return true;
                }

                var ea = ResolveEa((opcode >> 3) & 7, opcode & 7, M68kOperandSize.Word);
                State.StatusRegister = (ushort)ea.Read();
                AddCycles(12);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4E50)
            {
                var reg = opcode & 7;
                PushLong(State.A[reg]);
                var displacement = unchecked((short)FetchWord());
                SetAddressRegister(reg, State.A[7]);
                State.SetActiveStackPointer((uint)(State.A[7] + displacement));
                AddCycles(16);
                return true;
            }

            if ((opcode & 0xFFF0) == 0x4E40)
            {
                var vector = (uint)(32 + (opcode & 0x0F));
                var savedStatusRegister = State.StatusRegister;
                State.StatusRegister |= M68kCpuState.Supervisor;
                PushLong(State.ProgramCounter);
                PushWord(savedStatusRegister);
                SetProgramCounterAndFlushPrefetch(ReadLong(vector * 4));
                AddCycles(34);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4E58)
            {
                var reg = opcode & 7;
                State.SetActiveStackPointer(State.A[reg]);
                SetAddressRegister(reg, PullLong());
                AddCycles(12);
                return true;
            }

            if ((opcode & 0xFB80) == 0x4880 && ((opcode >> 3) & 7) != 0)
            {
                DecodeMovem(opcode);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4840)
            {
                var reg = opcode & 7;
                var value = State.D[reg];
                State.D[reg] = (value << 16) | ((value >> 16) & 0xFFFF);
                State.SetNegativeZero(State.D[reg], M68kOperandSize.Long);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
                AddCycles(4);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x4840)
            {
                var ea = ResolveEa((opcode >> 3) & 7, opcode & 7, M68kOperandSize.Long, addressOnly: true);
                PushLong(ea.Address);
                AddCycles(12);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x4880)
            {
                var reg = opcode & 7;
                var value = M68kCpuState.SignExtend(State.D[reg] & 0xFF, M68kOperandSize.Byte) & 0xFFFF;
                State.D[reg] = (State.D[reg] & 0xFFFF_0000) | value;
                State.SetNegativeZero(value, M68kOperandSize.Word);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
                AddCycles(4);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x48C0)
            {
                var reg = opcode & 7;
                var value = M68kCpuState.SignExtend(State.D[reg] & 0xFFFF, M68kOperandSize.Word);
                State.D[reg] = value;
                State.SetNegativeZero(value, M68kOperandSize.Long);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
                AddCycles(4);
                return true;
            }

            if ((opcode & 0xF1C0) == 0x41C0)
            {
                var addressRegister = (opcode >> 9) & 7;
                var ea = ResolveEa((opcode >> 3) & 7, opcode & 7, M68kOperandSize.Long, addressOnly: true);
                SetAddressRegister(addressRegister, ea.Address);
                AddCycles(8);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4E90)
            {
                var target = State.A[opcode & 7];
                PushLong(State.ProgramCounter);
                SetProgramCounterAndFlushPrefetch(target);
                AddCycles(16);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x4E80)
            {
                var ea = ResolveEa((opcode >> 3) & 7, opcode & 7, M68kOperandSize.Long, addressOnly: true);
                PushLong(State.ProgramCounter);
                SetProgramCounterAndFlushPrefetch(ea.Address);
                AddCycles(18);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x4EC0)
            {
                var ea = ResolveEa((opcode >> 3) & 7, opcode & 7, M68kOperandSize.Long, addressOnly: true);
                SetProgramCounterAndFlushPrefetch(ea.Address);
                AddCycles(12);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x4800)
            {
                var mode = (opcode >> 3) & 7;
                var reg = opcode & 7;
                if (mode == 1 || (mode == 7 && reg >= 2))
                {
                    RaiseException(4, instructionPc, 34);
                    return true;
                }

                var ea = ResolveEa(mode, reg, M68kOperandSize.Byte, write: true);
                var extend = State.GetFlag(M68kCpuState.Extend) ? 1 : 0;
                var result = M68kIntegerSemantics.SubtractBcdByte(0, (byte)ea.Read(), extend, out var carry);
                ea.Write(result);
                SetBcdFlags(result, carry);
                AddCycles(ea.IsRegister ? 6 : 8);
                return true;
            }

            var unary = opcode & 0xFF00;
            if (unary is 0x4200 or 0x4400 or 0x4600 or 0x4A00)
            {
                var size = DecodeImmediateSize(opcode);
                if (size == 0)
                {
                    RaiseException(4, instructionPc, 34);
                    return true;
                }

                var ea = ResolveEa((opcode >> 3) & 7, opcode & 7, size, write: unary != 0x4A00);
                var value = ea.Read();
                switch (unary)
                {
                    case 0x4200:
                        value = 0;
                        ea.Write(value);
                        State.SetNegativeZero(0, size);
                        State.SetFlag(M68kCpuState.Overflow, false);
                        State.SetFlag(M68kCpuState.Carry, false);
                        break;
                    case 0x4400:
                        value = Subtract(0, value, size, setExtend: true);
                        ea.Write(value);
                        break;
                    case 0x4600:
                        value = (~value) & M68kCpuState.Mask(size);
                        ea.Write(value);
                        SetLogicFlags(value, size);
                        break;
                    case 0x4A00:
                        State.SetNegativeZero(value, size);
                        State.SetFlag(M68kCpuState.Overflow, false);
                        State.SetFlag(M68kCpuState.Carry, false);
                        break;
                }

                AddCycles(size == M68kOperandSize.Long ? 12 : 8);
                return true;
            }

            _ = instructionPc;
            return false;
        }

        private bool DecodeLine5(ushort opcode)
        {
            if ((opcode & 0xF0F8) == 0x50C8)
            {
                var condition = (opcode >> 8) & 0x0F;
                var reg = opcode & 7;
                var branchBase = State.ProgramCounter;
                var displacement = unchecked((short)FetchWord());
                if (!CheckCondition(condition))
                {
                    var counter = (ushort)((State.D[reg] & 0xFFFF) - 1);
                    State.D[reg] = (State.D[reg] & 0xFFFF_0000) | counter;
                    if (counter != 0xFFFF)
                    {
                        var target = (uint)(branchBase + displacement);
                        if (_instructionFrequency.Enabled)
                        {
                            _instructionFrequency.RecordTakenBranch(State.LastInstructionProgramCounter, opcode, target, 4);
                        }

                        SetProgramCounterAndFlushPrefetch(target);
                        AddCycles(10);
                    }
                    else
                    {
                        AddCycles(14);
                    }
                }
                else
                {
                    AddCycles(12);
                }

                return true;
            }

            if ((opcode & 0xF0C0) == 0x50C0)
            {
                var condition = (opcode >> 8) & 0x0F;
                var conditionEa = ResolveEa((opcode >> 3) & 7, opcode & 7, M68kOperandSize.Byte, write: true);
                conditionEa.Write(CheckCondition(condition) ? 0xFFu : 0u);
                AddCycles(8);
                return true;
            }

            if ((opcode & 0xF000) != 0x5000)
            {
                return false;
            }

            var sizeCode = (opcode >> 6) & 3;
            if (sizeCode == 3)
            {
                return false;
            }

            var size = sizeCode switch
            {
                0 => M68kOperandSize.Byte,
                1 => M68kOperandSize.Word,
                _ => M68kOperandSize.Long
            };
            var count = (opcode >> 9) & 7;
            if (count == 0)
            {
                count = 8;
            }

            var subtract = (opcode & 0x0100) != 0;
            var mode = (opcode >> 3) & 7;
            if (mode == 1)
            {
                var reg = opcode & 7;
                SetAddressRegister(
                    reg,
                    subtract
                        ? unchecked(State.A[reg] - (uint)count)
                        : unchecked(State.A[reg] + (uint)count));
                AddCycles(8);
                return true;
            }

            var ea = ResolveEa(mode, opcode & 7, size, write: true);
            var old = ea.Read();
            var result = subtract
                ? Subtract(old, (uint)count, size, setExtend: true)
                : Add(old, (uint)count, size, setExtend: true);
            ea.Write(result);
            AddCycles(size == M68kOperandSize.Long ? 8 : 4);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetCompareByteFlags(uint destination, uint source)
        {
            destination &= 0xFF;
            source &= 0xFF;
            var result = (destination - source) & 0xFF;
            State.SetNegativeZero(result, M68kOperandSize.Byte);
            State.SetFlag(
                M68kCpuState.Overflow,
                ((destination ^ source) & (destination ^ result) & 0x80) != 0);
            State.SetFlag(M68kCpuState.Carry, source > destination);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryDecodeImmediateBtst(ushort opcode)
        {
            var mode = (opcode >> 3) & 7;
            var reg = opcode & 7;
            if (!IsValidImmediateBtstEa(mode, reg))
            {
                return false;
            }

            var bit = FetchWord() & 31;
            if (mode == 7 && reg == 1)
            {
                var address = FetchLong();
                var absoluteMaskedBit = bit & 7;
                var absoluteValue = ReadByte(address);
                State.SetFlag(M68kCpuState.Zero, (absoluteValue & (1u << absoluteMaskedBit)) == 0);
                AddCycles(GetImmediateBtstCycles(mode, reg));
                return true;
            }

            var bitSize = mode == 0 ? M68kOperandSize.Long : M68kOperandSize.Byte;
            var bitEa = ResolveEa(mode, reg, bitSize);
            var value = bitEa.Read();
            var maskedBit = mode == 0 ? bit : bit & 7;
            State.SetFlag(M68kCpuState.Zero, (value & (1u << maskedBit)) == 0);
            AddCycles(GetImmediateBtstCycles(mode, reg));
            return true;
        }

        private static bool IsValidImmediateBtstEa(int mode, int reg)
        {
            return mode switch
            {
                0 => true,
                2 or 3 or 4 or 5 or 6 => true,
                7 => reg <= 3,
                _ => false
            };
        }

        private static int GetImmediateBtstCycles(int mode, int reg)
        {
            return mode == 0 ? 10 : 8 + GetByteWordEaOperandCycles(mode, reg);
        }

        private static int GetCmpiCycles(M68kOperandSize size, int mode, int reg)
        {
            if (mode == 0)
            {
                return size == M68kOperandSize.Long ? 16 : 8;
            }

            var eaCycles = GetEaOperandCycles(mode, reg, size);
            return (size == M68kOperandSize.Long ? 12 : 8) + eaCycles;
        }

        private static int GetEaOperandCycles(int mode, int reg, M68kOperandSize size)
        {
            return size == M68kOperandSize.Long
                ? GetLongEaOperandCycles(mode, reg)
                : GetByteWordEaOperandCycles(mode, reg);
        }

        private static int GetByteWordEaOperandCycles(int mode, int reg)
        {
            return mode switch
            {
                2 or 3 => 4,
                4 => 6,
                5 => 8,
                6 => 10,
                7 => reg switch
                {
                    0 or 2 => 8,
                    1 or 3 => 12,
                    _ => throw new InvalidOperationException("Invalid byte/word effective-address timing mode.")
                },
                _ => throw new InvalidOperationException("Invalid byte/word effective-address timing mode.")
            };
        }

        private static int GetLongEaOperandCycles(int mode, int reg)
        {
            return mode switch
            {
                2 or 3 => 8,
                4 => 10,
                5 => 12,
                6 => 14,
                7 => reg switch
                {
                    0 or 2 => 12,
                    1 or 3 => 16,
                    _ => throw new InvalidOperationException("Invalid long effective-address timing mode.")
                },
                _ => throw new InvalidOperationException("Invalid long effective-address timing mode.")
            };
        }

        private bool DecodeArithmetic(ushort opcode)
        {
            var line = opcode >> 12;
            if (line is not (0x8 or 0x9 or 0xB or 0xC or 0xD))
            {
                return false;
            }

            if ((line == 0x8 || line == 0xC) && DecodeBcdArithmetic(opcode))
            {
                return true;
            }

            var reg = (opcode >> 9) & 7;
            var opmode = (opcode >> 6) & 7;
            var mode = (opcode >> 3) & 7;
            var eaReg = opcode & 7;

            if (line == 0xC && DecodeExchange(opcode))
            {
                return true;
            }

            if ((line == 0x9 || line == 0xD) && opmode is 4 or 5 or 6 && mode is 0 or 1)
            {
                DecodeAddSubX(line == 0xD, opmode, mode, reg, eaReg);
                return true;
            }

            if (line == 0xB && DecodeCmpm(opcode))
            {
                return true;
            }

            if (line == 0xC && opmode == 3)
            {
                var sourceEa = ResolveEa(mode, eaReg, M68kOperandSize.Word);
                var source = sourceEa.Read();
                State.D[reg] = (uint)((ushort)State.D[reg] * (ushort)source);
                State.SetNegativeZero(State.D[reg], M68kOperandSize.Long);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
                AddCycles(GetMultiplyCycles(sourceEa.EaCycles, source, signed: false));
                return true;
            }

            if (line == 0xC && opmode == 7)
            {
                var sourceEa = ResolveEa(mode, eaReg, M68kOperandSize.Word);
                var source = unchecked((short)sourceEa.Read());
                State.D[reg] = (uint)(unchecked((short)State.D[reg]) * source);
                State.SetNegativeZero(State.D[reg], M68kOperandSize.Long);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
                AddCycles(GetMultiplyCycles(sourceEa.EaCycles, (ushort)source, signed: true));
                return true;
            }

            if (line == 0x8 && (opmode == 3 || opmode == 7))
            {
                var divisor = ResolveEa(mode, eaReg, M68kOperandSize.Word).Read() & 0xFFFF;
                if (divisor == 0)
                {
                    RaiseException(5, State.ProgramCounter, 38);
                    return true;
                }

                var dividend = State.D[reg];
                uint quotient;
                uint remainder;
                if (opmode == 3)
                {
                    quotient = dividend / divisor;
                    remainder = dividend % divisor;
                    if ((quotient & 0xFFFF_0000) != 0)
                    {
                        State.SetFlag(M68kCpuState.Overflow, true);
                    }
                    else
                    {
                        State.D[reg] = ((remainder & 0xFFFF) << 16) | (quotient & 0xFFFF);
                        State.SetNegativeZero(quotient, M68kOperandSize.Word);
                        State.SetFlag(M68kCpuState.Overflow, false);
                        State.SetFlag(M68kCpuState.Carry, false);
                    }
                }
                else
                {
                    var signedDivisor = unchecked((short)divisor);
                    var signedDividend = unchecked((int)dividend);
                    var signedQuotient = signedDividend / signedDivisor;
                    var signedRemainder = signedDividend % signedDivisor;
                    if (signedQuotient < short.MinValue || signedQuotient > short.MaxValue)
                    {
                        State.SetFlag(M68kCpuState.Overflow, true);
                    }
                    else
                    {
                        quotient = unchecked((uint)signedQuotient);
                        remainder = unchecked((uint)signedRemainder);
                        State.D[reg] = ((remainder & 0xFFFF) << 16) | (quotient & 0xFFFF);
                        State.SetNegativeZero(quotient, M68kOperandSize.Word);
                        State.SetFlag(M68kCpuState.Overflow, false);
                        State.SetFlag(M68kCpuState.Carry, false);
                    }
                }

                AddCycles(140);
                return true;
            }

            if ((line == 0x9 || line == 0xD || line == 0xB) && (opmode == 3 || opmode == 7))
            {
                var size = opmode == 3 ? M68kOperandSize.Word : M68kOperandSize.Long;
                var ea = ResolveEa(mode, eaReg, size);
                var value = ea.Read();
                if (line == 0xB)
                {
                    var compareValue = size == M68kOperandSize.Word
                        ? M68kCpuState.SignExtend(value, M68kOperandSize.Word)
                        : value;
                    _ = Subtract(State.A[reg], compareValue, M68kOperandSize.Long, setExtend: false, storeResult: false);
                }
                else if (line == 0x9)
                {
                    SetAddressRegister(reg, State.A[reg] - M68kCpuState.SignExtend(value, size));
                }
                else
                {
                    SetAddressRegister(reg, State.A[reg] + M68kCpuState.SignExtend(value, size));
                }

                AddCycles(size == M68kOperandSize.Long ? 8 : 6);
                return true;
            }

            var operandSize = opmode switch
            {
                0 or 4 => M68kOperandSize.Byte,
                1 or 5 => M68kOperandSize.Word,
                2 or 6 => M68kOperandSize.Long,
                _ => (M68kOperandSize)0
            };
            if (operandSize == 0)
            {
                return false;
            }

            var registerToEa = opmode >= 4;
            if (line == 0xB)
            {
                registerToEa = true;
            }

            var eaOperand = ResolveEa(mode, eaReg, operandSize, write: registerToEa && line != 0xB);
            var eaValue = eaOperand.Read();
            var regValue = State.D[reg] & M68kCpuState.Mask(operandSize);
            uint result;
            switch (line)
            {
                case 0x8:
                    result = registerToEa ? eaValue | regValue : regValue | eaValue;
                    if (registerToEa)
                    {
                        eaOperand.Write(result);
                    }
                    else
                    {
                        WriteDataRegister(reg, result, operandSize);
                    }

                    SetLogicFlags(result, operandSize);
                    break;
                case 0x9:
                    if (registerToEa)
                    {
                        result = Subtract(eaValue, regValue, operandSize, setExtend: true);
                        eaOperand.Write(result);
                    }
                    else
                    {
                        result = Subtract(regValue, eaValue, operandSize, setExtend: true);
                        WriteDataRegister(reg, result, operandSize);
                    }

                    break;
                case 0xB:
                    if (opmode >= 4)
                    {
                        result = eaValue ^ regValue;
                        eaOperand.Write(result);
                        SetLogicFlags(result, operandSize);
                    }
                    else
                    {
                        _ = Subtract(regValue, eaValue, operandSize, setExtend: false, storeResult: false);
                    }

                    break;
                case 0xC:
                    result = registerToEa ? eaValue & regValue : regValue & eaValue;
                    if (registerToEa)
                    {
                        eaOperand.Write(result);
                    }
                    else
                    {
                        WriteDataRegister(reg, result, operandSize);
                    }

                    SetLogicFlags(result, operandSize);
                    break;
                default:
                    if (registerToEa)
                    {
                        result = Add(eaValue, regValue, operandSize, setExtend: true);
                        eaOperand.Write(result);
                    }
                    else
                    {
                        result = Add(regValue, eaValue, operandSize, setExtend: true);
                        WriteDataRegister(reg, result, operandSize);
                    }

                    break;
            }

            AddCycles(operandSize == M68kOperandSize.Long ? 12 : 8);
            return true;
        }

        private bool DecodeBcdArithmetic(ushort opcode)
        {
            if ((opcode & 0xF1F0) is not (0x8100 or 0xC100))
            {
                return false;
            }

            var subtract = (opcode & 0xF000) == 0x8000;
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var memoryMode = (opcode & 0x0008) != 0;
            byte source;
            byte destination;
            uint destinationAddress = 0;

            if (memoryMode)
            {
                SetAddressRegister(sourceRegister, State.A[sourceRegister] - AddressIncrement(sourceRegister, M68kOperandSize.Byte));
                SetAddressRegister(destinationRegister, State.A[destinationRegister] - AddressIncrement(destinationRegister, M68kOperandSize.Byte));
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
                ? M68kIntegerSemantics.SubtractBcdByte(destination, source, extend, out var carry)
                : M68kIntegerSemantics.AddBcdByte(destination, source, extend, out carry);

            if (memoryMode)
            {
                WriteByte(destinationAddress, result);
            }
            else
            {
                WriteDataRegister(destinationRegister, result, M68kOperandSize.Byte);
            }

            SetBcdFlags(result, carry);
            AddCycles(memoryMode ? 18 : 6);
            return true;
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

        private bool DecodeCmpm(ushort opcode)
        {
            if ((opcode & 0xF138) != 0xB108)
            {
                return false;
            }

            var size = ((opcode >> 6) & 3) switch
            {
                0 => M68kOperandSize.Byte,
                1 => M68kOperandSize.Word,
                2 => M68kOperandSize.Long,
                _ => (M68kOperandSize)0
            };
            if (size == 0)
            {
                return false;
            }

            var destinationRegister = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var sourceAddress = State.A[sourceRegister];
            var source = size switch
            {
                M68kOperandSize.Byte => ReadByte(sourceAddress),
                M68kOperandSize.Word => ReadWord(sourceAddress),
                _ => ReadLong(sourceAddress)
            };
            SetAddressRegister(sourceRegister, sourceAddress + AddressIncrement(sourceRegister, size));

            var destinationAddress = State.A[destinationRegister];
            var destination = size switch
            {
                M68kOperandSize.Byte => ReadByte(destinationAddress),
                M68kOperandSize.Word => ReadWord(destinationAddress),
                _ => ReadLong(destinationAddress)
            };
            SetAddressRegister(destinationRegister, destinationAddress + AddressIncrement(destinationRegister, size));

            _ = Subtract(destination, source, size, setExtend: false, storeResult: false);
            AddCycles(size == M68kOperandSize.Long ? 20 : 12);
            return true;
        }

        private bool DecodeExchange(ushort opcode)
        {
            var left = (opcode >> 9) & 7;
            var right = opcode & 7;
            if ((opcode & 0xF1F8) == 0xC140)
            {
                (State.D[left], State.D[right]) = (State.D[right], State.D[left]);
                AddCycles(6);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xC148)
            {
                var value = State.A[left];
                SetAddressRegister(left, State.A[right]);
                SetAddressRegister(right, value);
                AddCycles(6);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xC188)
            {
                var value = State.D[left];
                State.D[left] = State.A[right];
                SetAddressRegister(right, value);
                AddCycles(6);
                return true;
            }

            return false;
        }

        private void DecodeAddSubX(bool add, int opmode, int mode, int destinationRegister, int sourceRegister)
        {
            var size = opmode switch
            {
                4 => M68kOperandSize.Byte,
                5 => M68kOperandSize.Word,
                _ => M68kOperandSize.Long
            };
            uint source;
            uint destination;
            uint destinationAddress = 0;
            if (mode == 0)
            {
                source = State.D[sourceRegister];
                destination = State.D[destinationRegister];
            }
            else
            {
                var increment = AddressIncrement(sourceRegister, size);
                SetAddressRegister(sourceRegister, State.A[sourceRegister] - increment);
                SetAddressRegister(destinationRegister, State.A[destinationRegister] - AddressIncrement(destinationRegister, size));
                source = size switch
                {
                    M68kOperandSize.Byte => ReadByte(State.A[sourceRegister]),
                    M68kOperandSize.Word => ReadWord(State.A[sourceRegister]),
                    _ => ReadLong(State.A[sourceRegister])
                };
                destinationAddress = State.A[destinationRegister];
                destination = size switch
                {
                    M68kOperandSize.Byte => ReadByte(destinationAddress),
                    M68kOperandSize.Word => ReadWord(destinationAddress),
                    _ => ReadLong(destinationAddress)
                };
            }

            var result = add
                ? AddWithExtend(destination, source, size)
                : SubtractWithExtend(destination, source, size);
            if (mode == 0)
            {
                WriteDataRegister(destinationRegister, result, size);
            }
            else if (size == M68kOperandSize.Byte)
            {
                WriteByte(destinationAddress, (byte)result);
            }
            else if (size == M68kOperandSize.Word)
            {
                WriteWord(destinationAddress, (ushort)result);
            }
            else
            {
                WriteLong(destinationAddress, result);
            }

            AddCycles(mode == 0 ? 4 : size == M68kOperandSize.Long ? 30 : 18);
        }

        private bool DecodeShiftRotate(ushort opcode)
        {
            if ((opcode & 0xF000) != 0xE000)
            {
                return false;
            }

            if ((opcode & 0x00C0) == 0x00C0)
            {
                var ea = ResolveEa((opcode >> 3) & 7, opcode & 7, M68kOperandSize.Word, write: true);
                var value = ea.Read() & 0xFFFF;
                var type = (opcode >> 9) & 3;
                var left = (opcode & 0x0100) != 0;
                var result = Shift(value, 1, M68kOperandSize.Word, type, left);
                ea.Write(result);
                AddCycles(8);
                return true;
            }

            var reg = opcode & 7;
            var size = ((opcode >> 6) & 3) switch
            {
                0 => M68kOperandSize.Byte,
                1 => M68kOperandSize.Word,
                2 => M68kOperandSize.Long,
                _ => (M68kOperandSize)0
            };
            if (size == 0)
            {
                return false;
            }

            var count = (opcode >> 9) & 7;
            if ((opcode & 0x0020) != 0)
            {
                count = (int)(State.D[count] & 63);
            }
            else if (count == 0)
            {
                count = 8;
            }

            var typeRegister = (opcode >> 3) & 3;
            var leftRegister = (opcode & 0x0100) != 0;
            var valueRegister = State.D[reg] & M68kCpuState.Mask(size);
            var shifted = Shift(valueRegister, count, size, typeRegister, leftRegister);
            WriteDataRegister(reg, shifted, size);
            AddCycles(6 + (count * 2));
            return true;
        }

        private void DecodeMovem(ushort opcode)
        {
            var size = (opcode & 0x0040) == 0 ? M68kOperandSize.Word : M68kOperandSize.Long;
            var registerMask = FetchWord();
            var directionMemoryToRegisters = (opcode & 0x0400) != 0;
            var mode = (opcode >> 3) & 7;
            var reg = opcode & 7;

            if (!directionMemoryToRegisters && mode == 4)
            {
                var address = State.A[reg];
                for (var bit = 0; bit < 16; bit++)
                {
                    if ((registerMask & (1 << bit)) == 0)
                    {
                        continue;
                    }

                    var register = 15 - bit;
                    address -= (uint)size;
                    var value = register < 8 ? State.D[register] : State.A[register - 8];
                    if (size == M68kOperandSize.Word)
                    {
                        WriteWord(address, (ushort)value);
                    }
                    else
                    {
                        WriteLong(address, value);
                    }
                }

                SetAddressRegister(reg, address);
                AddCycles(8 + CountBits(registerMask) * (size == M68kOperandSize.Long ? 8 : 4));
                return;
            }

            var ea = ResolveEa(mode, reg, size, write: !directionMemoryToRegisters, addressOnly: true);
            var current = ea.Address;
            for (var register = 0; register < 16; register++)
            {
                if ((registerMask & (1 << register)) == 0)
                {
                    continue;
                }

                if (directionMemoryToRegisters)
                {
                    var value = size == M68kOperandSize.Word
                        ? M68kCpuState.SignExtend(ReadWord(current), M68kOperandSize.Word)
                        : ReadLong(current);
                    if (register < 8)
                    {
                        State.D[register] = value;
                    }
                    else
                    {
                        SetAddressRegister(register - 8, value);
                    }
                }
                else
                {
                    var value = register < 8 ? State.D[register] : State.A[register - 8];
                    if (size == M68kOperandSize.Word)
                    {
                        WriteWord(current, (ushort)value);
                    }
                    else
                    {
                        WriteLong(current, value);
                    }
                }

                current += (uint)size;
            }

            if (directionMemoryToRegisters && mode == 3)
            {
                SetAddressRegister(reg, current);
            }

            AddCycles(8 + CountBits(registerMask) * (size == M68kOperandSize.Long ? 8 : 4));
        }

        private EaOperand ResolveEa(int mode, int reg, M68kOperandSize size, bool write = false, bool addressOnly = false)
        {
            switch (mode)
            {
                case 0:
                    return EaOperand.DataRegister(this, reg, size);
                case 1:
                    return EaOperand.AddressRegister(this, reg, size);
                case 2:
                    return EaOperand.Memory(this, State.A[reg], size, GetEaOperandCycles(mode, reg, size));
                case 3:
                {
                    var address = State.A[reg];
                    if (!addressOnly)
                    {
                        SetAddressRegister(reg, State.A[reg] + AddressIncrement(reg, size));
                    }

                    return EaOperand.Memory(this, address, size, GetEaOperandCycles(mode, reg, size));
                }
                case 4:
                {
                    SetAddressRegister(reg, State.A[reg] - AddressIncrement(reg, size));
                    return EaOperand.Memory(this, State.A[reg], size, GetEaOperandCycles(mode, reg, size));
                }
                case 5:
                {
                    var displacement = unchecked((short)FetchWord());
                    return EaOperand.Memory(this, (uint)(State.A[reg] + displacement), size, GetEaOperandCycles(mode, reg, size));
                }
                case 6:
                {
                    var extension = FetchWord();
                    return EaOperand.Memory(
                        this,
                        M68kIntegerSemantics.CalculateM68000BriefIndexedAddress(State.A[reg], extension, State.D, State.A),
                        size,
                        GetEaOperandCycles(mode, reg, size));
                }
                case 7:
                    return ResolveMode7(reg, size);
                default:
                    throw new InvalidOperationException("Invalid effective address mode.");
            }
        }

        private EaOperand ResolveMode7(int reg, M68kOperandSize size)
        {
            return reg switch
            {
                0 => EaOperand.Memory(this, (uint)(short)FetchWord(), size, GetEaOperandCycles(7, reg, size)),
                1 => EaOperand.Memory(this, FetchLong(), size, GetEaOperandCycles(7, reg, size)),
                2 => ResolvePcRelative(size),
                3 => ResolvePcIndexed(size),
                4 => EaOperand.Immediate(this, FetchImmediate(size), size),
                _ => RaiseIllegalInstruction()
            };
        }

        private EaOperand RaiseIllegalInstruction()
        {
            RaiseException(4, State.LastInstructionProgramCounter, 34);
            throw M68kIllegalInstructionException.Instance;
        }

        private EaOperand ResolvePcRelative(M68kOperandSize size)
        {
            var extensionAddress = State.ProgramCounter;
            var displacement = unchecked((short)FetchWord());
            return EaOperand.Memory(this, (uint)(extensionAddress + displacement), size, GetEaOperandCycles(7, 2, size));
        }

        private EaOperand ResolvePcIndexed(M68kOperandSize size)
        {
            var extensionAddress = State.ProgramCounter;
            var extension = FetchWord();
            return EaOperand.Memory(
                this,
                M68kIntegerSemantics.CalculateM68000BriefIndexedAddress(extensionAddress, extension, State.D, State.A),
                size,
                GetEaOperandCycles(7, 3, size));
        }

        private uint ReadEaValue(EaOperand operand)
        {
            return operand.Read();
        }

        private void WriteDataRegister(int reg, uint value, M68kOperandSize size)
        {
            var mask = M68kCpuState.Mask(size);
            State.D[reg] = size == M68kOperandSize.Long
                ? value
                : (State.D[reg] & ~mask) | (value & mask);
        }

        private void SetAddressRegister(int reg, uint value)
        {
            if (reg == 7)
            {
                State.SetActiveStackPointer(value);
                return;
            }

            State.A[reg] = value;
        }

        private uint Add(uint destination, uint source, M68kOperandSize size, bool setExtend)
        {
            var arithmetic = M68kIntegerSemantics.Add(destination, source, size);
            State.SetNegativeZero(arithmetic.Value, size);
            State.SetFlag(M68kCpuState.Overflow, arithmetic.Overflow);
            State.SetFlag(M68kCpuState.Carry, arithmetic.Carry);
            if (setExtend)
            {
                State.SetFlag(M68kCpuState.Extend, arithmetic.Carry);
            }

            return arithmetic.Value;
        }

        private uint AddWithExtend(uint destination, uint source, M68kOperandSize size)
        {
            var extend = State.GetFlag(M68kCpuState.Extend) ? 1u : 0u;
            var arithmetic = M68kIntegerSemantics.Add(destination, source, size, extend);
            if (arithmetic.Value != 0)
            {
                State.SetFlag(M68kCpuState.Zero, false);
            }

            State.SetFlag(M68kCpuState.Negative, (arithmetic.Value & M68kCpuState.SignBit(size)) != 0);
            State.SetFlag(M68kCpuState.Overflow, arithmetic.Overflow);
            State.SetFlag(M68kCpuState.Carry, arithmetic.Carry);
            State.SetFlag(M68kCpuState.Extend, arithmetic.Carry);
            return arithmetic.Value;
        }

        private uint Subtract(uint destination, uint source, M68kOperandSize size, bool setExtend, bool storeResult = true)
        {
            var arithmetic = M68kIntegerSemantics.Subtract(destination, source, size);
            State.SetNegativeZero(arithmetic.Value, size);
            State.SetFlag(M68kCpuState.Overflow, arithmetic.Overflow);
            State.SetFlag(M68kCpuState.Carry, arithmetic.Carry);
            if (setExtend)
            {
                State.SetFlag(M68kCpuState.Extend, arithmetic.Carry);
            }

            _ = storeResult;
            return arithmetic.Value;
        }

        private uint SubtractWithExtend(uint destination, uint source, M68kOperandSize size)
        {
            var extend = State.GetFlag(M68kCpuState.Extend) ? 1u : 0u;
            var arithmetic = M68kIntegerSemantics.Subtract(destination, source, size, extend);
            if (arithmetic.Value != 0)
            {
                State.SetFlag(M68kCpuState.Zero, false);
            }

            State.SetFlag(M68kCpuState.Negative, (arithmetic.Value & M68kCpuState.SignBit(size)) != 0);
            State.SetFlag(M68kCpuState.Overflow, arithmetic.Overflow);
            State.SetFlag(M68kCpuState.Carry, arithmetic.Carry);
            State.SetFlag(M68kCpuState.Extend, arithmetic.Carry);
            return arithmetic.Value;
        }

        private uint Shift(uint value, int count, M68kOperandSize size, int type, bool left)
        {
            var shifted = M68kIntegerSemantics.Shift(value, count, size, type, left, State.GetFlag(M68kCpuState.Extend));
            State.SetNegativeZero(shifted.Value, size);
            State.SetFlag(M68kCpuState.Carry, shifted.Carry);
            if (shifted.ExtendChanged)
            {
                State.SetFlag(M68kCpuState.Extend, shifted.Extend);
            }

            State.SetFlag(M68kCpuState.Overflow, false);
            return shifted.Value;
        }

        private void SetCcr(ushort value)
        {
            State.StatusRegister = (ushort)((State.StatusRegister & 0xFFE0) | (value & 0x001F));
        }

        private void SetLogicFlags(uint value, M68kOperandSize size)
        {
            State.SetNegativeZero(value, size);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CheckCondition(int condition)
            => M68kIntegerSemantics.EvaluateCondition(State.StatusRegister, condition);

        private static M68kOperandSize DecodeImmediateSize(ushort opcode)
        {
            return ((opcode >> 6) & 3) switch
            {
                0 => M68kOperandSize.Byte,
                1 => M68kOperandSize.Word,
                2 => M68kOperandSize.Long,
                _ => (M68kOperandSize)0
            };
        }

        private uint FetchImmediate(M68kOperandSize size)
        {
            return size switch
            {
                M68kOperandSize.Byte => (uint)(FetchWord() & 0xFF),
                M68kOperandSize.Word => FetchWord(),
                _ => FetchLong()
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ushort FetchWord()
        {
            var address = State.ProgramCounter;
            if (!_prefetchValid || _prefetchAddress != address)
            {
                RefillPrefetchSlot(address);
            }

            var value = _prefetchWord;
            var completedCycle = _prefetchCompletedCycle;
            State.ProgramCounter = unchecked(address + 2);
            if (State.Cycles < completedCycle)
            {
                State.Cycles = completedCycle;
            }

            if (_cpuRetireBusCycle < completedCycle)
            {
                _cpuRetireBusCycle = completedCycle;
            }

            _prefetchValid = false;
            FillPrefetchSlot(unchecked(address + 2));
            return value;
        }

        private uint FetchLong()
        {
            var high = FetchWord();
            var low = FetchWord();
            return ((uint)high << 16) | low;
        }

        private void FlushPrefetch()
        {
            _prefetchValid = false;
        }

        private void SetProgramCounterAndFlushPrefetch(uint target)
        {
            State.ProgramCounter = target;
            FlushPrefetch();
        }

        private void ResetPrefetchPipeline()
        {
            FlushPrefetch();
            _cpuBusCycle = State.Cycles;
            _cpuRetireBusCycle = State.Cycles;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RefillPrefetchSlot(uint address)
        {
            FlushPrefetch();
            FillPrefetchSlot(address);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FillPrefetchSlot(uint address)
        {
            var value = ReadPrefetchWord(address, out var completedCycle);
            _prefetchAddress = address;
            _prefetchWord = value;
            _prefetchCompletedCycle = completedCycle;
            _prefetchValid = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ushort ReadPrefetchWord(uint address, out long completedCycle)
        {
            var cycle = BeginCpuBusAccessCycle();
            var value = ReadInstructionFetchWord(address, ref cycle);
            _cpuBusCycle = cycle;
            completedCycle = cycle;
            return value;
        }

        private ushort ReadInstructionFetchWord(uint address, ref long cycle)
        {
            if ((address & 1) != 0)
            {
                State.Cycles = Math.Max(State.Cycles, cycle);
                _cpuBusCycle = Math.Max(_cpuBusCycle, cycle);
                _cpuRetireBusCycle = Math.Max(_cpuRetireBusCycle, cycle);
                RaiseAddressError(address, isWrite: false, M68kBusAccessKind.CpuInstructionFetch);
                throw M68kAddressErrorException.Instance;
            }

            if (_instructionFetchWindowBus != null)
            {
                var bus = _instructionFetchWindowBus;
                if (!_instructionFetchWindow.ContainsWord(address) &&
                    (!TryGetInstructionFetchWindow(bus, address, out _instructionFetchWindow) ||
                    !_instructionFetchWindow.ContainsWord(address)))
                {
                    return _bus.ReadWord(address, ref cycle, M68kBusAccessKind.CpuInstructionFetch);
                }

                bus.CommitInstructionFetchWindowWord(in _instructionFetchWindow, address, ref cycle);
                return _instructionFetchWindow.ReadWord(address);
            }

            return _bus.ReadWord(address, ref cycle, M68kBusAccessKind.CpuInstructionFetch);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryGetInstructionFetchWindow(
            IM68kInstructionFetchWindowBus bus,
            uint address,
            out M68kInstructionFetchWindow window)
        {
            if (bus.TryGetInstructionFetchWindow(address, out window))
            {
                return true;
            }

            window = M68kInstructionFetchWindow.Empty;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte ReadByte(uint address)
        {
            var cycle = BeginCpuBusAccessCycle();
            var value = TCpuDataAccess.ReadByte(_typedBus, address, ref cycle);
            CompleteCpuBusAccess(cycle);
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ushort ReadWord(uint address)
        {
            if ((address & 1) != 0)
            {
                ThrowOddAddressAccess(address, isWrite: false, M68kBusAccessKind.CpuDataRead);
            }

            var cycle = BeginCpuBusAccessCycle();
            var value = TCpuDataAccess.ReadWord(_typedBus, address, ref cycle);
            CompleteCpuBusAccess(cycle);
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint ReadLong(uint address)
        {
            if ((address & 1) != 0)
            {
                ThrowOddAddressAccess(address, isWrite: false, M68kBusAccessKind.CpuDataRead);
            }

            var cycle = BeginCpuBusAccessCycle();
            var value = TCpuDataAccess.ReadLong(_typedBus, address, ref cycle);
            CompleteCpuBusAccess(cycle);
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteByte(uint address, byte value)
        {
            var cycle = BeginCpuBusAccessCycle();
            TCpuDataAccess.WriteByte(_typedBus, address, value, ref cycle);
            CompleteCpuBusAccess(cycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteWord(uint address, ushort value)
        {
            if ((address & 1) != 0)
            {
                ThrowOddAddressAccess(address, isWrite: true, M68kBusAccessKind.CpuDataWrite);
            }

            var cycle = BeginCpuBusAccessCycle();
            TCpuDataAccess.WriteWord(_typedBus, address, value, ref cycle);
            CompleteCpuBusAccess(cycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteLong(uint address, uint value)
        {
            if ((address & 1) != 0)
            {
                ThrowOddAddressAccess(address, isWrite: true, M68kBusAccessKind.CpuDataWrite);
            }

            var cycle = BeginCpuBusAccessCycle();
            TCpuDataAccess.WriteLong(_typedBus, address, value, ref cycle);
            CompleteCpuBusAccess(cycle);
        }

        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ThrowOddAddressAccess(uint address, bool isWrite, M68kBusAccessKind accessKind)
        {
            var faultCycle = BeginCpuBusAccessCycle();
            State.Cycles = faultCycle;
            _cpuBusCycle = faultCycle;
            _cpuRetireBusCycle = Math.Max(_cpuRetireBusCycle, faultCycle);
            RaiseAddressError(address, isWrite, accessKind);
            throw M68kAddressErrorException.Instance;
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

        private ushort PullWord()
        {
            var value = ReadWord(State.A[7]);
            State.SetActiveStackPointer(State.A[7] + 2);
            return value;
        }

        private uint PullLong()
        {
            var value = ReadLong(State.A[7]);
            State.SetActiveStackPointer(State.A[7] + 4);
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long BeginCpuBusAccessCycle()
            => State.Cycles < _cpuBusCycle ? _cpuBusCycle : State.Cycles;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CompleteCpuBusAccess(long completedCycle)
        {
            _cpuBusCycle = completedCycle;
            if (_cpuRetireBusCycle < completedCycle)
            {
                _cpuRetireBusCycle = completedCycle;
            }

            State.Cycles = completedCycle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddCycles(int cycles)
        {
            System.Diagnostics.Debug.Assert(cycles > 0, "MC68000 cycle increments must be positive.");
            if (_instructionCycleFloorActive)
            {
                _instructionCycleFloor = Math.Max(_instructionCycleFloor, _instructionCycleStart + cycles);
                return;
            }

            State.Cycles += cycles;
        }

        private void BeginInstructionCycleFloor(long startCycle)
        {
            _instructionCycleFloorActive = true;
            _instructionCycleStart = startCycle;
            _instructionCycleFloor = startCycle;
            if (_cpuBusCycle < startCycle)
            {
                _cpuBusCycle = startCycle;
            }

            if (_cpuRetireBusCycle < startCycle)
            {
                _cpuRetireBusCycle = startCycle;
            }
        }

        private int CompleteInstruction(long startCycle)
        {
            var completedCycle = Math.Max(_instructionCycleFloor, _cpuRetireBusCycle);
            if (State.Cycles < completedCycle)
            {
                State.Cycles = completedCycle;
            }

            return (int)(State.Cycles - startCycle);
        }

        private void RaiseException(int vector, uint stackedProgramCounter, int cycles)
        {
            var savedStatusRegister = State.StatusRegister;
            State.RecordException(vector, stackedProgramCounter, savedStatusRegister);
            State.StatusRegister |= M68kCpuState.Supervisor;
            PushLong(stackedProgramCounter);
            PushWord(savedStatusRegister);
            SetProgramCounterAndFlushPrefetch(ReadLong((uint)(vector * 4)));
            AddCycles(cycles);
        }

        private void RaiseAddressError(uint faultAddress, bool isWrite, M68kBusAccessKind accessKind)
        {
            var savedStatusRegister = State.StatusRegister;
            State.StatusRegister |= M68kCpuState.Supervisor;
            PushLong(State.ProgramCounter);
            PushWord(savedStatusRegister);
            PushWord(State.LastOpcode);
            PushLong(faultAddress);
            PushWord(CreateBusErrorStatusWord(faultAddress, savedStatusRegister, isWrite, accessKind));
            SetProgramCounterAndFlushPrefetch(ReadLong(0x0000_000C));
            AddCycles(AddressErrorExceptionCycles);
        }

        private static ushort CreateBusErrorStatusWord(
            uint faultAddress,
            ushort savedStatusRegister,
            bool isWrite,
            M68kBusAccessKind accessKind)
        {
            _ = faultAddress;
            var instruction = accessKind == M68kBusAccessKind.CpuInstructionFetch;
            var supervisor = (savedStatusRegister & M68kCpuState.Supervisor) != 0;
            var functionCode = instruction
                ? (supervisor ? 0x06 : 0x02)
                : (supervisor ? 0x05 : 0x01);
            var status = functionCode & 0x07;
            if (!instruction)
            {
                status |= 0x08;
            }

            if (!isWrite)
            {
                status |= 0x10;
            }

            return (ushort)status;
        }

        private static uint AddressIncrement(int reg, M68kOperandSize size)
            => M68kIntegerSemantics.AddressIncrement(reg, size);

        private static int EstimateEaCycles(EaOperand source, EaOperand destination, M68kOperandSize size, bool write)
        {
            _ = source;
            _ = destination;
            _ = write;
            var baseCycles = size == M68kOperandSize.Long ? 12 : 8;
            if (destination.IsRegister)
            {
                baseCycles = Math.Max(baseCycles, 4 + source.EaCycles);
            }

            return baseCycles;
        }

        private static int GetMultiplyCycles(int sourceEaCycles, uint sourceValue, bool signed)
            => M68kIntegerSemantics.GetMultiplyCycles(sourceEaCycles, sourceValue, signed);

        private static int GetMultiplyCoreCycles(uint sourceValue, bool signed)
            => M68kIntegerSemantics.GetMultiplyCoreCycles(sourceValue, signed);

        private static int CountBits(int value)
            => M68kIntegerSemantics.CountBits((uint)value);

        private readonly struct PlannedEaOperand
        {
            private PlannedEaOperand(int kind, int register, uint address, uint immediate, M68kOperandSize size, int eaCycles)
            {
                Kind = kind;
                Register = register;
                Address = address;
                Immediate = immediate;
                Size = size;
                EaCycles = eaCycles;
            }

            public int Kind { get; }

            public int Register { get; }

            public uint Address { get; }

            public uint Immediate { get; }

            public M68kOperandSize Size { get; }

            public int EaCycles { get; }

            public bool IsRegister => Kind is 0 or 1;

            public static PlannedEaOperand DataRegister(int register, M68kOperandSize size)
                => new PlannedEaOperand(0, register, 0, 0, size, 0);

            public static PlannedEaOperand AddressRegister(int register, M68kOperandSize size)
                => new PlannedEaOperand(1, register, 0, 0, size, 0);

            public static PlannedEaOperand Memory(uint address, M68kOperandSize size, int eaCycles)
                => new PlannedEaOperand(2, 0, address, 0, size, eaCycles);

            public static PlannedEaOperand ImmediateValue(uint value, M68kOperandSize size)
                => new PlannedEaOperand(3, 0, 0, value, size, size == M68kOperandSize.Long ? 8 : 4);
        }

        private readonly struct EaOperand
        {
            private readonly M68kInterpreterCore<TBus, TCpuDataAccess> _cpu;
            private readonly int _kind;
            private readonly int _reg;
            private readonly uint _immediate;

            private EaOperand(M68kInterpreterCore<TBus, TCpuDataAccess> cpu, int kind, int reg, uint address, uint immediate, M68kOperandSize size, int eaCycles)
            {
                _cpu = cpu;
                _kind = kind;
                _reg = reg;
                Address = address;
                _immediate = immediate;
                Size = size;
                EaCycles = eaCycles;
            }

            public uint Address { get; }

            public M68kOperandSize Size { get; }

            public int EaCycles { get; }

            public bool IsRegister => _kind is 0 or 1;

            public static EaOperand DataRegister(M68kInterpreterCore<TBus, TCpuDataAccess> cpu, int reg, M68kOperandSize size)
            {
                return new EaOperand(cpu, 0, reg, 0, 0, size, eaCycles: 0);
            }

            public static EaOperand AddressRegister(M68kInterpreterCore<TBus, TCpuDataAccess> cpu, int reg, M68kOperandSize size)
            {
                return new EaOperand(cpu, 1, reg, 0, 0, size, eaCycles: 0);
            }

            public static EaOperand Memory(M68kInterpreterCore<TBus, TCpuDataAccess> cpu, uint address, M68kOperandSize size, int eaCycles)
            {
                return new EaOperand(cpu, 2, 0, address, 0, size, eaCycles);
            }

            public static EaOperand Immediate(M68kInterpreterCore<TBus, TCpuDataAccess> cpu, uint value, M68kOperandSize size)
            {
                return new EaOperand(cpu, 3, 0, 0, value, size, eaCycles: size == M68kOperandSize.Long ? 8 : 4);
            }

            public uint Read()
            {
                return _kind switch
                {
                    0 => _cpu.State.D[_reg] & M68kCpuState.Mask(Size),
                    1 => Size == M68kOperandSize.Word ? _cpu.State.A[_reg] & 0xFFFF : _cpu.State.A[_reg],
                    2 => Size switch
                    {
                        M68kOperandSize.Byte => _cpu.ReadByte(Address),
                        M68kOperandSize.Word => _cpu.ReadWord(Address),
                        _ => _cpu.ReadLong(Address)
                    },
                    3 => _immediate & M68kCpuState.Mask(Size),
                    _ => 0
                };
            }

            public void Write(uint value)
            {
                value &= M68kCpuState.Mask(Size);
                switch (_kind)
                {
                    case 0:
                        _cpu.WriteDataRegister(_reg, value, Size);
                        break;
                    case 1:
                        _cpu.SetAddressRegister(
                            _reg,
                            Size == M68kOperandSize.Word
                                ? M68kCpuState.SignExtend(value, M68kOperandSize.Word)
                                : value);
                        break;
                    case 2:
                        if (Size == M68kOperandSize.Byte)
                        {
                            _cpu.WriteByte(Address, (byte)value);
                        }
                        else if (Size == M68kOperandSize.Word)
                        {
                            _cpu.WriteWord(Address, (ushort)value);
                        }
                        else
                        {
                            _cpu.WriteLong(Address, value);
                        }

                        break;
                    default:
                        throw new M68kEmulationException("Cannot write to an immediate MC68000 operand.");
                }
            }
        }
    }

    internal sealed class M68kInterpreter : M68kInterpreterCore<IM68kBus, M68kNoExactCpuDataAccess<IM68kBus>>
    {
        public M68kInterpreter(
            IM68kBus bus,
            M68kOpcodePlanDispatch opcodePlanDispatch = M68kOpcodePlanDispatch.KindTable)
            : base(bus, default, opcodePlanDispatch)
        {
        }

        internal M68kInterpreter(
            IM68kBus bus,
            M68kCpuState state,
            M68kInstructionFrequencyMatrix? instructionFrequency = null,
            bool enableInstructionFetchWindow = true,
            bool enableOpcodePlan = true,
            M68kOpcodePlanDispatch opcodePlanDispatch = M68kOpcodePlanDispatch.KindTable)
            : base(
                bus,
                default,
                state,
                instructionFrequency,
                enableInstructionFetchWindow,
                enableOpcodePlan,
                opcodePlanDispatch)
        {
        }

        internal static new bool HasDelegatePlanForOpcode(ushort opcode)
            => M68kInterpreterCore<IM68kBus, M68kNoExactCpuDataAccess<IM68kBus>>.HasDelegatePlanForOpcode(opcode);
    }
}
