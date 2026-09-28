using System;
using System.Collections.Generic;
using System.Text;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Native PAL/NTSC MonitorSpec ownership for the V36+ monitor vectors.
///
/// This is intentionally a small guest-memory implementation: it exposes the
/// public MonitorSpec timing/layout prefix needed by ViewExtra and display
/// queries, while driver callback vectors, semaphores, and copper publication
/// remain host/native-owned. The registry keeps one resident spec per native
/// monitor family and models OpenMonitor/CloseMonitor as open-count changes;
/// the resident object is not reclaimed at count zero. Unknown monitor names
/// or non-native ModeIDs are rejected so a separate CyberGraphX patch can claim
/// them.
/// </summary>
internal static partial class GraphicsMonitorOperations
{
    internal const int CloseFailure = 1;

    internal sealed partial class Registry
    {
        // MonitorSpec nodes are resident monitor-list objects.  OpenMonitor
        // only increments their public open count; CloseMonitor must not
        // reclaim the node when that count reaches zero because display
        // records and later opens may still hold the same guest pointer.
        private readonly Dictionary<uint, MonitorRecord> _resident = new();
        // Mode-ID opens do not carry a caller-owned name pointer. Keep one
        // canonical, registry-owned C string per native monitor family so
        // the resident ExtendedNode remains useful to a native monitor-list
        // walker. Named opens retain the caller pointer for ABI compatibility
        // and therefore do not add an entry here.
        private readonly Dictionary<uint, uint> _residentNameAddresses = new();
        private readonly bool _defaultMonitorNtsc;
        private uint _graphicsLibraryBase;

        internal Registry(
            bool defaultMonitorNtsc = false,
            bool supportsEcsDisplay = true,
            uint graphicsLibraryBase = 0)
        {
            _defaultMonitorNtsc = defaultMonitorNtsc;
            // Retain constructor compatibility. Chipset display capability
            // does not control OpenMonitor's registered-ID lookup.
            _ = supportsEcsDisplay;
            _graphicsLibraryBase = graphicsLibraryBase;
        }

        internal void RebindGraphicsLibraryBase(uint graphicsLibraryBase)
            => _graphicsLibraryBase = graphicsLibraryBase;

