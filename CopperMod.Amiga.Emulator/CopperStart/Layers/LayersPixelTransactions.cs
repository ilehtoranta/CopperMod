using System;
using System.Collections.Generic;
using Amiga;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using PortableLayers = CopperStart.Layers;

namespace CopperMod.Amiga.CopperStart.Layers;

internal sealed partial class LayersHostServices
{
    private const uint FirstPixelTransactionToken = 0xFFFF_0000;
    private const long MaximumPixelSnapshotBytes = 256L * 1024 * 1024;
    private const int MaximumPooledPixelTransactions = 32;
    private const int MaximumPooledPixelOperations = 256;
    private const long MaximumPooledPixelSnapshotBytes = 4L * 1024 * 1024;

    private readonly Dictionary<uint, PixelTransaction> _pixelTransactions = new();
    private readonly Stack<PixelTransaction> _pixelTransactionPool = new();
    private readonly OriginalSourceGraphicsMemory _originalSourceMemory = new();
    private readonly PlanarBitMapSnapshot _currentPlanarScratch = new();
    private bool _currentPlanarScratchInUse;
    private uint _nextPixelTransactionToken = FirstPixelTransactionToken;
    private bool _failNextPixelTransactionAfterApplyForTest;

    internal void FailNextPixelTransactionAfterApplyForTest()
        => _failNextPixelTransactionAfterApplyForTest = true;

    internal int PixelTransactionCountForTest => _pixelTransactions.Count;
    internal int PixelTransactionPoolCountForTest => _pixelTransactionPool.Count;
    internal long PixelTransactionRetainedSnapshotBytesForTest
    {
        get
        {
            long bytes = 0;
            foreach (var journal in _pixelTransactions.Values)
                bytes = checked(bytes + journal.RetainedSnapshotBufferBytes);
            foreach (var journal in _pixelTransactionPool)
                bytes = checked(bytes + journal.RetainedSnapshotBufferBytes);
            return bytes;
        }
    }

    internal uint BeginPixelTransactionForTest(int operationCapacity)
        => BeginLayersPixelTransaction(operationCapacity).Raw;

    internal bool StagePixelBackfillForTest(
        uint transaction,
        uint rastPort,
        uint destinationBitMap,
        uint hook,
        int destinationX,
        int destinationY,
        int width,
        int height,
        int offsetX = 0,
        int offsetY = 0)
        => StageLayersPixelBackfill(
            APTR.FromPointer(transaction),
            APTR.FromPointer(rastPort),
            APTR.FromPointer(destinationBitMap),
            APTR.FromPointer(hook),
            destinationX,
            destinationY,
            width,
            height,
            offsetX,
            offsetY);

    internal bool ApplyPixelTransactionForTest(uint transaction)
        => TryApplyLayersPixelTransaction(
            APTR.FromPointer(transaction),
            consumeOnSuccess: false);

    internal void CancelPixelTransactionForTest(uint transaction)
        => CancelLayersPixelTransaction(APTR.FromPointer(transaction));

    internal void CompletePixelTransactionForTest(uint transaction)
        => CompleteLayersPixelTransaction(APTR.FromPointer(transaction));

    internal uint BeginValidatedRasterUndo(int operationCapacity)
        => BeginLayersPixelTransaction(operationCapacity).Raw;

    internal bool StageValidatedRasterUndo(
        uint transaction,
        uint rastPort,
        uint destinationBitMap,
        int destinationX,
        int destinationY,
        int width,
        int height)
        => StageLayersPixelBackfill(
            APTR.FromPointer(transaction),
            APTR.FromPointer(rastPort),
            APTR.FromPointer(destinationBitMap),
            APTR.FromPointer(LayerBackfillHook.NeverBackfill),
            destinationX,
            destinationY,
            width,
            height,
            0,
            0);

    internal bool ApplyValidatedRasterUndo(uint transaction)
        => TryApplyLayersPixelTransaction(
            APTR.FromPointer(transaction),
            consumeOnSuccess: false);

    internal void CancelValidatedRasterUndo(uint transaction)
        => CancelLayersPixelTransaction(APTR.FromPointer(transaction));

    internal void CompleteValidatedRasterUndo(uint transaction)
        => CompleteLayersPixelTransaction(APTR.FromPointer(transaction));

    private enum PixelOperationKind : byte
    {
        Copy,
        Backfill,
        ExternalGuestMutation
    }

    private sealed class PixelTransaction
    {
        internal PixelTransaction(int capacity)
        {
            Operations = new List<PixelOperation>(capacity);
            Capacity = capacity;
        }

        internal int Capacity { get; private set; }
        internal List<PixelOperation> Operations { get; }
        internal Dictionary<uint, PixelBitMapSnapshot> Snapshots { get; } = new();
        internal List<PixelRegionSnapshot> ExternalSnapshots { get; } = new();
        private Stack<PixelBitMapSnapshot> SnapshotPool { get; } = new();
        private Stack<PixelRegionSnapshot> ExternalSnapshotPool { get; } = new();
        internal long SnapshotBytes { get; set; }
        internal long RetainedSnapshotBufferBytes { get; set; }
        internal long SnapshotHighWaterBytes { get; set; }
        internal bool Applied { get; set; }

