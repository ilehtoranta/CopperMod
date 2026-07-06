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
        /// Motorola MC68010-compatible execution.
        /// </summary>
        M68010 = 10,

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

    internal interface IM68kDeferredCpuInstructionTiming
    {
        void BeginDeferredCpuInstructionTiming(long cycle);

        void FlushDeferredCpuInstructionTiming(ref long cycle);

        bool IsDeferredCpuBusBatchActive { get; }

        bool IsDeferredCpuBusBatchEligibleInstructionFetchWindow(in M68kInstructionFetchWindow window);

        bool TryBeginDeferredCpuBusBatch(
            M68kCpuState state,
            long currentCycle,
            long? targetCycle,
            out long batchTargetCycle,
            out M68kTraceBatchWakeSource wakeSource);

        void CompleteDeferredCpuBusBatchInstruction(long previousCycle, long currentCycle);

        void EndDeferredCpuBusBatch(ref long cycle, M68kDeferredCpuBusBatchExitReason reason);
    }

    internal enum M68kDeferredCpuBusBatchExitReason
    {
        None = 0,
        Completed = 1,
        TargetCycle = 2,
        MaxInstructions = 3,
        PcLeftFastWindow = 4,
        ChipVisibleAccess = 5,
        Exception = 6,
        HaltedOrStopped = 7,
        Unsupported = 8
    }

    internal interface IM68kCpuDataAccess<TBus, TSelf>
        where TBus : IM68kBus
        where TSelf : struct, IM68kCpuDataAccess<TBus, TSelf>
    {
        static abstract byte ReadByte(TBus bus, uint address, ref long cycle);

        static abstract ushort ReadWord(TBus bus, uint address, ref long cycle);

        static abstract uint ReadLong(TBus bus, uint address, ref long cycle);

        static abstract void WriteByte(TBus bus, uint address, byte value, ref long cycle);

        static abstract void WriteTasByte(TBus bus, uint address, byte value, ref long cycle);

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
        public static void WriteTasByte(TBus bus, uint address, byte value, ref long cycle)
            => WriteByte(bus, address, value, ref cycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteWord(TBus bus, uint address, ushort value, ref long cycle)
            => bus.WriteWord(address, value, ref cycle, M68kBusAccessKind.CpuDataWrite);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteLong(TBus bus, uint address, uint value, ref long cycle)
            => bus.WriteLong(address, value, ref cycle, M68kBusAccessKind.CpuDataWrite);
    }

    internal readonly struct M68kCpuBusPhase
    {
        public M68kCpuBusPhase(
            uint instructionProgramCounter,
            uint address,
            M68kOperandSize size,
            long requestedCycle,
            long completedCycle,
            M68kBusAccessKind accessKind,
            bool isWrite)
        {
            InstructionProgramCounter = instructionProgramCounter;
            Address = address;
            Size = size;
            RequestedCycle = requestedCycle;
            CompletedCycle = completedCycle;
            AccessKind = accessKind;
            IsWrite = isWrite;
        }

        public uint InstructionProgramCounter { get; }

        public uint Address { get; }

        public M68kOperandSize Size { get; }

        public long RequestedCycle { get; }

        public long CompletedCycle { get; }

        public M68kBusAccessKind AccessKind { get; }

        public bool IsWrite { get; }
    }

    internal interface IM68kCpuBusPhaseTrace
    {
        bool CpuBusPhaseTracingEnabled { get; }

        void RecordCpuBusPhase(in M68kCpuBusPhase phase);
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
            bool enableCpuBusPhaseTrace = true,
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
                enableCpuBusPhaseTrace,
                enableOpcodePlan,
                opcodePlanDispatch ?? M68000OpcodePlanDispatch);

        /// <inheritdoc />
        public IM68kCore Create(M68kCpuModel model, IM68kBus bus)
        {
            return model switch
            {
                M68kCpuModel.M68000 => new M68kInterpreter(bus, opcodePlanDispatch: M68000OpcodePlanDispatch),
                M68kCpuModel.M68010 => new M68010Interpreter(bus),
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
        /// Status-register bit for 68000 trace mode.
        /// </summary>
        public const ushort Trace = 0x8000;

        /// <summary>
        /// Hardware reset status register value: supervisor mode with interrupt mask 7.
        /// </summary>
        public const ushort ResetStatusRegister = 0x2700;
        private const ushort M68000StatusRegisterMask = Trace | Supervisor | 0x0700 | ConditionCodeMask;
        private const ushort M68020StatusRegisterMask = Trace | Master | Supervisor | 0x0700 | ConditionCodeMask;
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

        internal void DisableM68020StackMode()
        {
            if (!M68020StackModeEnabled)
            {
                return;
            }

            SaveActiveM68020StackPointer(_statusRegister);
            _statusRegister &= M68000StatusRegisterMask;
            M68020StackModeEnabled = false;
            A[7] = (_statusRegister & Supervisor) != 0
                ? SupervisorStackPointer
                : UserStackPointer;
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

            value &= M68000StatusRegisterMask;
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
            value &= M68020StatusRegisterMask;
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
        /// Updates the negative and zero condition codes from a long-sized value
        /// without the Mask/SignBit dispatch overhead.
        /// </summary>
        /// <param name="value">The 32-bit value to test.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetNegativeZeroLong(uint value)
        {
            var status = _statusRegister & unchecked((ushort)~(Zero | Negative));
            if (value == 0)
            {
                status |= Zero;
            }

            if ((value & 0x8000_0000) != 0)
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
        private readonly IM68kBus _bus;
        private readonly TBus _typedBus;
        private readonly IM68kInstructionFetchWindowBus? _instructionFetchWindowBus;
        private readonly IM68kDeferredCpuInstructionTiming? _deferredCpuInstructionTiming;
        private readonly IM68kCpuBusPhaseTrace? _cpuBusPhaseTrace;
        private readonly M68kInstructionFrequencyMatrix _instructionFrequency;
        private readonly M68kOpcodePlanDispatch _opcodePlanDispatch;
        private M68kInstructionFetchWindow _instructionFetchWindow;
        private uint _prefetchAddress;
        private ushort _prefetchWord;
        private long _prefetchCompletedCycle;
        private bool _prefetchValid;
        private bool _prefetchDeferredCpuBusBatchEligible;
        private long _cpuBusCycle;
        private long _cpuRetireBusCycle;
        private bool _instructionCycleFloorActive;
        private uint _activeInstructionProgramCounter;
        private uint _dataAccessStackedProgramCounter;
        private ushort? _addressErrorInstructionWord;
        private bool? _addressErrorIsWriteOverride;
        private M68kBusAccessKind _dataReadFaultAccessKind;
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
            bool enableCpuBusPhaseTrace = true,
            bool enableOpcodePlan = true,
            M68kOpcodePlanDispatch opcodePlanDispatch = M68kOpcodePlanDispatch.KindTable)
        {
            _typedBus = bus ?? throw new ArgumentNullException(nameof(bus));
            _bus = bus;
            _ = cpuDataAccess;
            _instructionFetchWindowBus = enableInstructionFetchWindow
                ? bus as IM68kInstructionFetchWindowBus
                : null;
            _deferredCpuInstructionTiming = bus as IM68kDeferredCpuInstructionTiming;
            _cpuBusPhaseTrace = enableCpuBusPhaseTrace &&
                bus is IM68kCpuBusPhaseTrace { CpuBusPhaseTracingEnabled: true } trace
                    ? trace
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

                if (TryExecuteDeferredCpuBusBatch(
                    maxInstructions - instructions,
                    targetCycle,
                    boundary,
                    out var batchInstructions))
                {
                    instructions += batchInstructions;
                    continue;
                }

                if (!boundary.BeforeInstruction())
                {
                    break;
                }

                var previousCycle = State.Cycles;
                ExecuteSingleInstruction();
                boundary.AfterInstruction(previousCycle, State.Cycles);
                instructions++;
            }

            return instructions;
        }

        int IM68kBatchCore.ExecuteInstructions(int maxInstructions, long? targetCycle, IM68kInstructionBoundary boundary)
            => ExecuteInstructions(maxInstructions, targetCycle, boundary);

        public int ExecuteInstruction()
        {
            var startCycles = State.Cycles;
            if (TryExecuteDeferredCpuBusBatch(
                DeferredCpuBusBatchNoTargetInstructionLimit,
                targetCycle: null,
                NoOpInstructionBoundary.Instance,
                out _))
            {
                return (int)(State.Cycles - startCycles);
            }

            var elapsed = ExecuteSingleInstruction();
            _ = elapsed;
            if (TryExecuteDeferredCpuBusBatch(
                DeferredCpuBusBatchNoTargetInstructionLimit - 1,
                targetCycle: null,
                NoOpInstructionBoundary.Instance,
                out _))
            {
                return (int)(State.Cycles - startCycles);
            }

            return elapsed;
        }

        private int ExecuteSingleInstruction()
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
                if ((State.ProgramCounter & 1) != 0)
                {
                    ThrowOddInstructionFetchAddress(State.ProgramCounter);
                }

                var result = ExecuteInstructionBody(startCycles);
                _instructionCycleFloorActive = false;
                return result;
            }
            catch (M68kAddressErrorException)
            {
                _instructionCycleFloorActive = false;
                return CompleteInstruction(startCycles);
            }
            catch (M68kIllegalInstructionException)
            {
                _instructionCycleFloorActive = false;
                return CompleteInstruction(startCycles);
            }
        }

        private const int DeferredCpuBusBatchNoTargetInstructionLimit = 256;

        private bool TryExecuteDeferredCpuBusBatch(
            int maxInstructions,
            long? targetCycle,
            IM68kInstructionBoundary boundary,
            out int executedInstructions)
        {
            executedInstructions = 0;
            if (_deferredCpuInstructionTiming == null ||
                maxInstructions <= 1 ||
                State.Halted ||
                State.Stopped ||
                !IsCurrentPrefetchDeferredCpuBusBatchEligible())
            {
                return false;
            }

            var currentCycle = State.Cycles;
            if (!_deferredCpuInstructionTiming.TryBeginDeferredCpuBusBatch(
                State,
                currentCycle,
                targetCycle,
                out var batchTargetCycle,
                out _))
            {
                return false;
            }

            var batchStartCycle = currentCycle;
            var boundaryIsNoOp = ReferenceEquals(boundary, NoOpInstructionBoundary.Instance);
            var batchBoundary = boundaryIsNoOp ? null : boundary as IM68kBusAccessTraceBatchBoundary;
            var useBatchBoundary = false;
            var reason = M68kDeferredCpuBusBatchExitReason.Completed;
            var completedWithoutException = false;
            try
            {
                if (batchBoundary != null)
                {
                    if (!batchBoundary.TryBeginBusAccessTraceBatch(State, batchTargetCycle, out var boundaryBatchTargetCycle))
                    {
                        reason = M68kDeferredCpuBusBatchExitReason.Unsupported;
                        completedWithoutException = true;
                        return false;
                    }

                    batchTargetCycle = Math.Min(batchTargetCycle, boundaryBatchTargetCycle);
                    useBatchBoundary = true;
                }

                while (executedInstructions < maxInstructions &&
                    !State.Halted &&
                    !State.Stopped &&
                    State.Cycles < batchTargetCycle)
                {
                    if (!IsCurrentPrefetchDeferredCpuBusBatchEligible())
                    {
                        reason = M68kDeferredCpuBusBatchExitReason.PcLeftFastWindow;
                        break;
                    }

                    if (!boundaryIsNoOp &&
                        !useBatchBoundary &&
                        !boundary.BeforeInstruction())
                    {
                        reason = M68kDeferredCpuBusBatchExitReason.Unsupported;
                        break;
                    }

                    var previousCycle = State.Cycles;
                    ExecuteSingleInstruction();
                    if (!boundaryIsNoOp && !useBatchBoundary)
                    {
                        boundary.AfterInstruction(previousCycle, State.Cycles);
                    }

                    _deferredCpuInstructionTiming.CompleteDeferredCpuBusBatchInstruction(previousCycle, State.Cycles);
                    executedInstructions++;

                    if (!_deferredCpuInstructionTiming.IsDeferredCpuBusBatchActive)
                    {
                        reason = M68kDeferredCpuBusBatchExitReason.ChipVisibleAccess;
                        break;
                    }

                    if (State.Halted || State.Stopped)
                    {
                        reason = M68kDeferredCpuBusBatchExitReason.HaltedOrStopped;
                        break;
                    }

                    if (State.Cycles >= batchTargetCycle)
                    {
                        reason = M68kDeferredCpuBusBatchExitReason.TargetCycle;
                        break;
                    }
                }

                if (executedInstructions >= maxInstructions)
                {
                    reason = M68kDeferredCpuBusBatchExitReason.MaxInstructions;
                }

                completedWithoutException = true;
                return executedInstructions > 0;
            }
            catch
            {
                reason = M68kDeferredCpuBusBatchExitReason.Exception;
                throw;
            }
            finally
            {
                if (_deferredCpuInstructionTiming.IsDeferredCpuBusBatchActive)
                {
                    var cycle = State.Cycles;
                    _deferredCpuInstructionTiming.EndDeferredCpuBusBatch(ref cycle, reason);
                    if (State.Cycles < cycle)
                    {
                        State.Cycles = cycle;
                    }
                }

                if (completedWithoutException &&
                    useBatchBoundary &&
                    executedInstructions > 0)
                {
                    batchBoundary!.AfterBusAccessTraceBatch(batchStartCycle, State.Cycles, executedInstructions);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsCurrentPrefetchDeferredCpuBusBatchEligible()
            => _prefetchValid &&
                _prefetchAddress == State.ProgramCounter &&
                _prefetchDeferredCpuBusBatchEligible;

        private sealed class NoOpInstructionBoundary : IM68kInstructionBoundary
        {
            public static NoOpInstructionBoundary Instance { get; } = new NoOpInstructionBoundary();

            public bool BeforeInstruction()
                => true;

            public void AfterInstruction(long previousCycle, long currentCycle)
            {
                _ = previousCycle;
                _ = currentCycle;
            }
        }

        private int ExecuteInstructionBody(long startCycles)
        {
            var instructionPc = State.ProgramCounter;
            _activeInstructionProgramCounter = instructionPc;
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

            // Exception bookkeeping deferred to scalar path only.
            _dataAccessStackedProgramCounter = instructionPc;
            _addressErrorInstructionWord = null;
            _addressErrorIsWriteOverride = null;
            _dataReadFaultAccessKind = M68kBusAccessKind.CpuDataRead;

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
                if (opcode == 0xFF00)
                {
                    var trapId = FetchWord();
                    var returnProgramCounter = State.ProgramCounter;
                    if (_bus.TryInvokeHostTrap(instructionPc, trapId, State))
                    {
                        AddInstructionCycles(16);
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
                M68kOpcodePlanDispatch.PackedPlan => TryExecutePackedPlan(
                    opcode,
                    instructionPc,
                    in M68kOpcodePlanTable.PackedPlans[opcode]),
                _ => false
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryExecutePlannedKind(ushort opcode, uint instructionPc, M68kOpcodePlanKind kind)
        {
            switch (kind)
            {
                case M68kOpcodePlanKind.Nop:
                    AddInstructionCycles(4);
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
                    AddInstructionCycles(4);
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
                    ExecutePackedMove(in plan);
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

        private void ExecutePlannedMoveq(ushort opcode)
        {
            var register = (opcode >> 9) & 7;
            State.D[register] = unchecked((uint)(int)(sbyte)(opcode & 0xFF));
            State.SetNegativeZeroLong(State.D[register]);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            AddInstructionCycles(4);
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

                AddInstructionCycles(10);
                BranchTo(target, branchBase);
                return;
            }

            AddInstructionCycles(extensionDisplacement ? 12 : 8);
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
                if (counter != 0xFFFF)
                {
                    var target = unchecked((uint)(branchBase + displacement));
                    if (_instructionFrequency.Enabled)
                    {
                        _instructionFrequency.RecordTakenBranch(State.LastInstructionProgramCounter, opcode, target, 4);
                    }

                    AddInstructionCycles(10);
                    BranchTo(target, State.ProgramCounter);
                    State.D[register] = (State.D[register] & 0xFFFF_0000) | counter;
                    return;
                }

                State.D[register] = (State.D[register] & 0xFFFF_0000) | counter;
                AddInstructionCycles(14);
                return;
            }

            AddInstructionCycles(12);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePlannedDbra(ushort opcode)
        {
            var branchBase = State.ProgramCounter;
            var displacement = unchecked((short)FetchWord());
            var register = opcode & 7;
            var counter = (ushort)((State.D[register] & 0xFFFF) - 1);
            if (counter != 0xFFFF)
            {
                var target = unchecked((uint)(branchBase + displacement));
                if (_instructionFrequency.Enabled)
                {
                    _instructionFrequency.RecordTakenBranch(State.LastInstructionProgramCounter, opcode, target, 4);
                }

                AddInstructionCycles(10);
                BranchTo(target, State.ProgramCounter);
                State.D[register] = (State.D[register] & 0xFFFF_0000) | counter;
                return;
            }

            State.D[register] = (State.D[register] & 0xFFFF_0000) | counter;
            AddInstructionCycles(14);
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
                AddInstructionCycles(8);
                return;
            }

            var old = State.D[register] & M68kCpuState.Mask(size);
            var result = subtract
                ? Subtract(old, (uint)count, size, setExtend: true)
                : Add(old, (uint)count, size, setExtend: true);
            WriteDataRegister(register, result, size);
            AddInstructionCycles(size == M68kOperandSize.Long ? 8 : 4);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePlannedSubqLongOneDataRegister(ushort opcode)
        {
            var register = opcode & 7;
            State.D[register] = Subtract(State.D[register], 1, M68kOperandSize.Long, setExtend: true);
            AddInstructionCycles(8);
        }

        private void ExecutePlannedMove(ushort opcode)
        {
            var size = DecodePlannedMoveSize(opcode);
            var sourceMode = (opcode >> 3) & 7;
            var sourceRegister = opcode & 7;
            var destinationMode = (opcode >> 6) & 7;
            var destinationRegister = (opcode >> 9) & 7;
            if ((sourceMode == 3 || destinationMode == 3) &&
                TryExecuteMoveFast(sourceMode, sourceRegister, destinationMode, destinationRegister, size))
            {
                return;
            }

            if (sourceMode == 5 && destinationMode == 0)
            {
                ExecuteMoveDisplacementToData(sourceRegister, destinationRegister, size);
                return;
            }

            var source = ResolvePlannedEa(
                sourceMode,
                sourceRegister,
                size,
                completeWordPostIncrementBeforeRead: size == M68kOperandSize.Word);
            if (size == M68kOperandSize.Word && !source.IsRegister)
            {
                AddInstructionCyclesFromBase(_instructionCycleFloor, 4);
            }

            var value = ReadPlannedEaValue(in source);
            var updateNegativeZeroBeforeWrite =
                (size == M68kOperandSize.Word && destinationMode is 2 or 3 or 4 or 5 or 6 or 7) ||
                (size == M68kOperandSize.Long &&
                ((sourceMode == 4 && destinationMode is not 2 and not 3) ||
                    (sourceMode == 3 && destinationMode == 6) ||
                    destinationMode == 4 || destinationMode == 6 ||
                    (sourceMode == 2 && destinationMode == 5) ||
                    (sourceMode == 3 && destinationMode == 5) ||
                    (sourceMode == 5 && destinationMode == 5) ||
                    (sourceMode == 6 && destinationMode == 5) ||
                    (sourceMode == 7 && sourceRegister == 4 && destinationMode == 5) ||
                    (sourceMode == 7 && sourceRegister == 3 && destinationMode == 5) ||
                    (sourceMode == 7 && sourceRegister == 1 && destinationMode == 5) ||
                    (sourceMode == 0 && destinationMode == 5) ||
                    (sourceMode == 1 && destinationMode == 5) ||
                    (destinationMode == 7 && destinationRegister <= 1 &&
                        (sourceMode != 3 || destinationRegister == 0))));
            var updateZeroBeforeWrite = size == M68kOperandSize.Long &&
                ((sourceMode == 5 && sourceRegister == 7) ||
                    (sourceMode == 4 && destinationMode == 2 &&
                        !(destinationRegister <= 1 || (destinationRegister == 6 && sourceRegister == 0))) ||
                    (sourceMode == 7 && sourceRegister == 1 && destinationMode == 2));
            var updateLowWordNegativeBeforeWrite = size == M68kOperandSize.Long &&
                ((sourceMode == 6 && destinationMode is 2 or 3) ||
                    (sourceMode == 2 && (destinationMode == 3 ||
                        (destinationMode == 2 && destinationRegister == 2) ||
                        (sourceRegister == 7 && destinationMode == 2))) ||
                    (sourceMode == 3 && (destinationMode is 2 or 3 || (destinationMode == 7 && destinationRegister == 1))) ||
                    (sourceMode == 4 && destinationMode == 3) ||
                    (sourceMode == 5 && destinationMode is 2 or 3) ||
                    (sourceMode == 7 && sourceRegister == 0 && destinationMode == 3) ||
                    (sourceMode == 7 && sourceRegister == 2 && destinationMode == 2 && destinationRegister == 6) ||
                    (sourceMode == 7 && sourceRegister == 2 && destinationMode is 3 or 5));
            var clearNegativeZeroBeforeWrite = size == M68kOperandSize.Long &&
                destinationMode == 2 &&
                ((sourceMode == 4 &&
                        (destinationRegister <= 1 || (destinationRegister == 6 && sourceRegister == 0))) ||
                    (sourceMode == 2 && sourceRegister != 7 && destinationRegister != 2) ||
                    (sourceMode == 7 && sourceRegister is 2 or 3 &&
                        !(sourceRegister == 2 && destinationRegister == 6)));
            var preserveOverflowUntilSuccessfulWrite = size == M68kOperandSize.Long &&
                ((destinationMode == 6 && sourceMode is 0 or 1) ||
                    (sourceMode == 0 && destinationMode == 5) ||
                    (sourceMode == 1 && destinationMode == 5) ||
                    (sourceMode == 7 && sourceRegister == 4 && destinationMode == 5));
            var preserveCarryUntilSuccessfulWrite = size == M68kOperandSize.Long &&
                ((destinationMode == 6 && destinationRegister != 7 && sourceMode is 0 or 1) ||
                    (sourceMode == 0 && destinationMode == 6) ||
                    (sourceMode == 1 && destinationMode == 6) ||
                    (sourceMode == 0 && destinationMode == 5) ||
                    (sourceMode == 1 && destinationMode == 5));
            var deferConditionCodesUntilSuccessfulWrite = size == M68kOperandSize.Long &&
                ((sourceMode == 7 && sourceRegister == 4 && destinationMode is 2 or 3) ||
                    (sourceMode is 0 or 1 && destinationMode is 2 or 3));
            var addressErrorStackedProgramCounterOffset =
                GetMoveDestinationAddressErrorStackedProgramCounterOffset(size, sourceMode, destinationMode, destinationRegister);
            var destination = ResolvePlannedEa(
                destinationMode,
                destinationRegister,
                size,
                write: true,
                addressErrorStackedProgramCounterOffset: addressErrorStackedProgramCounterOffset);
            if (destinationMode != 1)
            {
                if (!preserveOverflowUntilSuccessfulWrite && !deferConditionCodesUntilSuccessfulWrite)
                {
                    State.SetFlag(M68kCpuState.Overflow, false);
                }

                if (!deferConditionCodesUntilSuccessfulWrite)
                {
                    if (!preserveCarryUntilSuccessfulWrite)
                    {
                        State.SetFlag(M68kCpuState.Carry, false);
                    }

                    if (updateNegativeZeroBeforeWrite)
                    {
                        State.SetNegativeZero(value, size);
                    }
                    else if (updateZeroBeforeWrite)
                    {
                        State.SetFlag(M68kCpuState.Zero, (value & M68kCpuState.Mask(size)) == 0);
                    }
                    else if (updateLowWordNegativeBeforeWrite)
                    {
                        State.SetFlag(M68kCpuState.Negative, (value & 0x8000) != 0);
                        State.SetFlag(M68kCpuState.Zero, false);
                    }
                    else if (clearNegativeZeroBeforeWrite)
                    {
                        State.SetFlag(M68kCpuState.Negative, false);
                        State.SetFlag(M68kCpuState.Zero, false);
                    }
                }
            }

            if (size == M68kOperandSize.Word && destinationMode == 4)
            {
                UsePrefetchedInstructionWordForAddressErrorFrame();
            }

            if (size == M68kOperandSize.Word && destinationMode != 4 && !destination.IsRegister && (destination.Address & 1) != 0)
            {
                AddInstructionCycles(GetMoveWordDestinationWriteFaultCycles(source.EaCycles, destinationMode, destinationRegister));
            }
            else if (size == M68kOperandSize.Word && destinationMode == 4 && !destination.IsRegister && (destination.Address & 1) != 0)
            {
                AddInstructionCycles(EstimateMoveCycles(source.EaCycles, destinationMode, destinationRegister, size) + 4);
            }
            else if (size == M68kOperandSize.Long && !destination.IsRegister && (destination.Address & 1) != 0)
            {
                var cycles = EstimateMoveCycles(source.EaCycles, destinationMode, destinationRegister, size);
                AddInstructionCycles(GetMoveLongDestinationWriteFaultCycles(cycles, destinationMode, destinationRegister));
            }

            WritePlannedEaValue(in destination, value);
            if (destinationMode != 1 && deferConditionCodesUntilSuccessfulWrite)
            {
                State.SetNegativeZero(value, size);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
            }
            else if (destinationMode != 1 && (updateZeroBeforeWrite || updateLowWordNegativeBeforeWrite))
            {
                State.SetNegativeZero(value, size);
            }
            else if (destinationMode != 1 && !updateNegativeZeroBeforeWrite && !updateZeroBeforeWrite)
            {
                State.SetNegativeZero(value, size);
            }
            else if (destinationMode != 1 && preserveOverflowUntilSuccessfulWrite)
            {
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
            }

            AddInstructionCycles(EstimateMoveCycles(source.EaCycles, destinationMode, destinationRegister, size));
        }

        private static int GetMoveDestinationAddressErrorStackedProgramCounterOffset(
            M68kOperandSize size,
            int sourceMode,
            int destinationMode,
            int destinationRegister)
        {
            if (size == M68kOperandSize.Word && destinationMode is 2 or 3 or 5 or 6)
            {
                return 2;
            }

            if (size == M68kOperandSize.Word && destinationMode == 7 && destinationRegister == 1)
            {
                return sourceMode is 0 or 1 ? 0 : -2;
            }

            if (size != M68kOperandSize.Long)
            {
                return 0;
            }

            if (destinationMode is 2 or 3)
            {
                return 2;
            }

            return destinationMode == 7 && destinationRegister == 1 && sourceMode is not 0 and not 1
                ? -2
                : 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ExecuteMoveDisplacementToData(
            int sourceRegister,
            int destinationRegister,
            M68kOperandSize size)
        {
            var value = ReadDisplacementMemory(sourceRegister, size);
            WriteDataRegisterSized(destinationRegister, value, size);
            SetLogicFlags(value, size);
            AddInstructionCycles(EstimateMoveCycles(GetEaOperandCycles(5, sourceRegister, size), 0, destinationRegister, size));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool TryExecuteMoveFast(
            int sourceMode,
            int sourceRegister,
            int destinationMode,
            int destinationRegister,
            M68kOperandSize size)
        {
            if (sourceMode == 3)
            {
                if (destinationMode == 3)
                {
                    ExecuteMovePostincrementToPostincrement(sourceRegister, destinationRegister, size);
                    return true;
                }

                if (destinationMode == 0)
                {
                    ExecuteMovePostincrementToData(sourceRegister, destinationRegister, size);
                    return true;
                }
            }
            else if (sourceMode == 0 && destinationMode == 3)
            {
                ExecuteMoveDataToPostincrement(sourceRegister, destinationRegister, size);
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecuteMovePostincrementToPostincrement(
            int sourceRegister,
            int destinationRegister,
            M68kOperandSize size)
        {
            var value = ReadPostincrement(sourceRegister, size);
            if (size == M68kOperandSize.Word)
            {
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
                State.SetNegativeZero(value, size);
            }
            else if (size == M68kOperandSize.Long)
            {
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
                State.SetFlag(M68kCpuState.Negative, (value & 0x8000) != 0);
                State.SetFlag(M68kCpuState.Zero, false);
            }
            else
            {
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
            }

            AddMovePostincrementWriteFaultFloor(GetEaOperandCycles(3, sourceRegister, size), destinationRegister, size);
            WritePostincrement(destinationRegister, value, size);
            State.SetNegativeZero(value, size);
            AddInstructionCycles(EstimateMoveCycles(GetEaOperandCycles(3, sourceRegister, size), 3, destinationRegister, size));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecuteMovePostincrementToData(
            int sourceRegister,
            int destinationRegister,
            M68kOperandSize size)
        {
            var value = ReadPostincrement(sourceRegister, size);
            WriteDataRegisterSized(destinationRegister, value, size);
            SetLogicFlags(value, size);
            AddInstructionCycles(EstimateMoveCycles(GetEaOperandCycles(3, sourceRegister, size), 0, destinationRegister, size));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecuteMoveDataToPostincrement(
            int sourceRegister,
            int destinationRegister,
            M68kOperandSize size)
        {
            var value = ReadDataRegisterSized(sourceRegister, size);
            if (size == M68kOperandSize.Word)
            {
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
                State.SetNegativeZero(value, size);
                AddMovePostincrementWriteFaultFloor(0, destinationRegister, size);
                WritePostincrement(destinationRegister, value, size);
            }
            else if (size == M68kOperandSize.Long)
            {
                WritePostincrement(destinationRegister, value, size);
                SetLogicFlags(value, size);
            }
            else
            {
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
                WritePostincrement(destinationRegister, value, size);
                State.SetNegativeZero(value, size);
            }

            AddInstructionCycles(size == M68kOperandSize.Long ? 12 : 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint ReadPostincrement(int register, M68kOperandSize size)
        {
            return size switch
            {
                M68kOperandSize.Byte => ReadPostincrementByte(register),
                M68kOperandSize.Word => ReadPostincrementWord(register),
                _ => ReadPostincrementLong(register)
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint ReadPostincrementByte(int register)
        {
            var address = State.A[register];
            _dataAccessStackedProgramCounter = State.ProgramCounter;
            _dataReadFaultAccessKind = M68kBusAccessKind.CpuDataRead;
            var value = ReadByte(address);
            SetAddressRegister(register, unchecked(address + AddressIncrement(register, M68kOperandSize.Byte)));
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint ReadPostincrementWord(int register)
        {
            var address = State.A[register];
            _dataAccessStackedProgramCounter = State.ProgramCounter;
            _dataReadFaultAccessKind = M68kBusAccessKind.CpuDataRead;
            if ((address & 1) != 0)
            {
                AddInstructionCycles(8);
            }
            else if (_instructionCycleFloor < _instructionCycleStart + 4)
            {
                AddInstructionCyclesFromBase(_instructionCycleFloor, 4);
            }

            SetAddressRegister(register, unchecked(address + 2));
            return ReadWord(address);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint ReadPostincrementLong(int register)
        {
            var address = State.A[register];
            _dataAccessStackedProgramCounter = State.ProgramCounter;
            _dataReadFaultAccessKind = M68kBusAccessKind.CpuDataRead;
            if ((address & 1) != 0)
            {
                AddInstructionCycles(8);
            }

            var value = ReadLong(address);
            SetAddressRegister(register, unchecked(address + 4));
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WritePostincrement(int register, uint value, M68kOperandSize size)
        {
            if (size == M68kOperandSize.Byte)
            {
                WritePostincrementByte(register, value);
            }
            else if (size == M68kOperandSize.Word)
            {
                WritePostincrementWord(register, value);
            }
            else
            {
                WritePostincrementLong(register, value);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddMovePostincrementWriteFaultFloor(int sourceEaCycles, int destinationRegister, M68kOperandSize size)
        {
            if (size == M68kOperandSize.Word && (State.A[destinationRegister] & 1) != 0)
            {
                AddInstructionCycles(EstimateMoveCycles(sourceEaCycles, 3, destinationRegister, size));
            }
            else if (size == M68kOperandSize.Long && (State.A[destinationRegister] & 1) != 0)
            {
                AddInstructionCycles(EstimateMoveCycles(sourceEaCycles, 3, destinationRegister, size) - 4);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WritePostincrementByte(int register, uint value)
        {
            var address = State.A[register];
            _dataAccessStackedProgramCounter = State.ProgramCounter;
            WriteByte(address, (byte)value);
            SetAddressRegister(register, unchecked(address + AddressIncrement(register, M68kOperandSize.Byte)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WritePostincrementWord(int register, uint value)
        {
            var address = State.A[register];
            _dataAccessStackedProgramCounter = State.ProgramCounter + 2;
            WriteWord(address, (ushort)value);
            SetAddressRegister(register, unchecked(address + 2));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WritePostincrementLong(int register, uint value)
        {
            var address = State.A[register];
            _dataAccessStackedProgramCounter = State.ProgramCounter + 2;
            WriteLong(address, value);
            SetAddressRegister(register, unchecked(address + 4));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint ReadDataRegisterSized(int register, M68kOperandSize size)
            => State.D[register] & M68kCpuState.Mask(size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteDataRegisterSized(int register, uint value, M68kOperandSize size)
            => WriteDataRegister(register, value, size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint ReadDisplacementMemory(int register, M68kOperandSize size)
        {
            var extensionAddress = State.ProgramCounter;
            var displacement = unchecked((short)FetchWord());
            var address = unchecked((uint)(State.A[register] + displacement));
            _dataAccessStackedProgramCounter = extensionAddress;
            _dataReadFaultAccessKind = M68kBusAccessKind.CpuDataRead;
            if (size == M68kOperandSize.Word && (address & 1) != 0)
            {
                AddInstructionCycles(12);
            }
            else if (size == M68kOperandSize.Long && (address & 1) != 0)
            {
                AddInstructionCycles(12);
            }

            return ReadMemorySized(address, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint ReadMemorySized(uint address, M68kOperandSize size)
        {
            return size switch
            {
                M68kOperandSize.Byte => ReadByte(address),
                M68kOperandSize.Word => ReadWord(address),
                _ => ReadLong(address)
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePlannedMoveLongPostincrementToData(ushort opcode)
        {
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var sourceAddress = State.A[sourceRegister];
            _dataAccessStackedProgramCounter = State.ProgramCounter;
            _dataReadFaultAccessKind = M68kBusAccessKind.CpuDataRead;
            if ((sourceAddress & 1) != 0)
            {
                AddInstructionCycles(8);
            }

            var value = ReadLong(sourceAddress);
            SetAddressRegister(sourceRegister, unchecked(sourceAddress + 4));
            State.D[destinationRegister] = value;
            State.SetNegativeZeroLong(value);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            AddInstructionCycles(12);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePlannedMoveLongDataToPostincrement(ushort opcode)
        {
            var sourceRegister = opcode & 7;
            var destinationRegister = (opcode >> 9) & 7;
            var destinationAddress = State.A[destinationRegister];
            var value = State.D[sourceRegister];
            _dataAccessStackedProgramCounter = State.ProgramCounter + 2;
            if ((destinationAddress & 1) != 0)
            {
                AddInstructionCycles(8);
            }

            WriteLong(destinationAddress, value);
            SetAddressRegister(destinationRegister, unchecked(destinationAddress + 4));
            State.SetNegativeZeroLong(value);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            AddInstructionCycles(12);
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
            AddInstructionCycles(GetImmediateFetchCycles(size));
            var isCompare = (opcode & 0xFF00) == 0x0C00;
            var writesEffectiveAddress = !isCompare;
            var addressErrorStackedProgramCounterOffset = writesEffectiveAddress &&
                size == M68kOperandSize.Long &&
                mode is 4 or 5 or 6
                    ? -2
                    : 0;
            var destinationEa = ResolvePlannedEa(
                mode,
                register,
                size,
                write: writesEffectiveAddress,
                completeWordPostIncrementBeforeRead: size == M68kOperandSize.Word,
                addressErrorStackedProgramCounterOffset: addressErrorStackedProgramCounterOffset);
            if (writesEffectiveAddress && size == M68kOperandSize.Long && mode == 4)
            {
                SetAddressRegister(register, destinationEa.Address);
            }

            if (size != M68kOperandSize.Long && !destinationEa.IsRegister)
            {
                AddInstructionCyclesFromBase(_instructionCycleFloor, 4);
            }

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

            AddInstructionCycles(isCompare
                ? GetCmpiCycles(size, mode, register)
                : GetImmediateAluCycles(mode, register, size));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePlannedAndiByteDataRegister(ushort opcode)
        {
            var register = opcode & 7;
            var result = (State.D[register] & 0xFF) & FetchImmediate(M68kOperandSize.Byte);
            WriteDataRegister(register, result, M68kOperandSize.Byte);
            SetLogicFlags(result, M68kOperandSize.Byte);
            AddInstructionCycles(8);
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
            AddInstructionCycles(GetImmediateBtstCycles(mode, register));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePlannedBtstImmediateAddressIndirect(ushort opcode)
        {
            var register = opcode & 7;
            var bit = FetchWord() & 7;
            var value = ReadByte(State.A[register]);
            State.SetFlag(M68kCpuState.Zero, (value & (1u << (int)bit)) == 0);
            AddInstructionCycles(GetImmediateBtstCycles(2, register));
        }

        private void ExecutePlannedRegisterArithmetic(ushort opcode)
        {
            var line = opcode >> 12;
            var opmode = (opcode >> 6) & 7;
            var register = (opcode >> 9) & 7;
            var sourceRegister = opcode & 7;
            var sourceMode = (opcode >> 3) & 7;
            var size = DecodePlannedRegisterArithmeticSize(opmode);
            if (sourceMode != 0)
            {
                ExecuteRegisterArithmeticEaToData(line, opmode, sourceMode, sourceRegister, register, size);
                return;
            }

            ExecuteDataRegisterArithmetic(line, opmode, sourceRegister, register, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecuteDataRegisterArithmetic(
            int line,
            int opmode,
            int sourceRegister,
            int register,
            M68kOperandSize size)
        {
            var registerToEa = opmode >= 4;
            if (line == 0xB)
            {
                registerToEa = true;
            }

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

            AddInstructionCycles(line == 0xB && opmode < 4
                ? GetCompareCycles(0, sourceRegister, size)
                : registerToEa
                    ? GetAluDataToEaCycles(0, sourceRegister, size)
                    : GetAluEaToDataCycles(0, sourceRegister, size));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ExecuteRegisterArithmeticEaToData(
            int line,
            int opmode,
            int sourceMode,
            int sourceRegister,
            int register,
            M68kOperandSize size)
        {
            var eaValue = ReadRegisterArithmeticSourceEa(sourceMode, sourceRegister, size);
            var regValue = State.D[register] & M68kCpuState.Mask(size);
            uint result;
            switch (line)
            {
                case 0x8:
                    result = regValue | eaValue;
                    WriteDataRegister(register, result, size);
                    SetLogicFlags(result, size);
                    break;
                case 0x9:
                    result = Subtract(regValue, eaValue, size, setExtend: true);
                    WriteDataRegister(register, result, size);
                    break;
                case 0xB:
                    _ = Subtract(regValue, eaValue, size, setExtend: false, storeResult: false);
                    break;
                case 0xC:
                    result = regValue & eaValue;
                    WriteDataRegister(register, result, size);
                    SetLogicFlags(result, size);
                    break;
                default:
                    result = Add(regValue, eaValue, size, setExtend: true);
                    WriteDataRegister(register, result, size);
                    break;
            }

            AddInstructionCycles(line == 0xB && opmode < 4
                ? GetCompareCycles(sourceMode, sourceRegister, size)
                : GetAluEaToDataCycles(sourceMode, sourceRegister, size));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint ReadRegisterArithmeticSourceEa(int sourceMode, int sourceRegister, M68kOperandSize size)
        {
            if (sourceMode == 1)
            {
                return State.A[sourceRegister] & M68kCpuState.Mask(size);
            }

            if (size != M68kOperandSize.Long)
            {
                AddInstructionCyclesFromBase(_instructionCycleFloor, 4);
            }

            AddInstructionCyclesFromBase(_instructionCycleFloor, GetEaOperandCycles(sourceMode, sourceRegister, size));
            if (sourceMode == 2)
            {
                var address = State.A[sourceRegister];
                _dataAccessStackedProgramCounter = State.ProgramCounter;
                _dataReadFaultAccessKind = M68kBusAccessKind.CpuDataRead;
                return ReadMemorySized(address, size);
            }

            if (sourceMode == 3)
            {
                return ReadPostincrement(sourceRegister, size);
            }

            return ReadDisplacementMemory(sourceRegister, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePlannedOrLongToDataRegister(ushort opcode)
        {
            var register = (opcode >> 9) & 7;
            var result = State.D[opcode & 7] | State.D[register];
            State.D[register] = result;
            SetLogicFlags(result, M68kOperandSize.Long);
            AddInstructionCycles(8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePlannedEorLongToDataRegister(ushort opcode)
        {
            var sourceRegister = opcode & 7;
            var result = State.D[sourceRegister] ^ State.D[(opcode >> 9) & 7];
            State.D[sourceRegister] = result;
            SetLogicFlags(result, M68kOperandSize.Long);
            AddInstructionCycles(8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePlannedAndLongToDataRegister(ushort opcode)
        {
            var register = (opcode >> 9) & 7;
            var result = State.D[opcode & 7] & State.D[register];
            State.D[register] = result;
            SetLogicFlags(result, M68kOperandSize.Long);
            AddInstructionCycles(8);
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
            AddInstructionCycles(8);
        }

        private void ExecutePackedMoveq(in M68kPackedOpcodePlan plan)
        {
            State.D[plan.Register] = unchecked((uint)(int)plan.Displacement);
            State.SetNegativeZeroLong(State.D[plan.Register]);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            AddInstructionCycles(4);
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

                AddInstructionCycles(10);
                BranchTo(target, branchBase);
                return;
            }

            AddInstructionCycles(plan.ExtensionDisplacement ? 12 : 8);
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
                if (counter != 0xFFFF)
                {
                    var target = unchecked((uint)(branchBase + displacement));
                    if (_instructionFrequency.Enabled)
                    {
                        _instructionFrequency.RecordTakenBranch(State.LastInstructionProgramCounter, State.LastOpcode, target, 4);
                    }

                    AddInstructionCycles(10);
                    BranchTo(target, State.ProgramCounter);
                    State.D[register] = (State.D[register] & 0xFFFF_0000) | counter;
                    return;
                }

                State.D[register] = (State.D[register] & 0xFFFF_0000) | counter;
                AddInstructionCycles(14);
                return;
            }

            AddInstructionCycles(12);
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
                AddInstructionCycles(8);
                return;
            }

            var old = State.D[register] & M68kCpuState.Mask(plan.Size);
            var result = plan.Variant != 0
                ? Subtract(old, count, plan.Size, setExtend: true)
                : Add(old, count, plan.Size, setExtend: true);
            WriteDataRegister(register, result, plan.Size);
            AddInstructionCycles(plan.Size == M68kOperandSize.Long ? 8 : 4);
        }

        private void ExecutePackedMove(in M68kPackedOpcodePlan plan)
        {
            if ((plan.SourceMode == 3 || plan.DestinationMode == 3) &&
                TryExecuteMoveFast(
                    plan.SourceMode,
                    plan.SourceRegister,
                    plan.DestinationMode,
                    plan.DestinationRegister,
                    plan.Size))
            {
                return;
            }

            if (plan.SourceMode == 5 && plan.DestinationMode == 0)
            {
                ExecuteMoveDisplacementToData(plan.SourceRegister, plan.DestinationRegister, plan.Size);
                return;
            }

            var source = ResolvePlannedEa(
                plan.SourceMode,
                plan.SourceRegister,
                plan.Size,
                completeWordPostIncrementBeforeRead: plan.Size == M68kOperandSize.Word);
            if (plan.Size == M68kOperandSize.Word && !source.IsRegister)
            {
                AddInstructionCyclesFromBase(_instructionCycleFloor, 4);
            }

            var value = ReadPlannedEaValue(in source);
            var updateNegativeZeroBeforeWrite =
                (plan.Size == M68kOperandSize.Word && plan.DestinationMode is 2 or 3 or 4 or 5 or 6 or 7) ||
                (plan.Size == M68kOperandSize.Long &&
                ((plan.SourceMode == 4 && plan.DestinationMode is not 2 and not 3) ||
                    (plan.SourceMode == 3 && plan.DestinationMode == 6) ||
                    plan.DestinationMode == 4 || plan.DestinationMode == 6 ||
                    (plan.SourceMode == 2 && plan.DestinationMode == 5) ||
                    (plan.SourceMode == 3 && plan.DestinationMode == 5) ||
                    (plan.SourceMode == 5 && plan.DestinationMode == 5) ||
                    (plan.SourceMode == 6 && plan.DestinationMode == 5) ||
                    (plan.SourceMode == 7 && plan.SourceRegister == 4 && plan.DestinationMode == 5) ||
                    (plan.SourceMode == 7 && plan.SourceRegister == 3 && plan.DestinationMode == 5) ||
                    (plan.SourceMode == 7 && plan.SourceRegister == 1 && plan.DestinationMode == 5) ||
                    (plan.SourceMode == 0 && plan.DestinationMode == 5) ||
                    (plan.SourceMode == 1 && plan.DestinationMode == 5) ||
                    (plan.DestinationMode == 7 && plan.DestinationRegister <= 1 &&
                        (plan.SourceMode != 3 || plan.DestinationRegister == 0))));
            var updateZeroBeforeWrite = plan.Size == M68kOperandSize.Long &&
                ((plan.SourceMode == 5 && plan.SourceRegister == 7) ||
                    (plan.SourceMode == 4 && plan.DestinationMode == 2 &&
                        !(plan.DestinationRegister <= 1 ||
                            (plan.DestinationRegister == 6 && plan.SourceRegister == 0))) ||
                    (plan.SourceMode == 7 && plan.SourceRegister == 1 && plan.DestinationMode == 2));
            var updateLowWordNegativeBeforeWrite = plan.Size == M68kOperandSize.Long &&
                ((plan.SourceMode == 6 && plan.DestinationMode is 2 or 3) ||
                    (plan.SourceMode == 2 && (plan.DestinationMode == 3 ||
                        (plan.DestinationMode == 2 && plan.DestinationRegister == 2) ||
                        (plan.SourceRegister == 7 && plan.DestinationMode == 2))) ||
                    (plan.SourceMode == 3 && (plan.DestinationMode is 2 or 3 ||
                        (plan.DestinationMode == 7 && plan.DestinationRegister == 1))) ||
                    (plan.SourceMode == 4 && plan.DestinationMode == 3) ||
                    (plan.SourceMode == 5 && plan.DestinationMode is 2 or 3) ||
                    (plan.SourceMode == 7 && plan.SourceRegister == 0 && plan.DestinationMode == 3) ||
                    (plan.SourceMode == 7 && plan.SourceRegister == 2 && plan.DestinationMode == 2 && plan.DestinationRegister == 6) ||
                    (plan.SourceMode == 7 && plan.SourceRegister == 2 && plan.DestinationMode is 3 or 5));
            var clearNegativeZeroBeforeWrite = plan.Size == M68kOperandSize.Long &&
                plan.DestinationMode == 2 &&
                ((plan.SourceMode == 4 &&
                        (plan.DestinationRegister <= 1 ||
                            (plan.DestinationRegister == 6 && plan.SourceRegister == 0))) ||
                    (plan.SourceMode == 2 && plan.SourceRegister != 7 && plan.DestinationRegister != 2) ||
                    (plan.SourceMode == 7 && plan.SourceRegister is 2 or 3 &&
                        !(plan.SourceRegister == 2 && plan.DestinationRegister == 6)));
            var preserveOverflowUntilSuccessfulWrite = plan.Size == M68kOperandSize.Long &&
                ((plan.DestinationMode == 6 && plan.SourceMode is 0 or 1) ||
                    (plan.SourceMode == 0 && plan.DestinationMode == 5) ||
                    (plan.SourceMode == 1 && plan.DestinationMode == 5) ||
                    (plan.SourceMode == 7 && plan.SourceRegister == 4 && plan.DestinationMode == 5));
            var preserveCarryUntilSuccessfulWrite = plan.Size == M68kOperandSize.Long &&
                ((plan.DestinationMode == 6 && plan.DestinationRegister != 7 && plan.SourceMode is 0 or 1) ||
                    (plan.SourceMode == 0 && plan.DestinationMode == 6) ||
                    (plan.SourceMode == 1 && plan.DestinationMode == 6) ||
                    (plan.SourceMode == 0 && plan.DestinationMode == 5) ||
                    (plan.SourceMode == 1 && plan.DestinationMode == 5));
            var deferConditionCodesUntilSuccessfulWrite = plan.Size == M68kOperandSize.Long &&
                ((plan.SourceMode == 7 && plan.SourceRegister == 4 && plan.DestinationMode is 2 or 3) ||
                    (plan.SourceMode is 0 or 1 && plan.DestinationMode is 2 or 3));
            var addressErrorStackedProgramCounterOffset = GetMoveDestinationAddressErrorStackedProgramCounterOffset(
                plan.Size,
                plan.SourceMode,
                plan.DestinationMode,
                plan.DestinationRegister);
            var destination = ResolvePlannedEa(
                plan.DestinationMode,
                plan.DestinationRegister,
                plan.Size,
                write: true,
                addressErrorStackedProgramCounterOffset: addressErrorStackedProgramCounterOffset);
            if (plan.DestinationMode != 1)
            {
                if (!preserveOverflowUntilSuccessfulWrite && !deferConditionCodesUntilSuccessfulWrite)
                {
                    State.SetFlag(M68kCpuState.Overflow, false);
                }

                if (!deferConditionCodesUntilSuccessfulWrite)
                {
                    if (!preserveCarryUntilSuccessfulWrite)
                    {
                        State.SetFlag(M68kCpuState.Carry, false);
                    }

                    if (updateNegativeZeroBeforeWrite)
                    {
                        State.SetNegativeZero(value, plan.Size);
                    }
                    else if (updateZeroBeforeWrite)
                    {
                        State.SetFlag(M68kCpuState.Zero, (value & M68kCpuState.Mask(plan.Size)) == 0);
                    }
                    else if (updateLowWordNegativeBeforeWrite)
                    {
                        State.SetFlag(M68kCpuState.Negative, (value & 0x8000) != 0);
                        State.SetFlag(M68kCpuState.Zero, false);
                    }
                    else if (clearNegativeZeroBeforeWrite)
                    {
                        State.SetFlag(M68kCpuState.Negative, false);
                        State.SetFlag(M68kCpuState.Zero, false);
                    }
                }
            }

            if (plan.Size == M68kOperandSize.Word && plan.DestinationMode == 4)
            {
                UsePrefetchedInstructionWordForAddressErrorFrame();
            }

            if (plan.Size == M68kOperandSize.Word &&
                plan.DestinationMode != 4 &&
                !destination.IsRegister &&
                (destination.Address & 1) != 0)
            {
                AddInstructionCycles(GetMoveWordDestinationWriteFaultCycles(
                    source.EaCycles,
                    plan.DestinationMode,
                    plan.DestinationRegister));
            }
            else if (plan.Size == M68kOperandSize.Word &&
                plan.DestinationMode == 4 &&
                !destination.IsRegister &&
                (destination.Address & 1) != 0)
            {
                AddInstructionCycles(
                    EstimateMoveCycles(source.EaCycles, plan.DestinationMode, plan.DestinationRegister, plan.Size) + 4);
            }
            else if (plan.Size == M68kOperandSize.Long && !destination.IsRegister && (destination.Address & 1) != 0)
            {
                var cycles = EstimateMoveCycles(
                    source.EaCycles,
                    plan.DestinationMode,
                    plan.DestinationRegister,
                    plan.Size);
                AddInstructionCycles(GetMoveLongDestinationWriteFaultCycles(
                    cycles,
                    plan.DestinationMode,
                    plan.DestinationRegister));
            }

            WritePlannedEaValue(in destination, value);
            if (plan.DestinationMode != 1 && deferConditionCodesUntilSuccessfulWrite)
            {
                State.SetNegativeZero(value, plan.Size);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
            }
            else if (plan.DestinationMode != 1 && (updateZeroBeforeWrite || updateLowWordNegativeBeforeWrite))
            {
                State.SetNegativeZero(value, plan.Size);
            }
            else if (plan.DestinationMode != 1 && !updateNegativeZeroBeforeWrite && !updateZeroBeforeWrite)
            {
                State.SetNegativeZero(value, plan.Size);
            }
            else if (plan.DestinationMode != 1 && preserveOverflowUntilSuccessfulWrite)
            {
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
            }

            AddInstructionCycles(EstimateMoveCycles(source.EaCycles, plan.DestinationMode, plan.DestinationRegister, plan.Size));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePackedMoveLongPostincrementToData(in M68kPackedOpcodePlan plan)
        {
            var sourceRegister = plan.SourceRegister;
            var sourceAddress = State.A[sourceRegister];
            _dataAccessStackedProgramCounter = State.ProgramCounter;
            _dataReadFaultAccessKind = M68kBusAccessKind.CpuDataRead;
            if ((sourceAddress & 1) != 0)
            {
                AddInstructionCycles(8);
            }

            var value = ReadLong(sourceAddress);
            SetAddressRegister(sourceRegister, unchecked(sourceAddress + 4));
            State.D[plan.DestinationRegister] = value;
            State.SetNegativeZeroLong(value);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            AddInstructionCycles(12);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePackedMoveLongDataToPostincrement(in M68kPackedOpcodePlan plan)
        {
            var destinationRegister = plan.DestinationRegister;
            var destinationAddress = State.A[destinationRegister];
            var value = State.D[plan.SourceRegister];
            _dataAccessStackedProgramCounter = State.ProgramCounter + 2;
            if ((destinationAddress & 1) != 0)
            {
                AddInstructionCycles(8);
            }

            WriteLong(destinationAddress, value);
            SetAddressRegister(destinationRegister, unchecked(destinationAddress + 4));
            State.SetNegativeZeroLong(value);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            AddInstructionCycles(12);
        }

        private void ExecutePackedImmediate(ushort opcode, in M68kPackedOpcodePlan plan)
        {
            if ((opcode & 0xFFF8) == 0x0200)
            {
                ExecutePlannedAndiByteDataRegister(opcode);
                return;
            }

            var immediate = FetchImmediate(plan.Size);
            AddInstructionCycles(GetImmediateFetchCycles(plan.Size));
            var destinationEa = ResolvePlannedEa(
                plan.DestinationMode,
                plan.DestinationRegister,
                plan.Size,
                write: plan.Variant != 5);
            if (plan.Size != M68kOperandSize.Long && !destinationEa.IsRegister)
            {
                AddInstructionCyclesFromBase(_instructionCycleFloor, 4);
            }

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

            AddInstructionCycles(plan.Variant == 5
                ? GetCmpiCycles(plan.Size, plan.DestinationMode, plan.DestinationRegister)
                : GetImmediateAluCycles(plan.DestinationMode, plan.DestinationRegister, plan.Size));
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
            AddInstructionCycles(GetImmediateBtstCycles(plan.DestinationMode, plan.DestinationRegister));
        }

        private void ExecutePackedRegisterArithmetic(in M68kPackedOpcodePlan plan)
        {
            var line = plan.Variant >> 4;
            var opmode = plan.Variant & 7;
            if (plan.SourceMode != 0)
            {
                ExecuteRegisterArithmeticEaToData(
                    line,
                    opmode,
                    plan.SourceMode,
                    plan.SourceRegister,
                    plan.Register,
                    plan.Size);
                return;
            }

            ExecuteDataRegisterArithmetic(line, opmode, plan.SourceRegister, plan.Register, plan.Size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePackedOrLongToDataRegister(in M68kPackedOpcodePlan plan)
        {
            var result = State.D[plan.SourceRegister] | State.D[plan.Register];
            State.D[plan.Register] = result;
            SetLogicFlags(result, M68kOperandSize.Long);
            AddInstructionCycles(8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePackedEorLongToDataRegister(in M68kPackedOpcodePlan plan)
        {
            var result = State.D[plan.SourceRegister] ^ State.D[plan.Register];
            State.D[plan.SourceRegister] = result;
            SetLogicFlags(result, M68kOperandSize.Long);
            AddInstructionCycles(8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePackedAndLongToDataRegister(in M68kPackedOpcodePlan plan)
        {
            var result = State.D[plan.SourceRegister] & State.D[plan.Register];
            State.D[plan.Register] = result;
            SetLogicFlags(result, M68kOperandSize.Long);
            AddInstructionCycles(8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecutePackedAddLongToDataRegister(in M68kPackedOpcodePlan plan)
        {
            State.D[plan.Register] = Add(
                State.D[plan.Register],
                State.D[plan.SourceRegister],
                M68kOperandSize.Long,
                setExtend: true);
            AddInstructionCycles(8);
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

        private PlannedEaOperand ResolvePlannedEa(
            int mode,
            int register,
            M68kOperandSize size,
            bool write = false,
            bool completeWordPostIncrementBeforeRead = false,
            int addressErrorStackedProgramCounterOffset = 0)
        {
            switch (mode)
            {
                case 0:
                    return PlannedEaOperand.DataRegister(register, size);
                case 1:
                    return PlannedEaOperand.AddressRegister(register, size);
                case 2:
                    return PlannedEaOperand.Memory(
                        State.A[register],
                        size,
                        GetEaOperandCycles(mode, register, size),
                        unchecked((uint)(State.ProgramCounter + addressErrorStackedProgramCounterOffset)));
                case 3:
                {
                    var address = State.A[register];
                    return PlannedEaOperand.Memory(
                        address,
                        size,
                        GetEaOperandCycles(mode, register, size),
                        unchecked((uint)(State.ProgramCounter + addressErrorStackedProgramCounterOffset)),
                        register,
                        AddressIncrement(register, size),
                        completePostIncrementOnRead: !write,
                        completePostIncrementBeforeRead: completeWordPostIncrementBeforeRead && size == M68kOperandSize.Word);
                }
                case 4:
                {
                    var predecrementAddress = State.A[register] - AddressIncrement(register, size);
                    if (!(write && size == M68kOperandSize.Long))
                    {
                        SetAddressRegister(register, predecrementAddress);
                    }

                    return PlannedEaOperand.Memory(
                        predecrementAddress,
                        size,
                        GetEaOperandCycles(mode, register, size),
                        unchecked((uint)(GetPredecrementStackedProgramCounter(size) +
                            (write && size == M68kOperandSize.Long ? 2u : 0u) +
                            addressErrorStackedProgramCounterOffset)),
                        descendingLongWrite: true,
                        delayedPredecrementRegister: write && size == M68kOperandSize.Long ? register : -1,
                        delayedPredecrementValue: predecrementAddress);
                }
                case 5:
                {
                    var extensionAddress = State.ProgramCounter;
                    var displacement = unchecked((short)FetchWord());
                    return PlannedEaOperand.Memory(
                        unchecked((uint)(State.A[register] + displacement)),
                        size,
                        GetEaOperandCycles(mode, register, size),
                        unchecked((uint)(extensionAddress +
                            (write && size == M68kOperandSize.Long ? 2u : 0u) +
                            addressErrorStackedProgramCounterOffset)));
                }
                case 6:
                {
                    var extensionAddress = State.ProgramCounter;
                    var extension = FetchWord();
                    return PlannedEaOperand.Memory(
                        M68kIntegerSemantics.CalculateM68000BriefIndexedAddress(State.A[register], extension, State.D, State.A),
                        size,
                        GetEaOperandCycles(mode, register, size),
                        unchecked((uint)(extensionAddress +
                            (write && size == M68kOperandSize.Long ? 2u : 0u) +
                            addressErrorStackedProgramCounterOffset)));
                }
                case 7:
                    return ResolvePlannedMode7(register, size, write, addressErrorStackedProgramCounterOffset);
                default:
                    throw new InvalidOperationException("Invalid planned effective address mode.");
            }
        }

        private PlannedEaOperand ResolvePlannedMode7(
            int register,
            M68kOperandSize size,
            bool write = false,
            int addressErrorStackedProgramCounterOffset = 0)
        {
            switch (register)
            {
                case 0:
                    return PlannedEaOperand.Memory(unchecked((uint)(short)FetchWord()), size, GetEaOperandCycles(7, register, size), State.ProgramCounter);
                case 1:
                {
                    var extensionAddress = State.ProgramCounter;
                    var address = FetchLong();
                    return PlannedEaOperand.Memory(
                        address,
                        size,
                        GetEaOperandCycles(7, register, size),
                        unchecked((uint)(State.ProgramCounter + addressErrorStackedProgramCounterOffset)));
                }
                case 2:
                {
                    var extensionAddress = State.ProgramCounter;
                    var displacement = unchecked((short)FetchWord());
                    return PlannedEaOperand.Memory(
                        unchecked((uint)(extensionAddress + displacement)),
                        size,
                        GetEaOperandCycles(7, register, size),
                        extensionAddress,
                        readFaultAccessKind: M68kBusAccessKind.CpuInstructionFetch);
                }
                case 3:
                {
                    var extensionAddress = State.ProgramCounter;
                    var extension = FetchWord();
                    return PlannedEaOperand.Memory(
                        M68kIntegerSemantics.CalculateM68000BriefIndexedAddress(extensionAddress, extension, State.D, State.A),
                        size,
                        GetEaOperandCycles(7, register, size),
                        extensionAddress,
                        readFaultAccessKind: M68kBusAccessKind.CpuInstructionFetch);
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
                2 => ReadPlannedMemoryEaValue(in operand),
                3 => operand.Immediate & M68kCpuState.Mask(operand.Size),
                _ => 0
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint ReadPlannedMemoryEaValue(in PlannedEaOperand operand)
        {
            _dataAccessStackedProgramCounter = operand.AddressErrorStackedProgramCounter;
            _dataReadFaultAccessKind = operand.ReadFaultAccessKind;
            if (operand.EaCycles > 0)
            {
                AddInstructionCyclesFromBase(_instructionCycleFloor, operand.EaCycles);
            }

            if (operand.CompletePostIncrementBeforeRead)
            {
                CompletePlannedMemoryEaAccess(in operand);
            }

            var value = operand.Size switch
            {
                M68kOperandSize.Byte => ReadByte(operand.Address),
                M68kOperandSize.Word => ReadWord(operand.Address),
                _ => ReadLong(operand.Address)
            };
            if (operand.CompletePostIncrementOnRead && !operand.CompletePostIncrementBeforeRead)
            {
                CompletePlannedMemoryEaAccess(in operand);
            }

            return value;
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
                    _dataAccessStackedProgramCounter = operand.AddressErrorStackedProgramCounter;
                    if (operand.EaCycles > 0)
                    {
                        AddInstructionCycles(operand.EaCycles);
                    }

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
                        if (operand.DescendingLongWrite)
                        {
                            WriteLongDescending(operand.Address, value);
                        }
                        else
                        {
                            WriteLong(operand.Address, value);
                        }
                    }

                    if (!operand.CompletePostIncrementBeforeRead)
                    {
                        CompletePlannedMemoryEaAccess(in operand);
                    }

                    if (operand.DelayedPredecrementRegister >= 0)
                    {
                        SetAddressRegister(operand.DelayedPredecrementRegister, operand.DelayedPredecrementValue);
                    }

                    return;
                default:
                    throw new M68kEmulationException("Cannot write to an immediate MC68000 operand.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CompletePlannedMemoryEaAccess(in PlannedEaOperand operand)
        {
            if (operand.PostIncrementRegister >= 0)
            {
                SetAddressRegister(
                    operand.PostIncrementRegister,
                    State.A[operand.PostIncrementRegister] + operand.PostIncrement);
            }
        }

        private static int EstimateMoveCycles(int sourceEaCycles, int destinationMode, int destinationRegister, M68kOperandSize size)
        {
            if (destinationMode is 0 or 1)
            {
                return 4 + sourceEaCycles;
            }

            var baseCycles = size == M68kOperandSize.Long ? 8 : 4;
            return baseCycles + sourceEaCycles + GetMoveDestinationEaCycles(destinationMode, destinationRegister);
        }

        private static int GetMoveDestinationEaCycles(int mode, int reg)
            => mode switch
            {
                2 or 3 or 4 => 4,
                5 => 8,
                6 => 10,
                7 => reg switch
                {
                    0 => 8,
                    1 => 12,
                    _ => throw new InvalidOperationException("Invalid MOVE destination effective-address timing mode.")
                },
                _ => throw new InvalidOperationException("Invalid MOVE destination effective-address timing mode.")
            };

        private static int GetMoveWordDestinationWriteFaultCycles(int sourceEaCycles, int destinationMode, int destinationRegister)
        {
            if (destinationMode == 7 && destinationRegister == 1)
            {
                return Math.Max(16, sourceEaCycles + 12);
            }

            return EstimateMoveCycles(sourceEaCycles, destinationMode, destinationRegister, M68kOperandSize.Word);
        }

        private static int GetMoveLongDestinationWriteFaultCycles(int successfulCycles, int destinationMode, int destinationRegister)
        {
            if (destinationMode == 4)
            {
                return successfulCycles;
            }

            if (destinationMode == 7 && destinationRegister == 1)
            {
                return successfulCycles - 8;
            }

            return successfulCycles - 4;
        }

        private static int GetMoveFromSrCycles(int mode, int reg)
            => mode switch
            {
                0 => 6,
                2 or 3 => 12,
                4 => 14,
                5 => 16,
                6 => 18,
                7 => reg switch
                {
                    0 => 16,
                    1 => 20,
                    _ => throw new InvalidOperationException("Invalid MOVE from SR destination timing mode.")
                },
                _ => throw new InvalidOperationException("Invalid MOVE from SR destination timing mode.")
            };

        private static int GetMoveFromSrWriteFaultCycles(int mode, int reg)
            => mode switch
            {
                2 => 8,
                4 => 10,
                3 => 8,
                5 => 12,
                6 => 14,
                7 => reg switch
                {
                    0 => 12,
                    1 => 16,
                    _ => throw new InvalidOperationException("Invalid MOVE from SR destination timing mode.")
                },
                _ => throw new InvalidOperationException("Invalid MOVE from SR destination timing mode.")
            };

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
            State.StatusRegister = (ushort)((savedStatusRegister & ~M68kCpuState.Trace & 0xF8FF) |
                ((level & 7) << 8) |
                M68kCpuState.Supervisor);
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

            var src = ResolveEa(
                (opcode >> 3) & 7,
                opcode & 7,
                size,
                completeWordPostIncrementBeforeRead: size == M68kOperandSize.Word);
            if (size == M68kOperandSize.Word && !src.IsRegister)
            {
                AddInstructionCyclesFromBase(_instructionCycleFloor, 4);
            }

            var value = src.Read();
            var srcMode = (opcode >> 3) & 7;
            var srcReg = opcode & 7;
            var destMode = (opcode >> 6) & 7;
            var destReg = (opcode >> 9) & 7;
            var updateNegativeZeroBeforeWrite =
                (size == M68kOperandSize.Word && destMode is 2 or 3 or 4 or 5 or 6 or 7) ||
                (size == M68kOperandSize.Long &&
                ((srcMode == 4 && destMode is not 2 and not 3) ||
                    (srcMode == 3 && destMode == 6) ||
                    destMode == 4 || destMode == 6 ||
                    (srcMode == 2 && destMode == 5) ||
                    (srcMode == 3 && destMode == 5) ||
                    (srcMode == 5 && destMode == 5) ||
                    (srcMode == 6 && destMode == 5) ||
                    (srcMode == 7 && srcReg == 4 && destMode == 5) ||
                    (srcMode == 7 && srcReg == 3 && destMode == 5) ||
                    (srcMode == 7 && srcReg == 1 && destMode == 5) ||
                    (srcMode == 0 && destMode == 5) ||
                    (srcMode == 1 && destMode == 5) ||
                    (destMode == 7 && destReg <= 1 && (srcMode != 3 || destReg == 0))));
            var updateZeroBeforeWrite = size == M68kOperandSize.Long &&
                ((srcMode == 5 && srcReg == 7) ||
                    (srcMode == 4 && destMode == 2 &&
                        !(destReg <= 1 || (destReg == 6 && srcReg == 0))) ||
                    (srcMode == 7 && srcReg == 1 && destMode == 2));
            var updateLowWordNegativeBeforeWrite = size == M68kOperandSize.Long &&
                ((srcMode == 6 && destMode is 2 or 3) ||
                    (srcMode == 2 && (destMode == 3 ||
                        (destMode == 2 && destReg == 2) ||
                        (srcReg == 7 && destMode == 2))) ||
                    (srcMode == 3 && (destMode is 2 or 3 || (destMode == 7 && destReg == 1))) ||
                    (srcMode == 4 && destMode == 3) ||
                    (srcMode == 5 && destMode is 2 or 3) ||
                    (srcMode == 7 && srcReg == 0 && destMode == 3) ||
                    (srcMode == 7 && srcReg == 2 && destMode == 2 && destReg == 6) ||
                    (srcMode == 7 && srcReg == 2 && destMode is 3 or 5));
            var clearNegativeZeroBeforeWrite = size == M68kOperandSize.Long &&
                destMode == 2 &&
                ((srcMode == 4 && (destReg <= 1 || (destReg == 6 && srcReg == 0))) ||
                    (srcMode == 2 && srcReg != 7 && destReg != 2) ||
                    (srcMode == 7 && srcReg is 2 or 3 && !(srcReg == 2 && destReg == 6)));
            var preserveOverflowUntilSuccessfulWrite = size == M68kOperandSize.Long &&
                ((destMode == 6 && srcMode is 0 or 1) ||
                    (srcMode == 0 && destMode == 5) ||
                    (srcMode == 1 && destMode == 5) ||
                    (srcMode == 7 && srcReg == 4 && destMode == 5));
            var preserveCarryUntilSuccessfulWrite = size == M68kOperandSize.Long &&
                ((destMode == 6 && destReg != 7 && srcMode is 0 or 1) ||
                    (srcMode == 0 && destMode == 6) ||
                    (srcMode == 1 && destMode == 6) ||
                    (srcMode == 0 && destMode == 5) ||
                    (srcMode == 1 && destMode == 5));
            var deferConditionCodesUntilSuccessfulWrite = size == M68kOperandSize.Long &&
                ((srcMode == 7 && srcReg == 4 && destMode is 2 or 3) ||
                    (srcMode is 0 or 1 && destMode is 2 or 3));
            var dest = ResolveEa(
                destMode,
                destReg,
                size,
                write: true,
                addressErrorStackedProgramCounterOffset: GetMoveDestinationAddressErrorStackedProgramCounterOffset(size, srcMode, destMode, destReg));
            // MOVEA does not alter the condition codes.
            if (destMode != 1)
            {
                if (!preserveOverflowUntilSuccessfulWrite && !deferConditionCodesUntilSuccessfulWrite)
                {
                    State.SetFlag(M68kCpuState.Overflow, false);
                }

                if (!deferConditionCodesUntilSuccessfulWrite)
                {
                    if (!preserveCarryUntilSuccessfulWrite)
                    {
                        State.SetFlag(M68kCpuState.Carry, false);
                    }

                    if (updateNegativeZeroBeforeWrite)
                    {
                        State.SetNegativeZero(value, size);
                    }
                    else if (updateZeroBeforeWrite)
                    {
                        State.SetFlag(M68kCpuState.Zero, (value & M68kCpuState.Mask(size)) == 0);
                    }
                    else if (updateLowWordNegativeBeforeWrite)
                    {
                        State.SetFlag(M68kCpuState.Negative, (value & 0x8000) != 0);
                        State.SetFlag(M68kCpuState.Zero, false);
                    }
                    else if (clearNegativeZeroBeforeWrite)
                    {
                        State.SetFlag(M68kCpuState.Negative, false);
                        State.SetFlag(M68kCpuState.Zero, false);
                    }
                }
            }

            if (size == M68kOperandSize.Word && destMode == 4)
            {
                UsePrefetchedInstructionWordForAddressErrorFrame();
            }

            if (size == M68kOperandSize.Word && destMode != 4 && !dest.IsRegister && (dest.Address & 1) != 0)
            {
                AddInstructionCycles(GetMoveWordDestinationWriteFaultCycles(src.EaCycles, destMode, destReg));
            }
            else if (size == M68kOperandSize.Word && destMode == 4 && !dest.IsRegister && (dest.Address & 1) != 0)
            {
                AddInstructionCycles(EstimateMoveCycles(src.EaCycles, destMode, destReg, size) + 4);
            }
            else if (size == M68kOperandSize.Long && !dest.IsRegister && (dest.Address & 1) != 0)
            {
                var cycles = EstimateMoveCycles(src.EaCycles, destMode, destReg, size);
                AddInstructionCycles(GetMoveLongDestinationWriteFaultCycles(cycles, destMode, destReg));
            }

            dest.Write(value);
            if (destMode != 1 && deferConditionCodesUntilSuccessfulWrite)
            {
                State.SetNegativeZero(value, size);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
            }
            else if (destMode != 1 && (updateZeroBeforeWrite || updateLowWordNegativeBeforeWrite))
            {
                State.SetNegativeZero(value, size);
            }
            else if (destMode != 1 && !updateNegativeZeroBeforeWrite && !updateZeroBeforeWrite)
            {
                State.SetNegativeZero(value, size);
            }
            else if (destMode != 1 && preserveOverflowUntilSuccessfulWrite)
            {
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
            }

            AddInstructionCycles(EstimateMoveCycles(src.EaCycles, destMode, destReg, size));
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
            State.SetNegativeZeroLong(State.D[reg]);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
            AddInstructionCycles(4);
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
                if ((target & 1) != 0)
                {
                    AddInstructionCycles(18);
                    PushLong(State.ProgramCounter);
                    BranchTo(target, target);
                    return true;
                }

                PushLong(State.ProgramCounter);
                SetProgramCounterAndFlushPrefetch(target);
                AddInstructionCycles(displacement == 0 ? 18 : 18);
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

                    AddInstructionCycles(displacement == 0 ? 10 : 10);
                    BranchTo(target, branchBase);
                }
                else
                {
                    _ = instructionPc;
                    AddInstructionCycles(displacement == 0 ? 12 : 8);
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

                    AddInstructionCycles(displacement == 0 ? 10 : 10);
                    BranchTo(target, branchBase);
                }
                else
                {
                    _ = instructionPc;
                    AddInstructionCycles(displacement == 0 ? 12 : 8);
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

                    AddInstructionCycles(displacement == 0 ? 10 : 10);
                    BranchTo(target, branchBase);
                }
                else
                {
                    _ = instructionPc;
                    AddInstructionCycles(displacement == 0 ? 12 : 8);
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

                AddInstructionCycles(displacement == 0 ? 10 : 10);
                BranchTo(target, branchBase);
            }
            else
            {
                _ = instructionPc;
                AddInstructionCycles(displacement == 0 ? 12 : 8);
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
                if (bitMode == 1 || (bitMode == 7 && bitReg == 4 && operation != 0))
                {
                    return false;
                }

                var bitSize = bitMode == 0 ? M68kOperandSize.Long : M68kOperandSize.Byte;
                if (bitMode == 7 && bitReg == 4)
                {
                    var immediateValue = FetchWord();
                    var immediateMask = 1u << (int)(bit & 7);
                    State.SetFlag(M68kCpuState.Zero, (immediateValue & immediateMask) == 0);
                    AddInstructionCycles(8);
                    return true;
                }

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

                    AddInstructionCycles(GetImmediateBitChangeCycles(operation, bitMode, bitReg, bit));
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

                AddInstructionCycles(operation == 0
                    ? GetImmediateBtstCycles(bitMode, bitReg)
                    : GetImmediateBitChangeCycles(operation, bitMode, bitReg, bit));
                return true;
            }

            if ((opcode & 0xF100) == 0x0100)
            {
                var bitRegister = (opcode >> 9) & 7;
                var operation = (opcode >> 6) & 3;
                var bitMode = (opcode >> 3) & 7;
                var bitReg = opcode & 7;
                if (bitMode == 1 || (bitMode == 7 && bitReg == 4 && operation != 0))
                {
                    return false;
                }

                var bitSize = bitMode == 0 ? M68kOperandSize.Long : M68kOperandSize.Byte;
                if (bitMode == 7 && bitReg == 4)
                {
                    var immediateValue = FetchWord();
                    var immediateBit = State.D[bitRegister] & 7u;
                    var immediateMask = 1u << (int)immediateBit;
                    State.SetFlag(M68kCpuState.Zero, (immediateValue & immediateMask) == 0);
                    AddInstructionCycles(10);
                    return true;
                }

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

                AddInstructionCycles(operation == 0
                    ? GetDynamicBtstCycles(bitMode, bitReg)
                    : GetDynamicBitChangeCycles(operation, bitMode, bitReg, bit));
                return true;
            }

            if (opcode == 0x0C39)
            {
                var compareImmediate = FetchWord() & 0xFFu;
                var compareAddress = FetchLong();
                var compareDestination = ReadByte(compareAddress);
                SetCompareByteFlags(compareDestination, compareImmediate);
                AddInstructionCycles(GetCmpiCycles(M68kOperandSize.Byte, 7, 1));
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
            AddInstructionCycles(GetImmediateFetchCycles(size));
            var mode = (opcode >> 3) & 7;
            var reg = opcode & 7;
            var writesEffectiveAddress = high != 0x0C00;
            var addressErrorStackedProgramCounterOffset = writesEffectiveAddress &&
                size == M68kOperandSize.Long &&
                mode is 4 or 5 or 6
                    ? -2
                    : 0;
            var ea = ResolveEa(
                mode,
                reg,
                size,
                write: writesEffectiveAddress,
                completeWordPostIncrementBeforeRead: size == M68kOperandSize.Word,
                addressErrorStackedProgramCounterOffset: addressErrorStackedProgramCounterOffset);
            if (writesEffectiveAddress && size == M68kOperandSize.Long && mode == 4)
            {
                SetAddressRegister(reg, ea.Address);
            }

            if (size != M68kOperandSize.Long && !ea.IsRegister)
            {
                AddInstructionCyclesFromBase(_instructionCycleFloor, 4);
            }

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

            AddInstructionCycles(high == 0x0C00
                ? GetCmpiCycles(size, mode, reg)
                : GetImmediateAluCycles(mode, reg, size));
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

            AddInstructionCycles(isLong ? 24 : 16);
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
                AddInstructionCycles(20);
                return true;
            }

            if (!State.GetFlag(M68kCpuState.Supervisor))
            {
                RaiseException(8, instructionPc, 34);
                return true;
            }

            State.StatusRegister = (ushort)result;
            AddInstructionCycles(20);
            return true;
        }

        private bool DecodeLine4(ushort opcode, uint instructionPc)
        {
            switch (opcode)
            {
                case 0x44FC:
                    SetCcr((ushort)(FetchWord() & 0x001F));
                    AddInstructionCycles(16);
                    return true;
                case 0x46FC:
                    if (!State.GetFlag(M68kCpuState.Supervisor))
                    {
                        RaiseException(8, instructionPc, 34);
                        return true;
                    }

                    State.StatusRegister = FetchWord();
                    AddInstructionCycles(16);
                    return true;
                case 0x4E70:
                    if (!State.GetFlag(M68kCpuState.Supervisor))
                    {
                        RaiseException(8, instructionPc, 34);
                        return true;
                    }

                    _bus.ResetExternalDevices(State.Cycles);
                    AddInstructionCycles(132);
                    return true;
                case 0x4E71:
                    AddInstructionCycles(4);
                    return true;
                case 0x4E72:
                    if (!State.GetFlag(M68kCpuState.Supervisor))
                    {
                        RaiseException(8, instructionPc, 34);
                        return true;
                    }

                    State.StatusRegister = FetchWord();
                    State.Stopped = true;
                    AddInstructionCycles(4);
                    return true;
                case 0x4E73:
                {
                    if (!State.GetFlag(M68kCpuState.Supervisor))
                    {
                        RaiseException(8, instructionPc, 34);
                        return true;
                    }

                    var statusRegister = PullWord();
                    var programCounter = PullLong();
                    State.StatusRegister = statusRegister;
                    AddInstructionCycles(20);
                    BranchTo(programCounter, State.ProgramCounter);
                    return true;
                }
                case 0x4E75:
                {
                    var programCounter = PullLong();
                    AddInstructionCycles(16);
                    BranchTo(programCounter, State.ProgramCounter);
                    return true;
                }
                case 0x4E76:
                    if (State.GetFlag(M68kCpuState.Overflow))
                    {
                        RaiseException(7, State.ProgramCounter, 34);
                    }
                    else
                    {
                        AddInstructionCycles(4);
                    }
                    return true;
                case 0x4E77:
                {
                    SetCcr(PullWord());
                    var programCounter = PullLong();
                    AddInstructionCycles(20);
                    BranchTo(programCounter, State.ProgramCounter);
                    return true;
                }
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

                AddInstructionCycles(4);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x40C0)
            {
                var mode = (opcode >> 3) & 7;
                var reg = opcode & 7;
                var ea = ResolveEa(mode, reg, M68kOperandSize.Word, write: true);
                if (mode == 3 && (ea.Address & 1) != 0)
                {
                    SetAddressRegister(reg, State.A[reg] + AddressIncrement(reg, M68kOperandSize.Word));
                }

                _addressErrorIsWriteOverride = false;
                if (!ea.IsRegister && (ea.Address & 1) != 0)
                {
                    AddInstructionCycles(GetMoveFromSrWriteFaultCycles(mode, reg));
                }

                ea.Write(State.StatusRegister);
                AddInstructionCycles(GetMoveFromSrCycles(mode, reg));
                return true;
            }

            if ((opcode & 0xFFC0) == 0x44C0)
            {
                var ea = ResolveEa(
                    (opcode >> 3) & 7,
                    opcode & 7,
                    M68kOperandSize.Word,
                    completeWordPostIncrementBeforeRead: true);
                if (!ea.IsRegister && (ea.Address & 1) != 0)
                {
                    AddInstructionCycles(4);
                }

                SetCcr((ushort)ea.Read());
                AddInstructionCycles(12 + (ea.IsRegister ? 0 : ea.EaCycles));
                return true;
            }

            if ((opcode & 0xFFC0) == 0x46C0)
            {
                if (!State.GetFlag(M68kCpuState.Supervisor))
                {
                    RaiseException(8, instructionPc, 34);
                    return true;
                }

                var ea = ResolveEa(
                    (opcode >> 3) & 7,
                    opcode & 7,
                    M68kOperandSize.Word,
                    completeWordPostIncrementBeforeRead: true);
                if (!ea.IsRegister && (ea.Address & 1) != 0)
                {
                    AddInstructionCycles(4);
                }

                State.StatusRegister = (ushort)ea.Read();
                AddInstructionCycles(12 + (ea.IsRegister ? 0 : ea.EaCycles));
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4E50)
            {
                var reg = opcode & 7;
                PushLong(State.A[reg]);
                var displacement = unchecked((short)FetchWord());
                SetAddressRegister(reg, State.A[7]);
                State.SetActiveStackPointer((uint)(State.A[7] + displacement));
                AddInstructionCycles(16);
                return true;
            }

            if ((opcode & 0xFFF0) == 0x4E40)
            {
                var vector = (uint)(32 + (opcode & 0x0F));
                var savedStatusRegister = State.StatusRegister;
                State.StatusRegister = (ushort)((savedStatusRegister | M68kCpuState.Supervisor) & ~M68kCpuState.Trace);
                PushLong(State.ProgramCounter);
                PushWord(savedStatusRegister);
                SetProgramCounterAndFlushPrefetch(ReadLong(vector * 4));
                AddInstructionCycles(34);
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4E58)
            {
                var reg = opcode & 7;
                if ((State.A[reg] & 1) != 0)
                {
                    AddInstructionCycles(8);
                    _dataAccessStackedProgramCounter = State.ProgramCounter + 2;
                    ThrowOddAddressAccess(
                        State.A[reg],
                        isWrite: false,
                        M68kBusAccessKind.CpuDataRead,
                        useDataAccessStackedProgramCounter: true);
                }

                State.SetActiveStackPointer(State.A[reg]);
                SetAddressRegister(reg, PullLong());
                AddInstructionCycles(12);
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
                State.SetNegativeZeroLong(State.D[reg]);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
                AddInstructionCycles(4);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x4840)
            {
                var mode = (opcode >> 3) & 7;
                var reg = opcode & 7;
                var ea = ResolveEa(mode, reg, M68kOperandSize.Long, addressOnly: true);
                PushLong(ea.Address);
                AddInstructionCycles(GetPeaCycles(mode, reg));
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
                AddInstructionCycles(4);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x48C0)
            {
                var reg = opcode & 7;
                var value = M68kCpuState.SignExtend(State.D[reg] & 0xFFFF, M68kOperandSize.Word);
                State.D[reg] = value;
                State.SetNegativeZeroLong(value);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
                AddInstructionCycles(4);
                return true;
            }

            if ((opcode & 0xF1C0) == 0x41C0)
            {
                var addressRegister = (opcode >> 9) & 7;
                var mode = (opcode >> 3) & 7;
                var register = opcode & 7;
                var ea = ResolveEa(mode, register, M68kOperandSize.Long, addressOnly: true);
                SetAddressRegister(addressRegister, ea.Address);
                AddInstructionCycles(GetLeaCycles(mode, register));
                return true;
            }

            if ((opcode & 0xFFF8) == 0x4E90)
            {
                var target = State.A[opcode & 7];
                AddInstructionCycles(GetJmpCycles(2, opcode & 7));
                JumpToSubroutine(target, State.ProgramCounter);
                AddInstructionCycles(16);
                return true;
            }

            if ((opcode & 0xFFC0) == 0x4E80)
            {
                var mode = (opcode >> 3) & 7;
                var reg = opcode & 7;
                var ea = ResolveEa(mode, reg, M68kOperandSize.Long, addressOnly: true);
                AddInstructionCycles(GetJmpCycles(mode, reg));
                JumpToSubroutine(ea.Address, State.ProgramCounter);
                AddInstructionCycles(GetJsrCycles(mode, reg));
                return true;
            }

            if ((opcode & 0xFFC0) == 0x4EC0)
            {
                var targetStackedProgramCounter = State.ProgramCounter;
                var mode = (opcode >> 3) & 7;
                var reg = opcode & 7;
                var ea = ResolveEa(mode, reg, M68kOperandSize.Long, addressOnly: true);
                AddInstructionCycles(GetJmpCycles(mode, reg));
                BranchTo(ea.Address, targetStackedProgramCounter);
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
                var result = M68kIntegerSemantics.SubtractBcdByte(
                    0,
                    (byte)ea.Read(),
                    extend,
                    out var carry,
                    out var overflow);
                ea.Write(result);
                SetBcdFlags(result, carry, overflow);
                AddInstructionCycles(ea.IsRegister ? 6 : 8 + ea.EaCycles);
                return true;
            }

            var unary = opcode & 0xFF00;
            if ((opcode & 0xFFC0) == 0x4AC0)
            {
                var mode = (opcode >> 3) & 7;
                var reg = opcode & 7;
                var ea = ResolveEa(mode, reg, M68kOperandSize.Byte, write: true);
                var value = ea.Read();
                State.SetNegativeZero(value, M68kOperandSize.Byte);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
                if (ea.IsRegister)
                {
                    ea.Write(value | 0x80);
                }
                else
                {
                    WriteTasByte(ea.Address, (byte)(value | 0x80));
                    if (mode == 3)
                    {
                        SetAddressRegister(reg, State.A[reg] + AddressIncrement(reg, M68kOperandSize.Byte));
                    }
                }

                AddInstructionCycles(ea.IsRegister ? 4 : 10 + GetByteWordEaOperandCycles(mode, reg));
                return true;
            }

            if (unary is 0x4000 or 0x4200 or 0x4400 or 0x4600 or 0x4A00)
            {
                var size = DecodeImmediateSize(opcode);
                if (size == 0)
                {
                    RaiseException(4, instructionPc, 34);
                    return true;
                }

                var mode = (opcode >> 3) & 7;
                var reg = opcode & 7;
                var writesEffectiveAddress = unary != 0x4A00;
                var addressErrorStackedProgramCounterOffset = writesEffectiveAddress &&
                    size == M68kOperandSize.Long &&
                    mode is 4 or 5 or 6
                        ? -2
                        : 0;
                var ea = ResolveEa(
                    mode,
                    reg,
                    size,
                    write: writesEffectiveAddress,
                    completeWordPostIncrementBeforeRead: size == M68kOperandSize.Word,
                    addressErrorStackedProgramCounterOffset: addressErrorStackedProgramCounterOffset);
                if (writesEffectiveAddress && size == M68kOperandSize.Long && mode == 4)
                {
                    SetAddressRegister(reg, ea.Address);
                }

                if (size != M68kOperandSize.Long && !ea.IsRegister)
                {
                    AddInstructionCyclesFromBase(_instructionCycleFloor, 4);
                }

                var value = ea.Read();
                switch (unary)
                {
                    case 0x4000:
                        value = SubtractWithExtend(0, value, size);
                        ea.Write(value);
                        break;
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

                AddInstructionCycles(GetUnaryCycles(unary, mode, reg, size));
                return true;
            }

            if ((opcode & 0xF1C0) == 0x4180)
            {
                var dataRegister = (opcode >> 9) & 7;
                var mode = (opcode >> 3) & 7;
                var ea = ResolveEa(
                    mode,
                    opcode & 7,
                    M68kOperandSize.Word,
                    completeWordPostIncrementBeforeRead: true);
                if (!ea.IsRegister)
                {
                    AddInstructionCyclesFromBase(_instructionCycleFloor, 4);
                }

                var upperBound = (short)(ushort)ea.Read();
                var value = (short)(ushort)(State.D[dataRegister] & 0xFFFF);
                var status = (ushort)(State.StatusRegister &
                    unchecked((ushort)~(M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry)));
                State.StatusRegister = status;
                var trapCycles = 38 + (ea.IsRegister ? 0 : ea.EaCycles);
                if (value < 0)
                {
                    State.StatusRegister = (ushort)(status | M68kCpuState.Negative);
                    RaiseException(6, State.ProgramCounter, trapCycles + GetChkLowerBoundTrapExtraCycles(value, upperBound));
                }
                else if (value > upperBound)
                {
                    RaiseException(6, State.ProgramCounter, trapCycles);
                }
                else
                {
                    AddInstructionCycles(10 + (ea.IsRegister ? 0 : ea.EaCycles));
                }

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
                    if (counter != 0xFFFF)
                    {
                        var target = (uint)(branchBase + displacement);
                        if (_instructionFrequency.Enabled)
                        {
                            _instructionFrequency.RecordTakenBranch(State.LastInstructionProgramCounter, opcode, target, 4);
                        }

                        AddInstructionCycles(10);
                        BranchTo(target, State.ProgramCounter);
                        State.D[reg] = (State.D[reg] & 0xFFFF_0000) | counter;
                    }
                    else
                    {
                        State.D[reg] = (State.D[reg] & 0xFFFF_0000) | counter;
                        AddInstructionCycles(14);
                    }
                }
                else
                {
                    AddInstructionCycles(12);
                }

                return true;
            }

            if ((opcode & 0xF0C0) == 0x50C0)
            {
                var condition = (opcode >> 8) & 0x0F;
                var conditionEa = ResolveEa((opcode >> 3) & 7, opcode & 7, M68kOperandSize.Byte, write: true);
                var conditionTrue = CheckCondition(condition);
                conditionEa.Write(conditionTrue ? 0xFFu : 0u);
                AddInstructionCycles(conditionEa.IsRegister ? conditionTrue ? 6 : 4 : 8 + conditionEa.EaCycles);
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
                AddInstructionCycles(8);
                return true;
            }

            var addressErrorStackedProgramCounterOffset = size == M68kOperandSize.Long &&
                mode is 4 or 5 or 6
                    ? -2
                    : 0;
            var ea = ResolveEa(
                mode,
                opcode & 7,
                size,
                write: true,
                completeWordPostIncrementBeforeRead: true,
                addressErrorStackedProgramCounterOffset: addressErrorStackedProgramCounterOffset);
            if (size == M68kOperandSize.Long && mode == 4)
            {
                SetAddressRegister(opcode & 7, ea.Address);
            }

            if (mode != 0 && size != M68kOperandSize.Long)
            {
                AddInstructionCyclesFromBase(_instructionCycleFloor, 4);
            }

            var old = ea.Read();
            var result = subtract
                ? Subtract(old, (uint)count, size, setExtend: true)
                : Add(old, (uint)count, size, setExtend: true);
            ea.Write(result);
            AddInstructionCycles(GetAddqSubqCycles(mode, opcode & 7, size));
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
            if (mode == 7 && reg == 4)
            {
                var immediateValue = FetchWord();
                var immediateBit = bit & 7;
                State.SetFlag(M68kCpuState.Zero, (immediateValue & (1u << immediateBit)) == 0);
                AddInstructionCycles(8);
                return true;
            }

            if (mode == 7 && reg == 1)
            {
                var address = FetchLong();
                var absoluteMaskedBit = bit & 7;
                var absoluteValue = ReadByte(address);
                State.SetFlag(M68kCpuState.Zero, (absoluteValue & (1u << absoluteMaskedBit)) == 0);
                AddInstructionCycles(GetImmediateBtstCycles(mode, reg));
                return true;
            }

            var bitSize = mode == 0 ? M68kOperandSize.Long : M68kOperandSize.Byte;
            var bitEa = ResolveEa(mode, reg, bitSize);
            var value = bitEa.Read();
            var maskedBit = mode == 0 ? bit : bit & 7;
            State.SetFlag(M68kCpuState.Zero, (value & (1u << maskedBit)) == 0);
            AddInstructionCycles(GetImmediateBtstCycles(mode, reg));
            return true;
        }

        private static bool IsValidImmediateBtstEa(int mode, int reg)
        {
            return mode switch
            {
                0 => true,
                2 or 3 or 4 or 5 or 6 => true,
                7 => reg <= 4,
                _ => false
            };
        }

        private static int GetImmediateBtstCycles(int mode, int reg)
        {
            return mode == 0 ? 10 : 8 + GetByteWordEaOperandCycles(mode, reg);
        }

        private static int GetImmediateBitChangeCycles(int operation, int mode, int reg, int bit)
        {
            if (mode != 0)
            {
                return 12 + GetByteWordEaOperandCycles(mode, reg);
            }

            var baseCycles = operation == 2 ? 12 : 10;
            return bit >= 16 ? baseCycles + 2 : baseCycles;
        }

        private static int GetDynamicBtstCycles(int mode, int reg)
        {
            return mode == 0 ? 6 : 4 + GetByteWordEaOperandCycles(mode, reg);
        }

        private static int GetDynamicBitChangeCycles(int operation, int mode, int reg, uint bit)
        {
            if (mode != 0)
            {
                return 8 + GetByteWordEaOperandCycles(mode, reg);
            }

            var baseCycles = operation == 2 ? 8 : 6;
            return bit >= 16 ? baseCycles + 2 : baseCycles;
        }

        private static int GetCmpiCycles(M68kOperandSize size, int mode, int reg)
        {
            if (mode == 0)
            {
                return size == M68kOperandSize.Long ? 14 : 8;
            }

            var eaCycles = GetEaOperandCycles(mode, reg, size);
            return (size == M68kOperandSize.Long ? 12 : 8) + eaCycles;
        }

        private static int GetImmediateAluCycles(int mode, int reg, M68kOperandSize size)
        {
            if (mode == 0)
            {
                return size == M68kOperandSize.Long ? 16 : 8;
            }

            var baseCycles = size == M68kOperandSize.Long ? 20 : 12;
            return baseCycles + GetEaOperandCycles(mode, reg, size);
        }

        private static int GetAluEaToDataCycles(int mode, int reg, M68kOperandSize size)
        {
            if (mode == 0)
            {
                return size == M68kOperandSize.Long ? 8 : 4;
            }

            if (mode == 1)
            {
                return size == M68kOperandSize.Long ? 8 : 4;
            }

            if (mode == 7 && reg == 4 && size == M68kOperandSize.Long)
            {
                return 16;
            }

            var baseCycles = size == M68kOperandSize.Long ? 6 : 4;
            return baseCycles + GetEaOperandCycles(mode, reg, size);
        }

        private static int GetCompareCycles(int mode, int reg, M68kOperandSize size)
        {
            if (mode is 0 or 1)
            {
                return size == M68kOperandSize.Long ? 6 : 4;
            }

            var baseCycles = size == M68kOperandSize.Long ? 6 : 4;
            return baseCycles + GetEaOperandCycles(mode, reg, size);
        }

        private static int GetAluDataToEaCycles(int mode, int reg, M68kOperandSize size)
        {
            if (mode == 0)
            {
                return size == M68kOperandSize.Long ? 8 : 4;
            }

            var baseCycles = size == M68kOperandSize.Long ? 12 : 8;
            return baseCycles + GetEaOperandCycles(mode, reg, size);
        }

        private static int GetAddqSubqCycles(int mode, int reg, M68kOperandSize size)
        {
            if (mode == 0)
            {
                return size == M68kOperandSize.Long ? 8 : 4;
            }

            if (mode == 1)
            {
                return 8;
            }

            var baseCycles = size == M68kOperandSize.Long ? 12 : 8;
            return baseCycles + GetEaOperandCycles(mode, reg, size);
        }

        private static int GetAddressArithmeticCycles(
            int sourceMode,
            int sourceRegister,
            bool sourceIsRegister,
            int sourceEaCycles,
            M68kOperandSize size)
        {
            if (sourceMode == 7 && sourceRegister == 4)
            {
                return size == M68kOperandSize.Long ? 16 : 12;
            }

            if (sourceIsRegister)
            {
                return 8;
            }

            return (size == M68kOperandSize.Long ? 6 : 8) + sourceEaCycles;
        }

        private static int GetCompareAddressCycles(
            int sourceMode,
            int sourceRegister,
            bool sourceIsRegister,
            int sourceEaCycles,
            M68kOperandSize size)
        {
            if (sourceIsRegister)
            {
                return 6;
            }

            if (sourceMode == 7 && sourceRegister == 4)
            {
                return size == M68kOperandSize.Long ? 14 : 10;
            }

            return 6 + sourceEaCycles;
        }

        private static int GetUnaryCycles(int operation, int mode, int reg, M68kOperandSize size)
        {
            if (mode == 0)
            {
                return operation == 0x4A00 || size != M68kOperandSize.Long ? 4 : 6;
            }

            var baseCycles = operation == 0x4A00
                ? 4
                : size == M68kOperandSize.Long ? 12 : 8;
            return baseCycles + GetEaOperandCycles(mode, reg, size);
        }

        private static int GetJmpCycles(int mode, int reg)
            => mode switch
            {
                2 => 8,
                5 => 10,
                6 => 14,
                7 => reg switch
                {
                    0 => 10,
                    1 => 12,
                    2 => 10,
                    3 => 14,
                    _ => throw new InvalidOperationException("Invalid JMP effective-address timing mode.")
                },
                _ => throw new InvalidOperationException("Invalid JMP effective-address timing mode.")
            };

        private static int GetJsrCycles(int mode, int reg)
            => mode == 2 ? 16 : GetJmpCycles(mode, reg) + 8;

        private static int GetPeaCycles(int mode, int reg)
            => mode switch
            {
                2 => 12,
                5 => 16,
                6 => 20,
                7 => reg switch
                {
                    0 or 2 => 16,
                    1 or 3 => 20,
                    _ => throw new InvalidOperationException("Invalid PEA effective-address timing mode.")
                },
                _ => throw new InvalidOperationException("Invalid PEA effective-address timing mode.")
            };

        private static int GetLeaCycles(int mode, int reg)
            => mode switch
            {
                2 => 4,
                5 => 8,
                6 => 12,
                7 => reg switch
                {
                    0 or 2 => 8,
                    1 or 3 => 12,
                    _ => throw new InvalidOperationException("Invalid LEA effective-address timing mode.")
                },
                _ => throw new InvalidOperationException("Invalid LEA effective-address timing mode.")
            };

        private static int GetChkLowerBoundTrapExtraCycles(short value, short upperBound)
        {
            if (value > upperBound)
            {
                return 0;
            }

            var compareResult = value - upperBound;
            return compareResult is < short.MinValue or > short.MaxValue ? 0 : 2;
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
                    1 => 12,
                    3 => 10,
                    4 => 4,
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
                    1 => 16,
                    3 => 14,
                    4 => 8,
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
                var sourceEa = ResolveEa(
                    mode,
                    eaReg,
                    M68kOperandSize.Word,
                    completeWordPostIncrementBeforeRead: true);
                if (!sourceEa.IsRegister)
                {
                    AddInstructionCyclesFromBase(_instructionCycleFloor, 4);
                }

                var source = sourceEa.Read();
                State.D[reg] = (uint)((ushort)State.D[reg] * (ushort)source);
                State.SetNegativeZeroLong(State.D[reg]);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
                AddInstructionCycles(GetMultiplyCycles(sourceEa.EaCycles, source, signed: false));
                return true;
            }

            if (line == 0xC && opmode == 7)
            {
                var sourceEa = ResolveEa(
                    mode,
                    eaReg,
                    M68kOperandSize.Word,
                    completeWordPostIncrementBeforeRead: true);
                if (!sourceEa.IsRegister)
                {
                    AddInstructionCyclesFromBase(_instructionCycleFloor, 4);
                }

                var source = unchecked((short)sourceEa.Read());
                State.D[reg] = (uint)(unchecked((short)State.D[reg]) * source);
                State.SetNegativeZeroLong(State.D[reg]);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
                AddInstructionCycles(GetMultiplyCycles(sourceEa.EaCycles, (ushort)source, signed: true));
                return true;
            }

            if (line == 0x8 && (opmode == 3 || opmode == 7))
            {
                var sourceEa = ResolveEa(
                    mode,
                    eaReg,
                    M68kOperandSize.Word,
                    completeWordPostIncrementBeforeRead: true);
                if (!sourceEa.IsRegister)
                {
                    AddInstructionCyclesFromBase(_instructionCycleFloor, 4);
                }

                var divisor = sourceEa.Read() & 0xFFFF;
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
                        State.SetFlag(M68kCpuState.Negative, true);
                        State.SetFlag(M68kCpuState.Zero, false);
                        State.SetFlag(M68kCpuState.Overflow, true);
                        State.SetFlag(M68kCpuState.Carry, false);
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
                    var signedQuotient = (long)signedDividend / signedDivisor;
                    var signedRemainder = (long)signedDividend % signedDivisor;
                    if (signedQuotient < short.MinValue || signedQuotient > short.MaxValue)
                    {
                        State.SetFlag(M68kCpuState.Negative, true);
                        State.SetFlag(M68kCpuState.Zero, false);
                        State.SetFlag(M68kCpuState.Overflow, true);
                        State.SetFlag(M68kCpuState.Carry, false);
                    }
                    else
                    {
                        quotient = unchecked((uint)(int)signedQuotient);
                        remainder = unchecked((uint)(int)signedRemainder);
                        State.D[reg] = ((remainder & 0xFFFF) << 16) | (quotient & 0xFFFF);
                        State.SetNegativeZero(quotient, M68kOperandSize.Word);
                        State.SetFlag(M68kCpuState.Overflow, false);
                        State.SetFlag(M68kCpuState.Carry, false);
                    }
                }

                AddInstructionCycles(4 + GetDivideCycles(sourceEa.EaCycles, dividend, (ushort)divisor, signed: opmode == 7));
                return true;
            }

            if ((line == 0x9 || line == 0xD || line == 0xB) && (opmode == 3 || opmode == 7))
            {
                var size = opmode == 3 ? M68kOperandSize.Word : M68kOperandSize.Long;
                var ea = ResolveEa(
                    mode,
                    eaReg,
                    size,
                    completeWordPostIncrementBeforeRead: size == M68kOperandSize.Word);
                if (size != M68kOperandSize.Long && !ea.IsRegister)
                {
                    AddInstructionCyclesFromBase(_instructionCycleFloor, 4);
                }

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

                AddInstructionCycles(line == 0xB
                    ? GetCompareAddressCycles(mode, eaReg, ea.IsRegister, ea.EaCycles, size)
                    : GetAddressArithmeticCycles(mode, eaReg, ea.IsRegister, ea.EaCycles, size));
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

            var writesEffectiveAddress = registerToEa && (line != 0xB || opmode >= 4);
            var addressErrorStackedProgramCounterOffset = writesEffectiveAddress &&
                operandSize == M68kOperandSize.Long &&
                mode is 4 or 5 or 6
                    ? -2
                    : 0;
            var eaOperand = ResolveEa(
                mode,
                eaReg,
                operandSize,
                write: writesEffectiveAddress,
                completeWordPostIncrementBeforeRead: operandSize == M68kOperandSize.Word,
                addressErrorStackedProgramCounterOffset: addressErrorStackedProgramCounterOffset);
            if (writesEffectiveAddress && operandSize == M68kOperandSize.Long && mode == 4)
            {
                SetAddressRegister(eaReg, eaOperand.Address);
            }

            if (operandSize != M68kOperandSize.Long && !eaOperand.IsRegister)
            {
                AddInstructionCyclesFromBase(_instructionCycleFloor, 4);
            }

            var eaValue = eaOperand.Read();
            var regValue = State.D[reg] & M68kCpuState.Mask(operandSize);
            uint result;
            void CompletePredecrementLongDestinationBeforeWrite()
            {
                if (writesEffectiveAddress && operandSize == M68kOperandSize.Long && mode == 4)
                {
                    SetAddressRegister(eaReg, eaOperand.Address);
                }
            }

            switch (line)
            {
                case 0x8:
                    result = registerToEa ? eaValue | regValue : regValue | eaValue;
                    if (registerToEa)
                    {
                        CompletePredecrementLongDestinationBeforeWrite();
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
                        CompletePredecrementLongDestinationBeforeWrite();
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
                        CompletePredecrementLongDestinationBeforeWrite();
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
                        CompletePredecrementLongDestinationBeforeWrite();
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
                        CompletePredecrementLongDestinationBeforeWrite();
                        eaOperand.Write(result);
                    }
                    else
                    {
                        result = Add(regValue, eaValue, operandSize, setExtend: true);
                        WriteDataRegister(reg, result, operandSize);
                    }

                    break;
            }

            AddInstructionCycles(line == 0xB && opmode < 4
                ? GetCompareCycles(mode, eaReg, operandSize)
                : registerToEa
                    ? GetAluDataToEaCycles(mode, eaReg, operandSize)
                    : GetAluEaToDataCycles(mode, eaReg, operandSize));
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
                source = ReadByte(State.A[sourceRegister]);
                SetAddressRegister(destinationRegister, State.A[destinationRegister] - AddressIncrement(destinationRegister, M68kOperandSize.Byte));
                destinationAddress = State.A[destinationRegister];
                destination = ReadByte(destinationAddress);
            }
            else
            {
                source = (byte)State.D[sourceRegister];
                destination = (byte)State.D[destinationRegister];
            }

            var extend = State.GetFlag(M68kCpuState.Extend) ? 1 : 0;
            var overflow = false;
            var result = subtract
                ? M68kIntegerSemantics.SubtractBcdByte(destination, source, extend, out var carry, out overflow)
                : M68kIntegerSemantics.AddBcdByte(destination, source, extend, out carry, out overflow);

            if (memoryMode)
            {
                WriteByte(destinationAddress, result);
            }
            else
            {
                WriteDataRegister(destinationRegister, result, M68kOperandSize.Byte);
            }

            SetBcdFlags(result, carry, overflow);
            AddInstructionCycles(memoryMode ? 18 : 6);
            return true;
        }

        private void SetBcdFlags(byte result, bool carry, bool overflow = false)
        {
            if (result != 0)
            {
                State.SetFlag(M68kCpuState.Zero, false);
            }

            State.SetFlag(M68kCpuState.Negative, (result & 0x80) != 0);
            State.SetFlag(M68kCpuState.Overflow, overflow);
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
            _dataAccessStackedProgramCounter = size != M68kOperandSize.Byte
                ? State.ProgramCounter + 2
                : State.ProgramCounter;
            if (size != M68kOperandSize.Byte && (sourceAddress & 1) != 0)
            {
                SetAddressRegister(sourceRegister, sourceAddress + 2);
                AddInstructionCyclesFromBase(_instructionCycleFloor, 8);
                if (size == M68kOperandSize.Word)
                {
                    _ = ReadWord(sourceAddress);
                }
                else
                {
                    _ = ReadLong(sourceAddress);
                }
            }

            var source = size switch
            {
                M68kOperandSize.Byte => ReadByte(sourceAddress),
                M68kOperandSize.Word => ReadWord(sourceAddress),
                _ => ReadLong(sourceAddress)
            };
            SetAddressRegister(sourceRegister, sourceAddress + AddressIncrement(sourceRegister, size));

            var destinationAddress = State.A[destinationRegister];
            if (size != M68kOperandSize.Byte && (destinationAddress & 1) != 0)
            {
                AddInstructionCyclesFromBase(_instructionCycleFloor, size == M68kOperandSize.Long ? 16 : 12);
            }

            var destination = size switch
            {
                M68kOperandSize.Byte => ReadByte(destinationAddress),
                M68kOperandSize.Word => ReadWord(destinationAddress),
                _ => ReadLong(destinationAddress)
            };
            SetAddressRegister(destinationRegister, destinationAddress + AddressIncrement(destinationRegister, size));

            _ = Subtract(destination, source, size, setExtend: false, storeResult: false);
            AddInstructionCycles(size == M68kOperandSize.Long ? 20 : 12);
            return true;
        }

        private bool DecodeExchange(ushort opcode)
        {
            var left = (opcode >> 9) & 7;
            var right = opcode & 7;
            if ((opcode & 0xF1F8) == 0xC140)
            {
                (State.D[left], State.D[right]) = (State.D[right], State.D[left]);
                AddInstructionCycles(6);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xC148)
            {
                var value = State.A[left];
                SetAddressRegister(left, State.A[right]);
                SetAddressRegister(right, value);
                AddInstructionCycles(6);
                return true;
            }

            if ((opcode & 0xF1F8) == 0xC188)
            {
                var value = State.D[left];
                State.D[left] = State.A[right];
                SetAddressRegister(right, value);
                AddInstructionCycles(6);
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
                _dataAccessStackedProgramCounter = State.ProgramCounter + 2;
                _dataReadFaultAccessKind = M68kBusAccessKind.CpuDataRead;
                var sourceAddress = State.A[sourceRegister] - increment;
                if (size != M68kOperandSize.Long)
                {
                    SetAddressRegister(sourceRegister, sourceAddress);
                }

                AddInstructionCyclesFromBase(_instructionCycleFloor, 10);

                source = size switch
                {
                    M68kOperandSize.Byte => ReadByte(sourceAddress),
                    M68kOperandSize.Word => ReadWord(sourceAddress),
                    _ => ReadLongDescending(sourceAddress)
                };
                if (size == M68kOperandSize.Long)
                {
                    SetAddressRegister(sourceRegister, sourceAddress);
                }

                AddInstructionCyclesFromBase(_instructionCycleFloor, size == M68kOperandSize.Long ? 8 : 4);
                destinationAddress = State.A[destinationRegister] - AddressIncrement(destinationRegister, size);
                if (size != M68kOperandSize.Long)
                {
                    SetAddressRegister(destinationRegister, destinationAddress);
                }

                destination = size switch
                {
                    M68kOperandSize.Byte => ReadByte(destinationAddress),
                    M68kOperandSize.Word => ReadWord(destinationAddress),
                    _ => ReadLongDescending(destinationAddress)
                };
                if (size == M68kOperandSize.Long)
                {
                    SetAddressRegister(destinationRegister, destinationAddress);
                }
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
                WriteLongDescending(destinationAddress, result);
            }

            AddInstructionCycles(mode == 0
                ? size == M68kOperandSize.Long ? 8 : 4
                : size == M68kOperandSize.Long ? 30 : 18);
        }

        private bool DecodeShiftRotate(ushort opcode)
        {
            if ((opcode & 0xF000) != 0xE000)
            {
                return false;
            }

            if ((opcode & 0x00C0) == 0x00C0)
            {
                var ea = ResolveEa(
                    (opcode >> 3) & 7,
                    opcode & 7,
                    M68kOperandSize.Word,
                    write: true,
                    completeWordPostIncrementBeforeRead: true);
                AddInstructionCyclesFromBase(_instructionCycleFloor, 4);
                var value = ea.Read() & 0xFFFF;
                var type = (opcode >> 9) & 3;
                var left = (opcode & 0x0100) != 0;
                var result = Shift(value, 1, M68kOperandSize.Word, type, left);
                ea.Write(result);
                AddInstructionCycles(8 + ea.EaCycles);
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
            AddInstructionCycles((size == M68kOperandSize.Long ? 8 : 6) + (count * 2));
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
                _dataAccessStackedProgramCounter = State.ProgramCounter + 2;
                var predecrementTransferCycles = size == M68kOperandSize.Long ? 8 : 4;
                var predecrementTransferred = 0;
                for (var bit = 0; bit < 16; bit++)
                {
                    if ((registerMask & (1 << bit)) == 0)
                    {
                        continue;
                    }

                    var register = 15 - bit;
                    address -= (uint)size;
                    var value = register < 8 ? State.D[register] : State.A[register - 8];
                    AddInstructionCycles(12 + (predecrementTransferred * predecrementTransferCycles));
                    if (size == M68kOperandSize.Word)
                    {
                        WriteWord(address, (ushort)value);
                    }
                    else
                    {
                        WriteLongDescending(address, value);
                    }

                    predecrementTransferred++;
                }

                SetAddressRegister(reg, address);
                AddInstructionCycles(8 + CountBits(registerMask) * predecrementTransferCycles);
                return;
            }

            var ea = ResolveEa(mode, reg, size, write: !directionMemoryToRegisters, addressOnly: true);
            var current = ea.Address;
            _dataAccessStackedProgramCounter = State.ProgramCounter + 2;
            _dataReadFaultAccessKind = ea.ReadFaultAccessKind;
            var transferCycles = size == M68kOperandSize.Long ? 8 : 4;
            var transferred = 0;
            for (var register = 0; register < 16; register++)
            {
                if ((registerMask & (1 << register)) == 0)
                {
                    continue;
                }

                if (directionMemoryToRegisters)
                {
                    var readFaultBaseCycles = size == M68kOperandSize.Word ? 8 : 4;
                    AddInstructionCycles(readFaultBaseCycles + ea.EaCycles + (transferred * transferCycles));
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
                    var writeFaultBaseCycles = size == M68kOperandSize.Word ? 8 : 4;
                    AddInstructionCycles(writeFaultBaseCycles + ea.EaCycles + (transferred * transferCycles));
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
                transferred++;
            }

            if (directionMemoryToRegisters && mode == 3)
            {
                SetAddressRegister(reg, current);
            }

            var baseCycles = directionMemoryToRegisters
                ? (size == M68kOperandSize.Word ? 8 : 4) + ea.EaCycles
                : size == M68kOperandSize.Word ? 4 + ea.EaCycles : ea.EaCycles;
            AddInstructionCycles(baseCycles + CountBits(registerMask) * (size == M68kOperandSize.Long ? 8 : 4));
        }

        private EaOperand ResolveEa(
            int mode,
            int reg,
            M68kOperandSize size,
            bool write = false,
            bool addressOnly = false,
            bool completeWordPostIncrementBeforeRead = false,
            int addressErrorStackedProgramCounterOffset = 0)
        {
            switch (mode)
            {
                case 0:
                    return EaOperand.DataRegister(this, reg, size);
                case 1:
                    return EaOperand.AddressRegister(this, reg, size);
                case 2:
                    return EaOperand.Memory(
                        this,
                        State.A[reg],
                        size,
                        GetEaOperandCycles(mode, reg, size),
                        unchecked((uint)(State.ProgramCounter + addressErrorStackedProgramCounterOffset)));
                case 3:
                {
                    var address = State.A[reg];
                    return addressOnly
                        ? EaOperand.Memory(
                            this,
                            address,
                            size,
                            GetEaOperandCycles(mode, reg, size),
                            unchecked((uint)(State.ProgramCounter + addressErrorStackedProgramCounterOffset)))
                        : EaOperand.Memory(
                            this,
                            address,
                            size,
                            GetEaOperandCycles(mode, reg, size),
                            unchecked((uint)(State.ProgramCounter + addressErrorStackedProgramCounterOffset)),
                            reg,
                            AddressIncrement(reg, size),
                            completePostIncrementOnRead: !write,
                            completePostIncrementBeforeRead: completeWordPostIncrementBeforeRead && size == M68kOperandSize.Word);
                }
                case 4:
                {
                    var predecrementAddress = State.A[reg] - AddressIncrement(reg, size);
                    if (!(write && size == M68kOperandSize.Long))
                    {
                        SetAddressRegister(reg, predecrementAddress);
                    }

                    return EaOperand.Memory(
                        this,
                        predecrementAddress,
                        size,
                        GetEaOperandCycles(mode, reg, size),
                        unchecked((uint)(GetPredecrementStackedProgramCounter(size) +
                            (write && size == M68kOperandSize.Long ? 2u : 0u) +
                            addressErrorStackedProgramCounterOffset)),
                        descendingLongWrite: true,
                        delayedPredecrementRegister: write && size == M68kOperandSize.Long ? reg : -1,
                        delayedPredecrementValue: predecrementAddress);
                }
                case 5:
                {
                    var extensionAddress = State.ProgramCounter;
                    var displacement = unchecked((short)FetchWord());
                    return EaOperand.Memory(
                        this,
                        (uint)(State.A[reg] + displacement),
                        size,
                        GetEaOperandCycles(mode, reg, size),
                        unchecked((uint)(extensionAddress +
                            (write && size == M68kOperandSize.Long ? 2u : 0u) +
                            addressErrorStackedProgramCounterOffset)));
                }
                case 6:
                {
                    var extensionAddress = State.ProgramCounter;
                    var extension = FetchWord();
                    return EaOperand.Memory(
                        this,
                        M68kIntegerSemantics.CalculateM68000BriefIndexedAddress(State.A[reg], extension, State.D, State.A),
                        size,
                        GetEaOperandCycles(mode, reg, size),
                        unchecked((uint)(extensionAddress +
                            (write && size == M68kOperandSize.Long ? 2u : 0u) +
                            addressErrorStackedProgramCounterOffset)));
                }
                case 7:
                    return ResolveMode7(reg, size, write, addressErrorStackedProgramCounterOffset);
                default:
                    throw new InvalidOperationException("Invalid effective address mode.");
            }
        }

        private uint GetPredecrementStackedProgramCounter(M68kOperandSize size)
            => size == M68kOperandSize.Word ? State.ProgramCounter + 2 : State.ProgramCounter;

        private EaOperand ResolveMode7(
            int reg,
            M68kOperandSize size,
            bool write = false,
            int addressErrorStackedProgramCounterOffset = 0)
        {
            switch (reg)
            {
                case 0:
                {
                    var address = (uint)(short)FetchWord();
                    return EaOperand.Memory(this, address, size, GetEaOperandCycles(7, reg, size), State.ProgramCounter);
                }
                case 1:
                {
                    var extensionAddress = State.ProgramCounter;
                    var address = FetchLong();
                    return EaOperand.Memory(
                        this,
                        address,
                        size,
                        GetEaOperandCycles(7, reg, size),
                        unchecked((uint)(State.ProgramCounter + addressErrorStackedProgramCounterOffset)));
                }
                case 2:
                    return ResolvePcRelative(size);
                case 3:
                    return ResolvePcIndexed(size);
                case 4:
                    return EaOperand.Immediate(this, FetchImmediate(size), size);
                default:
                    return RaiseIllegalInstruction();
            }
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
            return EaOperand.Memory(
                this,
                (uint)(extensionAddress + displacement),
                size,
                GetEaOperandCycles(7, 2, size),
                extensionAddress,
                readFaultAccessKind: M68kBusAccessKind.CpuInstructionFetch);
        }

        private EaOperand ResolvePcIndexed(M68kOperandSize size)
        {
            var extensionAddress = State.ProgramCounter;
            var extension = FetchWord();
            return EaOperand.Memory(
                this,
                M68kIntegerSemantics.CalculateM68000BriefIndexedAddress(extensionAddress, extension, State.D, State.A),
                size,
                GetEaOperandCycles(7, 3, size),
                extensionAddress,
                readFaultAccessKind: M68kBusAccessKind.CpuInstructionFetch);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecuteLeaDisplacementToAddress(int sourceRegister, int destinationRegister)
        {
            var displacement = unchecked((short)FetchWord());
            SetAddressRegister(destinationRegister, unchecked((uint)(State.A[sourceRegister] + displacement)));
            AddInstructionCycles(8);
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

            State.SetFlag(M68kCpuState.Overflow, shifted.Overflow);
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

        private static int GetImmediateFetchCycles(M68kOperandSize size)
            => size == M68kOperandSize.Long ? 8 : 4;

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
            _prefetchDeferredCpuBusBatchEligible = false;
        }

        private void SetProgramCounterAndFlushPrefetch(uint target)
        {
            State.ProgramCounter = target;
            FlushPrefetch();
        }

        private void BranchTo(uint target, uint stackedProgramCounter)
        {
            if ((target & 1) != 0)
            {
                _dataAccessStackedProgramCounter = stackedProgramCounter;
                ThrowOddAddressAccess(
                    target,
                    isWrite: false,
                    M68kBusAccessKind.CpuInstructionFetch,
                    useDataAccessStackedProgramCounter: true);
            }

            SetProgramCounterAndFlushPrefetch(target);
        }

        private void JumpToSubroutine(uint target, uint stackedProgramCounter)
        {
            if ((target & 1) != 0)
            {
                _dataAccessStackedProgramCounter = stackedProgramCounter;
                ThrowOddAddressAccess(
                    target,
                    isWrite: false,
                    M68kBusAccessKind.CpuInstructionFetch,
                    useDataAccessStackedProgramCounter: true);
            }

            PushLong(State.ProgramCounter);
            SetProgramCounterAndFlushPrefetch(target);
        }

        private void ResetPrefetchPipeline()
        {
            FlushPrefetch();
            _cpuBusCycle = State.Cycles;
            _cpuRetireBusCycle = State.Cycles;
        }

        private void UsePrefetchedInstructionWordForAddressErrorFrame()
        {
            if (_prefetchValid)
            {
                _addressErrorInstructionWord = _prefetchWord;
            }
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
            var requestedCycle = cycle;
            var value = ReadInstructionFetchWord(address, ref cycle);
            _cpuBusCycle = cycle;
            completedCycle = cycle;
            if (_cpuBusPhaseTrace != null)
            {
                RecordCpuBusPhase(
                    address,
                    M68kOperandSize.Word,
                    requestedCycle,
                    cycle,
                    M68kBusAccessKind.CpuInstructionFetch,
                    isWrite: false);
            }
            return value;
        }

        private ushort ReadInstructionFetchWord(uint address, ref long cycle)
        {
            if (_instructionFetchWindowBus != null)
            {
                var bus = _instructionFetchWindowBus;
                if (!_instructionFetchWindow.ContainsWord(address) &&
                    (!TryGetInstructionFetchWindow(bus, address, out _instructionFetchWindow) ||
                    !_instructionFetchWindow.ContainsWord(address)))
                {
                    _prefetchDeferredCpuBusBatchEligible = false;
                    return _bus.ReadWord(address, ref cycle, M68kBusAccessKind.CpuInstructionFetch);
                }

                _prefetchDeferredCpuBusBatchEligible =
                    _deferredCpuInstructionTiming?.IsDeferredCpuBusBatchEligibleInstructionFetchWindow(
                        in _instructionFetchWindow) == true;
                bus.CommitInstructionFetchWindowWord(in _instructionFetchWindow, address, ref cycle);
                return _instructionFetchWindow.ReadWord(address);
            }

            _prefetchDeferredCpuBusBatchEligible = false;
            return _bus.ReadWord(address, ref cycle, M68kBusAccessKind.CpuInstructionFetch);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ThrowOddInstructionFetchAddress(uint address)
        {
            var cycle = BeginCpuBusAccessCycle();
            _deferredCpuInstructionTiming?.FlushDeferredCpuInstructionTiming(ref cycle);
            State.Cycles = Math.Max(State.Cycles, cycle);
            _cpuBusCycle = Math.Max(_cpuBusCycle, cycle);
            _cpuRetireBusCycle = Math.Max(_cpuRetireBusCycle, cycle);
            _addressErrorInstructionWord = null;
            _addressErrorIsWriteOverride = null;
            RaiseAddressError(address, isWrite: false, M68kBusAccessKind.CpuInstructionFetch);
            throw M68kAddressErrorException.Instance;
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
            var requestedCycle = cycle;
            var value = TCpuDataAccess.ReadByte(_typedBus, address, ref cycle);
            CompleteCpuBusAccess(cycle);
            RecordCpuBusPhase(
                address,
                M68kOperandSize.Byte,
                requestedCycle,
                cycle,
                M68kBusAccessKind.CpuDataRead,
                isWrite: false);
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ushort ReadWord(uint address)
        {
            if ((address & 1) != 0)
            {
                ThrowOddAddressAccess(address, isWrite: false, _dataReadFaultAccessKind, useDataAccessStackedProgramCounter: true);
            }

            var cycle = BeginCpuBusAccessCycle();
            var requestedCycle = cycle;
            var value = TCpuDataAccess.ReadWord(_typedBus, address, ref cycle);
            CompleteCpuBusAccess(cycle);
            RecordCpuBusPhase(
                address,
                M68kOperandSize.Word,
                requestedCycle,
                cycle,
                M68kBusAccessKind.CpuDataRead,
                isWrite: false);
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint ReadLong(uint address)
        {
            if ((address & 1) != 0)
            {
                ThrowOddAddressAccess(address, isWrite: false, _dataReadFaultAccessKind, useDataAccessStackedProgramCounter: true);
            }

            var cycle = BeginCpuBusAccessCycle();
            var requestedCycle = cycle;
            var value = TCpuDataAccess.ReadLong(_typedBus, address, ref cycle);
            CompleteCpuBusAccess(cycle);
            RecordCpuBusPhase(
                address,
                M68kOperandSize.Long,
                requestedCycle,
                cycle,
                M68kBusAccessKind.CpuDataRead,
                isWrite: false);
            return value;
        }

        private uint ReadLongDescending(uint address)
        {
            var low = ReadWord(address + 2);
            var high = ReadWord(address);
            return ((uint)high << 16) | low;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteByte(uint address, byte value)
        {
            var cycle = BeginCpuBusAccessCycle();
            var requestedCycle = cycle;
            TCpuDataAccess.WriteByte(_typedBus, address, value, ref cycle);
            CompleteCpuBusAccess(cycle);
            RecordCpuBusPhase(
                address,
                M68kOperandSize.Byte,
                requestedCycle,
                cycle,
                M68kBusAccessKind.CpuDataWrite,
                isWrite: true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteTasByte(uint address, byte value)
        {
            var cycle = BeginCpuBusAccessCycle();
            var requestedCycle = cycle;
            TCpuDataAccess.WriteTasByte(_typedBus, address, value, ref cycle);
            CompleteCpuBusAccess(cycle);
            RecordCpuBusPhase(
                address,
                M68kOperandSize.Byte,
                requestedCycle,
                cycle,
                M68kBusAccessKind.CpuDataWrite,
                isWrite: true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteWord(uint address, ushort value)
        {
            if ((address & 1) != 0)
            {
                ThrowOddAddressAccess(address, isWrite: true, M68kBusAccessKind.CpuDataWrite);
            }

            var cycle = BeginCpuBusAccessCycle();
            var requestedCycle = cycle;
            TCpuDataAccess.WriteWord(_typedBus, address, value, ref cycle);
            CompleteCpuBusAccess(cycle);
            RecordCpuBusPhase(
                address,
                M68kOperandSize.Word,
                requestedCycle,
                cycle,
                M68kBusAccessKind.CpuDataWrite,
                isWrite: true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteLong(uint address, uint value)
        {
            if ((address & 1) != 0)
            {
                ThrowOddAddressAccess(address, isWrite: true, M68kBusAccessKind.CpuDataWrite);
            }

            var cycle = BeginCpuBusAccessCycle();
            var requestedCycle = cycle;
            TCpuDataAccess.WriteLong(_typedBus, address, value, ref cycle);
            CompleteCpuBusAccess(cycle);
            RecordCpuBusPhase(
                address,
                M68kOperandSize.Long,
                requestedCycle,
                cycle,
                M68kBusAccessKind.CpuDataWrite,
                isWrite: true);
        }

        private void WriteLongDescending(uint address, uint value)
        {
            WriteWord(address + 2, (ushort)value);
            WriteWord(address, (ushort)(value >> 16));
        }

        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ThrowOddAddressAccess(
            uint address,
            bool isWrite,
            M68kBusAccessKind accessKind,
            bool useDataAccessStackedProgramCounter = false)
        {
            var faultCycle = BeginCpuBusAccessCycle();
            _deferredCpuInstructionTiming?.FlushDeferredCpuInstructionTiming(ref faultCycle);
            State.Cycles = faultCycle;
            _cpuBusCycle = faultCycle;
            _cpuRetireBusCycle = Math.Max(_cpuRetireBusCycle, faultCycle);
            RaiseAddressError(address, isWrite, accessKind, useDataAccessStackedProgramCounter);
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
            WriteLongDescending(State.A[7], value);
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
        private void RecordCpuBusPhase(
            uint address,
            M68kOperandSize size,
            long requestedCycle,
            long completedCycle,
            M68kBusAccessKind accessKind,
            bool isWrite)
        {
            var trace = _cpuBusPhaseTrace;
            if (trace == null)
            {
                return;
            }

            var phase = new M68kCpuBusPhase(
                _activeInstructionProgramCounter,
                address,
                size,
                requestedCycle,
                completedCycle,
                accessKind,
                isWrite);
            trace.RecordCpuBusPhase(in phase);
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

        /// <summary>
        /// Branchless cycle accumulation for use within instruction execution only.
        /// Caller guarantees <see cref="_instructionCycleFloorActive"/> is true.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddInstructionCycles(int cycles)
        {
            System.Diagnostics.Debug.Assert(cycles > 0, "MC68000 cycle increments must be positive.");
            System.Diagnostics.Debug.Assert(
                _instructionCycleFloorActive,
                "AddInstructionCycles may only be called while an instruction cycle floor is active.");
            _instructionCycleFloor = Math.Max(_instructionCycleFloor, _instructionCycleStart + cycles);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddInstructionCyclesFromBase(long baseCycle, int cycles)
        {
            System.Diagnostics.Debug.Assert(cycles > 0, "MC68000 cycle increments must be positive.");
            if (_instructionCycleFloorActive)
            {
                _instructionCycleFloor = Math.Max(_instructionCycleFloor, baseCycle + cycles);
                return;
            }

            State.Cycles = Math.Max(State.Cycles, baseCycle) + cycles;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BeginInstructionCycleFloor(long startCycle)
        {
            _instructionCycleFloorActive = true;
            _instructionCycleStart = startCycle;
            _instructionCycleFloor = startCycle;
            _cpuBusCycle = Math.Max(_cpuBusCycle, startCycle);
            _cpuRetireBusCycle = Math.Max(_cpuRetireBusCycle, startCycle);
            _deferredCpuInstructionTiming?.BeginDeferredCpuInstructionTiming(startCycle);
        }

        private int CompleteInstruction(long startCycle)
        {
            var completedCycle = Math.Max(_instructionCycleFloor, _cpuRetireBusCycle);
            if (_deferredCpuInstructionTiming is { IsDeferredCpuBusBatchActive: true })
            {
            }
            else
            {
                _deferredCpuInstructionTiming?.FlushDeferredCpuInstructionTiming(ref completedCycle);
            }

            _cpuBusCycle = Math.Max(_cpuBusCycle, completedCycle);
            _cpuRetireBusCycle = Math.Max(_cpuRetireBusCycle, completedCycle);
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
            State.StatusRegister = (ushort)((savedStatusRegister | M68kCpuState.Supervisor) & ~M68kCpuState.Trace);
            PushLong(stackedProgramCounter);
            PushWord(savedStatusRegister);
            SetProgramCounterAndFlushPrefetch(ReadLong((uint)(vector * 4)));
            AddInstructionCycles(cycles);
        }

        private void RaiseAddressError(
            uint faultAddress,
            bool isWrite,
            M68kBusAccessKind accessKind,
            bool useDataAccessStackedProgramCounter = false)
        {
            var exceptionCycleBase = Math.Max(Math.Max(State.Cycles, _cpuRetireBusCycle), _instructionCycleFloor);
            var savedStatusRegister = State.StatusRegister;
            var instructionWord = _addressErrorInstructionWord ?? State.LastOpcode;
            var frameIsWrite = _addressErrorIsWriteOverride ?? isWrite;
            var stackedProgramCounter = accessKind == M68kBusAccessKind.CpuInstructionFetch &&
                !useDataAccessStackedProgramCounter
                ? State.ProgramCounter
                : _dataAccessStackedProgramCounter;
            State.RecordException(3, stackedProgramCounter, savedStatusRegister);
            State.StatusRegister = (ushort)((savedStatusRegister | M68kCpuState.Supervisor) & ~M68kCpuState.Trace);
            PushLong(stackedProgramCounter);
            PushWord(savedStatusRegister);
            PushWord(instructionWord);
            PushLong(faultAddress);
            PushWord(CreateBusErrorStatusWord(instructionWord, savedStatusRegister, frameIsWrite, accessKind));
            SetProgramCounterAndFlushPrefetch(ReadLong(0x0000_000C));
            AddInstructionCyclesFromBase(exceptionCycleBase, AddressErrorExceptionCycles);
        }

        private static ushort CreateBusErrorStatusWord(
            ushort instructionWord,
            ushort savedStatusRegister,
            bool isWrite,
            M68kBusAccessKind accessKind)
        {
            var instruction = accessKind == M68kBusAccessKind.CpuInstructionFetch;
            var supervisor = (savedStatusRegister & M68kCpuState.Supervisor) != 0;
            var status = instruction ? 0x02 : 0x01;
            if (supervisor)
            {
                status |= 0x04;
            }

            if (!isWrite)
            {
                status |= 0x10;
            }

            return (ushort)((instructionWord & 0xFFE0) | status);
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

        private static int GetDivideCycles(int sourceEaCycles, uint dividend, ushort divisor, bool signed)
            => M68kIntegerSemantics.GetDivideCycles(sourceEaCycles, dividend, divisor, signed);

        private static int GetMultiplyCoreCycles(uint sourceValue, bool signed)
            => M68kIntegerSemantics.GetMultiplyCoreCycles(sourceValue, signed);

        private static int CountBits(int value)
            => M68kIntegerSemantics.CountBits((uint)value);

        private readonly struct PlannedEaOperand
        {
            private PlannedEaOperand(
                int kind,
                int register,
                uint address,
                uint immediate,
                M68kOperandSize size,
                int eaCycles,
                uint addressErrorStackedProgramCounter,
                int postIncrementRegister,
                uint postIncrement,
                bool completePostIncrementOnRead,
                M68kBusAccessKind readFaultAccessKind,
                bool descendingLongWrite,
                bool completePostIncrementBeforeRead,
                int delayedPredecrementRegister,
                uint delayedPredecrementValue)
            {
                Kind = kind;
                Register = register;
                Address = address;
                Immediate = immediate;
                Size = size;
                EaCycles = eaCycles;
                AddressErrorStackedProgramCounter = addressErrorStackedProgramCounter;
                PostIncrementRegister = postIncrementRegister;
                PostIncrement = postIncrement;
                CompletePostIncrementOnRead = completePostIncrementOnRead;
                ReadFaultAccessKind = readFaultAccessKind;
                DescendingLongWrite = descendingLongWrite;
                CompletePostIncrementBeforeRead = completePostIncrementBeforeRead;
                DelayedPredecrementRegister = delayedPredecrementRegister;
                DelayedPredecrementValue = delayedPredecrementValue;
            }

            public int Kind { get; }

            public int Register { get; }

            public uint Address { get; }

            public uint Immediate { get; }

            public M68kOperandSize Size { get; }

            public int EaCycles { get; }

            public uint AddressErrorStackedProgramCounter { get; }

            public int PostIncrementRegister { get; }

            public uint PostIncrement { get; }

            public bool CompletePostIncrementOnRead { get; }

            public M68kBusAccessKind ReadFaultAccessKind { get; }

            public bool DescendingLongWrite { get; }

            public bool CompletePostIncrementBeforeRead { get; }

            public int DelayedPredecrementRegister { get; }

            public uint DelayedPredecrementValue { get; }

            public bool IsRegister => Kind is 0 or 1;

            public static PlannedEaOperand DataRegister(int register, M68kOperandSize size)
                => new PlannedEaOperand(
                    0,
                    register,
                    0,
                    0,
                    size,
                    0,
                    0,
                    -1,
                    0,
                    completePostIncrementOnRead: true,
                    M68kBusAccessKind.CpuDataRead,
                    descendingLongWrite: false,
                    completePostIncrementBeforeRead: false,
                    delayedPredecrementRegister: -1,
                    delayedPredecrementValue: 0);

            public static PlannedEaOperand AddressRegister(int register, M68kOperandSize size)
                => new PlannedEaOperand(
                    1,
                    register,
                    0,
                    0,
                    size,
                    0,
                    0,
                    -1,
                    0,
                    completePostIncrementOnRead: true,
                    M68kBusAccessKind.CpuDataRead,
                    descendingLongWrite: false,
                    completePostIncrementBeforeRead: false,
                    delayedPredecrementRegister: -1,
                    delayedPredecrementValue: 0);

            public static PlannedEaOperand Memory(
                uint address,
                M68kOperandSize size,
                int eaCycles,
                uint addressErrorStackedProgramCounter,
                int postIncrementRegister = -1,
                uint postIncrement = 0,
                bool completePostIncrementOnRead = true,
                M68kBusAccessKind readFaultAccessKind = M68kBusAccessKind.CpuDataRead,
                bool descendingLongWrite = false,
                bool completePostIncrementBeforeRead = false,
                int delayedPredecrementRegister = -1,
                uint delayedPredecrementValue = 0)
                => new PlannedEaOperand(
                    2,
                    0,
                    address,
                    0,
                    size,
                    eaCycles,
                    addressErrorStackedProgramCounter,
                    postIncrementRegister,
                    postIncrement,
                    completePostIncrementOnRead,
                    readFaultAccessKind,
                    descendingLongWrite,
                    completePostIncrementBeforeRead,
                    delayedPredecrementRegister,
                    delayedPredecrementValue);

            public static PlannedEaOperand ImmediateValue(uint value, M68kOperandSize size)
                => new PlannedEaOperand(
                    3,
                    0,
                    0,
                    value,
                    size,
                    size == M68kOperandSize.Long ? 8 : 4,
                    0,
                    -1,
                    0,
                    completePostIncrementOnRead: true,
                    M68kBusAccessKind.CpuDataRead,
                    descendingLongWrite: false,
                    completePostIncrementBeforeRead: false,
                    delayedPredecrementRegister: -1,
                    delayedPredecrementValue: 0);
        }

        private readonly struct EaOperand
        {
            private readonly M68kInterpreterCore<TBus, TCpuDataAccess> _cpu;
            private readonly int _kind;
            private readonly int _reg;
            private readonly uint _immediate;
            private readonly uint _addressErrorStackedProgramCounter;
            private readonly int _postIncrementRegister;
            private readonly uint _postIncrement;
            private readonly bool _completePostIncrementOnRead;
            private readonly M68kBusAccessKind _readFaultAccessKind;
            private readonly bool _descendingLongWrite;
            private readonly bool _completePostIncrementBeforeRead;
            private readonly int _delayedPredecrementRegister;
            private readonly uint _delayedPredecrementValue;

            private EaOperand(
                M68kInterpreterCore<TBus, TCpuDataAccess> cpu,
                int kind,
                int reg,
                uint address,
                uint immediate,
                M68kOperandSize size,
                int eaCycles,
                uint addressErrorStackedProgramCounter,
                int postIncrementRegister,
                uint postIncrement,
                bool completePostIncrementOnRead,
                M68kBusAccessKind readFaultAccessKind,
                bool descendingLongWrite,
                bool completePostIncrementBeforeRead,
                int delayedPredecrementRegister,
                uint delayedPredecrementValue)
            {
                _cpu = cpu;
                _kind = kind;
                _reg = reg;
                Address = address;
                _immediate = immediate;
                _addressErrorStackedProgramCounter = addressErrorStackedProgramCounter;
                _postIncrementRegister = postIncrementRegister;
                _postIncrement = postIncrement;
                _completePostIncrementOnRead = completePostIncrementOnRead;
                _readFaultAccessKind = readFaultAccessKind;
                _descendingLongWrite = descendingLongWrite;
                _completePostIncrementBeforeRead = completePostIncrementBeforeRead;
                _delayedPredecrementRegister = delayedPredecrementRegister;
                _delayedPredecrementValue = delayedPredecrementValue;
                Size = size;
                EaCycles = eaCycles;
            }

            public uint Address { get; }

            public uint AddressErrorStackedProgramCounter => _addressErrorStackedProgramCounter;

            public M68kBusAccessKind ReadFaultAccessKind => _readFaultAccessKind;

            public M68kOperandSize Size { get; }

            public int EaCycles { get; }

            public bool IsRegister => _kind is 0 or 1;

            public static EaOperand DataRegister(M68kInterpreterCore<TBus, TCpuDataAccess> cpu, int reg, M68kOperandSize size)
            {
                return new EaOperand(
                    cpu,
                    0,
                    reg,
                    0,
                    0,
                    size,
                    eaCycles: 0,
                    addressErrorStackedProgramCounter: 0,
                    postIncrementRegister: -1,
                    postIncrement: 0,
                    completePostIncrementOnRead: true,
                    readFaultAccessKind: M68kBusAccessKind.CpuDataRead,
                    descendingLongWrite: false,
                    completePostIncrementBeforeRead: false,
                    delayedPredecrementRegister: -1,
                    delayedPredecrementValue: 0);
            }

            public static EaOperand AddressRegister(M68kInterpreterCore<TBus, TCpuDataAccess> cpu, int reg, M68kOperandSize size)
            {
                return new EaOperand(
                    cpu,
                    1,
                    reg,
                    0,
                    0,
                    size,
                    eaCycles: 0,
                    addressErrorStackedProgramCounter: 0,
                    postIncrementRegister: -1,
                    postIncrement: 0,
                    completePostIncrementOnRead: true,
                    readFaultAccessKind: M68kBusAccessKind.CpuDataRead,
                    descendingLongWrite: false,
                    completePostIncrementBeforeRead: false,
                    delayedPredecrementRegister: -1,
                    delayedPredecrementValue: 0);
            }

            public static EaOperand Memory(
                M68kInterpreterCore<TBus, TCpuDataAccess> cpu,
                uint address,
                M68kOperandSize size,
                int eaCycles,
                uint addressErrorStackedProgramCounter,
                int postIncrementRegister = -1,
                uint postIncrement = 0,
                bool completePostIncrementOnRead = true,
                M68kBusAccessKind readFaultAccessKind = M68kBusAccessKind.CpuDataRead,
                bool descendingLongWrite = false,
                bool completePostIncrementBeforeRead = false,
                int delayedPredecrementRegister = -1,
                uint delayedPredecrementValue = 0)
            {
                return new EaOperand(
                    cpu,
                    2,
                    0,
                    address,
                    0,
                    size,
                    eaCycles,
                    addressErrorStackedProgramCounter,
                    postIncrementRegister,
                    postIncrement,
                    completePostIncrementOnRead,
                    readFaultAccessKind,
                    descendingLongWrite,
                    completePostIncrementBeforeRead,
                    delayedPredecrementRegister,
                    delayedPredecrementValue);
            }

            public static EaOperand Immediate(M68kInterpreterCore<TBus, TCpuDataAccess> cpu, uint value, M68kOperandSize size)
            {
                return new EaOperand(
                    cpu,
                    3,
                    0,
                    0,
                    value,
                    size,
                    eaCycles: size == M68kOperandSize.Long ? 8 : 4,
                    addressErrorStackedProgramCounter: 0,
                    postIncrementRegister: -1,
                    postIncrement: 0,
                    completePostIncrementOnRead: true,
                    readFaultAccessKind: M68kBusAccessKind.CpuDataRead,
                    descendingLongWrite: false,
                    completePostIncrementBeforeRead: false,
                    delayedPredecrementRegister: -1,
                    delayedPredecrementValue: 0);
            }

            public uint Read()
            {
                return _kind switch
                {
                    0 => _cpu.State.D[_reg] & M68kCpuState.Mask(Size),
                    1 => Size == M68kOperandSize.Word ? _cpu.State.A[_reg] & 0xFFFF : _cpu.State.A[_reg],
                    2 => Size switch
                    {
                        M68kOperandSize.Byte => ReadByte(),
                        M68kOperandSize.Word => ReadWord(),
                        _ => ReadLong()
                    },
                    3 => _immediate & M68kCpuState.Mask(Size),
                    _ => 0
                };
            }

            private byte ReadByte()
            {
                _cpu._dataAccessStackedProgramCounter = _addressErrorStackedProgramCounter;
                _cpu._dataReadFaultAccessKind = _readFaultAccessKind;
                if (EaCycles > 0)
                {
                    _cpu.AddInstructionCyclesFromBase(_cpu._instructionCycleFloor, EaCycles);
                }

                CompleteBeforeReadMemoryAccess();
                var value = _cpu.ReadByte(Address);
                CompleteReadMemoryAccess();
                return value;
            }

            private ushort ReadWord()
            {
                _cpu._dataAccessStackedProgramCounter = _addressErrorStackedProgramCounter;
                _cpu._dataReadFaultAccessKind = _readFaultAccessKind;
                if (EaCycles > 0)
                {
                    _cpu.AddInstructionCyclesFromBase(_cpu._instructionCycleFloor, EaCycles);
                }

                CompleteBeforeReadMemoryAccess();
                var value = _cpu.ReadWord(Address);
                CompleteReadMemoryAccess();
                return value;
            }

            private uint ReadLong()
            {
                _cpu._dataAccessStackedProgramCounter = _addressErrorStackedProgramCounter;
                _cpu._dataReadFaultAccessKind = _readFaultAccessKind;
                if (EaCycles > 0)
                {
                    _cpu.AddInstructionCyclesFromBase(_cpu._instructionCycleFloor, EaCycles);
                }

                CompleteBeforeReadMemoryAccess();
                var value = _cpu.ReadLong(Address);
                CompleteReadMemoryAccess();
                return value;
            }

            private void CompleteReadMemoryAccess()
            {
                if (_completePostIncrementOnRead && !_completePostIncrementBeforeRead)
                {
                    CompleteMemoryAccess();
                }
            }

            private void CompleteBeforeReadMemoryAccess()
            {
                if (_completePostIncrementBeforeRead)
                {
                    CompleteMemoryAccess();
                }
            }

            private void CompleteMemoryAccess()
            {
                if (_postIncrementRegister >= 0)
                {
                    _cpu.SetAddressRegister(
                        _postIncrementRegister,
                        _cpu.State.A[_postIncrementRegister] + _postIncrement);
                }
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
                        _cpu._dataAccessStackedProgramCounter = _addressErrorStackedProgramCounter;
                        if (EaCycles > 0)
                        {
                            _cpu.AddInstructionCycles(EaCycles);
                        }

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
                        if (_descendingLongWrite)
                        {
                            _cpu.WriteLongDescending(Address, value);
                        }
                        else
                        {
                            _cpu.WriteLong(Address, value);
                        }
                        }

                        if (!_completePostIncrementBeforeRead)
                        {
                            CompleteMemoryAccess();
                        }

                        if (_delayedPredecrementRegister >= 0)
                        {
                            _cpu.SetAddressRegister(_delayedPredecrementRegister, _delayedPredecrementValue);
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
            bool enableCpuBusPhaseTrace = true,
            bool enableOpcodePlan = true,
            M68kOpcodePlanDispatch opcodePlanDispatch = M68kOpcodePlanDispatch.KindTable)
            : base(
                bus,
                default,
                state,
                instructionFrequency,
                enableInstructionFetchWindow,
                enableCpuBusPhaseTrace,
                enableOpcodePlan,
                opcodePlanDispatch)
        {
        }

    }
}
