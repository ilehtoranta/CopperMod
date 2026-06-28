using System;
using System.Collections.Generic;

namespace Copper68k
{
    internal sealed class M68040FpuState
    {
        public const uint ConditionNan = 0x0100_0000;
        public const uint ConditionInfinity = 0x0200_0000;
        public const uint ConditionZero = 0x0400_0000;
        public const uint ConditionNegative = 0x0800_0000;
        private const uint ConditionMask = ConditionNan | ConditionInfinity | ConditionZero | ConditionNegative;

        public double[] FP { get; } = new double[8];

        public uint Fpcr { get; set; }

        public uint Fpsr { get; set; }

        public uint Fpiar { get; set; }

        public uint LastStateFrameAddress { get; set; }

        public ushort LastStateFrameHeader { get; set; }

        public uint LastStateFrameSize { get; set; }

        public bool LastStateFrameRestore { get; set; }

        public bool HasExecutedNonConditionalInstruction { get; set; }

        public void Reset()
        {
            Array.Clear(FP);
            Fpcr = 0;
            Fpsr = 0;
            Fpiar = 0;
            LastStateFrameAddress = 0;
            LastStateFrameHeader = 0;
            LastStateFrameSize = 0;
            LastStateFrameRestore = false;
            HasExecutedNonConditionalInstruction = false;
        }

        public void SetCondition(double value)
        {
            var condition = 0u;
            if (double.IsNaN(value))
            {
                condition |= ConditionNan;
            }
            else
            {
                if (value == 0.0)
                {
                    condition |= ConditionZero;
                }

                if (double.IsNegative(value))
                {
                    condition |= ConditionNegative;
                }

                if (double.IsInfinity(value))
                {
                    condition |= ConditionInfinity;
                }
            }

            Fpsr = (Fpsr & ~ConditionMask) | condition;
        }

        public void SetCompareCondition(double left, double right)
        {
            if (double.IsNaN(left) || double.IsNaN(right))
            {
                Fpsr = (Fpsr & ~ConditionMask) | ConditionNan;
                return;
            }

            var result = left - right;
            SetCondition(result);
        }

        public bool CheckCondition(int condition)
        {
            var nan = (Fpsr & ConditionNan) != 0;
            var zero = (Fpsr & ConditionZero) != 0;
            var negative = (Fpsr & ConditionNegative) != 0;
            return (condition & 0x0F) switch
            {
                0x0 => false,
                0x1 => zero,
                0x2 => !(nan || zero || negative),
                0x3 => zero || !(nan || negative),
                0x4 => negative && !nan,
                0x5 => zero || (negative && !nan),
                0x6 => !zero && !nan,
                0x7 => !nan,
                0x8 => nan,
                0x9 => nan || zero,
                0xA => nan || !(zero || negative),
                0xB => nan || zero || !negative,
                0xC => nan || negative,
                0xD => nan || zero || negative,
                0xE => nan || !zero,
                _ => true
            };
        }
    }

    internal enum M68040FpuJitKind
    {
        Operation = 0,
        MoveToEa = 1,
        MoveToControl = 2,
        MoveFromControl = 3,
        LineFTrap = 4,
        SaveState = 5,
        RestoreState = 6
    }

    internal static class M68040FpuHelpers
    {
        public const uint NullStateFrame = 0x0000_0000;
        public const uint NullStateFrameSize = 4;
        public const uint IdleStateFrame = 0x4100_0000;
        public const uint IdleStateFrameSize = 4;

        public static bool IsSupportedOperation(int opmode)
            => opmode is 0x00 or 0x04 or 0x18 or 0x1A or 0x20 or 0x22 or 0x23 or 0x28 or 0x38 or 0x3A;

        public static bool IsSupportedDataRegisterFormat(int format)
            => format is 0 or 1 or 4 or 6;

        public static bool IsSupportedMemoryFormat(int format)
            => format is 0 or 1 or 4 or 5 or 6;

        public static bool UsesBus(M68kDecodedInstruction instruction)
            => (M68040FpuJitKind)instruction.Variant is M68040FpuJitKind.LineFTrap or M68040FpuJitKind.SaveState or M68040FpuJitKind.RestoreState ||
                instruction.Source.IsMemory ||
                instruction.Destination.IsMemory;

        public static uint StateFrameSize(ushort header)
        {
            var size = header & 0x00FFu;
            return size == 0 ? NullStateFrameSize : size;
        }

        public static bool IsNullStateFrameHeader(ushort header)
            => header == 0;

        public static uint CurrentStateFrame(M68040FpuState fpu)
            => fpu.HasExecutedNonConditionalInstruction ? IdleStateFrame : NullStateFrame;

        public static uint FormatByteSize(int format)
        {
            return format switch
            {
                0 or 1 => 4,
                4 => 2,
                5 => 8,
                6 => 1,
                _ => 4
            };
        }

        public static double ReadDataRegister(uint value, int format)
        {
            return format switch
            {
                0 => unchecked((int)value),
                1 => BitConverter.Int32BitsToSingle(unchecked((int)value)),
                4 => unchecked((short)(value & 0xFFFF)),
                6 => unchecked((sbyte)(value & 0xFF)),
                _ => throw new M68kEmulationException($"Unsupported MC68040 FPU data-register format {format}.")
            };
        }

        public static uint WriteDataRegister(uint currentValue, int format, double value)
        {
            return format switch
            {
                0 => unchecked((uint)(int)value),
                1 => unchecked((uint)BitConverter.SingleToInt32Bits((float)value)),
                4 => (currentValue & 0xFFFF_0000u) | unchecked((ushort)(short)value),
                6 => (currentValue & 0xFFFF_FF00u) | unchecked((byte)(sbyte)value),
                _ => throw new M68kEmulationException($"Unsupported MC68040 FPU data-register format {format}.")
            };
        }

        public static double ReadImmediate(
            int format,
            ushort extension0,
            ushort extension1,
            uint immediate)
        {
            return format switch
            {
                0 => unchecked((int)immediate),
                1 => BitConverter.Int32BitsToSingle(unchecked((int)immediate)),
                4 => unchecked((short)immediate),
                5 => BitConverter.Int64BitsToDouble(
                    unchecked((long)(((ulong)extension0 << 48) |
                        ((ulong)extension1 << 32) |
                        immediate))),
                6 => unchecked((sbyte)(byte)immediate),
                _ => throw new M68kEmulationException($"Unsupported MC68040 FPU immediate format {format}.")
            };
        }