        internal void Reset(int capacity)
        {
            foreach (var snapshot in Snapshots.Values)
            {
                snapshot.Clear();
                SnapshotPool.Push(snapshot);
            }
            foreach (var snapshot in ExternalSnapshots)
            {
                snapshot.Clear();
                ExternalSnapshotPool.Push(snapshot);
            }
            Capacity = capacity;
            Operations.Clear();
            if (Operations.Capacity < capacity)
                Operations.Capacity = capacity;
            Snapshots.Clear();
            ExternalSnapshots.Clear();
            SnapshotBytes = 0;
            Applied = false;
        }

        internal PixelBitMapSnapshot RentBitMapSnapshot()
            => SnapshotPool.Count != 0
                ? SnapshotPool.Pop()
                : new PixelBitMapSnapshot();

        internal void ReturnBitMapSnapshot(PixelBitMapSnapshot snapshot)
        {
            snapshot.Clear();
            SnapshotPool.Push(snapshot);
        }

        internal PixelRegionSnapshot RentRegionSnapshot()
            => ExternalSnapshotPool.Count != 0
                ? ExternalSnapshotPool.Pop()
                : new PixelRegionSnapshot();

        internal void ReturnRegionSnapshot(PixelRegionSnapshot snapshot)
        {
            snapshot.Clear();
            ExternalSnapshotPool.Push(snapshot);
        }
    }

    private readonly record struct PixelOperation(
        PixelOperationKind Kind,
        APTR SourceBitMap,
        APTR DestinationBitMap,
        APTR RastPort,
        APTR Hook,
        int SourceX,
        int SourceY,
        int DestinationX,
        int DestinationY,
        int Width,
        int Height,
        int OffsetX,
        int OffsetY,
        byte Minterm,
        PortableLayers.LayersPixelCopySource SourceMode);

    private sealed class PlanarBitMapSnapshot
    {
        internal PlanarBitMapSnapshot()
        {
            Planes = CreatePlaneSnapshots();
        }

        internal uint BitMap { get; private set; }
        internal PlaneSnapshot[] Planes { get; }
        internal int PlaneCount { get; private set; }

        internal void Reset(uint bitMap, int planeCount)
        {
            BitMap = bitMap;
            PlaneCount = planeCount;
        }

        internal void Clear()
        {
            BitMap = 0;
            PlaneCount = 0;
        }
    }

    private sealed class PixelBitMapSnapshot
    {
        private readonly PlanarBitMapSnapshot _planar = new();
        private bool _hasPlanar;

        internal PlanarBitMapSnapshot? Planar => _hasPlanar ? _planar : null;
        internal object? Provider { get; private set; }

        internal PlanarBitMapSnapshot BeginPlanar(uint bitMap, int planeCount)
        {
            Provider = null;
            _hasPlanar = true;
            _planar.Reset(bitMap, planeCount);
            return _planar;
        }

        internal void ResetProvider(object provider)
        {
            _planar.Clear();
            _hasPlanar = false;
            Provider = provider;
        }

        internal void Clear()
        {
            Provider = null;
            _hasPlanar = false;
            _planar.Clear();
        }
    }

    private sealed class PixelRegionSnapshot
    {
        private readonly PlanarRegionSnapshot _planar = new();
        private bool _hasPlanar;

        internal uint BitMap { get; private set; }
        internal PlanarRegionSnapshot? Planar => _hasPlanar ? _planar : null;
        internal object? Provider { get; private set; }

        internal PlanarRegionSnapshot BeginPlanar(uint bitMap)
        {
            BitMap = bitMap;
            Provider = null;
            _hasPlanar = true;
            return _planar;
        }

        internal void ResetProvider(uint bitMap, object provider)
        {
            BitMap = bitMap;
            _planar.Clear();
            _hasPlanar = false;
            Provider = provider;
        }

        internal void Clear()
        {
            BitMap = 0;
            Provider = null;
            _hasPlanar = false;
            _planar.Clear();
        }
    }

    private sealed class PlanarRegionSnapshot
    {
        internal PlanarRegionSnapshot()
        {
            Planes = CreatePlaneSnapshots();
        }

        internal void Reset(
            uint bitMap,
            int firstByte,
            int bytesPerRow,
            int rowByteCount,
            int top,
            int height,
            byte firstMask,
            byte lastMask,
            int planeCount)
        {
            BitMap = bitMap;
            FirstByte = firstByte;
            BytesPerRow = bytesPerRow;
            RowByteCount = rowByteCount;
            Top = top;
            Height = height;
            FirstMask = firstMask;
            LastMask = lastMask;
            PlaneCount = planeCount;
        }