        /// <summary>
        /// Rebinds resident MonitorSpec objects that were opened before
        /// native graphics.library takeover.  Their extended-node backlink
        /// and list links move together when the old list contains exactly
        /// this registry's nodes and the new native list is empty.  A
        /// non-empty/provider-owned destination list is never rewritten.
        /// Every guest write is staged and rolled back on refusal.
        /// </summary>
        internal bool TryRebindGraphicsLibraryBase(
            IGraphicsMemory memory,
            uint graphicsLibraryBase)
        {
            if (graphicsLibraryBase == _graphicsLibraryBase)
                return true;

            var oldOrder = new List<uint>(_resident.Count);
            var oldFullOrder = new List<uint>(_resident.Count);
            var oldListWasLinked = false;
            var destinationHasMonitorList = false;
            var destinationProviderOrder = new List<uint>();
            uint destinationList = 0;
            uint oldList = 0;
            uint oldTailSentinel = 0;
            if (_resident.Count != 0)
            {
                if (TryReadMonitorList(
                        memory,
                        _graphicsLibraryBase,
                        out oldList,
                        out var oldHead,
                        out var oldTail,
                        out var oldTailPred,
                        out var oldType,
                        out var oldPad))
                {
                    if (!TryReadOwnedAndProviderMonitorList(
                            memory,
                            oldList,
                            oldHead,
                            oldTail,
                            oldTailPred,
                            oldType,
                            oldPad,
                            oldFullOrder,
                            oldOrder))
                    {
                        // A compact compatibility image can expose an empty
                        // list envelope while its resident nodes remain
                        // unlinked.  Keep that established path, but never
                        // reinterpret a mixed/foreign list as portable.
                        if (!TryReadUnlinkedResidentNodes(memory, oldOrder))
                            return false;
                    }
                    else
                    {
                        if (oldList > uint.MaxValue - 4u)
                            return false;

                        oldTailSentinel = oldList + 4u;
                        oldListWasLinked = true;
                    }
                }
                else if (!TryReadUnlinkedResidentNodes(memory, oldOrder))
                {
                    // A compact compatibility image may not map its private
                    // MonitorList tail at all. It is safe to attach resident
                    // nodes in that case only when every node still has null
                    // list links; a partially readable/foreign list remains
                    // native/provider-owned.
                    return false;
                }

                // Base zero is the compact/unbound compatibility image.  It
                // has no MonitorList publication to receive resident nodes;
                // the nodes are deliberately detached there.  A non-zero
                // destination must still prove an empty native envelope so a
                // provider-owned list is never rewritten during handoff.
                if (graphicsLibraryBase != 0)
                {
                    if (TryReadEmptyMonitorList(memory, graphicsLibraryBase))
                    {
                        if (!TryAddress(
                                graphicsLibraryBase,
                                GraphicsLayouts.GfxBaseMonitorList,
                                out destinationList) ||
                            destinationList > uint.MaxValue - 4u)
                        {
                            return false;
                        }
                    }
                    else if (!oldListWasLinked &&
                             TryReadProviderMonitorList(
                                 memory,
                                 graphicsLibraryBase,
                                 destinationProviderOrder,
                                 out destinationList))
                    {
                    }
                    else
                    {
                        return false;
                    }

                    destinationHasMonitorList = true;
                }
            }

            var writes = new List<(uint Address, uint Value)>(
                6 + (oldOrder.Count * 3));
            var writeMap = new Dictionary<uint, uint>();
            bool AddWrite(uint address, uint value)
            {
                if (writeMap.TryGetValue(address, out var existing))
                    return existing == value;

                writeMap[address] = value;
                return true;
            }

            uint movedDefault = 0, newDefaultField = 0;
            if (_resident.Count != 0)
            {
                // A default pointer to one of our residents moves with its
                // backlink/list ownership. Borrowed source defaults stay put;
                // a different destination default belongs to its provider.
                var hasDefault = TryAddress(_graphicsLibraryBase,
                    GraphicsLayouts.GfxBaseDefaultMonitor, out var oldDefaultField) &&
                    memory.TryReadLong(oldDefaultField, out movedDefault);
                if (!hasDefault && oldListWasLinked) return false;
                if (hasDefault && IsOwned(movedDefault))
                {
                    if (destinationHasMonitorList)
                    {
                        if (!TryAddress(
                                graphicsLibraryBase,
                                GraphicsLayouts.GfxBaseDefaultMonitor,
                                out newDefaultField) ||
                            !memory.TryReadLong(
                                newDefaultField,
                                out var destinationDefault) ||
                            destinationDefault != 0 &&
                            destinationDefault != movedDefault)
                        {
                            return false;
                        }
                    }

                    if (!AddWrite(oldDefaultField, 0))
                        return false;
                }
                else movedDefault = 0;

                if (oldListWasLinked)
                {
                    if (!TryAddress(
                            _graphicsLibraryBase,
                            GraphicsLayouts.GfxBaseMonitorListHead,
                            out var oldHeadField) ||
                        !TryAddress(
                            _graphicsLibraryBase,
                            GraphicsLayouts.GfxBaseMonitorListTailPred,
                            out var oldTailPredField))
                    {
                        return false;
                    }

                    // Rebuild only the provider-owned remainder of the old
                    // list.  Portable residents may have been appended after
                    // a native/provider node; removing them must update the
                    // provider's boundary links without taking ownership of
                    // or freeing that node.
                    var providerOrder = new List<uint>(oldFullOrder.Count);
                    foreach (var node in oldFullOrder)
                    {
                        if (!IsOwned(node))
                            providerOrder.Add(node);
                    }

                    var providerHead = providerOrder.Count == 0
                        ? oldTailSentinel
                        : providerOrder[0];
                    var providerTailPred = providerOrder.Count == 0
                        ? oldList
                        : providerOrder[^1];
                    if (!AddWrite(oldHeadField, providerHead) ||
                        !AddWrite(oldTailPredField, providerTailPred))
                    {
                        return false;
                    }

                    for (var index = 0; index < providerOrder.Count; index++)
                    {
                        var provider = providerOrder[index];
                        var successor = index + 1 == providerOrder.Count
                            ? oldTailSentinel
                            : providerOrder[index + 1];
                        var predecessor = index == 0
                            ? oldList
                            : providerOrder[index - 1];
                        if (!AddWrite(provider, successor) ||
                            !TryAddress(provider, 4, out var predecessorField) ||
                            !AddWrite(predecessorField, predecessor))
                        {
                            return false;
                        }
                    }
                }

                var newList = destinationList;
                var newTailSentinel = 0u;
                if (destinationHasMonitorList)
                {
                    if (newList > uint.MaxValue - 4u ||
                        !TryAddress(
                            graphicsLibraryBase,
                            GraphicsLayouts.GfxBaseMonitorListHead,
                            out var newHeadField) ||
                        !TryAddress(
                            graphicsLibraryBase,
                            GraphicsLayouts.GfxBaseMonitorListTailPred,
                            out var newTailPredField))
                    {
                        return false;
                    }

                    newTailSentinel = newList + 4u;
                    if (destinationProviderOrder.Count == 0 &&
                        !AddWrite(newHeadField, oldOrder[0]))
                    {
                        return false;
                    }

                    if (!AddWrite(newTailPredField, oldOrder[^1]))
                        return false;

                    if (destinationProviderOrder.Count != 0 &&
                        (!AddWrite(
                            destinationProviderOrder[^1],
                            oldOrder[0])))
                    {
                        return false;
                    }
                }

                for (var index = 0; index < oldOrder.Count; index++)
                {
                    var monitor = oldOrder[index];
                    var predecessor = destinationHasMonitorList
                        ? index == 0
                            ? destinationProviderOrder.Count == 0
                                ? newList
                                : destinationProviderOrder[^1]
                            : oldOrder[index - 1]
                        : 0;
                    var successor = destinationHasMonitorList
                        ? index + 1 == oldOrder.Count
                            ? newTailSentinel
                            : oldOrder[index + 1]
                        : 0;
                    if (!AddWrite(monitor, successor) ||
                        !TryAddress(monitor, 4, out var predecessorField) ||
                        !AddWrite(predecessorField, predecessor) ||
                        !TryAddress(
                            monitor,
                            GraphicsLayouts.ExtendedNodeLibrary,
                            out var libraryField) ||
                        !AddWrite(libraryField, graphicsLibraryBase))
                    {
                        return false;
                    }
                }

                if (movedDefault != 0 && destinationHasMonitorList &&
                    !AddWrite(newDefaultField, movedDefault))
                {
                    return false;
                }
            }

            foreach (var pair in writeMap)
            {
                writes.Add((pair.Key, pair.Value));
            }

            var originals = new List<(uint Address, byte[] Original)>(writes.Count);
            var seen = new HashSet<uint>();
            foreach (var write in writes)
            {
                if (!seen.Add(write.Address))
                    continue;

                if (!TrySnapshot(
                        memory,
                        write.Address,
                        sizeof(uint),
                        out var original))
                    return false;

                originals.Add((write.Address, original));
            }

            var applied = 0;
            for (; applied < writes.Count; applied++)
            {
                if (memory.TryWriteLong(writes[applied].Address, writes[applied].Value))
                    continue;

                // Restore the currently failing field as well as every
                // earlier publication.  A LONG adapter may have committed
                // its leading bytes before returning false; byte-wise
                // rollback prevents a mixed old/new monitor list or xln_Lib
                // backlink from escaping this cutover transaction.
                foreach (var original in originals)
                    Restore(memory, original.Address, original.Original);

                return false;
            }

            _graphicsLibraryBase = graphicsLibraryBase;
            return true;
        }

