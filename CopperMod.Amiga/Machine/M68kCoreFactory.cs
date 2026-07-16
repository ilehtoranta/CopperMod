/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.Runtime
{
    internal sealed class AmigaM68kCoreFactory : IM68kBackendCoreFactory
    {
        public static AmigaM68kCoreFactory Default { get; } = new AmigaM68kCoreFactory();

        public IM68kCore Create(M68kCpuModel model, IM68kBus bus)
            => model == M68kCpuModel.M68000 && bus is AmigaBus amigaBus
                ? M68kCoreFactory.CreateM68000Core(amigaBus, default(AmigaCpuDataAccess))
                : M68kCoreFactory.Default.Create(model, bus);

        public IM68kCore Create(M68kBackendKind backend, IM68kBus bus)
            => bus is AmigaBus amigaBus
                ? backend switch
                {
                    M68kBackendKind.AccurateM68000 => M68kCoreFactory.CreateM68000Core(amigaBus, default(AmigaCpuDataAccess)),
                    M68kBackendKind.JitM68000 => M68kJitCore.CreateM68000(
                        amigaBus,
                        (state, instructionFrequency, cpuModel) => CreateJitM68000Fallback(
                            amigaBus,
                            state,
                            instructionFrequency,
                            cpuModel)),
                    _ => M68kCoreFactory.Default.Create(backend, bus)
                }
                : M68kCoreFactory.Default.Create(backend, bus);

        private static IM68kCore CreateJitM68000Fallback(
            AmigaBus bus,
            M68kCpuState state,
            M68kInstructionFrequencyMatrix instructionFrequency,
            M68kJitCpuModel cpuModel)
        {
            if (cpuModel != M68kJitCpuModel.M68000)
            {
                throw new M68kEmulationException($"Unexpected Amiga M68k JIT fallback CPU model: {cpuModel}.");
            }

            return M68kCoreFactory.CreateM68000Core(
                bus,
                default(AmigaCpuDataAccess),
                state,
                instructionFrequency,
                enableInstructionFetchWindow: false,
                enableCpuBusPhaseTrace:
                    ((IM68000BusCycleTiming)bus).RequiresExactM68000PipelineFallback);
        }
    }

    internal readonly struct AmigaCpuDataAccess : IM68kCpuDataAccess<AmigaBus, AmigaCpuDataAccess>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ReadByte(AmigaBus bus, uint address, ref long cycle)
            => bus.TryReadExactCpuDataByte(address, ref cycle, out var value)
                ? value
                : ReadByteFallback(bus, address, ref cycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort ReadWord(AmigaBus bus, uint address, ref long cycle)
            => bus.TryReadExactCpuDataWord(address, ref cycle, out var value)
                ? value
                : ReadWordFallback(bus, address, ref cycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ReadLong(AmigaBus bus, uint address, ref long cycle)
            => bus.TryReadExactCpuDataLong(address, ref cycle, out var value)
                ? value
                : ReadLongFallback(bus, address, ref cycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteByte(AmigaBus bus, uint address, byte value, ref long cycle)
        {
            if (!bus.TryWriteExactCpuDataByte(address, value, ref cycle))
            {
                WriteByteFallback(bus, address, value, ref cycle);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteTasByte(AmigaBus bus, uint address, byte value, ref long cycle)
            => bus.WriteTasCpuDataByte(address, value, ref cycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteWord(AmigaBus bus, uint address, ushort value, ref long cycle)
        {
            if (!bus.TryWriteExactCpuDataWord(address, value, ref cycle))
            {
                WriteWordFallback(bus, address, value, ref cycle);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteLong(AmigaBus bus, uint address, uint value, ref long cycle)
        {
            if (!bus.TryWriteExactCpuDataLong(address, value, ref cycle))
            {
                WriteLongFallback(bus, address, value, ref cycle);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static byte ReadByteFallback(AmigaBus bus, uint address, ref long cycle)
            => ((IM68kBus)bus).ReadByte(address, ref cycle, M68kBusAccessKind.CpuDataRead);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ushort ReadWordFallback(AmigaBus bus, uint address, ref long cycle)
            => ((IM68kBus)bus).ReadWord(address, ref cycle, M68kBusAccessKind.CpuDataRead);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static uint ReadLongFallback(AmigaBus bus, uint address, ref long cycle)
            => ((IM68kBus)bus).ReadLong(address, ref cycle, M68kBusAccessKind.CpuDataRead);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WriteByteFallback(AmigaBus bus, uint address, byte value, ref long cycle)
            => ((IM68kBus)bus).WriteByte(address, value, ref cycle, M68kBusAccessKind.CpuDataWrite);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WriteWordFallback(AmigaBus bus, uint address, ushort value, ref long cycle)
            => ((IM68kBus)bus).WriteWord(address, value, ref cycle, M68kBusAccessKind.CpuDataWrite);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WriteLongFallback(AmigaBus bus, uint address, uint value, ref long cycle)
            => ((IM68kBus)bus).WriteLong(address, value, ref cycle, M68kBusAccessKind.CpuDataWrite);
    }
}
