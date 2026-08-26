using System;
using System.Collections.Generic;
using Copper68k;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>Lookup and unique insertion over the public Exec node registries.</summary>
internal sealed class ExecRegistryServices
{
    private const int MemListOffset = 0x142;
    private const int ResourceListOffset = 0x150;
    private const int DeviceListOffset = 0x15E;
    private const int LibraryListOffset = 0x17A;
    private const int PortListOffset = 0x188;
    private const int TaskReadyOffset = 0x196;
    private const int TaskWaitOffset = 0x1A4;
    // Eight 14-byte system lists, followed by five 16-byte soft-interrupt
    // headers, LastAlert[4], and the two frequency bytes.
    private const int SemaphoreListOffset = 0x214;

    // MorphOS ExecBase list IDs, deliberately not ln_Type values.
    private const uint ExecListDevice = 0;
    private const uint ExecListLibrary = 2;
    private const uint ExecListMemHeader = 4;
    private const uint ExecListPort = 5;
    private const uint ExecListResource = 6;
    private const uint ExecListSemaphore = 7;
    private const uint ExecListTask = 8;

    private const byte NodeTypeTask = 1;
    private const byte NodeTypeDevice = 3;
    private const byte NodeTypeMsgPort = 4;
    private const byte NodeTypeResource = 8;
    private const byte NodeTypeLibrary = 9;
    private const byte NodeTypeMemory = 10;
    private const byte NodeTypeProcess = 13;
    private const byte NodeTypeSignalSemaphore = 15;

    private const uint TagDone = 0;
    private const uint TagIgnore = 1;
    private const uint TagMore = 2;
    private const uint TagSkip = 3;
    private const uint SalType = 0x8000_03E9;
    private const uint SalPriority = 0x8000_03EA;
    private const uint SalName = 0x8000_03EB;
    private const uint MemfPublicClear = 0x0001_0001;
    private const int NodeTypeOffset = 8;
    private const int NodePriorityOffset = 9;
    private const int NodeNameOffset = 10;
    private const int NodeBytes = 14;
    private const int MsgPortBytes = 34;
    private const int MsgPortSignalBitOffset = 0x0F;
    private const int MsgPortSignalTaskOffset = 0x10;
    private const int MsgPortMessageListOffset = 0x14;
    private const int SignalSemaphoreBytes = 48;

    private readonly CopperStartExecContext _context;
    private readonly ExecListServices _lists;
    private readonly ExecSignalServices _signals;
    private readonly ExecSemaphoreServices _semaphores;