        private bool TryReadExactOwnedMonitorList(
            IGraphicsMemory memory,
            uint graphicsLibraryBase,
            List<uint> order)
        {
            order.Clear();
            if (!TryReadMonitorList(
                    memory,
                    graphicsLibraryBase,
                    out var list,
                    out var head,
                    out var tail,
                    out var tailPred,
                    out var type,
                    out var pad) ||
                type != 0 ||
                pad != 0 ||
                tail != 0)
            {
                return false;
            }

            var tailSentinel = list + 4u;
            if (head == tailSentinel || head == 0)
                return false;

            var previous = list;
            var current = head;
            var visited = new HashSet<uint>();
            while (current != tailSentinel)
            {
                if (current == 0 ||
                    (current & 1u) != 0 ||
                    current > uint.MaxValue - 7u ||
                    !visited.Add(current) ||
                    !_resident.ContainsKey(current) ||
                    order.Count >= _resident.Count ||
                    !memory.TryReadLong(current, out var successor) ||
                    !memory.TryReadLong(current + 4u, out var predecessor) ||
                    predecessor != previous)
                {
                    return false;
                }

                order.Add(current);
                previous = current;
                current = successor;
            }

            return order.Count == _resident.Count && tailPred == previous;
        }

        private bool TryReadOwnedAndProviderMonitorList(
            IGraphicsMemory memory,
            uint list,
            uint head,
            uint tail,
            uint tailPred,
            byte type,
            byte pad,
            List<uint> fullOrder,
            List<uint> ownedOrder)
        {
            fullOrder.Clear();
            ownedOrder.Clear();
            if (type != 0 || pad != 0 || tail != 0 ||
                list > uint.MaxValue - 4u)
            {
                return false;
            }

            var tailSentinel = list + 4u;
            if (head == 0 || head == tailSentinel)
                return false;

            var previous = list;
            var current = head;
            var visited = new HashSet<uint>();
            while (current != tailSentinel)
            {
                if (current == 0 ||
                    (current & 1u) != 0 ||
                    current > uint.MaxValue - 7u ||
                    !visited.Add(current) ||
                    !memory.TryReadLong(current, out var successor) ||
                    !memory.TryReadLong(current + 4u, out var predecessor) ||
                    predecessor != previous)
                {
                    fullOrder.Clear();
                    ownedOrder.Clear();
                    return false;
                }

                fullOrder.Add(current);
                if (_resident.ContainsKey(current))
                    ownedOrder.Add(current);

                previous = current;
                current = successor;
            }

            // Every resident must be present in the linked source list. A
            // partially linked registry is ambiguous: silently dropping an
            // unlinked resident while detaching provider nodes would leak a
            // public object or make a later retry unsafe.
            return previous == tailPred &&
                   ownedOrder.Count == _resident.Count;
        }

        private bool TryReadProviderMonitorList(
            IGraphicsMemory memory,
            uint graphicsLibraryBase,
            List<uint> order,
            out uint list)
        {
            order.Clear();
            list = 0;
            if (!TryReadMonitorList(
                    memory,
                    graphicsLibraryBase,
                    out list,
                    out var head,
                    out var tail,
                    out var tailPred,
                    out var type,
                    out var pad) ||
                type != 0 ||
                pad != 0 ||
                tail != 0 ||
                list > uint.MaxValue - 4u)
            {
                return false;
            }

            var tailSentinel = list + 4u;
            if (head == 0 || head == tailSentinel)
                return false;

            var previous = list;
            var current = head;
            var visited = new HashSet<uint>();
            while (current != tailSentinel)
            {
                if (current == 0 ||
                    (current & 1u) != 0 ||
                    current > uint.MaxValue - 7u ||
                    _resident.ContainsKey(current) ||
                    !visited.Add(current) ||
                    !memory.TryReadLong(current, out var successor) ||
                    !memory.TryReadLong(current + 4u, out var predecessor) ||
                    predecessor != previous)
                {
                    order.Clear();
                    return false;
                }

                order.Add(current);
                previous = current;
                current = successor;
            }

            return previous == tailPred && order.Count != 0;
        }

        private bool TryReadUnlinkedResidentNodes(
            IGraphicsMemory memory,
            List<uint> order)
        {
            order.Clear();
            foreach (var monitor in _resident.Keys)
            {
                if (monitor > uint.MaxValue - 7u ||
                    !memory.TryReadLong(monitor, out var successor) ||
                    !memory.TryReadLong(monitor + 4u, out var predecessor) ||
                    successor != 0 ||
                    predecessor != 0)
                {
                    return false;
                }

                order.Add(monitor);
            }

            return order.Count == _resident.Count;
        }

        private static bool TryReadEmptyMonitorList(
            IGraphicsMemory memory,
            uint graphicsLibraryBase)
        {
            return TryReadMonitorList(
                       memory,
                       graphicsLibraryBase,
                       out var list,
                       out var head,
                       out var tail,
                       out var tailPred,
                       out var type,
                       out var pad) &&
                   type == 0 &&
                   pad == 0 &&
                   tail == 0 &&
                   head == list + 4u &&
                   tailPred == list;
        }