        internal uint BitMap { get; private set; }
        internal int FirstByte { get; private set; }
        internal int BytesPerRow { get; private set; }
        internal int RowByteCount { get; private set; }
        internal int Top { get; private set; }
        internal int Height { get; private set; }
        internal byte FirstMask { get; private set; }
        internal byte LastMask { get; private set; }
        internal PlaneSnapshot[] Planes { get; }
        internal int PlaneCount { get; private set; }

        internal void Clear()
        {
            BitMap = 0;
            FirstByte = 0;
            BytesPerRow = 0;
            RowByteCount = 0;
            Top = 0;
            Height = 0;
            FirstMask = 0;
            LastMask = 0;
            PlaneCount = 0;
        }
    }

    private sealed class PlaneSnapshot
    {
        private byte[] _bytes = Array.Empty<byte>();

        internal uint Address { get; private set; }
        internal byte[] Bytes => _bytes;
        internal int Capacity => _bytes.Length;
        internal int Length { get; private set; }

        internal void Reset(uint address, int length)
        {
            Address = address;
            if (_bytes.Length < length)
                Array.Resize(ref _bytes, length);
            Length = length;
        }
    }

    private static PlaneSnapshot[] CreatePlaneSnapshots()
    {
        var planes = new PlaneSnapshot[8];
        for (var plane = 0; plane < planes.Length; plane++)
            planes[plane] = new PlaneSnapshot();
        return planes;
    }

    private APTR BeginLayersPixelTransaction(int operationCapacity)
    {
        if (!_active || operationCapacity < 0 ||
            operationCapacity > PortableLayers.LayersPartitionPixelCore.MaximumPixelOperations)
        {
            return APTR.Null;
        }

        for (var attempts = 0; attempts < int.MaxValue; attempts++)
        {
            var token = _nextPixelTransactionToken;
            _nextPixelTransactionToken = unchecked(_nextPixelTransactionToken - 4u);
            if (_nextPixelTransactionToken == 0)
                _nextPixelTransactionToken = FirstPixelTransactionToken;
            if (token == 0)
                continue;
            var journal = _pixelTransactionPool.Count != 0
                ? _pixelTransactionPool.Pop()
                : new PixelTransaction(operationCapacity);
            journal.Reset(operationCapacity);
            if (_pixelTransactions.TryAdd(token, journal))
            {
                return APTR.FromPointer(token);
            }
            RecyclePixelTransaction(journal);
        }

        return APTR.Null;
    }

    private bool StageLayersPixelCopy(
        APTR transaction,
        APTR sourceBitMap,
        APTR destinationBitMap,
        int sourceX,
        int sourceY,
        int destinationX,
        int destinationY,
        int width,
        int height,
        byte minterm,
        APTR mask,
        PortableLayers.LayersPixelCopySource sourceMode)
    {
        if (!TryGetStagingTransaction(transaction, out var journal) ||
            sourceBitMap.IsNull || destinationBitMap.IsNull || mask.IsNotNull ||
            minterm != 0xC0 || width <= 0 || height <= 0 ||
            sourceMode is not PortableLayers.LayersPixelCopySource.OriginalSnapshot and
                not PortableLayers.LayersPixelCopySource.Sequential ||
            !TryCaptureBitMap(journal, sourceBitMap) ||
            !TryCaptureBitMap(journal, destinationBitMap))
        {
            return false;
        }

        journal.Operations.Add(new PixelOperation(
            PixelOperationKind.Copy,
            sourceBitMap,
            destinationBitMap,
            APTR.Null,
            APTR.Null,
            sourceX,
            sourceY,
            destinationX,
            destinationY,
            width,
            height,
            0,
            0,
            minterm,
            sourceMode));
        return true;
    }

    private bool StageLayersPixelBackfill(
        APTR transaction,
        APTR rastPort,
        APTR destinationBitMap,
        APTR hook,
        int destinationX,
        int destinationY,
        int width,
        int height,
        int offsetX,
        int offsetY)
    {
        var kind = hook.Raw switch
        {
            LayerBackfillHook.Backfill => PixelOperationKind.Backfill,
            LayerBackfillHook.NeverBackfill => PixelOperationKind.ExternalGuestMutation,
            _ => (PixelOperationKind?)null
        };
        if (!TryGetStagingTransaction(transaction, out var journal) ||
            kind is null ||
            !_memory.IsMapped(rastPort.Raw, checked((int)RastPort.Size)) ||
            width <= 0 || height <= 0 ||
            !TryShort(destinationX, out _) || !TryShort(destinationY, out _) ||
            !TryShort((long)destinationX + width - 1, out _) ||
            !TryShort((long)destinationY + height - 1, out _))
        {
            return false;
        }

        var captured = kind == PixelOperationKind.ExternalGuestMutation
            ? TryCaptureExternalRegion(
                journal,
                destinationBitMap,
                destinationX,
                destinationY,
                width,
                height)
            : TryCaptureBitMap(journal, destinationBitMap);
        if (!captured)
        {
            return false;
        }

        journal.Operations.Add(new PixelOperation(
            kind.Value,
            APTR.Null,
            destinationBitMap,
            rastPort,
            hook,
            0,
            0,
            destinationX,
            destinationY,
            width,
            height,
            offsetX,
            offsetY,
            0,
            default));
        return true;
    }

