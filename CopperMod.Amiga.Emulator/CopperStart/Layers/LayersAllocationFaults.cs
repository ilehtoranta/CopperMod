using System;
using Amiga;
using PortableLayers = CopperStart.Layers;

namespace CopperMod.Amiga.CopperStart.Layers;

internal sealed partial class LayersHostServices
{
    private int _testMemoryFailOrdinal;
    private int _testMemoryAllocationCount;
    private int _testBitMapFailOrdinal;
    private int _testBitMapAllocationCount;
    private bool _failNextOpaqueFreeForTest;
    private uint _failNextRasterRetireDescriptorForTest;
    private bool _failNextRasterRetireReadActiveForTest;

    internal sealed class AllocationFaultScope : IDisposable
    {
        private readonly LayersHostServices _owner;
        private readonly bool _bitMap;
        private bool _disposed;

        internal AllocationFaultScope(
            LayersHostServices owner,
            bool bitMap,
            int failOrdinal)
        {
            _owner = owner;
            _bitMap = bitMap;
            if (failOrdinal < 0)
                throw new ArgumentOutOfRangeException(nameof(failOrdinal));
            if (bitMap)
            {
                if (owner._testBitMapFailOrdinal != 0)
                    throw new InvalidOperationException("A Layers bitmap fault scope is already active.");
                owner._testBitMapAllocationCount = 0;
                owner._testBitMapFailOrdinal = failOrdinal == 0 ? -1 : failOrdinal;
            }
            else
            {
                if (owner._testMemoryFailOrdinal != 0)
                    throw new InvalidOperationException("A Layers memory fault scope is already active.");
                owner._testMemoryAllocationCount = 0;
                owner._testMemoryFailOrdinal = failOrdinal == 0 ? -1 : failOrdinal;
            }
        }