        public static bool ApplyOperation(M68040FpuState fpu, int destination, int opmode, double source)
        {
            switch (opmode)
            {
                case 0x00:
                    fpu.HasExecutedNonConditionalInstruction = true;
                    fpu.FP[destination] = source;
                    fpu.SetCondition(source);
                    return true;
                case 0x04:
                    fpu.HasExecutedNonConditionalInstruction = true;
                    StoreUnary(fpu, destination, Math.Sqrt(source));
                    return true;
                case 0x18:
                    fpu.HasExecutedNonConditionalInstruction = true;
                    StoreUnary(fpu, destination, Math.Abs(source));
                    return true;
                case 0x1A:
                    fpu.HasExecutedNonConditionalInstruction = true;
                    StoreUnary(fpu, destination, -source);
                    return true;
                case 0x20:
                    fpu.HasExecutedNonConditionalInstruction = true;
                    StoreBinary(fpu, destination, fpu.FP[destination] / source);
                    return true;
                case 0x22:
                    fpu.HasExecutedNonConditionalInstruction = true;
                    StoreBinary(fpu, destination, fpu.FP[destination] + source);
                    return true;
                case 0x23:
                    fpu.HasExecutedNonConditionalInstruction = true;
                    StoreBinary(fpu, destination, fpu.FP[destination] * source);
                    return true;
                case 0x28:
                    fpu.HasExecutedNonConditionalInstruction = true;
                    StoreBinary(fpu, destination, fpu.FP[destination] - source);
                    return true;
                case 0x38:
                    fpu.HasExecutedNonConditionalInstruction = true;
                    fpu.SetCompareCondition(fpu.FP[destination], source);
                    return true;
                case 0x3A:
                    fpu.HasExecutedNonConditionalInstruction = true;
                    fpu.SetCondition(source);
                    return true;
                default:
                    return false;
            }
        }

        private static void StoreUnary(M68040FpuState fpu, int destination, double result)
        {
            fpu.FP[destination] = result;
            fpu.SetCondition(result);
        }

        private static void StoreBinary(M68040FpuState fpu, int destination, double result)
        {
            fpu.FP[destination] = result;
            fpu.SetCondition(result);
        }
    }

    internal sealed class M68040MmuState
    {
        private const uint TranslationEnable = 0x8000_0000;
        private readonly Dictionary<ulong, uint> _atc = [];
        private uint _translationControl;
        private uint _supervisorRootPointer;
        private uint _userRootPointer;
        private uint _instructionTransparentTranslation0;
        private uint _instructionTransparentTranslation1;
        private uint _dataTransparentTranslation0;
        private uint _dataTransparentTranslation1;

        public uint TranslationControl
        {
            get => _translationControl;
            set => SetTranslationRegister(ref _translationControl, value);
        }

        public uint SupervisorRootPointer
        {
            get => _supervisorRootPointer;
            set => SetTranslationRegister(ref _supervisorRootPointer, value);
        }

        public uint UserRootPointer
        {
            get => _userRootPointer;
            set => SetTranslationRegister(ref _userRootPointer, value);
        }

        public uint InstructionTransparentTranslation0
        {
            get => _instructionTransparentTranslation0;
            set => SetTranslationRegister(ref _instructionTransparentTranslation0, value);
        }

        public uint InstructionTransparentTranslation1
        {
            get => _instructionTransparentTranslation1;
            set => SetTranslationRegister(ref _instructionTransparentTranslation1, value);
        }

        public uint DataTransparentTranslation0
        {
            get => _dataTransparentTranslation0;
            set => SetTranslationRegister(ref _dataTransparentTranslation0, value);
        }

        public uint DataTransparentTranslation1
        {
            get => _dataTransparentTranslation1;
            set => SetTranslationRegister(ref _dataTransparentTranslation1, value);
        }

        public uint Status { get; set; }

        public bool BypassTranslation { get; set; }

        public bool Enabled => (TranslationControl & TranslationEnable) != 0;

        public void Reset()
        {
            TranslationControl = 0;
            SupervisorRootPointer = 0;
            UserRootPointer = 0;
            InstructionTransparentTranslation0 = 0;
            InstructionTransparentTranslation1 = 0;
            DataTransparentTranslation0 = 0;
            DataTransparentTranslation1 = 0;
            Status = 0;
            BypassTranslation = false;
            _atc.Clear();
        }

        public void Flush()
            => _atc.Clear();

        private void SetTranslationRegister(ref uint register, uint value)
        {
            if (register == value)
            {
                return;
            }

            register = value;
            Flush();
        }

        public bool TryTranslate(
            uint logicalAddress,
            M68kBusAccessKind accessKind,
            bool write,
            bool supervisor,
            Func<uint, uint> readPhysicalLong,
            out uint physicalAddress,
            out M68040MmuFault fault)
        {
            physicalAddress = logicalAddress;
            fault = default;

            if (BypassTranslation)
            {
                return TryAcceptPhysical(logicalAddress, accessKind, write, out physicalAddress, out fault);
            }

            Status = 0;
            if (!Enabled || MatchesTransparent(logicalAddress, accessKind, write, supervisor))
            {
                return TryAcceptPhysical(logicalAddress, accessKind, write, out physicalAddress, out fault);
            }

            var page = logicalAddress & 0xFFFF_F000u;
            var key = CreateAtcKey(page, accessKind, supervisor);
            if (_atc.TryGetValue(key, out var cachedBase))
            {
                return TryAcceptPhysical(cachedBase | (logicalAddress & 0x0000_0FFFu), accessKind, write, out physicalAddress, out fault);
            }

            var root = supervisor ? SupervisorRootPointer : UserRootPointer;
            if ((root & 0xFFFF_F000u) == 0)
            {
                return Fault(logicalAddress, accessKind, write, supervisor, 0x0000_0001, out fault);
            }

            var descriptorAddress = (root & 0xFFFF_F000u) + (((logicalAddress >> 12) & 0x000F_FFFFu) * 4u);
            uint descriptor;
            try
            {
                descriptor = readPhysicalLong(descriptorAddress);
            }
            catch (Exception ex) when (ex is M68kEmulationException or IndexOutOfRangeException)
            {
                return Fault(logicalAddress, accessKind, write, supervisor, 0x0000_0002, out fault);
            }

            if ((descriptor & 0x0000_0001u) == 0)
            {
                return Fault(logicalAddress, accessKind, write, supervisor, 0x0000_0004, out fault);
            }

            if (write && (descriptor & 0x0000_0004u) != 0)
            {
                return Fault(logicalAddress, accessKind, write, supervisor, 0x0000_0008, out fault);
            }

            var physicalBase = descriptor & 0xFFFF_F000u;
            _atc[key] = physicalBase;
            return TryAcceptPhysical(physicalBase | (logicalAddress & 0x0000_0FFFu), accessKind, write, out physicalAddress, out fault);
        }