    private bool TryApplyLayersPixelTransaction(APTR transaction)
        => TryApplyLayersPixelTransaction(transaction, consumeOnSuccess: true);

    private bool TryApplyLayersPixelTransaction(
        APTR transaction,
        bool consumeOnSuccess)
    {
        if (!_pixelTransactions.TryGetValue(transaction.Raw, out var journal) ||
            journal.Applied || journal.Operations.Count > journal.Capacity)
        {
            return false;
        }

        var actualMemory = _graphicsMemory;
        for (var index = 0; index < journal.Operations.Count; index++)
        {
            var operation = journal.Operations[index];
            var applied = operation.Kind switch
            {
                PixelOperationKind.Copy => ApplyStagedCopy(
                    actualMemory,
                    journal,
                    operation),
                PixelOperationKind.Backfill => ApplyStagedBackfill(operation),
                // The corresponding guest Hook runs only after provider writes
                // have committed. Its transaction-start snapshot is retained so
                // any later abort can roll those external writes back exactly.
                PixelOperationKind.ExternalGuestMutation => true,
                _ => false
            };
            if (applied)
            {
                if (operation.Kind == PixelOperationKind.Copy)
                    TraceProviderOperationForTest("BltBitMap");
                else if (operation.Kind == PixelOperationKind.Backfill)
                    TraceProviderOperationForTest("EraseRect");
                continue;
            }

            return FailLayersPixelTransaction(transaction, journal);
        }

        // Focused failure injection exercises the provider's strongest
        // contract: once any planar/RTG cross-product operation has written,
        // a failed atomic apply must already have restored every destination
        // before Layers can abort its topology publication.
        if (_failNextPixelTransactionAfterApplyForTest)
        {
            _failNextPixelTransactionAfterApplyForTest = false;
            return FailLayersPixelTransaction(transaction, journal);
        }

        journal.Applied = true;
        if (consumeOnSuccess &&
            _pixelTransactions.Remove(transaction.Raw, out var completed))
            RecyclePixelTransaction(completed);
        return true;
    }

    private bool FailLayersPixelTransaction(
        APTR transaction,
        PixelTransaction journal)
    {
        RestorePixelSnapshots(journal);
        if (_pixelTransactions.Remove(transaction.Raw, out var removed))
            RecyclePixelTransaction(removed);
        return false;
    }

    private void CancelLayersPixelTransaction(APTR transaction)
    {
        if (!_pixelTransactions.Remove(transaction.Raw, out var journal))
            return;
        // A guest Hook can mutate pixels while the portable continuation still
        // regards the provider transaction as staged. Restoring unconditionally
        // is therefore required; for a purely staged journal it is harmless.
        RestorePixelSnapshots(journal);
        RecyclePixelTransaction(journal);
    }

    private void CompleteLayersPixelTransaction(APTR transaction)
    {
        if (_pixelTransactions.Remove(transaction.Raw, out var journal))
            RecyclePixelTransaction(journal);
    }

    private void ResetPixelTransactions()
    {
        foreach (var journal in _pixelTransactions.Values)
        {
            RestorePixelSnapshots(journal);
            RecyclePixelTransaction(journal);
        }
        _pixelTransactions.Clear();
        // Reset is the hard lifetime boundary for every managed journal and
        // reusable byte buffer. Pools are retained only within one installed
        // Layers lifetime so a large guest transaction cannot pin host memory
        // across reset/reinstall.
        _pixelTransactionPool.Clear();
        _graphics.TransactionalBitMaps?.ResetSnapshotPool();
        _originalSourceMemory.Release();
        _currentPlanarScratch.Clear();
        _currentPlanarScratchInUse = false;
        _nextPixelTransactionToken = FirstPixelTransactionToken;
        _failNextPixelTransactionAfterApplyForTest = false;
    }

    private void RecyclePixelTransaction(PixelTransaction journal)
    {
        var retain = _pixelTransactionPool.Count < MaximumPooledPixelTransactions &&
            journal.Operations.Capacity <= MaximumPooledPixelOperations &&
            journal.SnapshotHighWaterBytes <= MaximumPooledPixelSnapshotBytes;
        ReleaseProviderSnapshots(journal);
        journal.Reset(0);
        if (retain)
            _pixelTransactionPool.Push(journal);
    }

    private void ReleaseProviderSnapshots(PixelTransaction journal)
    {
        var provider = _graphics.TransactionalBitMaps;
        if (provider is null)
            return;
        foreach (var snapshot in journal.Snapshots.Values)
        {
            if (snapshot.Provider is { } providerSnapshot)
                provider.ReleaseSnapshot(providerSnapshot);
        }
        foreach (var snapshot in journal.ExternalSnapshots)
        {
            if (snapshot.Provider is { } providerSnapshot)
                provider.ReleaseSnapshot(providerSnapshot);
        }
    }

