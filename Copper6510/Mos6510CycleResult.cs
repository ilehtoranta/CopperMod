/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace Copper6510
{
    /// <summary>
    /// Identifies a logical operation started or completed by the MOS 6510 core.
    /// </summary>
    public enum Mos6510OperationKind
    {
        /// <summary>No operation boundary occurred.</summary>
        None,
        /// <summary>A CPU instruction.</summary>
        Instruction,
        /// <summary>A maskable interrupt entry sequence.</summary>
        Irq,
        /// <summary>A non-maskable interrupt entry sequence.</summary>
        Nmi,
        /// <summary>A hardware reset sequence.</summary>
        Reset,
        /// <summary>The CPU is stopped by a JAM/KIL opcode.</summary>
        Halted
    }

    /// <summary>
    /// Describes the result of one PHI2 CPU cycle.
    /// </summary>
    public readonly struct Mos6510CycleResult
    {
        /// <summary>Initializes a cycle result.</summary>
        public Mos6510CycleResult(
            Mos6510OperationKind startedOperation,
            Mos6510OperationKind completedOperation,
            bool cpuAdvanced)
        {
            StartedOperation = startedOperation;
            CompletedOperation = completedOperation;
            CpuAdvanced = cpuAdvanced;
        }

        /// <summary>Gets the operation accepted on this cycle.</summary>
        public Mos6510OperationKind StartedOperation { get; }

        /// <summary>Gets the operation retired on this cycle.</summary>
        public Mos6510OperationKind CompletedOperation { get; }

        /// <summary>Gets whether the internal CPU microstate advanced.</summary>
        public bool CpuAdvanced { get; }
    }
}
