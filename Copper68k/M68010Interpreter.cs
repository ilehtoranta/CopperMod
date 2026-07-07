/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;

namespace Copper68k
{
    internal sealed class M68010Interpreter : M68020Interpreter
    {
        private const uint AddressMask = 0x00FF_FFFFu;

        public M68010Interpreter(IM68kBus bus)
            : base(
                MaskBus(bus),
                M68020CpuProfile.OcsAccelerator14Mhz,
                new M68kCpuState(),
                enableM68020StackMode: false,
                opcodeKinds: M68020OpcodeDispatchTable.M68010Kinds)
        {
        }

        internal M68010Interpreter(
            IM68kBus bus,
            M68kCpuState state,
            M68kInstructionFrequencyMatrix? instructionFrequency = null)
            : base(
                MaskBus(bus),
                M68020CpuProfile.OcsAccelerator14Mhz,
                state,
                instructionFrequency,
                enableM68020StackMode: false,
                opcodeKinds: M68020OpcodeDispatchTable.M68010Kinds)
        {
        }

        private static IM68kBus MaskBus(IM68kBus bus)
            => bus is IM68kCodeReader codeReader
                ? new M68010AddressMaskedCodeReaderBus(bus, codeReader)
                : new M68010AddressMaskedBus(bus);

        private class M68010AddressMaskedBus : IM68kBus, IM68kFastMemoryBus, IM68kPhysicalAddressMap
        {
            private readonly IM68kBus _bus;
            private readonly IM68kFastMemoryBus? _fastMemoryBus;
            private readonly IM68kPhysicalAddressMap? _physicalAddressMap;

            public M68010AddressMaskedBus(IM68kBus bus)
            {
                _bus = bus ?? throw new ArgumentNullException(nameof(bus));
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

            public bool HasHostTrapStub(uint address)
                => _bus.HasHostTrapStub(Mask(address));

            public bool TryInvokeHostTrap(uint instructionProgramCounter, ushort trapId, M68kCpuState state)
                => _bus.TryInvokeHostTrap(Mask(instructionProgramCounter), trapId, state);

            public void ResetExternalDevices(long cycle)
                => _bus.ResetExternalDevices(cycle);

            public bool TryReadFastByte(uint address, M68kBusAccessKind accessKind, out byte value)
                => TryReadFast(address, accessKind, out value);

            public bool TryReadFastWord(uint address, M68kBusAccessKind accessKind, out ushort value)
                => TryReadFast(address, accessKind, out value);

            public bool TryReadFastLong(uint address, M68kBusAccessKind accessKind, out uint value)
                => TryReadFast(address, accessKind, out value);

            public bool TryWriteFastByte(uint address, byte value, M68kBusAccessKind accessKind)
                => _fastMemoryBus is not null &&
                    _fastMemoryBus.TryWriteFastByte(Mask(address), value, accessKind);

            public bool TryWriteFastWord(uint address, ushort value, M68kBusAccessKind accessKind)
                => _fastMemoryBus is not null &&
                    _fastMemoryBus.TryWriteFastWord(Mask(address), value, accessKind);

            public bool TryWriteFastLong(uint address, uint value, M68kBusAccessKind accessKind)
                => _fastMemoryBus is not null &&
                    _fastMemoryBus.TryWriteFastLong(Mask(address), value, accessKind);

            public bool IsCpuPhysicalAddressMapped(uint address, int byteCount, M68kBusAccessKind accessKind)
                => _physicalAddressMap?.IsCpuPhysicalAddressMapped(Mask(address), byteCount, accessKind) ?? false;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            protected static uint Mask(uint address)
                => address & AddressMask;

            private bool TryReadFast(uint address, M68kBusAccessKind accessKind, out byte value)
            {
                if (_fastMemoryBus is not null)
                {
                    return _fastMemoryBus.TryReadFastByte(Mask(address), accessKind, out value);
                }

                value = 0;
                return false;
            }

            private bool TryReadFast(uint address, M68kBusAccessKind accessKind, out ushort value)
            {
                if (_fastMemoryBus is not null)
                {
                    return _fastMemoryBus.TryReadFastWord(Mask(address), accessKind, out value);
                }

                value = 0;
                return false;
            }

            private bool TryReadFast(uint address, M68kBusAccessKind accessKind, out uint value)
            {
                if (_fastMemoryBus is not null)
                {
                    return _fastMemoryBus.TryReadFastLong(Mask(address), accessKind, out value);
                }

                value = 0;
                return false;
            }
        }

        private sealed class M68010AddressMaskedCodeReaderBus : M68010AddressMaskedBus, IM68kCodeReader
        {
            private readonly IM68kCodeReader _codeReader;

            public M68010AddressMaskedCodeReaderBus(IM68kBus bus, IM68kCodeReader codeReader)
                : base(bus)
            {
                _codeReader = codeReader ?? throw new ArgumentNullException(nameof(codeReader));
            }

            public ushort ReadHostWord(uint address)
                => _codeReader.ReadHostWord(Mask(address));
        }

        protected override bool TryReadControlRegister(int register, uint instructionPc, out uint value)
        {
            switch (register)
            {
                case 0x000:
                    value = State.SourceFunctionCode;
                    return true;
                case 0x001:
                    value = State.DestinationFunctionCode;
                    return true;
                case 0x801:
                    value = State.VectorBaseRegister;
                    return true;
                default:
                    value = RaiseIllegalControlRegister(instructionPc);
                    return false;
            }
        }

        protected override bool TryWriteControlRegister(int register, uint value, uint instructionPc)
        {
            switch (register)
            {
                case 0x000:
                    State.SourceFunctionCode = value & 0x7;
                    return true;
                case 0x001:
                    State.DestinationFunctionCode = value & 0x7;
                    return true;
                case 0x801:
                    State.VectorBaseRegister = value;
                    return true;
                default:
                    _ = RaiseIllegalControlRegister(instructionPc);
                    return false;
            }
        }

        protected override bool TryRaiseMisalignedWordDataRead(uint address, uint instructionPc)
        {
            var savedStatusRegister = State.StatusRegister;
            State.RecordException(3, instructionPc, savedStatusRegister);
            State.StatusRegister = (ushort)((State.StatusRegister | M68kCpuState.Supervisor) & ~M68kCpuState.Trace);
            PushWord(0x800C);
            PushLong(instructionPc);
            PushWord(savedStatusRegister);
            State.ProgramCounter = ReadLong(State.VectorBaseRegister + 0x0C);
            CompleteTiming(M68kInstructionTimingKey.IllegalInstruction);
            _ = address;
            return true;
        }
    }
}
