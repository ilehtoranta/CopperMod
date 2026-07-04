/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

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

    internal sealed partial class AmigaBus :
        IM68kBus,
        IM68kCodeReader,
        IM68kPhysicalAddressMap,
        IM68kFastMemoryBus,
        IM68kCpuBusPhaseTrace,
        IM68kInstructionFetchWindowBus,
        IM68kDeferredCpuInstructionTiming,
        IM68kJitBus,
        IM68kJitFastMemoryBus,
        IM68kJitTimedMemoryBus
    {
        private const ushort HostTrapOpcode = 0xFF00;
        private const int MaxCapturedBusAccesses = 65536;
        private const int MaxCapturedCpuBusPhases = 65536;
        private const int MaxPendingInterruptEvents = 65536;
        private const int InstructionFetchWindowMaxBytes = 256;
        private const uint CpuAddressMask = 0x00FF_FFFFu;
        private const int CpuBusBankShift = 16;
        private const int CpuBusBankSize = 1 << CpuBusBankShift;
        private const int CpuBusBankCount = 1 << (24 - CpuBusBankShift);
        private const int MaxDeferredPseudoFastAccesses = 64;
        private const int CodeGenerationPageShift = 8;
        private const int CodeGenerationPageSize = 1 << CodeGenerationPageShift;
        private const uint MinimumChipRamDecodeSize = 0x0020_0000;
        private const string ExactCpuChipSlotFastPathSwitch = "CopperMod.Amiga.ExactCpuChipSlotFastPath";
        private const string ExactCpuChipSlotFastPathEnvironmentVariable = "COPPER_AMIGA_EXACT_CPU_CHIP_SLOT_FAST";
        private const bool ExactCpuChipSlotFastPathDefault = true;
        private const string PaulaDmaFixedSlotFastPathSwitch = "CopperMod.Amiga.PaulaDmaFixedSlotFastPath";
        private const string PaulaDmaFixedSlotFastPathEnvironmentVariable = "COPPER_AMIGA_PAULA_DMA_FIXED_SLOT_FAST";
        private const bool PaulaDmaFixedSlotFastPathDefault = false;
        private const byte CiaAPortAResetLatch = 0xFC;
        private const byte CiaAPortAResetDataDirection = 0x03;
        private const byte CiaAPortAOverlayBit = 0x01;
        private const byte CiaAPortAAudioFilterBit = 0x02;
        private readonly AmigaChipRamBackend _chipRam;
        private readonly byte[] _chipRamData;
        private readonly int _chipRamMask;
        private readonly AmigaLinearRamBackend _expansionRam;
        private readonly AmigaLinearRamBackend _realFastRam;
        private readonly Dictionary<uint, HostTrapStub> _hostTrapStubs = new Dictionary<uint, HostTrapStub>();
        private readonly Dictionary<ushort, Action<M68kCpuState>> _relocatableHostTrapStubs = new Dictionary<ushort, Action<M68kCpuState>>();
        private readonly AmigaMappedMemoryBackend _mappedMemory = new AmigaMappedMemoryBackend();
        private readonly List<AmigaCiaInterruptEvent> _pendingCiaInterrupts = new List<AmigaCiaInterruptEvent>(16);
        private readonly AmigaCiaInterruptEvent[] _drainedCiaInterruptBuffer = new AmigaCiaInterruptEvent[MaxPendingInterruptEvents];
        private readonly ReusableReadOnlyList<AmigaCiaInterruptEvent> _drainedCiaInterrupts = new ReusableReadOnlyList<AmigaCiaInterruptEvent>();
        private readonly uint[] _instructionFetchWindowGeneration = { 1u };
        private readonly BoundedBusAccessLog _busAccesses = new BoundedBusAccessLog(MaxCapturedBusAccesses);
        private readonly BoundedCpuBusPhaseLog _cpuBusPhases = new BoundedCpuBusPhaseLog(MaxCapturedCpuBusPhases);
        private readonly IAgnusChipSlotTiming _diagnosticChipSlots;
        private readonly AgnusHrmSlotEngine _hrmSlotEngine;
        private readonly AmigaRasterlineScheduleCache _rasterlineScheduleCache;
        private readonly AmigaHardwareScheduler _hardwareScheduler;
        private readonly CpuBusBankKind[] _cpuBusBankKinds = new CpuBusBankKind[CpuBusBankCount];
        private readonly int[] _cpuBusBankOffsets = new int[CpuBusBankCount];
        private readonly bool _captureBusAccesses;
        private readonly bool _useFastZeroWaitAccesses;
        private readonly bool _useChipSlotScheduler;
        private readonly bool _useExactCpuChipSlotFastPath;
        /// <summary>
        /// Combined fast-path guard: true when _useExactCpuChipSlotFastPath AND
        /// _useFastZeroWaitAccesses AND !_captureBusAccesses. Eliminates 3 branches
        /// from the per-access hot path.
        /// </summary>
        private readonly bool _exactCpuChipSlotFastPathEnabled;
        private readonly bool _usePaulaDmaFixedSlotFastPath;
        private readonly bool _liveAgnusDmaDefault;
        private readonly AmigaRealTimeClock? _realTimeClock;
        private readonly byte[] _pendingCustomBytes = new byte[0x200];
        private readonly bool[] _pendingCustomByteWritten = new bool[0x200];
        private readonly GamePortState[] _gamePorts = { new GamePortState(), new GamePortState() };
        private readonly long _palFrameCycles;
        private readonly long _palLineCycles;
        private bool _romOverlayEnabled = true;
        private long _nextVerticalBlankCycle;
        private long _nextHorizontalSyncIndex;
        private long _nextHorizontalSyncCycle;
        private long _lastRasterAdvanceCycle;
        private bool _deferredCpuInstructionTimingActive;
        private int _deferredPseudoFastAccessCount;
        private ulong _deferredPseudoFastLongShapeBits;
        private long _deferredPseudoFastReplayCycle;

        private enum CpuBusBankKind : byte
        {
            Unmapped,
            ChipRam,
            ExpansionRam,
            RealFastRam,
            RomOverlay,
            MappedRom,
            Special
        }
        private AmigaBusAccessResult? _lastCpuBusAccess;
        private AgnusChipSlotSnapshot? _lastCpuBusGrantedSlot;
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

            var chipRamDecodeSize = Math.Max(MinimumChipRamDecodeSize, (uint)chipRamSize);
            var chipDmaAddressMask = (((uint)chipRamSize - 1u) & AmigaConstants.A500OcsChipDmaAddressMask) & 0x00FF_FFFEu;
            _chipRam = new AmigaChipRamBackend(chipRamSize, chipRamDecodeSize, chipDmaAddressMask, CodeGenerationPageShift);
            _chipRamData = _chipRam.Data;
            _chipRamMask = _chipRam.Length - 1;
            ExpansionRamBase = NormalizeAddress(expansionRamBase);
            RealFastRamBase = NormalizeAddress(realFastRamBase);
            _expansionRam = new AmigaLinearRamBackend(expansionRamSize, ExpansionRamBase, CodeGenerationPageShift);
            _realFastRam = new AmigaLinearRamBackend(realFastRamSize, RealFastRamBase, CodeGenerationPageShift);
            _hrmSlotEngine = new AgnusHrmSlotEngine();
            _captureBusAccesses = captureBusAccesses;
            _liveAgnusDmaDefault = enableLiveAgnusDma;
            _realTimeClock = realTimeClockEnabled ? new AmigaRealTimeClock(realTimeClockNowProvider) : null;
            ChipDmaAddressMask = chipDmaAddressMask;
            Arbiter = arbiter ?? new ZeroWaitBusArbiter();
            _useChipSlotScheduler = Arbiter is ZeroWaitBusArbiter zeroWaitForSlots &&
                zeroWaitForSlots.BaseAccessCycles == 0;
            _useFastZeroWaitAccesses =
                !captureBusAccesses &&
                _useChipSlotScheduler;
            _useExactCpuChipSlotFastPath = ResolveExactCpuChipSlotFastPath();
            _exactCpuChipSlotFastPathEnabled = _useExactCpuChipSlotFastPath &&
                _useFastZeroWaitAccesses &&
                !captureBusAccesses;
            _usePaulaDmaFixedSlotFastPath = ResolvePaulaDmaFixedSlotFastPath();
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
            _rasterlineScheduleCache = new AmigaRasterlineScheduleCache(this);
            _hardwareScheduler = new AmigaHardwareScheduler(this);
            _palFrameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
            _palLineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;
            _nextVerticalBlankCycle = _palFrameCycles;
            _lastRasterAdvanceCycle = 0;
            ResetHorizontalSyncCounter();
            ResetCiaAForHardwareReset();
            CiaB.Reset();
            LiveAgnusDmaEnabled = _liveAgnusDmaDefault;
            AudioDmaMinimumPeriod = audioDmaMinimumPeriod;
            RebuildCpuBusBankTable();
        }

        private static bool ResolveExactCpuChipSlotFastPath()
        {
            if (AppContext.TryGetSwitch(ExactCpuChipSlotFastPathSwitch, out var switchEnabled))
            {
                return switchEnabled;
            }

            var value = Environment.GetEnvironmentVariable(ExactCpuChipSlotFastPathEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(value))
            {
                return ExactCpuChipSlotFastPathDefault;
            }

            return value.Trim() switch
            {
                "1" => true,
                "true" => true,
                "TRUE" => true,
                "True" => true,
                "0" => false,
                "false" => false,
                "FALSE" => false,
                "False" => false,
                _ => ExactCpuChipSlotFastPathDefault
            };
        }

        private static bool ResolvePaulaDmaFixedSlotFastPath()
        {
            if (AppContext.TryGetSwitch(PaulaDmaFixedSlotFastPathSwitch, out var switchEnabled))
            {
                return switchEnabled;
            }

            var value = Environment.GetEnvironmentVariable(PaulaDmaFixedSlotFastPathEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(value))
            {
                return PaulaDmaFixedSlotFastPathDefault;
            }

            return value.Trim() switch
            {
                "1" => true,
                "true" => true,
                "TRUE" => true,
                "True" => true,
                "0" => false,
                "false" => false,
                "FALSE" => false,
                "False" => false,
                _ => PaulaDmaFixedSlotFastPathDefault
            };
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

        public byte[] ChipRam => _chipRam.Data;

        public byte[] ExpansionRam => _expansionRam.Data;

        public byte[] RealFastRam => _realFastRam.Data;

        public uint ChipDmaAddressMask { get; }

        public uint ExpansionRamBase { get; }

        public uint RealFastRamBase { get; }

        public bool RealTimeClockEnabled => _realTimeClock != null;

        public bool StrictCpuPhysicalDataMapping { get; set; }

        public IReadOnlyList<CustomRegisterWrite> CustomRegisterWrites => Paula.Writes;

        public IReadOnlyList<AmigaBusAccessResult> BusAccesses => _busAccesses;

        internal IReadOnlyList<AmigaCpuBusPhaseTrace> CpuBusPhases => _cpuBusPhases;

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
            _chipRam.ClearData();
            _expansionRam.ClearData();
            _realFastRam.ClearData();
            Array.Clear(_pendingCustomBytes);
            Array.Clear(_pendingCustomByteWritten);
            _hostTrapStubs.Clear();
            _relocatableHostTrapStubs.Clear();
            _nextHostTrapId = 1;
            _mappedMemory.Clear();
            _romOverlayEnabled = true;
            InvalidateInstructionFetchWindows();
            StrictCpuPhysicalDataMapping = false;
            _pendingCiaInterrupts.Clear();
            _busAccesses.Clear();
            _cpuBusPhases.Clear();
            _lastCpuBusAccess = null;
            _lastCpuBusGrantedSlot = null;
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
            _rasterlineScheduleCache.Reset();
            _hardwareScheduler.Reset();
            RebuildCpuBusBankTable();
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
            InvalidateInstructionFetchWindows();
            RebuildCpuBusBankTable();
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
            _cpuBusPhases.Clear();
            _lastCpuBusAccess = null;
            _lastCpuBusGrantedSlot = null;
            ClearChipSlots();
            LiveAgnusDmaEnabled = _liveAgnusDmaDefault;
            ResetCiaAForHardwareReset();
            InvalidateInstructionFetchWindows();
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
            _rasterlineScheduleCache.Reset();
            _hardwareScheduler.Reset();
            RebuildCpuBusBankTable();
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

            _mappedMemory.MapMemory(baseAddress, data, readOnly);
            InvalidateInstructionFetchWindows();
            RebuildCpuBusBankTable();
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

        bool IM68kCpuBusPhaseTrace.CpuBusPhaseTracingEnabled => _captureBusAccesses;

        void IM68kDeferredCpuInstructionTiming.BeginDeferredCpuInstructionTiming(long cycle)
        {
            _deferredCpuInstructionTimingActive = !_captureBusAccesses;
            _deferredPseudoFastAccessCount = 0;
            _deferredPseudoFastLongShapeBits = 0;
            _deferredPseudoFastReplayCycle = cycle;
        }

        void IM68kDeferredCpuInstructionTiming.FlushDeferredCpuInstructionTiming(ref long cycle)
        {
            FlushDeferredPseudoFastTiming(ref cycle);
            _deferredCpuInstructionTimingActive = false;
        }

        void IM68kCpuBusPhaseTrace.RecordCpuBusPhase(in M68kCpuBusPhase phase)
        {
            if (!_captureBusAccesses)
            {
                return;
            }

            var access = _lastCpuBusAccess;
            var secondWordCycle = access.HasValue ? GetSecondWordCycle(access.Value) : phase.CompletedCycle;
            _cpuBusPhases.Add(new AmigaCpuBusPhaseTrace(
                phase,
                access,
                secondWordCycle,
                _lastCpuBusGrantedSlot));
        }

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryReadExactCpuDataByte(uint address, ref long cycle, out byte value)
        {
            if (!TryResolveExactCpuDataRamRegion(
                address,
                1,
                out var region))
            {
                value = 0;
                return false;
            }

            if (region.Target == AmigaBusAccessTarget.ExpansionRam &&
                TryDeferExactCpuExpansionDataTiming(AmigaBusAccessSize.Byte, ref cycle))
            {
                value = region.Memory[region.Offset];
                return true;
            }

            CommitExactCpuDataRamTiming(
                in region,
                AmigaBusAccessSize.Byte,
                ref cycle,
                isWrite: false,
                AmigaBusAccessKind.CpuDataRead,
                out _,
                out _);
            value = region.Memory[region.Offset];
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryReadExactCpuDataWord(uint address, ref long cycle, out ushort value)
        {
            if (!TryResolveExactCpuDataRamRegion(
                address,
                2,
                out var region))
            {
                value = 0;
                return false;
            }

            if (region.Target == AmigaBusAccessTarget.ExpansionRam &&
                TryDeferExactCpuExpansionDataTiming(AmigaBusAccessSize.Word, ref cycle))
            {
                value = (ushort)((region.Memory[region.Offset] << 8) | region.Memory[region.Offset + 1]);
                return true;
            }

            CommitExactCpuDataRamTiming(
                in region,
                AmigaBusAccessSize.Word,
                ref cycle,
                isWrite: false,
                AmigaBusAccessKind.CpuDataRead,
                out _,
                out _);
            value = (ushort)((region.Memory[region.Offset] << 8) | region.Memory[region.Offset + 1]);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryReadExactCpuDataLong(uint address, ref long cycle, out uint value)
        {
            if (!TryResolveExactCpuDataRamRegion(
                address,
                4,
                out var region))
            {
                value = 0;
                return false;
            }

            if (region.Target == AmigaBusAccessTarget.ExpansionRam &&
                TryDeferExactCpuExpansionDataTiming(AmigaBusAccessSize.Long, ref cycle))
            {
                value = ((uint)region.Memory[region.Offset] << 24) |
                    ((uint)region.Memory[region.Offset + 1] << 16) |
                    ((uint)region.Memory[region.Offset + 2] << 8) |
                    region.Memory[region.Offset + 3];
                return true;
            }

            CommitExactCpuDataRamTiming(
                in region,
                AmigaBusAccessSize.Long,
                ref cycle,
                isWrite: false,
                AmigaBusAccessKind.CpuDataRead,
                out _,
                out _);
            value = ((uint)region.Memory[region.Offset] << 24) |
                ((uint)region.Memory[region.Offset + 1] << 16) |
                ((uint)region.Memory[region.Offset + 2] << 8) |
                region.Memory[region.Offset + 3];
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryWriteExactCpuDataByte(uint address, byte value, ref long cycle)
        {
            if (!TryResolveExactCpuDataRamRegion(
                address,
                1,
                out var region))
            {
                return false;
            }

            if (region.Target == AmigaBusAccessTarget.ExpansionRam &&
                TryDeferExactCpuExpansionDataTiming(AmigaBusAccessSize.Byte, ref cycle))
            {
                WriteExactCpuDataByte(region, value, cycle);
                return true;
            }

            CommitExactCpuDataRamTiming(
                in region,
                AmigaBusAccessSize.Byte,
                ref cycle,
                isWrite: true,
                AmigaBusAccessKind.CpuDataWrite,
                out var grantedCycle,
                out _);
            WriteExactCpuDataByte(region, value, grantedCycle);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryWriteExactCpuDataWord(uint address, ushort value, ref long cycle)
        {
            if (!TryResolveExactCpuDataRamRegion(
                address,
                2,
                out var region))
            {
                return false;
            }

            if (region.Target == AmigaBusAccessTarget.ExpansionRam &&
                TryDeferExactCpuExpansionDataTiming(AmigaBusAccessSize.Word, ref cycle))
            {
                WriteExactCpuDataWord(region, value, cycle);
                return true;
            }

            CommitExactCpuDataRamTiming(
                in region,
                AmigaBusAccessSize.Word,
                ref cycle,
                isWrite: true,
                AmigaBusAccessKind.CpuDataWrite,
                out var grantedCycle,
                out _);
            WriteExactCpuDataWord(region, value, grantedCycle);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryWriteExactCpuDataLong(uint address, uint value, ref long cycle)
        {
            if (!TryResolveExactCpuDataRamRegion(
                address,
                4,
                out var region))
            {
                return false;
            }

            if (region.Target == AmigaBusAccessTarget.ExpansionRam &&
                TryDeferExactCpuExpansionDataTiming(AmigaBusAccessSize.Long, ref cycle))
            {
                WriteExactCpuDataLong(region, value, cycle, cycle);
                return true;
            }

            CommitExactCpuDataRamTiming(
                in region,
                AmigaBusAccessSize.Long,
                ref cycle,
                isWrite: true,
                AmigaBusAccessKind.CpuDataWrite,
                out var grantedCycle,
                out var secondWordCycle);
            WriteExactCpuDataLong(
                region,
                value,
                grantedCycle,
                secondWordCycle);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryResolveExactCpuDataRamRegion(
            uint address,
            int byteCount,
            out AmigaExactCpuDataRamRegion region)
        {
            var normalizedAddress = NormalizeAddress(address);
            region = default;
            if (!_useFastZeroWaitAccesses ||
                byteCount <= 0 ||
                (ulong)normalizedAddress + (ulong)byteCount > 0x0100_0000UL)
            {
                return false;
            }

            var lastAddress = normalizedAddress + (uint)(byteCount - 1);
            var bankIndex = (int)(normalizedAddress >> CpuBusBankShift);
            if (bankIndex != (int)(lastAddress >> CpuBusBankShift))
            {
                return false;
            }

            var offset = _cpuBusBankOffsets[bankIndex] + (int)(normalizedAddress & (CpuBusBankSize - 1));
            switch (_cpuBusBankKinds[bankIndex])
            {
                case CpuBusBankKind.ChipRam:
                    if (offset < 0 || offset + byteCount > _chipRam.Length)
                    {
                        return false;
                    }

                    region = new AmigaExactCpuDataRamRegion(
                        AmigaMemoryBackendKind.ChipRam,
                        AmigaBusAccessTarget.ChipRam,
                        normalizedAddress,
                        _chipRam.Data,
                        offset);
                    return true;

                case CpuBusBankKind.ExpansionRam:
                    if (offset < 0 || offset + byteCount > _expansionRam.Length)
                    {
                        return false;
                    }

                    region = new AmigaExactCpuDataRamRegion(
                        AmigaMemoryBackendKind.ExpansionRam,
                        AmigaBusAccessTarget.ExpansionRam,
                        normalizedAddress,
                        _expansionRam.Data,
                        offset);
                    return true;

                case CpuBusBankKind.RealFastRam:
                    if (offset < 0 || offset + byteCount > _realFastRam.Length)
                    {
                        return false;
                    }

                    region = new AmigaExactCpuDataRamRegion(
                        AmigaMemoryBackendKind.RealFastRam,
                        AmigaBusAccessTarget.RealFastRam,
                        normalizedAddress,
                        _realFastRam.Data,
                        offset);
                    return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryDeferExactCpuExpansionDataTiming(
            AmigaBusAccessSize size,
            ref long cycle)
        {
            if (!_deferredCpuInstructionTimingActive)
            {
                return false;
            }

            if (_deferredPseudoFastAccessCount >= MaxDeferredPseudoFastAccesses)
            {
                FlushDeferredPseudoFastTiming(ref cycle);
                return false;
            }

            var index = _deferredPseudoFastAccessCount;
            if (index == 0)
            {
                _deferredPseudoFastReplayCycle = cycle;
            }

            if (size == AmigaBusAccessSize.Long)
            {
                _deferredPseudoFastLongShapeBits |= 1UL << index;
            }

            _deferredPseudoFastAccessCount = index + 1;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FlushDeferredPseudoFastTiming(ref long cycle)
        {
            var count = _deferredPseudoFastAccessCount;
            if (count == 0)
            {
                return;
            }

            var longShapeBits = _deferredPseudoFastLongShapeBits;
            var replayCycle = _deferredPseudoFastReplayCycle;
            _deferredPseudoFastAccessCount = 0;
            _deferredPseudoFastLongShapeBits = 0;

            for (var i = 0; i < count; i++)
            {
                var size = ((longShapeBits >> i) & 1UL) != 0
                    ? AmigaBusAccessSize.Long
                    : AmigaBusAccessSize.Word;
                CommitExactCpuExpansionDataTiming(
                    ExpansionRamBase,
                    size,
                    ref replayCycle,
                    isWrite: false,
                    AmigaBusAccessKind.CpuDataRead,
                    out _,
                    out _);
            }

            if (cycle < replayCycle)
            {
                cycle = replayCycle;
            }

            _deferredPseudoFastReplayCycle = cycle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CommitExactCpuDataRamTiming(
            in AmigaExactCpuDataRamRegion region,
            AmigaBusAccessSize size,
            ref long cycle,
            bool isWrite,
            AmigaBusAccessKind kind,
            out long grantedCycle,
            out long secondWordCycle)
        {
            if (region.Target == AmigaBusAccessTarget.ExpansionRam)
            {
                CommitExactCpuExpansionDataTiming(
                    region.Address,
                    size,
                    ref cycle,
                    isWrite,
                    kind,
                    out grantedCycle,
                    out secondWordCycle);
                return;
            }

            CommitExactCpuDataTiming(
                region.Target,
                region.Address,
                size,
                ref cycle,
                isWrite,
                kind,
                out grantedCycle,
                out secondWordCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CommitExactCpuDataTiming(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            ref long cycle,
            bool isWrite,
            AmigaBusAccessKind kind,
            out long grantedCycle,
            out long secondWordCycle)
        {
            FlushDeferredPseudoFastTiming(ref cycle);
            var requestedCycle = cycle;
            if (TryCommitExactCpuChipDataAccessFast(
                target,
                address,
                size,
                requestedCycle,
                isWrite,
                kind,
                out grantedCycle,
                out secondWordCycle,
                out var completedCycle,
                synchronizeDmaAfterGrant: true))
            {
                cycle = completedCycle;
                return;
            }

            if (_useFastZeroWaitAccesses)
            {
                GrantFastCpuAccessCycles(
                    target,
                    address,
                    size,
                    cycle,
                    kind,
                    isWrite,
                    out grantedCycle,
                    out secondWordCycle,
                    out var fastCompletedCycle);
                AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, grantedCycle, isWrite);
                cycle = fastCompletedCycle;
                return;
            }

            var access = Arbitrate(
                AmigaBusRequester.Cpu,
                kind,
                target,
                address,
                size,
                cycle,
                isWrite);
            AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, access.GrantedCycle, isWrite);
            cycle = access.CompletedCycle;
            grantedCycle = access.GrantedCycle;
            secondWordCycle = GetSecondWordCycle(access);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CommitExactCpuExpansionDataTiming(
            uint address,
            AmigaBusAccessSize size,
            ref long cycle,
            bool isWrite,
            AmigaBusAccessKind kind,
            out long grantedCycle,
            out long secondWordCycle)
        {
            var requestedCycle = cycle;
            if (TryCommitExactCpuChipDataAccessFast(
                AmigaBusAccessTarget.ExpansionRam,
                address,
                size,
                requestedCycle,
                isWrite,
                kind,
                out grantedCycle,
                out secondWordCycle,
                out var completedCycle,
                synchronizeDmaAfterGrant: false))
            {
                cycle = completedCycle;
                return;
            }

            if (_useFastZeroWaitAccesses)
            {
                GrantFastCpuAccessCycles(
                    AmigaBusAccessTarget.ExpansionRam,
                    address,
                    size,
                    cycle,
                    kind,
                    isWrite,
                    out grantedCycle,
                    out secondWordCycle,
                    out var fastCompletedCycle);
                cycle = fastCompletedCycle;
                return;
            }

            var access = Arbitrate(
                AmigaBusRequester.Cpu,
                kind,
                AmigaBusAccessTarget.ExpansionRam,
                address,
                size,
                cycle,
                isWrite);
            cycle = access.CompletedCycle;
            grantedCycle = access.GrantedCycle;
            secondWordCycle = GetSecondWordCycle(access);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryCommitExactCpuChipDataAccessFast(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            AmigaBusAccessKind kind,
            out long grantedCycle,
            out long secondWordCycle,
            out long completedCycle,
            bool synchronizeDmaAfterGrant)
        {
            grantedCycle = 0;
            secondWordCycle = 0;
            completedCycle = 0;

            // #1: Single combined guard replaces 5 separate checks.
            // _useExactCpuChipSlotFastPath, _useFastZeroWaitAccesses are always true in production;
            // _captureBusAccesses is always false; target is already known to be ChipRam/ExpansionRam
            // from the caller; ShouldUseChipSlotScheduler is the only dynamic check needed.
            if (!_exactCpuChipSlotFastPathEnabled ||
                !ShouldUseChipSlotScheduler(target))
            {
                return false;
            }

            // #2: Check cached clean-through cycle before full drain.
            var grantRequestCycle = requestedCycle;
            if (!_hardwareScheduler.IsSlotContendedCleanThrough(grantRequestCycle))
            {
                _hardwareScheduler.DrainForCpuAccess(target, address, grantRequestCycle, isWrite);
            }

            if (Blitter.Busy)
            {
                grantRequestCycle = Blitter.AdvanceThroughCpuStall(grantRequestCycle);
            }

            grantRequestCycle = Math.Max(0, grantRequestCycle);

            // #3: Removed per-access BlitterPriorityEnabled write.
            // Now updated only when DMACON changes (see Paula DMACON write handler).

            // #4: Inline the display preparation check to avoid method call overhead.
            if (LiveAgnusDmaEnabled &&
                size == AmigaBusAccessSize.Word &&
                Display.HasLiveDisplayWork())
            {
                Display.CaptureLiveDisplayDmaBeforeHrmGrant(grantRequestCycle);
            }

            if (size == AmigaBusAccessSize.Long)
            {
                _hrmSlotEngine.GrantCpuDataLongSlots(
                    kind,
                    target,
                    address,
                    grantRequestCycle,
                    isWrite,
                    out grantedCycle,
                    out secondWordCycle,
                    out completedCycle);
            }
            else
            {
                _hrmSlotEngine.GrantCpuDataSingleSlot(
                    kind,
                    target,
                    address,
                    size,
                    grantRequestCycle,
                    isWrite,
                    out grantedCycle,
                    out completedCycle);
                secondWordCycle = grantedCycle;
            }

            // #5: Skip diagnostic call when no wait occurred.
            if (grantedCycle > grantRequestCycle)
            {
                Agnus.RecordCpuChipWaitCycles(grantedCycle - grantRequestCycle);
            }

            if (synchronizeDmaAfterGrant)
            {
                AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, grantedCycle, isWrite);
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PrepareLiveDisplayBeforeCpuHrmGrantIfNeeded(
            AmigaBusAccessTarget target,
            AmigaBusAccessSize size,
            bool isWrite,
            long grantCycle)
        {
            if (!LiveAgnusDmaEnabled ||
                size != AmigaBusAccessSize.Word ||
                !Display.HasLiveDisplayWork())
            {
                return;
            }

            if (target == AmigaBusAccessTarget.CustomRegisters && !isWrite)
            {
                Display.PrepareLiveDisplaySlotsBeforeHrmGrant(grantCycle);
                return;
            }

            Display.CaptureLiveDisplayDmaBeforeHrmGrant(grantCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteExactCpuDataByte(
            in AmigaExactCpuDataRamRegion region,
            byte value,
            long grantedCycle)
        {
            if (region.Target == AmigaBusAccessTarget.ChipRam)
            {
                _chipRam.WriteByteAtOffset(region.Offset, value, grantedCycle);
                TouchCodePage(region.Address);
                return;
            }

            region.Memory[region.Offset] = value;
            TouchCodePage(region.Address);
            NotifyJitEligibleMemoryWritten(region.Address, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteExactCpuDataWord(
            in AmigaExactCpuDataRamRegion region,
            ushort value,
            long grantedCycle)
        {
            if (region.Target == AmigaBusAccessTarget.ChipRam)
            {
                _chipRam.WriteContiguousWordAtOffset(region.Offset, value, grantedCycle);
                TouchCodePage(region.Address);
                TouchCodePage(region.Address + 1);
                return;
            }

            region.Memory[region.Offset] = (byte)(value >> 8);
            region.Memory[region.Offset + 1] = (byte)value;
            TouchCodePage(region.Address);
            TouchCodePage(region.Address + 1);
            NotifyJitEligibleMemoryWritten(region.Address, 2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteExactCpuDataLong(
            in AmigaExactCpuDataRamRegion region,
            uint value,
            long firstWordCycle,
            long secondWordCycle)
        {
            if (region.Target == AmigaBusAccessTarget.ChipRam)
            {
                _chipRam.WriteContiguousLongAtOffset(region.Offset, value, firstWordCycle, secondWordCycle);
                TouchCodePage(region.Address);
                TouchCodePage(region.Address + 1);
                TouchCodePage(region.Address + 2);
                TouchCodePage(region.Address + 3);
                return;
            }

            region.Memory[region.Offset] = (byte)(value >> 24);
            region.Memory[region.Offset + 1] = (byte)(value >> 16);
            region.Memory[region.Offset + 2] = (byte)(value >> 8);
            region.Memory[region.Offset + 3] = (byte)value;
            TouchCodePage(region.Address);
            TouchCodePage(region.Address + 1);
            TouchCodePage(region.Address + 2);
            TouchCodePage(region.Address + 3);
            NotifyJitEligibleMemoryWritten(region.Address, 4);
        }

        bool IM68kInstructionFetchWindowBus.TryGetInstructionFetchWindow(
            uint address,
            out M68kInstructionFetchWindow window)
            => TryGetInstructionFetchWindow(address, out window);

        void IM68kInstructionFetchWindowBus.CommitInstructionFetchWindowWord(
            in M68kInstructionFetchWindow window,
            uint address,
            ref long cycle)
            => CommitInstructionFetchWindowWord(in window, address, ref cycle);

        private static AmigaBusAccessKind ToAmigaBusAccessKind(M68kBusAccessKind accessKind)
            => accessKind switch
            {
                M68kBusAccessKind.CpuInstructionFetch => AmigaBusAccessKind.CpuInstructionFetch,
                M68kBusAccessKind.CpuDataWrite => AmigaBusAccessKind.CpuDataWrite,
                _ => AmigaBusAccessKind.CpuDataRead
            };

        internal bool TryGetInstructionFetchWindow(uint address, out M68kInstructionFetchWindow window)
        {
            window = default;
            address = NormalizeAddress(address);
            if ((address & 1) != 0)
            {
                return false;
            }

            var endAddress = GetInstructionFetchPageEnd(address);
            if (!TrimInstructionFetchWindowForHostTrap(address, ref endAddress))
            {
                return false;
            }

            return TryGetRomOverlayInstructionFetchWindow(address, endAddress, out window) ||
                TryGetChipRamInstructionFetchWindow(address, endAddress, out window) ||
                TryGetLinearInstructionFetchWindow(
                    address,
                    endAddress,
                    _expansionRam.Data,
                    _expansionRam.BaseAddress,
                    AmigaBusAccessTarget.ExpansionRam,
                    out window) ||
                TryGetLinearInstructionFetchWindow(
                    address,
                    endAddress,
                    _realFastRam.Data,
                    _realFastRam.BaseAddress,
                    AmigaBusAccessTarget.RealFastRam,
                    out window) ||
                TryGetMappedRomInstructionFetchWindow(address, endAddress, out window);
        }

        internal void CommitInstructionFetchWindowWord(
            in M68kInstructionFetchWindow window,
            uint address,
            ref long cycle)
        {
            address = NormalizeAddress(address);
            var target = (AmigaBusAccessTarget)window.BusTag;
            CommitExactCpuDataTiming(
                target,
                address,
                AmigaBusAccessSize.Word,
                ref cycle,
                isWrite: false,
                AmigaBusAccessKind.CpuInstructionFetch,
                out _,
                out _);
        }

        private static uint GetInstructionFetchPageEnd(uint address)
        {
            var end = (address & ~(uint)(InstructionFetchWindowMaxBytes - 1)) + InstructionFetchWindowMaxBytes;
            return end == 0 || end > 0x0100_0000u ? 0x0100_0000u : end;
        }

        private bool TryGetRomOverlayInstructionFetchWindow(
            uint address,
            uint endAddress,
            out M68kInstructionFetchWindow window)
        {
            window = default;
            if (!_mappedMemory.TryGetRomOverlayInstructionFetchMemory(
                address,
                endAddress,
                _romOverlayEnabled,
                out var memory,
                out var offset,
                out var windowEnd))
            {
                return false;
            }

            return TryCreateInstructionFetchWindow(
                memory,
                offset,
                address,
                windowEnd,
                AmigaBusAccessTarget.Rom,
                out window);
        }

        private bool TryGetChipRamInstructionFetchWindow(
            uint address,
            uint endAddress,
            out M68kInstructionFetchWindow window)
        {
            window = default;
            if (IsRomOverlayAddress(address) ||
                !TryGetChipRamOffset(address, out var offset))
            {
                return false;
            }

            var contiguousEnd = address + (uint)(_chipRam.Length - offset);
            var decodeEnd = Math.Min(endAddress, _chipRam.DecodeSize);
            return TryCreateInstructionFetchWindow(
                _chipRam.Data,
                offset,
                address,
                Math.Min(decodeEnd, contiguousEnd),
                AmigaBusAccessTarget.ChipRam,
                out window);
        }

        private bool TryGetLinearInstructionFetchWindow(
            uint address,
            uint endAddress,
            byte[] memory,
            uint baseAddress,
            AmigaBusAccessTarget target,
            out M68kInstructionFetchWindow window)
        {
            window = default;
            if (memory.Length == 0 || address < baseAddress)
            {
                return false;
            }

            var relative = address - baseAddress;
            if (relative >= memory.Length)
            {
                return false;
            }

            var memoryOffset = checked((int)relative);
            var contiguousEnd = address + (uint)(memory.Length - memoryOffset);
            return TryCreateInstructionFetchWindow(
                memory,
                memoryOffset,
                address,
                Math.Min(endAddress, contiguousEnd),
                target,
                out window);
        }

        private bool TryGetMappedRomInstructionFetchWindow(
            uint address,
            uint endAddress,
            out M68kInstructionFetchWindow window)
        {
            window = default;
            if (!_mappedMemory.TryGetMappedRomInstructionFetchMemory(
                address,
                endAddress,
                out var memory,
                out var offset,
                out var windowEnd))
            {
                return false;
            }

            return TryCreateInstructionFetchWindow(
                memory,
                offset,
                address,
                windowEnd,
                AmigaBusAccessTarget.Rom,
                out window);
        }

        private bool TryCreateInstructionFetchWindow(
            byte[] memory,
            int memoryOffset,
            uint address,
            uint endAddress,
            AmigaBusAccessTarget target,
            out M68kInstructionFetchWindow window)
        {
            window = default;
            if ((ulong)address + 1u >= endAddress)
            {
                return false;
            }

            window = new M68kInstructionFetchWindow(
                memory,
                memoryOffset,
                address,
                endAddress,
                CpuAddressMask,
                (int)target,
                _instructionFetchWindowGeneration,
                _instructionFetchWindowGeneration[0]);
            return true;
        }

        private bool TrimInstructionFetchWindowForHostTrap(uint startAddress, ref uint endAddress)
        {
            foreach (var entry in _hostTrapStubs.Values)
            {
                var trapAddress = NormalizeAddress(entry.Address);
                if (startAddress >= trapAddress && startAddress < trapAddress + 4u)
                {
                    return false;
                }

                if (trapAddress > startAddress && trapAddress < endAddress)
                {
                    endAddress = trapAddress;
                }
            }

            return true;
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
            FlushDeferredPseudoFastTiming(ref cycle);
            address = NormalizeAddress(address);
            if (IsCustomRegisterByteAddress(address))
            {
                return ReadCpuCustomByte(address, ref cycle, accessKind, sampleCustomAtGrantedCycle);
            }

            if (TryGetCiaRegister(address, out var directCia, out var directCiaRegister))
            {
                return ReadCpuCiaByte(address, directCia, directCiaRegister, ref cycle, accessKind);
            }

            var target = ClassifyTarget(address);
            var requestedCycle = cycle;
            if (_useFastZeroWaitAccesses)
            {
                GrantFastCpuAccessCycles(
                    target,
                    address,
                    AmigaBusAccessSize.Byte,
                    cycle,
                    accessKind,
                    isWrite: false,
                    out var grantedCycle,
                    out _,
                    out var completedCycle);
                AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, grantedCycle, isWrite: false);
                var fastValue = sampleCustomAtGrantedCycle && target == AmigaBusAccessTarget.CustomRegisters
                    ? ReadRawByte(address, grantedCycle)
                    : ReadRawByte(address);
                cycle = completedCycle;
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
            long completedCycle;
            if (_useFastZeroWaitAccesses)
            {
                GrantFastCpuAccessCycles(
                    AmigaBusAccessTarget.Cia,
                    address,
                    AmigaBusAccessSize.Byte,
                    cycle,
                    accessKind,
                    isWrite: false,
                    out _,
                    out _,
                    out completedCycle);
            }
            else
            {
                completedCycle = Arbitrate(
                    AmigaBusRequester.Cpu,
                    accessKind,
                    AmigaBusAccessTarget.Cia,
                    address,
                    AmigaBusAccessSize.Byte,
                    cycle,
                    isWrite: false).CompletedCycle;
            }

            var value = ReadCiaRegisterValue(cia, ciaRegister);
            cycle = completedCycle;
            if (cia == CiaA && ciaRegister == 0x0C)
            {
                Keyboard.AcknowledgeSerialDataRead(cycle);
            }

            return value;
        }

        private byte ReadCpuCustomByte(
            uint address,
            ref long cycle,
            AmigaBusAccessKind accessKind,
            bool sampleCustomAtGrantedCycle)
        {
            var requestedCycle = cycle;
            long grantedCycle;
            long completedCycle;
            if (_useFastZeroWaitAccesses)
            {
                GrantFastCpuAccessCycles(
                    AmigaBusAccessTarget.CustomRegisters,
                    address,
                    AmigaBusAccessSize.Byte,
                    cycle,
                    accessKind,
                    isWrite: false,
                    out grantedCycle,
                    out _,
                    out completedCycle);
            }
            else
            {
                var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, AmigaBusAccessTarget.CustomRegisters, address, AmigaBusAccessSize.Byte, cycle, isWrite: false);
                grantedCycle = access.GrantedCycle;
                completedCycle = access.CompletedCycle;
            }

            AdvanceDmaAfterCpuGrantIfNeeded(AmigaBusAccessTarget.CustomRegisters, address, requestedCycle, grantedCycle, isWrite: false);
            var sampleCycle = sampleCustomAtGrantedCycle ? grantedCycle : long.MinValue;
            var value = ReadCustomByte((ushort)(address - 0x00DFF000), sampleCycle);
            cycle = completedCycle;
            return value;
        }

        private ushort ReadCpuCustomWord(
            uint address,
            ref long cycle,
            AmigaBusAccessKind accessKind,
            bool sampleCustomAtGrantedCycle)
        {
            var requestedCycle = cycle;
            long grantedCycle;
            long completedCycle;
            if (_useFastZeroWaitAccesses)
            {
                GrantFastCpuAccessCycles(
                    AmigaBusAccessTarget.CustomRegisters,
                    address,
                    AmigaBusAccessSize.Word,
                    cycle,
                    accessKind,
                    isWrite: false,
                    out grantedCycle,
                    out _,
                    out completedCycle);
            }
            else
            {
                var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, AmigaBusAccessTarget.CustomRegisters, address, AmigaBusAccessSize.Word, cycle, isWrite: false);
                grantedCycle = access.GrantedCycle;
                completedCycle = access.CompletedCycle;
            }

            AdvanceDmaAfterCpuGrantIfNeeded(AmigaBusAccessTarget.CustomRegisters, address, requestedCycle, grantedCycle, isWrite: false);
            var sampleCycle = sampleCustomAtGrantedCycle ? grantedCycle : long.MinValue;
            var value = ReadCustomWord((ushort)(address - 0x00DFF000), sampleCycle);
            cycle = completedCycle;
            return value;
        }

        private uint ReadCpuCustomLong(
            uint address,
            ref long cycle,
            AmigaBusAccessKind accessKind,
            bool sampleCustomAtGrantedCycle)
        {
            var requestedCycle = cycle;
            long grantedCycle;
            long secondWordCycle;
            long completedCycle;
            if (_useFastZeroWaitAccesses)
            {
                GrantFastCpuAccessCycles(
                    AmigaBusAccessTarget.CustomRegisters,
                    address,
                    AmigaBusAccessSize.Long,
                    cycle,
                    accessKind,
                    isWrite: false,
                    out grantedCycle,
                    out secondWordCycle,
                    out completedCycle);
            }
            else
            {
                var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, AmigaBusAccessTarget.CustomRegisters, address, AmigaBusAccessSize.Long, cycle, isWrite: false);
                grantedCycle = access.GrantedCycle;
                secondWordCycle = GetSecondWordCycle(access);
                completedCycle = access.CompletedCycle;
            }

            AdvanceDmaAfterCpuGrantIfNeeded(AmigaBusAccessTarget.CustomRegisters, address, requestedCycle, grantedCycle, isWrite: false);
            var firstWordCycle = sampleCustomAtGrantedCycle ? grantedCycle : long.MinValue;
            var sampledSecondWordCycle = sampleCustomAtGrantedCycle ? secondWordCycle : long.MinValue;
            var value = ReadCustomLong(address, firstWordCycle, sampledSecondWordCycle);
            cycle = completedCycle;
            return value;
        }

        public void WriteByte(uint address, byte value, long cycle)
        {
            WriteByte(address, value, ref cycle, AmigaBusAccessKind.CpuDataWrite);
        }

        public void WriteByte(uint address, byte value, ref long cycle, AmigaBusAccessKind accessKind)
        {
            FlushDeferredPseudoFastTiming(ref cycle);
            address = NormalizeAddress(address);
            var target = ClassifyTarget(address);
            var requestedCycle = cycle;
            if (_useFastZeroWaitAccesses)
            {
                GrantFastCpuAccessCycles(
                    target,
                    address,
                    AmigaBusAccessSize.Byte,
                    cycle,
                    accessKind,
                    isWrite: true,
                    out var grantedCycle,
                    out _,
                    out var completedCycle);
                AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, grantedCycle, isWrite: true);
                WriteRawByte(address, value, grantedCycle);
                cycle = completedCycle;
                return;
            }

            var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, target, address, AmigaBusAccessSize.Byte, cycle, isWrite: true);
            AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, access.GrantedCycle, isWrite: true);
            WriteRawByte(address, value, access.GrantedCycle);
            cycle = access.CompletedCycle;
        }

        internal void WriteTasCpuDataByte(uint address, byte value, ref long cycle)
        {
            FlushDeferredPseudoFastTiming(ref cycle);
            address = NormalizeAddress(address);
            var target = ClassifyTarget(address);
            var requestedCycle = cycle;
            if (_useFastZeroWaitAccesses)
            {
                GrantFastCpuAccessCycles(
                    target,
                    address,
                    AmigaBusAccessSize.Byte,
                    cycle,
                    AmigaBusAccessKind.CpuDataWrite,
                    isWrite: true,
                    out var grantedCycle,
                    out _,
                    out var completedCycle);
                AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, grantedCycle, isWrite: true);
                if (target != AmigaBusAccessTarget.ChipRam)
                {
                    WriteRawByte(address, value, grantedCycle);
                }

                cycle = completedCycle;
                return;
            }

            var access = Arbitrate(AmigaBusRequester.Cpu, AmigaBusAccessKind.CpuDataWrite, target, address, AmigaBusAccessSize.Byte, cycle, isWrite: true);
            AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, access.GrantedCycle, isWrite: true);
            if (target != AmigaBusAccessTarget.ChipRam)
            {
                WriteRawByte(address, value, access.GrantedCycle);
            }

            cycle = access.CompletedCycle;
        }

        public void WriteWord(uint address, ushort value, long cycle)
        {
            WriteWord(address, value, ref cycle, AmigaBusAccessKind.CpuDataWrite);
        }

        public void WriteWord(uint address, ushort value, ref long cycle, AmigaBusAccessKind accessKind)
        {
            FlushDeferredPseudoFastTiming(ref cycle);
            address = NormalizeAddress(address);
            var target = ClassifyTarget(address);
            var requestedCycle = cycle;
            if (_useFastZeroWaitAccesses)
            {
                GrantFastCpuAccessCycles(
                    target,
                    address,
                    AmigaBusAccessSize.Word,
                    cycle,
                    accessKind,
                    isWrite: true,
                    out var grantedCycle,
                    out _,
                    out var completedCycle);
                AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, grantedCycle, isWrite: true);
                WriteRawWord(address, value, grantedCycle);
                cycle = completedCycle;
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
            FlushDeferredPseudoFastTiming(ref cycle);
            address = NormalizeAddress(address);
            if (IsCustomRegisterWordAddress(address))
            {
                return ReadCpuCustomWord(address, ref cycle, accessKind, sampleCustomAtGrantedCycle);
            }

            var target = ClassifyTarget(address);
            var requestedCycle = cycle;
            if (_useFastZeroWaitAccesses)
            {
                GrantFastCpuAccessCycles(
                    target,
                    address,
                    AmigaBusAccessSize.Word,
                    cycle,
                    accessKind,
                    isWrite: false,
                    out var grantedCycle,
                    out _,
                    out var completedCycle);
                AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, grantedCycle, isWrite: false);
                var fastValue = sampleCustomAtGrantedCycle && target == AmigaBusAccessTarget.CustomRegisters
                    ? ReadRawWord(address, grantedCycle)
                    : ReadRawWord(address);
                cycle = completedCycle;
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
            FlushDeferredPseudoFastTiming(ref cycle);
            address = NormalizeAddress(address);
            if (IsCustomRegisterByteAddress(address))
            {
                return ReadCpuCustomLong(address, ref cycle, accessKind, sampleCustomAtGrantedCycle);
            }

            var target = ClassifyTarget(address);
            var requestedCycle = cycle;
            if (_useFastZeroWaitAccesses)
            {
                GrantFastCpuAccessCycles(
                    target,
                    address,
                    AmigaBusAccessSize.Long,
                    cycle,
                    accessKind,
                    isWrite: false,
                    out var grantedCycle,
                    out var secondWordCycle,
                    out var completedCycle);
                AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, grantedCycle, isWrite: false);
                var fastValue = sampleCustomAtGrantedCycle && target == AmigaBusAccessTarget.CustomRegisters
                    ? ReadRawLong(address, grantedCycle, secondWordCycle)
                    : ReadRawLong(address);
                cycle = completedCycle;
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
            FlushDeferredPseudoFastTiming(ref cycle);
            address = NormalizeAddress(address);
            var target = ClassifyTarget(address);
            var requestedCycle = cycle;
            if (_useFastZeroWaitAccesses)
            {
                GrantFastCpuAccessCycles(
                    target,
                    address,
                    AmigaBusAccessSize.Long,
                    cycle,
                    accessKind,
                    isWrite: true,
                    out var grantedCycle,
                    out var secondWordCycle,
                    out var completedCycle);
                AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, grantedCycle, isWrite: true);
                WriteRawLong(address, value, grantedCycle, secondWordCycle);
                cycle = completedCycle;
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

            data.CopyTo(_chipRam.Data.AsSpan((int)address, data.Length));
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
            return ReadPaulaDmaWord(-1, address, requestedCycle);
        }

        public PaulaDmaReadResult ReadPaulaDmaWord(int channel, uint address, long requestedCycle)
        {
            var reservation = ReservePaulaDmaWord(channel, address, requestedCycle);
            return CommitPaulaDmaWord(in reservation);
        }

        internal AmigaDmaWordReservation ReservePaulaDmaWord(int channel, uint address, long requestedCycle)
        {
            address = MaskChipDmaAddress(address);
            requestedCycle = Math.Max(0, requestedCycle);
            if (_usePaulaDmaFixedSlotFastPath &&
                _useChipSlotScheduler &&
                LiveAgnusDmaEnabled &&
                (uint)channel < AmigaConstants.PaulaChannelCount)
            {
                var fastAccess = ReserveLivePaulaDmaWordSlot(channel, address, requestedCycle);
                if (_captureBusAccesses)
                {
                    _busAccesses.Add(fastAccess);
                }

                return new AmigaDmaWordReservation(address, granted: true, fastAccess);
            }

            var slotChannel = LiveAgnusDmaEnabled ? channel : -1;
            var access = Arbitrate(
                AmigaBusRequester.Paula,
                AmigaBusAccessKind.PaulaDma,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                isWrite: false,
                slotChannel);
            return new AmigaDmaWordReservation(address, granted: true, access);
        }

        internal bool TryReservePaulaDmaWordExactSlot(
            int channel,
            uint address,
            long slotCycle,
            out AmigaDmaWordReservation reservation)
        {
            address = MaskChipDmaAddress(address);
            slotCycle = Math.Max(0, slotCycle);
            var request = new AmigaBusAccessRequest(
                AmigaBusRequester.Paula,
                AmigaBusAccessKind.PaulaDma,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                slotCycle,
                isWrite: false,
                channel);

            AmigaBusAccessResult access;
            bool granted;
            if (!_useChipSlotScheduler)
            {
                access = new AmigaBusAccessResult(request, slotCycle, slotCycle);
                granted = true;
            }
            else
            {
                _hrmSlotEngine.BlitterPriorityEnabled = (Paula.Dmacon & 0x0400) != 0;
                if (Display.HasLiveDisplayWork())
                {
                    Display.CaptureLiveDisplayDmaBeforeHrmGrant(slotCycle);
                }

                granted = TryReserveExactFixedDmaSlot(request, out access);
            }

            reservation = new AmigaDmaWordReservation(address, granted, access);
            if (granted)
            {
                CaptureDmaReservation(in reservation);
            }

            return granted;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private AmigaBusAccessResult ReserveLivePaulaDmaWordSlot(int channel, uint address, long requestedCycle)
        {
            _hrmSlotEngine.BlitterPriorityEnabled = (Paula.Dmacon & 0x0400) != 0;
            if (Display.HasLiveDisplayWork())
            {
                Display.CaptureLiveDisplayDmaBeforeHrmGrant(requestedCycle);
            }

            return _hrmSlotEngine.ReservePaulaDmaWordSlot(channel, address, requestedCycle);
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
            return _chipRam.ReadWordForPresentation(address, cycle);
        }

        internal ushort CommitDmaWordRead(in AmigaDmaWordReservation reservation)
            => ReadChipWordForPresentation(reservation.Address, reservation.GrantedCycle);

        internal ushort CommitDmaWordReadUnchecked(in AmigaDmaWordReservation reservation)
            => ReadChipDmaWord(reservation.Address);

        internal void CommitDmaWordWrite(in AmigaDmaWordReservation reservation, ushort value)
            => WriteChipDmaWord(reservation.Address, value, reservation.GrantedCycle);

        internal PaulaDmaReadResult CommitPaulaDmaWord(in AmigaDmaWordReservation reservation)
            => new PaulaDmaReadResult(CommitDmaWordRead(reservation), reservation.Access);

        private void CaptureDmaReservation(in AmigaDmaWordReservation reservation)
        {
            if (_captureBusAccesses)
            {
                _busAccesses.Add(reservation.Access);
            }
        }

        public bool TryReserveDisplayDmaWord(
            AmigaBusRequester requester,
            AmigaBusAccessKind kind,
            uint address,
            long requestedCycle,
            out AmigaDmaWordReservation reservation)
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

            AmigaBusAccessResult access;
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

            reservation = new AmigaDmaWordReservation(address, granted, access);
            CaptureDmaReservation(in reservation);
            return granted;
        }

        public bool TryReadDisplayDmaWordForPresentation(
            AmigaBusRequester requester,
            AmigaBusAccessKind kind,
            uint address,
            long requestedCycle,
            out ushort value,
            out AmigaBusAccessResult access)
        {
            var granted = TryReserveDisplayDmaWord(
                requester,
                kind,
                address,
                requestedCycle,
                out var reservation);
            access = reservation.Access;

            if (!granted)
            {
                value = 0;
                return false;
            }

            value = CommitDmaWordRead(in reservation);
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
            var granted = TryReserveDisplayDmaWord(
                requester,
                kind,
                address,
                requestedCycle,
                out var reservation);
            access = reservation.Access;

            if (!granted)
            {
                value = 0;
                return false;
            }

            value = CommitDmaWordRead(in reservation);
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
            var reservation = ReserveLiveBitplaneDmaWord(address, requestedCycle);
            grantedCycle = reservation.GrantedCycle;
            if (!reservation.Granted)
            {
                value = 0;
                return false;
            }

            value = CommitDmaWordRead(in reservation);
            return true;
        }

        internal bool TryReadRowBitplaneDmaWord(uint address, long requestedCycle, out ushort value, out long grantedCycle)
            => TryReadLiveBitplaneDmaWord(address, requestedCycle, out value, out grantedCycle);

        internal AmigaDmaWordReservation ReserveLiveBitplaneDmaWord(uint address, long requestedCycle)
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

            var reservation = new AmigaDmaWordReservation(address, granted, access);
            CaptureDmaReservation(in reservation);
            return reservation;
        }

        internal void ReadRowBitplaneDmaFetchesForPresentation(
            ReadOnlySpan<RowDmaBitplaneEntry> entries,
            Span<ushort> values,
            Span<bool> granted,
            out int grantedCount,
            out long firstGrantedCycle,
            out long lastGrantedCycle)
        {
            Debug.Assert(values.Length >= entries.Length, "Row bitplane DMA value buffer is shorter than the fetch list.");
            Debug.Assert(granted.Length >= entries.Length, "Row bitplane DMA grant buffer is shorter than the fetch list.");
            grantedCount = 0;
            firstGrantedCycle = -1;
            lastGrantedCycle = -1;
            var usePresentationHistory = _chipRam.PresentationWriteHistory.HasWrites;
            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                if (!entry.RowPresent)
                {
                    values[index] = 0;
                    granted[index] = false;
                    continue;
                }

                var address = MaskChipDmaAddress(entry.Address);
                Debug.Assert(entry.Cycle >= 0, "Row bitplane DMA request cycles must be non-negative.");
                var reservation = ReserveLiveBitplaneDmaWord(address, entry.Cycle);

                if (!reservation.Granted)
                {
                    values[index] = 0;
                    granted[index] = false;
                    continue;
                }

                values[index] = usePresentationHistory
                    ? CommitDmaWordRead(in reservation)
                    : CommitDmaWordReadUnchecked(in reservation);
                granted[index] = true;
                grantedCount++;
                if (firstGrantedCycle < 0 || reservation.GrantedCycle < firstGrantedCycle)
                {
                    firstGrantedCycle = reservation.GrantedCycle;
                }

                if (lastGrantedCycle < 0 || reservation.GrantedCycle > lastGrantedCycle)
                {
                    lastGrantedCycle = reservation.GrantedCycle;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryReadRowSpriteDmaWordForPresentation(
            uint address,
            long slotCycle,
            out ushort value,
            out long grantedCycle)
        {
            var granted = TryReserveSpriteDmaWordExactSlot(
                address,
                slotCycle,
                out var reservation);
            grantedCycle = reservation.GrantedCycle;
            if (!granted)
            {
                value = 0;
                return false;
            }

            value = CommitDmaWordRead(in reservation);
            return granted;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryReserveSpriteDmaWordExactSlot(
            uint address,
            long slotCycle,
            out AmigaDmaWordReservation reservation)
        {
            address = MaskChipDmaAddress(address);
            slotCycle = Math.Max(0, slotCycle);
            var request = new AmigaBusAccessRequest(
                AmigaBusRequester.Sprite,
                AmigaBusAccessKind.Sprite,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                slotCycle,
                isWrite: false);

            AmigaBusAccessResult access;
            bool granted;
            if (!_useChipSlotScheduler)
            {
                access = new AmigaBusAccessResult(request, slotCycle, slotCycle);
                granted = true;
            }
            else
            {
                granted = TryReserveExactFixedDmaSlot(request, out access);
            }

            reservation = new AmigaDmaWordReservation(address, granted, access);
            CaptureDmaReservation(in reservation);
            return granted;
        }

        public ushort ReadLiveCopperDmaWord(uint address, long requestedCycle, out AmigaBusAccessResult access)
        {
            var reservation = ReserveLiveCopperDmaWord(address, requestedCycle);
            access = reservation.Access;
            return CommitDmaWordRead(in reservation);
        }

        internal AmigaDmaWordReservation ReserveLiveCopperDmaWord(uint address, long requestedCycle)
        {
            address = MaskChipDmaAddress(address);
            Debug.Assert(requestedCycle >= 0, "Live copper DMA request cycles must be non-negative.");
            AmigaBusAccessResult access;
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

            var reservation = new AmigaDmaWordReservation(address, granted: true, access);
            CaptureDmaReservation(in reservation);
            return reservation;
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

        internal bool TryReserveRowBitplaneDmaSlot(uint address, long requestedCycle, out long grantedCycle)
        {
            var granted = TryReserveDisplayDmaSlot(
                AmigaBusRequester.Bitplane,
                AmigaBusAccessKind.Bitplane,
                address,
                requestedCycle,
                out var access);
            grantedCycle = access.GrantedCycle;
            return granted;
        }

        public void ClearPresentationWriteHistory()
        {
            _chipRam.PresentationWriteHistory.Clear();
        }

        public AmigaDeviceWordReadResult ReadChipWordForDeviceWithResult(
            AmigaBusRequester requester,
            AmigaBusAccessKind kind,
            uint address,
            long requestedCycle)
        {
            var reservation = ReserveChipWordForDevice(requester, kind, address, requestedCycle);
            var value = CommitDmaWordRead(in reservation);

            return new AmigaDeviceWordReadResult(value, reservation.Access);
        }

        internal AmigaDmaWordReservation ReserveChipWordForDevice(
            AmigaBusRequester requester,
            AmigaBusAccessKind kind,
            uint address,
            long requestedCycle)
        {
            address = MaskChipDmaAddress(address);
            var access = Arbitrate(
                requester,
                kind,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                isWrite: false);
            return new AmigaDmaWordReservation(address, granted: true, access);
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

        internal bool TryReserveDiskDmaWordExactSlot(
            uint address,
            bool isWrite,
            long slotCycle,
            out AmigaDmaWordReservation reservation)
        {
            address = MaskChipDmaAddress(address);
            slotCycle = Math.Max(0, slotCycle);
            var request = new AmigaBusAccessRequest(
                AmigaBusRequester.Disk,
                AmigaBusAccessKind.DiskDma,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                slotCycle,
                isWrite);
            AmigaBusAccessResult access;
            bool granted;
            if (!_useChipSlotScheduler)
            {
                access = new AmigaBusAccessResult(request, slotCycle, slotCycle);
                granted = true;
            }
            else
            {
                granted = TryReserveExactFixedDmaSlot(request, out access);
            }

            reservation = new AmigaDmaWordReservation(address, granted, access);
            if (granted)
            {
                CaptureDmaReservation(in reservation);
            }

            return granted;
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
            var reservation = ReserveChipWordWriteForDevice(requester, kind, address, requestedCycle);
            CommitDmaWordWrite(in reservation, value);
            return reservation.Access;
        }

        internal AmigaDmaWordReservation ReserveChipWordWriteForDevice(
            AmigaBusRequester requester,
            AmigaBusAccessKind kind,
            uint address,
            long requestedCycle)
        {
            address = MaskChipDmaAddress(address);
            var access = Arbitrate(
                requester,
                kind,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                isWrite: true);
            return new AmigaDmaWordReservation(address, granted: true, access);
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
            _hardwareScheduler.NotifyWorkScheduled(cycle);
        }

        public void AdvanceRasterTo(long targetCycle)
            => _hardwareScheduler.DrainTo(
                targetCycle,
                AmigaHardwareEventMask.Raster | AmigaHardwareEventMask.ForceCatchUp);

        internal void AdvanceRasterCoreTo(long targetCycle)
        {
            targetCycle = Math.Max(0, targetCycle);
            if (targetCycle <= _lastRasterAdvanceCycle)
            {
                return;
            }

            _lastRasterAdvanceCycle = targetCycle;
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
            => _hardwareScheduler.DrainTo(
                targetCycle,
                AmigaHardwareEventMask.DiskEvents |
                    AmigaHardwareEventMask.CiaTimers |
                    AmigaHardwareEventMask.ForceCatchUp);

        public void AdvanceCiaTimersTo(long targetCycle)
            => _hardwareScheduler.DrainTo(
                targetCycle,
                AmigaHardwareEventMask.CiaTimers | AmigaHardwareEventMask.ForceCatchUp);

        public void AdvanceDmaTo(long targetCycle)
        {
            AdvanceDmaTo(targetCycle, advanceLiveAgnus: true, advancePassiveDiskInput: true);
        }

        public void AdvanceDmaTo(long targetCycle, bool advanceLiveAgnus)
        {
            AdvanceDmaTo(targetCycle, advanceLiveAgnus, advancePassiveDiskInput: true);
        }

        public void AdvanceDmaTo(long targetCycle, bool advanceLiveAgnus, bool advancePassiveDiskInput)
            => _hardwareScheduler.DrainTo(
                targetCycle,
                AmigaHardwareEventMask.PaulaRegister |
                    AmigaHardwareEventMask.DiskEvents |
                    (advancePassiveDiskInput ? AmigaHardwareEventMask.DiskPassiveInput : AmigaHardwareEventMask.None) |
                    (advanceLiveAgnus ? AmigaHardwareEventMask.Agnus : AmigaHardwareEventMask.None) |
                    AmigaHardwareEventMask.Blitter |
                    AmigaHardwareEventMask.ForceCatchUp);

        public void AdvanceHardwareTo(long targetCycle)
            => _hardwareScheduler.DrainTo(
                targetCycle,
                AmigaHardwareEventMask.All |
                    AmigaHardwareEventMask.DiskPassiveInput |
                    AmigaHardwareEventMask.ForceCatchUp);

        public void AdvanceHardwareEventsTo(long targetCycle)
            => _hardwareScheduler.DrainTo(
                targetCycle,
                AmigaHardwareEventMask.All | AmigaHardwareEventMask.CpuBoundary);

        public void AdvanceHardwareEventsTo(long targetCycle, int cpuInterruptMask)
        {
            var mask = (AmigaHardwareEventMask.All & ~AmigaHardwareEventMask.PaulaRegister) |
                AmigaHardwareEventMask.CpuBoundary;
            if (Paula.HasCpuWakeWorkThrough(targetCycle, cpuInterruptMask))
            {
                mask |= AmigaHardwareEventMask.PaulaRegister;
            }

            _hardwareScheduler.DrainTo(targetCycle, mask);
        }

        public AmigaHardwareSchedulerSnapshot CaptureHardwareSchedulerSnapshot()
            => _hardwareScheduler.CaptureSnapshot();

        public void SetHardwareSchedulerHostProfilingEnabled(bool enabled)
            => _hardwareScheduler.HostProfilingEnabled = enabled;

        public void ResetHardwareSchedulerHostProfile()
            => _hardwareScheduler.ResetHostProfile();

        internal bool TrySkipRasterlineScheduleDrain(
            long currentCycle,
            long targetCycle,
            AmigaHardwareEventMask mask)
            => _rasterlineScheduleCache.TrySkipDrain(currentCycle, targetCycle, mask);

        internal void InvalidateRasterlineSchedule(long cycle, AmigaHardwareEventMask mask)
            => _rasterlineScheduleCache.InvalidateFrom(cycle, mask);

        internal AmigaRasterlineScheduleCacheSnapshot CaptureRasterlineScheduleCacheSnapshot()
            => _rasterlineScheduleCache.CaptureSnapshot();

        internal void AdvanceDmaCoreTo(long targetCycle, bool advanceLiveAgnus, bool advancePassiveDiskInput)
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

        internal void AdvanceCiaTimersCoreTo(long targetCycle)
        {
            CiaA.AdvanceTo(targetCycle, _pendingCiaInterrupts);
            CiaB.AdvanceTo(targetCycle, _pendingCiaInterrupts);
        }

        internal void AdvanceAgnusCoreTo(long targetCycle)
        {
            if (LiveAgnusDmaEnabled)
            {
                Agnus.AdvanceTo(targetCycle, Display.HasLiveDisplayWork());
            }
        }

        internal long GetNextRasterEventCycle(long currentCycle, long targetCycle)
        {
            if (targetCycle < currentCycle)
            {
                return long.MaxValue;
            }

            var next = Math.Min(_nextHorizontalSyncCycle, _nextVerticalBlankCycle);
            if (next > targetCycle)
            {
                return long.MaxValue;
            }

            return next <= currentCycle ? currentCycle : next;
        }

        internal long GetNextCiaTimerEventCycle(long currentCycle, long targetCycle)
        {
            var ciaA = CiaA.GetNextInterruptCycle(targetCycle);
            var ciaB = CiaB.GetNextInterruptCycle(targetCycle);
            var next = long.MaxValue;
            if (ciaA.HasValue)
            {
                next = Math.Min(next, ciaA.Value);
            }

            if (ciaB.HasValue)
            {
                next = Math.Min(next, ciaB.Value);
            }

            if (next == long.MaxValue || next > targetCycle)
            {
                return long.MaxValue;
            }

            return next <= currentCycle ? currentCycle : next;
        }

        internal long GetNextAgnusEventCycle(long currentCycle, long targetCycle)
        {
            if (!LiveAgnusDmaEnabled)
            {
                return long.MaxValue;
            }

            return Agnus.GetNextWakeCandidateCycle(currentCycle, targetCycle, Display.HasLiveDisplayWork());
        }

        internal long GetNextCpuVisibleAgnusEventCycle(long currentCycle, long targetCycle)
        {
            if (!LiveAgnusDmaEnabled)
            {
                return long.MaxValue;
            }

            return Display.GetNextLiveCopperWakeCandidateCycle(currentCycle, targetCycle) ?? long.MaxValue;
        }

        private void AdvanceDmaBeforeCpuChipAccess(
            AmigaBusAccessTarget target,
            uint address,
            long grantedCycle,
            bool isWrite)
        {
            _hardwareScheduler.DrainForCpuAccess(target, address, grantedCycle, isWrite);
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

        internal bool HasPendingCiaInterrupts => _pendingCiaInterrupts.Count != 0;

        internal long NextVerticalBlankCycle => _nextVerticalBlankCycle;

        internal long NextHorizontalSyncCycle => _nextHorizontalSyncCycle;

        internal long PalLineCycles => _palLineCycles;

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
            => _hardwareScheduler.GetNextCpuVisibleEventCycle(
                currentCycle,
                targetCycle,
                cpuInterruptMask,
                out wakeSource);

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
            bool isWrite,
            int channel = -1)
        {
            if (requester == AmigaBusRequester.Cpu &&
                target == AmigaBusAccessTarget.CustomRegisters &&
                !isWrite)
            {
                _hardwareScheduler.DrainForCpuAccess(target, address, requestedCycle, isWrite);
                if (Blitter.Busy)
                {
                    requestedCycle = Blitter.AdvanceThroughCpuStall(requestedCycle);
                }
            }
            else if (requester == AmigaBusRequester.Cpu &&
                (target == AmigaBusAccessTarget.ChipRam ||
                    target == AmigaBusAccessTarget.ExpansionRam ||
                    target == AmigaBusAccessTarget.RealTimeClock ||
                    target == AmigaBusAccessTarget.CustomRegisters))
            {
                _hardwareScheduler.DrainForCpuAccess(target, address, requestedCycle, isWrite);
                if (Blitter.Busy)
                {
                    requestedCycle = Blitter.AdvanceThroughCpuStall(requestedCycle);
                }
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
                return RememberCpuBusAccess(ciaResult);
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
                    isWrite,
                    channel);
                var fastResult = new AmigaBusAccessResult(fastRequest, requestedCycle, requestedCycle);
                var fastAccess = ShouldUseChipSlotScheduler(target)
                    ? ArbitrateChipSlot(fastRequest, fastResult)
                    : fastResult;
                return RememberCpuBusAccess(fastAccess);
            }

            var request = new AmigaBusAccessRequest(
                requester,
                kind,
                target,
                address,
                size,
                requestedCycle,
                isWrite,
                channel);
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
            return RememberCpuBusAccess(result);
        }

        /// <summary>
        /// Layer 1 fast path: returns true when a chip/expansion RAM read can complete
        /// immediately without struct construction, drain, or slot arbitration.
        /// On success, grantedCycle = completedCycle = Max(0, requestedCycle).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGrantFastCpuAccessCycle(
            AmigaBusAccessTarget target,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle)
        {
            if (!isWrite &&
                (target == AmigaBusAccessTarget.ChipRam || target == AmigaBusAccessTarget.ExpansionRam) &&
                _hardwareScheduler.IsSlotContendedCleanThrough(requestedCycle) &&
                !Blitter.Busy &&
                (!_useChipSlotScheduler || !ShouldUseChipSlotScheduler(target)))
            {
                grantedCycle = Math.Max(0, requestedCycle);
                return true;
            }

            grantedCycle = 0;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void GrantFastCpuAccessCycles(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            AmigaBusAccessKind kind,
            bool isWrite,
            out long grantedCycle,
            out long secondWordCycle,
            out long completedCycle)
        {
            if (TryGrantFastCpuAccessCycle(target, requestedCycle, isWrite, out var fastCycle))
            {
                grantedCycle = fastCycle;
                completedCycle = fastCycle;
                secondWordCycle = fastCycle;
                return;
            }

            GrantCpuAccessSlowCycles(
                target,
                address,
                size,
                requestedCycle,
                kind,
                isWrite,
                out grantedCycle,
                out secondWordCycle,
                out completedCycle);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrantCpuAccessSlowCycles(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            AmigaBusAccessKind kind,
            bool isWrite,
            out long grantedCycle,
            out long secondWordCycle,
            out long completedCycle)
        {
            if (target == AmigaBusAccessTarget.ChipRam ||
                target == AmigaBusAccessTarget.ExpansionRam ||
                target == AmigaBusAccessTarget.RealTimeClock ||
                target == AmigaBusAccessTarget.CustomRegisters)
            {
                _hardwareScheduler.DrainForCpuAccess(target, address, requestedCycle, isWrite);
                if (Blitter.Busy)
                {
                    requestedCycle = Blitter.AdvanceThroughCpuStall(requestedCycle);
                }
            }

            requestedCycle = Math.Max(0, requestedCycle);
            if (target == AmigaBusAccessTarget.Cia)
            {
                grantedCycle = CiaPeripheralAccessTiming.AlignToCiaPeripheralAccessCycle(requestedCycle);
                completedCycle = grantedCycle;
                secondWordCycle = GetCpuSecondWordCycle(size, grantedCycle, completedCycle);
                return;
            }

            if (!_useChipSlotScheduler || !ShouldUseChipSlotScheduler(target))
            {
                grantedCycle = requestedCycle;
                completedCycle = requestedCycle;
                secondWordCycle = GetCpuSecondWordCycle(size, grantedCycle, completedCycle);
                return;
            }

            _hrmSlotEngine.BlitterPriorityEnabled = (Paula.Dmacon & 0x0400) != 0;
            PrepareLiveDisplayBeforeCpuHrmGrantIfNeeded(target, size, isWrite, requestedCycle);

            if (size == AmigaBusAccessSize.Long)
            {
                _hrmSlotEngine.GrantCpuDataLongSlots(
                    kind,
                    target,
                    address,
                    requestedCycle,
                    isWrite,
                    out grantedCycle,
                    out secondWordCycle,
                    out completedCycle);
            }
            else
            {
                _hrmSlotEngine.GrantCpuDataSingleSlot(
                    kind,
                    target,
                    address,
                    size,
                    requestedCycle,
                    isWrite,
                    out grantedCycle,
                    out completedCycle);
                secondWordCycle = grantedCycle;
            }

            Agnus.RecordCpuChipWaitCycles(grantedCycle - requestedCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private AmigaBusAccessResult RememberCpuBusAccess(AmigaBusAccessResult access)
        {
            if (!_captureBusAccesses || access.Request.Requester != AmigaBusRequester.Cpu)
            {
                return access;
            }

            _lastCpuBusAccess = access;
            _lastCpuBusGrantedSlot = GetMatchingLastGrantedSlot(access);
            return access;
        }

        private AgnusChipSlotSnapshot? GetMatchingLastGrantedSlot(AmigaBusAccessResult access)
        {
            var slot = _hrmSlotEngine.LastGrantedSlot;
            if (!slot.HasValue)
            {
                return null;
            }

            var value = slot.Value;
            if (value.GrantedCycle != access.GrantedCycle ||
                value.Kind != access.Request.Kind ||
                value.Address != access.Request.Address)
            {
                return null;
            }

            return value;
        }

        private void ClearChipSlots()
        {
            _hrmSlotEngine.Clear();
        }

        internal void ClearLiveDisplayDmaSlotsFrom(long cycle)
        {
            _hrmSlotEngine.ClearLiveDisplaySlotsFrom(cycle);
        }

        internal void InvalidateLiveDisplayHrmGrantCache()
        {
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

        private void RebuildCpuBusBankTable()
        {
            for (var bank = 0; bank < CpuBusBankCount; bank++)
            {
                var bankAddress = (uint)(bank << CpuBusBankShift);
                _cpuBusBankKinds[bank] = ClassifyCpuBusBank(bankAddress, out var offset);
                _cpuBusBankOffsets[bank] = offset;
            }
        }

        private CpuBusBankKind ClassifyCpuBusBank(uint bankAddress, out int offset)
        {
            offset = 0;
            if (HasSpecialCpuBusBankOverlap(bankAddress))
            {
                return CpuBusBankKind.Special;
            }

            var bankEnd = bankAddress + CpuBusBankSize - 1u;
            var startsInOverlay = IsRomOverlayAddress(bankAddress);
            var endsInOverlay = IsRomOverlayAddress(bankEnd);
            if (startsInOverlay || endsInOverlay)
            {
                return startsInOverlay && endsInOverlay
                    ? CpuBusBankKind.RomOverlay
                    : CpuBusBankKind.Special;
            }

            var mappedOverlap = _mappedMemory.ContainsMappedAddressInRange(bankAddress, CpuBusBankSize);
            var mappedOffset = 0;
            var mappedFullBank = mappedOverlap &&
                _mappedMemory.TryGetMappedReadMemory(bankAddress, CpuBusBankSize, out _, out mappedOffset);

            var chipFullBank = _chipRam.TryGetContiguousMemory(bankAddress, CpuBusBankSize, out _, out var chipOffset);
            var realFastFullBank = _realFastRam.TryGetContiguousMemory(bankAddress, CpuBusBankSize, out _, out var realFastOffset);
            var expansionFullBank = _expansionRam.TryGetContiguousMemory(bankAddress, CpuBusBankSize, out _, out var expansionOffset);

            if (mappedOverlap)
            {
                if (!mappedFullBank || chipFullBank || realFastFullBank || expansionFullBank)
                {
                    return CpuBusBankKind.Special;
                }

                offset = mappedOffset;
                return CpuBusBankKind.MappedRom;
            }

            if (chipFullBank)
            {
                offset = chipOffset;
                return CpuBusBankKind.ChipRam;
            }

            if (realFastFullBank)
            {
                offset = realFastOffset;
                return CpuBusBankKind.RealFastRam;
            }

            if (expansionFullBank)
            {
                offset = expansionOffset;
                return CpuBusBankKind.ExpansionRam;
            }

            return CpuBusBankKind.Unmapped;
        }

        private bool HasSpecialCpuBusBankOverlap(uint bankAddress)
        {
            var bankEndExclusive = bankAddress + CpuBusBankSize;
            if (RangesOverlap(bankAddress, bankEndExclusive, 0x00DFF000u, 0x00DFF200u) ||
                RangesOverlap(bankAddress, bankEndExclusive, 0x00BFD000u, 0x00BFF000u) ||
                HasHostTrapInCpuBusBank(bankAddress) ||
                (_realTimeClock != null && RangesOverlap(
                    bankAddress,
                    bankEndExclusive,
                    AmigaRealTimeClock.BaseAddress,
                    AmigaRealTimeClock.BaseAddress + AmigaRealTimeClock.ByteLength)))
            {
                return true;
            }

            if (CopperHdf.ContainsAutoConfigAddress(bankAddress) ||
                CopperHdf.ContainsAutoConfigAddress(bankEndExclusive - 1u) ||
                CopperHdf.ContainsBoardAddress(bankAddress) ||
                CopperHdf.ContainsBoardAddress(bankEndExclusive - 1u))
            {
                return true;
            }

            return false;
        }

        private bool HasHostTrapInCpuBusBank(uint bankAddress)
        {
            if (_hostTrapStubs.Count == 0)
            {
                return false;
            }

            var bankEndExclusive = bankAddress + CpuBusBankSize;
            foreach (var address in _hostTrapStubs.Keys)
            {
                var trapStart = NormalizeAddress(address);
                var trapEndExclusive = (ulong)trapStart + 4u;
                if ((ulong)bankAddress < trapEndExclusive && trapStart < bankEndExclusive)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RangesOverlap(uint start, uint endExclusive, uint otherStart, uint otherEndExclusive)
            => start < otherEndExclusive && otherStart < endExclusive;

        private AmigaBusAccessTarget ClassifyTarget(uint address)
        {
            address = NormalizeAddress(address);
            var bankKind = _cpuBusBankKinds[(int)(address >> CpuBusBankShift)];
            return bankKind switch
            {
                CpuBusBankKind.ChipRam => AmigaBusAccessTarget.ChipRam,
                CpuBusBankKind.ExpansionRam => AmigaBusAccessTarget.ExpansionRam,
                CpuBusBankKind.RealFastRam => AmigaBusAccessTarget.RealFastRam,
                CpuBusBankKind.RomOverlay => AmigaBusAccessTarget.Rom,
                CpuBusBankKind.MappedRom => AmigaBusAccessTarget.Rom,
                CpuBusBankKind.Unmapped => AmigaBusAccessTarget.Unmapped,
                _ => ClassifyTargetDetailed(address)
            };
        }

        private AmigaBusAccessTarget ClassifyTargetDetailed(uint address)
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

            if (_mappedMemory.ContainsMappedAddress(address))
            {
                return AmigaBusAccessTarget.Rom;
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

            // Hot path: chip RAM (most common case).
            if (address < _chipRam.DecodeSize && !_romOverlayEnabled)
            {
                return _chipRamData[(int)address & _chipRamMask];
            }

            // Custom registers.
            if (address >= 0x00DFF000 && address < 0x00DFF200)
            {
                var offset = (ushort)(address - 0x00DFF000);
                if (TryReadBeamPositionByte(offset, sampleCycle, out var beamPositionValue))
                {
                    return beamPositionValue;
                }

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

            // ROM overlay (only during early boot).
            if (TryReadRomOverlayByte(address, out var overlayValue))
            {
                return overlayValue;
            }

            // Chip RAM with overlay active.
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

            if (_realTimeClock != null && _realTimeClock.TryReadByte(address, out var realTimeClockValue))
            {
                return realTimeClockValue;
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

            if (_mappedMemory.TryReadMappedByte(address, out var value))
            {
                return value;
            }

            // Host traps (rare, checked last).
            if (TryReadHostTrapStubByte(address, out var hostTrapByte))
            {
                return hostTrapByte;
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

            if (_mappedMemory.ContainsMappedAddress(address))
            {
                return true;
            }

            if (!StrictCpuPhysicalDataMapping &&
                accessKind != AmigaBusAccessKind.CpuInstructionFetch)
            {
                return true;
            }

            if (address < _chipRam.DecodeSize)
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ushort ReadChipDmaWord(uint address)
        {
            return _chipRam.ReadDmaWord(address);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteChipDmaWord(uint address, ushort value, long grantedCycle)
        {
            _chipRam.WriteDmaWord(address, value, grantedCycle);
        }

        private ushort ReadRawWord(uint address)
        {
            address = NormalizeAddress(address);

            // Hot path: chip RAM (most common case).
            if (address < _chipRam.DecodeSize && !_romOverlayEnabled)
            {
                var chipOffset = (int)address & _chipRamMask;
                var nextOffset = (chipOffset + 1) & _chipRamMask;
                return (ushort)((_chipRamData[chipOffset] << 8) | _chipRamData[nextOffset]);
            }

            // Custom registers.
            if (IsCustomRegisterWordAddress(address))
            {
                return ReadCustomWord((ushort)(address - 0x00DFF000), long.MinValue);
            }

            // ROM overlay (only during early boot).
            if (TryReadRomOverlayWord(address, out var overlayWord))
            {
                return overlayWord;
            }

            // Chip RAM with overlay active.
            if (!IsRomOverlayAddress(address) && TryGetChipRamOffset(address, out var chipOffsetFallback))
            {
                var nextOffset = (chipOffsetFallback + 1) & (_chipRam.Length - 1);
                return (ushort)((_chipRam[chipOffsetFallback] << 8) | _chipRam[nextOffset]);
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

            if (_realTimeClock != null &&
                (AmigaRealTimeClock.ContainsAddress(address) || AmigaRealTimeClock.ContainsAddress(address + 1)))
            {
                return (ushort)((ReadRawByte(address) << 8) | ReadRawByte(address + 1));
            }

            // Host traps (rare, checked last).
            if (TryReadHostTrapStubWord(address, out var hostTrapWord))
            {
                return hostTrapWord;
            }

            return (ushort)((ReadRawByte(address) << 8) | ReadRawByte(address + 1));
        }

        private ushort ReadRawWord(uint address, long sampleCycle)
        {
            address = NormalizeAddress(address);

            // Hot path: chip RAM (most common case).
            if (address < _chipRam.DecodeSize && !_romOverlayEnabled)
            {
                var chipOffset = (int)address & _chipRamMask;
                var nextOffset = (chipOffset + 1) & _chipRamMask;
                return (ushort)((_chipRamData[chipOffset] << 8) | _chipRamData[nextOffset]);
            }

            // Custom registers.
            if (IsCustomRegisterWordAddress(address))
            {
                return ReadCustomWord((ushort)(address - 0x00DFF000), sampleCycle);
            }

            // ROM overlay (only during early boot).
            if (TryReadRomOverlayWord(address, out var overlayWord))
            {
                return overlayWord;
            }

            // Chip RAM with overlay active.
            if (!IsRomOverlayAddress(address) && TryGetChipRamOffset(address, out var chipOffsetFallback))
            {
                var nextOffset = (chipOffsetFallback + 1) & (_chipRam.Length - 1);
                return (ushort)((_chipRam[chipOffsetFallback] << 8) | _chipRam[nextOffset]);
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

            if (_realTimeClock != null &&
                (AmigaRealTimeClock.ContainsAddress(address) || AmigaRealTimeClock.ContainsAddress(address + 1)))
            {
                return (ushort)((ReadRawByte(address, sampleCycle) << 8) |
                    ReadRawByte(address + 1, sampleCycle));
            }

            // Host traps (rare, checked last).
            if (TryReadHostTrapStubWord(address, out var hostTrapWord))
            {
                return hostTrapWord;
            }

            return (ushort)((ReadRawByte(address, sampleCycle) << 8) |
                ReadRawByte(address + 1, sampleCycle));
        }

        private static bool IsCustomRegisterWordAddress(uint address)
            => address >= 0x00DFF000 && address < 0x00DFF1FF;

        private static bool IsCustomRegisterByteAddress(uint address)
            => address >= 0x00DFF000 && address < 0x00DFF200;

        private byte ReadCustomByte(ushort offset, long sampleCycle)
        {
            if (TryReadBeamPositionByte(offset, sampleCycle, out var beamPositionValue))
            {
                return beamPositionValue;
            }

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

        private ushort ReadCustomWord(ushort offset, long sampleCycle)
        {
            offset = (ushort)(offset & 0x01FE);
            if (TryReadBeamPositionWord(offset, sampleCycle, out var beamPositionValue))
            {
                return beamPositionValue;
            }

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

        private bool TryReadBeamPositionByte(ushort offset, long sampleCycle, out byte value)
        {
            var wordOffset = (ushort)(offset & 0x01FE);
            if (TryReadBeamPositionWord(wordOffset, sampleCycle, out var word))
            {
                value = (offset & 1) == 0 ? (byte)(word >> 8) : (byte)word;
                return true;
            }

            value = 0;
            return false;
        }

        private bool TryReadBeamPositionWord(ushort offset, long sampleCycle, out ushort value)
        {
            offset = (ushort)(offset & 0x01FE);
            if (offset != 0x004 && offset != 0x006)
            {
                value = 0;
                return false;
            }

            CalculateBeamPosition(sampleCycle == long.MinValue ? _lastRasterAdvanceCycle : sampleCycle, out var vposr, out var vhposr);
            value = offset == 0x004 ? vposr : vhposr;
            return true;
        }

        private void CalculateBeamPosition(long targetCycle, out ushort vposr, out ushort vhposr)
        {
            var cycleInFrame = Math.Max(0, targetCycle) % _palFrameCycles;
            var line = Math.Clamp((int)(cycleInFrame / _palLineCycles), 0, AmigaConstants.A500PalRasterLines - 1);
            var lineCycle = cycleInFrame - (line * _palLineCycles);
            var horizontal = Math.Clamp((int)(lineCycle / AmigaConstants.A500PalCpuCyclesPerColorClock), 0, 0xE2);
            var frame = Math.Max(0, targetCycle) / _palFrameCycles;
            vposr = (ushort)((((frame & 1) != 0 ? 1 : 0) << 15) | ((line >> 8) & 0x0001));
            vhposr = (ushort)(((line & 0x00FF) << 8) | horizontal);
        }

        private uint ReadCustomLong(uint address, long firstWordCycle, long secondWordCycle)
        {
            var high = IsCustomRegisterWordAddress(address)
                ? ReadCustomWord((ushort)(address - 0x00DFF000), firstWordCycle)
                : ReadRawWord(address, firstWordCycle);
            var lowAddress = address + 2;
            var low = IsCustomRegisterWordAddress(lowAddress)
                ? ReadCustomWord((ushort)(lowAddress - 0x00DFF000), secondWordCycle)
                : ReadRawWord(lowAddress, secondWordCycle);
            return ((uint)high << 16) | low;
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
                memory = _realFastRam.Data;
                offset = realFastOffset;
                memoryKind = M68kJitMemoryKind.FastRam;
                return true;
            }

            if (TryGetJitRomOverlayReadMemory(address, byteCount, out memory, out offset))
            {
                memoryKind = M68kJitMemoryKind.Overlay;
                return true;
            }

            if (_mappedMemory.TryGetMappedReadMemory(address, byteCount, out memory, out offset))
            {
                memoryKind = M68kJitMemoryKind.Rom;
                return true;
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

            memory = _realFastRam.Data;
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
            return _mappedMemory.TryGetRomOverlayReadMemory(
                address,
                byteCount,
                _romOverlayEnabled,
                out memory,
                out offset);
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
            if (TryReadJitExactCpuDataMemory(address, ref cycle, size, out var exactValue))
            {
                return exactValue;
            }

            var target = ClassifyTarget(address);
            var accessSize = ToBusAccessSize(size);
            if (_useFastZeroWaitAccesses)
            {
                GrantFastCpuAccessCycles(
                    target,
                    address,
                    accessSize,
                    cycle,
                    AmigaBusAccessKind.CpuDataRead,
                    isWrite: false,
                    out var fastGrantedCycle,
                    out var fastSecondWordCycle,
                    out var fastCompletedCycle);
                AdvanceDmaBeforeCpuChipAccess(target, address, fastGrantedCycle, isWrite: false);

                var fastValue = TryReadJitMappedMemory(target, address, size, out var fastMappedValue)
                    ? fastMappedValue
                    : ReadJitRawMemory(target, address, size, fastGrantedCycle, fastSecondWordCycle);
                cycle = fastCompletedCycle;
                AcknowledgeJitCiaSerialReadIfNeeded(target, address, size, cycle);
                return fastValue;
            }

            var access = Arbitrate(AmigaBusRequester.Cpu, AmigaBusAccessKind.CpuDataRead, target, address, accessSize, cycle, isWrite: false);
            AdvanceDmaBeforeCpuChipAccess(target, address, access.GrantedCycle, isWrite: false);

            var value = TryReadJitMappedMemory(target, address, size, out var mappedValue)
                ? mappedValue
                : ReadJitRawMemory(target, address, size, access);
            cycle = access.CompletedCycle;
            AcknowledgeJitCiaSerialReadIfNeeded(target, address, size, cycle);
            return value;
        }

        private void AcknowledgeJitCiaSerialReadIfNeeded(
            AmigaBusAccessTarget target,
            uint address,
            M68kOperandSize size,
            long cycle)
        {
            if (size == M68kOperandSize.Byte &&
                target == AmigaBusAccessTarget.Cia &&
                TryGetCiaRegister(address, out var cia, out var ciaRegister) &&
                cia == CiaA &&
                ciaRegister == 0x0C)
            {
                Keyboard.AcknowledgeSerialDataRead(cycle);
            }
        }

        private void WriteJitSlotAwareMemoryUnchecked(ref long cycle, uint address, uint value, M68kOperandSize size)
        {
            address = NormalizeAddress(address);
            if (TryWriteJitExactCpuDataMemory(address, ref cycle, value, size))
            {
                return;
            }

            var target = ClassifyTarget(address);
            var accessSize = ToBusAccessSize(size);
            long grantedCycle;
            long secondWordCycle;
            long completedCycle;
            if (_useFastZeroWaitAccesses)
            {
                GrantFastCpuAccessCycles(
                    target,
                    address,
                    accessSize,
                    cycle,
                    AmigaBusAccessKind.CpuDataWrite,
                    isWrite: true,
                    out grantedCycle,
                    out secondWordCycle,
                    out completedCycle);
            }
            else
            {
                var access = Arbitrate(AmigaBusRequester.Cpu, AmigaBusAccessKind.CpuDataWrite, target, address, accessSize, cycle, isWrite: true);
                grantedCycle = access.GrantedCycle;
                secondWordCycle = GetSecondWordCycle(access);
                completedCycle = access.CompletedCycle;
            }

            AdvanceDmaBeforeCpuChipAccess(target, address, grantedCycle, isWrite: true);
            if (!TryWriteJitMappedMemory(target, address, value, size))
            {
                if (size == M68kOperandSize.Byte)
                {
                    WriteRawByte(address, (byte)value, grantedCycle);
                }
                else if (size == M68kOperandSize.Word)
                {
                    WriteRawWord(address, (ushort)value, grantedCycle);
                }
                else
                {
                    WriteRawLong(address, value, grantedCycle, secondWordCycle);
                }
            }

            cycle = completedCycle;
        }

        private static AmigaBusAccessSize ToBusAccessSize(M68kOperandSize size)
            => size switch
            {
                M68kOperandSize.Byte => AmigaBusAccessSize.Byte,
                M68kOperandSize.Word => AmigaBusAccessSize.Word,
                _ => AmigaBusAccessSize.Long
            };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryReadJitExactCpuDataMemory(
            uint address,
            ref long cycle,
            M68kOperandSize size,
            out uint value)
        {
            if (size == M68kOperandSize.Byte &&
                TryReadExactCpuDataByte(address, ref cycle, out var byteValue))
            {
                value = byteValue;
                return true;
            }

            if (size == M68kOperandSize.Word &&
                TryReadExactCpuDataWord(address, ref cycle, out var wordValue))
            {
                value = wordValue;
                return true;
            }

            if (size == M68kOperandSize.Long &&
                TryReadExactCpuDataLong(address, ref cycle, out var longValue))
            {
                value = longValue;
                return true;
            }

            value = 0;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryWriteJitExactCpuDataMemory(
            uint address,
            ref long cycle,
            uint value,
            M68kOperandSize size)
        {
            if (size == M68kOperandSize.Byte)
            {
                return TryWriteExactCpuDataByte(address, (byte)value, ref cycle);
            }

            if (size == M68kOperandSize.Word)
            {
                return TryWriteExactCpuDataWord(address, (ushort)value, ref cycle);
            }

            return TryWriteExactCpuDataLong(address, value, ref cycle);
        }

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

        private uint ReadJitRawMemory(
            AmigaBusAccessTarget target,
            uint address,
            M68kOperandSize size,
            long grantedCycle,
            long secondWordCycle)
        {
            if (target == AmigaBusAccessTarget.CustomRegisters)
            {
                return size switch
                {
                    M68kOperandSize.Byte => ReadRawByte(address, grantedCycle),
                    M68kOperandSize.Word => ReadRawWord(address, grantedCycle),
                    _ => ReadRawLong(address, grantedCycle, secondWordCycle)
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
                _expansionRam.TryReadValue(address, size, out value))
            {
                return true;
            }

            if (target == AmigaBusAccessTarget.RealFastRam &&
                _realFastRam.TryReadValue(address, size, out value))
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

        private bool TryWriteJitMappedMemory(
            AmigaBusAccessTarget target,
            uint address,
            uint value,
            M68kOperandSize size)
        {
            if (target == AmigaBusAccessTarget.ExpansionRam &&
                _expansionRam.TryWriteValue(address, value, size))
            {
                var byteCount = size == M68kOperandSize.Long ? 4 : size == M68kOperandSize.Word ? 2 : 1;
                TouchCodePages(address, byteCount);
                NotifyJitEligibleMemoryWritten(address, byteCount);
                return true;
            }

            if (target == AmigaBusAccessTarget.RealFastRam &&
                _realFastRam.TryWriteValue(address, value, size))
            {
                var byteCount = size == M68kOperandSize.Long ? 4 : size == M68kOperandSize.Word ? 2 : 1;
                TouchCodePages(address, byteCount);
                NotifyJitEligibleMemoryWritten(address, byteCount);
                return true;
            }

            return false;
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
                _chipRam.WriteByteAtOffset(chipOffset, value, grantedCycle);
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
                _hardwareScheduler.NotifyWorkScheduled(grantedCycle);
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
                RebuildCpuBusBankTable();
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

            if (_mappedMemory.TryWriteMappedByte(address, value))
            {
                return;
            }
        }

        private void WriteRawWord(uint address, ushort value, long grantedCycle)
        {
            address = NormalizeAddress(address);
            if (address >= 0x00DFF000 && address + 1 < 0x00DFF200)
            {
                Paula.ScheduleWrite(grantedCycle, (ushort)(address - 0x00DFF000), value);
                Paula.AdvanceRegisterWritesTo(grantedCycle);
                Display.ScheduleWrite(grantedCycle, (ushort)(address - 0x00DFF000), value);
                Blitter.WriteRegister((ushort)(address - 0x00DFF000), value, grantedCycle);
                Disk.WriteRegister((ushort)(address - 0x00DFF000), value, grantedCycle);
                _hardwareScheduler.NotifyWorkScheduled(grantedCycle);
                return;
            }

            if (TryGetChipRamOffset(address, out var chipOffset))
            {
                _chipRam.WriteWordAtOffset(chipOffset, value, grantedCycle);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long GetCpuSecondWordCycle(
            AmigaBusAccessSize size,
            long grantedCycle,
            long completedCycle)
        {
            if (size != AmigaBusAccessSize.Long)
            {
                return grantedCycle;
            }

            var nextSlotCycle = grantedCycle + (2 * AgnusChipSlotScheduler.SlotCycles);
            return completedCycle >= nextSlotCycle ? nextSlotCycle : completedCycle;
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
            Paula.AdvanceRegisterWritesTo(cycle);
            Display.ScheduleWrite(cycle, wordOffset, wordValue);
            Blitter.WriteRegister(wordOffset, wordValue, cycle);
            Disk.WriteRegister(wordOffset, wordValue, cycle);
            _hardwareScheduler.NotifyWorkScheduled(cycle);

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
                return _chipRam.GetCodePageGeneration(chipOffset);
            }

            if (TryGetExpansionRamOffset(address, out var expansionOffset))
            {
                return _expansionRam.GetCodePageGeneration(expansionOffset);
            }

            if (TryGetRealFastRamOffset(address, out var realFastOffset))
            {
                return _realFastRam.GetCodePageGeneration(realFastOffset);
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
                _chipRam.SetCodePageGeneration(chipOffset, generation);
                return;
            }

            if (TryGetExpansionRamOffset(address, out var expansionOffset))
            {
                _expansionRam.SetCodePageGeneration(expansionOffset, generation);
                return;
            }

            if (TryGetRealFastRamOffset(address, out var realFastOffset))
            {
                _realFastRam.SetCodePageGeneration(realFastOffset, generation);
            }
        }

        private uint NextCodeGeneration()
        {
            _codeGenerationClock++;
            if (_codeGenerationClock != 0)
            {
                return _codeGenerationClock;
            }

            _chipRam.ClearCodePageGenerations();
            _expansionRam.ClearCodePageGenerations();
            _realFastRam.ClearCodePageGenerations();
            _codeGenerationClock = 1;
            return _codeGenerationClock;
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
            var overlayEnabled = (portA & CiaAPortAOverlayBit) != 0;
            if (_romOverlayEnabled != overlayEnabled)
            {
                _romOverlayEnabled = overlayEnabled;
                InvalidateInstructionFetchWindows();
                RebuildCpuBusBankTable();
            }
            else
            {
                _romOverlayEnabled = overlayEnabled;
            }
        }

        private void InvalidateInstructionFetchWindows()
        {
            var generation = _instructionFetchWindowGeneration[0] + 1u;
            _instructionFetchWindowGeneration[0] = generation == 0 ? 1u : generation;
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
    }
}
