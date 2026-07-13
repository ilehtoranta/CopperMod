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
        IM68kJitTimedMemoryBus,
        IM68kJitDirectRamBus
    {
        private const ushort HostTrapOpcode = 0xFF00;
        private const int MaxCapturedBusAccesses = 65536;
        private const int MaxCapturedCpuBusPhases = 65536;
        private const int MaxCapturedCustomRegisterReads = 65536;
        private const int MaxPendingInterruptEvents = 65536;
        private const int InstructionFetchWindowMaxBytes = 256;
        private const uint CpuAddressMask = 0x00FF_FFFFu;
        private const int CpuBusBankShift = 16;
        private const int CpuBusBankSize = 1 << CpuBusBankShift;
        private const int CpuBusBankCount = 1 << (24 - CpuBusBankShift);
        private const uint CustomRegisterBaseAddress = 0x00DFF000;
        private const int MaxDeferredCpuDataAccesses = 64;
        private const long DeferredCpuBusBatchDefaultCycleWindow = 4096;
        private const int CodeGenerationPageShift = 8;
        private const int CodeGenerationPageSize = 1 << CodeGenerationPageShift;
        private const int HostTrapStubPageCount = 1 << (24 - CodeGenerationPageShift);
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

        private interface IBusWritePolicy
        {
            AmigaBusRequester Requester { get; }
        }

        private readonly struct CpuWritePolicy : IBusWritePolicy
        {
            public AmigaBusRequester Requester
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => AmigaBusRequester.Cpu;
            }
        }

        private readonly struct HostWritePolicy : IBusWritePolicy
        {
            public AmigaBusRequester Requester
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => AmigaBusRequester.Host;
            }
        }

        private readonly struct RequesterWritePolicy : IBusWritePolicy
        {
            private readonly AmigaBusRequester _requester;

            public RequesterWritePolicy(AmigaBusRequester requester)
            {
                _requester = requester;
            }

            public AmigaBusRequester Requester
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _requester;
            }
        }

        private interface ICpuWriteTarget
        {
            void WriteByte(AmigaBus bus, uint address, byte value, long grantedCycle);

            void WriteWord(AmigaBus bus, uint address, ushort value, long grantedCycle);

            void WriteLong(AmigaBus bus, uint address, uint value, long firstWordCycle, long secondWordCycle);
        }

        private readonly struct CustomRegisterCpuWriteTarget : ICpuWriteTarget
        {
            public void WriteByte(AmigaBus bus, uint address, byte value, long grantedCycle)
                => bus.WriteCpuCustomRegisterByte(address, value, grantedCycle);

            public void WriteWord(AmigaBus bus, uint address, ushort value, long grantedCycle)
                => bus.WriteCpuCustomRegisterWord(address, value, grantedCycle);

            public void WriteLong(AmigaBus bus, uint address, uint value, long firstWordCycle, long secondWordCycle)
                => bus.WriteCpuCustomRegisterLong(address, value, firstWordCycle, secondWordCycle);
        }

        private readonly struct ChipRamCpuWriteTarget : ICpuWriteTarget
        {
            public void WriteByte(AmigaBus bus, uint address, byte value, long grantedCycle)
                => bus.WriteCpuChipRamByte(address, value, grantedCycle);

            public void WriteWord(AmigaBus bus, uint address, ushort value, long grantedCycle)
                => bus.WriteCpuChipRamWord(address, value, grantedCycle);

            public void WriteLong(AmigaBus bus, uint address, uint value, long firstWordCycle, long secondWordCycle)
                => bus.WriteCpuChipRamLong(address, value, firstWordCycle, secondWordCycle);
        }

        private readonly struct ExpansionRamCpuWriteTarget : ICpuWriteTarget
        {
            public void WriteByte(AmigaBus bus, uint address, byte value, long grantedCycle)
                => bus.WriteCpuExpansionRamByte(address, value, grantedCycle);

            public void WriteWord(AmigaBus bus, uint address, ushort value, long grantedCycle)
                => bus.WriteCpuExpansionRamWord(address, value, grantedCycle);

            public void WriteLong(AmigaBus bus, uint address, uint value, long firstWordCycle, long secondWordCycle)
                => bus.WriteCpuExpansionRamLong(address, value, firstWordCycle, secondWordCycle);
        }

        private readonly struct RealFastRamCpuWriteTarget : ICpuWriteTarget
        {
            public void WriteByte(AmigaBus bus, uint address, byte value, long grantedCycle)
                => bus.WriteCpuRealFastRamByte(address, value, grantedCycle);

            public void WriteWord(AmigaBus bus, uint address, ushort value, long grantedCycle)
                => bus.WriteCpuRealFastRamWord(address, value, grantedCycle);

            public void WriteLong(AmigaBus bus, uint address, uint value, long firstWordCycle, long secondWordCycle)
                => bus.WriteCpuRealFastRamLong(address, value, firstWordCycle, secondWordCycle);
        }

        private readonly struct CiaCpuWriteTarget : ICpuWriteTarget
        {
            public void WriteByte(AmigaBus bus, uint address, byte value, long grantedCycle)
                => bus.WriteCpuCiaByte(address, value, grantedCycle);

            public void WriteWord(AmigaBus bus, uint address, ushort value, long grantedCycle)
                => bus.WriteRawWord(address, value, grantedCycle, default(CpuWritePolicy));

            public void WriteLong(AmigaBus bus, uint address, uint value, long firstWordCycle, long secondWordCycle)
                => bus.WriteRawLong(address, value, firstWordCycle, secondWordCycle, default(CpuWritePolicy));
        }

        private readonly struct FallbackCpuWriteTarget : ICpuWriteTarget
        {
            public void WriteByte(AmigaBus bus, uint address, byte value, long grantedCycle)
                => bus.WriteRawByte(address, value, grantedCycle, default(CpuWritePolicy));

            public void WriteWord(AmigaBus bus, uint address, ushort value, long grantedCycle)
                => bus.WriteRawWord(address, value, grantedCycle, default(CpuWritePolicy));

            public void WriteLong(AmigaBus bus, uint address, uint value, long firstWordCycle, long secondWordCycle)
                => bus.WriteRawLong(address, value, firstWordCycle, secondWordCycle, default(CpuWritePolicy));
        }

        private readonly AmigaChipRamBackend _chipRam;
        private readonly byte[] _chipRamData;
        private readonly int _chipRamMask;
        private readonly AmigaLinearRamBackend _expansionRam;
        private readonly AmigaLinearRamBackend _realFastRam;
        private readonly Dictionary<uint, HostTrapStub> _hostTrapStubs = new Dictionary<uint, HostTrapStub>();
        private readonly Dictionary<ushort, Action<M68kCpuState>> _relocatableHostTrapStubs = new Dictionary<ushort, Action<M68kCpuState>>();
        private readonly List<uint> _hostTrapStubAddresses = new List<uint>();
        private readonly bool[] _hostTrapStubPages = new bool[HostTrapStubPageCount];
        private readonly AmigaMappedMemoryBackend _mappedMemory = new AmigaMappedMemoryBackend();
        private readonly List<AmigaCiaInterruptEvent> _pendingCiaInterrupts = new List<AmigaCiaInterruptEvent>(16);
        private readonly AmigaCiaInterruptEvent[] _drainedCiaInterruptBuffer = new AmigaCiaInterruptEvent[MaxPendingInterruptEvents];
        private readonly ReusableReadOnlyList<AmigaCiaInterruptEvent> _drainedCiaInterrupts = new ReusableReadOnlyList<AmigaCiaInterruptEvent>();
        private readonly uint[] _instructionFetchWindowGeneration = { 1u };
        private readonly BoundedBusAccessLog _busAccesses = new BoundedBusAccessLog(MaxCapturedBusAccesses);
        private readonly BoundedCpuBusPhaseLog _cpuBusPhases = new BoundedCpuBusPhaseLog(MaxCapturedCpuBusPhases);
        private readonly BoundedReadLog _customRegisterReads = new BoundedReadLog(MaxCapturedCustomRegisterReads);
        private BoundedCpuChipRamWriteTraceLog? _cpuChipRamWriteTrace;
        private BoundedReadLog? _customRegisterReadTrace;
        private uint _cpuChipRamWriteTraceStart;
        private uint _cpuChipRamWriteTraceEndExclusive;
        private ushort _customRegisterReadTraceStart;
        private ushort _customRegisterReadTraceEndExclusive;
        private readonly IAgnusChipSlotTiming _diagnosticChipSlots;
        private readonly AgnusHrmSlotEngine _hrmSlotEngine;
        private readonly AmigaRasterlineScheduleCache _rasterlineScheduleCache;
        private readonly AmigaHardwareScheduler _hardwareScheduler;
        private readonly CpuBusBankKind[] _cpuBusBankKinds = new CpuBusBankKind[CpuBusBankCount];
        private readonly int[] _cpuBusBankOffsets = new int[CpuBusBankCount];
        private readonly byte[] _jitDirectRamBankKinds = new byte[CpuBusBankCount];
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
        private readonly bool _deferredCpuBusBatchEnabled;
        private readonly bool _deferredCpuBusBatchVerifyEnabled;
        private readonly bool _forceCpuWaitSlotReference;
        private readonly bool _liveAgnusDmaDefault;
        private readonly AmigaRealTimeClock? _realTimeClock;
        private readonly GamePortState[] _gamePorts = { new GamePortState(), new GamePortState() };
        private readonly AgnusPalBeamClock _palBeamClock = new();
        private readonly long _palLineCycles;
        private int _customRegisterWriteContextDepth;
        private AmigaBusRequester _customRegisterWriteRequester;
        private ushort _customRegisterWriteOffset;
        private long _customRegisterWriteCycle;
        private bool _customRegisterWriteAffectsSchedule;
        private bool _romOverlayEnabled = true;
        private long _nextVerticalBlankCycle;
        private long _nextHorizontalSyncIndex;
        private long _nextHorizontalSyncCycle;
        private long _lastRasterAdvanceCycle;
        private bool _deferredCpuInstructionTimingActive;
        private int _deferredCpuDataAccessCount;
        private ulong _deferredCpuDataLongShapeBits;
        private ulong _deferredCpuDataCiaShapeBits;
        private long _deferredCpuDataReplayCycle;
        private bool _deferredCpuBusBatchActive;
        private bool _endingDeferredCpuBusBatch;
        private bool _deferredCpuBusBatchExecutionStarted;
        private M68kTraceBatchWakeSource _deferredCpuBusBatchPendingWakeSource;
        private AmigaDiskController.SchedulerWakeReason _deferredCpuBusBatchPendingDiskWakeReason;
        private long _deferredCpuBusBatchAttempts;
        private long _deferredCpuBusBatchUsed;
        private long _deferredCpuBusBatchInstructions;
        private long _deferredCpuBusBatchSkippedInstructionFlushes;
        private long _deferredCpuBusBatchFlushes;
        private long _deferredCpuBusBatchExitTargetCycle;
        private long _deferredCpuBusBatchExitMaxInstructions;
        private long _deferredCpuBusBatchExitChipVisibleAccess;
        private long _deferredCpuBusBatchExitPcLeftFastWindow;
        private long _deferredCpuBusBatchExitException;
        private long _deferredCpuBusBatchExitUnsupported;
        private long _deferredCpuBusBatchVerificationMismatches;
        private string _deferredCpuBusBatchFirstMismatch = string.Empty;
        private long _deferredCpuBusBatchWakeTargetCycle;
        private long _deferredCpuBusBatchWakePendingInterrupt;
        private long _deferredCpuBusBatchWakeVerticalBlank;
        private long _deferredCpuBusBatchWakeHorizontalSyncTod;
        private long _deferredCpuBusBatchWakeCiaTimer;
        private long _deferredCpuBusBatchWakeDisk;
        private long _deferredCpuBusBatchWakePaula;
        private long _deferredCpuBusBatchWakeCopper;
        private long _deferredCpuBusBatchWakeBlitter;
        private long _deferredCpuBusBatchDiskWakePendingDma;
        private long _deferredCpuBusBatchDiskWakeActiveDmaProgress;
        private long _deferredCpuBusBatchDiskWakeActiveDmaCompletion;
        private long _deferredCpuBusBatchDiskWakeSyncCandidate;
        private long _deferredCpuBusBatchDiskWakeIndexPulse;
        private long _deferredCpuBusBatchDiskWakePassiveByteReady;
        private long _deferredCpuBusBatchDiskWakeUnknown;
        private long _deferredCpuBatchExitChipAccessCycle = -1;
        private bool _deferredCpuWaitFixedImageProductionDisabled;
        private long _deferredCpuInternalNoBusWindowAttempts;
        private long _deferredCpuInternalNoBusWindowUsed;
        private long _deferredCpuInternalNoBusWindowTotalCycles;
        private long _deferredCpuInternalNoBusWindowAdvancedCycles;
        private long _deferredCpuInternalNoBusWindowMultiply;
        private long _deferredCpuInternalNoBusWindowDivide;
        private long _deferredCpuInternalNoBusWindowWakeTargetCycle;
        private long _deferredCpuInternalNoBusWindowWakePendingInterrupt;
        private long _deferredCpuInternalNoBusWindowWakeVerticalBlank;
        private long _deferredCpuInternalNoBusWindowWakeHorizontalSyncTod;
        private long _deferredCpuInternalNoBusWindowWakeCiaTimer;
        private long _deferredCpuInternalNoBusWindowWakeDisk;
        private long _deferredCpuInternalNoBusWindowWakePaula;
        private long _deferredCpuInternalNoBusWindowWakeCopper;
        private long _deferredCpuInternalNoBusWindowWakeBlitter;
        private long _deferredCpuInternalNoBusWindowVerificationMismatches;
        private string _deferredCpuInternalNoBusWindowFirstMismatch = string.Empty;
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
            IEnumerable<AmigaHardfileConfiguration>? hardfiles = null,
            bool enableCopperQuiescentFastPath = false,
            bool verifyCopperQuiescentFastPath = false,
            bool enableDeferredCpuBusBatch = false,
            bool verifyDeferredCpuBusBatch = false,
            bool enableCopperQuiescentDiagnostics = false,
            bool forceCpuWaitSlotReference = false)
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
            ExpansionRamBase = expansionRamBase;
            RealFastRamBase = realFastRamBase;
            _expansionRam = new AmigaLinearRamBackend(expansionRamSize, ExpansionRamBase, CodeGenerationPageShift);
            _realFastRam = new AmigaLinearRamBackend(realFastRamSize, RealFastRamBase, CodeGenerationPageShift);
            _hrmSlotEngine = new AgnusHrmSlotEngine(captureBusAccesses);
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
            CopperQuiescentFastPathEnabled = enableCopperQuiescentFastPath;
            CopperQuiescentFastPathVerifyEnabled = verifyCopperQuiescentFastPath;
            CopperQuiescentDiagnosticsEnabled = enableCopperQuiescentDiagnostics ||
                enableCopperQuiescentFastPath ||
                verifyCopperQuiescentFastPath;
            CopperQuiescentShadowPredictionEnabled = enableCopperQuiescentDiagnostics ||
                verifyCopperQuiescentFastPath;
            _deferredCpuBusBatchEnabled = enableDeferredCpuBusBatch;
            _deferredCpuBusBatchVerifyEnabled = verifyDeferredCpuBusBatch;
            _deferredCpuWaitDiagnosticsEnabled = verifyDeferredCpuBusBatch;
            _forceCpuWaitSlotReference = forceCpuWaitSlotReference;
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
            _hrmSlotEngine.BeamPositionProvider = GetPalBeamPosition;
            _palLineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;
            _palBeamClock.Reset();
            _nextVerticalBlankCycle = _palBeamClock.GetNextFrameStartCycle(0);
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

        internal IReadOnlyList<CustomRegisterRead> CustomRegisterReads => _customRegisterReads;

        internal IReadOnlyList<CustomRegisterRead> CustomRegisterReadTrace
        {
            get
            {
                if (_customRegisterReadTrace != null)
                {
                    return _customRegisterReadTrace;
                }

                return Array.Empty<CustomRegisterRead>();
            }
        }

        public IReadOnlyList<AmigaBusAccessResult> BusAccesses => _busAccesses;

        internal bool BusAccessCaptureEnabled => _captureBusAccesses;

        internal IReadOnlyList<AmigaCpuBusPhaseTrace> CpuBusPhases => _cpuBusPhases;

        internal IReadOnlyList<CpuChipRamWriteTrace> CpuChipRamWriteTrace
        {
            get
            {
                if (_cpuChipRamWriteTrace != null)
                {
                    return _cpuChipRamWriteTrace;
                }

                return Array.Empty<CpuChipRamWriteTrace>();
            }
        }

        internal void CaptureCpuChipRamWriteTrace(uint startAddress, uint byteCount, int capacity)
        {
            if (byteCount == 0)
            {
                _cpuChipRamWriteTrace = null;
                _cpuChipRamWriteTraceStart = 0;
                _cpuChipRamWriteTraceEndExclusive = 0;
                return;
            }

            _cpuChipRamWriteTrace = new BoundedCpuChipRamWriteTraceLog(capacity);
            _cpuChipRamWriteTraceStart = startAddress;
            _cpuChipRamWriteTraceEndExclusive = startAddress + byteCount;
        }

        internal void CaptureCustomRegisterReadTrace(ushort startOffset, ushort byteCount, int capacity)
        {
            if (byteCount == 0)
            {
                _customRegisterReadTrace = null;
                _customRegisterReadTraceStart = 0;
                _customRegisterReadTraceEndExclusive = 0;
                return;
            }

            _customRegisterReadTrace = new BoundedReadLog(capacity);
            _customRegisterReadTraceStart = (ushort)(startOffset & 0x01FE);
            _customRegisterReadTraceEndExclusive = (ushort)(_customRegisterReadTraceStart + byteCount);
        }

        public bool DiskDivergenceTraceEnabled => Disk.DivergenceTraceEnabled;

        public long CiaBTimerAIntervalCycles => CiaB.TimerAIntervalCycles;

        internal bool LiveAgnusDmaEnabled { get; private set; }

        internal bool CopperQuiescentFastPathEnabled { get; }

        internal bool CopperQuiescentFastPathVerifyEnabled { get; }

        internal bool CopperQuiescentDiagnosticsEnabled { get; }

        internal bool CopperQuiescentShadowPredictionEnabled { get; }

        internal long DeferredCpuBusBatchAttempts => _deferredCpuBusBatchAttempts;

        internal long DeferredCpuBusBatchUsed => _deferredCpuBusBatchUsed;

        internal long DeferredCpuBusBatchInstructions => _deferredCpuBusBatchInstructions;

        internal long DeferredCpuBusBatchSkippedInstructionFlushes => _deferredCpuBusBatchSkippedInstructionFlushes;

        internal long DeferredCpuBusBatchFlushes => _deferredCpuBusBatchFlushes;

        internal long DeferredCpuBusBatchExitTargetCycle => _deferredCpuBusBatchExitTargetCycle;

        internal long DeferredCpuBusBatchExitMaxInstructions => _deferredCpuBusBatchExitMaxInstructions;

        internal long DeferredCpuBusBatchExitChipVisibleAccess => _deferredCpuBusBatchExitChipVisibleAccess;

        internal long DeferredCpuBusBatchExitPcLeftFastWindow => _deferredCpuBusBatchExitPcLeftFastWindow;

        internal long DeferredCpuBusBatchExitException => _deferredCpuBusBatchExitException;

        internal long DeferredCpuBusBatchExitUnsupported => _deferredCpuBusBatchExitUnsupported;

        internal long DeferredCpuBusBatchVerificationMismatches => _deferredCpuBusBatchVerificationMismatches;

        internal string DeferredCpuBusBatchFirstMismatch => _deferredCpuBusBatchFirstMismatch;

        internal long DeferredCpuBusBatchWakeTargetCycle => _deferredCpuBusBatchWakeTargetCycle;

        internal long DeferredCpuBusBatchWakePendingInterrupt => _deferredCpuBusBatchWakePendingInterrupt;

        internal long DeferredCpuBusBatchWakeVerticalBlank => _deferredCpuBusBatchWakeVerticalBlank;

        internal long DeferredCpuBusBatchWakeHorizontalSyncTod => _deferredCpuBusBatchWakeHorizontalSyncTod;

        internal long DeferredCpuBusBatchWakeCiaTimer => _deferredCpuBusBatchWakeCiaTimer;

        internal long DeferredCpuBusBatchWakeDisk => _deferredCpuBusBatchWakeDisk;

        internal long DeferredCpuBusBatchWakePaula => _deferredCpuBusBatchWakePaula;

        internal long DeferredCpuBusBatchWakeCopper => _deferredCpuBusBatchWakeCopper;

        internal long DeferredCpuBusBatchWakeBlitter => _deferredCpuBusBatchWakeBlitter;

        internal long DeferredCpuBusBatchDiskWakePendingDma => _deferredCpuBusBatchDiskWakePendingDma;

        internal long DeferredCpuBusBatchDiskWakeActiveDmaProgress => _deferredCpuBusBatchDiskWakeActiveDmaProgress;

        internal long DeferredCpuBusBatchDiskWakeActiveDmaCompletion => _deferredCpuBusBatchDiskWakeActiveDmaCompletion;

        internal long DeferredCpuBusBatchDiskWakeSyncCandidate => _deferredCpuBusBatchDiskWakeSyncCandidate;

        internal long DeferredCpuBusBatchDiskWakeIndexPulse => _deferredCpuBusBatchDiskWakeIndexPulse;

        internal long DeferredCpuBusBatchDiskWakePassiveByteReady => _deferredCpuBusBatchDiskWakePassiveByteReady;

        internal long DeferredCpuBusBatchDiskWakeUnknown => _deferredCpuBusBatchDiskWakeUnknown;

        internal long DeferredCpuInternalNoBusWindowAttempts => _deferredCpuInternalNoBusWindowAttempts;

        internal long DeferredCpuInternalNoBusWindowUsed => _deferredCpuInternalNoBusWindowUsed;

        internal long DeferredCpuInternalNoBusWindowTotalCycles => _deferredCpuInternalNoBusWindowTotalCycles;

        internal long DeferredCpuInternalNoBusWindowAdvancedCycles => _deferredCpuInternalNoBusWindowAdvancedCycles;

        internal long DeferredCpuInternalNoBusWindowMultiply => _deferredCpuInternalNoBusWindowMultiply;

        internal long DeferredCpuInternalNoBusWindowDivide => _deferredCpuInternalNoBusWindowDivide;

        internal long DeferredCpuInternalNoBusWindowWakeTargetCycle => _deferredCpuInternalNoBusWindowWakeTargetCycle;

        internal long DeferredCpuInternalNoBusWindowWakePendingInterrupt => _deferredCpuInternalNoBusWindowWakePendingInterrupt;

        internal long DeferredCpuInternalNoBusWindowWakeVerticalBlank => _deferredCpuInternalNoBusWindowWakeVerticalBlank;

        internal long DeferredCpuInternalNoBusWindowWakeHorizontalSyncTod => _deferredCpuInternalNoBusWindowWakeHorizontalSyncTod;

        internal long DeferredCpuInternalNoBusWindowWakeCiaTimer => _deferredCpuInternalNoBusWindowWakeCiaTimer;

        internal long DeferredCpuInternalNoBusWindowWakeDisk => _deferredCpuInternalNoBusWindowWakeDisk;

        internal long DeferredCpuInternalNoBusWindowWakePaula => _deferredCpuInternalNoBusWindowWakePaula;

        internal long DeferredCpuInternalNoBusWindowWakeCopper => _deferredCpuInternalNoBusWindowWakeCopper;

        internal long DeferredCpuInternalNoBusWindowWakeBlitter => _deferredCpuInternalNoBusWindowWakeBlitter;

        internal long DeferredCpuInternalNoBusWindowVerificationMismatches => _deferredCpuInternalNoBusWindowVerificationMismatches;

        internal string DeferredCpuInternalNoBusWindowFirstMismatch => _deferredCpuInternalNoBusWindowFirstMismatch;

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
            _hostTrapStubs.Clear();
            _relocatableHostTrapStubs.Clear();
            _hostTrapStubAddresses.Clear();
            Array.Clear(_hostTrapStubPages);
            _nextHostTrapId = 1;
            _mappedMemory.Clear();
            _romOverlayEnabled = true;
            InvalidateInstructionFetchWindows();
            StrictCpuPhysicalDataMapping = false;
            _pendingCiaInterrupts.Clear();
            _busAccesses.Clear();
            _cpuBusPhases.Clear();
            _customRegisterReads.Clear();
            _lastCpuBusAccess = null;
            _lastCpuBusGrantedSlot = null;
            ResetDeferredCpuBusBatchState(resetCounters: true);
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
            _palBeamClock.Reset();
            _nextVerticalBlankCycle = _palBeamClock.GetNextFrameStartCycle(0);
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
            var trapId = AllocateHostTrapId();
            _hostTrapStubs[address] = new HostTrapStub(address, trapId, callback ?? throw new ArgumentNullException(nameof(callback)));
            AddHostTrapStubAddress(address);
            MarkHostTrapStubPages(address);
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
            _pendingCiaInterrupts.Clear();
            _busAccesses.Clear();
            _cpuBusPhases.Clear();
            _lastCpuBusAccess = null;
            _lastCpuBusGrantedSlot = null;
            ResetDeferredCpuBusBatchState(resetCounters: true);
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
            _palBeamClock.Reset();
            _nextVerticalBlankCycle = _palBeamClock.GetNextFrameStartCycle(0);
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

        void IM68kCpuBusPhaseTrace.RecordCpuBusPhase(in M68kCpuBusPhase phase)
        {
            if (!_captureBusAccesses)
            {
                return;
            }

            var access = phase.AccessKind == M68kBusAccessKind.CpuInterruptAcknowledge
                ? null
                : _lastCpuBusAccess;
            var grantedSlot = phase.AccessKind == M68kBusAccessKind.CpuInterruptAcknowledge
                ? null
                : _lastCpuBusGrantedSlot;
            var secondWordCycle = access.HasValue ? GetSecondWordCycle(access.Value) : phase.CompletedCycle;
            _cpuBusPhases.Add(new AmigaCpuBusPhaseTrace(
                phase,
                access,
                secondWordCycle,
                grantedSlot));

            if (phase.AccessKind == M68kBusAccessKind.CpuInterruptAcknowledge)
            {
                _lastCpuBusAccess = null;
                _lastCpuBusGrantedSlot = null;
            }
        }

        bool IM68kPhysicalAddressMap.IsCpuPhysicalAddressMapped(uint address, int byteCount, M68kBusAccessKind accessKind)
            => IsCpuPhysicalAddressMapped(address, byteCount, ToAmigaBusAccessKind(accessKind));

        bool IM68kFastMemoryBus.TryReadFastByte(uint address, M68kBusAccessKind accessKind, out byte value)
        {
            if (!CanUseInterpreterFastRead(address, byteCount: 1, accessKind))
            {
                value = 0;
                return false;
            }

            value = ReadHostByte(address);
            return true;
        }

        bool IM68kFastMemoryBus.TryReadFastWord(uint address, M68kBusAccessKind accessKind, out ushort value)
        {
            if (!CanUseInterpreterFastRead(address, byteCount: 2, accessKind))
            {
                value = 0;
                return false;
            }

            value = ReadHostWord(address);
            return true;
        }

        bool IM68kFastMemoryBus.TryReadFastLong(uint address, M68kBusAccessKind accessKind, out uint value)
        {
            if (!CanUseInterpreterFastRead(address, byteCount: 4, accessKind))
            {
                value = 0;
                return false;
            }

            value = ReadHostLong(address);
            return true;
        }

        bool IM68kFastMemoryBus.TryWriteFastByte(uint address, byte value, M68kBusAccessKind accessKind)
        {
            if (!CanUseInterpreterFastWrite(address, byteCount: 1))
            {
                return false;
            }

            WriteHostByte(address, value);
            return true;
        }

        bool IM68kFastMemoryBus.TryWriteFastWord(uint address, ushort value, M68kBusAccessKind accessKind)
        {
            if (!CanUseInterpreterFastWrite(address, byteCount: 2))
            {
                return false;
            }

            WriteHostWord(address, value);
            return true;
        }

        bool IM68kFastMemoryBus.TryWriteFastLong(uint address, uint value, M68kBusAccessKind accessKind)
        {
            if (!CanUseInterpreterFastWrite(address, byteCount: 4))
            {
                return false;
            }

            WriteHostLong(address, value);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CanUseInterpreterFastRead(uint address, int byteCount, M68kBusAccessKind accessKind)
        {
            // The 68040 JIT max-speed profile explicitly treats CIA-A Port A as a host
            // device access. Keep this narrow so normal CIA timing remains observable.
            if (byteCount == 1 && (address & CpuAddressMask) == 0x00BF_E001u)
            {
                return true;
            }

            if (TryGetRealFastRamOffset(address, out var realFastOffset) &&
                realFastOffset + byteCount <= _realFastRam.Length)
            {
                return true;
            }

            var amigaAccessKind = ToAmigaBusAccessKind(accessKind);
            var target = amigaAccessKind == AmigaBusAccessKind.CpuInstructionFetch
                ? ClassifyInstructionFetchTarget(address)
                : ClassifyTarget(address);
            if (target is not (AmigaBusAccessTarget.Rom or AmigaBusAccessTarget.RealFastRam))
            {
                return false;
            }

            var lastAddress = address + (uint)(byteCount - 1);
            var lastTarget = amigaAccessKind == AmigaBusAccessKind.CpuInstructionFetch
                ? ClassifyInstructionFetchTarget(lastAddress)
                : ClassifyTarget(lastAddress);
            return lastTarget == target;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CanUseInterpreterFastWrite(uint address, int byteCount)
        {
            if (byteCount == 1 && (address & CpuAddressMask) == 0x00BF_E001u)
            {
                return true;
            }

            if (TryGetRealFastRamOffset(address, out var realFastOffset) &&
                realFastOffset + byteCount <= _realFastRam.Length)
            {
                return true;
            }

            if (ClassifyTarget(address) != AmigaBusAccessTarget.RealFastRam)
            {
                return false;
            }

            var lastAddress = address + (uint)(byteCount - 1);
            return ClassifyTarget(lastAddress) == AmigaBusAccessTarget.RealFastRam;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryReadExactCpuDataByte(uint address, ref long cycle, out byte value)
        {
            var normalizedAddress = address;
            if (TryGetCiaRegister(normalizedAddress, out var cia, out var ciaRegister) &&
                CanDeferExactCpuCiaRead(ciaRegister) &&
                TryDeferExactCpuCiaDataTiming(ref cycle))
            {
                value = ReadCiaRegisterValue(cia, ciaRegister, cycle);
                return true;
            }

            if (!TryResolveExactCpuDataRamRegion(
                normalizedAddress,
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
                out var grantedCycle,
                out var secondWordCycle);
            value = ReadExactCpuDataRamLong(
                in region,
                grantedCycle,
                secondWordCycle);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint ReadExactCpuDataRamLong(
            in AmigaExactCpuDataRamRegion region,
            long firstWordCycle,
            long secondWordCycle)
        {
            if (region.Target == AmigaBusAccessTarget.ChipRam)
            {
                return ((uint)ReadChipWordForPresentation(region.Address, firstWordCycle) << 16) |
                    ReadChipWordForPresentation(region.Address + 2, secondWordCycle);
            }

            return ((uint)region.Memory[region.Offset] << 24) |
                ((uint)region.Memory[region.Offset + 1] << 16) |
                ((uint)region.Memory[region.Offset + 2] << 8) |
                region.Memory[region.Offset + 3];
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
                OcsLiveDmaScratchCpuWrite.Byte(region.Target, region.Address, value),
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
                OcsLiveDmaScratchCpuWrite.Word(region.Target, region.Address, value),
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
                OcsLiveDmaScratchCpuWrite.Long(region.Target, region.Address, value),
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
            var normalizedAddress = address;
            region = default;
            if (!_useFastZeroWaitAccesses ||
                byteCount <= 0 ||
                normalizedAddress > CpuAddressMask ||
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
            if (!HostTrapStubPageHasTrap(startAddress))
            {
                return true;
            }

            var index = _hostTrapStubAddresses.BinarySearch(startAddress);
            if (index >= 0)
            {
                return false;
            }

            index = ~index;
            if (index > 0)
            {
                var previousTrapAddress = _hostTrapStubAddresses[index - 1];
                if (startAddress < previousTrapAddress + 4u)
                {
                    return false;
                }
            }

            if (index < _hostTrapStubAddresses.Count)
            {
                var nextTrapAddress = _hostTrapStubAddresses[index];
                if (nextTrapAddress < endAddress)
                {
                    endAddress = nextTrapAddress;
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
            if (IsCustomRegisterByteAddress(address))
            {
                FlushDeferredCpuDataTiming(ref cycle);
                return ReadCpuCustomByte(address, ref cycle, accessKind, sampleCustomAtGrantedCycle);
            }

            if (TryGetCiaRegister(address, out var directCia, out var directCiaRegister))
            {
                FlushDeferredCpuDataTiming(ref cycle);
                return ReadCpuCiaByte(address, directCia, directCiaRegister, ref cycle, accessKind);
            }

            var target = ClassifyTarget(address);
            FlushDeferredCpuDataTimingForAccess(target, accessKind, isWrite: false, ref cycle);
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
            long sampleCycle;
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
                    out sampleCycle,
                    out _,
                    out completedCycle);
            }
            else
            {
                var access = Arbitrate(
                    AmigaBusRequester.Cpu,
                    accessKind,
                    AmigaBusAccessTarget.Cia,
                    address,
                    AmigaBusAccessSize.Byte,
                    cycle,
                    isWrite: false);
                sampleCycle = access.GrantedCycle;
                completedCycle = access.CompletedCycle;
            }

            var value = ReadCiaRegisterValue(cia, ciaRegister, sampleCycle);
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
            RecordCpuBeamRegisterRead(address, value, requestedCycle, grantedCycle, completedCycle, sampleCycle, accessKind);
            cycle = completedCycle;
            return value;
        }

        private void RecordCpuBeamRegisterRead(
            uint address,
            ushort value,
            long requestedCycle,
            long grantedCycle,
            long completedCycle,
            long sampleCycle,
            AmigaBusAccessKind accessKind)
        {
            var trace = _customRegisterReadTrace;
            if (!_captureBusAccesses && trace == null)
            {
                return;
            }

            var offset = (ushort)(address & 0x01FE);
            if (offset != 0x004 && offset != 0x006)
            {
                return;
            }

            var read = new CustomRegisterRead(
                offset,
                value,
                requestedCycle,
                grantedCycle,
                completedCycle,
                sampleCycle,
                accessKind);
            if (_captureBusAccesses)
            {
                _customRegisterReads.Add(read);
            }

            if (trace != null &&
                offset >= _customRegisterReadTraceStart &&
                offset < _customRegisterReadTraceEndExclusive)
            {
                trace.Add(read);
            }
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
            var target = ClassifyTarget(address);
            FlushDeferredCpuDataTimingForAccess(target, accessKind, isWrite: true, ref cycle);
            var requestedCycle = cycle;
            if (_useFastZeroWaitAccesses)
            {
                WriteCpuByteFast(target, address, value, ref cycle, accessKind, requestedCycle);
                return;
            }

            WriteCpuByteArbitrated(target, address, value, ref cycle, accessKind, requestedCycle);
        }

        internal void WriteTasCpuDataByte(uint address, byte value, ref long cycle)
        {
            var target = ClassifyTarget(address);
            FlushDeferredCpuDataTimingForAccess(
                target,
                AmigaBusAccessKind.CpuDataWrite,
                isWrite: true,
                ref cycle);
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
                    WriteRawByte(address, value, grantedCycle, default(CpuWritePolicy));
                }

                cycle = completedCycle;
                return;
            }

            var access = Arbitrate(AmigaBusRequester.Cpu, AmigaBusAccessKind.CpuDataWrite, target, address, AmigaBusAccessSize.Byte, cycle, isWrite: true);
            AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, access.GrantedCycle, isWrite: true);
            if (target != AmigaBusAccessTarget.ChipRam)
            {
                WriteRawByte(address, value, access.GrantedCycle, default(CpuWritePolicy));
            }

            cycle = access.CompletedCycle;
        }

        public void WriteWord(uint address, ushort value, long cycle)
        {
            WriteWord(address, value, ref cycle, AmigaBusAccessKind.CpuDataWrite);
        }

        public void WriteWord(uint address, ushort value, ref long cycle, AmigaBusAccessKind accessKind)
        {
            var target = ClassifyTarget(address);
            FlushDeferredCpuDataTimingForAccess(target, accessKind, isWrite: true, ref cycle);
            var requestedCycle = cycle;
            if (_useFastZeroWaitAccesses)
            {
                WriteCpuWordFast(target, address, value, ref cycle, accessKind, requestedCycle);
                return;
            }

            WriteCpuWordArbitrated(target, address, value, ref cycle, accessKind, requestedCycle);
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
            if (IsCustomRegisterWordAddress(address))
            {
                FlushDeferredCpuDataTiming(ref cycle);
                return ReadCpuCustomWord(address, ref cycle, accessKind, sampleCustomAtGrantedCycle);
            }

            var target = accessKind == AmigaBusAccessKind.CpuInstructionFetch
                ? ClassifyInstructionFetchTarget(address)
                : ClassifyTarget(address);
            FlushDeferredCpuDataTimingForAccess(target, accessKind, isWrite: false, ref cycle);
            var requestedCycle = cycle;
            if (accessKind == AmigaBusAccessKind.CpuInstructionFetch &&
                (target == AmigaBusAccessTarget.ChipRam ||
                    target == AmigaBusAccessTarget.ExpansionRam))
            {
                CommitExactCpuDataTiming(
                    target,
                    address,
                    AmigaBusAccessSize.Word,
                    ref cycle,
                    isWrite: false,
                    accessKind,
                    out var grantedCycle,
                    out _);
                return ReadCpuWordValue(target, address, grantedCycle, accessKind, sampleCustomAtGrantedCycle);
            }

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
                var fastValue = ReadCpuWordValue(target, address, grantedCycle, accessKind, sampleCustomAtGrantedCycle);
                cycle = completedCycle;
                return fastValue;
            }

            var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, target, address, AmigaBusAccessSize.Word, cycle, isWrite: false);
            AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, access.GrantedCycle, isWrite: false);
            var value = ReadCpuWordValue(target, address, access.GrantedCycle, accessKind, sampleCustomAtGrantedCycle);
            cycle = access.CompletedCycle;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ushort ReadCpuWordValue(
            AmigaBusAccessTarget target,
            uint address,
            long grantedCycle,
            AmigaBusAccessKind accessKind,
            bool sampleCustomAtGrantedCycle)
        {
            if (target == AmigaBusAccessTarget.HostTrap &&
                accessKind == AmigaBusAccessKind.CpuInstructionFetch)
            {
                return ReadHostWord(address);
            }

            return sampleCustomAtGrantedCycle && target == AmigaBusAccessTarget.CustomRegisters
                ? ReadRawWord(address, grantedCycle)
                : ReadRawWord(address);
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
            if (IsCustomRegisterByteAddress(address))
            {
                FlushDeferredCpuDataTiming(ref cycle);
                return ReadCpuCustomLong(address, ref cycle, accessKind, sampleCustomAtGrantedCycle);
            }

            var target = ClassifyTarget(address);
            FlushDeferredCpuDataTimingForAccess(target, accessKind, isWrite: false, ref cycle);
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
                var fastValue = (sampleCustomAtGrantedCycle && target == AmigaBusAccessTarget.CustomRegisters) ||
                    target == AmigaBusAccessTarget.ChipRam
                    ? ReadRawLong(address, grantedCycle, secondWordCycle)
                    : ReadRawLong(address);
                cycle = completedCycle;
                return fastValue;
            }

            var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, target, address, AmigaBusAccessSize.Long, cycle, isWrite: false);
            AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, access.GrantedCycle, isWrite: false);
            var value = (sampleCustomAtGrantedCycle && target == AmigaBusAccessTarget.CustomRegisters) ||
                target == AmigaBusAccessTarget.ChipRam
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
            var target = ClassifyTarget(address);
            FlushDeferredCpuDataTimingForAccess(target, accessKind, isWrite: true, ref cycle);
            var requestedCycle = cycle;
            if (_useFastZeroWaitAccesses)
            {
                WriteCpuLongFast(target, address, value, ref cycle, accessKind, requestedCycle);
                return;
            }

            WriteCpuLongArbitrated(target, address, value, ref cycle, accessKind, requestedCycle);
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
            if (TryReadHostTrapStubByte(address, out var hostTrapByte))
            {
                return hostTrapByte;
            }

            return ReadRawByte(address);
        }

        internal ushort ReadHostWord(uint address)
        {
            return (ushort)((ReadHostByte(address) << 8) | ReadHostByte(address + 1));
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

            var lastAddress = physicalAddress + (uint)(byteCount - 1);
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

            var lastAddress = physicalAddress + (uint)(byteCount - 1);
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
            return ((uint)ReadHostWord(address) << 16) | ReadHostWord(address + 2);
        }

        internal void WriteHostByte(uint address, byte value)
        {
            WriteRawByte(address, value, 0, default(HostWritePolicy));
        }

        internal void WriteHostWord(uint address, ushort value)
        {
            WriteRawWord(address, value, 0, default(HostWritePolicy));
        }

        internal void WriteHostLong(uint address, uint value)
        {
            WriteRawLong(address, value, 0, 0, default(HostWritePolicy));
        }

        public void CopyToMemory(uint address, ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty)
            {
                return;
            }

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

            if ((uint)(byteCount - 1) > uint.MaxValue - address)
            {
                return false;
            }

            if (byteCount > 0x0100_0000)
            {
                return false;
            }

            for (var offset = 0; offset < byteCount; offset++)
            {
                var byteAddress = address + (uint)offset;
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
            long lineStartCycle,
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
                var entryCycle = entry.GetCycle(lineStartCycle);
                Debug.Assert(entryCycle >= 0, "Row bitplane DMA request cycles must be non-negative.");
                var reservation = ReserveLiveBitplaneDmaWord(address, entryCycle);

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
            var value = reservation.Granted
                ? CommitDmaWordRead(in reservation)
                : (ushort)0;

            return new AmigaDeviceWordReadResult(value, reservation.Access);
        }

        internal AmigaDmaWordReservation ReserveChipWordForDevice(
            AmigaBusRequester requester,
            AmigaBusAccessKind kind,
            uint address,
            long requestedCycle)
        {
            if (requester == AmigaBusRequester.Blitter &&
                kind == AmigaBusAccessKind.Blitter)
            {
                return ReserveBlitterChipWord(address, requestedCycle, isWrite: false);
            }

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

        internal AmigaDmaWordReservation ReserveBlitterChipWord(uint address, long requestedCycle, bool isWrite)
        {
            address = MaskChipDmaAddress(address);
            requestedCycle = Math.Max(0, requestedCycle);
            AmigaBusAccessResult access;
            bool granted;
            if (!_useChipSlotScheduler || !ShouldUseChipSlotScheduler(AmigaBusAccessTarget.ChipRam))
            {
                var request = new AmigaBusAccessRequest(
                    AmigaBusRequester.Blitter,
                    AmigaBusAccessKind.Blitter,
                    AmigaBusAccessTarget.ChipRam,
                    address,
                    AmigaBusAccessSize.Word,
                    requestedCycle,
                    isWrite);
                access = new AmigaBusAccessResult(request, requestedCycle, requestedCycle);
                granted = true;
            }
            else
            {
                _hrmSlotEngine.BlitterPriorityEnabled = (Paula.Dmacon & 0x0400) != 0;
                if (LiveAgnusDmaEnabled &&
                    Display.HasLiveDisplayWork())
                {
                    Display.CaptureLiveDisplayDmaBeforeHrmGrant(requestedCycle);
                }

                access = _hrmSlotEngine.ReserveBlitterDmaWordSlot(address, requestedCycle, isWrite);
                granted = access.CompletedCycle > access.GrantedCycle;
            }

            var reservation = new AmigaDmaWordReservation(address, granted, access);
            if (granted)
            {
                CaptureDmaReservation(in reservation);
            }

            return reservation;
        }

        internal bool TryReserveBlitterChipWordExactSlot(
            uint address,
            long requestedCycle,
            long slotCycle,
            bool isWrite,
            out AmigaDmaWordReservation reservation)
        {
            address = MaskChipDmaAddress(address);
            requestedCycle = Math.Max(0, requestedCycle);
            slotCycle = AgnusChipSlotScheduler.AlignToSlot(Math.Max(0, slotCycle));
            AmigaBusAccessResult access;
            bool granted;
            if (!_useChipSlotScheduler || !ShouldUseChipSlotScheduler(AmigaBusAccessTarget.ChipRam))
            {
                var request = new AmigaBusAccessRequest(
                    AmigaBusRequester.Blitter,
                    AmigaBusAccessKind.Blitter,
                    AmigaBusAccessTarget.ChipRam,
                    address,
                    AmigaBusAccessSize.Word,
                    requestedCycle,
                    isWrite);
                access = new AmigaBusAccessResult(request, slotCycle, slotCycle);
                granted = true;
            }
            else
            {
                _hrmSlotEngine.BlitterPriorityEnabled = (Paula.Dmacon & 0x0400) != 0;
                PrepareLiveDisplaySlotsBeforeDmaGrant(AmigaBusAccessSize.Word, slotCycle);
                granted = _hrmSlotEngine.TryReserveBlitterDmaWordExactSlot(
                    address,
                    requestedCycle,
                    slotCycle,
                    isWrite,
                    out access);
            }

            reservation = new AmigaDmaWordReservation(address, granted, access);
            if (granted)
            {
                CaptureDmaReservation(in reservation);
            }

            return granted;
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
            if (reservation.Granted)
            {
                CommitDmaWordWrite(in reservation, value);
            }

            return reservation.Access;
        }

        internal AmigaDmaWordReservation ReserveChipWordWriteForDevice(
            AmigaBusRequester requester,
            AmigaBusAccessKind kind,
            uint address,
            long requestedCycle)
        {
            if (requester == AmigaBusRequester.Blitter &&
                kind == AmigaBusAccessKind.Blitter)
            {
                return ReserveBlitterChipWord(address, requestedCycle, isWrite: true);
            }

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
            var target = ClassifyTarget(address);
            if (target == AmigaBusAccessTarget.CustomRegisters && requester != AmigaBusRequester.Cpu)
            {
                var grantedCycle = Math.Max(0, requestedCycle);
                MarkCopperIntreqWriteIfNeeded(requester, address, value, grantedCycle);
                WriteCustomSpaceWord(new RequesterWritePolicy(requester), address, value, grantedCycle);
                return;
            }

            var access = Arbitrate(requester, kind, target, address, AmigaBusAccessSize.Word, requestedCycle, isWrite: true);
            MarkCopperIntreqWriteIfNeeded(requester, address, value, access.GrantedCycle);
            WriteRawWord(address, value, access.GrantedCycle, new RequesterWritePolicy(requester));
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
                    _hardwareScheduler.DrainForCpuAccess(target, address, requestedCycle, isWrite, size);
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
                _hardwareScheduler.DrainForCpuAccess(target, address, requestedCycle, isWrite, size);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private AmigaBusAccessResult ArbitrateChipSlot(AmigaBusAccessRequest request, AmigaBusAccessResult baseResult)
        {
            _hrmSlotEngine.BlitterPriorityEnabled = (Paula.Dmacon & 0x0400) != 0;
            if (request.Requester == AmigaBusRequester.Cpu)
            {
                PrepareLiveDisplayBeforeCpuHrmGrantUntilStable(
                    request.Target,
                    request.Address,
                    request.Size,
                    request.IsWrite,
                    request.Kind,
                    Math.Max(baseResult.GrantedCycle, request.RequestedCycle));
            }
            else if (LiveAgnusDmaEnabled &&
                (request.Size == AmigaBusAccessSize.Word || request.Size == AmigaBusAccessSize.Long) &&
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

            var result = _hrmSlotEngine.Arbitrate(request, baseResult);
            if (CopperQuiescentShadowPredictionEnabled &&
                request.Requester == AmigaBusRequester.Cpu)
            {
                _hardwareScheduler.RecordCopperQuiescentCpuSlotPrediction(
                    request.Kind,
                    request.Target,
                    request.Address,
                    request.Size,
                    request.RequestedCycle,
                    result.GrantedCycle,
                    result.CompletedCycle,
                    request.IsWrite);
            }

            return result;
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

        internal bool IsFixedDmaSlotReservedAfterPreparingLiveDisplay(long cycle)
        {
            if (_hrmSlotEngine.IsFixedDmaReserved(cycle))
            {
                return true;
            }

            if (!LiveAgnusDmaEnabled ||
                !Display.HasLiveDisplayWork() ||
                !Display.HasLiveDisplaySlotPreparationWorkBeforeHrmGrant(cycle))
            {
                return false;
            }

            Display.PrepareLiveDisplaySlotsBeforeHrmGrant(cycle);
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
                var kind = ClassifyCpuBusBank(bankAddress, out var offset);
                _cpuBusBankKinds[bank] = kind;
                _cpuBusBankOffsets[bank] = offset;
                _jitDirectRamBankKinds[bank] = kind switch
                {
                    CpuBusBankKind.ExpansionRam => (byte)M68kJitDirectRamBankKind.PseudoFast,
                    CpuBusBankKind.RealFastRam => (byte)M68kJitDirectRamBankKind.RealFast,
                    _ => (byte)M68kJitDirectRamBankKind.None
                };
            }
        }

        bool IM68kJitDirectRamBus.TryGetJitDirectRamMap(out M68kJitDirectRamMap map)
        {
            map = new M68kJitDirectRamMap(
                _jitDirectRamBankKinds,
                _cpuBusBankOffsets,
                _expansionRam.Data,
                _realFastRam.Data,
                CpuBusBankShift);
            return _expansionRam.Length != 0 || _realFastRam.Length != 0;
        }

        void IM68kJitDirectRamBus.ReplayJitPseudoFastAccesses(
            ref long cycle,
            int accessCount,
            ulong longAccessBits)
        {
            for (var index = 0; index < accessCount; index++)
            {
                var size = ((longAccessBits >> index) & 1UL) != 0
                    ? AmigaBusAccessSize.Long
                    : AmigaBusAccessSize.Word;
                CommitExactCpuExpansionDataTiming(
                    ExpansionRamBase,
                    size,
                    ref cycle,
                    isWrite: false,
                    AmigaBusAccessKind.CpuDataRead,
                    OcsLiveDmaScratchCpuWrite.None,
                    out _,
                    out _);
            }
        }

        void IM68kJitDirectRamBus.CompleteJitDirectRamWrite(uint physicalAddress, int byteCount)
            => CompleteJitZeroWaitWrite(physicalAddress, byteCount);

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
            if (_hostTrapStubAddresses.Count == 0)
            {
                return false;
            }

            var bankEndExclusive = bankAddress + CpuBusBankSize;
            var firstPossibleTrapStart = bankAddress >= 3u ? bankAddress - 3u : 0u;
            var index = _hostTrapStubAddresses.BinarySearch(firstPossibleTrapStart);
            if (index < 0)
            {
                index = ~index;
            }

            return index < _hostTrapStubAddresses.Count &&
                _hostTrapStubAddresses[index] < bankEndExclusive;
        }

        private static bool RangesOverlap(uint start, uint endExclusive, uint otherStart, uint otherEndExclusive)
            => start < otherEndExclusive && otherStart < endExclusive;

        private AmigaBusAccessTarget ClassifyTarget(uint address)
        {
            if (address > CpuAddressMask)
            {
                return ClassifyTargetDetailed(address);
            }

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

        private AmigaBusAccessTarget ClassifyInstructionFetchTarget(uint address)
        {
            if (address <= CpuAddressMask)
            {
                var bankKind = _cpuBusBankKinds[(int)(address >> CpuBusBankShift)];
                if (bankKind != CpuBusBankKind.Special)
                {
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
            }

            if (TryReadHostTrapStubByte(address, out _))
            {
                return AmigaBusAccessTarget.HostTrap;
            }

            return ClassifyTarget(address);
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

            return 0;
        }

        private bool IsCpuPhysicalByteMapped(uint address, AmigaBusAccessKind accessKind)
        {
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

        private byte ReadCiaRegisterValue(AmigaCia cia, int ciaRegister, long? sampleCycle = null)
        {
            if (cia == CiaA && ciaRegister == 0)
            {
                var inputPins = sampleCycle.HasValue
                    ? Disk.ReadCiaAPortA(0xFF, sampleCycle.Value)
                    : Disk.ReadCiaAPortA(0xFF);
                inputPins = ApplyGamePortFireBits(inputPins);
                return cia.ReadPortRegister(ciaRegister, inputPins);
            }

            return cia.ReadRegister(ciaRegister);
        }

        private bool TryReadHostTrapStubByte(uint address, out byte value)
        {
            if (!HostTrapStubPageHasTrap(address))
            {
                value = 0;
                return false;
            }

            for (var offset = 0u; offset < 4; offset++)
            {
                var baseAddress = address - offset;
                if (!_hostTrapStubs.TryGetValue(baseAddress, out var entry) ||
                    entry.Address + offset != address)
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

        private void MarkHostTrapStubPages(uint address)
        {
            if (address > CpuAddressMask || CpuAddressMask - address < 3)
            {
                return;
            }

            for (var offset = 0u; offset < 4; offset++)
            {
                _hostTrapStubPages[GetHostTrapStubPageIndex(address + offset)] = true;
            }
        }

        private void AddHostTrapStubAddress(uint address)
        {
            var index = _hostTrapStubAddresses.BinarySearch(address);
            if (index >= 0)
            {
                return;
            }

            _hostTrapStubAddresses.Insert(~index, address);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HostTrapStubPageHasTrap(uint address)
            => address > CpuAddressMask || _hostTrapStubPages[GetHostTrapStubPageIndex(address)];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetHostTrapStubPageIndex(uint address)
            => (int)((address & CpuAddressMask) >> CodeGenerationPageShift);

        private bool TryReadRelocatableHostTrapId(uint address, out ushort trapId)
        {
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

            return (ushort)((ReadRawByte(address) << 8) | ReadRawByte(address + 1));
        }

        private ushort ReadRawWord(uint address, long sampleCycle)
        {
            // Hot path: chip RAM (most common case).
            if (address < _chipRam.DecodeSize && !_romOverlayEnabled)
            {
                if (sampleCycle != long.MinValue)
                {
                    return ReadChipWordForPresentation(address, sampleCycle);
                }

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
                if (sampleCycle != long.MinValue)
                {
                    return ReadChipWordForPresentation(address, sampleCycle);
                }

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

            return (ushort)((ReadRawByte(address, sampleCycle) << 8) |
                ReadRawByte(address + 1, sampleCycle));
        }

        private static bool IsCustomRegisterWordAddress(uint address)
        {
            var offset = GetCustomRegisterOffset(address);
            return offset < 0x1FF && (offset & 1) == 0;
        }

        private static bool IsCustomRegisterByteAddress(uint address)
        {
            return GetCustomRegisterOffset(address) < 0x200;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint GetCustomRegisterOffset(uint address)
        {
            return (address & CpuAddressMask) - CustomRegisterBaseAddress;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGetCustomRegisterByteOffset(uint address, out ushort offset)
        {
            // A byte write may address the final odd byte; WriteCustomByte clears A0
            // before dispatching the effective 16-bit custom register write.
            var registerOffset = GetCustomRegisterOffset(address);
            if (registerOffset < 0x200)
            {
                offset = (ushort)registerOffset;
                return true;
            }

            offset = 0;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGetCustomRegisterWordOffset(uint address, out ushort offset)
        {
            var registerOffset = GetCustomRegisterOffset(address);
            if (registerOffset < 0x1FF && (registerOffset & 1) == 0)
            {
                offset = (ushort)registerOffset;
                return true;
            }

            offset = 0;
            return false;
        }

        internal bool TryReadJitZeroWaitMemory(uint address, M68kOperandSize size, out uint value)
        {
            value = 0;
            if (!_useChipSlotScheduler || (size != M68kOperandSize.Byte && (address & 1) != 0))
            {
                return false;
            }

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
                var lastAddress = address + (uint)(byteCount - 1);
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

            if (address < 0x00DFF180 || address > 0x00DFF1BE || (address & 1) != 0)
            {
                return false;
            }

            WriteCustomSpaceWord(default(CpuWritePolicy), address, (ushort)value, cycle);
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
                (physicalAddress & CpuAddressMask) != 0x00BF_E001u)
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
                var address = root + (uint)i;
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
                var address = root + (uint)offset;
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

        private int GetJitSnapshotPageCount(uint root, int byteCount)
        {
            var startPage = root >> CodeGenerationPageShift;
            var endPage = (root + (uint)Math.Max(0, byteCount - 1)) >> CodeGenerationPageShift;
            if (endPage < startPage)
            {
                return 1;
            }

            return checked((int)(endPage - startPage + 1));
        }

        private void FillJitSnapshotGenerations(uint root, int byteCount, uint[] pages, uint[] generations)
        {
            var startPage = root >> CodeGenerationPageShift;
            for (var i = 0; i < pages.Length; i++)
            {
                var pageAddress = (startPage + (uint)i) << CodeGenerationPageShift;
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
            WriteJitCpuDataTarget(target, address, value, size, grantedCycle, secondWordCycle);

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

        private uint ReadRawLong(uint address, long firstWordCycle, long secondWordCycle)
        {
            return ((uint)ReadRawWord(address, firstWordCycle) << 16) | ReadRawWord(address + 2, secondWordCycle);
        }

        private void WriteCpuByteFast(
            AmigaBusAccessTarget target,
            uint address,
            byte value,
            ref long cycle,
            AmigaBusAccessKind accessKind,
            long requestedCycle)
        {
            switch (target)
            {
                case AmigaBusAccessTarget.CustomRegisters:
                    WriteCpuByteFast<CustomRegisterCpuWriteTarget>(target, address, value, ref cycle, accessKind, requestedCycle);
                    break;
                case AmigaBusAccessTarget.ChipRam:
                    WriteCpuByteFast<ChipRamCpuWriteTarget>(target, address, value, ref cycle, accessKind, requestedCycle);
                    break;
                case AmigaBusAccessTarget.ExpansionRam:
                    WriteCpuByteFast<ExpansionRamCpuWriteTarget>(target, address, value, ref cycle, accessKind, requestedCycle);
                    break;
                case AmigaBusAccessTarget.RealFastRam:
                    WriteCpuByteFast<RealFastRamCpuWriteTarget>(target, address, value, ref cycle, accessKind, requestedCycle);
                    break;
                case AmigaBusAccessTarget.Cia:
                    WriteCpuByteFast<CiaCpuWriteTarget>(target, address, value, ref cycle, accessKind, requestedCycle);
                    break;
                default:
                    WriteCpuByteFast<FallbackCpuWriteTarget>(target, address, value, ref cycle, accessKind, requestedCycle);
                    break;
            }
        }

        private void WriteCpuByteArbitrated(
            AmigaBusAccessTarget target,
            uint address,
            byte value,
            ref long cycle,
            AmigaBusAccessKind accessKind,
            long requestedCycle)
        {
            var access = ArbitrateCpuWriteAndAdvance(target, address, AmigaBusAccessSize.Byte, cycle, accessKind, requestedCycle);
            switch (target)
            {
                case AmigaBusAccessTarget.CustomRegisters:
                    WriteCpuByteArbitrated<CustomRegisterCpuWriteTarget>(address, value, access.GrantedCycle);
                    break;
                case AmigaBusAccessTarget.ChipRam:
                    WriteCpuByteArbitrated<ChipRamCpuWriteTarget>(address, value, access.GrantedCycle);
                    break;
                case AmigaBusAccessTarget.ExpansionRam:
                    WriteCpuByteArbitrated<ExpansionRamCpuWriteTarget>(address, value, access.GrantedCycle);
                    break;
                case AmigaBusAccessTarget.RealFastRam:
                    WriteCpuByteArbitrated<RealFastRamCpuWriteTarget>(address, value, access.GrantedCycle);
                    break;
                case AmigaBusAccessTarget.Cia:
                    WriteCpuByteArbitrated<CiaCpuWriteTarget>(address, value, access.GrantedCycle);
                    break;
                default:
                    WriteCpuByteArbitrated<FallbackCpuWriteTarget>(address, value, access.GrantedCycle);
                    break;
            }

            cycle = access.CompletedCycle;
        }

        private void WriteCpuWordFast(
            AmigaBusAccessTarget target,
            uint address,
            ushort value,
            ref long cycle,
            AmigaBusAccessKind accessKind,
            long requestedCycle)
        {
            switch (target)
            {
                case AmigaBusAccessTarget.CustomRegisters:
                    WriteCpuWordFast<CustomRegisterCpuWriteTarget>(target, address, value, ref cycle, accessKind, requestedCycle);
                    break;
                case AmigaBusAccessTarget.ChipRam:
                    WriteCpuWordFast<ChipRamCpuWriteTarget>(target, address, value, ref cycle, accessKind, requestedCycle);
                    break;
                case AmigaBusAccessTarget.ExpansionRam:
                    WriteCpuWordFast<ExpansionRamCpuWriteTarget>(target, address, value, ref cycle, accessKind, requestedCycle);
                    break;
                case AmigaBusAccessTarget.RealFastRam:
                    WriteCpuWordFast<RealFastRamCpuWriteTarget>(target, address, value, ref cycle, accessKind, requestedCycle);
                    break;
                default:
                    WriteCpuWordFast<FallbackCpuWriteTarget>(target, address, value, ref cycle, accessKind, requestedCycle);
                    break;
            }
        }

        private void WriteCpuWordArbitrated(
            AmigaBusAccessTarget target,
            uint address,
            ushort value,
            ref long cycle,
            AmigaBusAccessKind accessKind,
            long requestedCycle)
        {
            var access = ArbitrateCpuWriteAndAdvance(target, address, AmigaBusAccessSize.Word, cycle, accessKind, requestedCycle);
            switch (target)
            {
                case AmigaBusAccessTarget.CustomRegisters:
                    WriteCpuWordArbitrated<CustomRegisterCpuWriteTarget>(address, value, access.GrantedCycle);
                    break;
                case AmigaBusAccessTarget.ChipRam:
                    WriteCpuWordArbitrated<ChipRamCpuWriteTarget>(address, value, access.GrantedCycle);
                    break;
                case AmigaBusAccessTarget.ExpansionRam:
                    WriteCpuWordArbitrated<ExpansionRamCpuWriteTarget>(address, value, access.GrantedCycle);
                    break;
                case AmigaBusAccessTarget.RealFastRam:
                    WriteCpuWordArbitrated<RealFastRamCpuWriteTarget>(address, value, access.GrantedCycle);
                    break;
                default:
                    WriteCpuWordArbitrated<FallbackCpuWriteTarget>(address, value, access.GrantedCycle);
                    break;
            }

            cycle = access.CompletedCycle;
        }

        private void WriteCpuLongFast(
            AmigaBusAccessTarget target,
            uint address,
            uint value,
            ref long cycle,
            AmigaBusAccessKind accessKind,
            long requestedCycle)
        {
            switch (target)
            {
                case AmigaBusAccessTarget.CustomRegisters:
                    WriteCpuCustomRegisterLongSplit(address, value, ref cycle, accessKind, requestedCycle);
                    break;
                case AmigaBusAccessTarget.ChipRam:
                    WriteCpuLongFast<ChipRamCpuWriteTarget>(target, address, value, ref cycle, accessKind, requestedCycle);
                    break;
                case AmigaBusAccessTarget.ExpansionRam:
                    WriteCpuLongFast<ExpansionRamCpuWriteTarget>(target, address, value, ref cycle, accessKind, requestedCycle);
                    break;
                case AmigaBusAccessTarget.RealFastRam:
                    WriteCpuLongFast<RealFastRamCpuWriteTarget>(target, address, value, ref cycle, accessKind, requestedCycle);
                    break;
                default:
                    WriteCpuLongFast<FallbackCpuWriteTarget>(target, address, value, ref cycle, accessKind, requestedCycle);
                    break;
            }
        }

        private void WriteCpuLongArbitrated(
            AmigaBusAccessTarget target,
            uint address,
            uint value,
            ref long cycle,
            AmigaBusAccessKind accessKind,
            long requestedCycle)
        {
            if (target == AmigaBusAccessTarget.CustomRegisters)
            {
                WriteCpuCustomRegisterLongSplit(address, value, ref cycle, accessKind, requestedCycle);
                return;
            }

            var access = ArbitrateCpuWriteAndAdvance(target, address, AmigaBusAccessSize.Long, cycle, accessKind, requestedCycle);
            var secondWordCycle = GetSecondWordCycle(access);
            switch (target)
            {
                case AmigaBusAccessTarget.ChipRam:
                    WriteCpuLongArbitrated<ChipRamCpuWriteTarget>(address, value, access.GrantedCycle, secondWordCycle);
                    break;
                case AmigaBusAccessTarget.ExpansionRam:
                    WriteCpuLongArbitrated<ExpansionRamCpuWriteTarget>(address, value, access.GrantedCycle, secondWordCycle);
                    break;
                case AmigaBusAccessTarget.RealFastRam:
                    WriteCpuLongArbitrated<RealFastRamCpuWriteTarget>(address, value, access.GrantedCycle, secondWordCycle);
                    break;
                default:
                    WriteCpuLongArbitrated<FallbackCpuWriteTarget>(address, value, access.GrantedCycle, secondWordCycle);
                    break;
            }

            cycle = access.CompletedCycle;
        }

        private void WriteCpuByteFast<TTarget>(
            AmigaBusAccessTarget target,
            uint address,
            byte value,
            ref long cycle,
            AmigaBusAccessKind accessKind,
            long requestedCycle)
            where TTarget : struct, ICpuWriteTarget
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
            default(TTarget).WriteByte(this, address, value, grantedCycle);
            cycle = completedCycle;
        }

        private void WriteCpuByteArbitrated<TTarget>(
            uint address,
            byte value,
            long grantedCycle)
            where TTarget : struct, ICpuWriteTarget
        {
            default(TTarget).WriteByte(this, address, value, grantedCycle);
        }

        private void WriteCpuWordFast<TTarget>(
            AmigaBusAccessTarget target,
            uint address,
            ushort value,
            ref long cycle,
            AmigaBusAccessKind accessKind,
            long requestedCycle)
            where TTarget : struct, ICpuWriteTarget
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
            default(TTarget).WriteWord(this, address, value, grantedCycle);
            cycle = completedCycle;
        }

        private void WriteCpuWordArbitrated<TTarget>(
            uint address,
            ushort value,
            long grantedCycle)
            where TTarget : struct, ICpuWriteTarget
        {
            default(TTarget).WriteWord(this, address, value, grantedCycle);
        }

        private void WriteCpuLongFast<TTarget>(
            AmigaBusAccessTarget target,
            uint address,
            uint value,
            ref long cycle,
            AmigaBusAccessKind accessKind,
            long requestedCycle)
            where TTarget : struct, ICpuWriteTarget
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
            default(TTarget).WriteLong(this, address, value, grantedCycle, secondWordCycle);
            cycle = completedCycle;
        }

        private void WriteCpuLongArbitrated<TTarget>(
            uint address,
            uint value,
            long firstWordCycle,
            long secondWordCycle)
            where TTarget : struct, ICpuWriteTarget
        {
            default(TTarget).WriteLong(this, address, value, firstWordCycle, secondWordCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private AmigaBusAccessResult ArbitrateCpuWriteAndAdvance(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long cycle,
            AmigaBusAccessKind accessKind,
            long requestedCycle)
        {
            var access = Arbitrate(AmigaBusRequester.Cpu, accessKind, target, address, size, cycle, isWrite: true);
            AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, access.GrantedCycle, isWrite: true);
            return access;
        }

        private void WriteJitCpuDataTarget(
            AmigaBusAccessTarget target,
            uint address,
            uint value,
            M68kOperandSize size,
            long grantedCycle,
            long secondWordCycle)
        {
            switch (target)
            {
                case AmigaBusAccessTarget.CustomRegisters:
                    WriteJitCpuDataTarget<CustomRegisterCpuWriteTarget>(address, value, size, grantedCycle, secondWordCycle);
                    break;
                case AmigaBusAccessTarget.ChipRam:
                    WriteJitCpuDataTarget<ChipRamCpuWriteTarget>(address, value, size, grantedCycle, secondWordCycle);
                    break;
                case AmigaBusAccessTarget.ExpansionRam:
                    WriteJitCpuDataTarget<ExpansionRamCpuWriteTarget>(address, value, size, grantedCycle, secondWordCycle);
                    break;
                case AmigaBusAccessTarget.RealFastRam:
                    WriteJitCpuDataTarget<RealFastRamCpuWriteTarget>(address, value, size, grantedCycle, secondWordCycle);
                    break;
                case AmigaBusAccessTarget.Cia when size == M68kOperandSize.Byte:
                    WriteJitCpuDataTarget<CiaCpuWriteTarget>(address, value, size, grantedCycle, secondWordCycle);
                    break;
                default:
                    WriteJitCpuDataTarget<FallbackCpuWriteTarget>(address, value, size, grantedCycle, secondWordCycle);
                    break;
            }
        }

        private void WriteJitCpuDataTarget<TTarget>(
            uint address,
            uint value,
            M68kOperandSize size,
            long grantedCycle,
            long secondWordCycle)
            where TTarget : struct, ICpuWriteTarget
        {
            if (size == M68kOperandSize.Byte)
            {
                default(TTarget).WriteByte(this, address, (byte)value, grantedCycle);
                return;
            }

            if (size == M68kOperandSize.Word)
            {
                default(TTarget).WriteWord(this, address, (ushort)value, grantedCycle);
                return;
            }

            default(TTarget).WriteLong(this, address, value, grantedCycle, secondWordCycle);
        }

        private void WriteCpuCiaByte(uint address, byte value, long grantedCycle)
        {
            if (!TryGetCiaRegister(address, out var cia, out var ciaRegister))
            {
                WriteRawByte(address, value, grantedCycle, default(CpuWritePolicy));
                return;
            }

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
        }

        private void WriteCpuChipRamByte(uint address, byte value, long grantedCycle)
        {
            if (!TryGetChipRamOffset(address, out var chipOffset))
            {
                WriteRawByte(address, value, grantedCycle, default(CpuWritePolicy));
                return;
            }

            _chipRam.WriteByteAtOffset(chipOffset, value, grantedCycle);
            RememberCpuChipRamWrite(address, value, M68kOperandSize.Byte, grantedCycle);
            TouchCodePage(address);
        }

        private void WriteCpuChipRamWord(uint address, ushort value, long grantedCycle)
        {
            if (!TryGetChipRamOffset(address, out var chipOffset))
            {
                WriteRawWord(address, value, grantedCycle, default(CpuWritePolicy));
                return;
            }

            _chipRam.WriteWordAtOffset(chipOffset, value, grantedCycle);
            RememberCpuChipRamWrite(address, value, M68kOperandSize.Word, grantedCycle);
            TouchCodePage(address);
            TouchCodePage(address + 1);
        }

        private void WriteCpuChipRamLong(uint address, uint value, long firstWordCycle, long secondWordCycle)
        {
            WriteCpuChipRamWord(address, (ushort)(value >> 16), firstWordCycle);
            WriteCpuChipRamWord(address + 2, (ushort)value, secondWordCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RememberCpuChipRamWrite(uint address, uint value, M68kOperandSize size, long grantedCycle)
        {
            var trace = _cpuChipRamWriteTrace;
            if (trace == null)
            {
                return;
            }

            var byteCount = size == M68kOperandSize.Byte ? 1u : size == M68kOperandSize.Word ? 2u : 4u;
            if (address + byteCount <= _cpuChipRamWriteTraceStart ||
                address >= _cpuChipRamWriteTraceEndExclusive)
            {
                return;
            }

            trace.Add(new CpuChipRamWriteTrace(address, value, size, grantedCycle));
        }

        private void WriteCpuExpansionRamByte(uint address, byte value, long grantedCycle)
        {
            if (!TryGetExpansionRamOffset(address, out var expansionOffset))
            {
                WriteRawByte(address, value, grantedCycle, default(CpuWritePolicy));
                return;
            }

            _expansionRam[expansionOffset] = value;
            TouchCodePage(address);
            NotifyJitEligibleMemoryWritten(address, 1);
        }

        private void WriteCpuExpansionRamWord(uint address, ushort value, long grantedCycle)
        {
            if (!TryGetExpansionRamOffset(address, out var expansionOffset) ||
                expansionOffset + 1 >= _expansionRam.Length)
            {
                WriteRawWord(address, value, grantedCycle, default(CpuWritePolicy));
                return;
            }

            _expansionRam[expansionOffset] = (byte)(value >> 8);
            _expansionRam[expansionOffset + 1] = (byte)value;
            TouchCodePage(address);
            TouchCodePage(address + 1);
            NotifyJitEligibleMemoryWritten(address, 2);
        }

        private void WriteCpuExpansionRamLong(uint address, uint value, long firstWordCycle, long secondWordCycle)
        {
            WriteCpuExpansionRamWord(address, (ushort)(value >> 16), firstWordCycle);
            WriteCpuExpansionRamWord(address + 2, (ushort)value, secondWordCycle);
        }

        private void WriteCpuRealFastRamByte(uint address, byte value, long grantedCycle)
        {
            if (!TryGetRealFastRamOffset(address, out var realFastOffset))
            {
                WriteRawByte(address, value, grantedCycle, default(CpuWritePolicy));
                return;
            }

            _realFastRam[realFastOffset] = value;
            TouchCodePage(address);
            NotifyJitEligibleMemoryWritten(address, 1);
        }

        private void WriteCpuRealFastRamWord(uint address, ushort value, long grantedCycle)
        {
            if (!TryGetRealFastRamOffset(address, out var realFastOffset) ||
                realFastOffset + 1 >= _realFastRam.Length)
            {
                WriteRawWord(address, value, grantedCycle, default(CpuWritePolicy));
                return;
            }

            _realFastRam[realFastOffset] = (byte)(value >> 8);
            _realFastRam[realFastOffset + 1] = (byte)value;
            TouchCodePage(address);
            TouchCodePage(address + 1);
            NotifyJitEligibleMemoryWritten(address, 2);
        }

        private void WriteCpuRealFastRamLong(uint address, uint value, long firstWordCycle, long secondWordCycle)
        {
            WriteCpuRealFastRamWord(address, (ushort)(value >> 16), firstWordCycle);
            WriteCpuRealFastRamWord(address + 2, (ushort)value, secondWordCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteRawByte(uint address, byte value, long grantedCycle)
            => WriteRawByte(address, value, grantedCycle, default(CpuWritePolicy));

        private void WriteRawByte(uint address, byte value, long grantedCycle, AmigaBusRequester requester)
        {
            if (requester == AmigaBusRequester.Cpu)
            {
                WriteRawByte(address, value, grantedCycle, default(CpuWritePolicy));
                return;
            }

            if (requester == AmigaBusRequester.Host)
            {
                WriteRawByte(address, value, grantedCycle, default(HostWritePolicy));
                return;
            }

            WriteRawByte(address, value, grantedCycle, new RequesterWritePolicy(requester));
        }

        private void WriteRawByte<TPolicy>(
            uint address,
            byte value,
            long grantedCycle,
            TPolicy policy)
            where TPolicy : struct, IBusWritePolicy
        {
            if (TryGetChipRamOffset(address, out var chipOffset))
            {
                _chipRam.WriteByteAtOffset(chipOffset, value, grantedCycle);
                if (policy.Requester == AmigaBusRequester.Cpu)
                {
                    RememberCpuChipRamWrite(address, value, M68kOperandSize.Byte, grantedCycle);
                }

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
                WriteCustomByte(policy.Requester, (ushort)(address - 0x00DFF000), value, grantedCycle);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteRawWord(uint address, ushort value, long grantedCycle)
            => WriteRawWord(address, value, grantedCycle, default(CpuWritePolicy));

        private void WriteRawWord(uint address, ushort value, long grantedCycle, AmigaBusRequester requester)
        {
            if (requester == AmigaBusRequester.Cpu)
            {
                WriteRawWord(address, value, grantedCycle, default(CpuWritePolicy));
                return;
            }

            if (requester == AmigaBusRequester.Host)
            {
                WriteRawWord(address, value, grantedCycle, default(HostWritePolicy));
                return;
            }

            WriteRawWord(address, value, grantedCycle, new RequesterWritePolicy(requester));
        }

        private void WriteRawWord<TPolicy>(
            uint address,
            ushort value,
            long grantedCycle,
            TPolicy policy)
            where TPolicy : struct, IBusWritePolicy
        {
            if (address >= 0x00DFF000 && address + 1 < 0x00DFF200)
            {
                WriteCustomWord(policy.Requester, (ushort)(address - 0x00DFF000), value, grantedCycle);
                return;
            }

            if (TryGetChipRamOffset(address, out var chipOffset))
            {
                _chipRam.WriteWordAtOffset(chipOffset, value, grantedCycle);
                if (policy.Requester == AmigaBusRequester.Cpu)
                {
                    RememberCpuChipRamWrite(address, value, M68kOperandSize.Word, grantedCycle);
                }

                TouchCodePage(address);
                TouchCodePage(address + 1);
                return;
            }

            if (_realTimeClock != null &&
                (AmigaRealTimeClock.ContainsAddress(address) || AmigaRealTimeClock.ContainsAddress(address + 1)))
            {
                WriteRawByte(address, (byte)(value >> 8), grantedCycle, policy);
                WriteRawByte(address + 1, (byte)value, grantedCycle, policy);
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

            WriteRawByte(address, (byte)(value >> 8), grantedCycle, policy);
            WriteRawByte(address + 1, (byte)value, grantedCycle, policy);
        }

        private void WriteCustomSpaceWord<TPolicy>(
            TPolicy policy,
            uint address,
            ushort value,
            long grantedCycle)
            where TPolicy : struct, IBusWritePolicy
        {
            if (TryGetCustomRegisterWordOffset(address, out var offset))
            {
                WriteCustomWord(policy.Requester, offset, value, grantedCycle);
                return;
            }

            if (TryGetCustomRegisterByteOffset(address, out offset))
            {
                WriteCustomByte(policy.Requester, offset, (byte)(value >> 8), grantedCycle);
            }

            WriteRawByte(address + 1, (byte)value, grantedCycle, policy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteRawLong(uint address, uint value, long firstWordCycle, long secondWordCycle)
            => WriteRawLong(address, value, firstWordCycle, secondWordCycle, default(CpuWritePolicy));

        private void WriteRawLong(uint address, uint value, long firstWordCycle, long secondWordCycle, AmigaBusRequester requester)
        {
            if (requester == AmigaBusRequester.Cpu)
            {
                WriteRawLong(address, value, firstWordCycle, secondWordCycle, default(CpuWritePolicy));
                return;
            }

            if (requester == AmigaBusRequester.Host)
            {
                WriteRawLong(address, value, firstWordCycle, secondWordCycle, default(HostWritePolicy));
                return;
            }

            WriteRawLong(address, value, firstWordCycle, secondWordCycle, new RequesterWritePolicy(requester));
        }

        private void WriteRawLong<TPolicy>(
            uint address,
            uint value,
            long firstWordCycle,
            long secondWordCycle,
            TPolicy policy)
            where TPolicy : struct, IBusWritePolicy
        {
            WriteRawWord(address, (ushort)(value >> 16), firstWordCycle, policy);
            WriteRawWord(address + 2, (ushort)value, secondWordCycle, policy);
        }

        private void NotifyJitEligibleMemoryWritten(uint address, int byteCount)
        {
            var handler = JitEligibleMemoryWritten;
            if (handler == null || byteCount <= 0)
            {
                return;
            }

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

        private static bool IsPowerOfTwo(int value)
        {
            return value > 0 && (value & (value - 1)) == 0;
        }

        internal uint GetCodePageGeneration(uint address)
        {
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

            var endAddress = address + (uint)(byteCount - 1);
            return GetCodePageGeneration(address) == startGeneration &&
                GetCodePageGeneration(endAddress) == endGeneration;
        }

        private void TouchCodePages(uint address, int byteCount)
        {
            if (byteCount <= 0)
            {
                return;
            }

            var remaining = byteCount;
            var current = address;
            while (remaining > 0)
            {
                TouchCodePage(current);
                var bytesToNextPage = CodeGenerationPageSize - ((int)current & (CodeGenerationPageSize - 1));
                var step = Math.Min(remaining, bytesToNextPage);
                remaining -= step;
                current += (uint)step;
            }
        }

        private void TouchCodePage(uint address)
        {
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