    private bool TryGetStagingTransaction(
        APTR transaction,
        out PixelTransaction journal)
    {
        if (_pixelTransactions.TryGetValue(transaction.Raw, out journal!) &&
            !journal.Applied && journal.Operations.Count < journal.Capacity)
        {
            return true;
        }

        journal = null!;
        return false;
    }

    private bool TryCaptureBitMap(PixelTransaction journal, APTR bitMap)
    {
        if (bitMap.IsNull)
            return false;
        if (journal.Snapshots.ContainsKey(bitMap.Raw))
            return true;
        if (_graphics.IsRtgBitMap?.Invoke(bitMap.Raw) == true)
        {
            var providerSnapshot = _graphics.TransactionalBitMaps?.CaptureBitMap(
                bitMap.Raw);
            if (providerSnapshot is null)
                return false;
            var providerSnapshotRecord = journal.RentBitMapSnapshot();
            providerSnapshotRecord.ResetProvider(providerSnapshot);
            journal.Snapshots.Add(bitMap.Raw, providerSnapshotRecord);
            return true;
        }
        var memory = CreatePlatform();
        if (!LayersBitMapCodec.IsMapped(ref memory, bitMap))
        {
            return false;
        }

        var bytesPerRow = LayersBitMapCodec.ReadBytesPerRow(ref memory, bitMap);
        var rows = LayersBitMapCodec.ReadRows(ref memory, bitMap);
        var depth = LayersBitMapCodec.ReadDepth(ref memory, bitMap);
        if (bytesPerRow == 0 || rows == 0 || depth is 0 or > 8)
            return false;

        var bytesPerPlaneLong = (long)bytesPerRow * rows;
        var snapshotBytes = bytesPerPlaneLong * depth;
        if (bytesPerPlaneLong <= 0 || bytesPerPlaneLong > int.MaxValue ||
            snapshotBytes > MaximumPixelSnapshotBytes - journal.SnapshotBytes)
        {
            return false;
        }

        var bytesPerPlane = checked((int)bytesPerPlaneLong);
        var snapshot = journal.RentBitMapSnapshot();
        var planar = snapshot.BeginPlanar(bitMap.Raw, depth);
        for (var plane = 0; plane < depth; plane++)
        {
            var address = LayersBitMapCodec.ReadPlane(ref memory, bitMap, plane);
            if (address.IsNull || !_memory.IsMapped(address.Raw, bytesPerPlane))
            {
                journal.ReturnBitMapSnapshot(snapshot);
                return false;
            }
            var planeSnapshot = planar.Planes[plane];
            ResetPlaneSnapshot(journal, planeSnapshot, address.Raw, bytesPerPlane);
            for (var offset = 0; offset < planeSnapshot.Length; offset++)
            {
                planeSnapshot.Bytes[offset] = _memory.ReadByte(
                    address.Raw + checked((uint)offset));
            }
        }

        journal.Snapshots.Add(bitMap.Raw, snapshot);
        journal.SnapshotBytes += snapshotBytes;
        journal.SnapshotHighWaterBytes = Math.Max(
            journal.SnapshotHighWaterBytes,
            journal.SnapshotBytes);
        return true;
    }

    private bool TryCaptureExternalRegion(
        PixelTransaction journal,
        APTR bitMap,
        int x,
        int y,
        int width,
        int height)
    {
        if (bitMap.IsNull)
            return false;
        var remainingBytes = MaximumPixelSnapshotBytes - journal.SnapshotBytes;
        if (remainingBytes < 0)
            return false;
        if (_graphics.IsRtgBitMap?.Invoke(bitMap.Raw) == true)
        {
            var maximumBytes = checked((int)Math.Min(int.MaxValue, remainingBytes));
            var snapshotBytes = 0;
            var providerSnapshot = _graphics.TransactionalBitMaps?.CaptureRectangle(
                bitMap.Raw,
                x,
                y,
                width,
                height,
                maximumBytes,
                out snapshotBytes);
            if (providerSnapshot is null || snapshotBytes < 0 ||
                snapshotBytes > remainingBytes)
            {
                return false;
            }
            var snapshot = journal.RentRegionSnapshot();
            snapshot.ResetProvider(bitMap.Raw, providerSnapshot);
            journal.ExternalSnapshots.Add(snapshot);
            journal.SnapshotBytes += snapshotBytes;
            journal.SnapshotHighWaterBytes = Math.Max(
                journal.SnapshotHighWaterBytes,
                journal.SnapshotBytes);
            return true;
        }

        var regionSnapshot = journal.RentRegionSnapshot();
        var planar = regionSnapshot.BeginPlanar(bitMap.Raw);
        if (!TryCapturePlanarRegion(
            journal,
            planar,
            bitMap,
            x,
            y,
            width,
            height,
            remainingBytes,
            out var planarBytes))
        {
            journal.ReturnRegionSnapshot(regionSnapshot);
            return false;
        }
        journal.ExternalSnapshots.Add(regionSnapshot);
        journal.SnapshotBytes += planarBytes;
        journal.SnapshotHighWaterBytes = Math.Max(
            journal.SnapshotHighWaterBytes,
            journal.SnapshotBytes);
        return true;
    }

