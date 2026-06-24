/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace Copper6510
{
    /// <summary>
    /// Identifies why the MOS 6510 CPU core is accessing the emulated bus.
    /// </summary>
    public enum Mos6510BusAccessKind
    {
        /// <summary>
        /// The CPU is fetching an opcode byte.
        /// </summary>
        OpcodeFetch,

        /// <summary>
        /// The CPU is fetching an operand byte.
        /// </summary>
        OperandFetch,

        /// <summary>
        /// The CPU is reading data from memory or a device.
        /// </summary>
        Read,

        /// <summary>
        /// The CPU is writing data to memory or a device.
        /// </summary>
        Write,

        /// <summary>
        /// The CPU is performing a dummy read cycle.
        /// </summary>
        DummyRead,

        /// <summary>
        /// The CPU is performing a dummy write cycle before a final write.
        /// </summary>
        DummyWrite,

        /// <summary>
        /// The CPU is reading from the stack page.
        /// </summary>
        StackRead,

        /// <summary>
        /// The CPU is writing to the stack page.
        /// </summary>
        StackWrite,

        /// <summary>
        /// The CPU is reading an interrupt or reset vector.
        /// </summary>
        VectorRead,

        /// <summary>
        /// The CPU consumed a bus cycle without a data transfer.
        /// </summary>
        Idle
    }
}