        private static bool TryReadMonitorList(
            IGraphicsMemory memory,
            uint graphicsLibraryBase,
            out uint list,
            out uint head,
            out uint tail,
            out uint tailPred,
            out byte type,
            out byte pad)
        {
            list = 0;
            head = 0;
            tail = 0;
            tailPred = 0;
            type = 0;
            pad = 0;
            if (!TryAddress(
                    graphicsLibraryBase,
                    GraphicsLayouts.GfxBaseMonitorList,
                    out list) ||
                !TryAddress(
                    graphicsLibraryBase,
                    GraphicsLayouts.GfxBaseMonitorListHead,
                    out var headAddress) ||
                !TryAddress(
                    graphicsLibraryBase,
                    GraphicsLayouts.GfxBaseMonitorListTail,
                    out var tailAddress) ||
                !TryAddress(
                    graphicsLibraryBase,
                    GraphicsLayouts.GfxBaseMonitorListTailPred,
                    out var tailPredAddress) ||
                !TryAddress(
                    graphicsLibraryBase,
                    GraphicsLayouts.GfxBaseMonitorListType,
                    out var typeAddress) ||
                !TryAddress(
                    graphicsLibraryBase,
                    GraphicsLayouts.GfxBaseMonitorListPad,
                    out var padAddress) ||
                !memory.TryReadLong(headAddress, out head) ||
                !memory.TryReadLong(tailAddress, out tail) ||
                !memory.TryReadLong(tailPredAddress, out tailPred) ||
                !memory.TryReadByte(typeAddress, out type) ||
                !memory.TryReadByte(padAddress, out pad))
            {
                return false;
            }

            return true;
        }

        internal uint Open(
            IGraphicsMemory memory,
            IGraphicsAllocatorBackend allocator,
            uint monitorName,
            uint displayId,
            ushort initialOpenCount = 1)
        {
            if (monitorName != 0 && TrySelectRegisteredName(memory, monitorName, out var registered, out _))
                return registered == 0 ? 0 : OpenRegisteredReference(memory, registered);
            if (monitorName == 0 && TrySelectRegisteredId(memory, displayId, out var selected, out _))
                return selected == 0 || initialOpenCount == 0 ? selected : OpenRegisteredReference(memory, selected);

            return OpenBackend(memory, allocator, monitorName, displayId, initialOpenCount);
        }

        private uint OpenBackend(
            IGraphicsMemory memory,
            IGraphicsAllocatorBackend allocator,
            uint monitorName,
            uint displayId,
            ushort initialOpenCount = 1)
        {
            if (!TryResolve(
                    memory,
                    monitorName,
                    displayId,
                    _defaultMonitorNtsc,
                    out var selection))
                return 0;

            // OpenMonitor returns the monitor-list object, not a fresh copy.
            // Share the portable spec for repeated opens of the same native
            // monitor and publish the guest open-count increment atomically,
            // including a resident spec whose previous count reached zero.
            foreach (var entry in _resident)
            {
                var openCountAddress = entry.Key +
                    (uint)GraphicsLayouts.MonitorSpecOpenCount;
                if (entry.Value.MonitorId != selection.MonitorId ||
                    !memory.TryReadWord(openCountAddress, out var openCount) ||
                    openCount == ushort.MaxValue)
                {
                    if (entry.Value.MonitorId == selection.MonitorId)
                        return 0;

                    continue;
                }

                // A resident MonitorSpec is shared across OpenMonitor calls.
                // Treat the open-count WORD as a publication transaction: a
                // sparse/native bridge may accept its high byte and then
                // reject the operation, so restoring with another WORD write
                // would preserve the corruption or fail a second time.  Keep
                // the original bytes until the increment commits and restore
                // them byte-wise on every decline.
                if (!TrySnapshot(
                        memory,
                        openCountAddress,
                        sizeof(ushort),
                        out var originalOpenCount))
                {
                    if (entry.Value.MonitorId == selection.MonitorId)
                        return 0;

                    continue;
                }

                if (!memory.TryWriteWord(
                        openCountAddress,
                        (ushort)(openCount + 1)))
                {
                    // The rejected WORD path may have published only a
                    // prefix, so restore captured bytes instead of retrying
                    // the same failing WORD operation.
                    Restore(memory, openCountAddress, originalOpenCount);
                    if (entry.Value.MonitorId == selection.MonitorId)
                        return 0;

                    continue;
                }

                return entry.Key;
            }

            if (!allocator.TryAllocate(
                (uint)GraphicsLayouts.MonitorSpecSize,
                    GraphicsMemoryClass.Public,
                    out var address) ||
                address == 0 ||
                _resident.ContainsKey(address))
            {
                if (address != 0)
                    allocator.Free(
                        address,
                        (uint)GraphicsLayouts.MonitorSpecSize,
                        GraphicsMemoryClass.Public);
                return 0;
            }

            // Keep the allocation's original public bytes available until
            // optional native GfxBase publication has also succeeded.  The
            // allocator may reuse this address immediately after a declined
            // open, so a failed list splice must restore the entire
            // MonitorSpec envelope, not just its private list links.
            if (!TrySnapshot(memory, address, GraphicsLayouts.MonitorSpecSize, out var originalEnvelope))
            {
                allocator.Free(
                    address,
                    (uint)GraphicsLayouts.MonitorSpecSize,
                    GraphicsMemoryClass.Public);
                return 0;
            }

            if (!Initialize(
                    memory,
                    address,
                    monitorName,
                    selection,
                    initialOpenCount,
                    _graphicsLibraryBase))
            {
                allocator.Free(
                    address,
                    (uint)GraphicsLayouts.MonitorSpecSize,
                    GraphicsMemoryClass.Public);
                return 0;
            }

            // A compact host shim does not expose the private tail of
            // GfxBase. When a full native-compatible base is mapped, publish
            // the resident MonitorSpec through its real MonitorList so
            // CopperSharp68k/native callers see the same ownership as the
            // OpenMonitor vector. Failure leaves the new allocation
            // unowned; native/provider ownership remains available.
            if (!PublishNativeGfxBase(memory, address, selection, _graphicsLibraryBase))
            {
                Restore(memory, address, originalEnvelope);
                allocator.Free(
                    address,
                    (uint)GraphicsLayouts.MonitorSpecSize,
                    GraphicsMemoryClass.Public);
                return 0;
            }

            // Publishing a canonical name is a best-effort extension to the
            // already-committed monitor object. A provider may expose a
            // sparse MonitorSpec envelope that can link successfully but
            // refuse the optional name field; leave that object nameless
            // rather than turning a valid OpenMonitor into a partial
            // allocation failure.
            if (monitorName == 0)
                TryPublishResidentName(memory, allocator, selection.MonitorId, address);

            _resident.Add(address, new MonitorRecord(selection.MonitorId, selection.IsPal));
            return address;
        }

