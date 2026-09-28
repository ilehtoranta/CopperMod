using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Guest-owned extended graphics nodes used to associate View/ViewPort state.
/// The registry owns only nodes returned by GfxNew; arbitrary guest pointers
/// are never interpreted as host objects.  Driver-private fields remain zero
/// until a native copper/display backend claims them.
/// </summary>
internal static class GraphicsExtendedNodeOperations
{
    internal const uint ViewExtraType = 1;
    internal const uint ViewPortExtraType = 2;
    internal const uint SpecialMonitorType = 3;
    internal const uint MonitorSpecType = 4;

    private const byte NtGraphics = 18;
    private const byte SsGraphics = 2;
    internal readonly record struct NodeRecord(
        uint ByteCount,
        int AssociationOffset,
        uint NodeType);

    internal sealed class Registry
    {
        private readonly Dictionary<uint, NodeRecord> _nodes = new();
        private readonly Dictionary<uint, uint> _associations = new();
        private uint _graphicsLibraryBase;

        internal Registry(uint graphicsLibraryBase = 0)
            => _graphicsLibraryBase = graphicsLibraryBase;

        internal void RebindGraphicsLibraryBase(uint graphicsLibraryBase)
            => _graphicsLibraryBase = graphicsLibraryBase;

        /// <summary>
        /// Rebinds the shared <c>xln_Lib</c> backlink for every extended node
        /// already published by this graphics instance.  A native
        /// graphics.library can be discovered after host-side GfxNew calls
        /// have created ViewExtra/ViewPortExtra nodes, so changing only the
        /// registry default would leave those guest nodes pointing at the
        /// compatibility image.  Stage and validate every backlink first and
        /// roll back earlier writes if a later guest publication is refused.
        /// </summary>
        internal bool TryRebindGraphicsLibraryBase(
            IGraphicsMemory memory,
            uint graphicsLibraryBase)
        {
            if (graphicsLibraryBase == _graphicsLibraryBase)
                return true;

            var originals = new List<(uint Address, byte[] Original)>(_nodes.Count);
            foreach (var node in _nodes.Keys)
            {
                if (node > uint.MaxValue -
                    (uint)(GraphicsLayouts.ExtendedNodeLibrary + sizeof(uint) - 1) ||
                    !TrySnapshot(
                        memory,
                        node + (uint)GraphicsLayouts.ExtendedNodeLibrary,
                        sizeof(uint),
                        out var original))
                {
                    return false;
                }

                originals.Add((
                    node + (uint)GraphicsLayouts.ExtendedNodeLibrary,
                    original));
            }

            var applied = 0;
            for (; applied < originals.Count; applied++)
            {
                if (memory.TryWriteLong(originals[applied].Address, graphicsLibraryBase))
                    continue;

                // Include the currently failing field: a provider may have
                // published its first one or two bytes before rejecting the
                // LONG operation.  Restore every staged backlink byte-wise so
                // the cutover remains retryable even when subsequent LONG
                // writes are refused by the same adapter.
                foreach (var original in originals)
                    Restore(memory, original.Address, original.Original);

                return false;
            }

            _graphicsLibraryBase = graphicsLibraryBase;
            return true;
        }

        internal sealed class Snapshot
        {
            internal Snapshot(
                Dictionary<uint, NodeRecord> nodes,
                Dictionary<uint, uint> associations)
            {
                Nodes = nodes;
                Associations = associations;
            }

            internal Dictionary<uint, NodeRecord> Nodes { get; }
            internal Dictionary<uint, uint> Associations { get; }
        }

        internal Snapshot CaptureState()
            => new(
                new Dictionary<uint, NodeRecord>(_nodes),
                new Dictionary<uint, uint>(_associations));

        internal void RestoreState(Snapshot snapshot)
        {
            _nodes.Clear();
            foreach (var pair in snapshot.Nodes)
                _nodes[pair.Key] = pair.Value;

            _associations.Clear();
            foreach (var pair in snapshot.Associations)
                _associations[pair.Key] = pair.Value;
        }