    public ExecRegistryServices(
        CopperStartExecContext context,
        ExecListServices lists,
        ExecSignalServices signals,
        ExecSemaphoreServices semaphores)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _lists = lists ?? throw new ArgumentNullException(nameof(lists));
        _signals = signals ?? throw new ArgumentNullException(nameof(signals));
        _semaphores = semaphores ?? throw new ArgumentNullException(nameof(semaphores));
    }

    /// <summary>FindExecNode(type=D0, name=A0), using MorphOS EXECLIST_* IDs.</summary>
    public uint FindExecNode(M68kCpuState state)
    {
        var execBase = _context.GetExecBase();
        if (execBase == 0 || state.A[0] == 0) return 0;
        if (state.D[0] == ExecListTask)
        {
            var current = _context.GetCurrentTask();
            if (NameEquals(current, state.A[0])) return current;
            return _lists.FindName(execBase + TaskReadyOffset, state.A[0]) is var ready and not 0
                ? ready
                : _lists.FindName(execBase + TaskWaitOffset, state.A[0]);
        }

        return TryGetList(state.D[0], out var list) ? _lists.FindName(execBase + (uint)list, state.A[0]) : 0;
    }

    /// <summary>
    /// MorphOS V50 AddExecNodeA(innode=A0, tags=A1). It is a host extension:
    /// only the documented allocatable node classes (ports and semaphores) are
    /// constructed here; all other nodes must be supplied by the caller.
    /// </summary>
    public uint AddExecNodeA(M68kCpuState state)
    {
        var supplied = state.A[0];
        if (supplied != 0 && !_context.Memory.IsMapped(supplied, NodeBytes)) return 0;

        var type = supplied == 0 ? (byte)0 : _context.Memory.ReadByte(supplied + NodeTypeOffset);
        var priority = supplied == 0 ? (sbyte)0 : unchecked((sbyte)_context.Memory.ReadByte(supplied + NodePriorityOffset));
        var name = supplied == 0 ? 0u : _context.Memory.ReadLong(supplied + NodeNameOffset);
        if (TryGetTag(state.A[1], SalType, out var tagType)) type = (byte)tagType;
        if (TryGetTag(state.A[1], SalPriority, out var tagPriority)) priority = unchecked((sbyte)tagPriority);
        if (TryGetTag(state.A[1], SalName, out var tagName)) name = tagName;
        if (name == 0 || !_context.Memory.IsMapped(name, 1) || !TryGetListForNodeType(type, out var listOffset)) return 0;

        var execBase = _context.GetExecBase();
        if (execBase == 0 || !_lists.IsValidList(execBase + (uint)listOffset) || _lists.FindName(execBase + (uint)listOffset, name) != 0)
            return 0;

        var node = supplied;
        if (node == 0)
        {
            node = CreateNode(type, name, priority);
            if (node == 0) return 0;
        }
        else
        {
            _context.Memory.WriteByte(node + NodeTypeOffset, type);
            _context.Memory.WriteByte(node + NodePriorityOffset, unchecked((byte)priority));
            _context.Memory.WriteLong(node + NodeNameOffset, name);
            if (type == NodeTypeMsgPort) _lists.Initialize(node + MsgPortMessageListOffset);
        }

        _lists.Enqueue(execBase + (uint)listOffset, node);
        return node;
    }

    private uint CreateNode(byte type, uint name, sbyte priority)
    {
        return type switch
        {
            NodeTypeMsgPort => CreateMessagePort(name, priority),
            NodeTypeSignalSemaphore => CreateSignalSemaphore(name, priority),
            _ => 0
        };
    }

    private uint CreateMessagePort(uint name, sbyte priority)
    {
        var port = _context.MemoryOperations.Allocate(MsgPortBytes, MemfPublicClear);
        if (port == 0) return 0;
        var allocateSignal = new M68kCpuState();
        allocateSignal.D[0] = uint.MaxValue;
        var signalBit = _signals.AllocSignal(allocateSignal);
        if (signalBit >= 32)
        {
            _context.MemoryOperations.Free(port, MsgPortBytes);
            return 0;
        }

        InitializeNode(port, NodeTypeMsgPort, name, priority);
        _context.Memory.WriteByte(port + MsgPortSignalBitOffset, (byte)signalBit);
        _context.Memory.WriteLong(port + MsgPortSignalTaskOffset, _context.GetCurrentTask());
        _lists.Initialize(port + MsgPortMessageListOffset);
        return port;
    }

    private uint CreateSignalSemaphore(uint name, sbyte priority)
    {
        var semaphore = _context.MemoryOperations.Allocate(SignalSemaphoreBytes, MemfPublicClear);
        if (semaphore == 0) return 0;
        InitializeNode(semaphore, NodeTypeSignalSemaphore, name, priority);
        var initialize = new M68kCpuState();
        initialize.A[0] = semaphore;
        _semaphores.InitSemaphore(initialize);
        return semaphore;
    }

    private void InitializeNode(uint node, byte type, uint name, sbyte priority)
    {
        _context.Memory.WriteByte(node + NodeTypeOffset, type);
        _context.Memory.WriteByte(node + NodePriorityOffset, unchecked((byte)priority));
        _context.Memory.WriteLong(node + NodeNameOffset, name);
    }

    private bool TryGetList(uint listId, out int offset)
    {
        offset = listId switch
        {
            ExecListDevice => DeviceListOffset,
            ExecListLibrary => LibraryListOffset,
            ExecListMemHeader => MemListOffset,
            ExecListPort => PortListOffset,
            ExecListResource => ResourceListOffset,
            ExecListSemaphore => SemaphoreListOffset,
            _ => -1
        };
        return offset >= 0;
    }

    private static bool TryGetListForNodeType(byte type, out int offset)
    {
        offset = type switch
        {
            NodeTypeDevice => DeviceListOffset,
            NodeTypeLibrary => LibraryListOffset,
            NodeTypeMemory => MemListOffset,
            NodeTypeMsgPort => PortListOffset,
            NodeTypeResource => ResourceListOffset,
            NodeTypeSignalSemaphore => SemaphoreListOffset,
            _ => -1
        };
        return offset >= 0;
    }

    private bool NameEquals(uint node, uint name)
        => node != 0 && _context.Memory.IsMapped(node + NodeNameOffset, 4) &&
           string.Equals(_context.ReadString(_context.Memory.ReadLong(node + NodeNameOffset), 96), _context.ReadString(name, 96), StringComparison.OrdinalIgnoreCase);

    private bool TryGetTag(uint tags, uint wanted, out uint value)
    {
        value = 0;
        var cursor = tags;
        var visited = new HashSet<uint>();
        for (var steps = 0; cursor != 0 && steps < 4096; steps++)
        {
            if (!_context.Memory.IsMapped(cursor, 8) || !visited.Add(cursor)) return false;
            var tag = _context.Memory.ReadLong(cursor);
            var data = _context.Memory.ReadLong(cursor + 4);
            switch (tag)
            {
                case TagDone: return false;
                case TagIgnore: cursor += 8; continue;
                case TagMore: cursor = data; continue;
                case TagSkip:
                    if (data > 4095 || cursor > uint.MaxValue - ((data + 1) * 8)) return false;
                    cursor += (data + 1) * 8;
                    continue;
                default:
                    if (tag == wanted) { value = data; return true; }
                    cursor += 8;
                    continue;
            }
        }
        return false;
    }
}