        private void TryPublishResidentName(
            IGraphicsMemory memory,
            IGraphicsAllocatorBackend allocator,
            uint monitorId,
            uint monitorSpec)
        {
            if (!TryGetOrCreateResidentName(
                    memory,
                    allocator,
                    monitorId,
                    out var nameAddress,
                    out var allocated))
            {
                return;
            }

            var nameField = monitorSpec +
                (uint)GraphicsLayouts.MonitorSpecNodeName;
            if (!TrySnapshot(
                    memory,
                    nameField,
                    sizeof(uint),
                    out var originalNameField))
            {
                if (allocated)
                    ReleaseResidentName(allocator, monitorId, nameAddress);

                return;
            }

            if (memory.TryWriteLong(nameField, nameAddress))
            {
                return;
            }

            // The optional name is not allowed to turn a valid resident
            // MonitorSpec into a dangling pointer. A sparse/native bridge
            // may publish a prefix of the APTR before declining the LONG;
            // restore the captured bytes before releasing a newly allocated
            // string so a later list walk never observes freed memory.
            Restore(memory, nameField, originalNameField);
            if (allocated)
                ReleaseResidentName(allocator, monitorId, nameAddress);
        }

        private bool TryGetOrCreateResidentName(
            IGraphicsMemory memory,
            IGraphicsAllocatorBackend allocator,
            uint monitorId,
            out uint address,
            out bool allocated)
        {
            allocated = false;
            if (allocator is not IGraphicsMonitorNameAllocator
                {
                    SupportsMonitorNameAllocation: true
                })
            {
                address = 0;
                return false;
            }

            if (_residentNameAddresses.TryGetValue(monitorId, out address))
                return true;

            var name = monitorId == GraphicsModeIds.NtscMonitor
                ? "ntsc.monitor"
                : monitorId == GraphicsModeIds.PalMonitor
                    ? "pal.monitor"
                    : string.Empty;
            if (name.Length == 0)
            {
                address = 0;
                return false;
            }

            var byteCount = checked((uint)name.Length + 1u);
            if (!allocator.TryAllocate(
                    byteCount,
                    GraphicsMemoryClass.Public,
                    out address) ||
                address == 0 ||
                address > uint.MaxValue - (byteCount - 1u))
            {
                if (address != 0)
                    allocator.Free(address, byteCount, GraphicsMemoryClass.Public);
                address = 0;
                return false;
            }

            for (var index = 0; index < name.Length; index++)
            {
                if (memory.TryWriteByte(address + (uint)index, (byte)name[index]))
                    continue;

                allocator.Free(address, byteCount, GraphicsMemoryClass.Public);
                address = 0;
                return false;
            }

            if (!memory.TryWriteByte(address + (uint)name.Length, 0))
            {
                allocator.Free(address, byteCount, GraphicsMemoryClass.Public);
                address = 0;
                return false;
            }

            _residentNameAddresses[monitorId] = address;
            allocated = true;
            return true;
        }

        private void ReleaseResidentName(
            IGraphicsAllocatorBackend allocator,
            uint monitorId,
            uint address)
        {
            if (!_residentNameAddresses.Remove(monitorId, out var owned) ||
                owned != address)
            {
                return;
            }

            var byteCount = monitorId == GraphicsModeIds.NtscMonitor
                ? 13u
                : 12u;
            allocator.Free(address, byteCount, GraphicsMemoryClass.Public);
        }

        internal int Close(
            IGraphicsMemory memory,
            IGraphicsAllocatorBackend allocator,
            uint monitorSpec)
        {
            // V40 accepts a NULL MonitorSpec as an idempotent close.  Claim
            // that no-op without touching the allocator; a non-zero spec
            // must still be a word-aligned, non-wrapping public structure
            // before the resident lookup or any guest state is touched.  A
            // byte-addressable host mapping must not turn an odd or wrapping
            // pointer into a portable close claim; native/provider ownership
            // retains that address-error envelope.
            if (monitorSpec == 0)
                return 0;

            if ((monitorSpec & 1u) != 0 ||
                monitorSpec > uint.MaxValue -
                    (uint)(GraphicsLayouts.MonitorSpecSize - 1))
            {
                return CloseFailure;
            }

            // A caller may hold a reference to a library-associated monitor
            // owned by another publisher, including one already unlinked from
            // MonitorList. Closing that reference does not adopt its allocation
            // into _resident or require the publisher's list to remain mapped.
            if (!_resident.ContainsKey(monitorSpec) &&
                !IsAssociatedMonitor(memory, monitorSpec))
                return CloseFailure;

            if (!memory.TryReadWord(
                    monitorSpec + (uint)GraphicsLayouts.MonitorSpecOpenCount,
                    out var openCount) ||
                openCount == 0)
            {
                return CloseFailure;
            }

            var openCountAddress = monitorSpec +
                (uint)GraphicsLayouts.MonitorSpecOpenCount;
            if (!TrySnapshot(
                    memory,
                    openCountAddress,
                    sizeof(ushort),
                    out var originalOpenCount))
            {
                return CloseFailure;
            }

            var nextOpenCount = openCount > 1
                ? (ushort)(openCount - 1)
                : (ushort)0;
            if (memory.TryWriteWord(openCountAddress, nextOpenCount))
            {
                // The node remains resident in the monitor list even when
                // its count reaches zero; DTAG_MNTR and a later OpenMonitor
                // continue to resolve the same guest object.
                return 0;
            }

            // A rejected decrement may have published only one byte of the
            // WORD. Restore the captured count byte-wise so a retry cannot
            // consume or leak a phantom monitor reference.
            Restore(memory, openCountAddress, originalOpenCount);
            return CloseFailure;
        }

