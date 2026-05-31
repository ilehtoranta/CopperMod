using System;
using System.Collections;
using System.Collections.Generic;

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

    internal sealed class AgnusChipSlotScheduler
    {
        public const int SlotCycles = AmigaConstants.A500PalCpuCyclesPerColorClock;
        private const int SlotsPerFrame = AmigaConstants.A500PalCpuCyclesPerFrame / SlotCycles;
        private const int SlotTableFrames = 2;
        private readonly AgnusChipSlotReservation[] _reservations = new AgnusChipSlotReservation[SlotsPerFrame * SlotTableFrames];
        private AmigaBusAccessResult? _lastReservation;
        private AgnusChipSlotSnapshot? _lastGrantedSlot;
        private AgnusChipSlotSnapshot? _lastDeniedFixedSlot;
        private int _deniedFixedSlotCount;

        public void Clear()
        {
            Array.Clear(_reservations);
            _lastReservation = null;
            _lastGrantedSlot = null;
            _lastDeniedFixedSlot = null;
            _deniedFixedSlotCount = 0;
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
            if (!CanReserveFixedSlot(request, granted, slotCount, priority))
            {
                result = new AmigaBusAccessResult(request, granted, granted);
                _lastDeniedFixedSlot = new AgnusChipSlotSnapshot(owner, request.Kind, request.Address, request.RequestedCycle, granted, denied: true);
                _deniedFixedSlotCount++;
                return false;
            }

            for (var slot = 0; slot < slotCount; slot++)
            {
                SetReservation(granted + (slot * SlotCycles), request, owner, priority);
            }

            result = new AmigaBusAccessResult(request, granted, granted + (slotCount * SlotCycles));
            _lastReservation = result;
            _lastGrantedSlot = new AgnusChipSlotSnapshot(owner, request.Kind, request.Address, request.RequestedCycle, granted, denied: false);
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
            return result;
        }

        public bool IsReserved(long cycle)
        {
            return TryGetReservation(AlignToSlot(cycle), out _);
        }

        public AmigaBusAccessResult? LastReservation => _lastReservation;

        public AgnusChipSlotSnapshot? LastGrantedSlot => _lastGrantedSlot;

        public AgnusChipSlotSnapshot? LastDeniedFixedSlot => _lastDeniedFixedSlot;

        public int DeniedFixedSlotCount => _deniedFixedSlotCount;

        public int ReservationCount => CountReservations();

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
            AgnusChipSlotPriority priority)
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
                    return false;
                }
            }

            return true;
        }

        private void SetReservation(
            long slotCycle,
            AmigaBusAccessRequest request,
            AgnusChipSlotOwner owner,
            AgnusChipSlotPriority priority)
        {
            var index = GetSlotIndex(slotCycle);
            _reservations[index] = new AgnusChipSlotReservation(slotCycle, request, owner, priority);
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
            AmigaBusAccessResult? lastFixedDmaReservation,
            AgnusChipSlotSnapshot? lastGrantedSlot,
            AgnusChipSlotSnapshot? lastDeniedFixedSlot,
            int deniedFixedSlotCount)
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
            LastFixedDmaReservation = lastFixedDmaReservation;
            LastGrantedSlot = lastGrantedSlot;
            LastDeniedFixedSlot = lastDeniedFixedSlot;
            DeniedFixedSlotCount = deniedFixedSlotCount;
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

        public AmigaBusAccessResult? LastFixedDmaReservation { get; }

        public AgnusChipSlotSnapshot? LastGrantedSlot { get; }

        public AgnusChipSlotSnapshot? LastDeniedFixedSlot { get; }

        public int DeniedFixedSlotCount { get; }
    }

    internal sealed class AgnusBeamDmaScheduler
    {
        private readonly AmigaBus _bus;
        private readonly AgnusChipSlotScheduler _chipSlots;
        private long _currentCycle;
        private long _cpuChipStallCycles;

        public AgnusBeamDmaScheduler(AmigaBus bus, AgnusChipSlotScheduler chipSlots)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _chipSlots = chipSlots ?? throw new ArgumentNullException(nameof(chipSlots));
        }

        public long CurrentCycle => _currentCycle;

        public void Reset()
        {
            _currentCycle = 0;
            _cpuChipStallCycles = 0;
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
            _currentCycle = targetCycle;
        }

        public void RecordCpuChipAccess(AmigaBusAccessResult access)
        {
            if (access.Request.Requester != AmigaBusRequester.Cpu)
            {
                return;
            }

            if (access.Request.Target != AmigaBusAccessTarget.ChipRam &&
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
            var cycleInFrame = _currentCycle % AmigaConstants.A500PalCpuCyclesPerFrame;
            var line = Math.Clamp((int)(cycleInFrame / AmigaConstants.A500PalCpuCyclesPerRasterLine), 0, AmigaConstants.A500PalRasterLines - 1);
            var lineCycle = cycleInFrame - (line * AmigaConstants.A500PalCpuCyclesPerRasterLine);
            var horizontal = Math.Clamp((int)(lineCycle / AmigaConstants.A500PalCpuCyclesPerColorClock), 0, 0xE2);
            var frame = (int)Math.Min(int.MaxValue, _currentCycle / AmigaConstants.A500PalCpuCyclesPerFrame);
            var display = _bus.Display.CaptureSnapshot();
            return new AgnusBeamDmaSnapshot(
                _currentCycle,
                _currentCycle - cycleInFrame,
                frame,
                line,
                horizontal,
                _cpuChipStallCycles,
                display.LastBitplaneDmaFetches,
                display.LastSpriteDmaFetches,
                display.LastMissedSpriteDmaSlots,
                _chipSlots.ReservationCount,
                _chipSlots.LastReservation,
                _chipSlots.LastGrantedSlot,
                _chipSlots.LastDeniedFixedSlot,
                _chipSlots.DeniedFixedSlotCount);
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
