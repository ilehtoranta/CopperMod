using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CopperMod.Amiga
{
    internal readonly struct HostTrapStub
    {
        public HostTrapStub(uint address, ushort trapId, Action<M68kCpuState> callback)
        {
            Address = address;
            TrapId = trapId;
            Callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public uint Address { get; }

        public ushort TrapId { get; }

        public Action<M68kCpuState> Callback { get; }
    }

    internal enum CustomRegisterReadAdvanceKind
    {
        RegisterWritesOnly,
        BeamPosition,
        InputOnly,
        BlitterStatus,
        InterruptSources,
        DiskEventOnly,
        DiskPassiveInput
    }

    internal sealed class AmigaBus :
        IM68kBus,
        IM68kCodeReader,
        IM68kPhysicalAddressMap,
        IM68kFastMemoryBus,
        IM68kJitBus,
        IM68kJitFastMemoryBus,
        IM68kJitTimedMemoryBus
    {
        private const ushort HostTrapOpcode = 0xFF00;
        private const int MaxCapturedBusAccesses = 65536;
        private const int MaxPendingInterruptEvents = 65536;
        private const int CodeGenerationPageShift = 8;
        private const int CodeGenerationPageSize = 1 << CodeGenerationPageShift;
        private const uint MinimumChipRamDecodeSize = 0x0020_0000;
        private const byte CiaAPortAResetLatch = 0xFC;
        private const byte CiaAPortAResetDataDirection = 0x03;
        private const byte CiaAPortAOverlayBit = 0x01;
        private const byte CiaAPortAAudioFilterBit = 0x02;
        private readonly byte[] _chipRam;
        private readonly byte[] _expansionRam;
        private readonly byte[] _realFastRam;
        private readonly uint[] _chipCodePageGenerations;
        private readonly uint[] _expansionCodePageGenerations;
        private readonly uint[] _realFastCodePageGenerations;
        private readonly Dictionary<uint, HostTrapStub> _hostTrapStubs = new Dictionary<uint, HostTrapStub>();
        private readonly Dictionary<ushort, Action<M68kCpuState>> _relocatableHostTrapStubs = new Dictionary<ushort, Action<M68kCpuState>>();
        private readonly List<MappedMemoryRegion> _mappedMemoryRegions = new List<MappedMemoryRegion>();
        private readonly List<AmigaCiaInterruptEvent> _pendingCiaInterrupts = new List<AmigaCiaInterruptEvent>(16);
        private readonly AmigaCiaInterruptEvent[] _drainedCiaInterruptBuffer = new AmigaCiaInterruptEvent[MaxPendingInterruptEvents];
        private readonly ReusableReadOnlyList<AmigaCiaInterruptEvent> _drainedCiaInterrupts = new ReusableReadOnlyList<AmigaCiaInterruptEvent>();
        private readonly BoundedBusAccessLog _busAccesses = new BoundedBusAccessLog(MaxCapturedBusAccesses);
        private readonly ChipPresentationWriteHistory _presentationWriteHistory;
        private readonly IAgnusChipSlotTiming _diagnosticChipSlots;
        private readonly AgnusHrmSlotEngine _hrmSlotEngine;
        private readonly bool _captureBusAccesses;
        private readonly bool _useFastZeroWaitAccesses;
        private readonly bool _useChipSlotScheduler;
        private readonly bool _liveAgnusDmaDefault;
        private readonly AmigaRealTimeClock? _realTimeClock;
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
        private ushort _nextHostTrapId = 1;

        internal event Action<uint, int>? JitEligibleMemoryWritten;

        event Action<uint, int>? IM68kJitBus.JitCodeRangeWritten
        {
            add => JitEligibleMemoryWritten += value;
            remove => JitEligibleMemoryWritten -= value;
        }

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
            int realFastRamSize = 0,
            uint realFastRamBase = AmigaConstants.A500RealFastRamBase,
            bool enableHardwareSpecialization = false,
            bool realTimeClockEnabled = false,
            Func<DateTimeOffset>? realTimeClockNowProvider = null,
            IEnumerable<AmigaHardfileConfiguration>? hardfiles = null)
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
            _hrmSlotEngine = new AgnusHrmSlotEngine();
            _presentationWriteHistory = new ChipPresentationWriteHistory(chipRamSize);
            _captureBusAccesses = captureBusAccesses;
            _liveAgnusDmaDefault = enableLiveAgnusDma;
            _realTimeClock = realTimeClockEnabled ? new AmigaRealTimeClock(realTimeClockNowProvider) : null;
            _chipRamDecodeSize = Math.Max(MinimumChipRamDecodeSize, (uint)chipRamSize);
            ChipDmaAddressMask = (((uint)chipRamSize - 1u) & AmigaConstants.A500OcsChipDmaAddressMask) & 0x00FF_FFFEu;
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
            CopperHdf = new CopperHdfController(hardfiles ?? Array.Empty<AmigaHardfileConfiguration>());
            Display = new OcsDisplay(this, enableLiveDisplayDma);
            _diagnosticChipSlots = _hrmSlotEngine;
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
            ResetCiaAForHardwareReset();
            CiaB.Reset();
            LiveAgnusDmaEnabled = _liveAgnusDmaDefault;
            AudioDmaMinimumPeriod = audioDmaMinimumPeriod;
        }

        public Paula Paula { get; }

        public AmigaDiskController Disk { get; }

        public CopperHdfController CopperHdf { get; }

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

        public bool RealTimeClockEnabled => _realTimeClock != null;

        public bool StrictCpuPhysicalDataMapping { get; set; }

        public IReadOnlyList<CustomRegisterWrite> CustomRegisterWrites => Paula.Writes;

        public IReadOnlyList<AmigaBusAccessResult> BusAccesses => _busAccesses;

        public bool DiskDivergenceTraceEnabled => Disk.DivergenceTraceEnabled;

        public long CiaBTimerAIntervalCycles => CiaB.TimerAIntervalCycles;

        internal bool LiveAgnusDmaEnabled { get; private set; }

        internal bool LiveDisplayDmaEnabled => Display.LiveDmaEnabled;

        internal int AudioDmaMinimumPeriod { get; }

        internal void ConfigureDiskDivergenceTrace(string backend, Func<AmigaDiskTraceCpuContext>? cpuContextProvider)
            => Disk.ConfigureDivergenceTrace(backend, cpuContextProvider);

        public void Dispose()
            => CopperHdf.Dispose();

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
            System.Diagnostics.Debug.Assert(cycle >= 0, "Agnus slot cycles must be non-negative.");
            return AgnusChipSlotScheduler.AlignToSlot(cycle);
        }

        internal long FindHrmDmaCandidate(long requestedCycle)
        {
            System.Diagnostics.Debug.Assert(requestedCycle >= 0, "Agnus DMA candidate cycles must be non-negative.");
            var candidate = NextChipSlotCycle(requestedCycle);
            while (_hrmSlotEngine.IsReserved(candidate))
            {
                candidate += AgnusChipSlotScheduler.SlotCycles;
            }

            return candidate;
        }

        internal bool IsHrmChipSlotReserved(long cycle)
        {
            return _hrmSlotEngine.IsReserved(cycle);
        }

        public void Reset()
        {
            Array.Clear(_chipRam);
            Array.Clear(_expansionRam);
            Array.Clear(_realFastRam);
            Array.Clear(_pendingCustomBytes);
            Array.Clear(_pendingCustomByteWritten);
            _hostTrapStubs.Clear();
            _relocatableHostTrapStubs.Clear();
            _nextHostTrapId = 1;
            _mappedMemoryRegions.Clear();
            _romOverlayRegion = null;
            _romOverlayEnabled = true;
            StrictCpuPhysicalDataMapping = false;
            _pendingCiaInterrupts.Clear();
            _busAccesses.Clear();
            ClearChipSlots();
            LiveAgnusDmaEnabled = _liveAgnusDmaDefault;
            _realTimeClock?.ResetControlRegisters();
            ResetCiaAForHardwareReset();
            CiaB.Reset();
            Keyboard.Reset();
            foreach (var gamePort in _gamePorts)
            {
                gamePort.Reset();
            }

            Paula.Reset();
            Disk.Reset();
            CopperHdf.Reset();
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

        public ushort RegisterHostTrapStub(uint address, Action<M68kCpuState> callback)
        {
            address = NormalizeAddress(address);
            var trapId = AllocateHostTrapId();
            _hostTrapStubs[address] = new HostTrapStub(address, trapId, callback ?? throw new ArgumentNullException(nameof(callback)));
            TouchCodePages(address, 4);
            NotifyJitEligibleMemoryWritten(address, 4);
            return trapId;
        }

        public ushort RegisterRelocatableHostTrapStub(Action<M68kCpuState> callback)
        {
            var trapId = AllocateHostTrapId();
            _relocatableHostTrapStubs[trapId] = callback ?? throw new ArgumentNullException(nameof(callback));
            return trapId;
        }

        public bool TryInvokeHostTrap(uint instructionProgramCounter, ushort trapId, M68kCpuState state)
        {
            instructionProgramCounter = NormalizeAddress(instructionProgramCounter);
            if (_hostTrapStubs.TryGetValue(instructionProgramCounter, out var stub) &&
                stub.TrapId == trapId)
            {
                stub.Callback(state);
                return true;
            }

            if (_relocatableHostTrapStubs.TryGetValue(trapId, out var callback) &&
                TryReadRelocatableHostTrapId(instructionProgramCounter, out var actualTrapId) &&
                actualTrapId == trapId)
            {
                callback(state);
                return true;
            }

            return false;
        }

        public bool HasHostTrapStub(uint address)
        {
            address = NormalizeAddress(address);
            return _hostTrapStubs.ContainsKey(address) ||
                TryReadRelocatableHostTrapId(address, out _);
        }

        bool IM68kCodeReader.HasHostTrapStub(uint address)
            => HasHostTrapStub(address);

        private ushort AllocateHostTrapId()
        {
            var trapId = _nextHostTrapId++;
            if (trapId == 0)
            {
                throw new AmigaEmulationException("Host trap id space exhausted.");
            }

            return trapId;
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
            ResetCiaAForHardwareReset();
            CiaB.Reset();
            Keyboard.Reset();
            UpdateCiaAPortAOutputSideEffects();
            Paula.Reset();
            Disk.Reset();
            CopperHdf.Reset();
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

        byte IM68kBus.ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
            => ReadByte(address, ref cycle, ToAmigaBusAccessKind(accessKind));

        ushort IM68kBus.ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
            => ReadWord(address, ref cycle, ToAmigaBusAccessKind(accessKind));

        uint IM68kBus.ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
            => ReadLong(address, ref cycle, ToAmigaBusAccessKind(accessKind));

        void IM68kBus.WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
            => WriteByte(address, value, ref cycle, ToAmigaBusAccessKind(accessKind));

        void IM68kBus.WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
            => WriteWord(address, value, ref cycle, ToAmigaBusAccessKind(accessKind));

        void IM68kBus.WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
            => WriteLong(address, value, ref cycle, ToAmigaBusAccessKind(accessKind));

        bool IM68kPhysicalAddressMap.IsCpuPhysicalAddressMapped(uint address, int byteCount, M68kBusAccessKind accessKind)
            => IsCpuPhysicalAddressMapped(address, byteCount, ToAmigaBusAccessKind(accessKind));

        bool IM68kFastMemoryBus.TryReadFastByte(uint address, M68kBusAccessKind accessKind, out byte value)
        {
            _ = accessKind;
            value = ReadHostByte(address);
            return true;
        }

        bool IM68kFastMemoryBus.TryReadFastWord(uint address, M68kBusAccessKind accessKind, out ushort value)
        {
            _ = accessKind;
            value = ReadHostWord(address);
            return true;
        }

        bool IM68kFastMemoryBus.TryReadFastLong(uint address, M68kBusAccessKind accessKind, out uint value)
        {
            _ = accessKind;
            value = ReadHostLong(address);
            return true;
        }

        bool IM68kFastMemoryBus.TryWriteFastByte(uint address, byte value, M68kBusAccessKind accessKind)
        {
            _ = accessKind;
            WriteHostByte(address, value);
            return true;
        }

        bool IM68kFastMemoryBus.TryWriteFastWord(uint address, ushort value, M68kBusAccessKind accessKind)
        {
            _ = accessKind;
            WriteHostWord(address, value);
            return true;
        }

        bool IM68kFastMemoryBus.TryWriteFastLong(uint address, uint value, M68kBusAccessKind accessKind)
        {
            _ = accessKind;
            WriteHostLong(address, value);
            return true;
        }

        private static AmigaBusAccessKind ToAmigaBusAccessKind(M68kBusAccessKind accessKind)
            => accessKind switch
            {
                M68kBusAccessKind.CpuInstructionFetch => AmigaBusAccessKind.CpuInstructionFetch,
                M68kBusAccessKind.CpuDataWrite => AmigaBusAccessKind.CpuDataWrite,
                _ => AmigaBusAccessKind.CpuDataRead
            };

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
            if (TryGetCiaRegister(address, out var directCia, out var directCiaRegister))
            {
                return ReadCpuCiaByte(address, directCia, directCiaRegister, ref cycle, accessKind);
            }

            var target = ClassifyTarget(address);
            var requestedCycle = cycle;
            if (_useFastZeroWaitAccesses)
            {
                var fastAccess = GrantFastCpuAccess(target, address, AmigaBusAccessSize.Byte, cycle, accessKind, isWrite: false);
                AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, fastAccess.GrantedCycle, isWrite: false);
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
            AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, access.GrantedCycle, isWrite: false);
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

        private byte ReadCpuCiaByte(
            uint address,
            AmigaCia cia,
            int ciaRegister,
            ref long cycle,
            AmigaBusAccessKind accessKind)
        {
            var access = _useFastZeroWaitAccesses
                ? GrantFastCpuAccess(AmigaBusAccessTarget.Cia, address, AmigaBusAccessSize.Byte, cycle, accessKind, isWrite: false)
                : Arbitrate(AmigaBusRequester.Cpu, accessKind, AmigaBusAccessTarget.Cia, address, AmigaBusAccessSize.Byte, cycle, isWrite: false);
            AdvanceCiaRegisterObservableEventsTo(access.GrantedCycle);
            var value = ReadCiaRegisterValue(cia, ciaRegister);
            cycle = access.CompletedCycle;
            if (cia == CiaA && ciaRegister == 0x0C)
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
            var requestedCycle = cycle;
            if (_useFastZeroWaitAccesses)
            {
                var fastAccess = GrantFastCpuAccess(target, address, AmigaBusAccessSize.Byte, cycle, accessKind, isWrite: true);
                AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, fastAccess.GrantedCycle, isWrite: true);
                WriteRawByte(address, value, fastAccess.GrantedCycle);
                cycle = fastAccess.CompletedCycle;
                return;
            }

            var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, target, address, AmigaBusAccessSize.Byte, cycle, isWrite: true);
            AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, access.GrantedCycle, isWrite: true);
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
            var requestedCycle = cycle;
            if (_useFastZeroWaitAccesses)
            {
                var fastAccess = GrantFastCpuAccess(target, address, AmigaBusAccessSize.Word, cycle, accessKind, isWrite: true);
                AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, fastAccess.GrantedCycle, isWrite: true);
                WriteRawWord(address, value, fastAccess.GrantedCycle);
                cycle = fastAccess.CompletedCycle;
                return;
            }

            var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, target, address, AmigaBusAccessSize.Word, cycle, isWrite: true);
            AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, access.GrantedCycle, isWrite: true);
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
            var requestedCycle = cycle;
            if (_useFastZeroWaitAccesses)
            {
                var fastAccess = GrantFastCpuAccess(target, address, AmigaBusAccessSize.Word, cycle, accessKind, isWrite: false);
                AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, fastAccess.GrantedCycle, isWrite: false);
                var fastValue = sampleCustomAtGrantedCycle && target == AmigaBusAccessTarget.CustomRegisters
                    ? ReadRawWord(address, fastAccess.GrantedCycle)
                    : ReadRawWord(address);
                cycle = fastAccess.CompletedCycle;
                return fastValue;
            }

            var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, target, address, AmigaBusAccessSize.Word, cycle, isWrite: false);
            AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, access.GrantedCycle, isWrite: false);
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
            var requestedCycle = cycle;
            if (_useFastZeroWaitAccesses)
            {
                var fastAccess = GrantFastCpuAccess(target, address, AmigaBusAccessSize.Long, cycle, accessKind, isWrite: false);
                AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, fastAccess.GrantedCycle, isWrite: false);
                var fastValue = sampleCustomAtGrantedCycle && target == AmigaBusAccessTarget.CustomRegisters
                    ? ReadRawLong(address, fastAccess.GrantedCycle, GetSecondWordCycle(fastAccess))
                    : ReadRawLong(address);
                cycle = fastAccess.CompletedCycle;
                return fastValue;
            }

            var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, target, address, AmigaBusAccessSize.Long, cycle, isWrite: false);
            AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, access.GrantedCycle, isWrite: false);
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
            var requestedCycle = cycle;
            if (_useFastZeroWaitAccesses)
            {
                var fastAccess = GrantFastCpuAccess(target, address, AmigaBusAccessSize.Long, cycle, accessKind, isWrite: true);
                AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, fastAccess.GrantedCycle, isWrite: true);
                WriteRawLong(address, value, fastAccess.GrantedCycle, GetSecondWordCycle(fastAccess));
                cycle = fastAccess.CompletedCycle;
                return;
            }

            var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, target, address, AmigaBusAccessSize.Long, cycle, isWrite: true);
            AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, access.GrantedCycle, isWrite: true);
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

        ushort IM68kCodeReader.ReadHostWord(uint address)
            => ReadHostWord(address);

        bool IM68kJitBus.IsJitCodeAddress(uint physicalAddress, int byteCount, M68kBusAccessKind accessKind)
        {
            _ = accessKind;
            if (byteCount <= 0)
            {
                return false;
            }

            physicalAddress = NormalizeAddress(physicalAddress);
            var lastAddress = NormalizeAddress(physicalAddress + (uint)(byteCount - 1));
            return IsJitSnapshotReadableAddress(physicalAddress) &&
                IsJitSnapshotReadableAddress(lastAddress);
        }

        bool IM68kJitBus.IsJitReadOnlyCodeAddress(uint physicalAddress, int byteCount, M68kBusAccessKind accessKind)
        {
            _ = accessKind;
            if (byteCount <= 0)
            {
                return false;
            }

            physicalAddress = NormalizeAddress(physicalAddress);
            var lastAddress = NormalizeAddress(physicalAddress + (uint)(byteCount - 1));
            return ClassifyTarget(physicalAddress) == AmigaBusAccessTarget.Rom &&
                ClassifyTarget(lastAddress) == AmigaBusAccessTarget.Rom;
        }

        ushort IM68kJitBus.ReadJitCodeWord(uint physicalAddress)
            => ReadHostWord(physicalAddress);

        uint IM68kJitBus.GetJitCodePageGeneration(uint physicalAddress)
            => GetCodePageGeneration(physicalAddress);

        bool IM68kJitBus.JitCodeRangeGenerationMatches(
            uint physicalAddress,
            int byteCount,
            uint startGeneration,
            uint endGeneration)
            => CodeRangeGenerationMatches(physicalAddress, byteCount, startGeneration, endGeneration);

        bool IM68kJitBus.TryCaptureJitCodeSnapshot(
            uint physicalRoot,
            int maxBytes,
            out M68kJitCodeSnapshot snapshot)
            => TryCaptureJitCodeSnapshot(physicalRoot, maxBytes, out snapshot);

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

        public void CopyFromMemory(uint address, Span<byte> destination)
        {
            if (destination.IsEmpty)
            {
                return;
            }

            address = NormalizeAddress(address);
            if (TryGetContiguousReadableSpan(address, destination.Length, out var source))
            {
                source.CopyTo(destination);
                return;
            }

            for (var i = 0; i < destination.Length; i++)
            {
                destination[i] = ReadByte(address + (uint)i);
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

        public bool IsCpuPhysicalAddressMapped(uint address, int byteCount, AmigaBusAccessKind accessKind)
        {
            if (byteCount < 0)
            {
                return false;
            }

            if (byteCount == 0)
            {
                return true;
            }

            if ((StrictCpuPhysicalDataMapping ||
                    accessKind == AmigaBusAccessKind.CpuInstructionFetch) &&
                (address > 0x00FF_FFFFu ||
                    (uint)(byteCount - 1) > 0x00FF_FFFFu - address))
            {
                return false;
            }

            if (byteCount > 0x0100_0000)
            {
                return false;
            }

            for (var offset = 0; offset < byteCount; offset++)
            {
                var byteAddress = NormalizeAddress(address + (uint)offset);
                if (!IsCpuPhysicalByteMapped(byteAddress, accessKind))
                {
                    return false;
                }
            }

            return true;
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
            var value = ReadChipWordForPresentation(address, access.GrantedCycle);

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
                granted = TryReserveExactFixedDmaSlot(request, out access);
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
                granted = TryReserveExactFixedDmaSlot(request, out access);
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
            Debug.Assert(requestedCycle >= 0, "Live bitplane DMA request cycles must be non-negative.");
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
                access = ReserveBitplaneDmaSlot(address, requestedCycle);
                granted = access.CompletedCycle > access.GrantedCycle;
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
            Debug.Assert(requestedCycle >= 0, "Live copper DMA request cycles must be non-negative.");
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

            return TryReserveExactFixedDmaSlot(request, out access);
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
            var value = ReadChipWordForPresentation(address, access.GrantedCycle);

            return new AmigaDeviceWordReadResult(value, access);
        }

        internal long PredictDiskDmaCompletionCycle(long requestedCycle)
        {
            requestedCycle = Math.Max(0, requestedCycle);
            if (!_useChipSlotScheduler)
            {
                return requestedCycle;
            }

            var grant = PredictDiskDmaGrantCycle(requestedCycle);
            return grant + AgnusChipSlotScheduler.SlotCycles;
        }

        internal long PredictDiskDmaGrantCycle(long requestedCycle)
        {
            requestedCycle = Math.Max(0, requestedCycle);
            return _useChipSlotScheduler
                ? AgnusHrmOcsSlotTable.FindNextFixedDmaSlot(requestedCycle, AgnusChipSlotOwner.Disk)
                : requestedCycle;
        }

        internal bool TryReserveDiskDmaSlotThrough(
            uint address,
            bool isWrite,
            long requestedCycle,
            long latestGrantCycle,
            out AmigaBusAccessResult access)
        {
            address = MaskChipDmaAddress(address);
            requestedCycle = Math.Max(0, requestedCycle);
            latestGrantCycle = Math.Max(0, latestGrantCycle);
            var request = new AmigaBusAccessRequest(
                AmigaBusRequester.Disk,
                AmigaBusAccessKind.DiskDma,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                isWrite);
            if (!_useChipSlotScheduler)
            {
                access = new AmigaBusAccessResult(request, requestedCycle, requestedCycle);
                return requestedCycle <= latestGrantCycle;
            }

            return _hrmSlotEngine.TryReserveFixedDmaSlotThrough(
                request,
                AgnusChipSlotOwner.Disk,
                latestGrantCycle,
                out access);
        }

        internal ushort ReadChipDmaWordAtGrantedSlot(uint address, long grantedCycle)
        {
            address = MaskChipDmaAddress(address);
            return ReadChipWordForPresentation(address, grantedCycle);
        }

        internal void WriteChipDmaWordAtGrantedSlot(uint address, ushort value, long grantedCycle)
        {
            address = MaskChipDmaAddress(address);
            WriteChipDmaWord(address, value, grantedCycle);
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
            var target = ClassifyTarget(address);
            if (target == AmigaBusAccessTarget.CustomRegisters && requester != AmigaBusRequester.Cpu)
            {
                var grantedCycle = Math.Max(0, requestedCycle);
                MarkCopperIntreqWriteIfNeeded(requester, address, value, grantedCycle);
                WriteRawWord(address, value, grantedCycle);
                return;
            }

            var access = Arbitrate(requester, kind, target, address, AmigaBusAccessSize.Word, requestedCycle, isWrite: true);
            MarkCopperIntreqWriteIfNeeded(requester, address, value, access.GrantedCycle);
            WriteRawWord(address, value, access.GrantedCycle);
        }

        private void MarkCopperIntreqWriteIfNeeded(AmigaBusRequester requester, uint address, ushort value, long cycle)
        {
            if (requester != AmigaBusRequester.Copper ||
                (value & 0x8000) == 0 ||
                (value & AmigaConstants.IntreqCopper) == 0 ||
                address < 0x00DFF000 ||
                address >= 0x00DFF200 ||
                ((address - 0x00DFF000) & 0x01FE) != 0x09C)
            {
                return;
            }

            Paula.DelayCopperInterruptRecognition(cycle);
        }

        internal void RequestHardwareInterrupt(ushort intreqBit, long cycle)
        {
            Paula.RequestInterrupt(intreqBit, Math.Max(0, cycle));
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
                RequestHardwareInterrupt(AmigaConstants.IntreqVerticalBlank, _nextVerticalBlankCycle);
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
            targetCycle = Math.Max(0, targetCycle);
            Disk.AdvanceEventsTo(targetCycle);
            CiaA.AdvanceTo(targetCycle, _pendingCiaInterrupts);
            CiaB.AdvanceTo(targetCycle, _pendingCiaInterrupts);
        }

        public void AdvanceCiaTimersTo(long targetCycle)
        {
            CiaA.AdvanceTo(targetCycle, _pendingCiaInterrupts);
            CiaB.AdvanceTo(targetCycle, _pendingCiaInterrupts);
        }

        internal void AdvanceCiaRegisterObservableEventsTo(long targetCycle)
        {
            targetCycle = Math.Max(0, targetCycle);
            AdvanceRasterTo(targetCycle);
            Disk.AdvanceCiaEventsTo(targetCycle);
            CiaA.AdvanceTo(targetCycle, _pendingCiaInterrupts);
            CiaB.AdvanceTo(targetCycle, _pendingCiaInterrupts);
        }

        public void AdvanceDmaTo(long targetCycle)
        {
            AdvanceDmaTo(targetCycle, advanceLiveAgnus: true, advancePassiveDiskInput: true);
        }

        public void AdvanceDmaTo(long targetCycle, bool advanceLiveAgnus)
        {
            AdvanceDmaTo(targetCycle, advanceLiveAgnus, advancePassiveDiskInput: true);
        }

        public void AdvanceDmaTo(long targetCycle, bool advanceLiveAgnus, bool advancePassiveDiskInput)
        {
            if (advanceLiveAgnus && LiveAgnusDmaEnabled)
            {
                Agnus.AdvanceTo(targetCycle);
            }

            Paula.AdvanceTo(targetCycle);
            if (advancePassiveDiskInput)
            {
                Disk.AdvanceTo(targetCycle);
            }
            else
            {
                Disk.AdvanceEventsTo(targetCycle);
            }

            Blitter.AdvanceTo(targetCycle);
            Paula.AdvanceTo(targetCycle);
        }

        internal void AdvanceRegisterObservableEventsTo(long targetCycle, uint customRegisterAddress)
        {
            targetCycle = Math.Max(0, targetCycle);
            switch (GetCustomRegisterReadAdvanceKind(customRegisterAddress))
            {
                case CustomRegisterReadAdvanceKind.BeamPosition:
                    UpdateBeamPosition(targetCycle);
                    return;

                case CustomRegisterReadAdvanceKind.InputOnly:
                    return;

                case CustomRegisterReadAdvanceKind.BlitterStatus:
                    Paula.AdvanceRegisterWritesTo(targetCycle);
                    Blitter.AdvanceTo(targetCycle);
                    return;

                case CustomRegisterReadAdvanceKind.InterruptSources:
                    AdvanceRasterTo(targetCycle);
                    CiaA.AdvanceTo(targetCycle, _pendingCiaInterrupts);
                    CiaB.AdvanceTo(targetCycle, _pendingCiaInterrupts);
                    Paula.AdvanceRegisterObservableTo(targetCycle);
                    Disk.AdvanceEventsTo(targetCycle);
                    Blitter.AdvanceTo(targetCycle);
                    Paula.AdvanceRegisterObservableTo(targetCycle);
                    return;

                case CustomRegisterReadAdvanceKind.DiskEventOnly:
                    Paula.AdvanceRegisterWritesTo(targetCycle);
                    Disk.AdvanceEventsTo(targetCycle);
                    return;

                case CustomRegisterReadAdvanceKind.DiskPassiveInput:
                    Paula.AdvanceRegisterWritesTo(targetCycle);
                    Disk.AdvanceTo(targetCycle);
                    return;

                default:
                    Paula.AdvanceRegisterWritesTo(targetCycle);
                    return;
            }
        }

        private static CustomRegisterReadAdvanceKind GetCustomRegisterReadAdvanceKind(uint customRegisterAddress)
        {
            var offset = (ushort)(customRegisterAddress & 0x01FE);
            switch (offset)
            {
                case 0x002:
                    return CustomRegisterReadAdvanceKind.BlitterStatus;
                case 0x004:
                case 0x006:
                    return CustomRegisterReadAdvanceKind.BeamPosition;
                case 0x008:
                case 0x01A:
                    return CustomRegisterReadAdvanceKind.DiskPassiveInput;
                case 0x00A:
                case 0x00C:
                case 0x016:
                    return CustomRegisterReadAdvanceKind.InputOnly;
                case 0x01E:
                    return CustomRegisterReadAdvanceKind.InterruptSources;
                case 0x020:
                case 0x022:
                case 0x024:
                case 0x07E:
                    return CustomRegisterReadAdvanceKind.DiskEventOnly;
                default:
                    return CustomRegisterReadAdvanceKind.RegisterWritesOnly;
            }
        }

        private void AdvanceDmaBeforeCpuChipAccess(
            AmigaBusAccessTarget target,
            uint address,
            long grantedCycle,
            bool isWrite)
        {
            if (target == AmigaBusAccessTarget.CustomRegisters && !isWrite)
            {
                AdvanceRegisterObservableEventsTo(grantedCycle, address);
                return;
            }

            if (target == AmigaBusAccessTarget.ChipRam ||
                target == AmigaBusAccessTarget.ExpansionRam ||
                target == AmigaBusAccessTarget.RealTimeClock ||
                target == AmigaBusAccessTarget.CustomRegisters)
            {
                AdvanceDmaTo(
                    grantedCycle,
                    advanceLiveAgnus: true,
                    advancePassiveDiskInput: ShouldAdvancePassiveDiskInputForCpuAccess(target, address));
                return;
            }

            if (target == AmigaBusAccessTarget.Cia)
            {
                AdvanceCiaRegisterObservableEventsTo(grantedCycle);
            }
        }

        private void AdvanceDmaAfterCpuGrantIfNeeded(
            AmigaBusAccessTarget target,
            uint address,
            long requestedCycle,
            long grantedCycle,
            bool isWrite)
        {
            if ((target == AmigaBusAccessTarget.ChipRam ||
                    target == AmigaBusAccessTarget.ExpansionRam ||
                    target == AmigaBusAccessTarget.RealTimeClock ||
                    target == AmigaBusAccessTarget.CustomRegisters) &&
                grantedCycle <= requestedCycle)
            {
                return;
            }

            AdvanceDmaBeforeCpuChipAccess(target, address, grantedCycle, isWrite);
        }

        private static bool ShouldAdvancePassiveDiskInputForCpuAccess(AmigaBusAccessTarget target, uint address)
            => target == AmigaBusAccessTarget.CustomRegisters &&
                AmigaDiskController.RequiresPassiveInputAdvance(address);

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
            => GetNextCpuBatchWakeCandidateCycle(currentCycle, targetCycle);

        public long GetNextStoppedCpuWakeCandidateCycle(long currentCycle, long targetCycle, int cpuInterruptMask)
            => GetNextCpuBatchWakeCandidateCycle(currentCycle, targetCycle, cpuInterruptMask);

        public long GetNextCpuBatchWakeCandidateCycle(long currentCycle, long targetCycle)
            => GetNextCpuBatchWakeCandidateCycle(currentCycle, targetCycle, out _);

        public long GetNextCpuBatchWakeCandidateCycle(long currentCycle, long targetCycle, int cpuInterruptMask)
            => GetNextCpuBatchWakeCandidateCycle(currentCycle, targetCycle, cpuInterruptMask, out _);

        public long GetNextCpuBatchWakeCandidateCycle(
            long currentCycle,
            long targetCycle,
            out M68kTraceBatchWakeSource wakeSource)
            => GetNextCpuBatchWakeCandidateCycle(currentCycle, targetCycle, cpuInterruptMask: -1, out wakeSource);

        public long GetNextCpuBatchWakeCandidateCycle(
            long currentCycle,
            long targetCycle,
            int cpuInterruptMask,
            out M68kTraceBatchWakeSource wakeSource)
        {
            wakeSource = M68kTraceBatchWakeSource.TargetCycle;
            currentCycle = Math.Max(0, currentCycle);
            targetCycle = Math.Max(currentCycle, targetCycle);
            if (targetCycle <= currentCycle)
            {
                return currentCycle;
            }

            var candidate = targetCycle;
            var pendingPaulaInterruptLevel = Paula.GetHighestCpuVisibleInterruptLevel(currentCycle);
            var pendingPaulaInterruptCanEnter = pendingPaulaInterruptLevel > 0 &&
                (cpuInterruptMask < 0 || pendingPaulaInterruptLevel > (cpuInterruptMask & 0x07));
            if (_pendingCiaInterrupts.Count != 0 || pendingPaulaInterruptCanEnter)
            {
                candidate = currentCycle + 1;
                wakeSource = M68kTraceBatchWakeSource.PendingInterrupt;
            }

            candidate = MinStoppedWakeCandidate(
                candidate,
                currentCycle,
                targetCycle,
                Paula.GetNextCpuVisibleInterruptCycle(currentCycle, targetCycle, cpuInterruptMask),
                M68kTraceBatchWakeSource.PendingInterrupt,
                ref wakeSource);
            candidate = MinStoppedWakeCandidate(
                candidate,
                currentCycle,
                targetCycle,
                _nextVerticalBlankCycle,
                M68kTraceBatchWakeSource.VerticalBlank,
                ref wakeSource);
            candidate = MinStoppedWakeCandidate(
                candidate,
                currentCycle,
                targetCycle,
                CiaB.GetNextTodInterruptCycle(targetCycle, _nextHorizontalSyncCycle, _palLineCycles),
                M68kTraceBatchWakeSource.HorizontalSyncTod,
                ref wakeSource);
            candidate = MinStoppedWakeCandidate(
                candidate,
                currentCycle,
                targetCycle,
                GetNextCiaInterruptCycle(targetCycle),
                M68kTraceBatchWakeSource.CiaTimer,
                ref wakeSource);
            candidate = MinStoppedWakeCandidate(
                candidate,
                currentCycle,
                targetCycle,
                Disk.GetNextWakeCandidateCycle(currentCycle, targetCycle),
                M68kTraceBatchWakeSource.Disk,
                ref wakeSource);
            candidate = MinStoppedWakeCandidate(
                candidate,
                currentCycle,
                targetCycle,
                Paula.GetNextWakeCandidateCycle(currentCycle, targetCycle),
                M68kTraceBatchWakeSource.Paula,
                ref wakeSource);
            candidate = MinStoppedWakeCandidate(
                candidate,
                currentCycle,
                targetCycle,
                Display.GetNextLiveCopperWakeCandidateCycle(currentCycle, targetCycle),
                M68kTraceBatchWakeSource.Copper,
                ref wakeSource);
            candidate = MinStoppedWakeCandidate(
                candidate,
                currentCycle,
                targetCycle,
                Blitter.GetNextWakeCandidateCycle(currentCycle, targetCycle),
                M68kTraceBatchWakeSource.Blitter,
                ref wakeSource);
            return Math.Clamp(candidate, currentCycle + 1, targetCycle);
        }

        private static long MinStoppedWakeCandidate(
            long candidate,
            long currentCycle,
            long targetCycle,
            long? eventCycle,
            M68kTraceBatchWakeSource eventSource,
            ref M68kTraceBatchWakeSource wakeSource)
        {
            if (!eventCycle.HasValue || eventCycle.Value > targetCycle)
            {
                return candidate;
            }

            var cycle = eventCycle.Value <= currentCycle ? currentCycle + 1 : eventCycle.Value;
            if (cycle < candidate)
            {
                wakeSource = eventSource;
                return cycle;
            }

            return candidate;
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
                target == AmigaBusAccessTarget.CustomRegisters &&
                !isWrite)
            {
                AdvanceRegisterObservableEventsTo(requestedCycle, address);
                requestedCycle = Blitter.AdvanceThroughCpuStall(requestedCycle);
            }
            else if (requester == AmigaBusRequester.Cpu &&
                (target == AmigaBusAccessTarget.ChipRam ||
                    target == AmigaBusAccessTarget.ExpansionRam ||
                    target == AmigaBusAccessTarget.RealTimeClock ||
                    target == AmigaBusAccessTarget.CustomRegisters))
            {
                AdvanceDmaTo(
                    requestedCycle,
                    advanceLiveAgnus: true,
                    advancePassiveDiskInput: ShouldAdvancePassiveDiskInputForCpuAccess(target, address));
                requestedCycle = Blitter.AdvanceThroughCpuStall(requestedCycle);
            }

            requestedCycle = Math.Max(0, requestedCycle);

            if (requester == AmigaBusRequester.Cpu && target == AmigaBusAccessTarget.Cia)
            {
                var ciaAccessCycle = CiaPeripheralAccessTiming.AlignToCiaPeripheralAccessCycle(requestedCycle);
                var ciaRequest = new AmigaBusAccessRequest(
                    requester,
                    kind,
                    target,
                    address,
                    size,
                    requestedCycle,
                    isWrite);
                var ciaResult = new AmigaBusAccessResult(ciaRequest, ciaAccessCycle, ciaAccessCycle);
                if (_captureBusAccesses)
                {
                    _busAccesses.Add(ciaResult);
                }

                Agnus.RecordCpuChipAccess(ciaResult);
                return ciaResult;
            }

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
                target == AmigaBusAccessTarget.RealTimeClock ||
                (target == AmigaBusAccessTarget.CustomRegisters && isWrite))
            {
                AdvanceDmaTo(
                    requestedCycle,
                    advanceLiveAgnus: true,
                    advancePassiveDiskInput: ShouldAdvancePassiveDiskInputForCpuAccess(target, address));
                requestedCycle = Blitter.AdvanceThroughCpuStall(requestedCycle);
            }
            else if (target == AmigaBusAccessTarget.CustomRegisters)
            {
                AdvanceRegisterObservableEventsTo(requestedCycle, address);
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
            if (target == AmigaBusAccessTarget.Cia)
            {
                var ciaAccessCycle = CiaPeripheralAccessTiming.AlignToCiaPeripheralAccessCycle(requestedCycle);
                return new AmigaBusAccessResult(request, ciaAccessCycle, ciaAccessCycle);
            }

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
            _hrmSlotEngine.Clear();
        }

        internal void ClearLiveDisplayDmaSlotsFrom(long cycle)
        {
            _hrmSlotEngine.ClearLiveDisplaySlotsFrom(cycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private AmigaBusAccessResult ArbitrateChipSlot(AmigaBusAccessRequest request, AmigaBusAccessResult baseResult)
        {
            _hrmSlotEngine.BlitterPriorityEnabled = (Paula.Dmacon & 0x0400) != 0;
            if (LiveAgnusDmaEnabled &&
                request.Size == AmigaBusAccessSize.Word &&
                Display.HasLiveDisplayWork() &&
                ShouldPrepareDisplayBeforeHrmGrant(request))
            {
                var grantCycle = Math.Max(baseResult.GrantedCycle, request.RequestedCycle);
                if (ShouldPrepareDisplaySlotsOnlyBeforeHrmGrant(request))
                {
                    Display.PrepareLiveDisplaySlotsBeforeHrmGrant(grantCycle);
                }
                else
                {
                    Display.CaptureLiveDisplayDmaBeforeHrmGrant(grantCycle);
                }
            }

            return _hrmSlotEngine.Arbitrate(request, baseResult);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryReserveFixedDmaSlot(AmigaBusAccessRequest request, out AmigaBusAccessResult result)
        {
            return _hrmSlotEngine.TryReserveFixedDmaSlot(request, out result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryReserveExactFixedDmaSlot(AmigaBusAccessRequest request, out AmigaBusAccessResult result)
        {
            return _hrmSlotEngine.TryReserveExactFixedDmaSlot(request, out result);
        }

        internal bool IsFixedDmaSlotReserved(long cycle)
        {
            return _hrmSlotEngine.IsFixedDmaReserved(cycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private AmigaBusAccessResult ReserveBitplaneDmaSlot(uint address, long requestedCycle)
        {
            return _hrmSlotEngine.ReserveBitplaneDmaSlot(address, requestedCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private AmigaBusAccessResult ReserveCopperDmaSlot(uint address, long requestedCycle)
        {
            return _hrmSlotEngine.ReserveCopperDmaSlot(address, requestedCycle);
        }

        private bool ShouldUseChipSlotScheduler(AmigaBusAccessTarget target)
        {
            if (!_useChipSlotScheduler)
            {
                return false;
            }

            if (target == AmigaBusAccessTarget.ChipRam ||
                target == AmigaBusAccessTarget.ExpansionRam ||
                target == AmigaBusAccessTarget.RealTimeClock)
            {
                return LiveAgnusDmaEnabled || !_useFastZeroWaitAccesses;
            }

            return target == AmigaBusAccessTarget.CustomRegisters && LiveAgnusDmaEnabled;
        }

        private static bool ShouldPrepareDisplayBeforeHrmGrant(AmigaBusAccessRequest request)
        {
            return request.Requester == AmigaBusRequester.Cpu ||
                request.Requester == AmigaBusRequester.Blitter ||
                request.Requester == AmigaBusRequester.Copper ||
                request.Requester == AmigaBusRequester.Paula ||
                request.Requester == AmigaBusRequester.Disk ||
                request.Requester == AmigaBusRequester.Host;
        }

        private static bool ShouldPrepareDisplaySlotsOnlyBeforeHrmGrant(AmigaBusAccessRequest request)
            => request.Requester == AmigaBusRequester.Cpu &&
                request.Target == AmigaBusAccessTarget.CustomRegisters &&
                !request.IsWrite;

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

            if (_realTimeClock != null && AmigaRealTimeClock.ContainsAddress(address))
            {
                return AmigaBusAccessTarget.RealTimeClock;
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

            if (TryReadHostTrapStubByte(address, out _))
            {
                return AmigaBusAccessTarget.HostTrap;
            }

            if (CopperHdf.ContainsAutoConfigAddress(address) ||
                CopperHdf.ContainsBoardAddress(address))
            {
                return AmigaBusAccessTarget.Rom;
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

        private byte ReadRawByte(uint address, long sampleCycle)
        {
            address = NormalizeAddress(address);
            if (TryReadHostTrapStubByte(address, out var hostTrapByte))
            {
                return hostTrapByte;
            }

            if (TryReadRomOverlayByte(address, out var overlayValue))
            {
                return overlayValue;
            }

            if (TryGetChipRamOffset(address, out var chipOffset))
            {
                return _chipRam[chipOffset];
            }

            if (_realTimeClock != null && _realTimeClock.TryReadByte(address, out var realTimeClockValue))
            {
                return realTimeClockValue;
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
                UpdateBeamPositionForCustomRead(offset, sampleCycle);
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
                return ReadCiaRegisterValue(cia, ciaRegister);
            }

            if (CopperHdf.ContainsAutoConfigAddress(address))
            {
                return CopperHdf.ReadAutoConfigByte(address);
            }

            if (CopperHdf.ContainsBoardAddress(address))
            {
                return CopperHdf.ReadBoardByte(address);
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

        private bool IsCpuPhysicalByteMapped(uint address, AmigaBusAccessKind accessKind)
        {
            if (address > 0x00FF_FFFFu)
            {
                return false;
            }

            if (IsRomOverlayAddress(address))
            {
                return true;
            }

            if (TryReadHostTrapStubByte(address, out _))
            {
                return true;
            }

            if (CopperHdf.ContainsAutoConfigAddress(address) ||
                CopperHdf.ContainsBoardAddress(address))
            {
                return true;
            }

            for (var i = _mappedMemoryRegions.Count - 1; i >= 0; i--)
            {
                if (_mappedMemoryRegions[i].Contains(address))
                {
                    return true;
                }
            }

            if (!StrictCpuPhysicalDataMapping &&
                accessKind != AmigaBusAccessKind.CpuInstructionFetch)
            {
                return true;
            }

            if (address < _chipRamDecodeSize)
            {
                return address < _chipRam.Length;
            }

            return IsRealFastRamAddress(address) ||
                (_realTimeClock != null && AmigaRealTimeClock.ContainsAddress(address)) ||
                IsExpansionRamAddress(address) ||
                (address >= 0x00DFF000 && address < 0x00DFF200) ||
                TryGetCiaRegister(address, out _, out _);
        }

        private byte ReadCiaRegisterValue(AmigaCia cia, int ciaRegister)
        {
            if (cia == CiaA && ciaRegister == 0)
            {
                var inputPins = Disk.ReadCiaAPortA(0xFF);
                inputPins = ApplyGamePortFireBits(inputPins);
                return cia.ReadPortRegister(ciaRegister, inputPins);
            }

            return cia.ReadRegister(ciaRegister);
        }

        private bool TryReadHostTrapStubByte(uint address, out byte value)
        {
            address = NormalizeAddress(address);
            for (var offset = 0u; offset < 4; offset++)
            {
                var baseAddress = NormalizeAddress(address - offset);
                if (!_hostTrapStubs.TryGetValue(baseAddress, out var entry) ||
                    NormalizeAddress(entry.Address + offset) != address)
                {
                    continue;
                }

                value = offset switch
                {
                    0 => (byte)(HostTrapOpcode >> 8),
                    1 => (byte)(HostTrapOpcode & 0x00FF),
                    2 => (byte)(entry.TrapId >> 8),
                    _ => (byte)entry.TrapId
                };
                return true;
            }

            value = 0;
            return false;
        }

        private bool TryReadRelocatableHostTrapId(uint address, out ushort trapId)
        {
            address = NormalizeAddress(address);
            var opcode = (ushort)((ReadRawByte(address) << 8) | ReadRawByte(address + 1));
            if (opcode != HostTrapOpcode)
            {
                trapId = 0;
                return false;
            }

            trapId = (ushort)((ReadRawByte(address + 2) << 8) | ReadRawByte(address + 3));
            return _relocatableHostTrapStubs.ContainsKey(trapId);
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
            if (TryReadHostTrapStubWord(address, out var hostTrapWord))
            {
                return hostTrapWord;
            }

            if (TryReadRomOverlayWord(address, out var overlayWord))
            {
                return overlayWord;
            }

            if (IsCustomRegisterWordAddress(address))
            {
                return ReadCustomWord((ushort)(address - 0x00DFF000), long.MinValue);
            }

            if (!IsRomOverlayAddress(address) && TryGetChipRamOffset(address, out var chipOffset))
            {
                var nextOffset = (chipOffset + 1) & (_chipRam.Length - 1);
                return (ushort)((_chipRam[chipOffset] << 8) | _chipRam[nextOffset]);
            }

            if (_realTimeClock != null &&
                (AmigaRealTimeClock.ContainsAddress(address) || AmigaRealTimeClock.ContainsAddress(address + 1)))
            {
                return (ushort)((ReadRawByte(address) << 8) | ReadRawByte(address + 1));
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

        private ushort ReadRawWord(uint address, long sampleCycle)
        {
            address = NormalizeAddress(address);
            if (TryReadHostTrapStubWord(address, out var hostTrapWord))
            {
                return hostTrapWord;
            }

            if (TryReadRomOverlayWord(address, out var overlayWord))
            {
                return overlayWord;
            }

            if (IsCustomRegisterWordAddress(address))
            {
                return ReadCustomWord((ushort)(address - 0x00DFF000), sampleCycle);
            }

            if (!IsRomOverlayAddress(address) && TryGetChipRamOffset(address, out var chipOffset))
            {
                var nextOffset = (chipOffset + 1) & (_chipRam.Length - 1);
                return (ushort)((_chipRam[chipOffset] << 8) | _chipRam[nextOffset]);
            }

            if (_realTimeClock != null &&
                (AmigaRealTimeClock.ContainsAddress(address) || AmigaRealTimeClock.ContainsAddress(address + 1)))
            {
                return (ushort)((ReadRawByte(address, sampleCycle) << 8) |
                    ReadRawByte(address + 1, sampleCycle));
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

            return (ushort)((ReadRawByte(address, sampleCycle) << 8) |
                ReadRawByte(address + 1, sampleCycle));
        }

        private static bool IsCustomRegisterWordAddress(uint address)
            => address >= 0x00DFF000 && address < 0x00DFF1FF;

        private ushort ReadCustomWord(ushort offset, long sampleCycle)
        {
            offset = (ushort)(offset & 0x01FE);
            UpdateBeamPositionForCustomRead(offset, sampleCycle);
            switch (offset)
            {
                case 0x002:
                    return (ushort)(Paula.Dmacon | Blitter.DmaconStatusBits);
                case 0x00A:
                    return ReadGamePortData(0);
                case 0x00C:
                    return ReadGamePortData(1);
                case 0x010:
                    return Paula.Adkcon;
                case 0x016:
                    return ReadPotGoData();
                case 0x01C:
                    return Paula.Intena;
                case 0x01E:
                    return Paula.Intreq;
            }

            if (Disk.TryReadWord(offset, out var diskValue))
            {
                return diskValue;
            }

            if (TryReadGamePortCustomByte(offset, out var highGamePortValue) ||
                Display.TryReadByte(offset, out highGamePortValue))
            {
                var lowOffset = (ushort)(offset + 1);
                var low = TryReadGamePortCustomByte(lowOffset, out var lowGamePortValue)
                    ? lowGamePortValue
                    : Display.TryReadByte(lowOffset, out var lowDisplayValue)
                        ? lowDisplayValue
                        : Paula.ReadByte(lowOffset);
                return (ushort)((highGamePortValue << 8) | low);
            }

            return (ushort)((Paula.ReadByte(offset) << 8) | Paula.ReadByte((ushort)(offset + 1)));
        }

        private uint ReadRawLong(uint address)
        {
            return ((uint)ReadRawWord(address) << 16) | ReadRawWord(address + 2);
        }

        internal bool TryReadJitZeroWaitMemory(uint address, M68kOperandSize size, out uint value)
        {
            value = 0;
            if (!_useChipSlotScheduler || (size != M68kOperandSize.Byte && (address & 1) != 0))
            {
                return false;
            }

            address = NormalizeAddress(address);
            var byteCount = GetJitMemoryByteCount(size);
            var target = ClassifyTarget(address);
            if (target == AmigaBusAccessTarget.RealFastRam)
            {
                if (!IsRealFastRamRange(address, byteCount))
                {
                    return false;
                }
            }
            else if (target == AmigaBusAccessTarget.Rom)
            {
                var lastAddress = NormalizeAddress(address + (uint)(byteCount - 1));
                if (ClassifyTarget(lastAddress) != AmigaBusAccessTarget.Rom)
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            value = size switch
            {
                M68kOperandSize.Byte => ReadRawByte(address),
                M68kOperandSize.Word => ReadRawWord(address),
                _ => ReadRawLong(address)
            };
            return true;
        }

        bool IM68kJitFastMemoryBus.TryReadJitZeroWaitMemory(uint physicalAddress, M68kOperandSize size, out uint value)
            => TryReadJitZeroWaitMemory(physicalAddress, size, out value);

        internal bool TryWriteJitZeroWaitMemory(uint address, uint value, M68kOperandSize size)
        {
            if (!_useChipSlotScheduler || (size != M68kOperandSize.Byte && (address & 1) != 0))
            {
                return false;
            }

            address = NormalizeAddress(address);
            var byteCount = GetJitMemoryByteCount(size);
            if (!IsRealFastRamRange(address, byteCount))
            {
                return false;
            }

            var realFastOffset = checked((int)(address - RealFastRamBase));
            if (size == M68kOperandSize.Byte)
            {
                _realFastRam[realFastOffset] = (byte)value;
            }
            else if (size == M68kOperandSize.Word)
            {
                _realFastRam[realFastOffset] = (byte)(value >> 8);
                _realFastRam[realFastOffset + 1] = (byte)value;
            }
            else
            {
                _realFastRam[realFastOffset] = (byte)(value >> 24);
                _realFastRam[realFastOffset + 1] = (byte)(value >> 16);
                _realFastRam[realFastOffset + 2] = (byte)(value >> 8);
                _realFastRam[realFastOffset + 3] = (byte)value;
            }

            CompleteJitZeroWaitWrite(address, byteCount);
            return true;
        }

        bool IM68kJitFastMemoryBus.TryWriteJitZeroWaitMemory(uint physicalAddress, uint value, M68kOperandSize size)
            => TryWriteJitZeroWaitMemory(physicalAddress, value, size);

        internal bool TryWriteJitMaxSpeedColorRegister(uint address, uint value, M68kOperandSize size, long cycle)
        {
            if (!_useChipSlotScheduler || size != M68kOperandSize.Word)
            {
                return false;
            }

            address = NormalizeAddress(address);
            if (address < 0x00DFF180 || address > 0x00DFF1BE || (address & 1) != 0)
            {
                return false;
            }

            WriteRawWord(address, (ushort)value, cycle);
            return true;
        }

        bool IM68kJitTimedMemoryBus.TryReadJitMaxSpeedDeviceRegister(
            uint physicalAddress,
            M68kOperandSize size,
            out uint value)
        {
            value = 0;
            if (!_useChipSlotScheduler ||
                size != M68kOperandSize.Byte ||
                (NormalizeAddress(physicalAddress) & 0x00FF_FFFFu) != 0x00BF_E001u)
            {
                return false;
            }

            value = ReadHostByte(physicalAddress);
            return true;
        }

        bool IM68kJitTimedMemoryBus.TryWriteJitMaxSpeedDeviceRegister(
            uint physicalAddress,
            uint value,
            M68kOperandSize size,
            long cycle)
        {
            physicalAddress = NormalizeAddress(physicalAddress);
            if (size == M68kOperandSize.Byte && physicalAddress == 0x00BF_E001u)
            {
                WriteHostByte(physicalAddress, (byte)value);
                return true;
            }

            return TryWriteJitMaxSpeedColorRegister(physicalAddress, value, size, cycle);
        }

        internal bool TryCaptureJitCodeSnapshot(uint root, int maxBytes, out M68kJitCodeSnapshot snapshot)
        {
            snapshot = default;
            if (maxBytes <= 0)
            {
                return false;
            }

            root = NormalizeAddress(root);
            if (!IsJitSnapshotReadableAddress(root))
            {
                return false;
            }

            var byteCount = Math.Min(maxBytes, 4096);
            var pageCount = GetJitSnapshotPageCount(root, byteCount);
            var pages = new uint[pageCount];
            var beforeGenerations = new uint[pageCount];
            FillJitSnapshotGenerations(root, byteCount, pages, beforeGenerations);
            var bytes = new byte[byteCount];
            for (var i = 0; i < bytes.Length; i++)
            {
                var address = NormalizeAddress(root + (uint)i);
                if (!IsJitSnapshotReadableAddress(address))
                {
                    return false;
                }

                bytes[i] = ReadRawByte(address);
            }

            var afterGenerations = new uint[pageCount];
            FillJitSnapshotGenerations(root, byteCount, pages, afterGenerations);
            for (var i = 0; i < beforeGenerations.Length; i++)
            {
                if (beforeGenerations[i] != afterGenerations[i])
                {
                    return false;
                }
            }

            var hostTrapStubs = new List<uint>();
            for (var offset = 0; offset + 1 < byteCount; offset += 2)
            {
                var address = NormalizeAddress(root + (uint)offset);
                if (HasHostTrapStub(address))
                {
                    hostTrapStubs.Add(address);
                }
            }

            snapshot = new M68kJitCodeSnapshot(
                root,
                bytes,
                new M68kCodeGenerationStamp(pages, beforeGenerations),
                hostTrapStubs.ToArray());
            return true;
        }

        private bool IsJitSnapshotReadableAddress(uint address)
        {
            return ClassifyTarget(address) is
                AmigaBusAccessTarget.ExpansionRam or
                AmigaBusAccessTarget.RealFastRam or
                AmigaBusAccessTarget.Rom;
        }

        private static int GetJitSnapshotPageCount(uint root, int byteCount)
        {
            var startPage = NormalizeAddress(root) >> CodeGenerationPageShift;
            var endPage = NormalizeAddress(root + (uint)Math.Max(0, byteCount - 1)) >> CodeGenerationPageShift;
            if (endPage < startPage)
            {
                return 1;
            }

            return checked((int)(endPage - startPage + 1));
        }

        private void FillJitSnapshotGenerations(uint root, int byteCount, uint[] pages, uint[] generations)
        {
            var startPage = NormalizeAddress(root) >> CodeGenerationPageShift;
            for (var i = 0; i < pages.Length; i++)
            {
                var pageAddress = NormalizeAddress((startPage + (uint)i) << CodeGenerationPageShift);
                pages[i] = pageAddress;
                generations[i] = GetCodePageGeneration(pageAddress);
            }
        }

        internal bool TryGetJitZeroWaitReadMemory(uint address, int byteCount, out byte[] memory, out int offset)
            => TryGetJitZeroWaitReadMemory(address, byteCount, out memory, out offset, out _);

        private bool TryGetJitZeroWaitReadMemory(
            uint address,
            int byteCount,
            out byte[] memory,
            out int offset,
            out M68kJitMemoryKind memoryKind)
        {
            memory = Array.Empty<byte>();
            offset = 0;
            memoryKind = M68kJitMemoryKind.FastRam;
            if (!CanUseJitZeroWaitRegion(address, byteCount))
            {
                return false;
            }

            address = NormalizeAddress(address);
            if (TryGetRealFastRamOffset(address, out var realFastOffset) &&
                realFastOffset + byteCount <= _realFastRam.Length)
            {
                memory = _realFastRam;
                offset = realFastOffset;
                memoryKind = M68kJitMemoryKind.FastRam;
                return true;
            }

            if (TryGetJitRomOverlayReadMemory(address, byteCount, out memory, out offset))
            {
                memoryKind = M68kJitMemoryKind.Overlay;
                return true;
            }

            for (var i = _mappedMemoryRegions.Count - 1; i >= 0; i--)
            {
                if (_mappedMemoryRegions[i].TryGetContiguousReadMemory(address, byteCount, out memory, out offset))
                {
                    memoryKind = M68kJitMemoryKind.Rom;
                    return true;
                }
            }

            memory = Array.Empty<byte>();
            offset = 0;
            return false;
        }

        bool IM68kJitFastMemoryBus.TryGetJitZeroWaitReadMemory(
            uint physicalAddress,
            int byteCount,
            out byte[] memory,
            out int offset,
            out M68kJitMemoryKind memoryKind)
            => TryGetJitZeroWaitReadMemory(physicalAddress, byteCount, out memory, out offset, out memoryKind);

        internal bool TryGetJitZeroWaitWriteMemory(uint address, int byteCount, out byte[] memory, out int offset)
            => TryGetJitZeroWaitWriteMemory(address, byteCount, out memory, out offset, out _);

        private bool TryGetJitZeroWaitWriteMemory(
            uint address,
            int byteCount,
            out byte[] memory,
            out int offset,
            out M68kJitMemoryKind memoryKind)
        {
            memory = Array.Empty<byte>();
            offset = 0;
            memoryKind = M68kJitMemoryKind.FastRam;
            if (!CanUseJitZeroWaitRegion(address, byteCount))
            {
                return false;
            }

            address = NormalizeAddress(address);
            if (!TryGetRealFastRamOffset(address, out var realFastOffset) ||
                realFastOffset + byteCount > _realFastRam.Length)
            {
                return false;
            }

            memory = _realFastRam;
            offset = realFastOffset;
            memoryKind = M68kJitMemoryKind.FastRam;
            return true;
        }

        bool IM68kJitFastMemoryBus.TryGetJitZeroWaitWriteMemory(
            uint physicalAddress,
            int byteCount,
            out byte[] memory,
            out int offset,
            out M68kJitMemoryKind memoryKind)
            => TryGetJitZeroWaitWriteMemory(physicalAddress, byteCount, out memory, out offset, out memoryKind);

        internal void CompleteJitZeroWaitWrite(uint address, int byteCount)
        {
            address = NormalizeAddress(address);
            TouchCodePages(address, byteCount);
            NotifyJitEligibleMemoryWritten(address, byteCount);
        }

        void IM68kJitFastMemoryBus.CompleteJitZeroWaitWrite(uint physicalAddress, int byteCount)
            => CompleteJitZeroWaitWrite(physicalAddress, byteCount);

        private static int GetJitMemoryByteCount(M68kOperandSize size)
        {
            return size == M68kOperandSize.Long ? 4 : size == M68kOperandSize.Word ? 2 : 1;
        }

        private bool CanUseJitZeroWaitRegion(uint address, int byteCount)
        {
            return _useChipSlotScheduler &&
                byteCount > 0 &&
                byteCount <= 4 &&
                (byteCount == 1 || (address & 1) == 0);
        }

        private bool TryGetJitRomOverlayReadMemory(uint address, int byteCount, out byte[] memory, out int offset)
        {
            memory = Array.Empty<byte>();
            offset = 0;
            if (!IsRomOverlayAddress(address))
            {
                return false;
            }

            var lastAddress = NormalizeAddress(address + (uint)(byteCount - 1));
            if (!IsRomOverlayAddress(lastAddress))
            {
                return false;
            }

            var overlayRegion = _romOverlayRegion!;
            var overlayOffset = checked((int)(address % (uint)overlayRegion.Length));
            if (overlayOffset + byteCount > overlayRegion.Length)
            {
                return false;
            }

            memory = overlayRegion.Data;
            offset = overlayOffset;
            return true;
        }

        internal uint ReadJitSlotAwareMemory(ref long cycle, uint address, M68kOperandSize size)
        {
            if (size == M68kOperandSize.Byte)
            {
                return ReadJitSlotAwareMemoryUnchecked(ref cycle, address, size);
            }

            if ((address & 1) != 0)
            {
                throw new AmigaEmulationException(
                    size == M68kOperandSize.Word
                        ? $"Odd MC68000 word read at 0x{address:X8}."
                        : $"Odd MC68000 long read at 0x{address:X8}.");
            }

            return ReadJitSlotAwareMemoryUnchecked(ref cycle, address, size);
        }

        uint IM68kJitTimedMemoryBus.ReadJitTimedMemory(ref long cycle, uint physicalAddress, M68kOperandSize size)
            => ReadJitSlotAwareMemory(ref cycle, physicalAddress, size);

        internal void WriteJitSlotAwareMemory(ref long cycle, uint address, uint value, M68kOperandSize size)
        {
            if (size == M68kOperandSize.Byte)
            {
                WriteJitSlotAwareMemoryUnchecked(ref cycle, address, value, size);
                return;
            }

            if ((address & 1) != 0)
            {
                throw new AmigaEmulationException(
                    size == M68kOperandSize.Word
                        ? $"Odd MC68000 word write at 0x{address:X8}."
                        : $"Odd MC68000 long write at 0x{address:X8}.");
            }

            WriteJitSlotAwareMemoryUnchecked(ref cycle, address, value, size);
        }

        void IM68kJitTimedMemoryBus.WriteJitTimedMemory(
            ref long cycle,
            uint physicalAddress,
            uint value,
            M68kOperandSize size)
            => WriteJitSlotAwareMemory(ref cycle, physicalAddress, value, size);

        private uint ReadJitSlotAwareMemoryUnchecked(ref long cycle, uint address, M68kOperandSize size)
        {
            address = NormalizeAddress(address);
            var target = ClassifyTarget(address);
            var accessSize = ToBusAccessSize(size);
            var access = _useFastZeroWaitAccesses
                ? GrantFastCpuAccess(target, address, accessSize, cycle, AmigaBusAccessKind.CpuDataRead, isWrite: false)
                : Arbitrate(AmigaBusRequester.Cpu, AmigaBusAccessKind.CpuDataRead, target, address, accessSize, cycle, isWrite: false);
            AdvanceDmaBeforeCpuChipAccess(target, address, access.GrantedCycle, isWrite: false);

            var value = TryReadJitMappedMemory(target, address, size, out var mappedValue)
                ? mappedValue
                : ReadJitRawMemory(target, address, size, access);
            cycle = access.CompletedCycle;
            if (size == M68kOperandSize.Byte &&
                target == AmigaBusAccessTarget.Cia &&
                TryGetCiaRegister(address, out var cia, out var ciaRegister) &&
                cia == CiaA &&
                ciaRegister == 0x0C)
            {
                Keyboard.AcknowledgeSerialDataRead(cycle);
            }

            return value;
        }

        private void WriteJitSlotAwareMemoryUnchecked(ref long cycle, uint address, uint value, M68kOperandSize size)
        {
            address = NormalizeAddress(address);
            var target = ClassifyTarget(address);
            var accessSize = ToBusAccessSize(size);
            var access = _useFastZeroWaitAccesses
                ? GrantFastCpuAccess(target, address, accessSize, cycle, AmigaBusAccessKind.CpuDataWrite, isWrite: true)
                : Arbitrate(AmigaBusRequester.Cpu, AmigaBusAccessKind.CpuDataWrite, target, address, accessSize, cycle, isWrite: true);
            AdvanceDmaBeforeCpuChipAccess(target, address, access.GrantedCycle, isWrite: true);
            if (!TryWriteJitMappedMemory(target, address, value, size))
            {
                if (size == M68kOperandSize.Byte)
                {
                    WriteRawByte(address, (byte)value, access.GrantedCycle);
                }
                else if (size == M68kOperandSize.Word)
                {
                    WriteRawWord(address, (ushort)value, access.GrantedCycle);
                }
                else
                {
                    WriteRawLong(address, value, access.GrantedCycle, GetSecondWordCycle(access));
                }
            }

            cycle = access.CompletedCycle;
        }

        private static AmigaBusAccessSize ToBusAccessSize(M68kOperandSize size)
            => size switch
            {
                M68kOperandSize.Byte => AmigaBusAccessSize.Byte,
                M68kOperandSize.Word => AmigaBusAccessSize.Word,
                _ => AmigaBusAccessSize.Long
            };

        private uint ReadJitRawMemory(
            AmigaBusAccessTarget target,
            uint address,
            M68kOperandSize size,
            AmigaBusAccessResult access)
        {
            if (target == AmigaBusAccessTarget.CustomRegisters)
            {
                return size switch
                {
                    M68kOperandSize.Byte => ReadRawByte(address, access.GrantedCycle),
                    M68kOperandSize.Word => ReadRawWord(address, access.GrantedCycle),
                    _ => ReadRawLong(address, access.GrantedCycle, GetSecondWordCycle(access))
                };
            }

            return size switch
            {
                M68kOperandSize.Byte => ReadRawByte(address),
                M68kOperandSize.Word => ReadRawWord(address),
                _ => ReadRawLong(address)
            };
        }

        private bool TryReadJitMappedMemory(
            AmigaBusAccessTarget target,
            uint address,
            M68kOperandSize size,
            out uint value)
        {
            if (target == AmigaBusAccessTarget.ChipRam)
            {
                value = size switch
                {
                    M68kOperandSize.Byte => _chipRam[GetChipRamOffset(address)],
                    M68kOperandSize.Word => ReadChipDmaWord(address),
                    _ => ((uint)ReadChipDmaWord(address) << 16) | ReadChipDmaWord(address + 2)
                };
                return true;
            }

            if (target == AmigaBusAccessTarget.ExpansionRam &&
                TryReadJitLinearMemory(_expansionRam, ExpansionRamBase, address, size, out value))
            {
                return true;
            }

            if (target == AmigaBusAccessTarget.RealFastRam &&
                TryReadJitLinearMemory(_realFastRam, RealFastRamBase, address, size, out value))
            {
                return true;
            }

            value = 0;
            return false;
        }

        private bool TryReadHostTrapStubWord(uint address, out ushort value)
        {
            if (TryReadHostTrapStubByte(address, out var high) &&
                TryReadHostTrapStubByte(address + 1, out var low))
            {
                value = (ushort)((high << 8) | low);
                return true;
            }

            value = 0;
            return false;
        }

        private static bool TryReadJitLinearMemory(
            byte[] memory,
            uint baseAddress,
            uint address,
            M68kOperandSize size,
            out uint value)
        {
            var byteCount = size == M68kOperandSize.Long ? 4 : size == M68kOperandSize.Word ? 2 : 1;
            if (memory.Length == 0 || address < baseAddress)
            {
                value = 0;
                return false;
            }

            var offset = address - baseAddress;
            if (offset >= memory.Length || (ulong)offset + (ulong)byteCount > (ulong)memory.Length)
            {
                value = 0;
                return false;
            }

            var index = (int)offset;
            value = size switch
            {
                M68kOperandSize.Byte => memory[index],
                M68kOperandSize.Word => (uint)((memory[index] << 8) | memory[index + 1]),
                _ => ((uint)memory[index] << 24) |
                    ((uint)memory[index + 1] << 16) |
                    ((uint)memory[index + 2] << 8) |
                    memory[index + 3]
            };
            return true;
        }

        private bool TryWriteJitMappedMemory(
            AmigaBusAccessTarget target,
            uint address,
            uint value,
            M68kOperandSize size)
        {
            if (target == AmigaBusAccessTarget.ExpansionRam &&
                TryWriteJitLinearMemory(_expansionRam, ExpansionRamBase, address, value, size))
            {
                var byteCount = size == M68kOperandSize.Long ? 4 : size == M68kOperandSize.Word ? 2 : 1;
                TouchCodePages(address, byteCount);
                NotifyJitEligibleMemoryWritten(address, byteCount);
                return true;
            }

            if (target == AmigaBusAccessTarget.RealFastRam &&
                TryWriteJitLinearMemory(_realFastRam, RealFastRamBase, address, value, size))
            {
                var byteCount = size == M68kOperandSize.Long ? 4 : size == M68kOperandSize.Word ? 2 : 1;
                TouchCodePages(address, byteCount);
                NotifyJitEligibleMemoryWritten(address, byteCount);
                return true;
            }

            return false;
        }

        private static bool TryWriteJitLinearMemory(
            byte[] memory,
            uint baseAddress,
            uint address,
            uint value,
            M68kOperandSize size)
        {
            var byteCount = size == M68kOperandSize.Long ? 4 : size == M68kOperandSize.Word ? 2 : 1;
            if (memory.Length == 0 || address < baseAddress)
            {
                return false;
            }

            var offset = address - baseAddress;
            if (offset >= memory.Length || (ulong)offset + (ulong)byteCount > (ulong)memory.Length)
            {
                return false;
            }

            var index = (int)offset;
            if (size == M68kOperandSize.Byte)
            {
                memory[index] = (byte)value;
            }
            else if (size == M68kOperandSize.Word)
            {
                memory[index] = (byte)(value >> 8);
                memory[index + 1] = (byte)value;
            }
            else
            {
                memory[index] = (byte)(value >> 24);
                memory[index + 1] = (byte)(value >> 16);
                memory[index + 2] = (byte)(value >> 8);
                memory[index + 3] = (byte)value;
            }

            return true;
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

            if (_realTimeClock != null && _realTimeClock.TryWriteByte(address, value))
            {
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
                    UpdateCiaAPortAOutputSideEffects();
                }

                if (cia == CiaA && ciaRegister == 2)
                {
                    UpdateCiaAPortAOutputSideEffects();
                }

                if (cia == CiaB && ciaRegister is 1 or 3)
                {
                    Disk.WriteCiaBRegister(1, cia.ReadPortRegister(1, 0xFF), grantedCycle);
                }

                return;
            }

            if (CopperHdf.ContainsAutoConfigAddress(address))
            {
                var wasConfigured = CopperHdf.IsConfigured;
                CopperHdf.WriteAutoConfigByte(address, value);
                if (!wasConfigured && CopperHdf.IsConfigured)
                {
                    CopperHdf.InstallBootstrapTraps(this);
                }

                return;
            }

            if (CopperHdf.ContainsBoardAddress(address))
            {
                CopperHdf.TryWriteBoardByte(address, value);
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

            if (_realTimeClock != null &&
                (AmigaRealTimeClock.ContainsAddress(address) || AmigaRealTimeClock.ContainsAddress(address + 1)))
            {
                WriteRawByte(address, (byte)(value >> 8), grantedCycle);
                WriteRawByte(address + 1, (byte)value, grantedCycle);
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

            var nextSlotCycle = access.GrantedCycle +
                (access.Request.Requester == AmigaBusRequester.Cpu
                    ? 2 * AgnusChipSlotScheduler.SlotCycles
                    : AgnusChipSlotScheduler.SlotCycles);
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

        private bool TryGetContiguousReadableSpan(uint address, int byteCount, out ReadOnlySpan<byte> span)
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

        private void ResetCiaAForHardwareReset()
        {
            CiaA.Reset(CiaAPortAResetLatch, CiaAPortAResetDataDirection);
            AudioFilterEnabled = (CiaA.ReadPortRegister(0, 0xFF) & CiaAPortAAudioFilterBit) == 0;
        }

        private void UpdateCiaAPortAOutputSideEffects()
        {
            var portA = CiaA.ReadPortRegister(0, 0xFF);
            AudioFilterEnabled = (portA & CiaAPortAAudioFilterBit) == 0;
            _romOverlayEnabled = (portA & CiaAPortAOverlayBit) != 0;
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

        private bool TryReadRomOverlayWord(uint address, out ushort value)
        {
            address = NormalizeAddress(address);
            if (!_romOverlayEnabled ||
                _romOverlayRegion == null ||
                address >= 0x0008_0000 ||
                address + 1 >= 0x0008_0000)
            {
                value = 0;
                return false;
            }

            var offset = checked((int)(address % (uint)_romOverlayRegion.Length));
            if (offset + 1 >= _romOverlayRegion.Length)
            {
                value = 0;
                return false;
            }

            var data = _romOverlayRegion.Data;
            value = (ushort)((data[offset] << 8) | data[offset + 1]);
            return true;
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

            internal byte[] Data => _data;

            public bool Contains(uint address)
            {
                var offset = address - BaseAddress;
                return address >= BaseAddress && offset < _data.Length;
            }

            public bool TryGetContiguousReadMemory(uint address, int byteCount, out byte[] memory, out int offset)
            {
                if (address < BaseAddress)
                {
                    memory = Array.Empty<byte>();
                    offset = 0;
                    return false;
                }

                var relative = address - BaseAddress;
                if (relative >= _data.Length ||
                    relative + byteCount > _data.Length)
                {
                    memory = Array.Empty<byte>();
                    offset = 0;
                    return false;
                }

                memory = _data;
                offset = checked((int)relative);
                return true;
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
}
