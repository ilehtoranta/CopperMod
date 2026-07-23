/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;

namespace Copper68k
{
    internal sealed class M68010AddressMaskedBus :
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

        public M68010AddressMaskedBus(IM68kBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _codeReader = bus as IM68kCodeReader;
            _fastMemoryBus = bus as IM68kFastMemoryBus;
            _physicalAddressMap = bus as IM68kPhysicalAddressMap;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
            => _bus.ReadByte(Mask(address), ref cycle, accessKind);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
            => _bus.ReadWord(Mask(address), ref cycle, accessKind);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
            => _bus.ReadLong(Mask(address), ref cycle, accessKind);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
            => _bus.WriteByte(Mask(address), value, ref cycle, accessKind);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
            => _bus.WriteWord(Mask(address), value, ref cycle, accessKind);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
            => _bus.WriteLong(Mask(address), value, ref cycle, accessKind);

        public bool HasHostGateway(uint address)
            => _bus.HasHostGateway(Mask(address));

        public bool TryInvokeHostGateway(uint instructionProgramCounter, uint token, M68kCpuState state)
            => _bus.TryInvokeHostGateway(Mask(instructionProgramCounter), token, state);

        public M68kHostGatewayInvocation InvokeHostGateway(uint instructionProgramCounter, uint token, M68kCpuState state)
            => _bus.InvokeHostGateway(Mask(instructionProgramCounter), token, state);

        public ushort ReadHostWord(uint address)
            => _codeReader is not null
                ? _codeReader.ReadHostWord(Mask(address))
                : throw new InvalidOperationException("The wrapped MC68010 bus does not provide host code reads.");

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
        private static uint Mask(uint address)
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

    internal sealed class M68010Interpreter :
        M68kInterpreterCore<M68010AddressMaskedBus, M68kNoExactCpuDataAccess<M68010AddressMaskedBus>>
    {
        public M68010Interpreter(IM68kBus bus)
            : base(
                CreateBus(bus),
                default,
                opcodePlanDispatch: M68kCoreFactory.M68000OpcodePlanDispatch)
        {
        }

        internal M68010Interpreter(
            IM68kBus bus,
            M68kCpuState state,
            M68kInstructionFrequencyMatrix? instructionFrequency = null)
            : base(
                CreateBus(bus),
                default,
                state,
                instructionFrequency,
                opcodePlanDispatch: M68kCoreFactory.M68000OpcodePlanDispatch)
        {
        }

        private static M68010AddressMaskedBus CreateBus(IM68kBus bus)
            => new(bus);

        protected override bool TryExecuteModelSpecificLine4(ushort opcode, uint instructionPc)
        {
            if (opcode is not (0x4E7A or 0x4E7B))
            {
                return false;
            }

            var extension = FetchWord();
            if (!State.GetFlag(M68kCpuState.Supervisor))
            {
                RaiseException(8, instructionPc, 34);
                return true;
            }

            var generalRegister = (extension >> 12) & 7;
            var useAddressRegister = (extension & 0x8000) != 0;
            var controlRegister = extension & 0x0FFF;
            if (opcode == 0x4E7A)
            {
                if (!TryReadControlRegister(controlRegister, instructionPc, out var value))
                {
                    return true;
                }

                WriteGeneralRegister(useAddressRegister, generalRegister, value);
            }
            else
            {
                var value = ReadGeneralRegister(useAddressRegister, generalRegister);
                if (!TryWriteControlRegister(controlRegister, value, instructionPc))
                {
                    return true;
                }
            }

            AddInstructionCycles(12);
            return true;
        }

        protected override uint GetExceptionVectorAddress(int vector)
            => State.VectorBaseRegister + unchecked((uint)(vector * 4));

        protected override uint GetInterruptVectorAddress(uint vectorAddress)
            => State.VectorBaseRegister + vectorAddress;

        protected override bool TryHandleModelSpecificExceptionFrame(
            int vector,
            uint stackedProgramCounter,
            ushort savedStatusRegister)
        {
            PushWord((ushort)((vector * 4) & 0x0FFF));
            PushLong(stackedProgramCounter);
            PushWord(savedStatusRegister);
            return true;
        }

        protected override bool TryHandleModelSpecificAddressError(
            uint faultAddress,
            bool isWrite,
            M68kBusAccessKind accessKind,
            bool useDataAccessStackedProgramCounter)
        {
            _ = faultAddress;
            _ = isWrite;
            _ = accessKind;
            _ = useDataAccessStackedProgramCounter;
            var stackedProgramCounter = State.LastInstructionProgramCounter;
            var savedStatusRegister = State.StatusRegister;
            State.RecordException(3, stackedProgramCounter, savedStatusRegister);
            State.StatusRegister = (ushort)((savedStatusRegister | M68kCpuState.Supervisor) & ~M68kCpuState.Trace);
            PushWord(0x800C);
            PushLong(stackedProgramCounter);
            PushWord(savedStatusRegister);
            State.ProgramCounter = ReadLong(GetExceptionVectorAddress(3));
            return true;
        }

        private bool TryReadControlRegister(int register, uint instructionPc, out uint value)
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
                    RaiseException(4, instructionPc, 34);
                    value = 0;
                    return false;
            }
        }

        private bool TryWriteControlRegister(int register, uint value, uint instructionPc)
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
                    RaiseException(4, instructionPc, 34);
                    return false;
            }
        }

        private uint ReadGeneralRegister(bool addressRegister, int register)
            => addressRegister ? State.A[register] : State.D[register];

        private void WriteGeneralRegister(bool addressRegister, int register, uint value)
        {
            if (addressRegister)
            {
                if (register == 7)
                {
                    State.SetActiveStackPointer(value);
                }
                else
                {
                    State.A[register] = value;
                }
            }
            else
            {
                State.D[register] = value;
            }
        }
    }
}