        internal uint New(
            IGraphicsMemory memory,
            IGraphicsAllocatorBackend allocator,
            uint nodeType)
        {
            uint address = 0;
            if (!TryGetLayout(nodeType, out var byteCount, out var associationOffset) ||
                !allocator.TryAllocate(byteCount, GraphicsMemoryClass.Public, out address) ||
                address == 0 ||
                (address & 1u) != 0 ||
                byteCount == 0 ||
                address > uint.MaxValue - (byteCount - 1u) ||
                _nodes.ContainsKey(address))
            {
                if (address != 0)
                    allocator.Free(address, byteCount, GraphicsMemoryClass.Public);
                return 0;
            }

            // GfxNew owns a public guest envelope, but the allocator may
            // expose an already-backed span whose bytes remain observable
            // after a failed publication.  Snapshot before clearing so a
            // late metadata write failure cannot leak a half-initialized
            // extended node to the native/provider owner or to a retry.
            if (!TrySnapshot(memory, address, byteCount, out var original) ||
                !Clear(memory, address, byteCount) ||
                !memory.TryWriteByte(address + (uint)GraphicsLayouts.ExtendedNodeType, NtGraphics) ||
                !memory.TryWriteByte(address + (uint)GraphicsLayouts.ExtendedNodeSubsystem, SsGraphics) ||
                !memory.TryWriteByte(
                    address + (uint)GraphicsLayouts.ExtendedNodeSubtype,
                    (byte)nodeType) ||
                !memory.TryWriteLong(
                    address + (uint)GraphicsLayouts.ExtendedNodeLibrary,
                    _graphicsLibraryBase))
            {
                Restore(memory, address, original);
                allocator.Free(address, byteCount, GraphicsMemoryClass.Public);
                return 0;
            }

            _nodes.Add(address, new NodeRecord(byteCount, associationOffset, nodeType));
            return address;
        }

        internal bool Free(
            IGraphicsMemory memory,
            IGraphicsAllocatorBackend allocator,
            uint node)
        {
            if (node == 0 || !_nodes.TryGetValue(node, out var record))
                return false;

            // Native GfxFree admits only graphics extended nodes.  Re-read
            // the guest metadata before touching the association backlink so
            // a caller-corrupted owned envelope remains available to the
            // native/provider owner instead of being freed by this registry.
            if (node > uint.MaxValue - (uint)GraphicsLayouts.ExtendedNodeSubtype ||
                !memory.TryReadByte(
                    node + (uint)GraphicsLayouts.ExtendedNodeType,
                    out var nodeType) ||
                !memory.TryReadByte(
                    node + (uint)GraphicsLayouts.ExtendedNodeSubsystem,
                    out var nodeSubsystem) ||
                !memory.TryReadByte(
                    node + (uint)GraphicsLayouts.ExtendedNodeSubtype,
                    out var nodeSubtype) ||
                nodeType != NtGraphics ||
                nodeSubsystem != SsGraphics ||
                nodeSubtype != record.NodeType)
            {
                return false;
            }

            // GfxFree must be fail-closed around the guest association field.
            // Removing the registry record before this read would leave a
            // pointer-to-node hash entry targeting an allocation that is about
            // to be released when the guest envelope is malformed or unreadable.
            uint associated = 0;
            if (record.AssociationOffset >= 0)
            {
                if (node > uint.MaxValue - (uint)record.AssociationOffset ||
                    !TrySnapshot(
                        memory,
                        node + (uint)record.AssociationOffset,
                        sizeof(uint),
                        out var associationBytes) ||
                    !memory.TryReadLong(
                        node + (uint)record.AssociationOffset,
                        out associated))
                {
                    return false;
                }

                // GfxFree disassociates the node from its guest owner before
                // releasing the allocation.  Keep the guest-visible backlink
                // in sync with the registry; if the write cannot be made,
                // leave both the allocation and lookup entry intact.
                if (!memory.TryWriteLong(
                        node + (uint)record.AssociationOffset,
                        0))
                {
                    Restore(
                        memory,
                        node + (uint)record.AssociationOffset,
                        associationBytes);
                    return false;
                }
            }

            _nodes.Remove(node);

            if (associated != 0 &&
                _associations.TryGetValue(associated, out var owner) &&
                owner == node)
            {
                _associations.Remove(associated);
            }

            // The guest backlink is public state and may have been edited by
            // the caller after association.  Remove every lookup entry that
            // still targets this node, regardless of whether the concrete
            // envelope exposes a backlink, so GfxLookUp cannot return a freed
            // node even when the guest field no longer names its owner.
            {
                var stalePointers = new List<uint>();
                foreach (var pair in _associations)
                {
                    if (pair.Value == node)
                        stalePointers.Add(pair.Key);
                }

                foreach (var pointer in stalePointers)
                    _associations.Remove(pointer);
            }

            allocator.Free(node, record.ByteCount, GraphicsMemoryClass.Public);
            return true;
        }