        public void Probe(
            uint logicalAddress,
            M68kBusAccessKind accessKind,
            bool write,
            bool supervisor,
            Func<uint, uint> readPhysicalLong)
        {
            _ = TryTranslate(logicalAddress, accessKind, write, supervisor, readPhysicalLong, out _, out _);
        }

        private bool TryAcceptPhysical(
            uint candidate,
            M68kBusAccessKind accessKind,
            bool write,
            out uint physicalAddress,
            out M68040MmuFault fault)
        {
            if (candidate <= 0x00FF_FFFFu)
            {
                physicalAddress = candidate;
                fault = default;
                return true;
            }

            if (accessKind != M68kBusAccessKind.CpuInstructionFetch)
            {
                physicalAddress = candidate & 0x00FF_FFFFu;
                fault = default;
                return true;
            }

            physicalAddress = 0;
            return Fault(candidate, accessKind, write, supervisor: true, 0x0000_0010, out fault);
        }

        private bool Fault(
            uint logicalAddress,
            M68kBusAccessKind accessKind,
            bool write,
            bool supervisor,
            uint status,
            out M68040MmuFault fault)
        {
            Status = status | (write ? 0x0000_0100u : 0) | (supervisor ? 0x0000_0200u : 0);
            fault = new M68040MmuFault(logicalAddress, accessKind, write, Status);
            return false;
        }

        private bool MatchesTransparent(uint address, M68kBusAccessKind accessKind, bool write, bool supervisor)
        {
            var instruction = accessKind == M68kBusAccessKind.CpuInstructionFetch;
            return MatchesTransparentRegister(address, instruction ? InstructionTransparentTranslation0 : DataTransparentTranslation0, write, supervisor) ||
                MatchesTransparentRegister(address, instruction ? InstructionTransparentTranslation1 : DataTransparentTranslation1, write, supervisor);
        }

        private static bool MatchesTransparentRegister(uint address, uint register, bool write, bool supervisor)
        {
            if ((register & 0x0000_8000u) == 0 && (register & 0x8000_0000u) == 0)
            {
                return false;
            }

            var userSupervisor = (register >> 13) & 0x3;
            if (userSupervisor == 0x1 && supervisor)
            {
                return false;
            }

            if (userSupervisor == 0x2 && !supervisor)
            {
                return false;
            }

            if (write && (register & 0x0000_0004u) != 0)
            {
                return false;
            }

            var baseByte = (register >> 24) & 0xFFu;
            var maskByte = (register >> 16) & 0xFFu;
            var addressByte = (address >> 24) & 0xFFu;
            return (addressByte & ~maskByte) == (baseByte & ~maskByte);
        }

        private static ulong CreateAtcKey(uint page, M68kBusAccessKind accessKind, bool supervisor)
            => ((ulong)page << 8) |
                ((ulong)(accessKind == M68kBusAccessKind.CpuInstructionFetch ? 1u : 0u) << 1) |
                (supervisor ? 1u : 0u);
    }

    internal readonly record struct M68040MmuFault(
        uint LogicalAddress,
        M68kBusAccessKind AccessKind,
        bool Write,
        uint Status,
        uint? StackedProgramCounter = null);

    internal sealed class UnsupportedM68040InstructionException : M68kEmulationException
    {
        public UnsupportedM68040InstructionException(
            ushort opcode,
            uint programCounter,
            string profileName,
            Exception? innerException = null)
            : base($"Unsupported MC68040 instruction form for opcode 0x{opcode:X4} at 0x{programCounter:X8} in profile {profileName}.")
        {
            Opcode = opcode;
            ProgramCounter = programCounter;
            ProfileName = profileName;
            OriginalException = innerException;
        }

        public ushort Opcode { get; }

        public uint ProgramCounter { get; }

        public string ProfileName { get; }

        public Exception? OriginalException { get; }
    }

    internal sealed class M68040MmuFaultException : M68kEmulationException
    {
        public M68040MmuFaultException(M68040MmuFault fault)
            : base($"MC68040 MMU fault at logical address 0x{fault.LogicalAddress:X8}.")
        {
            Fault = fault;
        }

        public M68040MmuFault Fault { get; }
    }

    internal sealed class M68040LogicalBus : IM68kBus, IM68kCodeReader, IM68kFastMemoryBus
    {
        private readonly IM68kBus _physicalBus;
        private readonly IM68kCodeReader? _codeReader;
        private readonly IM68kPhysicalAddressMap? _physicalAddressMap;
        private readonly M68kCpuState _state;

