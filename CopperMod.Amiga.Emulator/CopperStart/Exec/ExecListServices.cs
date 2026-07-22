using System;
using Copper68k;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>Guest-owned Exec List and Node primitives shared by CopperStart services.</summary>
internal sealed class ExecListServices
{
    private const int NodeSuccessorOffset = 0x00;
    private const int NodePredecessorOffset = 0x04;
    private const int NodeNameOffset = 0x0A;

    private readonly HostGuestMemory _memory;
    private readonly Func<uint, int, string> _readString;

    public ExecListServices(HostGuestMemory memory, Func<uint, int, string> readString)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _readString = readString ?? throw new ArgumentNullException(nameof(readString));
    }

    public bool IsValidList(uint list) => list != 0 && _memory.IsMapped(list, 14);
    public bool IsValidNode(uint node) => node != 0 && _memory.IsMapped(node, 8);

    public void Initialize(uint list)
    {
        var tail = list + 4;
        _memory.WriteLong(list, tail);
        _memory.WriteLong(tail, 0);
        _memory.WriteLong(list + 8, list);
    }

    public void Ensure(uint list)
    {
        if (!IsValidList(list) || _memory.ReadLong(list + 4) != 0 || _memory.ReadLong(list) == 0)
            Initialize(list);
    }

    public bool Contains(uint list, uint node)
    {
        if (!IsValidList(list)) return false;
        var tail = list + 4;
        for (var current = _memory.ReadLong(list); current != tail && IsValidNode(current); current = _memory.ReadLong(current))
            if (current == node) return true;
        return false;
    }

    public uint FindName(uint list, uint nameAddress)
    {
        if (!IsValidList(list) || nameAddress == 0) return 0;
        var target = _readString(nameAddress, 96);
        var tail = list + 4;
        for (var node = _memory.ReadLong(list); node != tail && IsValidNode(node); node = _memory.ReadLong(node))
        {
            var name = _memory.ReadLong(node + NodeNameOffset);
            if (string.Equals(target, _readString(name, 96), StringComparison.OrdinalIgnoreCase)) return node;
        }

        return 0;
    }

    public void AddTail(uint list, uint node)
    {
        var tail = list + 4;
        var predecessor = _memory.ReadLong(list + 8);
        if (predecessor == 0 || !_memory.IsMapped(predecessor, 8))
        {
            Initialize(list);
            predecessor = list;
        }

        _memory.WriteLong(node + NodeSuccessorOffset, tail);
        _memory.WriteLong(node + NodePredecessorOffset, predecessor);
        _memory.WriteLong(predecessor + NodeSuccessorOffset, node);
        _memory.WriteLong(list + 8, node);
    }

    public void Link(uint node, uint predecessor, uint successor)
    {
        _memory.WriteLong(node + NodePredecessorOffset, predecessor);
        _memory.WriteLong(node + NodeSuccessorOffset, successor);
        _memory.WriteLong(predecessor + NodeSuccessorOffset, node);
        _memory.WriteLong(successor + NodePredecessorOffset, node);
    }

    public uint Remove(uint node)
    {
        if (!IsValidNode(node)) return 0;
        var predecessor = _memory.ReadLong(node + NodePredecessorOffset);
        var successor = _memory.ReadLong(node + NodeSuccessorOffset);
        if (!IsValidNode(predecessor) || !IsValidNode(successor)) return 0;
        _memory.WriteLong(predecessor + NodeSuccessorOffset, successor);
        _memory.WriteLong(successor + NodePredecessorOffset, predecessor);
        if (successor >= 4 && IsValidList(successor - 4)) _memory.WriteLong(successor + 4, predecessor);
        _memory.WriteLong(node, 0);
        _memory.WriteLong(node + 4, 0);
        return node;
    }

    public uint RemoveEnd(uint list, bool head)
    {
        if (!IsValidList(list)) return 0;
        var node = head ? _memory.ReadLong(list) : _memory.ReadLong(list + 8);
        return node == list || node == list + 4 ? 0 : Remove(node);
    }

    public uint Insert(M68kCpuState state)
    {
        var list = state.A[0]; var node = state.A[1]; var predecessor = state.A[2] == 0 ? list : state.A[2];
        if (!IsValidList(list) || !IsValidNode(node) || !IsValidNode(predecessor)) return 0;
        var successor = _memory.ReadLong(predecessor); if (!IsValidNode(successor)) return 0;
        Link(node, predecessor, successor); if (successor == list + 4) _memory.WriteLong(list + 8, node); return 0;
    }
    public uint AddHead(M68kCpuState state)
    {
        var list = state.A[0]; var node = state.A[1]; if (!IsValidList(list) || !IsValidNode(node)) return 0;
        var successor = _memory.ReadLong(list); if (!IsValidNode(successor)) return 0;
        Link(node, list, successor); if (successor == list + 4) _memory.WriteLong(list + 8, node); return 0;
    }
    public uint AddTail(M68kCpuState state) { if (IsValidList(state.A[0]) && IsValidNode(state.A[1])) AddTail(state.A[0], state.A[1]); return 0; }
    public uint Remove(M68kCpuState state) => Remove(state.A[1]);
    public uint RemHead(M68kCpuState state) => RemoveEnd(state.A[0], true);
    public uint RemTail(M68kCpuState state) => RemoveEnd(state.A[0], false);
    public uint Enqueue(M68kCpuState state)
    {
        var list = state.A[0]; var node = state.A[1]; if (!IsValidList(list) || !IsValidNode(node)) return 0;
        var priority = unchecked((sbyte)_memory.ReadByte(node + 9)); var current = _memory.ReadLong(list);
        while (current != list + 4 && IsValidNode(current) && unchecked((sbyte)_memory.ReadByte(current + 9)) >= priority) current = _memory.ReadLong(current);
        var predecessor = _memory.ReadLong(current + NodePredecessorOffset); if (!IsValidNode(predecessor)) return 0;
        Link(node, predecessor, current); if (current == list + 4) _memory.WriteLong(list + 8, node); return 0;
    }
}