        internal bool Associate(
            IGraphicsMemory memory,
            uint pointer,
            uint node)
        {
            if (pointer == 0 ||
                (pointer & 1u) != 0 ||
                !_nodes.TryGetValue(node, out var record))
            {
                return false;
            }

            var hasBacklink = record.AssociationOffset >= 0;
            var nodeAssociation = 0u;
            var oldPointer = 0u;
            var originalNodeAssociation = Array.Empty<byte>();
            if (hasBacklink)
            {
                if (node > uint.MaxValue - (uint)record.AssociationOffset)
                    return false;

                nodeAssociation = node + (uint)record.AssociationOffset;
                if (!TrySnapshot(
                        memory,
                        nodeAssociation,
                        sizeof(uint),
                        out originalNodeAssociation) ||
                    !memory.TryReadLong(nodeAssociation, out oldPointer))
                    return false;
            }

            var previousNode = 0u;
            var previousAssociation = 0u;
            var originalPreviousAssociation = Array.Empty<byte>();
            var hasPreviousNode = _associations.TryGetValue(pointer, out previousNode);
            if (hasPreviousNode)
            {
                if (previousNode == node)
                {
                    hasPreviousNode = false;
                }
                else if (!_nodes.TryGetValue(previousNode, out var previousRecord))
                {
                    return false;
                }
                else if (previousRecord.AssociationOffset >= 0)
                {
                    if (previousNode > uint.MaxValue -
                            (uint)previousRecord.AssociationOffset)
                    {
                        return false;
                    }

                    previousAssociation = previousNode + (uint)previousRecord.AssociationOffset;
                    if (!TrySnapshot(
                            memory,
                            previousAssociation,
                            sizeof(uint),
                            out originalPreviousAssociation) ||
                        !memory.TryReadLong(previousAssociation, out var previousPointer) ||
                        previousPointer != pointer)
                    {
                        return false;
                    }
                }
            }

            // Publish the new node first.  If clearing the old node fails,
            // restore this write so both guest structures and the registry
            // retain the original association.
            if (hasBacklink && !memory.TryWriteLong(nodeAssociation, pointer))
            {
                Restore(memory, nodeAssociation, originalNodeAssociation);
                return false;
            }

            if (hasPreviousNode &&
                _nodes[previousNode].AssociationOffset >= 0 &&
                !memory.TryWriteLong(previousAssociation, 0))
            {
                Restore(memory, previousAssociation, originalPreviousAssociation);
                if (hasBacklink)
                    Restore(memory, nodeAssociation, originalNodeAssociation);
                return false;
            }

            if (oldPointer != 0 &&
                _associations.TryGetValue(oldPointer, out var oldOwner) &&
                oldOwner == node)
            {
                _associations.Remove(oldPointer);
            }

            if (hasPreviousNode &&
                _associations.TryGetValue(pointer, out var previousOwner) &&
                previousOwner == previousNode)
            {
                _associations.Remove(pointer);
            }

            // Nodes without a public backlink (SpecialMonitor and
            // MonitorSpec) can be re-associated as well.  Drop any older
            // opaque lookup entries for this node so one node never remains
            // reachable through multiple stale pointers.
            var staleNodePointers = new List<uint>();
            foreach (var pair in _associations)
            {
                if (pair.Value == node && pair.Key != pointer)
                    staleNodePointers.Add(pair.Key);
            }

            foreach (var stalePointer in staleNodePointers)
                _associations.Remove(stalePointer);

            _associations[pointer] = node;
            return true;
        }