    private bool TryCapturePlanarRegion(
        PixelTransaction journal,
        PlanarRegionSnapshot snapshot,
        APTR bitMap,
        int x,
        int y,
        int width,
        int height,
        long maximumBytes,
        out long snapshotBytes)
    {
        snapshotBytes = 0;
        var memory = CreatePlatform();
        if (!LayersBitMapCodec.IsMapped(ref memory, bitMap))
            return false;
        var bytesPerRow = LayersBitMapCodec.ReadBytesPerRow(ref memory, bitMap);
        var rows = LayersBitMapCodec.ReadRows(ref memory, bitMap);
        var depth = LayersBitMapCodec.ReadDepth(ref memory, bitMap);
        if (bytesPerRow == 0 || rows == 0 || depth is 0 or > 8)
            return false;

        var rightLong = (long)x + width;
        var bottomLong = (long)y + height;
        var left = Math.Max(0, x);
        var top = Math.Max(0, y);
        var right = (int)Math.Min(
            (long)bytesPerRow * 8,
            Math.Max(0L, rightLong));
        var bottom = (int)Math.Min(rows, Math.Max(0L, bottomLong));
        var clippedWidth = Math.Max(0, right - left);
        var clippedHeight = Math.Max(0, bottom - top);
        var firstByte = left >> 3;
        var lastByte = clippedWidth == 0 ? firstByte - 1 : (right - 1) >> 3;
        var rowByteCount = Math.Max(0, lastByte - firstByte + 1);
        var byteCountLong = (long)rowByteCount * clippedHeight * depth;
        if (byteCountLong > maximumBytes || byteCountLong > int.MaxValue)
            return false;

        var firstMask = clippedWidth == 0
            ? (byte)0
            : (byte)(0xFF >> (left & 7));
        var lastBit = clippedWidth == 0 ? 0 : (right - 1) & 7;
        var lastMask = clippedWidth == 0
            ? (byte)0
            : (byte)(0xFF << (7 - lastBit));
        if (rowByteCount == 1)
            firstMask &= lastMask;

        var bytesPerPlane = checked((int)((long)bytesPerRow * rows));
        var regionBytesPerPlane = checked(rowByteCount * clippedHeight);
        for (var plane = 0; plane < depth; plane++)
        {
            var address = LayersBitMapCodec.ReadPlane(ref memory, bitMap, plane);
            if (address.IsNull || !_memory.IsMapped(address.Raw, bytesPerPlane))
                return false;
            var planeSnapshot = snapshot.Planes[plane];
            ResetPlaneSnapshot(
                journal,
                planeSnapshot,
                address.Raw,
                regionBytesPerPlane);
            for (var row = 0; row < clippedHeight; row++)
            {
                var sourceOffset = checked((top + row) * bytesPerRow + firstByte);
                var destinationOffset = checked(row * rowByteCount);
                for (var offset = 0; offset < rowByteCount; offset++)
                {
                    planeSnapshot.Bytes[destinationOffset + offset] = _memory.ReadByte(
                        address.Raw + checked((uint)(sourceOffset + offset)));
                }
            }
        }

        snapshotBytes = byteCountLong;
        snapshot.Reset(
            bitMap.Raw,
            firstByte,
            bytesPerRow,
            rowByteCount,
            top,
            clippedHeight,
            firstMask,
            lastMask,
            depth);
        return true;
    }

    private static void ResetPlaneSnapshot(
        PixelTransaction journal,
        PlaneSnapshot snapshot,
        uint address,
        int length)
    {
        // Charge candidate growth before Array.Resize. A later plane can fail
        // validation after earlier buffers have already grown; recording the
        // retained capacity here prevents an incomplete capture from bypassing
        // the journal-pool retention limit.
        if (length > snapshot.Capacity)
        {
            journal.RetainedSnapshotBufferBytes = checked(
                journal.RetainedSnapshotBufferBytes +
                (length - snapshot.Capacity));
            journal.SnapshotHighWaterBytes = Math.Max(
                journal.SnapshotHighWaterBytes,
                journal.RetainedSnapshotBufferBytes);
        }
        snapshot.Reset(address, length);
    }