        public M68040LogicalBus(IM68kBus physicalBus, M68kCpuState state)
        {
            _physicalBus = physicalBus ?? throw new ArgumentNullException(nameof(physicalBus));
            _codeReader = physicalBus as IM68kCodeReader;
            _physicalAddressMap = physicalBus as IM68kPhysicalAddressMap;
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
            => _physicalBus.ReadByte(Translate(address, accessKind, write: false, byteCount: 1), ref cycle, accessKind);

        public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
            => _physicalBus.ReadWord(Translate(address, accessKind, write: false, byteCount: 2), ref cycle, accessKind);

        public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
            => _physicalBus.ReadLong(Translate(address, accessKind, write: false, byteCount: 4), ref cycle, accessKind);

        public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
            => _physicalBus.WriteByte(Translate(address, accessKind, write: true, byteCount: 1), value, ref cycle, accessKind);

        public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
            => _physicalBus.WriteWord(Translate(address, accessKind, write: true, byteCount: 2), value, ref cycle, accessKind);

        public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
            => _physicalBus.WriteLong(Translate(address, accessKind, write: true, byteCount: 4), value, ref cycle, accessKind);

        public bool HasHostTrapStub(uint address)
        {
            try
            {
                return _physicalBus.HasHostTrapStub(
                    Translate(address, M68kBusAccessKind.CpuInstructionFetch, write: false, byteCount: 2));
            }
            catch (M68040MmuFaultException)
            {
                return false;
            }
        }

        public bool TryInvokeHostTrap(uint instructionProgramCounter, ushort trapId, M68kCpuState state)
        {
            var physicalPc = Translate(
                instructionProgramCounter,
                M68kBusAccessKind.CpuInstructionFetch,
                write: false,
                byteCount: 2);
            return _physicalBus.TryInvokeHostTrap(physicalPc, trapId, state);
        }

        public void ResetExternalDevices(long cycle)
            => _physicalBus.ResetExternalDevices(cycle);

        public ushort ReadHostWord(uint address)
        {
            var physical = Translate(address, M68kBusAccessKind.CpuInstructionFetch, write: false, byteCount: 2);
            if (_codeReader != null)
            {
                return _codeReader.ReadHostWord(physical);
            }

            var cycle = _state.Cycles;
            return _physicalBus.ReadWord(physical, ref cycle, M68kBusAccessKind.CpuInstructionFetch);
        }

        public bool TryReadFastByte(uint address, M68kBusAccessKind accessKind, out byte value)
        {
            var physical = Translate(address, accessKind, write: false, byteCount: 1);
            if (_physicalBus is IM68kFastMemoryBus fastBus && CanFastReadPhysical(physical))
            {
                return fastBus.TryReadFastByte(physical, accessKind, out value);
            }

            value = 0;
            return false;
        }

        public bool TryReadFastWord(uint address, M68kBusAccessKind accessKind, out ushort value)
        {
            var physical = Translate(address, accessKind, write: false, byteCount: 2);
            if (_physicalBus is IM68kFastMemoryBus fastBus && CanFastReadPhysical(physical))
            {
                return fastBus.TryReadFastWord(physical, accessKind, out value);
            }

            value = 0;
            return false;
        }

        public bool TryReadFastLong(uint address, M68kBusAccessKind accessKind, out uint value)
        {
            var physical = Translate(address, accessKind, write: false, byteCount: 4);
            if (_physicalBus is IM68kFastMemoryBus fastBus && CanFastReadPhysical(physical))
            {
                return fastBus.TryReadFastLong(physical, accessKind, out value);
            }

            value = 0;
            return false;
        }

        public bool TryWriteFastByte(uint address, byte value, M68kBusAccessKind accessKind)
        {
            var physical = Translate(address, accessKind, write: true, byteCount: 1);
            if (_physicalBus is not IM68kFastMemoryBus fastBus || !CanFastWritePhysical(physical))
            {
                return false;
            }

            return fastBus.TryWriteFastByte(physical, value, accessKind);
        }

        public bool TryWriteFastWord(uint address, ushort value, M68kBusAccessKind accessKind)
        {
            var physical = Translate(address, accessKind, write: true, byteCount: 2);
            if (_physicalBus is not IM68kFastMemoryBus fastBus || !CanFastWritePhysical(physical))
            {
                return false;
            }

            return fastBus.TryWriteFastWord(physical, value, accessKind);
        }

        public bool TryWriteFastLong(uint address, uint value, M68kBusAccessKind accessKind)
        {
            var physical = Translate(address, accessKind, write: true, byteCount: 4);
            if (_physicalBus is not IM68kFastMemoryBus fastBus || !CanFastWritePhysical(physical))
            {
                return false;
            }

            return fastBus.TryWriteFastLong(physical, value, accessKind);
        }

        private static bool CanFastReadPhysical(uint physical)
            => (physical & 0x00FF_FFFFu) == 0x00BF_E001u ||
                M68020CpuProfile.ClassifyTarget(physical) is
                M68020MemoryTarget.ExpansionRam or
                M68020MemoryTarget.RealFastRam or
                M68020MemoryTarget.Rom;

        private static bool CanFastWritePhysical(uint physical)
            => (physical & 0x00FF_FFFFu) == 0x00BF_E001u ||
                M68020CpuProfile.ClassifyTarget(physical) is
                M68020MemoryTarget.ExpansionRam or
                M68020MemoryTarget.RealFastRam;

        private uint Translate(uint address, M68kBusAccessKind accessKind, bool write, int byteCount)
        {
            var supervisor = (_state.StatusRegister & M68kCpuState.Supervisor) != 0;
            if (_state.M68040Mmu.TryTranslate(
                address,
                accessKind,
                write,
                supervisor,
                ReadPhysicalLong,
                out var physical,
                out var fault))
            {
                if (!IsPhysicalAddressMapped(physical, byteCount, accessKind))
                {
                    throw new M68040MmuFaultException(
                        WithStackedProgramCounter(
                            CreatePhysicalAddressFault(address, accessKind, write, supervisor),
                            address,
                            accessKind));
                }

                return physical;
            }

            throw new M68040MmuFaultException(
                WithStackedProgramCounter(fault, address, accessKind));
        }

        private bool IsPhysicalAddressMapped(uint physicalAddress, int byteCount, M68kBusAccessKind accessKind)
            => _physicalAddressMap == null ||
                _physicalAddressMap.IsCpuPhysicalAddressMapped(physicalAddress, byteCount, accessKind);

        private static M68040MmuFault CreatePhysicalAddressFault(
            uint logicalAddress,
            M68kBusAccessKind accessKind,
            bool write,
            bool supervisor)
        {
            var status = 0x0000_0010u |
                (write ? 0x0000_0100u : 0) |
                (supervisor ? 0x0000_0200u : 0);
            return new M68040MmuFault(logicalAddress, accessKind, write, status);
        }

        private M68040MmuFault WithStackedProgramCounter(
            M68040MmuFault fault,
            uint logicalAddress,
            M68kBusAccessKind accessKind)
        {
            var stackedProgramCounter = accessKind == M68kBusAccessKind.CpuInstructionFetch
                ? logicalAddress
                : _state.LastInstructionProgramCounter;
            return fault with { StackedProgramCounter = stackedProgramCounter };
        }

        private uint ReadPhysicalLong(uint physicalAddress)
        {
            var bypass = _state.M68040Mmu.BypassTranslation;
            _state.M68040Mmu.BypassTranslation = true;
            try
            {
                if (!IsPhysicalAddressMapped(physicalAddress, 4, M68kBusAccessKind.CpuDataRead))
                {
                    throw new M68kEmulationException(
                        $"MC68040 MMU table read from unmapped physical address 0x{physicalAddress:X8}.");
                }

                var cycle = _state.Cycles;
                return _physicalBus.ReadLong(physicalAddress, ref cycle, M68kBusAccessKind.CpuDataRead);
            }
            finally
            {
                _state.M68040Mmu.BypassTranslation = bypass;
            }
        }
    }