        /// <summary>
        /// Describes the guest LONG writes performed by <see cref="Associate"/>
        /// for native-overlay admission.  The first span is the new node's
        /// backlink, when its node type exposes one; the second is the
        /// displaced node's backlink, when a pointer already owns another
        /// node.  All metadata and current backlink values are revalidated
        /// without mutating the registry or guest memory.
        /// </summary>
        internal bool TryGetAssociatePublicationSpans(
            IGraphicsMemory memory,
            uint pointer,
            uint node,
            out uint first,
            out uint second,
            out int count)
        {
            first = 0;
            second = 0;
            count = 0;
            if (pointer == 0 ||
                (pointer & 1u) != 0 ||
                !_nodes.TryGetValue(node, out var record))
            {
                return false;
            }

            if (!TryValidateMetadata(memory, node, record))
                return false;

            if (record.AssociationOffset >= 0)
            {
                if (node > uint.MaxValue - (uint)record.AssociationOffset)
                    return false;

                first = node + (uint)record.AssociationOffset;
                if (!TrySnapshot(memory, first, sizeof(uint), out _) ||
                    !memory.TryReadLong(first, out _))
                {
                    return false;
                }

                count = 1;
            }

            if (!_associations.TryGetValue(pointer, out var previousNode) ||
                previousNode == node)
            {
                return true;
            }

            if (!_nodes.TryGetValue(previousNode, out var previousRecord) ||
                !TryValidateMetadata(memory, previousNode, previousRecord))
            {
                return false;
            }

            if (previousRecord.AssociationOffset < 0)
                return true;

            if (previousNode > uint.MaxValue -
                    (uint)previousRecord.AssociationOffset)
            {
                return false;
            }

            second = previousNode + (uint)previousRecord.AssociationOffset;
            if (!TrySnapshot(memory, second, sizeof(uint), out _) ||
                !memory.TryReadLong(second, out var previousPointer) ||
                previousPointer != pointer)
            {
                return false;
            }

            count = 2;
            return true;
        }

        private static bool TryValidateMetadata(
            IGraphicsMemory memory,
            uint node,
            NodeRecord record)
        {
            return node <= uint.MaxValue -
                       (uint)GraphicsLayouts.ExtendedNodeSubtype &&
                   memory.TryReadByte(
                       node + (uint)GraphicsLayouts.ExtendedNodeType,
                       out var nodeType) &&
                   memory.TryReadByte(
                       node + (uint)GraphicsLayouts.ExtendedNodeSubsystem,
                       out var nodeSubsystem) &&
                   memory.TryReadByte(
                       node + (uint)GraphicsLayouts.ExtendedNodeSubtype,
                       out var nodeSubtype) &&
                   nodeType == NtGraphics &&
                   nodeSubsystem == SsGraphics &&
                   nodeSubtype == record.NodeType;
        }

        internal bool AssociateViewPortExtra(
            IGraphicsMemory memory,
            uint viewPort,
            uint node)
            => _nodes.TryGetValue(node, out var record) &&
               record.ByteCount == GraphicsLayouts.ViewPortExtraSize &&
               record.AssociationOffset == GraphicsLayouts.ViewPortExtraViewPort &&
               Associate(memory, viewPort, node);

        internal bool TryGetAssociatedViewExtra(
            uint view,
            out uint node)
        {
            node = LookUp(view);
            return node != 0 &&
                   _nodes.TryGetValue(node, out var record) &&
                   record.ByteCount == GraphicsLayouts.ViewExtraSize &&
                   record.AssociationOffset == GraphicsLayouts.ViewExtraView;
        }

        internal bool IsViewPortExtra(uint node)
            => _nodes.TryGetValue(node, out var record) &&
               record.ByteCount == GraphicsLayouts.ViewPortExtraSize &&
               record.AssociationOffset == GraphicsLayouts.ViewPortExtraViewPort;

