using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CopperMod.Amiga
{
    internal readonly struct CustomRegisterWrite
    {
        public CustomRegisterWrite(long cycle, ushort address, ushort value)
        {
            Cycle = cycle;
            Address = address;
            Value = value;
        }

        public long Cycle { get; }

        public ushort Address { get; }

        public ushort Value { get; }
    }

    internal sealed class AmigaBus : IM68kBus
    {
        private const int MaxCapturedBusAccesses = 65536;
        private const int MaxPendingInterruptEvents = 65536;
        private const int CodeGenerationPageShift = 8;
        private const int CodeGenerationPageSize = 1 << CodeGenerationPageShift;
        private const uint MinimumChipRamDecodeSize = 0x0020_0000;
        private readonly byte[] _chipRam;
        private readonly byte[] _expansionRam;
        private readonly byte[] _realFastRam;
        private readonly uint[] _chipCodePageGenerations;
        private readonly uint[] _expansionCodePageGenerations;
        private readonly uint[] _realFastCodePageGenerations;
        private readonly Dictionary<uint, Action<M68kCpuState>> _hostCallbacks = new Dictionary<uint, Action<M68kCpuState>>();
        private readonly List<MappedMemoryRegion> _mappedMemoryRegions = new List<MappedMemoryRegion>();
        private readonly List<AmigaCiaInterruptEvent> _pendingCiaInterrupts = new List<AmigaCiaInterruptEvent>();
        private readonly AmigaCiaInterruptEvent[] _drainedCiaInterruptBuffer = new AmigaCiaInterruptEvent[MaxPendingInterruptEvents];
        private readonly ReusableReadOnlyList<AmigaCiaInterruptEvent> _drainedCiaInterrupts = new ReusableReadOnlyList<AmigaCiaInterruptEvent>();
        private readonly BoundedBusAccessLog _busAccesses = new BoundedBusAccessLog(MaxCapturedBusAccesses);
        private readonly ChipPresentationWriteHistory _presentationWriteHistory;
        private readonly AgnusChipSlotScheduler _legacyChipSlots = new AgnusChipSlotScheduler();
        private readonly IAgnusChipSlotTiming _diagnosticChipSlots;
        private readonly AgnusSlotEngine? _slotEngine;
        private readonly AgnusShadowCompareSlotTiming? _shadowCompareSlotTiming;
        private readonly bool _captureBusAccesses;
        private readonly bool _useFastZeroWaitAccesses;
        private readonly bool _useChipSlotScheduler;
        private readonly bool _liveAgnusDmaDefault;
        private readonly byte[] _pendingCustomBytes = new byte[0x200];
        private readonly bool[] _pendingCustomByteWritten = new bool[0x200];
        private readonly GamePortState[] _gamePorts = { new GamePortState(), new GamePortState() };
        private readonly uint _chipRamDecodeSize;
        private readonly long _palFrameCycles;
        private readonly long _palLineCycles;
        private MappedMemoryRegion? _romOverlayRegion;
        private bool _romOverlayEnabled = true;
        private long _nextVerticalBlankCycle;
        private long _nextHorizontalSyncIndex;
        private long _nextHorizontalSyncCycle;
        private long _lastRasterAdvanceCycle;
        private uint _codeGenerationClock = 1;

        internal event Action<uint, int>? JitEligibleMemoryWritten;

        public AmigaBus(
            int chipRamSize = AmigaConstants.DefaultChipRamSize,
            IAmigaBusArbiter? arbiter = null,
            int expansionRamSize = 0,
            uint expansionRamBase = AmigaConstants.A500BootPseudoFastRamBase,
            int floppyDriveCount = 1,
            bool captureBusAccesses = true,
            bool enableLiveAgnusDma = true,
            bool enableLiveDisplayDma = true,
            int audioDmaMinimumPeriod = AmigaConstants.A500PalMinimumAudioDmaPeriod,
            AgnusTimingMode agnusTimingMode = AgnusTimingMode.SlotEngine,
            int realFastRamSize = 0,
            uint realFastRamBase = AmigaConstants.A500RealFastRamBase,
            bool enableHardwareSpecialization = false)
        {
            if (chipRamSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chipRamSize), chipRamSize, "Chip RAM size must be positive.");
            }

            if (chipRamSize > AmigaConstants.MaxChipRamSize)
            {
                throw new ArgumentOutOfRangeException(nameof(chipRamSize), chipRamSize, "Chip RAM size cannot exceed the custom-chip DMA address space.");
            }

            if (!IsPowerOfTwo(chipRamSize))
            {
                throw new ArgumentOutOfRangeException(nameof(chipRamSize), chipRamSize, "Chip RAM size must be a power of two so custom-chip DMA address masking is well-defined.");
            }

            if (expansionRamSize < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expansionRamSize), expansionRamSize, "Expansion RAM size cannot be negative.");
            }

            if (realFastRamSize < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(realFastRamSize), realFastRamSize, "Real fast RAM size cannot be negative.");
            }

            if (audioDmaMinimumPeriod <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(audioDmaMinimumPeriod), audioDmaMinimumPeriod, "Audio DMA minimum period must be positive.");
            }

            _chipRam = new byte[chipRamSize];
            _expansionRam = new byte[expansionRamSize];
            _realFastRam = new byte[realFastRamSize];
            _chipCodePageGenerations = new uint[Math.Max(1, (chipRamSize + CodeGenerationPageSize - 1) >> CodeGenerationPageShift)];
            _expansionCodePageGenerations = new uint[Math.Max(1, (expansionRamSize + CodeGenerationPageSize - 1) >> CodeGenerationPageShift)];
            _realFastCodePageGenerations = new uint[Math.Max(1, (realFastRamSize + CodeGenerationPageSize - 1) >> CodeGenerationPageShift)];
            _slotEngine = agnusTimingMode == AgnusTimingMode.SlotEngine
                ? new AgnusSlotEngine()
                : null;
            _shadowCompareSlotTiming = agnusTimingMode == AgnusTimingMode.ShadowCompare
                ? new AgnusShadowCompareSlotTiming()
                : null;
            _diagnosticChipSlots = _slotEngine ??
                (IAgnusChipSlotTiming?)_shadowCompareSlotTiming ??
                _legacyChipSlots;
            _presentationWriteHistory = new ChipPresentationWriteHistory(chipRamSize);
            _captureBusAccesses = captureBusAccesses;
            _liveAgnusDmaDefault = enableLiveAgnusDma;
            _chipRamDecodeSize = Math.Max(MinimumChipRamDecodeSize, (uint)chipRamSize);
            ChipDmaAddressMask = ((uint)chipRamSize - 1u) & 0x00FF_FFFEu;
            ExpansionRamBase = NormalizeAddress(expansionRamBase);
            RealFastRamBase = NormalizeAddress(realFastRamBase);
            Arbiter = arbiter ?? new ZeroWaitBusArbiter();
            _useChipSlotScheduler = Arbiter is ZeroWaitBusArbiter zeroWaitForSlots &&
                zeroWaitForSlots.BaseAccessCycles == 0;
            _useFastZeroWaitAccesses =
                !captureBusAccesses &&
                _useChipSlotScheduler;
            Paula = new Paula(this);
            Disk = new AmigaDiskController(this, floppyDriveCount, enableHardwareSpecialization);
            Display = new OcsDisplay(this, enableLiveDisplayDma);
            Agnus = new AgnusBeamDmaScheduler(this, _diagnosticChipSlots);
            Blitter = new AmigaBlitter(this, enableHardwareSpecialization);
            CiaA = new AmigaCia(AmigaCiaId.A);
            CiaB = new AmigaCia(AmigaCiaId.B);
            Keyboard = new AmigaKeyboard((rawKey, cycle) => CiaA.SetSerialData(rawKey, cycle, _pendingCiaInterrupts));
            _palFrameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
            _palLineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;
            _nextVerticalBlankCycle = _palFrameCycles;
            _lastRasterAdvanceCycle = 0;
            ResetHorizontalSyncCounter();
            CiaA.Reset(0x02);
            CiaB.Reset();
            LiveAgnusDmaEnabled = _liveAgnusDmaDefault;
            AgnusTimingMode = agnusTimingMode;
            AudioDmaMinimumPeriod = audioDmaMinimumPeriod;
        }

        public Paula Paula { get; }

        public AmigaDiskController Disk { get; }

        public OcsDisplay Display { get; }

        internal AgnusBeamDmaScheduler Agnus { get; }

        public AmigaBlitter Blitter { get; }

        public IAmigaBusArbiter Arbiter { get; }

        public AmigaCia CiaA { get; }

        public AmigaCia CiaB { get; }

        public AmigaKeyboard Keyboard { get; }

        public bool AudioFilterEnabled { get; private set; }

        public bool GamePort0FirePressed
        {
            get => _gamePorts[0].PrimaryFirePressed;
            set => _gamePorts[0].PrimaryFirePressed = value;
        }

        public bool GamePort1FirePressed
        {
            get => _gamePorts[1].PrimaryFirePressed;
            set => _gamePorts[1].PrimaryFirePressed = value;
        }

        public bool GamePort0SecondFirePressed
        {
            get => _gamePorts[0].SecondFirePressed;
            set => _gamePorts[0].SecondFirePressed = value;
        }

        public bool GamePort1SecondFirePressed
        {
            get => _gamePorts[1].SecondFirePressed;
            set => _gamePorts[1].SecondFirePressed = value;
        }

        public byte[] ChipRam => _chipRam;

        public byte[] ExpansionRam => _expansionRam;

        public byte[] RealFastRam => _realFastRam;

        public uint ChipDmaAddressMask { get; }

        public uint ExpansionRamBase { get; }

        public uint RealFastRamBase { get; }

        public IReadOnlyList<CustomRegisterWrite> CustomRegisterWrites => Paula.Writes;

        public IReadOnlyList<AmigaBusAccessResult> BusAccesses => _busAccesses;

        public long CiaBTimerAIntervalCycles => CiaB.TimerAIntervalCycles;

        internal bool LiveAgnusDmaEnabled { get; private set; }

        internal bool LiveDisplayDmaEnabled => Display.LiveDmaEnabled;

        internal int AudioDmaMinimumPeriod { get; }

        internal AgnusTimingMode AgnusTimingMode { get; }

        internal void EnableLiveAgnusDma()
        {
            LiveAgnusDmaEnabled = true;
        }

        internal void SetLiveAgnusDmaEnabled(bool enabled)
        {
            LiveAgnusDmaEnabled = enabled;
        }

        public long NextChipSlotCycle(long cycle)
        {
            return AgnusChipSlotScheduler.AlignToSlot(cycle);
        }

        internal long FindSlotEngineDmaCandidate(long requestedCycle)
        {
            var candidate = NextChipSlotCycle(Math.Max(0, requestedCycle));
            if (_slotEngine == null)
            {
                return candidate;
            }

            while (_slotEngine.IsReserved(candidate))
            {
                candidate += AgnusChipSlotScheduler.SlotCycles;
            }

            return candidate;
        }

        internal bool IsSlotEngineChipSlotReserved(long cycle)
        {
            return _slotEngine != null && _slotEngine.IsReserved(cycle);
        }

        public void Reset()
        {
            Array.Clear(_chipRam);
            Array.Clear(_expansionRam);
            Array.Clear(_realFastRam);
            Array.Clear(_pendingCustomBytes);
            Array.Clear(_pendingCustomByteWritten);
            _hostCallbacks.Clear();
            _mappedMemoryRegions.Clear();
            _romOverlayRegion = null;
            _romOverlayEnabled = true;
            _pendingCiaInterrupts.Clear();
            _busAccesses.Clear();
            ClearChipSlots();
            LiveAgnusDmaEnabled = _liveAgnusDmaDefault;
            CiaA.Reset(0x02);
            CiaB.Reset();
            Keyboard.Reset();
            AudioFilterEnabled = false;
            foreach (var gamePort in _gamePorts)
            {
                gamePort.Reset();
            }

            Paula.Reset();
            Disk.Reset();
            Display.Reset();
            Agnus.Reset();
            Blitter.Reset();
            _nextVerticalBlankCycle = _palFrameCycles;
            _lastRasterAdvanceCycle = 0;
            ResetHorizontalSyncCounter();
        }

        public void MoveGamePortMouse(int port, int deltaX, int deltaY)
        {
            var gamePort = GetGamePort(port);
            gamePort.MouseXCounter = unchecked((byte)(gamePort.MouseXCounter + deltaX));
            gamePort.MouseYCounter = unchecked((byte)(gamePort.MouseYCounter + deltaY));
        }

        public void SetGamePortJoystick(int port, bool up, bool down, bool left, bool right)
        {
            var gamePort = GetGamePort(port);
            gamePort.JoystickUp = up;
            gamePort.JoystickDown = down;
            gamePort.JoystickLeft = left;
            gamePort.JoystickRight = right;
        }

        public void RegisterHostCallback(uint address, Action<M68kCpuState> callback)
        {
            _hostCallbacks[address] = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public bool TryInvokeHost(uint address, M68kCpuState state)
        {
            address = NormalizeAddress(address);
            if (!_hostCallbacks.TryGetValue(address, out var callback))
            {
                return false;
            }

            var access = Arbitrate(
                AmigaBusRequester.Cpu,
                AmigaBusAccessKind.HostTrap,
                AmigaBusAccessTarget.HostTrap,
                address,
                AmigaBusAccessSize.Word,
                state.Cycles,
                isWrite: false);
            state.Cycles = access.CompletedCycle;
            callback(state);
            return true;
        }

        internal bool HasHostCallback(uint address)
        {
            return _hostCallbacks.ContainsKey(NormalizeAddress(address));
        }

        public void ResetExternalDevices(long cycle)
        {
            _ = cycle;
            Array.Clear(_pendingCustomBytes);
            Array.Clear(_pendingCustomByteWritten);
            _pendingCiaInterrupts.Clear();
            _busAccesses.Clear();
            ClearChipSlots();
            LiveAgnusDmaEnabled = _liveAgnusDmaDefault;
            _romOverlayEnabled = true;
            AudioFilterEnabled = false;
            Paula.Reset();
            Disk.Reset();
            Display.Reset();
            Agnus.Reset();
            Blitter.Reset();
            _nextVerticalBlankCycle = _palFrameCycles;
            _lastRasterAdvanceCycle = 0;
            ResetHorizontalSyncCounter();
        }

        public void MapReadOnlyMemory(uint baseAddress, ReadOnlySpan<byte> data)
        {
            MapMemory(baseAddress, data, readOnly: true);
        }

        public void MapWritableMemory(uint baseAddress, ReadOnlySpan<byte> data)
        {
            MapMemory(baseAddress, data, readOnly: false);
        }

        private void MapMemory(uint baseAddress, ReadOnlySpan<byte> data, bool readOnly)
        {
            if (data.IsEmpty)
            {
                throw new ArgumentException("Mapped memory cannot be empty.", nameof(data));
            }

            var copy = data.ToArray();
            var region = new MappedMemoryRegion(baseAddress, copy, readOnly);
            _mappedMemoryRegions.Add(region);
            if (readOnly && baseAddress + (uint)copy.Length == 0x0100_0000)
            {
                _romOverlayRegion = region;
            }
        }

        public byte ReadByte(uint address)
        {
            address = NormalizeAddress(address);
            var value = ReadRawByte(address);
            if (TryGetCiaRegister(address, out var cia, out var ciaRegister) && cia == CiaA && ciaRegister == 0x0C)
            {
                Keyboard.AcknowledgeSerialDataRead(0);
            }

            return value;
        }

        public byte ReadByte(uint address, ref long cycle, AmigaBusAccessKind accessKind)
        {
            return ReadByte(address, ref cycle, accessKind, sampleCustomAtGrantedCycle: true);
        }

        private byte ReadByte(
            uint address,
            ref long cycle,
            AmigaBusAccessKind accessKind,
            bool sampleCustomAtGrantedCycle)
        {
            address = NormalizeAddress(address);
            var target = ClassifyTarget(address);
            if (_useFastZeroWaitAccesses)
            {
                var fastAccess = GrantFastCpuAccess(target, address, AmigaBusAccessSize.Byte, cycle, accessKind, isWrite: false);
                AdvanceDmaBeforeCpuChipAccess(target, fastAccess.GrantedCycle);
                var fastValue = sampleCustomAtGrantedCycle && target == AmigaBusAccessTarget.CustomRegisters
                    ? ReadRawByte(address, fastAccess.GrantedCycle)
                    : ReadRawByte(address);
                cycle = fastAccess.CompletedCycle;
                if (TryGetCiaRegister(address, out var fastCia, out var fastCiaRegister) && fastCia == CiaA && fastCiaRegister == 0x0C)
                {
                    Keyboard.AcknowledgeSerialDataRead(cycle);
                }

                return fastValue;
            }

            var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, target, address, AmigaBusAccessSize.Byte, cycle, isWrite: false);
            AdvanceDmaBeforeCpuChipAccess(target, access.GrantedCycle);
            var value = sampleCustomAtGrantedCycle && target == AmigaBusAccessTarget.CustomRegisters
                ? ReadRawByte(address, access.GrantedCycle)
                : ReadRawByte(address);
            cycle = access.CompletedCycle;
            if (TryGetCiaRegister(address, out var cia, out var ciaRegister) && cia == CiaA && ciaRegister == 0x0C)
            {
                Keyboard.AcknowledgeSerialDataRead(cycle);
            }

            return value;
        }

        public void WriteByte(uint address, byte value, long cycle)
        {
            WriteByte(address, value, ref cycle, AmigaBusAccessKind.CpuDataWrite);
        }

        public void WriteByte(uint address, byte value, ref long cycle, AmigaBusAccessKind accessKind)
        {
            address = NormalizeAddress(address);
            var target = ClassifyTarget(address);
            if (_useFastZeroWaitAccesses)
            {
                var fastAccess = GrantFastCpuAccess(target, address, AmigaBusAccessSize.Byte, cycle, accessKind, isWrite: true);
                AdvanceDmaBeforeCpuChipAccess(target, fastAccess.GrantedCycle);
                WriteRawByte(address, value, fastAccess.GrantedCycle);
                cycle = fastAccess.CompletedCycle;
                return;
            }

            var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, target, address, AmigaBusAccessSize.Byte, cycle, isWrite: true);
            AdvanceDmaBeforeCpuChipAccess(target, access.GrantedCycle);
            WriteRawByte(address, value, access.GrantedCycle);
            cycle = access.CompletedCycle;
        }

        public void WriteWord(uint address, ushort value, long cycle)
        {
            WriteWord(address, value, ref cycle, AmigaBusAccessKind.CpuDataWrite);
        }

        public void WriteWord(uint address, ushort value, ref long cycle, AmigaBusAccessKind accessKind)
        {
            address = NormalizeAddress(address);
            var target = ClassifyTarget(address);
            if (_useFastZeroWaitAccesses)
            {
                var fastAccess = GrantFastCpuAccess(target, address, AmigaBusAccessSize.Word, cycle, accessKind, isWrite: true);
                AdvanceDmaBeforeCpuChipAccess(target, fastAccess.GrantedCycle);
                WriteRawWord(address, value, fastAccess.GrantedCycle);
                cycle = fastAccess.CompletedCycle;
                return;
            }

            var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, target, address, AmigaBusAccessSize.Word, cycle, isWrite: true);
            AdvanceDmaBeforeCpuChipAccess(target, access.GrantedCycle);
            WriteRawWord(address, value, access.GrantedCycle);
            cycle = access.CompletedCycle;
        }

        public ushort ReadWord(uint address)
        {
            return ReadHostWord(address);
        }

        public ushort ReadWord(uint address, ref long cycle, AmigaBusAccessKind accessKind)
        {
            return ReadWord(address, ref cycle, accessKind, sampleCustomAtGrantedCycle: true);
        }

        private ushort ReadWord(
            uint address,
            ref long cycle,
            AmigaBusAccessKind accessKind,
            bool sampleCustomAtGrantedCycle)
        {
            address = NormalizeAddress(address);
            var target = ClassifyTarget(address);
            if (_useFastZeroWaitAccesses)
            {
                var fastAccess = GrantFastCpuAccess(target, address, AmigaBusAccessSize.Word, cycle, accessKind, isWrite: false);
                AdvanceDmaBeforeCpuChipAccess(target, fastAccess.GrantedCycle);
                var fastValue = sampleCustomAtGrantedCycle && target == AmigaBusAccessTarget.CustomRegisters
                    ? ReadRawWord(address, fastAccess.GrantedCycle)
                    : ReadRawWord(address);
                cycle = fastAccess.CompletedCycle;
                return fastValue;
            }

            var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, target, address, AmigaBusAccessSize.Word, cycle, isWrite: false);
            AdvanceDmaBeforeCpuChipAccess(target, access.GrantedCycle);
            var value = sampleCustomAtGrantedCycle && target == AmigaBusAccessTarget.CustomRegisters
                ? ReadRawWord(address, access.GrantedCycle)
                : ReadRawWord(address);
            cycle = access.CompletedCycle;
            return value;
        }

        public uint ReadLong(uint address)
        {
            return ReadHostLong(address);
        }

        public uint ReadLong(uint address, ref long cycle, AmigaBusAccessKind accessKind)
        {
            return ReadLong(address, ref cycle, accessKind, sampleCustomAtGrantedCycle: true);
        }

        private uint ReadLong(
            uint address,
            ref long cycle,
            AmigaBusAccessKind accessKind,
            bool sampleCustomAtGrantedCycle)
        {
            address = NormalizeAddress(address);
            var target = ClassifyTarget(address);
            if (_useFastZeroWaitAccesses)
            {
                var fastAccess = GrantFastCpuAccess(target, address, AmigaBusAccessSize.Long, cycle, accessKind, isWrite: false);
                AdvanceDmaBeforeCpuChipAccess(target, fastAccess.GrantedCycle);
                var fastValue = sampleCustomAtGrantedCycle && target == AmigaBusAccessTarget.CustomRegisters
                    ? ReadRawLong(address, fastAccess.GrantedCycle, GetSecondWordCycle(fastAccess))
                    : ReadRawLong(address);
                cycle = fastAccess.CompletedCycle;
                return fastValue;
            }

            var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, target, address, AmigaBusAccessSize.Long, cycle, isWrite: false);
            AdvanceDmaBeforeCpuChipAccess(target, access.GrantedCycle);
            var value = sampleCustomAtGrantedCycle && target == AmigaBusAccessTarget.CustomRegisters
                ? ReadRawLong(address, access.GrantedCycle, GetSecondWordCycle(access))
                : ReadRawLong(address);
            cycle = access.CompletedCycle;
            return value;
        }

        public void WriteWord(uint address, ushort value)
        {
            WriteHostWord(address, value);
        }

        public void WriteLong(uint address, uint value)
        {
            WriteHostLong(address, value);
        }

        public void WriteLong(uint address, uint value, long cycle)
        {
            WriteLong(address, value, ref cycle, AmigaBusAccessKind.CpuDataWrite);
        }

        public void WriteLong(uint address, uint value, ref long cycle, AmigaBusAccessKind accessKind)
        {
            address = NormalizeAddress(address);
            var target = ClassifyTarget(address);
            if (_useFastZeroWaitAccesses)
            {
                var fastAccess = GrantFastCpuAccess(target, address, AmigaBusAccessSize.Long, cycle, accessKind, isWrite: true);
                AdvanceDmaBeforeCpuChipAccess(target, fastAccess.GrantedCycle);
                WriteRawLong(address, value, fastAccess.GrantedCycle, GetSecondWordCycle(fastAccess));
                cycle = fastAccess.CompletedCycle;
                return;
            }

            var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, target, address, AmigaBusAccessSize.Long, cycle, isWrite: true);
            AdvanceDmaBeforeCpuChipAccess(target, access.GrantedCycle);
            WriteRawLong(address, value, access.GrantedCycle, GetSecondWordCycle(access));
            cycle = access.CompletedCycle;
        }

        public void CopyToChipRam(uint address, ReadOnlySpan<byte> data)
        {
            if (address + data.Length > _chipRam.Length)
            {
                throw new AmigaEmulationException("Load data does not fit in the emulated chip RAM map.");
            }

            data.CopyTo(_chipRam.AsSpan((int)address, data.Length));
            TouchCodePages(address, data.Length);
        }

        internal byte ReadHostByte(uint address)
        {
            return ReadRawByte(address);
        }

        internal ushort ReadHostWord(uint address)
        {
            return ReadRawWord(address);
        }

        internal uint ReadHostLong(uint address)
        {
            return ReadRawLong(address);
        }

        internal void WriteHostByte(uint address, byte value)
        {
            WriteRawByte(address, value, 0);
        }

        internal void WriteHostWord(uint address, ushort value)
        {
            WriteRawWord(address, value, 0);
        }

        internal void WriteHostLong(uint address, uint value)
        {
            WriteRawLong(address, value, 0, 0);
        }

        public void CopyToMemory(uint address, ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty)
            {
                return;
            }

            address = NormalizeAddress(address);
            if (TryGetContiguousWritableSpan(address, data.Length, out var destination))
            {
                data.CopyTo(destination);
                TouchCodePages(address, data.Length);
                NotifyJitEligibleMemoryWritten(address, data.Length);
                return;
            }

            for (var i = 0; i < data.Length; i++)
            {
                WriteByte(address + (uint)i, data[i], 0);
            }
        }

        public void ClearMemory(uint address, int byteCount)
        {
            if (byteCount <= 0)
            {
                return;
            }

            address = NormalizeAddress(address);
            if (TryGetContiguousWritableSpan(address, byteCount, out var destination))
            {
                destination.Clear();
                TouchCodePages(address, byteCount);
                NotifyJitEligibleMemoryWritten(address, byteCount);
                return;
            }

            for (var i = 0; i < byteCount; i++)
            {
                WriteByte(address + (uint)i, 0, 0);
            }
        }

        public bool IsMappedMemoryRange(uint address, int byteCount)
        {
            if (byteCount < 0)
            {
                return false;
            }

            if (byteCount == 0)
            {
                return true;
            }

            address = NormalizeAddress(address);
            return IsChipRamRange(address, byteCount) ||
                IsExpansionRamRange(address, byteCount) ||
                IsRealFastRamRange(address, byteCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint MaskChipDmaAddress(uint address)
        {
            return address & ChipDmaAddressMask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint WriteChipDmaPointerHigh(uint pointer, ushort highWord)
        {
            return (((uint)highWord << 16) | (pointer & 0x0000_FFFEu)) & ChipDmaAddressMask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint WriteChipDmaPointerLow(uint pointer, ushort lowWord)
        {
            return ((pointer & 0x00FF_0000u) | (uint)(lowWord & 0xFFFE)) & ChipDmaAddressMask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint AddChipDmaPointerOffset(uint pointer, int byteOffset)
        {
            return (pointer + unchecked((uint)byteOffset)) & ChipDmaAddressMask;
        }

        public PaulaDmaReadResult ReadPaulaDmaWord(uint address, long requestedCycle)
        {
            address = MaskChipDmaAddress(address);
            var access = Arbitrate(
                AmigaBusRequester.Paula,
                AmigaBusAccessKind.PaulaDma,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                isWrite: false);
            var rawValue = ReadChipDmaWord(address);
            var value = rawValue;
            if (_slotEngine != null)
            {
                value = ReadChipWordForPresentation(address, access.GrantedCycle);
            }
            else if (_shadowCompareSlotTiming != null)
            {
                var shadowAccess = _shadowCompareSlotTiming.GetShadowResultFor(access);
                var shadowValue = ReadChipWordForPresentation(address, shadowAccess.GrantedCycle);
                if (shadowValue != rawValue)
                {
                    _shadowCompareSlotTiming.RecordDataDivergence(access, shadowAccess, rawValue, shadowValue);
                }
            }

            return new PaulaDmaReadResult(value, access);
        }

        public ushort ReadChipWordForDevice(AmigaBusRequester requester, AmigaBusAccessKind kind, uint address, long requestedCycle)
        {
            return ReadChipWordForDeviceWithResult(requester, kind, address, requestedCycle).Value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadChipWordForPresentation(uint address)
        {
            address = MaskChipDmaAddress(address);
            return ReadChipDmaWord(address);
        }

        public ushort ReadChipWordForPresentation(uint address, long cycle)
        {
            address = MaskChipDmaAddress(address);
            if (!_presentationWriteHistory.HasWrites)
            {
                return ReadChipDmaWord(address);
            }

            if (!_presentationWriteHistory.MayNeedPresentationRead(cycle))
            {
                return ReadChipDmaWord(address);
            }

            var offset = (int)(address & (uint)(_chipRam.Length - 1));
            var nextOffset = (offset + 1) & (_chipRam.Length - 1);
            if (!_presentationWriteHistory.NeedsPresentationRead(offset, cycle) &&
                !_presentationWriteHistory.NeedsPresentationRead(nextOffset, cycle))
            {
                return ReadChipDmaWord(address);
            }

            var high = _presentationWriteHistory.ReadByte(_chipRam, offset, cycle);
            var low = _presentationWriteHistory.ReadByte(_chipRam, nextOffset, cycle);
            return (ushort)((high << 8) | low);
        }

        public bool TryReadDisplayDmaWordForPresentation(
            AmigaBusRequester requester,
            AmigaBusAccessKind kind,
            uint address,
            long requestedCycle,
            out ushort value,
            out AmigaBusAccessResult access)
        {
            address = MaskChipDmaAddress(address);
            requestedCycle = Math.Max(0, requestedCycle);
            var request = new AmigaBusAccessRequest(
                requester,
                kind,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                isWrite: false);

            bool granted;
            if (!_useChipSlotScheduler)
            {
                access = new AmigaBusAccessResult(request, requestedCycle, requestedCycle);
                granted = true;
            }
            else
            {
                granted = TryReserveFixedDmaSlot(request, out access);
            }

            if (!granted)
            {
                value = 0;
                return false;
            }

            if (_captureBusAccesses)
            {
                _busAccesses.Add(access);
            }

            value = ReadChipWordForPresentation(address, access.GrantedCycle);
            return true;
        }

        public bool TryReadDisplayDmaWord(
            AmigaBusRequester requester,
            AmigaBusAccessKind kind,
            uint address,
            long requestedCycle,
            out ushort value,
            out AmigaBusAccessResult access)
        {
            address = MaskChipDmaAddress(address);
            requestedCycle = Math.Max(0, requestedCycle);
            var request = new AmigaBusAccessRequest(
                requester,
                kind,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                isWrite: false);

            bool granted;
            if (!_useChipSlotScheduler)
            {
                access = new AmigaBusAccessResult(request, requestedCycle, requestedCycle);
                granted = true;
            }
            else
            {
                granted = TryReserveFixedDmaSlot(request, out access);
            }

            if (!granted)
            {
                value = 0;
                return false;
            }

            if (_captureBusAccesses)
            {
                _busAccesses.Add(access);
            }

            value = ReadChipWordForPresentation(address, access.GrantedCycle);
            return true;
        }

        public ushort ReadLiveBitplaneDmaWord(uint address, long requestedCycle, out long grantedCycle)
        {
            return TryReadLiveBitplaneDmaWord(address, requestedCycle, out var value, out grantedCycle)
                ? value
                : (ushort)0;
        }

        public bool TryReadLiveBitplaneDmaWord(uint address, long requestedCycle, out ushort value, out long grantedCycle)
        {
            address = MaskChipDmaAddress(address);
            requestedCycle = Math.Max(0, requestedCycle);
            AmigaBusAccessResult access;
            bool granted;
            if (!_useChipSlotScheduler)
            {
                var request = new AmigaBusAccessRequest(
                    AmigaBusRequester.Bitplane,
                    AmigaBusAccessKind.Bitplane,
                    AmigaBusAccessTarget.ChipRam,
                    address,
                    AmigaBusAccessSize.Word,
                    requestedCycle,
                    isWrite: false);
                access = new AmigaBusAccessResult(request, requestedCycle, requestedCycle);
                granted = true;
            }
            else
            {
                if (_slotEngine != null)
                {
                    access = _slotEngine.ReserveBitplaneDmaSlot(address, requestedCycle);
                    granted = access.CompletedCycle > access.GrantedCycle;
                }
                else
                {
                    var request = new AmigaBusAccessRequest(
                        AmigaBusRequester.Bitplane,
                        AmigaBusAccessKind.Bitplane,
                        AmigaBusAccessTarget.ChipRam,
                        address,
                        AmigaBusAccessSize.Word,
                        requestedCycle,
                        isWrite: false);
                    granted = TryReserveFixedDmaSlot(request, out access);
                }
            }

            if (_captureBusAccesses)
            {
                _busAccesses.Add(access);
            }

            grantedCycle = access.GrantedCycle;
            if (!granted)
            {
                value = 0;
                return false;
            }

            value = ReadChipWordForPresentation(address, access.GrantedCycle);
            return true;
        }

        public ushort ReadLiveCopperDmaWord(uint address, long requestedCycle, out AmigaBusAccessResult access)
        {
            address = MaskChipDmaAddress(address);
            requestedCycle = Math.Max(0, requestedCycle);
            if (!_useChipSlotScheduler)
            {
                var request = new AmigaBusAccessRequest(
                    AmigaBusRequester.Copper,
                    AmigaBusAccessKind.Copper,
                    AmigaBusAccessTarget.ChipRam,
                    address,
                    AmigaBusAccessSize.Word,
                    requestedCycle,
                    isWrite: false);
                access = new AmigaBusAccessResult(request, requestedCycle, requestedCycle);
            }
            else
            {
                if (_slotEngine != null && LiveAgnusDmaEnabled)
                {
                    Display.CaptureLiveDisplayDmaBeforeSlotEngineGrant(requestedCycle);
                }

                access = ReserveCopperDmaSlot(address, requestedCycle);
            }

            if (_captureBusAccesses)
            {
                _busAccesses.Add(access);
            }

            return ReadChipWordForPresentation(address, access.GrantedCycle);
        }

        public ushort ReadChipWordForPresentationWithArbitration(
            AmigaBusRequester requester,
            AmigaBusAccessKind kind,
            uint address,
            long requestedCycle,
            out AmigaBusAccessResult access)
        {
            address = MaskChipDmaAddress(address);
            access = Arbitrate(
                requester,
                kind,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                isWrite: false);
            return ReadChipWordForPresentation(address, access.GrantedCycle);
        }

        public bool TryReserveDisplayDmaSlot(
            AmigaBusRequester requester,
            AmigaBusAccessKind kind,
            uint address,
            long requestedCycle,
            out AmigaBusAccessResult access)
        {
            address = MaskChipDmaAddress(address);
            requestedCycle = Math.Max(0, requestedCycle);
            var request = new AmigaBusAccessRequest(
                requester,
                kind,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                isWrite: false);

            if (!_useChipSlotScheduler)
            {
                access = new AmigaBusAccessResult(request, requestedCycle, requestedCycle);
                return true;
            }

            return TryReserveFixedDmaSlot(request, out access);
        }

        public void ClearPresentationWriteHistory()
        {
            _presentationWriteHistory.Clear();
        }

        public AmigaDeviceWordReadResult ReadChipWordForDeviceWithResult(
            AmigaBusRequester requester,
            AmigaBusAccessKind kind,
            uint address,
            long requestedCycle)
        {
            address = MaskChipDmaAddress(address);
            var access = Arbitrate(requester, kind, AmigaBusAccessTarget.ChipRam, address, AmigaBusAccessSize.Word, requestedCycle, isWrite: false);
            var rawValue = ReadChipDmaWord(address);
            var value = rawValue;
            if (_slotEngine != null)
            {
                value = ReadChipWordForPresentation(address, access.GrantedCycle);
            }
            else if (_shadowCompareSlotTiming != null)
            {
                var shadowAccess = _shadowCompareSlotTiming.GetShadowResultFor(access);
                var shadowValue = ReadChipWordForPresentation(address, shadowAccess.GrantedCycle);
                if (shadowValue != rawValue)
                {
                    _shadowCompareSlotTiming.RecordDataDivergence(access, shadowAccess, rawValue, shadowValue);
                }
            }

            return new AmigaDeviceWordReadResult(value, access);
        }

        public void WriteChipWordForDevice(AmigaBusRequester requester, AmigaBusAccessKind kind, uint address, ushort value, long requestedCycle)
        {
            _ = WriteChipWordForDeviceWithResult(requester, kind, address, value, requestedCycle);
        }

        public AmigaBusAccessResult WriteChipWordForDeviceWithResult(
            AmigaBusRequester requester,
            AmigaBusAccessKind kind,
            uint address,
            ushort value,
            long requestedCycle)
        {
            address = MaskChipDmaAddress(address);
            var access = Arbitrate(requester, kind, AmigaBusAccessTarget.ChipRam, address, AmigaBusAccessSize.Word, requestedCycle, isWrite: true);
            WriteChipDmaWord(address, value, access.GrantedCycle);
            return access;
        }

        public void WriteDeviceWord(AmigaBusRequester requester, AmigaBusAccessKind kind, uint address, ushort value, long requestedCycle)
        {
            address = NormalizeAddress(address);
            var access = Arbitrate(requester, kind, ClassifyTarget(address), address, AmigaBusAccessSize.Word, requestedCycle, isWrite: true);
            WriteRawWord(address, value, access.GrantedCycle);
        }

        public void AdvanceRasterTo(long targetCycle)
        {
            _lastRasterAdvanceCycle = Math.Max(_lastRasterAdvanceCycle, targetCycle);
            while (_nextHorizontalSyncCycle <= targetCycle)
            {
                CiaB.IncrementTod(_nextHorizontalSyncCycle, _pendingCiaInterrupts);
                _nextHorizontalSyncIndex++;
                _nextHorizontalSyncCycle = Math.Max(
                    _nextHorizontalSyncCycle + 1,
                    _nextHorizontalSyncIndex * _palLineCycles);
            }

            if (targetCycle < _nextVerticalBlankCycle)
            {
                return;
            }

            while (_nextVerticalBlankCycle <= targetCycle)
            {
                CiaA.IncrementTod(_nextVerticalBlankCycle, _pendingCiaInterrupts);
                WriteDeviceWord(
                    AmigaBusRequester.Bitplane,
                    AmigaBusAccessKind.CustomRegister,
                    0x00DFF09C,
                    (ushort)(0x8000 | AmigaConstants.IntreqVerticalBlank),
                    _nextVerticalBlankCycle);
                _nextVerticalBlankCycle += _palFrameCycles;
            }
        }

        private void UpdateBeamPosition(long targetCycle)
        {
            var cycleInFrame = Math.Max(0, targetCycle) % _palFrameCycles;
            var line = Math.Clamp((int)(cycleInFrame / _palLineCycles), 0, AmigaConstants.A500PalRasterLines - 1);
            var lineCycle = cycleInFrame - (line * _palLineCycles);
            var horizontal = Math.Clamp((int)(lineCycle / AmigaConstants.A500PalCpuCyclesPerColorClock), 0, 0xE2);
            var frame = Math.Max(0, targetCycle) / _palFrameCycles;
            Paula.SetBeamPosition(line, horizontal, (frame & 1) != 0);
        }

        private void ResetHorizontalSyncCounter()
        {
            _nextHorizontalSyncIndex = 1;
            _nextHorizontalSyncCycle = Math.Max(1, _palLineCycles);
        }

        public void AdvanceCiasTo(long targetCycle)
        {
            Disk.AdvanceTo(targetCycle);
            CiaA.AdvanceTo(targetCycle, _pendingCiaInterrupts);
            CiaB.AdvanceTo(targetCycle, _pendingCiaInterrupts);
        }

        public void AdvanceDmaTo(long targetCycle)
        {
            AdvanceDmaTo(targetCycle, advanceLiveAgnus: true);
        }

        public void AdvanceDmaTo(long targetCycle, bool advanceLiveAgnus)
        {
            Paula.AdvanceTo(targetCycle);
            Disk.AdvanceTo(targetCycle);
            if (advanceLiveAgnus && LiveAgnusDmaEnabled)
            {
                Agnus.AdvanceTo(targetCycle);
            }

            Blitter.AdvanceTo(targetCycle);
            Paula.AdvanceTo(targetCycle);
        }

        private void AdvanceDmaBeforeCpuChipAccess(AmigaBusAccessTarget target, long grantedCycle)
        {
            if (target == AmigaBusAccessTarget.ChipRam ||
                target == AmigaBusAccessTarget.ExpansionRam ||
                target == AmigaBusAccessTarget.CustomRegisters)
            {
                AdvanceDmaTo(grantedCycle);
                return;
            }

            if (target == AmigaBusAccessTarget.Cia)
            {
                AdvanceCiasTo(grantedCycle);
            }
        }

        public long? GetNextCiaInterruptCycle(long maxCycle)
        {
            var ciaA = CiaA.GetNextInterruptCycle(maxCycle);
            var ciaB = CiaB.GetNextInterruptCycle(maxCycle);
            if (!ciaA.HasValue)
            {
                return ciaB;
            }

            if (!ciaB.HasValue)
            {
                return ciaA;
            }

            return Math.Min(ciaA.Value, ciaB.Value);
        }

        public long GetNextStoppedCpuWakeCandidateCycle(long currentCycle, long targetCycle)
        {
            currentCycle = Math.Max(0, currentCycle);
            targetCycle = Math.Max(currentCycle, targetCycle);
            if (targetCycle <= currentCycle)
            {
                return currentCycle;
            }

            var candidate = targetCycle;
            if (_pendingCiaInterrupts.Count != 0 || Paula.GetHighestPendingInterruptLevel() > 0)
            {
                candidate = currentCycle + 1;
            }

            candidate = MinStoppedWakeCandidate(candidate, currentCycle, targetCycle, _nextVerticalBlankCycle);
            candidate = MinStoppedWakeCandidate(
                candidate,
                currentCycle,
                targetCycle,
                CiaB.GetNextTodInterruptCycle(targetCycle, _nextHorizontalSyncCycle, _palLineCycles));
            candidate = MinStoppedWakeCandidate(candidate, currentCycle, targetCycle, GetNextCiaInterruptCycle(targetCycle));
            candidate = MinStoppedWakeCandidate(candidate, currentCycle, targetCycle, Disk.GetNextWakeCandidateCycle(currentCycle, targetCycle));
            candidate = MinStoppedWakeCandidate(candidate, currentCycle, targetCycle, Paula.GetNextWakeCandidateCycle(currentCycle, targetCycle));
            candidate = MinStoppedWakeCandidate(candidate, currentCycle, targetCycle, Blitter.GetNextWakeCandidateCycle(currentCycle, targetCycle));
            return Math.Clamp(candidate, currentCycle + 1, targetCycle);
        }

        private static long MinStoppedWakeCandidate(
            long candidate,
            long currentCycle,
            long targetCycle,
            long? eventCycle)
        {
            if (!eventCycle.HasValue || eventCycle.Value > targetCycle)
            {
                return candidate;
            }

            var cycle = eventCycle.Value <= currentCycle ? currentCycle + 1 : eventCycle.Value;
            return Math.Min(candidate, cycle);
        }

        public AmigaCia GetCia(AmigaCiaId id)
        {
            return id == AmigaCiaId.A ? CiaA : CiaB;
        }

        public byte AbleCiaInterrupts(AmigaCiaId id, byte value, long cycle)
        {
            return GetCia(id).AbleInterrupts(value, cycle, _pendingCiaInterrupts);
        }

        public byte SetCiaInterrupts(AmigaCiaId id, byte value, long cycle)
        {
            return GetCia(id).SetInterrupts(value, cycle, _pendingCiaInterrupts);
        }

        public void PulseCiaFlag(AmigaCiaId id, long cycle)
        {
            GetCia(id).PulseFlag(cycle, _pendingCiaInterrupts);
        }

        public IReadOnlyList<AmigaCiaInterruptEvent> DrainCiaInterrupts()
        {
            if (_pendingCiaInterrupts.Count == 0)
            {
                return Array.Empty<AmigaCiaInterruptEvent>();
            }

            var count = Math.Min(_pendingCiaInterrupts.Count, _drainedCiaInterruptBuffer.Length);
            for (var i = 0; i < count; i++)
            {
                _drainedCiaInterruptBuffer[i] = _pendingCiaInterrupts[i];
            }

            SortCiaInterrupts(_drainedCiaInterruptBuffer, count);
            _pendingCiaInterrupts.Clear();
            _drainedCiaInterrupts.Reset(_drainedCiaInterruptBuffer, count);
            return _drainedCiaInterrupts;
        }

        private static void SortCiaInterrupts(AmigaCiaInterruptEvent[] events, int count)
        {
            for (var i = 1; i < count; i++)
            {
                var value = events[i];
                var j = i - 1;
                while (j >= 0 && CompareCiaInterrupts(events[j], value) > 0)
                {
                    events[j + 1] = events[j];
                    j--;
                }

                events[j + 1] = value;
            }
        }

        private static int CompareCiaInterrupts(AmigaCiaInterruptEvent left, AmigaCiaInterruptEvent right)
        {
            var cycleCompare = left.Cycle.CompareTo(right.Cycle);
            return cycleCompare != 0 ? cycleCompare : left.Cia.CompareTo(right.Cia);
        }

        private AmigaBusAccessResult Arbitrate(
            AmigaBusRequester requester,
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite)
        {
            if (requester == AmigaBusRequester.Cpu &&
                (target == AmigaBusAccessTarget.ChipRam ||
                    target == AmigaBusAccessTarget.ExpansionRam ||
                    target == AmigaBusAccessTarget.CustomRegisters))
            {
                AdvanceDmaTo(requestedCycle);
                requestedCycle = Blitter.AdvanceThroughCpuStall(requestedCycle);
            }

            requestedCycle = Math.Max(0, requestedCycle);

            if (_useFastZeroWaitAccesses)
            {
                var fastRequest = new AmigaBusAccessRequest(
                    requester,
                    kind,
                    target,
                    address,
                    size,
                    requestedCycle,
                    isWrite);
                var fastResult = new AmigaBusAccessResult(fastRequest, requestedCycle, requestedCycle);
                return ShouldUseChipSlotScheduler(target)
                    ? ArbitrateChipSlot(fastRequest, fastResult)
                    : fastResult;
            }

            var request = new AmigaBusAccessRequest(
                requester,
                kind,
                target,
                address,
                size,
                requestedCycle,
                isWrite);
            var result = Arbiter.Arbitrate(request);
            if (ShouldUseChipSlotScheduler(target))
            {
                result = ArbitrateChipSlot(request, result);
            }

            if (_captureBusAccesses)
            {
                _busAccesses.Add(result);
            }

            Agnus.RecordCpuChipAccess(result);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private AmigaBusAccessResult GrantFastCpuAccess(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            AmigaBusAccessKind kind,
            bool isWrite)
        {
            if (target == AmigaBusAccessTarget.ChipRam ||
                target == AmigaBusAccessTarget.ExpansionRam ||
                target == AmigaBusAccessTarget.CustomRegisters)
            {
                AdvanceDmaTo(requestedCycle);
                requestedCycle = Blitter.AdvanceThroughCpuStall(requestedCycle);
            }

            requestedCycle = Math.Max(0, requestedCycle);
            var request = new AmigaBusAccessRequest(
                AmigaBusRequester.Cpu,
                kind,
                target,
                address,
                size,
                requestedCycle,
                isWrite);
            if (!_useChipSlotScheduler || !ShouldUseChipSlotScheduler(target))
            {
                return new AmigaBusAccessResult(request, requestedCycle, requestedCycle);
            }

            var result = ArbitrateChipSlot(request, new AmigaBusAccessResult(request, requestedCycle, requestedCycle));
            Agnus.RecordCpuChipAccess(result);
            return result;
        }

        private void ClearChipSlots()
        {
            if (_slotEngine != null)
            {
                _slotEngine.Clear();
                return;
            }

            if (_shadowCompareSlotTiming != null)
            {
                _shadowCompareSlotTiming.Clear();
                return;
            }

            _legacyChipSlots.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private AmigaBusAccessResult ArbitrateChipSlot(AmigaBusAccessRequest request, AmigaBusAccessResult baseResult)
        {
            if (_slotEngine != null)
            {
                if (request.Requester == AmigaBusRequester.Cpu &&
                    LiveAgnusDmaEnabled &&
                    (request.Target == AmigaBusAccessTarget.ChipRam ||
                        request.Target == AmigaBusAccessTarget.ExpansionRam ||
                        request.Target == AmigaBusAccessTarget.CustomRegisters))
                {
                    return ArbitrateSlotEngineCpuAccess(request, baseResult);
                }

                if (LiveAgnusDmaEnabled &&
                    Display.HasLiveDisplayWork() &&
                    request.Size == AmigaBusAccessSize.Word &&
                    ShouldCaptureLiveDisplayBeforeSlotEngineDeviceAccess(request))
                {
                    Display.CaptureLiveDisplayDmaBeforeSlotEngineGrant(Math.Max(baseResult.GrantedCycle, request.RequestedCycle));
                }

                return _slotEngine.Arbitrate(request, baseResult);
            }

            if (_shadowCompareSlotTiming != null)
            {
                return _shadowCompareSlotTiming.Arbitrate(request, baseResult);
            }

            return _legacyChipSlots.Arbitrate(request, baseResult);
        }

        private AmigaBusAccessResult ArbitrateSlotEngineCpuAccess(AmigaBusAccessRequest request, AmigaBusAccessResult baseResult)
        {
            var slotCount = request.Size == AmigaBusAccessSize.Long ? 2 : 1;
            var candidate = AgnusChipSlotScheduler.AlignToSlot(Math.Max(baseResult.GrantedCycle, request.RequestedCycle));
            var hasLiveDisplayWork = Display.HasLiveDisplayWork();
            while (true)
            {
                var lastSlot = candidate + ((slotCount - 1) * AgnusChipSlotScheduler.SlotCycles);
                if (hasLiveDisplayWork && !Display.HasLiveDmaCapturedThrough(lastSlot))
                {
                    AdvanceDmaTo(lastSlot);
                }

                if (hasLiveDisplayWork)
                {
                    Display.CaptureLiveDisplayDmaBeforeSlotEngineGrant(candidate);
                    if (slotCount > 1)
                    {
                        Display.CaptureLiveDisplayDmaBeforeSlotEngineGrant(lastSlot);
                    }
                }

                var available = true;
                for (var slot = 0; slot < slotCount; slot++)
                {
                    if (_slotEngine!.IsReserved(candidate + (slot * AgnusChipSlotScheduler.SlotCycles)))
                    {
                        available = false;
                        break;
                    }
                }

                if (available)
                {
                    break;
                }

                candidate += AgnusChipSlotScheduler.SlotCycles;
            }

            var adjustedBase = new AmigaBusAccessResult(
                baseResult.Request,
                candidate,
                Math.Max(baseResult.CompletedCycle, candidate));
            return _slotEngine!.Arbitrate(request, adjustedBase);
        }

        private static bool ShouldCaptureLiveDisplayBeforeSlotEngineDeviceAccess(AmigaBusAccessRequest request)
        {
            if (request.Target != AmigaBusAccessTarget.ChipRam &&
                request.Target != AmigaBusAccessTarget.ExpansionRam &&
                request.Target != AmigaBusAccessTarget.CustomRegisters)
            {
                return false;
            }

            return request.Requester == AmigaBusRequester.Blitter ||
                request.Requester == AmigaBusRequester.Copper ||
                request.Requester == AmigaBusRequester.Paula ||
                request.Requester == AmigaBusRequester.Disk ||
                request.Requester == AmigaBusRequester.Host;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryReserveFixedDmaSlot(AmigaBusAccessRequest request, out AmigaBusAccessResult result)
        {
            if (_slotEngine != null)
            {
                return _slotEngine.TryReserveFixedDmaSlot(request, out result);
            }

            if (_shadowCompareSlotTiming != null)
            {
                return _shadowCompareSlotTiming.TryReserveFixedDmaSlot(request, out result);
            }

            return _legacyChipSlots.TryReserveFixedDmaSlot(request, out result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private AmigaBusAccessResult ReserveBitplaneDmaSlot(uint address, long requestedCycle)
        {
            if (_slotEngine != null)
            {
                return _slotEngine.ReserveBitplaneDmaSlot(address, requestedCycle);
            }

            if (_shadowCompareSlotTiming != null)
            {
                return _shadowCompareSlotTiming.ReserveBitplaneDmaSlot(address, requestedCycle);
            }

            return _legacyChipSlots.ReserveBitplaneDmaSlot(address, requestedCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private AmigaBusAccessResult ReserveCopperDmaSlot(uint address, long requestedCycle)
        {
            if (_slotEngine != null)
            {
                return _slotEngine.ReserveCopperDmaSlot(address, requestedCycle);
            }

            if (_shadowCompareSlotTiming != null)
            {
                return _shadowCompareSlotTiming.ReserveCopperDmaSlot(address, requestedCycle);
            }

            return _legacyChipSlots.ReserveCopperDmaSlot(address, requestedCycle);
        }

        private bool ShouldUseChipSlotScheduler(AmigaBusAccessTarget target)
        {
            if (!_useChipSlotScheduler)
            {
                return false;
            }

            if (target == AmigaBusAccessTarget.ChipRam ||
                target == AmigaBusAccessTarget.ExpansionRam)
            {
                return LiveAgnusDmaEnabled || !_useFastZeroWaitAccesses;
            }

            return target == AmigaBusAccessTarget.CustomRegisters && LiveAgnusDmaEnabled;
        }

        private AmigaBusAccessTarget ClassifyTarget(uint address)
        {
            if (IsRomOverlayAddress(address))
            {
                return AmigaBusAccessTarget.Rom;
            }

            if (IsChipRamAddress(address))
            {
                return AmigaBusAccessTarget.ChipRam;
            }

            if (IsRealFastRamAddress(address))
            {
                return AmigaBusAccessTarget.RealFastRam;
            }

            if (IsExpansionRamAddress(address))
            {
                return AmigaBusAccessTarget.ExpansionRam;
            }

            if (address >= 0x00DFF000 && address < 0x00DFF200)
            {
                return AmigaBusAccessTarget.CustomRegisters;
            }

            if (TryGetCiaRegister(address, out _, out _))
            {
                return AmigaBusAccessTarget.Cia;
            }

            if (_hostCallbacks.ContainsKey(address))
            {
                return AmigaBusAccessTarget.HostTrap;
            }

            for (var i = _mappedMemoryRegions.Count - 1; i >= 0; i--)
            {
                if (_mappedMemoryRegions[i].Contains(address))
                {
                    return AmigaBusAccessTarget.Rom;
                }
            }

            return AmigaBusAccessTarget.Unmapped;
        }

        private byte ReadRawByte(uint address)
        {
            return ReadRawByte(address, long.MinValue);
        }

        private byte ReadRawByte(uint address, long customRegisterSampleCycle)
        {
            address = NormalizeAddress(address);
            if (TryReadRomOverlayByte(address, out var overlayValue))
            {
                return overlayValue;
            }

            if (TryGetChipRamOffset(address, out var chipOffset))
            {
                return _chipRam[chipOffset];
            }

            if (TryGetExpansionRamOffset(address, out var expansionOffset))
            {
                return _expansionRam[expansionOffset];
            }

            if (TryGetRealFastRamOffset(address, out var realFastOffset))
            {
                return _realFastRam[realFastOffset];
            }

            if (address >= 0x00DFF000 && address < 0x00DFF200)
            {
                var offset = (ushort)(address - 0x00DFF000);
                UpdateBeamPositionForCustomRead(offset, customRegisterSampleCycle);
                if (TryReadGamePortCustomByte(offset, out var gamePortValue))
                {
                    return gamePortValue;
                }

                if (Display.TryReadByte(offset, out var displayValue))
                {
                    return displayValue;
                }

                return Disk.TryReadByte(offset, out var diskValue) ? diskValue : Paula.ReadByte(offset);
            }

            if (TryGetCiaRegister(address, out var cia, out var ciaRegister))
            {
                if (cia == CiaA && ciaRegister == 0)
                {
                    var inputPins = Disk.ReadCiaAPortA(0xFF);
                    inputPins = ApplyGamePortFireBits(inputPins);
                    return cia.ReadPortRegister(ciaRegister, inputPins);
                }

                return cia.ReadRegister(ciaRegister);
            }

            for (var i = _mappedMemoryRegions.Count - 1; i >= 0; i--)
            {
                if (_mappedMemoryRegions[i].TryReadByte(address, out var value))
                {
                    return value;
                }
            }

            return 0;
        }

        private void UpdateBeamPositionForCustomRead(ushort offset, long cycle)
        {
            if (offset < 0x004 ||
                offset > 0x007)
            {
                return;
            }

            UpdateBeamPosition(cycle == long.MinValue ? _lastRasterAdvanceCycle : cycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ushort ReadChipDmaWord(uint address)
        {
            var offset = (int)(address & ChipDmaAddressMask);
            var nextOffset = (offset + 1) & (_chipRam.Length - 1);
            return (ushort)((_chipRam[offset] << 8) | _chipRam[nextOffset]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteChipDmaWord(uint address, ushort value, long grantedCycle)
        {
            var offset = (int)(address & ChipDmaAddressMask);
            var nextOffset = (offset + 1) & (_chipRam.Length - 1);
            _presentationWriteHistory.RecordByte(offset, _chipRam[offset], (byte)(value >> 8), grantedCycle);
            _presentationWriteHistory.RecordByte(nextOffset, _chipRam[nextOffset], (byte)value, grantedCycle);
            _chipRam[offset] = (byte)(value >> 8);
            _chipRam[nextOffset] = (byte)value;
        }

        private ushort ReadRawWord(uint address)
        {
            address = NormalizeAddress(address);
            if (!IsRomOverlayAddress(address) && TryGetChipRamOffset(address, out var chipOffset))
            {
                var nextOffset = (chipOffset + 1) & (_chipRam.Length - 1);
                return (ushort)((_chipRam[chipOffset] << 8) | _chipRam[nextOffset]);
            }

            if (TryGetExpansionRamOffset(address, out var expansionOffset) &&
                expansionOffset + 1 < _expansionRam.Length)
            {
                return (ushort)((_expansionRam[expansionOffset] << 8) | _expansionRam[expansionOffset + 1]);
            }

            if (TryGetRealFastRamOffset(address, out var realFastOffset) &&
                realFastOffset + 1 < _realFastRam.Length)
            {
                return (ushort)((_realFastRam[realFastOffset] << 8) | _realFastRam[realFastOffset + 1]);
            }

            return (ushort)((ReadRawByte(address) << 8) | ReadRawByte(address + 1));
        }

        private ushort ReadRawWord(uint address, long customRegisterSampleCycle)
        {
            address = NormalizeAddress(address);
            if (!IsRomOverlayAddress(address) && TryGetChipRamOffset(address, out var chipOffset))
            {
                var nextOffset = (chipOffset + 1) & (_chipRam.Length - 1);
                return (ushort)((_chipRam[chipOffset] << 8) | _chipRam[nextOffset]);
            }

            if (TryGetExpansionRamOffset(address, out var expansionOffset) &&
                expansionOffset + 1 < _expansionRam.Length)
            {
                return (ushort)((_expansionRam[expansionOffset] << 8) | _expansionRam[expansionOffset + 1]);
            }

            if (TryGetRealFastRamOffset(address, out var realFastOffset) &&
                realFastOffset + 1 < _realFastRam.Length)
            {
                return (ushort)((_realFastRam[realFastOffset] << 8) | _realFastRam[realFastOffset + 1]);
            }

            return (ushort)((ReadRawByte(address, customRegisterSampleCycle) << 8) |
                ReadRawByte(address + 1, customRegisterSampleCycle));
        }

        private uint ReadRawLong(uint address)
        {
            return ((uint)ReadRawWord(address) << 16) | ReadRawWord(address + 2);
        }

        private uint ReadRawLong(uint address, long firstWordCycle, long secondWordCycle)
        {
            return ((uint)ReadRawWord(address, firstWordCycle) << 16) | ReadRawWord(address + 2, secondWordCycle);
        }

        private void WriteRawByte(uint address, byte value, long grantedCycle)
        {
            address = NormalizeAddress(address);
            if (TryGetChipRamOffset(address, out var chipOffset))
            {
                _presentationWriteHistory.RecordByte(chipOffset, _chipRam[chipOffset], value, grantedCycle);
                _chipRam[chipOffset] = value;
                TouchCodePage(address);
                return;
            }

            if (TryGetExpansionRamOffset(address, out var expansionOffset))
            {
                _expansionRam[expansionOffset] = value;
                TouchCodePage(address);
                NotifyJitEligibleMemoryWritten(address, 1);
                return;
            }

            if (TryGetRealFastRamOffset(address, out var realFastOffset))
            {
                _realFastRam[realFastOffset] = value;
                TouchCodePage(address);
                NotifyJitEligibleMemoryWritten(address, 1);
                return;
            }

            if (address >= 0x00DFF000 && address < 0x00DFF200)
            {
                WriteCustomByte((ushort)(address - 0x00DFF000), value, grantedCycle);
                return;
            }

            if (TryGetCiaRegister(address, out var cia, out var ciaRegister))
            {
                cia.WriteRegister(ciaRegister, value, grantedCycle, _pendingCiaInterrupts);
                if (cia == CiaA && ciaRegister == 0)
                {
                    AudioFilterEnabled = (value & 0x02) == 0;
                    _romOverlayEnabled = (value & 0x01) != 0;
                }

                if (cia == CiaB && ciaRegister is 1 or 3)
                {
                    Disk.WriteCiaBRegister(1, cia.ReadPortRegister(1, 0xFF), grantedCycle);
                }

                return;
            }

            for (var i = _mappedMemoryRegions.Count - 1; i >= 0; i--)
            {
                if (_mappedMemoryRegions[i].TryWriteByte(address, value))
                {
                    return;
                }
            }
        }

        private void WriteRawWord(uint address, ushort value, long grantedCycle)
        {
            address = NormalizeAddress(address);
            if (address >= 0x00DFF000 && address + 1 < 0x00DFF200)
            {
                Paula.ScheduleWrite(grantedCycle, (ushort)(address - 0x00DFF000), value);
                Display.ScheduleWrite(grantedCycle, (ushort)(address - 0x00DFF000), value);
                Blitter.WriteRegister((ushort)(address - 0x00DFF000), value, grantedCycle);
                Disk.WriteRegister((ushort)(address - 0x00DFF000), value, grantedCycle);
                return;
            }

            if (TryGetChipRamOffset(address, out var chipOffset))
            {
                var nextOffset = (chipOffset + 1) & (_chipRam.Length - 1);
                _presentationWriteHistory.RecordByte(chipOffset, _chipRam[chipOffset], (byte)(value >> 8), grantedCycle);
                _presentationWriteHistory.RecordByte(nextOffset, _chipRam[nextOffset], (byte)value, grantedCycle);
                _chipRam[chipOffset] = (byte)(value >> 8);
                _chipRam[nextOffset] = (byte)value;
                TouchCodePage(address);
                TouchCodePage(address + 1);
                return;
            }

            if (TryGetExpansionRamOffset(address, out var expansionOffset) &&
                expansionOffset + 1 < _expansionRam.Length)
            {
                _expansionRam[expansionOffset] = (byte)(value >> 8);
                _expansionRam[expansionOffset + 1] = (byte)value;
                TouchCodePage(address);
                TouchCodePage(address + 1);
                NotifyJitEligibleMemoryWritten(address, 2);
                return;
            }

            if (TryGetRealFastRamOffset(address, out var realFastOffset) &&
                realFastOffset + 1 < _realFastRam.Length)
            {
                _realFastRam[realFastOffset] = (byte)(value >> 8);
                _realFastRam[realFastOffset + 1] = (byte)value;
                TouchCodePage(address);
                TouchCodePage(address + 1);
                NotifyJitEligibleMemoryWritten(address, 2);
                return;
            }

            WriteRawByte(address, (byte)(value >> 8), grantedCycle);
            WriteRawByte(address + 1, (byte)value, grantedCycle);
        }

        private void WriteRawLong(uint address, uint value, long firstWordCycle, long secondWordCycle)
        {
            WriteRawWord(address, (ushort)(value >> 16), firstWordCycle);
            WriteRawWord(address + 2, (ushort)value, secondWordCycle);
        }

        private void NotifyJitEligibleMemoryWritten(uint address, int byteCount)
        {
            var handler = JitEligibleMemoryWritten;
            if (handler == null || byteCount <= 0)
            {
                return;
            }

            address = NormalizeAddress(address);
            if (IsExpansionRamRange(address, byteCount) || IsRealFastRamRange(address, byteCount))
            {
                handler(address, byteCount);
            }
        }

        private static long GetSecondWordCycle(AmigaBusAccessResult access)
        {
            if (access.Request.Size != AmigaBusAccessSize.Long)
            {
                return access.GrantedCycle;
            }

            var nextSlotCycle = access.GrantedCycle + AgnusChipSlotScheduler.SlotCycles;
            return access.CompletedCycle >= nextSlotCycle ? nextSlotCycle : access.CompletedCycle;
        }

        private void WriteCustomByte(ushort offset, byte value, long cycle)
        {
            var wordOffset = (ushort)(offset & 0x01FE);
            _pendingCustomBytes[offset] = value;
            _pendingCustomByteWritten[offset] = true;

            var highIndex = wordOffset;
            var lowIndex = wordOffset + 1;
            var high = _pendingCustomByteWritten[highIndex]
                ? _pendingCustomBytes[highIndex]
                : Paula.ReadByte((ushort)highIndex);
            var low = _pendingCustomByteWritten[lowIndex]
                ? _pendingCustomBytes[lowIndex]
                : Paula.ReadByte((ushort)lowIndex);
            var wordValue = (ushort)((high << 8) | low);
            Paula.ScheduleWrite(cycle, wordOffset, wordValue);
            Display.ScheduleWrite(cycle, wordOffset, wordValue);
            Blitter.WriteRegister(wordOffset, wordValue, cycle);
            Disk.WriteRegister(wordOffset, wordValue, cycle);

            _pendingCustomByteWritten[highIndex] = false;
            _pendingCustomByteWritten[lowIndex] = false;
        }

        private static uint NormalizeAddress(uint address)
        {
            return address & 0x00FF_FFFF;
        }

        private static bool IsPowerOfTwo(int value)
        {
            return value > 0 && (value & (value - 1)) == 0;
        }

        internal uint GetCodePageGeneration(uint address)
        {
            address = NormalizeAddress(address);
            if (TryGetChipRamOffset(address, out var chipOffset))
            {
                return _chipCodePageGenerations[chipOffset >> CodeGenerationPageShift];
            }

            if (TryGetExpansionRamOffset(address, out var expansionOffset))
            {
                return _expansionCodePageGenerations[expansionOffset >> CodeGenerationPageShift];
            }

            if (TryGetRealFastRamOffset(address, out var realFastOffset))
            {
                return _realFastCodePageGenerations[realFastOffset >> CodeGenerationPageShift];
            }

            return 0;
        }

        internal bool CodeRangeGenerationMatches(uint address, int byteCount, uint startGeneration, uint endGeneration)
        {
            if (byteCount <= 0)
            {
                return true;
            }

            address = NormalizeAddress(address);
            var endAddress = NormalizeAddress(address + (uint)(byteCount - 1));
            return GetCodePageGeneration(address) == startGeneration &&
                GetCodePageGeneration(endAddress) == endGeneration;
        }

        private void TouchCodePages(uint address, int byteCount)
        {
            if (byteCount <= 0)
            {
                return;
            }

            address = NormalizeAddress(address);
            var remaining = byteCount;
            var current = address;
            while (remaining > 0)
            {
                TouchCodePage(current);
                var bytesToNextPage = CodeGenerationPageSize - ((int)current & (CodeGenerationPageSize - 1));
                var step = Math.Min(remaining, bytesToNextPage);
                remaining -= step;
                current = NormalizeAddress(current + (uint)step);
            }
        }

        private void TouchCodePage(uint address)
        {
            address = NormalizeAddress(address);
            var generation = NextCodeGeneration();
            if (TryGetChipRamOffset(address, out var chipOffset))
            {
                _chipCodePageGenerations[chipOffset >> CodeGenerationPageShift] = generation;
                return;
            }

            if (TryGetExpansionRamOffset(address, out var expansionOffset))
            {
                _expansionCodePageGenerations[expansionOffset >> CodeGenerationPageShift] = generation;
                return;
            }

            if (TryGetRealFastRamOffset(address, out var realFastOffset))
            {
                _realFastCodePageGenerations[realFastOffset >> CodeGenerationPageShift] = generation;
            }
        }

        private uint NextCodeGeneration()
        {
            _codeGenerationClock++;
            if (_codeGenerationClock != 0)
            {
                return _codeGenerationClock;
            }

            Array.Clear(_chipCodePageGenerations);
            Array.Clear(_expansionCodePageGenerations);
            Array.Clear(_realFastCodePageGenerations);
            _codeGenerationClock = 1;
            return _codeGenerationClock;
        }

        private bool TryGetContiguousWritableSpan(uint address, int byteCount, out Span<byte> span)
        {
            if (IsContiguousChipRamRange(address, byteCount))
            {
                span = _chipRam.AsSpan(GetChipRamOffset(address), byteCount);
                return true;
            }

            if (IsExpansionRamRange(address, byteCount))
            {
                var offset = checked((int)(address - ExpansionRamBase));
                span = _expansionRam.AsSpan(offset, byteCount);
                return true;
            }

            if (IsRealFastRamRange(address, byteCount))
            {
                var offset = checked((int)(address - RealFastRamBase));
                span = _realFastRam.AsSpan(offset, byteCount);
                return true;
            }

            span = default;
            return false;
        }

        private bool IsChipRamRange(uint address, int byteCount)
        {
            if (byteCount < 0)
            {
                return false;
            }

            if (byteCount == 0)
            {
                return true;
            }

            return address < _chipRamDecodeSize &&
                (ulong)address + (ulong)byteCount <= _chipRamDecodeSize;
        }

        private bool IsContiguousChipRamRange(uint address, int byteCount)
        {
            if (!IsChipRamRange(address, byteCount))
            {
                return false;
            }

            var offset = GetChipRamOffset(address);
            return (ulong)offset + (ulong)byteCount <= (ulong)_chipRam.Length;
        }

        internal bool IsChipRamAddress(uint address)
        {
            return TryGetChipRamOffset(address, out _);
        }

        private bool TryGetChipRamOffset(uint address, out int offset)
        {
            address = NormalizeAddress(address);
            if (_chipRam.Length > 0 && address < _chipRamDecodeSize)
            {
                offset = (int)(address & ((uint)_chipRam.Length - 1u));
                return true;
            }

            offset = 0;
            return false;
        }

        private int GetChipRamOffset(uint address)
        {
            if (!TryGetChipRamOffset(address, out var offset))
            {
                throw new ArgumentOutOfRangeException(nameof(address), address, "Address is outside the chip RAM decode window.");
            }

            return offset;
        }

        private bool IsExpansionRamRange(uint address, int byteCount)
        {
            if (_expansionRam.Length == 0 || address < ExpansionRamBase)
            {
                return false;
            }

            var offset = address - ExpansionRamBase;
            return offset < _expansionRam.Length && (ulong)offset + (ulong)byteCount <= (ulong)_expansionRam.Length;
        }

        private bool IsExpansionRamAddress(uint address)
        {
            return TryGetExpansionRamOffset(address, out _);
        }

        private bool TryGetExpansionRamOffset(uint address, out int offset)
        {
            if (_expansionRam.Length != 0 && address >= ExpansionRamBase)
            {
                var candidate = address - ExpansionRamBase;
                if (candidate < _expansionRam.Length)
                {
                    offset = (int)candidate;
                    return true;
                }
            }

            offset = 0;
            return false;
        }

        private bool IsRealFastRamRange(uint address, int byteCount)
        {
            if (_realFastRam.Length == 0 || address < RealFastRamBase)
            {
                return false;
            }

            var offset = address - RealFastRamBase;
            return offset < _realFastRam.Length && (ulong)offset + (ulong)byteCount <= (ulong)_realFastRam.Length;
        }

        private bool IsRealFastRamAddress(uint address)
        {
            return TryGetRealFastRamOffset(address, out _);
        }

        private bool TryGetRealFastRamOffset(uint address, out int offset)
        {
            if (_realFastRam.Length != 0 && address >= RealFastRamBase)
            {
                var candidate = address - RealFastRamBase;
                if (candidate < _realFastRam.Length)
                {
                    offset = (int)candidate;
                    return true;
                }
            }

            offset = 0;
            return false;
        }

        private bool IsRomOverlayAddress(uint address)
        {
            address = NormalizeAddress(address);
            return _romOverlayEnabled &&
                _romOverlayRegion != null &&
                address < 0x0008_0000;
        }

        private bool TryReadRomOverlayByte(uint address, out byte value)
        {
            if (!IsRomOverlayAddress(address))
            {
                value = 0;
                return false;
            }

            var romAddress = _romOverlayRegion!.BaseAddress + (address % (uint)_romOverlayRegion.Length);
            return _romOverlayRegion.TryReadByte(romAddress, out value);
        }

        private byte ApplyGamePortFireBits(byte value)
        {
            if (GamePort0FirePressed)
            {
                value = (byte)(value & ~0x40);
            }
            else
            {
                value = (byte)(value | 0x40);
            }

            if (GamePort1FirePressed)
            {
                value = (byte)(value & ~0x80);
            }
            else
            {
                value = (byte)(value | 0x80);
            }

            return value;
        }

        private bool TryReadGamePortCustomByte(ushort offset, out byte value)
        {
            switch (offset)
            {
                case 0x00A:
                    value = (byte)(ReadGamePortData(0) >> 8);
                    return true;
                case 0x00B:
                    value = (byte)ReadGamePortData(0);
                    return true;
                case 0x00C:
                    value = (byte)(ReadGamePortData(1) >> 8);
                    return true;
                case 0x00D:
                    value = (byte)ReadGamePortData(1);
                    return true;
                case 0x016:
                    value = (byte)(ReadPotGoData() >> 8);
                    return true;
                case 0x017:
                    value = (byte)ReadPotGoData();
                    return true;
                default:
                    value = 0;
                    return false;
            }
        }

        private ushort ReadGamePortData(int port)
        {
            var gamePort = _gamePorts[port];
            var value = (ushort)((gamePort.MouseYCounter << 8) | gamePort.MouseXCounter);
            if (gamePort.JoystickUp ||
                gamePort.JoystickDown ||
                gamePort.JoystickLeft ||
                gamePort.JoystickRight)
            {
                var horizontal = (gamePort.JoystickDown ^ gamePort.JoystickRight ? 0x0001 : 0x0000) |
                    (gamePort.JoystickRight ? 0x0002 : 0x0000);
                var vertical = (gamePort.JoystickUp ^ gamePort.JoystickLeft ? 0x0100 : 0x0000) |
                    (gamePort.JoystickLeft ? 0x0200 : 0x0000);
                value = (ushort)((value & ~0x0303) | horizontal | vertical);
            }

            return value;
        }

        private ushort ReadPotGoData()
        {
            var value = (ushort)0x5500;
            if (GamePort0SecondFirePressed)
            {
                value = (ushort)(value & ~0x0400);
            }

            if (GamePort1SecondFirePressed)
            {
                value = (ushort)(value & ~0x4000);
            }

            return value;
        }

        private GamePortState GetGamePort(int port)
        {
            if ((uint)port >= _gamePorts.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(port), port, "Game port must be 0 or 1.");
            }

            return _gamePorts[port];
        }

        private bool TryGetCiaRegister(uint address, out AmigaCia cia, out int register)
        {
            if (address >= 0x00BFE001 && address <= 0x00BFEF01 && (address & 0xFF) == 0x01)
            {
                cia = CiaA;
                register = (int)((address >> 8) & 0x0F);
                return true;
            }

            if (address >= 0x00BFD000 && address <= 0x00BFDF00 && (address & 0xFF) == 0x00)
            {
                cia = CiaB;
                register = (int)((address >> 8) & 0x0F);
                return true;
            }

            cia = null!;
            register = 0;
            return false;
        }

        private sealed class GamePortState
        {
            public byte MouseXCounter { get; set; }

            public byte MouseYCounter { get; set; }

            public bool JoystickUp { get; set; }

            public bool JoystickDown { get; set; }

            public bool JoystickLeft { get; set; }

            public bool JoystickRight { get; set; }

            public bool PrimaryFirePressed { get; set; }

            public bool SecondFirePressed { get; set; }

            public void Reset()
            {
                MouseXCounter = 0;
                MouseYCounter = 0;
                JoystickUp = false;
                JoystickDown = false;
                JoystickLeft = false;
                JoystickRight = false;
                PrimaryFirePressed = false;
                SecondFirePressed = false;
            }
        }

        private sealed class MappedMemoryRegion
        {
            private readonly byte[] _data;

            public MappedMemoryRegion(uint baseAddress, byte[] data, bool readOnly)
            {
                BaseAddress = baseAddress;
                _data = data ?? throw new ArgumentNullException(nameof(data));
                ReadOnly = readOnly;
            }

            public uint BaseAddress { get; }

            public int Length => _data.Length;

            public bool ReadOnly { get; }

            public bool Contains(uint address)
            {
                var offset = address - BaseAddress;
                return address >= BaseAddress && offset < _data.Length;
            }

            public bool TryReadByte(uint address, out byte value)
            {
                var offset = address - BaseAddress;
                if (address < BaseAddress || offset >= _data.Length)
                {
                    value = 0;
                    return false;
                }

                value = _data[offset];
                return true;
            }

            public bool TryWriteByte(uint address, byte value)
            {
                var offset = address - BaseAddress;
                if (ReadOnly || address < BaseAddress || offset >= _data.Length)
                {
                    return false;
                }

                _data[offset] = value;
                return true;
            }
        }
    }

    internal sealed class Paula
    {
        private const ushort IntenaMasterEnable = 0x4000;
        private const ushort AudioInterruptMask = 0x0780;
        private const float VoiceScale = 0.25f;
        private const int MaxCapturedWrites = 65536;
        private const int MaxPendingInterruptEvents = 65536;
        private readonly AmigaBus _bus;
        private readonly PaulaChannel[] _channels = new PaulaChannel[AmigaConstants.PaulaChannelCount];
        private readonly List<PendingWrite> _pendingWrites = new List<PendingWrite>();
        private readonly List<PaulaInterruptEvent> _pendingInterrupts = new List<PaulaInterruptEvent>();
        private readonly PaulaInterruptEvent[] _drainedInterruptBuffer = new PaulaInterruptEvent[MaxPendingInterruptEvents];
        private readonly ReusableReadOnlyList<PaulaInterruptEvent> _drainedInterrupts = new ReusableReadOnlyList<PaulaInterruptEvent>();
        private readonly BoundedWriteLog _writes = new BoundedWriteLog(MaxCapturedWrites);
        private readonly byte[] _registerBytes = new byte[0x200];
        private int _pendingWriteIndex;
        private ushort _adkcon;
        private ushort _dmacon;
        private ushort _intena;
        private ushort _intreq;
        private ushort _vposr;
        private ushort _vhposr;
        private ushort _lastAudioDmaMask;
        private long _lastCycle;
        private float[][]? _captureSamples;
        private int _captureFrameIndex;
        private int _captureSampleRate;

        public Paula(AmigaBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            for (var i = 0; i < _channels.Length; i++)
            {
                _channels[i] = new PaulaChannel(i);
            }
        }

        public IReadOnlyList<CustomRegisterWrite> Writes => _writes;

        public ushort Adkcon => _adkcon;

        public ushort Dmacon => _dmacon;

        public ushort ActiveInterruptBits
        {
            get
            {
                if ((_intena & IntenaMasterEnable) == 0)
                {
                    return 0;
                }

                return (ushort)(_intreq & _intena & 0x3FFF);
            }
        }

        public void Reset()
        {
            Array.Clear(_registerBytes);
            _pendingWrites.Clear();
            _pendingInterrupts.Clear();
            _writes.Clear();
            _pendingWriteIndex = 0;
            _adkcon = 0;
            _dmacon = 0;
            _intena = 0;
            _intreq = 0;
            _vposr = 0;
            _vhposr = 0;
            _lastAudioDmaMask = 0;
            _lastCycle = 0;
            _captureSamples = null;
            _captureFrameIndex = 0;
            _captureSampleRate = 0;
            foreach (var channel in _channels)
            {
                channel.Reset();
            }
        }

        public byte ReadByte(ushort offset)
        {
            if (offset == 0x004)
            {
                return (byte)(_vposr >> 8);
            }

            if (offset == 0x005)
            {
                return (byte)_vposr;
            }

            if (offset == 0x010)
            {
                return (byte)(_adkcon >> 8);
            }

            if (offset == 0x011)
            {
                return (byte)_adkcon;
            }

            if (offset == 0x002)
            {
                return (byte)((_dmacon | _bus.Blitter.DmaconStatusBits) >> 8);
            }

            if (offset == 0x003)
            {
                return (byte)(_dmacon | _bus.Blitter.DmaconStatusBits);
            }

            if (offset == 0x006)
            {
                return (byte)(_vhposr >> 8);
            }

            if (offset == 0x007)
            {
                return (byte)_vhposr;
            }

            if (offset == 0x01C)
            {
                return (byte)(_intena >> 8);
            }

            if (offset == 0x01D)
            {
                return (byte)_intena;
            }

            if (offset == 0x01E)
            {
                return (byte)(_intreq >> 8);
            }

            if (offset == 0x01F)
            {
                return (byte)_intreq;
            }

            return offset < _registerBytes.Length ? _registerBytes[offset] : (byte)0;
        }

        public void SetBeamPosition(int verticalLine, int horizontalPosition, bool longFrame)
        {
            verticalLine = Math.Clamp(verticalLine, 0, 0x1FF);
            horizontalPosition = Math.Clamp(horizontalPosition, 0, 0xFF);
            _vposr = (ushort)(((longFrame ? 1 : 0) << 15) | ((verticalLine >> 8) & 0x0001));
            _vhposr = (ushort)(((verticalLine & 0x00FF) << 8) | horizontalPosition);
        }

        public void ScheduleWrite(long cycle, ushort offset, ushort value)
        {
            offset = (ushort)(offset & 0x01FE);
            var pending = new PendingWrite(cycle, offset, value);
            _writes.Add(new CustomRegisterWrite(cycle, offset, value));
            var insertIndex = _pendingWrites.Count;
            while (insertIndex > _pendingWriteIndex && _pendingWrites[insertIndex - 1].Cycle > cycle)
            {
                insertIndex--;
            }

            _pendingWrites.Insert(insertIndex, pending);
        }

        public IReadOnlyList<PaulaInterruptEvent> DrainInterrupts()
        {
            if (_pendingInterrupts.Count == 0)
            {
                return Array.Empty<PaulaInterruptEvent>();
            }

            var count = Math.Min(_pendingInterrupts.Count, _drainedInterruptBuffer.Length);
            for (var i = 0; i < count; i++)
            {
                _drainedInterruptBuffer[i] = _pendingInterrupts[i];
            }

            SortPaulaInterrupts(_drainedInterruptBuffer, count);
            _pendingInterrupts.Clear();
            _drainedInterrupts.Reset(_drainedInterruptBuffer, count);
            return _drainedInterrupts;
        }

        private static void SortPaulaInterrupts(PaulaInterruptEvent[] events, int count)
        {
            for (var i = 1; i < count; i++)
            {
                var value = events[i];
                var j = i - 1;
                while (j >= 0 && ComparePaulaInterrupts(events[j], value) > 0)
                {
                    events[j + 1] = events[j];
                    j--;
                }

                events[j + 1] = value;
            }
        }

        private static int ComparePaulaInterrupts(PaulaInterruptEvent left, PaulaInterruptEvent right)
        {
            var cycleCompare = left.Cycle.CompareTo(right.Cycle);
            return cycleCompare != 0 ? cycleCompare : left.Channel.CompareTo(right.Channel);
        }

        public int GetHighestPendingInterruptLevel()
        {
            var active = ActiveInterruptBits;
            if ((active & 0x2000) != 0)
            {
                return 6;
            }

            if ((active & 0x1800) != 0)
            {
                return 5;
            }

            if ((active & 0x0780) != 0)
            {
                return 4;
            }

            if ((active & 0x0070) != 0)
            {
                return 3;
            }

            if ((active & 0x0008) != 0)
            {
                return 2;
            }

            return (active & 0x0007) != 0 ? 1 : 0;
        }

        public long? GetNextWakeCandidateCycle(long currentCycle, long targetCycle)
        {
            if (targetCycle <= currentCycle)
            {
                return null;
            }

            long? candidate = null;
            if (_pendingWriteIndex < _pendingWrites.Count)
            {
                candidate = MinWakeCandidate(candidate, _pendingWrites[_pendingWriteIndex].Cycle);
            }

            for (var i = 0; i < _channels.Length; i++)
            {
                candidate = MinWakeCandidate(candidate, _channels[i].GetNextWakeCandidateCycle());
            }

            return ClampWakeCandidate(candidate, currentCycle, targetCycle);
        }

        public PaulaChannelSnapshot GetChannelSnapshot(int channel)
        {
            if ((uint)channel >= _channels.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(channel), channel, "Paula channel index is outside the supported range.");
            }

            return _channels[channel].GetSnapshot();
        }

        public void BeginChannelCapture(int frames, int sampleRate)
        {
            if (frames <= 0 || sampleRate <= 0)
            {
                _captureSamples = null;
                _captureFrameIndex = 0;
                _captureSampleRate = 0;
                return;
            }

            _captureSamples = new float[_channels.Length][];
            for (var i = 0; i < _captureSamples.Length; i++)
            {
                _captureSamples[i] = new float[frames];
            }

            _captureFrameIndex = 0;
            _captureSampleRate = sampleRate;
        }

        public AmigaChannelWaveform? FinishChannelCapture()
        {
            if (_captureSamples == null)
            {
                return null;
            }

            var channels = new AmigaChannelWaveformChannel[_captureSamples.Length];
            for (var i = 0; i < channels.Length; i++)
            {
                channels[i] = new AmigaChannelWaveformChannel(i, _captureSamples[i], IsActive(_captureSamples[i]));
            }

            var result = new AmigaChannelWaveform(channels, _captureFrameIndex, _captureSampleRate);
            _captureSamples = null;
            _captureFrameIndex = 0;
            _captureSampleRate = 0;
            return result;
        }

        public void RenderSample(long targetCycle, Span<float> destination, int frame, int channels)
        {
            AdvanceTo(targetCycle);
            var left = 0.0f;
            var right = 0.0f;
            var capture = _captureSamples;
            for (var i = 0; i < _channels.Length; i++)
            {
                if (IsAttachedSource(i))
                {
                    if (capture != null && _captureFrameIndex < capture[i].Length)
                    {
                        capture[i][_captureFrameIndex] = 0.0f;
                    }

                    continue;
                }

                var raw = _channels[i].CurrentSample / 128.0f;
                var sample = raw * (_channels[i].Volume / 64.0f) * VoiceScale;
                if (capture != null && _captureFrameIndex < capture[i].Length)
                {
                    capture[i][_captureFrameIndex] = sample;
                }

                if (i == 0 || i == 3)
                {
                    left += sample;
                }
                else
                {
                    right += sample;
                }
            }

            var offset = frame * channels;
            if (channels == 1)
            {
                destination[offset] = Math.Clamp((left + right) * 0.5f, -1.0f, 1.0f);
            }
            else
            {
                destination[offset] = Math.Clamp(left, -1.0f, 1.0f);
                destination[offset + 1] = Math.Clamp(right, -1.0f, 1.0f);
                for (var extra = 2; extra < channels; extra++)
                {
                    destination[offset + extra] = Math.Clamp((left + right) * 0.5f, -1.0f, 1.0f);
                }
            }

            if (capture != null)
            {
                _captureFrameIndex++;
            }
        }

        public void AdvanceTo(long targetCycle)
        {
            if (targetCycle < _lastCycle)
            {
                return;
            }

            while (_pendingWriteIndex < _pendingWrites.Count && _pendingWrites[_pendingWriteIndex].Cycle <= targetCycle)
            {
                var write = _pendingWrites[_pendingWriteIndex++];
                AdvanceChannels(write.Cycle - _lastCycle);
                ApplyWrite(write.Offset, write.Value);
            }

            AdvanceChannels(targetCycle - _lastCycle);
            CompactPendingWrites();
        }

        private void AdvanceChannels(long cpuCycles)
        {
            if (cpuCycles <= 0)
            {
                return;
            }

            var targetCycle = _lastCycle + cpuCycles;
            foreach (var channel in _channels)
            {
                channel.AdvanceTo(targetCycle, _bus, this);
            }

            _lastCycle = targetCycle;
        }

        private void ApplyWrite(ushort offset, ushort value)
        {
            if (offset + 1 < _registerBytes.Length)
            {
                _registerBytes[offset] = (byte)(value >> 8);
                _registerBytes[offset + 1] = (byte)value;
            }

            if (offset == 0x096)
            {
                var old = _dmacon;
                var mask = (ushort)(value & 0x7FFF);
                var audioMask = (ushort)(mask & 0x000F);
                if (audioMask != 0)
                {
                    _lastAudioDmaMask = audioMask;
                }

                if ((value & 0x8000) != 0)
                {
                    if (mask == 0 && _lastAudioDmaMask != 0)
                    {
                        mask = _lastAudioDmaMask;
                    }

                    _dmacon |= mask;
                }
                else
                {
                    _dmacon &= (ushort)~mask;
                }

                WriteRegisterWord(0x002, _dmacon);

                for (var i = 0; i < _channels.Length; i++)
                {
                    var bit = (ushort)(1 << i);
                    var wasEnabled = IsAudioDmaEnabled(old, bit);
                    var enabled = IsAudioDmaEnabled(_dmacon, bit);
                    if (enabled && !wasEnabled)
                    {
                        _channels[i].SetDmaEnabled(true, _lastCycle, _bus, this);
                    }
                    else if (!enabled && wasEnabled)
                    {
                        _channels[i].SetDmaEnabled(false, _lastCycle, _bus, this);
                    }
                }

                return;
            }

            if (offset == 0x09A)
            {
                ApplySetClear(ref _intena, value);
                WriteRegisterWord(0x01C, _intena);
                QueuePendingEnabledAudioInterrupts(_lastCycle);
                return;
            }

            if (offset == 0x09C)
            {
                var mask = (ushort)(value & 0x7FFF);
                if ((value & 0x8000) != 0)
                {
                    _intreq |= mask;
                    QueuePendingEnabledAudioInterrupts(_lastCycle, mask);
                }
                else
                {
                    _intreq &= (ushort)~mask;
                }

                WriteRegisterWord(0x01E, _intreq);

                return;
            }

            if (offset == 0x09E)
            {
                ApplySetClear(ref _adkcon, value);
                WriteRegisterWord(0x010, _adkcon);
                return;
            }

            var channelIndex = GetAudioChannelIndex(offset);
            if (channelIndex < 0)
            {
                return;
            }

            var channel = _channels[channelIndex];
            var register = offset - (0x0A0 + (channelIndex * 0x10));
            switch (register)
            {
                case 0x00:
                    channel.Location = _bus.WriteChipDmaPointerHigh(channel.Location, value);
                    break;
                case 0x02:
                    channel.Location = _bus.WriteChipDmaPointerLow(channel.Location, value);
                    break;
                case 0x04:
                    channel.LengthWords = Math.Max(1, (int)value);
                    break;
                case 0x06:
                    channel.Period = ClampPeriod(value);
                    break;
                case 0x08:
                    var volume = value & 0x7F;
                    if (volume == 0 && (value & 0x7F00) != 0)
                    {
                        volume = (value >> 8) & 0x7F;
                    }

                    channel.Volume = Math.Min(64, volume);
                    break;
                case 0x0A:
                    channel.WriteData(value, _lastCycle, this);
                    break;
            }
        }

        private static int GetAudioChannelIndex(ushort offset)
        {
            if (offset < 0x0A0 || offset > 0x0DA)
            {
                return -1;
            }

            var channel = (offset - 0x0A0) / 0x10;
            var register = (offset - 0x0A0) % 0x10;
            return channel < AmigaConstants.PaulaChannelCount && register <= 0x0A ? channel : -1;
        }

        private static bool IsAudioDmaEnabled(ushort dmacon, ushort channelBit)
        {
            return (dmacon & 0x0200) != 0 && (dmacon & channelBit) != 0;
        }

        private void RequestAudioInterrupt(int channel, long cycle)
        {
            if ((uint)channel < AmigaConstants.PaulaChannelCount)
            {
                var bit = GetAudioInterruptBit(channel);
                _intreq |= bit;
                WriteRegisterWord(0x01E, _intreq);
                if (IsInterruptEnabled(bit))
                {
                    _pendingInterrupts.Add(new PaulaInterruptEvent(channel, bit, cycle));
                }
            }
        }

        private void ApplyModulationFrom(int sourceChannel, ushort value)
        {
            var targetChannel = sourceChannel + 1;
            if (sourceChannel < 0 || targetChannel >= _channels.Length)
            {
                return;
            }

            if (IsVolumeAttached(sourceChannel))
            {
                _channels[targetChannel].Volume = Math.Min(64, value & 0x7F);
            }

            if (IsPeriodAttached(sourceChannel))
            {
                _channels[targetChannel].Period = ClampPeriod(value);
            }
        }

        private bool IsAttachedSource(int channel)
        {
            return IsVolumeAttached(channel) || IsPeriodAttached(channel);
        }

        private bool IsVolumeAttached(int sourceChannel)
        {
            return sourceChannel is >= 0 and <= 2 && (_adkcon & (1 << sourceChannel)) != 0;
        }

        private bool IsPeriodAttached(int sourceChannel)
        {
            return sourceChannel is >= 0 and <= 2 && (_adkcon & (1 << (sourceChannel + 4))) != 0;
        }

        public bool IsInterruptEnabled(ushort bit)
        {
            return (_intena & IntenaMasterEnable) != 0 && (_intena & bit) != 0;
        }

        private void QueuePendingEnabledAudioInterrupts(long cycle, ushort mask = AudioInterruptMask)
        {
            var active = (ushort)(_intreq & _intena & mask & AudioInterruptMask);
            if ((_intena & IntenaMasterEnable) == 0 || active == 0)
            {
                return;
            }

            for (var channel = 0; channel < _channels.Length; channel++)
            {
                var bit = GetAudioInterruptBit(channel);
                if ((active & bit) != 0)
                {
                    _pendingInterrupts.Add(new PaulaInterruptEvent(channel, bit, cycle));
                }
            }
        }

        private void WriteRegisterWord(ushort offset, ushort value)
        {
            if (offset + 1 >= _registerBytes.Length)
            {
                return;
            }

            _registerBytes[offset] = (byte)(value >> 8);
            _registerBytes[offset + 1] = (byte)value;
        }

        private static void ApplySetClear(ref ushort register, ushort value)
        {
            var mask = (ushort)(value & 0x7FFF);
            if ((value & 0x8000) != 0)
            {
                register |= mask;
            }
            else
            {
                register &= (ushort)~mask;
            }
        }

        private static ushort GetAudioInterruptBit(int channel)
        {
            return (ushort)(0x0080 << channel);
        }

        private static int ClampPeriod(ushort value)
        {
            return Math.Max(1, (int)value);
        }

        private static long GetPeriodCycles(int period)
        {
            return (long)Math.Max(1, period) * AmigaConstants.A500PalCpuCyclesPerColorClock;
        }

        private static long GetDmaPeriodCycles(AmigaBus bus, int period)
        {
            return (long)Math.Max(bus.AudioDmaMinimumPeriod, period) * AmigaConstants.A500PalCpuCyclesPerColorClock;
        }

        private static long? MinWakeCandidate(long? candidate, long? eventCycle)
        {
            if (!eventCycle.HasValue)
            {
                return candidate;
            }

            if (!candidate.HasValue)
            {
                return eventCycle;
            }

            return Math.Min(candidate.Value, eventCycle.Value);
        }

        private static long? ClampWakeCandidate(long? candidate, long currentCycle, long targetCycle)
        {
            if (!candidate.HasValue || candidate.Value > targetCycle)
            {
                return null;
            }

            return candidate.Value <= currentCycle ? currentCycle + 1 : candidate.Value;
        }

        private void CompactPendingWrites()
        {
            if (_pendingWriteIndex < 64 || _pendingWriteIndex * 2 < _pendingWrites.Count)
            {
                return;
            }

            _pendingWrites.RemoveRange(0, _pendingWriteIndex);
            _pendingWriteIndex = 0;
        }

        private static bool IsActive(ReadOnlySpan<float> samples)
        {
            for (var i = 0; i < samples.Length; i++)
            {
                if (Math.Abs(samples[i]) > 0.001f)
                {
                    return true;
                }
            }

            return false;
        }

        private readonly struct PendingWrite
        {
            public PendingWrite(long cycle, ushort offset, ushort value)
            {
                Cycle = cycle;
                Offset = offset;
                Value = value;
            }

            public long Cycle { get; }

            public ushort Offset { get; }

            public ushort Value { get; }
        }

        private sealed class PaulaChannel
        {
            private ushort _dataWord;
            private bool _hasDataWord;
            private bool _nextByteIsLow;
            private long _nextSampleCycle;
            private uint _currentAddress;
            private int _remainingWords;

            public PaulaChannel(int index)
            {
                Index = index;
                Reset();
            }

            public int Index { get; }

            public uint Location { get; set; }

            public int LengthWords { get; set; }

            public int Period { get; set; }

            public int Volume { get; set; }

            public sbyte CurrentSample { get; set; }

            public bool DmaEnabled { get; set; }

            public void Reset()
            {
                Location = 0;
                LengthWords = 1;
                Period = 428;
                Volume = 64;
                CurrentSample = 0;
                DmaEnabled = false;
                _dataWord = 0;
                _hasDataWord = false;
                _nextByteIsLow = false;
                _nextSampleCycle = 0;
                _currentAddress = 0;
                _remainingWords = 0;
            }

            public void SetDmaEnabled(bool enabled, long cycle, AmigaBus bus, Paula paula)
            {
                if (!enabled)
                {
                    DmaEnabled = false;
                    _hasDataWord = false;
                    _nextByteIsLow = false;
                    return;
                }

                DmaEnabled = true;
                _currentAddress = bus.MaskChipDmaAddress(Location);
                _remainingWords = Math.Max(1, LengthWords);
                FetchDmaWord(bus, cycle, paula, forceInterrupt: true);
                _nextSampleCycle = cycle + GetDmaPeriodCycles(bus, Period);
            }

            public void WriteData(ushort value, long cycle, Paula paula)
            {
                _dataWord = value;
                _hasDataWord = true;
                _nextByteIsLow = true;
                CurrentSample = unchecked((sbyte)(value >> 8));
                _nextSampleCycle = cycle + GetPeriodCycles(Period);
                paula.RequestAudioInterrupt(Index, cycle);
            }

            public void AdvanceTo(long targetCycle, AmigaBus bus, Paula paula)
            {
                if (!_hasDataWord && !DmaEnabled)
                {
                    return;
                }

                while (_nextSampleCycle <= targetCycle)
                {
                    if (_hasDataWord && _nextByteIsLow)
                    {
                        CurrentSample = unchecked((sbyte)_dataWord);
                        _nextByteIsLow = false;
                        paula.ApplyModulationFrom(Index, _dataWord);
                        _nextSampleCycle += GetActivePeriodCycles(bus);
                        continue;
                    }

                    if (DmaEnabled)
                    {
                        FetchDmaWord(bus, _nextSampleCycle, paula, forceInterrupt: false);
                        _nextSampleCycle += GetDmaPeriodCycles(bus, Period);
                    }
                    else
                    {
                        _hasDataWord = false;
                        break;
                    }
                }
            }

            public long? GetNextWakeCandidateCycle()
            {
                if (DmaEnabled || _hasDataWord)
                {
                    return _nextSampleCycle;
                }

                return null;
            }

            public PaulaChannelSnapshot GetSnapshot()
            {
                return new PaulaChannelSnapshot(
                    Index,
                    Location,
                    _currentAddress,
                    LengthWords,
                    _remainingWords,
                    Period,
                    Volume,
                    CurrentSample,
                    DmaEnabled,
                    _dataWord,
                    _hasDataWord,
                    _nextByteIsLow,
                    _nextSampleCycle);
            }

            private void FetchDmaWord(AmigaBus bus, long cycle, Paula paula, bool forceInterrupt)
            {
                if (_remainingWords <= 0)
                {
                    _currentAddress = bus.MaskChipDmaAddress(Location);
                    _remainingWords = Math.Max(1, LengthWords);
                    paula.RequestAudioInterrupt(Index, cycle);
                }

                var dmaRead = bus.ReadPaulaDmaWord(_currentAddress, cycle);
                _dataWord = dmaRead.Value;
                _currentAddress = bus.AddChipDmaPointerOffset(_currentAddress, 2);
                _remainingWords--;
                _hasDataWord = true;
                _nextByteIsLow = true;
                CurrentSample = unchecked((sbyte)(_dataWord >> 8));

                if (forceInterrupt || _remainingWords == 0)
                {
                    paula.RequestAudioInterrupt(Index, cycle);
                }
            }

            private long GetActivePeriodCycles(AmigaBus bus)
            {
                return DmaEnabled
                    ? GetDmaPeriodCycles(bus, Period)
                    : GetPeriodCycles(Period);
            }

        }
    }

    internal readonly struct PaulaInterruptEvent
    {
        public PaulaInterruptEvent(int channel, ushort intreqBit, long cycle)
        {
            Channel = channel;
            IntreqBit = intreqBit;
            Cycle = cycle;
        }

        public int Channel { get; }

        public ushort IntreqBit { get; }

        public long Cycle { get; }
    }

    internal readonly struct PaulaChannelSnapshot
    {
        public PaulaChannelSnapshot(
            int index,
            uint location,
            uint currentAddress,
            int lengthWords,
            int remainingWords,
            int period,
            int volume,
            sbyte currentSample,
            bool dmaEnabled,
            ushort dataWord,
            bool hasDataWord,
            bool nextByteIsLow,
            long nextSampleCycle)
        {
            Index = index;
            Location = location;
            CurrentAddress = currentAddress;
            LengthWords = lengthWords;
            RemainingWords = remainingWords;
            Period = period;
            Volume = volume;
            CurrentSample = currentSample;
            DmaEnabled = dmaEnabled;
            DataWord = dataWord;
            HasDataWord = hasDataWord;
            NextByteIsLow = nextByteIsLow;
            NextSampleCycle = nextSampleCycle;
        }

        public int Index { get; }

        public uint Location { get; }

        public uint CurrentAddress { get; }

        public int LengthWords { get; }

        public int RemainingWords { get; }

        public int Period { get; }

        public int Volume { get; }

        public sbyte CurrentSample { get; }

        public bool DmaEnabled { get; }

        public ushort DataWord { get; }

        public bool HasDataWord { get; }

        public bool NextByteIsLow { get; }

        public long NextSampleCycle { get; }
    }

    internal sealed class ReusableReadOnlyList<T> : IReadOnlyList<T>
    {
        private T[] _items = Array.Empty<T>();
        private int _count;

        public int Count => _count;

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _items[index];
            }
        }

        public void Reset(T[] items, int count)
        {
            if ((uint)count > (uint)items.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            _items = items;
            _count = count;
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (var i = 0; i < _count; i++)
            {
                yield return _items[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    internal sealed class ChipPresentationWriteHistory
    {
        private const int MaxCapturedWrites = 65536;
        private readonly int[] _headByOffset;
        private readonly int[] _tailByOffset;
        private readonly int[] _touchedOffsets;
        private readonly int[] _nextByWrite;
        private readonly ChipByteWrite[] _writes;
        private readonly bool[] _outOfOrderByOffset;
        private int _touchedOffsetCount;
        private int _writeCount;
        private long _latestWriteCycle = long.MinValue;
        private bool _hasOutOfOrderWrites;

        public ChipPresentationWriteHistory(int chipRamSize)
        {
            if (chipRamSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chipRamSize), chipRamSize, "Chip RAM size must be positive.");
            }

            _headByOffset = new int[chipRamSize];
            _tailByOffset = new int[chipRamSize];
            _touchedOffsets = new int[Math.Min(chipRamSize, MaxCapturedWrites)];
            _nextByWrite = new int[MaxCapturedWrites];
            _writes = new ChipByteWrite[MaxCapturedWrites];
            _outOfOrderByOffset = new bool[chipRamSize];
            Array.Fill(_headByOffset, -1);
            Array.Fill(_tailByOffset, -1);
        }

        public void RecordByte(int offset, byte oldValue, byte newValue, long cycle)
        {
            if ((uint)offset >= (uint)_headByOffset.Length ||
                _writeCount >= _writes.Length)
            {
                return;
            }

            var tailIndex = _tailByOffset[offset];
            if (tailIndex >= 0 && cycle < _writes[tailIndex].Cycle)
            {
                _outOfOrderByOffset[offset] = true;
                _hasOutOfOrderWrites = true;
            }

            oldValue = GetValueAtCycle(offset, cycle, oldValue);
            if (oldValue == newValue)
            {
                return;
            }

            if (_headByOffset[offset] < 0)
            {
                if (_touchedOffsetCount < _touchedOffsets.Length)
                {
                    _touchedOffsets[_touchedOffsetCount++] = offset;
                }

                _headByOffset[offset] = _writeCount;
            }
            else
            {
                _nextByWrite[_tailByOffset[offset]] = _writeCount;
            }

            _tailByOffset[offset] = _writeCount;
            _writes[_writeCount] = new ChipByteWrite(cycle, oldValue, newValue);
            _nextByWrite[_writeCount] = -1;
            _writeCount++;
            _latestWriteCycle = Math.Max(_latestWriteCycle, cycle);
        }

        public bool HasWrites => _writeCount != 0;

        public bool MayNeedPresentationRead(long cycle)
        {
            return _writeCount != 0 && (_hasOutOfOrderWrites || _latestWriteCycle > cycle);
        }

        public bool NeedsPresentationRead(int offset, long cycle)
        {
            if ((uint)offset >= (uint)_headByOffset.Length)
            {
                return false;
            }

            var headIndex = _headByOffset[offset];
            if (headIndex < 0)
            {
                return false;
            }

            if (_outOfOrderByOffset[offset])
            {
                return true;
            }

            var tailIndex = _tailByOffset[offset];
            return tailIndex >= 0 && _writes[tailIndex].Cycle > cycle;
        }

        public byte ReadByte(byte[] currentMemory, int offset, long cycle)
        {
            if ((uint)offset >= (uint)currentMemory.Length)
            {
                return 0;
            }

            if ((uint)offset >= (uint)_headByOffset.Length)
            {
                return currentMemory[offset];
            }

            return GetValueAtCycle(offset, cycle, currentMemory[offset]);
        }

        private byte GetValueAtCycle(int offset, long cycle, byte fallbackValue)
        {
            var index = _headByOffset[offset];
            if (index < 0)
            {
                return fallbackValue;
            }

            if (!_outOfOrderByOffset[offset])
            {
                var tailIndex = _tailByOffset[offset];
                if (tailIndex >= 0 && _writes[tailIndex].Cycle <= cycle)
                {
                    return fallbackValue;
                }

                while (index >= 0)
                {
                    var write = _writes[index];
                    if (write.Cycle > cycle)
                    {
                        return write.OldValue;
                    }

                    index = _nextByWrite[index];
                }

                return fallbackValue;
            }

            var latestPastCycle = long.MinValue;
            var latestPastValue = (byte)0;
            var hasPast = false;
            var earliestFutureCycle = long.MaxValue;
            var earliestFutureOldValue = (byte)0;
            var hasFuture = false;
            while (index >= 0)
            {
                var write = _writes[index];
                if (write.Cycle <= cycle)
                {
                    if (!hasPast || write.Cycle >= latestPastCycle)
                    {
                        latestPastCycle = write.Cycle;
                        latestPastValue = write.NewValue;
                        hasPast = true;
                    }
                }
                else if (!hasFuture || write.Cycle < earliestFutureCycle)
                {
                    earliestFutureCycle = write.Cycle;
                    earliestFutureOldValue = write.OldValue;
                    hasFuture = true;
                }

                index = _nextByWrite[index];
            }

            if (hasPast)
            {
                return latestPastValue;
            }

            return hasFuture ? earliestFutureOldValue : fallbackValue;
        }

        public void Clear()
        {
            for (var i = 0; i < _touchedOffsetCount; i++)
            {
                var offset = _touchedOffsets[i];
                _headByOffset[offset] = -1;
                _tailByOffset[offset] = -1;
                _outOfOrderByOffset[offset] = false;
            }

            _touchedOffsetCount = 0;
            _writeCount = 0;
            _latestWriteCycle = long.MinValue;
            _hasOutOfOrderWrites = false;
        }

        private readonly struct ChipByteWrite
        {
            public ChipByteWrite(long cycle, byte oldValue, byte newValue)
            {
                Cycle = cycle;
                OldValue = oldValue;
                NewValue = newValue;
            }

            public long Cycle { get; }

            public byte OldValue { get; }

            public byte NewValue { get; }
        }
    }

    internal sealed class BoundedWriteLog : IReadOnlyList<CustomRegisterWrite>
    {
        private readonly CustomRegisterWrite[] _buffer;
        private int _start;
        private int _count;

        public BoundedWriteLog(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
            }

            _buffer = new CustomRegisterWrite[capacity];
        }

        public int Count => _count;

        public CustomRegisterWrite this[int index]
        {
            get
            {
                if (index < 0 || index >= _count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _buffer[(_start + index) % _buffer.Length];
            }
        }

        public void Add(CustomRegisterWrite write)
        {
            if (_count < _buffer.Length)
            {
                _buffer[(_start + _count) % _buffer.Length] = write;
                _count++;
                return;
            }

            _buffer[_start] = write;
            _start = (_start + 1) % _buffer.Length;
        }

        public void Clear()
        {
            _start = 0;
            _count = 0;
        }

        public IEnumerator<CustomRegisterWrite> GetEnumerator()
        {
            for (var i = 0; i < _count; i++)
            {
                yield return this[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