        internal bool IsOwned(uint monitorSpec)
            => monitorSpec != 0 && _resident.ContainsKey(monitorSpec);

        // Bootstrap a new CMDB only from this registry's resident ownership.
        // Public DefaultMonitor/list membership alone never grants that right.
        // This snapshot neither allocates nor acquires an open reference and
        // deliberately ignores mutable names, timing flags and open counts.
        internal bool TryGetResidentRegistrations(IGraphicsMemory memory,
            out uint ntsc, out uint pal)
        {
            ntsc = pal = 0;
            foreach (var entry in _resident)
            {
                if (!IsRegisteredEnvelope(memory, entry.Key)) return false;
                if (entry.Value.MonitorId == GraphicsModeIds.NtscMonitor) ntsc = entry.Key;
                else if (entry.Value.MonitorId == GraphicsModeIds.PalMonitor) pal = entry.Key;
                else return false;
            }
            return true;
        }

        private bool IsAssociatedMonitor(IGraphicsMemory memory, uint monitorSpec)
            => _graphicsLibraryBase != 0 &&
               (_graphicsLibraryBase & 1u) == 0 &&
               _graphicsLibraryBase <= uint.MaxValue -
                   (uint)(GraphicsLayouts.GfxBaseDefaultMonitor + sizeof(uint) - 1) &&
               memory.TryReadByte(monitorSpec + (uint)GraphicsLayouts.MonitorSpecNodeType, out var type) &&
               type == 18 &&
               memory.TryReadWord(monitorSpec + (uint)GraphicsLayouts.MonitorSpecNodeSubsystem, out var kind) &&
               kind == 0x0204 &&
               memory.TryReadLong(monitorSpec + (uint)GraphicsLayouts.ExtendedNodeLibrary, out var library) &&
               library == _graphicsLibraryBase;

        internal uint FindOpenForDisplay(uint displayId)
        {
            var monitorId = displayId & 0xFFFF_1000u;
            // DEFAULT_MONITOR_ID has no explicit PAL/NTSC monitor bits.  The
            // Resolve the zero monitor part through the same active profile
            // used by Open.  This keeps DTAG_MNTR and MonitorSpec ownership
            // coherent for default ModeIDs on both PAL and NTSC machines.
            if (monitorId == GraphicsModeIds.DefaultMonitor)
                monitorId = _defaultMonitorNtsc
                    ? GraphicsModeIds.NtscMonitor
                    : GraphicsModeIds.PalMonitor;

            if (monitorId != GraphicsModeIds.PalMonitor &&
                monitorId != GraphicsModeIds.NtscMonitor)
            {
                return 0;
            }

            foreach (var entry in _resident)
            {
                if (entry.Value.MonitorId == monitorId)
                    return entry.Key;
            }

            return 0;
        }

        /// <summary>
        /// Resolves the resident native monitor object used by a
        /// <c>DTAG_MNTR</c> query.  Kickstart's display records refer to the
        /// monitor-list object even when the caller has not explicitly
        /// opened it yet.  The portable registry creates that resident
        /// lazily with an already-zero open count; an existing open count is
        /// therefore preserved byte-for-byte and no synthetic close can
        /// leave a partially opened object behind.
        /// </summary>
        internal uint FindOrCreateForDisplay(
            IGraphicsMemory memory,
            IGraphicsAllocatorBackend allocator,
            uint displayId)
        {
            var monitorId = displayId & 0xFFFF_1000u;
            if (monitorId == GraphicsModeIds.DefaultMonitor)
            {
                monitorId = _defaultMonitorNtsc
                    ? GraphicsModeIds.NtscMonitor
                    : GraphicsModeIds.PalMonitor;
            }

            if (monitorId != GraphicsModeIds.PalMonitor &&
                monitorId != GraphicsModeIds.NtscMonitor)
            {
                return 0;
            }

            foreach (var entry in _resident)
            {
                if (entry.Value.MonitorId == monitorId)
                    return entry.Key;
            }

            // Use the plain native monitor key rather than the caller's
            // feature key.  This deliberately keeps DTAG_MNTR available for
            // a discoverable-but-unavailable ECS record on an OCS host.  The
            // resident is initialized with an already-zero open count so the
            // query does not need a temporary OpenMonitor/CloseMonitor pair;
            // that pair could otherwise leave a count-one leak if a guest
            // write failed during the close boundary.
            return Open(memory, allocator, 0, monitorId, initialOpenCount: 0);
        }
    }

    private readonly record struct MonitorSelection(
        uint MonitorId,
        bool IsPal,
        bool IsDefault);

    private readonly record struct MonitorRecord(uint MonitorId, bool IsPal);