        /// <summary>
        /// Describes the guest backlink that GfxFree will clear for an owned
        /// associated node.  Native-overlay teardown uses this read-only
        /// admission probe before a preceding operation clears unrelated
        /// public state.  Revalidate the same metadata and backlink envelope
        /// as <see cref="Free"/> so malformed or sparse nodes remain with
        /// their resident/provider owner instead of being partially claimed.
        /// </summary>
        internal bool TryGetFreePublicationSpan(
            IGraphicsMemory memory,
            uint node,
            out uint backlink)
        {
            backlink = 0;
            if (node == 0 ||
                !_nodes.TryGetValue(node, out var record) ||
                node > uint.MaxValue -
                    (uint)GraphicsLayouts.ExtendedNodeSubtype ||
                !memory.TryReadByte(
                    node + (uint)GraphicsLayouts.ExtendedNodeType,
                    out var nodeType) ||
                !memory.TryReadByte(
                    node + (uint)GraphicsLayouts.ExtendedNodeSubsystem,
                    out var nodeSubsystem) ||
                !memory.TryReadByte(
                    node + (uint)GraphicsLayouts.ExtendedNodeSubtype,
                    out var nodeSubtype) ||
                nodeType != NtGraphics ||
                nodeSubsystem != SsGraphics ||
                nodeSubtype != record.NodeType)
            {
                return false;
            }

            // SpecialMonitor and MonitorSpec have no guest association
            // backlink. Their GfxFree transaction still revalidates the
            // graphics metadata, but has no public LONG to preflight.
            if (record.AssociationOffset < 0)
                return true;

            if (node > uint.MaxValue - (uint)record.AssociationOffset)
                return false;

            backlink = node + (uint)record.AssociationOffset;
            return TrySnapshot(memory, backlink, sizeof(uint), out _) &&
                memory.TryReadLong(backlink, out _);
        }

        internal bool TryGetAssociatedViewPortExtra(
            uint viewPort,
            out uint node)
        {
            node = LookUp(viewPort);
            return node != 0 && IsViewPortExtra(node);
        }

        internal uint LookUp(uint pointer)
            => pointer != 0 &&
               (pointer & 1u) == 0 &&
               _associations.TryGetValue(pointer, out var node)
                ? node
                : 0;

        internal bool IsOwned(uint node)
            => node != 0 && _nodes.ContainsKey(node);
    }

    private static bool TryGetLayout(
        uint nodeType,
        out uint byteCount,
        out int associationOffset)
    {
        switch (nodeType)
        {
            case ViewExtraType:
                byteCount = (uint)GraphicsLayouts.ViewExtraSize;
                associationOffset = GraphicsLayouts.ViewExtraView;
                return true;
            case ViewPortExtraType:
                byteCount = (uint)GraphicsLayouts.ViewPortExtraSize;
                associationOffset = GraphicsLayouts.ViewPortExtraViewPort;
                return true;
            case SpecialMonitorType:
                // SpecialMonitor has no public backwards-link field.  It is
                // still a valid GfxNew allocation, but only ViewExtra and
                // ViewPortExtra participate in the GfxAssociate hash path.
                byteCount = (uint)GraphicsLayouts.SpecialMonitorSize;
                associationOffset = -1;
                return true;
            case MonitorSpecType:
                // MonitorSpec is normally obtained through OpenMonitor; the
                // graphics API nevertheless permits callers to allocate the
                // public extended node directly with GfxNew.
                byteCount = (uint)GraphicsLayouts.MonitorSpecSize;
                associationOffset = -1;
                return true;
            default:
                byteCount = 0;
                associationOffset = -1;
                return false;
        }
    }

    private static bool Clear(
        IGraphicsMemory memory,
        uint address,
        uint byteCount)
    {
        for (var offset = 0u; offset < byteCount; offset++)
        {
            if (address > uint.MaxValue - offset ||
                !memory.TryWriteByte(address + offset, 0))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TrySnapshot(
        IGraphicsMemory memory,
        uint address,
        uint byteCount,
        out byte[] original)
    {
        original = Array.Empty<byte>();
        if (address == 0 || byteCount == 0 ||
            address > uint.MaxValue - (byteCount - 1u) ||
            byteCount > int.MaxValue)
        {
            return false;
        }

        original = new byte[(int)byteCount];
        for (var offset = 0u; offset < byteCount; offset++)
        {
            if (!memory.TryReadByte(address + offset, out original[(int)offset]))
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
        byte[] original)
    {
        for (var offset = original.Length - 1; offset >= 0; offset--)
            _ = memory.TryWriteByte(address + (uint)offset, original[offset]);
    }
}