    internal readonly struct M68040ApproximateFallbackCheckpoint
    {
        private readonly uint _d0;
        private readonly uint _d1;
        private readonly uint _d2;
        private readonly uint _d3;
        private readonly uint _d4;
        private readonly uint _d5;
        private readonly uint _d6;
        private readonly uint _d7;
        private readonly uint _a0;
        private readonly uint _a1;
        private readonly uint _a2;
        private readonly uint _a3;
        private readonly uint _a4;
        private readonly uint _a5;
        private readonly uint _a6;
        private readonly uint _a7;
        private readonly uint _programCounter;
        private readonly ushort _statusRegister;
        private readonly uint _userStackPointer;
        private readonly uint _supervisorStackPointer;
        private readonly uint _masterStackPointer;
        private readonly uint _vectorBaseRegister;
        private readonly uint _sourceFunctionCode;
        private readonly uint _destinationFunctionCode;
        private readonly uint _cacheControlRegister;
        private readonly uint _cacheAddressRegister;
        private readonly long _cycles;
        private readonly long _nativeCycles;
        private readonly bool _halted;
        private readonly bool _stopped;
        private readonly ushort _lastOpcode;
        private readonly uint _lastInstructionProgramCounter;

        private M68040ApproximateFallbackCheckpoint(M68kCpuState state)
        {
            _d0 = state.D[0];
            _d1 = state.D[1];
            _d2 = state.D[2];
            _d3 = state.D[3];
            _d4 = state.D[4];
            _d5 = state.D[5];
            _d6 = state.D[6];
            _d7 = state.D[7];
            _a0 = state.A[0];
            _a1 = state.A[1];
            _a2 = state.A[2];
            _a3 = state.A[3];
            _a4 = state.A[4];
            _a5 = state.A[5];
            _a6 = state.A[6];
            _a7 = state.A[7];
            _programCounter = state.ProgramCounter;
            _statusRegister = state.StatusRegister;
            _userStackPointer = state.UserStackPointer;
            _supervisorStackPointer = state.SupervisorStackPointer;
            _masterStackPointer = state.MasterStackPointer;
            _vectorBaseRegister = state.VectorBaseRegister;
            _sourceFunctionCode = state.SourceFunctionCode;
            _destinationFunctionCode = state.DestinationFunctionCode;
            _cacheControlRegister = state.CacheControlRegister;
            _cacheAddressRegister = state.CacheAddressRegister;
            _cycles = state.Cycles;
            _nativeCycles = state.NativeCycles;
            _halted = state.Halted;
            _stopped = state.Stopped;
            _lastOpcode = state.LastOpcode;
            _lastInstructionProgramCounter = state.LastInstructionProgramCounter;
        }

        public static M68040ApproximateFallbackCheckpoint Capture(M68kCpuState state)
            => new(state);

        public void Restore(M68kCpuState state)
        {
            state.D[0] = _d0;
            state.D[1] = _d1;
            state.D[2] = _d2;
            state.D[3] = _d3;
            state.D[4] = _d4;
            state.D[5] = _d5;
            state.D[6] = _d6;
            state.D[7] = _d7;
            state.A[0] = _a0;
            state.A[1] = _a1;
            state.A[2] = _a2;
            state.A[3] = _a3;
            state.A[4] = _a4;
            state.A[5] = _a5;
            state.A[6] = _a6;
            state.ResetStackPointers(
                _supervisorStackPointer,
                _userStackPointer,
                (_statusRegister & M68kCpuState.Supervisor) != 0);
            state.EnableM68020StackMode();
            state.SetMasterStackPointer(_masterStackPointer);
            state.SetInterruptStackPointer(_supervisorStackPointer);
            state.StatusRegister = _statusRegister;
            state.SetActiveStackPointer(_a7);
            state.ProgramCounter = _programCounter;
            state.VectorBaseRegister = _vectorBaseRegister;
            state.SourceFunctionCode = _sourceFunctionCode;
            state.DestinationFunctionCode = _destinationFunctionCode;
            state.CacheControlRegister = _cacheControlRegister;
            state.CacheAddressRegister = _cacheAddressRegister;
            state.Cycles = _cycles;
            state.NativeCycles = _nativeCycles;
            state.Halted = _halted;
            state.Stopped = _stopped;
            state.LastOpcode = _lastOpcode;
            state.LastInstructionProgramCounter = _lastInstructionProgramCounter;
        }
    }

    internal sealed class M68040Interpreter : M68020Interpreter
    {
        private const int VectorBusError = 2;
        private const int VectorLineF = 11;
        private readonly IM68kBus _physicalBus;
        private readonly M68kInterpreter _approximateIntegerFallback;

        public M68040Interpreter(IM68kBus bus)
            : this(bus, M68020CpuProfile.Ocs68040Accelerator25Mhz)
        {
        }

        internal M68040Interpreter(IM68kBus bus, M68020CpuProfile profile)
            : this(bus, profile, new M68kCpuState())
        {
        }

        internal M68040Interpreter(
            IM68kBus bus,
            M68020CpuProfile profile,
            M68kCpuState state,
            M68kInstructionFrequencyMatrix? instructionFrequency = null)
            : base(new M68040LogicalBus(bus, state), profile, state, instructionFrequency)
        {
            _physicalBus = bus ?? throw new ArgumentNullException(nameof(bus));
            if (profile.Model != M68kAcceleratorModel.M68040)
            {
                throw new ArgumentException("The MC68040 interpreter requires an MC68040 CPU profile.", nameof(profile));
            }

            _approximateIntegerFallback = new M68kInterpreter(
                _bus,
                State,
                _instructionFrequency,
                enableOpcodePlan: false);
        }

        public override int ExecuteInstruction()
        {
            var startCycles = State.Cycles;
            try
            {
                return base.ExecuteInstruction();
            }
            catch (M68040MmuFaultException ex)
            {
                RaiseMmuFault(ex.Fault);
                return (int)(State.Cycles - startCycles);
            }
            catch (UnsupportedM68kTimingException ex)
            {
                throw new UnsupportedM68040InstructionException(ex.Opcode, ex.ProgramCounter, _profile.Name, ex);
            }
        }

        public override void Reset(uint programCounter, uint stackPointer)
        {
            base.Reset(programCounter, stackPointer);
            State.M68040Fpu.Reset();
            State.M68040Mmu.Reset();
        }

