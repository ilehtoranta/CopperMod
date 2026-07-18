/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CopperMod.Amiga.CustomChips.Agnus
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
        RtgVram,
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

    [Flags]
    internal enum AgnusLiveDisplaySlotOwnerMask : byte
    {
        None = 0,
        Copper = 1 << 0,
        Sprite = 1 << 1,
        Bitplane = 1 << 2,
        All = Copper | Sprite | Bitplane
    }

    internal static class AgnusChipSlotOwners
    {
        public const int Count = (int)AgnusChipSlotOwner.Refresh + 1;
    }

    internal enum AgnusSlotAuditSource : byte
    {
        None,
        LiveBitplaneFetch,
        RowBitplanePresentation,
        PreparedBitplaneSlot,
        LiveSpriteFetch,
        ScratchLiveDma
    }

    internal readonly struct AgnusSlotAuditSourceState
    {
        public AgnusSlotAuditSourceState(AgnusSlotAuditSource source, int a, int b, int c)
        {
            Source = source;
            A = a;
            B = b;
            C = c;
        }

        public AgnusSlotAuditSource Source { get; }

        public int A { get; }

        public int B { get; }

        public int C { get; }
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AmigaBusAccessResult(AmigaBusAccessRequest request, long grantedCycle, long completedCycle)
        {
            System.Diagnostics.Debug.Assert(grantedCycle >= request.RequestedCycle, "Bus grant cannot happen before the requested cycle.");
            System.Diagnostics.Debug.Assert(completedCycle >= grantedCycle, "Bus completion cannot happen before the granted cycle.");
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

    internal readonly struct AgnusSlotScheduleAuditEntry
    {
        public AgnusSlotScheduleAuditEntry(
            long sequence,
            long slotCycle,
            AmigaBusRequester requester,
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            AgnusChipSlotOwner owner,
            bool replacedExisting,
            AgnusChipSlotOwner replacedOwner,
            AgnusSlotAuditSource source,
            int sourceA,
            int sourceB,
            int sourceC)
        {
            Sequence = sequence;
            SlotCycle = slotCycle;
            Requester = requester;
            Kind = kind;
            Target = target;
            Address = address;
            Size = size;
            RequestedCycle = requestedCycle;
            IsWrite = isWrite;
            Owner = owner;
            ReplacedExisting = replacedExisting;
            ReplacedOwner = replacedOwner;
            Source = source;
            SourceA = sourceA;
            SourceB = sourceB;
            SourceC = sourceC;
        }

        public long Sequence { get; }

        public long SlotCycle { get; }

        public AmigaBusRequester Requester { get; }

        public AmigaBusAccessKind Kind { get; }

        public AmigaBusAccessTarget Target { get; }

        public uint Address { get; }

        public AmigaBusAccessSize Size { get; }

        public long RequestedCycle { get; }

        public bool IsWrite { get; }

        public AgnusChipSlotOwner Owner { get; }

        public bool ReplacedExisting { get; }

        public AgnusChipSlotOwner ReplacedOwner { get; }

        public AgnusSlotAuditSource Source { get; }

        public int SourceA { get; }

        public int SourceB { get; }

        public int SourceC { get; }

        public long CompletedCycle => SlotCycle + AgnusChipSlotScheduler.SlotCycles;
    }

    internal readonly struct AgnusSlotTimelineSignature : IEquatable<AgnusSlotTimelineSignature>
    {
        public AgnusSlotTimelineSignature(
            int slotCount,
            ulong hash,
            long firstSlotCycle,
            long lastSlotCycle,
            AgnusChipSlotOwner firstOwner,
            AgnusChipSlotOwner lastOwner)
        {
            SlotCount = slotCount;
            Hash = hash;
            FirstSlotCycle = firstSlotCycle;
            LastSlotCycle = lastSlotCycle;
            FirstOwner = firstOwner;
            LastOwner = lastOwner;
        }

        public int SlotCount { get; }

        public ulong Hash { get; }

        public long FirstSlotCycle { get; }

        public long LastSlotCycle { get; }

        public AgnusChipSlotOwner FirstOwner { get; }

        public AgnusChipSlotOwner LastOwner { get; }

        public bool Equals(AgnusSlotTimelineSignature other)
            => SlotCount == other.SlotCount &&
                Hash == other.Hash &&
                FirstSlotCycle == other.FirstSlotCycle &&
                LastSlotCycle == other.LastSlotCycle &&
                FirstOwner == other.FirstOwner &&
                LastOwner == other.LastOwner;

        public override bool Equals(object? obj)
            => obj is AgnusSlotTimelineSignature other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(SlotCount, Hash, FirstSlotCycle, LastSlotCycle, FirstOwner, LastOwner);

        public override string ToString()
            => $"{SlotCount}@{FirstSlotCycle}->{LastSlotCycle}/0x{Hash:X16}/{FirstOwner}->{LastOwner}";
    }

    internal readonly struct AmigaDmaWordReservation
    {
        public AmigaDmaWordReservation(uint address, bool granted, AmigaBusAccessResult access)
        {
            Address = address;
            Granted = granted;
            Access = access;
        }

        public uint Address { get; }

        public bool Granted { get; }

        public AmigaBusAccessResult Access { get; }

        public long RequestedCycle => Access.RequestedCycle;

        public long GrantedCycle => Access.GrantedCycle;

        public long CompletedCycle => Access.CompletedCycle;
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

    internal readonly struct AgnusBeamPosition
    {
        public AgnusBeamPosition(
            long currentCycle,
            long frameStartCycle,
            int frameNumber,
            int beamLine,
            int beamHorizontal,
            int rasterLines = AmigaConstants.A500PalRasterLines,
            long frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame,
            bool isLongFrame = true)
        {
            CurrentCycle = currentCycle;
            FrameStartCycle = frameStartCycle;
            FrameNumber = frameNumber;
            BeamLine = beamLine;
            BeamHorizontal = beamHorizontal;
            RasterLines = rasterLines;
            FrameCycles = frameCycles;
            IsLongFrame = isLongFrame;
        }

        public long CurrentCycle { get; }

        public long FrameStartCycle { get; }

        public int FrameNumber { get; }

        public int BeamLine { get; }

        public int BeamHorizontal { get; }

        public int RasterLines { get; }

        public long FrameCycles { get; }

        public bool IsLongFrame { get; }

        public static AgnusBeamPosition FromCycle(long cycle)
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
            return new AgnusBeamPosition(cycle, frameStartCycle, frame, line, horizontal);
        }
    }

    internal sealed class AgnusBeamClock
    {
        private readonly RasterTiming _timing;
        private int _lineCycles;
        private int _longRasterLines;
        private int _shortRasterLines;
        private long _frameStartCycle;
        private int _frameNumber;
        private int _rasterLines;

        public AgnusBeamClock(RasterTiming timing)
        {
            _timing = timing;
            _lineCycles = timing.CpuCyclesPerLine;
            _longRasterLines = timing.LongFrameLines;
            _shortRasterLines = timing.ShortFrameLines;
            _rasterLines = _longRasterLines;
        }

        public int LineCycles => _lineCycles;

        public void Reset()
        {
            _frameStartCycle = 0;
            _frameNumber = 0;
            _lineCycles = _timing.CpuCyclesPerLine;
            _longRasterLines = _timing.LongFrameLines;
            _shortRasterLines = _timing.ShortFrameLines;
            _rasterLines = _longRasterLines;
        }

        public AgnusBeamPosition GetPosition(long cycle)
        {
            cycle = Math.Max(0, cycle);
            if (cycle < _frameStartCycle)
            {
                var frameCyclesForHistoricalPosition = GetFrameCycles(_rasterLines);
                var historicalFrameNumber = (int)Math.Min(int.MaxValue, cycle / frameCyclesForHistoricalPosition);
                var historicalFrameStart = historicalFrameNumber * frameCyclesForHistoricalPosition;
                return CreatePosition(cycle, historicalFrameStart, historicalFrameNumber, _rasterLines);
            }

            EnsureFrameContains(cycle);
            return CreatePosition(cycle, _frameStartCycle, _frameNumber, _rasterLines);
        }

        private AgnusBeamPosition CreatePosition(
            long cycle,
            long frameStartCycle,
            int frameNumber,
            int rasterLines)
        {
            var frameCycles = GetFrameCycles(rasterLines);
            var cycleInFrame = cycle - frameStartCycle;
            var line = Math.Clamp((int)(cycleInFrame / _lineCycles), 0, rasterLines - 1);
            var lineCycle = cycleInFrame - ((long)line * _lineCycles);
            var horizontal = Math.Clamp(
                (int)(lineCycle / _timing.CpuCyclesPerColorClock),
                0,
                (_lineCycles / _timing.CpuCyclesPerColorClock) - 1);
            return new AgnusBeamPosition(
                cycle,
                frameStartCycle,
                frameNumber,
                line,
                horizontal,
                rasterLines,
                frameCycles,
                rasterLines == _longRasterLines);
        }

        public void ApplyVposw(ushort value, long cycle)
        {
            cycle = Math.Max(0, cycle);
            _ = GetPosition(cycle);
            _rasterLines = (value & 0x8000) != 0 ? _longRasterLines : _shortRasterLines;
            EnsureFrameContains(cycle);
        }

        public void ApplyGeometry(int colorClocksPerLine, int frameLines, long cycle)
        {
            cycle = Math.Max(0, cycle);
            var position = GetPosition(cycle);
            _lineCycles = Math.Max(_timing.CpuCyclesPerColorClock, colorClocksPerLine * _timing.CpuCyclesPerColorClock);
            _longRasterLines = Math.Max(1, frameLines);
            _shortRasterLines = _longRasterLines;
            _rasterLines = _longRasterLines;
            _frameStartCycle = cycle -
                ((long)Math.Min(position.BeamLine, _rasterLines - 1) * _lineCycles) -
                ((long)Math.Min(position.BeamHorizontal, colorClocksPerLine - 1) * _timing.CpuCyclesPerColorClock);
            _frameNumber = position.FrameNumber;
        }

        public void ApplyHorizontalPosition(int horizontal, long cycle)
        {
            cycle = Math.Max(0, cycle);
            var position = GetPosition(cycle);
            var clocksPerLine = _lineCycles / _timing.CpuCyclesPerColorClock;
            horizontal = Math.Clamp(horizontal, 0, clocksPerLine - 1);
            _frameStartCycle = cycle - ((long)position.BeamLine * _lineCycles) -
                ((long)horizontal * _timing.CpuCyclesPerColorClock);
            _frameNumber = position.FrameNumber;
        }

        public long GetNextFrameStartCycle(long cycle)
        {
            var position = GetPosition(cycle);
            return position.FrameStartCycle + position.FrameCycles;
        }

        public long GetFrameStopCycle(long frameStartCycle)
            => Math.Max(0, frameStartCycle) + GetFrameCycles(_rasterLines);

        private void EnsureFrameContains(long cycle)
        {
            var frameCycles = GetFrameCycles(_rasterLines);
            if (cycle < _frameStartCycle)
            {
                _frameStartCycle = (cycle / frameCycles) * frameCycles;
                _frameNumber = (int)Math.Min(int.MaxValue, cycle / frameCycles);
                return;
            }

            while (cycle >= _frameStartCycle + frameCycles)
            {
                _frameStartCycle += frameCycles;
                if (_frameNumber < int.MaxValue)
                {
                    _frameNumber++;
                }
            }
        }

        private long GetFrameCycles(int rasterLines)
            => (long)rasterLines * _lineCycles;
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
        // Internal slot coordinates precede WinUAE's externally reported Agnus
        // hpos by three CCKs, mapping physical refresh hpos 3/5/7/9 to 0/2/4/6.
        public const int FirstRefreshHorizontal = 0x00;
        public const int LastRefreshHorizontal = 0x06;
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
            => (GetHorizontal(slotCycle) & 1) != 0;

        public static bool IsCopperAccessSlot(long slotCycle)
            => (GetHorizontal(slotCycle) & 1) == 0;

        public static AgnusChipSlotOwner GetFixedOwner(int horizontal)
        {
            if (horizontal >= FirstRefreshHorizontal &&
                horizontal <= LastRefreshHorizontal &&
                ((horizontal - FirstRefreshHorizontal) & 1) == 0)
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
        private readonly AgnusHrmCommittedSlotKey[] _slotKeys = new AgnusHrmCommittedSlotKey[SlotsPerFrame * SlotTableFrames];
        private readonly AgnusHrmCommittedSlotDebug[]? _slotDebug;
        private Action<AgnusSlotScheduleAuditEntry>? _slotScheduleAuditSink;
        private readonly long[] _slotGrantCountsByOwner = new long[AgnusChipSlotOwners.Count];
        private readonly int[] _deniedFixedSlotCountsByOwner = new int[AgnusChipSlotOwners.Count];
        private readonly int[] _deniedFixedSlotBlockerCountsByOwner = new int[AgnusChipSlotOwners.Count];
        private AmigaBusAccessResult? _lastReservation;
        private AgnusChipSlotSnapshot? _lastGrantedSlot;
        private AgnusChipSlotSnapshot? _lastDeniedFixedSlot;
        private AgnusChipSlotSnapshot? _lastDeniedFixedSlotBlocker;
        private bool _lastFastCpuReservationValid;
        private AmigaBusAccessKind _lastFastCpuReservationKind;
        private AmigaBusAccessTarget _lastFastCpuReservationTarget;
        private AmigaBusAccessSize _lastFastCpuReservationSize;
        private uint _lastFastCpuReservationAddress;
        private long _lastFastCpuReservationRequestedCycle;
        private long _lastFastCpuReservationGrantedCycle;
        private long _lastFastCpuReservationCompletedCycle;
        private bool _lastFastCpuReservationIsWrite;
        private bool _pendingCpuSlotRequestActive;
        private AmigaBusAccessKind _pendingCpuSlotRequestKind;
        private AmigaBusAccessTarget _pendingCpuSlotRequestTarget;
        private AmigaBusAccessSize _pendingCpuSlotRequestSize;
        private uint _pendingCpuSlotRequestAddress;
        private long _pendingCpuSlotRequestCycle;
        private bool _pendingCpuSlotRequestIsWrite;
        private int _pendingCpuSlotRequestBlitterMisses;
        private int _deniedFixedSlotCount;
        private int _niceBlitterCpuMisses;
        private long _slotGrantCount;
        private long _slotScheduleAuditSequence;
        private long _currentCycle;
        private long _nextRefreshCommitCycle =
            AgnusHrmOcsSlotTable.FirstRefreshHorizontal * AgnusChipSlotScheduler.SlotCycles;
        private AgnusBeamPosition _beam;
        private bool _beamValid;
        private AgnusSlotAuditSource _slotScheduleAuditSource;
        private int _slotScheduleAuditSourceA = -1;
        private int _slotScheduleAuditSourceB = -1;
        private int _slotScheduleAuditSourceC = -1;

        public AgnusHrmSlotEngine(bool captureSlotDebug = false)
        {
            _slotDebug = captureSlotDebug
                ? new AgnusHrmCommittedSlotDebug[SlotsPerFrame * SlotTableFrames]
                : null;
        }

        private AgnusHrmSlotEngine(AgnusHrmSlotEngine source)
        {
            Array.Copy(source._slots, _slots, source._slots.Length);
            Array.Copy(source._slotKeys, _slotKeys, source._slotKeys.Length);
            if (source._slotDebug != null)
            {
                _slotDebug = new AgnusHrmCommittedSlotDebug[source._slotDebug.Length];
                Array.Copy(source._slotDebug, _slotDebug, source._slotDebug.Length);
            }

            Array.Copy(source._slotGrantCountsByOwner, _slotGrantCountsByOwner, source._slotGrantCountsByOwner.Length);
            Array.Copy(source._deniedFixedSlotCountsByOwner, _deniedFixedSlotCountsByOwner, source._deniedFixedSlotCountsByOwner.Length);
            Array.Copy(source._deniedFixedSlotBlockerCountsByOwner, _deniedFixedSlotBlockerCountsByOwner, source._deniedFixedSlotBlockerCountsByOwner.Length);
            _lastReservation = source._lastReservation;
            _lastGrantedSlot = source._lastGrantedSlot;
            _lastDeniedFixedSlot = source._lastDeniedFixedSlot;
            _lastDeniedFixedSlotBlocker = source._lastDeniedFixedSlotBlocker;
            _lastFastCpuReservationValid = source._lastFastCpuReservationValid;
            _lastFastCpuReservationKind = source._lastFastCpuReservationKind;
            _lastFastCpuReservationTarget = source._lastFastCpuReservationTarget;
            _lastFastCpuReservationSize = source._lastFastCpuReservationSize;
            _lastFastCpuReservationAddress = source._lastFastCpuReservationAddress;
            _lastFastCpuReservationRequestedCycle = source._lastFastCpuReservationRequestedCycle;
            _lastFastCpuReservationGrantedCycle = source._lastFastCpuReservationGrantedCycle;
            _lastFastCpuReservationCompletedCycle = source._lastFastCpuReservationCompletedCycle;
            _lastFastCpuReservationIsWrite = source._lastFastCpuReservationIsWrite;
            _pendingCpuSlotRequestActive = source._pendingCpuSlotRequestActive;
            _pendingCpuSlotRequestKind = source._pendingCpuSlotRequestKind;
            _pendingCpuSlotRequestTarget = source._pendingCpuSlotRequestTarget;
            _pendingCpuSlotRequestSize = source._pendingCpuSlotRequestSize;
            _pendingCpuSlotRequestAddress = source._pendingCpuSlotRequestAddress;
            _pendingCpuSlotRequestCycle = source._pendingCpuSlotRequestCycle;
            _pendingCpuSlotRequestIsWrite = source._pendingCpuSlotRequestIsWrite;
            _pendingCpuSlotRequestBlitterMisses = source._pendingCpuSlotRequestBlitterMisses;
            _deniedFixedSlotCount = source._deniedFixedSlotCount;
            _niceBlitterCpuMisses = source._niceBlitterCpuMisses;
            _slotGrantCount = source._slotGrantCount;
            _slotScheduleAuditSequence = source._slotScheduleAuditSequence;
            _currentCycle = source._currentCycle;
            _nextRefreshCommitCycle = source._nextRefreshCommitCycle;
            _beam = source._beam;
            _beamValid = source._beamValid;
            _slotScheduleAuditSource = source._slotScheduleAuditSource;
            _slotScheduleAuditSourceA = source._slotScheduleAuditSourceA;
            _slotScheduleAuditSourceB = source._slotScheduleAuditSourceB;
            _slotScheduleAuditSourceC = source._slotScheduleAuditSourceC;
            BlitterPriorityEnabled = source.BlitterPriorityEnabled;
            BeamPositionProvider = source.BeamPositionProvider;
        }

        public bool BlitterPriorityEnabled { get; set; }

        public Func<long, AgnusBeamPosition>? BeamPositionProvider { get; set; }

        internal AgnusSlotAuditSource SlotScheduleAuditSource
        {
            get => _slotScheduleAuditSource;
            set
            {
                _slotScheduleAuditSource = value;
                _slotScheduleAuditSourceA = -1;
                _slotScheduleAuditSourceB = -1;
                _slotScheduleAuditSourceC = -1;
            }
        }

        internal AgnusSlotAuditSourceState SlotScheduleAuditSourceState
        {
            get => new(_slotScheduleAuditSource, _slotScheduleAuditSourceA, _slotScheduleAuditSourceB, _slotScheduleAuditSourceC);
            set
            {
                _slotScheduleAuditSource = value.Source;
                _slotScheduleAuditSourceA = value.A;
                _slotScheduleAuditSourceB = value.B;
                _slotScheduleAuditSourceC = value.C;
            }
        }

        internal AgnusHrmSlotEngine CreateShadowCopy()
            => new(this);

        public void BeginPendingCpuSlotRequest(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite)
        {
            _pendingCpuSlotRequestActive = true;
            _pendingCpuSlotRequestKind = kind;
            _pendingCpuSlotRequestTarget = target;
            _pendingCpuSlotRequestAddress = address;
            _pendingCpuSlotRequestSize = size;
            _pendingCpuSlotRequestCycle = requestedCycle;
            _pendingCpuSlotRequestIsWrite = isWrite;
            _pendingCpuSlotRequestBlitterMisses = 0;
        }

        public void ClearPendingCpuSlotRequest()
        {
            _pendingCpuSlotRequestActive = false;
            _pendingCpuSlotRequestKind = default;
            _pendingCpuSlotRequestTarget = default;
            _pendingCpuSlotRequestAddress = 0;
            _pendingCpuSlotRequestSize = default;
            _pendingCpuSlotRequestCycle = 0;
            _pendingCpuSlotRequestIsWrite = false;
            _pendingCpuSlotRequestBlitterMisses = 0;
        }

        public void SetSlotScheduleAuditSink(Action<AgnusSlotScheduleAuditEntry>? sink)
        {
            _slotScheduleAuditSink = sink;
        }

        public void Clear()
        {
            Array.Clear(_slots);
            Array.Clear(_slotKeys);
            if (_slotDebug != null)
            {
                Array.Clear(_slotDebug);
            }
            _lastReservation = null;
            _lastGrantedSlot = null;
            _lastDeniedFixedSlot = null;
            _lastDeniedFixedSlotBlocker = null;
            _lastFastCpuReservationValid = false;
            ClearPendingCpuSlotRequest();
            _deniedFixedSlotCount = 0;
            _niceBlitterCpuMisses = 0;
            Array.Clear(_deniedFixedSlotCountsByOwner);
            Array.Clear(_deniedFixedSlotBlockerCountsByOwner);
            _slotGrantCount = 0;
            _slotScheduleAuditSequence = 0;
            Array.Clear(_slotGrantCountsByOwner);
            _currentCycle = 0;
            _nextRefreshCommitCycle =
                AgnusHrmOcsSlotTable.FirstRefreshHorizontal * AgnusChipSlotScheduler.SlotCycles;
            _beam = default;
            _beamValid = false;
            _slotScheduleAuditSource = AgnusSlotAuditSource.None;
            _slotScheduleAuditSourceA = -1;
            _slotScheduleAuditSourceB = -1;
            _slotScheduleAuditSourceC = -1;
            BlitterPriorityEnabled = false;
            _slotScheduleAuditSink = null;
        }

        public void ClearLiveDisplaySlotsFrom(long cycle, AgnusLiveDisplaySlotOwnerMask owners)
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
                    slot.IsForSlot(slotCycle) &&
                    IsLiveDisplaySlotOwner(slot.Owner, owners))
                {
                    _slots[index] = default;
                    _slotKeys[index] = default;
                    if (_slotDebug != null)
                    {
                        _slotDebug[index] = default;
                    }
                }
            }

            if (_lastGrantedSlot is { GrantedCycle: >= 0 } granted &&
                granted.GrantedCycle >= firstSlot &&
                IsLiveDisplaySlotOwner(granted.Owner, owners))
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
            if (owner == AgnusChipSlotOwner.Cpu && request.Size == AmigaBusAccessSize.Long)
            {
                return ReserveCpuDataLongSlots(request, baseResult, requestedGrant);
            }

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

        private AmigaBusAccessResult ReserveCpuDataLongSlots(
            AmigaBusAccessRequest request,
            AmigaBusAccessResult baseResult,
            long requestedGrant)
        {
            var firstWordCycle = FindFreeCpuSingleSlot(requestedGrant, request);
            var secondWordCycle = FindFreeCpuSingleSlot(firstWordCycle + (2 * SlotCycles), request);
            CommitSlot(firstWordCycle, request, AgnusChipSlotOwner.Cpu, AgnusChipSlotPriority.Cpu);
            CommitSlot(secondWordCycle, request, AgnusChipSlotOwner.Cpu, AgnusChipSlotPriority.Cpu);

            var completed = Math.Max(baseResult.CompletedCycle, secondWordCycle + SlotCycles);
            var result = new AmigaBusAccessResult(request, firstWordCycle, completed);
            _lastReservation = result;
            _lastGrantedSlot = new AgnusChipSlotSnapshot(
                AgnusChipSlotOwner.Cpu,
                request.Kind,
                request.Address,
                request.RequestedCycle,
                firstWordCycle,
                denied: false);
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

        [HotPath]
        public AmigaBusAccessResult ReserveBlitterDmaWordSlot(uint address, long requestedCycle, bool isWrite)
        {
            System.Diagnostics.Debug.Assert(requestedCycle >= 0, "Blitter DMA request cycles must be non-negative.");
            var request = new AmigaBusAccessRequest(
                AmigaBusRequester.Blitter,
                AmigaBusAccessKind.Blitter,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                isWrite);
            var granted = FindFreeBlitterSlot(requestedCycle, request, out var deniedResult);
            if (deniedResult.HasValue)
            {
                return deniedResult.Value;
            }

            CommitSlot(
                granted,
                request,
                AgnusChipSlotOwner.Blitter,
                AgnusChipSlotPriority.Blitter);
            var result = new AmigaBusAccessResult(request, granted, granted + SlotCycles);
            _lastReservation = result;
            _lastGrantedSlot = new AgnusChipSlotSnapshot(AgnusChipSlotOwner.Blitter, request.Kind, request.Address, request.RequestedCycle, granted, denied: false);
            AdvanceTo(result.CompletedCycle);
            return result;
        }

        public bool TryReserveBlitterDmaWordExactSlot(
            uint address,
            long requestedCycle,
            long slotCycle,
            bool isWrite,
            out AmigaBusAccessResult result)
        {
            System.Diagnostics.Debug.Assert(requestedCycle >= 0, "Blitter DMA request cycles must be non-negative.");
            var request = new AmigaBusAccessRequest(
                AmigaBusRequester.Blitter,
                AmigaBusAccessKind.Blitter,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                isWrite);
            slotCycle = AgnusChipSlotScheduler.AlignToSlot(Math.Max(0, slotCycle));
            if (slotCycle < requestedCycle)
            {
                result = new AmigaBusAccessResult(request, slotCycle, slotCycle);
                _lastDeniedFixedSlot = new AgnusChipSlotSnapshot(
                    AgnusChipSlotOwner.Blitter,
                    request.Kind,
                    request.Address,
                    request.RequestedCycle,
                    slotCycle,
                    denied: true);
                _lastDeniedFixedSlotBlocker = new AgnusChipSlotSnapshot(
                    AgnusChipSlotOwner.Cpu,
                    request.Kind,
                    request.Address,
                    request.RequestedCycle,
                    slotCycle,
                    denied: false);
                RecordDeniedFixedSlot(AgnusChipSlotOwner.Blitter, AgnusChipSlotOwner.Cpu);
                AdvanceTo(slotCycle);
                return false;
            }

            CommitRefreshSlotsThrough(slotCycle);
            if (PendingCpuRequestClaimsBlitterCandidate(slotCycle))
            {
                result = new AmigaBusAccessResult(request, slotCycle, slotCycle);
                _lastDeniedFixedSlot = new AgnusChipSlotSnapshot(
                    AgnusChipSlotOwner.Blitter,
                    request.Kind,
                    request.Address,
                    request.RequestedCycle,
                    slotCycle,
                    denied: true);
                _lastDeniedFixedSlotBlocker = new AgnusChipSlotSnapshot(
                    AgnusChipSlotOwner.Cpu,
                    _pendingCpuSlotRequestKind,
                    _pendingCpuSlotRequestAddress,
                    _pendingCpuSlotRequestCycle,
                    slotCycle,
                    denied: false);
                RecordDeniedFixedSlot(AgnusChipSlotOwner.Blitter, AgnusChipSlotOwner.Cpu);
                AdvanceTo(slotCycle);
                return false;
            }

            if (TryGetSlot(slotCycle, out var existing) &&
                !SlotMatchesRequest(slotCycle, existing, request) &&
                existing.Priority >= AgnusChipSlotPriority.Blitter)
            {
                result = new AmigaBusAccessResult(request, slotCycle, slotCycle);
                _lastDeniedFixedSlot = new AgnusChipSlotSnapshot(
                    AgnusChipSlotOwner.Blitter,
                    request.Kind,
                    request.Address,
                    request.RequestedCycle,
                    slotCycle,
                    denied: true);
                _lastDeniedFixedSlotBlocker = GetSlotSnapshot(slotCycle, existing, denied: false);
                RecordDeniedFixedSlot(AgnusChipSlotOwner.Blitter, existing.Owner);
                AdvanceTo(slotCycle);
                return false;
            }

            CommitSlot(
                slotCycle,
                request,
                AgnusChipSlotOwner.Blitter,
                AgnusChipSlotPriority.Blitter);
            result = new AmigaBusAccessResult(request, slotCycle, slotCycle + SlotCycles);
            _lastReservation = result;
            _lastGrantedSlot = new AgnusChipSlotSnapshot(AgnusChipSlotOwner.Blitter, request.Kind, request.Address, request.RequestedCycle, slotCycle, denied: false);
            AdvanceTo(result.CompletedCycle);
            return true;
        }

        [HotPath]
        public void GrantCpuDataSingleSlot(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle,
            out long completedCycle)
        {
            System.Diagnostics.Debug.Assert(size != AmigaBusAccessSize.Long, "Single-slot CPU grant cannot be used for long accesses.");
            System.Diagnostics.Debug.Assert(requestedCycle >= 0, "CPU chip-slot request cycles must be non-negative.");
            grantedCycle = FindFreeCpuSingleSlot(
                requestedCycle,
                kind,
                target,
                address,
                size,
                isWrite);
            CommitSlot(
                grantedCycle,
                AmigaBusRequester.Cpu,
                kind,
                target,
                address,
                size,
                requestedCycle,
                isWrite,
                AgnusChipSlotOwner.Cpu,
                AgnusChipSlotPriority.Cpu);
            completedCycle = grantedCycle + SlotCycles;
            RecordFastCpuReservation(kind, target, address, size, requestedCycle, grantedCycle, completedCycle, isWrite);
            AdvanceTo(completedCycle);
        }

        internal bool TryGrantCpuDataSingleExactSlot(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            long slotCycle,
            bool isWrite,
            bool allowNiceBlitterSteal,
            out long completedCycle)
        {
            System.Diagnostics.Debug.Assert(size != AmigaBusAccessSize.Long, "Single-slot CPU grant cannot be used for long accesses.");
            completedCycle = 0;
            if (requestedCycle < 0)
            {
                return false;
            }

            slotCycle = AgnusChipSlotScheduler.AlignToSlot(slotCycle);
            if (slotCycle < requestedCycle)
            {
                return false;
            }

            CommitRefreshSlotsThrough(slotCycle);
            if (TryGetSlot(slotCycle, out var existing) &&
                !SlotMatchesRequest(slotCycle, existing, AmigaBusRequester.Cpu, kind, target, address, size, requestedCycle, isWrite) &&
                existing.Priority >= AgnusChipSlotPriority.Cpu)
            {
                if (existing.Owner == AgnusChipSlotOwner.Blitter && !BlitterPriorityEnabled && allowNiceBlitterSteal)
                {
                    _niceBlitterCpuMisses++;
                    if (_niceBlitterCpuMisses < 3)
                    {
                        AdvanceTo(slotCycle);
                        return false;
                    }
                }
                else
                {
                    _niceBlitterCpuMisses = 0;
                    AdvanceTo(slotCycle);
                    return false;
                }
            }

            _niceBlitterCpuMisses = 0;
            CommitSlot(
                slotCycle,
                AmigaBusRequester.Cpu,
                kind,
                target,
                address,
                size,
                requestedCycle,
                isWrite,
                AgnusChipSlotOwner.Cpu,
                AgnusChipSlotPriority.Cpu);
            completedCycle = slotCycle + SlotCycles;
            RecordFastCpuReservation(kind, target, address, size, requestedCycle, slotCycle, completedCycle, isWrite);
            AdvanceTo(completedCycle);
            return true;
        }

        internal bool TryPredictCpuDataSingleSlot(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle)
        {
            grantedCycle = 0;
            if (size == AmigaBusAccessSize.Long || requestedCycle < 0)
            {
                return false;
            }

            grantedCycle = PredictFreeCpuSingleSlot(
                requestedCycle,
                kind,
                target,
                address,
                size,
                isWrite);
            return true;
        }

        internal bool TryPredictCpuDataSingleSlotFrom(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long searchCycle,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle)
        {
            grantedCycle = 0;
            if (size == AmigaBusAccessSize.Long || searchCycle < 0 || requestedCycle < 0)
            {
                return false;
            }

            grantedCycle = PredictFreeCpuSingleSlot(
                searchCycle,
                kind,
                target,
                address,
                size,
                isWrite,
                requestedCycle);
            return true;
        }

        internal bool TryPredictCpuDataLongSlots(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            long requestedCycle,
            bool isWrite,
            out long firstWordCycle,
            out long secondWordCycle,
            out long completedCycle)
        {
            firstWordCycle = 0;
            secondWordCycle = 0;
            completedCycle = 0;
            if (requestedCycle < 0)
            {
                return false;
            }

            firstWordCycle = PredictFreeCpuSingleSlot(
                requestedCycle,
                kind,
                target,
                address,
                AmigaBusAccessSize.Long,
                isWrite);
            secondWordCycle = PredictFreeCpuSingleSlot(
                firstWordCycle + (2 * SlotCycles),
                kind,
                target,
                address,
                AmigaBusAccessSize.Long,
                isWrite);
            completedCycle = secondWordCycle + SlotCycles;
            return true;
        }

        internal bool TryPredictCpuDataLongWordPhaseSlot(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            long searchCycle,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle)
        {
            grantedCycle = 0;
            if (searchCycle < 0 || requestedCycle < 0)
            {
                return false;
            }

            grantedCycle = PredictFreeCpuSingleSlot(
                searchCycle,
                kind,
                target,
                address,
                AmigaBusAccessSize.Long,
                isWrite,
                requestedCycle);
            return true;
        }

        private long PredictFreeCpuSingleSlot(
            long requestedCycle,
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            bool isWrite)
            => PredictFreeCpuSingleSlot(
                requestedCycle,
                kind,
                target,
                address,
                size,
                isWrite,
                requestedCycle);

        private long PredictFreeCpuSingleSlot(
            long searchCycle,
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            bool isWrite,
            long requestedCycle)
        {
            var candidate = AgnusChipSlotScheduler.AlignToSlot(searchCycle);
            var niceBlitterCpuMisses = _niceBlitterCpuMisses;
            while (true)
            {
                if (AgnusHrmOcsSlotTable.IsMandatoryRefreshSlot(candidate))
                {
                    niceBlitterCpuMisses = 0;
                    candidate += SlotCycles;
                    continue;
                }

                if (!TryGetSlot(candidate, out var existing) ||
                    SlotMatchesRequest(candidate, existing, AmigaBusRequester.Cpu, kind, target, address, size, requestedCycle, isWrite) ||
                    existing.Priority < AgnusChipSlotPriority.Cpu)
                {
                    return candidate;
                }

                if (existing.Owner == AgnusChipSlotOwner.Blitter && !BlitterPriorityEnabled)
                {
                    niceBlitterCpuMisses++;
                    if (niceBlitterCpuMisses >= 3)
                    {
                        return candidate;
                    }
                }
                else
                {
                    niceBlitterCpuMisses = 0;
                }

                candidate += existing.Owner == AgnusChipSlotOwner.Copper
                    ? 2 * SlotCycles
                    : SlotCycles;
            }
        }

        private long PredictFreeCpuMultiSlot(
            long requestedCycle,
            int slotCount,
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            bool isWrite)
        {
            var candidate = AgnusChipSlotScheduler.AlignToSlot(requestedCycle);
            while (true)
            {
                if (AreCpuSlotsAvailableForPrediction(
                    candidate,
                    slotCount,
                    kind,
                    target,
                    address,
                    size,
                    requestedCycle,
                    isWrite))
                {
                    return candidate;
                }

                candidate += SlotCycles;
            }
        }

        private bool AreCpuSlotsAvailableForPrediction(
            long firstSlot,
            int slotCount,
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite)
        {
            const int slotStride = SlotCycles * 2;
            for (var slot = 0; slot < slotCount; slot++)
            {
                var slotCycle = firstSlot + (slot * slotStride);
                if (AgnusHrmOcsSlotTable.IsMandatoryRefreshSlot(slotCycle))
                {
                    return false;
                }

                if (!TryGetSlot(slotCycle, out var existing))
                {
                    continue;
                }

                if (SlotMatchesRequest(slotCycle, existing, AmigaBusRequester.Cpu, kind, target, address, size, requestedCycle, isWrite))
                {
                    continue;
                }

                if (existing.Priority >= AgnusChipSlotPriority.Cpu)
                {
                    return false;
                }
            }

            return true;
        }

        internal void GrantCpuDataLongWordPhaseSlot(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            long searchCycle,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle,
            out long completedCycle)
        {
            System.Diagnostics.Debug.Assert(searchCycle >= 0, "CPU chip-slot search cycles must be non-negative.");
            System.Diagnostics.Debug.Assert(requestedCycle >= 0, "CPU chip-slot request cycles must be non-negative.");
            grantedCycle = FindFreeCpuSingleSlot(
                searchCycle,
                kind,
                target,
                address,
                AmigaBusAccessSize.Long,
                isWrite,
                requestedCycle);
            CommitSlot(
                grantedCycle,
                AmigaBusRequester.Cpu,
                kind,
                target,
                address,
                AmigaBusAccessSize.Long,
                requestedCycle,
                isWrite,
                AgnusChipSlotOwner.Cpu,
                AgnusChipSlotPriority.Cpu);
            completedCycle = grantedCycle + SlotCycles;
            RecordFastCpuReservation(kind, target, address, AmigaBusAccessSize.Long, requestedCycle, grantedCycle, completedCycle, isWrite);
            AdvanceTo(completedCycle);
        }

        [HotPath]
        public void GrantCpuDataLongSlots(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            long requestedCycle,
            bool isWrite,
            out long firstWordCycle,
            out long secondWordCycle,
            out long completedCycle)
        {
            System.Diagnostics.Debug.Assert(requestedCycle >= 0, "CPU chip-slot request cycles must be non-negative.");
            firstWordCycle = FindFreeCpuSingleSlot(
                requestedCycle,
                kind,
                target,
                address,
                AmigaBusAccessSize.Long,
                isWrite);
            secondWordCycle = FindFreeCpuSingleSlot(
                firstWordCycle + (2 * SlotCycles),
                kind,
                target,
                address,
                AmigaBusAccessSize.Long,
                isWrite);
            CommitSlot(
                firstWordCycle,
                AmigaBusRequester.Cpu,
                kind,
                target,
                address,
                AmigaBusAccessSize.Long,
                requestedCycle,
                isWrite,
                AgnusChipSlotOwner.Cpu,
                AgnusChipSlotPriority.Cpu);
            CommitSlot(
                secondWordCycle,
                AmigaBusRequester.Cpu,
                kind,
                target,
                address,
                AmigaBusAccessSize.Long,
                requestedCycle,
                isWrite,
                AgnusChipSlotOwner.Cpu,
                AgnusChipSlotPriority.Cpu);
            completedCycle = secondWordCycle + SlotCycles;
            RecordFastCpuReservation(kind, target, address, AmigaBusAccessSize.Long, requestedCycle, firstWordCycle, completedCycle, isWrite);
            AdvanceTo(completedCycle);
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
                    SlotMatchesRequest(candidate, existing, request) ||
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

        internal bool TryGetCommittedSlotOwner(long cycle, out AgnusChipSlotOwner owner)
        {
            var slotCycle = AgnusChipSlotScheduler.AlignToSlot(Math.Max(0, cycle));
            if (AgnusHrmOcsSlotTable.IsMandatoryRefreshSlot(slotCycle))
            {
                owner = AgnusChipSlotOwner.Refresh;
                return true;
            }

            if (TryGetSlot(slotCycle, out var slot))
            {
                owner = slot.Owner;
                return true;
            }

            owner = AgnusChipSlotOwner.Free;
            return false;
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

        public AmigaBusAccessResult? LastReservation
        {
            get
            {
                if (_lastReservation.HasValue)
                {
                    return _lastReservation;
                }

                if (!_lastFastCpuReservationValid)
                {
                    return null;
                }

                var request = new AmigaBusAccessRequest(
                    AmigaBusRequester.Cpu,
                    _lastFastCpuReservationKind,
                    _lastFastCpuReservationTarget,
                    _lastFastCpuReservationAddress,
                    _lastFastCpuReservationSize,
                    _lastFastCpuReservationRequestedCycle,
                    _lastFastCpuReservationIsWrite);
                return new AmigaBusAccessResult(
                    request,
                    _lastFastCpuReservationGrantedCycle,
                    _lastFastCpuReservationCompletedCycle);
            }
        }

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

        internal AgnusSlotTimelineSignature CaptureTimelineSignature(long startCycle, long endExclusiveCycle)
        {
            if (endExclusiveCycle <= startCycle)
            {
                return default;
            }

            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            var count = 0;
            var firstCycle = 0L;
            var lastCycle = 0L;
            var firstOwner = AgnusChipSlotOwner.Free;
            var lastOwner = AgnusChipSlotOwner.Free;
            var slotCycle = AgnusChipSlotScheduler.AlignToSlot(startCycle);
            var lastCandidate = FloorToSlot(endExclusiveCycle - 1);
            for (; slotCycle <= lastCandidate; slotCycle += SlotCycles)
            {
                if (!TryGetSlot(slotCycle, out var slot))
                {
                    continue;
                }

                var key = _slotKeys[GetSlotIndex(slotCycle)];
                if (count == 0)
                {
                    firstCycle = slotCycle;
                    firstOwner = slot.Owner;
                }

                lastCycle = slotCycle;
                lastOwner = slot.Owner;
                count++;
                hash = MixTimelineHash(hash, unchecked((ulong)slotCycle));
                hash = MixTimelineHash(hash, (ulong)slot.Owner);
                hash = MixTimelineHash(hash, (ulong)slot.Priority);
                hash = MixTimelineHash(hash, (ulong)slot.Requester);
                hash = MixTimelineHash(hash, (ulong)slot.Kind);
                hash = MixTimelineHash(hash, (ulong)key.Target);
                hash = MixTimelineHash(hash, key.Address);
                hash = MixTimelineHash(hash, (ulong)key.Size);
                hash = MixTimelineHash(hash, unchecked((ulong)key.RequestedCycleLow));
                hash = MixTimelineHash(hash, key.IsWrite ? 1UL : 0UL);
            }

            return count == 0
                ? default
                : new AgnusSlotTimelineSignature(count, hash, firstCycle, lastCycle, firstOwner, lastOwner);

            static ulong MixTimelineHash(ulong current, ulong value)
                => (current ^ value) * prime;
        }

        internal AgnusSlotTimelineSignature CaptureOwnerTimelineSignature(long startCycle, long endExclusiveCycle)
        {
            if (endExclusiveCycle <= startCycle)
            {
                return default;
            }

            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            var count = 0;
            var firstCycle = 0L;
            var lastCycle = 0L;
            var firstOwner = AgnusChipSlotOwner.Free;
            var lastOwner = AgnusChipSlotOwner.Free;
            var slotCycle = AgnusChipSlotScheduler.AlignToSlot(startCycle);
            var lastCandidate = FloorToSlot(endExclusiveCycle - 1);
            for (; slotCycle <= lastCandidate; slotCycle += SlotCycles)
            {
                if (!TryGetSlot(slotCycle, out var slot))
                {
                    continue;
                }

                if (count == 0)
                {
                    firstCycle = slotCycle;
                    firstOwner = slot.Owner;
                }

                lastCycle = slotCycle;
                lastOwner = slot.Owner;
                count++;
                hash = (hash ^ unchecked((ulong)slotCycle)) * prime;
                hash = (hash ^ (ulong)slot.Owner) * prime;
            }

            return count == 0
                ? default
                : new AgnusSlotTimelineSignature(count, hash, firstCycle, lastCycle, firstOwner, lastOwner);
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

        private AgnusBeamPosition CurrentBeam
        {
            get
            {
                if (!_beamValid)
                {
                    _beam = BeamPositionProvider?.Invoke(_currentCycle) ?? AgnusBeamPosition.FromCycle(_currentCycle);
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

        [HotPath]
        public AmigaBusAccessResult ReservePaulaDmaWordSlot(int channel, uint address, long requestedCycle)
        {
            var request = new AmigaBusAccessRequest(
                AmigaBusRequester.Paula,
                AmigaBusAccessKind.PaulaDma,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                isWrite: false,
                channel);
            var candidate = AgnusHrmOcsSlotTable.FindNextFixedDmaSlot(requestedCycle, AgnusChipSlotOwner.Paula, channel);
            AmigaBusAccessResult result;
            while (!TryCommitFixedSingleWordSlot(
                request,
                AgnusChipSlotOwner.Paula,
                candidate,
                AgnusChipSlotPriority.Paula,
                out result))
            {
                candidate = AgnusHrmOcsSlotTable.FindNextFixedDmaSlot(candidate + SlotCycles, AgnusChipSlotOwner.Paula, channel);
            }

            return result;
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
            if (!CanCommitFixedSlot(request, owner, granted, slotCount, slotStride, priority, out var blocker, out var blockerSlotCycle))
            {
                result = new AmigaBusAccessResult(request, granted, granted);
                _lastDeniedFixedSlot = new AgnusChipSlotSnapshot(owner, request.Kind, request.Address, request.RequestedCycle, granted, denied: true);
                _lastDeniedFixedSlotBlocker = GetSlotSnapshot(blockerSlotCycle, blocker, denied: false);
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
                !SlotMatchesRequest(granted, existing, request) &&
                !existing.MatchesDisplayDmaReservation(owner, request) &&
                existing.Priority >= priority)
            {
                result = new AmigaBusAccessResult(request, granted, granted);
                _lastDeniedFixedSlot = new AgnusChipSlotSnapshot(owner, request.Kind, request.Address, request.RequestedCycle, granted, denied: true);
                _lastDeniedFixedSlotBlocker = GetSlotSnapshot(granted, existing, denied: false);
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

        private long FindFreeBlitterSlot(
            long requestedCycle,
            AmigaBusAccessRequest request,
            out AmigaBusAccessResult? deniedResult)
        {
            deniedResult = null;
            var candidate = AgnusChipSlotScheduler.AlignToSlot(requestedCycle);
            while (true)
            {
                CommitRefreshSlotsThrough(candidate);
                if (PendingCpuRequestClaimsBlitterCandidate(candidate))
                {
                    deniedResult = new AmigaBusAccessResult(request, candidate, candidate);
                    _lastDeniedFixedSlot = new AgnusChipSlotSnapshot(
                        AgnusChipSlotOwner.Blitter,
                        request.Kind,
                        request.Address,
                        request.RequestedCycle,
                        candidate,
                        denied: true);
                    _lastDeniedFixedSlotBlocker = new AgnusChipSlotSnapshot(
                        AgnusChipSlotOwner.Cpu,
                        _pendingCpuSlotRequestKind,
                        _pendingCpuSlotRequestAddress,
                        _pendingCpuSlotRequestCycle,
                        candidate,
                        denied: false);
                    return candidate;
                }

                if (AreSlotsAvailable(
                    candidate,
                    slotCount: 1,
                    AgnusChipSlotOwner.Blitter,
                    request,
                    AgnusChipSlotPriority.Blitter))
                {
                    return candidate;
                }

                candidate += SlotCycles;
            }
        }

        private bool PendingCpuRequestClaimsBlitterCandidate(long slotCycle)
        {
            if (!_pendingCpuSlotRequestActive ||
                slotCycle < _pendingCpuSlotRequestCycle)
            {
                return false;
            }

            var cpuCanUseSlot = _pendingCpuSlotRequestSize == AmigaBusAccessSize.Long
                ? AreCpuSlotsAvailable(
                    slotCycle,
                    slotCount: 2,
                    _pendingCpuSlotRequestKind,
                    _pendingCpuSlotRequestTarget,
                    _pendingCpuSlotRequestAddress,
                    _pendingCpuSlotRequestSize,
                    _pendingCpuSlotRequestCycle,
                    _pendingCpuSlotRequestIsWrite)
                : IsCpuSlotAvailable(
                    slotCycle,
                    _pendingCpuSlotRequestKind,
                    _pendingCpuSlotRequestTarget,
                    _pendingCpuSlotRequestAddress,
                    _pendingCpuSlotRequestSize,
                    _pendingCpuSlotRequestCycle,
                    _pendingCpuSlotRequestIsWrite);
            if (!cpuCanUseSlot)
            {
                _pendingCpuSlotRequestBlitterMisses = 0;
                return false;
            }

            if (BlitterPriorityEnabled)
            {
                return false;
            }

            _pendingCpuSlotRequestBlitterMisses++;
            if (_pendingCpuSlotRequestBlitterMisses < 3)
            {
                return false;
            }

            _pendingCpuSlotRequestBlitterMisses = 0;
            return true;
        }

        private long FindFreeCpuSingleSlot(long requestedCycle, AmigaBusAccessRequest request)
        {
            var candidate = AgnusChipSlotScheduler.AlignToSlot(requestedCycle);
            while (true)
            {
                CommitRefreshSlotsThrough(candidate);
                if (!TryGetSlot(candidate, out var existing) ||
                    SlotMatchesRequest(candidate, existing, request) ||
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

                candidate += existing.Owner == AgnusChipSlotOwner.Copper
                    ? 2 * SlotCycles
                    : SlotCycles;
            }
        }

        private long FindFreeCpuSingleSlot(
            long requestedCycle,
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            bool isWrite)
            => FindFreeCpuSingleSlot(
                requestedCycle,
                kind,
                target,
                address,
                size,
                isWrite,
                requestedCycle);

        private long FindFreeCpuSingleSlot(
            long searchCycle,
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            bool isWrite,
            long requestedCycle)
        {
            var candidate = AgnusChipSlotScheduler.AlignToSlot(searchCycle);
            while (true)
            {
                CommitRefreshSlotsThrough(candidate);
                if (!TryGetSlot(candidate, out var existing) ||
                    SlotMatchesRequest(candidate, existing, AmigaBusRequester.Cpu, kind, target, address, size, requestedCycle, isWrite) ||
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

                candidate += existing.Owner == AgnusChipSlotOwner.Copper
                    ? 2 * SlotCycles
                    : SlotCycles;
            }
        }

        private long FindFreeCpuMultiSlot(
            long requestedCycle,
            int slotCount,
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            bool isWrite)
        {
            var candidate = AgnusChipSlotScheduler.AlignToSlot(requestedCycle);
            while (true)
            {
                const int slotStride = SlotCycles * 2;
                CommitRefreshSlotsThrough(candidate + ((slotCount - 1) * slotStride));
                if (AreCpuSlotsAvailable(
                    candidate,
                    slotCount,
                    kind,
                    target,
                    address,
                    size,
                    requestedCycle,
                    isWrite))
                {
                    return candidate;
                }

                candidate += SlotCycles;
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

                if (SlotMatchesRequest(slotCycle, existing, request) ||
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

        private bool AreCpuSlotsAvailable(
            long firstSlot,
            int slotCount,
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite)
        {
            const int slotStride = SlotCycles * 2;
            for (var slot = 0; slot < slotCount; slot++)
            {
                var slotCycle = firstSlot + (slot * slotStride);
                if (!TryGetSlot(slotCycle, out var existing))
                {
                    continue;
                }

                if (SlotMatchesRequest(slotCycle, existing, AmigaBusRequester.Cpu, kind, target, address, size, requestedCycle, isWrite))
                {
                    continue;
                }

                if (existing.Priority >= AgnusChipSlotPriority.Cpu)
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsCpuSlotAvailable(
            long slotCycle,
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite)
        {
            if (!TryGetSlot(slotCycle, out var existing))
            {
                return true;
            }

            if (SlotMatchesRequest(slotCycle, existing, AmigaBusRequester.Cpu, kind, target, address, size, requestedCycle, isWrite))
            {
                return true;
            }

            return existing.Priority < AgnusChipSlotPriority.Cpu;
        }

        private bool CanCommitFixedSlot(
            AmigaBusAccessRequest request,
            AgnusChipSlotOwner owner,
            long firstSlot,
            int slotCount,
            int slotStride,
            AgnusChipSlotPriority priority,
            out AgnusHrmCommittedSlot blocker,
            out long blockerSlotCycle)
        {
            for (var slot = 0; slot < slotCount; slot++)
            {
                var slotCycle = firstSlot + (slot * slotStride);
                if (!TryGetSlot(slotCycle, out var existing))
                {
                    continue;
                }

                if (SlotMatchesRequest(slotCycle, existing, request) ||
                    existing.MatchesDisplayDmaReservation(owner, request))
                {
                    continue;
                }

                if (existing.Priority >= priority)
                {
                    blocker = existing;
                    blockerSlotCycle = slotCycle;
                    return false;
                }
            }

            blocker = default;
            blockerSlotCycle = 0;
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
            var refreshHorizontal = horizontal <= AgnusHrmOcsSlotTable.FirstRefreshHorizontal
                ? AgnusHrmOcsSlotTable.FirstRefreshHorizontal
                : horizontal <= AgnusHrmOcsSlotTable.LastRefreshHorizontal
                    ? horizontal + ((horizontal - AgnusHrmOcsSlotTable.FirstRefreshHorizontal) & 1)
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
            CommitSlot(
                slotCycle,
                request.Requester,
                request.Kind,
                request.Target,
                request.Address,
                request.Size,
                request.RequestedCycle,
                request.IsWrite,
                owner,
                priority);
        }

        private void CommitSlot(
            long slotCycle,
            AmigaBusRequester requester,
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            AgnusChipSlotOwner owner,
            AgnusChipSlotPriority priority)
        {
            var index = GetSlotIndex(slotCycle);
            var existing = _slots[index];
            var replacedExisting = existing.Valid && existing.IsForSlot(slotCycle);
            var replacedOwner = replacedExisting ? existing.Owner : AgnusChipSlotOwner.Free;
            if (existing.Valid &&
                existing.IsForSlot(slotCycle) &&
                (SlotMatchesRequest(slotCycle, existing, requester, kind, target, address, size, requestedCycle, isWrite) ||
                    existing.MatchesDisplayDmaReservation(owner, requester, kind)))
            {
                return;
            }

            _slots[index] = new AgnusHrmCommittedSlot(
                slotCycle,
                requester,
                kind,
                owner,
                priority);
            _slotKeys[index] = new AgnusHrmCommittedSlotKey(
                target,
                address,
                size,
                requestedCycle,
                isWrite);
            if (_slotDebug != null)
            {
                _slotDebug[index] = new AgnusHrmCommittedSlotDebug(
                slotCycle,
                requester,
                kind,
                target,
                address,
                size,
                requestedCycle,
                isWrite);
            }

            _slotScheduleAuditSink?.Invoke(new AgnusSlotScheduleAuditEntry(
                ++_slotScheduleAuditSequence,
                slotCycle,
                requester,
                kind,
                target,
                address,
                size,
                requestedCycle,
                isWrite,
                owner,
                replacedExisting,
                replacedOwner,
                _slotScheduleAuditSource,
                _slotScheduleAuditSourceA,
                _slotScheduleAuditSourceB,
                _slotScheduleAuditSourceC));
            RecordSlotGrant(owner);
        }

        private void RecordFastCpuReservation(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            long grantedCycle,
            long completedCycle,
            bool isWrite)
        {
            _lastReservation = null;
            _lastFastCpuReservationValid = true;
            _lastFastCpuReservationKind = kind;
            _lastFastCpuReservationTarget = target;
            _lastFastCpuReservationAddress = address;
            _lastFastCpuReservationSize = size;
            _lastFastCpuReservationRequestedCycle = requestedCycle;
            _lastFastCpuReservationGrantedCycle = grantedCycle;
            _lastFastCpuReservationCompletedCycle = completedCycle;
            _lastFastCpuReservationIsWrite = isWrite;
            _lastGrantedSlot = new AgnusChipSlotSnapshot(
                AgnusChipSlotOwner.Cpu,
                kind,
                address,
                requestedCycle,
                grantedCycle,
                denied: false);
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
            return reservation.Valid && reservation.IsForSlot(slotCycle);
        }

        private bool SlotMatchesRequest(long slotCycle, AgnusHrmCommittedSlot slot, AmigaBusAccessRequest request)
        {
            if (!slot.MatchesRequestClass(request.Requester, request.Kind))
            {
                return false;
            }

            return _slotKeys[GetSlotIndex(slotCycle)].Matches(request);
        }

        private bool SlotMatchesRequest(
            long slotCycle,
            AgnusHrmCommittedSlot slot,
            AmigaBusRequester requester,
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite)
        {
            if (!slot.MatchesRequestClass(requester, kind))
            {
                return false;
            }

            return _slotKeys[GetSlotIndex(slotCycle)].Matches(
                target,
                address,
                size,
                requestedCycle,
                isWrite);
        }

        private AgnusChipSlotSnapshot GetSlotSnapshot(long slotCycle, AgnusHrmCommittedSlot slot, bool denied)
        {
            var index = GetSlotIndex(slotCycle);
            return _slotDebug != null
                ? _slotDebug[index].ToSnapshot(slot.Owner, denied)
                : _slotKeys[index].ToSnapshot(slot.Owner, slot.Kind, slotCycle, denied);
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
            return owner is AgnusChipSlotOwner.Cpu or AgnusChipSlotOwner.Copper
                ? SlotCycles * 2
                : SlotCycles;
        }

        private static long GetCompletedCycle(long firstSlot, int slotCount, int slotStride)
        {
            return firstSlot + ((slotCount - 1) * slotStride) + SlotCycles;
        }

        private static bool RequiresEvenSlot(AgnusChipSlotOwner owner)
        {
            return owner == AgnusChipSlotOwner.Copper;
        }

        private static bool IsLiveDisplaySlotOwner(
            AgnusChipSlotOwner owner,
            AgnusLiveDisplaySlotOwnerMask owners)
            => owner switch
            {
                AgnusChipSlotOwner.Copper => (owners & AgnusLiveDisplaySlotOwnerMask.Copper) != 0,
                AgnusChipSlotOwner.Sprite => (owners & AgnusLiveDisplaySlotOwnerMask.Sprite) != 0,
                AgnusChipSlotOwner.Bitplane => (owners & AgnusLiveDisplaySlotOwnerMask.Bitplane) != 0,
                _ => false
            };

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

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private readonly struct AgnusHrmCommittedSlot
        {
            public AgnusHrmCommittedSlot(
                long slotCycle,
                AmigaBusRequester requester,
                AmigaBusAccessKind kind,
                AgnusChipSlotOwner owner,
                AgnusChipSlotPriority priority)
            {
                _slotCycleLow = unchecked((uint)slotCycle);
                _owner = (byte)owner;
                _priority = (byte)priority;
                _requester = (byte)requester;
                _kind = (byte)kind;
            }

            private readonly uint _slotCycleLow;

            private readonly byte _owner;

            private readonly byte _priority;

            private readonly byte _requester;

            private readonly byte _kind;

            public bool Valid => _owner != (byte)AgnusChipSlotOwner.Free;

            public AgnusChipSlotOwner Owner => (AgnusChipSlotOwner)_owner;

            public AgnusChipSlotPriority Priority => (AgnusChipSlotPriority)_priority;

            public AmigaBusRequester Requester => (AmigaBusRequester)_requester;

            public AmigaBusAccessKind Kind => (AmigaBusAccessKind)_kind;

            public bool IsForSlot(long slotCycle)
                => _slotCycleLow == unchecked((uint)slotCycle);

            public bool MatchesRequestClass(AmigaBusRequester requester, AmigaBusAccessKind kind)
                => _requester == (byte)requester && _kind == (byte)kind;

            public bool MatchesDisplayDmaReservation(AgnusChipSlotOwner owner, AmigaBusAccessRequest request)
                => MatchesDisplayDmaReservation(owner, request.Requester, request.Kind);

            public bool MatchesDisplayDmaReservation(
                AgnusChipSlotOwner owner,
                AmigaBusRequester requester,
                AmigaBusAccessKind kind)
            {
                if (Owner != owner ||
                    (owner != AgnusChipSlotOwner.Bitplane && owner != AgnusChipSlotOwner.Sprite))
                {
                    return false;
                }

                return MatchesRequestClass(requester, kind);
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private readonly struct AgnusHrmCommittedSlotKey
        {
            public AgnusHrmCommittedSlotKey(
                AmigaBusAccessTarget target,
                uint address,
                AmigaBusAccessSize size,
                long requestedCycle,
                bool isWrite)
            {
                _address = address;
                _requestedCycleLow = unchecked((uint)requestedCycle);
                _target = (byte)target;
                _size = (byte)size;
                _flags = isWrite ? (byte)1 : (byte)0;
                _reserved = 0;
            }

            private readonly uint _address;

            private readonly uint _requestedCycleLow;

            private readonly byte _target;

            private readonly byte _size;

            private readonly byte _flags;

            private readonly byte _reserved;

            public uint Address => _address;

            public uint RequestedCycleLow => _requestedCycleLow;

            public AmigaBusAccessTarget Target => (AmigaBusAccessTarget)_target;

            public AmigaBusAccessSize Size => (AmigaBusAccessSize)_size;

            public bool IsWrite => (_flags & 1) != 0;

            public bool Matches(AmigaBusAccessRequest request)
                => Matches(
                    request.Target,
                    request.Address,
                    request.Size,
                    request.RequestedCycle,
                    request.IsWrite);

            public bool Matches(
                AmigaBusAccessTarget target,
                uint address,
                AmigaBusAccessSize size,
                long requestedCycle,
                bool isWrite)
            {
                return _target == (byte)target &&
                    _address == address &&
                    _size == (byte)size &&
                    _requestedCycleLow == unchecked((uint)requestedCycle) &&
                    ((_flags & 1) != 0) == isWrite;
            }

            public AgnusChipSlotSnapshot ToSnapshot(
                AgnusChipSlotOwner owner,
                AmigaBusAccessKind kind,
                long slotCycle,
                bool denied)
            {
                return new AgnusChipSlotSnapshot(
                    owner,
                    kind,
                    _address,
                    _requestedCycleLow,
                    slotCycle,
                    denied);
            }
        }

        private readonly struct AgnusHrmCommittedSlotDebug
        {
            public AgnusHrmCommittedSlotDebug(
                long slotCycle,
                AmigaBusRequester requester,
                AmigaBusAccessKind kind,
                AmigaBusAccessTarget target,
                uint address,
                AmigaBusAccessSize size,
                long requestedCycle,
                bool isWrite)
            {
                SlotCycle = slotCycle;
                Requester = requester;
                Kind = kind;
                Target = target;
                Address = address;
                Size = size;
                RequestedCycle = requestedCycle;
                IsWrite = isWrite;
            }

            public long SlotCycle { get; }

            public AmigaBusRequester Requester { get; }

            public AmigaBusAccessKind Kind { get; }

            public AmigaBusAccessTarget Target { get; }

            public uint Address { get; }

            public AmigaBusAccessSize Size { get; }

            public long RequestedCycle { get; }

            public bool IsWrite { get; }

            public bool Matches(AmigaBusAccessRequest request)
                => Matches(
                    request.Requester,
                    request.Kind,
                    request.Target,
                    request.Address,
                    request.Size,
                    request.RequestedCycle,
                    request.IsWrite);

            public bool Matches(
                AmigaBusRequester requester,
                AmigaBusAccessKind kind,
                AmigaBusAccessTarget target,
                uint address,
                AmigaBusAccessSize size,
                long requestedCycle,
                bool isWrite)
            {
                return Requester == requester &&
                    Kind == kind &&
                    Target == target &&
                    Address == address &&
                    Size == size &&
                    RequestedCycle == requestedCycle &&
                    IsWrite == isWrite;
            }

            public AgnusChipSlotSnapshot ToSnapshot(AgnusChipSlotOwner owner, bool denied)
            {
                return new AgnusChipSlotSnapshot(
                    owner,
                    Kind,
                    Address,
                    RequestedCycle,
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

        internal OcsCpuWaitLiveSlotResult AdvanceCpuWaitLiveSlotTo(
            long targetCycle,
            out int bitplaneFetches,
            out int spriteFetches,
            out bool completedSafeCopper)
        {
            System.Diagnostics.Debug.Assert(targetCycle >= 0, "Agnus CPU-wait slot cycles must be non-negative.");
            var result = _bus.Display.AdvanceCpuWaitLiveSlot(
                targetCycle,
                out bitplaneFetches,
                out spriteFetches,
                out completedSafeCopper);
            if (result == OcsCpuWaitLiveSlotResult.Processed)
            {
                _chipSlots.AdvanceTo(targetCycle);
                _currentCycle = Math.Max(_currentCycle, targetCycle);
            }

            return result;
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

        public void RecordCpuChipWaitCycles(long waitCycles)
        {
            if (waitCycles > 0)
            {
                _cpuChipStallCycles += waitCycles;
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

    internal readonly struct AmigaCpuBusPhaseTrace
    {
        public AmigaCpuBusPhaseTrace(
            M68kCpuBusPhase cpuPhase,
            AmigaBusAccessResult? busAccess,
            long secondWordCycle,
            AgnusChipSlotSnapshot? grantedSlot)
        {
            CpuPhase = cpuPhase;
            BusAccess = busAccess;
            SecondWordCycle = secondWordCycle;
            GrantedSlot = grantedSlot;
        }

        public M68kCpuBusPhase CpuPhase { get; }

        public AmigaBusAccessResult? BusAccess { get; }

        public long SecondWordCycle { get; }

        public AgnusChipSlotSnapshot? GrantedSlot { get; }
    }

    internal sealed class BoundedCpuBusPhaseLog : IReadOnlyList<AmigaCpuBusPhaseTrace>
    {
        private readonly AmigaCpuBusPhaseTrace[] _buffer;
        private int _start;
        private int _count;

        public BoundedCpuBusPhaseLog(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
            }

            _buffer = new AmigaCpuBusPhaseTrace[capacity];
        }

        public int Count => _count;

        public AmigaCpuBusPhaseTrace this[int index]
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

        public void Add(AmigaCpuBusPhaseTrace result)
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

        public IEnumerator<AmigaCpuBusPhaseTrace> GetEnumerator()
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