        internal int Count => _bitMap
            ? _owner._testBitMapAllocationCount
            : _owner._testMemoryAllocationCount;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_bitMap)
            {
                _owner._testBitMapFailOrdinal = 0;
                _owner._testBitMapAllocationCount = 0;
            }
            else
            {
                _owner._testMemoryFailOrdinal = 0;
                _owner._testMemoryAllocationCount = 0;
            }
        }
    }

    internal AllocationFaultScope BeginMemoryAllocationFaultForTest(int failOrdinal)
        => new(this, bitMap: false, failOrdinal);

    internal AllocationFaultScope BeginBitMapAllocationFaultForTest(int failOrdinal)
        => new(this, bitMap: true, failOrdinal);

    internal void FailNextOpaqueFreeForTest()
        => _failNextOpaqueFreeForTest = true;

    private bool ShouldFailOpaqueFreeForTest()
    {
        if (!_failNextOpaqueFreeForTest)
            return false;
        _failNextOpaqueFreeForTest = false;
        return true;
    }

    internal void FailNextRasterRetireLinkReadForTest(uint layerInfo)
    {
        if (layerInfo == 0 || !_memory.IsMapped(
                layerInfo,
                checked((int)LayerInfo.Size)))
        {
            throw new ArgumentOutOfRangeException(nameof(layerInfo));
        }
        var memory = new LayersHostPlatform(this, state: null);
        var descriptor = LayersLayerInfoCodec.ReadExtra(
            ref memory,
            APTR.FromPointer(layerInfo));
        if (descriptor.IsNull || !_memory.IsMapped(
                descriptor.Raw,
                checked((int)PortableLayers.LayersLayerInfoCore.DescriptorSize)))
        {
            throw new InvalidOperationException(
                "LayerInfo has no live CopperStart descriptor.");
        }
        _failNextRasterRetireDescriptorForTest = descriptor.Raw;
        _failNextRasterRetireReadActiveForTest = false;
    }

    private void ArmRasterRetireLinkReadFaultForTest(APTR layer)
    {
        if (_failNextRasterRetireDescriptorForTest == 0 || layer.IsNull ||
            !_memory.IsMapped(layer.Raw, checked((int)Layer.Size)))
        {
            return;
        }
        var memory = new LayersHostPlatform(this, state: null);
        var layerInfo = LayersLayerCodec.ReadLayerInfo(ref memory, layer);
        if (layerInfo.IsNull || !_memory.IsMapped(
                layerInfo.Raw,
                checked((int)LayerInfo.Size)))
        {
            return;
        }
        _failNextRasterRetireReadActiveForTest = LayersLayerInfoCodec.ReadExtra(
            ref memory,
            layerInfo).Raw ==
            _failNextRasterRetireDescriptorForTest;
    }

    private bool ShouldFailRasterRetireLinkReadForTest(
        APTR address,
        int offset)
    {
        // LayersLayerInfoCore's private descriptor retains the internal
        // operation list head at byte 80. This test-only read fault leaves
        // guest memory untouched and makes exactly one Retire validation
        // retryable after provider execution.
        if (!_failNextRasterRetireReadActiveForTest || offset != 80 ||
            address.Raw != _failNextRasterRetireDescriptorForTest)
        {
            return false;
        }
        _failNextRasterRetireReadActiveForTest = false;
        _failNextRasterRetireDescriptorForTest = 0;
        return true;
    }

    private uint AllocatePortableLayersMemory(
        uint byteSize,
        global::Amiga.Exec.MemoryFlags requirements)
    {
        if (byteSize > int.MaxValue)
            return 0;
        if (_testMemoryFailOrdinal != 0 &&
            ++_testMemoryAllocationCount == _testMemoryFailOrdinal)
        {
            return 0;
        }
        return _allocate(checked((int)byteSize), (uint)requirements);
    }

    private int FindFreePortableLayersOpaqueSlot()
    {
        if (_opaqueAllocationCount >= MaximumOpaqueAllocations)
            return -1;
        if (_opaqueFreeHead >= 0)
        {
            var index = _opaqueFreeHead;
            _opaqueFreeHead = _opaqueFreeSlots[index];
            _opaqueFreeSlots[index] = 0;
            return index;
        }
        return _opaqueAllocationHighWater < MaximumOpaqueAllocations
            ? _opaqueAllocationHighWater++
            : -1;
    }

    private int FindPortableLayersOpaqueAllocation(
        uint payload,
        uint expectedSize)
    {
        if (payload == 0 || _opaqueAllocationCount == 0)
            return -1;

        var mask = _opaqueHashSlots.Length - 1;
        var hashIndex = OpaqueAllocationHash(payload, mask);
        for (var probe = 0; probe < _opaqueHashSlots.Length; probe++)
        {
            var hashSlot = _opaqueHashSlots[hashIndex];
            if (hashSlot == 0)
                return -1;
            if (hashSlot < 0)
            {
                hashIndex = (hashIndex + 1) & mask;
                continue;
            }

            var index = hashSlot - 1;
            var entry = _opaqueAllocations[index];
            if (entry.Payload == payload &&
                (expectedSize == 0 || entry.PayloadSize == expectedSize))
            {
                return index;
            }
            hashIndex = (hashIndex + 1) & mask;
        }
        return -1;
    }

    private bool RegisterPortableLayersOpaqueAllocation(
        uint payload,
        int ledgerIndex)
    {
        if (payload == 0 ||
            (uint)ledgerIndex >= (uint)_opaqueAllocations.Length)
        {
            return false;
        }

        var mask = _opaqueHashSlots.Length - 1;
        var hashIndex = OpaqueAllocationHash(payload, mask);
        var firstTombstone = -1;
        for (var probe = 0; probe < _opaqueHashSlots.Length; probe++)
        {
            var hashSlot = _opaqueHashSlots[hashIndex];
            if (hashSlot == 0)
            {
                _opaqueHashSlots[firstTombstone >= 0
                    ? firstTombstone
                    : hashIndex] = ledgerIndex + 1;
                return true;
            }
            if (hashSlot < 0)
            {
                if (firstTombstone < 0)
                    firstTombstone = hashIndex;
            }
            else if (_opaqueAllocations[hashSlot - 1].Payload == payload)
            {
                return false;
            }
            hashIndex = (hashIndex + 1) & mask;
        }

        if (firstTombstone < 0)
            return false;
        _opaqueHashSlots[firstTombstone] = ledgerIndex + 1;
        return true;
    }

    private void ReturnPortableLayersOpaqueSlot(int ledgerIndex)
    {
        if ((uint)ledgerIndex >= (uint)_opaqueAllocations.Length)
            return;
        _opaqueFreeSlots[ledgerIndex] = _opaqueFreeHead;
        _opaqueFreeHead = ledgerIndex;
    }

    private bool RemovePortableLayersOpaqueHash(
        uint payload,
        int ledgerIndex)
    {
        var mask = _opaqueHashSlots.Length - 1;
        var hashIndex = OpaqueAllocationHash(payload, mask);
        for (var probe = 0; probe < _opaqueHashSlots.Length; probe++)
        {
            var hashSlot = _opaqueHashSlots[hashIndex];
            if (hashSlot == 0)
                return false;
            if (hashSlot == ledgerIndex + 1)
            {
                _opaqueHashSlots[hashIndex] = -1;
                return true;
            }
            hashIndex = (hashIndex + 1) & mask;
        }
        return false;
    }

    private static int OpaqueAllocationHash(uint payload, int mask)
        => (int)((payload * 2_654_435_761u) & (uint)mask);

    private bool ReleasePortableLayersOpaqueAllocation(int ledgerIndex)
    {
        if ((uint)ledgerIndex >= (uint)_opaqueAllocations.Length ||
            !_opaqueAllocations[ledgerIndex].IsActive)
        {
            return false;
        }

        var entry = _opaqueAllocations[ledgerIndex];
        if (!RemovePortableLayersOpaqueHash(entry.Payload, ledgerIndex))
            return false;
        _opaqueAllocations[ledgerIndex] = default;
        _opaqueAllocationCount--;
        ReturnPortableLayersOpaqueSlot(ledgerIndex);
        if (entry.TotalSize <= int.MaxValue &&
            _memory.IsMapped(entry.Allocation, checked((int)entry.TotalSize)))
        {
            for (var offset = 0u; offset < entry.TotalSize; offset++)
                _memory.WriteByte(entry.Allocation + offset, 0);
        }
        _free(entry.Allocation, checked((int)entry.TotalSize));
        return true;
    }

    private void ReleaseAllPortableLayersOpaqueAllocations()
    {
        _failNextOpaqueFreeForTest = false;
        _failNextRasterRetireDescriptorForTest = 0;
        _failNextRasterRetireReadActiveForTest = false;
        for (var index = 0; index < _opaqueAllocationHighWater; index++)
            _ = ReleasePortableLayersOpaqueAllocation(index);
        _opaqueAllocationHighWater = 0;
        _opaqueFreeHead = -1;
        Array.Clear(_opaqueFreeSlots);
        Array.Clear(_opaqueHashSlots);
    }

    private bool ShouldFailPortableLayersBitMapAllocation()
        => _testBitMapFailOrdinal != 0 &&
           ++_testBitMapAllocationCount == _testBitMapFailOrdinal;
}
