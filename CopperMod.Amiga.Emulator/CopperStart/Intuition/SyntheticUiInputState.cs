/* Copyright (C) 2026 Ilkka Lehtoranta - SPDX-License-Identifier: MIT */
using System;
using Amiga;
using CopperMod.Amiga.Bus;
using PortableIntuition = CopperStart.Intuition;

namespace CopperMod.Amiga.CopperStart.Intuition;

/// <summary>Host facade over the guest-resident portable IDCMP/input state.</summary>
internal sealed class SyntheticUiInputState
{
	private HostGuestMemory? _memory; private Func<int, uint>? _allocate; private uint _state; private short _initialX, _initialY;
	public void Bind(HostGuestMemory memory, Func<int, uint> allocate) { _memory = memory; _allocate = allocate; }
	public int MouseX { get { if (!Ensure(out var p)) return _initialX; return PortableIntuition.IntuitionStateCore.GetMouseX(ref p, Ptr(_state)); } }
	public int MouseY { get { if (!Ensure(out var p)) return _initialY; return PortableIntuition.IntuitionStateCore.GetMouseY(ref p, Ptr(_state)); } }
	public bool PrimaryMousePressed { get { if (!Ensure(out var p)) return false; return PortableIntuition.IntuitionStateCore.IsPrimaryPressed(ref p, Ptr(_state)); } }
	public int MessageCount { get { if (!Ensure(out var p)) return 0; return PortableIntuition.IntuitionStateCore.MessageCount(ref p, Ptr(_state)); } }
	public void Reset(int mouseX, int mouseY) { _state = 0; _initialX = unchecked((short)mouseX); _initialY = unchecked((short)mouseY); }
	public void SetMousePosition(int x, int y, int screenWidth, int screenHeight) { if (Ensure(out var p)) PortableIntuition.IntuitionStateCore.SetMousePosition(ref p, Ptr(_state), x, y, screenWidth, screenHeight); }
	public void SetPrimaryMousePressed(bool pressed) { if (Ensure(out var p)) PortableIntuition.IntuitionStateCore.SetPrimaryPressed(ref p, Ptr(_state), pressed); }
	public void Enqueue(SyntheticIntuiMessage message) { if (Ensure(out var p)) PortableIntuition.IntuitionStateCore.Enqueue(ref p, Ptr(_state), message.Class, message.Code, message.Qualifier, Ptr(message.IAddress), unchecked((short)message.MouseX), unchecked((short)message.MouseY), unchecked((uint)(message.Cycles >> 32)), unchecked((uint)message.Cycles)); }
	public bool TryDequeue(out SyntheticIntuiMessage message) => Read(true, out message);
	public bool TryPeek(out SyntheticIntuiMessage message) => Read(false, out message);
	private bool Read(bool remove, out SyntheticIntuiMessage message)
	{
		message = default; if (!Ensure(out var p)) return false; var scratch = Ptr(_state + 488); var ok = remove ? PortableIntuition.IntuitionStateCore.Dequeue(ref p, Ptr(_state), scratch) : PortableIntuition.IntuitionStateCore.Peek(ref p, Ptr(_state), scratch); if (!ok) return false; var cycles = unchecked(((long)p.ReadUInt32(scratch, 16) << 32) | p.ReadUInt32(scratch, 20)); message = new SyntheticIntuiMessage(p.ReadUInt32(scratch), p.ReadUInt16(scratch, 4), p.ReadUInt16(scratch, 6), p.ReadUInt32(scratch, 8), unchecked((short)p.ReadUInt16(scratch, 12)), unchecked((short)p.ReadUInt16(scratch, 14)), cycles); return true;
	}
	private bool Ensure(out IntuitionHostMemoryPlatform platform)
	{
		platform = default; if (_memory is null || _allocate is null) return false; if (_state == 0) { _state = _allocate((int)PortableIntuition.IntuitionStateCore.StateSize); if (_state == 0) return false; platform = new IntuitionHostMemoryPlatform(_memory); PortableIntuition.IntuitionStateCore.Initialize(ref platform, Ptr(_state), _initialX, _initialY); } else platform = new IntuitionHostMemoryPlatform(_memory); return true;
	}
	private static APTR Ptr(uint value) => APTR.FromPointer(value);
}

internal readonly struct SyntheticIntuiMessage
{
	public SyntheticIntuiMessage(uint messageClass, ushort code, ushort qualifier, uint iAddress, int mouseX, int mouseY, long cycles) { Class = messageClass; Code = code; Qualifier = qualifier; IAddress = iAddress; MouseX = mouseX; MouseY = mouseY; Cycles = cycles; }
	public uint Class { get; } public ushort Code { get; } public ushort Qualifier { get; } public uint IAddress { get; } public int MouseX { get; } public int MouseY { get; } public long Cycles { get; }
}

internal readonly struct IntuitionHostMemoryPlatform : PortableIntuition.IIntuitionMemoryPlatform
{
	private readonly HostGuestMemory _memory; public IntuitionHostMemoryPlatform(HostGuestMemory memory) => _memory = memory;
	public byte ReadUInt8(APTR a, int o = 0) => _memory.ReadByte(a.Raw + (uint)o); public ushort ReadUInt16(APTR a, int o = 0) => _memory.ReadWord(a.Raw + (uint)o); public uint ReadUInt32(APTR a, int o = 0) => _memory.ReadLong(a.Raw + (uint)o);
	public void WriteUInt8(APTR a, int o, byte v) => _memory.WriteByte(a.Raw + (uint)o, v); public void WriteUInt16(APTR a, int o, ushort v) => _memory.WriteWord(a.Raw + (uint)o, v); public void WriteUInt32(APTR a, int o, uint v) => _memory.WriteLong(a.Raw + (uint)o, v);
	public void Clear(APTR a, uint n) { for (var i = 0u; i < n; i++) _memory.WriteByte(a.Raw + i, 0); } public bool IsMapped(APTR a, uint n) => n <= int.MaxValue && _memory.IsMapped(a.Raw, (int)n);
	public void Copy(APTR source, APTR destination, uint byteCount)
	{
		if (destination.Raw > source.Raw && destination.Raw < source.Raw + byteCount)
		{
			for (var offset = byteCount; offset != 0; offset--)
				_memory.WriteByte(destination.Raw + offset - 1, _memory.ReadByte(source.Raw + offset - 1));
			return;
		}

		for (var offset = 0u; offset < byteCount; offset++)
			_memory.WriteByte(destination.Raw + offset, _memory.ReadByte(source.Raw + offset));
	}
}
