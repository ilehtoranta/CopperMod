/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace Copper68k
{
    internal enum M68kJitDirectRamBankKind : byte
    {
        None = 0,
        PseudoFast = 1,
        RealFast = 2
    }

    internal readonly struct M68kJitDirectRamMap
    {
        public M68kJitDirectRamMap(
            byte[] bankKinds,
            int[] bankOffsets,
            byte[] pseudoFastMemory,
            byte[] realFastMemory,
            int bankShift,
            bool realFastIsZeroWait)
        {
            BankKinds = bankKinds ?? throw new ArgumentNullException(nameof(bankKinds));
            BankOffsets = bankOffsets ?? throw new ArgumentNullException(nameof(bankOffsets));
            PseudoFastMemory = pseudoFastMemory ?? throw new ArgumentNullException(nameof(pseudoFastMemory));
            RealFastMemory = realFastMemory ?? throw new ArgumentNullException(nameof(realFastMemory));
            BankShift = bankShift;
            RealFastIsZeroWait = realFastIsZeroWait;
        }

        public byte[] BankKinds { get; }

        public int[] BankOffsets { get; }

        public byte[] PseudoFastMemory { get; }

        public byte[] RealFastMemory { get; }

        public int BankShift { get; }

        /// <summary>
        /// Gets whether the real-fast banks can be accessed without invoking bus timing.
        /// </summary>
        public bool RealFastIsZeroWait { get; }

        public bool IsValid =>
            BankShift > 0 &&
            BankShift < 32 &&
            BankKinds != null &&
            BankOffsets != null &&
            BankKinds.Length == BankOffsets.Length;
    }

    internal interface IM68kJitDirectRamBus
    {
        bool TryGetJitDirectRamMap(out M68kJitDirectRamMap map);

        void ReplayJitPseudoFastAccesses(ref long cycle, int accessCount, ulong longAccessBits);

        void ReplayJitMove16PseudoFastAccesses(
            ref long retireCycle,
            bool sourcePseudoFast,
            bool destinationPseudoFast);

        void CompleteJitDirectRamWrite(uint physicalAddress, int byteCount);
    }

    /// <summary>
    /// Selects how a Copper68k core executes instructions.
    /// </summary>
    public enum M68kExecutionMode
    {
        /// <summary>
        /// Execute instructions with the interpreter backend.
        /// </summary>
        Interpreter = 0,

        /// <summary>
        /// Execute supported instructions with the JIT backend and fall back to the interpreter for unsupported cases.
        /// </summary>
        Jit = 1
    }

    /// <summary>
    /// Options used when creating a Copper68k CPU core.
    /// </summary>
	public sealed class M68kCoreOptions
	{
		/// <summary>
		/// Creates a default options instance that selects interpreter execution.
		/// </summary>
		public static M68kCoreOptions Default => new M68kCoreOptions();

        /// <summary>
        /// Gets or sets the requested execution mode.
        /// </summary>
        public M68kExecutionMode ExecutionMode { get; set; } = M68kExecutionMode.Interpreter;
    }

    /// <summary>
    /// Identifies a contiguous memory region used by optional JIT zero-wait memory access.
    /// </summary>
    public enum M68kJitMemoryKind
    {
        /// <summary>
        /// The memory region is writable fast RAM.
        /// </summary>
        FastRam = 0,

        /// <summary>
        /// The memory region is read-only ROM.
        /// </summary>
        Rom = 1,

        /// <summary>
        /// The memory region is a ROM overlay visible at a different CPU address.
        /// </summary>
        Overlay = 2
    }

    /// <summary>
    /// Optional bus capability required by the Copper68k JIT.
    /// </summary>
    /// <remarks>
    /// Implement this interface on an <see cref="IM68kBus"/> when code memory can be read
    /// consistently enough for trace compilation and invalidated when writable code changes.
    /// </remarks>
    public interface IM68kJitBus
    {
        /// <summary>
        /// Raised after a writable code range changes and compiled traces covering the range should be invalidated.
        /// </summary>
        event Action<uint, int>? JitCodeRangeWritten;

        /// <summary>
        /// Determines whether a physical instruction address can be compiled by the JIT.
        /// </summary>
        /// <param name="physicalAddress">The normalized physical instruction address.</param>
        /// <param name="byteCount">The number of instruction bytes to probe.</param>
        /// <param name="accessKind">The CPU access kind used for the probe.</param>
        /// <returns><see langword="true"/> if the range can be compiled.</returns>
        bool IsJitCodeAddress(uint physicalAddress, int byteCount, M68kBusAccessKind accessKind);

        /// <summary>
        /// Determines whether a physical instruction range is read-only code that may run before the MC68040 instruction cache is enabled.
        /// </summary>
        /// <param name="physicalAddress">The normalized physical instruction address.</param>
        /// <param name="byteCount">The number of instruction bytes to probe.</param>
        /// <param name="accessKind">The CPU access kind used for the probe.</param>
        /// <returns><see langword="true"/> if the range is read-only code.</returns>
        bool IsJitReadOnlyCodeAddress(uint physicalAddress, int byteCount, M68kBusAccessKind accessKind);

        /// <summary>
        /// Reads one big-endian instruction word without advancing the emulated CPU cycle.
        /// </summary>
        /// <param name="physicalAddress">The normalized physical instruction address.</param>
        /// <returns>The instruction word at <paramref name="physicalAddress"/>.</returns>
        ushort ReadJitCodeWord(uint physicalAddress);

        /// <summary>
        /// Gets the current generation value for the code page containing an address.
        /// </summary>
        /// <param name="physicalAddress">The normalized physical address to query.</param>
        /// <returns>The current code page generation.</returns>
        uint GetJitCodePageGeneration(uint physicalAddress);

        /// <summary>
        /// Determines whether a compiled code range still matches previously captured page generations.
        /// </summary>
        /// <param name="physicalAddress">The normalized physical start address.</param>
        /// <param name="byteCount">The number of bytes in the range.</param>
        /// <param name="startGeneration">The expected generation for the first page.</param>
        /// <param name="endGeneration">The expected generation for the last page.</param>
        /// <returns><see langword="true"/> when the range has not changed.</returns>
        bool JitCodeRangeGenerationMatches(uint physicalAddress, int byteCount, uint startGeneration, uint endGeneration);

        /// <summary>
        /// Captures a stable byte snapshot for asynchronous JIT compilation.
        /// </summary>
        /// <param name="physicalRoot">The normalized physical root address to capture from.</param>
        /// <param name="maxBytes">The maximum number of bytes to capture.</param>
        /// <param name="snapshot">The captured snapshot when this method returns <see langword="true"/>.</param>
        /// <returns><see langword="true"/> if the snapshot was captured consistently.</returns>
        bool TryCaptureJitCodeSnapshot(uint physicalRoot, int maxBytes, out M68kJitCodeSnapshot snapshot);
    }

    /// <summary>
    /// Optional bus capability for direct zero-wait memory access from compiled JIT traces.
    /// </summary>
    public interface IM68kJitFastMemoryBus
    {
        /// <summary>
        /// Reads a value from a zero-wait memory region.
        /// </summary>
        bool TryReadJitZeroWaitMemory(uint physicalAddress, M68kOperandSize size, out uint value);

        /// <summary>
        /// Writes a value to a zero-wait memory region.
        /// </summary>
        bool TryWriteJitZeroWaitMemory(uint physicalAddress, uint value, M68kOperandSize size);

        /// <summary>
        /// Gets a contiguous zero-wait memory buffer for direct reads.
        /// </summary>
        bool TryGetJitZeroWaitReadMemory(
            uint physicalAddress,
            int byteCount,
            out byte[] memory,
            out int offset,
            out M68kJitMemoryKind memoryKind);

        /// <summary>
        /// Gets a contiguous zero-wait memory buffer for direct writes.
        /// </summary>
        bool TryGetJitZeroWaitWriteMemory(
            uint physicalAddress,
            int byteCount,
            out byte[] memory,
            out int offset,
            out M68kJitMemoryKind memoryKind);

        /// <summary>
        /// Completes a direct zero-wait write and invalidates affected code pages.
        /// </summary>
        void CompleteJitZeroWaitWrite(uint physicalAddress, int byteCount);
    }

    /// <summary>
    /// Optional bus capability for host-specific timed memory and max-speed device shortcuts.
    /// </summary>
    public interface IM68kJitTimedMemoryBus
    {
        /// <summary>
        /// Reads memory using a host-specific timing model.
        /// </summary>
        uint ReadJitTimedMemory(ref long cycle, uint physicalAddress, M68kOperandSize size);

        /// <summary>
        /// Writes memory using a host-specific timing model.
        /// </summary>
        void WriteJitTimedMemory(ref long cycle, uint physicalAddress, uint value, M68kOperandSize size);

        /// <summary>
        /// Reads a host-specific max-speed device register when the shortcut is available.
        /// </summary>
        bool TryReadJitMaxSpeedDeviceRegister(uint physicalAddress, M68kOperandSize size, out uint value);

        /// <summary>
        /// Writes a host-specific max-speed device register when the shortcut is available.
        /// </summary>
        bool TryWriteJitMaxSpeedDeviceRegister(uint physicalAddress, uint value, M68kOperandSize size, long cycle);
    }
}
