using System;
using System.Collections.Generic;
using Copper68k;

namespace CopperMod.Amiga.CopperStart.Utility;

/// <summary>
/// Host implementation of utility.library's V36 TagItem family.  Tag lists
/// remain ordinary guest structures; this service only replaces their pure
/// traversal, filtering, and allocation operations.
/// </summary>
internal sealed class UtilityLibraryServices : IDisposable
{
    private const uint HookContinuationAddress = 0x00F0_8A00;
    private const int LibraryListOffset = 0x17A;
    private const int NodeNameOffset = 0x0A;
    private const uint TagDone = 0;
    private const uint TagIgnore = 1;
    private const uint TagMore = 2;
    private const uint TagSkip = 3;
    private const uint MemfPublicClear = 0x0001_0001;
    private const int TagItemBytes = 8;
    private const int AllocationHeaderBytes = 4;
    private const uint AnoNameSpace = 4000, AnoUserSpace = 4001, AnoPriority = 4002, AnoFlags = 4003;
    private const uint NsfNoDups = 1, NsfCase = 2;
    private readonly CopperStartUtilityContext _context;
    private readonly List<(uint Address, uint Token)> _gateways = new();
    private readonly Dictionary<uint, NamedObjectRecord> _namedObjects = new();
    private readonly List<uint> _rootNameSpace = new();