        protected override bool TryExecuteModelSpecificInstruction(ushort opcode)
        {
            if ((opcode & 0xF000) != 0xF000)
            {
                return false;
            }

            if (TryExecuteMove16(opcode))
            {
                return true;
            }

            if (TryExecuteMmuInstruction(opcode))
            {
                return true;
            }

            return TryExecuteFpuInstruction(opcode);
        }

        protected override bool TryExecuteApproximateInstruction(ushort opcode)
        {
            if ((opcode & 0xF000) is 0xA000 or 0xF000 || opcode == 0x4AFC)
            {
                return false;
            }

            var checkpoint = M68040ApproximateFallbackCheckpoint.Capture(State);
            var startCycles = State.Cycles;
            var startNativeCycles = State.NativeCycles;
            try
            {
                _approximateIntegerFallback.ExecuteInstruction();
                if (_profile.FixedInstructionNativeCycles is int fixedCycles)
                {
                    State.Cycles = startCycles;
                    State.NativeCycles = startNativeCycles;
                    _timing.CompleteInstruction(M68kInstructionPlan.CreateFlat(
                        M68kInstructionTimingKey.Nop,
                        "fixed JIT fallback",
                        fixedCycles));
                }
                else
                {
                    _timing.SynchronizeNativeToMachine();
                }

                State.EnableM68020StackMode();
                return true;
            }
            catch (UnsupportedM68kOpcodeException)
            {
                checkpoint.Restore(State);
                return false;
            }
        }

        protected override bool TryReadControlRegister(int register, uint instructionPc, out uint value)
        {
            switch (register)
            {
                case 0x003:
                    value = State.M68040Mmu.TranslationControl;
                    return true;
                case 0x004:
                    value = State.M68040Mmu.InstructionTransparentTranslation0;
                    return true;
                case 0x005:
                    value = State.M68040Mmu.InstructionTransparentTranslation1;
                    return true;
                case 0x006:
                    value = State.M68040Mmu.DataTransparentTranslation0;
                    return true;
                case 0x007:
                    value = State.M68040Mmu.DataTransparentTranslation1;
                    return true;
                case 0x805:
                    value = State.M68040Mmu.Status;
                    return true;
                case 0x806:
                    value = State.M68040Mmu.UserRootPointer;
                    return true;
                case 0x807:
                    value = State.M68040Mmu.SupervisorRootPointer;
                    return true;
                case 0x808:
                    value = RaiseLineFControlRegister(instructionPc);
                    return false;
                default:
                    return base.TryReadControlRegister(register, instructionPc, out value);
            }
        }

        protected override bool TryWriteControlRegister(int register, uint value, uint instructionPc)
        {
            switch (register)
            {
                case 0x003:
                    State.M68040Mmu.TranslationControl = value;
                    State.M68040Mmu.Flush();
                    return true;
                case 0x004:
                    State.M68040Mmu.InstructionTransparentTranslation0 = value;
                    State.M68040Mmu.Flush();
                    return true;
                case 0x005:
                    State.M68040Mmu.InstructionTransparentTranslation1 = value;
                    State.M68040Mmu.Flush();
                    return true;
                case 0x006:
                    State.M68040Mmu.DataTransparentTranslation0 = value;
                    State.M68040Mmu.Flush();
                    return true;
                case 0x007:
                    State.M68040Mmu.DataTransparentTranslation1 = value;
                    State.M68040Mmu.Flush();
                    return true;
                case 0x805:
                    State.M68040Mmu.Status = value;
                    return true;
                case 0x806:
                    State.M68040Mmu.UserRootPointer = value;
                    State.M68040Mmu.Flush();
                    return true;
                case 0x807:
                    State.M68040Mmu.SupervisorRootPointer = value;
                    State.M68040Mmu.Flush();
                    return true;
                case 0x808:
                    _ = RaiseLineFControlRegister(instructionPc);
                    return false;
                default:
                    return base.TryWriteControlRegister(register, value, instructionPc);
            }
        }

        private uint RaiseLineFControlRegister(uint instructionPc)
        {
            RaiseFormat0Exception(VectorLineF, instructionPc, M68kInstructionTimingKey.LineFException);
            return 0;
        }

        private bool TryExecuteMove16(ushort opcode)
        {
            if ((opcode & 0xFFF8) != 0xF620)
            {
                return false;
            }

            BeginInstruction(opcode);
            _ = FetchWord();
            var sourceRegister = opcode & 7;
            var extension = FetchWord();
            var destinationRegister = (extension >> 12) & 7;
            var source = State.A[sourceRegister] & 0xFFFF_FFF0u;
            var destination = State.A[destinationRegister] & 0xFFFF_FFF0u;
            for (var offset = 0u; offset < 16; offset += 4)
            {
                WriteLong(destination + offset, ReadLong(source + offset));
            }

            State.A[sourceRegister] += 16;
            State.A[destinationRegister] += 16;
            CompleteTiming(M68kInstructionTimingKey.Movec);
            return true;
        }

        private bool TryExecuteMmuInstruction(ushort opcode)
        {
            if ((opcode & 0xFFC0) == 0xF500)
            {
                BeginInstruction(opcode);
                _ = FetchWord();
                _ = FetchWord();
                State.M68040Mmu.Flush();
                CompleteTiming(M68kInstructionTimingKey.Movec);
                return true;
            }

            if ((opcode & 0xFFC0) == 0xF548)
            {
                BeginInstruction(opcode);
                _ = FetchWord();
                var extension = FetchWord();
                var write = (extension & 0x0200) != 0;
                var address = State.A[opcode & 7];
                State.M68040Mmu.Probe(
                    address,
                    M68kBusAccessKind.CpuDataRead,
                    write,
                    (State.StatusRegister & M68kCpuState.Supervisor) != 0,
                    ReadPhysicalLong);
                CompleteTiming(M68kInstructionTimingKey.Movec);
                return true;
            }

            if ((opcode & 0xFF00) == 0xF400)
            {
                BeginInstruction(opcode);
                _ = FetchWord();
                State.M68040Mmu.Flush();
                _timing.Reset();
                CompleteTiming(M68kInstructionTimingKey.Movec);
                return true;
            }

            return false;
        }

