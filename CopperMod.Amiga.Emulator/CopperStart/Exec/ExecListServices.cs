using System;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>Guest-owned Exec List and Node primitives shared by CopperStart services.</summary>
internal sealed class ExecListServices
{
    private const int NodeSuccessorOffset = 0x00;
    private const int NodePredecessorOffset = 0x04;
    private const int NodeNameOffset = 0x0A;

    private readonly AmigaBus _bus;
    private readonly Func<uint, int, string> _readString;

    public ExecListServices(AmigaBus bus, Func<uint, int, string> readString)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _readString = readString ?? throw new ArgumentNullException(nameof(readString));
    }

    public bool IsValidList(uint list) => list != 0 && _bus.IsMappedMemoryRange(list, 14);
    public bool IsValidNode(uint node) => node != 0 && _bus.IsMappedMemoryRange(node, 8);

    public void Initialize(uint list)
    {
        var tail = list + 4;
        _bus.WriteLong(list, tail);
        _bus.WriteLong(tail, 0);
        _bus.WriteLong(list + 8, list);
    }

    public void Ensure(uint list)
    {
        if (!IsValidList(list) || _bus.ReadLong(list + 4) != 0 || _bus.ReadLong(list) == 0)
            Initialize(list);
    }

    public bool Contains(uint list, uint node)
    {
        if (!IsValidList(list)) return false;
        var tail = list + 4;
        for (var current = _bus.ReadLong(list); current != tail && IsValidNode(current); current = _bus.ReadLong(current))
            if (current == node) return true;
        return false;
    }

    public uint FindName(uint list, uint nameAddress)
    {
        if (!IsValidList(list) || nameAddress == 0) return 0;
        var target = _readString(nameAddress, 96);
        var tail = list + 4;
        for (var node = _bus.ReadLong(list); node != tail && IsValidNode(node); node = _bus.ReadLong(node))
        {
            var name = _bus.ReadLong(node + NodeNameOffset);
            if (string.Equals(target, _readString(name, 96), StringComparison.OrdinalIgnoreCase)) return node;
        }

        return 0;
    }

    public void AddTail(uint list, uint node)
    {
        var tail = list + 4;
        var predecessor = _bus.ReadLong(list + 8);
        if (predecessor == 0 || !_bus.IsMappedMemoryRange(predecessor, 8))
        {
            Initialize(list);
            predecessor = list;
        }

        _bus.WriteLong(node + NodeSuccessorOffset, tail);
        _bus.WriteLong(node + NodePredecessorOffset, predecessor);
        _bus.WriteLong(predecessor + NodeSuccessorOffset, node);
        _bus.WriteLong(list + 8, node);
    }

    public void Link(uint node, uint predecessor, uint successor)
    {
        _bus.WriteLong(node + NodePredecessorOffset, predecessor);
        _bus.WriteLong(node + NodeSuccessorOffset, successor);
        _bus.WriteLong(predecessor + NodeSuccessorOffset, node);
        _bus.WriteLong(successor + NodePredecessorOffset, node);
    }

    public uint Remove(uint node)
    {
        if (!IsValidNode(node)) return 0;
        var predecessor = _bus.ReadLong(node + NodePredecessorOffset);
        var successor = _bus.ReadLong(node + NodeSuccessorOffset);
        if (!IsValidNode(predecessor) || !IsValidNode(successor)) return 0;
        _bus.WriteLong(predecessor + NodeSuccessorOffset, successor);
        _bus.WriteLong(successor + NodePredecessorOffset, predecessor);
        if (successor >= 4 && IsValidList(successor - 4)) _bus.WriteLong(successor + 4, predecessor);
        _bus.WriteLong(node, 0);
        _bus.WriteLong(node + 4, 0);
        return node;
    }

    public uint RemoveEnd(uint list, bool head)
    {
        if (!IsValidList(list)) return 0;
        var node = head ? _bus.ReadLong(list) : _bus.ReadLong(list + 8);
        return node == list || node == list + 4 ? 0 : Remove(node);
    }
}
