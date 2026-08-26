/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace CopperMod.Amiga.Diagnostics
{
    internal enum CpuDeferredPeripheralAccess : byte
    {
        Unsupported,
        ImmediateBarrier,
        JournalableWrite,
        SideEffectFreeRead
    }

    /// <summary>
    /// Conservative CPU-visible batching policy. This is intentionally separate
    /// from schedule-impact metadata: a register can leave slot ownership
    /// unchanged and still expose an immediate CPU/device side effect.
    /// </summary>
    internal static class CpuDeferredCustomAccessClassifier
    {
        public static CpuDeferredPeripheralAccess ClassifyCustom(
            AmigaChipset chipset,
            ushort offset,
            bool isWrite)
        {
            offset = CustomRegisterScheduleClassifier.NormalizeOffset(offset);
            ref readonly var register = ref CustomRegisterFile.GetResolved(chipset, offset);
            if (!register.IsPresent)
            {
                return CpuDeferredPeripheralAccess.Unsupported;
            }

            if (!isWrite)
            {
                // Beam, IRQ, disk/audio state, collision and open-bus behavior
                // remain exact barriers until a dedicated latched-read model is
                // proven for an individual register.
                return CpuDeferredPeripheralAccess.ImmediateBarrier;
            }

            if (register.WriteName == null)
            {
                return CpuDeferredPeripheralAccess.Unsupported;
            }

            // Address-only writes alter the unexecuted suffix of an already
            // causal DMA plan, but do not themselves start or stop a channel.
            // Admission is enabled separately after focused timing tests.
            if (IsDmaPointerRegister(offset) ||
                CustomRegisterScheduleClassifier.IsColorRegister(offset))
            {
                return CpuDeferredPeripheralAccess.JournalableWrite;
            }

            // All other implemented writes are explicit barriers. This includes
            // DMACON/INT*/ADKCON, DDF/DIW/BPLCON, Copper jumps, blitter/disk
            // starts, audio control/data, sprite control/data and palette state.
            return CpuDeferredPeripheralAccess.ImmediateBarrier;
        }

        public static bool IsDmaPointerRegister(ushort offset)
        {
            offset = CustomRegisterScheduleClassifier.NormalizeOffset(offset);
            return offset is >= 0x0E0 and <= 0x0FE || // BPL1PT-BPL8PT
                offset is >= 0x120 and <= 0x13E;      // SPR0PT-SPR7PT
        }

        public static CpuDeferredPeripheralAccess ClassifyCia(bool isWrite, byte register)
        {
            _ = isWrite;
            _ = register;
            // Timer, TOD, ICR and port state are CPU-visible. No CIA access is
            // admitted until a separate latched-read/control-event model exists.
            return CpuDeferredPeripheralAccess.ImmediateBarrier;
        }
    }
}