        private bool TryExecuteFpuInstruction(ushort opcode)
        {
            if ((opcode & 0xFFC0) == 0xF300)
            {
                BeginInstruction(opcode);
                State.M68040Fpu.Fpiar = State.LastInstructionProgramCounter;
                _ = FetchWord();
                ExecuteFsave(opcode);
                CompleteTiming(M68kInstructionTimingKey.Movec);
                return true;
            }

            if ((opcode & 0xFFC0) == 0xF340)
            {
                BeginInstruction(opcode);
                State.M68040Fpu.Fpiar = State.LastInstructionProgramCounter;
                _ = FetchWord();
                ExecuteFrestore(opcode);
                CompleteTiming(M68kInstructionTimingKey.Movec);
                return true;
            }

            if ((opcode & 0xFFC0) != 0xF200)
            {
                return false;
            }

            BeginInstruction(opcode);
            State.M68040Fpu.Fpiar = State.LastInstructionProgramCounter;
            _ = FetchWord();
            var extension = FetchWord();
            if ((extension & 0xE000) == 0xE000)
            {
                ExecuteFmovem(opcode, extension, registersToMemory: true);
                CompleteTiming(M68kInstructionTimingKey.Movec);
                return true;
            }

            if ((extension & 0xE000) == 0xC000)
            {
                ExecuteFmovem(opcode, extension, registersToMemory: false);
                CompleteTiming(M68kInstructionTimingKey.Movec);
                return true;
            }

            if ((extension & 0xE000) == 0xA000)
            {
                ExecuteFmoveControl(opcode, extension, toControl: true);
                CompleteTiming(M68kInstructionTimingKey.Movec);
                return true;
            }

            if ((extension & 0xE000) == 0x8000)
            {
                ExecuteFmoveControl(opcode, extension, toControl: false);
                CompleteTiming(M68kInstructionTimingKey.Movec);
                return true;
            }

            if ((extension & 0x6000) == 0x6000)
            {
                ExecuteFmoveToEa(opcode, extension);
                CompleteTiming(M68kInstructionTimingKey.Movec);
                return true;
            }

            var source = ((extension & 0x4000) == 0)
                ? State.M68040Fpu.FP[(extension >> 10) & 7]
                : ReadFpuEa(opcode, (extension >> 10) & 7);
            var destination = (extension >> 7) & 7;
            var opmode = extension & 0x007F;
            if (!M68040FpuHelpers.ApplyOperation(State.M68040Fpu, destination, opmode, source))
            {
                RaiseFormat0Exception(VectorLineF, State.LastInstructionProgramCounter, M68kInstructionTimingKey.LineFException);
                return true;
            }

            CompleteTiming(M68kInstructionTimingKey.Movec);
            return true;
        }

        private void ExecuteFsave(ushort opcode)
        {
            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            var address = GetFpuStateFrameAddress(mode, register, M68040FpuHelpers.IdleStateFrameSize);
            var stateFrame = M68040FpuHelpers.CurrentStateFrame(State.M68040Fpu);
            var header = (ushort)(stateFrame >> 16);
            var size = M68040FpuHelpers.StateFrameSize(header);
            State.M68040Fpu.LastStateFrameAddress = address;
            State.M68040Fpu.LastStateFrameHeader = header;
            State.M68040Fpu.LastStateFrameSize = size;
            State.M68040Fpu.LastStateFrameRestore = false;
            WriteLong(address, stateFrame);
            for (var offset = 4u; offset < size; offset += 4)
            {
                WriteLong(address + offset, 0);
            }

            if (mode == 3)
            {
                State.A[register] = address + size;
            }
        }

        private void ExecuteFrestore(ushort opcode)
        {
            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            var address = GetFpuStateFrameAddress(mode, register, M68040FpuHelpers.IdleStateFrameSize);
            var header = ReadWord(address);
            var size = M68040FpuHelpers.StateFrameSize(header);
            State.M68040Fpu.LastStateFrameAddress = address;
            State.M68040Fpu.LastStateFrameHeader = header;
            State.M68040Fpu.LastStateFrameSize = size;
            State.M68040Fpu.LastStateFrameRestore = true;
            State.M68040Fpu.HasExecutedNonConditionalInstruction = !M68040FpuHelpers.IsNullStateFrameHeader(header);
            if (mode == 3)
            {
                State.A[register] = address + size;
            }
        }

        private void ExecuteFmoveControl(ushort opcode, ushort extension, bool toControl)
        {
            State.M68040Fpu.HasExecutedNonConditionalInstruction = true;
            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            if (mode != 0)
            {
                var address = GetFpuMemoryAddress(mode, register, 4);
                if (toControl)
                {
                    WriteLong(address, State.M68040Fpu.Fpcr);
                }
                else
                {
                    State.M68040Fpu.Fpcr = ReadLong(address);
                }

                return;
            }

            if ((extension & 0x1000) != 0)
            {
                if (toControl)
                {
                    State.M68040Fpu.Fpcr = State.D[register];
                }
                else
                {
                    State.D[register] = State.M68040Fpu.Fpcr;
                }
            }

            if ((extension & 0x0800) != 0)
            {
                if (toControl)
                {
                    State.M68040Fpu.Fpsr = State.D[register];
                }
                else
                {
                    State.D[register] = State.M68040Fpu.Fpsr;
                }
            }

            if ((extension & 0x0400) != 0)
            {
                if (toControl)
                {
                    State.M68040Fpu.Fpiar = State.D[register];
                }
                else
                {
                    State.D[register] = State.M68040Fpu.Fpiar;
                }
            }
        }

        private void ExecuteFmovem(ushort opcode, ushort extension, bool registersToMemory)
        {
            State.M68040Fpu.HasExecutedNonConditionalInstruction = true;
            var mask = (byte)(extension & 0x00FF);
            var byteSize = (uint)(CountFpuRegisterMaskBits(mask) * 12);
            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            var address = GetFpuMemoryAddress(mode, register, byteSize);
            for (var fpRegister = 0; fpRegister < 8; fpRegister++)
            {
                if ((mask & (0x80 >> fpRegister)) == 0)
                {
                    continue;
                }

                if (registersToMemory)
                {
                    WriteFpuExtendedSlot(address, State.M68040Fpu.FP[fpRegister]);
                }
                else
                {
                    State.M68040Fpu.FP[fpRegister] = ReadFpuExtendedSlot(address);
                }

                address += 12;
            }
        }

        private void WriteFpuExtendedSlot(uint address, double value)
        {
            var bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
            WriteLong(address, 0);
            WriteLong(address + 4, (uint)(bits >> 32));
            WriteLong(address + 8, (uint)bits);
        }

