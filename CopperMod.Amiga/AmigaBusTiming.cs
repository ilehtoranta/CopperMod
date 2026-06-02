using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CopperMod.Amiga
{
    internal enum AmigaBusRequester
    {
        Cpu,
        Paula,
        Cia,
        Disk,
        Copper,
        Blitter,
        Bitplane,
        Sprite,
        Host
    }

    internal enum AmigaBusAccessKind
    {
        CpuInstructionFetch,
        CpuDataRead,
        CpuDataWrite,
        CustomRegister,
        Cia,
        Rom,
        HostTrap,
        PaulaDma,
        DiskDma,
        Copper,
        Blitter,
        Bitplane,
        Sprite
    }

    internal enum AmigaBusAccessTarget
    {
        ChipRam,
        ExpansionRam,
        RealFastRam,
        CustomRegisters,
        Cia,
        Rom,
        HostTrap,
        Unmapped
    }

    internal enum AmigaBusAccessSize
    {
        Byte = 1,
        Word = 2,
        Long = 4
    }

    internal enum AgnusChipSlotPriority
    {
        Cpu = 0,
        Blitter = 1,
        Copper = 2,
        Paula = 3,
        Disk = 4,
        Sprite = 5,
        Bitplane = 6
    }

    internal enum AgnusChipSlotOwner
    {
        Free,
        Cpu,
        Blitter,
        Copper,
        Paula,
        Disk,
        Sprite,
        Bitplane,
        Host
    }

    internal enum AgnusTimingMode
    {
        LegacyReservation,
        SlotEngine,
        ShadowCompare
    }

    internal readonly struct AgnusChipSlotSnapshot
    {
        public AgnusChipSlotSnapshot(
            AgnusChipSlotOwner owner,
            AmigaBusAccessKind kind,
            uint address,
            long requestedCycle,
            long grantedCycle,
            bool denied)
        {
            Owner = owner;
            Kind = kind;
            Address = address;
            RequestedCycle = requestedCycle;
            GrantedCycle = grantedCycle;
            Denied = denied;
        }

        public AgnusChipSlotOwner Owner { get; }

        public AmigaBusAccessKind Kind { get; }

        public uint Address { get; }

        public long RequestedCycle { get; }

        public long GrantedCycle { get; }

        public bool Denied { get; }
    }

    internal readonly struct AgnusSlotDivergenceSnapshot
    {
        public AgnusSlotDivergenceSnapshot(
            AmigaBusAccessRequest request,
            bool primaryGranted,
            bool shadowGranted,
            long primaryGrantedCycle,
            long shadowGrantedCycle,
            long primaryCompletedCycle,
            long shadowCompletedCycle,
            AgnusSlotDivergenceKind kind = AgnusSlotDivergenceKind.Timing,
            bool hasValueComparison = false,
            ushort primaryValue = 0,
            ushort shadowValue = 0)
        {
            Request = request;
            PrimaryGranted = primaryGranted;
            ShadowGranted = shadowGranted;
            PrimaryGrantedCycle = primaryGrantedCycle;
            ShadowGrantedCycle = shadowGrantedCycle;
            PrimaryCompletedCycle = primaryCompletedCycle;
            ShadowCompletedCycle = shadowCompletedCycle;
            Kind = kind;
            HasValueComparison = hasValueComparison;
            PrimaryValue = primaryValue;
            ShadowValue = shadowValue;
        }

        public AmigaBusAccessRequest Request { get; }

        public AgnusSlotDivergenceKind Kind { get; }

        public bool PrimaryGranted { get; }

        public bool ShadowGranted { get; }

        public long PrimaryGrantedCycle { get; }

        public long ShadowGrantedCycle { get; }

        public long PrimaryCompletedCycle { get; }

        public long ShadowCompletedCycle { get; }

        public bool HasValueComparison { get; }

        public ushort PrimaryValue { get; }

        public ushort ShadowValue { get; }
    }

    internal enum AgnusSlotDivergenceKind
    {
        Timing,
        Grant,
        Data
    }

    internal readonly struct AmigaBusAccessRequest
    {
        public AmigaBusAccessRequest(
            AmigaBusRequester requester,
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite)
        {
            Requester = requester;
            Kind = kind;
            Target = target;
            Address = address;
            Size = size;
            RequestedCycle = requestedCycle;
            IsWrite = isWrite;
        }

        public AmigaBusRequester Requester { get; }

        public AmigaBusAccessKind Kind { get; }

        public AmigaBusAccessTarget Target { get; }

        public uint Address { get; }

        public AmigaBusAccessSize Size { get; }

        public long RequestedCycle { get; }

        public bool IsWrite { get; }
    }

    internal readonly struct AmigaBusAccessResult
    {
        public AmigaBusAccessResult(AmigaBusAccessRequest request, long grantedCycle, long completedCycle)
        {
            if (grantedCycle < request.RequestedCycle)
            {
                throw new ArgumentOutOfRangeException(nameof(grantedCycle), grantedCycle, "Bus grant cannot happen before the requested cycle.");
            }

            if (completedCycle < grantedCycle)
            {
                throw new ArgumentOutOfRangeException(nameof(completedCycle), completedCycle, "Bus completion cannot happen before the granted cycle.");
            }

            Request = request;
            GrantedCycle = grantedCycle;
            CompletedCycle = completedCycle;
        }

        public AmigaBusAccessRequest Request { get; }

        public long RequestedCycle => Request.RequestedCycle;

        public long GrantedCycle { get; }

        public long CompletedCycle { get; }

        public long WaitCycles => GrantedCycle - RequestedCycle;

        public long AccessCycles => CompletedCycle - GrantedCycle;
    }

    internal interface IAmigaBusArbiter
    {
        AmigaBusAccessResult Arbitrate(AmigaBusAccessRequest request);
    }

    internal interface IAgnusChipSlotTiming
    {
        void Clear();

        void AdvanceTo(long targetCycle);

        AmigaBusAccessResult Arbitrate(AmigaBusAccessRequest request, AmigaBusAccessResult baseResult);

        bool TryReserveFixedDmaSlot(AmigaBusAccessRequest request, out AmigaBusAccessResult result);

        AmigaBusAccessResult ReserveBitplaneDmaSlot(uint address, long requestedCycle);

        AmigaBusAccessResult ReserveCopperDmaSlot(uint address, long requestedCycle);

        bool IsReserved(long cycle);

        AmigaBusAccessResult? LastReservation { get; }

        AgnusChipSlotSnapshot? LastGrantedSlot { get; }

        AgnusChipSlotSnapshot? LastDeniedFixedSlot { get; }

        AgnusChipSlotSnapshot? LastDeniedFixedSlotBlocker { get; }

        int DeniedFixedSlotCount { get; }

        int GetDeniedFixedSlotCount(AgnusChipSlotOwner owner);

        int GetDeniedFixedSlotBlockerCount(AgnusChipSlotOwner owner);

        int DivergenceCount { get; }

        AgnusSlotDivergenceSnapshot? LastDivergence { get; }

        int ReservationCount { get; }

        int GetReservationCount(AgnusChipSlotOwner owner);

        long SlotGrantCount { get; }

        long GetSlotGrantCount(AgnusChipSlotOwner owner);

        long CurrentCycle { get; }

        long FrameStartCycle { get; }

        int FrameNumber { get; }

        int BeamLine { get; }

        int BeamHorizontal { get; }

        void PruneBefore(long cycle);
    }

    internal readonly struct AgnusSparseChipBusRequest
    {
        public AgnusSparseChipBusRequest(AmigaBusAccessRequest request, bool fixedSlot)
        {
            Request = request;
            FixedSlot = fixedSlot;
        }

        public AmigaBusAccessRequest Request { get; }

        public bool FixedSlot { get; }
    }

    internal interface IAgnusSparseSlotParticipant
    {
        bool TryGetNextChipBusRequest(long currentCycle, long stopCycle, out AgnusSparseChipBusRequest request);

        void CommitChipBusGrant(AgnusSparseChipBusRequest request, AmigaBusAccessResult result);

        void AdvanceInternalTo(long cycle);
    }

    internal sealed class AgnusSparseSlotExecutor : IAgnusChipSlotTiming
    {
        private readonly AmigaBus _bus;
        private readonly AgnusSlotEngine _slotEngine;

        public AgnusSparseSlotExecutor(AmigaBus bus, AgnusSlotEngine slotEngine)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _slotEngine = slotEngine ?? throw new ArgumentNullException(nameof(slotEngine));
        }

        public void Clear()
        {
            _slotEngine.Clear();
        }

        public void AdvanceTo(long targetCycle)
        {
            _slotEngine.AdvanceTo(targetCycle);
        }

        [HotPath]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AmigaBusAccessResult Arbitrate(AmigaBusAccessRequest request, AmigaBusAccessResult baseResult)
        {
            if (IsCpuSlotRequest(request))
            {
                return ArbitrateCpuAccess(request, baseResult);
            }

            if (_bus.LiveAgnusDmaEnabled &&
                request.Size == AmigaBusAccessSize.Word &&
                _bus.Display.HasLiveDisplayWork() &&
                ShouldPrepareDisplayBeforeDeviceGrant(request))
            {
                _bus.Display.CaptureLiveDisplayDmaBeforeSlotEngineGrant(Math.Max(baseResult.GrantedCycle, request.RequestedCycle));
            }

            return _slotEngine.Arbitrate(request, baseResult);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReserveFixedDmaSlot(AmigaBusAccessRequest request, out AmigaBusAccessResult result)
        {
            return _slotEngine.TryReserveFixedDmaSlot(request, out result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AmigaBusAccessResult ReserveBitplaneDmaSlot(uint address, long requestedCycle)
        {
            return _slotEngine.ReserveBitplaneDmaSlot(address, requestedCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AmigaBusAccessResult ReserveCopperDmaSlot(uint address, long requestedCycle)
        {
            if (_bus.LiveAgnusDmaEnabled && _bus.Display.HasLiveDisplayWork())
            {
                _bus.Display.CaptureLiveDisplayDmaBeforeSlotEngineGrant(requestedCycle);
            }

            return _slotEngine.ReserveCopperDmaSlot(address, requestedCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsReserved(long cycle)
        {
            return _slotEngine.IsReserved(cycle);
        }

        public AmigaBusAccessResult? LastReservation => _slotEngine.LastReservation;

        public AgnusChipSlotSnapshot? LastGrantedSlot => _slotEngine.LastGrantedSlot;

        public AgnusChipSlotSnapshot? LastDeniedFixedSlot => _slotEngine.LastDeniedFixedSlot;

        public AgnusChipSlotSnapshot? LastDeniedFixedSlotBlocker => _slotEngine.LastDeniedFixedSlotBlocker;

        public int DeniedFixedSlotCount => _slotEngine.DeniedFixedSlotCount;

        public int GetDeniedFixedSlotCount(AgnusChipSlotOwner owner)
            => _slotEngine.GetDeniedFixedSlotCount(owner);

        public int GetDeniedFixedSlotBlockerCount(AgnusChipSlotOwner owner)
            => _slotEngine.GetDeniedFixedSlotBlockerCount(owner);

        public int DivergenceCount => _slotEngine.DivergenceCount;

        public AgnusSlotDivergenceSnapshot? LastDivergence => _slotEngine.LastDivergence;

        public int ReservationCount => _slotEngine.ReservationCount;

        public int GetReservationCount(AgnusChipSlotOwner owner)
            => _slotEngine.GetReservationCount(owner);

        public long SlotGrantCount => _slotEngine.SlotGrantCount;

        public long GetSlotGrantCount(AgnusChipSlotOwner owner)
            => _slotEngine.GetSlotGrantCount(owner);

        public long CurrentCycle => _slotEngine.CurrentCycle;

        public long FrameStartCycle => _slotEngine.FrameStartCycle;

        public int FrameNumber => _slotEngine.FrameNumber;

        public int BeamLine => _slotEngine.BeamLine;

        public int BeamHorizontal => _slotEngine.BeamHorizontal;

        public void PruneBefore(long cycle)
        {
            _slotEngine.PruneBefore(cycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private AmigaBusAccessResult ArbitrateCpuAccess(AmigaBusAccessRequest request, AmigaBusAccessResult baseResult)
        {
            var slotCount = request.Size == AmigaBusAccessSize.Long ? 2 : 1;
            var candidate = AgnusChipSlotScheduler.AlignToSlot(Math.Max(baseResult.GrantedCycle, request.RequestedCycle));
            var hasLiveDisplayWork = _bus.Display.HasLiveDisplayWork();
            while (true)
            {
                var lastSlot = candidate + ((slotCount - 1) * AgnusChipSlotScheduler.SlotCycles);
                if (hasLiveDisplayWork && !_bus.Display.HasLiveDmaCapturedThrough(lastSlot))
                {
                    _bus.AdvanceDmaTo(lastSlot);
                }

                if (hasLiveDisplayWork)
                {
                    _bus.Display.CaptureLiveDisplayDmaBeforeSlotEngineGrant(candidate);
                    if (slotCount > 1)
                    {
                        _bus.Display.CaptureLiveDisplayDmaBeforeSlotEngineGrant(lastSlot);
                    }
                }

                var available = true;
                for (var slot = 0; slot < slotCount; slot++)
                {
                    if (_slotEngine.IsReserved(candidate + (slot * AgnusChipSlotScheduler.SlotCycles)))
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
            return _slotEngine.Arbitrate(request, adjustedBase);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsCpuSlotRequest(AmigaBusAccessRequest request)
        {
            return request.Requester == AmigaBusRequester.Cpu &&
                (request.Target == AmigaBusAccessTarget.ChipRam ||
                    request.Target == AmigaBusAccessTarget.ExpansionRam ||
                    request.Target == AmigaBusAccessTarget.CustomRegisters);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ShouldPrepareDisplayBeforeDeviceGrant(AmigaBusAccessRequest request)
        {
            return request.Requester == AmigaBusRequester.Blitter ||
                request.Requester == AmigaBusRequester.Copper ||
                request.Requester == AmigaBusRequester.Paula ||
                request.Requester == AmigaBusRequester.Disk ||
                request.Requester == AmigaBusRequester.Host;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool UsesChipSlot(AmigaBusAccessRequest request)
        {
            return request.Target == AmigaBusAccessTarget.ChipRam ||
                request.Target == AmigaBusAccessTarget.ExpansionRam ||
                request.Target == AmigaBusAccessTarget.CustomRegisters;
        }
    }

    internal readonly struct AgnusPalBeamPosition
    {
        public AgnusPalBeamPosition(
            long currentCycle,
            long frameStartCycle,
            int frameNumber,
            int beamLine,
            int beamHorizontal)
        {
            CurrentCycle = currentCycle;
            FrameStartCycle = frameStartCycle;
            FrameNumber = frameNumber;
            BeamLine = beamLine;
            BeamHorizontal = beamHorizontal;
        }

        public long CurrentCycle { get; }

        public long FrameStartCycle { get; }

        public int FrameNumber { get; }

        public int BeamLine { get; }

        public int BeamHorizontal { get; }

        public static AgnusPalBeamPosition FromCycle(long cycle)
        {
            cycle = Math.Max(0, cycle);
            var cycleInFrame = cycle % AmigaConstants.A500PalCpuCyclesPerFrame;
            var frameStartCycle = cycle - cycleInFrame;
            var line = Math.Clamp(
                (int)(cycleInFrame / AmigaConstants.A500PalCpuCyclesPerRasterLine),
                0,
                AmigaConstants.A500PalRasterLines - 1);
            var lineCycle = cycleInFrame - (line * AmigaConstants.A500PalCpuCyclesPerRasterLine);
            var horizontal = Math.Clamp(
                (int)(lineCycle / AmigaConstants.A500PalCpuCyclesPerColorClock),
                0,
                AmigaConstants.A500PalColorClocksPerRasterLine - 1);
            var frame = (int)Math.Min(int.MaxValue, cycle / AmigaConstants.A500PalCpuCyclesPerFrame);
            return new AgnusPalBeamPosition(cycle, frameStartCycle, frame, line, horizontal);
        }
    }

    internal sealed class ZeroWaitBusArbiter : IAmigaBusArbiter
    {
        public ZeroWaitBusArbiter(long baseAccessCycles = 0)
        {
            if (baseAccessCycles < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(baseAccessCycles), baseAccessCycles, "Base access cycles cannot be negative.");
            }

            BaseAccessCycles = baseAccessCycles;
        }

        public long BaseAccessCycles { get; }

        public AmigaBusAccessResult Arbitrate(AmigaBusAccessRequest request)
        {
            return new AmigaBusAccessResult(request, request.RequestedCycle, request.RequestedCycle + BaseAccessCycles);
        }
    }

    internal sealed class AgnusChipSlotScheduler : IAgnusChipSlotTiming
    {
        public const int SlotCycles = AmigaConstants.A500PalCpuCyclesPerColorClock;
        private const int SlotsPerFrame = AmigaConstants.A500PalCpuCyclesPerFrame / SlotCycles;
        private const int SlotTableFrames = 2;
        private readonly AgnusChipSlotReservation[] _reservations = new AgnusChipSlotReservation[SlotsPerFrame * SlotTableFrames];
        private readonly long[] _slotGrantCountsByOwner = new long[(int)AgnusChipSlotOwner.Host + 1];
        private AmigaBusAccessResult? _lastReservation;
        private AgnusChipSlotSnapshot? _lastGrantedSlot;
        private AgnusChipSlotSnapshot? _lastDeniedFixedSlot;
        private AgnusChipSlotSnapshot? _lastDeniedFixedSlotBlocker;
        private readonly int[] _deniedFixedSlotCountsByOwner = new int[(int)AgnusChipSlotOwner.Host + 1];
        private readonly int[] _deniedFixedSlotBlockerCountsByOwner = new int[(int)AgnusChipSlotOwner.Host + 1];
        private int _deniedFixedSlotCount;
        private long _slotGrantCount;
        private long _currentCycle;
        private AgnusPalBeamPosition _beam;
        private bool _beamValid;

        public void Clear()
        {
            Array.Clear(_reservations);
            _lastReservation = null;
            _lastGrantedSlot = null;
            _lastDeniedFixedSlot = null;
            _lastDeniedFixedSlotBlocker = null;
            _deniedFixedSlotCount = 0;
            Array.Clear(_deniedFixedSlotCountsByOwner);
            Array.Clear(_deniedFixedSlotBlockerCountsByOwner);
            _slotGrantCount = 0;
            Array.Clear(_slotGrantCountsByOwner);
            _currentCycle = 0;
            _beam = default;
            _beamValid = false;
        }

        public void AdvanceTo(long targetCycle)
        {
            targetCycle = Math.Max(0, targetCycle);
            if (targetCycle <= _currentCycle)
            {
                return;
            }

            _currentCycle = targetCycle;
            _beamValid = false;
        }

        [HotPath]
        public AmigaBusAccessResult Arbitrate(AmigaBusAccessRequest request, AmigaBusAccessResult baseResult)
        {
            if (!UsesChipSlot(request))
            {
                return baseResult;
            }

            var slotCount = GetSlotCount(request.Size);
            var owner = GetOwner(request.Requester);
            var priority = GetPriority(owner);
            var granted = FindFreeSlot(Math.Max(baseResult.GrantedCycle, request.RequestedCycle), slotCount);
            for (var slot = 0; slot < slotCount; slot++)
            {
                SetReservation(granted + (slot * SlotCycles), request, owner, priority);
            }

            var completed = Math.Max(baseResult.CompletedCycle, granted + (slotCount * SlotCycles));
            var result = new AmigaBusAccessResult(request, granted, completed);
            _lastReservation = result;
            _lastGrantedSlot = new AgnusChipSlotSnapshot(owner, request.Kind, request.Address, request.RequestedCycle, granted, denied: false);
            AdvanceTo(completed);
            return result;
        }

        [HotPath]
        public bool TryReserveFixedDmaSlot(AmigaBusAccessRequest request, out AmigaBusAccessResult result)
        {
            if (!UsesChipSlot(request))
            {
                result = new AmigaBusAccessResult(request, request.RequestedCycle, request.RequestedCycle);
                return true;
            }

            var slotCount = GetSlotCount(request.Size);
            var owner = GetOwner(request.Requester);
            var priority = GetPriority(owner);
            var granted = AlignToSlot(request.RequestedCycle);
            if (!CanReserveFixedSlot(request, granted, slotCount, priority, out var blocker))
            {
                result = new AmigaBusAccessResult(request, granted, granted);
                _lastDeniedFixedSlot = new AgnusChipSlotSnapshot(owner, request.Kind, request.Address, request.RequestedCycle, granted, denied: true);
                _lastDeniedFixedSlotBlocker = blocker.ToSnapshot(denied: false);
                RecordDeniedFixedSlot(owner, blocker.Owner);
                AdvanceTo(granted);
                return false;
            }

            for (var slot = 0; slot < slotCount; slot++)
            {
                SetReservation(granted + (slot * SlotCycles), request, owner, priority);
            }

            result = new AmigaBusAccessResult(request, granted, granted + (slotCount * SlotCycles));
            _lastReservation = result;
            _lastGrantedSlot = new AgnusChipSlotSnapshot(owner, request.Kind, request.Address, request.RequestedCycle, granted, denied: false);
            AdvanceTo(result.CompletedCycle);
            return true;
        }

        [HotPath]
        public AmigaBusAccessResult ReserveBitplaneDmaSlot(uint address, long requestedCycle)
        {
            requestedCycle = Math.Max(0, requestedCycle);
            var request = new AmigaBusAccessRequest(
                AmigaBusRequester.Bitplane,
                AmigaBusAccessKind.Bitplane,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                isWrite: false);
            var granted = AlignToSlot(requestedCycle);
            var owner = AgnusChipSlotOwner.Bitplane;
            var priority = AgnusChipSlotPriority.Bitplane;
            SetReservation(granted, request, owner, priority);
            var result = new AmigaBusAccessResult(request, granted, granted + SlotCycles);
            _lastReservation = result;
            _lastGrantedSlot = new AgnusChipSlotSnapshot(owner, request.Kind, request.Address, request.RequestedCycle, granted, denied: false);
            AdvanceTo(result.CompletedCycle);
            return result;
        }

        [HotPath]
        public AmigaBusAccessResult ReserveCopperDmaSlot(uint address, long requestedCycle)
        {
            requestedCycle = Math.Max(0, requestedCycle);
            var request = new AmigaBusAccessRequest(
                AmigaBusRequester.Copper,
                AmigaBusAccessKind.Copper,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                isWrite: false);
            var owner = AgnusChipSlotOwner.Copper;
            var priority = AgnusChipSlotPriority.Copper;
            var granted = FindFreeSingleSlot(requestedCycle);
            SetReservation(granted, request, owner, priority);
            var result = new AmigaBusAccessResult(request, granted, granted + SlotCycles);
            _lastReservation = result;
            _lastGrantedSlot = new AgnusChipSlotSnapshot(owner, request.Kind, request.Address, request.RequestedCycle, granted, denied: false);
            AdvanceTo(result.CompletedCycle);
            return result;
        }

        public bool IsReserved(long cycle)
        {
            return TryGetReservation(AlignToSlot(cycle), out _);
        }

        public AmigaBusAccessResult? LastReservation => _lastReservation;

        public AgnusChipSlotSnapshot? LastGrantedSlot => _lastGrantedSlot;

        public AgnusChipSlotSnapshot? LastDeniedFixedSlot => _lastDeniedFixedSlot;

        public AgnusChipSlotSnapshot? LastDeniedFixedSlotBlocker => _lastDeniedFixedSlotBlocker;

        public int DeniedFixedSlotCount => _deniedFixedSlotCount;

        public int GetDeniedFixedSlotCount(AgnusChipSlotOwner owner)
        {
            return _deniedFixedSlotCountsByOwner[(int)owner];
        }

        public int GetDeniedFixedSlotBlockerCount(AgnusChipSlotOwner owner)
        {
            return _deniedFixedSlotBlockerCountsByOwner[(int)owner];
        }

        public int DivergenceCount => 0;

        public AgnusSlotDivergenceSnapshot? LastDivergence => null;

        public int ReservationCount => CountReservations();

        public int GetReservationCount(AgnusChipSlotOwner owner)
        {
            var count = 0;
            for (var i = 0; i < _reservations.Length; i++)
            {
                if (_reservations[i].Valid && _reservations[i].Owner == owner)
                {
                    count++;
                }
            }

            return count;
        }

        public long SlotGrantCount => _slotGrantCount;

        public long GetSlotGrantCount(AgnusChipSlotOwner owner)
        {
            return _slotGrantCountsByOwner[(int)owner];
        }

        public long CurrentCycle => _currentCycle;

        public long FrameStartCycle => CurrentBeam.FrameStartCycle;

        public int FrameNumber => CurrentBeam.FrameNumber;

        public int BeamLine => CurrentBeam.BeamLine;

        public int BeamHorizontal => CurrentBeam.BeamHorizontal;

        [HotPath]
        public void PruneBefore(long cycle)
        {
            _ = cycle;
        }

        public static long AlignToSlot(long cycle)
        {
            cycle = Math.Max(0, cycle);
            var remainder = cycle % SlotCycles;
            return remainder == 0 ? cycle : cycle + (SlotCycles - remainder);
        }

        private AgnusPalBeamPosition CurrentBeam
        {
            get
            {
                if (!_beamValid)
                {
                    _beam = AgnusPalBeamPosition.FromCycle(_currentCycle);
                    _beamValid = true;
                }

                return _beam;
            }
        }

        private long FindFreeSlot(long requestedCycle, int slotCount)
        {
            if (slotCount == 1)
            {
                return FindFreeSingleSlot(requestedCycle);
            }

            var candidate = AlignToSlot(requestedCycle);
            while (!AreSlotsFree(candidate, slotCount))
            {
                candidate += SlotCycles;
            }

            return candidate;
        }

        private long FindFreeSingleSlot(long requestedCycle)
        {
            var candidate = AlignToSlot(requestedCycle);
            while (true)
            {
                var reservation = _reservations[GetSlotIndex(candidate)];
                if (!reservation.Valid || reservation.SlotCycle != candidate)
                {
                    return candidate;
                }

                candidate += SlotCycles;
            }
        }

        private bool AreSlotsFree(long firstSlot, int slotCount)
        {
            for (var slot = 0; slot < slotCount; slot++)
            {
                if (TryGetReservation(firstSlot + (slot * SlotCycles), out _))
                {
                    return false;
                }
            }

            return true;
        }

        private bool CanReserveFixedSlot(
            AmigaBusAccessRequest request,
            long firstSlot,
            int slotCount,
            AgnusChipSlotPriority priority,
            out AgnusChipSlotReservation blocker)
        {
            for (var slot = 0; slot < slotCount; slot++)
            {
                if (!TryGetReservation(firstSlot + (slot * SlotCycles), out var existing))
                {
                    continue;
                }

                if (existing.Matches(request))
                {
                    continue;
                }

                if (existing.Priority >= priority)
                {
                    blocker = existing;
                    return false;
                }
            }

            blocker = default;
            return true;
        }

        private void SetReservation(
            long slotCycle,
            AmigaBusAccessRequest request,
            AgnusChipSlotOwner owner,
            AgnusChipSlotPriority priority)
        {
            var index = GetSlotIndex(slotCycle);
            var existing = _reservations[index];
            if (existing.Valid && existing.SlotCycle == slotCycle && existing.Matches(request))
            {
                return;
            }

            _reservations[index] = new AgnusChipSlotReservation(slotCycle, request, owner, priority);
            RecordSlotGrant(owner);
        }

        private void RecordSlotGrant(AgnusChipSlotOwner owner)
        {
            _slotGrantCount++;
            _slotGrantCountsByOwner[(int)owner]++;
        }

        private void RecordDeniedFixedSlot(AgnusChipSlotOwner owner, AgnusChipSlotOwner blocker)
        {
            _deniedFixedSlotCount++;
            _deniedFixedSlotCountsByOwner[(int)owner]++;
            _deniedFixedSlotBlockerCountsByOwner[(int)blocker]++;
        }

        private int CountReservations()
        {
            var count = 0;
            for (var i = 0; i < _reservations.Length; i++)
            {
                if (_reservations[i].Valid)
                {
                    count++;
                }
            }

            return count;
        }

        private bool TryGetReservation(long slotCycle, out AgnusChipSlotReservation reservation)
        {
            var index = GetSlotIndex(slotCycle);
            reservation = _reservations[index];
            return reservation.Valid && reservation.SlotCycle == slotCycle;
        }

        private static int GetSlotIndex(long slotCycle)
        {
            var absoluteSlot = Math.Max(0, slotCycle / SlotCycles);
            return (int)(absoluteSlot % (SlotsPerFrame * SlotTableFrames));
        }

        private static bool UsesChipSlot(AmigaBusAccessRequest request)
        {
            return request.Target == AmigaBusAccessTarget.ChipRam ||
                request.Target == AmigaBusAccessTarget.ExpansionRam ||
                request.Target == AmigaBusAccessTarget.CustomRegisters;
        }

        private static int GetSlotCount(AmigaBusAccessSize size)
        {
            return size == AmigaBusAccessSize.Long ? 2 : 1;
        }

        private static AgnusChipSlotOwner GetOwner(AmigaBusRequester requester)
        {
            return requester switch
            {
                AmigaBusRequester.Paula => AgnusChipSlotOwner.Paula,
                AmigaBusRequester.Disk => AgnusChipSlotOwner.Disk,
                AmigaBusRequester.Bitplane => AgnusChipSlotOwner.Bitplane,
                AmigaBusRequester.Sprite => AgnusChipSlotOwner.Sprite,
                AmigaBusRequester.Copper => AgnusChipSlotOwner.Copper,
                AmigaBusRequester.Blitter => AgnusChipSlotOwner.Blitter,
                AmigaBusRequester.Host => AgnusChipSlotOwner.Host,
                _ => AgnusChipSlotOwner.Cpu
            };
        }

        private static AgnusChipSlotPriority GetPriority(AgnusChipSlotOwner owner)
        {
            return owner switch
            {
                AgnusChipSlotOwner.Bitplane => AgnusChipSlotPriority.Bitplane,
                AgnusChipSlotOwner.Sprite => AgnusChipSlotPriority.Sprite,
                AgnusChipSlotOwner.Disk => AgnusChipSlotPriority.Disk,
                AgnusChipSlotOwner.Paula => AgnusChipSlotPriority.Paula,
                AgnusChipSlotOwner.Copper => AgnusChipSlotPriority.Copper,
                AgnusChipSlotOwner.Blitter => AgnusChipSlotPriority.Blitter,
                _ => AgnusChipSlotPriority.Cpu
            };
        }

        private readonly struct AgnusChipSlotReservation
        {
            private readonly AmigaBusAccessRequest _request;

            public AgnusChipSlotReservation(
                long slotCycle,
                AmigaBusAccessRequest request,
                AgnusChipSlotOwner owner,
                AgnusChipSlotPriority priority)
            {
                Valid = true;
                SlotCycle = slotCycle;
                _request = request;
                Requester = request.Requester;
                Owner = owner;
                Priority = priority;
            }

            public bool Valid { get; }

            public long SlotCycle { get; }

            public AmigaBusRequester Requester { get; }

            public AgnusChipSlotOwner Owner { get; }

            public AgnusChipSlotPriority Priority { get; }

            public bool Matches(AmigaBusAccessRequest request)
            {
                return _request.Requester == request.Requester &&
                    _request.Kind == request.Kind &&
                    _request.Target == request.Target &&
                    _request.Address == request.Address &&
                    _request.Size == request.Size &&
                    _request.RequestedCycle == request.RequestedCycle &&
                    _request.IsWrite == request.IsWrite;
            }

            public AgnusChipSlotSnapshot ToSnapshot(bool denied)
            {
                return new AgnusChipSlotSnapshot(
                    Owner,
                    _request.Kind,
                    _request.Address,
                    _request.RequestedCycle,
                    SlotCycle,
                    denied);
            }
        }
    }

    internal sealed class AgnusSlotEngine : IAgnusChipSlotTiming
    {
        private const int SlotCycles = AgnusChipSlotScheduler.SlotCycles;
        private const int SlotsPerFrame = AmigaConstants.A500PalCpuCyclesPerFrame / SlotCycles;
        private const int SlotTableFrames = 2;
        private readonly AgnusCommittedSlot[] _slots = new AgnusCommittedSlot[SlotsPerFrame * SlotTableFrames];
        private readonly long[] _slotGrantCountsByOwner = new long[(int)AgnusChipSlotOwner.Host + 1];
        private AmigaBusAccessResult? _lastReservation;
        private AgnusChipSlotSnapshot? _lastGrantedSlot;
        private AgnusChipSlotSnapshot? _lastDeniedFixedSlot;
        private AgnusChipSlotSnapshot? _lastDeniedFixedSlotBlocker;
        private readonly int[] _deniedFixedSlotCountsByOwner = new int[(int)AgnusChipSlotOwner.Host + 1];
        private readonly int[] _deniedFixedSlotBlockerCountsByOwner = new int[(int)AgnusChipSlotOwner.Host + 1];
        private int _deniedFixedSlotCount;
        private long _slotGrantCount;
        private long _currentCycle;
        private AgnusPalBeamPosition _beam;
        private bool _beamValid;

        public void Clear()
        {
            Array.Clear(_slots);
            _lastReservation = null;
            _lastGrantedSlot = null;
            _lastDeniedFixedSlot = null;
            _lastDeniedFixedSlotBlocker = null;
            _deniedFixedSlotCount = 0;
            Array.Clear(_deniedFixedSlotCountsByOwner);
            Array.Clear(_deniedFixedSlotBlockerCountsByOwner);
            _slotGrantCount = 0;
            Array.Clear(_slotGrantCountsByOwner);
            _currentCycle = 0;
            _beam = default;
            _beamValid = false;
        }

        public void AdvanceTo(long targetCycle)
        {
            targetCycle = Math.Max(0, targetCycle);
            if (targetCycle <= _currentCycle)
            {
                return;
            }

            _currentCycle = targetCycle;
            _beamValid = false;
        }

        [HotPath]
        public AmigaBusAccessResult Arbitrate(AmigaBusAccessRequest request, AmigaBusAccessResult baseResult)
        {
            if (!UsesChipSlot(request))
            {
                return baseResult;
            }

            var slotCount = GetSlotCount(request.Size);
            var owner = GetOwner(request.Requester);
            var requestedGrant = Math.Max(baseResult.GrantedCycle, request.RequestedCycle);
            if (slotCount == 1)
            {
                var grantedSingle = CommitSingleGrantSlot(requestedGrant, request, owner);
                var completedSingle = Math.Max(baseResult.CompletedCycle, grantedSingle + SlotCycles);
                var singleResult = new AmigaBusAccessResult(request, grantedSingle, completedSingle);
                _lastReservation = singleResult;
                _lastGrantedSlot = new AgnusChipSlotSnapshot(owner, request.Kind, request.Address, request.RequestedCycle, grantedSingle, denied: false);
                AdvanceTo(completedSingle);
                return singleResult;
            }

            var granted = FindFreeSlot(requestedGrant, slotCount);
            for (var slot = 0; slot < slotCount; slot++)
            {
                CommitSlot(granted + (slot * SlotCycles), request, owner);
            }

            var completed = Math.Max(baseResult.CompletedCycle, granted + (slotCount * SlotCycles));
            var result = new AmigaBusAccessResult(request, granted, completed);
            _lastReservation = result;
            _lastGrantedSlot = new AgnusChipSlotSnapshot(owner, request.Kind, request.Address, request.RequestedCycle, granted, denied: false);
            AdvanceTo(completed);
            return result;
        }

        [HotPath]
        public bool TryReserveFixedDmaSlot(AmigaBusAccessRequest request, out AmigaBusAccessResult result)
        {
            if (!UsesChipSlot(request))
            {
                result = new AmigaBusAccessResult(request, request.RequestedCycle, request.RequestedCycle);
                return true;
            }

            var slotCount = GetSlotCount(request.Size);
            var owner = GetOwner(request.Requester);
            var granted = AgnusChipSlotScheduler.AlignToSlot(request.RequestedCycle);
            if (slotCount == 1)
            {
                var index = GetSlotIndex(granted);
                var existing = _slots[index];
                if (existing.Valid && existing.SlotCycle == granted)
                {
                    if (!existing.Matches(request))
                    {
                        result = new AmigaBusAccessResult(request, granted, granted);
                        _lastDeniedFixedSlot = new AgnusChipSlotSnapshot(owner, request.Kind, request.Address, request.RequestedCycle, granted, denied: true);
                        _lastDeniedFixedSlotBlocker = existing.ToSnapshot(denied: false);
                        RecordDeniedFixedSlot(owner, existing.Owner);
                        AdvanceTo(granted);
                        return false;
                    }
                }
                else
                {
                    _slots[index] = new AgnusCommittedSlot(granted, request, owner);
                    RecordSlotGrant(owner);
                }

                result = new AmigaBusAccessResult(request, granted, granted + SlotCycles);
                _lastReservation = result;
                _lastGrantedSlot = new AgnusChipSlotSnapshot(owner, request.Kind, request.Address, request.RequestedCycle, granted, denied: false);
                AdvanceTo(result.CompletedCycle);
                return true;
            }

            if (!CanCommitFixedSlot(request, granted, slotCount, out var blocker))
            {
                result = new AmigaBusAccessResult(request, granted, granted);
                _lastDeniedFixedSlot = new AgnusChipSlotSnapshot(owner, request.Kind, request.Address, request.RequestedCycle, granted, denied: true);
                _lastDeniedFixedSlotBlocker = blocker.ToSnapshot(denied: false);
                RecordDeniedFixedSlot(owner, blocker.Owner);
                AdvanceTo(granted);
                return false;
            }

            for (var slot = 0; slot < slotCount; slot++)
            {
                CommitSlot(granted + (slot * SlotCycles), request, owner);
            }

            result = new AmigaBusAccessResult(request, granted, granted + (slotCount * SlotCycles));
            _lastReservation = result;
            _lastGrantedSlot = new AgnusChipSlotSnapshot(owner, request.Kind, request.Address, request.RequestedCycle, granted, denied: false);
            AdvanceTo(result.CompletedCycle);
            return true;
        }

        [HotPath]
        public AmigaBusAccessResult ReserveBitplaneDmaSlot(uint address, long requestedCycle)
        {
            requestedCycle = Math.Max(0, requestedCycle);
            var request = new AmigaBusAccessRequest(
                AmigaBusRequester.Bitplane,
                AmigaBusAccessKind.Bitplane,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                isWrite: false);
            var owner = AgnusChipSlotOwner.Bitplane;
            var granted = AgnusChipSlotScheduler.AlignToSlot(requestedCycle);
            var index = GetSlotIndex(granted);
            var existing = _slots[index];
            if (existing.Valid && existing.SlotCycle == granted)
            {
                if (!existing.Matches(request))
                {
                    var denied = new AmigaBusAccessResult(request, granted, granted);
                    _lastDeniedFixedSlot = new AgnusChipSlotSnapshot(owner, request.Kind, request.Address, request.RequestedCycle, granted, denied: true);
                    _lastDeniedFixedSlotBlocker = existing.ToSnapshot(denied: false);
                    RecordDeniedFixedSlot(owner, existing.Owner);
                    AdvanceTo(granted);
                    return denied;
                }
            }
            else
            {
                _slots[index] = new AgnusCommittedSlot(granted, request, owner);
                RecordSlotGrant(owner);
            }

            var result = new AmigaBusAccessResult(request, granted, granted + SlotCycles);
            _lastReservation = result;
            _lastGrantedSlot = new AgnusChipSlotSnapshot(owner, request.Kind, request.Address, request.RequestedCycle, granted, denied: false);
            AdvanceTo(result.CompletedCycle);
            return result;
        }

        [HotPath]
        public AmigaBusAccessResult ReserveCopperDmaSlot(uint address, long requestedCycle)
        {
            requestedCycle = Math.Max(0, requestedCycle);
            var request = new AmigaBusAccessRequest(
                AmigaBusRequester.Copper,
                AmigaBusAccessKind.Copper,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                isWrite: false);
            var owner = AgnusChipSlotOwner.Copper;
            var granted = CommitSingleGrantSlot(requestedCycle, request, owner);
            var result = new AmigaBusAccessResult(request, granted, granted + SlotCycles);
            _lastReservation = result;
            _lastGrantedSlot = new AgnusChipSlotSnapshot(owner, request.Kind, request.Address, request.RequestedCycle, granted, denied: false);
            AdvanceTo(result.CompletedCycle);
            return result;
        }

        public bool IsReserved(long cycle)
        {
            return TryGetSlot(AgnusChipSlotScheduler.AlignToSlot(cycle), out _);
        }

        public AmigaBusAccessResult? LastReservation => _lastReservation;

        public AgnusChipSlotSnapshot? LastGrantedSlot => _lastGrantedSlot;

        public AgnusChipSlotSnapshot? LastDeniedFixedSlot => _lastDeniedFixedSlot;

        public AgnusChipSlotSnapshot? LastDeniedFixedSlotBlocker => _lastDeniedFixedSlotBlocker;

        public int DeniedFixedSlotCount => _deniedFixedSlotCount;

        public int GetDeniedFixedSlotCount(AgnusChipSlotOwner owner)
        {
            return _deniedFixedSlotCountsByOwner[(int)owner];
        }

        public int GetDeniedFixedSlotBlockerCount(AgnusChipSlotOwner owner)
        {
            return _deniedFixedSlotBlockerCountsByOwner[(int)owner];
        }

        public int DivergenceCount => 0;

        public AgnusSlotDivergenceSnapshot? LastDivergence => null;

        public int ReservationCount => CountReservations();

        public int GetReservationCount(AgnusChipSlotOwner owner)
        {
            var count = 0;
            for (var i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Valid && _slots[i].Owner == owner)
                {
                    count++;
                }
            }

            return count;
        }

        public long SlotGrantCount => _slotGrantCount;

        public long GetSlotGrantCount(AgnusChipSlotOwner owner)
        {
            return _slotGrantCountsByOwner[(int)owner];
        }

        public long CurrentCycle => _currentCycle;

        public long FrameStartCycle => CurrentBeam.FrameStartCycle;

        public int FrameNumber => CurrentBeam.FrameNumber;

        public int BeamLine => CurrentBeam.BeamLine;

        public int BeamHorizontal => CurrentBeam.BeamHorizontal;

        [HotPath]
        public void PruneBefore(long cycle)
        {
            _ = cycle;
        }

        private AgnusPalBeamPosition CurrentBeam
        {
            get
            {
                if (!_beamValid)
                {
                    _beam = AgnusPalBeamPosition.FromCycle(_currentCycle);
                    _beamValid = true;
                }

                return _beam;
            }
        }

        private long CommitSingleGrantSlot(
            long requestedCycle,
            AmigaBusAccessRequest request,
            AgnusChipSlotOwner owner)
        {
            var granted = AgnusChipSlotScheduler.AlignToSlot(requestedCycle);
            while (true)
            {
                var index = GetSlotIndex(granted);
                var existing = _slots[index];
                if (!existing.Valid || existing.SlotCycle != granted)
                {
                    _slots[index] = new AgnusCommittedSlot(granted, request, owner);
                    RecordSlotGrant(owner);
                    return granted;
                }

                if (existing.Matches(request))
                {
                    return granted;
                }

                granted += SlotCycles;
            }
        }

        private long FindFreeSlot(long requestedCycle, int slotCount)
        {
            if (slotCount == 1)
            {
                return FindFreeSingleSlot(requestedCycle);
            }

            var candidate = AgnusChipSlotScheduler.AlignToSlot(requestedCycle);
            while (!AreSlotsFree(candidate, slotCount))
            {
                candidate += SlotCycles;
            }

            return candidate;
        }

        private long FindFreeSingleSlot(long requestedCycle)
        {
            var candidate = AgnusChipSlotScheduler.AlignToSlot(requestedCycle);
            while (true)
            {
                var reservation = _slots[GetSlotIndex(candidate)];
                if (!reservation.Valid || reservation.SlotCycle != candidate)
                {
                    return candidate;
                }

                candidate += SlotCycles;
            }
        }

        private bool AreSlotsFree(long firstSlot, int slotCount)
        {
            for (var slot = 0; slot < slotCount; slot++)
            {
                if (TryGetSlot(firstSlot + (slot * SlotCycles), out _))
                {
                    return false;
                }
            }

            return true;
        }

        private bool CanCommitFixedSlot(
            AmigaBusAccessRequest request,
            long firstSlot,
            int slotCount,
            out AgnusCommittedSlot blocker)
        {
            for (var slot = 0; slot < slotCount; slot++)
            {
                if (!TryGetSlot(firstSlot + (slot * SlotCycles), out var existing))
                {
                    continue;
                }

                if (existing.Matches(request))
                {
                    continue;
                }

                blocker = existing;
                return false;
            }

            blocker = default;
            return true;
        }

        private void CommitSlot(
            long slotCycle,
            AmigaBusAccessRequest request,
            AgnusChipSlotOwner owner)
        {
            var index = GetSlotIndex(slotCycle);
            var existing = _slots[index];
            if (existing.Valid && existing.SlotCycle == slotCycle && existing.Matches(request))
            {
                return;
            }

            _slots[index] = new AgnusCommittedSlot(slotCycle, request, owner);
            RecordSlotGrant(owner);
        }

        private void RecordSlotGrant(AgnusChipSlotOwner owner)
        {
            _slotGrantCount++;
            _slotGrantCountsByOwner[(int)owner]++;
        }

        private void RecordDeniedFixedSlot(AgnusChipSlotOwner owner, AgnusChipSlotOwner blocker)
        {
            _deniedFixedSlotCount++;
            _deniedFixedSlotCountsByOwner[(int)owner]++;
            _deniedFixedSlotBlockerCountsByOwner[(int)blocker]++;
        }

        private int CountReservations()
        {
            var count = 0;
            for (var i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Valid)
                {
                    count++;
                }
            }

            return count;
        }

        private bool TryGetSlot(long slotCycle, out AgnusCommittedSlot reservation)
        {
            var index = GetSlotIndex(slotCycle);
            reservation = _slots[index];
            return reservation.Valid && reservation.SlotCycle == slotCycle;
        }

        private static int GetSlotIndex(long slotCycle)
        {
            var absoluteSlot = Math.Max(0, slotCycle / SlotCycles);
            return (int)(absoluteSlot % (SlotsPerFrame * SlotTableFrames));
        }

        private static bool UsesChipSlot(AmigaBusAccessRequest request)
        {
            return request.Target == AmigaBusAccessTarget.ChipRam ||
                request.Target == AmigaBusAccessTarget.ExpansionRam ||
                request.Target == AmigaBusAccessTarget.CustomRegisters;
        }

        private static int GetSlotCount(AmigaBusAccessSize size)
        {
            return size == AmigaBusAccessSize.Long ? 2 : 1;
        }

        private static AgnusChipSlotOwner GetOwner(AmigaBusRequester requester)
        {
            return requester switch
            {
                AmigaBusRequester.Paula => AgnusChipSlotOwner.Paula,
                AmigaBusRequester.Disk => AgnusChipSlotOwner.Disk,
                AmigaBusRequester.Bitplane => AgnusChipSlotOwner.Bitplane,
                AmigaBusRequester.Sprite => AgnusChipSlotOwner.Sprite,
                AmigaBusRequester.Copper => AgnusChipSlotOwner.Copper,
                AmigaBusRequester.Blitter => AgnusChipSlotOwner.Blitter,
                AmigaBusRequester.Host => AgnusChipSlotOwner.Host,
                _ => AgnusChipSlotOwner.Cpu
            };
        }

        private readonly struct AgnusCommittedSlot
        {
            private readonly AmigaBusAccessRequest _request;

            public AgnusCommittedSlot(
                long slotCycle,
                AmigaBusAccessRequest request,
                AgnusChipSlotOwner owner)
            {
                Valid = true;
                SlotCycle = slotCycle;
                _request = request;
                Requester = request.Requester;
                Owner = owner;
            }

            public bool Valid { get; }

            public long SlotCycle { get; }

            public AmigaBusRequester Requester { get; }

            public AgnusChipSlotOwner Owner { get; }

            public bool Matches(AmigaBusAccessRequest request)
            {
                return _request.Requester == request.Requester &&
                    _request.Kind == request.Kind &&
                    _request.Target == request.Target &&
                    _request.Address == request.Address &&
                    _request.Size == request.Size &&
                    _request.RequestedCycle == request.RequestedCycle &&
                    _request.IsWrite == request.IsWrite;
            }

            public AgnusChipSlotSnapshot ToSnapshot(bool denied)
            {
                return new AgnusChipSlotSnapshot(
                    Owner,
                    _request.Kind,
                    _request.Address,
                    _request.RequestedCycle,
                    SlotCycle,
                    denied);
            }
        }
    }

    internal sealed class AgnusShadowCompareSlotTiming : IAgnusChipSlotTiming
    {
        private readonly AgnusChipSlotScheduler _primary = new AgnusChipSlotScheduler();
        private readonly AgnusSlotEngine _shadow = new AgnusSlotEngine();
        private AmigaBusAccessResult? _lastPrimaryResult;
        private AmigaBusAccessResult? _lastShadowResult;
        private int _divergenceCount;
        private AgnusSlotDivergenceSnapshot? _lastDivergence;

        public int DivergenceCount => _divergenceCount;

        public void Clear()
        {
            _primary.Clear();
            _shadow.Clear();
            _lastPrimaryResult = null;
            _lastShadowResult = null;
            _divergenceCount = 0;
            _lastDivergence = null;
        }

        public void AdvanceTo(long targetCycle)
        {
            _primary.AdvanceTo(targetCycle);
            _shadow.AdvanceTo(targetCycle);
        }

        public AmigaBusAccessResult Arbitrate(AmigaBusAccessRequest request, AmigaBusAccessResult baseResult)
        {
            var primary = _primary.Arbitrate(request, baseResult);
            var shadow = _shadow.Arbitrate(request, baseResult);
            Compare(primary, shadow);
            return primary;
        }

        public bool TryReserveFixedDmaSlot(AmigaBusAccessRequest request, out AmigaBusAccessResult result)
        {
            var primaryGranted = _primary.TryReserveFixedDmaSlot(request, out result);
            var shadowGranted = _shadow.TryReserveFixedDmaSlot(request, out var shadow);
            Compare(primaryGranted, result, shadowGranted, shadow);
            return primaryGranted;
        }

        public AmigaBusAccessResult ReserveBitplaneDmaSlot(uint address, long requestedCycle)
        {
            var primary = _primary.ReserveBitplaneDmaSlot(address, requestedCycle);
            var shadow = _shadow.ReserveBitplaneDmaSlot(address, requestedCycle);
            Compare(primary, shadow);
            return primary;
        }

        public AmigaBusAccessResult ReserveCopperDmaSlot(uint address, long requestedCycle)
        {
            var primary = _primary.ReserveCopperDmaSlot(address, requestedCycle);
            var shadow = _shadow.ReserveCopperDmaSlot(address, requestedCycle);
            Compare(primary, shadow);
            return primary;
        }

        public bool IsReserved(long cycle)
        {
            var primary = _primary.IsReserved(cycle);
            if (primary != _shadow.IsReserved(cycle))
            {
                _divergenceCount++;
            }

            return primary;
        }

        public AmigaBusAccessResult? LastReservation => _primary.LastReservation;

        public AgnusChipSlotSnapshot? LastGrantedSlot => _primary.LastGrantedSlot;

        public AgnusChipSlotSnapshot? LastDeniedFixedSlot => _primary.LastDeniedFixedSlot;

        public AgnusChipSlotSnapshot? LastDeniedFixedSlotBlocker => _primary.LastDeniedFixedSlotBlocker;

        public int DeniedFixedSlotCount => _primary.DeniedFixedSlotCount;

        public int GetDeniedFixedSlotCount(AgnusChipSlotOwner owner)
        {
            return _primary.GetDeniedFixedSlotCount(owner);
        }

        public int GetDeniedFixedSlotBlockerCount(AgnusChipSlotOwner owner)
        {
            return _primary.GetDeniedFixedSlotBlockerCount(owner);
        }

        public int ReservationCount => _primary.ReservationCount;

        public int GetReservationCount(AgnusChipSlotOwner owner)
        {
            return _primary.GetReservationCount(owner);
        }

        public long SlotGrantCount => _primary.SlotGrantCount;

        public long GetSlotGrantCount(AgnusChipSlotOwner owner)
        {
            return _primary.GetSlotGrantCount(owner);
        }

        public AgnusSlotDivergenceSnapshot? LastDivergence => _lastDivergence;

        public long CurrentCycle => _primary.CurrentCycle;

        public long FrameStartCycle => _primary.FrameStartCycle;

        public int FrameNumber => _primary.FrameNumber;

        public int BeamLine => _primary.BeamLine;

        public int BeamHorizontal => _primary.BeamHorizontal;

        public void PruneBefore(long cycle)
        {
            _primary.PruneBefore(cycle);
            _shadow.PruneBefore(cycle);
        }

        public AmigaBusAccessResult GetShadowResultFor(AmigaBusAccessResult primary)
        {
            if (_lastPrimaryResult.HasValue &&
                _lastShadowResult.HasValue &&
                ResultsMatch(_lastPrimaryResult.Value, primary))
            {
                return _lastShadowResult.Value;
            }

            return primary;
        }

        public void RecordDataDivergence(AmigaBusAccessResult primary, AmigaBusAccessResult shadow, ushort primaryValue, ushort shadowValue)
        {
            _divergenceCount++;
            _lastDivergence = new AgnusSlotDivergenceSnapshot(
                primary.Request,
                primaryGranted: true,
                shadowGranted: true,
                primary.GrantedCycle,
                shadow.GrantedCycle,
                primary.CompletedCycle,
                shadow.CompletedCycle,
                AgnusSlotDivergenceKind.Data,
                hasValueComparison: true,
                primaryValue,
                shadowValue);
        }

        private void Compare(AmigaBusAccessResult primary, AmigaBusAccessResult shadow)
        {
            _lastPrimaryResult = primary;
            _lastShadowResult = shadow;
            if (primary.GrantedCycle != shadow.GrantedCycle ||
                primary.CompletedCycle != shadow.CompletedCycle)
            {
                _divergenceCount++;
                _lastDivergence = new AgnusSlotDivergenceSnapshot(
                    primary.Request,
                    primaryGranted: true,
                    shadowGranted: true,
                    primary.GrantedCycle,
                    shadow.GrantedCycle,
                    primary.CompletedCycle,
                    shadow.CompletedCycle,
                    AgnusSlotDivergenceKind.Timing);
            }
        }

        private void Compare(
            bool primaryGranted,
            AmigaBusAccessResult primary,
            bool shadowGranted,
            AmigaBusAccessResult shadow)
        {
            _lastPrimaryResult = primary;
            _lastShadowResult = shadow;
            if (primaryGranted != shadowGranted ||
                primary.GrantedCycle != shadow.GrantedCycle ||
                primary.CompletedCycle != shadow.CompletedCycle)
            {
                _divergenceCount++;
                _lastDivergence = new AgnusSlotDivergenceSnapshot(
                    primary.Request,
                    primaryGranted,
                    shadowGranted,
                    primary.GrantedCycle,
                    shadow.GrantedCycle,
                    primary.CompletedCycle,
                    shadow.CompletedCycle,
                    primaryGranted == shadowGranted ? AgnusSlotDivergenceKind.Timing : AgnusSlotDivergenceKind.Grant);
            }
        }

        private static bool ResultsMatch(AmigaBusAccessResult left, AmigaBusAccessResult right)
        {
            return left.GrantedCycle == right.GrantedCycle &&
                left.CompletedCycle == right.CompletedCycle &&
                left.Request.Requester == right.Request.Requester &&
                left.Request.Kind == right.Request.Kind &&
                left.Request.Target == right.Request.Target &&
                left.Request.Address == right.Request.Address &&
                left.Request.Size == right.Request.Size &&
                left.Request.RequestedCycle == right.Request.RequestedCycle &&
                left.Request.IsWrite == right.Request.IsWrite;
        }
    }

    internal readonly struct AgnusBeamDmaSnapshot
    {
        public AgnusBeamDmaSnapshot(
            long currentCycle,
            long frameStartCycle,
            int frameNumber,
            int beamLine,
            int beamHorizontal,
            long cpuChipStallCycles,
            int liveBitplaneFetches,
            int liveSpriteFetches,
            int liveMissedSpriteSlots,
            int slotReservationCount,
            long slotGrantCount,
            int cpuSlotReservationCount,
            int blitterSlotReservationCount,
            int copperSlotReservationCount,
            int paulaSlotReservationCount,
            int diskSlotReservationCount,
            int spriteSlotReservationCount,
            int bitplaneSlotReservationCount,
            long cpuSlotGrantCount,
            long blitterSlotGrantCount,
            long copperSlotGrantCount,
            long paulaSlotGrantCount,
            long diskSlotGrantCount,
            long spriteSlotGrantCount,
            long bitplaneSlotGrantCount,
            AmigaBusAccessResult? lastFixedDmaReservation,
            AgnusChipSlotSnapshot? lastGrantedSlot,
            AgnusChipSlotSnapshot? lastDeniedFixedSlot,
            AgnusChipSlotSnapshot? lastDeniedFixedSlotBlocker,
            int deniedFixedSlotCount,
            int cpuDeniedFixedSlotCount,
            int blitterDeniedFixedSlotCount,
            int copperDeniedFixedSlotCount,
            int paulaDeniedFixedSlotCount,
            int diskDeniedFixedSlotCount,
            int spriteDeniedFixedSlotCount,
            int bitplaneDeniedFixedSlotCount,
            int cpuDeniedFixedSlotBlockerCount,
            int blitterDeniedFixedSlotBlockerCount,
            int copperDeniedFixedSlotBlockerCount,
            int paulaDeniedFixedSlotBlockerCount,
            int diskDeniedFixedSlotBlockerCount,
            int spriteDeniedFixedSlotBlockerCount,
            int bitplaneDeniedFixedSlotBlockerCount,
            int divergenceCount,
            AgnusSlotDivergenceSnapshot? lastDivergence)
        {
            CurrentCycle = currentCycle;
            FrameStartCycle = frameStartCycle;
            FrameNumber = frameNumber;
            BeamLine = beamLine;
            BeamHorizontal = beamHorizontal;
            CpuChipStallCycles = cpuChipStallCycles;
            LiveBitplaneFetches = liveBitplaneFetches;
            LiveSpriteFetches = liveSpriteFetches;
            LiveMissedSpriteSlots = liveMissedSpriteSlots;
            SlotReservationCount = slotReservationCount;
            SlotGrantCount = slotGrantCount;
            CpuSlotReservationCount = cpuSlotReservationCount;
            BlitterSlotReservationCount = blitterSlotReservationCount;
            CopperSlotReservationCount = copperSlotReservationCount;
            PaulaSlotReservationCount = paulaSlotReservationCount;
            DiskSlotReservationCount = diskSlotReservationCount;
            SpriteSlotReservationCount = spriteSlotReservationCount;
            BitplaneSlotReservationCount = bitplaneSlotReservationCount;
            CpuSlotGrantCount = cpuSlotGrantCount;
            BlitterSlotGrantCount = blitterSlotGrantCount;
            CopperSlotGrantCount = copperSlotGrantCount;
            PaulaSlotGrantCount = paulaSlotGrantCount;
            DiskSlotGrantCount = diskSlotGrantCount;
            SpriteSlotGrantCount = spriteSlotGrantCount;
            BitplaneSlotGrantCount = bitplaneSlotGrantCount;
            LastFixedDmaReservation = lastFixedDmaReservation;
            LastGrantedSlot = lastGrantedSlot;
            LastDeniedFixedSlot = lastDeniedFixedSlot;
            LastDeniedFixedSlotBlocker = lastDeniedFixedSlotBlocker;
            DeniedFixedSlotCount = deniedFixedSlotCount;
            CpuDeniedFixedSlotCount = cpuDeniedFixedSlotCount;
            BlitterDeniedFixedSlotCount = blitterDeniedFixedSlotCount;
            CopperDeniedFixedSlotCount = copperDeniedFixedSlotCount;
            PaulaDeniedFixedSlotCount = paulaDeniedFixedSlotCount;
            DiskDeniedFixedSlotCount = diskDeniedFixedSlotCount;
            SpriteDeniedFixedSlotCount = spriteDeniedFixedSlotCount;
            BitplaneDeniedFixedSlotCount = bitplaneDeniedFixedSlotCount;
            CpuDeniedFixedSlotBlockerCount = cpuDeniedFixedSlotBlockerCount;
            BlitterDeniedFixedSlotBlockerCount = blitterDeniedFixedSlotBlockerCount;
            CopperDeniedFixedSlotBlockerCount = copperDeniedFixedSlotBlockerCount;
            PaulaDeniedFixedSlotBlockerCount = paulaDeniedFixedSlotBlockerCount;
            DiskDeniedFixedSlotBlockerCount = diskDeniedFixedSlotBlockerCount;
            SpriteDeniedFixedSlotBlockerCount = spriteDeniedFixedSlotBlockerCount;
            BitplaneDeniedFixedSlotBlockerCount = bitplaneDeniedFixedSlotBlockerCount;
            DivergenceCount = divergenceCount;
            LastDivergence = lastDivergence;
        }

        public long CurrentCycle { get; }

        public long FrameStartCycle { get; }

        public int FrameNumber { get; }

        public int BeamLine { get; }

        public int BeamHorizontal { get; }

        public long CpuChipStallCycles { get; }

        public int LiveBitplaneFetches { get; }

        public int LiveSpriteFetches { get; }

        public int LiveMissedSpriteSlots { get; }

        public int SlotReservationCount { get; }

        public long SlotGrantCount { get; }

        public int CpuSlotReservationCount { get; }

        public int BlitterSlotReservationCount { get; }

        public int CopperSlotReservationCount { get; }

        public int PaulaSlotReservationCount { get; }

        public int DiskSlotReservationCount { get; }

        public int SpriteSlotReservationCount { get; }

        public int BitplaneSlotReservationCount { get; }

        public long CpuSlotGrantCount { get; }

        public long BlitterSlotGrantCount { get; }

        public long CopperSlotGrantCount { get; }

        public long PaulaSlotGrantCount { get; }

        public long DiskSlotGrantCount { get; }

        public long SpriteSlotGrantCount { get; }

        public long BitplaneSlotGrantCount { get; }

        public AmigaBusAccessResult? LastFixedDmaReservation { get; }

        public AgnusChipSlotSnapshot? LastGrantedSlot { get; }

        public AgnusChipSlotSnapshot? LastDeniedFixedSlot { get; }

        public AgnusChipSlotSnapshot? LastDeniedFixedSlotBlocker { get; }

        public int DeniedFixedSlotCount { get; }

        public int CpuDeniedFixedSlotCount { get; }

        public int BlitterDeniedFixedSlotCount { get; }

        public int CopperDeniedFixedSlotCount { get; }

        public int PaulaDeniedFixedSlotCount { get; }

        public int DiskDeniedFixedSlotCount { get; }

        public int SpriteDeniedFixedSlotCount { get; }

        public int BitplaneDeniedFixedSlotCount { get; }

        public int CpuDeniedFixedSlotBlockerCount { get; }

        public int BlitterDeniedFixedSlotBlockerCount { get; }

        public int CopperDeniedFixedSlotBlockerCount { get; }

        public int PaulaDeniedFixedSlotBlockerCount { get; }

        public int DiskDeniedFixedSlotBlockerCount { get; }

        public int SpriteDeniedFixedSlotBlockerCount { get; }

        public int BitplaneDeniedFixedSlotBlockerCount { get; }

        public int DivergenceCount { get; }

        public AgnusSlotDivergenceSnapshot? LastDivergence { get; }
    }

    internal sealed class AgnusBeamDmaScheduler
    {
        private readonly AmigaBus _bus;
        private readonly IAgnusChipSlotTiming _chipSlots;
        private long _currentCycle;
        private long _cpuChipStallCycles;

        public AgnusBeamDmaScheduler(AmigaBus bus, IAgnusChipSlotTiming chipSlots)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _chipSlots = chipSlots ?? throw new ArgumentNullException(nameof(chipSlots));
        }

        public long CurrentCycle => _currentCycle;

        public void Reset()
        {
            _currentCycle = 0;
            _cpuChipStallCycles = 0;
            _chipSlots.AdvanceTo(0);
            _bus.Display.ResetLiveDma();
        }

        public void AdvanceTo(long targetCycle)
        {
            targetCycle = Math.Max(0, targetCycle);
            if (targetCycle < _currentCycle)
            {
                return;
            }

            _bus.Display.AdvanceLiveDmaTo(targetCycle);
            _chipSlots.AdvanceTo(targetCycle);
            _currentCycle = targetCycle;
        }

        public void RecordCpuChipAccess(AmigaBusAccessResult access)
        {
            if (access.Request.Requester != AmigaBusRequester.Cpu)
            {
                return;
            }

            if (access.Request.Target != AmigaBusAccessTarget.ChipRam &&
                access.Request.Target != AmigaBusAccessTarget.ExpansionRam &&
                access.Request.Target != AmigaBusAccessTarget.CustomRegisters)
            {
                return;
            }

            if (access.WaitCycles > 0)
            {
                _cpuChipStallCycles += access.WaitCycles;
            }
        }

        public AgnusBeamDmaSnapshot CaptureSnapshot()
        {
            var display = _bus.Display.CaptureSnapshot();
            return new AgnusBeamDmaSnapshot(
                _chipSlots.CurrentCycle,
                _chipSlots.FrameStartCycle,
                _chipSlots.FrameNumber,
                _chipSlots.BeamLine,
                _chipSlots.BeamHorizontal,
                _cpuChipStallCycles,
                display.LastBitplaneDmaFetches,
                display.LastSpriteDmaFetches,
                display.LastMissedSpriteDmaSlots,
                _chipSlots.ReservationCount,
                _chipSlots.SlotGrantCount,
                _chipSlots.GetReservationCount(AgnusChipSlotOwner.Cpu),
                _chipSlots.GetReservationCount(AgnusChipSlotOwner.Blitter),
                _chipSlots.GetReservationCount(AgnusChipSlotOwner.Copper),
                _chipSlots.GetReservationCount(AgnusChipSlotOwner.Paula),
                _chipSlots.GetReservationCount(AgnusChipSlotOwner.Disk),
                _chipSlots.GetReservationCount(AgnusChipSlotOwner.Sprite),
                _chipSlots.GetReservationCount(AgnusChipSlotOwner.Bitplane),
                _chipSlots.GetSlotGrantCount(AgnusChipSlotOwner.Cpu),
                _chipSlots.GetSlotGrantCount(AgnusChipSlotOwner.Blitter),
                _chipSlots.GetSlotGrantCount(AgnusChipSlotOwner.Copper),
                _chipSlots.GetSlotGrantCount(AgnusChipSlotOwner.Paula),
                _chipSlots.GetSlotGrantCount(AgnusChipSlotOwner.Disk),
                _chipSlots.GetSlotGrantCount(AgnusChipSlotOwner.Sprite),
                _chipSlots.GetSlotGrantCount(AgnusChipSlotOwner.Bitplane),
                _chipSlots.LastReservation,
                _chipSlots.LastGrantedSlot,
                _chipSlots.LastDeniedFixedSlot,
                _chipSlots.LastDeniedFixedSlotBlocker,
                _chipSlots.DeniedFixedSlotCount,
                _chipSlots.GetDeniedFixedSlotCount(AgnusChipSlotOwner.Cpu),
                _chipSlots.GetDeniedFixedSlotCount(AgnusChipSlotOwner.Blitter),
                _chipSlots.GetDeniedFixedSlotCount(AgnusChipSlotOwner.Copper),
                _chipSlots.GetDeniedFixedSlotCount(AgnusChipSlotOwner.Paula),
                _chipSlots.GetDeniedFixedSlotCount(AgnusChipSlotOwner.Disk),
                _chipSlots.GetDeniedFixedSlotCount(AgnusChipSlotOwner.Sprite),
                _chipSlots.GetDeniedFixedSlotCount(AgnusChipSlotOwner.Bitplane),
                _chipSlots.GetDeniedFixedSlotBlockerCount(AgnusChipSlotOwner.Cpu),
                _chipSlots.GetDeniedFixedSlotBlockerCount(AgnusChipSlotOwner.Blitter),
                _chipSlots.GetDeniedFixedSlotBlockerCount(AgnusChipSlotOwner.Copper),
                _chipSlots.GetDeniedFixedSlotBlockerCount(AgnusChipSlotOwner.Paula),
                _chipSlots.GetDeniedFixedSlotBlockerCount(AgnusChipSlotOwner.Disk),
                _chipSlots.GetDeniedFixedSlotBlockerCount(AgnusChipSlotOwner.Sprite),
                _chipSlots.GetDeniedFixedSlotBlockerCount(AgnusChipSlotOwner.Bitplane),
                _chipSlots.DivergenceCount,
                _chipSlots.LastDivergence);
        }
    }

    internal sealed class BoundedBusAccessLog : IReadOnlyList<AmigaBusAccessResult>
    {
        private readonly AmigaBusAccessResult[] _buffer;
        private int _start;
        private int _count;

        public BoundedBusAccessLog(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
            }

            _buffer = new AmigaBusAccessResult[capacity];
        }

        public int Count => _count;

        public AmigaBusAccessResult this[int index]
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

        public void Add(AmigaBusAccessResult result)
        {
            if (_count < _buffer.Length)
            {
                _buffer[(_start + _count) % _buffer.Length] = result;
                _count++;
                return;
            }

            _buffer[_start] = result;
            _start = (_start + 1) % _buffer.Length;
        }

        public void Clear()
        {
            _start = 0;
            _count = 0;
        }

        public IEnumerator<AmigaBusAccessResult> GetEnumerator()
        {
            for (var i = 0; i < _count; i++)
            {
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    internal readonly struct PaulaDmaReadResult
    {
        public PaulaDmaReadResult(ushort value, AmigaBusAccessResult busAccess)
        {
            Value = value;
            BusAccess = busAccess;
        }

        public ushort Value { get; }

        public AmigaBusAccessResult BusAccess { get; }
    }

    internal readonly struct AmigaDeviceWordReadResult
    {
        public AmigaDeviceWordReadResult(ushort value, AmigaBusAccessResult busAccess)
        {
            Value = value;
            BusAccess = busAccess;
        }

        public ushort Value { get; }

        public AmigaBusAccessResult BusAccess { get; }
    }
}