    private bool ApplyStagedCopy(
        IGraphicsMemory actualMemory,
        PixelTransaction journal,
        PixelOperation operation)
    {
        if (!TryShort(operation.SourceX, out var sourceX) ||
            !TryShort(operation.SourceY, out var sourceY) ||
            !TryShort(operation.DestinationX, out var destinationX) ||
            !TryShort(operation.DestinationY, out var destinationY) ||
            !TryShort(operation.Width, out var width) ||
            !TryShort(operation.Height, out var height))
        {
            return false;
        }

        if (operation.SourceMode == PortableLayers.LayersPixelCopySource.OriginalSnapshot)
        {
            if (!journal.Snapshots.TryGetValue(
                    operation.SourceBitMap.Raw,
                    out var sourceSnapshot))
            {
                return false;
            }

            if (sourceSnapshot.Provider is not null)
            {
                return _graphics.TransactionalBitMaps?.CopyFromSnapshot(
                    sourceSnapshot.Provider,
                    operation.DestinationBitMap.Raw,
                    sourceX,
                    sourceY,
                    destinationX,
                    destinationY,
                    width,
                    height,
                    operation.Minterm,
                    0) == true;
            }

            if (sourceSnapshot.Planar is null)
                return false;
            if (_graphics.IsRtgBitMap?.Invoke(
                    operation.DestinationBitMap.Raw) == true)
            {
                return ApplyOriginalPlanarToProvider(
                    sourceSnapshot.Planar,
                    operation);
            }

            if (!_originalSourceMemory.TryBind(
                    actualMemory,
                    sourceSnapshot.Planar))
            {
                return false;
            }
            try
            {
                return GraphicsBlitOperations.BltBitMap(
                    _originalSourceMemory,
                    operation.SourceBitMap.Raw,
                    sourceX,
                    sourceY,
                    operation.DestinationBitMap.Raw,
                    destinationX,
                    destinationY,
                    width,
                    height,
                    operation.Minterm,
                    0xFF,
                    0,
                    _graphicsAllocator,
                    _pixelBlitScratch) !=
                    GraphicsRasterOperations.Failure;
            }
            finally
            {
                _originalSourceMemory.Release();
            }
        }

        return CopyRectangle(
            operation.SourceBitMap,
            operation.DestinationBitMap,
            operation.SourceX,
            operation.SourceY,
            operation.DestinationX,
            operation.DestinationY,
            operation.Width,
            operation.Height,
            operation.Minterm,
            APTR.Null);
    }

    private bool ApplyStagedBackfill(PixelOperation operation)
    {
        if (_graphics.IsRtgBitMap?.Invoke(
                operation.DestinationBitMap.Raw) == true)
        {
            return _graphics.TransactionalBitMaps?.Backfill(
                operation.RastPort.Raw,
                operation.DestinationBitMap.Raw,
                operation.DestinationX,
                operation.DestinationY,
                operation.Width,
                operation.Height) == true;
        }

        if (!TryShort(operation.DestinationX, out var x0) ||
            !TryShort(operation.DestinationY, out var y0) ||
            !TryShort((long)operation.DestinationX + operation.Width - 1, out var x1) ||
            !TryShort((long)operation.DestinationY + operation.Height - 1, out var y1))
        {
            return false;
        }

        var rastPort = operation.RastPort;
        if (operation.Hook.Raw != LayerBackfillHook.Backfill ||
            !_memory.IsMapped(rastPort.Raw, checked((int)RastPort.Size)))
        {
            return false;
        }

        var memory = CreatePlatform();
        var originalLayer = LayersRastPortCodec.ReadLayer(ref memory, rastPort);
        var originalBitMap = LayersRastPortCodec.ReadBitMap(ref memory, rastPort);
        try
        {
            LayersRastPortCodec.WriteLayer(ref memory, rastPort, APTR.Null);
            LayersRastPortCodec.WriteBitMap(
                ref memory,
                rastPort,
                operation.DestinationBitMap);
            return GraphicsRasterOperations.EraseRect(
                _graphicsMemory,
                rastPort.Raw,
                x0,
                y0,
                x1,
                y1,
                null,
                _providerSnapshotAddresses,
                _providerSnapshotValues,
                _providerNestedSnapshotAddresses,
                _providerNestedSnapshotValues,
                _pixelMintermScratch) != GraphicsRasterOperations.Failure;
        }
        finally
        {
            LayersRastPortCodec.WriteBitMap(
                ref memory,
                rastPort,
                originalBitMap);
            LayersRastPortCodec.WriteLayer(
                ref memory,
                rastPort,
                originalLayer);
        }
    }

    private void RestorePixelSnapshots(PixelTransaction journal)
    {
        foreach (var pair in journal.Snapshots)
        {
            var snapshot = pair.Value;
            if (snapshot.Provider is not null)
            {
                _ = _graphics.TransactionalBitMaps?.RestoreBitMap(
                    pair.Key,
                    snapshot.Provider);
            }
            else if (snapshot.Planar is not null)
                RestorePlanarSnapshot(snapshot.Planar);
        }


        foreach (var snapshot in journal.ExternalSnapshots)
        {
            if (snapshot.Provider is not null)
            {
                _ = _graphics.TransactionalBitMaps?.RestoreRectangle(
                    snapshot.BitMap,
                    snapshot.Provider);
            }
            else if (snapshot.Planar is not null)
                RestorePlanarRegion(snapshot.Planar);
        }
    }