        private double ReadFpuExtendedSlot(uint address)
        {
            var bits = unchecked(((ulong)ReadLong(address + 4) << 32) | ReadLong(address + 8));
            return BitConverter.Int64BitsToDouble(unchecked((long)bits));
        }

        private static int CountFpuRegisterMaskBits(byte mask)
        {
            var count = 0;
            while (mask != 0)
            {
                mask &= (byte)(mask - 1);
                count++;
            }

            return count;
        }

        private void ExecuteFmoveToEa(ushort opcode, ushort extension)
        {
            State.M68040Fpu.HasExecutedNonConditionalInstruction = true;
            var source = (extension >> 7) & 7;
            var format = (extension >> 10) & 7;
            WriteFpuEa(opcode, format, State.M68040Fpu.FP[source]);
        }

        private double ReadFpuEa(ushort opcode, int format)
        {
            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            if (mode == 0)
            {
                return ReadFpuDataRegister(State.D[register], format);
            }

            if (mode == 7 && register == 4)
            {
                return ReadFpuImmediate(format);
            }

            var address = GetFpuMemoryAddress(mode, register, FpuFormatByteSize(format));
            return format switch
            {
                0 => unchecked((int)ReadLong(address)),
                1 => BitConverter.Int32BitsToSingle(unchecked((int)ReadLong(address))),
                4 => unchecked((short)ReadWord(address)),
                5 => BitConverter.Int64BitsToDouble(unchecked((long)(((ulong)ReadLong(address) << 32) | ReadLong(address + 4)))),
                6 => unchecked((sbyte)ReadByte(address)),
                _ => throw new UnsupportedM68kTimingException(opcode, State.LastInstructionProgramCounter, _profile)
            };
        }

        private void WriteFpuEa(ushort opcode, int format, double value)
        {
            var mode = (opcode >> 3) & 7;
            var register = opcode & 7;
            if (mode == 0)
            {
                State.D[register] = format switch
                {
                    0 or 1 or 4 or 6 => M68040FpuHelpers.WriteDataRegister(State.D[register], format, value),
                    _ => throw new UnsupportedM68kTimingException(opcode, State.LastInstructionProgramCounter, _profile)
                };
                return;
            }

            var address = GetFpuMemoryAddress(mode, register, FpuFormatByteSize(format));
            switch (format)
            {
                case 0:
                    WriteLong(address, unchecked((uint)(int)value));
                    break;
                case 1:
                    WriteLong(address, unchecked((uint)BitConverter.SingleToInt32Bits((float)value)));
                    break;
                case 4:
                    WriteWord(address, unchecked((ushort)(short)value));
                    break;
                case 5:
                    var bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
                    WriteLong(address, (uint)(bits >> 32));
                    WriteLong(address + 4, (uint)bits);
                    break;
                case 6:
                    WriteByte(address, unchecked((byte)(sbyte)value));
                    break;
                default:
                    throw new UnsupportedM68kTimingException(opcode, State.LastInstructionProgramCounter, _profile);
            }
        }

        private double ReadFpuDataRegister(uint value, int format)
        {
            return format switch
            {
                0 or 1 or 4 or 6 => M68040FpuHelpers.ReadDataRegister(value, format),
                _ => throw new UnsupportedM68kTimingException(State.LastOpcode, State.LastInstructionProgramCounter, _profile)
            };
        }

        private double ReadFpuImmediate(int format)
        {
            return format switch
            {
                0 => unchecked((int)FetchLong()),
                1 => BitConverter.Int32BitsToSingle(unchecked((int)FetchLong())),
                4 => unchecked((short)FetchWord()),
                5 => BitConverter.Int64BitsToDouble(unchecked((long)(((ulong)FetchLong() << 32) | FetchLong()))),
                6 => unchecked((sbyte)(byte)FetchWord()),
                _ => throw new UnsupportedM68kTimingException(State.LastOpcode, State.LastInstructionProgramCounter, _profile)
            };
        }

        private static uint FpuFormatByteSize(int format)
        {
            return M68040FpuHelpers.FormatByteSize(format);
        }

        private uint GetFpuMemoryAddress(int mode, int register, uint byteSize)
        {
            return mode switch
            {
                2 => State.A[register],
                3 => PostIncrementAddress(register, byteSize),
                4 => PredecrementAddress(register, byteSize),
                5 => unchecked((uint)(State.A[register] + (int)(short)FetchWord())),
                7 when register == 1 => FetchLong(),
                _ => throw new UnsupportedM68kTimingException(State.LastOpcode, State.LastInstructionProgramCounter, _profile)
            };
        }

        private uint GetFpuStateFrameAddress(int mode, int register, uint byteSize)
        {
            return mode switch
            {
                2 => State.A[register],
                3 => State.A[register],
                4 => PredecrementAddress(register, byteSize),
                5 => unchecked((uint)(State.A[register] + (int)(short)FetchWord())),
                7 when register == 1 => FetchLong(),
                _ => throw new UnsupportedM68kTimingException(State.LastOpcode, State.LastInstructionProgramCounter, _profile)
            };
        }

        private uint PostIncrementAddress(int register, uint byteSize)
        {
            var address = State.A[register];
            State.A[register] += register == 7 && byteSize == 1 ? 2u : byteSize;
            return address;
        }

        private uint PredecrementAddress(int register, uint byteSize)
        {
            State.A[register] -= register == 7 && byteSize == 1 ? 2u : byteSize;
            return State.A[register];
        }

        private void RaiseMmuFault(M68040MmuFault fault)
        {
            var bypass = State.M68040Mmu.BypassTranslation;
            State.M68040Mmu.BypassTranslation = true;
            try
            {
                State.M68040Mmu.Status = fault.Status;
                var stackedProgramCounter = fault.StackedProgramCounter ??
                    (fault.AccessKind == M68kBusAccessKind.CpuInstructionFetch
                        ? fault.LogicalAddress
                        : State.LastInstructionProgramCounter);
                RaiseFormat0Exception(VectorBusError, stackedProgramCounter, M68kInstructionTimingKey.IllegalInstruction);
            }
            finally
            {
                State.M68040Mmu.BypassTranslation = bypass;
            }
        }

        private uint ReadPhysicalLong(uint physicalAddress)
        {
            var cycle = State.Cycles;
            return _physicalBus.ReadLong(physicalAddress, ref cycle, M68kBusAccessKind.CpuDataRead);
        }
    }
}
