/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;

namespace Copper68k
{
    internal sealed class M68EC020AddressMaskedBus :
        IM68kBus,
        IM68kCodeReader,
        IM68kFastMemoryBus,
        IM68kPhysicalAddressMap
    {
        private const uint AddressMask = 0x00FF_FFFFu;
        private readonly IM68kBus _bus;
        private readonly IM68kCodeReader? _codeReader;
        private readonly IM68kFastMemoryBus? _fastMemoryBus;
        private readonly IM68kPhysicalAddressMap? _physicalAddressMap;

        internal M68EC020AddressMaskedBus(IM68kBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _codeReader = bus as IM68kCodeReader;
            _fastMemoryBus = bus as IM68kFastMemoryBus;
            _physicalAddressMap = bus as IM68kPhysicalAddressMap;
        }

        public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
            => _bus.ReadByte(Mask(address), ref cycle, accessKind);

        public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
            => _bus.ReadWord(Mask(address), ref cycle, accessKind);

        public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
            => _bus.ReadLong(Mask(address), ref cycle, accessKind);

        public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
            => _bus.WriteByte(Mask(address), value, ref cycle, accessKind);

        public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
            => _bus.WriteWord(Mask(address), value, ref cycle, accessKind);

        public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
            => _bus.WriteLong(Mask(address), value, ref cycle, accessKind);

        public bool HasHostTrapStub(uint address) => _bus.HasHostTrapStub(Mask(address));

        public bool TryInvokeHostTrap(uint instructionProgramCounter, ushort trapId, M68kCpuState state)
            => _bus.TryInvokeHostTrap(Mask(instructionProgramCounter), trapId, state);

        public void ResetExternalDevices(long cycle) => _bus.ResetExternalDevices(cycle);

        public ushort ReadHostWord(uint address)
            => _codeReader is not null
                ? _codeReader.ReadHostWord(Mask(address))
                : throw new InvalidOperationException("The wrapped 68EC020 bus does not provide host code reads.");

        public bool TryReadFastByte(uint address, M68kBusAccessKind accessKind, out byte value)
            => _fastMemoryBus?.TryReadFastByte(Mask(address), accessKind, out value) ?? ReturnFalse(out value);

        public bool TryReadFastWord(uint address, M68kBusAccessKind accessKind, out ushort value)
            => _fastMemoryBus?.TryReadFastWord(Mask(address), accessKind, out value) ?? ReturnFalse(out value);

        public bool TryReadFastLong(uint address, M68kBusAccessKind accessKind, out uint value)
            => _fastMemoryBus?.TryReadFastLong(Mask(address), accessKind, out value) ?? ReturnFalse(out value);

        public bool TryWriteFastByte(uint address, byte value, M68kBusAccessKind accessKind)
            => _fastMemoryBus?.TryWriteFastByte(Mask(address), value, accessKind) ?? false;

        public bool TryWriteFastWord(uint address, ushort value, M68kBusAccessKind accessKind)
            => _fastMemoryBus?.TryWriteFastWord(Mask(address), value, accessKind) ?? false;

        public bool TryWriteFastLong(uint address, uint value, M68kBusAccessKind accessKind)
            => _fastMemoryBus?.TryWriteFastLong(Mask(address), value, accessKind) ?? false;

        public bool IsCpuPhysicalAddressMapped(uint address, int byteCount, M68kBusAccessKind accessKind)
            => _physicalAddressMap?.IsCpuPhysicalAddressMapped(Mask(address), byteCount, accessKind) ?? false;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Mask(uint address) => address & AddressMask;

        private static bool ReturnFalse<T>(out T value)
        {
            value = default!;
            return false;
        }
    }

    internal sealed class M68EC020Interpreter : M68kAdvancedTimingInterpreter
    {
        public M68EC020Interpreter(IM68kBus bus)
            : this(bus, M68020CpuProfile.OcsAccelerator14Mhz)
        {
        }

        internal M68EC020Interpreter(IM68kBus bus, M68020CpuProfile profile)
            : base(new M68EC020AddressMaskedBus(bus), profile, new M68kCpuState(), opcodeKinds: M68020OpcodeDispatchTable.M68020Kinds)
        {
        }
    }
}