    private void RestorePlanarRegion(PlanarRegionSnapshot snapshot)
    {
        if (snapshot.RowByteCount == 0 || snapshot.Height == 0)
            return;
        for (var plane = 0; plane < snapshot.PlaneCount; plane++)
        {
            var planeSnapshot = snapshot.Planes[plane];
            for (var row = 0; row < snapshot.Height; row++)
            {
                var sourceOffset = checked(row * snapshot.RowByteCount);
                var destinationOffset = checked(
                    (snapshot.Top + row) * snapshot.BytesPerRow +
                    snapshot.FirstByte);
                for (var offset = 0; offset < snapshot.RowByteCount; offset++)
                {
                    var mask = offset == 0
                        ? snapshot.FirstMask
                        : (byte)0xFF;
                    if (offset == snapshot.RowByteCount - 1)
                        mask &= snapshot.LastMask;
                    var address = planeSnapshot.Address +
                        checked((uint)(destinationOffset + offset));
                    if (!_memory.IsMapped(address, 1))
                        continue;
                    var current = _memory.ReadByte(address);
                    var original = planeSnapshot.Bytes[sourceOffset + offset];
                    _memory.WriteByte(
                        address,
                        (byte)((current & ~mask) | (original & mask)));
                }
            }
        }
    }

    private bool ApplyOriginalPlanarToProvider(
        PlanarBitMapSnapshot sourceSnapshot,
        PixelOperation operation)
    {
        if (_currentPlanarScratchInUse)
            return false;
        _currentPlanarScratchInUse = true;
        try
        {
            if (!TryCaptureCurrentPlanarSnapshot(
                    sourceSnapshot,
                    _currentPlanarScratch))
            {
                return false;
            }
            RestorePlanarSnapshot(sourceSnapshot);
            try
            {
                return CopyRectangle(
                    operation.SourceBitMap,
                    operation.DestinationBitMap,
                    operation.SourceX,
                    operation.SourceY,
                    operation.DestinationX,
                    operation.DestinationY,
                    operation.Width,
                    operation.Height,
                    operation.Minterm,
                    APTR.Null);
            }
            finally
            {
                RestorePlanarSnapshot(_currentPlanarScratch);
            }
        }
        finally
        {
            _currentPlanarScratch.Clear();
            _currentPlanarScratchInUse = false;
        }
    }

    private bool TryCaptureCurrentPlanarSnapshot(
        PlanarBitMapSnapshot shape,
        PlanarBitMapSnapshot target)
    {
        target.Reset(shape.BitMap, shape.PlaneCount);
        for (var plane = 0; plane < shape.PlaneCount; plane++)
        {
            var source = shape.Planes[plane];
            if (!_memory.IsMapped(source.Address, source.Length))
                return false;
            var destination = target.Planes[plane];
            destination.Reset(source.Address, source.Length);
            for (var offset = 0; offset < source.Length; offset++)
                destination.Bytes[offset] = _memory.ReadByte(
                    source.Address + checked((uint)offset));
        }
        return true;
    }

    private void RestorePlanarSnapshot(PlanarBitMapSnapshot snapshot)
    {
        for (var plane = 0; plane < snapshot.PlaneCount; plane++)
        {
            var planeSnapshot = snapshot.Planes[plane];
            if (!_memory.IsMapped(planeSnapshot.Address, planeSnapshot.Length))
                continue;
            for (var offset = 0; offset < planeSnapshot.Length; offset++)
            {
                _memory.WriteByte(
                    planeSnapshot.Address + checked((uint)offset),
                    planeSnapshot.Bytes[offset]);
            }
        }
    }

    private sealed class OriginalSourceGraphicsMemory : IGraphicsMemory
    {
        private IGraphicsMemory? _actual;
        private PlanarBitMapSnapshot? _source;

        internal bool TryBind(
            IGraphicsMemory actual,
            PlanarBitMapSnapshot source)
        {
            if (_actual is not null || _source is not null)
                return false;
            _actual = actual;
            _source = source;
            return true;
        }

        internal void Release()
        {
            _source = null;
            _actual = null;
        }

        public bool TryReadByte(uint address, out byte value)
        {
            var source = _source;
            if (source is null)
            {
                value = 0;
                return false;
            }
            for (var index = 0; index < source.PlaneCount; index++)
            {
                var plane = source.Planes[index];
                var offset = unchecked(address - plane.Address);
                if (offset < plane.Length)
                {
                    value = plane.Bytes[offset];
                    return true;
                }
            }
            return _actual!.TryReadByte(address, out value);
        }

        public bool TryReadWord(uint address, out ushort value)
        {
            if (!TryReadByte(address, out var high) ||
                !TryReadByte(unchecked(address + 1u), out var low))
            {
                value = 0;
                return false;
            }
            value = (ushort)((high << 8) | low);
            return true;
        }

        public bool TryReadLong(uint address, out uint value)
        {
            if (!TryReadWord(address, out var high) ||
                !TryReadWord(unchecked(address + 2u), out var low))
            {
                value = 0;
                return false;
            }
            value = ((uint)high << 16) | low;
            return true;
        }

        public bool TryWriteByte(uint address, byte value)
            => _actual!.TryWriteByte(address, value);
        public bool TryWriteWord(uint address, ushort value)
            => _actual!.TryWriteWord(address, value);
        public bool TryWriteLong(uint address, uint value)
            => _actual!.TryWriteLong(address, value);
    }
}