    private static bool TryResolve(
        IGraphicsMemory memory,
        uint monitorName,
        uint displayId,
        bool defaultMonitorNtsc,
        out MonitorSelection selection)
    {
        if (monitorName != 0)
        {
            if (!TryReadCString(memory, monitorName, out var name))
            {
                selection = default;
                return false;
            }

            // Kickstart 3.1 treats the default alias case-insensitively, but
            // canonical resident names are case-sensitive. Keep those paths
            // distinct (verified by the original-ROM selector matrix).
            if (string.Equals(name, "default.monitor", StringComparison.OrdinalIgnoreCase))
            {
                selection = defaultMonitorNtsc
                    ? new MonitorSelection(GraphicsModeIds.NtscMonitor, false, true)
                    : new MonitorSelection(GraphicsModeIds.PalMonitor, true, true);
                return true;
            }

            if (string.Equals(name, "pal.monitor", StringComparison.Ordinal))
            {
                selection = new MonitorSelection(GraphicsModeIds.PalMonitor, true, false);
                return true;
            }

            if (string.Equals(name, "ntsc.monitor", StringComparison.Ordinal))
            {
                selection = new MonitorSelection(GraphicsModeIds.NtscMonitor, false, false);
                return true;
            }

            selection = default;
            return false;
        }

        // Zero selects the jumper default. INVALID_ID is a display-database
        // sentinel, not an OpenMonitor default alias (original Kickstart 3.1).
        if (displayId == 0)
        {
            selection = defaultMonitorNtsc
                ? new MonitorSelection(GraphicsModeIds.NtscMonitor, false, true)
                : new MonitorSelection(GraphicsModeIds.PalMonitor, true, true);
            return true;
        }

        if (!GraphicsMonitorSelectors.TryGetFamily(displayId, out var monitorId))
        {
            selection = default;
            return false;
        }

        if (monitorId == GraphicsModeIds.DefaultMonitor)
        {
            selection = defaultMonitorNtsc
                ? new MonitorSelection(GraphicsModeIds.NtscMonitor, false, true)
                : new MonitorSelection(GraphicsModeIds.PalMonitor, true, true);
            return true;
        }

        if (monitorId == GraphicsModeIds.PalMonitor)
        {
            selection = new MonitorSelection(monitorId, true, false);
            return true;
        }

        if (monitorId == GraphicsModeIds.NtscMonitor)
        {
            selection = new MonitorSelection(monitorId, false, false);
            return true;
        }

        selection = default;
        return false;
    }

    private static bool Initialize(
        IGraphicsMemory memory,
        uint address,
        uint monitorName,
        MonitorSelection selection,
        ushort openCount,
        uint graphicsLibraryBase)
    {
        // MonitorSpec contains word and long fields, so the resident node is
        // required to be word-aligned.  More importantly, every byte of the
        // public envelope must remain in the 32-bit guest address space:
        // unchecked address arithmetic here would let a wrapping memory
        // provider alias the tail of a high allocation into low memory while
        // the caller observes a successful OpenMonitor.
        if (address == 0 ||
            (address & 1u) != 0 ||
            address > uint.MaxValue - (uint)(GraphicsLayouts.MonitorSpecSize - 1))
        {
            return false;
        }

        // Keep the complete public envelope private until every field has
        // been validated.  OpenMonitor allocates a resident node before it
        // reaches this method, so a late provider write failure must not
        // leave a partially initialized MonitorSpec behind for a later
        // allocator reuse or a native list walk.
        var image = GraphicsMonitorSpecImage.Create(address, monitorName, selection.IsPal,
            openCount, graphicsLibraryBase);
        var transaction = new MonitorMemoryTransaction(memory);
        for (var offset = 0; offset < image.Length; offset++)
        {
            if (!transaction.TryWriteByte(address + (uint)offset, image[offset]))
                return false;
        }
        return transaction.Commit();
    }

    /// <summary>
    /// Stages the MonitorSpec envelope until every public field has been
    /// prepared.  Commit uses byte writes so a provider that rejects a late
    /// guest write can be rolled back without exposing a half-built node.
    /// </summary>
    private sealed class MonitorMemoryTransaction : IGraphicsMemory
    {
        private readonly IGraphicsMemory _backing;
        private readonly Dictionary<uint, byte> _writes = new();

        internal MonitorMemoryTransaction(IGraphicsMemory backing)
        {
            _backing = backing;
        }

        public bool TryReadByte(uint address, out byte value)
        {
            if (_writes.TryGetValue(address, out value))
                return true;

            return _backing.TryReadByte(address, out value);
        }

        public bool TryReadWord(uint address, out ushort value)
        {
            if (address > uint.MaxValue - 1u ||
                !TryReadByte(address, out var high) ||
                !TryReadByte(address + 1u, out var low))
            {
                value = 0;
                return false;
            }

            value = (ushort)((high << 8) | low);
            return true;
        }

        public bool TryReadLong(uint address, out uint value)
        {
            value = 0;
            if (address > uint.MaxValue - 3u ||
                !TryReadByte(address, out var b0) ||
                !TryReadByte(address + 1u, out var b1) ||
                !TryReadByte(address + 2u, out var b2) ||
                !TryReadByte(address + 3u, out var b3))
            {
                return false;
            }

            value = ((uint)b0 << 24) |
                    ((uint)b1 << 16) |
                    ((uint)b2 << 8) |
                    b3;
            return true;
        }

        public bool TryWriteByte(uint address, byte value)
        {
            // A staged write still requires a readable mapped byte.  This
            // preserves the original memory-provider admission boundary.
            if (!_writes.ContainsKey(address) && !_backing.TryReadByte(address, out _))
                return false;

            _writes[address] = value;
            return true;
        }

        public bool TryWriteWord(uint address, ushort value)
            => address <= uint.MaxValue - 1u &&
               TryWriteByte(address, (byte)(value >> 8)) &&
               TryWriteByte(address + 1u, (byte)value);

        public bool TryWriteLong(uint address, uint value)
            => address <= uint.MaxValue - 3u &&
               TryWriteByte(address, (byte)(value >> 24)) &&
               TryWriteByte(address + 1u, (byte)(value >> 16)) &&
               TryWriteByte(address + 2u, (byte)(value >> 8)) &&
               TryWriteByte(address + 3u, (byte)value);

        internal bool Commit()
        {
            if (_writes.Count == 0)
                return true;

            var originals = new Dictionary<uint, byte>(_writes.Count);
            foreach (var address in _writes.Keys)
            {
                if (!_backing.TryReadByte(address, out var original))
                    return false;
                originals[address] = original;
            }

            foreach (var pair in _writes)
            {
                if (_backing.TryWriteByte(pair.Key, pair.Value))
                    continue;

                foreach (var original in originals)
                    _ = _backing.TryWriteByte(original.Key, original.Value);
                _writes.Clear();
                return false;
            }

            _writes.Clear();
            return true;
        }
    }

