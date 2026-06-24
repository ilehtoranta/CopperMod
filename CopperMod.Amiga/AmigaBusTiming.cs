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
        RealTimeClock,
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

    internal static class CiaPeripheralAccessTiming
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long AlignToCiaPeripheralAccessCycle(long requestedCycle)
        {
            var cycle = Math.Max(0, requestedCycle + 1);
            var remainder = cycle % AmigaConstants.A500PalCpuCyclesPerCiaTick;
            return remainder == 0
                ? cycle
                : cycle + AmigaConstants.A500PalCpuCyclesPerCiaTick - remainder;
        }
    }

    internal enum AgnusChipSlotPriority
    {
        Cpu = 0,
        Blitter = 1,
        Copper = 2,
        Paula = 3,
        Disk = 4,
        Sprite = 5,
        Bitplane = 6,
        Refresh = 7
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
        Host,
        Refresh
    }

    internal static class AgnusChipSlotOwners
    {
        public const int Count = (int)AgnusChipSlotOwner.Refresh + 1;
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

    internal readonly struct AmigaBusAccessRequest
    {
        public AmigaBusAccessRequest(
            AmigaBusRequester requester,
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            int channel = -1)
        {
            Requester = requester;
            Kind = kind;
            Target = target;
            Address = address;
            Size = size;
            RequestedCycle = requestedCycle;
            IsWrite = isWrite;
            Channel = channel;
        }

        public AmigaBusRequester Requester { get; }

        public AmigaBusAccessKind Kind { get; }

        public AmigaBusAccessTarget Target { get; }

        public uint Address { get; }

        public AmigaBusAccessSize Size { get; }

        public long RequestedCycle { get; }

        public bool IsWrite { get; }

        public int Channel { get; }
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

        bool TryReserveExactFixedDmaSlot(AmigaBusAccessRequest request, out AmigaBusAccessResult result);

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

        int ReservationCount { get; }

        int GetReservationCount(AgnusChipSlotOwner owner);

        long SlotGrantCount { get; }

        long GetSlotGrantCount(AgnusChipSlotOwner owner);

        long NextMandatoryRefreshCycle { get; }

        long CurrentCycle { get; }

        long FrameStartCycle { get; }

        int FrameNumber { get; }

        int BeamLine { get; }

        int BeamHorizontal { get; }

        void PruneBefore(long cycle);
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
            var frame = (int)Math.Min(int.MaxValue, cycle / AmigaConstants.A500PalCpuCyclesPerFrame);	// Max 81 years
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

    internal static class AgnusChipSlotScheduler
    {
        public const int SlotCycles = AmigaConstants.A500PalCpuCyclesPerColorClock;

        public static long AlignToSlot(long cycle)
        {
            System.Diagnostics.Debug.Assert(cycle >= 0, "Agnus slot cycles must be non-negative.");
            var remainder = cycle % SlotCycles;
            return remainder == 0 ? cycle : cycle + (SlotCycles - remainder);
        }
    }
    internal static class AgnusHrmOcsSlotTable
    {
        public const int RefreshSlotsPerLine = 4;
        public const int DiskSlotsPerLine = 3;
        public const int AudioSlotsPerLine = 4;
        public const int SpriteSlotsPerLine = 16;
        public const int NormalBitplaneSlotsPerLine = 80;
        public const int FirstPaulaHorizontal = 0x10;
        public const int LastPaulaHorizontal = 0x16;
        public const int FirstSpriteHorizontal = 0x18;
        public const int LastSpriteHorizontal = 0x36;

        public static int GetHorizontal(long slotCycle)
        {
            slotCycle = AgnusChipSlotScheduler.AlignToSlot(slotCycle);
            var lineCycle = slotCycle % AmigaConstants.A500PalCpuCyclesPerRasterLine;
            return (int)(lineCycle / AgnusChipSlotScheduler.SlotCycles);
        }

        public static bool IsCpuAccessibleSlot(long slotCycle)
        {
            var absoluteSlot = AgnusChipSlotScheduler.AlignToSlot(slotCycle) / AgnusChipSlotScheduler.SlotCycles;
            return (absoluteSlot & 1) == 0;
        }

        public static bool IsCopperAccessSlot(long slotCycle)
            => (GetHorizontal(slotCycle) & 1) == 0;

        public static AgnusChipSlotOwner GetFixedOwner(int horizontal)
        {
            if (horizontal is 0x00 or 0x02 or 0x04 or 0x06)
            {
                return AgnusChipSlotOwner.Refresh;
            }

            if (horizontal is 0x08 or 0x0A or 0x0C)
            {
                return AgnusChipSlotOwner.Disk;
            }

            if (horizontal >= FirstPaulaHorizontal &&
                horizontal <= LastPaulaHorizontal &&
                ((horizontal - FirstPaulaHorizontal) & 1) == 0)
            {
                return AgnusChipSlotOwner.Paula;
            }

            if (horizontal >= FirstSpriteHorizontal &&
                horizontal <= LastSpriteHorizontal &&
                ((horizontal - FirstSpriteHorizontal) & 1) == 0)
            {
                return AgnusChipSlotOwner.Sprite;
            }

            return AgnusChipSlotOwner.Free;
        }

        public static bool IsMandatoryRefreshSlot(long slotCycle)
        {
            return GetFixedOwner(GetHorizontal(slotCycle)) == AgnusChipSlotOwner.Refresh;
        }

        public static bool IsFixedDmaSlotForOwner(AgnusChipSlotOwner owner, long slotCycle, int channel = -1)
        {
            if (owner == AgnusChipSlotOwner.Bitplane)
            {
                return true;
            }

            var horizontal = GetHorizontal(slotCycle);
            if (owner == AgnusChipSlotOwner.Paula &&
                TryGetPaulaHorizontal(channel, out var paulaHorizontal))
            {
                return horizontal == paulaHorizontal;
            }

            return GetFixedOwner(horizontal) == owner;
        }

        public static long FindNextFixedDmaSlot(long requestedCycle, AgnusChipSlotOwner owner, int channel = -1)
        {
            System.Diagnostics.Debug.Assert(requestedCycle >= 0, "Agnus DMA request cycles must be non-negative.");
            var candidate = AgnusChipSlotScheduler.AlignToSlot(requestedCycle);
            if (owner != AgnusChipSlotOwner.Refresh &&
                owner != AgnusChipSlotOwner.Disk &&
                owner != AgnusChipSlotOwner.Paula &&
                owner != AgnusChipSlotOwner.Sprite)
            {
                return candidate;
            }

            while (!IsFixedDmaSlotForOwner(owner, candidate, channel))
            {
                candidate += AgnusChipSlotScheduler.SlotCycles;
            }

            return candidate;
        }

        private static bool TryGetPaulaHorizontal(int channel, out int horizontal)
        {
            if ((uint)channel < AudioSlotsPerLine)
            {
                horizontal = FirstPaulaHorizontal + (channel * 2);
                return true;
            }

            horizontal = 0;
            return false;
        }    }

    internal sealed class AgnusHrmSlotEngine : IAgnusChipSlotTiming
    {
        private const int SlotCycles = AgnusChipSlotScheduler.SlotCycles;
        private const int SlotsPerFrame = AmigaConstants.A500PalCpuCyclesPerFrame / SlotCycles;
        private const int SlotTableFrames = 2;
        private readonly AgnusHrmCommittedSlot[] _slots = new AgnusHrmCommittedSlot[SlotsPerFrame * SlotTableFrames];
        private readonly long[] _slotGrantCountsByOwner = new long[AgnusChipSlotOwners.Count];
        private readonly int[] _deniedFixedSlotCountsByOwner = new int[AgnusChipSlotOwners.Count];
        private readonly int[] _deniedFixedSlotBlockerCountsByOwner = new int[AgnusChipSlotOwners.Count];
        private AmigaBusAccessResult? _lastReservation;
        private AgnusChipSlotSnapshot? _lastGrantedSlot;
        private AgnusChipSlotSnapshot? _lastDeniedFixedSlot;
        private AgnusChipSlotSnapshot? _lastDeniedFixedSlotBlocker;
        private int _deniedFixedSlotCount;
        private int _niceBlitterCpuMisses;
        private long _slotGrantCount;
        private long _currentCycle;
        private long _nextRefreshCommitCycle;
        private AgnusPalBeamPosition _beam;
        private bool _beamValid;

        public bool BlitterPriorityEnabled { get; set; }

        public void Clear()
        {
            Array.Clear(_slots);
            _lastReservation = null;
            _lastGrantedSlot = null;
            _lastDeniedFixedSlot = null;
            _lastDeniedFixedSlotBlocker = null;
            _deniedFixedSlotCount = 0;
            _niceBlitterCpuMisses = 0;
            Array.Clear(_deniedFixedSlotCountsByOwner);
            Array.Clear(_deniedFixedSlotBlockerCountsByOwner);
            _slotGrantCount = 0;
            Array.Clear(_slotGrantCountsByOwner);
            _currentCycle = 0;
            _nextRefreshCommitCycle = 0;
            _beam = default;
            _beamValid = false;
            BlitterPriorityEnabled = false;
        }

        public void ClearLiveDisplaySlotsFrom(long cycle)
        {
            var firstSlot = FloorToSlot(cycle);
            var lastSlot = FloorToSlot(Math.Max(firstSlot, _currentCycle));
            var earliestSlot = Math.Max(
                0,
                lastSlot - ((long)((SlotsPerFrame * SlotTableFrames) - 1) * SlotCycles));
            var startSlot = Math.Max(firstSlot, earliestSlot);
            for (var slotCycle = startSlot; slotCycle <= lastSlot; slotCycle += SlotCycles)
            {
                var index = GetSlotIndex(slotCycle);
                var slot = _slots[index];
                if (slot.Valid &&
                    slot.SlotCycle == slotCycle &&
                    IsLiveDisplaySlotOwner(slot.Owner))
                {
                    _slots[index] = default;
                }
            }

            if (_lastGrantedSlot is { GrantedCycle: >= 0 } granted &&
                granted.GrantedCycle >= firstSlot &&
                IsLiveDisplaySlotOwner(granted.Owner))
            {
                _lastGrantedSlot = null;
            }

            if (_lastDeniedFixedSlot is { GrantedCycle: var deniedCycle } && deniedCycle >= firstSlot)
            {
                _lastDeniedFixedSlot = null;
                _lastDeniedFixedSlotBlocker = null;
            }

            _beamValid = false;
        }

        public void AdvanceTo(long targetCycle)
        {
            System.Diagnostics.Debug.Assert(targetCycle >= 0, "Agnus slot advance cycles must be non-negative.");
            CommitRefreshSlotsThrough(targetCycle);
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

            var owner = GetOwner(request.Requester);
            if (owner == AgnusChipSlotOwner.Paula ||
                owner == AgnusChipSlotOwner.Disk ||
                owner == AgnusChipSlotOwner.Sprite)
            {
                return ReserveDeviceFixedDmaSlot(request, baseResult, owner);
            }

            var slotCount = GetSlotCount(request.Size);
            var requestedGrant = Math.Max(baseResult.GrantedCycle, request.RequestedCycle);
            var granted = FindFreeSlot(requestedGrant, slotCount, owner, request);
            var slotStride = GetSlotStride(owner);
            for (var slot = 0; slot < slotCount; slot++)
            {
                CommitSlot(granted + (slot * slotStride), request, owner, GetPriority(owner));
            }

            var completed = Math.Max(baseResult.CompletedCycle, GetCompletedCycle(granted, slotCount, slotStride));
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

            var owner = GetOwner(request.Requester);
            var granted = AgnusHrmOcsSlotTable.FindNextFixedDmaSlot(request.RequestedCycle, owner, request.Channel);
            return TryCommitFixedSlot(request, owner, granted, out result);
        }

        [HotPath]
        public bool TryReserveExactFixedDmaSlot(AmigaBusAccessRequest request, out AmigaBusAccessResult result)
        {
            if (!UsesChipSlot(request))
            {
                result = new AmigaBusAccessResult(request, request.RequestedCycle, request.RequestedCycle);
                return true;
            }

            var owner = GetOwner(request.Requester);
            var granted = AgnusChipSlotScheduler.AlignToSlot(request.RequestedCycle);
            if (!AgnusHrmOcsSlotTable.IsFixedDmaSlotForOwner(owner, granted, request.Channel))
            {
                var fixedOwner = AgnusHrmOcsSlotTable.GetFixedOwner(AgnusHrmOcsSlotTable.GetHorizontal(granted));
                result = new AmigaBusAccessResult(request, granted, granted);
                _lastDeniedFixedSlot = new AgnusChipSlotSnapshot(owner, request.Kind, request.Address, request.RequestedCycle, granted, denied: true);
                _lastDeniedFixedSlotBlocker = new AgnusChipSlotSnapshot(fixedOwner, request.Kind, request.Address, request.RequestedCycle, granted, denied: false);
                RecordDeniedFixedSlot(owner, fixedOwner);
                AdvanceTo(granted);
                return false;
            }

            return TryCommitFixedSlot(request, owner, granted, out result);
        }

        internal bool TryReserveFixedDmaSlotThrough(
            AmigaBusAccessRequest request,
            AgnusChipSlotOwner owner,
            long latestGrantCycle,
            out AmigaBusAccessResult result)
        {
            var candidate = AgnusHrmOcsSlotTable.FindNextFixedDmaSlot(request.RequestedCycle, owner, request.Channel);
            while (candidate <= latestGrantCycle)
            {
                if (TryCommitFixedSlot(request, owner, candidate, out result))
                {
                    return true;
                }

                candidate = AgnusHrmOcsSlotTable.FindNextFixedDmaSlot(candidate + SlotCycles, owner, request.Channel);
            }

            result = new AmigaBusAccessResult(request, candidate, candidate);
            return false;
        }

        [HotPath]
        public AmigaBusAccessResult ReserveBitplaneDmaSlot(uint address, long requestedCycle)
        {
            System.Diagnostics.Debug.Assert(requestedCycle >= 0, "Bitplane DMA request cycles must be non-negative.");
            var request = new AmigaBusAccessRequest(
                AmigaBusRequester.Bitplane,
                AmigaBusAccessKind.Bitplane,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                isWrite: false);

            _ = TryCommitFixedSingleWordSlot(
                request,
                AgnusChipSlotOwner.Bitplane,
                AgnusChipSlotScheduler.AlignToSlot(requestedCycle),
                AgnusChipSlotPriority.Bitplane,
                out var result);
            return result;
        }

        [HotPath]
        public AmigaBusAccessResult ReserveCopperDmaSlot(uint address, long requestedCycle)
        {
            System.Diagnostics.Debug.Assert(requestedCycle >= 0, "Copper DMA request cycles must be non-negative.");
            var request = new AmigaBusAccessRequest(
                AmigaBusRequester.Copper,
                AmigaBusAccessKind.Copper,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                isWrite: false);
            var granted = FindFreeCopperSlot(requestedCycle, request);
            CommitSlot(granted, request, AgnusChipSlotOwner.Copper, AgnusChipSlotPriority.Copper);
            var result = new AmigaBusAccessResult(request, granted, granted + SlotCycles);
            _lastReservation = result;
            _lastGrantedSlot = new AgnusChipSlotSnapshot(AgnusChipSlotOwner.Copper, request.Kind, request.Address, request.RequestedCycle, granted, denied: false);
            AdvanceTo(result.CompletedCycle);
            return result;
        }

        private long FindFreeCopperSlot(long requestedCycle, AmigaBusAccessRequest request)
        {
            System.Diagnostics.Debug.Assert(requestedCycle >= 0, "Copper DMA request cycles must be non-negative.");
            var candidate = AgnusChipSlotScheduler.AlignToSlot(requestedCycle);
            while (true)
            {
                if (!AgnusHrmOcsSlotTable.IsCopperAccessSlot(candidate))
                {
                    candidate += SlotCycles;
                    continue;
                }

                CommitRefreshSlotsThrough(candidate);
                if (!TryGetSlot(candidate, out var existing) ||
                    existing.Matches(request) ||
                    existing.Priority < AgnusChipSlotPriority.Copper)
                {
                    return candidate;
                }

                candidate += SlotCycles;
            }
        }

        public bool IsReserved(long cycle)
        {
            System.Diagnostics.Debug.Assert(cycle >= 0, "Agnus slot cycles must be non-negative.");
            var slotCycle = AgnusChipSlotScheduler.AlignToSlot(cycle);
            return AgnusHrmOcsSlotTable.IsMandatoryRefreshSlot(slotCycle) || TryGetSlot(slotCycle, out _);
        }

        public bool IsFixedDmaReserved(long cycle)
        {
            System.Diagnostics.Debug.Assert(cycle >= 0, "Agnus slot cycles must be non-negative.");
            var slotCycle = AgnusChipSlotScheduler.AlignToSlot(cycle);
            if (AgnusHrmOcsSlotTable.IsMandatoryRefreshSlot(slotCycle))
            {
                return true;
            }

            return TryGetSlot(slotCycle, out var slot) && IsFixedDmaOwner(slot.Owner);
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

        public long NextMandatoryRefreshCycle => _nextRefreshCommitCycle;

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

        private AmigaBusAccessResult ReserveDeviceFixedDmaSlot(
            AmigaBusAccessRequest request,
            AmigaBusAccessResult baseResult,
            AgnusChipSlotOwner owner)
        {
            var candidate = AgnusHrmOcsSlotTable.FindNextFixedDmaSlot(Math.Max(baseResult.GrantedCycle, request.RequestedCycle), owner, request.Channel);
            AmigaBusAccessResult result;
            while (!TryCommitFixedSlot(request, owner, candidate, out result))
            {
                candidate = AgnusHrmOcsSlotTable.FindNextFixedDmaSlot(candidate + SlotCycles, owner, request.Channel);
            }

            var completed = Math.Max(baseResult.CompletedCycle, result.CompletedCycle);
            if (completed == result.CompletedCycle)
            {
                return result;
            }

            var adjusted = new AmigaBusAccessResult(request, result.GrantedCycle, completed);
            _lastReservation = adjusted;
            AdvanceTo(completed);
            return adjusted;
        }

        private bool TryCommitFixedSlot(
            AmigaBusAccessRequest request,
            AgnusChipSlotOwner owner,
            long granted,
            out AmigaBusAccessResult result)
        {
            granted = AgnusChipSlotScheduler.AlignToSlot(granted);
            CommitRefreshSlotsThrough(granted);
            var slotCount = GetSlotCount(request.Size);
            var slotStride = GetSlotStride(owner);
            var priority = GetPriority(owner);
            if (!CanCommitFixedSlot(request, owner, granted, slotCount, slotStride, priority, out var blocker))
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
                CommitSlot(granted + (slot * slotStride), request, owner, priority);
            }

            result = new AmigaBusAccessResult(request, granted, GetCompletedCycle(granted, slotCount, slotStride));
            _lastReservation = result;
            _lastGrantedSlot = new AgnusChipSlotSnapshot(owner, request.Kind, request.Address, request.RequestedCycle, granted, denied: false);
            AdvanceTo(result.CompletedCycle);
            return true;
        }

        private bool TryCommitFixedSingleWordSlot(
            AmigaBusAccessRequest request,
            AgnusChipSlotOwner owner,
            long granted,
            AgnusChipSlotPriority priority,
            out AmigaBusAccessResult result)
        {
            granted = AgnusChipSlotScheduler.AlignToSlot(granted);
            CommitRefreshSlotsThrough(granted);
            if (TryGetSlot(granted, out var existing) &&
                !existing.Matches(request) &&
                !existing.MatchesDisplayDmaReservation(owner, request) &&
                existing.Priority >= priority)
            {
                result = new AmigaBusAccessResult(request, granted, granted);
                _lastDeniedFixedSlot = new AgnusChipSlotSnapshot(owner, request.Kind, request.Address, request.RequestedCycle, granted, denied: true);
                _lastDeniedFixedSlotBlocker = existing.ToSnapshot(denied: false);
                RecordDeniedFixedSlot(owner, existing.Owner);
                AdvanceTo(granted);
                return false;
            }

            CommitSlot(granted, request, owner, priority);
            result = new AmigaBusAccessResult(request, granted, granted + SlotCycles);
            _lastReservation = result;
            _lastGrantedSlot = new AgnusChipSlotSnapshot(owner, request.Kind, request.Address, request.RequestedCycle, granted, denied: false);
            AdvanceTo(result.CompletedCycle);
            return true;
        }

        private long FindFreeSlot(
            long requestedCycle,
            int slotCount,
            AgnusChipSlotOwner owner,
            AmigaBusAccessRequest request)
        {
            System.Diagnostics.Debug.Assert(requestedCycle >= 0, "Agnus slot search cycles must be non-negative.");
            if (owner == AgnusChipSlotOwner.Cpu && slotCount == 1)
            {
                return FindFreeCpuSingleSlot(requestedCycle, request);
            }

            var candidate = AgnusChipSlotScheduler.AlignToSlot(requestedCycle);
            while (true)
            {
                if (RequiresEvenSlot(owner) && !AgnusHrmOcsSlotTable.IsCpuAccessibleSlot(candidate))
                {
                    candidate += SlotCycles;
                    continue;
                }

                var slotStride = GetSlotStride(owner);
                CommitRefreshSlotsThrough(candidate + ((slotCount - 1) * slotStride));
                if (AreSlotsAvailable(candidate, slotCount, owner, request, GetPriority(owner)))
                {
                    return candidate;
                }

                candidate += SlotCycles;
            }
        }

        private long FindFreeCpuSingleSlot(long requestedCycle, AmigaBusAccessRequest request)
        {
            var candidate = AgnusChipSlotScheduler.AlignToSlot(requestedCycle);
            if (!AgnusHrmOcsSlotTable.IsCpuAccessibleSlot(candidate))
            {
                candidate += SlotCycles;
            }

            while (true)
            {
                CommitRefreshSlotsThrough(candidate);
                if (!TryGetSlot(candidate, out var existing) ||
                    existing.Matches(request) ||
                    existing.Priority < AgnusChipSlotPriority.Cpu)
                {
                    _niceBlitterCpuMisses = 0;
                    return candidate;
                }

                if (existing.Owner == AgnusChipSlotOwner.Blitter && !BlitterPriorityEnabled)
                {
                    _niceBlitterCpuMisses++;
                    if (_niceBlitterCpuMisses >= 3)
                    {
                        _niceBlitterCpuMisses = 0;
                        return candidate;
                    }
                }
                else
                {
                    _niceBlitterCpuMisses = 0;
                }

                candidate += SlotCycles * 2;
            }
        }

        private bool AreSlotsAvailable(
            long firstSlot,
            int slotCount,
            AgnusChipSlotOwner owner,
            AmigaBusAccessRequest request,
            AgnusChipSlotPriority priority)
        {
            var slotStride = GetSlotStride(owner);
            for (var slot = 0; slot < slotCount; slot++)
            {
                var slotCycle = firstSlot + (slot * slotStride);
                if (RequiresEvenSlot(owner) && !AgnusHrmOcsSlotTable.IsCpuAccessibleSlot(slotCycle))
                {
                    return false;
                }

                if (!TryGetSlot(slotCycle, out var existing))
                {
                    continue;
                }

                if (existing.Matches(request) ||
                    existing.MatchesDisplayDmaReservation(owner, request))
                {
                    continue;
                }

                if (existing.Priority >= priority)
                {
                    return false;
                }
            }

            return true;
        }

        private bool CanCommitFixedSlot(
            AmigaBusAccessRequest request,
            AgnusChipSlotOwner owner,
            long firstSlot,
            int slotCount,
            int slotStride,
            AgnusChipSlotPriority priority,
            out AgnusHrmCommittedSlot blocker)
        {
            for (var slot = 0; slot < slotCount; slot++)
            {
                if (!TryGetSlot(firstSlot + (slot * slotStride), out var existing))
                {
                    continue;
                }

                if (existing.Matches(request) ||
                    existing.MatchesDisplayDmaReservation(owner, request))
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

        private void CommitRefreshSlotsThrough(long targetCycle)
        {
            var targetSlot = FloorToSlot(targetCycle);
            while (_nextRefreshCommitCycle <= targetSlot)
            {
                var request = new AmigaBusAccessRequest(
                    AmigaBusRequester.Host,
                    AmigaBusAccessKind.HostTrap,
                    AmigaBusAccessTarget.ChipRam,
                    0,
                    AmigaBusAccessSize.Word,
                    _nextRefreshCommitCycle,
                    isWrite: false);
                CommitSlot(_nextRefreshCommitCycle, request, AgnusChipSlotOwner.Refresh, AgnusChipSlotPriority.Refresh);
                _nextRefreshCommitCycle = GetNextRefreshSlotAtOrAfter(_nextRefreshCommitCycle + SlotCycles);
            }
        }

        private static long FloorToSlot(long cycle)
        {
            System.Diagnostics.Debug.Assert(cycle >= 0, "Agnus slot cycles must be non-negative.");
            return cycle - (cycle % SlotCycles);
        }

        private static long GetNextRefreshSlotAtOrAfter(long cycle)
        {
            cycle = FloorToSlot(Math.Max(0, cycle));
            var lineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;
            var lineStart = cycle - (cycle % lineCycles);
            var horizontal = (int)((cycle - lineStart) / SlotCycles);
            var refreshHorizontal = horizontal <= 0
                ? 0
                : horizontal <= 2
                    ? 2
                    : horizontal <= 4
                        ? 4
                        : horizontal <= 6
                            ? 6
                            : -1;
            return refreshHorizontal >= 0
                ? lineStart + ((long)refreshHorizontal * SlotCycles)
                : lineStart + lineCycles;
        }

        private void CommitSlot(
            long slotCycle,
            AmigaBusAccessRequest request,
            AgnusChipSlotOwner owner,
            AgnusChipSlotPriority priority)
        {
            var index = GetSlotIndex(slotCycle);
            var existing = _slots[index];
            if (existing.Valid &&
                existing.SlotCycle == slotCycle &&
                (existing.Matches(request) || existing.MatchesDisplayDmaReservation(owner, request)))
            {
                return;
            }

            _slots[index] = new AgnusHrmCommittedSlot(slotCycle, request, owner, priority);
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

        private bool TryGetSlot(long slotCycle, out AgnusHrmCommittedSlot reservation)
        {
            var index = GetSlotIndex(slotCycle);
            reservation = _slots[index];
            return reservation.Valid && reservation.SlotCycle == slotCycle;
        }

        private static int GetSlotIndex(long slotCycle)
        {
            System.Diagnostics.Debug.Assert(slotCycle >= 0, "Agnus slot cycles must be non-negative.");
            var absoluteSlot = slotCycle / SlotCycles;
            return (int)(absoluteSlot % (SlotsPerFrame * SlotTableFrames));
        }

        private static bool UsesChipSlot(AmigaBusAccessRequest request)
        {
            return request.Target == AmigaBusAccessTarget.ChipRam ||
                request.Target == AmigaBusAccessTarget.ExpansionRam ||
                request.Target == AmigaBusAccessTarget.RealTimeClock ||
                request.Target == AmigaBusAccessTarget.CustomRegisters;
        }

        private static int GetSlotCount(AmigaBusAccessSize size)
        {
            return size == AmigaBusAccessSize.Long ? 2 : 1;
        }

        private static int GetSlotStride(AgnusChipSlotOwner owner)
        {
            return RequiresEvenSlot(owner) ? SlotCycles * 2 : SlotCycles;
        }

        private static long GetCompletedCycle(long firstSlot, int slotCount, int slotStride)
        {
            return firstSlot + ((slotCount - 1) * slotStride) + SlotCycles;
        }

        private static bool RequiresEvenSlot(AgnusChipSlotOwner owner)
        {
            return owner == AgnusChipSlotOwner.Cpu ||
                owner == AgnusChipSlotOwner.Copper;
        }

        private static bool IsLiveDisplaySlotOwner(AgnusChipSlotOwner owner)
        {
            return owner == AgnusChipSlotOwner.Bitplane ||
                owner == AgnusChipSlotOwner.Sprite ||
                owner == AgnusChipSlotOwner.Copper;
        }

        private static bool IsFixedDmaOwner(AgnusChipSlotOwner owner)
        {
            return owner == AgnusChipSlotOwner.Refresh ||
                owner == AgnusChipSlotOwner.Bitplane ||
                owner == AgnusChipSlotOwner.Sprite ||
                owner == AgnusChipSlotOwner.Disk ||
                owner == AgnusChipSlotOwner.Paula;
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
                AgnusChipSlotOwner.Refresh => AgnusChipSlotPriority.Refresh,
                AgnusChipSlotOwner.Bitplane => AgnusChipSlotPriority.Bitplane,
                AgnusChipSlotOwner.Sprite => AgnusChipSlotPriority.Sprite,
                AgnusChipSlotOwner.Disk => AgnusChipSlotPriority.Disk,
                AgnusChipSlotOwner.Paula => AgnusChipSlotPriority.Paula,
                AgnusChipSlotOwner.Copper => AgnusChipSlotPriority.Copper,
                AgnusChipSlotOwner.Blitter => AgnusChipSlotPriority.Blitter,
                _ => AgnusChipSlotPriority.Cpu
            };
        }

        private readonly struct AgnusHrmCommittedSlot
        {
            private readonly AmigaBusAccessRequest _request;

            public AgnusHrmCommittedSlot(
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

            public bool MatchesDisplayDmaReservation(AgnusChipSlotOwner owner, AmigaBusAccessRequest request)
            {
                if (Owner != owner ||
                    (owner != AgnusChipSlotOwner.Bitplane && owner != AgnusChipSlotOwner.Sprite))
                {
                    return false;
                }

                return _request.Requester == request.Requester &&
                    _request.Kind == request.Kind;
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
            int refreshSlotReservationCount,
            long cpuSlotGrantCount,
            long blitterSlotGrantCount,
            long copperSlotGrantCount,
            long paulaSlotGrantCount,
            long diskSlotGrantCount,
            long spriteSlotGrantCount,
            long bitplaneSlotGrantCount,
            long refreshSlotGrantCount,
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
            int refreshDeniedFixedSlotCount,
            int cpuDeniedFixedSlotBlockerCount,
            int blitterDeniedFixedSlotBlockerCount,
            int copperDeniedFixedSlotBlockerCount,
            int paulaDeniedFixedSlotBlockerCount,
            int diskDeniedFixedSlotBlockerCount,
            int spriteDeniedFixedSlotBlockerCount,
            int bitplaneDeniedFixedSlotBlockerCount,
            int refreshDeniedFixedSlotBlockerCount)
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
            RefreshSlotReservationCount = refreshSlotReservationCount;
            CpuSlotGrantCount = cpuSlotGrantCount;
            BlitterSlotGrantCount = blitterSlotGrantCount;
            CopperSlotGrantCount = copperSlotGrantCount;
            PaulaSlotGrantCount = paulaSlotGrantCount;
            DiskSlotGrantCount = diskSlotGrantCount;
            SpriteSlotGrantCount = spriteSlotGrantCount;
            BitplaneSlotGrantCount = bitplaneSlotGrantCount;
            RefreshSlotGrantCount = refreshSlotGrantCount;
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
            RefreshDeniedFixedSlotCount = refreshDeniedFixedSlotCount;
            CpuDeniedFixedSlotBlockerCount = cpuDeniedFixedSlotBlockerCount;
            BlitterDeniedFixedSlotBlockerCount = blitterDeniedFixedSlotBlockerCount;
            CopperDeniedFixedSlotBlockerCount = copperDeniedFixedSlotBlockerCount;
            PaulaDeniedFixedSlotBlockerCount = paulaDeniedFixedSlotBlockerCount;
            DiskDeniedFixedSlotBlockerCount = diskDeniedFixedSlotBlockerCount;
            SpriteDeniedFixedSlotBlockerCount = spriteDeniedFixedSlotBlockerCount;
            BitplaneDeniedFixedSlotBlockerCount = bitplaneDeniedFixedSlotBlockerCount;
            RefreshDeniedFixedSlotBlockerCount = refreshDeniedFixedSlotBlockerCount;
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

        public int RefreshSlotReservationCount { get; }

        public long CpuSlotGrantCount { get; }

        public long BlitterSlotGrantCount { get; }

        public long CopperSlotGrantCount { get; }

        public long PaulaSlotGrantCount { get; }

        public long DiskSlotGrantCount { get; }

        public long SpriteSlotGrantCount { get; }

        public long BitplaneSlotGrantCount { get; }

        public long RefreshSlotGrantCount { get; }

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

        public int RefreshDeniedFixedSlotCount { get; }

        public int CpuDeniedFixedSlotBlockerCount { get; }

        public int BlitterDeniedFixedSlotBlockerCount { get; }

        public int CopperDeniedFixedSlotBlockerCount { get; }

        public int PaulaDeniedFixedSlotBlockerCount { get; }

        public int DiskDeniedFixedSlotBlockerCount { get; }

        public int SpriteDeniedFixedSlotBlockerCount { get; }

        public int BitplaneDeniedFixedSlotBlockerCount { get; }

        public int RefreshDeniedFixedSlotBlockerCount { get; }

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
            => AdvanceTo(targetCycle, advanceLiveDisplay: true);

        public void AdvanceTo(long targetCycle, bool advanceLiveDisplay)
        {
            System.Diagnostics.Debug.Assert(targetCycle >= 0, "Agnus beam advance cycles must be non-negative.");
            if (targetCycle < _currentCycle)
            {
                return;
            }

            if (advanceLiveDisplay)
            {
                _bus.Display.AdvanceLiveDmaTo(targetCycle);
            }

            _chipSlots.AdvanceTo(targetCycle);
            _currentCycle = targetCycle;
        }

        public long GetNextWakeCandidateCycle(long currentCycle, long targetCycle, bool includeLiveDisplay)
        {
            if (targetCycle < currentCycle)
            {
                return long.MaxValue;
            }

            var candidate = long.MaxValue;
            var refreshCycle = _chipSlots.NextMandatoryRefreshCycle;
            if (refreshCycle <= targetCycle)
            {
                candidate = refreshCycle <= currentCycle ? currentCycle : refreshCycle;
            }

            if (includeLiveDisplay)
            {
                var displayCycle = _bus.Display.GetNextLiveDisplayWakeCandidateCycle(currentCycle, targetCycle);
                if (displayCycle.HasValue)
                {
                    candidate = Math.Min(candidate, displayCycle.Value);
                }
            }

            return candidate;
        }

        public void RecordCpuChipAccess(AmigaBusAccessResult access)
        {
            if (access.Request.Requester != AmigaBusRequester.Cpu)
            {
                return;
            }

            if (access.Request.Target != AmigaBusAccessTarget.ChipRam &&
                access.Request.Target != AmigaBusAccessTarget.ExpansionRam &&
                access.Request.Target != AmigaBusAccessTarget.RealTimeClock &&
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
                _chipSlots.GetReservationCount(AgnusChipSlotOwner.Refresh),
                _chipSlots.GetSlotGrantCount(AgnusChipSlotOwner.Cpu),
                _chipSlots.GetSlotGrantCount(AgnusChipSlotOwner.Blitter),
                _chipSlots.GetSlotGrantCount(AgnusChipSlotOwner.Copper),
                _chipSlots.GetSlotGrantCount(AgnusChipSlotOwner.Paula),
                _chipSlots.GetSlotGrantCount(AgnusChipSlotOwner.Disk),
                _chipSlots.GetSlotGrantCount(AgnusChipSlotOwner.Sprite),
                _chipSlots.GetSlotGrantCount(AgnusChipSlotOwner.Bitplane),
                _chipSlots.GetSlotGrantCount(AgnusChipSlotOwner.Refresh),
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
                _chipSlots.GetDeniedFixedSlotCount(AgnusChipSlotOwner.Refresh),
                _chipSlots.GetDeniedFixedSlotBlockerCount(AgnusChipSlotOwner.Cpu),
                _chipSlots.GetDeniedFixedSlotBlockerCount(AgnusChipSlotOwner.Blitter),
                _chipSlots.GetDeniedFixedSlotBlockerCount(AgnusChipSlotOwner.Copper),
                _chipSlots.GetDeniedFixedSlotBlockerCount(AgnusChipSlotOwner.Paula),
                _chipSlots.GetDeniedFixedSlotBlockerCount(AgnusChipSlotOwner.Disk),
                _chipSlots.GetDeniedFixedSlotBlockerCount(AgnusChipSlotOwner.Sprite),
                _chipSlots.GetDeniedFixedSlotBlockerCount(AgnusChipSlotOwner.Bitplane),
                _chipSlots.GetDeniedFixedSlotBlockerCount(AgnusChipSlotOwner.Refresh));
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
