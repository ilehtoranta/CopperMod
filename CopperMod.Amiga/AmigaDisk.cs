/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CopperMod.Amiga
{
    internal sealed class AmigaPreparedTrack
    {
        private const int DefaultSyncOffsetCapacity = 16384;

        private readonly ushort[] _rollingWords;
        private readonly int[] _syncOffsets;
        private readonly bool _accelerationEnabled;
        private AmigaEncodedTrack _track;
        private byte[]? _trackArray;
        private int _trackArrayOffset;
        private int _trackArrayCount;
        private bool _hasTrack;
        private bool _rollingWordsValid;
        private bool _syncCacheValid;
        private ushort _syncCacheWord;
        private int _syncOffsetCount;
        private bool _syncOffsetOverflow;

        public AmigaPreparedTrack(AmigaEncodedTrack track)
            : this(Math.Max(1, track.BitLength), DefaultSyncOffsetCapacity)
        {
            Prepare(track);
        }

        internal AmigaPreparedTrack(int maxTrackBits, int syncOffsetCapacity, bool accelerationEnabled = true)
        {
            if (maxTrackBits <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxTrackBits));
            }

            if (syncOffsetCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(syncOffsetCapacity));
            }

            _rollingWords = new ushort[maxTrackBits];
            _syncOffsets = new int[syncOffsetCapacity];
            _accelerationEnabled = accelerationEnabled;
        }

        public int BitLength => RequireTrack().BitLength;

        public bool HasRollingWords => _rollingWordsValid;

        public bool UsesScalarReads
        {
            get
            {
                var track = RequireTrack();
                return !_accelerationEnabled || track.BitLength > _rollingWords.Length || RequiresDynamicRead(track);
            }
        }

        public void Prepare(AmigaEncodedTrack track)
        {
            if (track.BitLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(track), "Prepared tracks require a non-empty encoded track.");
            }

            _track = track;
            CaptureTrackIdentity(track.EncodedData);
            _hasTrack = true;
            _rollingWordsValid = false;
            _syncCacheValid = false;
            _syncOffsetCount = 0;
            _syncOffsetOverflow = false;
        }

        public void Clear()
        {
            _hasTrack = false;
            _trackArray = null;
            _trackArrayOffset = 0;
            _trackArrayCount = 0;
            _rollingWordsValid = false;
            _syncCacheValid = false;
            _syncOffsetCount = 0;
            _syncOffsetOverflow = false;
        }

        public bool Matches(AmigaEncodedTrack track)
        {
            if (!_hasTrack ||
                _track.BitLength != track.BitLength ||
                _track.Features != track.Features ||
                !RegionsEqual(_track.Regions, track.Regions))
            {
                return false;
            }

            if (_trackArray != null &&
                MemoryMarshal.TryGetArray(track.EncodedData, out ArraySegment<byte> segment) &&
                segment.Array != null)
            {
                return ReferenceEquals(_trackArray, segment.Array) &&
                    _trackArrayOffset == segment.Offset &&
                    _trackArrayCount == segment.Count;
            }

            return _track.EncodedData.Span.SequenceEqual(track.EncodedData.Span);
        }

        public byte ReadByte(int bitOffset)
            => (byte)(ReadUInt16(bitOffset) >> 8);

        public ushort ReadUInt16(int bitOffset)
        {
            var track = RequireTrack();
            if (!_accelerationEnabled || track.BitLength > _rollingWords.Length || RequiresDynamicRead(track))
            {
                return track.ReadUInt16AtBit(bitOffset);
            }

            EnsureRollingWords(track);
            return _rollingWords[AmigaEncodedTrack.WrapBitOffset(bitOffset, track.BitLength)];
        }

        public int CopySyncOffsets(ushort sync, Span<int> destination, out bool cacheHit)
        {
            if (!_accelerationEnabled || RequiresDynamicRead(RequireTrack()))
            {
                cacheHit = false;
                return CopySyncOffsetsScalar(sync, destination);
            }

            EnsureSyncOffsets(sync, out cacheHit);
            var count = Math.Min(_syncOffsetCount, destination.Length);
            _syncOffsets.AsSpan(0, count).CopyTo(destination);
            return count;
        }

        public int CopyByteSyncOffsets(byte sync, Span<int> destination, out bool cacheHit)
        {
            cacheHit = false;
            var track = RequireTrack();
            if (_accelerationEnabled && track.BitLength <= _rollingWords.Length && !RequiresDynamicRead(track))
            {
                EnsureRollingWords(track);
                var fastCount = 0;
                for (var offset = 0; offset < track.BitLength; offset++)
                {
                    if ((byte)(_rollingWords[offset] >> 8) != sync)
                    {
                        continue;
                    }

                    if (fastCount < destination.Length)
                    {
                        destination[fastCount] = offset;
                    }

                    fastCount++;
                }

                return Math.Min(fastCount, destination.Length);
            }

            var count = 0;
            for (var offset = 0; offset < track.BitLength; offset++)
            {
                if (ReadByte(offset) != sync)
                {
                    continue;
                }

                if (count < destination.Length)
                {
                    destination[count] = offset;
                }

                count++;
            }

            return Math.Min(count, destination.Length);
        }

        public bool TryFindSyncWithinNextWord(ushort sync, int sourceBit, out int distance, out int syncOffset)
        {
            var track = RequireTrack();
            if (track.BitLength < 16)
            {
                distance = 0;
                syncOffset = -1;
                return false;
            }

            if (!_accelerationEnabled)
            {
                return TryFindSyncWithinNextWordScalar(sync, sourceBit, out distance, out syncOffset);
            }

            EnsureSyncOffsets(sync, out _);
            if (_syncOffsetCount == 0)
            {
                distance = 0;
                syncOffset = -1;
                return false;
            }

            if (_syncOffsetOverflow)
            {
                return TryFindSyncWithinNextWordScalar(sync, sourceBit, out distance, out syncOffset);
            }

            return TryFindCachedSyncWithinNextWord(
                _syncOffsets.AsSpan(0, _syncOffsetCount),
                track.BitLength,
                sourceBit,
                out distance,
                out syncOffset);
        }

        public int FindSyncOffset(ushort sync, int startOffset)
        {
            var track = RequireTrack();
            if (track.BitLength < 16)
            {
                return -1;
            }

            if (!_accelerationEnabled || RequiresDynamicRead(track))
            {
                return FindSyncOffsetScalar(sync, startOffset);
            }

            EnsureSyncOffsets(sync, out _);
            if (_syncOffsetCount == 0)
            {
                return -1;
            }

            if (_syncOffsetOverflow)
            {
                return FindSyncOffsetScalar(sync, startOffset);
            }

            startOffset = AmigaEncodedTrack.WrapBitOffset(startOffset, track.BitLength);
            var index = LowerBound(_syncOffsets, _syncOffsetCount, startOffset);
            var offset = index < _syncOffsetCount ? _syncOffsets[index] : _syncOffsets[0];
            return RewindConsecutiveSync(sync, offset);
        }

        public int RewindConsecutiveSync(ushort sync, int offset)
        {
            var track = RequireTrack();
            if (track.BitLength < 32)
            {
                return offset;
            }

            var current = offset;
            var maxSteps = Math.Max(1, track.BitLength / 16);
            for (var step = 0; step < maxSteps; step++)
            {
                var previous = (current - 16 + track.BitLength) % track.BitLength;
                if (previous == offset || ReadUInt16(previous) != sync)
                {
                    return current;
                }

                current = previous;
            }

            return current;
        }

        public int RewindConsecutiveByteSync(byte sync, int offset)
        {
            var track = RequireTrack();
            if (track.BitLength < 16)
            {
                return offset;
            }

            var current = offset;
            var maxSteps = Math.Max(1, track.BitLength / 8);
            for (var step = 0; step < maxSteps; step++)
            {
                var previous = (current - 8 + track.BitLength) % track.BitLength;
                if (previous == offset || ReadByte(previous) != sync)
                {
                    return current;
                }

                current = previous;
            }

            return current;
        }

        private void EnsureSyncOffsets(ushort sync, out bool cacheHit)
        {
            if (_syncCacheValid && _syncCacheWord == sync)
            {
                cacheHit = true;
                return;
            }

            cacheHit = false;
            var track = RequireTrack();
            _syncOffsetCount = 0;
            _syncOffsetOverflow = false;
            if (_accelerationEnabled && track.BitLength <= _rollingWords.Length && !RequiresDynamicRead(track))
            {
                EnsureRollingWords(track);
                for (var offset = 0; offset < track.BitLength; offset++)
                {
                    if (_rollingWords[offset] != sync)
                    {
                        continue;
                    }

                    if (_syncOffsetCount < _syncOffsets.Length)
                    {
                        _syncOffsets[_syncOffsetCount++] = offset;
                        continue;
                    }

                    _syncOffsetOverflow = true;
                }
            }
            else
            {
                for (var offset = 0; offset < track.BitLength; offset++)
                {
                    if (ReadUInt16(offset) != sync)
                    {
                        continue;
                    }

                    if (_syncOffsetCount < _syncOffsets.Length)
                    {
                        _syncOffsets[_syncOffsetCount++] = offset;
                        continue;
                    }

                    _syncOffsetOverflow = true;
                }
            }

            _syncCacheWord = sync;
            _syncCacheValid = true;
        }

        private int CopySyncOffsetsScalar(ushort sync, Span<int> destination)
        {
            var track = RequireTrack();
            var count = 0;
            for (var offset = 0; offset < track.BitLength; offset++)
            {
                if (track.ReadUInt16AtBit(offset) != sync)
                {
                    continue;
                }

                if (count < destination.Length)
                {
                    destination[count] = offset;
                }

                count++;
            }

            return Math.Min(count, destination.Length);
        }

        private void EnsureRollingWords(AmigaEncodedTrack track)
        {
            if (_rollingWordsValid)
            {
                return;
            }

            var span = track.EncodedData.Span;
            var directLimit = Math.Max(0, track.BitLength - 15);
            for (var offset = 0; offset < track.BitLength; offset++)
            {
                if (offset < directLimit)
                {
                    var byteOffset = offset >> 3;
                    var bitShift = offset & 7;
                    if (bitShift == 0)
                    {
                        _rollingWords[offset] = (ushort)((span[byteOffset] << 8) | span[byteOffset + 1]);
                    }
                    else
                    {
                        var window = (uint)((span[byteOffset] << 16) |
                            (span[byteOffset + 1] << 8) |
                            span[byteOffset + 2]);
                        _rollingWords[offset] = (ushort)((window >> (8 - bitShift)) & 0xFFFF);
                    }

                    continue;
                }

                _rollingWords[offset] = track.ReadUInt16AtBit(offset);
            }

            _rollingWordsValid = true;
        }

        private AmigaEncodedTrack RequireTrack()
        {
            if (!_hasTrack)
            {
                throw new InvalidOperationException("Prepared track has not been initialized.");
            }

            return _track;
        }

        private void CaptureTrackIdentity(ReadOnlyMemory<byte> data)
        {
            if (MemoryMarshal.TryGetArray(data, out ArraySegment<byte> segment) && segment.Array != null)
            {
                _trackArray = segment.Array;
                _trackArrayOffset = segment.Offset;
                _trackArrayCount = segment.Count;
                return;
            }

            _trackArray = null;
            _trackArrayOffset = 0;
            _trackArrayCount = data.Length;
        }

        private bool TryFindSyncWithinNextWordScalar(ushort sync, int sourceBit, out int distance, out int syncOffset)
        {
            var track = RequireTrack();
            if (track.BitLength < 16)
            {
                distance = 0;
                syncOffset = -1;
                return false;
            }

            for (var bitDistance = 1; bitDistance < 16; bitDistance++)
            {
                var offset = (sourceBit + bitDistance) % track.BitLength;
                if (ReadUInt16(offset) == sync)
                {
                    distance = bitDistance;
                    syncOffset = offset;
                    return true;
                }
            }

            distance = 0;
            syncOffset = -1;
            return false;
        }

        private int FindSyncOffsetScalar(ushort sync, int startOffset)
        {
            var track = RequireTrack();
            if (track.BitLength < 16)
            {
                return -1;
            }

            startOffset = AmigaEncodedTrack.WrapBitOffset(startOffset, track.BitLength);
            for (var step = 0; step < track.BitLength; step++)
            {
                var offset = (startOffset + step) % track.BitLength;
                if (ReadUInt16(offset) == sync)
                {
                    return RewindConsecutiveSync(sync, offset);
                }
            }

            return -1;
        }

        public bool TryFindByteSyncWithinNextWord(byte sync, int sourceBit, out int distance, out int syncOffset)
        {
            var track = RequireTrack();
            if (track.BitLength < 8)
            {
                distance = 0;
                syncOffset = -1;
                return false;
            }

            for (var bitDistance = 1; bitDistance < 16; bitDistance++)
            {
                var offset = (sourceBit + bitDistance) % track.BitLength;
                if (ReadByte(offset) == sync)
                {
                    distance = bitDistance;
                    syncOffset = offset;
                    return true;
                }
            }

            distance = 0;
            syncOffset = -1;
            return false;
        }

        public int FindByteSyncOffset(byte sync, int startOffset)
        {
            var track = RequireTrack();
            if (track.BitLength < 8)
            {
                return -1;
            }

            startOffset = AmigaEncodedTrack.WrapBitOffset(startOffset, track.BitLength);
            for (var step = 0; step < track.BitLength; step++)
            {
                var offset = (startOffset + step) % track.BitLength;
                if (ReadByte(offset) == sync)
                {
                    return RewindConsecutiveByteSync(sync, offset);
                }
            }

            return -1;
        }

        private static bool TryFindCachedSyncWithinNextWord(
            ReadOnlySpan<int> syncOffsets,
            int trackLength,
            int sourceBit,
            out int distance,
            out int syncOffset)
        {
            var start = sourceBit + 1;
            var stop = sourceBit + 15;
            if (stop < trackLength)
            {
                var index = LowerBound(syncOffsets, syncOffsets.Length, start);
                if (index < syncOffsets.Length && syncOffsets[index] <= stop)
                {
                    syncOffset = syncOffsets[index];
                    distance = syncOffset - sourceBit;
                    return true;
                }
            }
            else
            {
                var index = LowerBound(syncOffsets, syncOffsets.Length, start);
                if (index < syncOffsets.Length)
                {
                    syncOffset = syncOffsets[index];
                    distance = syncOffset - sourceBit;
                    return true;
                }

                var wrappedStop = stop - trackLength;
                if (syncOffsets[0] <= wrappedStop)
                {
                    syncOffset = syncOffsets[0];
                    distance = (trackLength - sourceBit) + syncOffset;
                    return true;
                }
            }

            distance = 0;
            syncOffset = -1;
            return false;
        }

        private static int LowerBound(ReadOnlySpan<int> values, int count, int target)
        {
            var low = 0;
            var high = count;
            while (low < high)
            {
                var mid = (int)((uint)(low + high) >> 1);
                if (values[mid] < target)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            return low;
        }

        private static bool RequiresDynamicRead(AmigaEncodedTrack track)
        {
            return (track.Features & (AmigaTrackFeatures.WeakData | AmigaTrackFeatures.NoFlux)) != 0 ||
                track.Regions.Count != 0;
        }

        private static bool RegionsEqual(IReadOnlyList<AmigaTrackRegion> left, IReadOnlyList<AmigaTrackRegion> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (var i = 0; i < left.Count; i++)
            {
                if (left[i].StartBit != right[i].StartBit ||
                    left[i].BitLength != right[i].BitLength ||
                    left[i].Features != right[i].Features)
                {
                    return false;
                }
            }

            return true;
        }
    }

    internal readonly struct AmigaDiskSpecializationCounters
    {
        public AmigaDiskSpecializationCounters(
            long preparedTrackHits,
            long preparedTrackMisses,
            long rollingWindowHits,
            long rollingWindowMisses,
            long syncIndexHits,
            long syncIndexMisses,
            long scalarBitReads,
            long fullTrackSyncScans,
            long fullTrackSyncScanBits,
            long fullTrackWordSyncScans,
            long fullTrackByteSyncScans,
            long activeDmaPredictionPasses,
            long activeDmaPredictedWordPlans,
            long activeDmaRuntimeWordPlans,
            long activeDmaRequestsCreated,
            long activeDmaRequestsServed,
            long activeDmaRequestsBlocked)
        {
            PreparedTrackHits = preparedTrackHits;
            PreparedTrackMisses = preparedTrackMisses;
            RollingWindowHits = rollingWindowHits;
            RollingWindowMisses = rollingWindowMisses;
            SyncIndexHits = syncIndexHits;
            SyncIndexMisses = syncIndexMisses;
            ScalarBitReads = scalarBitReads;
            FullTrackSyncScans = fullTrackSyncScans;
            FullTrackSyncScanBits = fullTrackSyncScanBits;
            FullTrackWordSyncScans = fullTrackWordSyncScans;
            FullTrackByteSyncScans = fullTrackByteSyncScans;
            ActiveDmaPredictionPasses = activeDmaPredictionPasses;
            ActiveDmaPredictedWordPlans = activeDmaPredictedWordPlans;
            ActiveDmaRuntimeWordPlans = activeDmaRuntimeWordPlans;
            ActiveDmaRequestsCreated = activeDmaRequestsCreated;
            ActiveDmaRequestsServed = activeDmaRequestsServed;
            ActiveDmaRequestsBlocked = activeDmaRequestsBlocked;
        }

        public long PreparedTrackHits { get; }

        public long PreparedTrackMisses { get; }

        public long RollingWindowHits { get; }

        public long RollingWindowMisses { get; }

        public long SyncIndexHits { get; }

        public long SyncIndexMisses { get; }

        public long ScalarBitReads { get; }

        public long FullTrackSyncScans { get; }

        public long FullTrackSyncScanBits { get; }

        public long FullTrackWordSyncScans { get; }

        public long FullTrackByteSyncScans { get; }

        public long ActiveDmaPredictionPasses { get; }

        public long ActiveDmaPredictedWordPlans { get; }

        public long ActiveDmaRuntimeWordPlans { get; }

        public long ActiveDmaRequestsCreated { get; }

        public long ActiveDmaRequestsServed { get; }

        public long ActiveDmaRequestsBlocked { get; }
    }

    internal readonly struct AmigaDiskSchedulerCounters
    {
        public AmigaDiskSchedulerCounters(
            long nextWakeCandidateQueries,
            long nextEventWakeCandidateQueries,
            long hasWakeCandidateThroughQueries,
            long hasEventWakeCandidateThroughQueries,
            long refreshNextIndexPulseQueries,
            long inputAdvanceCalls,
            long schedulerGateTrue,
            long schedulerGateFalse,
            long pendingDmaWakeSources,
            long activeDmaProgressWakeSources,
            long activeDmaCompletionWakeSources,
            long syncCandidateWakeSources,
            long indexPulseWakeSources,
            long passiveByteReadyWakeSources)
        {
            NextWakeCandidateQueries = nextWakeCandidateQueries;
            NextEventWakeCandidateQueries = nextEventWakeCandidateQueries;
            HasWakeCandidateThroughQueries = hasWakeCandidateThroughQueries;
            HasEventWakeCandidateThroughQueries = hasEventWakeCandidateThroughQueries;
            RefreshNextIndexPulseQueries = refreshNextIndexPulseQueries;
            InputAdvanceCalls = inputAdvanceCalls;
            SchedulerGateTrue = schedulerGateTrue;
            SchedulerGateFalse = schedulerGateFalse;
            PendingDmaWakeSources = pendingDmaWakeSources;
            ActiveDmaProgressWakeSources = activeDmaProgressWakeSources;
            ActiveDmaCompletionWakeSources = activeDmaCompletionWakeSources;
            SyncCandidateWakeSources = syncCandidateWakeSources;
            IndexPulseWakeSources = indexPulseWakeSources;
            PassiveByteReadyWakeSources = passiveByteReadyWakeSources;
        }

        public long NextWakeCandidateQueries { get; }

        public long NextEventWakeCandidateQueries { get; }

        public long HasWakeCandidateThroughQueries { get; }

        public long HasEventWakeCandidateThroughQueries { get; }

        public long RefreshNextIndexPulseQueries { get; }

        public long InputAdvanceCalls { get; }

        public long SchedulerGateTrue { get; }

        public long SchedulerGateFalse { get; }

        public long PendingDmaWakeSources { get; }

        public long ActiveDmaProgressWakeSources { get; }

        public long ActiveDmaCompletionWakeSources { get; }

        public long SyncCandidateWakeSources { get; }

        public long IndexPulseWakeSources { get; }

        public long PassiveByteReadyWakeSources { get; }
    }

    internal sealed class AmigaFloppyDrive
    {
        private const int MinCylinder = 0;
        private const int MaxCylinder = AmigaDiskGeometry.CylinderCount - 1;
        private IAmigaDiskImage? _cachedTrackDisk;
        private AmigaEncodedTrack? _cachedTrack;
        private int _cachedTrackCylinder = -1;
        private int _cachedTrackHead = -1;
        private ulong _wakeVersion;

        public IAmigaDiskImage? Disk { get; private set; }

        public bool HasDisk => Disk != null;

        public int Cylinder { get; private set; }

        public int Head { get; private set; }

        public bool MotorOn { get; private set; }

        public long MotorOnCycle { get; private set; }

        public bool Selected { get; private set; }

        public bool DiskChanged { get; private set; }

        public bool WriteProtected { get; private set; }

        internal ulong WakeVersion => _wakeVersion;

        public void Insert(IAmigaDiskImage disk, bool markChanged = false, bool? writeProtected = null)
        {
            Disk = disk ?? throw new ArgumentNullException(nameof(disk));
            DiskChanged = markChanged;
            WriteProtected = writeProtected ?? disk.DefaultWriteProtected;
            ClearTrackCache();
            _wakeVersion++;
        }

        public void Eject()
        {
            Disk = null;
            DiskChanged = true;
            WriteProtected = false;
            ClearTrackCache();
            _wakeVersion++;
        }

        public void ResetPosition()
        {
            Cylinder = 0;
            Head = 0;
            MotorOn = false;
            MotorOnCycle = 0;
            Selected = false;
            ClearTrackCache();
            _wakeVersion++;
        }

        public void SetHead(int head)
        {
            var normalizedHead = head == 0 ? 0 : 1;
            if (Head == normalizedHead)
            {
                return;
            }

            Head = normalizedHead;
            ClearTrackCache();
            _wakeVersion++;
        }

        public void SetMotorOn(bool motorOn)
        {
            SetMotorOn(motorOn, 0);
        }

        public void SetMotorOn(bool motorOn, long cycle)
        {
            if (MotorOn == motorOn)
            {
                return;
            }

            MotorOn = motorOn;
            MotorOnCycle = motorOn ? cycle : 0;
            _wakeVersion++;
        }

        public void SetSelected(bool selected)
        {
            if (Selected == selected)
            {
                return;
            }

            Selected = selected;
            _wakeVersion++;
        }

        public void SetWriteProtected(bool writeProtected)
        {
            if (WriteProtected == writeProtected)
            {
                return;
            }

            WriteProtected = writeProtected;
            _wakeVersion++;
        }

        public void Step(int delta)
        {
            var cylinder = Math.Clamp(Cylinder + delta, MinCylinder, MaxCylinder);
            if (Cylinder != cylinder)
            {
                Cylinder = cylinder;
                ClearTrackCache();
                _wakeVersion++;
            }

            if (Disk != null)
            {
                DiskChanged = false;
            }
        }

        public ReadOnlySpan<byte> ReadSector(int cylinder, int head, int sector)
        {
            return RequireDisk().ReadSector(cylinder, head, sector);
        }

        public ReadOnlySpan<byte> ReadBytes(int byteOffset, int byteCount)
        {
            return RequireDisk().ReadBytes(byteOffset, byteCount);
        }

        public AmigaEncodedTrack ReadEncodedTrack(int cylinder, int head)
        {
            var disk = RequireDisk();
            if (ReferenceEquals(_cachedTrackDisk, disk) &&
                _cachedTrack.HasValue &&
                _cachedTrackCylinder == cylinder &&
                _cachedTrackHead == head)
            {
                return _cachedTrack.Value;
            }

            var track = disk.ReadEncodedTrack(cylinder, head);
            _cachedTrackDisk = disk;
            _cachedTrack = track;
            _cachedTrackCylinder = cylinder;
            _cachedTrackHead = head;
            return track;
        }

        public bool TryWriteEncodedTrack(int cylinder, int head, AmigaEncodedTrack track)
        {
            var written = Disk != null && Disk.TryWriteEncodedTrack(cylinder, head, track);
            if (written)
            {
                ClearTrackCache();
            }

            return written;
        }

        private IAmigaDiskImage RequireDisk()
        {
            return Disk ?? throw new AmigaEmulationException("No disk is inserted in DF0:.");
        }

        private void ClearTrackCache()
        {
            _cachedTrackDisk = null;
            _cachedTrack = null;
            _cachedTrackCylinder = -1;
            _cachedTrackHead = -1;
        }

    }

    internal sealed class AmigaDiskController
    {
        private enum SchedulerWakeReason
        {
            None,
            PendingDma,
            ActiveDmaProgress,
            ActiveDmaCompletion,
            SyncCandidate,
            IndexPulse,
            PassiveByteReady
        }

        private const int MaxFloppyDriveCount = 4;
        private const ushort DskDatr = 0x008;
        private const ushort DskBytr = 0x01A;
        private const ushort DskPth = 0x020;
        private const ushort DskPtl = 0x022;
        private const ushort DskLen = 0x024;
        private const ushort DskDat = 0x026;
        private const ushort DskSync = 0x07E;
        private const ushort AdkCon = 0x09E;
        private const ushort DskBlkInterrupt = AmigaConstants.IntreqDiskBlock;
        private const ushort DskSynInterrupt = 0x1000;
        private const ushort DmaconMasterEnable = 0x0200;
        private const ushort DmaconDiskEnable = 0x0010;
        private const ushort DskBytReady = 0x8000;
        private const ushort DskBytDmaOn = 0x4000;
        private const ushort DskBytDiskWrite = 0x2000;
        private const ushort DskBytWordEqual = 0x1000;
        private const ushort DskDmaEnable = 0x8000;
        private const ushort DskWriteMode = 0x4000;
        private const ushort DskLengthMask = 0x3FFF;
        private const ushort AdkconFast = 0x0100;
        private const ushort AdkconMsbSync = 0x0200;
        private const ushort AdkconWordSync = 0x0400;
        private const ushort AdkconPrecompMask = 0x6000;
        private const int MaxDmaTraceEntries = 4096;
        private const int DiskRevolutionsPerSecond = 5;
        private const double DiskStreamPositionQuantum = 1e-6;
        private static readonly double FastDiskCyclesPerBit = AmigaConstants.A500PalCpuClockHz / 500_000.0;
        private static readonly long DiskWordEqualHoldCycles = Math.Max(
            1,
            (long)Math.Round(AmigaConstants.A500PalCpuClockHz / 500_000.0));
        private static readonly long DiskIndexPulseCycles = Math.Max(
            1,
            (long)Math.Round(AmigaConstants.A500PalCpuClockHz / DiskRevolutionsPerSecond));
        private static readonly long DiskMotorReadyDelayCycles = Math.Max(
            1,
            (long)Math.Round(AmigaConstants.A500PalCpuClockHz * 0.5));
        private static readonly bool UseLegacyDiskInputPublisher =
            string.Equals(Environment.GetEnvironmentVariable("COPPER_AMIGA_LEGACY_DISK_INPUT"), "1", StringComparison.Ordinal);

        private readonly AmigaBus _bus;
        private readonly bool _specializationEnabled;
        private readonly AmigaFloppyDrive[] _drives;
        private readonly DiskTrackEventPlan?[] _diskInputPlans;
        private readonly int[] _diskInputPlanEventIndexes;
        private readonly List<AmigaDiskDmaTraceEntry> _dmaTrace = new List<AmigaDiskDmaTraceEntry>(MaxDmaTraceEntries);
        private readonly AmigaDiskTraceRecorder? _traceRecorder;
        private long _currentCycle;
        private ushort _dsklen;
        private ushort _dsksync = 0x4489;
        private uint _diskPointer;
        private byte _ciabPortB = 0xFF;
        private ushort _diskDataRegister;
        private byte _dskbytrData;
        private bool _dskbytrByteReady;
        private long _dskbytrWordEqualUntilCycle;
        private int _transferCount;
        private int _lastTransferWords;
        private int _lastTransferDrive;
        private int _lastTransferCylinder;
        private int _lastTransferHead;
        private uint _lastTransferAddress;
        private ushort? _armedDsklen;
        private readonly DiskStreamState[] _streams;
        private long _nextIndexPulseCycle;
        private long _nextDiskInputAdvanceCycle;
        private long _nextDiskSyncAdvanceCycle;
        private ushort _pendingReadDmaWords;
        private ushort _pendingWriteDmaWords;
        private bool _activeDma;
        private bool _activeDmaWriteMode;
        private int _activeDmaDrive;
        private uint _activeDmaTargetAddress;
        private int _activeDmaNextSourceBit;
        private long _activeDmaNextWordStartCycle;
        private AmigaDiskSyncMode _activeDmaSyncMode;
        private ushort _activeDmaRequestedWords;
        private int _activeDmaTransferredWords;
        private int _activeDmaCylinder;
        private int _activeDmaHead;
        private int _activeDmaTrackBitLength;
        private int _activeDmaTrackStartBit;
        private AmigaTrackFeatures _activeDmaTrackFeatures;
        private double _activeDmaStreamStartPosition;
        private long _activeDmaStartCycle;
        private long _activeDmaCompletionCycle;
        private long _activeDmaNextBusRequestCycle;
        private double _activeDmaCyclesPerBit;
        private DiskDmaWordLatch _diskDmaWordLatch;
        private bool _activeDmaRequestPending;
        private uint _activeDmaRequestTargetAddress;
        private int _activeDmaRequestSourceBit;
        private ushort _activeDmaRequestReadValue;
        private long _activeDmaRequestCycle;
        private long _activeDmaRequestServiceCycle;
        private int _activeDmaRequestNextSourceBit;
        private long _activeDmaRequestNextWordStartCycle;
        private ActiveDmaWordPlan _activeDmaCachedWordPlan;
        private bool _activeDmaCachedWordPlanValid;
        private ActiveDmaTransferPlan? _activeDmaTransferPlan;
        private byte[]? _activeWriteTrackData;
        private bool _activeWriteTrackMutated;
        private long _preparedTrackHits;
        private long _preparedTrackMisses;
        private long _rollingWindowHits;
        private long _rollingWindowMisses;
        private long _syncIndexHits;
        private long _syncIndexMisses;
        private long _scalarBitReads;
        private long _fullTrackSyncScans;
        private long _fullTrackSyncScanBits;
        private long _fullTrackWordSyncScans;
        private long _fullTrackByteSyncScans;
        private long _activeDmaPredictionPasses;
        private long _activeDmaPredictedWordPlans;
        private long _activeDmaRuntimeWordPlans;
        private long _activeDmaRequestsCreated;
        private long _activeDmaRequestsServed;
        private long _activeDmaRequestsBlocked;
        private long _schedulerNextWakeCandidateQueries;
        private long _schedulerNextEventWakeCandidateQueries;
        private long _schedulerHasWakeCandidateThroughQueries;
        private long _schedulerHasEventWakeCandidateThroughQueries;
        private long _schedulerRefreshNextIndexPulseQueries;
        private long _schedulerInputAdvanceCalls;
        private long _schedulerGateTrue;
        private long _schedulerGateFalse;
        private long _schedulerPendingDmaWakeSources;
        private long _schedulerActiveDmaProgressWakeSources;
        private long _schedulerActiveDmaCompletionWakeSources;
        private long _schedulerSyncCandidateWakeSources;
        private long _schedulerIndexPulseWakeSources;
        private long _schedulerPassiveByteReadyWakeSources;
        private ulong _schedulerWakeVersion;

        public AmigaDiskController(AmigaBus bus, int connectedDriveCount = 1, bool enableSpecialization = false)
        {
            if (connectedDriveCount is < 1 or > MaxFloppyDriveCount)
            {
                throw new ArgumentOutOfRangeException(nameof(connectedDriveCount), connectedDriveCount, "The connected floppy drive count must be between 1 and 4.");
            }

            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _specializationEnabled = enableSpecialization;
            ConnectedDriveCount = connectedDriveCount;
            Drive0 = new AmigaFloppyDrive();
            Drive1 = new AmigaFloppyDrive();
            Drive2 = new AmigaFloppyDrive();
            Drive3 = new AmigaFloppyDrive();
            _drives = [Drive0, Drive1, Drive2, Drive3];
            _streams = [.. Enumerable.Range(0, MaxFloppyDriveCount).Select(_ => new DiskStreamState())];
            _diskInputPlans = new DiskTrackEventPlan?[MaxFloppyDriveCount];
            _diskInputPlanEventIndexes = new int[MaxFloppyDriveCount];
            _traceRecorder = AmigaDiskTraceRecorder.IsEnvironmentEnabled()
                ? new AmigaDiskTraceRecorder()
                : null;
            Reset();
        }

        public int ConnectedDriveCount { get; }

        public AmigaFloppyDrive Drive0 { get; }

        public AmigaFloppyDrive Drive1 { get; }

        public AmigaFloppyDrive Drive2 { get; }

        public AmigaFloppyDrive Drive3 { get; }

        public int TransferCount => _transferCount;

        public bool DivergenceTraceEnabled => _traceRecorder != null;

        internal ulong SchedulerWakeVersion
        {
            get
            {
                unchecked
                {
                    return _schedulerWakeVersion ^
                        (Drive0.WakeVersion * 397UL) ^
                        (Drive1.WakeVersion * 1543UL) ^
                        (Drive2.WakeVersion * 6151UL) ^
                        (Drive3.WakeVersion * 24593UL);
                }
            }
        }

        internal void ConfigureDivergenceTrace(string backend, Func<AmigaDiskTraceCpuContext>? cpuContextProvider)
            => _traceRecorder?.Configure(backend, cpuContextProvider);

        public AmigaDiskControllerSnapshot CaptureSnapshot()
        {
            return new AmigaDiskControllerSnapshot(
                _diskPointer,
                _dsklen,
                _dsksync,
                PeekDskbytr(),
                Drive0.Cylinder,
                Drive0.Head,
                Drive0.MotorOn,
                Drive0.Selected,
                _ciabPortB,
                _transferCount,
                _lastTransferWords,
                _lastTransferDrive,
                _lastTransferCylinder,
                _lastTransferHead,
                _lastTransferAddress,
                GetSelectedDriveIndex(),
                _activeDmaDrive,
                _activeDma,
                _activeDmaCompletionCycle,
                _diskDataRegister,
                ConnectedDriveCount,
                CaptureSpecializationCounters(),
                CaptureSchedulerCounters());
        }

        public AmigaDiskSpecializationCounters CaptureSpecializationCounters()
            => new AmigaDiskSpecializationCounters(
                _preparedTrackHits,
                _preparedTrackMisses,
                _rollingWindowHits,
                _rollingWindowMisses,
                _syncIndexHits,
                _syncIndexMisses,
                _scalarBitReads,
                _fullTrackSyncScans,
                _fullTrackSyncScanBits,
                _fullTrackWordSyncScans,
                _fullTrackByteSyncScans,
                _activeDmaPredictionPasses,
                _activeDmaPredictedWordPlans,
                _activeDmaRuntimeWordPlans,
                _activeDmaRequestsCreated,
                _activeDmaRequestsServed,
                _activeDmaRequestsBlocked);

        public AmigaDiskSchedulerCounters CaptureSchedulerCounters()
            => new AmigaDiskSchedulerCounters(
                _schedulerNextWakeCandidateQueries,
                _schedulerNextEventWakeCandidateQueries,
                _schedulerHasWakeCandidateThroughQueries,
                _schedulerHasEventWakeCandidateThroughQueries,
                _schedulerRefreshNextIndexPulseQueries,
                _schedulerInputAdvanceCalls,
                _schedulerGateTrue,
                _schedulerGateFalse,
                _schedulerPendingDmaWakeSources,
                _schedulerActiveDmaProgressWakeSources,
                _schedulerActiveDmaCompletionWakeSources,
                _schedulerSyncCandidateWakeSources,
                _schedulerIndexPulseWakeSources,
                _schedulerPassiveByteReadyWakeSources);

        public AmigaDiskDmaTraceEntry[] CaptureDmaTrace()
            => _dmaTrace.ToArray();

        public AmigaDiskTraceEvent[] CaptureDivergenceTrace()
            => _traceRecorder?.Capture() ?? Array.Empty<AmigaDiskTraceEvent>();

        public void ClearDmaTrace()
        {
            _dmaTrace.Clear();
            _traceRecorder?.Clear();
        }

        public void Reset()
        {
            _currentCycle = 0;
            _dsklen = 0;
            _dsksync = 0x4489;
            _diskPointer = 0;
            _ciabPortB = 0xFF;
            _diskDataRegister = 0;
            _dskbytrData = 0;
            _dskbytrByteReady = false;
            _dskbytrWordEqualUntilCycle = 0;
            _transferCount = 0;
            _dmaTrace.Clear();
            _traceRecorder?.Clear();
            _lastTransferWords = 0;
            _lastTransferDrive = -1;
            _lastTransferCylinder = 0;
            _lastTransferHead = 0;
            _lastTransferAddress = 0;
            _armedDsklen = null;
            _nextIndexPulseCycle = long.MaxValue;
            _nextDiskInputAdvanceCycle = 0;
            _nextDiskSyncAdvanceCycle = 0;
            foreach (var stream in _streams)
            {
                stream.Reset();
            }

            InvalidateDiskInputPlans();
            _pendingReadDmaWords = 0;
            _pendingWriteDmaWords = 0;
            ClearActiveDma();
            _preparedTrackHits = 0;
            _preparedTrackMisses = 0;
            _rollingWindowHits = 0;
            _rollingWindowMisses = 0;
            _syncIndexHits = 0;
            _syncIndexMisses = 0;
            _scalarBitReads = 0;
            _fullTrackSyncScans = 0;
            _fullTrackSyncScanBits = 0;
            _fullTrackWordSyncScans = 0;
            _fullTrackByteSyncScans = 0;
            _activeDmaPredictionPasses = 0;
            _activeDmaPredictedWordPlans = 0;
            _activeDmaRuntimeWordPlans = 0;
            _activeDmaRequestsCreated = 0;
            _activeDmaRequestsServed = 0;
            _activeDmaRequestsBlocked = 0;
            _schedulerNextWakeCandidateQueries = 0;
            _schedulerNextEventWakeCandidateQueries = 0;
            _schedulerHasWakeCandidateThroughQueries = 0;
            _schedulerHasEventWakeCandidateThroughQueries = 0;
            _schedulerRefreshNextIndexPulseQueries = 0;
            _schedulerInputAdvanceCalls = 0;
            _schedulerGateTrue = 0;
            _schedulerGateFalse = 0;
            _schedulerPendingDmaWakeSources = 0;
            _schedulerActiveDmaProgressWakeSources = 0;
            _schedulerActiveDmaCompletionWakeSources = 0;
            _schedulerSyncCandidateWakeSources = 0;
            _schedulerIndexPulseWakeSources = 0;
            _schedulerPassiveByteReadyWakeSources = 0;
            foreach (var drive in _drives)
            {
                drive.ResetPosition();
            }

            _schedulerWakeVersion++;
        }

        public void AdvanceTo(long targetCycle)
        {
            if (targetCycle < _currentCycle)
            {
                return;
            }

            if (CanSkipAdvanceTo(targetCycle))
            {
                if (_currentCycle != targetCycle)
                {
                    _currentCycle = targetCycle;
                    _schedulerWakeVersion++;
                }

                return;
            }

            _currentCycle = targetCycle;
            if (_activeDma &&
                _activeDmaTransferredWords >= _activeDmaRequestedWords &&
                _activeDmaCompletionCycle > 0 &&
                targetCycle > _activeDmaCompletionCycle)
            {
                AdvanceDiskInputTo(_activeDmaCompletionCycle);
                AdvanceActiveDmaTo(_activeDmaCompletionCycle);
                AdvanceDiskInputTo(targetCycle);
                TryStartPendingDma(targetCycle);
            }
            else
            {
                AdvanceDiskInputTo(targetCycle);
                TryStartPendingDma(targetCycle);
                AdvanceActiveDmaTo(targetCycle);
            }

            AdvanceIndexPulsesTo(targetCycle);
            _schedulerWakeVersion++;
        }

        public void AdvanceEventsTo(long targetCycle)
        {
            if (targetCycle < _currentCycle)
            {
                return;
            }

            var hasPendingDma = _pendingReadDmaWords != 0 || _pendingWriteDmaWords != 0;
            var hasActiveDmaEvent = GetNextActiveDmaAdvanceCycle() <= targetCycle;
            var hasSyncEvent = GetNextSelectedSyncCompletionCycleCached(targetCycle) <= targetCycle;
            RefreshNextIndexPulseCycle();
            var hasIndexPulse = (_bus.CiaB.InterruptMask & AmigaCia.FlagInterruptMask) != 0 &&
                AnyConnectedDriveMotorOn() &&
                _nextIndexPulseCycle <= targetCycle;
            var hasEvent = hasPendingDma || hasActiveDmaEvent || hasSyncEvent || hasIndexPulse;
            if (targetCycle == _currentCycle && !hasEvent)
            {
                return;
            }

            var previousCycle = _currentCycle;
            _currentCycle = targetCycle;
            if (!hasEvent)
            {
                if (previousCycle != _currentCycle)
                {
                    _schedulerWakeVersion++;
                }

                return;
            }

            if (hasSyncEvent)
            {
                AdvanceDiskInputTo(targetCycle);
            }

            if (hasPendingDma)
            {
                TryStartPendingDma(targetCycle);
            }

            if (hasActiveDmaEvent || _activeDma)
            {
                AdvanceActiveDmaTo(targetCycle);
            }

            if (hasIndexPulse)
            {
                AdvanceIndexPulsesTo(targetCycle);
            }

            _schedulerWakeVersion++;
        }

        public void AdvanceCiaEventsTo(long targetCycle)
        {
            if (targetCycle < _currentCycle)
            {
                return;
            }

            RefreshNextIndexPulseCycle();
            if (targetCycle == _currentCycle && targetCycle < _nextIndexPulseCycle)
            {
                return;
            }

            var previousCycle = _currentCycle;
            _currentCycle = targetCycle;
            AdvanceIndexPulsesTo(targetCycle);
            if (previousCycle != _currentCycle || targetCycle >= _nextIndexPulseCycle)
            {
                _schedulerWakeVersion++;
            }
        }

        private void AdvanceIndexPulsesTo(long targetCycle)
        {
            RefreshNextIndexPulseCycle();
            if (targetCycle < _nextIndexPulseCycle)
            {
                return;
            }

            while (_nextIndexPulseCycle <= targetCycle)
            {
                var pulseCycle = _nextIndexPulseCycle;
                var pulse = false;
                for (var driveIndex = 0; driveIndex < ConnectedDriveCount; driveIndex++)
                {
                    var drive = _drives[driveIndex];
                    var stream = _streams[driveIndex];
                    if (drive.Disk == null ||
                        !drive.MotorOn ||
                        stream.NextIndexPulseCycle > pulseCycle)
                    {
                        continue;
                    }

                    pulse = true;
                    var period = GetDriveIndexPeriodCycles(driveIndex);
                    do
                    {
                        stream.NextIndexPulseCycle += period;
                    }
                    while (stream.NextIndexPulseCycle <= pulseCycle);
                }

                if (pulse)
                {
                    _bus.PulseCiaFlag(AmigaCiaId.B, pulseCycle);
                }

                RefreshNextIndexPulseCycle();
                if (!pulse && _nextIndexPulseCycle <= pulseCycle)
                {
                    _nextIndexPulseCycle = long.MaxValue;
                    return;
                }
            }
        }

        private bool HasEventAdvanceTo(long targetCycle)
        {
            RefreshNextIndexPulseCycle();
            if (_pendingReadDmaWords != 0 || _pendingWriteDmaWords != 0)
            {
                return true;
            }

            if (GetNextActiveDmaAdvanceCycle() <= targetCycle)
            {
                return true;
            }

            if (GetNextSelectedSyncCompletionCycleCached(targetCycle) <= targetCycle)
            {
                return true;
            }

            return (_bus.CiaB.InterruptMask & AmigaCia.FlagInterruptMask) != 0 &&
                AnyConnectedDriveMotorOn() &&
                _nextIndexPulseCycle <= targetCycle;
        }

        private bool CanSkipAdvanceTo(long targetCycle)
        {
            RefreshNextIndexPulseCycle();
            if (_pendingReadDmaWords != 0 || _pendingWriteDmaWords != 0)
            {
                return false;
            }

            var nextDiskInput = _nextDiskInputAdvanceCycle > 0
                ? _nextDiskInputAdvanceCycle
                : long.MaxValue;
            if (_nextDiskInputAdvanceCycle == 0 && HasUnknownSelectedDiskInput(targetCycle))
            {
                return false;
            }

            var nextEventCycle = Math.Min(
                Math.Min(nextDiskInput, GetNextActiveDmaAdvanceCycle()),
                _nextIndexPulseCycle);
            return targetCycle < nextEventCycle;
        }

        private bool HasUnknownSelectedDiskInput(long targetCycle)
        {
            var driveIndex = GetSelectedDriveIndex();
            if (driveIndex < 0 || !IsDriveConnected(driveIndex))
            {
                return false;
            }

            var drive = _drives[driveIndex];
            return drive.Disk != null &&
                drive.MotorOn &&
                targetCycle >= GetDriveReadyCycle(drive);
        }

        public long? GetNextWakeCandidateCycle(long currentCycle, long targetCycle)
        {
            _schedulerNextWakeCandidateQueries++;
            if (targetCycle <= currentCycle)
            {
                return null;
            }

            RefreshNextIndexPulseCycle();
            long? candidate = null;
            candidate = MinWakeCandidate(candidate, GetPendingDmaWakeCandidateCycle(currentCycle));
            candidate = MinWakeCandidate(candidate, GetPassiveInputWakeCandidateCycle(currentCycle, targetCycle));

            if (_nextDiskInputAdvanceCycle > 0)
            {
                candidate = MinWakeCandidate(candidate, _nextDiskInputAdvanceCycle);
            }

            if (_activeDma && IsDiskDmaControlEnabled())
            {
                candidate = MinWakeCandidate(candidate, GetActiveDmaWakeCycle(includeActiveDmaProgress: false));
            }

            if ((_bus.CiaB.InterruptMask & AmigaCia.FlagInterruptMask) != 0 && AnyConnectedDriveMotorOn())
            {
                candidate = MinWakeCandidate(candidate, _nextIndexPulseCycle);
            }

            var clamped = ClampWakeCandidate(candidate, currentCycle, targetCycle);
            if (clamped.HasValue)
            {
                AppendDivergenceTrace(
                    AmigaDiskTraceEventKind.WakeCandidate,
                    currentCycle,
                    targetCycle: targetCycle,
                    candidateCycle: clamped.Value,
                    detail: "candidate");
            }

            return clamped;
        }

        internal bool HasWakeCandidateThrough(long targetCycle)
        {
            _schedulerHasWakeCandidateThroughQueries++;
            if (targetCycle < _currentCycle)
            {
                return false;
            }

            var probeCycle = targetCycle > 0 ? targetCycle - 1 : -1;
            return GetNextWakeCandidateCycle(probeCycle, targetCycle).HasValue;
        }

        internal bool HasEventWakeCandidateThrough(long targetCycle, bool includeActiveDmaProgress = false)
        {
            _schedulerHasEventWakeCandidateThroughQueries++;
            if (targetCycle < _currentCycle)
            {
                return false;
            }

            var probeCycle = targetCycle > 0 ? targetCycle - 1 : -1;
            return GetNextEventWakeCandidateCycle(probeCycle, targetCycle, includeActiveDmaProgress).HasValue;
        }

        internal bool HasCiaEventThrough(long targetCycle)
        {
            if (targetCycle < _currentCycle)
            {
                return false;
            }

            RefreshNextIndexPulseCycle();
            return _nextIndexPulseCycle <= targetCycle;
        }

        internal bool HasCiaWakeSource()
        {
            var hasSource = HasObservedIndexPulseSource();
            RecordSchedulerWakeReason(hasSource ? SchedulerWakeReason.IndexPulse : SchedulerWakeReason.None);
            return hasSource;
        }

        internal bool HasSchedulerWakeSourceThrough(
            long targetCycle,
            bool includePassiveInput,
            bool includeEvents,
            bool includeActiveDmaProgress)
        {
            if (targetCycle < _currentCycle)
            {
                RecordSchedulerWakeReason(SchedulerWakeReason.None);
                return false;
            }

            var reason = GetSchedulerWakeReasonThrough(
                targetCycle,
                includePassiveInput,
                includeEvents,
                includeActiveDmaProgress);
            RecordSchedulerWakeReason(reason);
            return reason != SchedulerWakeReason.None;
        }

        internal long? GetNextEventWakeCandidateCycle(
            long currentCycle,
            long targetCycle,
            bool includeActiveDmaProgress = false)
        {
            _schedulerNextEventWakeCandidateQueries++;
            if (targetCycle <= currentCycle)
            {
                return null;
            }

            RefreshNextIndexPulseCycle();
            long? candidate = null;
            candidate = MinWakeCandidate(candidate, GetPendingDmaWakeCandidateCycle(currentCycle));

            if (_activeDma && IsDiskDmaControlEnabled())
            {
                candidate = MinWakeCandidate(
                    candidate,
                    GetActiveDmaWakeCycle(includeActiveDmaProgress));
            }

            var syncCycle = GetNextSelectedSyncCompletionCycleCached(targetCycle);
            if (syncCycle <= targetCycle)
            {
                candidate = MinWakeCandidate(candidate, syncCycle);
            }

            if ((_bus.CiaB.InterruptMask & AmigaCia.FlagInterruptMask) != 0 && AnyConnectedDriveMotorOn())
            {
                candidate = MinWakeCandidate(candidate, _nextIndexPulseCycle);
            }

            return ClampWakeCandidate(candidate, currentCycle, targetCycle);
        }

        internal long? GetNextSlotDmaWakeCandidateCycle(long currentCycle, long targetCycle)
        {
            if (targetCycle <= currentCycle)
            {
                return null;
            }

            long? candidate = null;
            candidate = MinWakeCandidate(candidate, GetPendingDmaWakeCandidateCycle(currentCycle));

            if (_activeDma && IsDiskDmaControlEnabled())
            {
                candidate = MinWakeCandidate(candidate, GetNextActiveDmaAdvanceCycle());
            }

            return ClampWakeCandidate(candidate, currentCycle, targetCycle);
        }

        internal bool HasSlotDmaWakeSourceThrough(long targetCycle)
        {
            if (targetCycle < _currentCycle)
            {
                RecordSchedulerWakeReason(SchedulerWakeReason.None);
                return false;
            }

            var reason = GetSlotDmaWakeReasonThrough(targetCycle);
            RecordSchedulerWakeReason(reason);
            return reason != SchedulerWakeReason.None;
        }

        private SchedulerWakeReason GetSchedulerWakeReasonThrough(
            long targetCycle,
            bool includePassiveInput,
            bool includeEvents,
            bool includeActiveDmaProgress)
        {
            if (includePassiveInput)
            {
                var passiveReason = GetPassiveInputWakeReasonThrough(targetCycle);
                if (passiveReason != SchedulerWakeReason.None)
                {
                    return passiveReason;
                }
            }

            return includeEvents
                ? GetEventWakeReasonThrough(targetCycle, includeActiveDmaProgress)
                : SchedulerWakeReason.None;
        }

        private SchedulerWakeReason GetPassiveInputWakeReasonThrough(long targetCycle)
        {
            if (HasPendingDmaWakeSourceThrough(targetCycle))
            {
                return SchedulerWakeReason.PendingDma;
            }

            if (_nextDiskInputAdvanceCycle > 0 && _nextDiskInputAdvanceCycle <= targetCycle)
            {
                return SchedulerWakeReason.PassiveByteReady;
            }

            if (HasUnknownSelectedDiskInput(targetCycle))
            {
                return SchedulerWakeReason.PassiveByteReady;
            }

            if (_activeDma &&
                IsDiskDmaControlEnabled() &&
                _activeDmaTransferredWords >= _activeDmaRequestedWords &&
                _activeDmaCompletionCycle <= targetCycle)
            {
                return SchedulerWakeReason.ActiveDmaCompletion;
            }

            return HasObservedIndexPulseSource()
                ? SchedulerWakeReason.IndexPulse
                : SchedulerWakeReason.None;
        }

        private SchedulerWakeReason GetEventWakeReasonThrough(long targetCycle, bool includeActiveDmaProgress)
        {
            if (HasPendingDmaWakeSourceThrough(targetCycle))
            {
                return SchedulerWakeReason.PendingDma;
            }

            if (_activeDma && IsDiskDmaControlEnabled())
            {
                var activeDmaCycle = GetActiveDmaWakeCycle(includeActiveDmaProgress);
                if (activeDmaCycle <= targetCycle)
                {
                    return !includeActiveDmaProgress || IsActiveDmaCompletionWakeCycle(activeDmaCycle)
                        ? SchedulerWakeReason.ActiveDmaCompletion
                        : SchedulerWakeReason.ActiveDmaProgress;
                }
            }

            if (_nextDiskSyncAdvanceCycle > 0 && _nextDiskSyncAdvanceCycle <= targetCycle)
            {
                return SchedulerWakeReason.SyncCandidate;
            }

            if (_nextDiskSyncAdvanceCycle == 0 && HasUnknownSelectedDiskInput(targetCycle))
            {
                return SchedulerWakeReason.SyncCandidate;
            }

            return HasObservedIndexPulseSource()
                ? SchedulerWakeReason.IndexPulse
                : SchedulerWakeReason.None;
        }

        private SchedulerWakeReason GetSlotDmaWakeReasonThrough(long targetCycle)
        {
            if (HasPendingDmaWakeSourceThrough(targetCycle))
            {
                return SchedulerWakeReason.PendingDma;
            }

            if (!_activeDma || !IsDiskDmaControlEnabled())
            {
                return SchedulerWakeReason.None;
            }

            var activeDmaCycle = GetNextActiveDmaAdvanceCycle();
            if (activeDmaCycle > targetCycle)
            {
                return SchedulerWakeReason.None;
            }

            return IsActiveDmaCompletionWakeCycle(activeDmaCycle)
                ? SchedulerWakeReason.ActiveDmaCompletion
                : SchedulerWakeReason.ActiveDmaProgress;
        }

        private bool HasPendingDmaWakeSourceThrough(long targetCycle)
        {
            if ((_pendingReadDmaWords == 0 && _pendingWriteDmaWords == 0) || !IsDiskDmaControlEnabled())
            {
                return false;
            }

            var driveIndex = GetSelectedDriveIndex();
            if (driveIndex < 0 || !IsDriveConnected(driveIndex))
            {
                return false;
            }

            var drive = _drives[driveIndex];
            return drive.Disk != null &&
                drive.MotorOn &&
                GetDriveReadyCycle(drive) <= targetCycle;
        }

        private bool HasObservedIndexPulseSource()
            => (_bus.CiaB.InterruptMask & AmigaCia.FlagInterruptMask) != 0 &&
                AnyConnectedDriveMotorOn();

        private void RecordSchedulerWakeReason(SchedulerWakeReason reason)
        {
            if (reason == SchedulerWakeReason.None)
            {
                _schedulerGateFalse++;
                return;
            }

            _schedulerGateTrue++;
            switch (reason)
            {
                case SchedulerWakeReason.PendingDma:
                    _schedulerPendingDmaWakeSources++;
                    break;
                case SchedulerWakeReason.ActiveDmaProgress:
                    _schedulerActiveDmaProgressWakeSources++;
                    break;
                case SchedulerWakeReason.ActiveDmaCompletion:
                    _schedulerActiveDmaCompletionWakeSources++;
                    break;
                case SchedulerWakeReason.SyncCandidate:
                    _schedulerSyncCandidateWakeSources++;
                    break;
                case SchedulerWakeReason.IndexPulse:
                    _schedulerIndexPulseWakeSources++;
                    break;
                case SchedulerWakeReason.PassiveByteReady:
                    _schedulerPassiveByteReadyWakeSources++;
                    break;
            }
        }

        private long? GetPassiveInputWakeCandidateCycle(long currentCycle, long targetCycle)
        {
            if (_nextDiskInputAdvanceCycle > 0)
            {
                return _nextDiskInputAdvanceCycle;
            }

            if (!HasUnknownSelectedDiskInput(targetCycle))
            {
                return null;
            }

            var driveIndex = GetSelectedDriveIndex();
            if (driveIndex < 0 || !IsDriveConnected(driveIndex))
            {
                return null;
            }

            var readyCycle = GetDriveReadyCycle(_drives[driveIndex]);
            return currentCycle >= readyCycle ? currentCycle : readyCycle;
        }

        private long? GetPendingDmaWakeCandidateCycle(long currentCycle)
        {
            if ((_pendingReadDmaWords == 0 && _pendingWriteDmaWords == 0) || !IsDiskDmaControlEnabled())
            {
                return null;
            }

            var driveIndex = GetSelectedDriveIndex();
            if (driveIndex < 0 || !IsDriveConnected(driveIndex))
            {
                return null;
            }

            var drive = _drives[driveIndex];
            if (drive.Disk == null || !drive.MotorOn)
            {
                return null;
            }

            var readyCycle = GetDriveReadyCycle(drive);
            return currentCycle >= readyCycle ? currentCycle + 1 : readyCycle;
        }

        private long GetNextActiveDmaAdvanceCycle()
        {
            if (!_activeDma)
            {
                return long.MaxValue;
            }

            if (!IsDiskDmaControlEnabled())
            {
                return _currentCycle;
            }

            if (_activeDmaRequestPending)
            {
                return Math.Min(_activeDmaRequestServiceCycle, _activeDmaCompletionCycle);
            }

            if (_activeDmaTransferredWords >= _activeDmaRequestedWords)
            {
                return _activeDmaCompletionCycle;
            }

            var nextWordCompletionCycle = _activeDmaNextWordStartCycle + CyclesForBits(16, _activeDmaCyclesPerBit);
            return Math.Min(nextWordCompletionCycle, _activeDmaCompletionCycle);
        }

        private long GetActiveDmaWakeCycle(bool includeActiveDmaProgress)
        {
            if (!_activeDma || !IsDiskDmaControlEnabled())
            {
                return long.MaxValue;
            }

            if (includeActiveDmaProgress || _activeDmaTransferredWords < _activeDmaRequestedWords)
            {
                return includeActiveDmaProgress
                    ? GetNextActiveDmaAdvanceCycle()
                    : _activeDmaCompletionCycle;
            }

            return _activeDmaCompletionCycle;
        }

        private bool IsActiveDmaCompletionWakeCycle(long cycle)
            => _activeDmaTransferredWords >= _activeDmaRequestedWords &&
                _activeDmaCompletionCycle > 0 &&
                cycle >= _activeDmaCompletionCycle;

        private static long? MinWakeCandidate(long? candidate, long? eventCycle)
        {
            if (!eventCycle.HasValue)
            {
                return candidate;
            }

            return MinWakeCandidate(candidate, eventCycle.Value);
        }

        private static long? MinWakeCandidate(long? candidate, long eventCycle)
        {
            if (!candidate.HasValue)
            {
                return eventCycle;
            }

            return Math.Min(candidate.Value, eventCycle);
        }

        private static long? ClampWakeCandidate(long? candidate, long currentCycle, long targetCycle)
        {
            if (!candidate.HasValue || candidate.Value > targetCycle)
            {
                return null;
            }

            return candidate.Value <= currentCycle ? currentCycle + 1 : candidate.Value;
        }

        public byte ReadCiaAPortA(byte latchedPortA)
        {
            var value = (byte)(latchedPortA | 0xFC);
            var driveIndex = GetSelectedLineDriveIndex();
            if (driveIndex >= 0 && !IsDriveConnected(driveIndex))
            {
                return value;
            }

            var drive = driveIndex >= 0 ? _drives[driveIndex] : Drive0;
            value = SetActiveLow(value, 2, drive.DiskChanged || drive.Disk == null);
            value = SetActiveLow(value, 3, drive.Disk != null && drive.WriteProtected);
            value = SetActiveLow(value, 4, drive.Disk != null && drive.Cylinder == 0);
            value = SetActiveLow(value, 5, IsDriveReady(drive, _currentCycle));
            return value;
        }

        public byte ReadByte(ushort offset)
        {
            return TryReadByte(offset, out var value) ? value : (byte)0;
        }

        public bool TryReadWord(ushort offset, out ushort value)
        {
            offset = (ushort)(offset & 0x01FE);
            if (!IsDiskRegister(offset))
            {
                value = 0;
                return false;
            }

            value = ReadWord(offset);
            return true;
        }

        public bool TryReadByte(ushort offset, out byte value)
        {
            var wordOffset = (ushort)(offset & 0x01FE);
            if (!IsDiskRegister(wordOffset))
            {
                value = 0;
                return false;
            }

            var word = ReadWord(wordOffset);
            value = (offset & 1) == 0 ? (byte)(word >> 8) : (byte)word;
            return true;
        }

        public ushort ReadWord(ushort offset)
        {
            offset = (ushort)(offset & 0x01FE);
            return offset switch
            {
                DskDatr => _diskDataRegister,
                DskBytr => ReadDskbytr(),
                DskPth => (ushort)((_diskPointer >> 16) & (_bus.ChipDmaAddressMask >> 16)),
                DskPtl => (ushort)(_diskPointer & 0xFFFE),
                DskLen => _dsklen,
                DskSync => _dsksync,
                _ => 0
            };
        }

        public void WriteRegister(ushort offset, ushort value, long cycle)
        {
            _currentCycle = Math.Max(_currentCycle, cycle);
            offset = (ushort)(offset & 0x01FE);
            switch (offset)
            {
                case DskDat:
                    _diskDataRegister = value;
                    AppendDivergenceTrace(AmigaDiskTraceEventKind.RegisterWrite, cycle, offset, value);
                    break;
                case DskPth:
                    _diskPointer = _bus.WriteChipDmaPointerHigh(_diskPointer, value);
                    AppendDivergenceTrace(AmigaDiskTraceEventKind.RegisterWrite, cycle, offset, value);
                    break;
                case DskPtl:
                    _diskPointer = _bus.WriteChipDmaPointerLow(_diskPointer, value);
                    AppendDivergenceTrace(AmigaDiskTraceEventKind.RegisterWrite, cycle, offset, value);
                    break;
                case DskLen:
                    AdvanceActiveDmaTo(cycle);
                    _dsklen = value;
                    if ((value & DskDmaEnable) == 0 || (value & DskLengthMask) == 0)
                    {
                        CancelActiveDma(cycle);
                        _armedDsklen = null;
                        _pendingReadDmaWords = 0;
                        _pendingWriteDmaWords = 0;
                        AppendDivergenceTrace(AmigaDiskTraceEventKind.RegisterWrite, cycle, offset, value);
                        break;
                    }

                    if (_armedDsklen == value)
                    {
                        _armedDsklen = null;
                        var requestedWords = (ushort)(value & DskLengthMask);
                        if ((value & DskWriteMode) != 0)
                        {
                            _pendingReadDmaWords = 0;
                            _pendingWriteDmaWords = requestedWords;
                        }
                        else
                        {
                            _pendingWriteDmaWords = 0;
                            _pendingReadDmaWords = requestedWords;
                        }

                        TryStartPendingDma(cycle);
                    }
                    else
                    {
                        _armedDsklen = value;
                    }

                    AppendDivergenceTrace(AmigaDiskTraceEventKind.RegisterWrite, cycle, offset, value);
                    break;
                case DskSync:
                    _dsksync = value;
                    _activeDmaCachedWordPlanValid = false;
                    _activeDmaTransferPlan = null;
                    InvalidateSyncCaches();
                    InvalidateNextDiskInputAdvanceCycle();
                    TryStartPendingDma(cycle);
                    AppendDivergenceTrace(AmigaDiskTraceEventKind.RegisterWrite, cycle, offset, value);
                    break;
                case AdkCon:
                    AdvanceDiskInputTo(cycle);
                    _activeDmaCachedWordPlanValid = false;
                    _activeDmaTransferPlan = null;
                    InvalidateSyncCaches();
                    InvalidateNextDiskInputAdvanceCycle();
                    AppendDivergenceTrace(AmigaDiskTraceEventKind.RegisterWrite, cycle, offset, value);
                    break;
            }

            _schedulerWakeVersion++;
        }

        public void WriteCiaBRegister(int register, byte value, long cycle)
        {
            if ((register & 0x0F) != 1)
            {
                return;
            }

            _currentCycle = Math.Max(_currentCycle, cycle);
            var previous = _ciabPortB;
            AdvanceCiaEventsTo(cycle);
            if (value != previous)
            {
                AdvanceDriveControlStreamsTo(cycle);
            }

            _ciabPortB = value;
            var head = (value & 0x04) == 0 ? 1 : 0;
            for (var driveIndex = 0; driveIndex < _drives.Length; driveIndex++)
            {
                if (!IsDriveConnected(driveIndex))
                {
                    _drives[driveIndex].SetSelected(false);
                    continue;
                }

                var selectMask = 1 << (driveIndex + 3);
                var previousSelected = (previous & selectMask) == 0;
                var selected = (value & selectMask) == 0;
                var drive = _drives[driveIndex];
                var previousMotorOn = drive.MotorOn;
                if (selected && !previousSelected)
                {
                    drive.SetMotorOn((value & 0x80) == 0, cycle);
                    if (drive.MotorOn != previousMotorOn)
                    {
                        _streams[driveIndex].Cycle = cycle;
                        ScheduleNextIndexPulseForDrive(driveIndex, cycle);
                    }
                }

                drive.SetSelected(selected);
                drive.SetHead(head);
            }

            InvalidateNextDiskInputAdvanceCycle();
            AppendDivergenceTrace(
                AmigaDiskTraceEventKind.CiaBDriveControlWrite,
                cycle,
                register: 0xB100,
                value);
            var previousStepHigh = (previous & 0x01) != 0;
            var stepHigh = (value & 0x01) != 0;
            if (previousStepHigh && !stepHigh)
            {
                var inward = (value & 0x02) == 0;
                for (var driveIndex = 0; driveIndex < _drives.Length; driveIndex++)
                {
                    if (!IsDriveConnected(driveIndex))
                    {
                        continue;
                    }

                    var drive = _drives[driveIndex];
                    if (drive.Selected)
                    {
                        drive.Step(inward ? 1 : -1);
                    }
                }
            }

            TryStartPendingDma(cycle);
            _schedulerWakeVersion++;
        }

        private void AdvanceDriveControlStreamsTo(long cycle)
        {
            for (var driveIndex = 0; driveIndex < _drives.Length; driveIndex++)
            {
                AdvanceDriveControlStreamTo(driveIndex, cycle);
            }
        }

        private void AdvanceDriveControlStreamTo(int driveIndex, long cycle)
        {
            var drive = _drives[driveIndex];
            var stream = _streams[driveIndex];
            if (!IsDriveConnected(driveIndex) ||
                cycle <= stream.Cycle)
            {
                return;
            }

            if (drive.Disk == null || !drive.MotorOn)
            {
                stream.Cycle = cycle;
                return;
            }

            var readyCycle = GetDriveReadyCycle(drive);
            if (cycle < readyCycle)
            {
                stream.Cycle = cycle;
                return;
            }

            if (stream.Cycle < readyCycle)
            {
                SetStreamPosition(stream, stream.Position, readyCycle);
            }

            var track = drive.ReadEncodedTrack(drive.Cylinder, drive.Head);
            ResetStreamIfTrackChanged(drive, stream, stream.Cycle);
            var cyclesPerBit = GetDiskDmaCyclesPerBit(track.BitLength);
            AdvanceStreamTo(stream, track.BitLength, cyclesPerBit, cycle);
        }

        private static bool IsDiskRegister(ushort offset)
        {
            return offset is DskDatr or DskBytr or DskPth or DskPtl or DskLen or DskSync;
        }

        internal static bool RequiresPassiveInputAdvance(uint customRegisterAddress)
        {
            var offset = (ushort)(customRegisterAddress & 0x01FE);
            return offset is DskDatr or DskBytr;
        }

        private ushort ReadDskbytr()
        {
            var value = PeekDskbytr();
            _dskbytrByteReady = false;
            return value;
        }

        private ushort PeekDskbytr()
        {
            var value = (ushort)_dskbytrData;
            if (_dskbytrByteReady)
            {
                value |= DskBytReady;
            }

            if (IsDiskDmaActuallyOn())
            {
                value |= DskBytDmaOn;
            }

            if ((_dsklen & DskWriteMode) != 0)
            {
                value |= DskBytDiskWrite;
            }

            if (_dskbytrWordEqualUntilCycle != 0 && GetDskbytrReferenceCycle() <= _dskbytrWordEqualUntilCycle)
            {
                value |= DskBytWordEqual;
            }

            return value;
        }

        private int GetReadyDmaDriveIndex()
        {
            for (var driveIndex = 0; driveIndex < _drives.Length; driveIndex++)
            {
                var drive = _drives[driveIndex];
                if (IsDriveConnected(driveIndex) && drive.Selected && IsDriveReady(drive, _currentCycle))
                {
                    return driveIndex;
                }
            }

            return -1;
        }

        private int GetSelectedDriveIndex()
        {
            for (var driveIndex = 0; driveIndex < _drives.Length; driveIndex++)
            {
                if (_drives[driveIndex].Selected)
                {
                    return driveIndex;
                }
            }

            return -1;
        }

        private int GetSelectedLineDriveIndex()
        {
            for (var driveIndex = 0; driveIndex < _drives.Length; driveIndex++)
            {
                var selectMask = 1 << (driveIndex + 3);
                if ((_ciabPortB & selectMask) == 0)
                {
                    return driveIndex;
                }
            }

            return -1;
        }

        private AmigaFloppyDrive? GetDriveOrNull(int driveIndex)
        {
            return IsDriveConnected(driveIndex) ? _drives[driveIndex] : null;
        }

        private bool IsDriveConnected(int driveIndex)
        {
            return (uint)driveIndex < (uint)ConnectedDriveCount;
        }

        private bool AnyConnectedDriveMotorOn()
        {
            for (var driveIndex = 0; driveIndex < ConnectedDriveCount; driveIndex++)
            {
                var drive = _drives[driveIndex];
                if (drive.Disk != null && drive.MotorOn)
                {
                    return true;
                }
            }

            return false;
        }

        private void ScheduleNextIndexPulseForDrive(int driveIndex, long afterCycle)
        {
            var stream = _streams[driveIndex];
            stream.NextIndexPulseCycle = GetInitialNextIndexPulseCycle(driveIndex, afterCycle);
            RefreshNextIndexPulseCycle();
        }

        private long GetInitialNextIndexPulseCycle(int driveIndex, long afterCycle)
        {
            if (!IsDriveConnected(driveIndex))
            {
                return long.MaxValue;
            }

            var drive = _drives[driveIndex];
            if (drive.Disk == null || !drive.MotorOn)
            {
                return long.MaxValue;
            }

            var period = GetDriveIndexPeriodCycles(driveIndex);
            var stream = _streams[driveIndex];
            var next = stream.Cycle + period;
            try
            {
                var track = drive.ReadEncodedTrack(drive.Cylinder, drive.Head);
                if (track.BitLength > 0)
                {
                    var cyclesPerBit = GetDiskRotationCyclesPerBit(track.BitLength);
                    var position = stream.Position % track.BitLength;
                    if (position < 0)
                    {
                        position += track.BitLength;
                    }

                    var distanceBits = track.BitLength - position;
                    if (distanceBits <= DiskStreamPositionQuantum)
                    {
                        distanceBits = track.BitLength;
                    }

                    next = stream.Cycle + CyclesForBits(distanceBits, cyclesPerBit);
                }
            }
            catch (InvalidOperationException)
            {
                next = afterCycle + period;
            }

            while (next <= afterCycle)
            {
                next += period;
            }

            return next;
        }

        private long GetDriveIndexPeriodCycles(int driveIndex)
        {
            if (!IsDriveConnected(driveIndex))
            {
                return DiskIndexPulseCycles;
            }

            var drive = _drives[driveIndex];
            if (drive.Disk == null)
            {
                return DiskIndexPulseCycles;
            }

            try
            {
                var track = drive.ReadEncodedTrack(drive.Cylinder, drive.Head);
                return track.BitLength > 0
                    ? CyclesForBits(track.BitLength, GetDiskRotationCyclesPerBit(track.BitLength))
                    : DiskIndexPulseCycles;
            }
            catch (InvalidOperationException)
            {
                return DiskIndexPulseCycles;
            }
        }

        private void RefreshNextIndexPulseCycle()
        {
            _schedulerRefreshNextIndexPulseQueries++;
            var next = long.MaxValue;
            for (var driveIndex = 0; driveIndex < ConnectedDriveCount; driveIndex++)
            {
                var drive = _drives[driveIndex];
                if (drive.Disk == null || !drive.MotorOn)
                {
                    _streams[driveIndex].NextIndexPulseCycle = long.MaxValue;
                    continue;
                }

                next = Math.Min(next, _streams[driveIndex].NextIndexPulseCycle);
            }

            _nextIndexPulseCycle = next;
        }

        private static bool IsDriveReady(AmigaFloppyDrive drive, long cycle)
        {
            return drive.Disk != null &&
                drive.MotorOn &&
                cycle >= drive.MotorOnCycle + DiskMotorReadyDelayCycles;
        }

        private static long GetDriveReadyCycle(AmigaFloppyDrive drive)
        {
            return drive.MotorOnCycle + DiskMotorReadyDelayCycles;
        }

        private DiskStreamState GetActiveDmaStream()
        {
            return (uint)_activeDmaDrive < (uint)_streams.Length ? _streams[_activeDmaDrive] : _streams[0];
        }

        private long GetDskbytrReferenceCycle()
        {
            var driveIndex = GetSelectedDriveIndex();
            return driveIndex >= 0 ? _streams[driveIndex].Cycle : _streams[0].Cycle;
        }

        private void InvalidateSyncCaches()
        {
            foreach (var stream in _streams)
            {
                stream.InvalidateSyncCache();
            }

            InvalidateDiskInputPlans();
        }

        private static byte SetActiveLow(byte value, int bit, bool asserted)
        {
            var mask = (byte)(1 << bit);
            return asserted ? (byte)(value & ~mask) : (byte)(value | mask);
        }

        private void TryStartPendingDma(long cycle)
        {
            if (_pendingReadDmaWords != 0 && StartDiskDma(_pendingReadDmaWords, writeMode: false, cycle))
            {
                _pendingReadDmaWords = 0;
                return;
            }

            if (_pendingWriteDmaWords != 0 && StartDiskDma(_pendingWriteDmaWords, writeMode: true, cycle))
            {
                _pendingWriteDmaWords = 0;
            }
        }

        private bool StartDiskDma(ushort requestedWords, bool writeMode, long cycle)
        {
            _currentCycle = Math.Max(_currentCycle, cycle);
            var driveIndex = GetReadyDmaDriveIndex();
            if (requestedWords == 0 || driveIndex < 0 || !IsDiskDmaControlEnabled())
            {
                AppendBlockedDmaStartTrace(requestedWords, cycle, GetDmaBlockedReason(requestedWords));
                return false;
            }

            CancelActiveDma(cycle);

            var drive = _drives[driveIndex];
            var stream = _streams[driveIndex];
            var track = drive.ReadEncodedTrack(drive.Cylinder, drive.Head);
            var preparedTrack = GetPreparedTrack(drive, stream, track);
            var cyclesPerBit = GetDiskDmaCyclesPerBit(track.BitLength);
            AdvanceDiskInputTo(cycle);
            ResetStreamIfTrackChanged(drive, stream, cycle);

            var sourceStartBit = stream.Offset;
            var syncWaitBits = 0;
            var syncMode = GetDmaSyncMode();
            var syncOffsetCount = 0;
            if (syncMode != AmigaDiskSyncMode.None)
            {
                syncOffsetCount = GetSyncOffsets(drive, stream, preparedTrack, track.BitLength, syncMode);
                var syncOffset = FindSyncOffset(preparedTrack, stream.Offset, syncMode, stream.SyncCacheOffsets, syncOffsetCount);
                if (syncOffset < 0)
                {
                    AppendDmaTrace(
                        AmigaDiskDmaTraceKind.SyncMissing,
                        cycle,
                        driveIndex,
                        drive.Cylinder,
                        drive.Head,
                        targetAddress: _bus.MaskChipDmaAddress(_diskPointer),
                        requestedWords,
                        transferredWords: 0,
                        sourceBit: stream.Offset,
                        syncWaitBits: -1,
                        track.BitLength,
                        syncMode,
                        completionCycle: 0,
                        blockedReason: AmigaDiskDmaBlockedReason.SyncMissing);
                    return false;
                }

                sourceStartBit = (syncOffset + GetSyncBitCount(syncMode)) % track.BitLength;
                syncWaitBits = (sourceStartBit - stream.Offset + track.BitLength) % track.BitLength;
            }

            var targetAddress = _bus.MaskChipDmaAddress(_diskPointer);
            _transferCount++;
            _lastTransferWords = requestedWords;
            _lastTransferDrive = driveIndex;
            _lastTransferCylinder = drive.Cylinder;
            _lastTransferHead = drive.Head;
            _lastTransferAddress = targetAddress;
            var dataStartCycle = cycle + CyclesForBits(syncWaitBits, cyclesPerBit);
            if (syncMode == AmigaDiskSyncMode.None)
            {
                var wordStartBit = GetRecoveredWordStartBit(stream.Position, track.BitLength);
                var elapsedWordBits = GetForwardBitDistance(wordStartBit, stream.Position, track.BitLength);
                var remainingWordBits = elapsedWordBits == 0 ? 16 : Math.Max(0, 16 - elapsedWordBits);
                sourceStartBit = wordStartBit;
                dataStartCycle = cycle + CyclesForBits(remainingWordBits, cyclesPerBit) - CyclesForBits(16, cyclesPerBit);
            }

            _activeDma = true;
            _activeDmaWriteMode = writeMode;
            _activeDmaDrive = driveIndex;
            _activeDmaTargetAddress = targetAddress;
            _activeDmaNextSourceBit = sourceStartBit;
            _activeDmaNextWordStartCycle = dataStartCycle;
            _activeDmaSyncMode = syncMode;
            _activeDmaRequestedWords = requestedWords;
            _activeDmaTransferredWords = 0;
            _activeDmaCylinder = drive.Cylinder;
            _activeDmaHead = drive.Head;
            _activeDmaTrackBitLength = track.BitLength;
            _activeDmaTrackStartBit = track.StartBit;
            _activeDmaTrackFeatures = track.Features;
            _activeDmaStreamStartPosition = stream.Position;
            _activeDmaStartCycle = cycle;
            _activeDmaTransferPlan = BuildActiveDmaTransferPlan(
                preparedTrack,
                _dsksync,
                firstWord: 0,
                sourceStartBit,
                dataStartCycle,
                requestedWords,
                dataStartCycle,
                cyclesPerBit,
                syncMode,
                stream.SyncCacheOffsets,
                stream.SyncCacheOffsetCount);

            _activeDmaCompletionCycle = _activeDmaTransferPlan.PredictedCompletionCycle;
            _activeDmaNextBusRequestCycle = dataStartCycle;
            _activeDmaCyclesPerBit = cyclesPerBit;
            _activeWriteTrackData = writeMode && !drive.WriteProtected && drive.Disk != null && drive.Disk.CanWriteTracks
                ? track.EncodedData.ToArray()
                : null;
            _activeWriteTrackMutated = false;
            AppendDmaTrace(
                AmigaDiskDmaTraceKind.Started,
                cycle,
                driveIndex,
                drive.Cylinder,
                drive.Head,
                targetAddress,
                requestedWords,
                transferredWords: 0,
                sourceStartBit,
                syncWaitBits,
                track.BitLength,
                syncMode,
                _activeDmaCompletionCycle);
            AdvanceActiveDmaTo(cycle);
            return true;
        }

        private void AppendBlockedDmaStartTrace(ushort requestedWords, long cycle, AmigaDiskDmaBlockedReason blockedReason)
        {
            var selectedDrive = GetSelectedDriveIndex();
            var drive = GetDriveOrNull(selectedDrive);
            var sourceBit = 0;
            var trackBitLength = 0;
            if (drive != null)
            {
                sourceBit = _streams[selectedDrive].Offset;
                if (drive.Disk != null)
                {
                    trackBitLength = drive.ReadEncodedTrack(drive.Cylinder, drive.Head).BitLength;
                }
            }

            AppendDmaTrace(
                AmigaDiskDmaTraceKind.StartBlocked,
                cycle,
                selectedDrive,
                drive?.Cylinder ?? 0,
                drive?.Head ?? 0,
                targetAddress: _bus.MaskChipDmaAddress(_diskPointer),
                requestedWords,
                transferredWords: 0,
                sourceBit,
                syncWaitBits: 0,
                trackBitLength,
                GetDmaSyncMode(),
                completionCycle: 0,
                blockedReason: blockedReason);
        }

        private AmigaDiskDmaBlockedReason GetDmaBlockedReason(ushort requestedWords)
        {
            if (requestedWords == 0)
            {
                return AmigaDiskDmaBlockedReason.ZeroLength;
            }

            if (!IsDiskDmaControlEnabled())
            {
                return AmigaDiskDmaBlockedReason.DmaDisabled;
            }

            var selectedLine = GetSelectedLineDriveIndex();
            if (selectedLine < 0)
            {
                return AmigaDiskDmaBlockedReason.NoSelectedDrive;
            }

            if (!IsDriveConnected(selectedLine))
            {
                return AmigaDiskDmaBlockedReason.SelectedDriveUnconnected;
            }

            var drive = _drives[selectedLine];
            if (drive.Disk == null)
            {
                return AmigaDiskDmaBlockedReason.NoDisk;
            }

            if (!drive.MotorOn)
            {
                return AmigaDiskDmaBlockedReason.MotorOff;
            }

            if (!IsDriveReady(drive, _currentCycle))
            {
                return AmigaDiskDmaBlockedReason.MotorNotReady;
            }

            return AmigaDiskDmaBlockedReason.NoReadyDrive;
        }

        private void AdvanceActiveDmaTo(long targetCycle)
        {
            if (!_activeDma)
            {
                return;
            }

            var activeDrive = GetDriveOrNull(_activeDmaDrive);
            if (activeDrive?.Disk == null)
            {
                ClearActiveDma();
                return;
            }

            if (!IsDiskDmaControlEnabled())
            {
                StopActiveDma(targetCycle);
                return;
            }

            if (targetCycle < _activeDmaStartCycle)
            {
                return;
            }

            var advanceCycle = targetCycle;
            if (_activeDmaTransferredWords < _activeDmaRequestedWords)
            {
                var track = activeDrive.ReadEncodedTrack(_activeDmaCylinder, _activeDmaHead);
                var stream = GetActiveDmaStream();
                var preparedTrack = GetPreparedTrack(activeDrive, stream, track, _activeDmaCylinder, _activeDmaHead);
                while (_activeDmaTransferredWords < _activeDmaRequestedWords)
                {
                    if (_activeDmaRequestPending)
                    {
                        if (!TryServeActiveDmaRequestThrough(advanceCycle))
                        {
                            break;
                        }

                        continue;
                    }

                    var word = _activeDmaTransferredWords;
                    var plan = GetOrCreateActiveDmaCachedWordPlan(
                        preparedTrack,
                        stream,
                        word);
                    if (plan.CompletionCycle > advanceCycle)
                    {
                        break;
                    }

                    CreateActiveDmaRequest(preparedTrack, word, plan);
                    if (!TryServeActiveDmaRequestThrough(advanceCycle))
                    {
                        break;
                    }
                }
            }

            if (_activeDmaTransferredWords < _activeDmaRequestedWords)
            {
                return;
            }

            if (targetCycle < _activeDmaCompletionCycle)
            {
                return;
            }

            CompleteActiveDma();
        }

        private void CreateActiveDmaRequest(
            AmigaPreparedTrack preparedTrack,
            int word,
            ActiveDmaWordPlan plan)
        {
            _activeDmaRequestTargetAddress = _bus.AddChipDmaPointerOffset(_activeDmaTargetAddress, word * 2);
            _activeDmaRequestSourceBit = plan.SourceBit;
            _activeDmaRequestReadValue = _activeDmaWriteMode
                ? (ushort)0
                : ReadPreparedUInt16(preparedTrack, plan.SourceBit);
            _activeDmaRequestCycle = Math.Max(plan.CompletionCycle, _activeDmaNextBusRequestCycle);
            _activeDmaRequestServiceCycle = GetNextDiskDmaSlotCycle(_activeDmaRequestCycle);
            _activeDmaRequestNextSourceBit = plan.NextSourceBit;
            _activeDmaRequestNextWordStartCycle = plan.NextWordStartCycle;
            _activeDmaRequestPending = true;
            _activeDmaCachedWordPlanValid = false;
            if (_specializationEnabled)
            {
                _activeDmaRequestsCreated++;
            }
            _activeDmaCompletionCycle = Math.Max(
                _activeDmaCompletionCycle,
                GetDiskDmaSlotCompletionCycle(_activeDmaRequestServiceCycle));
        }

        private ActiveDmaWordPlan GetOrCreateActiveDmaCachedWordPlan(
            AmigaPreparedTrack preparedTrack,
            DiskStreamState stream,
            int word)
        {
            var transferPlan = _activeDmaTransferPlan;
            if (transferPlan != null && transferPlan.ContainsWord(word))
            {
                return transferPlan.GetWordPlan(word);
            }

            if (_activeDmaCachedWordPlanValid)
            {
                return _activeDmaCachedWordPlan;
            }

            _activeDmaTransferPlan = BuildActiveDmaTransferPlan(
                preparedTrack,
                _dsksync,
                word,
                _activeDmaNextSourceBit,
                _activeDmaNextWordStartCycle,
                _activeDmaRequestedWords,
                _activeDmaNextBusRequestCycle,
                _activeDmaCyclesPerBit,
                _activeDmaSyncMode,
                stream.SyncCacheOffsets,
                stream.SyncCacheOffsetCount);
            if (_activeDmaTransferPlan.ContainsWord(word))
            {
                return _activeDmaTransferPlan.GetWordPlan(word);
            }

            if (_specializationEnabled)
            {
                _activeDmaRuntimeWordPlans++;
            }
            _activeDmaCachedWordPlan = GetActiveDmaWordPlan(
                preparedTrack,
                _dsksync,
                word,
                _activeDmaNextSourceBit,
                _activeDmaNextWordStartCycle,
                _activeDmaCyclesPerBit,
                _activeDmaSyncMode,
                stream.SyncCacheOffsets,
                stream.SyncCacheOffsetCount);
            _activeDmaCachedWordPlanValid = true;
            return _activeDmaCachedWordPlan;
        }

        private bool TryServeActiveDmaRequestThrough(long targetCycle)
        {
            if (!_activeDmaRequestPending)
            {
                return true;
            }

            while (_activeDmaRequestServiceCycle <= targetCycle)
            {
                if (_bus.TryReserveDiskDmaWordExactSlot(
                    _activeDmaRequestTargetAddress,
                    _activeDmaWriteMode,
                    _activeDmaRequestServiceCycle,
                    out var diskReservation))
                {
                    _activeDmaCompletionCycle = Math.Max(_activeDmaCompletionCycle, diskReservation.CompletedCycle);
                    _activeDmaNextBusRequestCycle = diskReservation.CompletedCycle;
                    _diskDmaWordLatch = new DiskDmaWordLatch(
                        _activeDmaWriteMode,
                        _activeDmaRequestTargetAddress,
                        _activeDmaRequestSourceBit,
                        _activeDmaRequestReadValue,
                        diskReservation);
                    var value = ConsumeDiskDmaWordLatch(ref _diskDmaWordLatch);
                    if (_traceRecorder != null)
                    {
                        AppendDivergenceTrace(
                            AmigaDiskTraceEventKind.DmaWord,
                            diskReservation.GrantedCycle,
                            drive: _activeDmaDrive,
                            requestedWords: _activeDmaRequestedWords,
                            transferredWords: _activeDmaTransferredWords + 1,
                            sourceBit: _activeDmaRequestSourceBit,
                            targetAddress: _activeDmaRequestTargetAddress,
                            completionCycle: _activeDmaCompletionCycle,
                            detail: _activeDmaWriteMode ? $"write=0x{value:X4}" : $"value=0x{value:X4}");
                    }

                    _activeDmaNextSourceBit = _activeDmaRequestNextSourceBit;
                    _activeDmaNextWordStartCycle = _activeDmaRequestNextWordStartCycle;
                    _activeDmaTransferredWords++;
                    UpdateActiveDmaPointer();
                    UpdateActiveDmaLength();
                    ClearActiveDmaRequest();
                    if (_specializationEnabled)
                    {
                        _activeDmaRequestsServed++;
                    }
                    return true;
                }

                _activeDmaRequestServiceCycle = GetNextDiskDmaSlotCycle(
                    _activeDmaRequestServiceCycle + AgnusChipSlotScheduler.SlotCycles);
                if (_specializationEnabled)
                {
                    _activeDmaRequestsBlocked++;
                }
            }

            _activeDmaCompletionCycle = Math.Max(
                _activeDmaCompletionCycle,
                GetDiskDmaSlotCompletionCycle(_activeDmaRequestServiceCycle));
            return false;
        }

        private long GetNextDiskDmaSlotCycle(long cycle)
            => _bus.PredictDiskDmaGrantCycle(cycle);

        private long GetDiskDmaSlotCompletionCycle(long slotCycle)
            => _bus.PredictDiskDmaCompletionCycle(slotCycle);

        private void ClearActiveDmaRequest()
        {
            _activeDmaRequestPending = false;
            _activeDmaRequestTargetAddress = 0;
            _activeDmaRequestSourceBit = 0;
            _activeDmaRequestReadValue = 0;
            _activeDmaRequestCycle = 0;
            _activeDmaRequestServiceCycle = 0;
            _activeDmaRequestNextSourceBit = 0;
            _activeDmaRequestNextWordStartCycle = 0;
        }

        private ushort ConsumeDiskDmaWordLatch(ref DiskDmaWordLatch latch)
        {
            if (!latch.HasValue)
            {
                return 0;
            }

            var reservation = latch.Reservation;
            var value = latch.WriteMode
                ? _bus.CommitDmaWordRead(in reservation)
                : latch.Value;
            _diskDataRegister = value;
            if (latch.WriteMode)
            {
                if (_activeWriteTrackData != null)
                {
                    WriteTrackUInt16(_activeWriteTrackData, _activeDmaTrackBitLength, latch.SourceBit, value);
                    _activeWriteTrackMutated = true;
                }
            }
            else
            {
                _bus.CommitDmaWordWrite(in reservation, value);
            }

            latch = default;
            return value;
        }

        private void CompleteActiveDma()
        {
            _activeDmaTransferredWords = _activeDmaRequestedWords;
            UpdateActiveDmaPointer();
            UpdateActiveDmaLength();
            var elapsedCycles = Math.Max(0, _activeDmaCompletionCycle - _activeDmaStartCycle);
            var position = (_activeDmaStreamStartPosition + (elapsedCycles / _activeDmaCyclesPerBit)) % _activeDmaTrackBitLength;
            SetStreamPosition(GetActiveDmaStream(), position, _activeDmaCompletionCycle);
            CommitActiveWriteTrackIfNeeded();
            AppendDmaTrace(
                AmigaDiskDmaTraceKind.Completed,
                _activeDmaCompletionCycle,
                _activeDmaDrive,
                _activeDmaCylinder,
                _activeDmaHead,
                _activeDmaTargetAddress,
                _activeDmaRequestedWords,
                _activeDmaTransferredWords,
                GetActiveDmaStream().Offset,
                syncWaitBits: 0,
                _activeDmaTrackBitLength,
                _activeDmaSyncMode,
                _activeDmaCompletionCycle);
            AppendDivergenceTrace(
                AmigaDiskTraceEventKind.DiskInterruptWrite,
                _activeDmaCompletionCycle,
                register: 0x09C,
                value: (ushort)(0x8000 | DskBlkInterrupt),
                drive: _activeDmaDrive,
                requestedWords: _activeDmaRequestedWords,
                transferredWords: _activeDmaTransferredWords,
                targetAddress: _activeDmaTargetAddress,
                completionCycle: _activeDmaCompletionCycle,
                detail: "dskblk");
            _bus.RequestHardwareInterrupt(DskBlkInterrupt, _activeDmaCompletionCycle);
            ClearActiveDma();
        }

        private void CancelActiveDma(long cycle)
        {
            if (!_activeDma)
            {
                return;
            }

            if (IsDiskDmaControlEnabled())
            {
                AdvanceActiveDmaTo(cycle);
            }

            if (!_activeDma)
            {
                return;
            }

            var elapsedCycles = Math.Max(0, cycle - _activeDmaStartCycle);
            var position = (_activeDmaStreamStartPosition + (elapsedCycles / _activeDmaCyclesPerBit)) % _activeDmaTrackBitLength;
            SetStreamPosition(GetActiveDmaStream(), position, cycle);
            UpdateActiveDmaLength();
            CommitActiveWriteTrackIfNeeded();
            AppendDmaTrace(
                AmigaDiskDmaTraceKind.Cancelled,
                cycle,
                _activeDmaDrive,
                _activeDmaCylinder,
                _activeDmaHead,
                _activeDmaTargetAddress,
                _activeDmaRequestedWords,
                _activeDmaTransferredWords,
                GetActiveDmaStream().Offset,
                syncWaitBits: 0,
                _activeDmaTrackBitLength,
                _activeDmaSyncMode,
                _activeDmaCompletionCycle);
            ClearActiveDma();
        }

        private void StopActiveDma(long cycle)
        {
            var elapsedCycles = Math.Max(0, cycle - _activeDmaStartCycle);
            var position = (_activeDmaStreamStartPosition + (elapsedCycles / _activeDmaCyclesPerBit)) % _activeDmaTrackBitLength;
            SetStreamPosition(GetActiveDmaStream(), position, cycle);
            UpdateActiveDmaLength();
            CommitActiveWriteTrackIfNeeded();
            AppendDmaTrace(
                AmigaDiskDmaTraceKind.Stopped,
                cycle,
                _activeDmaDrive,
                _activeDmaCylinder,
                _activeDmaHead,
                _activeDmaTargetAddress,
                _activeDmaRequestedWords,
                _activeDmaTransferredWords,
                GetActiveDmaStream().Offset,
                syncWaitBits: 0,
                _activeDmaTrackBitLength,
                _activeDmaSyncMode,
                _activeDmaCompletionCycle);
            ClearActiveDma();
        }

        private void CommitActiveWriteTrackIfNeeded()
        {
            if (!_activeDmaWriteMode || !_activeWriteTrackMutated || _activeWriteTrackData == null)
            {
                return;
            }

            var drive = GetDriveOrNull(_activeDmaDrive);
            if (drive == null || drive.WriteProtected)
            {
                return;
            }

            var writtenTrack = new AmigaEncodedTrack(
                _activeWriteTrackData,
                _activeDmaTrackBitLength,
                _activeDmaTrackStartBit,
                _activeDmaTrackFeatures);
            if (!drive.TryWriteEncodedTrack(_activeDmaCylinder, _activeDmaHead, writtenTrack))
            {
                return;
            }

            var stream = GetActiveDmaStream();
            stream.PreparedTrackValid = false;
            stream.InvalidateSyncCache();
        }

        private void AppendDmaTrace(
            AmigaDiskDmaTraceKind kind,
            long cycle,
            int drive,
            int cylinder,
            int head,
            uint targetAddress,
            int requestedWords,
            int transferredWords,
            int sourceBit,
            int syncWaitBits,
            int trackBitLength,
            AmigaDiskSyncMode syncMode,
            long completionCycle,
            AmigaDiskDmaBlockedReason blockedReason = AmigaDiskDmaBlockedReason.None)
        {
            if (_dmaTrace.Count == MaxDmaTraceEntries)
            {
                _dmaTrace.RemoveAt(0);
            }

            _dmaTrace.Add(new AmigaDiskDmaTraceEntry(
                kind,
                _transferCount,
                cycle,
                drive,
                cylinder,
                head,
                GetSelectedDriveIndex(),
                _dsklen,
                _dsksync,
                _bus.Paula.Adkcon,
                PeekDskbytr(),
                _diskDataRegister,
                targetAddress,
                requestedWords,
                transferredWords,
                sourceBit,
                syncWaitBits,
                trackBitLength,
                syncMode,
                completionCycle,
                blockedReason));
            AppendDivergenceTrace(
                kind switch
                {
                    AmigaDiskDmaTraceKind.Started => AmigaDiskTraceEventKind.DmaStarted,
                    AmigaDiskDmaTraceKind.Completed => AmigaDiskTraceEventKind.DmaCompleted,
                    AmigaDiskDmaTraceKind.Cancelled => AmigaDiskTraceEventKind.DmaCancelled,
                    AmigaDiskDmaTraceKind.Stopped => AmigaDiskTraceEventKind.DmaStopped,
                    AmigaDiskDmaTraceKind.StartBlocked => AmigaDiskTraceEventKind.DmaStartBlocked,
                    AmigaDiskDmaTraceKind.SyncMissing => AmigaDiskTraceEventKind.DmaSyncMissing,
                    _ => AmigaDiskTraceEventKind.Unknown
                },
                cycle,
                drive: drive,
                requestedWords: requestedWords,
                transferredWords: transferredWords,
                sourceBit: sourceBit,
                targetAddress: targetAddress,
                completionCycle: completionCycle,
                detail: GetDmaTraceDetail(syncMode, blockedReason));
        }

        private static string GetDmaTraceDetail(AmigaDiskSyncMode syncMode, AmigaDiskDmaBlockedReason blockedReason)
        {
            if (blockedReason != AmigaDiskDmaBlockedReason.None)
            {
                return $"blocked:{blockedReason}";
            }

            return syncMode switch
            {
                AmigaDiskSyncMode.Word => "wordsync",
                AmigaDiskSyncMode.Byte => "bytesync",
                _ => string.Empty
            };
        }

        private void AppendDivergenceTrace(
            AmigaDiskTraceEventKind kind,
            long cycle,
            ushort register = 0,
            ushort value = 0,
            int drive = -1,
            int requestedWords = 0,
            int transferredWords = 0,
            int sourceBit = 0,
            uint targetAddress = 0,
            long targetCycle = 0,
            long candidateCycle = 0,
            long completionCycle = 0,
            string detail = "")
        {
            if (_traceRecorder == null)
            {
                return;
            }

            var traceDrive = ResolveTraceDrive(drive);
            var stream = GetTraceStream(traceDrive);
            var driveState = GetDriveOrNull(traceDrive);
            var builder = new AmigaDiskTraceEventBuilder
            {
                Kind = kind,
                Cycle = cycle,
                Register = register,
                Value = value,
                Dmacon = _bus.Paula.Dmacon,
                Adkcon = _bus.Paula.Adkcon,
                Intena = _bus.Paula.Intena,
                Intreq = _bus.Paula.Intreq,
                Dsklen = _dsklen,
                Dsksync = _dsksync,
                Dskbytr = PeekDskbytr(),
                Dskdatr = _diskDataRegister,
                DiskPointer = _diskPointer,
                SelectedDrive = GetSelectedDriveIndex(),
                ActiveDmaDrive = _activeDmaDrive,
                ActiveDma = _activeDma,
                Cylinder = driveState?.Cylinder ?? 0,
                Head = driveState?.Head ?? 0,
                PendingReadDmaWords = _pendingReadDmaWords,
                RequestedWords = requestedWords != 0 ? requestedWords : _activeDmaRequestedWords,
                TransferredWords = transferredWords != 0 ? transferredWords : _activeDmaTransferredWords,
                SourceBit = sourceBit,
                StreamOffset = stream?.Offset ?? 0,
                StreamPosition = stream?.Position ?? 0,
                StreamCycle = stream?.Cycle ?? 0,
                TargetAddress = targetAddress != 0 ? targetAddress : _activeDmaTargetAddress,
                TargetCycle = targetCycle,
                CandidateCycle = candidateCycle,
                CompletionCycle = completionCycle != 0 ? completionCycle : _activeDmaCompletionCycle,
                Detail = detail
            };
            _traceRecorder.Add(builder);
        }

        private int ResolveTraceDrive(int drive)
        {
            if ((uint)drive < (uint)_streams.Length)
            {
                return drive;
            }

            var selected = GetSelectedDriveIndex();
            if ((uint)selected < (uint)_streams.Length)
            {
                return selected;
            }

            if ((uint)_activeDmaDrive < (uint)_streams.Length)
            {
                return _activeDmaDrive;
            }

            return 0;
        }

        private DiskStreamState? GetTraceStream(int drive)
            => (uint)drive < (uint)_streams.Length ? _streams[drive] : null;

        private void ClearActiveDma()
        {
            _activeDma = false;
            _activeDmaWriteMode = false;
            _activeDmaDrive = -1;
            _activeDmaTargetAddress = 0;
            _activeDmaNextSourceBit = 0;
            _activeDmaNextWordStartCycle = 0;
            _activeDmaSyncMode = AmigaDiskSyncMode.None;
            _activeDmaRequestedWords = 0;
            _activeDmaTransferredWords = 0;
            _activeDmaCylinder = 0;
            _activeDmaHead = 0;
            _activeDmaTrackBitLength = 0;
            _activeDmaTrackStartBit = 0;
            _activeDmaTrackFeatures = AmigaTrackFeatures.None;
            _activeDmaStreamStartPosition = 0;
            _activeDmaStartCycle = 0;
            _activeDmaCompletionCycle = 0;
            _activeDmaNextBusRequestCycle = 0;
            _activeDmaCyclesPerBit = 0;
            _diskDmaWordLatch = default;
            _activeDmaCachedWordPlan = default;
            _activeDmaCachedWordPlanValid = false;
            _activeDmaTransferPlan = null;
            ClearActiveDmaRequest();
            _activeWriteTrackData = null;
            _activeWriteTrackMutated = false;
        }

        private void UpdateActiveDmaPointer()
        {
            _diskPointer = _bus.AddChipDmaPointerOffset(_activeDmaTargetAddress, _activeDmaTransferredWords * 2);
        }

        private void UpdateActiveDmaLength()
        {
            var remaining = Math.Max(0, _activeDmaRequestedWords - _activeDmaTransferredWords);
            _dsklen = (ushort)((_dsklen & (DskDmaEnable | DskWriteMode)) | (remaining & DskLengthMask));
        }

        private void ResetStreamIfTrackChanged(AmigaFloppyDrive drive, DiskStreamState stream, long cycle)
        {
            if (stream.Cylinder == drive.Cylinder && stream.Head == drive.Head)
            {
                return;
            }

            stream.Cylinder = drive.Cylinder;
            stream.Head = drive.Head;
            stream.InvalidateSyncCache();
            SetStreamPosition(stream, 0, cycle);
        }

        private bool IsDiskDmaControlEnabled()
        {
            return (_dsklen & DskDmaEnable) != 0 &&
                (_bus.Paula.Dmacon & (DmaconMasterEnable | DmaconDiskEnable)) == (DmaconMasterEnable | DmaconDiskEnable);
        }

        private bool IsDiskDmaActuallyOn()
        {
            return _activeDma && IsDiskDmaControlEnabled();
        }

        private AmigaDiskSyncMode GetDmaSyncMode()
        {
            if ((_bus.Paula.Adkcon & AdkconMsbSync) != 0)
            {
                return AmigaDiskSyncMode.Byte;
            }

            return (_bus.Paula.Adkcon & AdkconWordSync) != 0
                ? AmigaDiskSyncMode.Word
                : AmigaDiskSyncMode.None;
        }

        private AmigaDiskSyncMode GetInputSyncMode()
        {
            return (_bus.Paula.Adkcon & AdkconMsbSync) != 0
                ? AmigaDiskSyncMode.Byte
                : AmigaDiskSyncMode.Word;
        }

        private static int GetSyncBitCount(AmigaDiskSyncMode syncMode)
        {
            return syncMode == AmigaDiskSyncMode.Byte ? 8 : 16;
        }

        private ushort GetSyncCacheKey(AmigaDiskSyncMode syncMode)
        {
            return syncMode == AmigaDiskSyncMode.Byte
                ? (ushort)(_dsksync >> 8)
                : _dsksync;
        }

        private double GetDiskDmaCyclesPerBit(int trackBitLength)
        {
            if (trackBitLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(trackBitLength), trackBitLength, "Encoded track bit length must be positive.");
            }

            if ((_bus.Paula.Adkcon & AdkconFast) != 0)
            {
                return FastDiskCyclesPerBit;
            }

            return AmigaConstants.A500PalCpuClockHz / (trackBitLength * DiskRevolutionsPerSecond);
        }

        private static double GetDiskRotationCyclesPerBit(int trackBitLength)
        {
            if (trackBitLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(trackBitLength), trackBitLength, "Encoded track bit length must be positive.");
            }

            return AmigaConstants.A500PalCpuClockHz / (trackBitLength * DiskRevolutionsPerSecond);
        }

        private void AdvanceStreamTo(DiskStreamState stream, int trackBitLength, double cyclesPerBit, long cycle)
        {
            if (cycle <= stream.Cycle)
            {
                return;
            }

            var elapsedCycles = cycle - stream.Cycle;
            var position = (stream.Position + (elapsedCycles / cyclesPerBit)) % trackBitLength;
            SetStreamPosition(stream, position, cycle);
        }

        private void AdvanceDiskInputTo(long cycle)
        {
            if (UseLegacyDiskInputPublisher)
            {
                AdvanceDiskInputToLegacy(cycle);
                return;
            }

            PublishDiskInputTo(cycle);
        }

        private void AdvanceDiskInputToLegacy(long cycle)
        {
            _schedulerInputAdvanceCalls++;
            if (_traceRecorder != null)
            {
                var traceDrive = ResolveTraceDrive(-1);
                var traceStream = GetTraceStream(traceDrive);
                AppendDivergenceTrace(
                    AmigaDiskTraceEventKind.DiskInputAdvance,
                    traceStream?.Cycle ?? _currentCycle,
                    drive: traceDrive,
                    targetCycle: cycle,
                    detail: "start");
            }

            var nextInputAdvanceCycle = long.MaxValue;
            var nextSyncAdvanceCycle = long.MaxValue;
            for (var driveIndex = 0; driveIndex < _drives.Length; driveIndex++)
            {
                var drive = _drives[driveIndex];
                var stream = _streams[driveIndex];
                if (!IsDriveConnected(driveIndex))
                {
                    stream.Cycle = cycle;
                    continue;
                }

                if (cycle <= stream.Cycle)
                {
                    continue;
                }

                if (drive.Disk == null || !drive.MotorOn)
                {
                    stream.Cycle = cycle;
                    continue;
                }

                var readyCycle = GetDriveReadyCycle(drive);
                if (cycle < readyCycle)
                {
                    stream.Cycle = cycle;
                    continue;
                }

                if (stream.Cycle < readyCycle)
                {
                    SetStreamPosition(stream, stream.Position, readyCycle);
                }

                var track = drive.ReadEncodedTrack(drive.Cylinder, drive.Head);
                ResetStreamIfTrackChanged(drive, stream, stream.Cycle);
                var preparedTrack = GetPreparedTrack(drive, stream, track);
                var cyclesPerBit = GetDiskDmaCyclesPerBit(track.BitLength);
                AdvanceInputStreamTo(drive, stream, preparedTrack, cyclesPerBit, cycle, drive.Selected);
                if (drive.Selected)
                {
                    nextSyncAdvanceCycle = Math.Min(
                        nextSyncAdvanceCycle,
                        GetNextSyncCompletionCycle(drive, stream, preparedTrack, cyclesPerBit));
                    nextInputAdvanceCycle = Math.Min(
                        nextInputAdvanceCycle,
                        GetNextSelectedInputAdvanceCycle(drive, stream, preparedTrack, cyclesPerBit));
                }
            }

            _nextDiskInputAdvanceCycle = nextInputAdvanceCycle == long.MaxValue ? 0 : nextInputAdvanceCycle;
            _nextDiskSyncAdvanceCycle = nextSyncAdvanceCycle == long.MaxValue ? -1 : nextSyncAdvanceCycle;
            if (_traceRecorder != null)
            {
                var traceDrive = ResolveTraceDrive(-1);
                AppendDivergenceTrace(
                    AmigaDiskTraceEventKind.DiskInputAdvance,
                    cycle,
                    drive: traceDrive,
                    targetCycle: cycle,
                    candidateCycle: _nextDiskInputAdvanceCycle,
                    detail: "end");
            }
        }

        private void PublishDiskInputTo(long cycle)
        {
            _schedulerInputAdvanceCalls++;
            if (_traceRecorder != null)
            {
                var traceDrive = ResolveTraceDrive(-1);
                var traceStream = GetTraceStream(traceDrive);
                AppendDivergenceTrace(
                    AmigaDiskTraceEventKind.DiskInputAdvance,
                    traceStream?.Cycle ?? _currentCycle,
                    drive: traceDrive,
                    targetCycle: cycle,
                    detail: "publish-start");
            }

            var nextInputAdvanceCycle = long.MaxValue;
            var nextSyncAdvanceCycle = long.MaxValue;
            for (var driveIndex = 0; driveIndex < _drives.Length; driveIndex++)
            {
                var drive = _drives[driveIndex];
                var stream = _streams[driveIndex];
                if (!IsDriveConnected(driveIndex))
                {
                    stream.Cycle = cycle;
                    continue;
                }

                if (cycle <= stream.Cycle)
                {
                    if (drive.Selected)
                    {
                        RefreshPublishedInputWakeCandidate(driveIndex, cycle, ref nextInputAdvanceCycle, ref nextSyncAdvanceCycle);
                    }

                    continue;
                }

                if (drive.Disk == null || !drive.MotorOn)
                {
                    stream.Cycle = cycle;
                    ClearDiskInputPlan(driveIndex);
                    continue;
                }

                var readyCycle = GetDriveReadyCycle(drive);
                if (cycle < readyCycle)
                {
                    stream.Cycle = cycle;
                    ClearDiskInputPlan(driveIndex);
                    continue;
                }

                if (stream.Cycle < readyCycle)
                {
                    SetStreamPosition(stream, stream.Position, readyCycle);
                    ClearDiskInputPlan(driveIndex);
                }

                if (!drive.Selected)
                {
                    AdvanceDriveControlStreamTo(driveIndex, cycle);
                    ClearDiskInputPlan(driveIndex);
                    continue;
                }

                PublishSelectedDiskInputTo(driveIndex, cycle);
                RefreshPublishedInputWakeCandidate(driveIndex, cycle, ref nextInputAdvanceCycle, ref nextSyncAdvanceCycle);
            }

            _nextDiskInputAdvanceCycle = nextInputAdvanceCycle == long.MaxValue ? 0 : nextInputAdvanceCycle;
            _nextDiskSyncAdvanceCycle = nextSyncAdvanceCycle == long.MaxValue ? -1 : nextSyncAdvanceCycle;
            if (_traceRecorder != null)
            {
                var traceDrive = ResolveTraceDrive(-1);
                AppendDivergenceTrace(
                    AmigaDiskTraceEventKind.DiskInputAdvance,
                    cycle,
                    drive: traceDrive,
                    targetCycle: cycle,
                    candidateCycle: _nextDiskInputAdvanceCycle,
                    detail: "publish-end");
            }
        }

        private void PublishSelectedDiskInputTo(int driveIndex, long cycle)
        {
            while (cycle > _streams[driveIndex].Cycle)
            {
                var plan = GetOrCreateDiskInputPlan(driveIndex);
                if (plan == null)
                {
                    return;
                }

                var drive = _drives[driveIndex];
                var stream = _streams[driveIndex];
                var track = drive.ReadEncodedTrack(drive.Cylinder, drive.Head);
                var preparedTrack = GetPreparedTrack(drive, stream, track);
                var publishThrough = Math.Min(cycle, plan.EndCycle);
                var eventIndex = _diskInputPlanEventIndexes[driveIndex];
                while (eventIndex < plan.Events.Length && plan.Events[eventIndex].Cycle <= publishThrough)
                {
                    PublishDiskInputEvent(plan.Events[eventIndex], preparedTrack);
                    eventIndex++;
                }

                _diskInputPlanEventIndexes[driveIndex] = eventIndex;
                SetStreamPosition(
                    _streams[driveIndex],
                    GetPlanStreamPositionAt(plan, publishThrough),
                    publishThrough,
                    invalidateInputPlan: false);

                if (publishThrough < plan.EndCycle)
                {
                    break;
                }

                ClearDiskInputPlan(driveIndex);
            }
        }

        private void PublishDiskInputEvent(DiskTrackEvent diskEvent, AmigaPreparedTrack preparedTrack)
        {
            switch (diskEvent.Kind)
            {
                case DiskTrackEventKind.ByteReady:
                    _dskbytrData = ReadPreparedByte(preparedTrack, diskEvent.ByteStartBit);
                    if (diskEvent.HasWordValue)
                    {
                        _diskDataRegister = ReadPreparedUInt16(preparedTrack, diskEvent.WordStartBit);
                    }

                    _dskbytrByteReady = true;
                    break;
                case DiskTrackEventKind.SyncMatch:
                    _dskbytrWordEqualUntilCycle = Math.Max(
                        _dskbytrWordEqualUntilCycle,
                        diskEvent.Cycle + DiskWordEqualHoldCycles);
                    AppendDivergenceTrace(
                        AmigaDiskTraceEventKind.DiskInterruptWrite,
                        diskEvent.Cycle,
                        register: 0x09C,
                        value: (ushort)(0x8000 | DskSynInterrupt),
                        detail: "dsksyn");
                    _bus.RequestHardwareInterrupt(DskSynInterrupt, diskEvent.Cycle);
                    break;
            }
        }

        private void RefreshPublishedInputWakeCandidate(
            int driveIndex,
            long cycle,
            ref long nextInputAdvanceCycle,
            ref long nextSyncAdvanceCycle)
        {
            var plan = GetOrCreateDiskInputPlan(driveIndex);
            if (plan != null)
            {
                for (var index = _diskInputPlanEventIndexes[driveIndex]; index < plan.Events.Length; index++)
                {
                    var diskEvent = plan.Events[index];
                    if (diskEvent.Cycle <= cycle)
                    {
                        continue;
                    }

                    nextInputAdvanceCycle = Math.Min(nextInputAdvanceCycle, diskEvent.Cycle);
                    if (diskEvent.Kind == DiskTrackEventKind.SyncMatch)
                    {
                        nextSyncAdvanceCycle = Math.Min(nextSyncAdvanceCycle, diskEvent.Cycle);
                    }

                    if (diskEvent.Kind == DiskTrackEventKind.ByteReady)
                    {
                        break;
                    }
                }
            }

            var wordEqualExpireCycle = _dskbytrWordEqualUntilCycle >= _streams[driveIndex].Cycle
                ? _dskbytrWordEqualUntilCycle + 1
                : long.MaxValue;
            nextInputAdvanceCycle = Math.Min(nextInputAdvanceCycle, wordEqualExpireCycle);
        }

        private DiskTrackEventPlan? GetOrCreateDiskInputPlan(int driveIndex)
        {
            var drive = _drives[driveIndex];
            var stream = _streams[driveIndex];
            if (!IsDriveConnected(driveIndex) || drive.Disk == null || !drive.MotorOn)
            {
                ClearDiskInputPlan(driveIndex);
                return null;
            }

            var track = drive.ReadEncodedTrack(drive.Cylinder, drive.Head);
            ResetStreamIfTrackChanged(drive, stream, stream.Cycle);
            var preparedTrack = GetPreparedTrack(drive, stream, track);
            var syncMode = GetInputSyncMode();
            var cyclesPerBit = GetDiskDmaCyclesPerBit(track.BitLength);
            var plan = _diskInputPlans[driveIndex];
            if (plan != null &&
                plan.Matches(driveIndex, drive, track, syncMode, _dsksync, _bus.Paula.Adkcon, cyclesPerBit) &&
                stream.Cycle >= plan.StartCycle &&
                stream.Cycle <= plan.EndCycle)
            {
                return plan;
            }

            plan = BuildDiskInputPlan(driveIndex, drive, stream, preparedTrack, track, syncMode, cyclesPerBit);
            _diskInputPlans[driveIndex] = plan;
            _diskInputPlanEventIndexes[driveIndex] = 0;
            return plan;
        }

        private DiskTrackEventPlan BuildDiskInputPlan(
            int driveIndex,
            AmigaFloppyDrive drive,
            DiskStreamState stream,
            AmigaPreparedTrack preparedTrack,
            AmigaEncodedTrack track,
            AmigaDiskSyncMode syncMode,
            double cyclesPerBit)
        {
            var events = new List<DiskTrackEvent>(Math.Max(16, track.BitLength / 8));
            var startCycle = stream.Cycle;
            var startPosition = stream.Position;
            var endPositionAbsolute = startPosition + track.BitLength;

            AppendByteReadyEvents(preparedTrack, startCycle, startPosition, endPositionAbsolute, cyclesPerBit, events);
            AppendSyncMatchEvents(drive, stream, preparedTrack, startCycle, startPosition, endPositionAbsolute, cyclesPerBit, syncMode, events);
            events.Sort(static (left, right) =>
            {
                var cycleOrder = left.Cycle.CompareTo(right.Cycle);
                return cycleOrder != 0 ? cycleOrder : left.Kind.CompareTo(right.Kind);
            });

            return new DiskTrackEventPlan(
                driveIndex,
                drive.Cylinder,
                drive.Head,
                track.BitLength,
                track.StartBit,
                track.Features,
                _dsksync,
                (ushort)(_bus.Paula.Adkcon & (AdkconFast | AdkconMsbSync)),
                syncMode,
                cyclesPerBit,
                startCycle,
                startPosition,
                startCycle + CyclesForBits(track.BitLength, cyclesPerBit),
                events.ToArray());
        }

        private void AppendByteReadyEvents(
            AmigaPreparedTrack track,
            long startCycle,
            double startPosition,
            double endPositionAbsolute,
            double cyclesPerBit,
            List<DiskTrackEvent> events)
        {
            var completedBefore = (long)Math.Floor(startPosition / 8.0);
            var completedAfter = (long)Math.Floor(endPositionAbsolute / 8.0);
            for (var completed = completedBefore + 1; completed <= completedAfter; completed++)
            {
                var eventCycle = startCycle + CyclesForBits((completed * 8.0) - startPosition, cyclesPerBit);
                var byteStartBit = Mod((int)((completed - 1) * 8), track.BitLength);
                var hasWord = completed >= 2;
                var wordStartBit = Mod((int)((completed - 2) * 8), track.BitLength);
                events.Add(DiskTrackEvent.ByteReady(
                    eventCycle,
                    byteStartBit,
                    hasWord,
                    hasWord ? wordStartBit : 0));
            }
        }

        private void AppendSyncMatchEvents(
            AmigaFloppyDrive drive,
            DiskStreamState stream,
            AmigaPreparedTrack track,
            long startCycle,
            double startPosition,
            double endPositionAbsolute,
            double cyclesPerBit,
            AmigaDiskSyncMode syncMode,
            List<DiskTrackEvent> events)
        {
            var length = track.BitLength;
            var syncBitCount = GetSyncBitCount(syncMode);
            if (length < syncBitCount)
            {
                return;
            }

            var syncOffsetCount = GetSyncOffsets(drive, stream, track, length, syncMode);
            if (syncOffsetCount <= 0)
            {
                return;
            }

            var startOffset = (int)Math.Floor(startPosition - syncBitCount) + 1;
            var syncOffsetIndex = startOffset <= 0
                ? 0
                : LowerBound(stream.SyncCacheOffsets, syncOffsetCount, startOffset);
            var rotationBase = 0.0;
            if (syncOffsetIndex >= syncOffsetCount)
            {
                syncOffsetIndex = 0;
                rotationBase = length;
            }

            while (true)
            {
                var completionPosition = rotationBase + stream.SyncCacheOffsets[syncOffsetIndex] + syncBitCount;
                if (completionPosition <= startPosition)
                {
                    AdvanceSyncCompletionCursor(syncOffsetCount, length, ref syncOffsetIndex, ref rotationBase);
                    continue;
                }

                if (completionPosition > endPositionAbsolute)
                {
                    break;
                }

                events.Add(DiskTrackEvent.SyncMatch(
                    startCycle + CyclesForBits(completionPosition - startPosition, cyclesPerBit)));
                AdvanceSyncCompletionCursor(syncOffsetCount, length, ref syncOffsetIndex, ref rotationBase);
            }
        }

        private static double GetPlanStreamPositionAt(DiskTrackEventPlan plan, long cycle)
        {
            var elapsedCycles = Math.Max(0, cycle - plan.StartCycle);
            var elapsedBits = elapsedCycles / plan.CyclesPerBit;
            return (plan.StartPosition + elapsedBits) % plan.TrackBitLength;
        }

        private void AdvanceInputStreamTo(
            AmigaFloppyDrive drive,
            DiskStreamState stream,
            AmigaPreparedTrack track,
            double cyclesPerBit,
            long cycle,
            bool selected)
        {
            if (cycle <= stream.Cycle)
            {
                return;
            }

            var startCycle = stream.Cycle;
            var startPosition = stream.Position;
            var elapsedBits = (cycle - startCycle) / cyclesPerBit;
            var endPositionAbsolute = startPosition + elapsedBits;
            if (selected)
            {
                UpdateDskbytrData(track, startPosition, endPositionAbsolute);
                SignalSyncMatches(drive, stream, track, startCycle, startPosition, endPositionAbsolute, cyclesPerBit);
            }

            SetStreamPosition(stream, endPositionAbsolute % track.BitLength, cycle);
        }

        private long GetNextSelectedInputAdvanceCycle(
            AmigaFloppyDrive drive,
            DiskStreamState stream,
            AmigaPreparedTrack track,
            double cyclesPerBit)
        {
            var nextBytePosition = (Math.Floor(stream.Position / 8.0) + 1.0) * 8.0;
            var nextByteCycle = stream.Cycle + CyclesForBits(nextBytePosition - stream.Position, cyclesPerBit);
            var nextSyncCycle = GetNextSyncCompletionCycle(drive, stream, track, cyclesPerBit);
            var wordEqualExpireCycle = _dskbytrWordEqualUntilCycle >= stream.Cycle
                ? _dskbytrWordEqualUntilCycle + 1
                : long.MaxValue;
            return Math.Min(Math.Min(nextByteCycle, nextSyncCycle), wordEqualExpireCycle);
        }

        private long GetNextSelectedSyncCompletionCycle(long targetCycle)
        {
            var driveIndex = GetSelectedDriveIndex();
            if (driveIndex < 0 || !IsDriveConnected(driveIndex))
            {
                return long.MaxValue;
            }

            var drive = _drives[driveIndex];
            if (drive.Disk == null || !drive.MotorOn)
            {
                return long.MaxValue;
            }

            var readyCycle = GetDriveReadyCycle(drive);
            if (targetCycle < readyCycle)
            {
                return long.MaxValue;
            }

            var stream = _streams[driveIndex];
            var track = drive.ReadEncodedTrack(drive.Cylinder, drive.Head);
            ResetStreamIfTrackChanged(drive, stream, stream.Cycle);
            var preparedTrack = GetPreparedTrack(drive, stream, track);
            var cyclesPerBit = GetDiskDmaCyclesPerBit(track.BitLength);
            return GetNextSyncCompletionCycle(drive, stream, preparedTrack, cyclesPerBit);
        }

        private long GetNextSelectedSyncCompletionCycleCached(long targetCycle)
        {
            if (_nextDiskSyncAdvanceCycle > 0)
            {
                return _nextDiskSyncAdvanceCycle;
            }

            if (_nextDiskSyncAdvanceCycle < 0)
            {
                return long.MaxValue;
            }

            if (!HasUnknownSelectedDiskInput(targetCycle))
            {
                return long.MaxValue;
            }

            var nextSyncCycle = GetNextSelectedSyncCompletionCycle(targetCycle);
            _nextDiskSyncAdvanceCycle = nextSyncCycle == long.MaxValue ? -1 : nextSyncCycle;
            return nextSyncCycle;
        }

        private long GetNextSyncCompletionCycle(
            AmigaFloppyDrive drive,
            DiskStreamState stream,
            AmigaPreparedTrack track,
            double cyclesPerBit)
        {
            var length = track.BitLength;
            var syncMode = GetInputSyncMode();
            var syncBitCount = GetSyncBitCount(syncMode);
            if (length < syncBitCount)
            {
                return long.MaxValue;
            }

            var syncOffsetCount = GetSyncOffsets(drive, stream, track, length, syncMode);
            if (syncOffsetCount <= 0)
            {
                return long.MaxValue;
            }

            var startOffset = (int)Math.Floor(stream.Position - syncBitCount) + 1;
            var syncOffsetIndex = startOffset <= 0
                ? 0
                : LowerBound(stream.SyncCacheOffsets, syncOffsetCount, startOffset);
            var rotationBase = 0.0;
            if (syncOffsetIndex >= syncOffsetCount)
            {
                syncOffsetIndex = 0;
                rotationBase = length;
            }

            while (true)
            {
                var completionPosition = rotationBase + stream.SyncCacheOffsets[syncOffsetIndex] + syncBitCount;
                if (completionPosition > stream.Position)
                {
                    return stream.Cycle + CyclesForBits(completionPosition - stream.Position, cyclesPerBit);
                }

                AdvanceSyncCompletionCursor(syncOffsetCount, length, ref syncOffsetIndex, ref rotationBase);
            }
        }

        private void UpdateDskbytrData(AmigaPreparedTrack track, double startPosition, double endPositionAbsolute)
        {
            var completedBefore = (long)Math.Floor(startPosition / 8.0);
            var completedAfter = (long)Math.Floor(endPositionAbsolute / 8.0);
            if (completedAfter <= completedBefore)
            {
                return;
            }

            var byteStartBit = Mod((int)((completedAfter - 1) * 8), track.BitLength);
            _dskbytrData = ReadPreparedByte(track, byteStartBit);
            if (completedAfter >= 2)
            {
                var wordStartBit = Mod((int)((completedAfter - 2) * 8), track.BitLength);
                _diskDataRegister = ReadPreparedUInt16(track, wordStartBit);
            }

            _dskbytrByteReady = true;
        }

        private void SignalSyncMatches(
            AmigaFloppyDrive drive,
            DiskStreamState stream,
            AmigaPreparedTrack track,
            long startCycle,
            double startPosition,
            double endPositionAbsolute,
            double cyclesPerBit)
        {
            var length = track.BitLength;
            var syncMode = GetInputSyncMode();
            var syncBitCount = GetSyncBitCount(syncMode);
            if (length < syncBitCount)
            {
                return;
            }

            var syncOffsetCount = GetSyncOffsets(drive, stream, track, length, syncMode);
            if (syncOffsetCount <= 0)
            {
                return;
            }

            var startOffset = (int)Math.Floor(startPosition - syncBitCount) + 1;
            var syncOffsetIndex = startOffset <= 0
                ? 0
                : LowerBound(stream.SyncCacheOffsets, syncOffsetCount, startOffset);
            var rotationBase = 0.0;
            if (syncOffsetIndex >= syncOffsetCount)
            {
                syncOffsetIndex = 0;
                rotationBase = length;
            }

            while (true)
            {
                var completionPosition = rotationBase + stream.SyncCacheOffsets[syncOffsetIndex] + syncBitCount;
                if (completionPosition <= startPosition)
                {
                    AdvanceSyncCompletionCursor(syncOffsetCount, length, ref syncOffsetIndex, ref rotationBase);
                    continue;
                }

                if (completionPosition > endPositionAbsolute)
                {
                    break;
                }

                var matchCycle = startCycle + CyclesForBits(completionPosition - startPosition, cyclesPerBit);
                _dskbytrWordEqualUntilCycle = Math.Max(_dskbytrWordEqualUntilCycle, matchCycle + DiskWordEqualHoldCycles);
                AppendDivergenceTrace(
                    AmigaDiskTraceEventKind.DiskInterruptWrite,
                    matchCycle,
                    register: 0x09C,
                    value: (ushort)(0x8000 | DskSynInterrupt),
                    detail: "dsksyn");
                _bus.RequestHardwareInterrupt(DskSynInterrupt, matchCycle);
                AdvanceSyncCompletionCursor(syncOffsetCount, length, ref syncOffsetIndex, ref rotationBase);
            }
        }

        private static void AdvanceSyncCompletionCursor(
            int syncOffsetCount,
            int trackLength,
            ref int syncOffsetIndex,
            ref double rotationBase)
        {
            syncOffsetIndex++;
            if (syncOffsetIndex < syncOffsetCount)
            {
                return;
            }

            syncOffsetIndex = 0;
            rotationBase += trackLength;
        }

        private AmigaPreparedTrack GetPreparedTrack(AmigaFloppyDrive drive, DiskStreamState stream, AmigaEncodedTrack track)
            => GetPreparedTrack(drive, stream, track, drive.Cylinder, drive.Head);

        private AmigaPreparedTrack GetPreparedTrack(
            AmigaFloppyDrive drive,
            DiskStreamState stream,
            AmigaEncodedTrack track,
            int cylinder,
            int head)
        {
            if (stream.PreparedTrackValid &&
                stream.PreparedTrackCylinder == cylinder &&
                stream.PreparedTrackHead == head &&
                stream.PreparedTrack.Matches(track))
            {
                if (_specializationEnabled)
                {
                    _preparedTrackHits++;
                }

                return stream.PreparedTrack;
            }

            if (_specializationEnabled)
            {
                _preparedTrackMisses++;
            }

            stream.PreparedTrack.Prepare(track);
            stream.PreparedTrackValid = true;
            stream.PreparedTrackCylinder = cylinder;
            stream.PreparedTrackHead = head;
            return stream.PreparedTrack;
        }

        private ushort ReadPreparedUInt16(AmigaPreparedTrack track, int bitOffset)
        {
            if (_specializationEnabled && track.UsesScalarReads)
            {
                _scalarBitReads++;
            }

            if (_specializationEnabled && track.HasRollingWords)
            {
                _rollingWindowHits++;
            }
            else if (_specializationEnabled)
            {
                _rollingWindowMisses++;
            }

            return track.ReadUInt16(bitOffset);
        }

        private byte ReadPreparedByte(AmigaPreparedTrack track, int bitOffset)
        {
            if (_specializationEnabled && track.UsesScalarReads)
            {
                _scalarBitReads++;
            }

            if (_specializationEnabled && track.HasRollingWords)
            {
                _rollingWindowHits++;
            }
            else if (_specializationEnabled)
            {
                _rollingWindowMisses++;
            }

            return track.ReadByte(bitOffset);
        }

        private int GetSyncOffsets(
            AmigaFloppyDrive drive,
            DiskStreamState stream,
            AmigaPreparedTrack track,
            int length,
            AmigaDiskSyncMode syncMode)
        {
            var cacheKey = GetSyncCacheKey(syncMode);
            if (stream.SyncCacheMode == syncMode &&
                stream.SyncCacheWord == cacheKey &&
                stream.SyncCacheCylinder == drive.Cylinder &&
                stream.SyncCacheHead == drive.Head &&
                stream.SyncCacheTrackLength == length)
            {
                if (_specializationEnabled)
                {
                    _syncIndexHits++;
                }

                return stream.SyncCacheOffsetCount;
            }

            bool preparedCacheHit;
            if (_specializationEnabled)
            {
                _fullTrackSyncScans++;
                _fullTrackSyncScanBits += length;
                if (syncMode == AmigaDiskSyncMode.Byte)
                {
                    _fullTrackByteSyncScans++;
                }
                else
                {
                    _fullTrackWordSyncScans++;
                }
            }

            var offsetCount = syncMode == AmigaDiskSyncMode.Byte
                ? track.CopyByteSyncOffsets((byte)cacheKey, stream.SyncCacheOffsets, out preparedCacheHit)
                : track.CopySyncOffsets(_dsksync, stream.SyncCacheOffsets, out preparedCacheHit);
            if (_specializationEnabled && preparedCacheHit)
            {
                _syncIndexHits++;
            }
            else if (_specializationEnabled)
            {
                _syncIndexMisses++;
            }

            stream.SyncCacheMode = syncMode;
            stream.SyncCacheWord = cacheKey;
            stream.SyncCacheCylinder = drive.Cylinder;
            stream.SyncCacheHead = drive.Head;
            stream.SyncCacheTrackLength = length;
            stream.SyncCacheOffsetCount = offsetCount;
            return stream.SyncCacheOffsetCount;
        }

        private static double GetFirstCompletionAfter(double completionPosition, int trackLength, double startPosition)
        {
            if (completionPosition <= startPosition)
            {
                var rotations = Math.Floor((startPosition - completionPosition) / trackLength) + 1;
                completionPosition += rotations * trackLength;
            }

            return completionPosition;
        }

        private static long CyclesForBits(double bitCount, double cyclesPerBit)
        {
            return Math.Max(0, (long)Math.Ceiling(bitCount * cyclesPerBit));
        }

        private static int GetRecoveredWordStartBit(double position, int trackBitLength)
        {
            if (trackBitLength <= 0)
            {
                return 0;
            }

            var bit = Math.Max(0, (int)Math.Floor(position));
            return Mod(bit - (bit % 16), trackBitLength);
        }

        private static double GetForwardBitDistance(int sourceBit, double position, int trackBitLength)
        {
            if (trackBitLength <= 0)
            {
                return 0;
            }

            position = CanonicalizeStreamPosition(position);
            if (position < sourceBit)
            {
                position += trackBitLength;
            }

            return Math.Max(0, position - sourceBit);
        }

        private long PredictReadDmaBusCompletionCycle(
            AmigaPreparedTrack track,
            ushort sync,
            int sourceStartBit,
            long dataStartCycle,
            ushort requestedWords,
            double cyclesPerBit,
            AmigaDiskSyncMode syncMode,
            ReadOnlySpan<int> syncOffsets,
            int syncOffsetCount)
        {
            var sourceBit = sourceStartBit;
            var wordStartCycle = dataStartCycle;
            var completionCycle = dataStartCycle;
            var nextBusRequestCycle = dataStartCycle;
            if (_specializationEnabled)
            {
                _activeDmaPredictionPasses++;
            }

            for (var word = 0; word < requestedWords; word++)
            {
                if (_specializationEnabled)
                {
                    _activeDmaPredictedWordPlans++;
                }
                var plan = GetActiveDmaWordPlan(
                    track,
                    sync,
                    word,
                    sourceBit,
                    wordStartCycle,
                    cyclesPerBit,
                    syncMode,
                    syncOffsets,
                    syncOffsetCount);
                sourceBit = plan.NextSourceBit;
                wordStartCycle = plan.NextWordStartCycle;
                var busRequestCycle = Math.Max(plan.CompletionCycle, nextBusRequestCycle);
                completionCycle = _bus.PredictDiskDmaCompletionCycle(busRequestCycle);
                nextBusRequestCycle = completionCycle;
            }

            return completionCycle;
        }

        private ActiveDmaTransferPlan BuildActiveDmaTransferPlan(
            AmigaPreparedTrack track,
            ushort sync,
            int firstWord,
            int sourceStartBit,
            long dataStartCycle,
            ushort requestedWords,
            long nextBusRequestCycle,
            double cyclesPerBit,
            AmigaDiskSyncMode syncMode,
            ReadOnlySpan<int> syncOffsets,
            int syncOffsetCount)
        {
            var remainingWords = Math.Max(0, requestedWords - firstWord);
            var wordPlans = new ActiveDmaWordPlan[remainingWords];
            var sourceBit = sourceStartBit;
            var wordStartCycle = dataStartCycle;
            var completionCycle = nextBusRequestCycle;
            var busRequestCycle = nextBusRequestCycle;
            if (_specializationEnabled)
            {
                _activeDmaPredictionPasses++;
            }

            for (var index = 0; index < remainingWords; index++)
            {
                if (_specializationEnabled)
                {
                    _activeDmaPredictedWordPlans++;
                }

                var word = firstWord + index;
                var plan = GetActiveDmaWordPlan(
                    track,
                    sync,
                    word,
                    sourceBit,
                    wordStartCycle,
                    cyclesPerBit,
                    syncMode,
                    syncOffsets,
                    syncOffsetCount);
                wordPlans[index] = plan;
                sourceBit = plan.NextSourceBit;
                wordStartCycle = plan.NextWordStartCycle;
                var requestCycle = Math.Max(plan.CompletionCycle, busRequestCycle);
                completionCycle = _bus.PredictDiskDmaCompletionCycle(requestCycle);
                busRequestCycle = completionCycle;
            }

            return new ActiveDmaTransferPlan(firstWord, wordPlans, completionCycle);
        }

        private static ActiveDmaWordPlan GetActiveDmaWordPlan(
            AmigaPreparedTrack track,
            ushort sync,
            int wordIndex,
            int sourceBit,
            long wordStartCycle,
            double cyclesPerBit,
            AmigaDiskSyncMode syncMode,
            ReadOnlySpan<int> syncOffsets,
            int syncOffsetCount)
        {
            var nextSourceBit = (sourceBit + 16) % track.BitLength;
            var bitsUntilNextWord = 16;
            if (syncMode != AmigaDiskSyncMode.None &&
                wordIndex > 0 &&
                TryFindSyncWithinNextWord(track, sync, syncMode, sourceBit, syncOffsets, syncOffsetCount, out var syncDistance, out var syncOffset))
            {
                var syncBitCount = GetSyncBitCount(syncMode);
                nextSourceBit = (syncOffset + syncBitCount) % track.BitLength;
                bitsUntilNextWord = syncDistance + syncBitCount;
            }

            var completionCycle = wordStartCycle + CyclesForBits(16, cyclesPerBit);
            return new ActiveDmaWordPlan(
                sourceBit,
                nextSourceBit,
                completionCycle,
                wordStartCycle + CyclesForBits(bitsUntilNextWord, cyclesPerBit));
        }

        private static void WriteTrackUInt16(byte[] data, int bitLength, int bitOffset, ushort value)
        {
            if (bitLength <= 0)
            {
                return;
            }

            bitOffset = Mod(bitOffset, bitLength);
            for (var bit = 0; bit < 16; bit++)
            {
                var trackBit = (bitOffset + bit) % bitLength;
                var byteIndex = trackBit >> 3;
                var bitMask = (byte)(1 << (7 - (trackBit & 7)));
                if (((value >> (15 - bit)) & 1) != 0)
                {
                    data[byteIndex] |= bitMask;
                }
                else
                {
                    data[byteIndex] &= (byte)~bitMask;
                }
            }
        }

        private static bool TryFindSyncWithinNextWord(
            AmigaPreparedTrack track,
            ushort sync,
            AmigaDiskSyncMode syncMode,
            int sourceBit,
            ReadOnlySpan<int> syncOffsets,
            int syncOffsetCount,
            out int distance,
            out int syncOffset)
        {
            if (syncOffsetCount > 0 && syncOffsetCount < DiskStreamState.MaxSyncCacheOffsets)
            {
                return TryFindCachedSyncWithinNextWord(
                    syncOffsets,
                    syncOffsetCount,
                    track.BitLength,
                    sourceBit,
                    out distance,
                    out syncOffset);
            }

            return syncMode == AmigaDiskSyncMode.Byte
                ? track.TryFindByteSyncWithinNextWord((byte)(sync >> 8), sourceBit, out distance, out syncOffset)
                : track.TryFindSyncWithinNextWord(sync, sourceBit, out distance, out syncOffset);
        }

        private static bool TryFindCachedSyncWithinNextWord(
            ReadOnlySpan<int> syncOffsets,
            int syncOffsetCount,
            int trackLength,
            int sourceBit,
            out int distance,
            out int syncOffset)
        {
            var start = sourceBit + 1;
            var stop = sourceBit + 15;
            if (stop < trackLength)
            {
                var index = LowerBound(syncOffsets, syncOffsetCount, start);
                if (index < syncOffsetCount && syncOffsets[index] <= stop)
                {
                    syncOffset = syncOffsets[index];
                    distance = syncOffset - sourceBit;
                    return true;
                }
            }
            else
            {
                var index = LowerBound(syncOffsets, syncOffsetCount, start);
                if (index < syncOffsetCount)
                {
                    syncOffset = syncOffsets[index];
                    distance = syncOffset - sourceBit;
                    return true;
                }

                var wrappedStop = stop - trackLength;
                if (syncOffsets[0] <= wrappedStop)
                {
                    syncOffset = syncOffsets[0];
                    distance = (trackLength - sourceBit) + syncOffset;
                    return true;
                }
            }

            distance = 0;
            syncOffset = -1;
            return false;
        }

        private void SetStreamPosition(
            DiskStreamState stream,
            double position,
            long cycle,
            bool invalidateInputPlan = true)
        {
            position = CanonicalizeStreamPosition(position);
            stream.Position = position;
            stream.Offset = Math.Max(0, (int)Math.Floor(position));
            stream.Cycle = cycle;
            if (invalidateInputPlan)
            {
                InvalidateNextDiskInputAdvanceCycle();
            }
        }

        private static double CanonicalizeStreamPosition(double position)
        {
            if (!double.IsFinite(position))
            {
                return 0;
            }

            position = Math.Round(position / DiskStreamPositionQuantum) * DiskStreamPositionQuantum;
            return position == 0 ? 0 : position;
        }

        private void InvalidateNextDiskInputAdvanceCycle()
        {
            _nextDiskInputAdvanceCycle = 0;
            _nextDiskSyncAdvanceCycle = 0;
            InvalidateDiskInputPlans();
        }

        private void InvalidateDiskInputPlans()
        {
            Array.Clear(_diskInputPlans, 0, _diskInputPlans.Length);
            Array.Clear(_diskInputPlanEventIndexes, 0, _diskInputPlanEventIndexes.Length);
        }

        private void ClearDiskInputPlan(int driveIndex)
        {
            if ((uint)driveIndex >= (uint)_diskInputPlans.Length)
            {
                return;
            }

            _diskInputPlans[driveIndex] = null;
            _diskInputPlanEventIndexes[driveIndex] = 0;
        }

        private int FindSyncOffset(
            AmigaPreparedTrack track,
            int startOffset,
            AmigaDiskSyncMode syncMode,
            ReadOnlySpan<int> syncOffsets = default,
            int syncOffsetCount = 0)
        {
            var length = track.BitLength;
            var syncBitCount = GetSyncBitCount(syncMode);
            if (length < syncBitCount)
            {
                return -1;
            }

            startOffset = Mod(startOffset, length);
            if (syncOffsetCount > 0 && syncOffsetCount < DiskStreamState.MaxSyncCacheOffsets)
            {
                var index = LowerBound(syncOffsets, syncOffsetCount, startOffset);
                var offset = index < syncOffsetCount ? syncOffsets[index] : syncOffsets[0];
                return syncMode == AmigaDiskSyncMode.Byte
                    ? track.RewindConsecutiveByteSync((byte)(_dsksync >> 8), offset)
                    : track.RewindConsecutiveSync(_dsksync, offset);
            }

            return syncMode == AmigaDiskSyncMode.Byte
                ? track.FindByteSyncOffset((byte)(_dsksync >> 8), startOffset)
                : track.FindSyncOffset(_dsksync, startOffset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int LowerBound(ReadOnlySpan<int> values, int count, int target)
        {
            var low = 0;
            var high = count;
            while (low < high)
            {
                var mid = (int)((uint)(low + high) >> 1);
                if (values[mid] < target)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            return low;
        }

        private static int Mod(int value, int modulus)
        {
            var result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        private readonly struct DiskDmaWordLatch
        {
            public DiskDmaWordLatch(
                bool writeMode,
                uint targetAddress,
                int sourceBit,
                ushort value,
                AmigaDmaWordReservation reservation)
            {
                WriteMode = writeMode;
                TargetAddress = targetAddress;
                SourceBit = sourceBit;
                Value = value;
                Reservation = reservation;
                HasValue = true;
            }

            public bool WriteMode { get; }

            public uint TargetAddress { get; }

            public int SourceBit { get; }

            public ushort Value { get; }

            public AmigaDmaWordReservation Reservation { get; }

            public bool HasValue { get; }
        }

        private enum DiskTrackEventKind
        {
            ByteReady,
            SyncMatch
        }

        private readonly record struct DiskTrackEvent(
            long Cycle,
            DiskTrackEventKind Kind,
            int ByteStartBit,
            bool HasWordValue,
            int WordStartBit)
        {
            public static DiskTrackEvent ByteReady(long cycle, int byteStartBit, bool hasWordValue, int wordStartBit)
                => new DiskTrackEvent(cycle, DiskTrackEventKind.ByteReady, byteStartBit, hasWordValue, wordStartBit);

            public static DiskTrackEvent SyncMatch(long cycle)
                => new DiskTrackEvent(cycle, DiskTrackEventKind.SyncMatch, 0, false, 0);
        }

        private sealed class DiskTrackEventPlan
        {
            public DiskTrackEventPlan(
                int driveIndex,
                int cylinder,
                int head,
                int trackBitLength,
                int trackStartBit,
                AmigaTrackFeatures trackFeatures,
                ushort sync,
                ushort adkconRelevant,
                AmigaDiskSyncMode syncMode,
                double cyclesPerBit,
                long startCycle,
                double startPosition,
                long endCycle,
                DiskTrackEvent[] events)
            {
                DriveIndex = driveIndex;
                Cylinder = cylinder;
                Head = head;
                TrackBitLength = trackBitLength;
                TrackStartBit = trackStartBit;
                TrackFeatures = trackFeatures;
                Sync = sync;
                AdkconRelevant = adkconRelevant;
                SyncMode = syncMode;
                CyclesPerBit = cyclesPerBit;
                StartCycle = startCycle;
                StartPosition = startPosition;
                EndCycle = endCycle;
                Events = events;
            }

            public int DriveIndex { get; }

            public int Cylinder { get; }

            public int Head { get; }

            public int TrackBitLength { get; }

            public int TrackStartBit { get; }

            public AmigaTrackFeatures TrackFeatures { get; }

            public ushort Sync { get; }

            public ushort AdkconRelevant { get; }

            public AmigaDiskSyncMode SyncMode { get; }

            public double CyclesPerBit { get; }

            public long StartCycle { get; }

            public double StartPosition { get; }

            public long EndCycle { get; }

            public DiskTrackEvent[] Events { get; }

            public bool Matches(
                int driveIndex,
                AmigaFloppyDrive drive,
                AmigaEncodedTrack track,
                AmigaDiskSyncMode syncMode,
                ushort sync,
                ushort adkcon,
                double cyclesPerBit)
                => DriveIndex == driveIndex &&
                    Cylinder == drive.Cylinder &&
                    Head == drive.Head &&
                    TrackBitLength == track.BitLength &&
                    TrackStartBit == track.StartBit &&
                    TrackFeatures == track.Features &&
                    Sync == sync &&
                    AdkconRelevant == (ushort)(adkcon & (AdkconFast | AdkconMsbSync)) &&
                    SyncMode == syncMode &&
                    CyclesPerBit.Equals(cyclesPerBit);
        }

        private sealed class DiskStreamState
        {
            public const int MaxSyncCacheOffsets = 16384;
            private const int MaxPreparedTrackBits = 262144;

            public int Cylinder { get; set; }

            public int Head { get; set; }

            public int Offset { get; set; }

            public double Position { get; set; }

            public long Cycle { get; set; }

            public long NextIndexPulseCycle { get; set; }

            public AmigaDiskSyncMode SyncCacheMode { get; set; }

            public ushort SyncCacheWord { get; set; }

            public int SyncCacheCylinder { get; set; }

            public int SyncCacheHead { get; set; }

            public int SyncCacheTrackLength { get; set; }

            public int[] SyncCacheOffsets { get; } = new int[MaxSyncCacheOffsets];

            public int SyncCacheOffsetCount { get; set; }

            public AmigaPreparedTrack PreparedTrack { get; }

            public bool PreparedTrackValid { get; set; }

            public int PreparedTrackCylinder { get; set; }

            public int PreparedTrackHead { get; set; }

            public DiskStreamState()
            {
                PreparedTrack = new AmigaPreparedTrack(MaxPreparedTrackBits, MaxSyncCacheOffsets);
            }

            public void Reset()
            {
                Cylinder = 0;
                Head = 0;
                Offset = 0;
                Position = 0;
                Cycle = 0;
                NextIndexPulseCycle = long.MaxValue;
                PreparedTrack.Clear();
                PreparedTrackValid = false;
                PreparedTrackCylinder = -1;
                PreparedTrackHead = -1;
                InvalidateSyncCache();
            }

            public void InvalidateSyncCache()
            {
                SyncCacheMode = AmigaDiskSyncMode.None;
                SyncCacheWord = 0;
                SyncCacheCylinder = -1;
                SyncCacheHead = -1;
                SyncCacheTrackLength = 0;
                SyncCacheOffsetCount = 0;
            }
        }

        private readonly record struct ActiveDmaWordPlan(
            int SourceBit,
            int NextSourceBit,
            long CompletionCycle,
            long NextWordStartCycle);

        private sealed class ActiveDmaTransferPlan
        {
            public ActiveDmaTransferPlan(int firstWord, ActiveDmaWordPlan[] words, long predictedCompletionCycle)
            {
                FirstWord = firstWord;
                Words = words;
                PredictedCompletionCycle = predictedCompletionCycle;
            }

            public int FirstWord { get; }

            public ActiveDmaWordPlan[] Words { get; }

            public long PredictedCompletionCycle { get; }

            public bool ContainsWord(int word)
                => word >= FirstWord && word - FirstWord < Words.Length;

            public ActiveDmaWordPlan GetWordPlan(int word)
                => Words[word - FirstWord];
        }
    }

    internal readonly struct AmigaDiskControllerSnapshot
    {
        public AmigaDiskControllerSnapshot(
            uint diskPointer,
            ushort dsklen,
            ushort dsksync,
            ushort dskbytr,
            int cylinder,
            int head,
            bool motorOn,
            bool selected,
            byte ciabPortB,
            int transferCount,
            int lastTransferWords,
            int lastTransferDrive,
            int lastTransferCylinder,
            int lastTransferHead,
            uint lastTransferAddress,
            int selectedDrive,
            int activeDmaDrive,
            bool activeDma,
            long activeDmaCompletionCycle,
            ushort dskdatr,
            int connectedDriveCount,
            AmigaDiskSpecializationCounters specializationCounters,
            AmigaDiskSchedulerCounters schedulerCounters)
        {
            DiskPointer = diskPointer;
            Dsklen = dsklen;
            Dsksync = dsksync;
            Dskbytr = dskbytr;
            Cylinder = cylinder;
            Head = head;
            MotorOn = motorOn;
            Selected = selected;
            CiabPortB = ciabPortB;
            TransferCount = transferCount;
            LastTransferWords = lastTransferWords;
            LastTransferDrive = lastTransferDrive;
            LastTransferCylinder = lastTransferCylinder;
            LastTransferHead = lastTransferHead;
            LastTransferAddress = lastTransferAddress;
            SelectedDrive = selectedDrive;
            ActiveDmaDrive = activeDmaDrive;
            ActiveDma = activeDma;
            ActiveDmaCompletionCycle = activeDmaCompletionCycle;
            Dskdatr = dskdatr;
            ConnectedDriveCount = connectedDriveCount;
            SpecializationCounters = specializationCounters;
            SchedulerCounters = schedulerCounters;
        }

        public uint DiskPointer { get; }

        public ushort Dsklen { get; }

        public ushort Dsksync { get; }

        public ushort Dskbytr { get; }

        public int Cylinder { get; }

        public int Head { get; }

        public bool MotorOn { get; }

        public bool Selected { get; }

        public byte CiabPortB { get; }

        public int TransferCount { get; }

        public int LastTransferWords { get; }

        public int LastTransferDrive { get; }

        public int LastTransferCylinder { get; }

        public int LastTransferHead { get; }

        public uint LastTransferAddress { get; }

        public int SelectedDrive { get; }

        public int ActiveDmaDrive { get; }

        public bool ActiveDma { get; }

        public long ActiveDmaCompletionCycle { get; }

        public ushort Dskdatr { get; }

        public int ConnectedDriveCount { get; }

        public AmigaDiskSpecializationCounters SpecializationCounters { get; }

        public AmigaDiskSchedulerCounters SchedulerCounters { get; }
    }

    internal enum AmigaDiskDmaTraceKind
    {
        Started,
        Completed,
        Cancelled,
        Stopped,
        StartBlocked,
        SyncMissing
    }

    internal enum AmigaDiskSyncMode
    {
        None,
        Word,
        Byte
    }

    internal enum AmigaDiskDmaBlockedReason
    {
        None,
        ZeroLength,
        DmaDisabled,
        NoSelectedDrive,
        SelectedDriveUnconnected,
        NoDisk,
        MotorOff,
        MotorNotReady,
        NoReadyDrive,
        SyncMissing
    }

    internal readonly struct AmigaDiskDmaTraceEntry
    {
        public AmigaDiskDmaTraceEntry(
            AmigaDiskDmaTraceKind kind,
            int transferCount,
            long cycle,
            int drive,
            int cylinder,
            int head,
            int selectedDrive,
            ushort dsklen,
            ushort dsksync,
            ushort adkcon,
            ushort dskbytr,
            ushort dskdatr,
            uint targetAddress,
            int requestedWords,
            int transferredWords,
            int sourceBit,
            int syncWaitBits,
            int trackBitLength,
            AmigaDiskSyncMode syncMode,
            long completionCycle,
            AmigaDiskDmaBlockedReason blockedReason)
        {
            Kind = kind;
            TransferCount = transferCount;
            Cycle = cycle;
            Drive = drive;
            Cylinder = cylinder;
            Head = head;
            SelectedDrive = selectedDrive;
            Dsklen = dsklen;
            Dsksync = dsksync;
            Adkcon = adkcon;
            Dskbytr = dskbytr;
            Dskdatr = dskdatr;
            TargetAddress = targetAddress;
            RequestedWords = requestedWords;
            TransferredWords = transferredWords;
            SourceBit = sourceBit;
            SyncWaitBits = syncWaitBits;
            TrackBitLength = trackBitLength;
            SyncMode = syncMode;
            WordSyncEnabled = syncMode == AmigaDiskSyncMode.Word;
            CompletionCycle = completionCycle;
            BlockedReason = blockedReason;
        }

        public AmigaDiskDmaTraceKind Kind { get; }

        public int TransferCount { get; }

        public long Cycle { get; }

        public int Drive { get; }

        public int Cylinder { get; }

        public int Head { get; }

        public int SelectedDrive { get; }

        public ushort Dsklen { get; }

        public ushort Dsksync { get; }

        public ushort Adkcon { get; }

        public ushort Dskbytr { get; }

        public ushort Dskdatr { get; }

        public uint TargetAddress { get; }

        public int RequestedWords { get; }

        public int TransferredWords { get; }

        public int SourceBit { get; }

        public int SyncWaitBits { get; }

        public int TrackBitLength { get; }

        public bool WordSyncEnabled { get; }

        public AmigaDiskSyncMode SyncMode { get; }

        public long CompletionCycle { get; }

        public AmigaDiskDmaBlockedReason BlockedReason { get; }
    }

    internal interface IAmigaDiskDmaEngine
    {
        void ReadBytesToChipRam(AmigaFloppyDrive drive, AmigaBus bus, int diskByteOffset, int byteCount, uint destinationAddress, long requestedCycle);
    }

    internal sealed class ImmediateDiskDmaEngine : IAmigaDiskDmaEngine
    {
        public void ReadBytesToChipRam(AmigaFloppyDrive drive, AmigaBus bus, int diskByteOffset, int byteCount, uint destinationAddress, long requestedCycle)
        {
            _ = requestedCycle;
            ArgumentNullException.ThrowIfNull(drive);
            ArgumentNullException.ThrowIfNull(bus);
            bus.CopyToChipRam(destinationAddress, drive.ReadBytes(diskByteOffset, byteCount));
        }
    }

    internal sealed class CycleExactDiskDmaEngine : IAmigaDiskDmaEngine
    {
        public void ReadBytesToChipRam(AmigaFloppyDrive drive, AmigaBus bus, int diskByteOffset, int byteCount, uint destinationAddress, long requestedCycle)
        {
            throw new NotImplementedException("Cycle-exact Amiga disk DMA is reserved for a later accuracy pass.");
        }
    }
}