    public UtilityLibraryServices(CopperStartUtilityContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

    public uint LibraryBase { get; private set; }
    public bool IsInstalled => _gateways.Count != 0;
    internal int GatewayRegistrationCountForTests() => _gateways.Count;

    /// <summary>Installs direct gateways after the genuine ROM links utility.library.</summary>
    public bool TryInstall(uint execBase)
    {
        if (IsInstalled || execBase == 0 || !_context.Memory.IsMapped(execBase + LibraryListOffset, 14)) return IsInstalled;
        var library = FindLibrary(execBase + LibraryListOffset, "utility.library");
        if (library == 0 || library < 96 || !_context.Memory.IsMapped(library - 96, 96)) return false;

        LibraryBase = library;
        Register(-30, FindTagItem);
        Register(-36, GetTagData);
        Register(-42, PackBoolTags);
        Register(-48, NextTagItem);
        Register(-54, FilterTagChanges);
        Register(-60, MapTags);
        Register(-66, AllocateTagItems);
        Register(-72, CloneTagItems);
        Register(-78, FreeTagItems);
        Register(-84, RefreshTagItemClones);
        Register(-90, TagInArray);
        Register(-96, FilterTagItems);
        Register(-102, CallHookPkt);
        Register(-186, ApplyTagChanges);
        Register(-198, SMult64);
        Register(-204, UMult64);
        Register(-210, PackStructureTags);
        Register(-216, UnpackStructureTags);
        Register(-120, Amiga2Date);
        Register(-126, Date2Amiga);
        Register(-132, CheckDate);
        Register(-138, SMult32);
        Register(-144, UMult32);
        Register(-150, SDivMod32);
        Register(-156, UDivMod32);
        Register(-162, Stricmp);
        Register(-168, Strnicmp);
        Register(-174, ToUpper);
        Register(-180, ToLower);
        Register(-270, GetUniqueId);
        Register(-222, AddNamedObject);
        Register(-228, AllocNamedObjectA);
        Register(-234, AttemptRemNamedObject);
        Register(-240, FindNamedObject);
        Register(-246, FreeNamedObject);
        Register(-252, NamedObjectName);
        Register(-258, ReleaseNamedObject);
        Register(-264, RemNamedObject);
        RegisterAddress(HookContinuationAddress, ContinueHook);
        return true;
    }

    public void Reset()
    {
        for (var index = _gateways.Count - 1; index >= 0; index--)
            _context.Bus.RemoveHostGateway(_gateways[index].Address, _gateways[index].Token);
        _gateways.Clear();
        LibraryBase = 0;
        _nextUniqueId = 1;
        _namedObjects.Clear();
        _rootNameSpace.Clear();
    }

    public void Dispose() => Reset();

    private uint _nextUniqueId = 1;

    private void Register(int lvo, Action<M68kCpuState> handler)
    {
        var address = unchecked((uint)((int)LibraryBase + lvo));
        RegisterAddress(address, handler);
    }

    private void RegisterAddress(uint address, Action<M68kCpuState> handler)
        => _gateways.Add((address, _context.Bus.RegisterHostGateway(address, handler)));

    private void FindTagItem(M68kCpuState state) => state.D[0] = FindTag(state.D[0], state.A[0]);

    private void GetTagData(M68kCpuState state)
    {
        var item = FindTag(state.D[0], state.A[0]);
        state.D[0] = item == 0 ? state.D[1] : _context.Memory.ReadLong(item + 4);
    }

    private void NextTagItem(M68kCpuState state)
    {
        var statePointer = state.A[0];
        if (statePointer == 0 || !_context.Memory.IsMapped(statePointer, 4)) { state.D[0] = 0; return; }
        var cursor = _context.Memory.ReadLong(statePointer);
        state.D[0] = TryNext(ref cursor, out var item) ? item : 0;
        _context.Memory.WriteLong(statePointer, cursor);
    }

    private void PackBoolTags(M68kCpuState state)
    {
        var flags = state.D[0];
        foreach (var item in Enumerate(state.A[0]))
        {
            var map = FindTag(_context.Memory.ReadLong(item), state.A[1]);
            if (map == 0) continue;
            var mask = _context.Memory.ReadLong(map + 4);
            if (_context.Memory.ReadLong(item + 4) != 0) flags |= mask; else flags &= ~mask;
        }
        state.D[0] = flags;
    }

    private void FilterTagChanges(M68kCpuState state)
    {
        foreach (var item in Enumerate(state.A[0]))
        {
            var original = FindTag(_context.Memory.ReadLong(item), state.A[1]);
            if (original == 0) continue;
            if (_context.Memory.ReadLong(item + 4) == _context.Memory.ReadLong(original + 4))
                _context.Memory.WriteLong(item, TagIgnore);
            else if (state.D[0] != 0)
                _context.Memory.WriteLong(original + 4, _context.Memory.ReadLong(item + 4));
        }
        state.D[0] = 0;
    }

    private void MapTags(M68kCpuState state)
    {
        uint mapped = 0;
        foreach (var item in Enumerate(state.A[0]))
        {
            var map = FindTag(_context.Memory.ReadLong(item), state.A[1]);
            if (map != 0)
            {
                _context.Memory.WriteLong(item, _context.Memory.ReadLong(map + 4));
                mapped++;
            }
            else if (state.D[0] == 0)
                _context.Memory.WriteLong(item, TagIgnore);
        }
        state.D[0] = mapped;
    }

    private void AllocateTagItems(M68kCpuState state)
    {
        var count = state.D[0];
        if (count > (int.MaxValue - AllocationHeaderBytes) / TagItemBytes) { state.D[0] = 0; return; }
        var bytes = checked((int)(count * TagItemBytes));
        var block = _context.Allocate(bytes + AllocationHeaderBytes, MemfPublicClear);
        if (block == 0) { state.D[0] = 0; return; }
        _context.Memory.WriteLong(block, (uint)(bytes + AllocationHeaderBytes));
        state.D[0] = block + AllocationHeaderBytes;
    }

    private void CloneTagItems(M68kCpuState state)
    {
        var source = Enumerate(state.A[0]);
        var count = source.Count + 1; // Include TAG_DONE.
        if (count > int.MaxValue / TagItemBytes) { state.D[0] = 0; return; }
        var block = _context.Allocate(checked(count * TagItemBytes + AllocationHeaderBytes), MemfPublicClear);
        if (block == 0) { state.D[0] = 0; return; }
        _context.Memory.WriteLong(block, (uint)(count * TagItemBytes + AllocationHeaderBytes));
        var clone = block + AllocationHeaderBytes;
        for (var index = 0; index < source.Count; index++)
        {
            _context.Memory.WriteLong(clone + (uint)(index * TagItemBytes), _context.Memory.ReadLong(source[index]));
            _context.Memory.WriteLong(clone + (uint)(index * TagItemBytes + 4), _context.Memory.ReadLong(source[index] + 4));
        }
        _context.Memory.WriteLong(clone + (uint)(source.Count * TagItemBytes), TagDone);
        state.D[0] = clone;
    }

    private void FreeTagItems(M68kCpuState state)
    {
        var tagList = state.A[0];
        if (tagList < AllocationHeaderBytes || !_context.Memory.IsMapped(tagList - AllocationHeaderBytes, 4)) return;
        var block = tagList - AllocationHeaderBytes;
        var bytes = _context.Memory.ReadLong(block);
        if (bytes is >= AllocationHeaderBytes and <= int.MaxValue) _context.Free(block, (int)bytes);
    }

    private void RefreshTagItemClones(M68kCpuState state)
    {
        foreach (var clone in Enumerate(state.A[0]))
        {
            var original = FindTag(_context.Memory.ReadLong(clone), state.A[1]);
            if (original != 0) _context.Memory.WriteLong(clone + 4, _context.Memory.ReadLong(original + 4));
        }
        state.D[0] = 0;
    }

    private void TagInArray(M68kCpuState state)
    {
        var array = state.A[0];
        if (array == 0) { state.D[0] = 0; return; }
        for (var index = 0; index < 2048 && _context.Memory.IsMapped(array + (uint)(index * 4), 4); index++)
        {
            var tag = _context.Memory.ReadLong(array + (uint)(index * 4));
            if (tag == TagDone) break;
            if (tag == state.D[0]) { state.D[0] = 1; return; }
        }
        state.D[0] = 0;
    }

    private void FilterTagItems(M68kCpuState state)
    {
        uint remaining = 0;
        foreach (var item in Enumerate(state.A[0]))
        {
            var match = IsTagInArray(_context.Memory.ReadLong(item), state.A[1]);
            // TAGFILTER_AND keeps only filter hits; TAGFILTER_NOT keeps
            // everything except filter hits.
            var keep = state.D[0] == 0 ? match : !match;
            if (keep) remaining++; else _context.Memory.WriteLong(item, TagIgnore);
        }
        state.D[0] = remaining;
    }

    /// <summary>
    /// Applies values from a change list to matching items in a destination
    /// list. Control tags are traversed in the usual way and change-only tags
    /// deliberately remain absent from the destination.
    /// </summary>
    private void ApplyTagChanges(M68kCpuState state)
    {
        foreach (var change in Enumerate(state.A[1]))
        {
            var destination = FindTag(_context.Memory.ReadLong(change), state.A[0]);
            if (destination != 0)
                _context.Memory.WriteLong(destination + 4, _context.Memory.ReadLong(change + 4));
        }
        state.D[0] = 0;
    }

    // utility/pack.h: direction flags, type field, tag offset, and structure
    // offset.  These converters predate MorphOS; its public documentation is
    // particularly useful because it specifies the formerly under-documented
    // bit-field behavior shared with classic utility.library.
    private const uint PackFlagUnpackOnly = 0x4000_0000;
    private const uint PackFlagPackOnly = 0x2000_0000;
    private const uint PackFlagSignedOrFlip = 0x8000_0000;
    private const uint PackFlagExists = 0x0400_0000;
    private const uint PackTypeMask = 0x1800_0000;
    private const int PackTagOffsetShift = 16;
    private const uint PackTagOffsetMask = 0x03FF_0000;
    private const uint PackTableNewBase = uint.MaxValue;

    /// <summary>PackStructureTags(data=A0, table=A1, tags=A2).</summary>
    private void PackStructureTags(M68kCpuState state)
    {
        uint written = 0;
        foreach (var entry in EnumeratePackTable(state.A[1]))
        {
            if ((entry.Control & PackFlagUnpackOnly) != 0) continue;
            var item = FindTag(entry.Tag, state.A[2]);
            if (item == 0) continue;
            var value = _context.Memory.ReadLong(item + 4);
            if (WritePackedField(state.A[0], entry.Control, value, tagExists: true)) written++;
        }
        state.D[0] = written;
    }

    /// <summary>UnpackStructureTags(data=A0, table=A1, tags=A2).</summary>
    private void UnpackStructureTags(M68kCpuState state)
    {
        uint written = 0;
        foreach (var entry in EnumeratePackTable(state.A[1]))
        {
            if ((entry.Control & PackFlagPackOnly) != 0) continue;
            var item = FindTag(entry.Tag, state.A[2]);
            if (item == 0 || !TryReadPackedField(state.A[0], entry.Control, out var value)) continue;
            _context.Memory.WriteLong(item + 4, value);
            written++;
        }
        state.D[0] = written;
    }

    // ClockData: seven big-endian UWORDs (sec, min, hour, mday, month, year,
    // wday).  Amiga time deliberately has a fixed 1978 epoch and never reads
    // host wall-clock time.
    private void Amiga2Date(M68kCpuState state)
    {
        if (!TryGetDate(state.D[0], out var date) || !_context.Memory.IsMapped(state.A[0], 14)) return;
        _context.Memory.WriteWord(state.A[0], (ushort)date.Second);
        _context.Memory.WriteWord(state.A[0] + 2, (ushort)date.Minute);
        _context.Memory.WriteWord(state.A[0] + 4, (ushort)date.Hour);
        _context.Memory.WriteWord(state.A[0] + 6, (ushort)date.Day);
        _context.Memory.WriteWord(state.A[0] + 8, (ushort)date.Month);
        _context.Memory.WriteWord(state.A[0] + 10, (ushort)date.Year);
        _context.Memory.WriteWord(state.A[0] + 12, (ushort)((int)date.DayOfWeek));
    }

    private void Date2Amiga(M68kCpuState state)
        => state.D[0] = TryReadClockData(state.A[0], out var date) ? ToAmigaSeconds(date) : 0;

    private void CheckDate(M68kCpuState state)
        => state.D[0] = TryReadClockData(state.A[0], out var date) ? ToAmigaSeconds(date) : 0;

    // Unlike normal library calls, these legacy math vectors do not require
    // A6 and preserve A0/A1. They take their inputs in D0/D1 only.
    private static void SMult32(M68kCpuState state)
        => state.D[0] = unchecked((uint)((int)state.D[0] * (int)state.D[1]));

    private static void UMult32(M68kCpuState state)
        => state.D[0] = unchecked(state.D[0] * state.D[1]);

    private static void SDivMod32(M68kCpuState state)
    {
        var dividend = (int)state.D[0];
        var divisor = (int)state.D[1];
        if (divisor == 0) { state.D[0] = 0; state.D[1] = unchecked((uint)dividend); return; }

        var quotient = (long)dividend / divisor;
        var remainder = (long)dividend % divisor;
        state.D[0] = unchecked((uint)quotient);
        state.D[1] = unchecked((uint)remainder);
    }

    private static void UDivMod32(M68kCpuState state)
    {
        var dividend = state.D[0];
        var divisor = state.D[1];
        if (divisor == 0) { state.D[0] = 0; state.D[1] = dividend; return; }

        state.D[0] = dividend / divisor;
        state.D[1] = dividend % divisor;
    }

    private static void SMult64(M68kCpuState state)
    {
        var product = (long)(int)state.D[0] * (int)state.D[1];
        state.D[0] = unchecked((uint)(product >> 32));
        state.D[1] = unchecked((uint)product);
    }

    private static void UMult64(M68kCpuState state)
    {
        var product = (ulong)state.D[0] * state.D[1];
        state.D[0] = (uint)(product >> 32);
        state.D[1] = (uint)product;
    }

    private void Stricmp(M68kCpuState state) => state.D[0] = unchecked((uint)CompareStrings(state.A[0], state.A[1], int.MaxValue));

    private void Strnicmp(M68kCpuState state)
        => state.D[0] = unchecked((uint)CompareStrings(state.A[0], state.A[1], unchecked((int)state.D[0])));

    private static void ToUpper(M68kCpuState state) => state.D[0] = ToUpperAscii((byte)state.D[0]);
    private static void ToLower(M68kCpuState state) => state.D[0] = ToLowerAscii((byte)state.D[0]);

    private void GetUniqueId(M68kCpuState state)
    {
        state.D[0] = _nextUniqueId;
        _nextUniqueId++;
    }

    private void CallHookPkt(M68kCpuState state)
    {
        var hook = state.A[0];
        if (hook == 0 || !_context.Memory.IsMapped(hook, 12) || _context.StartGuestSubroutine is null)
        {
            state.D[0] = 0;
            return;
        }

        var entry = _context.Memory.ReadLong(hook + 8); // struct Hook.h_Entry after MinNode.
        if (entry == 0)
        {
            state.D[0] = 0;
            return;
        }

        // The guest hook owns its result in D0. StartGuestSubroutine pushes
        // our continuation and transfers control without recursive execution.
        _context.StartGuestSubroutine(state, entry, HookContinuationAddress);
    }

    private static void ContinueHook(M68kCpuState state)
    {
        // The ordinary gateway return performs the final RTS to CallHookPkt's
        // original caller, retaining the hook's D0 result.
    }

    private sealed class NamedObjectRecord
    {
        public required uint Address;
        public required uint Allocation;
        public required int AllocationBytes;
        public required uint NameAddress;
        public required string Name;
        public required sbyte Priority;
        public uint UserSpace;
        public int UserSpaceBytes;
        public uint Parent;
        public uint NameSpaceFlags;
        public bool HasNameSpace;
        public uint PendingRemovalMessage;
        public int UseCount = 1;
        public List<uint> Children { get; } = new();
    }

    private void AllocNamedObjectA(M68kCpuState state)
    {
        var name = ReadCString(state.A[0], 512);
        if (string.IsNullOrEmpty(name)) { state.D[0] = 0; return; }
        var bytes = checked(4 + name.Length + 1);
        var allocation = _context.Allocate(bytes, MemfPublicClear);
        if (allocation == 0 || !_context.Memory.IsMapped(allocation, bytes)) { state.D[0] = 0; return; }

        var objectAddress = allocation;
        var userSpaceBytes = FindTag(AnoUserSpace, state.A[1]) is var userTag && userTag != 0 ? _context.Memory.ReadLong(userTag + 4) : 0;
        if (userSpaceBytes > int.MaxValue) { _context.Free(allocation, bytes); state.D[0] = 0; return; }
        var userSpace = userSpaceBytes == 0 ? 0 : _context.Allocate((int)userSpaceBytes, MemfPublicClear);
        if (userSpaceBytes != 0 && userSpace == 0) { _context.Free(allocation, bytes); state.D[0] = 0; return; }

        var priorityTag = FindTag(AnoPriority, state.A[1]);
        var flagsTag = FindTag(AnoFlags, state.A[1]);
        var namespaceTag = FindTag(AnoNameSpace, state.A[1]);
        _context.Memory.WriteLong(objectAddress, userSpace);
        WriteCString(objectAddress + 4, name);
        _namedObjects[objectAddress] = new NamedObjectRecord
        {
            Address = objectAddress, Allocation = allocation, AllocationBytes = bytes,
            NameAddress = objectAddress + 4, Name = name,
            Priority = priorityTag == 0 ? (sbyte)0 : unchecked((sbyte)_context.Memory.ReadLong(priorityTag + 4)),
            UserSpace = userSpace, UserSpaceBytes = (int)userSpaceBytes,
            HasNameSpace = namespaceTag != 0 && _context.Memory.ReadLong(namespaceTag + 4) != 0,
            NameSpaceFlags = flagsTag == 0 ? 0 : _context.Memory.ReadLong(flagsTag + 4)
        };
        state.D[0] = objectAddress;
    }

    private void AddNamedObject(M68kCpuState state)
    {
        if (!_namedObjects.TryGetValue(state.A[1], out var item) || item.Parent != 0) { state.D[0] = 0; return; }
        var target = GetNameSpace(state.A[0]);
        if (target is null) { state.D[0] = 0; return; }
        var list = target.Value.Children;
        var flags = target.Value.Flags;
        if ((flags & NsfNoDups) != 0 && list.Exists(address => NamesEqual(_namedObjects[address].Name, item.Name, flags))) { state.D[0] = 0; return; }
        var insertAt = list.FindIndex(address => _namedObjects[address].Priority < item.Priority);
        if (insertAt < 0) list.Add(item.Address); else list.Insert(insertAt, item.Address);
        item.Parent = state.A[0] == 0 ? uint.MaxValue : state.A[0];
        state.D[0] = 1;
    }

    private void FindNamedObject(M68kCpuState state)
    {
        var target = GetNameSpace(state.A[0]);
        if (target is null) { state.D[0] = 0; return; }
        var name = state.A[1] == 0 ? null : ReadCString(state.A[1], 512);
        var start = 0;
        if (state.A[2] != 0) { start = target.Value.Children.IndexOf(state.A[2]); if (start < 0) { state.D[0] = 0; return; } start++; }
        for (var index = start; index < target.Value.Children.Count; index++)
        {
            var record = _namedObjects[target.Value.Children[index]];
            if (name is not null && !NamesEqual(record.Name, name, target.Value.Flags)) continue;
            record.UseCount++;
            state.D[0] = record.Address;
            return;
        }
        state.D[0] = 0;
    }

    private void AttemptRemNamedObject(M68kCpuState state)
    {
        if (!_namedObjects.TryGetValue(state.A[0], out var item) || item.UseCount > 1) { state.D[0] = 0; return; }
        RemoveNamedObject(item, state, 0);
        state.D[0] = 1;
    }

    private void ReleaseNamedObject(M68kCpuState state)
    {
        if (!_namedObjects.TryGetValue(state.A[0], out var item) || item.UseCount == 0) return;
        item.UseCount--;
        if (item.UseCount == 0 && item.PendingRemovalMessage != 0) RemoveNamedObject(item, state, item.PendingRemovalMessage);
    }

    private void RemNamedObject(M68kCpuState state)
    {
        if (!_namedObjects.TryGetValue(state.A[0], out var item)) return;
        if (state.A[1] != 0 && item.PendingRemovalMessage != 0)
        {
            _context.Memory.WriteLong(state.A[1] + 0x0A, 0);
            _context.ReplyMessage?.Invoke(state.A[1], state);
            return;
        }
        if (state.A[1] != 0) { _context.Memory.WriteLong(state.A[1] + 0x0A, item.Address); item.PendingRemovalMessage = state.A[1]; }
        if (item.UseCount == 0) RemoveNamedObject(item, state, item.PendingRemovalMessage);
    }

    private void FreeNamedObject(M68kCpuState state)
    {
        if (!_namedObjects.Remove(state.A[0], out var item)) return;
        if (item.UserSpace != 0) _context.Free(item.UserSpace, item.UserSpaceBytes);
        _context.Free(item.Allocation, item.AllocationBytes);
    }

    private void NamedObjectName(M68kCpuState state)
        => state.D[0] = _namedObjects.TryGetValue(state.A[0], out var item) ? item.NameAddress : 0;

    private void RemoveNamedObject(NamedObjectRecord item, M68kCpuState state, uint message)
    {
        var target = GetNameSpace(item.Parent == uint.MaxValue ? 0 : item.Parent);
        target?.Children.Remove(item.Address);
        item.Parent = 0;
        if (message != 0) _context.ReplyMessage?.Invoke(message, state);
    }

    private (List<uint> Children, uint Flags)? GetNameSpace(uint objectAddress)
    {
        if (objectAddress == 0) return (_rootNameSpace, 0);
        return _namedObjects.TryGetValue(objectAddress, out var record) && record.HasNameSpace
            ? (record.Children, record.NameSpaceFlags)
            : null;
    }

    private static bool NamesEqual(string left, string right, uint flags)
        => string.Equals(left, right, (flags & NsfCase) != 0 ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private string ReadCString(uint address, int maximum)
    {
        if (address == 0 || maximum <= 0) return string.Empty;
        var bytes = new List<char>();
        for (var offset = 0; offset < maximum && _context.Memory.IsMapped(address + (uint)offset, 1); offset++)
        {
            var value = _context.Memory.ReadByte(address + (uint)offset);
            if (value == 0) break;
            bytes.Add((char)value);
        }
        return new string(bytes.ToArray());
    }

    private void WriteCString(uint address, string value)
    {
        for (var index = 0; index < value.Length; index++) _context.Memory.WriteByte(address + (uint)index, (byte)value[index]);
        _context.Memory.WriteByte(address + (uint)value.Length, 0);
    }

    private uint FindTag(uint wanted, uint tagList)
    {
        foreach (var item in Enumerate(tagList))
            if (_context.Memory.ReadLong(item) == wanted) return item;
        return 0;
    }

    private readonly record struct PackEntry(uint Tag, uint Control);

    private List<PackEntry> EnumeratePackTable(uint table)
    {
        var entries = new List<PackEntry>();
        if (table == 0 || !_context.Memory.IsMapped(table, 4)) return entries;
        var baseTag = _context.Memory.ReadLong(table);
        var cursor = table + 4;
        for (var steps = 0; steps < 4096 && _context.Memory.IsMapped(cursor, 4); steps++, cursor += 4)
        {
            var control = _context.Memory.ReadLong(cursor);
            if (control == TagDone) break;
            if (control == PackTableNewBase)
            {
                cursor += 4;
                if (!_context.Memory.IsMapped(cursor, 4)) break;
                baseTag = _context.Memory.ReadLong(cursor);
                continue;
            }
            var tag = unchecked(baseTag + ((control & PackTagOffsetMask) >> PackTagOffsetShift));
            entries.Add(new PackEntry(tag, control));
        }
        return entries;
    }

    private bool WritePackedField(uint data, uint control, uint value, bool tagExists)
    {
        var type = (control & PackTypeMask) >> 27;
        var offset = type == 3 ? control & 0x1FFF : control & 0xFFFF;
        var address = unchecked(data + offset);
        switch (type)
        {
            case 0:
                if (!_context.Memory.IsMapped(address, 1)) return false;
                _context.Memory.WriteByte(address, (byte)value);
                return true;
            case 1:
                if (!_context.Memory.IsMapped(address, 2)) return false;
                _context.Memory.WriteWord(address, (ushort)value);
                return true;
            case 2:
                if (!_context.Memory.IsMapped(address, 4)) return false;
                _context.Memory.WriteLong(address, value);
                return true;
            case 3:
                if (!_context.Memory.IsMapped(address, 1)) return false;
                var bit = (int)((control >> 13) & 7);
                var set = (control & PackFlagExists) != 0 ? tagExists : value != 0;
                if ((control & PackFlagSignedOrFlip) != 0) set = !set;
                var current = _context.Memory.ReadByte(address);
                _context.Memory.WriteByte(address, set ? (byte)(current | (1 << bit)) : (byte)(current & ~(1 << bit)));
                return true;
            default:
                return false;
        }
    }

    private bool TryReadPackedField(uint data, uint control, out uint value)
    {
        value = 0;
        var type = (control & PackTypeMask) >> 27;
        var offset = type == 3 ? control & 0x1FFF : control & 0xFFFF;
        var address = unchecked(data + offset);
        switch (type)
        {
            case 0:
                if (!_context.Memory.IsMapped(address, 1)) return false;
                var byteValue = _context.Memory.ReadByte(address);
                value = (control & PackFlagSignedOrFlip) != 0 ? unchecked((uint)(int)(sbyte)byteValue) : byteValue;
                return true;
            case 1:
                if (!_context.Memory.IsMapped(address, 2)) return false;
                var wordValue = _context.Memory.ReadWord(address);
                value = (control & PackFlagSignedOrFlip) != 0 ? unchecked((uint)(int)(short)wordValue) : wordValue;
                return true;
            case 2:
                if (!_context.Memory.IsMapped(address, 4)) return false;
                value = _context.Memory.ReadLong(address);
                return true;
            case 3:
                if (!_context.Memory.IsMapped(address, 1)) return false;
                value = (uint)((_context.Memory.ReadByte(address) >> (int)((control >> 13) & 7)) & 1);
                return true;
            default:
                return false;
        }
    }

    private bool TryReadClockData(uint pointer, out DateTime date)
    {
        date = default;
        if (pointer == 0 || !_context.Memory.IsMapped(pointer, 14)) return false;
        var second = _context.Memory.ReadWord(pointer);
        var minute = _context.Memory.ReadWord(pointer + 2);
        var hour = _context.Memory.ReadWord(pointer + 4);
        var day = _context.Memory.ReadWord(pointer + 6);
        var month = _context.Memory.ReadWord(pointer + 8);
        var year = _context.Memory.ReadWord(pointer + 10);
        if (year is < 1978 or > 2113 || month is < 1 or > 12 || hour > 23 || minute > 59 || second > 59) return false;
        try { date = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified); return true; }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static bool TryGetDate(uint seconds, out DateTime date)
    {
        date = default;
        try
        {
            date = new DateTime(1978, 1, 1).AddSeconds(seconds);
            return date.Year <= 2113;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static uint ToAmigaSeconds(DateTime date)
        => checked((uint)(date - new DateTime(1978, 1, 1)).TotalSeconds);

    private int CompareStrings(uint left, uint right, int maximum)
    {
        if (maximum <= 0) return 0;
        for (var index = 0; index < maximum; index++)
        {
            var leftValue = ReadStringByte(left, index);
            var rightValue = ReadStringByte(right, index);
            var difference = ToUpperAscii(leftValue) - ToUpperAscii(rightValue);
            if (difference != 0 || leftValue == 0 || rightValue == 0) return difference;
        }
        return 0;
    }

    private byte ReadStringByte(uint address, int offset)
        => address != 0 && address <= uint.MaxValue - (uint)offset && _context.Memory.IsMapped(address + (uint)offset, 1)
            ? _context.Memory.ReadByte(address + (uint)offset)
            : (byte)0;

    private static byte ToUpperAscii(byte value) => value is >= (byte)'a' and <= (byte)'z' ? (byte)(value - ('a' - 'A')) : value;
    private static byte ToLowerAscii(byte value) => value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + ('a' - 'A')) : value;

    private List<uint> Enumerate(uint tagList)
    {
        var items = new List<uint>();
        var cursor = tagList;
        while (TryNext(ref cursor, out var item)) items.Add(item);
        return items;
    }

    private bool TryNext(ref uint cursor, out uint item)
    {
        item = 0;
        var visited = new HashSet<uint>();
        for (var steps = 0; cursor != 0 && steps < 4096; steps++)
        {
            if (!_context.Memory.IsMapped(cursor, TagItemBytes) || !visited.Add(cursor)) { cursor = 0; return false; }
            var tag = _context.Memory.ReadLong(cursor);
            var data = _context.Memory.ReadLong(cursor + 4);
            switch (tag)
            {
                case TagDone:
                    cursor = 0;
                    return false;
                case TagIgnore:
                    cursor += TagItemBytes;
                    continue;
                case TagMore:
                    cursor = data;
                    continue;
                case TagSkip:
                    if (data > 4095 || cursor > uint.MaxValue - (data + 1) * TagItemBytes) { cursor = 0; return false; }
                    cursor += (data + 1) * TagItemBytes;
                    continue;
                default:
                    item = cursor;
                    cursor += TagItemBytes;
                    return true;
            }
        }

        cursor = 0;
        return false;
    }

    private bool IsTagInArray(uint wanted, uint array)
    {
        for (var index = 0; array != 0 && index < 2048 && _context.Memory.IsMapped(array + (uint)(index * 4), 4); index++)
        {
            var tag = _context.Memory.ReadLong(array + (uint)(index * 4));
            if (tag == TagDone) break;
            if (tag == wanted) return true;
        }
        return false;
    }

    private uint FindLibrary(uint list, string name)
    {
        var tail = list + 4;
        for (var node = _context.Memory.ReadLong(list); node != tail && node != 0 && _context.Memory.IsMapped(node, NodeNameOffset + 4); node = _context.Memory.ReadLong(node))
        {
            var nameAddress = _context.Memory.ReadLong(node + NodeNameOffset);
            if (ReadString(nameAddress).Equals(name, StringComparison.OrdinalIgnoreCase)) return node;
        }
        return 0;
    }

    private string ReadString(uint address)
    {
        if (address == 0) return string.Empty;
        var chars = new List<char>();
        for (var index = 0; index < 96 && _context.Memory.IsMapped(address + (uint)index, 1); index++)
        {
            var value = _context.Memory.ReadByte(address + (uint)index);
            if (value == 0) break;
            chars.Add((char)value);
        }
        return new string(chars.ToArray());
    }
}