    private static bool PublishNativeGfxBase(
        IGraphicsMemory memory,
        uint monitorSpec,
        MonitorSelection selection,
        uint graphicsLibraryBase)
    {
        // The compact compatibility image ends before the private GfxBase
        // monitor fields.  Treat that as an intentional host-only path; a
        // full native image will expose the fields and take this branch.
        if (graphicsLibraryBase == 0 ||
            !TryAddress(graphicsLibraryBase, GraphicsLayouts.GfxBaseMonitorListHead, out var listHead) ||
            !memory.TryReadLong(listHead, out var originalHead))
        {
            return true;
        }

        if (!TryAddress(graphicsLibraryBase, GraphicsLayouts.GfxBaseMonitorListTail, out var listTail) ||
            !TryAddress(graphicsLibraryBase, GraphicsLayouts.GfxBaseMonitorListTailPred, out var listTailPred) ||
            !TryAddress(graphicsLibraryBase, GraphicsLayouts.GfxBaseMonitorListType, out var listType) ||
            !TryAddress(graphicsLibraryBase, GraphicsLayouts.GfxBaseMonitorListPad, out var listPad) ||
            !TryAddress(graphicsLibraryBase, GraphicsLayouts.GfxBaseDefaultMonitor, out var defaultMonitor) ||
            !memory.TryReadLong(listTail, out var originalTail) ||
            !memory.TryReadLong(listTailPred, out var originalTailPred) ||
            !memory.TryReadByte(listType, out var originalListType) ||
            !memory.TryReadByte(listPad, out var originalListPad) ||
            !memory.TryReadLong(defaultMonitor, out var originalDefaultMonitor))
        {
            return false;
        }

        // Exec List.lh_Type/lh_Pad are zero for a graphics MonitorList. Do
        // not reinterpret a foreign or partially initialized list as ours;
        // native/provider ownership must retain that malformed envelope.
        if (originalListType != 0 || originalListPad != 0)
            return false;

        var listAddress = graphicsLibraryBase + (uint)GraphicsLayouts.GfxBaseMonitorList;
        var tailSentinel = listAddress + 4u;
        if (originalTail != 0 ||
            (originalHead == 0 && originalTailPred == 0) ||
            (originalHead != tailSentinel && originalTailPred == listAddress) ||
            (originalHead == tailSentinel && originalTailPred != listAddress))
        {
            // A malformed or partially initialized native list is not ours
            // to repair.  Decline the optional publication and let the
            // native/provider monitor owner retain control.
            return false;
        }

        var oldPredecessor = originalTailPred;
        var oldPredecessorSucc = originalHead;
        if (oldPredecessor != listAddress &&
            (!TryAddress(oldPredecessor, 0, out var oldPredecessorAddress) ||
             !memory.TryReadLong(oldPredecessorAddress, out oldPredecessorSucc)))
        {
            return false;
        }

        var nodeSucc = tailSentinel;
        var nodePred = oldPredecessor;
        var writes = new List<(uint Address, uint Value)>(6)
        {
            (monitorSpec, nodeSucc),
            (monitorSpec + 4u, nodePred),
            (oldPredecessor, monitorSpec),
            (listTailPred, monitorSpec)
        };
        if (oldPredecessor == listAddress)
            writes.Add((listHead, monitorSpec));

        if (selection.IsDefault && originalDefaultMonitor == 0)
            writes.Add((defaultMonitor, monitorSpec));

        // Capture every public LONG before the first link publication.  The
        // native-list handoff can be rejected after an adapter has committed
        // only the leading bytes of a LONG; a write-first rollback must cover
        // the currently failing field as well as earlier links.
        var originals = new List<(uint Address, byte[] Original)>(writes.Count);
        var seen = new HashSet<uint>();
        foreach (var write in writes)
        {
            if (!seen.Add(write.Address))
                continue;

            if (!TrySnapshot(
                    memory,
                    write.Address,
                    sizeof(uint),
                    out var original))
            {
                return false;
            }

            originals.Add((write.Address, original));
        }

        foreach (var write in writes)
        {
            if (!memory.TryWriteLong(write.Address, write.Value))
            {
                foreach (var rollback in originals)
                    Restore(memory, rollback.Address, rollback.Original);

                return false;
            }
        }

        return true;
    }

    private static bool TryAddress(uint baseAddress, int offset, out uint address)
    {
        if (offset < 0 || baseAddress > uint.MaxValue - (uint)offset)
        {
            address = 0;
            return false;
        }

        address = baseAddress + (uint)offset;
        return true;
    }

    private static bool TrySnapshot(
        IGraphicsMemory memory,
        uint address,
        int byteCount,
        out byte[] original)
    {
        original = Array.Empty<byte>();
        if (address == 0 || (address & 1u) != 0 || byteCount <= 0 ||
            address > uint.MaxValue - (uint)(byteCount - 1))
        {
            return false;
        }

        original = new byte[byteCount];
        for (var offset = 0; offset < byteCount; offset++)
        {
            if (!memory.TryReadByte(address + (uint)offset, out original[offset]))
            {
                original = Array.Empty<byte>();
                return false;
            }
        }

        return true;
    }

    private static void Restore(
        IGraphicsMemory memory,
        uint address,
        IReadOnlyList<byte> original)
    {
        for (var offset = original.Count - 1; offset >= 0; offset--)
            _ = memory.TryWriteByte(address + (uint)offset, original[offset]);
    }

    private static bool TryReadCString(
        IGraphicsMemory memory,
        uint address,
        out string value)
    {
        var bytes = new List<byte>(64);
        for (var index = 0; index < 64; index++)
        {
            if (address > uint.MaxValue - (uint)index ||
                !memory.TryReadByte(address + (uint)index, out var current))
            {
                value = string.Empty;
                return false;
            }

            if (current == 0)
            {
                value = Encoding.ASCII.GetString(bytes.ToArray());
                return true;
            }

            bytes.Add(current);
        }

        value = string.Empty;
        return false;
    }

}
